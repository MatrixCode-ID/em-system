using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      // The login screen currently shown on MainStack, kept so its sign-in can be answered, its theme
      // state refreshed, and a restored session reported on it. Null once somebody is in.
      private ILoginScreen? _loginControl;

      internal ILoginScreen? ActiveLoginControl => _loginControl;

      // The one way to the login screen, on either layout: it becomes the only entry of MainStack, so
      // nothing that was open can still be reached from behind it. Used when the application opens
      // and every time a session ends, so both end up in exactly the same state.
      internal async Task ShowLoginScreenAsync(string? notice) {
         var logon = Navigations.First(r => r.Name == LogonNavigationName);
         if (!await NavigateToRoot(logon)) return;

         // The body was just built by that navigation, so reading it here builds nothing more. It
         // lives as long as its entry: once the stack is cleared after a sign in, the control goes and
         // the next visit gets a clean form.
         if (MainStack.Current?.Body is not ILoginScreen login) return;

         if (!ReferenceEquals(_loginControl, login)) {
            if (_loginControl is not null) _loginControl.Vm.SignInSucceeded -= OnSignInSucceeded;
            login.Vm.SignInSucceeded += OnSignInSucceeded;
            _loginControl = login;
         }

         // Why the previous session ended only needs saying once, and the screen is rebuilt every
         // time it shows, so the notice is handed over here and not kept anywhere else.
         login.Vm.SessionEndedNotice = notice;
         MainWindow.ShowLoginMode();
      }

      // The login screen has nothing more to do once somebody is in; the window releases it together
      // with the rest of the stack when it switches to the signed-in state.
      private Task ShowSignedInAfterLoginAsync() {
         if (_loginControl is not null) _loginControl.Vm.SignInSucceeded -= OnSignInSucceeded;
         _loginControl = null;
         return MainWindow.ShowSignedInAsync();
      }

      // Async void only because SignInSucceeded is an Action: nobody awaits it there, so whatever goes
      // wrong is shown here rather than rethrown on the dispatcher.
      private async void OnSignInSucceeded() {
         try {
            await ShowSignedInAfterLoginAsync();
         }
         catch (Exception x) {
            MainWindow.ShowMboxError(x);
         }
      }

      // Runs on the UI thread (Run dispatches it there). Windows other than the main one go first and
      // without asking: the session they worked for is already gone, so there is nothing left that
      // could be saved. Active debug never opens a session, so it never comes back to the login screen
      // this way either; a simulated login does, exactly as the application does without debug.
      private async Task OnSessionEndedAsync(string? reason) {
         try {
            await MainWindow.ReleaseWorkspaceAsync();
         }
         catch (Exception x) {
            MainWindow.ShowMboxError(x);
         }

         if (IsDebugActive) return;

         try {
            await ShowLoginScreenAsync(reason);
         }
         catch (Exception x) {
            MainWindow.ShowMboxError(x);
         }
      }

      // Called once, on the first Loaded of the main window: the connection list is read, the debug
      // connection (if any) becomes the active one, and a remembered session is resumed.
      internal async Task OnMainWindowLoadedAsync() {
         RetrieveApiConnections();
         if (DefaultDebugConnection is { } debugConnection && ActiveConnection != debugConnection)
            ActiveConnection = debugConnection;

         await RestoreSessionAsync();
      }

      // Resumes the session stored the last time the application was used, so a user who chose to stay
      // signed in does not have to type the password again. Failing is not the user's fault - a
      // revoked or expired session just leaves the login screen as it is, without a red message.
      private async Task RestoreSessionAsync() {
         if (!RememberSignIn) return;
         if (RememberedProfileName is not { Length: > 0 } profileName) return;

         var login = _loginControl?.Vm;
         login?.SetRestoringSession(true);

         try {
            if (await TryRestoreSessionAsync(profileName)) {
               // A restored session is no different from one opened through the form, so the window
               // goes to the same place - and the login screen is released along with the stack.
               // Debug mode is already there.
               if (!IsDebugActive) await ShowSignedInAfterLoginAsync();
               return;
            }
         }
         finally {
            login?.SetRestoringSession(false);
         }

         // The active connection has already moved to the profile that was tried, so the pick on the
         // login screen is brought back in line, and the user carries on against the same server.
         login?.SyncSelectedConnection();
      }

      // The one sign-out path, used by the account button of either layout. Active debug has no session
      // at all - its user is made in BuildApp and never traded for a token - and its account menu does
      // not offer sign out; this refusal is the second layer. Simulate Login is the way to test signing
      // in and out from a debug build: there the session is ended and SessionEnded does the rest, as
      // without debug.
      internal async Task SignOutAsync() {
         if (IsDebugActive) return;

         await EndSessionAsync(notifyServer: true);
      }
   }
}
