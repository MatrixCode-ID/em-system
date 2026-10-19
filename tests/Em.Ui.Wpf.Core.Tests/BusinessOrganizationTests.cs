using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public class BusinessOrganizationTests
{
   [Fact]
   public async Task DepartmentTreeOptionsAndUnsavedGuard() {
      var service = new OrgService();
      var vm = new BusinessModuleManagerDeptVm(service) { EmployeeService = service };
      Assert.False(vm.NewDepartmentCommandAllowed());
      vm.SelectedTenant = new() { cTenantId = "T" }; await vm.Loading;
      Assert.True(vm.NewDepartmentCommandAllowed());
      Assert.Single(vm.RootNodes); Assert.Single(vm.RootNodes[0].Children);
      vm.RootNodes[0].IsSelected = true;
      Assert.Equal([null], vm.ParentOptions.Select(x => x.Id));
      Assert.Equal([null, "E"], vm.HeadOptions.Select(x => x.Id));
      Assert.True(vm.CanChooseHead); Assert.False(vm.IsDirty);
      vm.EditName = "changed";
      Assert.True(vm.SaveCommandAllowed()); Assert.False(vm.ToggleStateCommandAllowed());
      vm.ConfirmDiscard = () => false;
      vm.SelectedTenant = new() { cTenantId = "Other" };
      Assert.Equal("T", vm.SelectedTenant!.cTenantId);
      vm.ConfirmDiscard = () => true;
      vm.NewChildCommand(); Assert.False(vm.CanChooseHead); Assert.True(vm.IsDraftSelected);
   }
   [Fact]
   public async Task EmployeePersonModesFilteringAndCommandGuards() {
      var service = new OrgService(); var vm = new BusinessModuleManagerEmpVm(service, service);
      Assert.False(vm.NewEmployeeCommandAllowed());
      vm.SelectedTenant = new() { cTenantId = "T" }; await vm.Loading;
      vm.NewEmployeeCommand();
      Assert.True(vm.ShowExistingPerson); Assert.False(vm.SaveCommandAllowed());
      await vm.SearchPeopleCommand(); Assert.Equal(2, vm.Candidates.Count);
      vm.SelectedCandidate = vm.Candidates[1]; Assert.Null(vm.SelectedCandidate);
      vm.SelectedCandidate = vm.Candidates[0]; Assert.True(vm.SaveCommandAllowed());
      vm.NewPerson = true; Assert.True(vm.ShowNewPerson); Assert.False(vm.ShowExistingPerson);
      vm.ConfirmDiscard = () => false; await vm.RefreshCommand(); Assert.True(vm.IsDraft);
      vm.ConfirmDiscard = () => true; await vm.RefreshCommand();
      vm.Filter = "Person"; Assert.Equal(3, vm.Rows.Count);
      vm.Filter = "root"; Assert.Equal(2, vm.Rows.Count);
      vm.Filter = "nobody"; Assert.Empty(vm.Rows);
   }
   [WpfFact]
   public async Task FilteringDoesNotDiscardAnOpenDirtyEditor() {
      var service = new OrgService(); var vm = new BusinessModuleManagerEmpVm(service, service);
      vm.SelectedTenant = new() { cTenantId = "T" }; await vm.Loading;
      var list = new System.Windows.Controls.ListBox { DataContext = vm, ItemsSource = vm.Rows };
      list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
         new System.Windows.Data.Binding(nameof(vm.SelectedRow)) { Mode = System.Windows.Data.BindingMode.TwoWay });
      vm.SelectedRow = vm.Rows[0]; vm.EditCode = "unsaved";
      vm.Filter = "nobody";
      Assert.Empty(vm.Rows); Assert.Equal("unsaved", vm.EditCode); Assert.True(vm.IsDirty);
      vm.Filter = "";
      Assert.Equal("E", vm.SelectedRow!.cTenantEmpId); Assert.Equal("unsaved", vm.EditCode);
   }
   [Fact]
   public async Task SwitchingTabsAsksBeforeDiscardingTenantChanges() {
      var vm = new BusinessModuleManagerVm(new TenantStub()); await vm.ReloadAsync();
      vm.RootNodes[0].IsSelected = true; vm.EditName = "change";
      vm.ConfirmDiscard = () => false; vm.ActiveTab = vm.Tabs[1]; Assert.True(vm.IsTenantsTab);
      vm.ConfirmDiscard = () => true; vm.ActiveTab = vm.Tabs[1]; Assert.True(vm.IsDepartmentsTab); Assert.False(vm.IsDirty);
   }
   private sealed class TenantStub : ITenantServices {
      public Task<vi_Tenant[]> GetVi_Tenants() => Task.FromResult<vi_Tenant[]>([new() { cTenantId = "T", cTenantName = "Tenant" }]);
      public Task<vi_Tenant> PostGetTa_Tenant_New(ta_Tenant row) => throw new NotSupportedException();
      public Task<vi_Tenant> PostGetTa_Tenant_Update(ta_Tenant row) => throw new NotSupportedException();
   }
   private sealed class OrgService : ITenantDeptServices, ITenantEmpServices {
      public Task<vi_TenantDept[]> GetVi_TenantDepts(string tenantId) => Task.FromResult<vi_TenantDept[]>([
         new() { cTenantDeptId = "D", cTenantId = tenantId, cTenantDeptCode = "root", cTenantDeptName = "Root" },
         new() { cTenantDeptId = "C", cTenantId = tenantId, cTenantDeptCode = "child", cTenantDeptName = "Child", cTenantDeptParent_cTenantDeptId = "D" }]);
      public Task<vi_TenantDept> PostGetTa_TenantDept_New(ta_TenantDept row) => throw new NotSupportedException();
      public Task<vi_TenantDept> PostGetTa_TenantDept_Update(ta_TenantDept row) => throw new NotSupportedException();
      public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) => Task.FromResult<vi_TenantEmp[]>([
         new() { cTenantEmpId = "E", cTenantId = tenantId, cTenantDeptId = "D", cvContactFullName = "Person One", cvTenantDeptName = "Root" },
         new() { cTenantEmpId = "I", cTenantId = tenantId, cTenantDeptId = "D", cvContactFullName = "Person Inactive", cvTenantDeptName = "Root", cTenantEmpState = -1 },
         new() { cTenantEmpId = "X", cTenantId = tenantId, cTenantDeptId = "C", cvContactFullName = "Person Child" }]);
      public Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) => Task.FromResult<TenantEmpContactCandidate[]>([new() { ContactId = "available", FullName = "Available" }, new() { ContactId = "taken", FullName = "Taken", TenantEmpId = "E" }]);
      public Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) => throw new NotSupportedException();
      public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) => throw new NotSupportedException();
   }
}
