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
            _activeUserHooked = true;
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

      // EmApp is a plain object rather than a bindable source, so a new signed-in user does not
      // reach the account button on its own - this is what tells the toolbar to read it again.
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

      // Toolbar yang disembunyikan tidak menawarkan apa-apa, termasuk lewat keyboard dan tombol
      // thumb mouse: keduanya sampai ke sini sebagai BrowseBack/BrowseForward, dan tanpa syarat ini
      // sebuah layar seperti login - yang mematikan toolbar-nya tapi tidak menyebut tombolnya satu
      // per satu - masih bisa ditinggalkan dengan Alt+Left.
      private bool CanInvoke(string commandName, Visibility offered) =>
         Vm.IsToolbarVisible == Visibility.Visible
         && offered == Visibility.Visible
         && Vm.Commands[commandName]?.CanExecute(null) == true;
   }
   public class SpaNavigationHostVm : MvvmModelBase
   {
      /// <summary>
      /// Nama command tombol Back pada toolbar, dipakai sumber input lain (tombol mouse, shortcut
      /// keyboard) supaya memanggil command yang sama dengan tombolnya.
      /// </summary>
      public const string BackCommand = nameof(Back);

      /// <summary>
      /// Nama command tombol Forward pada toolbar. Lihat <see cref="BackCommand"/>.
      /// </summary>
      public const string ForwardCommand = nameof(Forward);

      public SpaNavigationHostVm() {
         RegisterCommand(nameof(Back), Back, BackAllowed);
         RegisterCommand(nameof(Forward), Forward, ForwardAllowed);
         RegisterCommand(nameof(Home), Home, HomeAllowed);
         RegisterCommand(nameof(Reload), Reload, ReloadAllowed);
         RegisterCommand(nameof(DetachWindow), DetachWindow, DetachWindowAllowed);
         RegisterCommand(nameof(ColorTheme), ColorTheme, ColorThemeAllowed);
         RegisterCommand(nameof(ChangePasswordCommand), ChangePasswordCommand, ChangePasswordCommandAllowed);
         RegisterCommand(nameof(SignOutCommand), SignOutCommand, SignOutCommandAllowed);
      }
      /// <summary>
      /// Stack yang ditampilkan host ini. Host mengikuti stack itu sendiri - entri yang sedang tampil
      /// dan isi jalurnya - jadi tidak ada pihak lain yang perlu mendorong perubahan ke sini.
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

      /// <summary>Entri yang sedang tampil, atau <c>null</c> sebelum ada yang pernah ditampilkan.</summary>
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

      /// <summary>Definisi layar yang sedang tampil; sumber saklar-saklar toolbar.</summary>
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
      public UserControl? NavigationBody => Entry?.Body as UserControl;
      
      // Each switch is owned by the navigation being shown; the host only translates it for XAML.
      // A navigation is assigned long after the DataContext is built, so a null one still has to
      // render something - the toolbar stays complete until a navigation says otherwise.
      public Visibility IsToolbarVisible => ToVisibility(Navigation?.IsToolbarVisible);
      public Visibility IsTitleVisible => ToVisibility(Navigation?.IsTitleVisible);
      public Visibility IsBackVisible => ToVisibility(Navigation?.IsBackVisible);
      public Visibility IsForwardVisible => ToVisibility(Navigation?.IsForwardVisible);
      public Visibility IsReloadVisible => ToVisibility(Navigation?.IsReloadVisible);
      // A detached window has no home to go back to, and the session is managed from the main window
      // alone, so both switches are off there whatever the navigation says.
      public Visibility IsHomeVisible => IsMainHost ? ToVisibility(Navigation?.IsHomeVisible) : Visibility.Collapsed;
      public Visibility IsDetachVisible =>
         Entry != null && Entry == Stack?.Home ? Visibility.Collapsed : ToVisibility(Navigation?.IsDetachVisible);
      public Visibility IsColorThemeVisible => ToVisibility(Navigation?.IsColorThemeVisible);
      public Visibility IsUserVisible => IsMainHost ? ToVisibility(Navigation?.IsUserVisible) : Visibility.Collapsed;

      /// <summary>
      /// <c>true</c> kalau host ini menampilkan stack utama aplikasi di window utama; <c>false</c> untuk
      /// host di dalam window detach.
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
      /// Apakah menu akun (dropdown di balik tombol pengguna) sedang terbuka. Tombolnya dan popup-nya
      /// sama-sama terikat ke property ini, jadi keduanya tidak pernah berbeda keadaan — dan command
      /// di dalam menu bisa menutup menunya cukup dengan mengisi <c>false</c> di sini.
      /// </summary>
      public bool IsUserMenuOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Pengguna yang sedang aktif, atau <c>null</c> selama belum ada yang sign in. Dibaca ulang dari
      /// aplikasi setiap kali <see cref="RefreshActiveUser"/> dipanggil.
      /// </summary>
      public User? ActiveUser => EmApp?.ActiveUser;

      /// <summary>
      /// Nama lengkap pengguna aktif untuk ditampilkan di menu akun; jatuh ke nama akunnya kalau nama
      /// lengkapnya kosong, dan ke teks penanda belum sign in kalau memang belum ada penggunanya.
      /// </summary>
      public string ActiveUserDisplayName => UserAvatar.DisplayName(ActiveUser);

      /// <summary>
      /// Nama akun pengguna aktif (baris kedua di menu akun), kosong kalau belum ada yang sign in.
      /// </summary>
      public string ActiveUserAccount => UserAvatar.Account(ActiveUser);

      /// <summary>
      /// Inisial pengguna aktif untuk dipakai sebagai avatar, mis. "SYSTEM DEBUGGER" jadi "SD".
      /// Selalu berisi sesuatu: <c>?</c> selama belum ada yang sign in, supaya lingkaran avatarnya
      /// tidak pernah tampil kosong.
      /// </summary>
      public string ActiveUserInitials => UserAvatar.Initials(ActiveUser);

      /// <summary>
      /// Warna lingkaran avatar pengguna aktif. Dipilih dari palet tetap berdasarkan identitas akunnya,
      /// jadi orang yang sama selalu dapat warna yang sama; abu netral selama belum ada yang sign in.
      /// </summary>
      public SolidColorBrush ActiveUserAvatarBrush => UserAvatar.Brush(ActiveUser);

      /// <summary>
      /// Memberi tahu UI supaya membaca ulang identitas pengguna aktif. Perlu dipanggil sendiri karena
      /// pemiliknya (<see cref="Core.EmApp.ActiveUser"/>) bukan sumber binding ber-notifikasi.
      /// </summary>
      public void RefreshActiveUser() {
         NotifyChanged(nameof(ActiveUser));
         NotifyChanged(nameof(ActiveUserDisplayName));
         NotifyChanged(nameof(ActiveUserAccount));
         NotifyChanged(nameof(ActiveUserInitials));
         NotifyChanged(nameof(ActiveUserAvatarBrush));
         Commands[nameof(ChangePasswordCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(SignOutCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>
      /// Membuka layar ganti password milik pengguna aktif. Layarnya belum ada, jadi command ini masih
      /// kosong dan <see cref="ChangePasswordCommandAllowed"/> selalu menolak — tombolnya sengaja tetap
      /// ada di menu supaya tempatnya sudah pasti saat layarnya menyusul.
      /// </summary>
      public void ChangePasswordCommand() {
         IsUserMenuOpen = false;
      }

      public bool ChangePasswordCommandAllowed() => false;

      /// <summary>
      /// Mengakhiri sesi: menutup menu akun, lalu meminta aplikasi membuang sesinya — di server
      /// sekaligus di sisi client. Jalurnya sama dengan tombol akun di layout multi-tab, jadi keluar
      /// dari layout mana pun berakhir di keadaan yang sama.
      /// </summary>
      public async Task SignOutCommand() {
         IsUserMenuOpen = false;

         // Tidak boleh ada yang lolos dari sini. ICommand.Execute itu void, jadi UiCommandAsync
         // menjalankannya sebagai async void: exception yang keluar dilempar ulang di dispatcher, dan
         // aplikasi ini tidak punya DispatcherUnhandledException yang menangkapnya.
         try {
            await EmApp!.SignOutAsync();
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      public bool SignOutCommandAllowed() => EmApp != null;

      #endregion

      async Task Back() {
         await Stack!.Backward();
      }
      bool BackAllowed() {
         //nanti bisa saja tidak boleh back dari module atas kondisi tertentu
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
