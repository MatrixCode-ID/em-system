using System.Buffers;
using System.Data;
using System.Security.Cryptography;
using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage.Volumes;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// Implementation of <see cref="IObjectStore"/> on <see cref="ObjectStorageDbContext"/> and the volumes of
/// <see cref="ObjectStorageRuntime"/>. Content is written to volumes before any version refers to it, so a
/// version never points to a blob without a ready copy; a crash leaves at most an orphan blob for garbage
/// collection.
/// </summary>
public sealed class ObjectStore(ObjectStorageDbContext db, ObjectStorageRuntime runtime) : IObjectStore
{
   private const int BufferSize = 81920;

   internal ObjectStorageDbContext Db => db;
   internal ObjectStorageRuntime Runtime => runtime;

   #region Lookups

   internal async Task<(ta_StoreBucket Bucket, ta_StorePolicy Policy)> RequireBucketAsync(string name, CancellationToken ct) {
      StoreNames.Bucket(name);
      var bucket = await db.Buckets.SingleOrDefaultAsync(b => b.cStoreBucketName == name, ct) ?? throw new ActionException("Bucket not found.", 404);
      var policy = await db.Policies.SingleAsync(p => p.cStorePolicyId == bucket.cStorePolicyId, ct);
      return (bucket, policy);
   }

   internal async Task<ta_StoreBucket> RequireBucketByIdAsync(string bucketId, CancellationToken ct) =>
      await db.Buckets.SingleOrDefaultAsync(b => b.cStoreBucketId == bucketId, ct) ?? throw new ActionException("Bucket not found.", 404);

   private async Task<ta_StoreObject?> FindObjectAsync(string bucketId, string key, CancellationToken ct) {
      var hash = StoreNames.KeyHash(key);
      return await db.Objects.SingleOrDefaultAsync(o => o.cStoreBucketId == bucketId && o.cStoreObjectKeyHash == hash, ct);
   }

   /// <summary>Volumes of a policy in priority order.</summary>
   internal async Task<List<ta_StoreVolume>> PolicyVolumesAsync(string policyId, CancellationToken ct) =>
      await (from pv in db.PolicyVolumes
             join v in db.Volumes on pv.cStoreVolumeId equals v.cStoreVolumeId
             where pv.cStorePolicyId == policyId
             orderby pv.cStorePolicyVolumeOrder, v.cStoreVolumeName
             select v).ToListAsync(ct);

   internal static StoreObjectInfo Info(ta_StoreObject o, ta_StoreObjectVersion v) =>
      new(o.cStoreObjectId, o.cStoreBucketId, o.cStoreObjectKey, v.cStoreObjectVersionId, v.cStoreObjectVersionSize,
         v.cStoreObjectVersionMime, v.cStoreObjectVersionMarker, v.datestamp,
         v.cStoreObjectBlobHash is null ? null : StoreNames.Hex(v.cStoreObjectBlobHash));

   private static void RequireWritable(ta_StoreBucket bucket) {
      if (bucket.cStoreBucketState != (int)StoreBucketState.Active) throw new ActionException("Bucket is read-only.", 409);
   }

   #endregion

   #region Upload

