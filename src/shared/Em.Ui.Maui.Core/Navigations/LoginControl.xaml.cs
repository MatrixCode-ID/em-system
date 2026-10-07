using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The login screen: type the server address, account, and password, then open a session. This is the
   /// screen the application installs when it opens as long as there is no session that can be restored,
   /// and every time a session ends.
   /// </summary>
   public partial class LoginControl : ContentView, INavigationBody
   {
      public LoginControl() {
         InitializeComponent();
         Vm.SignInSucceeded += OnSignInSucceeded;
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public LoginControlVm Vm => (LoginControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.SignInSucceeded -= OnSignInSucceeded;
         return Task.CompletedTask;
      }

      // Signing in succeeded, so this screen has no business left: NavigateHome clears and releases the whole
      // navigation path at once, and the login screen is among what is released there.
      private void OnSignInSucceeded() {
         if (Vm.EmApp is not { } app) return;
         Dispatcher.Dispatch(async () => {
            await app.MainStack.NavigateHome();
            await app.MainStack.Home!.Reload();
         });
      }
   }

   /// <summary>View model <see cref="LoginControl"/>.</summary>
   public class LoginControlVm : MvvmModelBase
   {
      /// <summary>
      /// The only answer for every shape of wrong account/password pair. It does not say which part is wrong -
      /// that is exactly what the login screen must not tell.
      /// </summary>
      public const string InvalidCredentialsMessage = "The account or password is not correct.";

      // The lifetime of requests for a profile created from the address typed here. The number follows the
      // default of a new profile in the desktop client, so the same server does not behave differently just
      // because it is opened from a different device.
      private const int DefaultTimeoutSeconds = 30;

      public LoginControlVm() {
         RegisterCommand(nameof(SignInCommand), SignInCommand, SignInCommandAllowed);
         RegisterCommand(nameof(ToggleThemeCommand), ToggleThemeCommand);
      }

      /// <summary>Raised after the session has really been opened, so this screen can be left.</summary>
      public event Action? SignInSucceeded;

      /// <summary>The application logo that is in force.</summary>
      public ImageSource? LogoImage => EmApp is { } app ? BrandingImages.LoadLogo(app.Branding) : null;

      /// <summary>The brand title of the application.</summary>
      public string BrandTitle => EmApp?.Branding.DisplayTitle ?? string.Empty;

      /// <summary>The brand subtitle of the application.</summary>
      public string BrandTagline => EmApp?.Branding.DisplayTagline ?? string.Empty;

      /// <summary>The copyright text of the application brand.</summary>
      public string BrandCopyright => EmApp?.Branding.DisplayCopyright ?? string.Empty;

      /// <summary>The API server address used to sign in.</summary>
      public string ServerUrl {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>
      /// Whether the server address may not be typed. In debug mode the answer is always yes: the list of
      /// servers is decided at compile time, and what applies is the one currently selected in the account
      /// panel - including when this screen is opened through Simulate Login. Outside debug this address is
      /// the only way to name the server, so it must be typeable.
      /// </summary>
      public bool IsServerLocked => EmApp?.IsDebugMode ?? false;

      /// <summary>The account name that was typed.</summary>
      public string UserName {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>The password that was typed. It is never stored anywhere.</summary>
      public string Password {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>
      /// A note on why the previous session ended, or <c>null</c> when the user signed out by themselves. Not
      /// an error message - which is why it looks different from <see cref="SignInError"/>.
      /// </summary>
      public string? SessionEndedNotice {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSessionEndedNotice)));
      }

      /// <summary><c>true</c> when there is a session-ended note to show.</summary>
      public bool HasSessionEndedNotice => !string.IsNullOrWhiteSpace(SessionEndedNotice);

      /// <summary>The last sign-in failure message, or <c>null</c> when there is none.</summary>
      public string? SignInError {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSignInError)));
      }

      /// <summary><c>true</c> when there is a failure message to show.</summary>
      public bool HasSignInError => !string.IsNullOrWhiteSpace(SignInError);

      /// <summary>
      /// Prepares the fields of the screen again: the server address that applies and the last account name
      /// used to sign in.
      /// </summary>
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;

         app.RetrieveApiConnections();

         // "Stay signed in" is no longer a choice that is offered: the only way out is the sign-out button in the
         // account panel, so its switch is always turned on here. The switch itself still exists and is still
         // written - the session restoration path reads it - there is just nothing left on screen that can turn
         // it off.
         app.RememberSignIn = true;
         UserName = app.RememberedUserName ?? string.Empty;

         // In debug the address follows the connection currently selected. Outside debug, what is filled back in
         // is the address that last really worked for signing in - the only profile that is stored.
         ServerUrl = app.ActiveConnection?.Host
            ?? app.UIConnections.FirstOrDefault()?.Host
            ?? string.Empty;

         NotifyChanged(nameof(IsServerLocked));
         NotifyChanged(nameof(LogoImage));
         NotifyChanged(nameof(BrandTitle));
         NotifyChanged(nameof(BrandTagline));
         NotifyChanged(nameof(BrandCopyright));
         return Task.CompletedTask;
      }

      /// <summary>
      /// Checks the account/password pair against the server and then opens its session. If it succeeds,
      /// <see cref="SignInSucceeded"/> is raised; if not, <see cref="SignInError"/> is filled and the screen
      /// stays where it is - this command never throws an exception to its caller.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is nothing
      // above it to catch it - the process ends.
      public async Task SignInCommand() {
         SignInError = null;

         try {
            WaiterText = "Signing in...";
            IsBusy = InWaiting = true;
            RaiseSignInCommandChanged();

            var app = EmApp!;
            var connection = ResolveConnection(app);

            // The password is checked on the server and nowhere else.
            var services = app.ServiceProvider.GetRequiredService<ICredentialServices>();
            var token = await services.PostGetMeta_SignIn(UserName, Password);

            // Everything from here on runs on a password that was already accepted. Should it fail -
            // loading the account behind the token, say - the second catch below is the right one:
            // what went wrong is not the pair that was typed.
            await app.BeginSessionAsync(token, remember: true);

            // The address has only now been proven usable for signing in, by the lines above, and only now does it
            // deserve to be stored. A debug connection never goes along - it belongs to the code, not to storage.
            if (!app.IsDebugMode) StoreSingleProfile(app, connection);

            // The account name and the profile name are both stored: a stored session is deposited per profile name,
            // so without that name there is nothing that can be restored when the application is opened again.
            app.RememberedUserName = UserName;
            app.RememberedProfileName = connection.ProfileName;

            SignInSucceeded?.Invoke();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            // The one answer the server gives for every way the pair can be wrong. Nothing more is
            // shown and nothing more is kept: which half was wrong is exactly what a login screen
            // must not tell whoever is typing.
            FailSignIn(InvalidCredentialsMessage);
         }
         catch (Exception) {
            // Everything left is the server, the network, or a misconfigured client - none of it the
            // user's doing, so it is worth saying plainly.
            FailSignIn("Cannot sign in right now. The server could not be reached.");
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSignInCommandChanged();
         }
      }

      // The connection whose credentials will be checked. In debug it is already installed and its address is
      // locked, so it is used as-is. Outside debug the address typed by the user decides: its profile is
      // formed here and immediately becomes the active connection, because without an active connection there
      // is no server that can be asked at all.
      private ApiConnection ResolveConnection(EmApp app) {
         if (app.IsDebugMode) {
            return app.ActiveConnection
               ?? throw new InvalidOperationException(
                  "Debug mode has no active API connection to sign in on.");
         }

         var host = ServerUrl.Trim();

         // A stored profile is reused if its address really is the same, so a session already deposited on it is
         // not lost just because its object was created again.
         var connection = app.UIConnections.FirstOrDefault(r =>
            !r.IsDebugConnection && string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase))
            ?? new ApiConnection {
               ProfileName = DeriveProfileName(host),
               Host = host,
               Timeout = DefaultTimeoutSeconds,
               IgnoreSslErrors = false
            };

         app.ActiveConnection = connection;
         return connection;
      }

      // No more than one is ever stored. Other profiles are discarded first - together with the session
      // deposited on them, because a session of another server is of no use anymore - so the server list never
      // piles up and a new server overwrites the old one.
      private static void StoreSingleProfile(EmApp app, ApiConnection connection) {
         app.UIConnections
            .Where(r => !r.IsDebugConnection)
            .Where(r => !string.Equals(r.ProfileName, connection.ProfileName, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .EachOf(app.DeleteApiConnection);

         app.AddApiConnection(connection);
      }

      // The profile name is derived from its address, not typed by the user: it is just a name in storage, and
      // the only thing that must be guaranteed is that the same address never produces two profiles. An
      // address that is not shaped like a URL is used as-is - refusing it here would only block signing in to
      // a server that can in fact be reached.
      private static string DeriveProfileName(string host) =>
         Uri.TryCreate(host, UriKind.Absolute, out var uri)
            ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
            : host;

      /// <summary>Switches the application theme to the one that is not in use.</summary>
      public void ToggleThemeCommand() {
         if (EmApp is not { } app) return;
         app.CurrentTheme = app.IsLightTheme ? ThemeVariant.Dark : ThemeVariant.Light;
      }

      // Emptying the password counts as the user editing the field, and editing either field is what
      // clears the last message - so the message has to be set after the field, never before.
      private void FailSignIn(string message) {
         Password = string.Empty;
         SignInError = message;
      }

      /// <summary>
      /// Sign in may only run after the server address is filled in: the server where the credentials are
      /// checked is that address itself, so without an address there is nothing to contact.
      /// </summary>
      public bool SignInCommandAllowed() =>
         IsNotBusy
         && !string.IsNullOrWhiteSpace(ServerUrl)
         && !string.IsNullOrWhiteSpace(UserName)
         && !string.IsNullOrWhiteSpace(Password);

      private void RaiseSignInCommandChanged() =>
         Commands[nameof(SignInCommand)]?.RaiseCanExecuteChanged();
   }
}
