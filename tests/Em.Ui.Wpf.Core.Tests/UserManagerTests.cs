using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Converters;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class UserManagerTests
{
   // Loads XAML resources only: no window, layout render, screenshot or live application.
   [WpfFact]
   public void ManagerXamlLoadsWithoutRendering() {
      var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      var view = new UserManager(app);
      Assert.IsType<Style>(view.Resources["columnHeaderButtonStyle"]);
      Assert.IsType<RoleBadgeConverter>(view.Resources["roleBadgeConverter"]);
      Assert.Equal(["All", "Active", "Pending", "Suspended", "Inactive"], view.Vm.QuickStates.Select(o => o.Caption));
   }

   [Fact]
   public async Task ReloadAsksForTheFirstPageAndAttachesRolesInEffect() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();

      var query = server.Queries.Last();
      Assert.Equal(1, query.Page);
      Assert.Equal(200, query.PageSize);
      Assert.Empty(query.Filter.States);
      Assert.Empty(query.Filter.RoleIds);
      Assert.False(query.Filter.IncludeDeleted);
      Assert.Equal(UserListSortField.None, query.SortField);

      Assert.Equal(2, vm.TotalRecords);
      Assert.Equal(["Alpha", "Beta"], vm.Users.Single(u => u.cUserId == "U1").RoleNames);
      Assert.Empty(vm.Users.Single(u => u.cUserId == "U2").RoleNames);
      Assert.True(vm.QuickStates[0].IsSelected);
      Assert.False(vm.HasActiveFilters);
   }

   [Fact]
   public async Task QuickFilterPicksOneStateWithoutAChip() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();

      vm.QuickStates.Single(o => o.Caption == "Pending").IsSelected = true;

      Assert.Equal([UserState.Pending], server.Queries.Last().Filter.States);
      Assert.False(vm.QuickStates[0].IsSelected);
      Assert.False(vm.HasActiveFilters);
   }

   [Fact]
   public async Task SheetAppliesItsDraftAndStaysInStepWithTheSegments() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();

      vm.IsFilterSheetOpen = true;
      Assert.Equal(["Alpha", "Beta"], vm.SheetRoles.Select(o => o.Caption));
      vm.SheetStates.Single(o => o.Value == UserState.Active).IsSelected = true;
      vm.SheetStates.Single(o => o.Value == UserState.Suspended).IsSelected = true;
      vm.SheetRoles.Single(o => o.Caption == "Beta").IsSelected = true;
      vm.SheetAdminOnly = true;
      vm.ApplyFilterCommand();

      var filter = server.Queries.Last().Filter;
      Assert.Equal([UserState.Active, UserState.Suspended], filter.States);
      Assert.Equal(["R2"], filter.RoleIds);
      Assert.True(filter.AdminOnly);
      Assert.False(vm.IsFilterSheetOpen);

      // Two states: no single segment tells the truth, so all are off and a chip says it instead.
      Assert.All(vm.QuickStates, o => Assert.False(o.IsSelected));
      Assert.Equal(["Status: Active, Suspended", "Role: Beta", "Administrators only"],
         vm.ActiveFilters.Select(c => c.Caption));
      Assert.Equal(3, vm.ActiveFilterCount);

      vm.RemoveFilterCommand(vm.ActiveFilters.Single(c => c.Caption == "Role: Beta"));
      Assert.Empty(server.Queries.Last().Filter.RoleIds);

      vm.ClearAllFiltersCommand();
      Assert.Empty(server.Queries.Last().Filter.States);
      Assert.False(server.Queries.Last().Filter.AdminOnly);
      Assert.True(vm.QuickStates[0].IsSelected);
      Assert.False(vm.HasActiveFilters);
   }

   [Fact]
   public async Task ClosingTheSheetWithoutApplyChangesNothing() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();
      var reads = server.Queries.Count;

      vm.IsFilterSheetOpen = true;
      vm.SheetIncludeDeleted = true;
      vm.CloseFilterSheetCommand();

      Assert.Equal(reads, server.Queries.Count);
      vm.IsFilterSheetOpen = true;
      Assert.False(vm.SheetIncludeDeleted);
   }

   [Fact]
   public async Task HeaderClickCyclesAscendingDescendingAndDefault() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();

      vm.SortCommand(UserListSortField.Account);
      Assert.Equal(ListSortDirection.Ascending, vm.Sort[UserListSortField.Account]);
      Assert.Null(vm.Sort[UserListSortField.Email]);
      Assert.Equal((UserListSortField.Account, false), (server.Queries.Last().SortField, server.Queries.Last().SortDescending));

      vm.SortCommand(UserListSortField.Account);
      Assert.Equal(ListSortDirection.Descending, vm.Sort[UserListSortField.Account]);
      Assert.True(server.Queries.Last().SortDescending);

      vm.SortCommand(UserListSortField.Account);
      Assert.Null(vm.Sort[UserListSortField.Account]);
      Assert.Equal(UserListSortField.None, server.Queries.Last().SortField);

      // Another column always starts ascending.
      vm.SortCommand(UserListSortField.Email);
      vm.SortCommand(UserListSortField.State);
      Assert.Equal(ListSortDirection.Ascending, vm.Sort[UserListSortField.State]);
   }

   [Fact]
   public async Task SearchIsSentTrimmedAndTheClearButtonEmptiesIt() {
      var (vm, server) = NewVm();
      await vm.ReloadAsync();
      Assert.False(vm.ClearSearchCommandAllowed());

      vm.SearchText = "  ali ";
      Assert.True(vm.ClearSearchCommandAllowed());
      await vm.ReloadAsync();
      Assert.Equal("ali", server.Queries.Last().Filter.Search);
      Assert.Equal("No user matches the search or filters", vm.EmptyCaption);

      vm.ClearSearchCommand();
      Assert.Equal("", server.Queries.Last().Filter.Search);
      Assert.Equal("No user yet", vm.EmptyCaption);
   }

   [Fact]
   public void RoleBadgesPutAdminFirstShowTwoNamesAndFoldTheRest() {
      var converter = new RoleBadgeConverter();
      string[] names = ["Alpha", "Beta", "Gamma", "Delta"];
      List<RoleBadge> Badges(string[] roles, bool admin) =>
         (List<RoleBadge>)converter.Convert([roles, admin], typeof(object), null, CultureInfo.InvariantCulture);
      // Cast to object: a string[] would otherwise bind to the multi-value overload through array covariance.
      object? ToolTip(string[] roles) => converter.Convert((object)roles, typeof(object), null, CultureInfo.InvariantCulture);

      // Two named chips at most, ADMIN counted, so a narrow cell never needs more than two lines.
      Assert.Equal([new("Alpha", RoleBadgeKind.Role), new("Beta", RoleBadgeKind.Role), new("+2", RoleBadgeKind.Overflow)],
         Badges(names, admin: false));
      Assert.Equal([new("ADMIN", RoleBadgeKind.Admin), new("Alpha", RoleBadgeKind.Role), new("+3", RoleBadgeKind.Overflow)],
         Badges(names, admin: true));
      Assert.Equal([new RoleBadge("ADMIN", RoleBadgeKind.Admin)], Badges([], admin: true));
      Assert.Equal([new RoleBadge("Alpha", RoleBadgeKind.Role)], Badges(["Alpha"], admin: false));
      Assert.Empty(Badges([], admin: false));

      Assert.Equal(string.Join(Environment.NewLine, names), ToolTip(names));
      Assert.Null(ToolTip([]));
   }

   private static (UserManagerVm Vm, FakeServer Server) NewVm() {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic,
         null, [Array.Empty<string>()], null)!;
      var service = DispatchProxy.Create<ICredentialServices, FakeServer>();
      var server = (FakeServer)(object)service;
      var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);

      var vm = new UserManagerVm();
      typeof(UserManagerVm).GetProperty(nameof(UserManagerVm.EmApp))!.SetValue(vm, app);
      return (vm, server);
   }

   public class FakeServer : DispatchProxy
   {
      public List<UserListQuery> Queries { get; } = [];

      protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch {
         nameof(ICredentialServices.GetVi_Roles) => Task.FromResult<vi_Role[]>([
            new() { cRoleId = "R2", cRoleName = "Beta", cRoleState = RoleState.Active },
            new() { cRoleId = "R1", cRoleName = "Alpha", cRoleState = RoleState.Active },
            new() { cRoleId = "R3", cRoleName = "Gone", cRoleState = RoleState.Disabled },
         ]),
         nameof(ICredentialServices.PostGetMeta_UserListPage) => Task.FromResult(Answer((UserListQuery)args![0]!)),
         _ => throw new InvalidOperationException(targetMethod.Name)
      };

      private UserListPage Answer(UserListQuery query) {
         Queries.Add(query);
         return new UserListPage {
            TotalRecords = 2,
            Rows = [
               new() { cUserId = "U1", cUserAccount = "alice", cUserState = UserState.Active },
               new() { cUserId = "U2", cUserAccount = "bob", cUserState = UserState.Pending },
            ],
            Roles = [
               new() { cUserId = "U1", cRoleId = "R1", cRoleName = "Alpha" },
               new() { cUserId = "U1", cRoleId = "R2", cRoleName = "Beta" },
            ]
         };
      }
   }
}
