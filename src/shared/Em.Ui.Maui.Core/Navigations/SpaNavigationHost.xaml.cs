using System.ComponentModel;
using Microsoft.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// Halaman utama aplikasi: bilah atas, panel account yang masuk dari kanan, dan satu tempat kosong
   /// yang diisi body layar yang sedang dibuka. Inilah satu-satunya halaman aplikasi - berpindah layar
   /// berarti mengganti isi tempat itu, bukan menumpuk halaman baru.
   /// </summary>
   public partial class SpaNavigationHost : ContentPage
   {
      // Lama animasi tirai panel account, dan seberapa gelap ia saat panelnya terbuka penuh.
      // Angkanya mengikuti gerak baku Material: membuka sedikit lebih lambat daripada menutup.
      private const uint OpenDuration = 260;
      private const uint CloseDuration = 200;
      private const double ScrimOpacity = 0.4;

      public SpaNavigationHost(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.HostPage = this;
         Vm.PropertyChanged += VmOnPropertyChanged;
         Vm.Stack = app.MainStack;

         AccountPanelView.Vm.EmApp = app;
         AccountPanelView.Vm.HostPage = this;
         // Panel yang menutup dirinya sendiri hanya akan meluncur keluar tanpa tirainya ikut memudar:
         // tirai itu milik halaman ini. Jadi panel memintanya, dan halaman ini yang mengerjakan.
         AccountPanelView.Vm.CloseRequested += (_, _) => Vm.IsAccountPanelOpen = false;

         app.ActiveUserChanged += (_, _) => Vm.RefreshAccount();
         app.ActiveConnectionChanged += (_, _) => Vm.RefreshAccount();
         app.ThemeChanged += (_, _) => AccountPanelView.Vm.RefreshTheme();
         // Sesi bisa berakhir tanpa ada yang menekan tombol keluar - umurnya habis, atau dicabut dari
         // tempat lain. Keduanya harus mendarat di layar login lewat jalan yang sama persis.
         app.SessionEnded += OnSessionEnded;
      }

      /// <summary>View model halaman ini, dibaca balik dari BindingContext yang dipasang di XAML.</summary>
      public SpaNavigationHostVm Vm => (SpaNavigationHostVm)BindingContext;

      // Dua hal di bawah ini memang urusan tampilan, bukan view model: memasang control ke sebuah slot
      // memindahkan instance-nya, bukan nilai, dan menggeser panel beserta tirainya adalah animasi
      // atas elemen yang konkret. Keduanya tidak bisa diungkapkan sebagai binding.
      private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         switch (e.PropertyName) {
            case nameof(SpaNavigationHostVm.Entry):
               BodyHost.Content = Vm.Entry?.Body as View;
               break;
            case nameof(SpaNavigationHostVm.IsAccountPanelOpen):
               _ = AnimateAccountPanelAsync(Vm.IsAccountPanelOpen);
               break;
         }
      }

      private async Task AnimateAccountPanelAsync(bool open) {
         // Tirai dibuat bisa disentuh lebih dulu saat membuka, dan baru dilepas sesudah tertutup penuh
         // saat menutup - supaya tidak pernah ada saat panel masih terlihat tapi sentuhan sudah tembus
         // ke isi halaman di belakangnya.
         if (open) Scrim.InputTransparent = false;

         var duration = open ? OpenDuration : CloseDuration;
         var easing = open ? Easing.CubicOut : Easing.CubicIn;

         await Task.WhenAll(
            AccountPanelView.AnimateAsync(open, Vm.Navigation),
            Scrim.FadeToAsync(open ? ScrimOpacity : 0, duration, easing));

         if (!open) Scrim.InputTransparent = true;
      }

      // Tombol kembali milik perangkat adalah jalan keluar yang paling sering dipakai di Android, jadi
      // ia harus mengerti keadaan layar: menutup panel account kalau sedang terbuka, mundur satu layar
      // kalau masih ada jalur yang bisa ditelusuri, dan baru menyerah ke perilaku bawaan kalau tidak.
      protected override bool OnBackButtonPressed() {
         if (Vm.IsAccountPanelOpen) {
            Vm.IsAccountPanelOpen = false;
            return true;
         }

         if (Vm.Stack is not { CanGoBack: true } stack) return base.OnBackButtonPressed();

         _ = stack.Backward();
         return true;
      }

      private void OnSessionEnded(object? sender, SessionEndedEventArgs e) {
         Dispatcher.Dispatch(() => {
            Vm.IsAccountPanelOpen = false;
            _ = Vm.EmApp!.ShowLoginScreen(e.Reason);
         });
      }
   }

   /// <summary>
   /// View model <see cref="SpaNavigationHost"/>: menyimpan layar yang sedang dibuka dan
   /// perintah-perintah bilah atas.
   /// </summary>
   public class SpaNavigationHostVm : MvvmModelBase
   {
      public SpaNavigationHostVm() {
         RegisterCommand(nameof(LeadingCommand), LeadingCommand, LeadingCommandAllowed);
         RegisterCommand(nameof(HomeCommand), HomeCommand, HomeCommandAllowed);
         RegisterCommand(nameof(ForwardCommand), ForwardCommand, ForwardCommandAllowed);
         RegisterCommand(nameof(ReloadCommand), ReloadCommand, ReloadCommandAllowed);
         RegisterCommand(nameof(AccountPanelCommand), AccountPanelCommand, AccountPanelCommandAllowed);
         RegisterCommand(nameof(CloseAccountPanelCommand), CloseAccountPanelCommand);
      }

      /// <summary>Apakah panel account sedang terbuka. Halaman menganimasikan perpindahannya.</summary>
      public bool IsAccountPanelOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Stack yang ditampilkan halaman ini. Halaman mengikuti stack itu sendiri - entri yang sedang
      /// tampil dan isi jalurnya - jadi tidak ada pihak lain yang perlu mendorong perubahan ke sini.
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

            // The title belongs to the entry and may be renamed while it is shown, so the page has to
            // stop listening to the one it is leaving - an entry that stays alive in the stack keeps
            // raising changes, and an old subscription would let it repaint a bar it no longer owns.
            if (field != null) field.PropertyChanged -= EntryPropertyChanged;
            field = value;
            if (field != null) field.PropertyChanged += EntryPropertyChanged;

            NotifyChanged();
            NotifyChanged(nameof(Navigation));
            NotifyChanged(nameof(IsToolbarVisible));
            NotifyChanged(nameof(IsTitleVisible));
            NotifyChanged(nameof(IsReloadVisible));
            RefreshNavigationCommands();
         }
      }

      /// <summary>Definisi layar yang sedang tampil; sumber saklar-saklar bilah atas.</summary>
      public Navigation? Navigation => Entry?.Navigation;

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Entry = Stack?.Current;
      }

      private void StackChanged(object? sender, EventArgs e) => RefreshNavigationCommands();

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) NotifyChanged(nameof(Title));
      }

      // Home tidak pernah jadi entri jalur navigasi, jadi "sedang di home" berarti yang tampil adalah
      // entri home itu sendiri - atau belum ada yang tampil sama sekali.
      private bool IsAtHome => Stack?.Current is null || Stack.Current == Stack.Home;

      /// <summary>Judul di bilah atas: nama aplikasi saat di home, judul entrinya saat di layar lain.</summary>
      public string Title => IsAtHome
         ? EmApp?.Branding.DisplayTitle ?? string.Empty
         : Entry?.Title ?? string.Empty;

      /// <summary>Keterangan di bawah judul.</summary>
      public string Subtitle => IsAtHome
         ? EmApp?.Branding.DisplayTagline ?? string.Empty
         : Navigation?.Subtitle ?? string.Empty;

      /// <summary><c>true</c> kalau ada keterangan yang perlu digambar di bawah judul.</summary>
      public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

      /// <summary>Apakah masih ada layar di depan yang bisa dituju.</summary>
      /// <remarks>
      /// Tidak ada tombolnya di bilah atas Android, tapi jalurnya sengaja dipertahankan: host ini bisa
      /// dipakai lagi di MAUI desktop, tempat maju punya arti dan punya tempatnya sendiri.
      /// </remarks>
      public bool CanGoForward {
         get {
            if (Navigation is null) return false;
            if (!Navigation.IsForwardVisible) return false;

            return Stack?.CanGoForward == true;
         }
      }

      // Setiap saklar bilah atas dimiliki navigasinya. Tanpa navigasi sama sekali tidak ada yang perlu
      // digambar, jadi jawabannya false - bukan nilai bawaan milik Navigation.
      public bool IsToolbarVisible => Navigation?.IsToolbarVisible ?? false;
      public bool IsTitleVisible => Navigation?.IsTitleVisible ?? false;
      public bool IsReloadVisible => Navigation?.IsReloadVisible ?? false;

      /// <summary>
      /// Apakah tombol mundur perlu digambar. Di home jawabannya selalu tidak: jalur navigasinya masih
      /// kosong, dan tombol mundur yang tidak menuju ke mana-mana hanya membingungkan.
      /// </summary>
      public bool IsBackVisible => !IsAtHome && (Navigation?.IsBackVisible ?? false);

      /// <summary>Apakah tombol pulang perlu digambar; seperti mundur, di home ia tidak ada gunanya.</summary>
      public bool IsHomeVisible => !IsAtHome && (Navigation?.IsHomeVisible ?? false);

      /// <summary>
      /// Apakah badge akun perlu digambar. Berbeda dari mundur dan pulang, badge ini ada di setiap
      /// layar termasuk home - dialah satu-satunya jalan ke panel account.
      /// </summary>
      public bool IsUserVisible => Navigation?.IsUserVisible ?? false;

      /// <summary>
      /// Inisial akun yang sedang masuk, isi lingkaran badge. Satu-satunya keterangan akun yang masih
      /// digambar halaman ini - selebihnya milik panel account. Nama lengkap didahulukan, dan nama
      /// akun jadi cadangan kalau kontaknya belum punya nama.
      /// </summary>
      public string AccountInitials => EmApp?.ActiveUser is { } user
         ? (user.cContactFullName is { Length: > 0 } fullName ? fullName : user.cUserAccount).ToInitials()
         : "?";

      /// <summary>
      /// Meminta seluruh tombol bilah atas menghitung ulang apakah dirinya masih boleh ditekan.
      /// Dipanggil setiap kali isi jalur navigasi berubah.
      /// </summary>
      public void RefreshNavigationCommands() {
         NotifyChanged(nameof(CanGoForward));
         NotifyChanged(nameof(IsBackVisible));
         NotifyChanged(nameof(IsHomeVisible));
         NotifyChanged(nameof(IsUserVisible));
         NotifyChanged(nameof(Title));
         NotifyChanged(nameof(Subtitle));
         NotifyChanged(nameof(HasSubtitle));
         Commands[nameof(LeadingCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(HomeCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ForwardCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ReloadCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>Menggambar ulang badge akun sesudah pengguna atau servernya berganti.</summary>
      public void RefreshAccount() => NotifyChanged(nameof(AccountInitials));

      /// <summary>Tombol paling kiri bilah atas: mundur satu layar.</summary>
      public Task LeadingCommand() => Stack!.Backward();
      public bool LeadingCommandAllowed() => Stack?.CanGoBack == true;

      /// <summary>Pulang ke home dari layar mana pun, tanpa perlu mundur selangkah demi selangkah.</summary>
      public Task HomeCommand() => Stack!.NavigateHome();
      public bool HomeCommandAllowed() => Stack?.Home is not null && !IsAtHome;

      public Task ForwardCommand() => Stack!.Forward();
      public bool ForwardCommandAllowed() => CanGoForward;

      public Task ReloadCommand() => Entry?.Reload() ?? Task.CompletedTask;
      public bool ReloadCommandAllowed() => Entry is not null;

      /// <summary>Membuka - atau menutup lagi - panel account lewat badge di bilah atas.</summary>
      public void AccountPanelCommand() => IsAccountPanelOpen = !IsAccountPanelOpen;
      public bool AccountPanelCommandAllowed() => EmApp is not null;

      public void CloseAccountPanelCommand() => IsAccountPanelOpen = false;
   }
}
