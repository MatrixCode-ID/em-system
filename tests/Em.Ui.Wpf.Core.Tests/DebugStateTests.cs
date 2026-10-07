using System.Reflection;
using System.Runtime.CompilerServices;
using Em.Api.Core.Models;
using Em.Shared;

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
   }
}
