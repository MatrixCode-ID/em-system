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
   /// Window utama aplikasi di kedua layout, dengan chrome buatan sendiri: satu baris judul yang memuat logo,
   /// menu Apps, deretan tab, menu Tools, tombol tema, tombol akun, dan tombol caption window, lalu
   /// kartu konten di bawahnya.
   /// <para>
   /// Di layout multi-tab setiap tab adalah satu entri <see cref="NavigationStack"/> window ini, dan
   /// kartu konten menampilkan body entri yang sedang aktif; selama belum ada yang masuk, kartu itu
   /// berisi layar login dan hampir seluruh baris judul disembunyikan. Di layout satu halaman kartu
   /// konten berisi host navigasi <see cref="EmApp.MainStack"/>, dan baris judul hanya memuat logo,
   /// judul, dan tombol caption.
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
      /// Membuat window utama aplikasi. Constructor-nya hanya memuat XAML dan menyambungkan aplikasi;
      /// isi window disiapkan oleh <see cref="EmApp.Run"/> sesuai layout yang dipilih.
      /// </summary>
      /// <param name="app">Aplikasi pemilik window ini.</param>
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

      /// <summary>ViewModel window ini, dideklarasikan di XAML sebagai <c>DataContext</c>.</summary>
      public TabbedMainWindowVm Vm => (TabbedMainWindowVm)DataContext;

      // Only the torn-off windows close themselves once empty; the main window never does.
      private bool IsTearOff => Vm.ClosesWhenEmpty;

      /// <summary>
      /// Stack yang ditampilkan window ini sebagai tab, atau <c>null</c> di layout satu halaman.
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

   public class HubTaskSectionVm(string source, IEnumerable<HubTaskInfo> tasks) : MvvmModelBase
   {
      public string Title { get; } = source switch {
         "approval.document" => "DOCUMENT NEED APPROVAL",
         "approval.data" => "DATA NEED APPROVAL",
         _ => source
      };
      public HubTaskRowVm[] Items { get; } = tasks.Select(t => new HubTaskRowVm(t)).ToArray();
   }

   public class HubTaskRowVm(HubTaskInfo info) : MvvmModelBase
   {
      public HubTaskInfo Info { get; } = info;
      public string Title => Info.Title;
      public int Count => Info.Count;
      public string? Description => Info.Description;
      public string? ActionName => Info.ActionName;
      public bool CanOpen => Info.ActionName == "Open"
         && Info.NavigationTarget == ApprovalManagerNavigationPayload.NavigationName;
      public string OldestAgeText => Info.OldestAge is not { } age ? "" : age.TotalDays >= 1
         ? $"Oldest {Math.Floor(age.TotalDays):0}d" : age.TotalHours >= 1
         ? $"Oldest {Math.Floor(age.TotalHours):0}h" : $"Oldest {Math.Max(0, Math.Floor(age.TotalMinutes)):0}m";
   }

   /// <summary>
   /// ViewModel <see cref="TabbedMainWindow"/>: tab-tab yang mengikuti stack window, isi menu Apps
   /// dan Tools, koneksi aktif, identitas pengguna di tombol akun, bagian baris judul yang tampil
   /// untuk layout dan keadaan login saat ini, serta keadaan window (minimize/maximize/tutup).
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
      /// Dipicu saat window perlu ditutup: pengguna menekan tombol tutup, atau window hasil tab yang
      /// ditarik keluar kehabisan tab. Window menjembataninya ke <see cref="Window.Close"/>, karena
      /// menutup window adalah urusan view.
      /// </summary>
      public event Action? RequestClose;

      /// <summary>
      /// Dipicu saat pengguna memilih "Move to New Window" pada sebuah tab. Membuat window adalah
      /// urusan view, jadi window-lah yang menjawabnya dengan memindahkan tab itu ke window baru.
      /// </summary>
      public event Action<TabbedMainWindowTab>? TearOffRequested;

      #region Window

      /// <summary>Judul window, tampil di baris judul dan di taskbar.</summary>
      public string Title {
         get => Get("Em");
         set => Set(value);
      }

      /// <summary>
      /// Keadaan window (normal, minimize, maximize), terikat dua arah dengan window sehingga tombol
      /// caption cukup mengubah nilai ini.
      /// </summary>
      public WindowState WindowState {
         get => Get<WindowState>();
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> kalau window ini dipakai layout satu halaman: kartu konten berisi host navigasi
      /// (<see cref="SpaHost"/>), dan baris judul hanya memuat logo, judul, dan tombol caption.
      /// </summary>
      public bool IsSpaLayout {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>
      /// <c>true</c> selama ada yang masuk di layout multi-tab. Selama <c>false</c>, kartu konten
      /// menampilkan layar login dan deretan tab, menu, combobox koneksi, tombol tema, serta tombol
      /// akun disembunyikan. Ditulis oleh aplikasi saat alur login berpindah, bukan dihitung dari
      /// pengguna aktif, karena mode debug punya pengguna tanpa pernah login.
      /// </summary>
      public bool IsSignedIn {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>
      /// <c>true</c> untuk window yang lahir dari tab yang ditarik keluar: window seperti itu tidak punya
      /// isi selain tabnya, jadi menutup dirinya begitu tab terakhirnya ditutup atau dipindah, dan tidak
      /// menampilkan menu Apps, menu Tools, combobox koneksi, tombol tema, maupun tombol akun - semuanya
      /// hanya ada di window utama. Window utama bernilai <c>false</c> dan tidak pernah dibiarkan
      /// kosong karena drag.
      /// </summary>
      public bool ClosesWhenEmpty {
         get => Get<bool>();
         set => Set(value, _ => RefreshChrome());
      }

      /// <summary>Apakah aplikasi berjalan dalam mode debug.</summary>
      public bool IsDebugMode => EmApp?.IsDebugMode ?? false;

      /// <summary>Visibilitas deretan tab dan tombol daftar tab: hanya di mode tab layout multi-tab.</summary>
      public Visibility TabStripVisibility =>
         !IsSpaLayout && IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibilitas tombol dropdown daftar tab: seperti <see cref="TabStripVisibility"/>, dan hanya
      /// selama ada tab - tanpa tab, daftarnya kosong dan kartu konten sudah menampilkan "No tab open".
      /// </summary>
      public Visibility TabListButtonVisibility =>
         TabStripVisibility == Visibility.Visible && Tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibilitas menu Apps, menu Tools, tombol tema, dan tombol akun: hanya di window utama layout
      /// multi-tab, dan hanya selama ada yang masuk.
      /// </summary>
      public Visibility WorkspaceToolsVisibility =>
         !IsSpaLayout && IsSignedIn && !ClosesWhenEmpty ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>
      /// Visibilitas combobox koneksi: seperti <see cref="WorkspaceToolsVisibility"/>, dan hanya di mode
      /// debug - di luar itu koneksi dipilih di layar login.
      /// </summary>
      public Visibility ConnectionVisibility =>
         IsDebugMode && WorkspaceToolsVisibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

      /// <summary>Apakah teks "No tab open" ditampilkan: mode tab tanpa satu tab pun yang aktif.</summary>
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
      /// Host navigasi yang mengisi kartu konten di layout satu halaman, atau <c>null</c> di layout
      /// multi-tab.
      /// </summary>
      public object? SpaHost {
         get => Get<object?>();
         set => Set(value, _ => NotifyChanged(nameof(CardContent)));
      }

      /// <summary>
      /// Isi kartu konten: host navigasi di layout satu halaman, atau body entri yang sedang aktif di
      /// layout multi-tab (termasuk layar login selama belum ada yang masuk).
      /// </summary>
      public object? CardContent => IsSpaLayout ? SpaHost : ActiveTab?.Content;

      #endregion

      #region Tabs

      /// <summary>
      /// Stack yang ditampilkan window ini sebagai tab, atau <c>null</c> di layout satu halaman.
      /// <see cref="Tabs"/>, <see cref="SelectedTab"/>, dan isi kartu konten mengikuti stack ini.
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

      /// <summary>Tab yang sedang terbuka, berurutan dari kiri ke kanan.</summary>
      public ObservableCollection<TabbedMainWindowTab> Tabs {
         get => Get<ObservableCollection<TabbedMainWindowTab>>();
         private set => Set(value);
      }

      /// <summary>
      /// Tab yang disorot di deretan tab. Memilih tab dari UI memindahkan stack ke entri tab itu - body
      /// yang ditinggalkan tetap ditanya dan boleh menolak; kalau menolak, sorotan kembali ke tab
      /// semula.
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
      /// Tab yang body-nya sedang tampil di kartu konten, yaitu entri <see cref="NavigationStack.Current"/>.
      /// Bisa sesaat berbeda dari <see cref="SelectedTab"/> selama body yang ditinggalkan masih ditanya.
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
      /// Apakah dropdown daftar tab (tombol di kanan deretan tab) sedang terbuka. Membukanya
      /// sekaligus mengosongkan <see cref="TabListFilter"/>, supaya daftar selalu mulai lengkap.
      /// </summary>
      public bool IsTabListOpen {
         get => Get<bool>();
         set => Set(value, open => {
            if (open) TabListFilter = string.Empty;
         });
      }

      /// <summary>Teks pencarian di dropdown daftar tab; dicocokkan ke judul tab tanpa peduli huruf besar/kecil.</summary>
      public string TabListFilter {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(TabListItems)));
      }

      /// <summary>Tab yang ditampilkan dropdown daftar tab: semua tab, disaring oleh <see cref="TabListFilter"/>.</summary>
      public IEnumerable<TabbedMainWindowTab> TabListItems =>
         string.IsNullOrWhiteSpace(TabListFilter)
            ? Tabs
            : Tabs.Where(t => t.Title.Contains(TabListFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

      /// <summary>
      /// Apakah kotak pencarian di dropdown daftar tab ditampilkan. Baru muncul saat tabnya cukup banyak
      /// untuk perlu dicari; dengan sedikit tab, daftar itu sendiri sudah cukup dibaca sekilas.
      /// </summary>
      public bool IsTabSearchVisible => Tabs.Count >= TabSearchThreshold;

      // The number of open tabs from which the tab list offers a search box.
      private const int TabSearchThreshold = 8;

      public async Task CloseTabCommand(TabbedMainWindowTab tab) {
         try {
            await tab.Entry.Close();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

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

      public bool CloseOtherTabsCommandAllowed(TabbedMainWindowTab tab) => Tabs.Contains(tab) && Tabs.Count > 1;

      // When the tab shown is among the ones going, the tab clicked is the natural one to land on.
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

      public bool CloseTabsToRightCommandAllowed(TabbedMainWindowTab tab) {
         var index = Tabs.IndexOf(tab);
         return index >= 0 && index < Tabs.Count - 1;
      }

      public void MoveTabToNewWindowCommand(TabbedMainWindowTab tab) => TearOffRequested?.Invoke(tab);

      public bool MoveTabToNewWindowCommandAllowed(TabbedMainWindowTab tab) => CanTearOff(tab);

      // UiCommand is not tied to CommandManager.RequerySuggested, so the tab menu's commands have to be
      // told when the tab list they depend on has changed.
      private void RefreshTabCommands() {
         Commands[nameof(CloseOtherTabsCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(CloseTabsToRightCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(MoveTabToNewWindowCommand)]?.RaiseCanExecuteChanged();
      }

      public void SelectTabCommand(TabbedMainWindowTab tab) {
         IsTabListOpen = false;
         SelectedTab = tab;
      }

      /// <summary>
      /// Apakah <paramref name="tab"/> boleh ditarik keluar menjadi window baru. Hanya kalau window ini
      /// masih punya tab lain: tab tunggal tidak dipisah lagi, window-nya cukup ditutup.
      /// </summary>
      /// <param name="tab">Tab yang hendak ditarik.</param>
      public bool CanTearOff(TabbedMainWindowTab tab) => Tabs.Contains(tab) && Stack is { Entries.Count: > 1 };

      /// <summary>
      /// Apakah <paramref name="tab"/> boleh keluar dari window ini, entah menjadi window baru atau
      /// pindah ke window lain. Tab terakhir window utama tidak boleh, supaya window utama tidak
      /// kosong; tab terakhir window hasil drag-out boleh pindah ke window lain, lalu window-nya
      /// menutup sendiri.
      /// </summary>
      /// <param name="tab">Tab yang hendak ditarik.</param>
      public bool CanMoveOut(TabbedMainWindowTab tab) =>
         Tabs.Contains(tab) && (ClosesWhenEmpty || Stack is { Entries.Count: > 1 });

      /// <summary>
      /// Apakah <paramref name="tab"/> dari window lain boleh masuk ke window ini. Ditolak kalau di sini
      /// sudah ada tab lain dengan judul yang sama, karena judul adalah kunci unik tab.
      /// </summary>
      /// <param name="tab">Tab yang hendak dimasukkan.</param>
      public bool CanAcceptTab(TabbedMainWindowTab tab) =>
         Stack != null
         && !Tabs.Any(t => t != tab && string.Equals(t.Title, tab.Title, NavigationStack.TitleComparison));

      /// <summary>
      /// Menggeser <paramref name="tab"/> ke posisi <paramref name="index"/> di deretan tab window ini,
      /// tanpa mengubah tab yang aktif dan tanpa memberi tahu body mana pun.
      /// </summary>
      /// <param name="tab">Tab yang digeser; harus milik window ini.</param>
      /// <param name="index">Posisi barunya, dihitung dari kiri mulai 0.</param>
      public void MoveTab(TabbedMainWindowTab tab, int index) => Stack?.Move(tab.Entry, index);

      #endregion

      #region Menus

      /// <summary>
      /// Isi menu Apps, level teratas: navigasi yang tampil di menu dan boleh dibuka pengguna aktif,
      /// dikelompokkan menurut jalur menunya. Item yang punya <see cref="TabbedMainWindowMenuItem.Items"/>
      /// membuka submenu ke kanan, jadi menunya bisa bertingkat sedalam yang dibutuhkan.
      /// </summary>
      public ObservableCollection<TabbedMainWindowMenuItem> AppMenu {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }

      /// <summary>
      /// Isi dropdown Tools di kanan baris judul: tool bawaan aplikasi, sama dengan yang tampil di home
      /// layout satu halaman. Bentuknya sama dengan <see cref="AppMenu"/>.
      /// </summary>
      public ObservableCollection<TabbedMainWindowMenuItem> ToolsMenu {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }

      /// <summary>
      /// Apakah dropdown Apps sedang terbuka. Tombol Apps dan menunya sama-sama terikat ke property
      /// ini, jadi keduanya tidak pernah berbeda keadaan.
      /// </summary>
      public bool IsAppsMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Apakah dropdown Tools sedang terbuka.</summary>
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
      /// Profil koneksi yang ditawarkan combobox koneksi, yaitu koleksi milik
      /// <see cref="Core.EmApp.UIConnections"/> apa adanya. Null-safe karena XAML membuat ViewModel ini
      /// sebelum <see cref="MvvmModelBase.EmApp"/> sempat di-set.
      /// </summary>
      public ObservableCollection<ApiConnection>? Connections => EmApp?.UIConnections;

      /// <summary>
      /// Profil koneksi yang dipilih di combobox koneksi. Memilihnya menjadikannya koneksi aktif
      /// aplikasi (<see cref="Core.EmApp.ActiveConnection"/>).
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

      public ObservableCollection<HubTaskSectionVm> HubTaskSections { get; } = [];
      public bool HasHubTasks => HubTaskSections.Count > 0;
      public bool HasRunningTasks => TaskHubItems.Count > 0;
      public bool HubTaskLoadFailed { get => Get<bool>(); private set => Set(value); }

      public bool OpenHubTaskCommandAllowed(HubTaskRowVm? row) => row?.CanOpen == true && EmApp?.ActiveUser is not null;

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

      /// <summary>Task personal user yang login, sebagaimana dipantau <see cref="BusinessTaskTracker"/>.</summary>
      public ObservableCollection<BusinessTaskItem> TaskHubItems { get; } = [];

      /// <summary>
      /// Visibilitas tombol hub task: di window utama kedua layout, selama ada user yang login.
      /// </summary>
      public Visibility TaskHubVisibility =>
         _taskTracker is not null && !ClosesWhenEmpty && EmApp?.ActiveUser is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

      /// <summary>Jumlah task personal yang masih antri atau berjalan, untuk badge tombol hub.</summary>
      public int TaskHubAliveCount => _taskTracker?.AliveCount ?? 0;

      /// <summary><c>true</c> selama ada task personal yang hidup: ikon hub berputar dan badge tampil.</summary>
      public bool IsTaskHubBusy => TaskHubAliveCount > 0;

      /// <summary><c>true</c> kalau ada task yang selesai sejak popup hub terakhir dibuka.</summary>
      public bool TaskHubHasUnseen => _taskTracker?.HasUnseen ?? false;

      /// <summary><c>true</c> kalau di antara task yang belum dilihat ada yang gagal; titik penandanya merah.</summary>
      public bool TaskHubHasUnseenFailure => _taskTracker?.HasUnseenFailure ?? false;

      /// <summary><c>true</c> kalau semua bagian daftar pekerjaan kosong.</summary>
      public bool IsTaskHubEmpty => TaskHubItems.Count == 0 && !HasHubTasks;

      /// <summary>
      /// Apakah popup hub task sedang terbuka. Membukanya memuat ulang daftar task dan menandai semua
      /// task yang sudah selesai sebagai sudah dilihat.
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

      /// <summary>Membuka dialog status task <paramref name="item"/>.</summary>
      public void OpenTaskCommand(BusinessTaskItem? item) {
         if (item is null || EmApp is not { } app) return;

         IsTaskHubOpen = false;
         var dialog = new BusinessTaskStatusDialog(app, item.Info) { Owner = DialogOwner };
         dialog.ShowDialog();
         _ = _taskTracker?.RefreshAsync();
      }

      /// <summary>Hanya untuk sebuah baris task.</summary>
      public bool OpenTaskCommandAllowed(BusinessTaskItem? item) => item is not null;

      #endregion

      #region Account

      /// <summary>Apakah menu akun (dropdown di balik tombol pengguna) sedang terbuka.</summary>
      public bool IsUserMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Nama pengguna yang ditampilkan di menu akun dan tooltip tombol akun.</summary>
      public string UserDisplayName {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>Nama akun pengguna, baris kedua di menu akun.</summary>
      public string UserAccount {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>
      /// Inisial untuk lingkaran avatar. Selalu berisi sesuatu: <c>?</c> kalau dikosongkan, supaya
      /// lingkarannya tidak pernah tampil kosong.
      /// </summary>
      public string UserInitials {
         get => Get("?");
         set => Set(string.IsNullOrWhiteSpace(value) ? "?" : value);
      }

      /// <summary>
      /// Warna lingkaran avatar, dari palet yang sama dengan tombol akun layout satu halaman: orang yang
      /// sama selalu dapat warna yang sama.
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

      public void ChangePasswordCommand() {
         IsUserMenuOpen = false;
      }

      public bool ChangePasswordCommandAllowed() => false;

      // The same sign-out path as the single-page layout's account button.
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
      public void ColorThemeCommand() {
         EmApp!.CurrentTheme = EmApp.CurrentTheme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
      }

      public bool ColorThemeCommandAllowed() => EmApp != null;

      #endregion

      /// <summary>
      /// Menyalin bingkai window lain ke window ini - judul, keadaan login, dan aplikasinya - lalu
      /// menandainya <see cref="ClosesWhenEmpty"/>. Dipakai untuk window baru yang menampung tab yang
      /// ditarik keluar. Window seperti itu hanya berisi deretan tab dan dropdown daftar tab: menu
      /// Apps, menu Tools, dan identitas pengguna milik window utama saja, jadi tidak ikut disalin.
      /// Tab-tabnya sendiri mengikuti stack window ini.
      /// </summary>
      /// <param name="source">Window asal tab.</param>
      public void CopyShellFrom(TabbedMainWindowVm source) {
         Title = source.Title;
         IsSignedIn = source.IsSignedIn;
         ClosesWhenEmpty = true;
         EmApp = source.EmApp;
      }

      public void MinimizeCommand() {
         WindowState = WindowState.Minimized;
      }

      public void MaximizeRestoreCommand() {
         WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
      }

      public void CloseWindowCommand() {
         RequestClose?.Invoke();
      }
   }

   /// <summary>
   /// Satu tab di <see cref="TabbedMainWindow"/>: satu entri navigasi di stack window itu. Judulnya
   /// mengikuti judul entri, termasuk sesudah entrinya mengganti judul sendiri.
   /// </summary>
   public class TabbedMainWindowTab : NotifyPropertyBase
   {
      internal TabbedMainWindowTab(NavigationEntry entry) {
         Entry = entry;
         Title = entry.Title;
         Content = entry.Body;
         entry.PropertyChanged += EntryPropertyChanged;
      }

      /// <summary>Entri navigasi yang ditampilkan tab ini.</summary>
      public NavigationEntry Entry { get; }

      /// <summary>Judul tab, yaitu judul entrinya - sekaligus kunci uniknya di seluruh aplikasi.</summary>
      public string Title {
         get => Get(string.Empty);
         private set => Set(value);
      }

      /// <summary>
      /// Apakah tab ini yang sedang disorot di window-nya. Diisi oleh
      /// <see cref="TabbedMainWindowVm.SelectedTab"/>, dipakai dropdown daftar tab untuk menandainya.
      /// </summary>
      public bool IsActive {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Isi tab, yaitu body entrinya; dipakai ulang setiap kali tab dipilih.</summary>
      public object Content { get; }

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) Title = Entry.Title;
      }

      // The tab has left its window; it stops following the entry, which lives on elsewhere.
      internal void Detach() => Entry.PropertyChanged -= EntryPropertyChanged;
   }

   /// <summary>
   /// Satu item di menu Apps atau Tools. Item daun membuka <see cref="Navigation"/> - sebagai tab baru,
   /// atau memilih tab yang sudah membukanya - atau menjalankan <see cref="Invoke"/>. Item cabang
   /// (keduanya <c>null</c>) hanya membuka submenu berisi <see cref="Items"/>.
   /// </summary>
   public class TabbedMainWindowMenuItem : NotifyPropertyBase
   {
      public TabbedMainWindowMenuItem() {
         Items = [];
      }

      /// <summary>Judul item.</summary>
      public string Title {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>Keterangan singkat di bawah judul, atau <c>null</c> kalau tidak ada.</summary>
      public string? Subtitle {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>Navigasi yang dibuka item ini, atau <c>null</c> untuk item cabang dan tool yang bukan navigasi.</summary>
      public Navigation? Navigation {
         get => Get<Navigation?>();
         set => Set(value);
      }

      /// <summary>Aksi item yang bukan navigasi (mis. membuka dialog), atau <c>null</c>.</summary>
      public Func<Task>? Invoke {
         get => Get<Func<Task>?>();
         set => Set(value);
      }

      /// <summary>Apakah item ini cabang: tidak membuka apa pun selain submenunya.</summary>
      public bool IsBranch => Navigation == null && Invoke == null;

      /// <summary>Anak-anak item ini, ditampilkan sebagai submenu. Kosong untuk item daun.</summary>
      public ObservableCollection<TabbedMainWindowMenuItem> Items {
         get => Get<ObservableCollection<TabbedMainWindowMenuItem>>();
         private set => Set(value);
      }
   }
}
