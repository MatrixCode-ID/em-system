using System.Data;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.ObjectStorage;

public sealed partial class ObjectStorageServices
{
   private static bool IsUniqueConflict(DbUpdateException ex) =>
      EmDbErrors.IsUniqueConflict(ex);

   /// <summary>
   /// Managed bucket name per policy ID, for every policy a managed bucket uses (or only <paramref name="policyId"/>).
   /// A policy is managed while a managed bucket uses it; the policy row itself carries no mark.
   /// </summary>
   private async Task<Dictionary<string, string>> ManagedPoliciesAsync(string? policyId = null) {
      var query = db.Buckets.Where(b => b.json_object != null);
      if (policyId is not null) query = query.Where(b => b.cStorePolicyId == policyId);
      var rows = await query.Select(b => new { b.cStorePolicyId, b.cStoreBucketName, b.json_object }).ToListAsync(AbortToken);
      var map = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var row in rows.Where(r => ManagedBucketJson.Owner(r.json_object) is not null))
         map.TryAdd(row.cStorePolicyId, row.cStoreBucketName);
      return map;
   }

   /// <summary>409 for a change that belongs to the application managing the bucket.</summary>
   private static ActionException ManagedConflict(string bucketName, string owner, string what) =>
      new($"Bucket '{bucketName}' is managed by {owner}; {what}", 409);

   #region Volumes

   /// <inheritdoc />
   [GetAction(claim: IObjectStorageServices.ManagerClaim)]
   public async Task<StoreVolumeInfo[]> GetMeta_StoreVolumes() {
      var rows = await db.Volumes.OrderBy(v => v.cStoreVolumeName).ToListAsync(AbortToken);
      var counters = await (from c in db.Copies
                            join b in db.Blobs on c.cStoreObjectBlobHash equals b.cStoreObjectBlobHash
                            group new { c.cStoreObjectBlobVolState, b.cStoreObjectBlobSize } by c.cStoreVolumeId into g
                            select new {
                               Id = g.Key,
                               Blobs = g.LongCount(x => x.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready),
                               Bytes = g.Sum(x => x.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Ready ? x.cStoreObjectBlobSize : 0),
                               Missing = g.LongCount(x => x.cStoreObjectBlobVolState == (int)StoreBlobCopyState.Missing)
                            }).ToDictionaryAsync(x => x.Id, AbortToken);
      var policies = await db.PolicyVolumes.GroupBy(p => p.cStoreVolumeId).Select(g => new { g.Key, Count = g.Count() })
         .ToDictionaryAsync(x => x.Key, x => x.Count, AbortToken);
      return rows.Select(v => {
         counters.TryGetValue(v.cStoreVolumeId, out var c);
         return new StoreVolumeInfo(v.cStoreVolumeId, v.cStoreVolumeName, (StoreVolumeState)v.cStoreVolumeState, (StoreVolumeType)v.cStoreVolumeType,
            v.cStoreVolumePath, v.cStoreVolumeNote, c?.Blobs ?? 0, c?.Bytes ?? 0, c?.Missing ?? 0, policies.GetValueOrDefault(v.cStoreVolumeId), Probe(v));
      }).ToArray();
   }

   private StoreVolumeValidation Probe(ta_StoreVolume row) {
      if (row.cStoreVolumeState == (int)StoreVolumeState.Offline)
         return new StoreVolumeValidation(false, null, "The volume is offline.", null, false);
      try { return runtime.Volume(row).Probe(); }
      catch (ActionException ex) { return new StoreVolumeValidation(false, null, ex.Message, null, false); }
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public Task<StoreVolumeValidation> PostGetMeta_StoreVolumeValidate(StoreVolumeSave draft) {
      ArgumentNullException.ThrowIfNull(draft);
      return Task.FromResult(runtime.Provider(draft.Type).Validate(draft.Path));
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task<StoreVolumeInfo> PostGetMeta_StoreVolumeSave(StoreVolumeSave request) {
      ArgumentNullException.ThrowIfNull(request);
      var name = StoreNames.DisplayName(request.Name, "Volume");
      if (!Enum.IsDefined(request.State)) throw new ActionException("Invalid volume state.", 400);
      var provider = runtime.Provider(request.Type);
      if (provider is Volumes.LocalFolderVolumeProvider local) local.Resolve(request.Path);
      var path = request.Path.Trim();
      if (request.State != StoreVolumeState.Offline && provider.Validate(path) is { IsValid: false } probe)
         throw new ActionException(probe.Message + " Save the volume as Offline to keep it without a reachable folder.", 400);

      var now = DateTime.UtcNow;
      string id;
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
         ta_StoreVolume row;
         if (string.IsNullOrEmpty(request.Id)) {
            row = new ta_StoreVolume { cStoreVolumeId = Ulid.NewUlid().ToString(), datestamp = now, cStoreVolumeType = (int)request.Type, cStoreVolumePath = path };
            db.Volumes.Add(row);
         }
         else {
            row = await db.Volumes.AsTracking().SingleOrDefaultAsync(v => v.cStoreVolumeId == request.Id, AbortToken)
               ?? throw new ActionException("Volume not found.", 404);
            if ((row.cStoreVolumePath != path || row.cStoreVolumeType != (int)request.Type) &&
                await db.Copies.AnyAsync(c => c.cStoreVolumeId == row.cStoreVolumeId, AbortToken))
               throw new ActionException("The volume holds copies, so its path cannot change. Data is never moved automatically.", 409);
            row.cStoreVolumePath = path;
            row.cStoreVolumeType = (int)request.Type;
         }
         row.cStoreVolumeName = name;
         row.cStoreVolumeState = (int)request.State;
         row.cStoreVolumeNote = StoreNames.Note(request.Note);
         row.ustamp = now;
         await db.SaveChangesAsync(AbortToken);
         await tx.CommitAsync(AbortToken);
         id = row.cStoreVolumeId;
      }
      catch (DbUpdateException ex) when (IsUniqueConflict(ex)) {
         throw new ActionException("A volume with this name already exists.", 409);
      }
      finally {
         db.ChangeTracker.Clear();
         runtime.InvalidateVolumes();
      }
      return (await GetMeta_StoreVolumes()).Single(v => v.Id == id);
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task PostMeta_StoreVolumeDelete(string volumeId) {
      await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      if (!await db.Volumes.AnyAsync(v => v.cStoreVolumeId == volumeId, AbortToken)) throw new ActionException("Volume not found.", 404);
      if (await db.Copies.AnyAsync(c => c.cStoreVolumeId == volumeId, AbortToken))
         throw new ActionException("The volume still holds blob copies.", 409);
      if (await db.PolicyVolumes.AnyAsync(p => p.cStoreVolumeId == volumeId, AbortToken))
         throw new ActionException("The volume is used by a policy. Remove it from every policy first.", 409);
      await db.Volumes.Where(v => v.cStoreVolumeId == volumeId).ExecuteDeleteAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      runtime.InvalidateVolumes();
      await AuditSystemAsync("System.ObjectStorage", volumeId, "volume.deleted", $"Deleted volume {volumeId}", "volume");
   }

   #endregion

   #region Policies

   /// <inheritdoc />
   [GetAction(claim: IObjectStorageServices.ManagerClaim)]
   public async Task<StorePolicyInfo[]> GetMeta_StorePolicies() {
      var rows = await db.Policies.OrderBy(p => p.cStorePolicyName).ToListAsync(AbortToken);
      var volumes = await db.PolicyVolumes.OrderBy(p => p.cStorePolicyVolumeOrder).ToListAsync(AbortToken);
      var buckets = await db.Buckets.GroupBy(b => b.cStorePolicyId).Select(g => new { g.Key, Count = g.Count() })
         .ToDictionaryAsync(x => x.Key, x => x.Count, AbortToken);
      var managed = await ManagedPoliciesAsync();
      return rows.Select(p => new StorePolicyInfo(p.cStorePolicyId, p.cStorePolicyName, (StorePolicyState)p.cStorePolicyState, p.cStorePolicyNote,
         p.cStorePolicyReplicaMin, p.cStorePolicyReplicaTarget, p.cStorePolicyKeepVersions, p.cStorePolicyKeepDays, p.cStorePolicyQuotaBytes,
         p.cStorePolicyQuotaObjects, p.cStorePolicyMaxObjectBytes, (StoreAccess)p.cStorePolicyAccess,
         volumes.Where(v => v.cStorePolicyId == p.cStorePolicyId).Select(v => v.cStoreVolumeId).ToArray(),
         buckets.GetValueOrDefault(p.cStorePolicyId), managed.GetValueOrDefault(p.cStorePolicyId))).ToArray();
   }

   /// <summary>Checks the rules of a policy draft; throws 400 for the first rule it breaks.</summary>
   internal static void ValidatePolicy(StorePolicySave request) {
      if (!Enum.IsDefined(request.State)) throw new ActionException("Invalid policy state.", 400);
      if (!Enum.IsDefined(request.Access)) throw new ActionException("Invalid access.", 400);
      if (request.ReplicaMin < 1) throw new ActionException("Minimum replicas must be at least 1.", 400);
      if (request.ReplicaTarget < request.ReplicaMin) throw new ActionException("Target replicas must not be less than minimum replicas.", 400);
      if (request.ReplicaTarget > 16) throw new ActionException("Target replicas must not exceed 16.", 400);
      if (request.KeepVersions is < 0 or > 100000) throw new ActionException("Kept versions must be between 0 and 100000.", 400);
      if (request.KeepDays is < 1 or > 36500) throw new ActionException("Kept days must be between 1 and 36500.", 400);
      if (request.QuotaBytes is < 0) throw new ActionException("Byte quota must not be negative.", 400);
      if (request.QuotaObjects is < 0) throw new ActionException("Object quota must not be negative.", 400);
      if (request.MaxObjectBytes is < 1) throw new ActionException("Maximum object size must be at least 1 byte.", 400);
      var ids = request.VolumeIds ?? [];
      if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ActionException("A volume is listed more than once.", 400);
      if (ids.Length < request.ReplicaTarget) throw new ActionException("The policy needs at least as many volumes as target replicas.", 400);
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task<StorePolicyInfo> PostGetMeta_StorePolicySave(StorePolicySave request) {
      ArgumentNullException.ThrowIfNull(request);
      request.VolumeIds ??= [];
      var name = StoreNames.DisplayName(request.Name, "Policy");
      ValidatePolicy(request);
      var now = DateTime.UtcNow;
      string id;
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
         var known = await db.Volumes.Where(v => request.VolumeIds.Contains(v.cStoreVolumeId)).CountAsync(AbortToken);
         if (known != request.VolumeIds.Length) throw new ActionException("A listed volume does not exist.", 400);
         ta_StorePolicy row;
         if (string.IsNullOrEmpty(request.Id)) {
            row = new ta_StorePolicy { cStorePolicyId = Ulid.NewUlid().ToString(), datestamp = now };
            db.Policies.Add(row);
         }
         else {
            row = await db.Policies.AsTracking().SingleOrDefaultAsync(p => p.cStorePolicyId == request.Id, AbortToken)
               ?? throw new ActionException("Policy not found.", 404);
            if (row.cStorePolicyAccess != (int)request.Access &&
                (await ManagedPoliciesAsync(row.cStorePolicyId)).TryGetValue(row.cStorePolicyId, out var managedBucket))
               throw new ActionException($"Policy '{row.cStorePolicyName}' belongs to managed bucket '{managedBucket}'; its access is set by the " +
                  "application that manages the bucket and cannot change here. Every other field can.", 409);
            await db.PolicyVolumes.Where(p => p.cStorePolicyId == row.cStorePolicyId).ExecuteDeleteAsync(AbortToken);
         }
         row.cStorePolicyName = name;
         row.cStorePolicyState = (int)request.State;
         row.cStorePolicyNote = StoreNames.Note(request.Note);
         row.cStorePolicyReplicaMin = request.ReplicaMin;
         row.cStorePolicyReplicaTarget = request.ReplicaTarget;
         row.cStorePolicyKeepVersions = request.KeepVersions;
         row.cStorePolicyKeepDays = request.KeepDays;
         row.cStorePolicyQuotaBytes = request.QuotaBytes;
         row.cStorePolicyQuotaObjects = request.QuotaObjects;
         row.cStorePolicyMaxObjectBytes = request.MaxObjectBytes;
         row.cStorePolicyAccess = (int)request.Access;
         row.ustamp = now;
         for (var i = 0; i < request.VolumeIds.Length; i++)
            db.PolicyVolumes.Add(new ta_StorePolicyVolume { cStorePolicyId = row.cStorePolicyId, cStoreVolumeId = request.VolumeIds[i], cStorePolicyVolumeOrder = i + 1 });
         await db.SaveChangesAsync(AbortToken);
         await tx.CommitAsync(AbortToken);
         id = row.cStorePolicyId;
      }
      catch (DbUpdateException ex) when (IsUniqueConflict(ex)) {
         throw new ActionException("A policy with this name already exists.", 409);
      }
      finally { db.ChangeTracker.Clear(); }
      // A higher replica target applies to existing blobs through background replication.
      runtime.SignalReplication();
      return (await GetMeta_StorePolicies()).Single(p => p.Id == id);
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task PostMeta_StorePolicyDelete(string policyId) {
      await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      if (!await db.Policies.AnyAsync(p => p.cStorePolicyId == policyId, AbortToken)) throw new ActionException("Policy not found.", 404);
      if ((await ManagedPoliciesAsync(policyId)).TryGetValue(policyId, out var managedBucket))
         throw new ActionException($"The policy belongs to managed bucket '{managedBucket}' and cannot be deleted.", 409);
      if (await db.Buckets.AnyAsync(b => b.cStorePolicyId == policyId, AbortToken)) throw new ActionException("The policy is used by a bucket.", 409);
      await db.PolicyVolumes.Where(p => p.cStorePolicyId == policyId).ExecuteDeleteAsync(AbortToken);
      await db.Policies.Where(p => p.cStorePolicyId == policyId).ExecuteDeleteAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      await AuditSystemAsync("System.ObjectStorage", policyId, "policy.deleted", $"Deleted policy {policyId}", "policy");
   }

   #endregion

   #region Buckets

   /// <inheritdoc />
   [GetAction(claim: IObjectStorageServices.ManagerClaim)]
   public async Task<StoreBucketInfo[]> GetMeta_StoreBuckets() =>
      (await (from b in db.Buckets
              join p in db.Policies on b.cStorePolicyId equals p.cStorePolicyId
              orderby b.cStoreBucketName
              select new { b, p }).ToListAsync(AbortToken)).Select(r => BucketInfo(r.b, r.p)).ToArray();

   private StoreBucketInfo BucketInfo(ta_StoreBucket b, ta_StorePolicy p) {
      var (owner, description) = ManagedBucketJson.Read(b.json_object);
      return new(b.cStoreBucketId, b.cStoreBucketName, p.cStorePolicyId, p.cStorePolicyName, (StoreBucketState)b.cStoreBucketState, b.cStoreBucketNote,
         b.cStoreBucketBytes, b.cStoreBucketObjects, p.cStorePolicyQuotaBytes, p.cStorePolicyQuotaObjects, p.cStorePolicyMaxObjectBytes,
         (StoreAccess)p.cStorePolicyAccess, b.datestamp, owner, description, owner is not null && runtime.ManagedBuckets.IsDeclared(b.cStoreBucketName));
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task<StoreBucketInfo> PostGetMeta_StoreBucketSave(StoreBucketSave request) {
      ArgumentNullException.ThrowIfNull(request);
      if (!Enum.IsDefined(request.State)) throw new ActionException("Invalid bucket state.", 400);
      var now = DateTime.UtcNow;
      string id;
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
         var policy = await db.Policies.SingleOrDefaultAsync(p => p.cStorePolicyId == request.PolicyId, AbortToken)
            ?? throw new ActionException("Policy not found.", 400);
         ta_StoreBucket row;
         if (string.IsNullOrEmpty(request.Id)) {
            if (policy.cStorePolicyState != (int)StorePolicyState.Active) throw new ActionException("The policy is disabled.", 400);
            await RequireUnmanagedPolicyAsync(policy);
            row = new ta_StoreBucket { cStoreBucketId = Ulid.NewUlid().ToString(), cStoreBucketName = StoreNames.Bucket(request.Name), datestamp = now };
            db.Buckets.Add(row);
         }
         else {
            row = await db.Buckets.LockRows(EmRowLock.Row, (nameof(ta_StoreBucket.cStoreBucketId), request.Id))
               .AsTracking().SingleOrDefaultAsync(AbortToken) ?? throw new ActionException("Bucket not found.", 404);
            if (!string.IsNullOrEmpty(request.Name) && request.Name != row.cStoreBucketName)
               throw new ActionException("A bucket name cannot change.", 400);
            if (row.cStorePolicyId != policy.cStorePolicyId) {
               if (ManagedBucketJson.Owner(row.json_object) is { } owner)
                  throw ManagedConflict(row.cStoreBucketName, owner, "it keeps its own policy. Change volumes, replicas, retention and quotas in that policy instead.");
               if (policy.cStorePolicyState != (int)StorePolicyState.Active) throw new ActionException("The policy is disabled.", 400);
               await RequireUnmanagedPolicyAsync(policy);
            }
         }
         row.cStorePolicyId = policy.cStorePolicyId;
         row.cStoreBucketState = (int)request.State;
         row.cStoreBucketNote = StoreNames.Note(request.Note);
         row.ustamp = now;
         await db.SaveChangesAsync(AbortToken);
         await tx.CommitAsync(AbortToken);
         id = row.cStoreBucketId;
      }
      catch (DbUpdateException ex) when (IsUniqueConflict(ex)) {
         throw new ActionException("A bucket with this name already exists.", 409);
      }
      finally { db.ChangeTracker.Clear(); }
      runtime.SignalReplication();
      return (await GetMeta_StoreBuckets()).Single(b => b.Id == id);
   }

   private async Task RequireUnmanagedPolicyAsync(ta_StorePolicy policy) {
      if ((await ManagedPoliciesAsync(policy.cStorePolicyId)).TryGetValue(policy.cStorePolicyId, out var managedBucket))
         throw new ActionException($"Policy '{policy.cStorePolicyName}' belongs to managed bucket '{managedBucket}' and cannot be used by another bucket.", 409);
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task PostMeta_StoreBucketDelete(string bucketId) {
      await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var bucket = await db.Buckets.LockRows(EmRowLock.Row, (nameof(ta_StoreBucket.cStoreBucketId), bucketId))
         .SingleOrDefaultAsync(AbortToken) ?? throw new ActionException("Bucket not found.", 404);
      if (ManagedBucketJson.Owner(bucket.json_object) is { } owner)
         throw ManagedConflict(bucket.cStoreBucketName, owner, runtime.ManagedBuckets.IsDeclared(bucket.cStoreBucketName)
            ? "it cannot be deleted while the application declares it."
            : "the application no longer declares it. Release it first; it can be deleted as an ordinary bucket afterwards.");
      if (await db.Objects.AnyAsync(o => o.cStoreBucketId == bucket.cStoreBucketId, AbortToken))
         throw new ActionException("The bucket still holds objects, including deleted objects with history. Purge them first.", 409);
      await db.Buckets.Where(b => b.cStoreBucketId == bucketId).ExecuteDeleteAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      await AuditSystemAsync("System.ObjectStorage", bucketId, "bucket.deleted", $"Deleted bucket {bucket.cStoreBucketName}", "bucket");
   }

   /// <inheritdoc />
   [PostAction(claim: IObjectStorageServices.SettingsClaim)]
   public async Task PostMeta_StoreBucketRelease(string bucketId) {
      var (name, owner) = await ReleaseBucketCoreAsync(bucketId);
      await AuditSystemAsync("System.ObjectStorage", name, "bucket.released", $"Released managed bucket {name} of {owner}", "bucket", new { owner });
   }

   /// <summary>Body of <see cref="PostMeta_StoreBucketRelease"/> without the audit entry; returns the bucket name and its former owner.</summary>
   internal async Task<(string Name, string Owner)> ReleaseBucketCoreAsync(string bucketId) {
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
         var bucket = await db.Buckets.LockRows(EmRowLock.Row, (nameof(ta_StoreBucket.cStoreBucketId), bucketId))
            .AsTracking().SingleOrDefaultAsync(AbortToken) ?? throw new ActionException("Bucket not found.", 404);
         var (owner, description) = ManagedBucketJson.Read(bucket.json_object);
         if (owner is null) throw new ActionException($"Bucket '{bucket.cStoreBucketName}' is not a managed bucket.", 409);
         if (runtime.ManagedBuckets.IsDeclared(bucket.cStoreBucketName))
            throw ManagedConflict(bucket.cStoreBucketName, owner, "it is still declared by the running application and cannot be released.");
         // The description stays as a record of what the contents are.
         bucket.json_object = ManagedBucketJson.Write(bucket.json_object, null, description);
         bucket.ustamp = DateTime.UtcNow;
         await db.SaveChangesAsync(AbortToken);
         await tx.CommitAsync(AbortToken);
         return (bucket.cStoreBucketName, owner);
      }
      finally { db.ChangeTracker.Clear(); }
   }

   #endregion
}
