using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Dilempar Sync saat keadaan yang dibandingkan sudah tidak berlaku lagi: <c>release.json</c> di tujuan
   /// berubah (ada yang Sync lebih dulu), atau local publish folder berubah. Jalan keluarnya selalu sama:
   /// bandingkan ulang.
   /// </summary>
   public sealed class ReleaseConflictException(string message) : Exception(message);

   /// <summary>
   /// Menerbitkan local publish folder ke tujuan, satu arah dan tanpa lock, dengan urutan yang tercantum
   /// di <c>doc/release-format.md</c> bagian 7: folder, unggah file baru/berubah, <c>release.json.sig</c>,
   /// <c>release.json</c>, lalu hapus yang tidak tercantum.
   /// </summary>
   public static class ReleaseSync
   {
      /// <summary>
      /// Menjalankan Sync di atas hasil perbandingan <paramref name="comparison"/>.
      /// </summary>
      /// <param name="target">Tujuan yang sama dengan yang dipakai saat membandingkan.</param>
      /// <param name="comparison">Hasil Compare terakhir.</param>
      /// <param name="signingKey">Private key ECDSA P-256 untuk menandatangani <c>release.json</c>.</param>
      /// <param name="progress">Menerima kemajuan; boleh <c>null</c>.</param>
      /// <param name="token">
      /// Membatalkan Sync. Yang dibatalkan sebelum <c>release.json.sig</c> ditulis tidak menulis
      /// <c>release.json</c> baru; Sync yang sama tinggal diulang.
      /// </param>
      /// <returns>Manifest yang sudah diterbitkan.</returns>
      /// <exception cref="ReleaseConflictException">Tujuan atau local publish folder berubah sejak dibandingkan.</exception>
      public static async Task<ReleaseManifest> SyncAsync(ReleaseTarget target, ReleaseComparison comparison,
         ECDsa signingKey, IProgress<ReleaseProgress>? progress, CancellationToken token) {
         var local = comparison.Local;
         var uploads = comparison.Items.Where(ReleaseComparison.IsUpload).ToArray();

         progress?.Report(new ReleaseProgress("Checking", 0, 0, null, 0, 0));
         if (await Task.Run(local.FindChange, token).ConfigureAwait(false) is { } changed) {
            throw new ReleaseConflictException(
               $"The local publish folder has changed since it was compared ('{changed}'). Compare again.");
         }

         if (await target.GetMaxFileSizeAsync(token).ConfigureAwait(false) is { } maxSize &&
             uploads.Where(r => r.LocalSize > maxSize).ToArray() is { Length: > 0 } tooLarge) {
            throw new InvalidOperationException(
               $"These files are larger than the {maxSize:N0} bytes the target accepts per file:\n" +
               string.Join("\n", tooLarge.Take(15).Select(r => r.Path)));
         }

         // The guard that stands in for a lock: whoever synced since the compare wins, and this Sync stops
         // before touching anything.
         var current = await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token).ConfigureAwait(false);
         if (!SameBytes(current, comparison.RemoteManifestBytes)) {
            throw new ReleaseConflictException(
               "The release at the target has changed since it was compared (someone else may have synced). Compare again.");
         }

         // Signed before the first upload, so a key that cannot sign fails with nothing touched.
         var manifest = new ReleaseManifest {
            PublishedAtUtc = TruncateToSeconds(DateTime.UtcNow),
            Files = local.Files
         };
         var manifestBytes = ReleaseManifestSerializer.Serialize(manifest);
         var signatureBytes = ReleaseManifestSerializer.SerializeSignature(ReleaseSignature.Sign(manifestBytes, signingKey));

         // 1. Folders, parents first.
         var folders = uploads.Select(r => ReleaseTarget.ParentOf(r.Path))
            .OfType<string>()
            .SelectMany(Ancestry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.Count(c => c == '/'))
            .ThenBy(r => r, StringComparer.Ordinal)
            .ToArray();
         progress?.Report(new ReleaseProgress("Creating folders", 0, folders.Length + 1, null, 0, 0));
         await target.EnsureFolderAsync(ReleaseLayout.BinariesFolder, token).ConfigureAwait(false);
         for (var index = 0; index < folders.Length; index++) {
            progress?.Report(new ReleaseProgress("Creating folders", index + 2, folders.Length + 1, folders[index], 0, 0));
            await target.EnsureFolderAsync(ReleaseTarget.BinaryPath(folders[index]), token).ConfigureAwait(false);
         }

         // 2. New and changed files.
         var total = uploads.Sum(r => r.LocalSize ?? 0);
         var done = 0L;
         var sinceReport = Stopwatch.StartNew();
         for (var index = 0; index < uploads.Length; index++) {
            token.ThrowIfCancellationRequested();
            var item = uploads[index];
            var before = done;
            var position = index + 1;
            progress?.Report(new ReleaseProgress("Uploading", position, uploads.Length, item.Path, before, total));
            var fileProgress = new InlineProgress<long>(sent => {
               if (sinceReport.ElapsedMilliseconds < 100) return;
               sinceReport.Restart();
               progress?.Report(new ReleaseProgress("Uploading", position, uploads.Length, item.Path, before + sent, total));
            });

            await using var file = new FileStream(local.FullPathOf(item.Path), FileMode.Open, FileAccess.Read,
               FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await target.WriteFileAsync(ReleaseTarget.BinaryPath(item.Path), file, fileProgress, token)
               .ConfigureAwait(false);
            done += item.LocalSize ?? 0;
         }

         // 3 and 4. Signature first, then the manifest it signs. Past this point Cancel no longer stops the
         // pair: a new signature beside the old manifest would leave the release unreadable until the next
         // Sync.
         token.ThrowIfCancellationRequested();
         progress?.Report(new ReleaseProgress("Writing release.json", 0, 0, null, total, total));
         await WriteBytesAsync(target, ReleaseLayout.SignatureFileName, signatureBytes, CancellationToken.None)
            .ConfigureAwait(false);
         await WriteBytesAsync(target, ReleaseLayout.ManifestFileName, manifestBytes, CancellationToken.None)
            .ConfigureAwait(false);

         // 5. Whatever the new release no longer lists. Files inside a folder that goes as a whole are left
         // to that folder's delete.
         var deletes = comparison.Items
            .Where(r => r.Status == ReleaseDiffStatus.Removed && !comparison.FoldersToDelete.Any(f =>
               r.Path.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(r => (r.Path, IsFolder: false))
            .Concat(comparison.FoldersToDelete.Select(r => (Path: r, IsFolder: true)))
            .ToArray();
         for (var index = 0; index < deletes.Length; index++) {
            progress?.Report(new ReleaseProgress("Deleting", index + 1, deletes.Length, deletes[index].Path, 0, 0));
            await target.DeleteAsync(ReleaseTarget.BinaryPath(deletes[index].Path), deletes[index].IsFolder, token)
               .ConfigureAwait(false);
         }

         return manifest;
      }

      /// <summary><c>true</c> kalau kedua isi file sama persis, termasuk sama-sama tidak ada.</summary>
      public static bool SameBytes(byte[]? left, byte[]? right) =>
         left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

      private static async Task WriteBytesAsync(ReleaseTarget target, string path, byte[] bytes, CancellationToken token) {
         await using var stream = new MemoryStream(bytes, writable: false);
         await target.WriteFileAsync(path, stream, null, token).ConfigureAwait(false);
      }

      private static IEnumerable<string> Ancestry(string folder) {
         for (var current = folder; current is not null; current = ReleaseTarget.ParentOf(current))
            yield return current;
      }

      private static DateTime TruncateToSeconds(DateTime value) =>
         new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
   }
}
