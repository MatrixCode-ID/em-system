using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The account panel: the signed-in account, static navigation, server choice, theme switch, and sign
   /// out. Installed as a layer over the main page and entering from the right when the badge in the top
   /// bar is tapped. It replaces the side menu - one place for everything that is not an ordinary screen
   /// change.
   /// </summary>
   public partial class AccountPanel : ContentView
   {
      // Exactly the same as the duration of the curtain animation on its owning page: the two are one
      // movement, and a movement whose panel finishes before its curtain reads as two unrelated things.
      private const uint OpenDuration = 260;
      private const uint CloseDuration = 200;

      public AccountPanel() {
         InitializeComponent();
         // Starts from outside the right edge of the screen. Its width is used as-is, not measured while the
         // animation runs: while the panel has never been shown, its size is still zero and it would slide in
         // from the wrong place.
         TranslationX = WidthRequest;
      }

      /// <summary>The view model of this panel, read back from the BindingContext set in XAML.</summary>
      public AccountPanelVm Vm => (AccountPanelVm)BindingContext;

      /// <summary>
      /// Slides the panel in or out of the screen. Its content is rebuilt right before it comes in - not once
      /// when the panel is created - because the claim catalog may only finish loading after the main page
      /// stands up, and a row computed too early would be missing for the whole first session.
      /// </summary>
      /// <param name="open"><c>true</c> to bring the panel in, <c>false</c> to take it out.</param>
      /// <param name="navigation">The screen being opened, which decides the visibility switches of this panel.</param>
      public async Task AnimateAsync(bool open, Navigation? navigation) {
         if (open) {
            Vm.Reload(navigation);
            // Hidden while closed, not merely slid out: a panel that has slid away still occupies a strip as wide as
            // itself at the right edge of the screen, and on some devices that strip still catches touches meant for
            // the page content behind it.
            IsVisible = true;
         }

         var duration = open ? OpenDuration : CloseDuration;
         var easing = open ? Easing.CubicOut : Easing.CubicIn;
         await this.TranslateToAsync(open ? 0 : WidthRequest, 0, duration, easing);

         if (!open) IsVisible = false;
      }
   }

   /// <summary>
   /// One row in the static navigation list of the account panel. It may represent a screen, or - like
   /// Simulate Login - an action that has no screen of its own.
   /// </summary>
   public sealed class AccountPanelItemVm
   {
      /// <summary>The text of this row.</summary>
      public required string Title { get; init; }

      /// <summary>The icon of this row, taken from <see cref="FontIcons"/>.</summary>
      public required string Glyph { get; init; }

      /// <summary>What this row does when it is tapped.</summary>
      public required Func<Task> Action { get; init; }

      /// <summary>
      /// Whether this row may be pressed. A row that may not still gets drawn, only faded - removing it would
      /// make the panel look different in content for no readable reason.
      /// </summary>
      public bool IsEnabled { get; init; } = true;

      /// <summary>
      /// Whether this row needs a divider line above it. Filled when the list is built, so the first row has
      /// no line stuck to the top edge of the card.
      /// </summary>
      public bool HasDividerAbove { get; set; }
   }

   /// <summary>One server choice in the account panel.</summary>
   public sealed class AccountPanelConnectionVm(ApiConnection connection) : NotifyPropertyBase
   {
      /// <summary>The connection profile this row represents.</summary>
      public ApiConnection Connection { get; } = connection;

      /// <summary>The name of the connection profile.</summary>
      public string ProfileName => Connection.ProfileName;

      /// <summary>The server address of this profile.</summary>
      public string Host => Connection.Host;

      /// <summary>
      /// Whether this row is the selected one. It is always the panel that turns it on - only the panel knows
      /// which other row must also go off, and without that two rows could light up at once.
      /// </summary>
      public bool IsChecked {
         get => Get<bool>();
         set => Set(value);
      }
   }

   /// <summary>View model <see cref="AccountPanel"/>.</summary>
   public class AccountPanelVm : MvvmModelBase
   {
      /// <summary>
      /// Raised when the panel needs to close - through the cross button, or after a row has taken its user to
      /// another screen. The owning page closes it, because it is also the one holding the curtain behind this
      /// panel.
      /// </summary>
      public event EventHandler? CloseRequested;

      public AccountPanelVm() {
         RegisterCommand(nameof(CloseCommand), CloseCommand);
         RegisterCommand(nameof(OpenItemCommand), OpenItemCommand);
         RegisterCommand(nameof(SelectConnectionCommand), SelectConnectionCommand);
         RegisterCommand(nameof(ToggleThemeCommand), ToggleThemeCommand);
         RegisterCommand(nameof(SignOutCommand), SignOutCommand, SignOutCommandAllowed);
      }

      /// <summary>The static navigation rows, rebuilt every time the panel is opened.</summary>
      public ObservableCollection<AccountPanelItemVm> StaticItems { get; } = [];

      /// <summary>The server choices available, rebuilt every time the panel is opened.</summary>
      public ObservableCollection<AccountPanelConnectionVm> Connections { get; } = [];

      /// <summary>
      /// <c>true</c> when there are static navigation rows to draw. Its card is hidden entirely when there are
      /// none - an empty card reads as a list that failed to load.
      /// </summary>
      public bool HasStaticItems => StaticItems.Count > 0;

      // The screen being opened, handed over by the owning page every time this panel is about to appear. The
      // panel's visibility switches are owned by its navigation, just like the switches of the top bar.
      private Navigation? _navigation;

      /// <summary>Logo aplikasi, digambar di kepala panel.</summary>
      public ImageSource? LogoImage => EmApp is { } app ? BrandingImages.LoadLogo(app.Branding) : null;

      /// <summary>The full name of the owner of the signed-in account.</summary>
      public string FullName => EmApp?.ActiveUser?.cContactFullName ?? "Not signed in";

      /// <summary>
      /// The initials of the account owner, the content of the avatar circle. The full name comes first, and
      /// the account name is the fallback when the contact has no name yet.
      /// </summary>
      public string Initials => EmApp?.ActiveUser is { } user
         ? (user.cContactFullName is { Length: > 0 } fullName ? fullName : user.cUserAccount).ToInitials()
         : "?";

      /// <summary>The e-mail address of the signed-in account.</summary>
      public string EmailAddress => EmApp?.ActiveUser?.cCommValue ?? string.Empty;

      /// <summary>
      /// Whether the server choice list is drawn too. Only in debug mode: otherwise there is exactly one
      /// server - the one typed on the login screen - so the row would only repeat the caption above it, and a
      /// radio button with no other choice promises something that does not exist.
      /// </summary>
      public bool HasConnectionChoice => EmApp?.IsDebugMode ?? false;

      /// <summary>A caption of the server in use, a twin of the similar chip on the login screen.</summary>
      public string ConnectionStatus => EmApp?.ActiveConnection is { } connection
         ? $"Connected to {connection.Host}"
         : "No server selected";

      /// <summary>Whether the theme switch row is drawn in this panel.</summary>
      public bool IsThemeVisible => _navigation?.IsColorThemeVisible ?? false;

      /// <summary>The text of the theme switch row, naming the theme it will go to - not the one that is active.</summary>
      public string ThemeCaption => EmApp?.IsLightTheme == true ? "Dark theme" : "Light theme";

      /// <summary>The icon of the theme switch row, a pair with <see cref="ThemeCaption"/>.</summary>
      public string ThemeGlyph => EmApp?.IsLightTheme == true ? FontIcons.Moon : FontIcons.Sun;

      /// <summary>Whether the sign-out row needs to be drawn at all.</summary>
      public bool CanSignOut => EmApp?.ActiveUser is not null;

      /// <summary>
      /// Whether the sign-out row may be pressed. In debug mode the answer is no: its user is made active by
      /// the application itself when it is built and has no session on the server to end, so signing out there
      /// would only leave the application with nobody in it and no way back in. What is available in debug is
      /// Simulate Login, not sign out.
      /// </summary>
      public bool IsSignOutEnabled => EmApp is { IsDebugMode: false, ActiveUser: not null };

      /// <summary>
      /// Rebuilds the whole content of the panel. Called right before the panel enters the screen, not once
      /// when the panel is created.
      /// </summary>
      /// <param name="navigation">The screen being opened when this panel is called.</param>
      public void Reload(Navigation? navigation) {
         _navigation = navigation;
         if (EmApp is not { } app) return;

         RebuildStaticItems(app);

         app.RetrieveApiConnections();
         Connections.Clear();
         app.UIConnections
            .Select(r => new AccountPanelConnectionVm(r) { IsChecked = r == app.ActiveConnection })
            .EachOf(Connections.Add);

         NotifyChanged(nameof(LogoImage));
         NotifyChanged(nameof(FullName));
         NotifyChanged(nameof(Initials));
         NotifyChanged(nameof(EmailAddress));
         NotifyChanged(nameof(ConnectionStatus));
         NotifyChanged(nameof(HasConnectionChoice));
         NotifyChanged(nameof(CanSignOut));
         NotifyChanged(nameof(IsSignOutEnabled));
         NotifyChanged(nameof(IsThemeVisible));
         RefreshTheme();
         Commands[nameof(SignOutCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>Redraws the theme switch row after the theme changed.</summary>
      public void RefreshTheme() {
         NotifyChanged(nameof(ThemeCaption));
         NotifyChanged(nameof(ThemeGlyph));
      }

      // The static navigation list follows the same pattern as the desktop client, but its content belongs to
      // this panel itself. For now it is only Simulate Login, and only in debug mode: no MAUI screen needs
      // rights restrictions yet.
      private void RebuildStaticItems(EmApp app) {
         StaticItems.Clear();

         // The debugger account has no stored row anywhere - its id is deliberately not a valid id - so there is
         // no password of its own that could be changed. Its row is still drawn so the panel does not change shape
         // depending on who is signed in.
         AddStaticItem("Change Password", FontIcons.Key,
            () => app.NavigateTo(EmApp.ChangePasswordNavigationName),
            isEnabled: app.ActiveUser?.cUserId != Defaults.DebuggerUserId);

         if (app.IsDebugMode) {
            AddStaticItem("Simulate Login", FontIcons.RightToBracket, () => app.ShowLoginScreen(null));
         }

         for (var index = 0; index < StaticItems.Count; index++) {
            StaticItems[index].HasDividerAbove = index > 0;
         }

         NotifyChanged(nameof(HasStaticItems));
      }

      private void AddStaticItem(string title, string glyph, Func<Task> action, bool isEnabled = true) =>
         StaticItems.Add(new AccountPanelItemVm {
            Title = title, Glyph = glyph, Action = action, IsEnabled = isEnabled
         });

      /// <summary>
      /// Answers whether the signed-in user may see a static row. Nobody calls it yet: MAUI has not registered
      /// any navigation that needs rights restrictions. It is built now so a claim-bound row can simply be
      /// attached when its screen exists, and so the restriction is not invented again in another way
      /// elsewhere.
      /// </summary>
      /// <param name="moduleName">The name of the module that owns the claim.</param>
      /// <param name="claimName">The name of the claim inside that module.</param>
      private bool HasClaim(string moduleName, string claimName) =>
         EmApp is { } app && new ClaimCollection(moduleName, app.AllClaims, app.ActiveUser)[claimName];

      /// <summary>
      /// Chooses a server profile. Its row is turned on and the other rows are turned off here, then the
      /// profile is installed right away as the active connection - there is no "apply" button to press
      /// afterwards. The caption above the list also changes, because it reads the same active connection.
      /// </summary>
      public void SelectConnectionCommand(object? parameter) {
         if (parameter is not AccountPanelConnectionVm selected || EmApp is not { } app) return;

         foreach (var item in Connections) item.IsChecked = item == selected;

         // Guarded against a choice that changes nothing: installing the same active connection once more still
         // triggers recomputing the whole claim catalog.
         if (app.ActiveConnection == selected.Connection) return;

         app.ActiveConnection = selected.Connection;
         NotifyChanged(nameof(ConnectionStatus));
      }

      public void CloseCommand() => CloseRequested?.Invoke(this, EventArgs.Empty);

      /// <summary>
      /// Runs a static navigation row. The panel is closed first so the screen change can be seen, not happen
      /// behind a panel that still covers the screen.
      /// </summary>
      public Task OpenItemCommand(object? parameter) {
         if (parameter is not AccountPanelItemVm { IsEnabled: true } item) return Task.CompletedTask;

         CloseRequested?.Invoke(this, EventArgs.Empty);
         return item.Action();
      }

      public void ToggleThemeCommand() {
         if (EmApp is not { } app) return;
         app.CurrentTheme = app.IsLightTheme ? ThemeVariant.Dark : ThemeVariant.Light;
      }

      public Task SignOutCommand() {
         CloseRequested?.Invoke(this, EventArgs.Empty);
         return EmApp!.EndSessionAsync(notifyServer: true);
      }

      public bool SignOutCommandAllowed() => IsSignOutEnabled;
   }
}
