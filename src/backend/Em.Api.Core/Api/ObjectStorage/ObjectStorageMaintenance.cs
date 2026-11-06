using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// Maintenance of object storage: replication and repair of blob copies, version retention, garbage
/// collection of orphan blobs and cleanup of staging folders. Used by the background service and by the
/// maintenance actions; each job is serialized by its own gate in <see cref="ObjectStorageRuntime"/>.
/// </summary>
public sealed class ObjectStorageMaintenance(ObjectStore store, ILogger<ObjectStorageMaintenance> logger)
{
   private ObjectStorageDbContext Db => store.Db;
   private ObjectStorageRuntime Runtime => store.Runtime;

   /// <summary>Progress callback: percent (or <c>null</c> when unknown) and caption.</summary>
   public delegate void Progress(double? percent, string caption);

   private sealed class HashTarget
   {
      public byte[] Hash { get; set; } = [];
      public int Target { get; set; }
   }

   #region Replication and repair

   // Referenced blobs with the highest replica target of the policies whose buckets refer to them.
   private const string TargetsSql = """
      SELECT v.cStoreObjectBlobHash AS Hash, MAX(p.cStorePolicyReplicaTarget) AS Target
      FROM ta_StoreObjectVersion v
      JOIN ta_StoreObject o ON o.cStoreObjectId = v.cStoreObjectId
      JOIN ta_StoreBucket k ON k.cStoreBucketId = o.cStoreBucketId
      JOIN ta_StorePolicy p ON p.cStorePolicyId = k.cStorePolicyId
      WHERE v.cStoreObjectBlobHash IS NOT NULL
      GROUP BY v.cStoreObjectBlobHash
      """;

   private const string UnderReplicatedFilter = """
      WHERE t.Target > (SELECT COUNT(*) FROM ta_StoreObjectBlobVol c
                        JOIN ta_StoreVolume vol ON vol.cStoreVolumeId = c.cStoreVolumeId
                        WHERE c.cStoreObjectBlobHash = t.Hash AND c.cStoreObjectBlobVolState = 2 AND vol.cStoreVolumeState <> 3)
      """;

