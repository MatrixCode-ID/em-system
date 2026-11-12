using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The units of measure shared by every tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class UnitServices(ApiCoreContext ctx) : ServicesBase, IUnitServices
{
   private const string DuplicateCode = "The unit code is already used.";
   private const string InUse = "The unit is used by products and cannot be deleted.";

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_Unit> PostGetTa_Unit_New(ta_Unit row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      Validate(row);
      var code = row.cUnitId.Trim();
      if (await ctx.ta_Units.AnyAsync(x => x.cUnitId == code, AbortToken)) throw new ActionException(DuplicateCode, 409);
      var now = DateTime.UtcNow;
      var entity = new ta_Unit { cUnitId = code, cUnitSymbol = row.cUnitSymbol.Trim(), cUnitDesc = row.cUnitDesc.Trim(), ustamp = now, datestamp = now };
      ctx.ta_Units.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      Audit(entity.cUnitId, "unit.created", "Created unit '" + entity.cUnitId + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cUnitId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_Unit> PostGetTa_Unit_Update(ta_Unit row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_Units.AsNoTracking().SingleOrDefaultAsync(x => x.cUnitId == row.cUnitId, AbortToken)
         ?? throw new ActionException("The unit was not found.", 404);
      if (current.ustamp != row.ustamp) throw new ActionException("The unit changed. Refresh before saving.", 409);
      Validate(row);
      // The code is the key products refer to: only the symbol and the description are written.
      await ctx.ta_Units.Where(x => x.cUnitId == current.cUnitId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cUnitSymbol, row.cUnitSymbol.Trim()).SetProperty(x => x.cUnitDesc, row.cUnitDesc.Trim())
         .SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken);
      Audit(current.cUnitId, "unit.changed", "Saved unit '" + current.cUnitId + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(current.cUnitId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_Unit[]> GetVi_Units() => ctx.vi_Units.AsNoTracking().OrderBy(x => x.cUnitId).ToArrayAsync(AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task PostMeta_Unit_Delete(string unitId, DateTime ustamp) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_Units.AsNoTracking().SingleOrDefaultAsync(x => x.cUnitId == unitId, AbortToken)
         ?? throw new ActionException("The unit was not found.", 404);
      if (current.ustamp != ustamp) throw new ActionException("The unit changed. Refresh before deleting it.", 409);
      if (await ctx.ta_TenantProducts.AnyAsync(x => x.cUnitId == current.cUnitId, AbortToken)) throw new ActionException(InUse, 409);
      try {
         await ctx.ta_Units.Where(x => x.cUnitId == current.cUnitId).ExecuteDeleteAsync(AbortToken);
      }
      // Something outside the product tables may still refer to the unit; the foreign key then refuses the delete.
      catch (Exception ex) when (ex is not OperationCanceledException and not ActionException) {
         throw new ActionException("The unit is still in use and cannot be deleted.", 409);
      }
      Audit(current.cUnitId, "unit.deleted", "Deleted unit '" + current.cUnitId + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
   }
   #endregion

   private static void Validate(ta_Unit row) {
      if (string.IsNullOrWhiteSpace(row.cUnitId)) throw new ActionException("Enter the unit code.", 400);
      if (row.cUnitId.Trim().Length > 50) throw new ActionException("The unit code is longer than 50 characters.", 400);
      if (string.IsNullOrWhiteSpace(row.cUnitDesc)) throw new ActionException("Enter the unit description.", 400);
      if (string.IsNullOrWhiteSpace(row.cUnitSymbol)) throw new ActionException("Enter the unit symbol.", 400);
   }
   private Task<vi_Unit> Read(string id) => ctx.vi_Units.AsNoTracking().SingleAsync(x => x.cUnitId == id, AbortToken);
   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "unit"
      });
   }
}
