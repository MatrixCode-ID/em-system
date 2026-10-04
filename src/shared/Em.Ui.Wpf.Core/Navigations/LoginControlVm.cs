using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// Hasil pemeriksaan sambungan ke server yang dipilih di layar login. Menentukan warna indikator
   /// pada status bar layar login, bukan sesuatu yang disimpan ke database — jadi urutannya sekadar
   /// alur yang dilewati sebuah probe.
   /// </summary>
   public enum ServerProbeStatus
   {
      /// <summary>Belum ada koneksi yang dipilih, jadi tidak ada yang diperiksa.</summary>
      NotSelected,

      /// <summary>Probe sedang berjalan.</summary>
      Probing,

      /// <summary>Server menjawab probe dan tanda tangannya terverifikasi.</summary>
      Connected,

      /// <summary>Server tidak menjawab, atau jawabannya tidak lolos verifikasi.</summary>
      Unreachable
   }

   /// <summary>
   /// ViewModel untuk <see cref="ILoginScreen"/>: proses sign in dan pemilihan tema terang/gelap.
   /// </summary>
   public class LoginControlVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel layar login dan mendaftarkan command-nya (sign in dan ganti tema).
      /// </summary>
      public LoginControlVm() {
         RegisterCommand(nameof(SignInCommand), SignInCommand, SignInCommandAllowed);
         RegisterCommand<ThemeVariant>(nameof(ChangeThemeCommand), ChangeThemeCommand);
         RegisterCommand(nameof(ConnectionConfigCommand), ConnectionConfigCommand);
      }

      /// <summary>
      /// Dipicu setelah sign in berhasil. Dipakai aplikasi (<see cref="Core.EmApp"/>) untuk
      /// berpindah dari layar login ke workspace.
      /// </summary>
      public event Action? SignInSucceeded;

      /// <summary>
      /// Nama akun yang diketik user. Terisi sendiri saat layar dibuka kalau sebelumnya user memilih
      /// diingat (<see cref="RememberMe"/>).
      /// </summary>
      // Empty rather than null when nothing has been typed: a text field always has a value, and
      // every reader of this property - the sign in call included - would otherwise have to guard
      // against a null that only ever means "still empty".
      public string UserName {
         get => Get<string>() ?? string.Empty;
         set => Set(value, OnCredentialFieldChanged);
      }

      /// <summary>
      /// Password yang diketik user. Hidupnya hanya selama layar login terbuka: tidak pernah disimpan
      /// ke Registry maupun ke mana pun, dan tidak ikut diingat oleh <see cref="RememberMe"/>.
      /// </summary>
      public string Password {
         get => Get<string>() ?? string.Empty;
         set => Set(value, OnPasswordChanged);
      }

      /// <summary>
      /// Dipicu setiap kali <see cref="Password"/> berubah, supaya view bisa menyamakan kotak
      /// password-nya. Kotak password tidak bisa di-binding, jadi nilai yang diubah ViewModel sendiri -
      /// mis. dikosongkan sesudah sign in gagal - hanya sampai ke layar lewat event ini.
      /// </summary>
      public event Action? PasswordBoxSyncRequested;

      private void OnPasswordChanged(string value) {
         OnCredentialFieldChanged(value);
         PasswordBoxSyncRequested?.Invoke();
      }

      /// <summary>
      /// Kenapa sign in terakhir gagal, atau <c>null</c> kalau tidak ada yang perlu dilaporkan. Strip
      /// merah di atas form membaca properti ini: terisi berarti muncul, <c>null</c> berarti hilang.
      /// </summary>
      public string? SignInError {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// Keterangan kenapa sesi sebelumnya berakhir, atau <c>null</c> kalau tidak ada yang perlu
      /// dikatakan. Dibedakan dari <see cref="SignInError"/> yang berwarna merah: sesi yang habis
      /// umurnya bukan kegagalan user, jadi kalimatnya muncul sebagai keterangan biasa.
      /// </summary>
      public string? SessionEndedNotice {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>
      /// Keterangan teknis dari kegagalan terakhir — pesan exception aslinya — untuk tooltip strip
      /// error. Selalu <c>null</c> untuk kegagalan kredensial: di situ memang tidak ada detail yang
      /// boleh diceritakan.
      /// </summary>
      public string? SignInErrorDetail {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// Pilihan "keep me signed in" milik user. Nilainya langsung tersimpan ke Registry begitu
      /// diubah (<see cref="Core.EmApp.RememberSignIn"/>), sedangkan nama akunnya baru diingat
      /// setelah sign in benar-benar dijalankan.
      /// </summary>
      public bool RememberMe {
         get => Get<bool>();
         set => Set(value, OnRememberMeChanged);
      }

      // One message for every way the pair can be wrong: no such account, an account that is not
      // allowed in, a credential that was never enrolled or has been revoked, and a password that
      // simply does not match. The server answers all four with the same 401 for the same reason
      // this screen shows one message: telling them apart would turn either one into a way of
      // finding out which accounts exist.
      private const string InvalidCredentialsMessage = "Incorrect username or password. Please try again.";

      /// <summary>
      /// Menjalankan sign in: menyerahkan nama akun dan password ke server, yang memeriksanya dan
      /// menerbitkan token kalau cocok. Kalau lolos, host diberi tahu lewat
      /// <see cref="SignInSucceeded"/>; kalau tidak, <see cref="SignInError"/> yang terisi dan layar
      /// tetap di tempat — command ini tidak pernah melempar exception ke pemanggilnya.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is no
      // DispatcherUnhandledException handler in this application to catch it - the process ends.
      public async Task SignInCommand() {
         SignInError = null;
         SignInErrorDetail = null;

         try {
            WaiterText = "Signing in...";
            IsBusy = InWaiting = true;
            RaiseSignInCommandChanged();

            // The password is checked on the server and nowhere else. What used to happen here -
            // pulling the stored credential down and comparing the hash locally - meant the hash of
            // every account was there for the asking.
            var services = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();

            var token = await services.PostGetMeta_SignIn(UserName, Password);

            // Everything from here on runs on a password that was already accepted. Should it fail -
            // loading the account behind the token, say - the second catch below is the right one:
            // what went wrong is not the pair that was typed.
            await EmApp!.BeginSessionAsync(token, RememberMe);

            // The switch itself is saved the moment it is flipped; the name and the profile are only
            // worth keeping once they have actually been used to sign in. The profile is kept as well
            // as the name because a stored session lives under its own connection: without knowing
            // which one, there is nothing to restore at the next start.
            EmApp!.RememberedUserName = RememberMe ? UserName : null;
            EmApp!.RememberedProfileName = RememberMe ? SelectedConnection!.ProfileName : null;

            SignInSucceeded?.Invoke();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            // The one answer the server gives for every way the pair can be wrong. Nothing more is
            // shown and nothing more is kept: which half was wrong is exactly what a login screen
            // must not tell whoever is typing.
            FailSignIn(InvalidCredentialsMessage);
         }
         catch (Exception x) {
            // Everything left is the server, the network, or a misconfigured client - none of it the
            // user's doing, so it is worth saying plainly, and worth keeping the detail for.
            FailSignIn("Cannot sign in right now. The server could not be reached.", x.Message);
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSignInCommandChanged();
         }
      }

      // Emptying the password counts as the user editing the field, and editing either field is what
      // clears the last message - so the message has to be set after the field, never before.
      private void FailSignIn(string message, string? detail = null) {
         Password = string.Empty;
         SignInError = message;
         SignInErrorDetail = detail;
      }

      /// <summary>
      /// Sign in baru boleh dijalankan setelah user memilih koneksi: server tempat kredensialnya
      /// diperiksa adalah koneksi itu sendiri, jadi tanpa pilihan tidak ada yang bisa dihubungi.
      /// Berlaku juga di debug mode — di sana layar ini yang menyiapkan koneksi untuk simulasi login.
      /// </summary>
      // Unlike ChangeThemeCommand, a predicate is safe here: it reads this screen's own state, not
      // EmApp, and every property it reads re-raises CanExecuteChanged itself - which UiCommandBase
      // needs, not being tied to CommandManager.RequerySuggested.
      public bool SignInCommandAllowed() =>
         IsNotBusy
         && SelectedConnection is not null
         && !string.IsNullOrWhiteSpace(UserName)
         && !string.IsNullOrWhiteSpace(Password);

      private void RaiseSignInCommandChanged() =>
         Commands[nameof(SignInCommand)]?.RaiseCanExecuteChanged();

      /// <summary>
      /// Menyalakan atau mematikan keadaan "sedang memulihkan sesi tersimpan". Layar login digambar
      /// lebih dulu dalam keadaan sibuk, bukan ditahan sebagai window kosong, karena penukaran token
      /// yang dilakukannya memakan waktu jaringan.
      /// </summary>
      /// <param name="restoring"><c>true</c> selama pemulihan berjalan.</param>
      public void SetRestoringSession(bool restoring) {
         WaiterText = "Restoring session...";
         IsBusy = InWaiting = restoring;
         RaiseSignInCommandChanged();
      }

      private void OnCredentialFieldChanged(string value) {
         // Typing is the user's answer to whatever the last attempt said, so the message goes as soon
         // as either field is touched - including the moment a failed attempt empties the password.
         SignInError = null;
         SignInErrorDetail = null;

         // The notice about the previous session goes with it: the moment the user starts typing,
         // they have read it.
         SessionEndedNotice = null;
         RaiseSignInCommandChanged();
      }

      /// <summary>
      /// Mengganti mode tema aplikasi ke <paramref name="theme"/>. Layar login perlu punya pilihan tema
      /// sendiri karena toolbar window utama — berikut submenu Color Mode-nya — ikut tersembunyi selama
      /// layar ini ditampilkan.
      /// </summary>
      /// <param name="theme">Mode tema yang akan diterapkan.</param>
      public void ChangeThemeCommand(ThemeVariant theme) {
         // Guarded instead of gated behind a can-execute predicate on purpose. XAML builds this VM
         // before the host injects EmApp, and UiCommandBase.CanExecuteChanged is a plain event -
         // it is not tied to CommandManager.RequerySuggested. A predicate would therefore be
         // evaluated once, while EmApp is still null, and leave the buttons disabled for good.
         if (EmApp is null) return;

         EmApp.CurrentTheme = theme;
      }

      /// <summary>
      /// <c>true</c> jika mode tema aktif saat ini adalah mode terang.
      /// </summary>
      public bool LightModeSelected =>
         (EmApp?.CurrentTheme ?? ThemeVariant.Dark) == ThemeVariant.Light;

      /// <summary>
      /// <c>true</c> jika mode tema aktif saat ini adalah mode gelap.
      /// </summary>
      public bool DarkModeSelected =>
         (EmApp?.CurrentTheme ?? ThemeVariant.Dark) == ThemeVariant.Dark;

      /// <summary>
      /// Memberi tahu UI untuk mengevaluasi ulang <see cref="LightModeSelected"/> dan
      /// <see cref="DarkModeSelected"/>. Dipanggil setiap kali tema aplikasi berganti, dari mana pun
      /// perubahannya berasal.
      /// </summary>
      public void RefreshThemeState() {
         NotifyChanged(nameof(LightModeSelected));
         NotifyChanged(nameof(DarkModeSelected));
      }

      #region API Connections

      /// <summary>
      /// Daftar profil koneksi API yang ditawarkan layar login. Ini koleksi milik
      /// <see cref="Core.EmApp.UIConnections"/> apa adanya — bukan salinannya — jadi profil yang
      /// ditambah atau dihapus lewat dialog Connection Config langsung ikut terlihat di sini.
      /// Null-safe karena XAML membuat ViewModel ini sebelum <see cref="MvvmModelBase.EmApp"/>
      /// sempat di-set.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// Profil yang sedang dipilih user di layar login. Menyetelnya sekaligus menjadikannya koneksi
      /// aktif aplikasi (<see cref="Core.EmApp.ActiveConnection"/>), jadi server yang dipakai
      /// sesudah sign in adalah yang dipilih di sini.
      /// </summary>
      public ApiConnection? SelectedConnection {
         get => Get<ApiConnection?>();
         set => Set(value, OnSelectedConnectionChanged);
      }

      /// <summary>
      /// Hasil probe terakhir, dipakai status bar di bawah layar login untuk memilih warna indikatornya.
      /// </summary>
      public ServerProbeStatus ProbeStatus {
         get => Get<ServerProbeStatus>();
         private set => Set(value);
      }

      /// <summary>
      /// Kalimat status yang ditampilkan status bar: koneksi mana yang sedang diperiksa, dan hasilnya.
      /// </summary>
      public string ServerStatusText {
         get => Get<string>() ?? "No server connection selected";
         private set => Set(value);
      }

      /// <summary>
      /// Keterangan panjang dari probe terakhir — biasanya pesan error aslinya — untuk tooltip status
      /// bar. <c>null</c> kalau tidak ada yang perlu dijelaskan.
      /// </summary>
      public string? ServerStatusDetail {
         get => Get<string?>();
         private set => Set(value);
      }

      /// <summary>
      /// Menyambungkan ViewModel ini ke aplikasi, lalu memberi tahu UI supaya binding daftar koneksi
      /// dievaluasi ulang dan pilihannya terisi. Notifikasinya wajib: XAML sudah membuat ViewModel ini
      /// berikut seluruh binding-nya sebelum <see cref="MvvmModelBase.EmApp"/> sempat di-set, jadi
      /// tanpa ini daftar koneksinya keburu terbaca kosong dan tidak pernah terisi.
      /// </summary>
      /// <param name="app">Objek aplikasi pemilik ViewModel ini.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         NotifyChanged(nameof(ApiConnections));
         SyncSelectedConnection();

         // Reading the switch back writes the very same value to the Registry through the property
         // below. That is one redundant write at start up, and it buys the screen a single path in
         // and out of the setting instead of a second one just for loading it.
         RememberMe = app.RememberSignIn;
         if (RememberMe) UserName = app.RememberedUserName ?? string.Empty;
      }

      private void OnRememberMeChanged(bool remember) {
         if (EmApp is null) return;

         EmApp.RememberSignIn = remember;

         // Switching it off forgets the name and the profile there and then, rather than at the next
         // sign in: the point of the switch is that nothing of the last user is left behind on the
         // machine. The stored session goes with them - without a profile to look under, nothing
         // would ever read it again anyway.
         if (!remember) {
            if (EmApp.RememberedProfileName is { Length: > 0 } profileName) {
               EmApp.ServiceProvider.GetRequiredService<ISessionStorage>().Clear(profileName);
            }

            EmApp.RememberedUserName = null;
            EmApp.RememberedProfileName = null;
         }
      }

      /// <summary>
      /// Menyamakan pilihan koneksi dengan isi <see cref="ApiConnections"/> yang terbaru. Urutan
      /// prioritasnya: profil yang tadi dipilih di layar ini, lalu koneksi yang sedang aktif di
      /// aplikasi (<see cref="Core.EmApp.ActiveConnection"/>) — supaya pilihan user tetap bertahan
      /// walau layar login dibuat ulang — lalu koneksi debug bawaan.
      /// <para>
      /// Kalau tidak ada satu pun yang cocok, pilihannya sengaja dibiarkan kosong dan user harus
      /// memilih sendiri: sign in memang tidak diizinkan sebelum ada koneksi
      /// (<see cref="SignInCommandAllowed"/>), jadi memilihkan profil pertama begitu saja cuma
      /// menyembunyikan keputusan yang seharusnya diambil user.
      /// </para>
      /// </summary>
      public void SyncSelectedConnection() {
         if (EmApp is null) return;

         SelectedConnection =
            FindConnection(_selectedProfileName)
            ?? FindConnection(EmApp.ActiveConnection?.ProfileName)
            ?? EmApp.DefaultDebugConnection;
      }

      /// <summary>
      /// Membuka dialog konfigurasi koneksi API (<see cref="Dialogs.ConnectionConfig"/>). Layar login
      /// perlu punya jalan masuk sendiri ke dialog ini karena toolbar window utama — berikut menu
      /// Tools-nya — ikut tersembunyi selama layar ini ditampilkan.
      /// </summary>
      public void ConnectionConfigCommand() {
         // Guarded the same way as ChangeThemeCommand: XAML builds this VM before the host injects
         // EmApp, and a can-execute predicate would be evaluated while it is still null.
         if (EmApp is null) return;

         var dlg = new Dialogs.ConnectionConfig(EmApp) {
            Owner = EmApp.MainWindow
         };
         dlg.ShowDialog();
      }

      // Looking the pick up by profile name rather than by reference: rebuilding the profile list
      // replaces every stored entry with a brand new ApiConnection object, so a reference held from
      // before the rebuild - ActiveConnection included - is no longer in the collection.
      private ApiConnection? FindConnection(string? profileName) =>
         profileName is null
            ? null
            : EmApp!.UIConnections.FirstOrDefault(r => r.ProfileName == profileName);

      // The profile the user last picked on this screen, remembered by name for the reason above.
      private string? _selectedProfileName;

      private void OnSelectedConnectionChanged(ApiConnection? connection) {
         // A rebuild empties the selection for a moment. The remembered name is deliberately left
         // untouched then, so SyncSelectedConnection can put the same profile back afterwards.
         if (connection is not null) _selectedProfileName = connection.ProfileName;

         if (EmApp is not null) EmApp.ActiveConnection = connection;

         // Sign in hangs off this pick, and nothing else re-asks whether it is allowed.
         RaiseSignInCommandChanged();

         // Deliberately not awaited, and deliberately not a registered command: UiCommandAsync
         // refuses to start while it is still running, which would drop exactly the probe the user
         // asked for by picking another profile mid-probe.
         _ = ProbeConnectionAsync(connection);
      }

      // Every probe carries the number it was started with. Only the newest one may report, so a
      // slow answer for a profile the user has already moved off cannot paint over the profile that
      // replaced it - nor can a failure arriving after the next probe already said "connected".
      private int _probeToken;

      private async Task ProbeConnectionAsync(ApiConnection? connection) {
         var token = ++_probeToken;

         if (connection is null) {
            ReportProbe(token, ServerProbeStatus.NotSelected, "No server connection selected");
            return;
         }

         ReportProbe(token, ServerProbeStatus.Probing, $"Probing {connection.Host}…");

         try {
            using var api = connection.CreateApiClient();
            await api.HandshakeAsync();
            ReportProbe(token, ServerProbeStatus.Connected, $"Connected — {connection.Host}");
         }
         catch (Exception x) {
            // The status line is the whole report: an unreachable server while the user is still
            // picking one is an answer, not an accident worth an error dialog. The message itself is
            // kept for the tooltip, where it explains the red dot without shouting.
            ReportProbe(token, ServerProbeStatus.Unreachable, $"Cannot reach {connection.Host}", x.Message);
         }
      }

      private void ReportProbe(int token, ServerProbeStatus status, string text, string? detail = null) {
         if (token != _probeToken) return;

         ProbeStatus = status;
         ServerStatusText = text;
         ServerStatusDetail = detail;
      }

      #endregion
   }
}
