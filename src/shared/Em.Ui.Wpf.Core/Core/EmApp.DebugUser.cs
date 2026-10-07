using Em.Api.Core.Models;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      // The debug tokens of the debug connections, put aside while a login is simulated so not a single
      // request of the simulation carries one. Restored when the simulation ends.
      private Dictionary<ApiConnection, string?>? _simulationDebugTokens;

      /// <summary>
      /// Debug only: makes <paramref name="user"/> the active user without its password. Every request then
      /// carries the debug token together with that user's identity, and the server runs it with the user's
      /// real permissions; the client follows the same permissions (see <see cref="IsDebugBypass"/>).
      /// Everything open is closed first, the same way as signing out and back in.
      /// </summary>
      /// <param name="user">The account to act as: the debugger, the built-in administrator, or a stored user.</param>
      /// <returns>
      /// <c>true</c> when the switch happened (or <paramref name="user"/> was already active); <c>false</c> when
      /// a screen with unsaved changes refused to close, in which case nothing changed.
      /// </returns>
      /// <exception cref="InvalidOperationException">When debug is not active.</exception>
      /// <remarks>
      /// When the server refuses the account - a suspended, pending or deleted user, or a disabled built-in
      /// administrator - the debugger account is restored and the server's error is rethrown.
      /// </remarks>
      internal async Task<bool> SwitchDebugUserAsync(User user) {
         ArgumentNullException.ThrowIfNull(user);
         if (!IsDebugActive) {
            throw new InvalidOperationException("Switch User is only available while debug mode is active.");
         }

         if (ActiveUser?.cUserId == user.cUserId) return true;
         if (!await ConfirmLeaveWorkspaceAsync()) return false;

         await MainWindow.ReleaseWorkspaceAsync();
         // The single-page window keeps its path behind home; screens opened for the previous account
         // must not stay reachable through Back.
         if (ApplicationLayout == ApplicationLayout.SinglePage) await MainStack.ReleaseAll();
         SetActiveUser(user);

         try {
            _claimsRefresh = RefreshClaimsAsync();
            await _claimsRefresh;
         }
         catch (Exception) {
            await RestoreDebuggerAsync();
            throw;
         }

         await MainWindow.ShowSignedInAsync();
         return true;
      }

      // The account the server would not act as is dropped for the debugger, which it always accepts,
      // so the developer is never left on an empty workspace.
      private async Task RestoreDebuggerAsync() {
         SetActiveUser(CreateDebuggerUser(this));
         _claimsRefresh = RefreshClaimsAsync();
         try {
            await _claimsRefresh;
         }
         catch (Exception) {
            // The original failure is the one worth reporting; the menus fill in on the next refresh.
         }

         await MainWindow.ShowSignedInAsync();
      }

      /// <summary>
      /// Debug only: starts running this debug build as the application runs without debug. The workspace is
      /// closed, the debug token is taken off every debug connection, and the login screen comes up; from
      /// there signing in, the session, signing out and an expired session all behave as without debug, and
      /// nothing about the session is stored.
      /// </summary>
      /// <returns>
      /// <c>true</c> when the simulation started; <c>false</c> when a screen with unsaved changes refused to
      /// close, or a simulation is already running.
      /// </returns>
      /// <exception cref="InvalidOperationException">When this is not a debug build.</exception>
      internal async Task<bool> BeginLoginSimulationAsync() {
         if (!IsDebugMode) {
            throw new InvalidOperationException("Simulate Login is only available in debug mode.");
         }

         if (IsSimulatingLogin) return false;
         if (!await ConfirmLeaveWorkspaceAsync()) return false;

         await MainWindow.ReleaseWorkspaceAsync();

         // Turned on before anything else, so the session a debug run may have restored at startup is
         // ended without touching what is stored for the runs without debug.
         IsSimulatingLogin = true;
         if (_sessionClient is not null) await EndSessionCoreAsync(notifyServer: false, null, raiseEnded: false);

         _simulationDebugTokens = DebugConnections.ToDictionary(c => c, c => c.DebugToken);
         foreach (var connection in DebugConnections) connection.DebugToken = null;

         SetActiveUser(null);
         DebugStateChanged?.Invoke(this, EventArgs.Empty);

         await ShowLoginScreenAsync(null);
         return true;
      }

      /// <summary>
      /// Ends Simulate Login and goes back to the debugger account without restarting. A simulated session
      /// that is still signed in is signed out of the server first (its failure is ignored), after the open
      /// screens agreed to close.
      /// </summary>
      /// <returns>
      /// <c>true</c> when the debugger is back (or no simulation was running); <c>false</c> when a screen with
      /// unsaved changes refused to close, in which case the simulation goes on.
      /// </returns>
      internal async Task<bool> EndLoginSimulationAsync() {
         if (!IsSimulatingLogin) return true;

         if (ActiveUser is not null && !await ConfirmLeaveWorkspaceAsync()) return false;

         // Still inside the simulation here, so ending the session stores and clears nothing.
         if (_sessionClient is not null) await EndSessionCoreAsync(notifyServer: true, null, raiseEnded: false);
         await MainWindow.ReleaseWorkspaceAsync();
         if (ApplicationLayout == ApplicationLayout.SinglePage) await MainStack.ReleaseAll();

         if (_simulationDebugTokens is { } tokens) {
            foreach (var (connection, token) in tokens) connection.DebugToken = token;
         }

         _simulationDebugTokens = null;
         IsSimulatingLogin = false;

         // The login screen is done with; it goes with the stack once the window switches back.
         if (_loginControl is not null) _loginControl.Vm.SignInSucceeded -= OnSignInSucceeded;
         _loginControl = null;

         SetActiveUser(CreateDebuggerUser(this));
         if (DefaultDebugConnection is { } debugConnection && !ReferenceEquals(ActiveConnection, debugConnection))
            ActiveConnection = debugConnection;

         _claimsRefresh = RefreshClaimsAsync();
         DebugStateChanged?.Invoke(this, EventArgs.Empty);

         await MainWindow.ShowSignedInAsync();
         return true;
      }

      // The same questions closing the main window asks: on the single-page layout the detached
      // windows and the screen being shown, on the multi-tab layout the torn-off windows and every tab.
      // One refusal answers false with nothing closed.
      private async Task<bool> ConfirmLeaveWorkspaceAsync() {
         if (ApplicationLayout == ApplicationLayout.SinglePage) {
            if (!await ConfirmCloseDetachedWindowsAsync()) return false;
            return await MainStack.AskCurrentToLeave();
         }

         if (!await ConfirmCloseTearOffWindowsAsync()) return false;
         return await ConfirmCloseTabsAsync(MainStack);
      }
   }
}
