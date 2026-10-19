using System.Reflection;
using System.Runtime.CompilerServices;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Navigations;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core.Tests;

public class BusinessModuleManagerTests
{
   // Loads XAML resources only: no window, layout render, screenshot or live application.
   [WpfFact]
   public void ManagerXamlLoadsWithoutRendering() {
      var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      var view = new BusinessModuleManager(app);
      Assert.IsType<System.Windows.Style>(view.Resources["tenantTreeItemStyle"]);
      Assert.IsType<System.Windows.Controls.TreeView>(view.FindName("tenantTree"));
      Assert.True(view.Vm.IsTenantsTab);
      Assert.Equal(["Tenants", "Departments", "Employees"], view.Vm.Tabs.Select(t => t.Title));
      Assert.False(view.Vm.CanBrowse);
   }

   [Fact]
   public async Task ReloadBuildsTheTreeInDisplayOrder() {
      var service = new StubService();
      var vm = new BusinessModuleManagerVm(service);
      Assert.False(vm.NewTenantCommandAllowed());
      await vm.ReloadAsync();

      Assert.Equal(["Beta", "Alpha"], vm.RootNodes.Select(n => n.DisplayName));
      var alpha = vm.RootNodes[1];
      Assert.Equal(["Alpha Child"], alpha.Children.Select(n => n.DisplayName));
      Assert.Equal(["Alpha Grandchild"], alpha.Children[0].Children.Select(n => n.DisplayName));
      Assert.True(vm.RootNodes[0].IsInactive);
      Assert.True(vm.NewTenantCommandAllowed());
      Assert.False(vm.NewChildCommandAllowed());
      Assert.False(vm.SaveCommandAllowed());
      Assert.False(vm.ToggleStateCommandAllowed());
   }

   [Fact]
   public async Task ParentChoicesExcludeTheTenantAndItsDescendants() {
      var service = new StubService();
      var vm = new BusinessModuleManagerVm(service);
      await vm.ReloadAsync();
      vm.RootNodes[1].IsSelected = true;

      Assert.Equal("Alpha", vm.SelectedNode!.DisplayName);
      Assert.Equal([null, StubService.Beta], vm.ParentOptions.Select(o => o.Id));
      Assert.Equal("(none)", vm.ParentOptions[0].Caption);
      Assert.Equal("Beta (beta.example)", vm.ParentOptions[1].Caption);
      Assert.Null(vm.EditParent!.Id);
      Assert.True(vm.NewChildCommandAllowed());
      Assert.True(vm.ToggleStateCommandAllowed());
      Assert.Equal("Deactivate", vm.ToggleStateCaption);
      Assert.False(vm.SaveCommandAllowed());

      vm.EditName = "Alpha Renamed";
      Assert.True(vm.IsDirty);
      Assert.True(vm.SaveCommandAllowed());
      Assert.False(vm.ToggleStateCommandAllowed());
   }

   [Fact]
   public async Task NewChildDraftIsSavedAndSelectedAfterReload() {
      var service = new StubService();
      var vm = new BusinessModuleManagerVm(service);
      await vm.ReloadAsync();
      vm.RootNodes[1].IsSelected = true;
      vm.NewChildCommand();

      var draft = vm.SelectedNode!;
      Assert.True(draft.IsDraft);
      Assert.Contains(draft, vm.RootNodes[1].Children);
      Assert.Equal(StubService.Alpha, vm.EditParent!.Id);
      Assert.True(vm.SaveCommandAllowed());
      Assert.False(vm.NewChildCommandAllowed());

      vm.EditCode = "alpha.example.new"; vm.EditName = "Alpha New";
      await vm.SaveCommand();
      Assert.Equal("", service.LastNew!.cTenantId);
      Assert.Equal(StubService.Alpha, service.LastNew.cTenantParent_cTenantId);
      Assert.False(vm.SelectedNode!.IsDraft);
      Assert.Equal("Alpha New", vm.SelectedNode.DisplayName);
      Assert.True(vm.SelectedNode.IsSelected);
      Assert.Contains("created", vm.Status);
      Assert.DoesNotContain(vm.AllNodes, n => n.IsDraft);
   }

   [Fact]
   public async Task DecliningToDiscardKeepsTheEditedTenant() {
      var service = new StubService();
      var vm = new BusinessModuleManagerVm(service);
      await vm.ReloadAsync();
      var alpha = vm.RootNodes[1]; var beta = vm.RootNodes[0];
      alpha.IsSelected = true;
      vm.EditName = "Changed";

      var asked = 0;
      vm.ConfirmDiscard = () => { asked++; return false; };
      beta.IsSelected = true;
      Assert.Equal(1, asked);
      Assert.Same(alpha, vm.SelectedNode);
      Assert.True(alpha.IsSelected);
      Assert.False(beta.IsSelected);
      Assert.Equal("Changed", vm.EditName);

      vm.ConfirmDiscard = () => true;
      beta.IsSelected = true;
      Assert.Same(beta, vm.SelectedNode);
      Assert.Equal("Beta", vm.EditName);
   }

