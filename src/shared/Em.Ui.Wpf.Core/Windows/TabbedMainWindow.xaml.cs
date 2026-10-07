using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Point = System.Windows.Point;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// The application's main window in both layouts, with its own hand-made chrome: one title row holding
   /// the logo, the Apps menu, the row of tabs, the Tools menu, the theme button, the account button, and
   /// the window caption buttons, then the content card below it.
   /// <para>
   /// In the multi-tab layout every tab is one entry of this window's <see cref="NavigationStack"/>, and
   /// the content card shows the body of the active entry; until someone signs in, the card holds the login
   /// screen and almost the whole title row is hidden. In the single-page layout the content card holds the
   /// navigation host <see cref="EmApp.MainStack"/>, and the title row only holds the logo, the title, and
   /// the caption buttons.
   /// </para>
   /// </summary>
   public partial class TabbedMainWindow : Window
   {
      private readonly EmApp _app;
      private bool _firstLoad = true;
      private bool _closeAgreed;
      private bool _closed;
      private Task _release = Task.CompletedTask;

      /// <summary>
      /// Creates the application's main window. Its constructor only loads the XAML and connects the
      /// application; the window's content is prepared by <see cref="EmApp.Run"/> according to the chosen
      /// layout.
      /// </summary>
      /// <param name="app">The application that owns this window.</param>
      public TabbedMainWindow(EmApp app) : this(app, null) { }

      // A window a tab was torn off into: it shows the given tabbed stack and nothing else of the
      // application's chrome.
      internal TabbedMainWindow(EmApp app, NavigationStack? stack) {
         _app = app;
         InitializeComponent();
         // The application's own icon (BrandingInfo.IconSource) for the taskbar and the title bar, on
         // the main window and on torn-off windows alike; the built-in one stays when none is set.
         if (BrandingImages.LoadIcon(app.Branding) is { } icon) Icon = icon;
         // A ContextMenu opened through IsOpen (not by a right click) gets no PlacementTarget of its
         // own, and its DataContext binding in XAML reads the view model through that target.
         appsMenu.PlacementTarget = appsButton;
         toolsMenu.PlacementTarget = toolsButton;
         // A tab selected from code - the tab list, a menu, a drop from another window - is not
         // scrolled into view by the ListBox on its own; only a click on the tab itself is.
         tabStrip.SelectionChanged += (_, _) => {
            if (tabStrip.SelectedItem != null) tabStrip.ScrollIntoView(tabStrip.SelectedItem);
         };

         // Dialogs asked from this window's own commands and menus appear on this window.
         Vm.MainWindow = this;
         Vm.EmApp = app;
         Vm.Commands[nameof(TabbedMainWindowVm.ColorThemeCommand)]?.RaiseCanExecuteChanged();
         Vm.RequestClose += () => {
            if (!_closed) Close();
         };
         // A new window cascades from this one, since a menu command has no drop point to go by.
         Vm.TearOffRequested += tab => _ = TearOffAsync(tab, new Point(Left + 200, Top + 60));

         if (stack != null) Vm.Stack = stack;
         // Pinning any of the application's windows must pin the launcher, not this version's exe.
         LauncherIntegration.AttachToWindow(this, app.ApplicationName);
      }

      /// <summary>The view model of this window, declared in XAML as the <c>DataContext</c>.</summary>
      public TabbedMainWindowVm Vm => (TabbedMainWindowVm)DataContext;

      // Only the torn-off windows close themselves once empty; the main window never does.
      private bool IsTearOff => Vm.ClosesWhenEmpty;

      /// <summary>
      /// The stack this window shows as tabs, or <c>null</c> in the single-page layout.
      /// </summary>
      public NavigationStack? Stack => Vm.Stack;

      #region Layout

      // The single initialisation point of the main window, called by EmApp.Run right after the
      // window is made and before it shows. Kept apart from the constructor because it reads the
      // application's configuration, which is only complete once BuildApp has finished.
      internal void InitLayout() {
         Vm.Title = _app.ApplicationName;
         // The main window opens where it was closed last time: the same monitor, size, and
         // maximized or not (WindowPlacementStore). With nothing remembered it opens maximized, and
         // restoring it brings it back centred on the screen (WindowStartupLocation in XAML).
         // Torn-off windows are placed by hand instead and never remembered.
         var placement = WindowPlacementStore.Load(_app);
         Vm.WindowState = placement is { IsMaximized: false } ? WindowState.Normal : WindowState.Maximized;
         if (placement is { } remembered)
            SourceInitialized += (_, _) => WindowPlacementStore.Apply(this, remembered);
         Loaded += OnLoaded;
         Vm.AttachTaskHub();

         if (_app.ApplicationLayout == ApplicationLayout.SinglePage) {
            Vm.IsSpaLayout = true;
            Vm.SpaHost = new SpaNavigationHost(_app, _app.MainStack);
            return;
         }

         Vm.Stack = _app.MainStack;
         Vm.IsSignedIn = false;
         Vm.AttachApp();
      }

      // The login screen is on MainStack now. On the single-page layout the host already shows it;
      // on the multi-tab layout it is shown as the whole window rather than as a tab.
      internal void ShowLoginMode() {
         if (!Vm.IsSpaLayout) Vm.IsSignedIn = false;
      }

      // Somebody is in: the single-page layout goes home, the multi-tab layout drops the login screen
      // and opens an empty workspace whose menus are drawn for the user now signed in.
      internal async Task ShowSignedInAsync() {
         var stack = _app.MainStack;
         if (Vm.IsSpaLayout) {
            await stack.NavigateHome();
            if (_app.IsDebugMode) await stack.Home!.Reload();
            return;
         }

         await stack.ReleaseAll();
         Vm.IsSignedIn = true;
         Vm.RefreshUser();
         Vm.RebuildMenus();
         _ = Vm.RebuildMenusWhenClaimsLoadedAsync();
      }

      // Everything the session worked on goes, without asking: the other windows are closed and
      // every body on them released. The multi-tab layout releases its own tabs as well; the
      // single-page layout leaves its path to the login screen, which replaces it anyway.
      internal async Task ReleaseWorkspaceAsync() {
         if (Vm.IsSpaLayout) {
            await _app.CloseDetachedWindowsAsync();
            return;
         }

         await _app.CloseTearOffWindowsAsync();
         await _app.MainStack.ReleaseAll();
      }

      // The theme changed, from wherever: the login screen's Light/Dark indicator is a plain computed
      // property, so it is refreshed by hand. Colours need nothing here - they are theme tokens.
      internal void OnThemeChanged() {
         if (_app.ActiveLoginControl is not { } login) return;

         login.Vm.RefreshThemeState();
      }

      private async void OnLoaded(object sender, RoutedEventArgs e) {
         if (!_firstLoad) return;
         _firstLoad = false;

         try {
            await _app.OnMainWindowLoadedAsync();
         }
         catch (Exception x) {
            this.ShowMboxError(x);
         }
      }

      #endregion

      #region Closing

      // Closing asks first whenever there is a body that could hold input: on the single-page layout
      // the body of every detached window, on the multi-tab layout every tab of every window. The
      // bodies answer asynchronously and a close cannot wait, so the first close is cancelled and
      // repeated once everyone has agreed.
      /// <inheritdoc />
      protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
         base.OnClosing(e);
         if (e.Cancel) return;
         if (_closeAgreed || !NeedsConfirmation()) {
            // The close goes ahead: remember where the main window is. A close that is still being
            // asked about saves nothing yet; it comes back through here once everyone has agreed.
            if (!IsTearOff) WindowPlacementStore.Save(this, _app);
            return;
         }

         e.Cancel = true;
         // Deferred, because Close cannot be called again while this window is still inside its
         // own closing.
         Dispatcher.InvokeAsync(ConfirmCloseAsync);
      }

      private bool NeedsConfirmation() {
         if (Vm.IsSpaLayout) return _app.DetachedWindows.Count > 0;
         if (IsTearOff) return Vm.Stack is { Entries.Count: > 0 };

         // The login screen holds nothing worth keeping, so it is never asked.
         return Vm.IsSignedIn && (_app.MainStack.Entries.Count > 0 || _app.HasTearOffWindows);
      }

      private async Task ConfirmCloseAsync() {
         try {
            if (_closed) return;

            if (Vm.IsSpaLayout) {
               // The body on the main window itself is not asked here, as before.
               if (!await _app.ConfirmCloseDetachedWindowsAsync()) return;
               await _app.CloseDetachedWindowsAsync();
            }
            else if (IsTearOff) {
               if (Vm.Stack is { } stack && !await _app.ConfirmCloseTabsAsync(stack)) return;
            }
            else {
               // Torn-off windows first, then the main window's own tabs; one refusal cancels the
               // whole close with nothing closed or released.
               if (!await _app.ConfirmCloseTearOffWindowsAsync()) return;
               if (!await _app.ConfirmCloseTabsAsync(_app.MainStack)) return;
               await _app.CloseTearOffWindowsAsync();
               await _app.MainStack.ReleaseAll();
            }

            _closeAgreed = true;
            Close();
         }
         catch (Exception x) {
            this.ShowMboxError(x);
         }
      }

      /// <inheritdoc />
      protected override void OnClosed(EventArgs e) {
         _closed = true;
         Vm.DetachApp();
         Vm.DetachTaskHub();

         if (IsTearOff) {
            _app.UnregisterTearOffWindow(this);
            var stack = Vm.Stack;
            Vm.Stack = null;
            if (stack != null) _release = ReleaseStackAsync(stack);
         }

         base.OnClosed(e);
      }

      // Every body left on a torn-off window is released once the window is gone - nothing of it is
      // mounted anywhere any more.
      private async Task ReleaseStackAsync(NavigationStack stack) {
         try {
            await stack.ReleaseAll();
         }
         catch (Exception x) {
            _app.MainWindow.ShowMboxError(x);
         }
      }

      // Closes this window without asking anyone and waits until every body on it has been released.
      // Used when the session is gone, and when the main window's closing has already been agreed to.
      internal async Task CloseWithoutAskingAsync() {
         if (!_closed) {
            _closeAgreed = true;
            Close();
         }

         await _release;
      }

      #endregion

      #region Moving tabs between windows

      // Moves a tab to a window of its own standing at screenPoint, the way a tab pulled out of the
      // strip does. The entry moves alive - body and unsaved input included - and its own
      // NavigateTo then opens editors in the new window.
      internal async Task TearOffAsync(TabbedMainWindowTab tab, Point screenPoint) {
         try {
            if (Vm.Stack is not { } source) return;

            var width = WindowState == WindowState.Normal ? ActualWidth : RestoreBounds.Width;
            var height = WindowState == WindowState.Normal ? ActualHeight : RestoreBounds.Height;

            var entry = tab.Entry;
            if (!await source.Extract(entry)) return;
            // The body only lets go of this window's content presenter on the next layout pass, and a
            // control cannot be the visual child of two windows at once.
            if (!_closed) UpdateLayout();

            var stack = new NavigationStack(_app, home: null, tabbed: true);
            stack.Adopt(entry, 0);

            var window = new TabbedMainWindow(_app, stack) {
               WindowStartupLocation = WindowStartupLocation.Manual,
               Width = width,
               Height = height,
               // Puts the new window's first tab under the cursor, the way the tab was being held.
               Left = screenPoint.X - 160,
               Top = screenPoint.Y - 20
            };
            window.Vm.CopyShellFrom(Vm);
            _app.RegisterTearOffWindow(window);
            window.Show();
            window.Activate();

            await stack.MoveTo(entry);
         }
         catch (Exception x) {
            this.ShowMboxError(x);
         }
      }

      // Moves a tab onto another window's strip at the given position. The body shown there is asked
      // before the tab comes to the front; if it refuses, the tab still moves but stays behind it.
      internal async Task MoveTabToAsync(TabbedMainWindowTab tab, TabbedMainWindow target, int index) {
         try {
            if (Vm.Stack is not { } source || target.Vm.Stack is not { } destination) return;

            var entry = tab.Entry;
            if (!await source.Extract(entry)) return;
            if (!_closed) UpdateLayout();

            destination.Adopt(entry, index);
            target.Activate();
            await destination.MoveTo(entry);
         }
         catch (Exception x) {
            this.ShowMboxError(x);
         }
      }

      #endregion
   }

   /// <summary>One section of the task hub, from one source.</summary>
   public class HubTaskSectionVm(string source, IEnumerable<HubTaskInfo> tasks) : MvvmModelBase
   {
      /// <summary>The title.</summary>
      public string Title { get; } = source switch {
         "approval.document" => "DOCUMENT NEED APPROVAL",
         "approval.data" => "DATA NEED APPROVAL",
         _ => source
      };
      /// <summary>The items.</summary>
      public HubTaskRowVm[] Items { get; } = tasks.Select(t => new HubTaskRowVm(t)).ToArray();
   }

   /// <summary>One row of the task hub.</summary>
   public class HubTaskRowVm(HubTaskInfo info) : MvvmModelBase
   {
      /// <summary>The info.</summary>
      public HubTaskInfo Info { get; } = info;
      /// <summary>The title.</summary>
      public string Title => Info.Title;
      /// <summary>The count.</summary>
      public int Count => Info.Count;
      /// <summary>The description.</summary>
      public string? Description => Info.Description;
      /// <summary>The action name.</summary>
      public string? ActionName => Info.ActionName;
      /// <summary>Indicates it can open.</summary>
      public bool CanOpen => Info.ActionName == "Open"
         && Info.NavigationTarget == ApprovalManagerNavigationPayload.NavigationName;
      /// <summary>The oldest age text.</summary>
      public string OldestAgeText => Info.OldestAge is not { } age ? "" : age.TotalDays >= 1
         ? $"Oldest {Math.Floor(age.TotalDays):0}d" : age.TotalHours >= 1
         ? $"Oldest {Math.Floor(age.TotalHours):0}h" : $"Oldest {Math.Max(0, Math.Floor(age.TotalMinutes)):0}m";
   }

   /// <summary>
   /// View model of <see cref="TabbedMainWindow"/>: the tabs that follow the window's stack, the content of
   /// the Apps and Tools menus, the active connection, the user's identity on the account button, the parts
   /// of the title row shown for the current layout and sign-in state, and the window state
   /// (minimize/maximize/close).
   /// </summary>
   public class TabbedMainWindowVm : MvvmModelBase
   {
      // The view model is made by XAML on the UI thread, which is where a refused tab switch has to
      // be put back.
      private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

      // True while the tabs are being brought in line with the stack, so the selection changes that
      // causes are not mistaken for the user picking a tab.
      private bool _syncing;

      private bool _appAttached;

      /// <summary>Creates a new instance of <see cref="TabbedMainWindowVm"/>.</summary>
      public TabbedMainWindowVm() {
         Tabs = [];
         Tabs.CollectionChanged += (_, _) => {
            // With the last tab gone the list button collapses, and an open tab list would be left
            // behind as an empty frame.
            if (Tabs.Count == 0) IsTabListOpen = false;
            NotifyChanged(nameof(TabListItems));
            NotifyChanged(nameof(IsTabSearchVisible));
            NotifyChanged(nameof(TabListButtonVisibility));
            RefreshTabCommands();
         };
         AppMenu = [];
         ToolsMenu = [];

         RegisterCommand<TabbedMainWindowMenuItem>(nameof(OpenMenuItemCommand), OpenMenuItemCommand);
         RegisterCommand<TabbedMainWindowTab>(nameof(CloseTabCommand), CloseTabCommand);
         RegisterCommand<TabbedMainWindowTab>(nameof(ReloadTabCommand), ReloadTabCommand);
         RegisterCommand<TabbedMainWindowTab>(nameof(SelectTabCommand), SelectTabCommand);
         RegisterCommand<TabbedMainWindowTab>(nameof(CloseOtherTabsCommand), CloseOtherTabsCommand, CloseOtherTabsCommandAllowed);
         RegisterCommand<TabbedMainWindowTab>(nameof(CloseTabsToRightCommand), CloseTabsToRightCommand, CloseTabsToRightCommandAllowed);
         RegisterCommand<TabbedMainWindowTab>(nameof(MoveTabToNewWindowCommand), MoveTabToNewWindowCommand, MoveTabToNewWindowCommandAllowed);
         RegisterCommand(nameof(ColorThemeCommand), ColorThemeCommand, ColorThemeCommandAllowed);
         RegisterCommand(nameof(ChangePasswordCommand), ChangePasswordCommand, ChangePasswordCommandAllowed);
         RegisterCommand(nameof(SignOutCommand), SignOutCommand);
         RegisterCommand(nameof(MinimizeCommand), MinimizeCommand);
         RegisterCommand(nameof(MaximizeRestoreCommand), MaximizeRestoreCommand);
         RegisterCommand(nameof(CloseWindowCommand), CloseWindowCommand);
         RegisterCommand<BusinessTaskItem?>(nameof(OpenTaskCommand), OpenTaskCommand, OpenTaskCommandAllowed);
         RegisterCommand<HubTaskRowVm?>(nameof(OpenHubTaskCommand), OpenHubTaskCommand, OpenHubTaskCommandAllowed);
      }

      /// <summary>
      /// Raised when the window needs to be closed: the user pressed the close button, or a window born from
      /// a dragged-out tab ran out of tabs. The window bridges it to <see cref="Window.Close"/>, because
      /// closing a window is the view's business.
      /// </summary>
      public event Action? RequestClose;

      /// <summary>
      /// Raised when the user chooses "Move to New Window" on a tab. Creating a window is the view's
      /// business, so the window answers it by moving that tab to a new window.
      /// </summary>
      public event Action<TabbedMainWindowTab>? TearOffRequested;

      #region Window

      /// <summary>Title of the window, shown in the title row and on the taskbar.</summary>
      public string Title {
         get => Get("Em");
         set => Set(value);
      }

      /// <summary>
      /// The window state (normal, minimized, maximized), bound two-way to the window so the caption buttons
      /// only need to change this value.
      /// </summary>
      public WindowState WindowState {
         get => Get<WindowState>();
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> when this window uses the single-page layout: the content card holds the navigation host
      /// (<see cref="SpaHost"/>), and the title row only holds the logo, the title, and the caption buttons.
      /// </summary>
      public bool IsSpaLayout {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>
      /// <c>true</c> while someone has signed in, in the multi-tab layout. While <c>false</c>, the content
      /// card shows the login screen, and the row of tabs, the menus, the connection combobox, the theme
      /// button, and the account button are hidden. Written by the application when the login flow moves,
      /// not computed from the active user, because debug mode has a user who never signed in.
      /// </summary>
      public bool IsSignedIn {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>
      /// <c>true</c> for a window born from a dragged-out tab: such a window has no content other than its
      /// tabs, so it closes itself as soon as its last tab is closed or moved, and shows neither the Apps
      /// menu, the Tools menu, the connection combobox, the theme button, nor the account button - all of
      /// those exist only in the main window. The main window is <c>false</c> and is never left empty
      /// because of a drag.
      /// </summary>
      public bool ClosesWhenEmpty {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>Whether the application is running in debug mode.</summary>
      public bool IsDebugMode => EmApp?.IsDebugMode ?? false;

      /// <summary>Visibility of the row of tabs and the tab list button: only in the multi-tab layout's tab mode.</summary>
      public Visibility TabStripVisibility =>
         !IsSpaLayout && IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibility of the tab list dropdown button: like <see cref="TabStripVisibility"/>, and only while
      /// there are tabs - without tabs the list is empty and the content card already shows "No tab open".
      /// </summary>
      public Visibility TabListButtonVisibility =>
         TabStripVisibility == Visibility.Visible && Tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibility of the Apps menu, the Tools menu, the theme button, and the account button: only in the
      /// main window of the multi-tab layout, and only while someone has signed in.
      /// </summary>
      public Visibility WorkspaceToolsVisibility =>
         !IsSpaLayout && IsSignedIn && !ClosesWhenEmpty ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibility of the connection combobox: like <see cref="WorkspaceToolsVisibility"/>, and only in
      /// debug mode - otherwise the connection is chosen on the login screen.
      /// </summary>
      public Visibility ConnectionVisibility =>
         IsDebugMode && WorkspaceToolsVisibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>Whether the text "No tab open" is shown: tab mode with no active tab at all.</summary>
      public bool IsEmptyHintVisible => !IsSpaLayout && IsSignedIn && ActiveTab == null;

      private void RefreshChrome() {
         NotifyChanged(nameof(TabStripVisibility));
         NotifyChanged(nameof(TabListButtonVisibility));
         NotifyChanged(nameof(WorkspaceToolsVisibility));
         NotifyChanged(nameof(ConnectionVisibility));
         NotifyChanged(nameof(IsEmptyHintVisible));
         NotifyChanged(nameof(CardContent));
      }

      /// <summary>
      /// The navigation host that fills the content card in the single-page layout, or <c>null</c> in the
      /// multi-tab layout.
      /// </summary>
      public object? SpaHost {
         get => Get<object?>();
         set => Set(value, _ => NotifyChanged(nameof(CardContent)));
      }

      /// <summary>
      /// The content of the content card: the navigation host in the single-page layout, or the body of the
      /// active entry in the multi-tab layout (including the login screen until someone signs in).
      /// </summary>
      public object? CardContent => IsSpaLayout ? SpaHost : ActiveTab?.Content;

      #endregion

      #region Tabs

      /// <summary>
      /// The stack this window shows as tabs, or <c>null</c> in the single-page layout.
      /// <see cref="Tabs"/>, <see cref="SelectedTab"/>, and the content of the content card follow this stack.
      /// </summary>
      public NavigationStack? Stack {
         get;
         internal set {
            if (field == value) return;

            if (field != null) {
               field.Changed -= StackChanged;
               field.PropertyChanged -= StackPropertyChanged;
            }

            field = value;
            if (field != null) {
               field.Changed += StackChanged;
               field.PropertyChanged += StackPropertyChanged;
            }

            Sync();
         }
      }

      private void StackChanged(object? sender, EventArgs e) => Sync();

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Sync();
      }

      // Brings the tabs in line with the stack: one tab per entry in the same order - an entry keeps
      // its tab object, so its template is not rebuilt - and the selection on the entry being shown.
      // Called on both stack events, because Current can be swapped before the path has finished
      // changing.
      private void Sync() {
         if (Stack is not { } stack) return;

         _syncing = true;
         try {
            var entries = stack.Entries;
            foreach (var gone in Tabs.Where(t => !entries.Contains(t.Entry)).ToList()) {
               gone.Detach();
               Tabs.Remove(gone);
            }

            for (var i = 0; i < entries.Count; i++) {
               var at = IndexOfTab(entries[i]);
               if (at < 0) Tabs.Insert(i, new TabbedMainWindowTab(entries[i]));
               else if (at != i) Tabs.Move(at, i);
            }

            var index = stack.Current is { } entry ? IndexOfTab(entry) : -1;
            var current = index >= 0 ? Tabs[index] : null;
            SelectedTab = current;
            ActiveTab = current;
         }
         finally {
            _syncing = false;
         }

         if (Tabs.Count == 0 && ClosesWhenEmpty) RequestClose?.Invoke();
      }

      private int IndexOfTab(NavigationEntry entry) {
         for (var i = 0; i < Tabs.Count; i++) {
            if (Tabs[i].Entry == entry) return i;
         }

         return -1;
      }

      /// <summary>The tabs that are open, in order from left to right.</summary>
      public ObservableCollection<TabbedMainWindowTab> Tabs {
         get => Get<ObservableCollection<TabbedMainWindowTab>>();
         private set => Set(value);
      }

      /// <summary>
      /// The tab highlighted in the row of tabs. Choosing a tab from the UI moves the stack to that tab's
      /// entry - the body being left is still asked and may refuse; if it refuses, the highlight returns to
      /// the previous tab.
      /// </summary>
      public TabbedMainWindowTab? SelectedTab {
         get => Get<TabbedMainWindowTab?>();
         set {
            var previous = SelectedTab;
            if (!Set(value)) return;

            if (previous != null) previous.IsActive = false;
            if (value != null) value.IsActive = true;

            if (!_syncing && value != null) _ = ActivateAsync(value);
         }
      }

      /// <summary>
      /// The tab whose body is shown in the content card, that is, the <see cref="NavigationStack.Current"/>
      /// entry. It may briefly differ from <see cref="SelectedTab"/> while the body being left is still being
      /// asked.
      /// </summary>
      public TabbedMainWindowTab? ActiveTab {
         get => Get<TabbedMainWindowTab?>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(CardContent));
            NotifyChanged(nameof(IsEmptyHintVisible));
         });
      }

      // The highlight has already moved; the content only follows once the stack agrees.
      private async Task ActivateAsync(TabbedMainWindowTab tab) {
         try {
            if (Stack != null && await Stack.MoveTo(tab.Entry)) return;
         }
         catch (Exception x) {
            AlertError(x);
         }

         // Refused: the highlight goes back to where the stack still is. Deferred, so the ListBox -
         // which may still be inside its own selection change - follows it.
         _ = _dispatcher.InvokeAsync(Sync);
      }

      /// <summary>
      /// Whether the tab list dropdown (the button at the right of the row of tabs) is open. Opening it also
      /// empties <see cref="TabListFilter"/>, so the list always starts complete.
      /// </summary>
      public bool IsTabListOpen {
         get => Get<bool>();
         set => Set(value, open => {
            if (open) TabListFilter = string.Empty;
         });
      }

      /// <summary>Search text in the tab list dropdown; matched against tab titles, case-insensitively.</summary>
      public string TabListFilter {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(TabListItems)));
      }

      /// <summary>The tabs shown by the tab list dropdown: all tabs, filtered by <see cref="TabListFilter"/>.</summary>
      public IEnumerable<TabbedMainWindowTab> TabListItems =>
         string.IsNullOrWhiteSpace(TabListFilter)
            ? Tabs
            : Tabs.Where(t => t.Title.Contains(TabListFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

      /// <summary>
      /// Whether the search box in the tab list dropdown is shown. It only appears when there are enough tabs
      /// to need searching; with few tabs, the list itself is readable at a glance.
      /// </summary>
      public bool IsTabSearchVisible => Tabs.Count >= TabSearchThreshold;

      // The number of open tabs from which the tab list offers a search box.
      private const int TabSearchThreshold = 8;

      /// <summary>Runs the close tab command.</summary>
      public async Task CloseTabCommand(TabbedMainWindowTab tab) {
         try {
            await tab.Entry.Close();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Runs the reload tab command.</summary>
      public async Task ReloadTabCommand(TabbedMainWindowTab tab) {
         try {
            await tab.Entry.Reload();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      // The tab clicked becomes the one shown first, then the others go one by one; the first body
      // that refuses keeps its tab and stops the rest.
      /// <summary>Runs the close other tabs command.</summary>
      public async Task CloseOtherTabsCommand(TabbedMainWindowTab tab) {
         if (Stack is not { } stack) return;

         try {
            if (!await stack.MoveTo(tab.Entry)) return;
            foreach (var other in stack.Entries.Where(e => e != tab.Entry).ToList()) {
               if (!await other.Close()) return;
            }
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Whether the close other tabs command may run now.</summary>
      public bool CloseOtherTabsCommandAllowed(TabbedMainWindowTab tab) => Tabs.Contains(tab) && Tabs.Count > 1;

      // When the tab shown is among the ones going, the tab clicked is the natural one to land on.
      /// <summary>Runs the close tabs to right command.</summary>
      public async Task CloseTabsToRightCommand(TabbedMainWindowTab tab) {
         if (Stack is not { } stack) return;

         try {
            var index = stack.IndexOf(tab.Entry);
            if (index < 0) return;

            if (stack.Current is { } current && stack.IndexOf(current) > index && !await stack.MoveTo(tab.Entry))
               return;

            foreach (var right in stack.Entries.Skip(index + 1).ToList()) {
               if (!await right.Close()) return;
            }
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Whether the close tabs to right command may run now.</summary>
      public bool CloseTabsToRightCommandAllowed(TabbedMainWindowTab tab) {
         var index = Tabs.IndexOf(tab);
         return index >= 0 && index < Tabs.Count - 1;
      }

      /// <summary>Runs the move tab to new window command.</summary>
      public void MoveTabToNewWindowCommand(TabbedMainWindowTab tab) => TearOffRequested?.Invoke(tab);

      /// <summary>Whether the move tab to new window command may run now.</summary>
      public bool MoveTabToNewWindowCommandAllowed(TabbedMainWindowTab tab) => CanTearOff(tab);

      // UiCommand is not tied to CommandManager.RequerySuggested, so the tab menu's commands have to be
      // told when the tab list they depend on has changed.
      private void RefreshTabCommands() {
         Commands[nameof(CloseOtherTabsCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(CloseTabsToRightCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(MoveTabToNewWindowCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>Runs the select tab command.</summary>
      public void SelectTabCommand(TabbedMainWindowTab tab) {
         IsTabListOpen = false;
         SelectedTab = tab;
      }

      /// <summary>
      /// Whether <paramref name="tab"/> may be dragged out to become a new window. Only when this window
      /// still has other tabs: a single tab is not split off anymore, its window is simply closed.
      /// </summary>
      /// <param name="tab">The tab about to be dragged out.</param>
      public bool CanTearOff(TabbedMainWindowTab tab) => Tabs.Contains(tab) && Stack is { Entries.Count: > 1 };

      /// <summary>
      /// Whether <paramref name="tab"/> may leave this window, either to become a new window or to move to
      /// another window. The last tab of the main window may not, so the main window is not left empty; the
      /// last tab of a window born from a drag-out may move to another window, after which its window closes
      /// itself.
      /// </summary>
      /// <param name="tab">The tab about to be dragged out.</param>
      public bool CanMoveOut(TabbedMainWindowTab tab) =>
         Tabs.Contains(tab) && (ClosesWhenEmpty || Stack is { Entries.Count: > 1 });

      /// <summary>
      /// Whether <paramref name="tab"/> from another window may enter this window. Refused when there is
      /// already another tab here with the same title, because the title is the tab's unique key.
      /// </summary>
      /// <param name="tab">The tab about to be brought in.</param>
      public bool CanAcceptTab(TabbedMainWindowTab tab) =>
         Stack != null
         && !Tabs.Any(t => t != tab && string.Equals(t.Title, tab.Title, NavigationStack.TitleComparison));

      /// <summary>
      /// Moves <paramref name="tab"/> to position <paramref name="index"/> in this window's row of tabs,
      /// without changing the active tab and without notifying any body.
      /// </summary>
      /// <param name="tab">The tab being moved; it must belong to this window.</param>
      /// <param name="index">Its new position, counted from the left starting at 0.</param>
      public void MoveTab(TabbedMainWindowTab tab, int index) => Stack?.Move(tab.Entry, index);

      #endregion

      #region Menus

      /// <summary>
      /// Content of the Apps menu, top level: the navigations that appear in the menu and may be opened by
      /// the active user, grouped by their menu path. An item that has <see cref="TabbedMainWindowMenuItem.Items"/>
      /// opens a submenu to its right, so the menu can be as deep as needed.
      /// </summary>
      public ObservableCollection<TabbedMainWindowMenuItem> AppMenu {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }

      /// <summary>
      /// Content of the Tools dropdown at the right of the title row: the application's built-in tools, the
      /// same as those shown on the single-page layout's home. It has the same shape as <see cref="AppMenu"/>.
      /// </summary>
      public ObservableCollection<TabbedMainWindowMenuItem> ToolsMenu {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }

      /// <summary>
      /// Whether the Apps dropdown is open. The Apps button and its menu are both bound to this property, so
      /// their states never differ.
      /// </summary>
      public bool IsAppsMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Whether the Tools dropdown is open.</summary>
      public bool IsToolsMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      // Redraws both menus for the user signed in now. The text is copied out of each navigation, not
      // bound to it, so a navigation re-titling one of its open entries leaves the menu alone.
      internal void RebuildMenus() {
         AppMenu.Clear();
         ToolsMenu.Clear();
         if (EmApp is not { } app) return;

         // Like the home screen: navigations without a path sit above the groups.
         var roots = new List<TabbedMainWindowMenuItem>();
         var groups = new List<TabbedMainWindowMenuItem>();
         foreach (var nav in app.Navigations.Where(r => r.IsMenuVisible && app.CanOpen(r))) {
            var item = new TabbedMainWindowMenuItem {
               Title = nav.Title,
               Subtitle = string.IsNullOrWhiteSpace(nav.Subtitle) ? null : nav.Subtitle,
               Navigation = nav
            };

            var segments = MenuPaths.Split(nav.MenuPath);
            if (segments.Length == 0) roots.Add(item);
            else ResolveBranch(groups, segments).Items.Add(item);
         }

         foreach (var item in roots.Concat(groups)) AppMenu.Add(item);

         foreach (var tool in app.GetStaticTools()) {
            var invoke = tool.Invoke;
            ToolsMenu.Add(new TabbedMainWindowMenuItem {
               Title = tool.Title,
               Subtitle = string.IsNullOrWhiteSpace(tool.Subtitle) ? null : tool.Subtitle,
               Navigation = tool.Navigation,
               Invoke = invoke == null ? null : () => invoke(DialogOwner!)
            });
         }
      }

      // Walks the path segment by segment, creating the branches that do not exist yet, and returns
      // the deepest one.
      private static TabbedMainWindowMenuItem ResolveBranch(List<TabbedMainWindowMenuItem> groups, string[] segments) {
         IList<TabbedMainWindowMenuItem> level = groups;
         TabbedMainWindowMenuItem? current = null;

         foreach (var header in segments) {
            current = level.FirstOrDefault(r =>
               r.IsBranch && string.Equals(r.Title, header, MenuPaths.SegmentComparison));
            if (current == null) {
               current = new TabbedMainWindowMenuItem { Title = header };
               level.Add(current);
            }

            level = current.Items;
         }

         return current!;
      }

      // Claims may still be arriving when the workspace opens; once they have, the menus are drawn
      // again so a navigation the user turns out to be allowed shows up.
      internal async Task RebuildMenusWhenClaimsLoadedAsync() {
         if (EmApp is not { } app) return;

         try {
            await app.EnsureClaimsLoadedAsync();
            await app.ServiceProvider.GetRequiredService<ApprovalAccessCatalog>().LoadAsync();
         }
         catch (Exception) {
            // Already reported by whoever started the refresh; the menus stay as they are.
            return;
         }

         if (IsSignedIn) RebuildMenus();
      }

      /// <summary>Runs the open menu item command.</summary>
      public async Task OpenMenuItemCommand(TabbedMainWindowMenuItem item) {
         // A branch only opens its submenu; there is nothing behind it.
         if (item.IsBranch) return;

         IsAppsMenuOpen = false;
         IsToolsMenuOpen = false;

         try {
            // Always relative to the main stack: the Apps and Tools menus only exist on the main window.
            if (item.Navigation != null) await EmApp!.NavigateTo(item.Navigation);
            else if (item.Invoke != null) await item.Invoke();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      #endregion

      #region Application

      // The main window of the multi-tab layout follows the application: who is signed in, and which
      // connection is active. Torn-off windows show neither, so they never subscribe.
      internal void AttachApp() {
         if (_appAttached || EmApp is not { } app) return;

         _appAttached = true;
         app.ActiveUserChanged += AppActiveUserChanged;
         app.ActiveConnectionChanged += AppActiveConnectionChanged;
         app.UIConnections.CollectionChanged += AppConnectionsChanged;
         NotifyChanged(nameof(Connections));
         RefreshChrome();
         RefreshUser();
      }

      internal void DetachApp() {
         if (!_appAttached || EmApp is not { } app) return;

         _appAttached = false;
         app.ActiveUserChanged -= AppActiveUserChanged;
         app.ActiveConnectionChanged -= AppActiveConnectionChanged;
         app.UIConnections.CollectionChanged -= AppConnectionsChanged;
      }

      private void AppActiveUserChanged(object? sender, EventArgs e) {
         RefreshUser();
         if (IsSignedIn) RebuildMenus();
      }

      // In debug mode a new connection brings a new claim catalogue with it.
      private void AppActiveConnectionChanged(object? sender, EventArgs e) {
         SyncSelectedConnection();
         if (IsSignedIn) _ = RebuildMenusWhenClaimsLoadedAsync();
      }

      private void AppConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncSelectedConnection();

      /// <summary>
      /// The connection profiles offered by the connection combobox, which are the collection of
      /// <see cref="Core.EmApp.UIConnections"/> as-is. Null-safe because XAML creates this view model before
      /// <see cref="MvvmModelBase.EmApp"/> has been set.
      /// </summary>
      public ObservableCollection<ApiConnection>? Connections => EmApp?.UIConnections;

      /// <summary>
      /// The connection profile chosen in the connection combobox. Choosing it makes it the application's
      /// active connection (<see cref="Core.EmApp.ActiveConnection"/>).
      /// </summary>
      public ApiConnection? SelectedConnection {
         get => Get<ApiConnection?>();
         set => Set(value, connection => {
            // A rebuild of the list empties the selection for a moment; that is not a pick.
            if (connection != null && EmApp is { } app && app.ActiveConnection != connection)
               app.ActiveConnection = connection;
         });
      }

      // Only in debug mode, the one mode the combobox is shown in. Elsewhere the login screen picks the
      // connection, and a rebuilt list must not swap the active one for a fresh object of the same
      // profile - the session belongs to the object it was opened on.
      private void SyncSelectedConnection() {
         if (EmApp is not { IsDebugMode: true } app) return;

         // Looked up by profile name: rebuilding the list replaces every stored profile with a new
         // object, so a reference held from before the rebuild is no longer in the collection.
         var name = app.ActiveConnection?.ProfileName;
         SelectedConnection =
            (name == null ? null : app.UIConnections.FirstOrDefault(r => r.ProfileName == name))
            ?? app.DefaultDebugConnection;
      }

      #endregion

      #region Task hub

      private BusinessTaskTracker? _taskTracker;
      private DispatcherTimer? _hubTimer;
      private int _hubEpoch;
      private Task? _hubRefresh;

      // Only the main window carries the hub, on either layout; a torn-off window never attaches it.
      internal void AttachTaskHub() {
         if (_taskTracker is not null || EmApp is not { } app) return;

         _taskTracker = app.ServiceProvider.GetRequiredService<BusinessTaskTracker>();
         _taskTracker.Changed += TaskTrackerChanged;
         app.ActiveUserChanged += HubIdentityChanged;
         app.ActiveConnectionChanged += HubIdentityChanged;
         _hubTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background,
            HubTimerTick, _dispatcher);
         SyncTaskHub();
         _ = RefreshHubTasksAsync();
      }

      internal void DetachTaskHub() {
         if (_taskTracker is null) return;

         _taskTracker.Changed -= TaskTrackerChanged;
         if (EmApp is { } app) {
            app.ActiveUserChanged -= HubIdentityChanged;
            app.ActiveConnectionChanged -= HubIdentityChanged;
         }
         _hubTimer?.Stop();
         if (_hubTimer is not null) _hubTimer.Tick -= HubTimerTick;
         _hubTimer = null;
         _taskTracker = null;
         ResetHubTasks();
      }

      private void HubTimerTick(object? sender, EventArgs e) {
         if (MainWindow?.IsActive == true) _ = RefreshHubTasksAsync();
      }

      private void HubIdentityChanged(object? sender, EventArgs e) {
         if (!_dispatcher.CheckAccess()) {
            _dispatcher.InvokeAsync(() => HubIdentityChanged(sender, e));
            return;
         }
         ResetHubTasks();
         NotifyChanged(nameof(TaskHubVisibility));
         _ = RefreshHubTasksAsync();
      }

      private void ResetHubTasks() {
         ++_hubEpoch;
         _hubRefresh = null;
         HubTaskSections.Clear();
         HubTaskLoadFailed = false;
         IsTaskHubOpen = false;
         NotifyHubTasksChanged();
      }

      // Coalesce concurrent opens/ticks; responses from an old session or connection are discarded.
      internal Task RefreshHubTasksAsync() {
         if (_taskTracker is null || EmApp?.ActiveUser is null) return Task.CompletedTask;
         if (_hubRefresh is { IsCompleted: false }) return _hubRefresh;
         return _hubRefresh = LoadHubTasksAsync(_hubEpoch);
      }

      private async Task LoadHubTasksAsync(int epoch) {
         var app = EmApp!;
         var user = app.ActiveUser;
         var connection = app.ActiveConnection;
         try {
            var tasks = await app.ServiceProvider.GetRequiredService<IApprovalServices>().GetMeta_UserHubTasks();
            if (epoch != _hubEpoch || user != app.ActiveUser || connection != app.ActiveConnection) return;
            HubTaskSections.Clear();
            foreach (var group in tasks.GroupBy(t => t.Source).OrderBy(g => g.Key switch {
               "approval.document" => 0, "approval.data" => 1, _ => 2
            })) HubTaskSections.Add(new HubTaskSectionVm(group.Key, group));
            HubTaskLoadFailed = false;
         }
         catch (Exception) {
            if (epoch != _hubEpoch || user != app.ActiveUser || connection != app.ActiveConnection) return;
            HubTaskSections.Clear();
            HubTaskLoadFailed = true;
         }
         if (epoch == _hubEpoch) NotifyHubTasksChanged();
      }

      private void NotifyHubTasksChanged() {
         NotifyChanged(nameof(HasHubTasks));
         NotifyChanged(nameof(IsTaskHubEmpty));
      }

      /// <summary>The hub task sections.</summary>
      public ObservableCollection<HubTaskSectionVm> HubTaskSections { get; } = [];
      /// <summary>Indicates there is hub tasks.</summary>
      public bool HasHubTasks => HubTaskSections.Count > 0;
      /// <summary>Indicates there is running tasks.</summary>
      public bool HasRunningTasks => TaskHubItems.Count > 0;
      /// <summary>Indicates hub task load failed.</summary>
      public bool HubTaskLoadFailed { get => Get<bool>(); private set => Set(value); }

      /// <summary>Whether the open hub task command may run now.</summary>
      public bool OpenHubTaskCommandAllowed(HubTaskRowVm? row) => row?.CanOpen == true && EmApp?.ActiveUser is not null;

      /// <summary>Runs the open hub task command.</summary>
      public async Task OpenHubTaskCommand(HubTaskRowVm? row) {
         if (!OpenHubTaskCommandAllowed(row)) return;
         try {
            var payload = new ApprovalManagerNavigationPayload {
               DocType = row!.Info.NavigationParameter, WaitingForMeOnly = true
            };
            var existing = payload.Title is { } title ? EmApp!.FindEntry(title) : null;
            if (await EmApp!.NavigateTo(row!.Info.NavigationTarget!, payload)) {
               // Reusing a tab does not deliver the new payload through the core router.
               if (existing?.Body is ApprovalManager manager) {
                  manager.Vm.Configure(payload);
                  await manager.Vm.RefreshAsync();
               }
               IsTaskHubOpen = false;
            }
         }
         catch (Exception) {
            HubTaskLoadFailed = true;
         }
      }

      private void TaskTrackerChanged(object? sender, EventArgs e) => SyncTaskHub();

      private void SyncTaskHub() {
         if (_taskTracker is not { } tracker) return;

         BusinessTaskItem.Sync(TaskHubItems, tracker.Tasks);
         // The popup is where finished tasks are looked at, so while it is open they count as seen.
         if (IsTaskHubOpen && tracker.HasUnseen) tracker.MarkAllSeen();
         NotifyChanged(nameof(TaskHubVisibility));
         NotifyChanged(nameof(TaskHubAliveCount));
         NotifyChanged(nameof(IsTaskHubBusy));
         NotifyChanged(nameof(TaskHubHasUnseen));
         NotifyChanged(nameof(TaskHubHasUnseenFailure));
         NotifyChanged(nameof(IsTaskHubEmpty));
         NotifyChanged(nameof(HasRunningTasks));
      }

      /// <summary>The signed-in user's personal tasks, as monitored by <see cref="BusinessTaskTracker"/>.</summary>
      public ObservableCollection<BusinessTaskItem> TaskHubItems { get; } = [];

      /// <summary>
      /// Visibility of the task hub button: in the main window of both layouts, while a user is signed in.
      /// </summary>
      public Visibility TaskHubVisibility =>
         _taskTracker is not null && !ClosesWhenEmpty && EmApp?.ActiveUser is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

      /// <summary>Number of personal tasks still queued or running, for the hub button's badge.</summary>
      public int TaskHubAliveCount => _taskTracker?.AliveCount ?? 0;

      /// <summary><c>true</c> while a personal task is live: the hub icon spins and the badge appears.</summary>
      public bool IsTaskHubBusy => TaskHubAliveCount > 0;

      /// <summary><c>true</c> when a task has finished since the hub popup was last opened.</summary>
      public bool TaskHubHasUnseen => _taskTracker?.HasUnseen ?? false;

      /// <summary><c>true</c> when one of the tasks not yet seen has failed; the marker dot is red.</summary>
      public bool TaskHubHasUnseenFailure => _taskTracker?.HasUnseenFailure ?? false;

      /// <summary><c>true</c> when every part of the task list is empty.</summary>
      public bool IsTaskHubEmpty => TaskHubItems.Count == 0 && !HasHubTasks;

      /// <summary>
      /// Whether the task hub popup is open. Opening it reloads the task list and marks every finished task as
      /// seen.
      /// </summary>
      public bool IsTaskHubOpen {
         get => Get<bool>();
         set => Set(value, open => {
            if (!open || _taskTracker is not { } tracker) return;

            tracker.MarkAllSeen();
            _ = tracker.RefreshAsync();
            _ = RefreshHubTasksAsync();
         });
      }

      /// <summary>Opens the status dialog of task <paramref name="item"/>.</summary>
      public void OpenTaskCommand(BusinessTaskItem? item) {
         if (item is null || EmApp is not { } app) return;

         IsTaskHubOpen = false;
         var dialog = new BusinessTaskStatusDialog(app, item.Info) { Owner = DialogOwner };
         dialog.ShowDialog();
         _ = _taskTracker?.RefreshAsync();
      }

      /// <summary>Only for a task row.</summary>
      public bool OpenTaskCommandAllowed(BusinessTaskItem? item) => item is not null;

      #endregion

      #region Account

      /// <summary>Whether the account menu (the dropdown behind the user button) is open.</summary>
      public bool IsUserMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>The user name shown in the account menu and the account button's tooltip.</summary>
      public string UserDisplayName {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>The user's account name, the second line in the account menu.</summary>
      public string UserAccount {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>
      /// The initials for the avatar circle. It always holds something: <c>?</c> when emptied, so the circle
      /// never appears empty.
      /// </summary>
      public string UserInitials {
         get => Get("?");
         set => Set(string.IsNullOrWhiteSpace(value) ? "?" : value);
      }

      /// <summary>
      /// Color of the avatar circle, from the same palette as the account button of the single-page layout:
      /// the same person always gets the same color.
      /// </summary>
      public System.Windows.Media.Brush? UserAvatarBrush {
         get => Get<System.Windows.Media.Brush?>();
         set => Set(value);
      }

      // EmApp is a plain object rather than a bindable source, so the account button is told to
      // read the signed-in user again whenever it changes.
      internal void RefreshUser() {
         var user = EmApp?.ActiveUser;
         UserDisplayName = UserAvatar.DisplayName(user);
         UserAccount = UserAvatar.Account(user);
         UserInitials = UserAvatar.Initials(user);
         UserAvatarBrush = UserAvatar.Brush(user);
      }

      /// <summary>Runs the change password command.</summary>
      public void ChangePasswordCommand() {
         IsUserMenuOpen = false;
      }

      /// <summary>Whether the change password command may run now.</summary>
      public bool ChangePasswordCommandAllowed() => false;

      // The same sign-out path as the single-page layout's account button.
      /// <summary>Runs the sign out command.</summary>
      public async Task SignOutCommand() {
         IsUserMenuOpen = false;

         // Nothing may escape from here: ICommand.Execute is void, so UiCommandAsync runs this as
         // async void, and the application has no DispatcherUnhandledException to catch it.
         try {
            await EmApp!.SignOutAsync();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      // The theme belongs to the whole application, so switching it here repaints every window.
      /// <summary>Runs the color theme command.</summary>
      public void ColorThemeCommand() {
         EmApp!.CurrentTheme = EmApp.CurrentTheme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
      }

      /// <summary>Whether the color theme command may run now.</summary>
      public bool ColorThemeCommandAllowed() => EmApp != null;

      #endregion

      /// <summary>
      /// Copies the frame of another window to this window - the title, the sign-in state, and the
      /// application - then marks it <see cref="ClosesWhenEmpty"/>. Used for a new window that holds a tab
      /// that was dragged out. Such a window only holds the row of tabs and the tab list dropdown: the Apps
      /// menu, the Tools menu, and the user identity belong to the main window alone, so they are not copied.
      /// Its tabs follow this window's stack.
      /// </summary>
      /// <param name="source">The window the tab came from.</param>
      public void CopyShellFrom(TabbedMainWindowVm source) {
         Title = source.Title;
         IsSignedIn = source.IsSignedIn;
         ClosesWhenEmpty = true;
         EmApp = source.EmApp;
      }

      /// <summary>Runs the minimize command.</summary>
      public void MinimizeCommand() {
         WindowState = WindowState.Minimized;
      }

      /// <summary>Runs the maximize restore command.</summary>
      public void MaximizeRestoreCommand() {
         WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
      }

      /// <summary>Runs the close window command.</summary>
      public void CloseWindowCommand() {
         RequestClose?.Invoke();
      }
   }

   /// <summary>
   /// One tab in <see cref="TabbedMainWindow"/>: one navigation entry in that window's stack. Its title
   /// follows the entry's title, including after the entry changes its own title.
   /// </summary>
   public class TabbedMainWindowTab : NotifyPropertyBase
   {
      internal TabbedMainWindowTab(NavigationEntry entry) {
         Entry = entry;
         Title = entry.Title;
         Content = entry.Body;
         entry.PropertyChanged += EntryPropertyChanged;
      }

      /// <summary>The navigation entry shown by this tab.</summary>
      public NavigationEntry Entry { get; }

      /// <summary>Title of the tab, which is its entry's title - and also its unique key across the application.</summary>
      public string Title {
         get => Get(string.Empty);
         private set => Set(value);
      }

      /// <summary>
      /// Whether this tab is the one highlighted in its window. Set by
      /// <see cref="TabbedMainWindowVm.SelectedTab"/>, and used by the tab list dropdown to mark it.
      /// </summary>
      public bool IsActive {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Content of the tab, which is its entry's body; reused every time the tab is selected.</summary>
      public object Content { get; }

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) Title = Entry.Title;
      }

      // The tab has left its window; it stops following the entry, which lives on elsewhere.
      internal void Detach() => Entry.PropertyChanged -= EntryPropertyChanged;
   }

   /// <summary>
   /// One item in the Apps or Tools menu. A leaf item opens its <see cref="Navigation"/> - as a new tab,
   /// or by selecting the tab that already has it open - or runs <see cref="Invoke"/>. A branch item (both
   /// <c>null</c>) only opens a submenu holding <see cref="Items"/>.
   /// </summary>
   public class TabbedMainWindowMenuItem : NotifyPropertyBase
   {
      /// <summary>Creates a new instance of <see cref="TabbedMainWindowMenuItem"/>.</summary>
      public TabbedMainWindowMenuItem() {
         Items = [];
      }

      /// <summary>Judul item.</summary>
      public string Title {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>Short caption below the title, or <c>null</c> when there is none.</summary>
      public string? Subtitle {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>The navigation this item opens, or <c>null</c> for branch items and tools that are not navigations.</summary>
      public Navigation? Navigation {
         get => Get<Navigation?>();
         set => Set(value);
      }

      /// <summary>The action of an item that is not a navigation (e.g. opening a dialog), or <c>null</c>.</summary>
      public Func<Task>? Invoke {
         get => Get<Func<Task>?>();
         set => Set(value);
      }

      /// <summary>Whether this item is a branch: it opens nothing but its submenu.</summary>
      public bool IsBranch => Navigation == null && Invoke == null;

      /// <summary>The children of this item, shown as a submenu. Empty for a leaf item.</summary>
      public ObservableCollection<TabbedMainWindowMenuItem> Items {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }
   }
}
