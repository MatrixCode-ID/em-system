using System.Reflection;
using System.Runtime.CompilerServices;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Navigations;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core.Tests
{
   // EmApp has no public constructor and its debug state is only set while BuildApp runs, so the state
   // is set here the way a headless harness does it: an uninitialised object and its private setters.
   public sealed class DebugStateTests
   {
      private static EmApp NewApp(bool debugMode, bool simulating) {
         var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
         SetPrivate(app, nameof(EmApp.IsDebugMode), debugMode);
         SetPrivate(app, nameof(EmApp.IsSimulatingLogin), simulating);
         return app;
      }

      private static void SetPrivate(EmApp app, string property, object? value) =>
         typeof(EmApp).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(app, [value]);

      private static User NewUser(EmApp app, string id) =>
         User.Build(app, new vi_User { cUserId = id, cUserAccount = id, cUserIsAdmin = true });

      [Theory]
      [InlineData(false, false, false)]
      [InlineData(true, false, true)]
      [InlineData(true, true, false)]
      public void DebugIsActiveOnlyOutsideSimulation(bool debugMode, bool simulating, bool active) {
         Assert.Equal(active, NewApp(debugMode, simulating).IsDebugActive);
      }

      [Fact]
      public void BypassOnlyForTheDebuggerWhileDebugIsActive() {
         var app = NewApp(debugMode: true, simulating: false);

         SetPrivate(app, nameof(EmApp.ActiveUser), NewUser(app, Defaults.DebuggerUserId));
         Assert.True(app.IsDebugBypass);

         // Switch User: an administrator or a plain user follows its own rights, as on the server.
         SetPrivate(app, nameof(EmApp.ActiveUser), NewUser(app, Defaults.AdminUserId));
         Assert.False(app.IsDebugBypass);

         SetPrivate(app, nameof(EmApp.ActiveUser), NewUser(app, "01JUSER"));
         Assert.False(app.IsDebugBypass);

         SetPrivate(app, nameof(EmApp.ActiveUser), null);
         Assert.False(app.IsDebugBypass);
      }

      [Fact]
      public void NoBypassWhileSimulatingEvenForTheDebugger() {
         var app = NewApp(debugMode: true, simulating: true);
         SetPrivate(app, nameof(EmApp.ActiveUser), NewUser(app, Defaults.DebuggerUserId));
         Assert.False(app.IsDebugBypass);
      }

      [Theory]
      [InlineData(true, false, Defaults.DebuggerUserId, false, false)]
      [InlineData(false, false, Defaults.AdminUserId, true, false)]
      [InlineData(true, false, "ordinary-user", false, true)]
      [InlineData(true, true, "ordinary-user", false, true)]
      public void ManagerDefaultsToAllVisibleRequestsForDebuggerAndAdministrators(
         bool debugMode, bool simulating, string userId, bool admin, bool needsMyAction) {
         var app = NewApp(debugMode, simulating);
         SetPrivate(app, nameof(EmApp.ActiveUser), User.Build(app,
            new vi_User { cUserId = userId, cUserAccount = userId, cUserIsAdmin = admin }));
         var vm = new ApprovalManagerVm();
         typeof(ApprovalManagerVm).GetProperty(nameof(vm.EmApp))!.SetValue(vm, app);
         vm.Configure(null);
         Assert.Equal(needsMyAction, vm.WaitingForMe);
      }

      [Theory]
      [InlineData(true)]
      [InlineData(false)]
      public void ManagerPreservesExplicitWaitingFilterForDebugger(bool needsMyAction) {
         var app = NewApp(debugMode: true, simulating: false);
         SetPrivate(app, nameof(EmApp.ActiveUser), NewUser(app, Defaults.DebuggerUserId));
         var vm = new ApprovalManagerVm();
         typeof(ApprovalManagerVm).GetProperty(nameof(vm.EmApp))!.SetValue(vm, app);
         vm.Configure(new ApprovalManagerNavigationPayload { WaitingForMeOnly = needsMyAction });
         Assert.Equal(needsMyAction, vm.WaitingForMe);
      }

      [WpfTheory]
      [InlineData(true, false, Defaults.DebuggerUserId, true, true)]
      [InlineData(true, false, Defaults.DebuggerUserId, false, true)]
      [InlineData(false, false, Defaults.AdminUserId, true, true)]
      [InlineData(true, false, "ordinary-user", false, false)]
      [InlineData(true, true, "ordinary-user", false, false)]
      public void ApprovalManagerToolRespectsDebuggerAndAdministratorAccess(
         bool debugMode, bool simulating, string userId, bool admin, bool expected) {
         var app = (EmApp)Activator.CreateInstance(typeof(EmApp),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<string>()], null)!;
         SetPrivate(app, nameof(EmApp.IsDebugMode), debugMode);
         SetPrivate(app, nameof(EmApp.IsSimulatingLogin), simulating);
         SetPrivate(app, nameof(EmApp.ActiveUser), User.Build(app,
            new vi_User { cUserId = userId, cUserAccount = userId, cUserIsAdmin = admin }));
         var catalogType = typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Core.ApprovalAccessCatalog")!;
         using var services = new ServiceCollection()
            .AddSingleton(catalogType, Activator.CreateInstance(catalogType, app)!)
            .BuildServiceProvider();
         typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(app, services);
         typeof(EmApp).GetMethod("InitInternalNavigation", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [app, null]);
         var tools = (System.Collections.IEnumerable)typeof(EmApp)
            .GetMethod("GetStaticTools", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
         var approvalTools = tools.Cast<object>().Where(tool =>
            (tool.GetType().GetProperty("Navigation")!.GetValue(tool) as Navigation)?.Name ==
            ApprovalManagerNavigationPayload.NavigationName).ToArray();
         Assert.Equal(expected ? 1 : 0, approvalTools.Length);
      }
   }
}