   [Fact]
   public async Task ToggleStateSendsTheOppositeStateAndServerErrorsAreShown() {
      var service = new StubService();
      var vm = new BusinessModuleManagerVm(service);
      await vm.ReloadAsync();
      vm.RootNodes[0].IsSelected = true;
      Assert.Equal("Activate", vm.ToggleStateCaption);
      await vm.ToggleStateCommand();
      Assert.Equal(ta_Tenant.StateActive, service.LastUpdate!.cTenantState);
      Assert.Contains("activated", vm.Status);

      service.Fail = new ActionException("The tenant code is already in use.", 409);
      vm.EditCode = "alpha.example";
      await vm.SaveCommand();
      Assert.Equal("The tenant code is already in use.", vm.Status);
      Assert.True(vm.CanBrowse);
   }

   [WpfTheory]
   [InlineData(false, false, false)]
   [InlineData(false, true, true)]
   [InlineData(true, false, true)]
   public void NavigationIsListedForAdministratorsAndClaimHoldersOnly(bool admin, bool hasClaim, bool expected) {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp),
         BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<string>()], null)!;
      var user = User.Build(app, new vi_User { cUserId = "01JUSER", cUserAccount = "user", cUserIsAdmin = admin });
      if (hasClaim) user.AvailableClaims = [ClaimAction.FromKey(Defaults.BusinessModuleName + ":" + ITenantServices.ManagerClaim)];
      typeof(EmApp).GetProperty(nameof(EmApp.ActiveUser))!.GetSetMethod(nonPublic: true)!.Invoke(app, [user]);
      var catalogType = typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Core.ApprovalAccessCatalog")!;
      using var services = new ServiceCollection()
         .AddSingleton(catalogType, Activator.CreateInstance(catalogType, app)!)
         .BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, services);
      typeof(EmApp).GetMethod("InitInternalNavigation", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [app, null]);

      var navigation = app.Navigations.Single(n => n.Name == EmApp.BusinessModuleManagerNavigationName);
      Assert.Equal(expected, app.CanOpen(navigation));
      var tools = (System.Collections.IEnumerable)typeof(EmApp)
         .GetMethod("GetStaticTools", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
      var listed = tools.Cast<object>().Count(tool =>
         (tool.GetType().GetProperty("Navigation")!.GetValue(tool) as Navigation)?.Name == EmApp.BusinessModuleManagerNavigationName);
      Assert.Equal(expected ? 1 : 0, listed);
   }

   private sealed class StubService : ITenantServices
   {
      public const string Alpha = "01ALPHA000000000000000000A";
      public const string Beta = "01BETA0000000000000000000B";
      private const string Child = "01CHILD000000000000000000C";
      private const string Grandchild = "01GRAND000000000000000000G";
      private readonly List<vi_Tenant> _rows = [
         new() { cTenantId = Alpha, cTenantCode = "alpha.example", cTenantName = "Alpha", cTenantOrder = 2 },
         new() { cTenantId = Beta, cTenantCode = "beta.example", cTenantName = "Beta", cTenantOrder = 1, cTenantState = ta_Tenant.StateInactive },
         new() { cTenantId = Child, cTenantCode = "alpha.example.child", cTenantName = "Alpha Child", cTenantParent_cTenantId = Alpha },
         new() { cTenantId = Grandchild, cTenantCode = "alpha.example.grand", cTenantName = "Alpha Grandchild", cTenantParent_cTenantId = Child }
      ];

      public ta_Tenant? LastNew { get; private set; }
      public ta_Tenant? LastUpdate { get; private set; }
      public Exception? Fail { get; set; }

      public Task<vi_Tenant[]> GetVi_Tenants() => Task.FromResult(_rows.ToArray());

      public Task<vi_Tenant> PostGetTa_Tenant_New(ta_Tenant row) {
         if (Fail is { } fail) throw fail;
         LastNew = row;
         var saved = Read(row, Ulid.NewUlid().ToString());
         _rows.Add(saved);
         return Task.FromResult(saved);
      }

      public Task<vi_Tenant> PostGetTa_Tenant_Update(ta_Tenant row) {
         if (Fail is { } fail) throw fail;
         LastUpdate = row;
         var saved = Read(row, row.cTenantId);
         _rows[_rows.FindIndex(r => r.cTenantId == row.cTenantId)] = saved;
         return Task.FromResult(saved);
      }

      private static vi_Tenant Read(ta_Tenant row, string id) => new() {
         cTenantId = id, cTenantParent_cTenantId = row.cTenantParent_cTenantId, cTenantCode = row.cTenantCode,
         cTenantName = row.cTenantName, cTenantOrder = row.cTenantOrder, cTenantState = row.cTenantState,
         cTenantNote = row.cTenantNote, ustamp = DateTime.UtcNow
      };
   }
}
