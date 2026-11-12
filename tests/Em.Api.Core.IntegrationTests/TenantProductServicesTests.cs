using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.IntegrationTests;

public sealed class TenantProductServicesTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
   protected override string[] AdditionalScripts => [
      "tables/100-business.sql", "tables/120-product.sql", "tables/120-product.sql",
      "views/vi_Unit.sql", "views/vi_TenantProductType.sql", "views/vi_TenantProduct.sql"
   ];

   private TenantProductServices Products() => new(Db());
   private TenantProductTypeServices Types() => new(Db());
   private UnitServices Units() => new(Db());

   private Task<vi_TenantProductType> Type(string tenantId, string code) =>
      Types().PostGetMeta_TenantProductType_Ensure(tenantId, code, code, ProductKind.Goods);

   private Task<vi_TenantProduct> Product(vi_TenantProductType type, string code, string? parent = null, string? unit = null) =>
      Products().PostGetTa_TenantProduct_New(new() {
         cTenantId = type.cTenantId, cTenantProductTypeId = type.cTenantProductTypeId, cTenantProductParentId = parent,
         cTenantProductCode = code, cTenantProductName = code, cUnitId = unit ?? TenantProductSystem.UnitId
      });

   [Fact]
   public async Task SystemProductIsSeededOnceAndItsIdentityIsLocked()
   {
      var tenant = await Tenant("seed");
      var service = Products();
      var tenantId = tenant.cTenantId;
      var type = Assert.Single(await Types().GetVi_TenantProductTypes(tenantId));
      Assert.Equal(ProductKind.GenericTransaction, type.cTenantProductTypeKind);
      var product = Assert.Single(await service.GetVi_TenantProducts_ByType(tenantId, type.cTenantProductTypeId));
      Assert.Equal(TenantProductSystem.Code, product.cTenantProductCode);
      await service.PostMeta_TenantProduct_EnsureSystem(tenantId);
      Assert.Equal(product.cTenantProductId, Assert.Single(await service.GetVi_TenantProducts_Children(tenantId, null, null)).cTenantProductId);
      Assert.Single(await Units().GetVi_Units());
      await Status(400, () => Product(type, "new-system"));
      await Status(400, () => Types().PostGetMeta_TenantProductType_Ensure(tenantId, "OTHER", "Other", ProductKind.GenericTransaction));

      var row = Copy<ta_TenantProduct>(product);
      row.cTenantProductName = "Renamed";
      var saved = await service.PostGetTa_TenantProduct_Update(row);
      Assert.Equal("Renamed", saved.cTenantProductName);
      row = Copy<ta_TenantProduct>(saved);
      row.cTenantProductState = ta_TenantProduct.StateInactive;
      await Status(400, () => service.PostGetTa_TenantProduct_Update(row));
   }

   [Fact]
   public async Task UnitDeletionChecksUsageAndStamp()
   {
      var tenant = await Tenant("unit");
      var type = await Type(tenant.cTenantId, "GOODS");
      var unit = await Units().PostGetTa_Unit_New(new() { cUnitId = "BOX", cUnitDesc = "Box", cUnitSymbol = "bx" });
      var unused = await Units().PostGetTa_Unit_New(new() { cUnitId = "PCS", cUnitDesc = "Pieces", cUnitSymbol = "pc" });
      await Product(type, "box", unit: unit.cUnitId);
      await Status(409, () => Units().PostMeta_Unit_Delete(unit.cUnitId, unit.ustamp));
      await Status(409, () => Units().PostMeta_Unit_Delete(unused.cUnitId, unused.ustamp.AddMinutes(-1)));
      await Units().PostMeta_Unit_Delete(unused.cUnitId, unused.ustamp);
      Assert.DoesNotContain(await Units().GetVi_Units(), x => x.cUnitId == unused.cUnitId);
   }

   [Fact]
   public async Task HierarchyEnforcesTenantCodesKindsAndStamps()
   {
      var tenant = await Tenant("hierarchy");
      var foreign = await Tenant("foreign");
      var type = await Type(tenant.cTenantId, "GOODS");
      var otherType = await Type(tenant.cTenantId, "OTHER");
      var foreignType = await Type(foreign.cTenantId, "GOODS");
      var root = await Product(type, "root");
      var child = await Product(otherType, "child", root.cTenantProductId);
      await Status(409, () => Product(type, "child"));
      await Status(400, () => Product(foreignType, "foreign-child", root.cTenantProductId));
      var changedType = Copy<ta_TenantProductType>(otherType);
      changedType.cTenantProductTypeKind = ProductKind.Service;
      await Status(400, () => Types().PostGetTa_TenantProductType_Update(changedType));
      changedType.cTenantProductTypeKind = ProductKind.Goods;
      changedType.cTenantProductTypeState = ta_TenantProductType.StateInactive;
      await Types().PostGetTa_TenantProductType_Update(changedType);
      await Status(400, () => Product(otherType, "new-inactive-type"));

      var products = Products();
      Assert.Equal(child.cTenantProductId, Assert.Single(await products.GetVi_TenantProducts_Children(tenant.cTenantId, root.cTenantProductId, null)).cTenantProductId);
      Assert.Empty(await products.GetVi_TenantProducts_Children(tenant.cTenantId, root.cTenantProductId, type.cTenantProductTypeId));
      var hit = Assert.Single(await products.GetMeta_TenantProduct_Search(tenant.cTenantId, "child", null));
      Assert.Equal(new[] { root.cTenantProductId }, hit.ParentIds);
      Assert.Equal(new[] { root.cTenantProductId, child.cTenantProductId },
         (await products.GetMeta_TenantProduct_Path(child.cTenantProductId)).Select(row => row.cTenantProductId));

      var edit = Copy<ta_TenantProduct>(child);
      edit.cTenantProductParentId = null;
      await Status(400, () => products.PostGetTa_TenantProduct_Update(edit));
      edit.cTenantProductParentId = root.cTenantProductId;
      edit.cTenantProductState = ta_TenantProduct.StateInactive;
      await products.PostGetTa_TenantProduct_Update(edit);
      await Status(409, () => products.PostGetTa_TenantProduct_Update(edit));
      await Status(409, () => Product(type, "child"));
   }

   [Fact]
   public async Task BatchAndCopyAreAtomicAndAppendInRowOrder()
   {
      var tenant = await Tenant("batch");
      var type = await Type(tenant.cTenantId, "GOODS");
      var source = await Product(type, "source");
      var target = await Product(type, "target");
      var existing = await Product(type, "existing", target.cTenantProductId);
      var products = Products();
      var batch = new TenantProductBatch {
         TenantId = tenant.cTenantId, ParentId = source.cTenantProductId,
         Rows = [new() { Code = "b", Name = "Second letter" }, new() { Code = "existing", Name = "Duplicate" }]
      };
      await Status(409, () => products.PostGetMeta_TenantProduct_AddChildren(batch));
      Assert.Empty(await products.GetVi_TenantProducts_Children(tenant.cTenantId, source.cTenantProductId, null));
      batch.Rows[1].Code = "a";
      var sourceRows = await products.PostGetMeta_TenantProduct_AddChildren(batch);
      Assert.Equal(new[] { "b", "a" }, sourceRows.Select(row => row.cTenantProductCode));
      await Product(type, "grandchild", sourceRows[0].cTenantProductId);

      var copy = new TenantProductCopy {
         TenantId = tenant.cTenantId, SourceParentId = source.cTenantProductId, TargetParentId = target.cTenantProductId,
         Rows = [new() { Code = "copy-b", SourceProductId = sourceRows[0].cTenantProductId },
                 new() { Code = "existing", SourceProductId = sourceRows[1].cTenantProductId }]
      };
      await Status(409, () => products.PostGetMeta_TenantProduct_CopyChildren(copy));
      Assert.Single(await products.GetVi_TenantProducts_Children(tenant.cTenantId, target.cTenantProductId, null));
      copy.Rows[1].Code = "copy-a";
      var copies = await products.PostGetMeta_TenantProduct_CopyChildren(copy);
      Assert.Equal(new[] { "copy-b", "copy-a" }, copies.Select(row => row.cTenantProductCode));
      Assert.Equal(new[] { existing.cTenantProductOrder + 1, existing.cTenantProductOrder + 2 },
         copies.Select(row => row.cTenantProductOrder));
      Assert.All(copies, row => Assert.Equal(0, row.ccTenantProductChildCount));
   }
}
