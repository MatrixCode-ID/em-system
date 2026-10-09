using System.Collections.ObjectModel;
using System.Windows;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Application = System.Windows.Application;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp : IEmAppUi
   {
      private const ThemeVariant DefaultTheme = ThemeVariant.Dark;
      private Dictionary<ApiConnection, ApiClient> _apiClients { get; } = [];
      /// <summary>Raised for active connection changed.</summary>
      public event EventHandler? ActiveConnectionChanged;

      /// <summary>
      /// Raised every time <see cref="ActiveUser"/> changes. Used by UI that shows the identity of the active
      /// user (e.g. the account button in the navigation toolbar) so it can redraw itself - <see cref="EmApp"/>
      /// itself is not a source of bindings with notification, so without this event the display would stay
      /// on the previous user.
      /// </summary>
      public event EventHandler? ActiveUserChanged;

      private EmApp(string[] args) {
         Args = args;
         // Debug mode has no sign-in moment: its user is already made active synchronously inside BuildApp
         // (InitDebugMode), before there is an active connection for any await to ride on. The moment that is
         // really available is when an active connection is attached - that is, when the connection card on the
         // home screen selects DefaultDebugConnection.
         ActiveConnectionChanged += (_, _) => {
            if (IsDebugActive && ActiveConnection is not null) {
               _claimsRefresh = RefreshClaimsAsync();
            }
         };
      }

      #region Properties

      /// <summary>The application layout.</summary>
      public ApplicationLayout ApplicationLayout { get; private set; }

      // How long the single-page host takes to slide one screen out and the next one in; zero means
      // no animation at all, which is also what every multi-tab application gets.
      internal TimeSpan NavigationTransitionTime { get; private set; }

      internal bool EnableFieldAnimation { get; private set; }

      /// <summary>
      /// Settings of the application's brand display (logo, login screen texts, and light/dark themes).
      /// Filled from <see cref="EmAppBuilder.ApplyBranding"/> during <see cref="BuildApp"/>; if the
      /// application never calls it, it stays an empty <see cref="BrandingInfo"/>, so every member
      /// automatically falls back to generic defaults - callers need no null check.
      /// </summary>
      public BrandingInfo Branding { get; private set; } = null!;

      /// <summary>
      /// Whether this build was started with a debug configuration (<c>EmAppBuilder</c> debug connections).
      /// It never changes while the application runs; ask <see cref="IsDebugActive"/> for whether the debug
      /// features are on right now, and <see cref="IsDebugBypass"/> for whether permission checks are skipped.
      /// </summary>
      public bool IsDebugMode { get; private set; } = false;

      /// <summary>
      /// Whether a debug build is currently running as a normal application, see Simulate Login. While it is
      /// on, every debug-only behaviour is off: no debug token is sent, the login screen and sign out work
      /// as they do without debug, and nothing about the session is stored.
      /// </summary>
      public bool IsSimulatingLogin { get; private set; }

      /// <summary>
      /// Whether debug-only features (the debug connection pick, Switch User, debug cards) are shown: a debug
      /// build that is not simulating a normal login.
      /// </summary>
      public bool IsDebugActive => IsDebugMode && !IsSimulatingLogin;

      /// <summary>
      /// Whether the client skips its own permission checks: only while debug is active and the active user is
      /// the debugger account. When a developer switches to another user, the client follows that user's real
      /// permissions, the same way the server does.
      /// </summary>
      public bool IsDebugBypass => IsDebugActive && ActiveUser?.cUserId == Defaults.DebuggerUserId;

      /// <summary>
      /// Raised when <see cref="IsSimulatingLogin"/> changes, so whatever shows or hides a debug feature (the
      /// connection pick, the Tools menu, sign out, the SIMULATED chip) can redraw itself.
      /// </summary>
      public event EventHandler? DebugStateChanged;

      /// <summary>
      /// The password rules in force in this application, read by screens that accept a new password. Filled
      /// from <see cref="EmAppBuilder.UsePasswordPolicy"/> during <see cref="BuildApp"/>; if the application
      /// never calls it, it holds a <see cref="PasswordPolicy"/> with default values - so it is never
      /// <c>null</c> and its users need no null check.
      /// </summary>
      public PasswordPolicy PasswordPolicy { get; private set; } = null!;
      /// <summary>The ui connections.</summary>
      public ObservableCollection<ApiConnection> UIConnections { get; } = [];
      /// <summary>The debug connections.</summary>
      public ApiConnection[] DebugConnections { get; private set; } = [];
      /// <summary>The default debug connection.</summary>
      public ApiConnection? DefaultDebugConnection { get; private set; }

      /// <summary>
      /// Name of the application, used as the main window title and as the name of the Registry subkey where
      /// application settings (e.g. API connections, theme) are stored.
      /// </summary>
      public string ApplicationName { get; private set; } = null!;

      /// <summary>
      /// The command-line arguments the application received at startup.
      /// </summary>
      public string[] Args { get; init; }

      /// <summary>
      /// The application's DI container, alive only during <see cref="BuildApp"/>. Modules register services
      /// through the <see cref="EmAppBuilder"/> callback, not directly into this property; once the
      /// <see cref="ServiceProvider"/> is built, additions to this collection no longer have any effect.
      /// </summary>
      internal IServiceCollection Services { get; } = new ServiceCollection();

      /// <summary>
      /// The WPF <see cref="System.Windows.Application"/> instance, available after <see cref="Run"/> is called.
      /// </summary>
      public Application? App { get; private set; }

      // Kept as the concrete type so it can be disposed when the application stops, while only
      // IServiceProvider is exposed to callers through ServiceProvider.
      private ServiceProvider _serviceProvider = null!;

      /// <inheritdoc />
      /// <remarks>
      /// On the UI side there is no per-request scope like in the API, so what is returned is always the root
      /// provider built from <see cref="Services"/> at the end of <see cref="BuildApp"/>.
      /// </remarks>
      public IServiceProvider ServiceProvider => _serviceProvider;

      /// <summary>The active connection.</summary>
      public ApiConnection? ActiveConnection {
         get;
         set {
            field = value;
            ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
         }
      }

      /// <summary>
      /// The user who is signed in, or <c>null</c> when nobody is. "Nobody signed in" is a normal state that
      /// comes twice - before login and after sign out - not just a temporary state at startup.
      /// </summary>
      public User? ActiveUser { get; private set; }

      // The claims of the client's own built-in screens. Declared in code during BuildApp and frozen from
      // there on, so a claim can never appear - or disappear - while the application is running.
      private readonly List<ClaimAction> _internalClaims = [];

      // The catalogue as the active server declares it, replaced wholesale on every refresh: a
      // different server is a different set of modules, so what the previous one declared must not
      // survive the switch.
      private ClaimAction[] _serverClaims = [];

      // Both halves, merged once per refresh rather than on every read. AllClaims is asked inside
      // XxxCommandAllowed - a path WPF runs over and over - and merging there would allocate a new
      // list each time a button decides whether it is enabled.
      private ClaimAction[] _allClaims = [];

      private bool _internalClaimsSealed;

      /// <summary>
      /// The catalog of every claim known to the application: the claims of the client's built-in screens
      /// merged with the catalog of the active server (loaded by <see cref="RefreshClaimsAsync"/>). When a key
      /// exists on both sides, the client's declaration is used - it is the one that cannot change while the
      /// application runs. It belongs to nobody: it is not cleared on sign out, and is read through the
      /// <c>Claims()</c> extension method on <c>IServices</c> when forming a <see cref="ClaimCollection"/>.
      /// Its name deliberately matches the server's <c>EmApp.AllClaims</c> exactly: two different types in two
      /// different assemblies, one meaning, one name.
      /// </summary>
      public IReadOnlyList<ClaimAction> AllClaims => _allClaims;

      // Internal, and with no public counterpart at all: the client-side catalogue is the engine's
      // own, declared from InitInternalClaims while BuildApp runs. A module declares its claims on
      // the server, and they reach here through RefreshClaimsAsync like every other server claim.
      internal void AddInternalClaim(string key) {
         if (_internalClaimsSealed) {
            throw new InvalidOperationException(
               $"Claim '{key}' cannot be declared after the application has been built.");
         }

         var separatorIndex = key.IndexOf(ClaimAction.Separator);
         if (separatorIndex <= 0 || separatorIndex == key.Length - 1) {
            throw new ArgumentException(
               $"Claim key '{key}' must be written as 'module{ClaimAction.Separator}name'.", nameof(key));
         }

         var claim = ClaimAction.FromKey(key);
         if (_internalClaims.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"Claim '{claim.Key}' is already declared.");
         }

         _internalClaims.Add(claim);
      }

      // Closes the window InitInternalClaims declares through, at the end of BuildApp. Called once
      // whether or not a single claim was declared, because it is also what builds the first
      // catalogue - before any server has been asked, AllClaims is the internal half alone.
      internal void SealInternalClaims() {
         _internalClaimsSealed = true;
         RebuildClaimCatalog();
      }

      private void RebuildClaimCatalog() =>
         _allClaims = [
            .. _internalClaims,
            .. _serverClaims.Where(s =>
               !_internalClaims.Any(i => string.Equals(i.Key, s.Key, StringComparison.OrdinalIgnoreCase)))
         ];

      /// <summary>
      /// Whether <paramref name="navigation"/> may be opened by the active user - used by both the home menu
      /// and <see cref="NavigateTo(Navigation,object?)"/>, so what is hidden and what is refused never
      /// differ. The debugger account skips the rights check (<see cref="IsDebugBypass"/>), but not the module
      /// check: once the server catalog is loaded, a navigation whose module is not declared by the server
      /// (e.g. a test module that is turned off) is refused for anyone. A developer acting as another user
      /// through Switch User gets that user's real rights.
      /// </summary>
      /// <param name="navigation">The navigation about to be opened.</param>
      public bool CanOpen(Navigation navigation) =>
         (!_serverClaimsLoaded || NavigationAccess.IsDeclared(navigation, _allClaims)) &&
         (IsDebugBypass || NavigationAccess.CanOpen(navigation, ActiveUser));

      // Until a server has been asked, module presence is unknown and must not hide anything.
      private bool _serverClaimsLoaded;

      // The application's built-in tools, in the order every surface lists them: the home screen of
      // the single-page layout and the Tools menu of the multi-tab window. A tool that is a
      // navigation the user may not open is left out, by the same rule the application menu follows.
      // Connection Config and the debug tools come first, and the first tool after them starts a new
      // group (StartsGroup), so a developer sees them as soon as the list opens.
      internal IReadOnlyList<StaticTool> GetStaticTools() {
         var tools = new List<StaticTool> {
            new() {
               Name = "tools.apisettings",
               Title = "Connection Config",
               Subtitle = "Configure API Server",
               Description = "Configure the API server connection used by this client.",
               Icon = EFontAwesomeIcon.Solid_Plug.CreateImageSource(System.Windows.Media.Brushes.Gray),
               Invoke = owner => {
                  new Dialogs.ConnectionConfig(this) { Owner = owner }.ShowDialog();
                  return Task.CompletedTask;
               }
            }
         };

         // The two debug tools follow it. Neither is offered while a login is being simulated: the
         // application is then meant to behave exactly as it does without debug.
         if (IsDebugActive) {
            tools.Add(new StaticTool {
               Name = "tools.switchuser",
               Title = "Switch User",
               Subtitle = "Act as another user",
               Description = "Debug only: act as another account without its password to test permissions.",
               Icon = EFontAwesomeIcon.Solid_UserSecret.CreateImageSource(System.Windows.Media.Brushes.Gray),
               Invoke = owner => {
                  new Dialogs.SwitchUserDialog(this) { Owner = owner }.ShowDialog();
                  return Task.CompletedTask;
               }
            });

            tools.Add(new StaticTool {
               Name = "tools.simulatelogin",
               Title = "Simulate Login",
               Subtitle = "Run as without debug",
               Description = "Debug only: sign in with a real account and password, as the application runs without " +
                             "debug. Exit from the login screen or the SIMULATED chip.",
               Icon = EFontAwesomeIcon.Solid_RightToBracket.CreateImageSource(System.Windows.Media.Brushes.Gray),
               Invoke = async owner => {
                  try {
                     await BeginLoginSimulationAsync();
                  }
                  catch (Exception x) {
                     owner.ShowMboxError(x);
                  }
               }
            });
         }

         var groupEnd = tools.Count;

         foreach (var name in (string[])["admin.users", "admin.roles", "admin.cdn", "admin.tasks", "admin.release", "admin.container", "admin.nupak", "admin.smtp"]) {
            var nav = Navigations.Single(r => r.Name == name);
            if (!CanOpen(nav)) continue;

            tools.Add(new StaticTool {
               Name = nav.Name,
               Title = nav.Title,
               Subtitle = nav.Subtitle,
               Description = nav.Description,
               Icon = nav.NavigationIcon,
               Navigation = nav
            });
         }

         if (Navigations.FirstOrDefault(n => n.Name == ApprovalManagerNavigationPayload.NavigationName) is { } approval &&
             Em.Ui.Wpf.Navigations.ApprovalManagerVm.CanOpenManager(this)) {
            tools.Add(new StaticTool { Name = "approval.manager", Title = approval.Title,
               Subtitle = approval.Subtitle, Description = approval.Description, Navigation = approval,
               Icon = EFontAwesomeIcon.Solid_Check.CreateImageSource(System.Windows.Media.Brushes.Gray) });
         }
         if (tools.Count > groupEnd) tools[groupEnd].StartsGroup = true;

         return tools;
      }

      /// <summary>Gets the API client of the active connection, with the active user attached to its requests.</summary>
      public ApiClient? GetActiveApiClient() {
         if (ActiveConnection == null)
            return null;

         if (!_apiClients.TryGetValue(ActiveConnection, out var result)) {
            result = ActiveConnection.CreateApiClient();
            _apiClients.Add(ActiveConnection, result);
         }

         // Set here, not once when the client is created: a client lives longer than one user, and what must
         // accompany every request is the user who is active at that time. Both are filled together - the
         // identity header carries both, and filling only half would send a header naming a different person
         // from the one meant.
         result.ActiveUserId = ActiveUser?.cUserId;
         result.ActiveUserAccount = ActiveUser?.cUserAccount;
         return result;
      }

      /// <summary>
      /// The application's main window, created by <see cref="Run"/>. The same class serves both layouts: in
      /// the multi-tab layout it shows the tabs of <see cref="MainStack"/>, in the single-page layout it shows
      /// the navigation host of <see cref="MainStack"/>. Closing it ends the application.
      /// </summary>
      public TabbedMainWindow MainWindow { get; private set; } = null!;

      /// <summary>
      /// The application's base Registry key (<c>HKCU\{ApplicationName}</c>), created automatically if it
      /// does not exist.
      /// </summary>
      public RegistryKey BaseRegKey =>
         Registry.CurrentUser.OpenSubKey(ApplicationName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
         Registry.CurrentUser.CreateSubKey(ApplicationName);

      // Internal, not private: the session store rides on the same subkey, and two places that may type its
      // name themselves means one typo would leave a session stored in a Registry branch nobody ever reads.
      internal const string ApiConnectionsSubKey = "Api Connections";

      /// <summary>
      /// The Registry key where the list of API connections (<see cref="ApiConnection"/>) is stored, under
      /// <see cref="BaseRegKey"/>.
      /// </summary>
      private RegistryKey ApiConnectionsRegKey =>
         BaseRegKey.OpenSubKey(ApiConnectionsSubKey, writable: true) ??
         BaseRegKey.CreateSubKey(ApiConnectionsSubKey);

      /// <summary>
      /// Whether the user ticked "keep me signed in" on the login screen (stored in the Registry, so it
      /// carries across sessions and only applies to the Windows user who is signed in).
      /// <para>
      /// Only the choice is stored. Credentials are never written to the Registry - the only thing remembered
      /// besides this flag is <see cref="RememberedUserName"/>.
      /// </para>
      /// </summary>
      public bool RememberSignIn {
         get {
            using var key = BaseRegKey;
            return (int)(key.GetValue(nameof(RememberSignIn)) ?? 0) != 0;
         }
         set {
            using var key = BaseRegKey;
            key.SetValue(nameof(RememberSignIn), value ? 1 : 0, RegistryValueKind.DWord);
         }
      }

      /// <summary>
      /// The account name last used to sign in, so the login screen can fill it back in while
      /// <see cref="RememberSignIn"/> is on. Set to <c>null</c> (or empty text) to forget it - the value is
      /// removed from the Registry directly, not stored as an empty string.
      /// </summary>
      public string? RememberedUserName {
         get {
            using var key = BaseRegKey;
            return key.GetValue(nameof(RememberedUserName)) as string;
         }
         set {
            using var key = BaseRegKey;
            if (string.IsNullOrWhiteSpace(value)) {
               key.DeleteValue(nameof(RememberedUserName), throwOnMissingValue: false);
               return;
            }

            key.SetValue(nameof(RememberedUserName), value, RegistryValueKind.String);
         }
      }

      /// <summary>
      /// The name of the connection profile last used to sign in, a pair with
      /// <see cref="RememberedUserName"/>. Needed because a stored session lives in the subkey of its own
      /// profile: without knowing which profile, nothing can be restored when the application is opened again.
      /// Set to <c>null</c> (or empty text) to forget it.
      /// </summary>
      public string? RememberedProfileName {
         get {
            using var key = BaseRegKey;
            return key.GetValue(nameof(RememberedProfileName)) as string;
         }
         set {
            using var key = BaseRegKey;
            if (string.IsNullOrWhiteSpace(value)) {
               key.DeleteValue(nameof(RememberedProfileName), throwOnMissingValue: false);
               return;
            }

            key.SetValue(nameof(RememberedProfileName), value, RegistryValueKind.String);
         }
      }

      /// <summary>
      /// The theme mode that is active, light or dark (stored in the Registry, so it carries across
      /// sessions). Setting this value immediately applies the new theme to all windows and raises
      /// <see cref="ThemeChanged"/>. Default: <see cref="ThemeVariant.Dark"/>.
      /// </summary>
      public ThemeVariant CurrentTheme {
         get {
            using var key = BaseRegKey;
            return ParseThemeVariant(key.GetValue(nameof(CurrentTheme)) as string);
         }
         set {
            if (CurrentTheme == value)
               return;

            using (var key = BaseRegKey) {
               key.SetValue(nameof(CurrentTheme), value.ToString(), RegistryValueKind.String);
            }

            ApplyTheme();
         }
      }

      /// <summary>
      /// The theme in use: <see cref="BrandingInfo.LightTheme"/> or <see cref="BrandingInfo.DarkTheme"/> of
      /// <see cref="Branding"/>, according to <see cref="CurrentTheme"/>.
      /// </summary>
      public ThemeBase ActiveTheme => Branding.GetTheme(CurrentTheme);

      /// <summary>
      /// Raised every time the theme has finished being applied again because <see cref="CurrentTheme"/>
      /// changed. Used by screens that draw some of their colors from code, not from theme resources, so they
      /// can redraw themselves.
      /// </summary>
      public event EventHandler? ThemeChanged;

      // Older builds stored the name of the control library's theme, the MAUI client stores the bare variant
      // name; both still read back as the variant they meant. Anything else falls to the default.
      private static ThemeVariant ParseThemeVariant(string? value) => value switch {
         "Light" or "Win11Light" => ThemeVariant.Light,
         "Dark" or "Win11Dark" => ThemeVariant.Dark,
         _ => DefaultTheme
      };

      // One path for startup and for every switch afterwards, in a fixed order: the engine's own
      // tokens first, so a third-party applier that reads them sees the new values, then the windows
      // that paint part of themselves from code, then whoever listens.
      private void ApplyTheme() {
         var theme = ActiveTheme;

         if (App is not null) ThemeResources.Apply(App.Resources, theme);

         foreach (var applier in ServiceProvider.GetServices<IThemeApplier>()) {
            applier.Apply(theme);
         }

         MainWindow?.OnThemeChanged();
         ThemeChanged?.Invoke(this, EventArgs.Empty);
      }

      #endregion

      #region Methods

      /// <summary>
      /// Sets the user who is signed in, or <c>null</c> to clear it.
      /// </summary>
      /// <param name="user">The user who signed in, or <c>null</c> when nobody is signed in anymore.</param>
      public void SetActiveUser(User? user) {
         ActiveUser = user;

         // Clients that were already created are updated here; those created after this get the value through
         // GetActiveApiClient. Both are filled and cleared together.
         foreach (var client in _apiClients.Values) {
            client.ActiveUserId = user?.cUserId;
            client.ActiveUserAccount = user?.cUserAccount;
         }

         ActiveUserChanged?.Invoke(this, EventArgs.Empty);
      }

      private void SyncBusinessTaskTracker() =>
         ServiceProvider.GetRequiredService<BusinessTaskTracker>().SetUser(ActiveUser?.cUserId);

      /// <summary>
      /// Runs the WPF application: creates the <see cref="System.Windows.Application"/>, applies
      /// <see cref="CurrentTheme"/>, then shows <see cref="MainWindow"/>. This method blocks while the
      /// application runs (following the WPF <c>Application.Run</c> lifecycle).
      /// </summary>
      public void Run() {
         App = new Application {
            // Detached windows and windows born from a dragged-out tab are deliberately not made owned windows so
            // they can sit behind the main window. Without this, closing the main window would not end the
            // application as long as such a window still exists.
            ShutdownMode = ShutdownMode.OnMainWindowClose
         };
         // Themed scroll bars for every screen, including those that merge no engine style: the stock
         // ones are painted from system colours and stay light on the dark theme.
         App.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/ScrollBars.xaml")
         });
         // Before the main window exists, so it is created against the right tokens already; the
         // MainWindow?.OnThemeChanged() inside is a no-op at this point.
         ApplyTheme();
         // Assigned before InitLayout, so anything the initialisation reaches (theme changes, dialogs)
         // can already find the window through EmApp.MainWindow.
         MainWindow = new TabbedMainWindow(this);
         MainWindow.InitLayout();

         // One way home for both causes of a session ending - the user signing out by themselves and a session
         // dying midway. Going through the dispatcher is required: this event can arrive from any thread,
         // because it is born in the middle of an HTTP request that failed to renew.
         SessionEnded += (_, e) => App.Dispatcher.InvokeAsync(() => OnSessionEndedAsync(e.Reason));

         // The task hub follows whoever is signed in. Its polling continues on the thread that starts
         // it, so it is always started from the dispatcher; the direct call covers the debug user, who
         // is already signed in before this point.
         ActiveUserChanged += (_, _) => App.Dispatcher.InvokeAsync(SyncBusinessTaskTracker);
         App.Dispatcher.InvokeAsync(SyncBusinessTaskTracker);

         // Debug mode has had its user since BuildApp (see InitDebugMode), so there is nothing to ask and the
         // application opens straight away. Otherwise nobody is there yet: the login screen is installed.
         // The first screen is installed through the dispatcher, not directly here: installing it is
         // asynchronous, while the message loop that runs its continuation only comes alive in App.Run below.
         // Normal priority makes it still finish before the window handles Loaded, so what the user sees first
         // does not change.
         App.Dispatcher.InvokeAsync(ShowFirstScreenAsync);

         App.Run(MainWindow);
      }

      // There is nobody left above this method - the dispatcher that runs it does not wait for the result,
      // and this application has no DispatcherUnhandledException - so the exception is caught and shown here.
      private async Task ShowFirstScreenAsync() {
         try {
            if (IsDebugMode) await MainWindow.ShowSignedInAsync();
            else await ShowLoginScreenAsync(null);
         }
         catch (Exception x) {
            MainWindow.ShowMboxError(x);
         }
      }

      /// <summary>
      /// Rebuilds <see cref="UIConnections"/>: <see cref="DebugConnections"/> first (if any), followed by all
      /// API connection profiles stored in the Registry.
      /// </summary>
      public void RetrieveApiConnections() {
         using var container = ApiConnectionsRegKey;
         UIConnections.Clear();

         // Debug connections do not come from the Registry. Their instances belong to DebugConnections and are
         // deliberately reused every time the list is rebuilt, so the references stay stable.
         DebugConnections.EachOf(UIConnections.Add);

         container.GetSubKeyNames()
            .Select(name => {
               using var key = container.OpenSubKey(name);
               if (key is null) return null;

               return new ApiConnection {
                  ProfileName = key.GetValue(nameof(ApiConnection.ProfileName)) as string ?? name,
                  Host = key.GetValue(nameof(ApiConnection.Host)) as string ?? string.Empty,
                  Timeout = (int)(key.GetValue(nameof(ApiConnection.Timeout)) ?? 0),
                  IgnoreSslErrors = (int)(key.GetValue(nameof(ApiConnection.IgnoreSslErrors)) ?? 0) != 0,
               };
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .EachOf(UIConnections.Add);
      }

      /// <summary>
      /// Stores a new API connection profile in the Registry, and also adds it to
      /// <see cref="UIConnections"/> so the list shown by the UI is updated too.
      /// </summary>
      /// <param name="apiConnection">The connection data to store.</param>
      /// <exception cref="InvalidOperationException">When the connection comes from the debug configuration.</exception>
      public void AddApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);
         using var container = ApiConnectionsRegKey;
         using var key = container.CreateSubKey(apiConnection.ProfileName);

         key.SetValue(nameof(ApiConnection.ProfileName), apiConnection.ProfileName, RegistryValueKind.String);
         key.SetValue(nameof(ApiConnection.Host), apiConnection.Host, RegistryValueKind.String);
         key.SetValue(nameof(ApiConnection.Timeout), apiConnection.Timeout, RegistryValueKind.DWord);
         key.SetValue(nameof(ApiConnection.IgnoreSslErrors), apiConnection.IgnoreSslErrors ? 1 : 0,
            RegistryValueKind.DWord);

         // The same object may come in through UpdateApiConnection (edited in place, so it is already in the
         // collection); Contains uses reference equality because ApiConnection does not override Equals.
         if (!UIConnections.Contains(apiConnection)) {
            UIConnections.Add(apiConnection);
         }
      }

      /// <summary>
      /// Updates an API connection profile. If the profile name changed, the old entry under the previous name
      /// is removed first before the new entry is stored.
      /// </summary>
      /// <param name="originalProfileName">The profile name before the change.</param>
      /// <param name="apiConnection">The latest connection data.</param>
      public void UpdateApiConnection(string originalProfileName, ApiConnection apiConnection) {
         if (!string.Equals(originalProfileName, apiConnection.ProfileName, StringComparison.OrdinalIgnoreCase))
            DeleteApiConnection(originalProfileName);

         AddApiConnection(apiConnection);
      }

      /// <summary>
      /// Deletes an API connection profile by its object, from the Registry as well as from
      /// <see cref="UIConnections"/>.
      /// </summary>
      /// <param name="apiConnection">The connection to delete.</param>
      /// <exception cref="InvalidOperationException">When the connection comes from the debug configuration.</exception>
      public void DeleteApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);
         DeleteApiConnection(apiConnection.ProfileName);
         UIConnections.Remove(apiConnection);
      }

      /// <summary>
      /// Keeps debug connections from being written to or deleted from the Registry. This is the last layer of
      /// protection: the UI already prevents it first, so reaching here means a calling mistake.
      /// </summary>
      private static void ThrowIfDebugConnection(ApiConnection apiConnection) {
         if (!apiConnection.IsDebugConnection) {
            return;
         }

         throw new InvalidOperationException(
            $"Connection '{apiConnection.ProfileName}' is defined in the debug configuration, " +
            "so it cannot be saved, changed, or deleted.");
      }

      /// <summary>
      /// Deletes an API connection profile by its profile name. Only touches the Registry and does not change
      /// <see cref="UIConnections"/> - used by <see cref="UpdateApiConnection"/> to remove the old entry when
      /// the profile name changed, while the object itself stays in the collection.
      /// </summary>
      /// <param name="profileName">The profile name to delete.</param>
      public void DeleteApiConnection(string profileName) {
         using var container = ApiConnectionsRegKey;
         container.DeleteSubKeyTree(profileName, throwOnMissingSubKey: false);
      }

      /// <summary>Gets the current time used to stamp rows.</summary>
      public async Task<DateTime> GetDateStampAsync() {
         var client = GetActiveApiClient();
         if (client is null) throw new InvalidOperationException("There is no active API Client.");
         return await client!.GetServerTimeStampAsync();
      }

      #region Session

      /// <summary>
      /// Raised every time a session ends - whether because the user signed out by themselves or because the
      /// session died on its own and cannot be recovered. The application itself listens to it to close other
      /// windows, release open screens, then return to the login screen, so both causes pass through exactly
      /// the same way.
      /// </summary>
      public event EventHandler<SessionEndedEventArgs>? SessionEnded;

      // The connection whose session is alive, together with its client. Kept so the event subscription can
      // be released again and its debug token can be restored when the session ends.
      private ApiClient? _sessionClient;
      private ApiConnection? _sessionConnection;
      private string? _suspendedDebugToken;
      private bool _rememberSession;

      private ISessionStorage SessionStorage => ServiceProvider.GetRequiredService<ISessionStorage>();

      private Task? _claimsRefresh;

      /// <summary>Used by screens that want to wait until rights have finished loading before drawing themselves.</summary>
      public Task EnsureClaimsLoadedAsync() => _claimsRefresh ?? Task.CompletedTask;

      /// <summary>
      /// Reloads the claim catalog and - if there is an active user - the rights they really hold. Called once
      /// after a successful login and after a debug connection is attached; it need not be called on sign out
      /// - <see cref="EndSessionAsync(bool)"/> already calls <see cref="SetActiveUser"/> with <c>null</c>, and
      /// without an active user the <see cref="ClaimCollection"/> indexer answers <c>false</c> by itself.
      /// </summary>
      public async Task RefreshClaimsAsync() {
         if (GetActiveApiClient() is null) return;

         _serverClaims = await ServiceProvider.GetRequiredService<ICredentialServices>()
            .GetMeta_AllClaimActions();
         _serverClaimsLoaded = true;
         RebuildClaimCatalog();

         if (ActiveUser is not { } user) return;

         // An administrator need not have their grants read at all: the indexer already answers true for them as
         // long as the claim's name is in the catalog. The debugger account and the built-in administrator
         // account also go into this branch because their cUserIsAdmin is indeed true - and that also keeps
         // GetClaims() from ever being called for a system account id, which would throw
         // SystemAccountException.
         // Direct rights and rights that come through roles are read separately so the client knows where a
         // right came from, then merged here. DistinctBy is there because one right may come twice - granted
         // directly and also carried by a role - and AvailableClaims is also read by screens, not just
         // ClaimCollection which does not care about duplicate rows.
         user.AvailableClaims = user.cUserIsAdmin
            ? []
            : [
               .. (await user.GetClaims())
                  .Concat(await user.GetRoleClaims())
                  .DistinctBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            ];
      }

      /// <summary>
      /// Opens a session from a pair of tokens that the server just issued: installs it in the active
      /// connection's client, loads its owner's identity, and - if asked - stores it so the next restart does
      /// not need the password typed again.
      /// </summary>
      /// <param name="token">The pair of tokens from a sign-in or from restoring a session.</param>
      /// <param name="remember">
      /// <c>true</c> when the user chose to stay signed in: the refresh token is stored, and every rotation
      /// result overwrites it.
      /// </param>
      /// <exception cref="InvalidOperationException">When there is no active connection yet.</exception>
      /// <remarks>
      /// If the identity of its owner fails to load, the whole thing is cancelled and the exception rises to
      /// the caller: half signed in is worse than failing to sign in.
      /// </remarks>
      public async Task BeginSessionAsync(TokenResult token, bool remember) {
         ArgumentNullException.ThrowIfNull(token);

         var connection = ActiveConnection
            ?? throw new InvalidOperationException("There is no active API connection to open a session on.");
         var client = GetActiveApiClient()!;

         client.SetSession(token);

         // The gate checks the debug token first, so as long as that token is attached, the Bearer that comes
         // with it would never be read - and the JWT flow would never really be tested from a dev build. The
         // value is stored, not regenerated later when it is restored.
         _suspendedDebugToken = connection.DebugToken;
         connection.DebugToken = null;

         try {
            SetActiveUser(await LoadSessionUserAsync(token.cUserId));
            // Failing here deliberately fails the login (still inside the same try): signing in with unknown rights
            // is worse than not signing in - the user would see an application whose buttons are all off with no
            // explanation.
            _claimsRefresh = RefreshClaimsAsync();
            await _claimsRefresh;
         }
         catch (Exception) {
            connection.DebugToken = _suspendedDebugToken;
            _suspendedDebugToken = null;
            client.ClearSession();
            // A simulated login stores nothing and therefore clears nothing: what is stored belongs to the
            // runs without debug.
            if (!IsSimulatingLogin) SessionStorage.Clear(connection.ProfileName);
            throw;
         }

         _sessionClient = client;
         _sessionConnection = connection;
         _rememberSession = remember;
         client.SessionChanged += OnApiClientSessionChanged;
         client.SessionEnded += OnApiClientSessionEnded;

         // Storing once at login is not enough: every renewal issues a new refresh token and kills the old one,
         // so what is stored must be overwritten as well - see OnApiClientSessionChanged.
         if (remember) StoreSession();
         else if (!IsSimulatingLogin) SessionStorage.Clear(connection.ProfileName);
      }

      /// <summary>
      /// Ends the session that is running: tells the server if asked, discards the session on the client side,
      /// clears the identity, then raises <see cref="SessionEnded"/>.
      /// </summary>
      /// <param name="notifyServer">
      /// <c>true</c> when the server needs to be told so the session also ends there. Its failure is
      /// deliberately ignored: a server that cannot be reached must not hold the user inside the application.
      /// </param>
      public Task EndSessionAsync(bool notifyServer) => EndSessionAsync(notifyServer, null);

      /// <inheritdoc cref="EndSessionAsync(bool)" />
      /// <param name="notifyServer"><inheritdoc cref="EndSessionAsync(bool)" path="/param[@name='notifyServer']" /></param>
      /// <param name="reason">
      /// The sentence shown by the login screen, or <c>null</c> when the user signed out by themselves.
      /// </param>
      public Task EndSessionAsync(bool notifyServer, string? reason) =>
         EndSessionCoreAsync(notifyServer, reason, raiseEnded: true);

      // The body of EndSessionAsync. Leaving Simulate Login ends the simulated session the same way but
      // must not raise SessionEnded, which would put the login screen back up instead of the debugger.
      private async Task EndSessionCoreAsync(bool notifyServer, string? reason, bool raiseEnded) {
         var client = _sessionClient;
         var connection = _sessionConnection;

         if (client is not null) {
            client.SessionChanged -= OnApiClientSessionChanged;
            client.SessionEnded -= OnApiClientSessionEnded;
         }

         // The debug token path has no session at all, and the server would answer 401 anyway because the
         // caller's session is empty - so something that has no session has no one to tell.
         if (notifyServer && client is { HasSession: true }) {
            try {
               await ServiceProvider.GetRequiredService<ICredentialServices>().PostMeta_SignOut();
            }
            catch (Exception) {
               // Ignored deliberately: the user still signs out even if the server does not answer.
            }
         }

         client?.ClearSession();

         if (connection is not null) {
            if (!IsSimulatingLogin) SessionStorage.Clear(connection.ProfileName);
            if (_suspendedDebugToken is not null) connection.DebugToken = _suspendedDebugToken;
         }

         _suspendedDebugToken = null;
         _sessionClient = null;
         _sessionConnection = null;
         _rememberSession = false;

         SetActiveUser(null);
         if (raiseEnded) SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
      }

      /// <summary>
      /// Loads the identity of the session's owner. The built-in administrator account has no user row, so its
      /// identity is made here - following the same pattern as the debugger account. Checked first, rather
      /// than by catching the exception, because the row lookup does throw
      /// <c>SystemAccountException</c> for an id like that.
      /// </summary>
      private Task<User> LoadSessionUserAsync(string cUserId) =>
         cUserId == Defaults.AdminUserId
            ? Task.FromResult(CreateAdminUser(this))
            : User.GetUser_ByIdAsync(this, cUserId);

      // Stored again every time the session content changes, including rotation results: the old refresh
      // token dies as soon as it is exchanged, so what is stored must always be the latest - otherwise the
      // next restart uses a dead token and the user is thrown to the login screen for no visible reason.
      private void OnApiClientSessionChanged(object? sender, EventArgs e) {
         if (_rememberSession && !IsSimulatingLogin && _sessionClient is { HasSession: true }) StoreSession();
      }

      // The session died on its own midway - the refresh failed, or the session was revoked from elsewhere.
      private void OnApiClientSessionEnded(object? sender, EventArgs e) {
         // This event is born in the middle of an HTTP request that failed to renew, so it can come from any
         // thread - while what is done below it clears the identity and raises ActiveUserChanged, which touches
         // bindings directly. If it is not returned to the UI thread first, the symptom would only show up when
         // a session really dies.
         var dispatcher = App?.Dispatcher;
         if (dispatcher is null || dispatcher.CheckAccess()) {
            _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice);
            return;
         }

         dispatcher.InvokeAsync(() => EndSessionAsync(notifyServer: false, SessionExpiredNotice));
      }

      /// <summary>
      /// The note shown by the login screen when a session ended not because the user signed out by
      /// themselves. Not an error message: a session that has simply expired is not the user's error.
      /// </summary>
      public const string SessionExpiredNotice = "Your session has ended. Please sign in again.";

      private void StoreSession() {
         if (_sessionConnection is not { } connection || _sessionClient is not { } client) return;
         if (client.RefreshToken is not { Length: > 0 } refreshToken) return;

         SessionStorage.Save(connection.ProfileName, new SavedSession(
            refreshToken,
            client.SessionUserId ?? string.Empty,
            ActiveUser?.cUserAccount ?? string.Empty));
      }

      /// <summary>
      /// Tries to restore the stored session of profile <paramref name="profileName"/>: exchanges the stored
      /// refresh token for a new pair of tokens, then opens its session. Called at startup, after the list of
      /// connections has been loaded.
      /// </summary>
      /// <param name="profileName">Name of the connection profile whose session is restored.</param>
      /// <returns>
      /// <c>true</c> when the session really is restored. <c>false</c> means nothing is stored, or what is
      /// stored is no longer valid - and that is an ordinary answer, not a failure.
      /// </returns>
      public async Task<bool> TryRestoreSessionAsync(string profileName) {
         if (UIConnections.FirstOrDefault(r => r.ProfileName == profileName) is not { } connection) {
            return false;
         }

         if (SessionStorage.Load(profileName) is not { } saved) return false;

         ActiveConnection = connection;

         try {
            // The action is public, so it needs nothing except that token itself - which is also the only thing
            // left: the access token of the previous session is certainly dead.
            var token = await ServiceProvider.GetRequiredService<ICredentialServices>()
               .PostGetMeta_RefreshToken(saved.RefreshToken);
            await BeginSessionAsync(token, remember: true);
            return true;
         }
         catch (Exception) {
            SessionStorage.Clear(profileName);
            return false;
         }
      }

      #endregion

      #endregion
   }
}
