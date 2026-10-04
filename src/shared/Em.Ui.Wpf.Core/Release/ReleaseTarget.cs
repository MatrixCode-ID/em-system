using System.Diagnostics;
using System.IO;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Satu isi folder <c>binaries/</c> di tujuan rilis.
   /// </summary>
   /// <param name="Path">Path relatif terhadap <c>binaries/</c>, dipisah <c>/</c>.</param>
   /// <param name="IsFolder"><c>true</c> untuk folder.</param>
   /// <param name="Size">Ukuran file dalam byte; <c>0</c> untuk folder.</param>
   public sealed record ReleaseTargetEntry(string Path, bool IsFolder, long Size);

   /// <summary>
   /// Kemajuan sebuah operasi rilis (Prepare, Compare, Sync, Verify) untuk ditampilkan di layar.
   /// </summary>
   /// <param name="Stage">Tahap yang sedang berjalan, mis. <c>"Uploading"</c>.</param>
   /// <param name="FileIndex">Urutan file yang sedang dikerjakan, mulai 1; <c>0</c> kalau tahap ini tidak per file.</param>
   /// <param name="FileCount">Jumlah file di tahap ini.</param>
   /// <param name="CurrentPath">Path file yang sedang dikerjakan, kalau ada.</param>
   /// <param name="BytesDone">Byte yang sudah diproses di tahap ini.</param>
   /// <param name="BytesTotal">Total byte tahap ini; <c>0</c> kalau tidak diketahui.</param>
   public sealed record ReleaseProgress(string Stage, int FileIndex, int FileCount, string? CurrentPath, long BytesDone,
      long BytesTotal);

   /// <summary>
   /// Tempat sebuah folder rilis berada - CDN server atau folder biasa - beserta operasi yang dibutuhkan
   /// Compare, Sync, dan Verify. Setiap path di sini relatif terhadap folder rilis itu sendiri dan
   /// dipisah <c>/</c> (mis. <c>release.json</c>, <c>binaries/runtimes/x.dll</c>).
   /// </summary>
   public abstract class ReleaseTarget
   {
      /// <summary>Keterangan tujuan yang bisa dibaca manusia, dipakai di konfirmasi dan log.</summary>
      public abstract string Description { get; }

      /// <summary>Isi sebuah file, atau <c>null</c> kalau file itu tidak ada.</summary>
      public abstract Task<byte[]?> ReadFileAsync(string path, CancellationToken token);

      /// <summary>
      /// Ukuran dan SHA-256 sebuah file, dihitung sambil membacanya tanpa menyimpan isinya; <c>null</c>
      /// kalau file itu tidak ada.
      /// </summary>
      public abstract Task<(long Size, string Sha256)?> HashFileAsync(string path, IProgress<long>? progress,
         CancellationToken token);

      /// <summary>
      /// Seluruh file dan folder di bawah <c>binaries/</c>, path-nya relatif terhadap <c>binaries/</c>.
      /// Daftar kosong kalau <c>binaries/</c> belum ada.
      /// </summary>
      public abstract Task<IReadOnlyList<ReleaseTargetEntry>> ListBinariesAsync(CancellationToken token);

      /// <summary>Memastikan sebuah folder (dan folder di atasnya) ada.</summary>
      public abstract Task EnsureFolderAsync(string folder, CancellationToken token);

      /// <summary>
      /// Menulis sebuah file, menimpa yang lama. Isi baru tidak pernah terlihat setengah jadi: ditulis ke
      /// nama sementara dulu, baru dipindah ke namanya. Folder tempatnya harus sudah ada.
      /// </summary>
      /// <param name="path">Path file.</param>
      /// <param name="content">Isi file, dibaca dari posisinya sekarang sampai habis.</param>
      /// <param name="progress">Menerima jumlah byte yang sudah terkirim; boleh <c>null</c>.</param>
      /// <param name="token">Membatalkan penulisan; file lama tetap utuh.</param>
      public abstract Task WriteFileAsync(string path, Stream content, IProgress<long>? progress, CancellationToken token);

      /// <summary>Menghapus sebuah file, atau folder beserta isinya. Yang sudah tidak ada diabaikan.</summary>
      public abstract Task DeleteAsync(string path, bool isFolder, CancellationToken token);

      /// <summary>
      /// Batas ukuran satu file yang bisa ditulis ke tujuan ini, atau <c>null</c> kalau tidak dibatasi.
      /// Sekaligus memastikan tujuannya bisa dipakai (mis. CDN server memang dinyalakan).
      /// </summary>
      public virtual Task<long?> GetMaxFileSizeAsync(CancellationToken token) => Task.FromResult<long?>(null);

      /// <summary>Path sebuah file rilis di dalam folder rilis: <c>binaries/</c> + <paramref name="path"/>.</summary>
      public static string BinaryPath(string path) => $"{ReleaseLayout.BinariesFolder}/{path}";

      /// <summary>
      /// Merapikan nama folder rilis yang diketik user: pemisah menjadi <c>/</c> dan tanpa <c>/</c> di
      /// ujungnya. String kosong menjadi <see cref="ReleaseLayout.DefaultReleaseFolder"/>.
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
   /// Membungkus stream yang sedang dikirim: melaporkan berapa yang sudah terbaca, paling sering sepuluh
   /// kali sedetik, dan memutus pengiriman dengan melempar dari Read begitu token dibatalkan. Seek
   /// diteruskan, supaya request masih punya Content-Length dan bisa dikirim ulang setelah token
   /// login diperbarui.
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
