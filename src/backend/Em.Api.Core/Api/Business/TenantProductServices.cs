using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Creates the product type and the product every tenant has, when they are missing.</summary>
internal static class TenantProductSeed
{
   /// <summary>Whether a code is the one kept for the system product type and the system product.</summary>
   internal static bool IsReserved(string? code) => TenantOrg.Same(code?.Trim(), TenantProductSystem.Code);

   internal static async Task EnsureAsync(ApiCoreContext ctx, string tenantId, IAuditWriter? audit, CancellationToken ct) {
      if (await Missing(ctx, tenantId, ct) is null) return;
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == tenantId, ct)) throw new ActionException("The tenant was not found.", 404);
      // A caller that already runs a transaction keeps it; the records then commit with its own work.
      var tx = ctx.Database.CurrentTransaction is null ? await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
      try {
         if (await Missing(ctx, tenantId, ct) is not { } missing) return;
         var now = DateTime.UtcNow;
         if (!await ctx.ta_Units.AnyAsync(x => x.cUnitId == TenantProductSystem.UnitId, ct))
            ctx.ta_Units.Add(new ta_Unit { cUnitId = TenantProductSystem.UnitId, cUnitSymbol = TenantProductSystem.UnitSymbol, cUnitDesc = TenantProductSystem.UnitDesc, ustamp = now, datestamp = now });
         var typeId = missing.TypeId;
         if (typeId is null) {
            typeId = Ulid.NewUlid().ToString();
            ctx.ta_TenantProductTypes.Add(new ta_TenantProductType {
               cTenantProductTypeId = typeId, cTenantId = tenantId, cTenantProductTypeCode = TenantProductSystem.Code, cTenantProductTypeName = TenantProductSystem.TypeName,
               cTenantProductTypeKind = ProductKind.GenericTransaction, cTenantProductTypeState = ta_TenantProductType.StateActive, ustamp = now, datestamp = now
            });
         }
         // Save the referenced rows first. The database enforces these foreign keys.
         await ctx.SaveChangesAsync(ct);
         var productId = Ulid.NewUlid().ToString();
         ctx.ta_TenantProducts.Add(new ta_TenantProduct {
            cTenantProductId = productId, cTenantId = tenantId, cTenantProductTypeId = typeId, cUnitId = TenantProductSystem.UnitId,
            cTenantProductCode = TenantProductSystem.Code, cTenantProductName = TenantProductSystem.ProductName,
            cTenantProductState = ta_TenantProduct.StateActive, cTenantProductOrder = 0, ustamp = now, datestamp = now
         });
         await ctx.SaveChangesAsync(ct);
         if (audit is not null) {
            if (missing.TypeId is null) audit.Use(ctx).AddEntry(new AuditEntryDraft { Group = "System.Settings", Key = typeId, Kind = "tenant.product.type.created", Summary = "Created the system product type", TargetType = "product type" });
            audit.Use(ctx).AddEntry(new AuditEntryDraft { Group = "System.Settings", Key = productId, Kind = "tenant.product.created", Summary = "Created the system product", TargetType = "product" });
            await ctx.SaveChangesAsync(ct);
         }
         if (tx is not null) await tx.CommitAsync(ct);
      }
      // Another request created the records at the same moment; the next call finds them.
      catch (Exception ex) when (EmDbErrors.IsUniqueConflict(ex)) {
         ctx.ChangeTracker.Clear();
      }
      finally {
         if (tx is not null) await tx.DisposeAsync();
      }
   }

   private sealed record Gap(string? TypeId);

   // Null when the tenant has both records; otherwise the id of the system type when that one exists already.
   private static async Task<Gap?> Missing(ApiCoreContext ctx, string tenantId, CancellationToken ct) {
      var typeId = await ctx.ta_TenantProductTypes.AsNoTracking()
         .Where(x => x.cTenantId == tenantId && x.cTenantProductTypeKind == ProductKind.GenericTransaction).Select(x => x.cTenantProductTypeId).FirstOrDefaultAsync(ct);
      if (typeId is null) return new Gap(null);
      return await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantId == tenantId && x.cTenantProductTypeId == typeId, ct) ? null : new Gap(typeId);
   }
}

