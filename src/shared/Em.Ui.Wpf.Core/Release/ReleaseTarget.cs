using System.Diagnostics;
using System.IO;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// One item of the <c>binaries/</c> folder at the release target.
   /// </summary>
   /// <param name="Path">Path relative to <c>binaries/</c>, separated by <c>/</c>.</param>
   /// <param name="IsFolder"><c>true</c> for a folder.</param>
   /// <param name="Size">File size in bytes; <c>0</c> for a folder.</param>
   public sealed record ReleaseTargetEntry(string Path, bool IsFolder, long Size);

   /// <summary>
   /// Progress of a release operation (Prepare, Compare, Sync, Verify) to be shown on screen.
   /// </summary>
   /// <param name="Stage">The stage that is running, e.g. <c>"Uploading"</c>.</param>
   /// <param name="FileIndex">Sequence of the file being worked on, starting at 1; <c>0</c> when this stage is not per file.</param>
   /// <param name="FileCount">The number of files in this stage.</param>
   /// <param name="CurrentPath">The path of the file being worked on, if any.</param>
   /// <param name="BytesDone">The bytes already processed in this stage.</param>
   /// <param name="BytesTotal">The total bytes of this stage; <c>0</c> when not known.</param>
   public sealed record ReleaseProgress(string Stage, int FileIndex, int FileCount, string? CurrentPath, long BytesDone,
      long BytesTotal);

   /// <summary>
   /// Where a release folder lives - the server CDN or an ordinary folder - together with the operations
   /// needed by Compare, Sync, and Verify. Every path here is relative to the release folder itself and
   /// separated by <c>/</c> (e.g. <c>release.json</c>, <c>binaries/runtimes/x.dll</c>).
   /// </summary>
   public abstract class ReleaseTarget
   {
      /// <summary>A human-readable description of the target, used in confirmations and logs.</summary>
      public abstract string Description { get; }

      /// <summary>The content of a file, or <c>null</c> when the file does not exist.</summary>
      public abstract Task<byte[]?> ReadFileAsync(string path, CancellationToken token);

      /// <summary>
      /// The size and SHA-256 of a file, computed while reading it without keeping its content; <c>null</c>
      /// when the file does not exist.
      /// </summary>
      public abstract Task<(long Size, string Sha256)?> HashFileAsync(string path, IProgress<long>? progress,
         CancellationToken token);

      /// <summary>
      /// All files and folders under <c>binaries/</c>, with paths relative to <c>binaries/</c>. An empty list
      /// when <c>binaries/</c> does not exist yet.
      /// </summary>
      public abstract Task<IReadOnlyList<ReleaseTargetEntry>> ListBinariesAsync(CancellationToken token);

      /// <summary>Makes sure a folder (and the folders above it) exists.</summary>
      public abstract Task EnsureFolderAsync(string folder, CancellationToken token);

      /// <summary>
      /// Writes a file, overwriting the old one. New content is never seen half-finished: it is written to a
      /// temporary name first, then moved to its name. The folder it lives in must already exist.
      /// </summary>
      /// <param name="path">The file path.</param>
      /// <param name="content">The file content, read from its current position to the end.</param>
      /// <param name="progress">Receives the number of bytes sent; may be <c>null</c>.</param>
      /// <param name="token">Cancels the write; the old file stays intact.</param>
      public abstract Task WriteFileAsync(string path, Stream content, IProgress<long>? progress, CancellationToken token);

      /// <summary>Deletes a file, or a folder together with its content. Something that no longer exists is ignored.</summary>
      public abstract Task DeleteAsync(string path, bool isFolder, CancellationToken token);

      /// <summary>
      /// The size limit of one file that can be written to this target, or <c>null</c> when it is not
      /// limited. It also makes sure the target can be used (e.g. the server CDN is really turned on).
      /// </summary>
      public virtual Task<long?> GetMaxFileSizeAsync(CancellationToken token) => Task.FromResult<long?>(null);

      /// <summary>The path of a release file inside the release folder: <c>binaries/</c> + <paramref name="path"/>.</summary>
      public static string BinaryPath(string path) => $"{ReleaseLayout.BinariesFolder}/{path}";

      /// <summary>
      /// Tidies the release folder name typed by the user: separators become <c>/</c> and there is no
      /// trailing <c>/</c>. An empty string becomes <see cref="ReleaseLayout.DefaultReleaseFolder"/>.
      /// </summary>
      public static string NormalizeReleaseFolder(string? releaseFolder) {
         var normalized = (releaseFolder ?? "").Replace('\\', '/').Trim().Trim('/');
         return normalized.Length == 0 ? ReleaseLayout.DefaultReleaseFolder : normalized;
      }

      internal static string? ParentOf(string path) {
         var index = path.LastIndexOf('/');
         return index < 0 ? null : path[..index];
      }
   }

   /// <summary>
   /// Wraps a stream that is being sent: it reports how much has been read, at most ten times a second, and
   /// interrupts the sending by throwing from Read as soon as the token is cancelled. Seek is passed
   /// through, so the request still has a Content-Length and can be sent again after the login token is
   /// renewed.
   /// </summary>
   internal sealed class ProgressReadStream(Stream inner, IProgress<long>? progress, CancellationToken token) : Stream
   {
      private const long ReportIntervalMs = 100;
      private readonly Stopwatch _sinceReport = Stopwatch.StartNew();

      public override bool CanRead => true;
      public override bool CanSeek => inner.CanSeek;
      public override bool CanWrite => false;
      public override long Length => inner.Length;

      public override long Position {
         get => inner.Position;
         set => inner.Position = value;
      }

      public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

      public override int Read(Span<byte> buffer) {
         token.ThrowIfCancellationRequested();
         return Report(inner.Read(buffer));
      }

      public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
         ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
         token.ThrowIfCancellationRequested();
         return Report(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
      }

      private int Report(int read) {
         if (progress is not null && (read == 0 || _sinceReport.ElapsedMilliseconds >= ReportIntervalMs)) {
            _sinceReport.Restart();
            progress.Report(inner.Position);
         }

         return read;
      }

      public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

      public override void Flush() { }

      public override void SetLength(long value) => throw new NotSupportedException();

      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
   }
}
