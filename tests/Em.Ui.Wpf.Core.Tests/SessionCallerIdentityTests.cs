using System.Reflection;
using System.Runtime.CompilerServices;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class SessionCallerIdentityTests {
   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task BeginSessionRefreshesCachedServiceClientBeforeLoadingTheProfile(bool rejectProfile) {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic,
         null, [Array.Empty<string>()], null)!;
      typeof(EmApp).GetProperty(nameof(EmApp.IsSimulatingLogin))!.SetValue(app, true);
      var connection = new ApiConnection { Host = "http://127.0.0.1", ProfileName = "test", DebugToken = "test-debug-token" };
      app.ActiveConnection = connection;
      var debugger = User.Build(app, new vi_User { cUserId = Defaults.DebuggerUserId, cUserAccount = Defaults.DebuggerUserAccount });
      app.SetActiveUser(debugger);
      using var cachedClient = app.GetActiveApiClient()!;
      var service = DispatchProxy.Create<ICredentialServices, CachedSessionService>();
      var profileRead = false;
      ((CachedSessionService)(object)service).Call = (method, _) => {
         if (method.Name == nameof(ICredentialServices.GetVi_User_ById)) {
            profileRead = true;
            Assert.True(cachedClient.HasSession);
            Assert.Null(connection.DebugToken);
            Assert.Equal("session-user", cachedClient.ActiveUserId);
            Assert.Null(cachedClient.ActiveUserAccount);
            if (rejectProfile) throw new InvalidOperationException("Profile fixture failed.");
            return Task.FromResult<vi_User?>(new() { cUserId = "session-user", cUserAccount = "session-account" });
         }
         return method.Name switch {
            nameof(ICredentialServices.GetMeta_AllClaimActions) or nameof(ICredentialServices.GetMeta_UserClaims) or
               nameof(ICredentialServices.GetMeta_UserRoleClaims) => Task.FromResult(Array.Empty<ClaimAction>()),
            _ => throw new InvalidOperationException(method.Name)
         };
      };
      using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);
      var token = new TokenResult { cUserId = "session-user", AccessToken = "test-access-token", RefreshToken = "test-refresh-token", ExpiresIn = 60 };
      if (rejectProfile) {
         await Assert.ThrowsAsync<InvalidOperationException>(() => app.BeginSessionAsync(token, remember: false));
         Assert.Equal("test-debug-token", connection.DebugToken);
         Assert.False(cachedClient.HasSession);
         Assert.Same(debugger, app.ActiveUser);
      }
      else {
         await app.BeginSessionAsync(token, remember: false);
         Assert.Equal("session-account", app.ActiveUser?.cUserAccount);
         Assert.Equal("session-account", cachedClient.ActiveUserAccount);
      }
      Assert.True(profileRead);
   }

   public class CachedSessionService : DispatchProxy {
      public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
      protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
   }

   [Theory]
   [InlineData(false, false, Defaults.DebuggerUserId, Defaults.DebuggerUserAccount)]
   [InlineData(true, true, Defaults.DebuggerUserId, Defaults.DebuggerUserAccount)]
   [InlineData(true, false, "session-user", null)]
   public void SessionBootstrapNeverNamesTheOldDebuggerWithoutItsToken(bool hasSession, bool debugToken, string expectedId, string? expectedAccount) {
      var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      typeof(EmApp).GetField("<_apiClients>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
         .SetValue(app, new Dictionary<ApiConnection, ApiClient>());
      var connection = new ApiConnection { Host = "http://127.0.0.1", ProfileName = "test", DebugToken = debugToken ? "test-debug-token" : null };
      app.ActiveConnection = connection;
      app.SetActiveUser(User.Build(app, new vi_User { cUserId = Defaults.DebuggerUserId, cUserAccount = Defaults.DebuggerUserAccount }));
      using var client = app.GetActiveApiClient()!;
      if (hasSession) client.SetSession(new TokenResult { cUserId = "session-user", AccessToken = "test-access-token", RefreshToken = "test-refresh-token", ExpiresIn = 60 });
      Assert.Same(client, app.GetActiveApiClient());
      Assert.Equal(expectedId, client.ActiveUserId);
      Assert.Equal(expectedAccount, client.ActiveUserAccount);
      if (hasSession && !debugToken) {
         app.SetActiveUser(User.Build(app, new vi_User { cUserId = "session-user", cUserAccount = "session-account" }));
         Assert.Equal("session-account", app.GetActiveApiClient()!.ActiveUserAccount);
         client.ClearSession();
         app.SetActiveUser(User.Build(app, new vi_User { cUserId = Defaults.DebuggerUserId, cUserAccount = Defaults.DebuggerUserAccount }));
         Assert.Equal(Defaults.DebuggerUserId, app.GetActiveApiClient()!.ActiveUserId);
      }
   }
}