/// <summary>The product tree of a tenant.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantProductServices(ApiCoreContext ctx) : ServicesBase, ITenantProductServices
{
   private const string DuplicateCode = "The product code is already used in this tenant.";
   private const string SystemType = "Products of the system type are created by the system only.";
   private const int SearchLimit = 200;

   #region Tables
   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct> PostGetTa_TenantProduct_New(ta_TenantProduct row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var parentId = TenantOrg.Optional(row.cTenantProductParentId);
      await ValidateFields(row.cTenantId, row.cTenantProductCode, row.cTenantProductName, row.cTenantProductState);
      await ValidateType(row.cTenantId, row.cTenantProductTypeId, null);
      await ValidateUnit(row.cUnitId);
      if (parentId is not null && !await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantProductId == parentId && x.cTenantId == row.cTenantId, AbortToken))
         throw new ActionException("The parent product must belong to this tenant.", 400);
      var code = row.cTenantProductCode.Trim();
      if (await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantId == row.cTenantId && x.cTenantProductCode == code, AbortToken)) throw new ActionException(DuplicateCode, 409);
      var now = DateTime.UtcNow;
      var entity = new ta_TenantProduct {
         cTenantProductId = string.IsNullOrWhiteSpace(row.cTenantProductId) ? Ulid.NewUlid().ToString() : row.cTenantProductId,
         cTenantId = row.cTenantId, cTenantProductTypeId = row.cTenantProductTypeId, cTenantProductParentId = parentId, cUnitId = row.cUnitId,
         cTenantProductCode = code, cTenantProductName = row.cTenantProductName.Trim(), cTenantProductState = row.cTenantProductState,
         cTenantProductOrder = row.cTenantProductOrder >= 0 ? row.cTenantProductOrder : await LastOrder(row.cTenantId, parentId) + 1,
         cTenantProductNote = row.cTenantProductNote, json_object = row.json_object, ustamp = now, datestamp = now
      };
      if (TenantOrg.Same(entity.cTenantProductId, parentId)) throw new ActionException("A product cannot be its own parent.", 400);
      ctx.ta_TenantProducts.Add(entity);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      Audit(entity.cTenantProductId, "tenant.product.created", "Created product '" + entity.cTenantProductCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(entity.cTenantProductId);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct> PostGetTa_TenantProduct_Update(ta_TenantProduct row) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var current = await ctx.ta_TenantProducts.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductId == row.cTenantProductId, AbortToken)
         ?? throw new ActionException("The product was not found.", 404);
      if (!TenantOrg.Same(current.cTenantId, row.cTenantId)) throw new ActionException("The tenant cannot be changed.", 400);
      if (current.ustamp != row.ustamp) throw new ActionException("The product changed. Refresh before saving.", 409);
      if (!TenantOrg.Same(current.cTenantProductParentId, TenantOrg.Optional(row.cTenantProductParentId))) throw new ActionException("The parent of a product cannot be changed.", 400);
      await ValidateFields(row.cTenantId, row.cTenantProductCode, row.cTenantProductName, row.cTenantProductState, current);
      var currentKind = await ctx.ta_TenantProductTypes.AsNoTracking().Where(x => x.cTenantProductTypeId == current.cTenantProductTypeId).Select(x => x.cTenantProductTypeKind).SingleAsync(AbortToken);
      if (currentKind == ProductKind.GenericTransaction) {
         // The system product: only its name, unit, order and note can change.
         if (!TenantOrg.Same(current.cTenantProductCode, row.cTenantProductCode.Trim()) || !TenantOrg.Same(current.cTenantProductTypeId, row.cTenantProductTypeId) ||
             row.cTenantProductState != ta_TenantProduct.StateActive)
            throw new ActionException("The system product only accepts a new name and note.", 400);
      }
      else await ValidateType(row.cTenantId, row.cTenantProductTypeId, current.cTenantProductTypeId);
      if (!TenantOrg.Same(current.cUnitId, row.cUnitId)) await ValidateUnit(row.cUnitId);
      var code = row.cTenantProductCode.Trim();
      if (!TenantOrg.Same(current.cTenantProductCode, code) &&
          await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantId == current.cTenantId && x.cTenantProductCode == code && x.cTenantProductId != current.cTenantProductId, AbortToken))
         throw new ActionException(DuplicateCode, 409);
      await TenantOrg.Save(() => ctx.ta_TenantProducts.Where(x => x.cTenantProductId == current.cTenantProductId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantProductTypeId, row.cTenantProductTypeId).SetProperty(x => x.cUnitId, row.cUnitId)
         .SetProperty(x => x.cTenantProductCode, code).SetProperty(x => x.cTenantProductName, row.cTenantProductName.Trim())
         .SetProperty(x => x.cTenantProductState, row.cTenantProductState).SetProperty(x => x.cTenantProductOrder, row.cTenantProductOrder)
         .SetProperty(x => x.cTenantProductNote, row.cTenantProductNote).SetProperty(x => x.json_object, row.json_object)
         .SetProperty(x => x.ustamp, TenantOrg.Stamp(current.ustamp)), AbortToken), DuplicateCode);
      Audit(current.cTenantProductId, "tenant.product.changed", "Saved product '" + code + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await Read(current.cTenantProductId);
   }
   #endregion

   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct[]> GetVi_TenantProducts_Children(string tenantId, string? parentId, string? typeId) {
      var parent = TenantOrg.Optional(parentId); var type = TenantOrg.Optional(typeId);
      if (parent is null) await TenantProductSeed.EnsureAsync(ctx, tenantId, Writer, AbortToken);
      var rows = ctx.vi_TenantProducts.AsNoTracking().Where(x => x.cTenantId == tenantId);
      rows = parent is null ? rows.Where(x => x.cTenantProductParentId == null) : rows.Where(x => x.cTenantProductParentId == parent);
      if (type is not null) rows = rows.Where(x => x.cTenantProductTypeId == type);
      return await rows.OrderBy(x => x.cTenantProductOrder).ThenBy(x => x.cTenantProductCode).ThenBy(x => x.cTenantProductId).ToArrayAsync(AbortToken);
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantProduct[]> GetVi_TenantProducts_ByType(string tenantId, string typeId) => ctx.vi_TenantProducts.AsNoTracking()
      .Where(x => x.cTenantId == tenantId && x.cTenantProductTypeId == typeId).OrderBy(x => x.cTenantProductCode).ThenBy(x => x.cTenantProductId).ToArrayAsync(AbortToken);

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantProduct?> GetVi_TenantProduct_ById(string productId) =>
      ctx.vi_TenantProducts.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductId == productId, AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantProductSearchHit[]> GetMeta_TenantProduct_Search(string tenantId, string text, string? typeId) {
      var term = (text ?? "").Trim(); var type = TenantOrg.Optional(typeId);
      if (term.Length == 0) return [];
      var rows = ctx.vi_TenantProducts.AsNoTracking().Where(x => x.cTenantId == tenantId && (x.cTenantProductCode.Contains(term) || x.cTenantProductName.Contains(term)));
      if (type is not null) rows = rows.Where(x => x.cTenantProductTypeId == type);
      var found = await rows.OrderBy(x => x.cTenantProductCode).ThenBy(x => x.cTenantProductId).Take(SearchLimit).ToArrayAsync(AbortToken);
      var parents = await Parents(found.Select(x => x.cTenantProductParentId));
      return [.. found.Select(x => new TenantProductSearchHit { Row = x, ParentIds = Chain(x.cTenantProductParentId, parents) })];
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct[]> GetMeta_TenantProduct_Path(string productId) {
      var row = await ctx.vi_TenantProducts.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductId == productId, AbortToken)
         ?? throw new ActionException("The product was not found.", 404);
      var ids = Chain(row.cTenantProductParentId, await Parents([row.cTenantProductParentId]));
      if (ids.Length == 0) return [row];
      var up = await ctx.vi_TenantProducts.AsNoTracking().Where(x => ids.Contains(x.cTenantProductId)).ToDictionaryAsync(x => x.cTenantProductId, StringComparer.OrdinalIgnoreCase, AbortToken);
      return [.. ids.Where(up.ContainsKey).Select(id => up[id]), row];
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<string[]> PostGetMeta_TenantProduct_CodesExist(string tenantId, string[] codes) {
      var wanted = (codes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      if (wanted.Length == 0) return [];
      return await ctx.ta_TenantProducts.AsNoTracking().Where(x => x.cTenantId == tenantId && wanted.Contains(x.cTenantProductCode))
         .Select(x => x.cTenantProductCode).OrderBy(x => x).ToArrayAsync(AbortToken);
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_AddChildren(TenantProductBatch batch) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var parent = await ctx.ta_TenantProducts.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductId == batch.ParentId && x.cTenantId == batch.TenantId, AbortToken)
         ?? throw new ActionException("The parent product was not found in this tenant.", 404);
      var rows = batch.Rows ?? [];
      if (rows.Length == 0) throw new ActionException("Enter at least one child.", 400);
      if (rows.Any(x => string.IsNullOrWhiteSpace(x.Name))) throw new ActionException("Enter the product name of every child.", 400);
      var typeId = TenantOrg.Optional(batch.TypeId) ?? parent.cTenantProductTypeId;
      var unitId = TenantOrg.Optional(batch.UnitId) ?? parent.cUnitId;
      await ValidateType(batch.TenantId, typeId, null);
      await ValidateUnit(unitId);
      await ValidateCodes(batch.TenantId, [.. rows.Select(x => x.Code)]);
      var order = await LastOrder(batch.TenantId, parent.cTenantProductId);
      var now = DateTime.UtcNow;
      var created = rows.Select(x => new ta_TenantProduct {
         cTenantProductId = Ulid.NewUlid().ToString(), cTenantId = batch.TenantId, cTenantProductTypeId = typeId, cTenantProductParentId = parent.cTenantProductId,
         cUnitId = unitId, cTenantProductCode = x.Code.Trim(), cTenantProductName = x.Name.Trim(), cTenantProductState = ta_TenantProduct.StateActive,
         cTenantProductOrder = ++order, ustamp = now, datestamp = now
      }).ToArray();
      return await SaveCreated(tx, created, "Added product '");
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_CopyChildren(TenantProductCopy copy) {
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      if (!await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantProductId == copy.SourceParentId && x.cTenantId == copy.TenantId, AbortToken))
         throw new ActionException("The source product was not found in this tenant.", 404);
      if (!await ctx.ta_TenantProducts.AnyAsync(x => x.cTenantProductId == copy.TargetParentId && x.cTenantId == copy.TenantId, AbortToken))
         throw new ActionException("The target product was not found in this tenant.", 404);
      var rows = copy.Rows ?? [];
      if (rows.Length == 0) throw new ActionException("Choose at least one child to copy.", 400);
      var ids = rows.Select(x => x.SourceProductId).ToArray();
      var sources = await ctx.ta_TenantProducts.AsNoTracking()
         .Where(x => x.cTenantId == copy.TenantId && x.cTenantProductParentId == copy.SourceParentId && ids.Contains(x.cTenantProductId))
         .ToDictionaryAsync(x => x.cTenantProductId, StringComparer.OrdinalIgnoreCase, AbortToken);
      if (rows.Any(x => !sources.ContainsKey(x.SourceProductId))) throw new ActionException("Only direct children of the source product can be copied.", 400);
      // A copy is a new product, so its type has to be one a new product may take.
      foreach (var typeId in sources.Values.Select(x => x.cTenantProductTypeId).Distinct(StringComparer.OrdinalIgnoreCase)) await ValidateType(copy.TenantId, typeId, null);
      await ValidateCodes(copy.TenantId, [.. rows.Select(x => x.Code)]);
      var order = await LastOrder(copy.TenantId, copy.TargetParentId);
      var now = DateTime.UtcNow;
      var created = rows.Select(x => (Row: x, Source: sources[x.SourceProductId])).Select(x => new ta_TenantProduct {
         cTenantProductId = Ulid.NewUlid().ToString(), cTenantId = copy.TenantId, cTenantProductTypeId = x.Source.cTenantProductTypeId, cTenantProductParentId = copy.TargetParentId,
         cUnitId = x.Source.cUnitId, cTenantProductCode = x.Row.Code.Trim(), cTenantProductName = x.Source.cTenantProductName, cTenantProductNote = x.Source.cTenantProductNote,
         cTenantProductState = ta_TenantProduct.StateActive, cTenantProductOrder = ++order, ustamp = now, datestamp = now
      }).ToArray();
      return await SaveCreated(tx, created, "Copied product '");
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public Task PostMeta_TenantProduct_EnsureSystem(string tenantId) => TenantProductSeed.EnsureAsync(ctx, tenantId, Writer, AbortToken);
   #endregion

   private async Task<vi_TenantProduct[]> SaveCreated(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ta_TenantProduct[] created, string summary) {
      ctx.ta_TenantProducts.AddRange(created);
      await TenantOrg.Save(() => ctx.SaveChangesAsync(AbortToken), DuplicateCode);
      foreach (var row in created) Audit(row.cTenantProductId, "tenant.product.created", summary + row.cTenantProductCode + "'");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      var ids = created.Select(x => x.cTenantProductId).ToArray();
      return await ctx.vi_TenantProducts.AsNoTracking().Where(x => ids.Contains(x.cTenantProductId)).OrderBy(x => x.cTenantProductOrder).ThenBy(x => x.cTenantProductCode).ToArrayAsync(AbortToken);
   }

   private async Task ValidateFields(string tenantId, string code, string name, int state, ta_TenantProduct? current = null) {
      if (current is null && !await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == tenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      if (string.IsNullOrWhiteSpace(code)) throw new ActionException("Enter the product code.", 400);
      if (string.IsNullOrWhiteSpace(name)) throw new ActionException("Enter the product name.", 400);
      if (state is not (ta_TenantProduct.StateActive or ta_TenantProduct.StateInactive)) throw new ActionException("The product state is not valid.", 400);
      // The system product keeps the reserved code it already has.
      if (TenantProductSeed.IsReserved(code) && !TenantOrg.Same(current?.cTenantProductCode, code.Trim())) throw new ActionException("This code is reserved for the system product.", 400);
   }

   // A product takes an active type of its own tenant, or keeps the type it was saved with.
   private async Task ValidateType(string tenantId, string typeId, string? savedTypeId) {
      var type = await ctx.ta_TenantProductTypes.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantProductTypeId == typeId && x.cTenantId == tenantId, AbortToken)
         ?? throw new ActionException("The product type must belong to this tenant.", 400);
      if (type.cTenantProductTypeKind == ProductKind.GenericTransaction) throw new ActionException(SystemType, 400);
      if (type.cTenantProductTypeState != ta_TenantProductType.StateActive && !TenantOrg.Same(typeId, savedTypeId)) throw new ActionException("The product type is inactive.", 400);
   }

   private async Task ValidateUnit(string unitId) {
      if (string.IsNullOrWhiteSpace(unitId) || !await ctx.ta_Units.AnyAsync(x => x.cUnitId == unitId, AbortToken)) throw new ActionException("The unit was not found.", 400);
   }

   // Codes of a batch: none blank or reserved, none repeated, none used in the tenant already.
   private async Task ValidateCodes(string tenantId, string[] codes) {
      if (codes.Any(string.IsNullOrWhiteSpace)) throw new ActionException("Enter the product code of every row.", 400);
      var trimmed = codes.Select(x => x.Trim()).ToArray();
      if (trimmed.Any(TenantProductSeed.IsReserved)) throw new ActionException("This code is reserved for the system product.", 400);
      var repeated = trimmed.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
      if (repeated.Length > 0) throw new ActionException("These codes are repeated: " + string.Join(", ", repeated) + ".", 409);
      var used = await ctx.ta_TenantProducts.AsNoTracking().Where(x => x.cTenantId == tenantId && trimmed.Contains(x.cTenantProductCode)).Select(x => x.cTenantProductCode).ToArrayAsync(AbortToken);
      if (used.Length > 0) throw new ActionException("These codes are already used in this tenant: " + string.Join(", ", used.Order()) + ".", 409);
   }

   // Highest order among the products with this parent; 0 when there is none yet.
   private async Task<int> LastOrder(string tenantId, string? parentId) {
      var siblings = ctx.ta_TenantProducts.AsNoTracking().Where(x => x.cTenantId == tenantId);
      siblings = parentId is null ? siblings.Where(x => x.cTenantProductParentId == null) : siblings.Where(x => x.cTenantProductParentId == parentId);
      return Math.Max(0, await siblings.MaxAsync(x => (int?)x.cTenantProductOrder, AbortToken) ?? 0);
   }

   // Reads the parent link of every product above the given ones, one tree level per query.
   private async Task<Dictionary<string, string?>> Parents(IEnumerable<string?> startIds) {
      var links = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
      var wanted = startIds.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      // The depth limit only guards against a cycle written outside the services.
      for (var depth = 0; wanted.Length > 0 && depth < 64; depth++) {
         var ids = wanted;
         var rows = await ctx.ta_TenantProducts.AsNoTracking().Where(x => ids.Contains(x.cTenantProductId))
            .Select(x => new { x.cTenantProductId, x.cTenantProductParentId }).ToArrayAsync(AbortToken);
         foreach (var row in rows) links[row.cTenantProductId] = row.cTenantProductParentId;
         wanted = [.. rows.Select(x => x.cTenantProductParentId).Where(x => !string.IsNullOrEmpty(x) && !links.ContainsKey(x!)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase)];
      }
      return links;
   }

   // Parent ids from the root down to the given parent.
   private static string[] Chain(string? parentId, Dictionary<string, string?> links) {
      var chain = new List<string>();
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      for (var id = parentId; !string.IsNullOrEmpty(id) && seen.Add(id); id = links.GetValueOrDefault(id)) chain.Add(id);
      chain.Reverse();
      return [.. chain];
   }

   private Task<vi_TenantProduct> Read(string id) => ctx.vi_TenantProducts.AsNoTracking().SingleAsync(x => x.cTenantProductId == id, AbortToken);
   private IAuditWriter? Writer => App is null ? null : GetService<IAuditWriter>();
   private void Audit(string id, string kind, string summary) => Writer?.Use(ctx).AddEntry(new AuditEntryDraft {
      Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = "product"
   });
}
