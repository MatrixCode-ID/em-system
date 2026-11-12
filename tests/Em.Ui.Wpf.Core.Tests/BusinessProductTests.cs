using System.Runtime.CompilerServices;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class BusinessProductTests
{
   [Fact]
   public async Task ProductPagesLoadUnitsTypesAndLazyTree()
   {
      var service = new Catalog();
      var tenant = new vi_Tenant { cTenantId = "T", cTenantName = "Fixture" };
      var units = new BusinessModuleManagerUnitVm(service);
      await units.RefreshCommand();
      Assert.Single(units.Rows);
      units.NewUnitCommand();
      Assert.True(units.CanEditCode);

      var types = new BusinessModuleManagerProductTypeVm(service) { SelectedTenant = tenant };
      await types.Loading;
      Assert.Single(types.Rows);
      Assert.DoesNotContain(ProductKindOption.Choices, x => x.Value == ProductKind.GenericTransaction);

      var products = new BusinessModuleManagerProductVm(service, service, service) { SelectedTenant = tenant };
      await products.Loading;
      var root = Assert.Single(products.RootNodes);
      Assert.False(root.ChildrenLoaded);
      products.SearchText = "S-1";
      await products.SearchCommand();
      await products.OpenHitCommand(Assert.Single(products.SearchResults));
      Assert.Equal("child", products.SelectedNode?.Product.cTenantProductId);
      Assert.True(root.ChildrenLoaded);
   }

   [Fact]
   public async Task BatchAndCopyDialogsBuildOnlySelectedRows()
   {
      var service = new Catalog();
      var add = new AddProductChildrenDialogVm();
      add.Configure(service, service.Target);
      add.Lines = "TAKEN, Existing\nNEW, New product\nNEW2, Another";
      await add.PreviewCommand();
      Assert.False(add.Items[0].IsChecked);
      add.Items[2].IsChecked = false;
      add.AddCommand();
      Assert.Equal("NEW", Assert.Single(add.Result!.Rows).Code);

      var copy = new CopyProductChildrenDialogVm();
      copy.Configure(service, service.Target);
      copy.Source = service.Source;
      await copy.Loading;
      var item = Assert.Single(copy.Items);
      Assert.Equal("T-1", item.Code);
      item.Code = "EDITED";
      item.IsChecked = true;
      copy.CopyCommand();
      Assert.Equal("EDITED", Assert.Single(copy.Result!.Rows).Code);
   }

   [WpfFact]
   public void ProductPagesAndDialogsLoadTheirMarkupWithoutRendering()
   {
      var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      var manager = new BusinessModuleManager(app);
      Assert.IsType<ListBox>(manager.FindName("unitList"));
      Assert.IsType<ListBox>(manager.FindName("productTypeList"));
      Assert.IsType<TreeView>(manager.FindName("productTree"));
      Assert.True(Assert.IsType<TextBox>(manager.FindName("productParent")).IsReadOnly);

      var add = new AddProductChildrenDialog();
      var copy = new CopyProductChildrenDialog();
      try {
         Assert.IsType<TextBox>(add.FindName("linesBox"));
         Assert.IsType<ItemsControl>(add.FindName("previewList"));
         Assert.IsType<ComboBox>(copy.FindName("sourcePicker"));
         Assert.IsType<ItemsControl>(copy.FindName("childList"));
      }
      finally {
         copy.Close();
         add.Close();
      }
   }

   private sealed class Catalog : IUnitServices, ITenantProductTypeServices, ITenantProductServices
   {
      public vi_TenantProduct Source { get; } = new() {
         cTenantId = "T", cTenantProductId = "source", cTenantProductCode = "S",
         cTenantProductTypeId = "goods", cUnitId = "UNIT", ccTenantProductChildCount = 1
      };
      public vi_TenantProduct Target { get; } = new() {
         cTenantId = "T", cTenantProductId = "target", cTenantProductCode = "T",
         cTenantProductTypeId = "goods", cUnitId = "UNIT"
      };
      private vi_TenantProduct Child { get; } = new() {
         cTenantId = "T", cTenantProductId = "child", cTenantProductCode = "S-1", cTenantProductName = "Child",
         cTenantProductTypeId = "goods", cTenantProductParentId = "source", cUnitId = "UNIT"
      };

      public Task<vi_Unit[]> GetVi_Units() => Task.FromResult<vi_Unit[]>([new() { cUnitId = "UNIT", cUnitDesc = "Unit", cUnitSymbol = "u" }]);
      public Task<vi_TenantProductType[]> GetVi_TenantProductTypes(string tenantId) =>
         Task.FromResult<vi_TenantProductType[]>([new() { cTenantProductTypeId = "goods", cTenantProductTypeName = "Goods", cTenantId = "T", cTenantProductTypeKind = ProductKind.Goods }]);
      public Task<vi_TenantProduct[]> GetVi_TenantProducts_Children(string tenantId, string? parentId, string? typeId) =>
         Task.FromResult<vi_TenantProduct[]>(parentId is null ? [Source] : parentId == "source" ? [Child] : []);
      public Task<TenantProductSearchHit[]> GetMeta_TenantProduct_Search(string tenantId, string text, string? typeId) =>
         Task.FromResult<TenantProductSearchHit[]>([new() { Row = Child, ParentIds = ["source"] }]);
      public Task<string[]> PostGetMeta_TenantProduct_CodesExist(string tenantId, string[] codes) =>
         Task.FromResult(codes.Where(x => x == "TAKEN").ToArray());
      public Task<vi_Unit> PostGetTa_Unit_New(ta_Unit row) => throw new NotSupportedException();
      public Task<vi_Unit> PostGetTa_Unit_Update(ta_Unit row) => throw new NotSupportedException();
      public Task PostMeta_Unit_Delete(string unitId, DateTime ustamp) => throw new NotSupportedException();
      public Task<vi_TenantProductType> PostGetTa_TenantProductType_New(ta_TenantProductType row) => throw new NotSupportedException();
      public Task<vi_TenantProductType> PostGetTa_TenantProductType_Update(ta_TenantProductType row) => throw new NotSupportedException();
      public Task<vi_TenantProductType> PostGetMeta_TenantProductType_Ensure(string tenantId, string code, string name, ProductKind kind) => throw new NotSupportedException();
      public Task<vi_TenantProduct> PostGetTa_TenantProduct_New(ta_TenantProduct row) => throw new NotSupportedException();
      public Task<vi_TenantProduct> PostGetTa_TenantProduct_Update(ta_TenantProduct row) => throw new NotSupportedException();
      public Task<vi_TenantProduct[]> GetVi_TenantProducts_ByType(string tenantId, string typeId) => throw new NotSupportedException();
      public Task<vi_TenantProduct?> GetVi_TenantProduct_ById(string productId) => throw new NotSupportedException();
      public Task<vi_TenantProduct[]> GetMeta_TenantProduct_Path(string productId) =>
         Task.FromResult<vi_TenantProduct[]>([Source, Child]);
      public Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_AddChildren(TenantProductBatch batch) => throw new NotSupportedException();
      public Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_CopyChildren(TenantProductCopy copy) => throw new NotSupportedException();
      public Task PostMeta_TenantProduct_EnsureSystem(string tenantId) => throw new NotSupportedException();
   }
}
