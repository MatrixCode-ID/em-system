using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Em.Api.Core.NuPak;

/// <summary>Lets robots be granted access to NuPak prefixes through the robot manager.</summary>
public sealed class NuPakRobotAccessManager(NuPakDbContext db, DbContextOptions<NuPakDbContext> options) : IRobotAccessManager
{
   /// <inheritdoc />
   public string Id => "NuGet";
   /// <inheritdoc />
   public async Task<RobotAccessManagerInfo> DescribeAsync(CancellationToken ct) => new() {
      Id = Id, Name = "NuGet", Description = "Read: restore. Write: restore, push and recycle. Resources are package prefixes.",
      Options = [new() { Code = "R", Label = "Read" }, new() { Code = "W", Label = "Write" }],
      Resources = await (from p in db.Prefixes join f in db.Feeds on p.cNuPakFeedId equals f.cNuPakFeedId
         orderby f.cNuPakFeedSlug,p.cNuPakPrefixName select new RobotAccessResourceInfo {Id=p.cNuPakPrefixId,Name=EF.Functions.Collate(f.cNuPakFeedName,"Latin1_General_100_CI_AS")+" / "+p.cNuPakPrefixName}).ToArrayAsync(ct)
   };
   /// <inheritdoc />
   public Task<RobotAccessInfo[]> ReadAsync(CancellationToken ct) => db.Grants.Select(r => new RobotAccessInfo {
      RobotId = r.cRobotId, ManagerId = Id, ResourceId = r.cNuPakPrefixId, Access = r.cNuPakPrefixRobotAccess }).ToArrayAsync(ct);
   /// <inheritdoc />
   public async Task SetAsync(string robotId, string resourceId, string access, CancellationToken ct) {
      if (access == "") { await db.Grants.Where(r => r.cRobotId == robotId && r.cNuPakPrefixId == resourceId).ExecuteDeleteAsync(ct); return; }
      if (access is not ("R" or "W")) throw new ActionException("Access must be R or W.", 400);
      if (!await db.Prefixes.AnyAsync(r => r.cNuPakPrefixId == resourceId, ct)) throw new ActionException("Prefix not found.", 404);
      var row = await db.Grants.AsTracking().SingleOrDefaultAsync(r => r.cRobotId == robotId && r.cNuPakPrefixId == resourceId, ct);
      if (row is null) { row = new() { cRobotId = robotId, cNuPakPrefixId = resourceId, datestamp = DateTime.UtcNow }; db.Grants.Add(row); }
      row.cNuPakPrefixRobotAccess = access; row.ustamp = DateTime.UtcNow;
      try { await db.SaveChangesAsync(ct); } catch (DbUpdateException ex) when (NuPakOperations.IsUniqueConflict(ex)) { throw new ActionException("Access changed concurrently. Reload and retry.", 409); }
   }
   /// <inheritdoc />
   public async Task PrepareDeleteAsync(RobotContext identities, string robotId, CancellationToken ct) {
      await using var cleanup = new NuPakDbContext(options);
      cleanup.Database.SetDbConnection(identities.Database.GetDbConnection(), false);
      await cleanup.Database.UseTransactionAsync(identities.Database.CurrentTransaction!.GetDbTransaction(), ct);
      await cleanup.Versions.Where(r => r.cNuPakVersionPushedBy_cRobotId == robotId)
         .ExecuteUpdateAsync(s => s.SetProperty(r => r.cNuPakVersionPushedBy_cRobotId, (string?)null), ct);
      await cleanup.Grants.Where(r => r.cRobotId == robotId).ExecuteDeleteAsync(ct);
   }
   /// <inheritdoc />
   public Task AfterDeleteAsync(CancellationToken ct) => Task.CompletedTask;
}
