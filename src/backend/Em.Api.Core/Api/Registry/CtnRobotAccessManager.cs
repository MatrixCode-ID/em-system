using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Registry;

internal sealed class CtnRobotAccessManager(CtnContext db, CtnBlobStore store,
   DbContextOptions<CtnContext> options) : IRobotAccessManager
{
   public string Id => "Container";
   private List<string> _deletedUploads = [];

   public async Task<RobotAccessManagerInfo> DescribeAsync(CancellationToken ct) => new() {
      Id = Id, Name = "Container",
      Description = "Read allows pull. Write allows push and pull. Resources are container roots.",
      Options = [new() { Code = "R", Label = "Read" }, new() { Code = "W", Label = "Write" }],
      Resources = await db.Roots.OrderBy(r => r.cCtnRootName).Select(r => new RobotAccessResourceInfo {
         Id = r.cCtnRootId, Name = r.cCtnRootName
      }).ToArrayAsync(ct)
   };

   public Task<RobotAccessInfo[]> ReadAsync(CancellationToken ct) => db.RobotRoots.Select(r => new RobotAccessInfo {
      RobotId = r.cRobotId, ManagerId = Id, ResourceId = r.cCtnRootId, Access = r.cCtnRootRobotAccess
   }).ToArrayAsync(ct);

   public async Task SetAsync(string robotId, string resourceId, string access, CancellationToken ct) {
      if (access.Length == 0) {
         await db.RobotRoots.Where(r => r.cRobotId == robotId && r.cCtnRootId == resourceId).ExecuteDeleteAsync(ct);
         return;
      }
      access = CtnNames.ValidateAccess(access);
      var now = DateTime.UtcNow;
      var rows = await db.RobotRoots.Where(r => r.cRobotId == robotId && r.cCtnRootId == resourceId)
         .ExecuteUpdateAsync(s => s.SetProperty(r => r.cCtnRootRobotAccess, access).SetProperty(r => r.ustamp, now), ct);
      if (rows > 0) return;
      db.RobotRoots.Add(new ta_CtnRootRobot {
         cRobotId = robotId, cCtnRootId = resourceId, cCtnRootRobotAccess = access, ustamp = now, datestamp = now
      });
      try { await db.SaveChangesAsync(ct); }
      catch (DbUpdateException) { throw new ActionException("Robot access changed concurrently. Reload and try again.", 409); }
   }

   public async Task PrepareDeleteAsync(RobotContext identities, string robotId, CancellationToken ct) {
      // Registry and identities use the core database. A separate short-lived context shares
      // the exact connection/transaction so deletion never leaves half-cleaned references.
      await using var cleanup = new CtnContext(options);
      cleanup.Database.SetDbConnection(identities.Database.GetDbConnection(), contextOwnsConnection: false);
      await cleanup.Database.UseTransactionAsync(identities.Database.CurrentTransaction!.GetDbTransaction(), ct);
      _deletedUploads = await cleanup.Uploads.Where(u => u.cRobotId == robotId).Select(u => u.cCtnUploadId).ToListAsync(ct);
      await cleanup.Uploads.Where(u => u.cRobotId == robotId).ExecuteDeleteAsync(ct);
      await cleanup.Manifests.Where(m => m.cCtnManifestPushedBy_cRobotId == robotId)
         .ExecuteUpdateAsync(s => s.SetProperty(m => m.cCtnManifestPushedBy_cRobotId, (string?)null), ct);
      await cleanup.RobotRoots.Where(r => r.cRobotId == robotId).ExecuteDeleteAsync(ct);
   }

   public Task AfterDeleteAsync(CancellationToken ct) {
      foreach (var id in _deletedUploads) store.DeleteUploadFile(id);
      _deletedUploads.Clear();
      return Task.CompletedTask;
   }
}