   /// <summary>Number of referenced blobs with fewer ready copies than their replica target.</summary>
   public async Task<long> CountUnderReplicatedAsync(CancellationToken ct) =>
      (await Db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM ({TargetsSql}) t {UnderReplicatedFilter}").ToListAsync(ct)).Single();

   /// <summary>
   /// Writes copies until each blob reaches its replica target. With <paramref name="verify"/> every blob
   /// is visited and every copy's file is checked for existence and size first (manual repair); without it
   /// only blobs that have fewer ready copies than their target are visited (background replication).
   /// </summary>
   /// <returns>Summary for the task caption or the status card.</returns>
   public async Task<string> ReplicateAsync(bool verify, Progress? progress, CancellationToken ct) {
      await Runtime.RepairGate.WaitAsync(ct);
      try {
         var sql = verify ? $"SELECT t.Hash, t.Target FROM ({TargetsSql}) t" : $"SELECT t.Hash, t.Target FROM ({TargetsSql}) t {UnderReplicatedFilter}";
         var targets = await Db.Database.SqlQueryRaw<HashTarget>(sql).ToListAsync(ct);
         int written = 0, missing = 0, failed = 0, done = 0;
         foreach (var target in targets) {
            ct.ThrowIfCancellationRequested();
            try {
               var result = await EnsureCopiesAsync(target.Hash, verify, ct);
               written += result.Written;
               missing += result.Missing;
               if (result.Short) failed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or InvalidOperationException) {
               failed++;
               logger.LogWarning(ex, "Object storage could not replicate blob {Hash}.", StoreNames.Hex(target.Hash));
            }
            done++;
            if (done % 20 == 0 || done == targets.Count)
               progress?.Invoke(100.0 * done / targets.Count, $"Checked {done:N0} of {targets.Count:N0} blobs");
         }
         return $"{(verify ? "Repair" : "Replication")}: {targets.Count:N0} blobs checked, {written:N0} copies written, " +
                $"{missing:N0} copies found missing, {failed:N0} blobs still below target.";
      }
      finally { Runtime.RepairGate.Release(); }
   }

   private readonly record struct CopyResult(int Written, int Missing, bool Short);

   private async Task<CopyResult> EnsureCopiesAsync(byte[] hash, bool verify, CancellationToken ct) {
      using var _ = await Runtime.HashLock.EnterAsync(hash, ct);
      try {
         var blob = await Db.Blobs.SingleOrDefaultAsync(b => b.cStoreObjectBlobHash == hash, ct);
         if (blob is null || blob.cStoreObjectBlobState != (int)StoreBlobState.Ready) return default;

         // Candidate volumes: the volumes of every policy that refers to the blob, in policy order.
         var policyIds = await (from v in Db.Versions
                                join o in Db.Objects on v.cStoreObjectId equals o.cStoreObjectId
                                join k in Db.Buckets on o.cStoreBucketId equals k.cStoreBucketId
                                where v.cStoreObjectBlobHash == hash
                                select k.cStorePolicyId).Distinct().ToListAsync(ct);
         var policies = await Db.Policies.Where(p => policyIds.Contains(p.cStorePolicyId)).ToListAsync(ct);
         if (policies.Count == 0) return default;
         var target = policies.Max(p => p.cStorePolicyReplicaTarget);
         var candidates = new List<ta_StoreVolume>();
         foreach (var policy in policies.OrderBy(p => p.cStorePolicyName, StringComparer.Ordinal))
            foreach (var volume in await store.PolicyVolumesAsync(policy.cStorePolicyId, ct))
               if (candidates.All(c => c.cStoreVolumeId != volume.cStoreVolumeId)) candidates.Add(volume);

         var volumes = await Db.Volumes.ToDictionaryAsync(v => v.cStoreVolumeId, ct);
         var copies = await Db.Copies.AsTracking().Where(c => c.cStoreObjectBlobHash == hash).ToListAsync(ct);
         var missing = 0;
         foreach (var copy in copies) {
            var volume = volumes[copy.cStoreVolumeId];
            if (volume.cStoreVolumeState == (int)StoreVolumeState.Offline) continue;
            // Under the hash lock nothing else writes this blob, so a copy still marked Writing was left by
            // an interrupted write and is treated as missing.
            if (copy.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Writing) {
               copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
               missing++;
            }
            else if (verify && copy.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready) {
               bool present;
               long length;
               try { present = Runtime.Volume(volume).TryGetLength(hash, out length); }
               catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
               if (present && length == blob.cStoreObjectBlobSize) copy.cStoreObjectBlobVolVerifiedAt = DateTime.UtcNow;
               else {
                  copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
                  missing++;
               }
            }
         }
         await Db.SaveChangesAsync(ct);

         bool IsReady(string volumeId) => copies.Any(c => c.cStoreVolumeId == volumeId && c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready);
         var ready = candidates.Count(v => v.cStoreVolumeState != (int)StoreVolumeState.Offline && IsReady(v.cStoreVolumeId));
         var written = 0;
         foreach (var volume in candidates.Where(v => v.cStoreVolumeState == (int)StoreVolumeState.Active && !IsReady(v.cStoreVolumeId))) {
            if (ready >= target) break;
            if (await CopyFromAnySourceAsync(blob, volume, copies, volumes, ct)) {
               ready++;
               written++;
            }
         }

         // Once the target is met, lost copies have been replaced and their rows are dropped.
         if (ready >= target) {
            foreach (var lost in copies.Where(c => c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Missing).ToList()) {
               var volume = volumes[lost.cStoreVolumeId];
               if (volume.cStoreVolumeState != (int)StoreVolumeState.Offline) {
                  try { Runtime.Volume(volume).Delete(hash); }
                  catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
               }
               Db.Copies.Remove(lost);
            }
            await Db.SaveChangesAsync(ct);
         }
         return new CopyResult(written, missing, ready < target);
      }
      finally { Db.ChangeTracker.Clear(); }
   }

   private async Task<bool> CopyFromAnySourceAsync(ta_StoreObjectBlob blob, ta_StoreVolume target, List<ta_StoreObjectBlobVol> copies,
      Dictionary<string, ta_StoreVolume> volumes, CancellationToken ct) {
      var hash = blob.cStoreObjectBlobHash;
      var sources = copies.Where(c => c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready &&
                                      volumes[c.cStoreVolumeId].cStoreVolumeState != (int)StoreVolumeState.Offline).ToList();
      foreach (var source in sources) {
         Stream? stream;
         try { stream = Runtime.Volume(volumes[source.cStoreVolumeId]).OpenRead(hash); }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
         if (stream is null) {
            source.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
            await Db.SaveChangesAsync(ct);
            continue;
         }
         await using (stream) {
            var copy = store.SetCopyState(copies, hash, target.cStoreVolumeId, StoreBlobCopyState.Writing);
            await Db.SaveChangesAsync(ct);
            try {
               await ObjectStore.WriteCopyAsync(Runtime.Volume(target), hash, stream, blob.cStoreObjectBlobSize, blob.cStoreObjectBlobCrc32, ct);
            }
            catch (StoreCopyMismatchException) {
               // The source copy is damaged: mark it and try the next source.
               source.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
               copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
               await Db.SaveChangesAsync(ct);
               continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
               copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Missing;
               await Db.SaveChangesAsync(ct);
               logger.LogWarning(ex, "Object storage could not write blob {Hash} to volume {Volume}.", StoreNames.Hex(hash), target.cStoreVolumeName);
               return false;
            }
            copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Ready;
            copy.cStoreObjectBlobVolVerifiedAt = DateTime.UtcNow;
            await Db.SaveChangesAsync(ct);
            return true;
         }
      }
      return false;
   }

   #endregion

   #region Retention

   /// <summary>
   /// Applies version retention to every bucket: non-current versions beyond the policy's
   /// <c>KeepVersions</c> or older than its <c>KeepDays</c> are purged, and an object whose only remaining
   /// version is a delete marker is removed.
   /// </summary>
   public async Task<string> RetentionAsync(Progress? progress, CancellationToken ct) {
      await Runtime.RetentionGate.WaitAsync(ct);
      try {
         var buckets = await (from k in Db.Buckets
                              join p in Db.Policies on k.cStorePolicyId equals p.cStorePolicyId
                              orderby k.cStoreBucketName
                              select new { k.cStoreBucketId, k.cStoreBucketName, p.cStorePolicyKeepVersions, p.cStorePolicyKeepDays }).ToListAsync(ct);
         int purged = 0, removed = 0, done = 0;
         foreach (var bucket in buckets) {
            var cutoff = bucket.cStorePolicyKeepDays is { } days ? DateTime.UtcNow.AddDays(-days) : (DateTime?)null;
            string? after = null;
            while (true) {
               ct.ThrowIfCancellationRequested();
               // Objects with history, or whose current version is a delete marker, in pages by ID.
               var page = await Db.Objects
                  .Where(o => o.cStoreBucketId == bucket.cStoreBucketId && (after == null || string.Compare(o.cStoreObjectId, after) > 0) &&
                              (Db.Versions.Count(v => v.cStoreObjectId == o.cStoreObjectId) > 1 ||
                               Db.Versions.Any(v => v.cStoreObjectId == o.cStoreObjectId && v.cStoreObjectVersionId == o.cStoreObjectVersionCurrent && v.cStoreObjectVersionMarker)))
                  .OrderBy(o => o.cStoreObjectId).Select(o => new { o.cStoreObjectId, o.cStoreObjectVersionCurrent }).Take(200).ToListAsync(ct);
               if (page.Count == 0) break;
               after = page[^1].cStoreObjectId;
               foreach (var obj in page) {
                  var versions = await Db.Versions.Where(v => v.cStoreObjectId == obj.cStoreObjectId)
                     .OrderByDescending(v => v.cStoreObjectVersionId)
                     .Select(v => new { v.cStoreObjectVersionId, v.cStoreObjectVersionMarker, v.datestamp }).ToListAsync(ct);
                  var expired = ExpiredVersions(versions.Select(v => (v.cStoreObjectVersionId, v.datestamp)), obj.cStoreObjectVersionCurrent,
                     bucket.cStorePolicyKeepVersions, cutoff);
                  foreach (var version in expired)
                     if (await store.PurgeVersionCoreAsync(bucket.cStoreBucketId, obj.cStoreObjectId, version, ct)) purged++;
                  var left = versions.Count - expired.Length;
                  var current = versions.FirstOrDefault(v => v.cStoreObjectVersionId == obj.cStoreObjectVersionCurrent);
                  if (left == 1 && current is { cStoreObjectVersionMarker: true } &&
                      await store.PurgeVersionCoreAsync(bucket.cStoreBucketId, obj.cStoreObjectId, current.cStoreObjectVersionId, ct)) {
                     purged++;
                     removed++;
                  }
               }
            }
            done++;
            progress?.Invoke(100.0 * done / Math.Max(1, buckets.Count), $"Retention applied to {done:N0} of {buckets.Count:N0} buckets");
         }
         return $"Retention: {buckets.Count:N0} buckets, {purged:N0} versions purged, {removed:N0} deleted objects removed.";
      }
      finally { Runtime.RetentionGate.Release(); }
   }

   /// <summary>
   /// Versions retention purges: among the non-current versions (newest first), those beyond
   /// <paramref name="keepVersions"/> or created before <paramref name="cutoff"/>. The current version is never purged.
   /// </summary>
   /// <param name="versions">Every version of an object as (number, creation time).</param>
   /// <param name="current">Current version number.</param>
   /// <param name="keepVersions">Non-current versions to keep; <c>null</c> keeps all.</param>
   /// <param name="cutoff">Oldest creation time to keep; <c>null</c> keeps all ages.</param>
   internal static int[] ExpiredVersions(IEnumerable<(int Version, DateTime Created)> versions, int? current, int? keepVersions, DateTime? cutoff) =>
      versions.Where(v => v.Version != current).OrderByDescending(v => v.Version)
         .Where((v, i) => (keepVersions is { } keep && i >= keep) || (cutoff is { } c && v.Created < c))
         .Select(v => v.Version).ToArray();

   #endregion

   #region Garbage collection

   private static void CheckGrace(int graceHours) {
      if (graceHours is < 1 or > 720) throw new ActionException("Grace period must be between 1 and 720 hours.", 400);
   }

   /// <summary>Dry run of <see cref="CollectGarbageAsync"/>: nothing is changed.</summary>
   public async Task<StoreGcReview> ReviewGarbageAsync(int graceHours, CancellationToken ct) {
      CheckGrace(graceHours);
      var cutoff = DateTime.UtcNow.AddHours(-graceHours);
      var unreferenced = Db.Blobs.Where(b => b.cStoreObjectBlobState == (int)StoreBlobState.Ready &&
                                             !Db.Versions.Any(v => v.cStoreObjectBlobHash == b.cStoreObjectBlobHash));
      var orphans = Db.Blobs.Where(b => b.cStoreObjectBlobState == (int)StoreBlobState.Orphan &&
                                        !Db.Versions.Any(v => v.cStoreObjectBlobHash == b.cStoreObjectBlobHash));
      var eligible = orphans.Where(b => b.cStoreObjectBlobOrphanAt < cutoff);
      var sample = await eligible.OrderBy(b => b.cStoreObjectBlobOrphanAt).Take(100)
         .Select(b => new { b.cStoreObjectBlobHash, b.cStoreObjectBlobSize, b.cStoreObjectBlobOrphanAt,
            Copies = Db.Copies.Count(c => c.cStoreObjectBlobHash == b.cStoreObjectBlobHash) }).ToListAsync(ct);
      return new StoreGcReview(graceHours, await unreferenced.CountAsync(ct), await orphans.CountAsync(ct),
         (await orphans.SumAsync(b => (long?)b.cStoreObjectBlobSize, ct) ?? 0) + (await unreferenced.SumAsync(b => (long?)b.cStoreObjectBlobSize, ct) ?? 0),
         await eligible.CountAsync(ct), await eligible.SumAsync(b => (long?)b.cStoreObjectBlobSize, ct) ?? 0,
         sample.Select(b => new StoreGcBlob(StoreNames.Hex(b.cStoreObjectBlobHash), b.cStoreObjectBlobSize, b.cStoreObjectBlobOrphanAt, b.Copies)).ToArray());
   }

   /// <summary>
   /// Marks unreferenced blobs as orphans, returns referenced orphans to ready, then deletes orphans older than
   /// <paramref name="graceHours"/>: every copy's file first, then the rows. Each blob is deleted under its
   /// hash lock after checking again that no version refers to it. A blob with a copy on an offline volume
   /// is skipped until the volume is back.
   /// </summary>
   public async Task<string> CollectGarbageAsync(int graceHours, Progress? progress, CancellationToken ct) {
      CheckGrace(graceHours);
      await Runtime.GcGate.WaitAsync(ct);
      try {
         var now = DateTime.UtcNow;
         var marked = await Db.Blobs.Where(b => b.cStoreObjectBlobState == (int)StoreBlobState.Ready &&
                                                !Db.Versions.Any(v => v.cStoreObjectBlobHash == b.cStoreObjectBlobHash))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.cStoreObjectBlobState, (int)StoreBlobState.Orphan)
               .SetProperty(b => b.cStoreObjectBlobOrphanAt, (DateTime?)now), ct);
         var revived = await Db.Blobs.Where(b => b.cStoreObjectBlobState == (int)StoreBlobState.Orphan &&
                                                 Db.Versions.Any(v => v.cStoreObjectBlobHash == b.cStoreObjectBlobHash))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.cStoreObjectBlobState, (int)StoreBlobState.Ready)
               .SetProperty(b => b.cStoreObjectBlobOrphanAt, (DateTime?)null), ct);

         var cutoff = now.AddHours(-graceHours);
         var candidates = await Db.Blobs.Where(b => b.cStoreObjectBlobState == (int)StoreBlobState.Orphan && b.cStoreObjectBlobOrphanAt < cutoff)
            .Select(b => b.cStoreObjectBlobHash).ToListAsync(ct);
         int deleted = 0, skipped = 0, done = 0;
         long bytes = 0;
         foreach (var hash in candidates) {
            ct.ThrowIfCancellationRequested();
            using (await Runtime.HashLock.EnterAsync(hash, ct)) {
               try {
                  var blob = await Db.Blobs.AsTracking().SingleOrDefaultAsync(b => b.cStoreObjectBlobHash == hash, ct);
                  if (blob is null || blob.cStoreObjectBlobState != (int)StoreBlobState.Orphan || blob.cStoreObjectBlobOrphanAt >= cutoff ||
                      await Db.Versions.AnyAsync(v => v.cStoreObjectBlobHash == hash, ct)) continue;
                  var copies = await Db.Copies.AsTracking().Where(c => c.cStoreObjectBlobHash == hash).ToListAsync(ct);
                  var volumes = await Db.Volumes.ToDictionaryAsync(v => v.cStoreVolumeId, ct);
                  if (copies.Any(c => volumes[c.cStoreVolumeId].cStoreVolumeState == (int)StoreVolumeState.Offline)) { skipped++; continue; }
                  foreach (var copy in copies) copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Deleting;
                  await Db.SaveChangesAsync(ct);
                  var failed = false;
                  foreach (var copy in copies) {
                     try {
                        Runtime.Volume(volumes[copy.cStoreVolumeId]).Delete(hash);
                        Db.Copies.Remove(copy);
                     }
                     catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                        failed = true;
                        logger.LogWarning(ex, "Object storage could not delete blob {Hash} from volume {Volume}.", StoreNames.Hex(hash), copy.cStoreVolumeId);
                     }
                  }
                  if (!failed) Db.Blobs.Remove(blob);
                  await Db.SaveChangesAsync(ct);
                  if (failed) { skipped++; continue; }
                  deleted++;
                  bytes += blob.cStoreObjectBlobSize;
               }
               finally { Db.ChangeTracker.Clear(); }
            }
            done++;
            if (done % 20 == 0 || done == candidates.Count)
               progress?.Invoke(100.0 * done / candidates.Count, $"Collected {done:N0} of {candidates.Count:N0} orphan blobs");
         }
         return $"Garbage collection: {marked:N0} blobs marked orphan, {revived:N0} returned to ready, {deleted:N0} deleted ({bytes:N0} bytes), {skipped:N0} skipped.";
      }
      finally { Runtime.GcGate.Release(); }
   }

   #endregion

   #region Startup

   /// <summary>Deletes leftover staging content on every reachable volume; only one instance writes, so all of it is stale at startup.</summary>
   public async Task<int> CleanStagingAsync(CancellationToken ct) {
      var count = 0;
      foreach (var volume in await Db.Volumes.Where(v => v.cStoreVolumeState != (int)StoreVolumeState.Offline).ToListAsync(ct)) {
         try { count += Runtime.Volume(volume).CleanStaging(); }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ActionException) {
            logger.LogWarning(ex, "Object storage could not clean the staging folder of volume {Volume}.", volume.cStoreVolumeName);
         }
      }
      return count;
   }

   #endregion
}
