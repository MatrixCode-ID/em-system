using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class SpaNavigationHost : UserControl
   {
      private readonly EmApp _app;

      // The window the keyboard shortcuts are currently registered on, and the bindings put there,
      // kept so unloading can take back exactly what loading added.
      private Window? _shortcutWindow;
      private readonly List<CommandBinding> _shortcutBindings = [];

      // Tracked separately from _shortcutWindow: the account button follows the application object,
      // not the window, so it stays subscribed even on a host that never found a window to put its
      // keyboard shortcuts on.
      private bool _activeUserHooked;

      /// <summary>Creates a new instance of <see cref="SpaNavigationHost"/>.</summary>
      public SpaNavigationHost(EmApp app, NavigationStack stack) {
         _app = app;
         // Vm only exists once InitializeComponent has built the DataContext declared in XAML, so
         // the view model has to be ready before this host may receive a navigation request -
         // subscribing first would let a request arrive while Vm is still null.
         InitializeComponent();
         Vm.BodySwitching += SlideBody;
         Vm.EmApp = app;
         Vm.Stack = stack;
         // The bindings behind the account button were created by InitializeComponent above, while
         // EmApp - and with it the signed-in user - was still missing, so they have to be told to
         // read it again now that it is there.
         Vm.RefreshActiveUser();

         // Back/forward are also reachable from the keyboard and from the mouse thumb buttons.
         // Both are wired here rather than in the window: this host owns the commands and the rules
         // saying when they are allowed, so a window doing it would only have to reach back into it.
         Loaded += HostLoaded;
         Unloaded += HostUnloaded;
      }
      /// <summary>The vm.</summary>
      public SpaNavigationHostVm Vm => (SpaNavigationHostVm)DataContext;
      private async void TestClicked(object sender, RoutedEventArgs e) {
         await _app.NavigateTo("logon");
      }

      // The shortcuts go on the window, not on this control: a CommandBinding here would only fire
      // while the focus sits inside the host, and a navigation body is free to hand the focus to a
      // control that keeps it.
      private void HostLoaded(object sender, RoutedEventArgs e) {
         if (!_activeUserHooked) {
            _app.ActiveUserChanged += AppActiveUserChanged;
            _app.DebugStateChanged += AppActiveUserChanged;
            _activeUserHooked = true;
            // Whatever moved while this host was out of the tree is read again now.
            Vm.RefreshActiveUser();
         }

         if (_shortcutWindow != null) return;

         _shortcutWindow = Window.GetWindow(this);
         if (_shortcutWindow == null) return;

         // BrowseBack/BrowseForward already carry every gesture a user expects, so nothing of our
         // own has to be declared: Alt+Left and Alt+Right, the browser keys on a media keyboard,
         // and the two thumb buttons on a mouse. The thumb buttons arrive the long way round -
         // Windows turns the released button into WM_APPCOMMAND and WPF translates that into these
         // very commands - which is also why handling WM_XBUTTON here would navigate twice per
         // click, once on the button going down and once on the app command that follows it.
         // Backspace is deliberately not among the gestures: it belongs to whatever is being typed.
         AddShortcut(NavigationCommands.BrowseBack, SpaNavigationHostVm.BackCommand, () => Vm.IsBackVisible);
         AddShortcut(NavigationCommands.BrowseForward, SpaNavigationHostVm.ForwardCommand, () => Vm.IsForwardVisible);
      }

      // Handed back on unload so a window never keeps invoking a host that has left the tree. It
      // matters more later than now: today the host is created once, but once every tab carries its
      // own, a stale binding would drive the wrong one.
      private void HostUnloaded(object sender, RoutedEventArgs e) {
         if (_activeUserHooked) {
            _app.ActiveUserChanged -= AppActiveUserChanged;
            _app.DebugStateChanged -= AppActiveUserChanged;
            _activeUserHooked = false;
         }

         if (_shortcutWindow == null) return;

         foreach (var binding in _shortcutBindings)
            _shortcutWindow.CommandBindings.Remove(binding);

         _shortcutBindings.Clear();
         _shortcutWindow = null;
      }

      // How far a screen travels while it slides. Kept short on purpose: the fade does most of the
      // work, and a full-width slide would make frequent Back/Forward feel slow.
      private const double SlideDistance = 40;

      // Code-behind rather than a binding: the outgoing screen has to be photographed at the exact
      // moment before the new body replaces it, which is an event, not a state XAML could follow.
      private void SlideBody(bool forward) {
         var duration = _app.NavigationTransitionTime;
         if (duration <= TimeSpan.Zero || bodyHost.ActualWidth < 1 || bodyHost.ActualHeight < 1) return;

         outgoingSnapshot.Source = Snapshot(bodyHost);
         outgoingSnapshot.Visibility = Visibility.Visible;

         var shift = forward ? SlideDistance : -SlideDistance;
         var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
         var time = new Duration(duration);

         bodyShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shift, 0, time) { EasingFunction = easing });
         bodyHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = easing });
         outgoingShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -shift, time) { EasingFunction = easing });

         var fadeOut = new DoubleAnimation(1, 0, time) { EasingFunction = easing };
         // A second switch before this one ends starts fresh animations on the same properties, and
         // the snapshot it put up must outlive this handler.
         fadeOut.Completed += (_, _) => {
            if (outgoingSnapshot.Opacity > 0) return;
            outgoingSnapshot.Visibility = Visibility.Collapsed;
            outgoingSnapshot.Source = null;
         };
         outgoingSnapshot.BeginAnimation(OpacityProperty, fadeOut);
      }

      // Drawn through a VisualBrush so the picture starts at the element's own corner; rendering the
      // element directly would carry its offset inside the parent into the bitmap. The viewbox is
      // pinned to the element's layout size: left to its default, the brush frames whatever the body
      // happens to paint - which on a body that paints only part of its area gets stretched to fill it.
      private static BitmapSource Snapshot(FrameworkElement element) {
         var dpi = VisualTreeHelper.GetDpi(element);
         var size = new Size(element.ActualWidth, element.ActualHeight);
         var brush = new VisualBrush(element) {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(size),
            Stretch = Stretch.Fill
         };
         var visual = new DrawingVisual();
         using (var context = visual.RenderOpen())
            context.DrawRectangle(brush, null, new Rect(size));

         var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
         bitmap.Render(visual);
         bitmap.Freeze();
         return bitmap;
      }

      // EmApp is a plain object rather than a bindable source, so a new signed-in user - or Simulate
      // Login starting or ending - does not reach the account button on its own; this is what tells
      // the toolbar to read it again.
      private void AppActiveUserChanged(object? sender, EventArgs e) => Vm.RefreshActiveUser();

      private void AddShortcut(RoutedUICommand gesture, string commandName, Func<Visibility> offered) {
         var binding = new CommandBinding(gesture);
         binding.Executed += (_, e) => {
            e.Handled = true;
            Invoke(commandName, offered());
         };
         // Handled, otherwise the query keeps travelling and something further up may answer for a
         // command this host has already spoken for.
         binding.CanExecute += (_, e) => {
            e.Handled = true;
            e.CanExecute = CanInvoke(commandName, offered());
         };

         _shortcutWindow!.CommandBindings.Add(binding);
         _shortcutBindings.Add(binding);
      }

      // Every extra input source ends up on the toolbar command itself, so when back or forward is
      // allowed is stated in one place only. A navigation that hides the button hides the shortcut
      // with it: the switch says this navigation does not offer back, not just draw no button.
      private void Invoke(string commandName, Visibility offered) {
         if (CanInvoke(commandName, offered)) Vm.Commands[commandName]!.Execute(null);
      }

      // A hidden toolbar offers nothing, including through the keyboard and the mouse thumb buttons: both
      // arrive here as BrowseBack/BrowseForward, and without this condition a screen like login - which turns
      // off its toolbar but does not name its buttons one by one - could still be left with Alt+Left.
      private bool CanInvoke(string commandName, Visibility offered) =>
         Vm.IsToolbarVisible == Visibility.Visible
         && offered == Visibility.Visible
         && Vm.Commands[commandName]?.CanExecute(null) == true;
   }
   /// <summary>View model of the single-page navigation host.</summary>
   public class SpaNavigationHostVm : MvvmModelBase
   {
      /// <summary>
      /// The command name of the Back button on the toolbar, used by other input sources (mouse buttons,
      /// keyboard shortcuts) so they call the same command as the button.
      /// </summary>
      public const string BackCommand = nameof(Back);

      /// <summary>
      /// The command name of the Forward button on the toolbar. See <see cref="BackCommand"/>.
      /// </summary>
      public const string ForwardCommand = nameof(Forward);

      /// <summary>Creates a new instance of <see cref="SpaNavigationHostVm"/>.</summary>
      public SpaNavigationHostVm() {
         RegisterCommand(nameof(Back), Back, BackAllowed);
         RegisterCommand(nameof(Forward), Forward, ForwardAllowed);
         RegisterCommand(nameof(Home), Home, HomeAllowed);
         RegisterCommand(nameof(Reload), Reload, ReloadAllowed);
         RegisterCommand(nameof(DetachWindow), DetachWindow, DetachWindowAllowed);
         RegisterCommand(nameof(ColorTheme), ColorTheme, ColorThemeAllowed);
         RegisterCommand(nameof(ChangePasswordCommand), ChangePasswordCommand, ChangePasswordCommandAllowed);
         RegisterCommand(nameof(SignOutCommand), SignOutCommand, SignOutCommandAllowed);
         RegisterCommand(nameof(ExitSimulationCommand), ExitSimulationCommand);
      }
      /// <summary>
      /// The stack shown by this host. The host follows that stack itself - the entry being shown and the
      /// content of its path - so no one else needs to push changes into it.
      /// </summary>
      public NavigationStack? Stack {
         get;
         set {
            if (field != null) {
               field.PropertyChanged -= StackPropertyChanged;
               field.Changed -= StackChanged;
            }

            field = value;
            if (field != null) {
               field.PropertyChanged += StackPropertyChanged;
               field.Changed += StackChanged;
            }

            Entry = field?.Current;
         }
      }

      /// <summary>The entry being shown, or <c>null</c> before anything has ever been shown.</summary>
      public NavigationEntry? Entry {
         get;
         private set {
            if (field == value) return;

            // The toolbar switches belong to the navigation, so the host has to stop listening to the
            // one it is leaving - a navigation that stays alive in the stack keeps raising changes, and
            // an old subscription would let it repaint a toolbar it no longer owns. The title needs no
            // such care: it is bound through Entry, which WPF already follows on its own.
            if (field != null) field.Navigation.PropertyChanged -= NavigationPropertyChanged;
            var leaving = field;
            field = value;
            if (field != null) field.Navigation.PropertyChanged += NavigationPropertyChanged;

            // Raised before NavigationBody is announced: the view still shows the body being left at
            // this point, and that is the one it has to take a picture of to slide out.
            if (SlideDirection(leaving, value) is { } forward) BodySwitching?.Invoke(forward);

            NotifyChanged();
            NotifyChanged(nameof(Navigation));
            NotifyChanged(nameof(NavigationBody));
            RefreshToolbarVisibility();
            RefreshNavigationCommands();
         }
      }

      // Tells the view which way to slide, true for forward. Only raised when one shown entry replaces
      // another; the first entry a host ever shows simply appears.
      internal event Action<bool>? BodySwitching;

      // The direction follows the position on the path, home counting as -1 since it sits in front of
      // it. Every move keeps the entry being left on the path until the new one is current, so both
      // positions are still valid here - Back, Home, closing and detaching all land further left, a
      // new screen or Forward further right.
      private bool? SlideDirection(NavigationEntry? leaving, NavigationEntry? entering) {
         if (Stack == null || leaving == null || entering == null) return null;

         var from = leaving == Stack.Home ? -1 : Stack.IndexOf(leaving);
         var to = entering == Stack.Home ? -1 : Stack.IndexOf(entering);
         return from == to ? null : to > from;
      }

      /// <summary>The definition of the screen being shown; the source of the toolbar switches.</summary>
      public Navigation? Navigation => Entry?.Navigation;

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Entry = Stack?.Current;
      }

      private void StackChanged(object? sender, EventArgs e) => RefreshNavigationCommands();

      private void NavigationPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (string.IsNullOrEmpty(e.PropertyName) || ToolbarVisibilityNames.Contains(e.PropertyName))
            RefreshToolbarVisibility();
      }

      // Back/Forward/Home read the stack, not just the entry being shown, so they are asked again
      // whenever the stack changes - the current entry is swapped before the path has finished
      // changing, which would otherwise leave Forward enabled on an empty stack.
      internal void RefreshNavigationCommands() {
         Commands[nameof(Back)]!.RaiseCanExecuteChanged();
         Commands[nameof(Home)]!.RaiseCanExecuteChanged();
         Commands[nameof(Forward)]!.RaiseCanExecuteChanged();
         Commands[nameof(Reload)]!.RaiseCanExecuteChanged();
         Commands[nameof(DetachWindow)]!.RaiseCanExecuteChanged();
      }
      // The ContentControl binds to this the moment the XAML DataContext is created, which is long
      // before the first navigation lands, so both the entry and its body have to stay optional
      // here - a throwing getter takes the whole binding down with it.
      /// <summary>The navigation body.</summary>
      public UserControl? NavigationBody => Entry?.Body as UserControl;
      
      // Each switch is owned by the navigation being shown; the host only translates it for XAML.
      // A navigation is assigned long after the DataContext is built, so a null one still has to
      // render something - the toolbar stays complete until a navigation says otherwise.
      /// <summary>The is toolbar visible.</summary>
      public Visibility IsToolbarVisible => ToVisibility(Navigation?.IsToolbarVisible);
      /// <summary>The is title visible.</summary>
      public Visibility IsTitleVisible => ToVisibility(Navigation?.IsTitleVisible);
      /// <summary>The is back visible.</summary>
      public Visibility IsBackVisible => ToVisibility(Navigation?.IsBackVisible);
      /// <summary>The is forward visible.</summary>
      public Visibility IsForwardVisible => ToVisibility(Navigation?.IsForwardVisible);
      /// <summary>The is reload visible.</summary>
      public Visibility IsReloadVisible => ToVisibility(Navigation?.IsReloadVisible);
      // A detached window has no home to go back to, and the session is managed from the main window
      // alone, so both switches are off there whatever the navigation says.
      /// <summary>The is home visible.</summary>
      public Visibility IsHomeVisible => IsMainHost ? ToVisibility(Navigation?.IsHomeVisible) : Visibility.Collapsed;
      /// <summary>The is detach visible.</summary>
      public Visibility IsDetachVisible =>
         Entry != null && Entry == Stack?.Home ? Visibility.Collapsed : ToVisibility(Navigation?.IsDetachVisible);
      /// <summary>The is color theme visible.</summary>
      public Visibility IsColorThemeVisible => ToVisibility(Navigation?.IsColorThemeVisible);
      /// <summary>The is user visible.</summary>
      public Visibility IsUserVisible => IsMainHost ? ToVisibility(Navigation?.IsUserVisible) : Visibility.Collapsed;

      /// <summary>
      /// <c>true</c> when this host shows the application's main stack in the main window; <c>false</c> for a
      /// host inside a detached window.
      /// </summary>
      public bool IsMainHost => Stack != null && EmApp != null && Stack == EmApp.MainStack;

      private static Visibility ToVisibility(bool? flag) =>
         flag != false ? Visibility.Visible : Visibility.Collapsed;

      // Both sides name the same switch identically, so one list covers telling XAML to re-read
      // them all and recognising which navigation changes are worth reacting to.
      private static readonly string[] ToolbarVisibilityNames = [
         nameof(IsToolbarVisible), nameof(IsTitleVisible), nameof(IsBackVisible),
         nameof(IsForwardVisible), nameof(IsReloadVisible), nameof(IsHomeVisible),
         nameof(IsDetachVisible), nameof(IsColorThemeVisible), nameof(IsUserVisible),
      ];

      internal void RefreshToolbarVisibility() {
         foreach (var name in ToolbarVisibilityNames)
            NotifyChanged(name);
      }

      #region Account

      /// <summary>
      /// Whether the account menu (the dropdown behind the user button) is open. The button and its popup are
      /// both bound to this property, so their states never differ - and a command inside the menu can close
      /// the menu just by setting <c>false</c> here.
      /// </summary>
      public bool IsUserMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// The user who is active, or <c>null</c> while nobody has signed in. Read again from the application
      /// every time <see cref="RefreshActiveUser"/> is called.
      /// </summary>
      public User? ActiveUser => EmApp?.ActiveUser;

      /// <summary>
      /// The full name of the active user to show in the account menu; falls back to the account name if the
      /// full name is empty, and to the "not signed in" text if there is no user at all.
      /// </summary>
      public string ActiveUserDisplayName => UserAvatar.DisplayName(ActiveUser);

      /// <summary>
      /// The account name of the active user (the second line in the account menu), empty when nobody has
      /// signed in.
      /// </summary>
      public string ActiveUserAccount => UserAvatar.Account(ActiveUser);

      /// <summary>
      /// The initials of the active user, used as an avatar, e.g. "SYSTEM DEBUGGER" becomes "SD". It always
      /// holds something: <c>?</c> while nobody has signed in, so the avatar circle never appears empty.
      /// </summary>
      public string ActiveUserInitials => UserAvatar.Initials(ActiveUser);

      /// <summary>
      /// The color of the active user's avatar circle. Chosen from a fixed palette based on the account's
      /// identity, so the same person always gets the same color; a neutral gray while nobody has signed in.
      /// </summary>
      public SolidColorBrush ActiveUserAvatarBrush => UserAvatar.Brush(ActiveUser);

      /// <summary>
      /// The tooltip of the account button: the user's name, and in debug mode a reminder when the developer
      /// is acting as another account through Switch User.
      /// </summary>
      public string AccountToolTip =>
         EmApp is { IsDebugActive: true } app && ActiveUser is { } user && !app.IsDebugBypass
            ? $"{ActiveUserDisplayName}\nDebug: acting as {user.cUserAccount}"
            : ActiveUserDisplayName;

      /// <summary>
      /// Visibility of Sign Out in the account menu. Hidden while debug is active - the debugger account
      /// never signed in, and Simulate Login is the way to test signing in and out - and shown otherwise,
      /// simulation included.
      /// </summary>
      public Visibility SignOutVisibility =>
         EmApp is { } app && (!app.IsDebugMode || app.IsSimulatingLogin) ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibility of the SIMULATED chip and its Exit button: only on the main host, while somebody is signed
      /// in during Simulate Login.
      /// </summary>
      public Visibility SimulatedChipVisibility =>
         IsMainHost && EmApp is { IsSimulatingLogin: true, ActiveUser: not null }
            ? Visibility.Visible
            : Visibility.Collapsed;

      /// <summary>
      /// Tells the UI to read the active user's identity again. It needs to be called by hand because its
      /// owner (<see cref="Core.EmApp.ActiveUser"/>) is not a source of bindings with notification.
      /// </summary>
      public void RefreshActiveUser() {
         NotifyChanged(nameof(ActiveUser));
         NotifyChanged(nameof(ActiveUserDisplayName));
         NotifyChanged(nameof(ActiveUserAccount));
         NotifyChanged(nameof(ActiveUserInitials));
         NotifyChanged(nameof(ActiveUserAvatarBrush));
         NotifyChanged(nameof(AccountToolTip));
         NotifyChanged(nameof(SignOutVisibility));
         NotifyChanged(nameof(SimulatedChipVisibility));
         Commands[nameof(ChangePasswordCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(SignOutCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>
      /// Opens the active user's change password screen. The screen does not exist yet, so this command is
      /// still empty and <see cref="ChangePasswordCommandAllowed"/> always refuses - its button deliberately
      /// stays in the menu so its place is already settled when the screen follows.
      /// </summary>
      public void ChangePasswordCommand() {
         IsUserMenuOpen = false;
      }

      /// <summary>Whether the change password command may run now.</summary>
      public bool ChangePasswordCommandAllowed() => false;

      /// <summary>
      /// Ends the session: closes the account menu, then asks the application to discard its session - on the
      /// server as well as on the client side. The path is the same as the account button of the multi-tab
      /// layout, so signing out from either layout ends in the same state.
      /// </summary>
      public async Task SignOutCommand() {
         IsUserMenuOpen = false;

         // Nothing may escape from here. ICommand.Execute is void, so UiCommandAsync runs it as async void: an
         // exception that comes out is rethrown on the dispatcher, and this application has no
         // DispatcherUnhandledException to catch it.
         try {
            await EmApp!.SignOutAsync();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Whether the sign out command may run now.</summary>
      public bool SignOutCommandAllowed() => EmApp != null;

      /// <summary>
      /// Leaves Simulate Login and goes back to the debugger account, signing the simulated session out of
      /// the server first.
      /// </summary>
      public async Task ExitSimulationCommand() {
         IsUserMenuOpen = false;

         try {
            await EmApp!.EndLoginSimulationAsync();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      #endregion

      async Task Back() {
         await Stack!.Backward();
      }
      bool BackAllowed() {
         // later it may be that going back from a module is not allowed under certain conditions
         return Stack?.CanGoBack == true;
      }

      async Task Forward() {
         await Stack!.Forward();
      }
      bool ForwardAllowed() {
         return Stack?.CanGoForward == true;
      }

      async Task Home() {
         await Stack!.NavigateHome();
      }
      bool HomeAllowed() {
         return Stack?.Home != null && Entry != null && Entry != Stack.Home;
      }
      async Task Reload() {
         await Entry!.Reload();
      }
      bool ReloadAllowed() {
         return Entry != null;
      }
      // The entry being shown moves to a window of its own, body and unsaved input included.
      async Task DetachWindow() {
         await EmApp!.DetachAsync(Entry!);
      }
      bool DetachWindowAllowed() {
         return Entry != null && EmApp?.CanDetach(Entry) == true;
      }
      // The theme belongs to the whole application, so switching it here - from the main window or a
      // detached one - repaints every window at once.
      void ColorTheme() {
         EmApp!.CurrentTheme = EmApp!.CurrentTheme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
      }
      bool ColorThemeAllowed() {
         return true;
      }
   }
}
