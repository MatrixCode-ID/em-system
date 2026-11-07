using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The attendance code list of a tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantAttCodeServices(ApiCoreContext ctx) : ServicesBase, ITenantAttCodeServices
{
   private const string DuplicateCode = "The attendance code is already used in this tenant.";

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantAttCode> PostGetTa_TenantAttCode_New(ta_TenantAttCode row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await Validate(row);
      var now = DateTime.UtcNow;
      var entity = new ta_TenantAttCode {
         cTenantAttCodeId = string.IsNullOrWhiteSpace(row.cTenantAttCodeId) ? Ulid.NewUlid().ToString() : row.cTenantAttCodeId,
         cTenantId = row.cTenantId, cTenantAttCodeCode = row.cTenantAttCodeCode.Trim(), cTenantAttCodeName = row.cTenantAttCodeName.Trim(),
         cTenantAttCodeCategory = row.cTenantAttCodeCategory, cTenantAttCodeState = row.cTenantAttCodeState, cTenantAttCodeOrder = row.cTenantAttCodeOrder,
         cTenantAttCodeNote = row.cTenantAttCodeNote, json_object = row.json_object, ustamp = now, datestamp = now
      };
      ctx.ta_TenantAttCodes.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      Audit(entity.cTenantAttCodeId, "tenant.attcode.created", "Created attendance code '" + entity.cTenantAttCodeCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cTenantAttCodeId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantAttCode> PostGetTa_TenantAttCode_Update(ta_TenantAttCode row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_TenantAttCodes.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantAttCodeId == row.cTenantAttCodeId, AbortToken)
         ?? throw new ActionException("The attendance code was not found.", 404);
      if (!TenantOrg.Same(current.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
      if (current.ustamp != row.ustamp) throw new ActionException("The attendance code changed. Refresh before saving.", 409);
      await Validate(row);
      await TenantOrg.Save(() => ctx.ta_TenantAttCodes.Where(x => x.cTenantAttCodeId == row.cTenantAttCodeId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantAttCodeCode, row.cTenantAttCodeCode.Trim()).SetProperty(x => x.cTenantAttCodeName, row.cTenantAttCodeName.Trim())
         .SetProperty(x => x.cTenantAttCodeCategory, row.cTenantAttCodeCategory).SetProperty(x => x.cTenantAttCodeState, row.cTenantAttCodeState)
         .SetProperty(x => x.cTenantAttCodeOrder, row.cTenantAttCodeOrder).SetProperty(x => x.cTenantAttCodeNote, row.cTenantAttCodeNote)
         .SetProperty(x => x.json_object, row.json_object).SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken), DuplicateCode);
      Audit(row.cTenantAttCodeId, "tenant.attcode.changed", "Saved attendance code '" + row.cTenantAttCodeCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(row.cTenantAttCodeId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantAttCode[]> GetVi_TenantAttCodes(string tenantId) => ctx.vi_TenantAttCodes.AsNoTracking()
      .Where(x => x.cTenantId == tenantId).OrderBy(x => x.cTenantAttCodeOrder).ThenBy(x => x.cTenantAttCodeName).ThenBy(x => x.cTenantAttCodeId).ToArrayAsync(AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantAttCode[]> PostGetMeta_TenantAttCode_Copy(TenantAttCodeCopy copy) {
      if (TenantOrg.Same(copy.SourceTenantId, copy.TargetTenantId)) throw new ActionException("Choose another tenant to copy from.", 400);
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == copy.SourceTenantId, AbortToken) || !await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == copy.TargetTenantId, AbortToken))
         throw new ActionException("The tenant was not found.", 400);
      var sources = await ctx.ta_TenantAttCodes.AsNoTracking()
         .Where(x => x.cTenantId == copy.SourceTenantId && x.cTenantAttCodeState == ta_TenantAttCode.StateActive && copy.CodeIds.Contains(x.cTenantAttCodeId)).ToArrayAsync(AbortToken);
      var codes = (await ctx.ta_TenantAttCodes.AsNoTracking().Where(x => x.cTenantId == copy.TargetTenantId).Select(x => x.cTenantAttCodeCode).ToArrayAsync(AbortToken))
         .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var now = DateTime.UtcNow;
      var created = sources.Where(x => codes.Add(x.cTenantAttCodeCode)).Select(x => new ta_TenantAttCode {
         cTenantAttCodeId = Ulid.NewUlid().ToString(), cTenantId = copy.TargetTenantId, cTenantAttCodeCode = x.cTenantAttCodeCode, cTenantAttCodeName = x.cTenantAttCodeName,
         cTenantAttCodeCategory = x.cTenantAttCodeCategory, cTenantAttCodeOrder = x.cTenantAttCodeOrder, cTenantAttCodeNote = x.cTenantAttCodeNote,
         cTenantAttCodeState = ta_TenantAttCode.StateActive, ustamp = now, datestamp = now
      }).ToArray();
      ctx.ta_TenantAttCodes.AddRange(created);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      foreach (var row in created) Audit(row.cTenantAttCodeId, "tenant.attcode.created", "Copied attendance code '" + row.cTenantAttCodeCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      var ids = created.Select(x => x.cTenantAttCodeId).ToArray();
      return await ctx.vi_TenantAttCodes.AsNoTracking().Where(x => ids.Contains(x.cTenantAttCodeId)).OrderBy(x => x.cTenantAttCodeOrder).ThenBy(x => x.cTenantAttCodeName).ToArrayAsync(AbortToken);
   }
   #endregion

   private async Task Validate(ta_TenantAttCode row) {
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantAttCodeCode)) throw new ActionException("Enter the attendance code.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantAttCodeName)) throw new ActionException("Enter the code name.", 400);
      if (!Enum.IsDefined(row.cTenantAttCodeCategory)) throw new ActionException("The category is not valid.", 400);
      if (row.cTenantAttCodeState is not (ta_TenantAttCode.StateActive or ta_TenantAttCode.StateInactive)) throw new ActionException("The code state is not valid.", 400);
   }

   private Task<vi_TenantAttCode> Read(string id) => ctx.vi_TenantAttCodes.AsNoTracking().SingleAsync(x => x.cTenantAttCodeId == id, AbortToken);

   private void Audit(string id, string kind, string summary) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "attendance code"
      });
   }
}
