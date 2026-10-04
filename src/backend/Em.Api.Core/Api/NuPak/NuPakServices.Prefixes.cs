using Em;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
namespace Em.Api.Core.NuPak;

public sealed partial class NuPakServices
{
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakPrefixInfo[]> GetMeta_NuPakPrefixes(string feedId) {
      await RequireFeed(feedId);
      var rows = await db.Prefixes.Where(p=>p.cNuPakFeedId==feedId).OrderBy(r => r.cNuPakPrefixName).ToArrayAsync(AbortToken);
      var packages = await db.Packages.Where(p=>p.cNuPakFeedId==feedId).GroupBy(p => p.cNuPakPrefixId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(r => r.Id, AbortToken);
      var sizes = await (from p in db.Packages join v in db.Versions on p.cNuPakPackageId equals v.cNuPakPackageId
         where p.cNuPakFeedId==feedId
         group v by p.cNuPakPrefixId into g select new { Id = g.Key, Bytes = g.Sum(v => v.cNuPakVersionSize) }).ToDictionaryAsync(r => r.Id, AbortToken);
      return rows.Select(r => PrefixInfo(r, packages.GetValueOrDefault(r.cNuPakPrefixId)?.Count ?? 0, sizes.GetValueOrDefault(r.cNuPakPrefixId)?.Bytes ?? 0)).ToArray();
   }
   private static NuPakPrefixInfo PrefixInfo(ta_NuPakPrefix r, int count = 0, long size = 0) => new(r.cNuPakPrefixId, r.cNuPakPrefixName, r.cNuPakPrefixDescription, r.cNuPakPrefixState == 1, count, size);
   private static string PrefixName(string name) {
      name = name.Trim(); NuPakStore.Id(name.TrimEnd('.', '_', '-'));
      if (name.Length > 100 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))) throw new ActionException("Invalid prefix name.", 400);
      return name;
   }
   private static string? Description(string? text) {
      if (text?.Length > 500) throw new ActionException("Description is at most 500 characters.", 400);
      return text;
   }
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixCreate(string feedId, string name, string? description) {
      await store.Gate.WaitAsync(AbortToken);
      try {
      var feed = await RequireFeed(feedId);
      name = PrefixName(name); description = Description(description);
      var now = DateTime.UtcNow;
      var row = new ta_NuPakPrefix { cNuPakFeedId=feedId, cNuPakPrefixId = $"{Ulid.NewUlid()}", cNuPakPrefixName = name,
         cNuPakPrefixDescription = description, cNuPakPrefixState = 1, ustamp = now, datestamp = now };
      db.Prefixes.Add(row); NuPakOperations.Audit(db, Actor, "PrefixCreate", detail: name,feed:feed);
      try { await db.SaveChangesAsync(); } catch (DbUpdateException ex) when (NuPakOperations.IsUniqueConflict(ex)) { throw new ActionException("Prefix already exists.", 409); }
      return PrefixInfo(row);
      } finally {store.Gate.Release();}
   }
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixUpdate(string feedId, string prefixId, string name, string? description, bool active) {
      await store.Gate.WaitAsync(AbortToken);
      try {
      var feed = await RequireFeed(feedId);
      name = PrefixName(name); description = Description(description);
      await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
      var row = await db.Prefixes.AsTracking().SingleOrDefaultAsync(p => p.cNuPakFeedId==feedId && p.cNuPakPrefixId == prefixId) ?? throw new ActionException("Prefix not found.", 404);
      if (!string.Equals(row.cNuPakPrefixName, name, StringComparison.Ordinal) && await db.Packages.AnyAsync(p => p.cNuPakFeedId==feedId && p.cNuPakPrefixId == prefixId))
         throw new ActionException("A prefix containing packages cannot be renamed, including recycled packages.", 409);
      row.cNuPakPrefixName = name; row.cNuPakPrefixDescription = description; row.cNuPakPrefixState = active ? 1 : 0; row.ustamp = DateTime.UtcNow;
      NuPakOperations.Audit(db, Actor, "PrefixUpdate", detail: name,feed:feed);
      try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException ex) when (NuPakOperations.IsUniqueConflict(ex)) { throw new ActionException("Prefix name already exists.", 409); }
      return PrefixInfo(row);
      } finally {store.Gate.Release();}
   }
   [PostAction(claim: INuPakServices.ManagerClaim)]
   public async Task PostMeta_NuPakPrefixDelete(string feedId, string prefixId) {
      await store.Gate.WaitAsync(AbortToken);
      try {
      var feed=await RequireFeed(feedId);
      await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
      var row = await db.Prefixes.SingleOrDefaultAsync(p => p.cNuPakFeedId==feedId && p.cNuPakPrefixId == prefixId) ?? throw new ActionException("Prefix not found.", 404);
      if (await db.Packages.AnyAsync(p => p.cNuPakFeedId==feedId && p.cNuPakPrefixId == prefixId)) throw new ActionException("Prefix still contains packages, including recycle bin. Purge them first.", 409);
      await db.Grants.Where(g => g.cNuPakPrefixId == prefixId).ExecuteDeleteAsync();
      await db.Prefixes.Where(p => p.cNuPakFeedId==feedId && p.cNuPakPrefixId == prefixId).ExecuteDeleteAsync();
      NuPakOperations.Audit(db, Actor, "PrefixDelete", detail: row.cNuPakPrefixName,feed:feed);
      await db.SaveChangesAsync(); await tx.CommitAsync();
      } finally {store.Gate.Release();}
   }
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakAccessInfo[]> GetMeta_NuPakPrefixAccess(string feedId,string prefixId) {
      await RequirePrefix(feedId,prefixId);
      return await (from g in db.Grants join r in db.Robots on g.cRobotId equals r.cRobotId where g.cNuPakPrefixId==prefixId
         select new NuPakAccessInfo(r.cRobotId,r.cRobotName,g.cNuPakPrefixRobotAccess)).ToArrayAsync(AbortToken);
   }
}
