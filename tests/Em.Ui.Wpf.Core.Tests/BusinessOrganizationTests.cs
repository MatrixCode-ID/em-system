using Em.Api.Core.Models;
using System.Reflection;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Dialogs;

namespace Em.Ui.Wpf.Core.Tests;

public class BusinessOrganizationTests
{
   [Fact]
   public async Task SearchContactPagesOnServerAndKeepsAppliedSearchAndSelectionRules() {
      var requests = new List<(string? Search, int Page)>();
      var filter = new ContactSearchFilter { State = ContactState.Active, ExcludedContactIds = ["excluded"] };
      var service = ContactService(query => {
         var search = query.Search; var page = query.Page;
         Assert.Same(filter, query.Filter);
         requests.Add((search, page));
         return Task.FromResult(new ContactSearchPage { HasMore = page == 1, Rows = [new() { ContactId = "P", FullName = "Person", ContactState = ContactState.Inactive, UserAccounts = ["account"] }, new() { ContactId = "taken" }] });
      });
      var vm = new ContactPickerDialogVm { Service = service, Filter = filter, Search = "person", SelectionBlockReason = c => c.ContactId == "taken" ? "Unavailable" : null };
      await vm.SearchCommand(); Assert.True(vm.NextPageCommandAllowed()); Assert.False(vm.PreviousPageCommandAllowed());
      vm.Search = "unsent"; await vm.NextPageCommand();
      Assert.Equal(("person", 2), requests[^1]); Assert.False(vm.NextPageCommandAllowed()); Assert.True(vm.PreviousPageCommandAllowed());
      Assert.Equal(ContactState.Inactive, vm.Rows[0].Contact.ContactState); Assert.Equal(["account"], vm.Rows[0].Contact.UserAccounts);
      vm.SelectedRow = vm.Rows[1]; Assert.False(vm.ChooseCommandAllowed());
      vm.SelectedRow = vm.Rows[0]; var accepted = false; vm.RequestClose += result => accepted = result; vm.ChooseCommand(); Assert.True(accepted);
      await vm.SearchCommand(); Assert.Equal(("unsent", 1), requests[^1]); Assert.Null(vm.SelectedContact);
   }
   [Fact]
   public async Task ContactDialogIgnoresLateResultAfterClosing() {
      var pending = new TaskCompletionSource<ContactSearchPage>();
      var vm = new ContactPickerDialogVm { Service = ContactService(_ => pending.Task) };
      var load = vm.SearchCommand(); Assert.False(vm.SearchCommandAllowed()); vm.Release();
      pending.SetResult(new() { Rows = [new() { FullName = "Late" }] }); await load;
      Assert.Empty(vm.Rows); Assert.False(vm.ChooseCommandAllowed());
   }
   [WpfFact]
   public async Task ContactDialogXamlLoadsAndDisplaysContactStateWithoutRendering() {
      var dialog = new ContactPickerDialog();
      var list = Assert.IsType<System.Windows.Controls.ListBox>(dialog.FindName("contactResults"));
      var row = (System.Windows.FrameworkElement)list.ItemTemplate.LoadContent();
      row.DataContext = new ContactPickerRow(new ContactSearchItem { FullName = "Example", ContactState = ContactState.Inactive, UserAccounts = ["example-user"] }, null);
      await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
      Assert.Equal("Inactive", ((System.Windows.Controls.TextBlock)row.FindName("contactStatus")).Text);
   }
   [Fact]
   public async Task EmployeeEditorLoadsSharedDefaultsAndTracksContactEdits() {
      var service = new OrgService(); var vm = new BusinessModuleManagerEmpVm(service, service);
      vm.SelectedTenant = new() { cTenantId = "T" }; await vm.Loading;
      vm.SelectedRow = vm.Rows[0]; await vm.PersonLoading;
      Assert.Equal("P@example.test", vm.EditCommValue); Assert.Equal("P address", vm.EditAddressLocation);
      Assert.False(vm.IsDirty); vm.EditCommValue = "changed@example.test";
      Assert.True(vm.IsDirty); Assert.True(vm.SaveCommandAllowed());
      vm.ConfirmDiscard = () => false; vm.SelectedRow = vm.Rows[1]; Assert.Equal("E", vm.SelectedRow!.cTenantEmpId);
      vm.ConfirmDiscard = () => true; vm.NewEmployeeCommand();
      vm.PickContact = _ => new() { ContactId = "available", FullName = "Available" };
      await vm.SearchPeopleCommand();
      Assert.Equal("available@example.test", vm.EditCommValue);
      vm.NewPerson = true; Assert.Empty(vm.EditCommValue); Assert.Empty(vm.EditAddressLocation);
      vm.EditCommValue = "new@example.test"; vm.EditAddressLocation = "New address";
      await vm.SaveCommand();
      Assert.Equal("", service.LastSave!.Person!.ContactId);
      Assert.Equal("new@example.test", service.LastSave.Person.Comm!.cCommValue);
      Assert.Equal("New address", service.LastSave.Person.Address!.cAddressLocation);
   }
   [Fact]
   public async Task FailedOrLatePersonLoadCannotSaveOrOverwriteAnotherMode() {
      var service = new OrgService(); var vm = new BusinessModuleManagerEmpVm(service, service);
      vm.SelectedTenant = new() { cTenantId = "T" }; await vm.Loading;
      vm.NewEmployeeCommand();
      service.PersonLoader = _ => Task.FromException<TenantEmpPerson>(new ActionException("Contact unavailable", 404));
      vm.SelectedCandidate = new() { ContactId = "available", FullName = "Available" }; await vm.PersonLoading;
      Assert.False(vm.SaveCommandAllowed()); Assert.Contains("Contact unavailable", vm.Status);
      vm.SelectedCandidate = null;
      var pending = new TaskCompletionSource<TenantEmpPerson>(); service.PersonLoader = _ => pending.Task;
      vm.SelectedCandidate = new() { ContactId = "available", FullName = "Available" }; var load = vm.PersonLoading;
      Assert.True(vm.IsPersonLoading); Assert.False(vm.SaveCommandAllowed());
      vm.NewPerson = true; vm.EditCommValue = "new@example.test";
      pending.SetResult(new() { ContactId = "available", Comm = new() { cCommValue = "old@example.test" } });
      await load; Assert.Equal("new@example.test", vm.EditCommValue); Assert.False(vm.IsPersonLoading);
   }
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
      var pickerFilter = vm.BuildContactPickerFilter();
      Assert.True(pickerFilter.PeopleOnly); Assert.Equal(ContactState.Active, pickerFilter.State);
      Assert.Equal(["P", "I"], pickerFilter.ExcludedContactIds);
      Assert.NotNull(vm.GetContactSelectionBlockReason(new() { ContactId = "P" }));
      Assert.True(vm.ShowExistingPerson); Assert.False(vm.SaveCommandAllowed());
      vm.SelectedCandidate = new() { ContactId = "taken", TenantEmpId = "E" }; Assert.Null(vm.SelectedCandidate);
      vm.PickContact = _ => new() { ContactId = "available", FullName = "Available" };
      await vm.SearchPeopleCommand(); Assert.True(vm.SaveCommandAllowed());
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
   private static IContactServices ContactService(Func<ContactSearchQuery, Task<ContactSearchPage>> loader) {
      var service = DispatchProxy.Create<IContactServices, ContactProxy>();
      ((ContactProxy)(object)service).Loader = loader;
      return service;
   }
   public class ContactProxy : DispatchProxy {
      public Func<ContactSearchQuery, Task<ContactSearchPage>> Loader { get; set; } = null!;
      protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IContactServices.PostGetMeta_ContactSearchPage)
         ? Loader((ContactSearchQuery)args![0]!) : throw new NotSupportedException();
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
      public Func<string, Task<TenantEmpPerson>>? PersonLoader { get; set; }
      public TenantEmpSave? LastSave { get; private set; }
      public Task<TenantEmpPerson> GetMeta_TenantEmpPerson(string contactId) => PersonLoader?.Invoke(contactId) ?? Task.FromResult(new TenantEmpPerson { ContactId = contactId, Comm = new() { cCommValue = contactId + "@example.test" }, Address = new() { cAddressLocation = contactId + " address" } });
      public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) => Task.FromResult<vi_TenantEmp[]>([
         new() { cTenantEmpId = "E", cContactId = "P", cTenantId = tenantId, cTenantDeptId = "D", cvContactFullName = "Person One", cvTenantDeptName = "Root" },
         new() { cTenantEmpId = "I", cContactId = "I", cTenantId = tenantId, cTenantDeptId = "D", cvContactFullName = "Person Inactive", cvTenantDeptName = "Root", cTenantEmpState = -1 },
         new() { cTenantEmpId = "X", cTenantId = tenantId, cTenantDeptId = "C", cvContactFullName = "Person Child" }]);
      public Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) => Task.FromResult<TenantEmpContactCandidate[]>([new() { ContactId = "available", FullName = "Available" }, new() { ContactId = "taken", FullName = "Taken", TenantEmpId = "E" }]);
      public Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) { LastSave = save; return Task.FromResult(new vi_TenantEmp { cTenantEmpId = "E" }); }
      public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) => throw new NotSupportedException();
      public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) => throw new NotSupportedException();
      public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) => throw new NotSupportedException();
   }
}
