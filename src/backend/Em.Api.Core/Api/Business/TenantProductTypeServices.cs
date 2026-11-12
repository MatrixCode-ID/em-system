using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>The product types of a tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantProductTypeServices(ApiCoreContext ctx) : ServicesBase, ITenantProductTypeServices
{
   private const string DuplicateCode = "The product type code is already used in this tenant.";
   private const string SystemKind = "The generic transaction kind is created by the system only.";

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProductType> PostGetTa_TenantProductType_New(ta_TenantProductType row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      await Validate(row);
      var entity = await Insert(row.cTenantProductTypeId, row.cTenantId, row.cTenantProductTypeCode, row.cTenantProductTypeName, row.cTenantProductTypeKind,
         row.cTenantProductTypeState, row.cTenantProductTypeOrder, row.cTenantProductTypeNote, row.json_object);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cTenantProductTypeId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProductType> PostGetTa_TenantProductType_Update(ta_TenantProductType row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_TenantProductTypes.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductTypeId == row.cTenantProductTypeId, AbortToken)
         ?? throw new ActionException("The product type was not found.", 404);
      if (!TenantOrg.Same(current.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
      if (current.ustamp != row.ustamp) throw new ActionException("The product type changed. Refresh before saving.", 409);
      if (current.cTenantProductTypeKind == ProductKind.GenericTransaction) {
         // The system type: only its name, order and note can change.
         if (string.IsNullOrWhiteSpace(row.cTenantProductTypeName)) throw new ActionException("Enter the product type name.", 400);
         if (!TenantOrg.Same(current.cTenantProductTypeCode, row.cTenantProductTypeCode?.Trim()) || row.cTenantProductTypeKind != current.cTenantProductTypeKind ||
             row.cTenantProductTypeState != ta_TenantProductType.StateActive)
            throw new ActionException("The system product type only accepts a new name and note.", 400);
      }
      else {
         await Validate(row);
         if (row.cTenantProductTypeKind != current.cTenantProductTypeKind &&
             await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantProductTypeId == current.cTenantProductTypeId, AbortToken))
            throw new ActionException("The kind cannot change because this product type already has products.", 400);
      }
      await TenantOrg.Save(() => ctx.ta_TenantProductTypes.Where(x => x.cTenantProductTypeId == current.cTenantProductTypeId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantProductTypeCode, row.cTenantProductTypeCode!.Trim()).SetProperty(x => x.cTenantProductTypeName, row.cTenantProductTypeName.Trim())
         .SetProperty(x => x.cTenantProductTypeKind, row.cTenantProductTypeKind).SetProperty(x => x.cTenantProductTypeState, row.cTenantProductTypeState)
         .SetProperty(x => x.cTenantProductTypeOrder, row.cTenantProductTypeOrder).SetProperty(x => x.cTenantProductTypeNote, row.cTenantProductTypeNote)
         .SetProperty(x => x.json_object, row.json_object).SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken), DuplicateCode);
      Audit(current.cTenantProductTypeId, "tenant.product.type.changed", "Saved product type '" + row.cTenantProductTypeCode!.Trim() + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(current.cTenantProductTypeId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProductType[]> GetVi_TenantProductTypes(string tenantId) {
      await TenantProductSeed.EnsureAsync(ctx, tenantId, Writer, AbortToken);
      return await ctx.vi_TenantProductTypes.AsNoTracking().Where(x => x.cTenantId == tenantId)
         .OrderBy(x => x.cTenantProductTypeOrder).ThenBy(x => x.cTenantProductTypeName).ThenBy(x => x.cTenantProductTypeId).ToArrayAsync(AbortToken);
   }
   #endregion

   #region Meta's
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProductType> PostGetMeta_TenantProductType_Ensure(string tenantId, string code, string name, ProductKind kind) {
      if (string.IsNullOrWhiteSpace(code)) throw new ActionException("Enter the product type code.", 400);
      var key = code.Trim();
      Task<vi_TenantProductType?> Find() => ctx.vi_TenantProductTypes.AsNoTracking().FirstOrDefaultAsync(x => x.cTenantId == tenantId && x.cTenantProductTypeCode == key, AbortToken);
      if (await Find() is { } found) return found;
      try {
         await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
         if (await Find() is { } existing) return existing;
         await Validate(new ta_TenantProductType { cTenantId = tenantId, cTenantProductTypeCode = key, cTenantProductTypeName = name, cTenantProductTypeKind = kind });
         var entity = await Insert("", tenantId, key, name, kind, ta_TenantProductType.StateActive, -1, null, null);
         await tx.CommitAsync(AbortToken);
         return await Read(entity.cTenantProductTypeId);
      }
      // Another request created the same type at the same moment.
      catch (ActionException ex) when (ex.StatusCode == 409) {
         ctx.ChangeTracker.Clear();
         return await Find() ?? throw new ActionException(DuplicateCode, 409);
      }
   }
   #endregion

   private async Task<ta_TenantProductType> Insert(string? id, string tenantId, string code, string name, ProductKind kind, int state, int order, string? note, string? json) {
      var now = DateTime.UtcNow;
      var entity = new ta_TenantProductType {
         cTenantProductTypeId = string.IsNullOrWhiteSpace(id) ? Ulid.NewUlid().ToString() : id, cTenantId = tenantId, cTenantProductTypeCode = code.Trim(),
         cTenantProductTypeName = name.Trim(), cTenantProductTypeKind = kind, cTenantProductTypeState = state, cTenantProductTypeOrder = order,
         cTenantProductTypeNote = note, json_object = json, ustamp = now, datestamp = now
      };
      ctx.ta_TenantProductTypes.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      Audit(entity.cTenantProductTypeId, "tenant.product.type.created", "Created product type '" + entity.cTenantProductTypeCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      return entity;
   }

   private async Task Validate(ta_TenantProductType row) {
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantProductTypeCode)) throw new ActionException("Enter the product type code.", 400);
      if (string.IsNullOrWhiteSpace(row.cTenantProductTypeName)) throw new ActionException("Enter the product type name.", 400);
      if (row.cTenantProductTypeState is not (ta_TenantProductType.StateActive or ta_TenantProductType.StateInactive)) throw new ActionException("The product type state is not valid.", 400);
      if (row.cTenantProductTypeKind == ProductKind.GenericTransaction) throw new ActionException(SystemKind, 400);
      if (!Enum.IsDefined(row.cTenantProductTypeKind)) throw new ActionException("The product type kind is not valid.", 400);
      if (TenantProductSeed.IsReserved(row.cTenantProductTypeCode)) throw new ActionException("This code is reserved for the system product type.", 400);
   }
   private Task<vi_TenantProductType> Read(string id) => ctx.vi_TenantProductTypes.AsNoTracking().SingleAsync(x => x.cTenantProductTypeId == id, AbortToken);
   private IAuditWriter? Writer => App is null ? null : GetService<IAuditWriter>();
   private void Audit(string id, string kind, string summary) => Writer?.Use(ctx).AddEntry(new AuditEntryDraft {
      Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "product type"
   });
}