   /// <inheritdoc />
   public async Task<StoreObjectInfo> PutAsync(string bucketName, string key, Stream content, string? mime = null,
      IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) {
      ArgumentNullException.ThrowIfNull(content);
      StoreNames.Key(key);
      mime = StoreNames.Mime(mime);
      var json = StoreNames.Metadata(metadata);
      var (bucket, policy) = await RequireBucketAsync(bucketName, ct);
      RequireWritable(bucket);
      var keyHash = StoreNames.KeyHash(key);

      // Early checks against the counters, before any content is read. The final quota check happens in
      // the transaction that records the version.
      long? known = null;
      try { if (content.CanSeek) known = content.Length - content.Position; } catch (NotSupportedException) { }
      if (policy.cStorePolicyMaxObjectBytes is { } max && known > max)
         throw new ActionException($"Object exceeds the maximum size of {max:N0} bytes.", 413);
      if (policy.cStorePolicyQuotaBytes is { } quota && bucket.cStoreBucketBytes + (known ?? 0) > quota)
         throw new ActionException("Bucket byte quota would be exceeded.", 409);
      if (policy.cStorePolicyQuotaObjects is { } quotaObjects && bucket.cStoreBucketObjects >= quotaObjects &&
          !await db.Objects.AnyAsync(o => o.cStoreBucketId == bucket.cStoreBucketId && o.cStoreObjectKeyHash == keyHash, ct))
         throw new ActionException("Bucket object quota would be exceeded.", 409);

      var volumes = await PolicyVolumesAsync(policy.cStorePolicyId, ct);
      var writable = volumes.Where(v => v.cStoreVolumeState == (int)StoreVolumeState.Active).ToList();
      if (writable.Count < policy.cStorePolicyReplicaMin)
         throw new ActionException("Not enough active volumes for the bucket's policy.", 503);

      var stagingVolume = writable[0];
      await using var staging = runtime.Volume(stagingVolume).BeginWrite();
      var (hash, crc, size) = await CopyHashedAsync(content, staging.Stream, policy.cStorePolicyMaxObjectBytes, ct);

      using (await runtime.HashLock.EnterAsync(hash, ct)) {
         var ready = await StoreBlobAsync(hash, crc, size, volumes, stagingVolume, staging, policy.cStorePolicyReplicaMin, ct);
         var version = new ta_StoreObjectVersion {
            cStoreObjectBlobHash = hash, cStoreObjectVersionSize = size, cStoreObjectVersionMime = mime, json_object = json
         };
         var info = await InBucketTransactionAsync(bucket.cStoreBucketId, async (b, p) => {
            RequireWritable(b);
            var obj = await LockObjectAsync(b.cStoreBucketId, keyHash, ct);
            return await AppendVersionAsync(b, p, obj, key, keyHash, version, ct);
         }, ct);
         if (ready < policy.cStorePolicyReplicaTarget) runtime.SignalReplication();
         return info;
      }
   }

