using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Navigations;
using Em.Ui.Maui.Shared;
using Application = Microsoft.Maui.Controls.Application;

namespace Em.Ui.Maui.Core
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

      /// <summary>
      /// Raised every time the theme has finished being applied again because <see cref="CurrentTheme"/>
      /// changed. Used by screens that draw some of their colors from code, not from theme resources, so they
      /// can redraw themselves.
      /// </summary>
      public event EventHandler? ThemeChanged;

      private EmApp(string[] args) {
         Args = args;
         // Debug mode has no sign-in moment: its user is already made active synchronously inside BuildApp
         // (InitDebugMode), before there is an active connection for any await to ride on. The moment that is
         // really available is when an active connection is attached - that is, when the connection card on the
         // home screen selects DefaultDebugConnection.
         ActiveConnectionChanged += (_, _) => {
            if (IsDebugMode && ActiveConnection is not null) {
               _claimsRefresh = RefreshClaimsAsync();
            }
         };
      }

      #region Properties

      /// <summary>
      /// Settings of the application's brand display (logo, login screen texts, and light/dark themes).
      /// Filled from <see cref="EmAppBuilder.ApplyBranding"/> during <see cref="BuildApp"/>; if the
      /// application never calls it, it stays an empty <see cref="BrandingInfo"/>, so every member
      /// automatically falls back to generic defaults - callers need no null check.
      /// </summary>
      public BrandingInfo Branding { get; private set; } = null!;

      /// <summary><c>true</c> when the application runs with the debug configuration.</summary>
      public bool IsDebugMode { get; private set; }

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
      /// Name of the application, used as the title of the main page and as the prefix of every key in the
      /// settings storage where application settings (e.g. API connections, theme) are kept.
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

      // Kept as the concrete type so it can be disposed when the application stops, while only
      // IServiceProvider is exposed to callers through ServiceProvider.
      private ServiceProvider _serviceProvider = null!;

      /// <inheritdoc />
      /// <remarks>
      /// On the UI side there is no per-request scope like in the API, so what is returned is always the root
      /// provider built from <see cref="Services"/> at the end of <see cref="BuildApp"/>.
      /// </remarks>
      public IServiceProvider ServiceProvider => _serviceProvider;

      /// <summary>Where the application's settings are stored on the device.</summary>
      public AppSettings Settings { get; private set; } = null!;

      /// <summary>
      /// The application's main page, available after <see cref="CreateRootPage"/> is called. Used as the
      /// dialog owner by view models that have no page of their own.
      /// </summary>
      public Page? RootPage { get; private set; }

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
      // XxxCommandAllowed - a path the UI runs over and over - and merging there would allocate a
      // new list each time a button decides whether it is enabled.
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
      /// differ. Debug mode skips the rights check, but not the module check: once the server catalog is
      /// loaded, a navigation whose module is not declared by the server (e.g. a test module that is turned
      /// off) is refused for anyone.
      /// </summary>
      /// <param name="navigation">The navigation about to be opened.</param>
      public bool CanOpen(Navigation navigation) =>
         (!_serverClaimsLoaded || NavigationAccess.IsDeclared(navigation, _allClaims)) &&
         (IsDebugMode || NavigationAccess.CanOpen(navigation, ActiveUser));

      // Until a server has been asked, module presence is unknown and must not hide anything.
      private bool _serverClaimsLoaded;

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

      // The name of the setting where the list of connection profiles is stored. Unlike the desktop client,
      // which gives each profile one subkey, here the whole list is stored as a single JSON text - MAUI
      // settings storage does not know a branching layout.
      private const string ApiConnectionsSettingName = "Api Connections";

      /// <summary>
      /// Whether the user ticked "keep me signed in" on the login screen (stored in the device settings, so
      /// it carries across sessions).
      /// <para>
      /// Only the choice is stored. Credentials are never written to the settings - the only thing remembered
      /// besides this flag is <see cref="RememberedUserName"/>.
      /// </para>
      /// </summary>
      public bool RememberSignIn {
         get => Settings.GetBool(nameof(RememberSignIn));
         set => Settings.SetBool(nameof(RememberSignIn), value);
      }

      /// <summary>
      /// The account name last used to sign in, so the login screen can fill it back in while
      /// <see cref="RememberSignIn"/> is on. Set to <c>null</c> (or empty text) to forget it - the value is
      /// removed from the settings directly, not stored as an empty string.
      /// </summary>
      public string? RememberedUserName {
         get => Settings.GetString(nameof(RememberedUserName));
         set => Settings.SetString(nameof(RememberedUserName), value);
      }

      /// <summary>
      /// The name of the connection profile last used to sign in, a pair with
      /// <see cref="RememberedUserName"/>. Needed because a stored session is deposited per profile name:
      /// without knowing which profile, nothing can be restored when the application is opened again.
      /// Set to <c>null</c> (or empty text) to forget it.
      /// </summary>
      public string? RememberedProfileName {
         get => Settings.GetString(nameof(RememberedProfileName));
         set => Settings.SetString(nameof(RememberedProfileName), value);
      }

      /// <summary>
      /// The theme mode that is active, light or dark (stored in the device settings, so it carries across
      /// sessions). Setting this value immediately applies the new theme to the application and raises
      /// <see cref="ThemeChanged"/>. Default: <see cref="ThemeVariant.Dark"/>.
      /// </summary>
      public ThemeVariant CurrentTheme {
         get => ParseThemeVariant(Settings.GetString(nameof(CurrentTheme)));
         set {
            if (CurrentTheme == value)
               return;

            Settings.SetString(nameof(CurrentTheme), value.ToString());
            ApplyTheme();
         }
      }

      /// <summary><c>true</c> when the active theme mode is the light mode.</summary>
      public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

      /// <summary>
      /// The theme in use: <see cref="BrandingInfo.LightTheme"/> or <see cref="BrandingInfo.DarkTheme"/> of
      /// <see cref="Branding"/>, according to <see cref="CurrentTheme"/>.
      /// </summary>
      public ThemeBase ActiveTheme => Branding.GetTheme(CurrentTheme);

      // The desktop client used to store the name of its control library's theme; both spellings
      // still read back as the variant they meant. Anything else falls to the default.
      private static ThemeVariant ParseThemeVariant(string? value) => value switch {
         "Light" or "Win11Light" => ThemeVariant.Light,
         "Dark" or "Win11Dark" => ThemeVariant.Dark,
         _ => DefaultTheme
      };

      // One path for startup and for every switch afterwards, in the same order as the desktop
      // client: the engine's own theme first, then the registered appliers, then whoever listens.
      // The palette itself needs nothing here - both variants are loaded, and AppThemeBinding picks.
      private void ApplyTheme() {
         if (Application.Current is { } app) {
            app.UserAppTheme = CurrentTheme == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark;
         }

         var theme = ActiveTheme;
         foreach (var applier in ServiceProvider.GetServices<IThemeApplier>()) {
            applier.Apply(theme);
         }

         // The light/dark marker in the toolbar and on the login screen is an ordinary computed property, so all
         // of them must be asked to redraw.
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

      /// <summary>
      /// Builds the application's main page and installs its first screen. Called once from
      /// <c>App.CreateWindow</c> - this is the MAUI counterpart of <c>Run</c> in the desktop client, which
      /// there blocks until the application stops while here its lifecycle is held by MAUI itself.
      /// </summary>
      /// <returns>The page installed into the application window.</returns>
      public Page CreateRootPage() {
         ApplyTheme();

         var host = new SpaNavigationHost(this);
         RootPage = host;

         // Debug mode already has its user since BuildApp (see InitDebugMode), so there is nothing to ask and the
         // application opens straight to home. Otherwise nobody is there yet: the login screen is installed, and
         // home is only built after someone really signs in.
         // Handed to the dispatcher, not run here: installing the first screen is asynchronous, while this method
         // must return its page right now so the window can be opened.
         host.Dispatcher.Dispatch(() => _ = ShowFirstScreenAsync());
         return host;
      }

      // There is nobody left above this method - the dispatcher that runs it does not wait for the result,
      // and this application has no DispatcherUnhandledException - so the exception is caught and shown here.
      private async Task ShowFirstScreenAsync() {
         try {
            if (IsDebugMode) {
               // Debug mode skips the login screen, so no screen got the chance to choose its server. It is chosen
               // here, right before home stands up: the active connection is what opens the way to the claim catalog
               // (see the constructor), and home draws itself from the rights that exist. The account panel may
               // replace it at any time after this.
               RetrieveApiConnections();
               ActiveConnection ??= DefaultDebugConnection;

               await GoHomeAsync();
               return;
            }

            // The login screen is installed first, then the stored session is tried: that screen is what builds the
            // list of connections, and without that list there is no profile that can be restored. If restoring
            // succeeds, the login screen is left right away - NavigateHome releases it together with the whole
            // navigation path.
            await ShowLoginScreen(null);
            if (await TryRestoreRememberedSessionAsync()) await GoHomeAsync();
         }
         catch (Exception x) {
            if (RootPage is { } page) await page.DisplayAlertAsync("Error", x.SerializedMessagesDefault(), "OK");
         }
      }

      // Going home is always paired with reloading its content: home is built once and never released, so
      // without a reload it would show the state from before anyone signed in.
      private async Task GoHomeAsync() {
         await MainStack.NavigateHome();
         await MainStack.Home!.Reload();
      }

      /// <summary>
      /// Tries to resume the session stored from the last time the application was used, so a user who chose
      /// to stay signed in need not type the password again. Failing is not the user's error - a session that
      /// was revoked or has expired simply leaves the login screen as it is, without an error message.
      /// </summary>
      /// <returns><c>true</c> when the session really is restored.</returns>
      public Task<bool> TryRestoreRememberedSessionAsync() {
         if (!RememberSignIn) return Task.FromResult(false);
         if (RememberedProfileName is not { Length: > 0 } profileName) return Task.FromResult(false);

         return TryRestoreSessionAsync(profileName);
      }

      /// <summary>
      /// Installs the login screen as the only content of the navigation path, so there is no way back to
      /// anything that was open before. Called when the application is opened and every time a session ends.
      /// </summary>
      /// <param name="notice">
      /// The note shown by the login screen, or <c>null</c> when the user signed out by themselves.
      /// </param>
      public async Task ShowLoginScreen(string? notice) {
         var logon = Navigations.First(r => r.Name == LogonNavigationName);
         if (!await NavigateToRoot(logon)) return;

         // Its body was already built by the navigation just now, so reading it here builds nothing more. Its
         // lifetime is that of its entry: as soon as the stack is cleared after a successful login, this control
         // is released and the next visit gets a clean form.
         if (MainStack.Current?.Body is LoginControl login) login.Vm.SessionEndedNotice = notice;
      }

      /// <summary>
      /// Rebuilds <see cref="UIConnections"/>: <see cref="DebugConnections"/> first (if any), followed by all
      /// API connection profiles stored in the device settings.
      /// </summary>
      public void RetrieveApiConnections() {
         UIConnections.Clear();

         // Debug connections do not come from storage. Their instances belong to DebugConnections and are
         // deliberately reused every time the list is rebuilt, so the references stay stable.
         if (IsDebugMode) {
            DebugConnections.EachOf(UIConnections.Add);
            return;
         }

         LoadStoredConnections()
            .Select(r => new ApiConnection {
               ProfileName = r.ProfileName,
               Host = r.Host,
               Timeout = r.Timeout,
               IgnoreSslErrors = r.IgnoreSslErrors,
            })
            .EachOf(UIConnections.Add);
      }

      /// <summary>
      /// Stores a new API connection profile in the device settings, and also adds it to
      /// <see cref="UIConnections"/> so the list shown by the UI is updated too.
      /// </summary>
      /// <param name="apiConnection">The connection data to store.</param>
      /// <exception cref="InvalidOperationException">When the connection comes from the debug configuration.</exception>
      public void AddApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);

         var stored = LoadStoredConnections()
            .Where(r => !string.Equals(r.ProfileName, apiConnection.ProfileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

         stored.Add(new StoredConnection(
            apiConnection.ProfileName, apiConnection.Host, apiConnection.Timeout, apiConnection.IgnoreSslErrors));
         SaveStoredConnections(stored);

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
      /// Deletes an API connection profile by its object, from the device settings as well as from
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
      /// Deletes an API connection profile by its profile name. Only touches the device settings and does not change
      /// <see cref="UIConnections"/> - used by <see cref="UpdateApiConnection"/> to remove the old entry when
      /// the profile name changed, while the object itself stays in the collection.
      /// </summary>
      /// <param name="profileName">The profile name to delete.</param>
      public void DeleteApiConnection(string profileName) {
         SaveStoredConnections(LoadStoredConnections()
            .Where(r => !string.Equals(r.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .ToList());

         // A stored session does not ride on its profile as in the desktop client, so it must be removed here -
         // otherwise it would be left behind for a profile that no longer exists.
         SessionStorage.Clear(profileName);
      }

      /// <summary>
      /// Keeps debug connections from being written to or deleted from the device settings. This is the last layer of
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

      // The shape of a connection profile as it is stored. Deliberately kept apart from ApiConnection: there
      // are two members there that must not be stored at all - the debug connection marker and its debug
      // token.
      private sealed record StoredConnection(string ProfileName, string Host, int Timeout, bool IgnoreSslErrors);

      private List<StoredConnection> LoadStoredConnections() {
         var payload = Settings.GetString(ApiConnectionsSettingName);
         if (string.IsNullOrWhiteSpace(payload)) return [];

         try {
            return JsonSerializer.Deserialize<List<StoredConnection>>(payload) ?? [];
         }
         catch (JsonException) {
            // A list that cannot be read will never be readable again. It is discarded now so it is not tried again
            // every time the application is opened.
            Settings.SetString(ApiConnectionsSettingName, null);
            return [];
         }
      }

      private void SaveStoredConnections(List<StoredConnection> connections) =>
         Settings.SetString(ApiConnectionsSettingName, JsonSerializer.Serialize(connections));

      /// <inheritdoc />
      public async Task<DateTime> GetDateStampAsync() {
         var client = GetActiveApiClient();
         if (client is null) throw new InvalidOperationException("There is no active API Client.");
         return await client.GetServerTimeStampAsync();
      }

      #region Session

      /// <summary>
      /// Raised every time a session ends - whether because the user signed out by themselves or because the
      /// session died on its own and cannot be recovered. The application itself listens to it to release
      /// open screens and return to the login screen, so both causes pass through exactly the same way.
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
            SessionStorage.Clear(connection.ProfileName);
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
         else SessionStorage.Clear(connection.ProfileName);
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
      public async Task EndSessionAsync(bool notifyServer, string? reason) {
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
            SessionStorage.Clear(connection.ProfileName);
            if (_suspendedDebugToken is not null) connection.DebugToken = _suspendedDebugToken;
         }

         _suspendedDebugToken = null;
         _sessionClient = null;
         _sessionConnection = null;
         _rememberSession = false;

         SetActiveUser(null);
         SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
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
         if (_rememberSession && _sessionClient is { HasSession: true }) StoreSession();
      }

      // The session died on its own midway - the refresh failed, or the session was revoked from elsewhere.
      private void OnApiClientSessionEnded(object? sender, EventArgs e) {
         // This event is born in the middle of an HTTP request that failed to renew, so it can come from any
         // thread - while what is done below it clears the identity and raises ActiveUserChanged, which touches
         // bindings directly. If it is not returned to the UI thread first, the symptom would only show up when a
         // session really dies.
         var dispatcher = RootPage?.Dispatcher ?? Application.Current?.Dispatcher;
         if (dispatcher is null || !dispatcher.IsDispatchRequired) {
            _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice);
            return;
         }

         dispatcher.Dispatch(() => _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice));
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
