using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The position list of a tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantPositionServices(ApiCoreContext ctx) : ServicesBase, ITenantPositionServices
{
   private const string DuplicateCode = "The position code is already used in this tenant.";

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantPosition> PostGetTa_TenantPosition_New(ta_TenantPosition row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await Validate(row);
      var now = DateTime.UtcNow;
      var entity = new ta_TenantPosition {
         cTenantPositionId = string.IsNullOrWhiteSpace(row.cTenantPositionId) ? Ulid.NewUlid().ToString() : row.cTenantPositionId,
         cTenantId = row.cTenantId, cTenantPositionCode = row.cTenantPositionCode.Trim(), cTenantPositionName = row.cTenantPositionName.Trim(),
         cTenantPositionState = row.cTenantPositionState, cTenantPositionNote = row.cTenantPositionNote, json_object = row.json_object,
         ustamp = now, datestamp = now
      };
      ctx.ta_TenantPositions.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      Audit(entity.cTenantPositionId, "tenant.position.created", "Created position '" + entity.cTenantPositionCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cTenantPositionId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantPosition> PostGetTa_TenantPosition_Update(ta_TenantPosition row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_TenantPositions.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantPositionId == row.cTenantPositionId, AbortToken)
         ?? throw new ActionException("The position was not found.", 404);
      if (!TenantOrg.Same(current.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
      if (current.ustamp != row.ustamp) throw new ActionException("The position changed. Refresh before saving.", 409);
      await Validate(row);
      await TenantOrg.Save(() => ctx.ta_TenantPositions.Where(x => x.cTenantPositionId == row.cTenantPositionId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantPositionCode, row.cTenantPositionCode.Trim()).SetProperty(x => x.cTenantPositionName, row.cTenantPositionName.Trim())
         .SetProperty(x => x.cTenantPositionState, row.cTenantPositionState).SetProperty(x => x.cTenantPositionNote, row.cTenantPositionNote)
         .SetProperty(x => x.json_object, row.json_object).SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken), DuplicateCode);
      Audit(row.cTenantPositionId, "tenant.position.changed", "Saved position '" + row.cTenantPositionCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(row.cTenantPositionId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantPosition[]> GetVi_TenantPositions(string tenantId) => ctx.vi_TenantPositions.AsNoTracking()
      .Where(x => x.cTenantId == tenantId).OrderBy(x => x.cTenantPositionName).ThenBy(x => x.cTenantPositionCode).ThenBy(x => x.cTenantPositionId).ToArrayAsync(AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantPosition[]> PostGetMeta_TenantPosition_Copy(TenantPositionCopy copy) {
      if (TenantOrg.Same(copy.SourceTenantId, copy.TargetTenantId)) throw new ActionException("Choose another tenant to copy from.", 400);
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == copy.SourceTenantId, AbortToken) || !await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == copy.TargetTenantId, AbortToken))
         throw new ActionException("The tenant was not found.", 400);
      var sources = await ctx.ta_TenantPositions.AsNoTracking()
         .Where(x => x.cTenantId == copy.SourceTenantId && x.cTenantPositionState == ta_TenantPosition.StateActive && copy.PositionIds.Contains(x.cTenantPositionId)).ToArrayAsync(AbortToken);
      var codes = (await ctx.ta_TenantPositions.AsNoTracking().Where(x => x.cTenantId == copy.TargetTenantId).Select(x => x.cTenantPositionCode).ToArrayAsync(AbortToken))
         .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var now = DateTime.UtcNow;
      var created = sources.Where(x => codes.Add(x.cTenantPositionCode)).Select(x => new ta_TenantPosition {
         cTenantPositionId = Ulid.NewUlid().ToString(), cTenantId = copy.TargetTenantId, cTenantPositionCode = x.cTenantPositionCode,
         cTenantPositionName = x.cTenantPositionName, cTenantPositionNote = x.cTenantPositionNote, cTenantPositionState = ta_TenantPosition.StateActive,
         ustamp = now, datestamp = now
      }).ToArray();
      ctx.ta_TenantPositions.AddRange(created);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      foreach (var row in created) Audit(row.cTenantPositionId, "tenant.position.created", "Copied position '" + row.cTenantPositionCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      var ids = created.Select(x => x.cTenantPositionId).ToArray();
      return await ctx.vi_TenantPositions.AsNoTracking().Where(x => ids.Contains(x.cTenantPositionId)).OrderBy(x => x.cTenantPositionName).ToArrayAsync(AbortToken);
   }
   #endregion

   private async Task Validate(ta_TenantPosition row) {
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantPositionCode)) throw new ActionException("Enter the position code.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantPositionName)) throw new ActionException("Enter the position name.", 400);
      if (row.cTenantPositionState is not (ta_TenantPosition.StateActive or ta_TenantPosition.StateInactive)) throw new ActionException("The position state is not valid.", 400);
   }
   private Task<vi_TenantPosition> Read(string id) => ctx.vi_TenantPositions.AsNoTracking().SingleAsync(x => x.cTenantPositionId == id, AbortToken);
   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "position"
      });
   }
}
