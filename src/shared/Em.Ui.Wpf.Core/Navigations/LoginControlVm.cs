using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// Result of checking the connection to the server chosen on the login screen. It decides the color of
   /// the indicator on the login screen's status bar, and is not something stored in the database - so its
   /// order is merely the flow a probe goes through.
   /// </summary>
   public enum ServerProbeStatus
   {
      /// <summary>No connection is chosen yet, so nothing is being checked.</summary>
      NotSelected,

      /// <summary>The probe is running.</summary>
      Probing,

      /// <summary>The server answered the probe and its signature was verified.</summary>
      Connected,

      /// <summary>The server did not answer, or its answer did not pass verification.</summary>
      Unreachable
   }

   /// <summary>
   /// View model for <see cref="ILoginScreen"/>: the sign-in process and the choice of light/dark theme.
   /// </summary>
   public class LoginControlVm : MvvmModelBase
   {
      /// <summary>
      /// Creates the login screen view model and registers its commands (sign in and change theme).
      /// </summary>
      public LoginControlVm() {
         RegisterCommand(nameof(SignInCommand), SignInCommand, SignInCommandAllowed);
         RegisterCommand<ThemeVariant>(nameof(ChangeThemeCommand), ChangeThemeCommand);
         RegisterCommand(nameof(ConnectionConfigCommand), ConnectionConfigCommand);
         RegisterCommand(nameof(ExitSimulationCommand), ExitSimulationCommand);
      }

      /// <summary>
      /// Whether this login screen was opened by Simulate Login in a debug build. The way back to the
      /// debugger is then offered under the form.
      /// </summary>
      public bool IsSimulatingLogin => EmApp?.IsSimulatingLogin == true;

      /// <summary>
      /// Whether the "keep me signed in" choice is offered. Hidden during Simulate Login, which stores
      /// nothing about its session.
      /// </summary>
      public bool IsRememberVisible => EmApp?.IsSimulatingLogin != true;

      /// <summary>Leaves Simulate Login and goes back to the debugger account.</summary>
      public async Task ExitSimulationCommand() {
         // Guarded the same way as ChangeThemeCommand: XAML builds this VM before the host injects EmApp.
         if (EmApp is null) return;

         try {
            await EmApp.EndLoginSimulationAsync();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>
      /// Raised after a successful sign in. Used by the application (<see cref="Core.EmApp"/>) to move from
      /// the login screen to the workspace.
      /// </summary>
      public event Action? SignInSucceeded;

      /// <summary>
      /// The account name typed by the user. Filled in by itself when the screen opens if the user chose to
      /// be remembered earlier (<see cref="RememberMe"/>).
      /// </summary>
      // Empty rather than null when nothing has been typed: a text field always has a value, and
      // every reader of this property - the sign in call included - would otherwise have to guard
      // against a null that only ever means "still empty".
      public string UserName {
         get => Get<string>() ?? string.Empty;
         set => Set(value, OnCredentialFieldChanged);
      }

      /// <summary>
      /// The password typed by the user. It lives only while the login screen is open: it is never stored in
      /// the Registry or anywhere else, and is not remembered by <see cref="RememberMe"/>.
      /// </summary>
      public string Password {
         get => Get<string>() ?? string.Empty;
         set => Set(value, OnPasswordChanged);
      }

      /// <summary>
      /// Raised every time <see cref="Password"/> changes, so the view can synchronize its password box. A
      /// password box cannot be bound, so a value changed by the view model itself - e.g. cleared after a
      /// failed sign in - only reaches the screen through this event.
      /// </summary>
      public event Action? PasswordBoxSyncRequested;

      private void OnPasswordChanged(string value) {
         OnCredentialFieldChanged(value);
         PasswordBoxSyncRequested?.Invoke();
      }

      /// <summary>
      /// Why the last sign in failed, or <c>null</c> when there is nothing to report. The red strip above the
      /// form reads this property: filled means it appears, <c>null</c> means it disappears.
      /// </summary>
      public string? SignInError {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// A note on why the previous session ended, or <c>null</c> when there is nothing to say. Kept apart from
      /// <see cref="SignInError"/>, which is red: a session that simply expired is not the user's failure, so
      /// its sentence appears as an ordinary note.
      /// </summary>
      public string? SessionEndedNotice {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>
      /// The technical detail of the last failure - the original exception message - for the tooltip of the
      /// error strip. Always <c>null</c> for a credential failure: there is no detail that may be told there.
      /// </summary>
      public string? SignInErrorDetail {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// The user's "keep me signed in" choice. Its value is stored in the Registry as soon as it changes
      /// (<see cref="Core.EmApp.RememberSignIn"/>), while the account name is only remembered after a sign in
      /// has really been carried out.
      /// </summary>
      public bool RememberMe {
         get => Get<bool>();
         set => Set(value, OnRememberMeChanged);
      }

      // One message for every way the pair can be wrong: no such account, an account that is not
      // allowed in, a credential that was never enrolled or has been revoked, and a password that
      // simply does not match. The server answers all four with the same 401 for the same reason
      // this screen shows one message: telling them apart would turn either one into a way of
      // finding out which accounts exist.
      private const string InvalidCredentialsMessage = "Incorrect username or password. Please try again.";

      /// <summary>
      /// Runs the sign in: hands the account name and password to the server, which checks them and issues a
      /// token if they match. When it succeeds, the host is told through <see cref="SignInSucceeded"/>; if
      /// not, <see cref="SignInError"/> is filled and the screen stays where it is - this command never throws
      /// an exception to its caller.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is no
      // DispatcherUnhandledException handler in this application to catch it - the process ends.
      public async Task SignInCommand() {
         SignInError = null;
         SignInErrorDetail = null;

         try {
            WaiterText = "Signing in...";
            IsBusy = InWaiting = true;
            RaiseSignInCommandChanged();

            // The password is checked on the server and nowhere else. What used to happen here -
            // pulling the stored credential down and comparing the hash locally - meant the hash of
            // every account was there for the asking.
            var services = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();

            var token = await services.PostGetMeta_SignIn(UserName, Password);

            // Everything from here on runs on a password that was already accepted. Should it fail -
            // loading the account behind the token, say - the second catch below is the right one:
            // what went wrong is not the pair that was typed.
            // A simulated login keeps nothing: the Registry belongs to the runs without debug.
            var simulating = EmApp!.IsSimulatingLogin;
            await EmApp!.BeginSessionAsync(token, RememberMe && !simulating);

            // The switch itself is saved the moment it is flipped; the name and the profile are only
            // worth keeping once they have actually been used to sign in. The profile is kept as well
            // as the name because a stored session lives under its own connection: without knowing
            // which one, there is nothing to restore at the next start.
            if (!simulating) {
               EmApp!.RememberedUserName = RememberMe ? UserName : null;
               EmApp!.RememberedProfileName = RememberMe ? SelectedConnection!.ProfileName : null;
            }

            SignInSucceeded?.Invoke();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            // The one answer the server gives for every way the pair can be wrong. Nothing more is
            // shown and nothing more is kept: which half was wrong is exactly what a login screen
            // must not tell whoever is typing.
            FailSignIn(InvalidCredentialsMessage);
         }
         catch (Exception x) {
            // Everything left is the server, the network, or a misconfigured client - none of it the
            // user's doing, so it is worth saying plainly, and worth keeping the detail for.
            FailSignIn("Cannot sign in right now. The server could not be reached.", x.Message);
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSignInCommandChanged();
         }
      }

      // Emptying the password counts as the user editing the field, and editing either field is what
      // clears the last message - so the message has to be set after the field, never before.
      private void FailSignIn(string message, string? detail = null) {
         Password = string.Empty;
         SignInError = message;
         SignInErrorDetail = detail;
      }

      /// <summary>
      /// Sign in may only run after the user has chosen a connection: the server where the credentials are
      /// checked is that connection itself, so without a choice there is nobody to contact. This also applies
      /// in debug mode - there this screen is what prepares the connection for simulating a login.
      /// </summary>
      // Unlike ChangeThemeCommand, a predicate is safe here: it reads this screen's own state, not
      // EmApp, and every property it reads re-raises CanExecuteChanged itself - which UiCommandBase
      // needs, not being tied to CommandManager.RequerySuggested.
      public bool SignInCommandAllowed() =>
         IsNotBusy
         && SelectedConnection is not null
         && !string.IsNullOrWhiteSpace(UserName)
         && !string.IsNullOrWhiteSpace(Password);

      private void RaiseSignInCommandChanged() =>
         Commands[nameof(SignInCommand)]?.RaiseCanExecuteChanged();

      /// <summary>
      /// Turns the "restoring the stored session" state on or off. The login screen is drawn first in a busy
      /// state, rather than being held back as an empty window, because the token exchange it performs takes
      /// network time.
      /// </summary>
      /// <param name="restoring"><c>true</c> while the restoration is running.</param>
      public void SetRestoringSession(bool restoring) {
         WaiterText = "Restoring session...";
         IsBusy = InWaiting = restoring;
         RaiseSignInCommandChanged();
      }

      private void OnCredentialFieldChanged(string value) {
         // Typing is the user's answer to whatever the last attempt said, so the message goes as soon
         // as either field is touched - including the moment a failed attempt empties the password.
         SignInError = null;
         SignInErrorDetail = null;

         // The notice about the previous session goes with it: the moment the user starts typing,
         // they have read it.
         SessionEndedNotice = null;
         RaiseSignInCommandChanged();
      }

      /// <summary>
      /// Changes the application's theme mode to <paramref name="theme"/>. The login screen needs its own
      /// theme choice because the main window's toolbar - with its Color Mode submenu - is hidden while this
      /// screen is shown.
      /// </summary>
      /// <param name="theme">The theme mode to apply.</param>
      public void ChangeThemeCommand(ThemeVariant theme) {
         // Guarded instead of gated behind a can-execute predicate on purpose. XAML builds this VM
         // before the host injects EmApp, and UiCommandBase.CanExecuteChanged is a plain event -
         // it is not tied to CommandManager.RequerySuggested. A predicate would therefore be
         // evaluated once, while EmApp is still null, and leave the buttons disabled for good.
         if (EmApp is null) return;

         EmApp.CurrentTheme = theme;
      }

      /// <summary>
      /// <c>true</c> when the currently active theme mode is the light mode.
      /// </summary>
      public bool LightModeSelected =>
         (EmApp?.CurrentTheme ?? ThemeVariant.Dark) == ThemeVariant.Light;

      /// <summary>
      /// <c>true</c> when the currently active theme mode is the dark mode.
      /// </summary>
      public bool DarkModeSelected =>
         (EmApp?.CurrentTheme ?? ThemeVariant.Dark) == ThemeVariant.Dark;

      /// <summary>
      /// Tells the UI to re-evaluate <see cref="LightModeSelected"/> and <see cref="DarkModeSelected"/>.
      /// Called every time the application's theme changes, wherever the change came from.
      /// </summary>
      public void RefreshThemeState() {
         NotifyChanged(nameof(LightModeSelected));
         NotifyChanged(nameof(DarkModeSelected));
      }

      #region API Connections

      /// <summary>
      /// The list of API connection profiles offered by the login screen. This is the collection of
      /// <see cref="Core.EmApp.UIConnections"/> as-is - not a copy of it - so a profile added or removed
      /// through the Connection Config dialog is immediately visible here. Null-safe because XAML creates this
      /// view model before <see cref="MvvmModelBase.EmApp"/> could be set.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// The profile currently chosen by the user on the login screen. Setting it also makes it the
      /// application's active connection (<see cref="Core.EmApp.ActiveConnection"/>), so the server used after
      /// sign in is the one chosen here.
      /// </summary>
      public ApiConnection? SelectedConnection {
         get => Get<ApiConnection?>();
         set => Set(value, OnSelectedConnectionChanged);
      }

      /// <summary>
      /// The result of the last probe, used by the status bar below the login screen to choose the color of
      /// its indicator.
      /// </summary>
      public ServerProbeStatus ProbeStatus {
         get => Get<ServerProbeStatus>();
         private set => Set(value);
      }

      /// <summary>
      /// The status sentence shown by the status bar: which connection is being checked, and the result.
      /// </summary>
      public string ServerStatusText {
         get => Get<string>() ?? "No server connection selected";
         private set => Set(value);
      }

      /// <summary>
      /// The long description of the last probe - usually its original error message - for the status bar
      /// tooltip. <c>null</c> when there is nothing to explain.
      /// </summary>
      public string? ServerStatusDetail {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// Connects this view model to the application, then tells the UI to re-evaluate the connection list
      /// binding and fill its selection. The notification is required: XAML creates this view model together
      /// with all its bindings before <see cref="MvvmModelBase.EmApp"/> could be set, so without it the
      /// connection list would be read empty too early and never be filled.
      /// </summary>
      /// <param name="app">The application object that owns this view model.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         NotifyChanged(nameof(ApiConnections));
         NotifyChanged(nameof(IsSimulatingLogin));
         NotifyChanged(nameof(IsRememberVisible));
         SyncSelectedConnection();

         // Simulate Login neither reads nor writes the remembered sign-in.
         if (app.IsSimulatingLogin) return;

         // Reading the switch back writes the very same value to the Registry through the property
         // below. That is one redundant write at start up, and it buys the screen a single path in
         // and out of the setting instead of a second one just for loading it.
         RememberMe = app.RememberSignIn;
         if (RememberMe) UserName = app.RememberedUserName ?? string.Empty;
      }

      private void OnRememberMeChanged(bool remember) {
         if (EmApp is null || EmApp.IsSimulatingLogin) return;

         EmApp.RememberSignIn = remember;

         // Switching it off forgets the name and the profile there and then, rather than at the next
         // sign in: the point of the switch is that nothing of the last user is left behind on the
         // machine. The stored session goes with them - without a profile to look under, nothing
         // would ever read it again anyway.
         if (!remember) {
            if (EmApp.RememberedProfileName is { Length: > 0 } profileName) {
               EmApp.ServiceProvider.GetRequiredService<ISessionStorage>().Clear(profileName);
            }

            EmApp.RememberedUserName = null;
            EmApp.RememberedProfileName = null;
         }
      }

      /// <summary>
      /// Aligns the connection choice with the latest content of <see cref="ApiConnections"/>. Priority order:
      /// the profile chosen on this screen earlier, then the connection active in the application
      /// (<see cref="Core.EmApp.ActiveConnection"/>) - so the user's choice survives even if the login screen
      /// is created again - then the default debug connection.
      /// <para>
      /// If none of them matches, the choice is deliberately left empty and the user must choose: sign in is
      /// not allowed before there is a connection (<see cref="SignInCommandAllowed"/>), so choosing the first
      /// profile just like that would only hide a decision that should be the user's.
      /// </para>
      /// </summary>
      public void SyncSelectedConnection() {
         if (EmApp is null) return;

         SelectedConnection =
            FindConnection(_selectedProfileName)
            ?? FindConnection(EmApp.ActiveConnection?.ProfileName)
            ?? EmApp.DefaultDebugConnection;
      }

      /// <summary>
      /// Opens the API connection configuration dialog (<see cref="Dialogs.ConnectionConfig"/>). The login
      /// screen needs its own way into this dialog because the main window's toolbar - with its Tools menu -
      /// is hidden while this screen is shown.
      /// </summary>
      public void ConnectionConfigCommand() {
         // Guarded the same way as ChangeThemeCommand: XAML builds this VM before the host injects
         // EmApp, and a can-execute predicate would be evaluated while it is still null.
         if (EmApp is null) return;

         var dlg = new Dialogs.ConnectionConfig(EmApp) {
            Owner = EmApp.MainWindow
         };
         dlg.ShowDialog();
      }

      // Looking the pick up by profile name rather than by reference: rebuilding the profile list
      // replaces every stored entry with a brand new ApiConnection object, so a reference held from
      // before the rebuild - ActiveConnection included - is no longer in the collection.
      private ApiConnection? FindConnection(string? profileName) =>
         profileName is null
            ? null
            : EmApp!.UIConnections.FirstOrDefault(r => r.ProfileName == profileName);

      // The profile the user last picked on this screen, remembered by name for the reason above.
      private string? _selectedProfileName;

      private void OnSelectedConnectionChanged(ApiConnection? connection) {
         // A rebuild empties the selection for a moment. The remembered name is deliberately left
         // untouched then, so SyncSelectedConnection can put the same profile back afterwards.
         if (connection is not null) _selectedProfileName = connection.ProfileName;

         if (EmApp is not null) EmApp.ActiveConnection = connection;

         // Sign in hangs off this pick, and nothing else re-asks whether it is allowed.
         RaiseSignInCommandChanged();

         // Deliberately not awaited, and deliberately not a registered command: UiCommandAsync
         // refuses to start while it is still running, which would drop exactly the probe the user
         // asked for by picking another profile mid-probe.
         _ = ProbeConnectionAsync(connection);
      }

      // Every probe carries the number it was started with. Only the newest one may report, so a
      // slow answer for a profile the user has already moved off cannot paint over the profile that
      // replaced it - nor can a failure arriving after the next probe already said "connected".
      private int _probeToken;

      private async Task ProbeConnectionAsync(ApiConnection? connection) {
         var token = ++_probeToken;

         if (connection is null) {
            ReportProbe(token, ServerProbeStatus.NotSelected, "No server connection selected");
            return;
         }

         ReportProbe(token, ServerProbeStatus.Probing, $"Probing {connection.Host}…");

         try {
            using var api = connection.CreateApiClient();
            await api.HandshakeAsync();
            ReportProbe(token, ServerProbeStatus.Connected, $"Connected — {connection.Host}");
         }
         catch (Exception x) {
            // The status line is the whole report: an unreachable server while the user is still
            // picking one is an answer, not an accident worth an error dialog. The message itself is
            // kept for the tooltip, where it explains the red dot without shouting.
            ReportProbe(token, ServerProbeStatus.Unreachable, $"Cannot reach {connection.Host}", x.Message);
         }
      }

      private void ReportProbe(int token, ServerProbeStatus status, string text, string? detail = null) {
         if (token != _probeToken) return;

         ProbeStatus = status;
         ServerStatusText = text;
         ServerStatusDetail = detail;
      }

      #endregion
   }
}
