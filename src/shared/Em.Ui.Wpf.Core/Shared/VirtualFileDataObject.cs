using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ComFileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// An OLE data object carrying virtual files: the shell's FileGroupDescriptorW / FileContents
   /// pair, which is what lets Explorer write a dragged file straight to the folder it was dropped
   /// in while pulling the bytes from us - no temporary copy on disk. It also answers the in-app
   /// payload format, so a <see cref="DropTarget"/> inside the application still sees the drag as
   /// its own.
   /// </summary>
   /// <remarks>
   /// Async capable: Explorer returns from the drop at once and copies on a worker of its own,
   /// which keeps the drag loop from standing still for the length of a download.
   /// <para>
   /// Implemented against <see cref="IOleDataObject"/> rather than the framework's
   /// <c>ComTypes.IDataObject</c>, whose methods are void: there "this format is not here" - which
   /// the shell asks a dozen times per drag - can only be said by throwing, and a debugger stops
   /// on every one of those as user-unhandled, freezing the drag until Explorer gives up. Here
   /// every answer is an HRESULT and nothing is thrown.
   /// </para>
   /// <para>
   /// Exported from the MTA (<see cref="MtaExport"/>): calls from Explorer then run on RPC threads,
   /// never on the UI thread, and WPF's own drop targets receive a COM proxy they can read through
   /// the interface they know, instead of this .NET object they would try to cast.
   /// </para>
   /// </remarks>
   internal sealed class VirtualFileDataObject : IOleDataObject, IDataObjectAsyncCapability, ICustomQueryInterface
   {
      private const int S_OK = 0;
      private const int S_FALSE = 1;
      private const int E_NOTIMPL = unchecked((int)0x80004001);
      private const int E_FAIL = unchecked((int)0x80004005);
      private const int DV_E_FORMATETC = unchecked((int)0x80040064);
      private const int DV_E_LINDEX = unchecked((int)0x80040068);
      private const int DV_E_TYMED = unchecked((int)0x80040069);
      private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
      private const int DATA_S_SAMEFORMATETC = 0x00040130;

      private const uint FD_ATTRIBUTES = 0x4;
      private const uint FD_WRITESTIME = 0x20;
      private const uint FD_FILESIZE = 0x40;
      private const uint FD_PROGRESSUI = 0x4000;
      private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
      private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
      private const int DescriptorSize = 592;
      private const int MaxNameChars = 259;

      private static readonly short FileDescriptorFormat = (short)RegisterClipboardFormat("FileGroupDescriptorW");
      private static readonly short FileContentsFormat = (short)RegisterClipboardFormat("FileContents");

      private readonly IVirtualFileSource _source;
      private readonly short _payloadFormat;
      private readonly object _gate = new();
      private readonly List<VirtualFileStream> _openStreams = [];
      private IReadOnlyList<VirtualFile>? _files;
      private volatile bool _asyncMode = true;
      private volatile bool _inOperation;

      public VirtualFileDataObject(IVirtualFileSource source, string payloadFormat) {
         _source = source;
         _payloadFormat = (short)RegisterClipboardFormat(payloadFormat);
      }

      #region IDataObject

      public int GetData(ref FORMATETC format, out STGMEDIUM medium) {
         medium = default;

         if (format.cfFormat == FileDescriptorFormat) {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) return DV_E_TYMED;
            if (!TryGetFiles(out var files)) return E_FAIL;

            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = BuildDescriptor(files);
            return S_OK;
         }

         if (format.cfFormat == FileContentsFormat) {
            if ((format.tymed & TYMED.TYMED_ISTREAM) == 0) return DV_E_TYMED;
            if (!TryGetFiles(out var files)) return E_FAIL;
            if (format.lindex < 0 || format.lindex >= files.Count || files[format.lindex].OpenRead is null) {
               return DV_E_LINDEX;
            }

            var stream = new VirtualFileStream(files[format.lindex]);
            lock (_openStreams) _openStreams.Add(stream);
            medium.tymed = TYMED.TYMED_ISTREAM;
            medium.unionmember = MtaExport.Export(stream, typeof(IOleStream), MtaExport.IID_IStream);
            return S_OK;
         }

         if (format.cfFormat == _payloadFormat) {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) return DV_E_TYMED;

            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(medium.unionmember, 1);
            return S_OK;
         }

         return DV_E_FORMATETC;
      }

      public int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => DV_E_FORMATETC;

      public int QueryGetData(ref FORMATETC format) {
         if (format.cfFormat == FileDescriptorFormat || format.cfFormat == _payloadFormat) {
            return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? S_OK : DV_E_TYMED;
         }

         if (format.cfFormat == FileContentsFormat) {
            return (format.tymed & TYMED.TYMED_ISTREAM) != 0 ? S_OK : DV_E_TYMED;
         }

         return DV_E_FORMATETC;
      }

      public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) {
         formatOut = formatIn;
         formatOut.ptd = IntPtr.Zero;
         return DATA_S_SAMEFORMATETC;
      }

      // The shell hands back bookkeeping formats here (performed drop effect, drop descriptions).
      // Nothing is done with them, but refusing would make some targets treat the drop as failed.
      public int SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) {
         if (release) ReleaseStgMedium(ref medium);
         return S_OK;
      }

      public int EnumFormatEtc(DATADIR direction, out IEnumFORMATETC? enumerator) {
         enumerator = null;
         if (direction != DATADIR.DATADIR_GET) return E_NOTIMPL;

         enumerator = new FormatEnumerator([
            Format(FileDescriptorFormat, TYMED.TYMED_HGLOBAL),
            Format(FileContentsFormat, TYMED.TYMED_ISTREAM),
            Format(_payloadFormat, TYMED.TYMED_HGLOBAL)
         ]);
         return S_OK;
      }

      public int DAdvise(ref FORMATETC format, ADVF advf, IntPtr adviseSink, out int connection) {
         connection = 0;
         return OLE_E_ADVISENOTSUPPORTED;
      }

      public int DUnadvise(int connection) => OLE_E_ADVISENOTSUPPORTED;

      public int EnumDAdvise(out IntPtr enumAdvise) {
         enumAdvise = IntPtr.Zero;
         return OLE_E_ADVISENOTSUPPORTED;
      }

      #endregion

      #region IDataObjectAsyncCapability

      public void SetAsyncMode(bool fDoOpAsync) => _asyncMode = fDoOpAsync;

      public void GetAsyncMode(out bool pfIsOpAsync) => pfIsOpAsync = _asyncMode;

      public void StartOperation(IntPtr pbcReserved) => _inOperation = true;

      public void InOperation(out bool pfInAsyncOp) => pfInAsyncOp = _inOperation;

      public void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects) {
         _inOperation = false;
         CloseStreams();
      }

      #endregion

      public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv) =>
         MtaExport.RefuseAgility(ref iid, out ppv);

      /// <summary>
      /// Closes every download still open, unless the target has taken the copy over
      /// asynchronously - then <see cref="EndOperation"/> does it once the target is done.
      /// </summary>
      public void CloseStreamsUnlessAsync() {
         if (!_inOperation) CloseStreams();
      }

      private void CloseStreams() {
         VirtualFileStream[] streams;
         lock (_openStreams) {
            streams = [.. _openStreams];
            _openStreams.Clear();
         }

         foreach (var stream in streams) stream.Close();
      }

      // Resolved once, on first demand: an in-app drop never asks, and a drop on Explorer asks
      // for the descriptor before any content.
      private bool TryGetFiles(out IReadOnlyList<VirtualFile> files) {
         lock (_gate) {
            try {
               files = _files ??= _source.ResolveVirtualFiles();
               return true;
            }
            catch (Exception x) {
               Debug.WriteLine($"Virtual file list could not be resolved: {x}");
               files = [];
               return false;
            }
         }
      }

      private static FORMATETC Format(short format, TYMED tymed) => new() {
         cfFormat = format,
         dwAspect = DVASPECT.DVASPECT_CONTENT,
         lindex = -1,
         ptd = IntPtr.Zero,
         tymed = tymed
      };

      // FILEGROUPDESCRIPTORW: a count followed by FILEDESCRIPTORW records of 592 bytes each, laid
      // out by hand because the structure is fixed and a marshaled struct would add nothing.
      private static IntPtr BuildDescriptor(IReadOnlyList<VirtualFile> files) {
         var size = sizeof(uint) + files.Count * DescriptorSize;
         var memory = Marshal.AllocHGlobal(size);
         Marshal.Copy(new byte[size], 0, memory, size);
         Marshal.WriteInt32(memory, files.Count);

         for (var index = 0; index < files.Count; index++) {
            var file = files[index];
            var record = memory + sizeof(uint) + index * DescriptorSize;

            var flags = FD_ATTRIBUTES | FD_WRITESTIME | FD_PROGRESSUI | (file.IsFolder ? 0 : FD_FILESIZE);
            Marshal.WriteInt32(record, 0, (int)flags);
            Marshal.WriteInt32(record, 36, (int)(file.IsFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL));
            Marshal.WriteInt64(record, 56, file.LastWriteTimeUtc == default ? 0 : file.LastWriteTimeUtc.ToFileTimeUtc());
            Marshal.WriteInt32(record, 64, (int)(file.Size >> 32));
            Marshal.WriteInt32(record, 68, (int)(file.Size & 0xFFFFFFFF));

            var name = file.RelativePath.Length > MaxNameChars ? file.RelativePath[..MaxNameChars] : file.RelativePath;
            var chars = (name + '\0').ToCharArray();
            Marshal.Copy(chars, 0, record + 72, chars.Length);
         }

         return memory;
      }

      [DllImport("user32.dll", CharSet = CharSet.Unicode)]
      private static extern uint RegisterClipboardFormat(string format);

      [DllImport("ole32.dll")]
      private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

      private sealed class FormatEnumerator(FORMATETC[] formats) : IEnumFORMATETC
      {
         private int _index;

         public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched) {
            var fetched = 0;
            while (fetched < celt && _index < formats.Length) {
               rgelt[fetched++] = formats[_index++];
            }

            if (pceltFetched is { Length: > 0 }) pceltFetched[0] = fetched;
            return fetched == celt ? S_OK : S_FALSE;
         }

         public int Skip(int celt) {
            _index += celt;
            return _index <= formats.Length ? S_OK : S_FALSE;
         }

         public int Reset() {
            _index = 0;
            return S_OK;
         }

         public void Clone(out IEnumFORMATETC newEnum) => newEnum = new FormatEnumerator(formats) { _index = _index };
      }
   }

   /// <summary>
   /// Read-only stream over one virtual file's content. Every method answers with an HRESULT - see
   /// <see cref="VirtualFileDataObject"/> for why nothing here may throw back into COM.
   /// </summary>
   internal sealed class VirtualFileStream : IOleStream, ICustomQueryInterface
   {
      private const int S_OK = 0;
      private const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
      private const int STG_E_READFAULT = unchecked((int)0x8003001E);
      private const int STG_E_REVERTED = unchecked((int)0x80030102);
      private const int STATFLAG_NONAME = 1;

      private readonly VirtualFile _file;
      private readonly object _gate = new();
      private Stream? _content;
      private long _contentPosition;
      private long _position;
      private bool _closed;

      public VirtualFileStream(VirtualFile file) {
         _file = file;
      }

      public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv) =>
         MtaExport.RefuseAgility(ref iid, out ppv);

      public void Close() {
         lock (_gate) {
            _closed = true;
            _content?.Dispose();
            _content = null;
         }
      }

      // A short read means end of stream to a COM caller, so the buffer is filled completely unless
      // the content really ends. The content is (re)opened at the position asked for, so a caller
      // that measures the stream by seeking to its end and back - the shell does - costs nothing,
      // and one that really jumps gets the bytes from there.
      public int Read(byte[] pv, int cb, IntPtr pcbRead) {
         var total = 0;
         try {
            lock (_gate) {
               if (_closed) return STG_E_REVERTED;

               if (_position < _file.Size) {
                  if (_content == null || _contentPosition != _position) {
                     _content?.Dispose();
                     _content = _file.OpenRead!(_position);
                     _contentPosition = _position;
                  }

                  while (total < cb) {
                     var read = _content.Read(pv, total, cb - total);
                     if (read == 0) break;
                     total += read;
                  }

                  _position += total;
                  _contentPosition = _position;
               }

               if (total < cb) {
                  _content?.Dispose();
                  _content = null;
               }
            }

            return S_OK;
         }
         catch (Exception x) {
            Debug.WriteLine($"Virtual file '{_file.RelativePath}' could not be read at {_position}: {x}");
            return STG_E_READFAULT;
         }
         finally {
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, total);
         }
      }

      public int Write(byte[] pv, int cb, IntPtr pcbWritten) => STG_E_INVALIDFUNCTION;

      // Only the position moves here; the next read reopens the content there if it has to.
      public int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition) {
         lock (_gate) {
            var target = dwOrigin switch {
               0 => dlibMove,
               1 => _position + dlibMove,
               2 => _file.Size + dlibMove,
               _ => -1
            };

            if (target < 0) return STG_E_INVALIDFUNCTION;

            _position = target;
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, _position);
            return S_OK;
         }
      }

      public int SetSize(long libNewSize) => STG_E_INVALIDFUNCTION;

      public int CopyTo(IntPtr pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => STG_E_INVALIDFUNCTION;

      public int Commit(int grfCommitFlags) => S_OK;

      public int Revert() => STG_E_INVALIDFUNCTION;

      public int LockRegion(long libOffset, long cb, int dwLockType) => STG_E_INVALIDFUNCTION;

      public int UnlockRegion(long libOffset, long cb, int dwLockType) => STG_E_INVALIDFUNCTION;

      public int Stat(out STATSTG pstatstg, int grfStatFlag) {
         var time = _file.LastWriteTimeUtc == default ? 0 : _file.LastWriteTimeUtc.ToFileTimeUtc();
         var fileTime = new ComFileTime { dwLowDateTime = (int)(time & 0xFFFFFFFF), dwHighDateTime = (int)(time >> 32) };
         pstatstg = new STATSTG {
            pwcsName = (grfStatFlag & STATFLAG_NONAME) != 0 ? null! : Path.GetFileName(_file.RelativePath),
            type = 2,
            cbSize = _file.Size,
            mtime = fileTime,
            ctime = fileTime,
            atime = fileTime
         };
         return S_OK;
      }

      public int Clone(out IntPtr ppstm) {
         ppstm = IntPtr.Zero;
         return STG_E_INVALIDFUNCTION;
      }
   }

   /// <summary>
   /// Hands a .NET object to COM as if it lived in the MTA.
   /// </summary>
   /// <remarks>
   /// A .NET object given to COM is agile, so a call from another process lands in whichever
   /// apartment first exported it - the UI thread, where a drag starts - and a .NET consumer in this
   /// process gets the object itself back rather than a COM reference. Refusing IMarshal and
   /// IAgileObject (<see cref="RefuseAgility"/>) makes COM treat it like any apartment-bound object;
   /// exporting it from a thread-pool (MTA) thread and unmarshaling it here yields a proxy, and a
   /// proxy passed on still points back to the MTA.
   /// </remarks>
   internal static class MtaExport
   {
      public static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");
      public static readonly Guid IID_IStream = new("0000000C-0000-0000-C000-000000000046");
      private static readonly Guid IID_IMarshal = new("00000003-0000-0000-C000-000000000046");
      private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

      public static CustomQueryInterfaceResult RefuseAgility(ref Guid iid, out IntPtr ppv) {
         ppv = IntPtr.Zero;
         return iid == IID_IMarshal || iid == IID_IAgileObject
            ? CustomQueryInterfaceResult.Failed
            : CustomQueryInterfaceResult.NotHandled;
      }

      /// <summary>
      /// Returns an owned interface pointer for <paramref name="target"/>, served in the MTA. Falls
      /// back to the plain pointer if COM refuses the round trip.
      /// </summary>
      public static IntPtr Export(object target, Type interfaceType, Guid iid) {
         var carrier = Task.Run(() => {
            var unknown = Marshal.GetComInterfaceForObject(target, interfaceType);
            try {
               return CoMarshalInterThreadInterfaceInStream(ref iid, unknown, out var stream) == 0 ? stream : IntPtr.Zero;
            }
            finally {
               Marshal.Release(unknown);
            }
         }).GetAwaiter().GetResult();

         if (carrier != IntPtr.Zero && CoGetInterfaceAndReleaseStream(carrier, ref iid, out var proxy) == 0) {
            return proxy;
         }

         Debug.WriteLine($"{target.GetType().Name} could not be exported from the MTA; serving it directly.");
         return Marshal.GetComInterfaceForObject(target, interfaceType);
      }

      [DllImport("ole32.dll")]
      private static extern int CoMarshalInterThreadInterfaceInStream(ref Guid riid, IntPtr pUnk, out IntPtr ppStm);

      [DllImport("ole32.dll")]
      private static extern int CoGetInterfaceAndReleaseStream(IntPtr pStm, ref Guid riid, out IntPtr ppv);
   }

   /// <summary>OLE IDataObject with every method answering by HRESULT.</summary>
   [ComImport]
   [Guid("0000010E-0000-0000-C000-000000000046")]
   [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
   internal interface IOleDataObject
   {
      [PreserveSig] int GetData(ref FORMATETC format, out STGMEDIUM medium);
      [PreserveSig] int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium);
      [PreserveSig] int QueryGetData(ref FORMATETC format);
      [PreserveSig] int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut);
      [PreserveSig] int SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, [MarshalAs(UnmanagedType.Bool)] bool release);
      [PreserveSig] int EnumFormatEtc(DATADIR direction, out IEnumFORMATETC? enumerator);
      [PreserveSig] int DAdvise(ref FORMATETC format, ADVF advf, IntPtr adviseSink, out int connection);
      [PreserveSig] int DUnadvise(int connection);
      [PreserveSig] int EnumDAdvise(out IntPtr enumAdvise);
   }

   /// <summary>OLE IStream (with ISequentialStream first) answering by HRESULT.</summary>
   [ComImport]
   [Guid("0000000C-0000-0000-C000-000000000046")]
   [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
   internal interface IOleStream
   {
      [PreserveSig] int Read([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv, int cb, IntPtr pcbRead);
      [PreserveSig] int Write([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv, int cb, IntPtr pcbWritten);
      [PreserveSig] int Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition);
      [PreserveSig] int SetSize(long libNewSize);
      [PreserveSig] int CopyTo(IntPtr pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten);
      [PreserveSig] int Commit(int grfCommitFlags);
      [PreserveSig] int Revert();
      [PreserveSig] int LockRegion(long libOffset, long cb, int dwLockType);
      [PreserveSig] int UnlockRegion(long libOffset, long cb, int dwLockType);
      [PreserveSig] int Stat(out STATSTG pstatstg, int grfStatFlag);
      [PreserveSig] int Clone(out IntPtr ppstm);
   }

   [ComImport]
   [Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
   [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
   internal interface IDataObjectAsyncCapability
   {
      void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);

      void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);

      void StartOperation(IntPtr pbcReserved);

      void InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);

      void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects);
   }

   [ComImport]
   [Guid("00000121-0000-0000-C000-000000000046")]
   [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
   internal interface IDropSource
   {
      [PreserveSig]
      int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, uint grfKeyState);

      [PreserveSig]
      int GiveFeedback(uint dwEffect);
   }

   /// <summary>
   /// Runs an OLE drag with a data object of our own, which WPF's <c>DragDrop.DoDragDrop</c> cannot
   /// do: WPF wraps whatever it is given, and the wrapper answers neither virtual-file content by
   /// index nor the async capability.
   /// </summary>
   internal static class NativeDragDrop
   {
      private const int DRAGDROP_S_DROP = 0x00040100;
      private const int DRAGDROP_S_CANCEL = 0x00040101;
      private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
      private const uint MK_LBUTTON = 0x1;

      public const int DropEffectCopy = 1;
      public const int DropEffectMove = 2;

      public static int Run(VirtualFileDataObject data, int allowedEffects, Action feedback) {
         var effect = new int[1];
         var pointer = MtaExport.Export(data, typeof(IOleDataObject), MtaExport.IID_IDataObject);
         try {
            DoDragDrop(pointer, new DropSource(feedback), allowedEffects, effect);
         }
         finally {
            Marshal.Release(pointer);
            data.CloseStreamsUnlessAsync();
         }

         return effect[0];
      }

      [DllImport("ole32.dll")]
      private static extern int DoDragDrop(IntPtr pDataObj, IDropSource pDropSource, int dwOKEffects, int[] pdwEffect);

      private sealed class DropSource(Action feedback) : IDropSource
      {
         public int QueryContinueDrag(bool fEscapePressed, uint grfKeyState) {
            if (fEscapePressed) return DRAGDROP_S_CANCEL;
            return (grfKeyState & MK_LBUTTON) == 0 ? DRAGDROP_S_DROP : 0;
         }

         public int GiveFeedback(uint dwEffect) {
            feedback();
            return DRAGDROP_S_USEDEFAULTCURSORS;
         }
      }
   }
}