   private static async Task<(byte[] Hash, byte[] Crc, long Size)> CopyHashedAsync(Stream source, Stream target, long? max, CancellationToken ct) {
      using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      var crc = new StoreCrc32();
      long total = 0;
      var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
      try {
         int read;
         while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0) {
            total += read;
            if (max is { } limit && total > limit) throw new ActionException($"Object exceeds the maximum size of {limit:N0} bytes.", 413);
            sha.AppendData(buffer, 0, read);
            crc.Append(buffer.AsSpan(0, read));
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
         }
      }
      finally { ArrayPool<byte>.Shared.Return(buffer); }
      await target.FlushAsync(ct);
      return (sha.GetHashAndReset(), crc.ToBytes(), total);
   }

   /// <summary>
   /// Makes sure the blob exists with at least <paramref name="replicaMin"/> ready copies on the policy's
   /// volumes. Must be called under the hash lock. Returns the number of ready copies on policy volumes.
   /// </summary>
   private async Task<int> StoreBlobAsync(byte[] hash, byte[] crc, long size, List<ta_StoreVolume> policyVolumes,
      ta_StoreVolume stagingVolume, IStoreStaging staging, int replicaMin, CancellationToken ct) {
      var now = DateTime.UtcNow;
      var blob = await db.Blobs.AsTracking().SingleOrDefaultAsync(b => b.cStoreObjectBlobHash == hash, ct);
      if (blob is null) {
         // Created as an orphan: it only becomes ready in the transaction that records a version, so content
         // left by a crash before that point is collected after the grace period.
         blob = new ta_StoreObjectBlob {
            cStoreObjectBlobHash = hash, cStoreObjectBlobCrc32 = crc, cStoreObjectBlobSize = size,
            cStoreObjectBlobState = (int)StoreBlobState.Orphan, cStoreObjectBlobOrphanAt = now, datestamp = now
         };
         db.Blobs.Add(blob);
         await db.SaveChangesAsync(ct);
      }
      else if (blob.cStoreObjectBlobSize != size || !blob.cStoreObjectBlobCrc32.AsSpan().SequenceEqual(crc)) {
         throw new InvalidOperationException("Stored blob metadata does not match the content hash.");
      }

      var copies = await db.Copies.AsTracking().Where(c => c.cStoreObjectBlobHash == hash).ToListAsync(ct);
      bool IsReady(string volumeId) => copies.Any(c => c.cStoreVolumeId == volumeId && c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready);
      var ready = policyVolumes.Count(v => v.cStoreVolumeState != (int)StoreVolumeState.Offline && IsReady(v.cStoreVolumeId));
      var need = replicaMin - ready;
      if (need <= 0) return ready;

      var targets = policyVolumes.Where(v => v.cStoreVolumeState == (int)StoreVolumeState.Active && !IsReady(v.cStoreVolumeId)).Take(need).ToList();
      if (targets.Count < need) throw new ActionException("Not enough active volumes to store the required copies.", 503);

      // Other volumes first, copied from the staged file; the staging volume last, because committing moves
      // the staged file into place and it can no longer be read as a source.
      foreach (var target in targets.OrderBy(t => t.cStoreVolumeId == stagingVolume.cStoreVolumeId ? 1 : 0)) {
         var copy = SetCopyState(copies, hash, target.cStoreVolumeId, StoreBlobCopyState.Writing);
         await db.SaveChangesAsync(ct);
         if (target.cStoreVolumeId == stagingVolume.cStoreVolumeId) {
            await staging.CommitAsync(hash, size, ct);
         }
         else {
            await using var source = staging.OpenRead();
            await WriteCopyAsync(runtime.Volume(target), hash, source, size, crc, ct);
         }
         copy.cStoreObjectBlobVolState = (int)StoreBlobCopyState.Ready;
         copy.cStoreObjectBlobVolVerifiedAt = DateTime.UtcNow;
         await db.SaveChangesAsync(ct);
         ready++;
      }
      return ready;
   }

   internal ta_StoreObjectBlobVol SetCopyState(List<ta_StoreObjectBlobVol> copies, byte[] hash, string volumeId, StoreBlobCopyState state) {
      var copy = copies.FirstOrDefault(c => c.cStoreVolumeId == volumeId);
      if (copy is null) {
         copy = new ta_StoreObjectBlobVol { cStoreObjectBlobHash = hash, cStoreVolumeId = volumeId, datestamp = DateTime.UtcNow };
         db.Copies.Add(copy);
         copies.Add(copy);
      }
      copy.cStoreObjectBlobVolState = (int)state;
      return copy;
   }

   /// <summary>Writes a copy of a blob to a volume and verifies its size and CRC32 before moving it into place.</summary>
   internal static async Task WriteCopyAsync(IStoreVolume volume, byte[] hash, Stream source, long size, byte[] crc, CancellationToken ct) {
      await using var staging = volume.BeginWrite();
      var check = new StoreCrc32();
      long total = 0;
      var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
      try {
         int read;
         while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0) {
            total += read;
            if (total > size) break;
            check.Append(buffer.AsSpan(0, read));
            await staging.Stream.WriteAsync(buffer.AsMemory(0, read), ct);
         }
      }
      finally { ArrayPool<byte>.Shared.Return(buffer); }
      if (total != size || !check.ToBytes().AsSpan().SequenceEqual(crc)) throw new StoreCopyMismatchException();
      await staging.CommitAsync(hash, size, ct);
   }

   #endregion

   #region Version transactions

   /// <summary>
   /// Runs <paramref name="work"/> in a serializable transaction that holds an update lock on the bucket row,
   /// so every change of a bucket's versions and counters is serialized. Changes are saved and committed when
   /// the work returns; on failure nothing is kept.
   /// </summary>
   internal async Task<T> InBucketTransactionAsync<T>(string bucketId, Func<ta_StoreBucket, ta_StorePolicy, Task<T>> work, CancellationToken ct) {
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
         var bucket = await db.Buckets.FromSql($"SELECT * FROM dbo.ta_StoreBucket WITH (UPDLOCK, ROWLOCK) WHERE cStoreBucketId = {bucketId}")
            .AsTracking().SingleOrDefaultAsync(ct) ?? throw new ActionException("Bucket not found.", 404);
         var policy = await db.Policies.SingleAsync(p => p.cStorePolicyId == bucket.cStorePolicyId, ct);
         var result = await work(bucket, policy);
         await db.SaveChangesAsync(ct);
         await tx.CommitAsync(ct);
         return result;
      }
      finally {
         db.ChangeTracker.Clear();
      }
   }

   private async Task<ta_StoreObject?> LockObjectAsync(string bucketId, byte[] keyHash, CancellationToken ct) =>
      await db.Objects.FromSql($"SELECT * FROM dbo.ta_StoreObject WITH (UPDLOCK, HOLDLOCK) WHERE cStoreBucketId = {bucketId} AND cStoreObjectKeyHash = {keyHash}")
         .AsTracking().SingleOrDefaultAsync(ct);

   /// <summary>
   /// Adds <paramref name="version"/> as the new current version of <paramref name="obj"/>, creating the object
   /// when it is <c>null</c>, and updates the bucket counters and quota. Call inside <see cref="InBucketTransactionAsync{T}"/>.
   /// </summary>
   private async Task<StoreObjectInfo> AppendVersionAsync(ta_StoreBucket bucket, ta_StorePolicy policy, ta_StoreObject? obj, string key,
      byte[] keyHash, ta_StoreObjectVersion version, CancellationToken ct) {
      var now = DateTime.UtcNow;
      var isNew = obj is null;
      if (obj is null) {
         obj = new ta_StoreObject {
            cStoreObjectId = Ulid.NewUlid().ToString(), cStoreBucketId = bucket.cStoreBucketId, cStoreObjectKey = key,
            cStoreObjectKeyHash = keyHash, cStoreObjectVersionNext = 1, cStoreObjectState = 1, ustamp = now, datestamp = now
         };
         db.Objects.Add(obj);
         bucket.cStoreBucketObjects++;
      }
      else if (!string.Equals(obj.cStoreObjectKey, key, StringComparison.Ordinal)) {
         throw new InvalidOperationException("Object key hash collision.");
      }

      var number = obj.cStoreObjectVersionNext;
      obj.cStoreObjectVersionNext = number + 1;
      obj.cStoreObjectVersionCurrent = number;
      obj.ustamp = now;
      version.cStoreObjectId = obj.cStoreObjectId;
      version.cStoreObjectVersionId = number;
      version.datestamp = now;
      db.Versions.Add(version);

      bucket.cStoreBucketBytes += version.cStoreObjectVersionSize;
      bucket.ustamp = now;
      if (policy.cStorePolicyQuotaBytes is { } quota && version.cStoreObjectVersionSize > 0 && bucket.cStoreBucketBytes > quota)
         throw new ActionException("Bucket byte quota exceeded.", 409);
      if (isNew && policy.cStorePolicyQuotaObjects is { } quotaObjects && bucket.cStoreBucketObjects > quotaObjects)
         throw new ActionException("Bucket object quota exceeded.", 409);

      if (version.cStoreObjectBlobHash is { } hash) {
         await db.Blobs.Where(b => b.cStoreObjectBlobHash == hash && b.cStoreObjectBlobState == (int)StoreBlobState.Orphan)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.cStoreObjectBlobState, (int)StoreBlobState.Ready)
               .SetProperty(b => b.cStoreObjectBlobOrphanAt, (DateTime?)null), ct);
      }
      return Info(obj, version);
   }

   /// <inheritdoc />
   public async Task<StoreObjectInfo> DeleteAsync(string bucketName, string key, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);
      var keyHash = StoreNames.KeyHash(key);
      return await InBucketTransactionAsync(bucket.cStoreBucketId, async (b, p) => {
         RequireWritable(b);
         var obj = await LockObjectAsync(b.cStoreBucketId, keyHash, ct) ?? throw new ActionException("Object not found.", 404);
         var current = await db.Versions.SingleOrDefaultAsync(v => v.cStoreObjectId == obj.cStoreObjectId && v.cStoreObjectVersionId == obj.cStoreObjectVersionCurrent, ct);
         if (current is { cStoreObjectVersionMarker: true }) return Info(obj, current);
         var marker = new ta_StoreObjectVersion { cStoreObjectVersionMarker = true, cStoreObjectVersionMime = StoreNames.DefaultMime };
         return await AppendVersionAsync(b, p, obj, key, keyHash, marker, ct);
      }, ct);
   }

   /// <inheritdoc />
   public async Task<StoreObjectInfo> RestoreAsync(string bucketName, string key, int version, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);
      var keyHash = StoreNames.KeyHash(key);
      return await InBucketTransactionAsync(bucket.cStoreBucketId, async (b, p) => {
         RequireWritable(b);
         var obj = await LockObjectAsync(b.cStoreBucketId, keyHash, ct) ?? throw new ActionException("Object not found.", 404);
         var source = await db.Versions.SingleOrDefaultAsync(v => v.cStoreObjectId == obj.cStoreObjectId && v.cStoreObjectVersionId == version, ct)
            ?? throw new ActionException("Version not found.", 404);
         if (source.cStoreObjectVersionMarker) throw new ActionException("A delete marker cannot be restored.", 400);
         var copy = new ta_StoreObjectVersion {
            cStoreObjectBlobHash = source.cStoreObjectBlobHash, cStoreObjectVersionSize = source.cStoreObjectVersionSize,
            cStoreObjectVersionMime = source.cStoreObjectVersionMime, json_object = source.json_object
         };
         return await AppendVersionAsync(b, p, obj, key, keyHash, copy, ct);
      }, ct);
   }

   /// <inheritdoc />
   public async Task PurgeVersionAsync(string bucketName, string key, int version, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);
      var obj = await FindObjectAsync(bucket.cStoreBucketId, key, ct) ?? throw new ActionException("Object not found.", 404);
      if (!await PurgeVersionCoreAsync(bucket.cStoreBucketId, obj.cStoreObjectId, version, ct))
         throw new ActionException("Version not found.", 404);
   }

   /// <summary>
   /// Permanently deletes one version inside its own bucket transaction; also used by retention. Read-only
   /// buckets are not checked here: purging is an administrative operation. Returns <c>false</c> when the
   /// version does not exist.
   /// </summary>
   internal Task<bool> PurgeVersionCoreAsync(string bucketId, string objectId, int version, CancellationToken ct) =>
      InBucketTransactionAsync(bucketId, async (bucket, _) => {
         var obj = await db.Objects.FromSql($"SELECT * FROM dbo.ta_StoreObject WITH (UPDLOCK, HOLDLOCK) WHERE cStoreObjectId = {objectId}")
            .AsTracking().SingleOrDefaultAsync(ct);
         if (obj is null || obj.cStoreBucketId != bucketId) return false;
         var versions = await db.Versions.AsTracking().Where(v => v.cStoreObjectId == objectId)
            .OrderByDescending(v => v.cStoreObjectVersionId).ToListAsync(ct);
         var row = versions.FirstOrDefault(v => v.cStoreObjectVersionId == version);
         if (row is null) return false;

         var now = DateTime.UtcNow;
         db.Versions.Remove(row);
         versions.Remove(row);
         bucket.cStoreBucketBytes = Math.Max(0, bucket.cStoreBucketBytes - row.cStoreObjectVersionSize);
         bucket.ustamp = now;
         if (versions.Count == 0) {
            await db.SaveChangesAsync(ct);
            db.Objects.Remove(obj);
            bucket.cStoreBucketObjects = Math.Max(0, bucket.cStoreBucketObjects - 1);
         }
         else if (obj.cStoreObjectVersionCurrent == version) {
            var next = versions.FirstOrDefault(v => !v.cStoreObjectVersionMarker) ?? versions[0];
            obj.cStoreObjectVersionCurrent = next.cStoreObjectVersionId;
            obj.ustamp = now;
         }
         await db.SaveChangesAsync(ct);

         if (row.cStoreObjectBlobHash is { } hash) {
            // The last reference is gone: the blob waits for garbage collection after the grace period.
            await db.Blobs.Where(b => b.cStoreObjectBlobHash == hash && b.cStoreObjectBlobState == (int)StoreBlobState.Ready &&
                                      !db.Versions.Any(v => v.cStoreObjectBlobHash == hash))
               .ExecuteUpdateAsync(s => s.SetProperty(b => b.cStoreObjectBlobState, (int)StoreBlobState.Orphan)
                  .SetProperty(b => b.cStoreObjectBlobOrphanAt, (DateTime?)now), ct);
         }
         return true;
      }, ct);

   #endregion

   #region Read

   /// <inheritdoc />
   public async Task<StoreObjectRead> OpenAsync(string bucketName, string key, int? version = null, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, policy) = await RequireBucketAsync(bucketName, ct);
      var obj = await FindObjectAsync(bucket.cStoreBucketId, key, ct) ?? throw new ActionException("Object not found.", 404);
      var number = version ?? obj.cStoreObjectVersionCurrent ?? throw new ActionException("Object not found.", 404);
      var row = await db.Versions.SingleOrDefaultAsync(v => v.cStoreObjectId == obj.cStoreObjectId && v.cStoreObjectVersionId == number, ct)
         ?? throw new ActionException("Version not found.", 404);
      if (row.cStoreObjectVersionMarker || row.cStoreObjectBlobHash is null)
         throw new ActionException(version is null ? "Object is deleted." : "The version is a delete marker.", 404);
      var stream = await OpenBlobAsync(row.cStoreObjectBlobHash, row.cStoreObjectVersionSize, policy.cStorePolicyId, ct);
      return new StoreObjectRead(Info(obj, row), stream, StoreNames.ReadMetadata(row.json_object));
   }

   /// <summary>
   /// Opens the first ready copy, in the policy's volume order, whose file exists with the expected size. A
   /// copy whose file is gone or has a wrong size is marked missing and replication is signaled.
   /// </summary>
   private async Task<Stream> OpenBlobAsync(byte[] hash, long size, string policyId, CancellationToken ct) {
      var volumes = await (from c in db.Copies
                           join v in db.Volumes on c.cStoreVolumeId equals v.cStoreVolumeId
                           where c.cStoreObjectBlobHash == hash && c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready &&
                                 v.cStoreVolumeState != (int)StoreVolumeState.Offline
                           select v).ToListAsync(ct);
      var order = await db.PolicyVolumes.Where(p => p.cStorePolicyId == policyId)
         .ToDictionaryAsync(p => p.cStoreVolumeId, p => p.cStorePolicyVolumeOrder, ct);
      foreach (var volume in volumes.OrderBy(v => order.TryGetValue(v.cStoreVolumeId, out var o) ? o : int.MaxValue)) {
         Stream? stream;
         try { stream = runtime.Volume(volume).OpenRead(hash); }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // The volume is unreachable right now; it is not proof that the copy is gone.
            continue;
         }
         if (stream is not null && stream.Length == size) return stream;
         if (stream is not null) await stream.DisposeAsync();
         await db.Copies.Where(c => c.cStoreObjectBlobHash == hash && c.cStoreVolumeId == volume.cStoreVolumeId &&
                                    c.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.cStoreObjectBlobVolState, (int)StoreBlobCopyState.Missing), ct);
         runtime.SignalReplication();
      }
      throw new ActionException("No readable copy of the object content is available.", 503);
   }

   /// <inheritdoc />
   public async Task<StoreObjectInfo?> GetAsync(string bucketName, string key, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);
      var obj = await FindObjectAsync(bucket.cStoreBucketId, key, ct);
      if (obj?.cStoreObjectVersionCurrent is not { } current) return null;
      var row = await db.Versions.SingleAsync(v => v.cStoreObjectId == obj.cStoreObjectId && v.cStoreObjectVersionId == current, ct);
      return Info(obj, row);
   }

   /// <inheritdoc />
   public async Task<StoreObjectVersionInfo[]> VersionsAsync(string bucketName, string key, CancellationToken ct = default) {
      StoreNames.Key(key);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);
      var obj = await FindObjectAsync(bucket.cStoreBucketId, key, ct) ?? throw new ActionException("Object not found.", 404);
      var rows = await db.Versions.Where(v => v.cStoreObjectId == obj.cStoreObjectId)
         .OrderByDescending(v => v.cStoreObjectVersionId).ToListAsync(ct);
      return rows.Select(v => new StoreObjectVersionInfo(obj.cStoreObjectId, v.cStoreObjectVersionId,
         v.cStoreObjectVersionId == obj.cStoreObjectVersionCurrent, v.cStoreObjectVersionMarker, v.cStoreObjectVersionSize,
         v.cStoreObjectVersionMime, v.cStoreObjectBlobHash is null ? null : StoreNames.Hex(v.cStoreObjectBlobHash), v.datestamp,
         StoreNames.ReadMetadata(v.json_object))).ToArray();
   }

   /// <inheritdoc />
   public async Task<StoreObjectListing> ListAsync(string bucketName, string? prefix = null, string? delimiter = null,
      string? continuation = null, int limit = 1000, CancellationToken ct = default) {
      if (limit is < 1 or > 1000) throw new ActionException("Limit must be between 1 and 1000.", 400);
      prefix = StoreNames.Prefix(prefix);
      if (string.IsNullOrEmpty(delimiter)) delimiter = null;
      else if (delimiter.Length > 16 || delimiter.Any(char.IsControl)) throw new ActionException("Invalid delimiter.", 400);
      var cursor = StoreNames.DecodeContinuation(continuation);
      var (bucket, _) = await RequireBucketAsync(bucketName, ct);

      var objects = new List<StoreObjectInfo>();
      var prefixes = new List<string>();
      var exhausted = false;
      while (objects.Count + prefixes.Count < limit) {
         var query = from o in db.Objects
                     join v in db.Versions on o.cStoreObjectId equals v.cStoreObjectId
                     where o.cStoreBucketId == bucket.cStoreBucketId && v.cStoreObjectVersionId == o.cStoreObjectVersionCurrent &&
                           !v.cStoreObjectVersionMarker
                     select new { o, v };
         if (prefix.Length > 0) query = query.Where(r => r.o.cStoreObjectKey.StartsWith(prefix));
         if (cursor is not null) {
            var after = cursor;
            query = query.Where(r => string.Compare(r.o.cStoreObjectKey, after) > 0);
         }
         var requested = limit - objects.Count - prefixes.Count + 1;
         var batch = await query.OrderBy(r => r.o.cStoreObjectKey).Take(requested).ToListAsync(ct);
         if (batch.Count == 0) { exhausted = true; break; }

         var regrouped = false;
         foreach (var r in batch) {
            if (objects.Count + prefixes.Count == limit) break;
            var key = r.o.cStoreObjectKey;
            if (cursor is not null && string.CompareOrdinal(key, cursor) <= 0) continue;
            var at = delimiter is null ? -1 : key.IndexOf(delimiter, prefix.Length, StringComparison.Ordinal);
            if (at >= 0) {
               var common = key[..(at + delimiter!.Length)];
               prefixes.Add(common);
               // Skip every other key of this group: U+FFFF sorts after any UTF-16 code unit under the
               // binary collation of the key column.
               cursor = common + '￿';
               regrouped = true;
               continue;
            }
            objects.Add(Info(r.o, r.v));
            cursor = key;
         }
         if (!regrouped && batch.Count < requested) { exhausted = true; break; }
      }
      return new StoreObjectListing(prefix.Length == 0 ? null : prefix, delimiter, prefixes.ToArray(), objects.ToArray(),
         exhausted || cursor is null ? null : StoreNames.EncodeContinuation(cursor));
   }

   #endregion
}

/// <summary>Raised when content copied to a volume does not match the blob's size or CRC32: the source copy is damaged.</summary>
public sealed class StoreCopyMismatchException() : IOException("Copied content does not match the blob.");

