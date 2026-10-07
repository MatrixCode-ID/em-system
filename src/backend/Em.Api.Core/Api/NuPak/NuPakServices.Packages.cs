using Em;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
namespace Em.Api.Core.NuPak;

public sealed partial class NuPakServices
{
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakPackageInfo[]> GetMeta_NuPakPackages(string feedId, string prefixId, string? search, int skip, int take) {
      await RequirePrefix(feedId,prefixId);
      var rows = await db.Packages.Where(p => p.cNuPakPrefixId == prefixId && (search == null || p.cNuPakPackageName.Contains(search)))
         .OrderBy(p => p.cNuPakPackageName).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToArrayAsync(AbortToken);
      var ids = rows.Select(p => p.cNuPakPackageId).ToArray();
      var versions = await db.Versions.Where(v => ids.Contains(v.cNuPakPackageId)).Select(v => new { v.cNuPakPackageId, v.cNuPakVersionNumber, v.cNuPakVersionState, v.cNuPakVersionSize }).ToArrayAsync(AbortToken);
      return rows.Select(p => { var all = versions.Where(v => v.cNuPakPackageId == p.cNuPakPackageId).ToArray();
         return new NuPakPackageInfo(p.cNuPakPackageId, p.cNuPakPackageName, all.Count(v => v.cNuPakVersionState == 1), all.Sum(v => v.cNuPakVersionSize),
            all.Where(v => v.cNuPakVersionState == 1).Select(v => v.cNuPakVersionNumber).OrderByDescending(v => NuGetVersion.Parse(v)).FirstOrDefault()); }).ToArray();
   }
   private IQueryable<NuPakVersionInfo> VersionQuery(IQueryable<ta_NuPakVersion> query) =>
      from v in query join p in db.Packages on v.cNuPakPackageId equals p.cNuPakPackageId
      join r in db.Robots on v.cNuPakVersionPushedBy_cRobotId equals r.cRobotId into robots
      from r in robots.DefaultIfEmpty()
      select new NuPakVersionInfo(v.cNuPakVersionId, p.cNuPakPackageId, p.cNuPakPackageName, v.cNuPakVersionNumber, v.cNuPakVersionOriginal,
         v.cNuPakVersionPrerelease, v.cNuPakVersionState, v.cNuPakVersionSize, v.cNuPakVersionHash, r == null ? null : r.cRobotName, v.datestamp, v.cNuPakVersionRecycledAt);
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakVersionInfo[]> GetMeta_NuPakVersions(string feedId, string packageId) {
      await RequirePackage(feedId,packageId);
      return (await VersionQuery(db.Versions.Where(v => v.cNuPakVersionState == 1 && v.cNuPakPackageId == packageId)).ToArrayAsync(AbortToken)).OrderByDescending(v => NuGetVersion.Parse(v.Version)).ToArray();
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakVersionInfo[]> GetMeta_NuPakRecycleBin(string feedId, string? prefixId, int skip, int take) {
      await RequireFeed(feedId); if(prefixId is not null) await RequirePrefix(feedId,prefixId);
      var query = db.Versions.Where(v => v.cNuPakVersionState == -2 && db.Packages.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==v.cNuPakPackageId));
      if (prefixId is not null) query = query.Where(v => db.Packages.Any(p => p.cNuPakPackageId == v.cNuPakPackageId && p.cNuPakPrefixId == prefixId));
      return await VersionQuery(query.OrderByDescending(v => v.cNuPakVersionRecycledAt).ThenBy(v => v.cNuPakVersionId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))).ToArrayAsync(AbortToken);
   }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public Task PostMeta_NuPakVersionRecycle(string feedId, string versionId) => NuPakOperations.ChangeStateAsync(db, store, feedId, versionId, false, Actor);
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public Task PostMeta_NuPakVersionRestore(string feedId, string versionId) => NuPakOperations.ChangeStateAsync(db, store, feedId, versionId, true, Actor);
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public async Task PostMeta_NuPakVersionPurge(string feedId, string versionId) { await NuPakOperations.PurgeAsync(db, store, feedId, versionId, Actor); }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public async Task<NuPakEmptyResult> PostMeta_NuPakRecycleBinEmpty(string feedId, string? prefixId) {
      await store.Gate.WaitAsync(AbortToken);
      try {
      await RequireFeed(feedId); if(prefixId is not null) await RequirePrefix(feedId,prefixId);
      var query = db.Versions.Where(v => v.cNuPakVersionState == -2 && db.Packages.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==v.cNuPakPackageId));
      if (prefixId is not null) query = query.Where(v => db.Packages.Any(p => p.cNuPakPackageId == v.cNuPakPackageId && p.cNuPakPrefixId == prefixId));
      var ids = await query.Select(v => v.cNuPakVersionId).ToArrayAsync(); int count = 0, failed = 0; long size = 0;
      foreach (var id in ids) {
         try { size += await NuPakOperations.PurgeUnderGateAsync(db, store, feedId, id, Actor); count++; }
         catch (ActionException ex) when (ex.StatusCode == 404) { failed++; db.ChangeTracker.Clear(); }
         catch (IOException) { failed++; db.ChangeTracker.Clear(); }
         catch (UnauthorizedAccessException) { failed++; db.ChangeTracker.Clear(); }
      }
      NuPakOperations.Audit(db, Actor, "EmptyBin",feed:await RequireFeed(feedId), result: failed > 0 ? "Failed" : "Success", detail: $"Purged {count}, bytes {size}, failed {failed}");
      await db.SaveChangesAsync(); return new(count, size, failed);
      } finally {store.Gate.Release();}
   }

}
