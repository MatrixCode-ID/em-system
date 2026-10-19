using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Em.Api.Core;

internal static class TenantOrg
{
   internal static string? Optional(string? id) => string.IsNullOrWhiteSpace(id) ? null : id;
   internal static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
   internal static DateTime Stamp(DateTime previous) => DateTime.UtcNow > previous.AddMilliseconds(10) ? DateTime.UtcNow : previous.AddMilliseconds(10);
   internal static async Task Save(Func<Task> action, string message) {
      try { await action(); }
      catch (Exception ex) when ((ex as SqlException ?? ex.InnerException as SqlException) is { Number: 2601 or 2627 }) {
         throw new ActionException(message, 409);
      }
   }
}

/// <summary>Tenant department hierarchy and head management.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantDeptServices(ApiCoreContext ctx) : ServicesBase, ITenantDeptServices
{
   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantDept> PostGetTa_TenantDept_New(ta_TenantDept row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await Validate(row, false);
      var entity = new ta_TenantDept {
         cTenantDeptId = string.IsNullOrWhiteSpace(row.cTenantDeptId) ? Ulid.NewUlid().ToString() : row.cTenantDeptId,
         cTenantId = row.cTenantId, cTenantDeptParent_cTenantDeptId = TenantOrg.Optional(row.cTenantDeptParent_cTenantDeptId),
         cTenantDeptCode = row.cTenantDeptCode, cTenantDeptName = row.cTenantDeptName, cTenantDeptAbv = row.cTenantDeptAbv,
         cTenantDeptState = row.cTenantDeptState, cTenantDeptOrder = row.cTenantDeptOrder, cTenantDeptNote = row.cTenantDeptNote,
         cTenantDeptHead_cTenantEmpId = null, json_object = row.json_object, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow
      };
      ctx.ta_TenantDepts.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), "The department code is already used in this tenant.");
      Audit(entity.cTenantDeptId, "tenant.dept.created", "Created department '" + entity.cTenantDeptCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cTenantDeptId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantDept> PostGetTa_TenantDept_Update(ta_TenantDept row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_TenantDepts.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantDeptId == row.cTenantDeptId, AbortToken)
         ?? throw new ActionException("The department was not found.", 404);
      if (!TenantOrg.Same(current.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
      if (current.ustamp != row.ustamp) throw new ActionException("The department changed. Refresh before saving.", 409);
      await Validate(row, true);
      await TenantOrg.Save(() => ctx.ta_TenantDepts.Where(x => x.cTenantDeptId == row.cTenantDeptId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantDeptParent_cTenantDeptId, TenantOrg.Optional(row.cTenantDeptParent_cTenantDeptId))
         .SetProperty(x => x.cTenantDeptHead_cTenantEmpId, TenantOrg.Optional(row.cTenantDeptHead_cTenantEmpId))
         .SetProperty(x => x.cTenantDeptCode, row.cTenantDeptCode).SetProperty(x => x.cTenantDeptName, row.cTenantDeptName)
         .SetProperty(x => x.cTenantDeptAbv, row.cTenantDeptAbv).SetProperty(x => x.cTenantDeptState, row.cTenantDeptState)
         .SetProperty(x => x.cTenantDeptOrder, row.cTenantDeptOrder).SetProperty(x => x.cTenantDeptNote, row.cTenantDeptNote)
         .SetProperty(x => x.json_object, row.json_object).SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken),
         "The department code is already used in this tenant.");
      Audit(row.cTenantDeptId, "tenant.dept.changed", "Saved department '" + row.cTenantDeptCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(row.cTenantDeptId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantDept[]> GetVi_TenantDepts(string tenantId) => ctx.vi_TenantDepts.AsNoTracking()
      .Where(x => x.cTenantId == tenantId).OrderBy(x => x.cTenantDeptOrder).ThenBy(x => x.cTenantDeptName).ThenBy(x => x.cTenantDeptId).ToArrayAsync(AbortToken);
   #endregion

   private async Task Validate(ta_TenantDept row, bool update) {
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      var parent = TenantOrg.Optional(row.cTenantDeptParent_cTenantDeptId);
      if (parent is not null) {
         var links = await ctx.ta_TenantDepts.AsNoTracking().Where(x => x.cTenantId == row.cTenantId)
            .Select(x => new { x.cTenantDeptId, x.cTenantDeptParent_cTenantDeptId }).ToDictionaryAsync(x => x.cTenantDeptId, StringComparer.OrdinalIgnoreCase, AbortToken);
         if (!links.ContainsKey(parent)) throw new ActionException("The parent department must belong to this tenant.", 400);
         var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         for (var id = parent; id is not null; id = links.TryGetValue(id, out var up) ? up.cTenantDeptParent_cTenantDeptId : null) {
            if (!visited.Add(id) || TenantOrg.Same(id, row.cTenantDeptId)) throw new ActionException("The department parent would create a cycle.", 400);
         }
      }
      var head = TenantOrg.Optional(row.cTenantDeptHead_cTenantEmpId);
      if (update && head is not null && !await ctx.ta_TenantEmps.AnyAsync(x => x.cTenantEmpId == head && x.cTenantDeptId == row.cTenantDeptId && x.cTenantId == row.cTenantId && x.cTenantEmpState == ta_TenantEmp.StateActive, AbortToken))
         throw new ActionException("The head must be an active employee of this department.", 400);
   }
   private Task<vi_TenantDept> Read(string id) => ctx.vi_TenantDepts.AsNoTracking().SingleAsync(x => x.cTenantDeptId == id, AbortToken);
   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "department"
      });
   }
}
