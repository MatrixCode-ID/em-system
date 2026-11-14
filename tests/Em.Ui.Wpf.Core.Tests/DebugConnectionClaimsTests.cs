using System.Reflection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class DebugConnectionClaimsTests {
   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task StartupLooksUpTheRememberedSessionOnlyOutsideDebug(bool debug) {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic,
         null, [Array.Empty<string>()], null)!;
      var registryPath = @"Software\Em.Tests\DebugConnection\" + Guid.NewGuid().ToString("N");
      app.BaseRegistryKeyFactory = () => Registry.CurrentUser.CreateSubKey(registryPath);
      var local = new ApiConnection { Host = "http://localhost", ProfileName = "Local", IsDebugConnection = true };
      var remote = new ApiConnection { Host = "https://remote.example", ProfileName = "Installed" };
      var storage = new SessionProbe();
      var service = DispatchProxy.Create<ICredentialServices, SessionCallerIdentityTests.CachedSessionService>();
      ((SessionCallerIdentityTests.CachedSessionService)(object)service).Call = (method, _) => {
         Assert.Equal(nameof(ICredentialServices.GetMeta_AllClaimActions), method.Name);
         return Task.FromResult(Array.Empty<ClaimAction>());
      };
      using var provider = new ServiceCollection().AddSingleton(service)
         .AddSingleton<ISessionStorage>(storage).BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);

      try {
         app.AddApiConnection(remote);
         app.RememberSignIn = true;
         app.RememberedProfileName = remote.ProfileName;
         if (debug) {
            typeof(EmApp).GetProperty(nameof(EmApp.IsDebugMode))!.SetValue(app, true);
            typeof(EmApp).GetProperty(nameof(EmApp.DebugConnections))!.SetValue(app, new[] { local });
            typeof(EmApp).GetProperty(nameof(EmApp.DefaultDebugConnection))!.SetValue(app, local);
            app.SetActiveUser(EmApp.CreateDebuggerUser(app));
         }

         await app.OnMainWindowLoadedAsync();
         await app.EnsureClaimsLoadedAsync();

         if (debug) {
            Assert.Same(local, app.ActiveConnection);
            Assert.Null(storage.LoadedProfile);
         }
         else {
            Assert.Equal(remote.ProfileName, storage.LoadedProfile);
         }
      }
      finally {
         Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
      }
   }

   [WpfFact]
   public async Task DebugAppsMenuIsIndependentOfServerClaimsAndReusedAfterSimulation() {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic,
         null, [Array.Empty<string>()], null)!;
      typeof(EmApp).GetProperty(nameof(EmApp.IsDebugMode))!.SetValue(app, true);
      var user = User.Build(app, new vi_User { cUserId = "test-user", cUserAccount = "test-user" });
      app.SetActiveUser(user);
      using var provider = new ServiceCollection().AddSingleton(new ApprovalAccessCatalog(app)).BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);
      typeof(EmApp).GetMethod("InitInternalNavigation", BindingFlags.Static | BindingFlags.NonPublic)!
         .Invoke(null, [app, null]);
      var navigation = new Navigation {
         Name = "test.module", Title = "Test Module", BodyType = BodyType.Of<Em.Ui.Wpf.Navigations.DefaultHomeControl>(),
         Kind = NavigationKind.Manager, IsMenuVisible = true,
         RequireParameter = false, OrderIndex = 1, ModuleName = "test.module",
         RequiredClaim = new ClaimAction { ModuleName = "test.module", Name = "Open" }
      };
      typeof(EmApp).GetMethod("AddNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!
         .Invoke(app, [navigation]);
      var vm = new TabbedMainWindowVm { EmApp = app, IsSignedIn = true };

      app.ActiveConnection = new ApiConnection { Host = "https://remote.example", ProfileName = "Remote" };
      vm.RebuildMenus();
      var debugMenu = vm.AppMenu;
      Assert.Contains(debugMenu, item => item.Navigation == navigation);
      app.ActiveConnection = new ApiConnection { Host = "http://localhost", ProfileName = "Local" };
      await app.EnsureClaimsLoadedAsync();
      await vm.RebuildMenusWhenClaimsLoadedAsync();
      Assert.Same(debugMenu, vm.AppMenu);

      typeof(EmApp).GetProperty(nameof(EmApp.IsSimulatingLogin))!.SetValue(app, true);
      vm.RebuildMenus();
      Assert.DoesNotContain(vm.AppMenu, item => item.Navigation == navigation);
      typeof(EmApp).GetProperty(nameof(EmApp.IsSimulatingLogin))!.SetValue(app, false);
      vm.RebuildMenus();
      Assert.Same(debugMenu, vm.AppMenu);
   }

   [Fact]
   public async Task ExplicitClaimsRefreshUsesTheCurrentServerAndDiscardsTheOldResponse() {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic,
         null, [Array.Empty<string>()], null)!;
      typeof(EmApp).GetProperty(nameof(EmApp.IsDebugMode))!.SetValue(app, true);
      app.SetActiveUser(EmApp.CreateDebuggerUser(app));
      var remote = new ApiConnection { Host = "https://remote.example", ProfileName = "Remote" };
      var local = new ApiConnection { Host = "http://localhost", ProfileName = "Local" };
      var delayedRemote = new TaskCompletionSource<ClaimAction[]>(TaskCreationOptions.RunContinuationsAsynchronously);
      var remoteClaim = new ClaimAction { ModuleName = "remote.module", Name = "Open" };
      var localClaim = new ClaimAction { ModuleName = "local.module", Name = "Open" };
      var probe = new ConnectionProbe(app);
      var requestedConnections = new List<ApiConnection>();
      var service = DispatchProxy.Create<ICredentialServices, SessionCallerIdentityTests.CachedSessionService>();
      ((SessionCallerIdentityTests.CachedSessionService)(object)service).Call = (method, _) => {
         Assert.Equal(nameof(ICredentialServices.GetMeta_AllClaimActions), method.Name);
         var connection = probe.ApiClient!.Connection;
         requestedConnections.Add(connection);
         return connection == remote ? delayedRemote.Task : Task.FromResult(new[] { localClaim });
      };
      using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);

      app.ActiveConnection = remote;
      var remoteRefresh = app.RefreshClaimsAsync();
      app.ActiveConnection = local;
      var localRefresh = app.RefreshClaimsAsync();
      delayedRemote.SetResult([remoteClaim]);
      await Task.WhenAll(remoteRefresh, localRefresh);

      Assert.Equal(new[] { remote, local }, requestedConnections);
      Assert.Contains(localClaim, app.AllClaims);
      Assert.DoesNotContain(remoteClaim, app.AllClaims);
   }

   private sealed class ConnectionProbe(EmApp app) : ServiceWpfBase(app) { }

   private sealed class SessionProbe : ISessionStorage {
      public string? LoadedProfile { get; private set; }

      public SavedSession? Load(string profileName) {
         LoadedProfile = profileName;
         return null;
      }

      public void Save(string profileName, SavedSession session) => throw new NotSupportedException();
      public void Clear(string profileName) => throw new NotSupportedException();
   }
}
