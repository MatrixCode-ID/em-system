using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// Startup checks and background maintenance of object storage. At startup: verifies the schema, takes the
/// instance lease in <c>ta_Meta</c> (a warning, not a guarantee, when another instance looks alive), applies
/// the managed bucket declarations (<see cref="ManagedBucketProvisioner"/>) and cleans the staging folders.
/// Afterwards: replication when an upload signals it and at least hourly, and version retention every six hours. Garbage collection only runs on request (review, then run).
/// </summary>
internal sealed class ObjectStorageBackground(IServiceProvider services, ObjectStorageRuntime runtime, ILogger<ObjectStorageBackground> logger)
   : IHostedService, IDisposable
{
   internal const string LeaseKey = "ObjectStore.Lease";
   private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(10);
   private static readonly TimeSpan ReplicationInterval = TimeSpan.FromHours(1);
   private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(6);
   private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
   private readonly CancellationTokenSource _stop = new();
   private Task? _loop;

   public async Task StartAsync(CancellationToken ct) {
      using (var scope = services.CreateScope()) {
         var db = scope.ServiceProvider.GetRequiredService<ObjectStorageDbContext>();
         try {
            await EmSqlDialects.For(db).VerifySchemaAsync(db, new EmSchemaRequirement {
               Tables = ["ta_StoreVolume", "ta_StorePolicy", "ta_StorePolicyVolume", "ta_StoreBucket", "ta_StoreObject",
                  "ta_StoreObjectVersion", "ta_StoreObjectBlob", "ta_StoreObjectBlobVol"]
            }, "Object storage tables are missing.", ct);
            var version = await db.Meta.Where(m => m.cMetaKey == "ObjectStoreSchemaVersion").Select(m => m.cMetaValue).SingleOrDefaultAsync(ct);
            if (version != "1") throw new InvalidOperationException("Object storage schema version 1 is required.");
            await db.Volumes.OrderBy(v => v.cStoreVolumeId).FirstOrDefaultAsync(ct);
            await db.Objects.OrderBy(o => o.cStoreObjectId).FirstOrDefaultAsync(ct);
            await db.Copies.OrderBy(c => c.cStoreVolumeId).FirstOrDefaultAsync(ct);
         }
         catch (Exception ex) {
            throw new InvalidOperationException($"Object storage schema is unavailable. Run {EmSqlDialects.TryFor(db)?.ScriptFolder}/tables/060-objectstore.sql and " +
               "views/vi_Store*.sql on the core database, or remove AddObjectStorage() from the host.", ex);
         }
         await RenewLeaseAsync(db, true, ct);
         // Managed buckets exist before any module request needs them; a bucket that cannot be provisioned stops startup.
         await new ManagedBucketProvisioner(db, runtime, logger, scope.ServiceProvider.GetService<IAuditWriter>()).ProvisionAsync(ct);
         var cleaned = await scope.ServiceProvider.GetRequiredService<ObjectStorageMaintenance>().CleanStagingAsync(ct);
         if (cleaned > 0) logger.LogInformation("Object storage removed {Count} interrupted uploads from staging.", cleaned);
      }
      _loop = Task.Run(() => LoopAsync(_stop.Token), CancellationToken.None);
   }

   public async Task StopAsync(CancellationToken ct) {
      await _stop.CancelAsync();
      if (_loop is not null) await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, ct));
   }

   public void Dispose() => _stop.Dispose();

   private async Task LoopAsync(CancellationToken ct) {
      var nextReplication = DateTime.UtcNow.AddMinutes(1);
      var nextRetention = DateTime.UtcNow.AddMinutes(10);
      while (!ct.IsCancellationRequested) {
         try {
            var now = DateTime.UtcNow;
            var wait = (nextReplication < nextRetention ? nextReplication : nextRetention) - now;
            if (wait > TimeSpan.FromMinutes(5)) wait = TimeSpan.FromMinutes(5);
            var signaled = wait > TimeSpan.Zero && await runtime.WaitSignalAsync(wait, ct);
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObjectStorageDbContext>();
            await RenewLeaseAsync(db, false, ct);
            var maintenance = scope.ServiceProvider.GetRequiredService<ObjectStorageMaintenance>();
            if (signaled) await Task.Delay(TimeSpan.FromSeconds(5), ct); // let a burst of uploads finish first
            if (signaled || DateTime.UtcNow >= nextReplication) {
               runtime.ReportMaintenance(await maintenance.ReplicateAsync(false, null, ct));
               nextReplication = DateTime.UtcNow + ReplicationInterval;
            }
            if (DateTime.UtcNow >= nextRetention) {
               runtime.ReportMaintenance(await maintenance.RetentionAsync(null, ct));
               nextRetention = DateTime.UtcNow + RetentionInterval;
            }
         }
         catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return;
         }
         catch (Exception ex) {
            logger.LogError(ex, "Object storage background maintenance failed.");
            runtime.ReportMaintenance($"Background maintenance failed: {ex.Message}");
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) { return; }
         }
      }
   }

   private async Task RenewLeaseAsync(ObjectStorageDbContext db, bool starting, CancellationToken ct) {
      var now = DateTime.UtcNow;
      var row = await db.Meta.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == LeaseKey, ct);
      if (row is null) {
         db.Meta.Add(new ta_Meta { cMetaKey = LeaseKey, cMetaValue = _owner, cMetaDescription = "Object storage instance lease", ustamp = now });
      }
      else {
         if (row.cMetaValue != _owner && now - row.ustamp < LeaseTimeout) {
            logger.LogWarning(starting
                  ? "Object storage lease is held by another API instance ({Owner}) renewed {Age} ago. Running two instances on the same database and volumes can corrupt blob locking and garbage collection."
                  : "Another API instance ({Owner}) renewed the object storage lease {Age} ago. Only one instance may serve object storage.",
               row.cMetaValue, now - row.ustamp);
         }
         row.cMetaValue = _owner;
         row.ustamp = now;
      }
      await db.SaveChangesAsync(ct);
      db.ChangeTracker.Clear();
   }
}
