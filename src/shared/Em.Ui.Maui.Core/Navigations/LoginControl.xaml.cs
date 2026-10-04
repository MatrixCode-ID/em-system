using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// Layar login: mengetik alamat server, akun, dan kata sandi, lalu membuka sesi. Layar inilah yang
   /// dipasang aplikasi saat dibuka selama tidak ada sesi yang bisa dipulihkan, dan setiap kali sebuah
   /// sesi berakhir.
   /// </summary>
   public partial class LoginControl : ContentView, INavigationBody
   {
      public LoginControl() {
         InitializeComponent();
         Vm.SignInSucceeded += OnSignInSucceeded;
      }

      /// <summary>View model layar ini, dibaca balik dari BindingContext yang dipasang di XAML.</summary>
      public LoginControlVm Vm => (LoginControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.SignInSucceeded -= OnSignInSucceeded;
         return Task.CompletedTask;
      }

      // Masuknya berhasil, jadi layar ini sudah tidak punya urusan lagi: NavigateHome membersihkan
      // sekaligus melepas seluruh jalur navigasi, dan layar login termasuk yang dilepas di situ.
      private void OnSignInSucceeded() {
         if (Vm.EmApp is not { } app) return;
         Dispatcher.Dispatch(async () => {
            await app.MainStack.NavigateHome();
            await app.MainStack.Home!.Reload();
         });
      }
   }

   /// <summary>View model <see cref="LoginControl"/>.</summary>
   public class LoginControlVm : MvvmModelBase
   {
      /// <summary>
      /// Satu-satunya jawaban untuk setiap bentuk pasangan akun/sandi yang salah. Tidak menyebut
      /// bagian mana yang keliru - justru itu yang tidak boleh diberitahukan layar login.
      /// </summary>
      public const string InvalidCredentialsMessage = "The account or password is not correct.";

      // Umur permintaan untuk profil yang dibuat dari alamat yang diketik di sini. Angkanya mengikuti
      // nilai bawaan profil baru di client desktop, supaya server yang sama tidak berperilaku berbeda
      // hanya karena dibuka dari perangkat yang berbeda.
      private const int DefaultTimeoutSeconds = 30;

      public LoginControlVm() {
         RegisterCommand(nameof(SignInCommand), SignInCommand, SignInCommandAllowed);
         RegisterCommand(nameof(ToggleThemeCommand), ToggleThemeCommand);
      }

      /// <summary>Dipicu sesudah sesi benar-benar terbuka, supaya layar ini bisa ditinggalkan.</summary>
      public event Action? SignInSucceeded;

      /// <summary>Logo aplikasi yang berlaku.</summary>
      public ImageSource? LogoImage => EmApp is { } app ? BrandingImages.LoadLogo(app.Branding) : null;

      /// <summary>Judul brand aplikasi.</summary>
      public string BrandTitle => EmApp?.Branding.DisplayTitle ?? string.Empty;

      /// <summary>Sub-judul brand aplikasi.</summary>
      public string BrandTagline => EmApp?.Branding.DisplayTagline ?? string.Empty;

      /// <summary>Teks hak cipta brand aplikasi.</summary>
      public string BrandCopyright => EmApp?.Branding.DisplayCopyright ?? string.Empty;

      /// <summary>Alamat server API yang dipakai masuk.</summary>
      public string ServerUrl {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>
      /// Apakah alamat server tidak boleh diketik. Di mode debug jawabannya selalu ya: daftar
      /// servernya ditentukan saat compile, dan yang berlaku adalah yang sedang terpilih di panel
      /// account - termasuk saat layar ini dibuka lewat Simulate Login. Di luar debug alamat inilah
      /// satu-satunya cara menyebut server, jadi ia harus bisa diketik.
      /// </summary>
      public bool IsServerLocked => EmApp?.IsDebugMode ?? false;

      /// <summary>Nama akun yang diketik.</summary>
      public string UserName {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>Kata sandi yang diketik. Tidak pernah ikut tersimpan ke mana pun.</summary>
      public string Password {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SignInError = null;
            RaiseSignInCommandChanged();
         });
      }

      /// <summary>
      /// Keterangan kenapa sesi sebelumnya berakhir, atau <c>null</c> kalau user sendiri yang keluar.
      /// Bukan pesan kesalahan - karena itu tampilannya berbeda dari <see cref="SignInError"/>.
      /// </summary>
      public string? SessionEndedNotice {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSessionEndedNotice)));
      }

      /// <summary><c>true</c> kalau ada keterangan berakhirnya sesi yang perlu ditampilkan.</summary>
      public bool HasSessionEndedNotice => !string.IsNullOrWhiteSpace(SessionEndedNotice);

      /// <summary>Pesan kegagalan masuk yang terakhir, atau <c>null</c> kalau tidak ada.</summary>
      public string? SignInError {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSignInError)));
      }

      /// <summary><c>true</c> kalau ada pesan kegagalan yang perlu ditampilkan.</summary>
      public bool HasSignInError => !string.IsNullOrWhiteSpace(SignInError);

      /// <summary>
      /// Menyiapkan ulang isian layar: alamat server yang berlaku dan nama akun terakhir yang dipakai
      /// masuk.
      /// </summary>
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;

         app.RetrieveApiConnections();

         // "Tetap masuk" bukan lagi pilihan yang ditawarkan: satu-satunya jalan keluar adalah tombol
         // keluar di panel account, jadi saklarnya selalu dinyalakan di sini. Saklarnya sendiri tetap
         // ada dan tetap ditulis - jalur pemulihan sesi membacanya - hanya tidak ada lagi yang bisa
         // mematikannya dari layar.
         app.RememberSignIn = true;
         UserName = app.RememberedUserName ?? string.Empty;

         // Di debug alamatnya mengikuti koneksi yang sedang terpilih. Di luar debug yang diisikan
         // kembali adalah alamat yang terakhir benar-benar berhasil dipakai masuk - satu-satunya
         // profil yang tersimpan.
         ServerUrl = app.ActiveConnection?.Host
            ?? app.UIConnections.FirstOrDefault()?.Host
            ?? string.Empty;

         NotifyChanged(nameof(IsServerLocked));
         NotifyChanged(nameof(LogoImage));
         NotifyChanged(nameof(BrandTitle));
         NotifyChanged(nameof(BrandTagline));
         NotifyChanged(nameof(BrandCopyright));
         return Task.CompletedTask;
      }

      /// <summary>
      /// Memeriksa pasangan akun/sandi ke server lalu membuka sesinya. Kalau berhasil, memicu
      /// <see cref="SignInSucceeded"/>; kalau tidak, <see cref="SignInError"/> yang terisi dan layar
      /// tetap di tempat - command ini tidak pernah melempar exception ke pemanggilnya.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is nothing
      // above it to catch it - the process ends.
      public async Task SignInCommand() {
         SignInError = null;

         try {
            WaiterText = "Signing in...";
            IsBusy = InWaiting = true;
            RaiseSignInCommandChanged();

            var app = EmApp!;
            var connection = ResolveConnection(app);

            // The password is checked on the server and nowhere else.
            var services = app.ServiceProvider.GetRequiredService<ICredentialServices>();
            var token = await services.PostGetMeta_SignIn(UserName, Password);

            // Everything from here on runs on a password that was already accepted. Should it fail -
            // loading the account behind the token, say - the second catch below is the right one:
            // what went wrong is not the pair that was typed.
            await app.BeginSessionAsync(token, remember: true);

            // Alamatnya baru terbukti bisa dipakai masuk di baris-baris di atas, dan barulah sekarang
            // ia layak disimpan. Koneksi debug tidak pernah ikut - ia milik kode, bukan penyimpanan.
            if (!app.IsDebugMode) StoreSingleProfile(app, connection);

            // Nama akun dan nama profilnya sama-sama disimpan: sesi tersimpan dititipkan per nama
            // profil, jadi tanpa nama itu tidak ada yang bisa dipulihkan saat aplikasi dibuka lagi.
            app.RememberedUserName = UserName;
            app.RememberedProfileName = connection.ProfileName;

            SignInSucceeded?.Invoke();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            // The one answer the server gives for every way the pair can be wrong. Nothing more is
            // shown and nothing more is kept: which half was wrong is exactly what a login screen
            // must not tell whoever is typing.
            FailSignIn(InvalidCredentialsMessage);
         }
         catch (Exception) {
            // Everything left is the server, the network, or a misconfigured client - none of it the
            // user's doing, so it is worth saying plainly.
            FailSignIn("Cannot sign in right now. The server could not be reached.");
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSignInCommandChanged();
         }
      }

      // Koneksi yang kredensialnya akan diperiksa. Di debug ia sudah terpasang dan alamatnya terkunci,
      // jadi dipakai apa adanya. Di luar debug alamat yang diketik user-lah yang menentukan: profilnya
      // dibentuk di sini dan langsung jadi koneksi aktif, karena tanpa koneksi aktif tidak ada server
      // yang bisa ditanyai sama sekali.
      private ApiConnection ResolveConnection(EmApp app) {
         if (app.IsDebugMode) {
            return app.ActiveConnection
               ?? throw new InvalidOperationException(
                  "Debug mode has no active API connection to sign in on.");
         }

         var host = ServerUrl.Trim();

         // Profil yang tersimpan dipakai ulang kalau alamatnya memang sama, supaya sesi yang sudah
         // tertitip padanya tidak terbuang hanya karena objeknya dibuat ulang.
         var connection = app.UIConnections.FirstOrDefault(r =>
            !r.IsDebugConnection && string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase))
            ?? new ApiConnection {
               ProfileName = DeriveProfileName(host),
               Host = host,
               Timeout = DefaultTimeoutSeconds,
               IgnoreSslErrors = false
            };

         app.ActiveConnection = connection;
         return connection;
      }

      // Yang tersimpan tidak pernah lebih dari satu. Profil lain dibuang lebih dulu - berikut sesi
      // yang tertitip padanya, karena sesi milik server lain tidak ada gunanya lagi - jadi daftar
      // server tidak pernah menumpuk, dan server yang baru menimpa yang lama.
      private static void StoreSingleProfile(EmApp app, ApiConnection connection) {
         app.UIConnections
            .Where(r => !r.IsDebugConnection)
            .Where(r => !string.Equals(r.ProfileName, connection.ProfileName, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .EachOf(app.DeleteApiConnection);

         app.AddApiConnection(connection);
      }

      // Nama profil diturunkan dari alamatnya, bukan diketik user: ia cuma nama di penyimpanan, dan
      // satu-satunya yang perlu dijamin adalah alamat yang sama tidak pernah melahirkan dua profil.
      // Alamat yang tidak berbentuk URL dipakai apa adanya - menolaknya di sini hanya akan menghalangi
      // masuk ke server yang sebenarnya bisa dihubungi.
      private static string DeriveProfileName(string host) =>
         Uri.TryCreate(host, UriKind.Absolute, out var uri)
            ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
            : host;

      /// <summary>Mengganti tema aplikasi ke tema yang sedang tidak dipakai.</summary>
      public void ToggleThemeCommand() {
         if (EmApp is not { } app) return;
         app.CurrentTheme = app.IsLightTheme ? ThemeVariant.Dark : ThemeVariant.Light;
      }

      // Emptying the password counts as the user editing the field, and editing either field is what
      // clears the last message - so the message has to be set after the field, never before.
      private void FailSignIn(string message) {
         Password = string.Empty;
         SignInError = message;
      }

      /// <summary>
      /// Sign in baru boleh dijalankan setelah alamat server terisi: server tempat kredensialnya
      /// diperiksa adalah alamat itu sendiri, jadi tanpa alamat tidak ada yang bisa dihubungi.
      /// </summary>
      public bool SignInCommandAllowed() =>
         IsNotBusy
         && !string.IsNullOrWhiteSpace(ServerUrl)
         && !string.IsNullOrWhiteSpace(UserName)
         && !string.IsNullOrWhiteSpace(Password);

      private void RaiseSignInCommandChanged() =>
         Commands[nameof(SignInCommand)]?.RaiseCanExecuteChanged();
   }
}
