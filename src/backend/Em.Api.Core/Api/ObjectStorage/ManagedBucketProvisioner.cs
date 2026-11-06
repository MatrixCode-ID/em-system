using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// Applies the managed bucket declarations at startup, one serializable transaction per bucket: creates a missing
/// bucket with its own policy on the default volume, adopts an ordinary bucket of the same name (copying a policy
/// that other buckets share), keeps owner and description equal to the declaration, and enforces the declared
/// access. Other policy values are never overwritten: they belong to administrators once the policy exists.
/// </summary>
internal sealed class ManagedBucketProvisioner(ObjectStorageDbContext db, ObjectStorageRuntime runtime, ILogger logger, IAuditWriter? audit = null)
{
   /// <summary><c>ta_Meta</c> key holding the ID of the volume created for managed buckets.</summary>
   internal const string DefaultVolumeKey = "ObjectStore.DefaultVolume";

   /// <summary>Name of the volume created for managed buckets (a suffix is added when it is taken).</summary>
   internal const string DefaultVolumeName = "managed-default";

   private readonly List<AuditEntryDraft> _audits = [];

   /// <summary>Applies every declaration; a failure names the bucket and its cause.</summary>
   public async Task ProvisionAsync(CancellationToken ct) {
      var replicate = false;
      try {
         foreach (var declaration in runtime.ManagedBuckets.Items) {
            try {
               replicate |= await ApplyAsync(declaration, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
               throw new InvalidOperationException($"Managed bucket '{declaration.Name}' of {declaration.Owner} could not be provisioned: {ex.Message}", ex);
            }
            finally {
               db.ChangeTracker.Clear();
            }
         }
      }
      finally {
         // Buckets applied before a failure are committed, so their entries are written either way.
         if (replicate) runtime.SignalReplication();
         await WriteAuditsAsync(ct);
      }
   }

   // Returns true when a policy was created, so background replication looks at it.
   private async Task<bool> ApplyAsync(ManagedBucketDeclaration declaration, CancellationToken ct) {
      var now = DateTime.UtcNow;
      var createdPolicy = false;
      await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
      var bucket = await db.Buckets.LockRows(EmRowLock.Row, (nameof(ta_StoreBucket.cStoreBucketName), declaration.Name))
         .AsTracking().SingleOrDefaultAsync(ct);
      ta_StorePolicy policy;
      if (bucket is null) {
         var volumeId = await EnsureDefaultVolumeAsync(now, ct);
         policy = await AddPolicyAsync(declaration, volumeId, now, ct);
         bucket = new ta_StoreBucket {
            cStoreBucketId = Ulid.NewUlid().ToString(), cStoreBucketName = declaration.Name, cStorePolicyId = policy.cStorePolicyId,
            cStoreBucketState = (int)StoreBucketState.Active, json_object = ManagedBucketJson.Write(null, declaration.Owner, declaration.Description),
            ustamp = now, datestamp = now
         };
         db.Buckets.Add(bucket);
         createdPolicy = true;
         logger.LogInformation("Object storage created managed bucket {Bucket} of {Owner}.", declaration.Name, declaration.Owner);
         Audit(declaration.Name, "bucket.provisioned", $"Created managed bucket {declaration.Name} of {declaration.Owner}");
      }
      else {
         policy = await db.Policies.AsTracking().SingleAsync(p => p.cStorePolicyId == bucket.cStorePolicyId, ct);
         var (owner, description) = ManagedBucketJson.Read(bucket.json_object);
         if (owner is null) {
            if (await db.Buckets.AnyAsync(b => b.cStorePolicyId == policy.cStorePolicyId && b.cStoreBucketId != bucket.cStoreBucketId, ct)) {
               // The policy is shared: the managed bucket gets its own copy so locking it does not lock other buckets.
               policy = await CopyPolicyAsync(policy, declaration, now, ct);
               bucket.cStorePolicyId = policy.cStorePolicyId;
               createdPolicy = true;
            }
            logger.LogInformation("Object storage adopted bucket {Bucket} as a managed bucket of {Owner}; its contents stay.", declaration.Name, declaration.Owner);
            Audit(declaration.Name, "bucket.adopted", $"Adopted bucket {declaration.Name} as a managed bucket of {declaration.Owner}",
               new { policy = policy.cStorePolicyName, copiedPolicy = createdPolicy });
         }
         else if (owner != declaration.Owner) {
            logger.LogInformation("Owner of managed bucket {Bucket} changed from {Old} to {New}.", declaration.Name, owner, declaration.Owner);
         }
         if (owner != declaration.Owner || description != declaration.Description) {
            bucket.json_object = ManagedBucketJson.Write(bucket.json_object, declaration.Owner, declaration.Description);
            bucket.ustamp = now;
         }
      }

      if (policy.cStorePolicyAccess != (int)declaration.Defaults.Access) {
         var old = (StoreAccess)policy.cStorePolicyAccess;
         policy.cStorePolicyAccess = (int)declaration.Defaults.Access;
         policy.ustamp = now;
         logger.LogWarning("Access of managed bucket {Bucket} was {Old}; object storage set it back to {New} as its application declares.",
            declaration.Name, old, declaration.Defaults.Access);
         Audit(declaration.Name, "bucket.access.enforced", $"Set access of managed bucket {declaration.Name} from {old} to {declaration.Defaults.Access}",
            new { from = old.ToString(), to = declaration.Defaults.Access.ToString() });
      }
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return createdPolicy;
   }

   private async Task<ta_StorePolicy> AddPolicyAsync(ManagedBucketDeclaration declaration, string volumeId, DateTime now, CancellationToken ct) {
      var draft = declaration.PolicyDraft(await UniquePolicyNameAsync(declaration.Name, ct), volumeId);
      var policy = new ta_StorePolicy {
         cStorePolicyId = Ulid.NewUlid().ToString(), cStorePolicyName = draft.Name, cStorePolicyState = (int)draft.State,
         cStorePolicyNote = StoreNames.Note(draft.Note), cStorePolicyReplicaMin = draft.ReplicaMin, cStorePolicyReplicaTarget = draft.ReplicaTarget,
         cStorePolicyKeepVersions = draft.KeepVersions, cStorePolicyKeepDays = draft.KeepDays, cStorePolicyQuotaBytes = draft.QuotaBytes,
         cStorePolicyQuotaObjects = draft.QuotaObjects, cStorePolicyMaxObjectBytes = draft.MaxObjectBytes, cStorePolicyAccess = (int)draft.Access,
         ustamp = now, datestamp = now
      };
      db.Policies.Add(policy);
      db.PolicyVolumes.Add(new ta_StorePolicyVolume { cStorePolicyId = policy.cStorePolicyId, cStoreVolumeId = volumeId, cStorePolicyVolumeOrder = 1 });
      return policy;
   }

   private async Task<ta_StorePolicy> CopyPolicyAsync(ta_StorePolicy source, ManagedBucketDeclaration declaration, DateTime now, CancellationToken ct) {
      var copy = new ta_StorePolicy {
         cStorePolicyId = Ulid.NewUlid().ToString(), cStorePolicyName = await UniquePolicyNameAsync(declaration.Name, ct),
         cStorePolicyState = (int)StorePolicyState.Active, cStorePolicyNote = $"Copied from policy '{source.cStorePolicyName}' for managed bucket '{declaration.Name}'.",
         cStorePolicyReplicaMin = source.cStorePolicyReplicaMin, cStorePolicyReplicaTarget = source.cStorePolicyReplicaTarget,
         cStorePolicyKeepVersions = source.cStorePolicyKeepVersions, cStorePolicyKeepDays = source.cStorePolicyKeepDays,
         cStorePolicyQuotaBytes = source.cStorePolicyQuotaBytes, cStorePolicyQuotaObjects = source.cStorePolicyQuotaObjects,
         cStorePolicyMaxObjectBytes = source.cStorePolicyMaxObjectBytes, cStorePolicyAccess = source.cStorePolicyAccess,
         ustamp = now, datestamp = now
      };
      db.Policies.Add(copy);
      var volumes = await db.PolicyVolumes.Where(v => v.cStorePolicyId == source.cStorePolicyId).OrderBy(v => v.cStorePolicyVolumeOrder).ToListAsync(ct);
      foreach (var volume in volumes)
         db.PolicyVolumes.Add(new ta_StorePolicyVolume { cStorePolicyId = copy.cStorePolicyId, cStoreVolumeId = volume.cStoreVolumeId, cStorePolicyVolumeOrder = volume.cStorePolicyVolumeOrder });
      return copy;
   }

   private async Task<string> UniquePolicyNameAsync(string bucketName, CancellationToken ct) {
      var baseName = $"{bucketName} (managed)";
      for (var i = 1; ; i++) {
         var candidate = i == 1 ? baseName : $"{baseName}-{i}";
         if (!await db.Policies.AnyAsync(p => p.cStorePolicyName == candidate, ct)) return candidate;
      }
   }

   /// <summary>
   /// ID of the volume for managed buckets, created on <see cref="ObjectStorageOptions.DefaultVolumePath"/> when the
   /// remembered one does not exist (never created, or deleted by an administrator).
   /// </summary>
   private async Task<string> EnsureDefaultVolumeAsync(DateTime now, CancellationToken ct) {
      var meta = await db.Meta.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == DefaultVolumeKey, ct);
      if (meta is not null && await db.Volumes.AnyAsync(v => v.cStoreVolumeId == meta.cMetaValue, ct)) return meta.cMetaValue;

      var path = runtime.Options.DefaultVolumePath.Trim();
      var probe = runtime.Provider(StoreVolumeType.LocalFolder).Validate(path);
      if (!probe.IsValid)
         throw new InvalidOperationException($"The default volume folder '{path}' cannot be used: {probe.Message} " +
            "Set ObjectStorageOptions.DefaultVolumePath through AddObjectStorage(o => o.DefaultVolumePath = \"...\") to a writable folder.");

      var name = DefaultVolumeName;
      for (var i = 2; await db.Volumes.AnyAsync(v => v.cStoreVolumeName == name, ct); i++) name = $"{DefaultVolumeName}-{i}";
      var volume = new ta_StoreVolume {
         cStoreVolumeId = Ulid.NewUlid().ToString(), cStoreVolumeName = name, cStoreVolumeState = (int)StoreVolumeState.Active,
         cStoreVolumeType = (int)StoreVolumeType.LocalFolder, cStoreVolumePath = path,
         cStoreVolumeNote = "Created by object storage for managed buckets (ObjectStorageOptions.DefaultVolumePath).", ustamp = now, datestamp = now
      };
      db.Volumes.Add(volume);
      if (meta is null)
         db.Meta.Add(new ta_Meta { cMetaKey = DefaultVolumeKey, cMetaValue = volume.cStoreVolumeId, cMetaDescription = "Object storage volume for managed buckets", ustamp = now });
      else {
         meta.cMetaValue = volume.cStoreVolumeId;
         meta.ustamp = now;
      }
      runtime.InvalidateVolumes();
      logger.LogInformation("Object storage created volume {Volume} at {Path} for managed buckets.", name, probe.ResolvedPath);
      return volume.cStoreVolumeId;
   }

   private void Audit(string bucket, string kind, string summary, object? detail = null) =>
      _audits.Add(new AuditEntryDraft {
         Group = SystemAuditCatalog.ObjectStorage, Key = bucket, Kind = kind, TargetType = "bucket", Summary = summary, Detail = detail,
         ActorType = AuditActorTypes.System
      });

   // Written after every bucket is applied: an audit store that is not ready must not stop object storage.
   private async Task WriteAuditsAsync(CancellationToken ct) {
      if (audit is null) return;
      foreach (var entry in _audits) {
         try { await audit.WriteNowAsync(entry, ct); }
         catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Object storage could not write audit entry {Kind} of bucket {Bucket}.", entry.Kind, entry.Key);
         }
      }
   }
}
