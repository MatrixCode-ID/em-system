using System.IO;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One file or folder that can be dragged out of the application - to Windows Explorer or another
   /// application that accepts files - without ever existing as a local file. The receiver asks for its
   /// content through <see cref="OpenRead"/> and writes it straight to the destination; there is no
   /// temporary copy on disk.
   /// </summary>
   public sealed class VirtualFile
   {
      /// <summary>
      /// The name relative to the drop place, with <c>\</c> as the folder separator (e.g.
      /// <c>Release 1.0\setup.msi</c>). Its parent folder must be registered first as a
      /// <see cref="VirtualFile"/> whose <see cref="IsFolder"/> is <c>true</c>.
      /// </summary>
      public required string RelativePath { get; init; }

      /// <summary><c>true</c> for a folder, which has no content.</summary>
      public bool IsFolder { get; init; }

      /// <summary>Size of the content in bytes; used by the receiver to show progress.</summary>
      public long Size { get; init; }

      /// <summary>The last modified time, which is also written to the destination file.</summary>
      public DateTime LastWriteTimeUtc { get; init; }

      /// <summary>
      /// Opens its content for reading starting from the requested byte position to the end. It is only
      /// called when the receiver really asks for the content, may be called more than once if the receiver
      /// jumps to another position, and from any thread - not the UI thread - so it may block, for example
      /// while downloading. <c>null</c> for a folder.
      /// </summary>
      public Func<long, Stream>? OpenRead { get; init; }
   }

   /// <summary>
   /// A <see cref="DragSource"/> payload that can also be dropped outside the application as files. Such a
   /// payload is still accepted as-is by <see cref="DropTarget"/> inside the application; outside the
   /// application, the receiver gets the files from <see cref="ResolveVirtualFiles"/>.
   /// </summary>
   public interface IVirtualFileSource
   {
      /// <summary>
      /// Composes the list of files and folders handed to a receiver outside the application. Only called when
      /// the receiver asks for it, on the UI thread, and at most once per drag.
      /// </summary>
      IReadOnlyList<VirtualFile> ResolveVirtualFiles();
   }
}
