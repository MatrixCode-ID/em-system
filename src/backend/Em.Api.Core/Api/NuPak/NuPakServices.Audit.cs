using Em;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
namespace Em.Api.Core.NuPak;

/// <summary>NuPak management actions: feeds, prefixes, packages, robot access, audit, and storage settings.</summary>
public sealed partial class NuPakServices
{
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<ta_NuPakAudit[]> GetMeta_NuPakAudit(string feedId,NuPakAuditFilter filter,int skip,int take) {
      await RequireFeed(feedId); return await AuditQuery(db.Audits.Where(a=>a.cNuPakFeedId==feedId),filter,skip,take);
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public Task<ta_NuPakAudit[]> GetMeta_NuPakServerAudit(NuPakAuditFilter filter,int skip,int take) => AuditQuery(db.Audits,filter,skip,take);
   private Task<ta_NuPakAudit[]> AuditQuery(IQueryable<ta_NuPakAudit> q,NuPakAuditFilter filter,int skip,int take) {
      if (!string.IsNullOrWhiteSpace(filter.Package)) q = q.Where(r => r.cNuPakAuditPackage != null && r.cNuPakAuditPackage.Contains(filter.Package));
      if (!string.IsNullOrWhiteSpace(filter.Version)) q = q.Where(r => r.cNuPakAuditVersion == filter.Version);
      if (!string.IsNullOrWhiteSpace(filter.Actor)) q = q.Where(r => r.cNuPakAuditActorName.Contains(filter.Actor));
      if (!string.IsNullOrWhiteSpace(filter.Action)) q = q.Where(r => r.cNuPakAuditAction == filter.Action);
      if (!string.IsNullOrWhiteSpace(filter.Result)) q = q.Where(r => r.cNuPakAuditResult == filter.Result);
      if (filter.From is { } from) q = q.Where(r => r.cNuPakAuditAt >= from);
      if (filter.To is { } to) q = q.Where(r => r.cNuPakAuditAt <= to);
      return q.OrderByDescending(r => r.cNuPakAuditAt).ThenByDescending(r => r.cNuPakAuditId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToArrayAsync(AbortToken);
   }
}
