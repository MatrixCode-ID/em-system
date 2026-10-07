using System.IO;
using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Computes the size and SHA-256 of the content of a release file (<c>doc/release-format.md</c> section
   /// 2.4) while reading it once from start to end, so neither a large file nor a download that is still
   /// streaming needs to be held in memory.
   /// </summary>
   public static class ReleaseHash
   {
      private const int BufferSize = 81920;

      /// <summary>
      /// Reads <paramref name="content"/> to the end and returns its number of bytes together with its
      /// SHA-256 in lowercase hex.
      /// </summary>
      /// <param name="content">The file content, read from its current position.</param>
      /// <param name="progress">Receives the number of bytes read from this stream; may be <c>null</c>.</param>
      /// <param name="token">Stops the reading midway.</param>
      public static async ValueTask<(long Size, string Sha256)> ComputeAsync(Stream content, IProgress<long>? progress,
         CancellationToken token) {
         using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
         var buffer = new byte[BufferSize];
         long size = 0;
         int read;
         while ((read = await content.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) {
            hash.AppendData(buffer, 0, read);
            size += read;
            progress?.Report(size);
         }

         return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
      }

      /// <summary>Computes the size and SHA-256 of a file on disk (see <see cref="ComputeAsync(Stream,IProgress{long},CancellationToken)"/>).</summary>
      /// <param name="filePath">The path of the file on disk.</param>
      /// <param name="progress">Receives the number of bytes read; may be <c>null</c>.</param>
      /// <param name="token">Stops the reading midway.</param>
      public static async ValueTask<(long Size, string Sha256)> ComputeAsync(string filePath, IProgress<long>? progress,
         CancellationToken token) {
         await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
         return await ComputeAsync(file, progress, token).ConfigureAwait(false);
      }
   }
}
