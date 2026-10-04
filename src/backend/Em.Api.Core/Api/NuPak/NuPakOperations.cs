using Em.Shared;
using Em.Api.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.NuPak;

public sealed record NuPakActor(string Kind, string? Id, string Name, string? Address);
public static class NuPakOperations
{
   public static bool IsUniqueConflict(DbUpdateException ex) => ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
   public static void Audit(NuPakDbContext db, NuPakActor actor, string action, string? package = null,
      string? version = null, string result = "Success", string? detail = null, ta_NuPakFeed? feed = null) {
      var now = DateTime.UtcNow;
      db.Audits.Add(new() { cNuPakFeedId=feed?.cNuPakFeedId,cNuPakAuditFeedSlug=feed?.cNuPakFeedSlug,cNuPakAuditFeedName=feed?.cNuPakFeedName, cNuPakAuditId = $"{Ulid.NewUlid()}", cNuPakAuditAt = now,
         cNuPakAuditAction = action, cNuPakAuditPackage = package, cNuPakAuditVersion = version,
         cNuPakAuditActorKind = actor.Kind, cNuPakAuditActorId = actor.Id, cNuPakAuditActorName = actor.Name,
         cNuPakAuditAddress = actor.Address, cNuPakAuditResult = result,
         cNuPakAuditDetail = detail is null ? null : detail[..Math.Min(500, detail.Length)], ustamp = now, datestamp = now });
   }
   public static async Task PushAsync(NuPakDbContext db, NuPakStore store, ta_NuPakFeed feed, NuPakStore.Upload upload, string robotId, NuPakActor actor) {
      await store.Gate.WaitAsync();
      string? placed = null;
      try {
         await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
         var currentFeed=await db.Feeds.SingleOrDefaultAsync(f=>f.cNuPakFeedId==feed.cNuPakFeedId) ?? throw new ActionException("Feed not found.",404);
         if(!currentFeed.cNuPakFeedEnabled || !bool.TryParse(await db.Meta.Where(m=>m.cMetaKey=="NuPakEnable").Select(m=>m.cMetaValue).SingleOrDefaultAsync(),out var serverEnabled) || !serverEnabled) throw new ActionException("Feed is off.",404);
         feed=currentFeed;
         var package = await db.Packages.SingleOrDefaultAsync(p => p.cNuPakFeedId==feed.cNuPakFeedId && p.cNuPakPackageName == upload.Id);
         var prefix = package is null
            ? (await db.Prefixes.Where(p => p.cNuPakFeedId==feed.cNuPakFeedId && p.cNuPakPrefixState == 1).ToListAsync())
               .Where(p => upload.Id.StartsWith(p.cNuPakPrefixName, StringComparison.OrdinalIgnoreCase))
               .OrderByDescending(p => p.cNuPakPrefixName.Length).FirstOrDefault()
            : await db.Prefixes.SingleOrDefaultAsync(p => p.cNuPakPrefixId == package.cNuPakPrefixId && p.cNuPakPrefixState == 1);
         if (prefix is null) throw new ActionException("No registered active prefix matches this package.", 403);
         if (!await db.Grants.AnyAsync(g => g.cRobotId == robotId && g.cNuPakPrefixId == prefix.cNuPakPrefixId && g.cNuPakPrefixRobotAccess == "W"))
            throw new ActionException("Write access is required for this prefix.", 403);
         if (package is not null && await db.Versions.AnyAsync(v => v.cNuPakPackageId == package.cNuPakPackageId && v.cNuPakVersionNumber == upload.Version))
            throw new ActionException("This package version already exists, possibly in the recycle bin.", 409);
         var now = DateTime.UtcNow;
         if (package is null) {
            package = new() { cNuPakFeedId=feed.cNuPakFeedId, cNuPakPackageId = $"{Ulid.NewUlid()}", cNuPakPrefixId = prefix.cNuPakPrefixId,
               cNuPakPackageName = upload.Id, cNuPakPackageState = 1, ustamp = now, datestamp = now };
            db.Packages.Add(package);
         }
         var path = store.PackagePath(feed.cNuPakFeedId,upload.Id, upload.Version);
         Directory.CreateDirectory(Path.GetDirectoryName(path)!);
         if (File.Exists(path)) throw new ActionException("Package file already exists. Contact the server administrator.", 409);
         File.Move(upload.Path, path); placed = path;
         db.Versions.Add(new() { cNuPakVersionId = $"{Ulid.NewUlid()}", cNuPakPackageId = package.cNuPakPackageId,
            cNuPakVersionNumber = upload.Version, cNuPakVersionOriginal = upload.Original, cNuPakVersionPrerelease = upload.Prerelease,
            cNuPakVersionState = 1, cNuPakVersionSize = upload.Size, cNuPakVersionHash = upload.Hash,
            cNuPakVersionNuspec = upload.Nuspec, cNuPakVersionTitle = upload.Title, cNuPakVersionDescription = upload.Description,
            cNuPakVersionAuthors = upload.Authors, cNuPakVersionTags = upload.Tags,
            cNuPakVersionPushedBy_cRobotId = robotId, ustamp = now, datestamp = now });
         Audit(db, actor, "Push", upload.Id, upload.Version,feed:feed);
         await db.SaveChangesAsync(); await tx.CommitAsync(); placed = null;
      } catch (DbUpdateException ex) when (IsUniqueConflict(ex)) { throw new ActionException("Package changed concurrently or the version already exists (including recycle bin).", 409); }
      finally {
         try { if (placed is not null) File.Delete(placed); File.Delete(upload.Path); }
         finally { store.Gate.Release(); }
      }
   }
   public static async Task ChangeStateAsync(NuPakDbContext db, NuPakStore store, string feedId, string versionId, bool restore, NuPakActor actor) {
      await store.Gate.WaitAsync();
      try {
         await using var tx = await db.Database.BeginTransactionAsync();
         var feed=await db.Feeds.SingleOrDefaultAsync(f=>f.cNuPakFeedId==feedId)??throw new ActionException("Feed not found.",404);
         var row = await db.Versions.AsTracking().SingleOrDefaultAsync(v => v.cNuPakVersionId == versionId && db.Packages.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==v.cNuPakPackageId))
            ?? throw new ActionException("Version not found.", 404);
         if (row.cNuPakVersionState != (restore ? -2 : 1)) throw new ActionException("Version is not in the required state.", restore ? 409 : 404);
         var name = await db.Packages.Where(p => p.cNuPakPackageId == row.cNuPakPackageId).Select(p => p.cNuPakPackageName).SingleAsync();
         row.cNuPakVersionState = restore ? 1 : -2; row.cNuPakVersionRecycledAt = restore ? null : DateTime.UtcNow; row.ustamp = DateTime.UtcNow;
         Audit(db, actor, restore ? "Restore" : "Recycle", name, row.cNuPakVersionNumber,feed:feed);
         await db.SaveChangesAsync(); await tx.CommitAsync();
      } finally { store.Gate.Release(); }
   }
   public static async Task<long> PurgeAsync(NuPakDbContext db, NuPakStore store, string feedId, string versionId, NuPakActor actor) {
      await store.Gate.WaitAsync();
      try {return await PurgeUnderGateAsync(db,store,feedId,versionId,actor);}
      finally {store.Gate.Release();}
   }
   internal static async Task<long> PurgeUnderGateAsync(NuPakDbContext db,NuPakStore store,string feedId,string versionId,NuPakActor actor) {
      string? source = null, quarantine = null; bool committed = false;
      try {
         await using var tx = await db.Database.BeginTransactionAsync();
         var feed=await db.Feeds.SingleOrDefaultAsync(f=>f.cNuPakFeedId==feedId)??throw new ActionException("Feed not found.",404);
         var row = await db.Versions.SingleOrDefaultAsync(v => v.cNuPakVersionId == versionId && v.cNuPakVersionState == -2 && db.Packages.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==v.cNuPakPackageId))
            ?? throw new ActionException("Recycled version not found.", 404);
         var package = await db.Packages.SingleAsync(p => p.cNuPakPackageId == row.cNuPakPackageId);
         source = store.PackagePath(feedId,package.cNuPakPackageName, row.cNuPakVersionNumber);
         // Rename first. If SQL rolls back, restore the file. Recovery files survive a crash and startup
         // reconciles them against SQL; they must never be treated as disposable upload scratch files.
         quarantine = source + ".purge";
         if (File.Exists(source)) File.Move(source, quarantine);
         else if (!File.Exists(quarantine)) throw new IOException("Package file is missing; reconcile storage before purging.");
         await db.Versions.Where(v => v.cNuPakVersionId == versionId).ExecuteDeleteAsync();
         if (!await db.Versions.AnyAsync(v => v.cNuPakPackageId == package.cNuPakPackageId))
            await db.Packages.Where(p => p.cNuPakPackageId == package.cNuPakPackageId).ExecuteDeleteAsync();
         Audit(db, actor, "Purge", package.cNuPakPackageName, row.cNuPakVersionNumber,feed:feed);
         await db.SaveChangesAsync(); await tx.CommitAsync(); committed = true;
         File.Delete(quarantine); store.CleanDirectories(source);
         return row.cNuPakVersionSize;
      } finally {
         if (!committed && source is not null && quarantine is not null && File.Exists(quarantine)) File.Move(quarantine, source);
      }
   }
}
