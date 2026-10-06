using System.Data;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Garbage collection registry. Blob yatim = tidak disebut manifest mana pun, dicatat sebelum batas waktu,
   /// dan tidak punya tautan yang lebih baru dari batas waktu (tanda push sedang berjalan). Baris dihapus lebih
   /// dulu daripada berkas, sehingga metadata tidak pernah menunjuk berkas yang hilang; berkas yang gagal
   /// dihapus tersapu sebagai berkas tanpa metadata pada run berikutnya.
   /// </summary>
   internal sealed class CtnGarbageCollector(CtnContext db, CtnBlobStore store)
   {
      // Hanya satu run sekaligus; review tidak memakainya.
      private static readonly SemaphoreSlim RunLock = new(1, 1);

      public Task<CtnGcReport> RunAsync(int graceHours, bool dryRun, CancellationToken ct) =>
         RunAsync(graceHours, dryRun, DateTime.UtcNow, ct);

      internal async Task<CtnGcReport> RunAsync(int graceHours, bool dryRun, DateTime utcNow, CancellationToken ct) {
         if (graceHours < 1 || graceHours > ICtnServices.GcMaxGraceHours) {
            throw new ActionException($"Grace period must be 1-{ICtnServices.GcMaxGraceHours} hours.", 400);
         }

         if (!dryRun && !await RunLock.WaitAsync(0, ct)) {
            throw new ActionException("Garbage collection is already running.", 409);
         }

         try {
            var cutoff = utcNow.AddHours(-graceHours);
            var report = new CtnGcReport { DryRun = dryRun, GraceHours = graceHours, CutoffUtc = cutoff };
            var warnings = new List<string>();
            await CollectBlobsAsync(report, cutoff, dryRun, warnings, ct);
            await CollectStaleUploadsAsync(report, cutoff, dryRun, warnings, ct);
            await CollectOrphanBlobFilesAsync(report, cutoff, dryRun, warnings, ct);
            await CollectOrphanUploadFilesAsync(report, cutoff, dryRun, warnings, ct);
            report.Warnings = [.. warnings];
            return report;
         } finally {
            if (!dryRun) RunLock.Release();
         }
      }

      private IQueryable<ta_CtnBlob> Candidates(DateTime cutoff) =>
         db.Blobs.Where(b => b.datestamp < cutoff &&
                             !db.ManifestBlobs.Any(mb => mb.cCtnBlobId == b.cCtnBlobId) &&
                             !db.BlobLinks.Any(l => l.cCtnBlobId == b.cCtnBlobId && l.datestamp >= cutoff));

      #region Blob yatim

      private sealed record BlobRow(string Id, string Digest, long Size, DateTime CreatedAt);

      private async Task CollectBlobsAsync(CtnGcReport report, DateTime cutoff, bool dryRun, List<string> warnings, CancellationToken ct) {
         var all = await Candidates(cutoff).OrderByDescending(b => b.cCtnBlobSize)
            .Select(b => new BlobRow(b.cCtnBlobId, b.cCtnBlobDigest, b.cCtnBlobSize, b.datestamp)).ToListAsync(ct);

         if (dryRun) {
            report.BlobCount = all.Count;
            report.BlobBytes = all.Sum(b => b.Size);
            var listed = all.Take(CtnGcReport.MaxListedBlobs).ToList();
            var ids = listed.Select(b => b.Id).ToList();
            var links = await (from l in db.BlobLinks
                               join i in db.Images on l.cCtnImageId equals i.cCtnImageId
                               join r in db.Roots on i.cCtnRootId equals r.cCtnRootId
                               where ids.Contains(l.cCtnBlobId)
                               select new { l.cCtnBlobId, Name = r.cCtnRootName + "/" + i.cCtnImageName }).ToListAsync(ct);
            var byBlob = links.GroupBy(l => l.cCtnBlobId)
               .ToDictionary(g => g.Key, g => g.Select(l => l.Name).Order(StringComparer.Ordinal).ToArray());
            report.Blobs = [.. listed.Select(b => ToReportBlob(b, byBlob.GetValueOrDefault(b.Id) ?? []))];
            return;
         }

         var deleted = new List<BlobRow>();
         foreach (var blob in all) {
            try {
               using (await CtnBlobGate.EnterAsync(ct)) {
                  await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                  if (!await Candidates(cutoff).AnyAsync(b => b.cCtnBlobId == blob.Id, ct)) {
                     await tx.RollbackAsync(ct);
                     continue;   // sudah dipakai lagi oleh push; bukan warning
                  }

                  await db.BlobLinks.Where(l => l.cCtnBlobId == blob.Id).ExecuteDeleteAsync(ct);
                  await db.Blobs.Where(b => b.cCtnBlobId == blob.Id).ExecuteDeleteAsync(ct);
                  await tx.CommitAsync(ct);
                  if (!TryDeleteFile(store.BlobPath(blob.Digest), out var error)) {
                     warnings.Add($"Blob {blob.Digest}: row removed, file kept ({error}).");
                  }
               }

               deleted.Add(blob);   // dihitung meski berkasnya gagal dihapus: metadatanya sudah hilang
            } catch (Exception ex) when (ex is not OperationCanceledException) {
               warnings.Add($"Blob {blob.Digest} skipped: {ex.Message}");
            }
         }

         report.BlobCount = deleted.Count;
         report.BlobBytes = deleted.Sum(b => b.Size);
         report.Blobs = [.. deleted.Take(CtnGcReport.MaxListedBlobs).Select(b => ToReportBlob(b, []))];
      }

      private static CtnGcBlob ToReportBlob(BlobRow b, string[] linkedImages) => new() {
         Digest = b.Digest,
         Size = b.Size,
         CreatedAt = b.CreatedAt,
         LinkedImages = linkedImages
      };

      #endregion

      #region Upload basi

      private async Task CollectStaleUploadsAsync(CtnGcReport report, DateTime cutoff, bool dryRun, List<string> warnings, CancellationToken ct) {
         var stale = await db.Uploads.Where(u => u.ustamp < cutoff)
            .Select(u => new { u.cCtnUploadId, u.cCtnUploadSize }).ToListAsync(ct);

         foreach (var upload in stale) {
            var path = SafeUploadPath(upload.cCtnUploadId);
            var info = path is null ? null : new FileInfo(path);
            var size = info is { Exists: true } ? info.Length : upload.cCtnUploadSize;

            if (dryRun) {
               report.StaleUploadCount++;
               report.StaleUploadBytes += size;
               continue;
            }

            try {
               var rows = await db.Uploads.Where(u => u.cCtnUploadId == upload.cCtnUploadId && u.ustamp < cutoff).ExecuteDeleteAsync(ct);
               if (rows != 1) continue;   // upload hidup lagi sejak review

               if (path is not null && !TryDeleteFile(path, out var error)) {
                  warnings.Add($"Upload {upload.cCtnUploadId}: row removed, file kept ({error}).");
               }

               report.StaleUploadCount++;
               report.StaleUploadBytes += size;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
               warnings.Add($"Upload {upload.cCtnUploadId} skipped: {ex.Message}");
            }
         }
      }

      // Id yang tidak berbentuk ULID tidak punya path sah; barisnya tetap bisa dihapus.
      private string? SafeUploadPath(string uploadId) {
         try {
            return store.UploadPath(uploadId);
         } catch (ArgumentException) {
            return null;
         }
      }

      #endregion

      #region Berkas tanpa metadata

      private async Task CollectOrphanBlobFilesAsync(CtnGcReport report, DateTime cutoff, bool dryRun, List<string> warnings, CancellationToken ct) {
         var dir = Path.Combine(store.RootPath, "blobs", "sha256");
         if (!Directory.Exists(dir)) return;

         try {
            ManagedStorageSettings.RejectLinks(dir);
         } catch (ActionException ex) {
            warnings.Add($"Blob folder skipped: {ex.Message}");
            return;
         }

         var known = (await db.Blobs.Select(b => b.cCtnBlobDigest).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
         foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(store.RootPath, file);
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
               warnings.Add($"Link skipped: {relative}");
               continue;
            }

            var name = info.Name;
            if (!IsBlobFileName(name) || info.Directory?.Name != name[..2]) {
               warnings.Add($"Unexpected file skipped: {relative}");
               continue;
            }

            var digest = "sha256:" + name;
            if (known.Contains(digest) || info.LastWriteTimeUtc >= cutoff) continue;

            if (dryRun) {
               report.OrphanBlobFileCount++;
               report.OrphanBlobFileBytes += info.Length;
               continue;
            }

            try {
               var length = info.Length;
               using (await CtnBlobGate.EnterAsync(ct)) {
                  if (await db.Blobs.AnyAsync(b => b.cCtnBlobDigest == digest, ct)) continue;   // dicatat push sejak review
                  if (!TryDeleteFile(file, out var error)) {
                     warnings.Add($"File {relative} could not be deleted ({error}).");
                     continue;
                  }
               }

               report.OrphanBlobFileCount++;
               report.OrphanBlobFileBytes += length;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
               warnings.Add($"File {relative} skipped: {ex.Message}");
            }
         }
      }

      private async Task CollectOrphanUploadFilesAsync(CtnGcReport report, DateTime cutoff, bool dryRun, List<string> warnings, CancellationToken ct) {
         var dir = Path.Combine(store.RootPath, "uploads");
         if (!Directory.Exists(dir)) return;

         try {
            ManagedStorageSettings.RejectLinks(dir);
         } catch (ActionException ex) {
            warnings.Add($"Upload folder skipped: {ex.Message}");
            return;
         }

         var known = (await db.Uploads.Select(u => u.cCtnUploadId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
         foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)) {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(store.RootPath, file);
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
               warnings.Add($"Link skipped: {relative}");
               continue;
            }

            var name = info.Name;
            if (name.Length != 26 || !name.All(char.IsAsciiLetterOrDigit)) {
               warnings.Add($"Unexpected file skipped: {relative}");
               continue;
            }

            if (known.Contains(name) || info.LastWriteTimeUtc >= cutoff) continue;

            if (dryRun) {
               report.OrphanUploadFileCount++;
               report.OrphanUploadFileBytes += info.Length;
               continue;
            }

            try {
               var length = info.Length;
               if (await db.Uploads.AnyAsync(u => u.cCtnUploadId == name, ct)) continue;   // dicatat sejak review
               if (!TryDeleteFile(file, out var error)) {
                  warnings.Add($"File {relative} could not be deleted ({error}).");
                  continue;
               }

               report.OrphanUploadFileCount++;
               report.OrphanUploadFileBytes += length;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
               warnings.Add($"File {relative} skipped: {ex.Message}");
            }
         }
      }

      private static bool IsBlobFileName(string name) =>
         name.Length == 64 && name.All(c => char.IsAsciiHexDigit(c) && !char.IsAsciiLetterUpper(c));

      #endregion

      private static bool TryDeleteFile(string path, out string? error) {
         try {
            File.Delete(path);   // tidak melempar bila berkas tidak ada
            error = null;
            return true;
         } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            error = ex.Message;
            return false;
         }
      }
   }
}
