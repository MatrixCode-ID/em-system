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
   /// Panel account: akun yang sedang masuk, navigasi statis, pilihan server, ganti tema, dan keluar.
   /// Dipasang sebagai lapisan di atas halaman utama dan masuk dari kanan saat badge di bilah atas
   /// di-tap. Inilah yang menggantikan menu samping - satu tempat untuk semua yang bukan perpindahan
   /// layar biasa.
   /// </summary>
   public partial class AccountPanel : ContentView
   {
      // Sama persis dengan lama animasi tirai di halaman pemiliknya: keduanya satu gerakan, dan gerakan
      // yang panelnya selesai lebih dulu dari tirainya terbaca sebagai dua hal yang tidak berhubungan.
      private const uint OpenDuration = 260;
      private const uint CloseDuration = 200;

      public AccountPanel() {
         InitializeComponent();
         // Mulai dari luar tepi kanan layar. Lebarnya dipakai apa adanya, bukan diukur saat animasi
         // berjalan: saat panel belum pernah tampil, ukurannya masih nol dan ia akan meluncur dari
         // tempat yang salah.
         TranslationX = WidthRequest;
      }

      /// <summary>View model panel ini, dibaca balik dari BindingContext yang dipasang di XAML.</summary>
      public AccountPanelVm Vm => (AccountPanelVm)BindingContext;

      /// <summary>
      /// Menggeser panel masuk atau keluar layar. Isinya dibangun ulang tepat sebelum ia masuk - bukan
      /// sekali saat panel dibuat - karena katalog claim bisa baru selesai dimuat sesudah halaman
      /// utama berdiri, dan baris yang dihitung terlalu dini akan hilang sepanjang sesi pertama.
      /// </summary>
      /// <param name="open"><c>true</c> untuk memasukkan panel, <c>false</c> untuk mengeluarkannya.</param>
      /// <param name="navigation">Layar yang sedang dibuka, penentu saklar tampil milik panel ini.</param>
      public async Task AnimateAsync(bool open, Navigation? navigation) {
         if (open) {
            Vm.Reload(navigation);
            // Disembunyikan selama tertutup, bukan sekadar digeser keluar: panel yang tergeser tetap
            // menempati pita selebar dirinya di tepi kanan layar, dan di sebagian perangkat pita itu
            // masih menangkap sentuhan yang sebenarnya ditujukan ke isi halaman di belakangnya.
            IsVisible = true;
         }

         var duration = open ? OpenDuration : CloseDuration;
         var easing = open ? Easing.CubicOut : Easing.CubicIn;
         await this.TranslateToAsync(open ? 0 : WidthRequest, 0, duration, easing);

         if (!open) IsVisible = false;
      }
   }

   /// <summary>
   /// Satu baris di daftar navigasi statis panel account. Bisa mewakili sebuah layar, atau - seperti
   /// Simulate Login - sebuah perbuatan yang tidak punya layar sendiri.
   /// </summary>
   public sealed class AccountPanelItemVm
   {
      /// <summary>Teks baris ini.</summary>
      public required string Title { get; init; }

      /// <summary>Ikon baris ini, diambil dari <see cref="FontIcons"/>.</summary>
      public required string Glyph { get; init; }

      /// <summary>Yang dikerjakan baris ini saat di-tap.</summary>
      public required Func<Task> Action { get; init; }

      /// <summary>
      /// Apakah baris ini boleh ditekan. Baris yang tidak boleh tetap digambar, hanya dipudarkan -
      /// menghilangkannya akan membuat panel terlihat berbeda-beda isinya tanpa alasan yang terbaca.
      /// </summary>
      public bool IsEnabled { get; init; } = true;

      /// <summary>
      /// Apakah baris ini perlu garis pemisah di atasnya. Diisi saat daftarnya dibangun, supaya baris
      /// pertama tidak punya garis yang menempel ke tepi atas card.
      /// </summary>
      public bool HasDividerAbove { get; set; }
   }

   /// <summary>Satu pilihan server di panel account.</summary>
   public sealed class AccountPanelConnectionVm(ApiConnection connection) : NotifyPropertyBase
   {
      /// <summary>Profil koneksi yang diwakili baris ini.</summary>
      public ApiConnection Connection { get; } = connection;

      /// <summary>Nama profil koneksi.</summary>
      public string ProfileName => Connection.ProfileName;

      /// <summary>Alamat server profil ini.</summary>
      public string Host => Connection.Host;

      /// <summary>
      /// Apakah baris ini yang sedang terpilih. Yang menyalakannya selalu panel - hanya panel yang
      /// tahu baris mana lagi yang harus ikut padam, dan tanpa itu dua baris bisa menyala sekaligus.
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
      /// Dipicu saat panel perlu ditutup - lewat tombol silang, atau sesudah sebuah baris membawa
      /// penggunanya ke layar lain. Halaman pemiliknya yang menutup, karena dialah yang juga memegang
      /// tirai di belakang panel ini.
      /// </summary>
      public event EventHandler? CloseRequested;

      public AccountPanelVm() {
         RegisterCommand(nameof(CloseCommand), CloseCommand);
         RegisterCommand(nameof(OpenItemCommand), OpenItemCommand);
         RegisterCommand(nameof(SelectConnectionCommand), SelectConnectionCommand);
         RegisterCommand(nameof(ToggleThemeCommand), ToggleThemeCommand);
         RegisterCommand(nameof(SignOutCommand), SignOutCommand, SignOutCommandAllowed);
      }

      /// <summary>Baris-baris navigasi statis, dibangun ulang setiap panel dibuka.</summary>
      public ObservableCollection<AccountPanelItemVm> StaticItems { get; } = [];

      /// <summary>Pilihan server yang tersedia, dibangun ulang setiap panel dibuka.</summary>
      public ObservableCollection<AccountPanelConnectionVm> Connections { get; } = [];

      /// <summary>
      /// <c>true</c> kalau ada baris navigasi statis yang perlu digambar. Card-nya disembunyikan
      /// seluruhnya kalau tidak ada - card kosong terbaca sebagai daftar yang gagal dimuat.
      /// </summary>
      public bool HasStaticItems => StaticItems.Count > 0;

      // Layar yang sedang dibuka, dititipkan halaman pemilik setiap kali panel ini akan tampil.
      // Saklar tampil milik panel dimiliki navigasinya, sama seperti saklar bilah atas.
      private Navigation? _navigation;

      /// <summary>Logo aplikasi, digambar di kepala panel.</summary>
      public ImageSource? LogoImage => EmApp is { } app ? BrandingImages.LoadLogo(app.Branding) : null;

      /// <summary>Nama lengkap pemilik akun yang sedang masuk.</summary>
      public string FullName => EmApp?.ActiveUser?.cContactFullName ?? "Not signed in";

      /// <summary>
      /// Inisial pemilik akun, isi lingkaran avatar. Nama lengkap didahulukan, dan nama akun jadi
      /// cadangan kalau kontaknya belum punya nama.
      /// </summary>
      public string Initials => EmApp?.ActiveUser is { } user
         ? (user.cContactFullName is { Length: > 0 } fullName ? fullName : user.cUserAccount).ToInitials()
         : "?";

      /// <summary>Alamat surel akun yang sedang masuk.</summary>
      public string EmailAddress => EmApp?.ActiveUser?.cCommValue ?? string.Empty;

      /// <summary>
      /// Apakah daftar pilihan server ikut digambar. Hanya di mode debug: di luar itu servernya tepat
      /// satu - yang diketik sendiri di layar login - jadi barisnya cuma akan mengulang keterangan di
      /// atasnya, dan radio yang tidak punya pilihan lain menjanjikan sesuatu yang tidak ada.
      /// </summary>
      public bool HasConnectionChoice => EmApp?.IsDebugMode ?? false;

      /// <summary>Keterangan server yang sedang dipakai, kembar dengan chip serupa di layar login.</summary>
      public string ConnectionStatus => EmApp?.ActiveConnection is { } connection
         ? $"Connected to {connection.Host}"
         : "No server selected";

      /// <summary>Apakah baris ganti tema ikut digambar di panel ini.</summary>
      public bool IsThemeVisible => _navigation?.IsColorThemeVisible ?? false;

      /// <summary>Teks baris ganti tema, menyebut tema yang akan dituju - bukan yang sedang aktif.</summary>
      public string ThemeCaption => EmApp?.IsLightTheme == true ? "Dark theme" : "Light theme";

      /// <summary>Ikon baris ganti tema, sepasang dengan <see cref="ThemeCaption"/>.</summary>
      public string ThemeGlyph => EmApp?.IsLightTheme == true ? FontIcons.Moon : FontIcons.Sun;

      /// <summary>Apakah baris keluar perlu digambar sama sekali.</summary>
      public bool CanSignOut => EmApp?.ActiveUser is not null;

      /// <summary>
      /// Apakah baris keluar boleh ditekan. Di mode debug jawabannya tidak: penggunanya diangkat
      /// sendiri oleh aplikasi saat dibangun dan tidak punya sesi di server untuk diakhiri, jadi
      /// keluar di sana hanya akan meninggalkan aplikasi tanpa siapa-siapa dan tanpa jalan masuk
      /// kembali. Yang tersedia di debug adalah Simulate Login, bukan keluar.
      /// </summary>
      public bool IsSignOutEnabled => EmApp is { IsDebugMode: false, ActiveUser: not null };

      /// <summary>
      /// Membangun ulang seluruh isi panel. Dipanggil tepat sebelum panel masuk ke layar, bukan sekali
      /// saat panel dibuat.
      /// </summary>
      /// <param name="navigation">Layar yang sedang dibuka saat panel ini dipanggil.</param>
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

      /// <summary>Menggambar ulang baris ganti tema sesudah temanya berganti.</summary>
      public void RefreshTheme() {
         NotifyChanged(nameof(ThemeCaption));
         NotifyChanged(nameof(ThemeGlyph));
      }

      // Daftar navigasi statis mengikuti pola yang sama dengan client desktop, tapi isinya milik panel
      // ini sendiri. Untuk sekarang hanya Simulate Login, dan itu pun hanya di mode debug: belum ada
      // satu pun layar MAUI yang perlu dibatasi hak.
      private void RebuildStaticItems(EmApp app) {
         StaticItems.Clear();

         // Akun debugger tidak punya baris tersimpan di mana pun - id-nya sengaja bukan id yang sah -
         // jadi tidak ada sandi miliknya yang bisa diganti. Barisnya tetap digambar supaya panelnya
         // tidak berubah bentuk tergantung siapa yang masuk.
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
      /// Menjawab apakah pengguna yang sedang masuk boleh melihat sebuah baris statis. Belum ada yang
      /// memanggilnya: MAUI belum mendaftarkan satu pun navigasi yang perlu dibatasi hak. Dibangun
      /// sekarang supaya baris ber-claim tinggal dipasang saat layarnya ada, dan supaya pembatasannya
      /// tidak dikarang ulang dengan cara lain di tempat lain.
      /// </summary>
      /// <param name="moduleName">Nama module pemilik claim.</param>
      /// <param name="claimName">Nama claim di dalam module itu.</param>
      private bool HasClaim(string moduleName, string claimName) =>
         EmApp is { } app && new ClaimCollection(moduleName, app.AllClaims, app.ActiveUser)[claimName];

      /// <summary>
      /// Memilih sebuah profil server. Barisnya dinyalakan dan baris lain dipadamkan di sini, lalu
      /// profilnya langsung dipasang sebagai koneksi aktif - tidak ada tombol "terapkan" yang perlu
      /// ditekan sesudahnya. Keterangan di atas daftar ikut berganti, karena ia membaca koneksi aktif
      /// yang sama.
      /// </summary>
      public void SelectConnectionCommand(object? parameter) {
         if (parameter is not AccountPanelConnectionVm selected || EmApp is not { } app) return;

         foreach (var item in Connections) item.IsChecked = item == selected;

         // Dijaga terhadap pilihan yang tidak mengubah apa-apa: memasang koneksi aktif yang sama
         // sekali lagi tetap memicu penghitungan ulang seluruh katalog claim.
         if (app.ActiveConnection == selected.Connection) return;

         app.ActiveConnection = selected.Connection;
         NotifyChanged(nameof(ConnectionStatus));
      }

      public void CloseCommand() => CloseRequested?.Invoke(this, EventArgs.Empty);

      /// <summary>
      /// Menjalankan sebuah baris navigasi statis. Panel ditutup lebih dulu supaya perpindahan layarnya
      /// terlihat, bukan terjadi di balik panel yang masih menutupi layar.
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
