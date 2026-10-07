using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core;
using Em.Shared;
using Em.Api.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.NuPak;
public sealed partial class NuPakServices {
   private async Task<ta_NuPakFeed> RequireFeed(string feedId) => await db.Feeds.SingleOrDefaultAsync(f=>f.cNuPakFeedId==feedId,AbortToken) ?? throw new ActionException("Feed not found.",404);
   private async Task RequirePrefix(string feedId,string prefixId) {
      await RequireFeed(feedId);
      if(!await db.Prefixes.AnyAsync(p=>p.cNuPakFeedId==feedId&&p.cNuPakPrefixId==prefixId,AbortToken)) throw new ActionException("Prefix not found in feed.",404);
   }
   private async Task RequirePackage(string feedId,string packageId) {
      await RequireFeed(feedId);
      if(!await db.Packages.AnyAsync(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==packageId,AbortToken)) throw new ActionException("Package not found in feed.",404);
   }
   private async Task<NuPakFeedInfo> FeedInfo(ta_NuPakFeed f) {
      var enabled=(await settings.ReadAsync(db,AbortToken)).Enabled && (HttpContext?.RequestServices?.GetService<Em.Api.Core.Storage.ManagedStorageSettings>()?.Active(2).Enabled ?? true);
      var versions=db.Versions.Where(v=>db.Packages.Any(p=>p.cNuPakFeedId==f.cNuPakFeedId&&p.cNuPakPackageId==v.cNuPakPackageId));
      return new(f.cNuPakFeedId,f.cNuPakFeedSlug,f.cNuPakFeedName,f.cNuPakFeedDescription,f.cNuPakFeedEnabled,f.cNuPakFeedAnonymousRead,enabled&&f.cNuPakFeedEnabled,
         await db.Prefixes.CountAsync(p=>p.cNuPakFeedId==f.cNuPakFeedId,AbortToken),
         await db.Grants.CountAsync(g=>db.Prefixes.Any(p=>p.cNuPakFeedId==f.cNuPakFeedId&&p.cNuPakPrefixId==g.cNuPakPrefixId),AbortToken),
         await db.Packages.CountAsync(p=>p.cNuPakFeedId==f.cNuPakFeedId,AbortToken),
         await versions.CountAsync(v=>v.cNuPakVersionState==1,AbortToken),await versions.CountAsync(v=>v.cNuPakVersionState==-2,AbortToken));
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakFeedInfo[]> GetMeta_NuPakFeeds() {
      var rows=await db.Feeds.OrderBy(f=>f.cNuPakFeedSlug).ToArrayAsync(AbortToken);
      var result=new List<NuPakFeedInfo>(); foreach(var f in rows) result.Add(await FeedInfo(f)); return result.ToArray();
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakFeedInfo> GetMeta_NuPakFeed(string feedId) => await FeedInfo(await RequireFeed(feedId));
   private static string FeedName(string name) {
      if(string.IsNullOrWhiteSpace(name)) throw new ActionException("Feed name is required.",400);
      name=name.Trim(); if(name.Length >100) throw new ActionException("Feed name must contain 1–100 characters.",400); return name;
   }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public async Task<NuPakFeedInfo> PostGetMeta_NuPakFeedCreate(string slug,string name,string? description) {
      slug=NuPakStore.Slug(slug); name=FeedName(name); description=Description(description);
      await store.Gate.WaitAsync(AbortToken);
      ta_NuPakFeed row;
      try {
         var now=DateTime.UtcNow;
         row=new(){cNuPakFeedId=$"{Ulid.NewUlid()}",cNuPakFeedSlug=slug,cNuPakFeedName=name,cNuPakFeedDescription=description,ustamp=now,datestamp=now};
         db.Feeds.Add(row); NuPakOperations.Audit(db,Actor,"FeedCreate",feed:row);
         try {await db.SaveChangesAsync(AbortToken);} catch(DbUpdateException ex) when(NuPakOperations.IsUniqueConflict(ex)) {throw new ActionException("Feed slug already exists.",409);}
      } finally {store.Gate.Release();}
      return await FeedInfo(row);
   }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public async Task<NuPakFeedInfo> PostGetMeta_NuPakFeedUpdate(string feedId,string name,string? description,bool enabled,bool anonymousRead) {
      name=FeedName(name);description=Description(description); await store.Gate.WaitAsync(AbortToken);
      ta_NuPakFeed row;
      try {
         await using var tx=await db.Database.BeginTransactionAsync(AbortToken);
         row=await db.Feeds.AsTracking().SingleOrDefaultAsync(f=>f.cNuPakFeedId==feedId,AbortToken)??throw new ActionException("Feed not found.",404);
         row.cNuPakFeedName=name;row.cNuPakFeedDescription=description;row.cNuPakFeedEnabled=enabled;row.cNuPakFeedAnonymousRead=anonymousRead;row.ustamp=DateTime.UtcNow;
         NuPakOperations.Audit(db,Actor,"FeedUpdate",detail:$"Enabled={enabled}; Anonymous={anonymousRead}",feed:row);
         await db.SaveChangesAsync(AbortToken);await tx.CommitAsync(AbortToken);
      } finally {store.Gate.Release();}
      return await FeedInfo(row);
   }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public async Task PostMeta_NuPakFeedDelete(string feedId) {
      await store.Gate.WaitAsync(AbortToken);
      try {
         await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,AbortToken);
         var feed=await RequireFeed(feedId);
         if(await db.Packages.AnyAsync(p=>p.cNuPakFeedId==feedId,AbortToken)) throw new ActionException("Feed contains packages, including recycled versions. Purge them first.",409);
         // Never delete untracked files: an orphan requires administrator reconciliation.
         store.EnsureFeedEmpty(feedId);
         await db.Grants.Where(g=>db.Prefixes.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPrefixId==g.cNuPakPrefixId)).ExecuteDeleteAsync(AbortToken);
         await db.Prefixes.Where(p=>p.cNuPakFeedId==feedId).ExecuteDeleteAsync(AbortToken);
         NuPakOperations.Audit(db,Actor,"FeedDelete",feed:feed);await db.SaveChangesAsync(AbortToken);
         await db.Audits.Where(a=>a.cNuPakFeedId==feedId).ExecuteUpdateAsync(s=>s.SetProperty(a=>a.cNuPakFeedId,(string?)null),AbortToken);
         await db.Feeds.Where(f=>f.cNuPakFeedId==feedId).ExecuteDeleteAsync(AbortToken);await tx.CommitAsync(AbortToken);
         try {store.DeleteEmptyFeed(feedId);} catch(IOException) {throw new ActionException("Feed deleted; empty folder cleanup failed. Inspect the server store.",500);}
         catch(UnauthorizedAccessException) {throw new ActionException("Feed deleted; folder cleanup access denied. Inspect the server store.",500);}
      } finally {store.Gate.Release();}
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakStorageInfo> GetMeta_NuPakFeedStorageSize(string feedId) {
      await RequireFeed(feedId); var q=db.Versions.Where(v=>db.Packages.Any(p=>p.cNuPakFeedId==feedId&&p.cNuPakPackageId==v.cNuPakPackageId));
      return new(await q.Where(v=>v.cNuPakVersionState==1).SumAsync(v=>(long?)v.cNuPakVersionSize,AbortToken)??0,
         await q.Where(v=>v.cNuPakVersionState==-2).SumAsync(v=>(long?)v.cNuPakVersionSize,AbortToken)??0,
         await db.Packages.CountAsync(p=>p.cNuPakFeedId==feedId,AbortToken),await q.CountAsync(AbortToken));
   }
}
