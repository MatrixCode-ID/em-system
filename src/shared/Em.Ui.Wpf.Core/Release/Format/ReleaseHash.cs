using System.IO;
using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Menghitung ukuran dan SHA-256 isi sebuah file rilis (<c>doc/release-format.md</c> bagian 2.4)
   /// sambil membacanya sekali dari awal sampai habis, jadi file besar maupun unduhan yang masih mengalir
   /// tidak perlu ditampung di memori.
   /// </summary>
   public static class ReleaseHash
   {
      private const int BufferSize = 81920;

      /// <summary>
      /// Membaca <paramref name="content"/> sampai habis dan mengembalikan jumlah byte-nya beserta
      /// SHA-256-nya dalam hex huruf kecil.
      /// </summary>
      /// <param name="content">Isi file, dibaca dari posisinya sekarang.</param>
      /// <param name="progress">Menerima jumlah byte yang sudah terbaca dari stream ini; boleh <c>null</c>.</param>
      /// <param name="token">Menghentikan pembacaan di tengah jalan.</param>
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

      /// <summary>Menghitung ukuran dan SHA-256 file di disk (lihat <see cref="ComputeAsync(Stream,IProgress{long},CancellationToken)"/>).</summary>
      /// <param name="filePath">Path file di disk.</param>
      /// <param name="progress">Menerima jumlah byte yang sudah terbaca; boleh <c>null</c>.</param>
      /// <param name="token">Menghentikan pembacaan di tengah jalan.</param>
      public static async ValueTask<(long Size, string Sha256)> ComputeAsync(string filePath, IProgress<long>? progress,
         CancellationToken token) {
         await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
         return await ComputeAsync(file, progress, token).ConfigureAwait(false);
      }
   }
}
