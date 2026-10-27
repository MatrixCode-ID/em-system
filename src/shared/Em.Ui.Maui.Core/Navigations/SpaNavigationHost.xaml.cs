using System.ComponentModel;
using Microsoft.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>The exclusive modal layer displayed by the shell.</summary>
   public enum ShellOverlay { None, Account, Tasks }

   /// <summary>Adaptive single-page host with one modal overlay and a shared task cache.</summary>
   public partial class SpaNavigationHost : ContentPage
   {
      private readonly IDispatcherTimer _hubTimer;
      private Window? _window;
      private bool _detached;
      private VisualElement? _opener;
      private readonly Controls.MotionPolicy _taskMotion = new(), _bodyMotion = new(), _scrimMotion = new();
      private NavigationEntry? _presentedEntry;
      private bool _presentedCatalogue;
#if ANDROID
      private Android.Views.View? _nativeRoot, _keyTarget;
      private Android.Views.ViewTreeObserver? _focusObserver;
#endif
      public SpaNavigationHost(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app; Vm.HostPage = this; Vm.PropertyChanged += VmOnPropertyChanged; Vm.Stack = app.MainStack;
         AccountPanelView.Vm.EmApp = app; AccountPanelView.Vm.HostPage = this;
         AccountPanelView.Vm.CloseRequested += CloseRequested;
         TasksPanel.Vm.EmApp = app; TasksPanel.Vm.HostPage = this; TasksPanel.Vm.CloseRequested += CloseRequested;
         app.ActiveUserChanged += OnAccountChanged; app.ActiveConnectionChanged += OnAccountChanged;
         app.ThemeChanged += OnThemeChanged; app.SessionEnded += OnSessionEnded; app.HubTasksChanged += OnHubTasksChanged;
         _hubTimer = Dispatcher.CreateTimer(); _hubTimer.Interval = TimeSpan.FromSeconds(60); _hubTimer.Tick += OnHubTimerTick; _hubTimer.Start();
         SizeChanged += OnSizeChanged; Vm.RefreshAccount(); TasksPanel.Vm.Update(); _ = app.RefreshHubTasksAsync();
      }
      public SpaNavigationHostVm Vm => (SpaNavigationHostVm)BindingContext;
      protected override void OnHandlerChanged() {
         base.OnHandlerChanged();
#if ANDROID
         ClearNativeKeys();
#endif
         if (_detached) return;
         if (_window is not null) { _window.Resumed -= OnWindowResumed; _window.Stopped -= OnWindowStopped; }
         _window = Window;
         if (_window is not null) { _window.Resumed += OnWindowResumed; _window.Stopped += OnWindowStopped; }
#if ANDROID
         if (Handler?.PlatformView is Android.Views.View native) {
            _nativeRoot = native; native.KeyPress += OnNativeKey;
            _focusObserver = native.ViewTreeObserver;
            if (_focusObserver is not null) _focusObserver.GlobalFocusChange += OnNativeFocusChanged;
            SetKeyTarget(native.FindFocus());
         }
#endif
      }
#if ANDROID
      private void SetKeyTarget(Android.Views.View? target) {
         if (_keyTarget is not null && _keyTarget != _nativeRoot) _keyTarget.KeyPress -= OnNativeKey;
         _keyTarget = target;
         if (_keyTarget is not null && _keyTarget != _nativeRoot) _keyTarget.KeyPress += OnNativeKey;
      }
      private void OnNativeFocusChanged(object? sender, Android.Views.ViewTreeObserver.GlobalFocusChangeEventArgs e) => SetKeyTarget(e.NewFocus);
      private void ClearNativeKeys() {
         SetKeyTarget(null);
         if (_nativeRoot is not null) _nativeRoot.KeyPress -= OnNativeKey;
         if (_focusObserver is { IsAlive: true }) _focusObserver.GlobalFocusChange -= OnNativeFocusChanged;
         _focusObserver = null; _nativeRoot = null;
      }
      private void OnNativeKey(object? sender, Android.Views.View.KeyEventArgs e) {
         if (e.KeyCode == Android.Views.Keycode.Escape && Vm.Overlay != ShellOverlay.None) { Vm.Overlay = ShellOverlay.None; e.Handled = true; }
         else if (e.KeyCode == Android.Views.Keycode.Escape && BodyHost.Content is DefaultHomeControl { Vm.IsContextMenuOpen: true } home) { home.Vm.CloseMenuCommand(); e.Handled = true; }
      }
#endif
      private void OnSizeChanged(object? sender, EventArgs e) {
         Vm.IsCompact = Width < 600;
         var width = Math.Max(0, Width);
         TasksPanel.WidthRequest = width < 600 ? width : Math.Min(400, width);
         AccountPanelView.WidthRequest = Math.Min(400, Math.Max(0, width - 48));
      }
      private void CloseRequested(object? sender, EventArgs e) => Vm.Overlay = ShellOverlay.None;
      private void OnWindowResumed(object? sender, EventArgs e) { _hubTimer.Start(); _ = Vm.EmApp?.RefreshHubTasksAsync(); }
      private void OnWindowStopped(object? sender, EventArgs e) => _hubTimer.Stop();
      private void OnHubTimerTick(object? sender, EventArgs e) => _ = Vm.EmApp?.RefreshHubTasksAsync();
      private void OnHubTasksChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(() => { if (_detached) return; Vm.RefreshHubTasks(); TasksPanel.Vm.Update(); });
      private void OnAccountChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(() => {
         if (_detached) return;
         ResetMotion();
         Vm.Overlay = ShellOverlay.None; Vm.RefreshAccount();
         if (Vm.Stack?.Home?.Body is DefaultHomeControl home) _ = home.Vm.ReloadAsync();
         TasksPanel.Vm.Update(); _ = Vm.EmApp?.RefreshHubTasksAsync();
      });
      private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(() => { if (!_detached) AccountPanelView.Vm.RefreshTheme(); });
      private void OnSessionEnded(object? sender, SessionEndedEventArgs e) => Dispatcher.Dispatch(() => {
         if (_detached) return;
         Vm.EmApp!.ResetHubTasks(); Vm.Overlay = ShellOverlay.None; _ = Vm.EmApp.ShowLoginScreen(e.Reason);
      });
      internal void Detach() {
         _detached = true; Vm.Overlay = ShellOverlay.None;
         ResetMotion();
         var app = Vm.EmApp!;
         app.ActiveUserChanged -= OnAccountChanged; app.ActiveConnectionChanged -= OnAccountChanged;
         app.ThemeChanged -= OnThemeChanged; app.SessionEnded -= OnSessionEnded; app.HubTasksChanged -= OnHubTasksChanged;
         _hubTimer.Stop(); _hubTimer.Tick -= OnHubTimerTick;
         if (_window is not null) { _window.Resumed -= OnWindowResumed; _window.Stopped -= OnWindowStopped; _window = null; }
         AccountPanelView.Vm.CloseRequested -= CloseRequested; TasksPanel.Vm.CloseRequested -= CloseRequested;
         TasksPanel.Vm.Release();
         SizeChanged -= OnSizeChanged; Vm.PropertyChanged -= VmOnPropertyChanged; Vm.Stack = null;
#if ANDROID
         ClearNativeKeys();
#endif
      }
      private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (_detached) return;
         if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(() => VmOnPropertyChanged(sender, e)); return; }
         if (e.PropertyName == nameof(SpaNavigationHostVm.Entry)) {
            BodyHost.Content = Vm.Entry?.Body as View;
            AnimateBody();
         }
         else if (e.PropertyName == nameof(SpaNavigationHostVm.IsAllModulesWorkspace)) AnimateBody();
         else if (e.PropertyName == nameof(SpaNavigationHostVm.Overlay)) ApplyOverlay();
      }
      private void AnimateBody() {
         if (_presentedEntry == Vm.Entry && _presentedCatalogue == Vm.IsAllModulesWorkspace) return;
         var animate = _presentedEntry is not null && BodyHost.Content is not null;
         var entries = Vm.Stack?.Entries.ToList();
         var sourceIndex = _presentedEntry is null ? -1 : entries?.IndexOf(_presentedEntry) ?? -1;
         var targetIndex = Vm.Entry is null ? -1 : entries?.IndexOf(Vm.Entry) ?? -1;
         var backward = (_presentedEntry != Vm.Entry && Vm.Entry == Vm.Stack?.Home)
            || (_presentedEntry == Vm.Entry && _presentedCatalogue && !Vm.IsAllModulesWorkspace)
            || (sourceIndex >= 0 && targetIndex >= 0 && targetIndex < sourceIndex);
         _presentedEntry = Vm.Entry; _presentedCatalogue = Vm.IsAllModulesWorkspace;
         if (animate) _ = _bodyMotion.SwipeAsync(BodyHost, backward);
         else { _bodyMotion.Cancel(BodyHost); BodyHost.TranslationX = 0; BodyHost.TranslationY = 0; BodyHost.Opacity = 1; }
      }
      private void ResetMotion() {
         AccountPanelView.StopMotion(); _taskMotion.Cancel(TasksPanel); TasksPanel.IsVisible = false;
         _scrimMotion.Cancel(Scrim); Scrim.IsVisible = false; Scrim.InputTransparent = true; Scrim.Opacity = 0;
         _bodyMotion.Cancel(BodyHost); BodyHost.TranslationX = 0; BodyHost.TranslationY = 0; BodyHost.Opacity = 1;
         MainContent.InputTransparent = false;
      }
      private static void SetSurfaceAccess(VisualElement view, bool active) {
         view.InputTransparent = !active;
#if ANDROID
         if (view.Handler?.PlatformView is Android.Views.ViewGroup native) {
            native.ImportantForAccessibility = active ? Android.Views.ImportantForAccessibility.Auto : Android.Views.ImportantForAccessibility.NoHideDescendants;
            native.DescendantFocusability = active ? Android.Views.DescendantFocusability.AfterDescendants : Android.Views.DescendantFocusability.BlockDescendants;
         }
#endif
      }
      private void ApplyOverlay() {
         var overlay = Vm.Overlay;
         var open = overlay != ShellOverlay.None;
         if (open && BodyHost.Content is DefaultHomeControl home) home.Vm.CloseMenuCommand();
         if (open) _opener = overlay == ShellOverlay.Tasks ? TasksButton.FocusTarget : AccountButton;
         // Logical input/focus state changes immediately; motion only affects presentation.
         SetSurfaceAccess(AccountPanelView, overlay == ShellOverlay.Account);
         SetSurfaceAccess(TasksPanel, overlay == ShellOverlay.Tasks);
         _ = AccountPanelView.AnimateAsync(overlay == ShellOverlay.Account, Vm.Navigation);
         _ = _taskMotion.SetOpenAsync(TasksPanel, overlay == ShellOverlay.Tasks, Math.Max(0, TasksPanel.WidthRequest));
         TasksPanel.Vm.IsActive = overlay == ShellOverlay.Tasks;
         Scrim.IsVisible = open; Scrim.InputTransparent = !open;
         if (open) _ = _scrimMotion.EnterAsync(Scrim, 0, .4);
         else { _scrimMotion.Cancel(Scrim); Scrim.Opacity = 0; }
         MainContent.InputTransparent = open;
#if ANDROID
         if (MainContent.Handler?.PlatformView is Android.Views.ViewGroup background) {
            background.ImportantForAccessibility = open ? Android.Views.ImportantForAccessibility.NoHideDescendants : Android.Views.ImportantForAccessibility.Auto;
            background.DescendantFocusability = open ? Android.Views.DescendantFocusability.BlockDescendants : Android.Views.DescendantFocusability.AfterDescendants;
         }
#endif
         if (overlay == ShellOverlay.Account) { AccountPanelView.FindByName<Controls.IconButton>("CloseButton")?.Focus(); }
         else if (overlay == ShellOverlay.Tasks) { TasksPanel.Vm.Update(); TasksPanel.FocusFirst(); _ = Vm.EmApp?.RefreshHubTasksAsync(); }
         else { _opener?.Focus(); _opener = null; }
      }
      protected override bool OnBackButtonPressed() {
         if (Vm.Overlay != ShellOverlay.None) { Vm.Overlay = ShellOverlay.None; return true; }
         if (BodyHost.Content is DefaultHomeControl { Vm.IsContextMenuOpen: true } menuHome) { menuHome.Vm.CloseMenuCommand(); return true; }
         if (BodyHost.Content is DefaultHomeControl { Vm.IsCatalogueOpen: true } home) { home.Vm.BackToHomeCommand(); return true; }
         if (Vm.Stack is not { CanGoBack: true } stack) return base.OnBackButtonPressed();
         _ = stack.Backward(); return true;
      }
   }

   /// <summary>View model of the single-page navigation host.</summary>
   public class SpaNavigationHostVm : MvvmModelBase
   {
      public SpaNavigationHostVm() {
         RegisterCommand(nameof(LeadingCommand), LeadingCommand, LeadingCommandAllowed);
         RegisterCommand(nameof(HomeCommand), HomeCommand, HomeCommandAllowed);
         RegisterCommand(nameof(AllModulesCommand), AllModulesCommand, AllModulesCommandAllowed);
         RegisterCommand(nameof(ForwardCommand), ForwardCommand, ForwardCommandAllowed);
         RegisterCommand(nameof(ReloadCommand), ReloadCommand, ReloadCommandAllowed);
         RegisterCommand(nameof(AccountPanelCommand), AccountPanelCommand, AccountPanelCommandAllowed);
         RegisterCommand(nameof(CloseAccountPanelCommand), CloseAccountPanelCommand);
         RegisterCommand(nameof(HubTasksCommand), HubTasksCommand, HubTasksCommandAllowed);
         RegisterCommand(nameof(ExitSimulationCommand), ExitSimulationCommand, ExitSimulationCommandAllowed);
      }

      /// <summary>Shows the return action throughout an authenticated simulation.</summary>
      public bool IsSimulationVisible => EmApp is { IsSimulatingLogin: true, ActiveUser: not null };

      /// <summary>Returns to SYSTEM DEBUGGER after closing the simulated workspace.</summary>
      public async Task ExitSimulationCommand() {
         if (!ExitSimulationCommandAllowed()) return;
         try {
            IsBusy = true;
            Commands[nameof(ExitSimulationCommand)]?.RaiseCanExecuteChanged();
            IsAccountPanelOpen = false;
            await EmApp!.EndLoginSimulationAsync();
         }
         catch (Exception x) { AlertError(x); }
         finally {
            IsBusy = false;
            Commands[nameof(ExitSimulationCommand)]?.RaiseCanExecuteChanged();
         }
      }

      /// <summary>Prevents repeated requests while the simulation is ending.</summary>
      public bool ExitSimulationCommandAllowed() => IsNotBusy && IsSimulationVisible;

      /// <summary>The number of work items waiting for the user, shown on the inbox button.</summary>
      public int HubTaskCount => EmApp?.HubTaskCount ?? 0;

      /// <summary>Task access is available after sign-in even when the count is unknown or zero.</summary>
      public bool HasHubTasks => IsUserVisible && EmApp?.ActiveUser is not null;
      public bool HasTaskBadge => HasHubTasks && HubTaskCount > 0;
      public string HubTaskBadge => HubTaskCount > 99 ? "99+" : HubTaskCount.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
      public string HubTaskAccessibleName => EmApp?.HubTaskRefreshedAt is null ? "My Tasks, count not loaded" : $"My Tasks, {HubTaskCount} waiting";
      public void RefreshHubTasks() {
         foreach (var name in new[] { nameof(HubTaskCount), nameof(HasHubTasks), nameof(HasTaskBadge), nameof(HubTaskBadge), nameof(HubTaskAccessibleName) }) NotifyChanged(name);
         Commands[nameof(HubTasksCommand)]?.RaiseCanExecuteChanged();
      }
      public Task HubTasksCommand() { Overlay = Overlay == ShellOverlay.Tasks ? ShellOverlay.None : ShellOverlay.Tasks; return Task.CompletedTask; }
      public bool HubTasksCommandAllowed() => HasHubTasks;
      public ShellOverlay Overlay {
         get => Get<ShellOverlay>();
         set => Set(value, _ => NotifyChanged(nameof(IsAccountPanelOpen)));
      }
      public bool IsAccountPanelOpen { get => Overlay == ShellOverlay.Account; set { if (value) Overlay = ShellOverlay.Account; else if (IsAccountPanelOpen) Overlay = ShellOverlay.None; } }
      public bool IsCompact { get => Get<bool>(); set => Set(value, _ => RefreshNavigationCommands()); }
      public bool ShowHomeAction => !IsCompact && IsHomeVisible;
      public bool ShowReloadAction => !IsCompact && IsReloadVisible;
      /// <summary>The home workspace uses host branding instead of a second screen title.</summary>
      public bool IsHomeWorkspace => IsAtHome;
      /// <summary>Marks Home only while the pinned shortlist is displayed.</summary>
      public bool IsHomeTabSelected => IsAtHome && !IsAllModulesWorkspace;
      /// <summary>Marks the complete catalogue, including when opened through View all.</summary>
      public bool IsAllModulesWorkspace => IsAtHome && Entry?.Body is DefaultHomeControl { Vm.IsCatalogueOpen: true };
      /// <summary>The title area used for ordinary module pages.</summary>
      public bool ShowScreenTitle => !IsAtHome && IsTitleVisible;
      /// <summary>The account stays at the right edge on every workspace screen.</summary>
      public int AccountColumn => 6;
      /// <summary>The host's logo, without a product-specific dependency.</summary>
      public Microsoft.Maui.Controls.ImageSource? LogoImage => EmApp is { } app ? BrandingImages.LoadLogo(app.Branding) : null;
      /// <summary>Bottom navigation is only present in an authenticated workspace.</summary>
      public bool ShowBottomNavigation => HasHubTasks && IsToolbarVisible;

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

            Overlay = ShellOverlay.None;
            // The title belongs to the entry and may be renamed while it is shown, so the page has to
            // stop listening to the one it is leaving - an entry that stays alive in the stack keeps
            // raising changes, and an old subscription would let it repaint a bar it no longer owns.
            if (field != null) field.PropertyChanged -= EntryPropertyChanged;
            if (field?.Body is DefaultHomeControl previousHome) previousHome.Vm.PropertyChanged -= HomePropertyChanged;
            field = value;
            if (field != null) field.PropertyChanged += EntryPropertyChanged;
            if (field?.Body is DefaultHomeControl currentHome) currentHome.Vm.PropertyChanged += HomePropertyChanged;

            NotifyChanged();
            NotifyChanged(nameof(Navigation));
            NotifyChanged(nameof(IsToolbarVisible));
            NotifyChanged(nameof(IsTitleVisible));
            NotifyChanged(nameof(IsReloadVisible));
            RefreshNavigationCommands();
         }
      }

      /// <summary>The definition of the screen being shown; the source of the toolbar switches.</summary>
      public Navigation? Navigation => Entry?.Navigation;

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Entry = Stack?.Current;
      }

      private void StackChanged(object? sender, EventArgs e) => RefreshNavigationCommands();

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) NotifyChanged(nameof(Title));
      }

      private void HomePropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName != nameof(DefaultHomeControlVm.IsCatalogueOpen)) return;
         NotifyChanged(nameof(IsHomeTabSelected)); NotifyChanged(nameof(IsAllModulesWorkspace));
      }

      // Home is never an entry of the navigation path, so "being at home" means that what is shown is the
      // home entry itself - or that nothing has been shown at all yet.
      private bool IsAtHome => Stack?.Current is null || Stack.Current == Stack.Home;

      /// <summary>The title in the top bar: the application name at home, the title of its entry on another screen.</summary>
      public string Title => IsAtHome
         ? EmApp?.Branding.DisplayTitle ?? string.Empty
         : Entry?.Title ?? string.Empty;

      /// <summary>The caption below the title.</summary>
      public string Subtitle => IsAtHome
         ? EmApp?.Branding.DisplayTagline ?? string.Empty
         : Navigation?.Subtitle ?? string.Empty;

      /// <summary><c>true</c> when there is a caption to draw below the title.</summary>
      public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

      /// <summary>Whether there is still a screen ahead that can be gone to.</summary>
      /// <remarks>
      /// There is no button for it in the Android top bar, but the path is deliberately kept: this host can be
      /// used again in MAUI desktop, where going forward has meaning and its own place.
      /// </remarks>
      public bool CanGoForward {
         get {
            if (Navigation is null) return false;
            if (!Navigation.IsForwardVisible) return false;

            return Stack?.CanGoForward == true;
         }
      }

      // Every top bar switch is owned by its navigation. Without a navigation at all there is nothing to draw,
      // so the answer is false - not a default value owned by Navigation.
      public bool IsToolbarVisible => EmApp?.ActiveUser is not null && (Navigation?.IsToolbarVisible ?? false);
      public bool IsTitleVisible => Navigation?.IsTitleVisible ?? false;
      public bool IsReloadVisible => Navigation?.IsReloadVisible ?? false;

      /// <summary>
      /// Whether the back button needs to be drawn. At home the answer is always no: its navigation path is
      /// still empty, and a back button that leads nowhere only confuses.
      /// </summary>
      public bool IsBackVisible => !IsAtHome && (Navigation?.IsBackVisible ?? false);

      /// <summary>Whether the home button needs to be drawn; like back, it is of no use at home.</summary>
      public bool IsHomeVisible => !IsAtHome && (Navigation?.IsHomeVisible ?? false);

      /// <summary>
      /// Whether the account badge needs to be drawn. Unlike back and home, this badge is on every screen
      /// including home - it is the one and only way to the account panel.
      /// </summary>
      public bool IsUserVisible => EmApp?.ActiveUser is not null && (Navigation?.IsUserVisible ?? false);

      /// <summary>
      /// The initials of the signed-in account, the content of the badge circle. The only account information
      /// this page still draws - the rest belongs to the account panel. The full name comes first, and the
      /// account name is the fallback when the contact has no name yet.
      /// </summary>
      public string AccountInitials => EmApp?.ActiveUser is { } user
         ? (user.cContactFullName is { Length: > 0 } fullName ? fullName : user.cUserAccount).ToInitials()
         : "?";

      /// <summary>
      /// Asks all top bar buttons to recompute whether they may still be pressed. Called every time the
      /// content of the navigation path changes.
      /// </summary>
      public void RefreshNavigationCommands() {
         NotifyChanged(nameof(ShowHomeAction)); NotifyChanged(nameof(ShowReloadAction));
         NotifyChanged(nameof(CanGoForward));
         NotifyChanged(nameof(IsBackVisible));
         NotifyChanged(nameof(IsHomeVisible));
         NotifyChanged(nameof(IsUserVisible));
         NotifyChanged(nameof(HasHubTasks));
         NotifyChanged(nameof(Title));
         NotifyChanged(nameof(Subtitle));
         NotifyChanged(nameof(HasSubtitle));
         foreach (var name in new[] { nameof(IsHomeWorkspace), nameof(ShowScreenTitle), nameof(AccountColumn), nameof(LogoImage), nameof(ShowBottomNavigation) }) NotifyChanged(name);
         NotifyChanged(nameof(IsHomeTabSelected)); NotifyChanged(nameof(IsAllModulesWorkspace));
         Commands[nameof(LeadingCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(HomeCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(AllModulesCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ForwardCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ReloadCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(HubTasksCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(AccountPanelCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>Redraws the account badge after the user or the server changed.</summary>
      public void RefreshAccount() {
         NotifyChanged(nameof(IsToolbarVisible)); NotifyChanged(nameof(IsUserVisible)); RefreshHubTasks();
         NotifyChanged(nameof(ShowBottomNavigation)); NotifyChanged(nameof(LogoImage));
         NotifyChanged(nameof(AccountInitials));
         NotifyChanged(nameof(IsSimulationVisible));
         Commands[nameof(ExitSimulationCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(AccountPanelCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(AllModulesCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>The leftmost button of the top bar: go back one screen.</summary>
      public Task LeadingCommand() { if (Overlay != ShellOverlay.None) { Overlay = ShellOverlay.None; return Task.CompletedTask; } return Stack!.Backward(); }
      public bool LeadingCommandAllowed() => Stack?.CanGoBack == true;

      /// <summary>Go home from any screen, without having to go back step by step.</summary>
      public Task HomeCommand() {
         Overlay = ShellOverlay.None;
         if (Stack?.Home?.Body is DefaultHomeControl home) home.Vm.BackToHomeCommand();
         return Stack!.NavigateHome();
      }
      public bool HomeCommandAllowed() => Stack?.Home is not null;

      /// <summary>Opens the existing catalogue after navigation home has completed.</summary>
      public async Task AllModulesCommand() {
         if (!AllModulesCommandAllowed()) return;
         Overlay = ShellOverlay.None;
         var stack = Stack!; var user = EmApp!.ActiveUser; var connection = EmApp.ActiveConnection;
         await stack.NavigateHome();
         if (Stack == stack && stack.Current == stack.Home && ReferenceEquals(user, EmApp?.ActiveUser)
             && ReferenceEquals(connection, EmApp?.ActiveConnection) && stack.Home?.Body is DefaultHomeControl home) home.Vm.ViewAllCommand();
      }
      public bool AllModulesCommandAllowed() => HasHubTasks && Stack?.Home?.Body is DefaultHomeControl;

      public Task ForwardCommand() => Stack!.Forward();
      public bool ForwardCommandAllowed() => CanGoForward;

      public Task ReloadCommand() { Overlay = ShellOverlay.None; return Entry?.Reload() ?? Task.CompletedTask; }
      public bool ReloadCommandAllowed() => Entry is not null;

      /// <summary>Opens - or closes again - the account panel through the badge in the top bar.</summary>
      public void AccountPanelCommand() => IsAccountPanelOpen = !IsAccountPanelOpen;
      public bool AccountPanelCommandAllowed() => EmApp is not null;

      public void CloseAccountPanelCommand() => Overlay = ShellOverlay.None;
   }
}
