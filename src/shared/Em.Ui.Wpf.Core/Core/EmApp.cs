using System.Collections.ObjectModel;
using System.Windows;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Application = System.Windows.Application;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp : IEmAppUi
   {
      private const ThemeVariant DefaultTheme = ThemeVariant.Dark;
      private Dictionary<ApiConnection, ApiClient> _apiClients { get; } = [];
      public event EventHandler? ActiveConnectionChanged;

      /// <summary>
      /// Dipicu setiap kali <see cref="ActiveUser"/> berganti. Dipakai UI yang menampilkan identitas
      /// pengguna yang sedang aktif (mis. tombol akun di toolbar navigasi) supaya bisa menggambar ulang
      /// dirinya - <see cref="EmApp"/> sendiri bukan sumber binding ber-notifikasi, jadi tanpa event
      /// ini tampilan akan tertinggal pada pengguna yang lama.
      /// </summary>
      public event EventHandler? ActiveUserChanged;

      private EmApp(string[] args) {
         Args = args;
         // Mode debug tidak punya momen login: penggunanya sudah diangkat sinkron di dalam BuildApp
         // (InitDebugMode), sebelum ada koneksi aktif untuk ditumpangi satu pun await. Momen yang
         // benar-benar tersedia adalah saat koneksi aktif terpasang - yaitu ketika kartu koneksi di
         // home memilihkan DefaultDebugConnection.
         ActiveConnectionChanged += (_, _) => {
            if (IsDebugMode && ActiveConnection is not null) {
               _claimsRefresh = RefreshClaimsAsync();
            }
         };
      }

      #region Properties

      public ApplicationLayout ApplicationLayout { get; private set; }

      // How long the single-page host takes to slide one screen out and the next one in; zero means
      // no animation at all, which is also what every multi-tab application gets.
      internal TimeSpan NavigationTransitionTime { get; private set; }

      internal bool EnableFieldAnimation { get; private set; }

      /// <summary>
      /// Pengaturan tampilan brand aplikasi (logo, teks-teks layar login, dan tema terang/gelap).
      /// Diisi dari <see cref="EmAppBuilder.ApplyBranding"/> saat <see cref="BuildApp"/>;
      /// kalau aplikasi tidak pernah memanggilnya, tetap berupa <see cref="BrandingInfo"/> kosong,
      /// jadi setiap anggotanya sudah otomatis jatuh ke nilai bawaan generik - tidak perlu null-check
      /// di sisi pemanggil.
      /// </summary>
      public BrandingInfo Branding { get; private set; } = null!;

      public bool IsDebugMode { get; private set; } = false;

      /// <summary>
      /// Aturan kata sandi yang berlaku di aplikasi ini, dibaca layar yang menerima kata sandi baru.
      /// Diisi dari <see cref="EmAppBuilder.UsePasswordPolicy"/> saat <see cref="BuildApp"/>; kalau
      /// aplikasi tidak pernah memanggilnya, berisi <see cref="PasswordPolicy"/> dengan nilai bawaan -
      /// jadi tidak pernah <c>null</c> dan pemakainya tidak perlu null-check.
      /// </summary>
      public PasswordPolicy PasswordPolicy { get; private set; } = null!;
      public ObservableCollection<ApiConnection> UIConnections { get; } = [];
      public ApiConnection[] DebugConnections { get; private set; } = [];
      public ApiConnection? DefaultDebugConnection { get; private set; }

      /// <summary>
      /// Nama aplikasi, dipakai sebagai judul window utama dan sebagai nama subkey Registry
      /// tempat pengaturan aplikasi (mis. koneksi API, tema) disimpan.
      /// </summary>
      public string ApplicationName { get; private set; } = null!;

      /// <summary>
      /// Argumen command-line yang diterima aplikasi saat startup.
      /// </summary>
      public string[] Args { get; init; }

      /// <summary>
      /// DI container aplikasi, hanya hidup selama <see cref="BuildApp"/>. Module mendaftarkan service
      /// lewat callback <see cref="EmAppBuilder"/>, bukan langsung ke property ini; setelah
      /// <see cref="ServiceProvider"/> dibangun, penambahan ke koleksi ini tidak lagi berpengaruh.
      /// </summary>
      internal IServiceCollection Services { get; } = new ServiceCollection();

      /// <summary>
      /// Instance <see cref="System.Windows.Application"/> WPF, tersedia setelah <see cref="Run"/> dipanggil.
      /// </summary>
      public Application? App { get; private set; }

      // Disimpan sebagai tipe konkret supaya bisa di-dispose saat aplikasi berhenti, sementara yang
      // dibuka ke pemanggil cukup IServiceProvider lewat ServiceProvider.
      private ServiceProvider _serviceProvider = null!;

      /// <inheritdoc />
      /// <remarks>
      /// Di sisi UI tidak ada scope per-request seperti di API, jadi yang dikembalikan selalu provider
      /// akar hasil build dari <see cref="Services"/> di akhir <see cref="BuildApp"/>.
      /// </remarks>
      public IServiceProvider ServiceProvider => _serviceProvider;

      public ApiConnection? ActiveConnection {
         get;
         set {
            field = value;
            ActiveConnectionChanged?.Invoke(this, EventArgs.Empty);
         }
      }

      /// <summary>
      /// Pengguna yang sedang masuk, atau <c>null</c> kalau belum ada. "Belum ada yang masuk" adalah
      /// keadaan normal yang datang dua kali - sebelum login dan sesudah sign out - bukan lagi
      /// keadaan sementara saat startup.
      /// </summary>
      public User? ActiveUser { get; private set; }

      // Claim milik layar bawaan client sendiri. Declared in code during BuildApp and frozen from
      // there on, so a claim can never appear - or disappear - while the application is running.
      private readonly List<ClaimAction> _internalClaims = [];

      // The catalogue as the active server declares it, replaced wholesale on every refresh: a
      // different server is a different set of modules, so what the previous one declared must not
      // survive the switch.
      private ClaimAction[] _serverClaims = [];

      // Both halves, merged once per refresh rather than on every read. AllClaims is asked inside
      // XxxCommandAllowed - a path WPF runs over and over - and merging there would allocate a new
      // list each time a button decides whether it is enabled.
      private ClaimAction[] _allClaims = [];

      private bool _internalClaimsSealed;

      /// <summary>
      /// Katalog seluruh claim yang dikenal aplikasi: claim milik layar bawaan client digabung dengan
      /// katalog milik server aktif (dimuat <see cref="RefreshClaimsAsync"/>). Kalau sebuah kunci ada
      /// di kedua sisi, yang dipakai adalah deklarasi client - ia yang tidak bisa berubah saat
      /// aplikasi berjalan. Bukan milik siapa-siapa: tidak dibersihkan saat sign out, dan dibaca lewat
      /// extension method <c>Claims()</c> pada <c>IServices</c> saat membentuk
      /// <see cref="ClaimCollection"/>. Namanya sengaja sama persis dengan <c>EmApp.AllClaims</c>
      /// milik server: dua tipe berbeda di dua assembly berbeda, satu arti, satu nama.
      /// </summary>
      public IReadOnlyList<ClaimAction> AllClaims => _allClaims;

      // Internal, and with no public counterpart at all: the client-side catalogue is the engine's
      // own, declared from InitInternalClaims while BuildApp runs. A module declares its claims on
      // the server, and they reach here through RefreshClaimsAsync like every other server claim.
      internal void AddInternalClaim(string key) {
         if (_internalClaimsSealed) {
            throw new InvalidOperationException(
               $"Claim '{key}' cannot be declared after the application has been built.");
         }

         var separatorIndex = key.IndexOf(ClaimAction.Separator);
         if (separatorIndex <= 0 || separatorIndex == key.Length - 1) {
            throw new ArgumentException(
               $"Claim key '{key}' must be written as 'module{ClaimAction.Separator}name'.", nameof(key));
         }

         var claim = ClaimAction.FromKey(key);
         if (_internalClaims.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"Claim '{claim.Key}' is already declared.");
         }

         _internalClaims.Add(claim);
      }

      // Closes the window InitInternalClaims declares through, at the end of BuildApp. Called once
      // whether or not a single claim was declared, because it is also what builds the first
      // catalogue - before any server has been asked, AllClaims is the internal half alone.
      internal void SealInternalClaims() {
         _internalClaimsSealed = true;
         RebuildClaimCatalog();
      }

      private void RebuildClaimCatalog() =>
         _allClaims = [
            .. _internalClaims,
            .. _serverClaims.Where(s =>
               !_internalClaims.Any(i => string.Equals(i.Key, s.Key, StringComparison.OrdinalIgnoreCase)))
         ];

      /// <summary>
      /// Apakah <paramref name="navigation"/> boleh dibuka pengguna yang sedang aktif - dipakai baik
      /// oleh menu home maupun oleh <see cref="NavigateTo(Navigation,object?)"/>, supaya yang
      /// disembunyikan dan yang ditolak tidak pernah berbeda. Mode debug melewati seluruh pengecekan.
      /// </summary>
      /// <param name="navigation">Navigasi yang hendak dibuka.</param>
      public bool CanOpen(Navigation navigation) => IsDebugMode || NavigationAccess.CanOpen(navigation, ActiveUser);

      // The application's built-in tools, in the order every surface lists them: the home screen of
      // the single-page layout and the Tools menu of the multi-tab window. A tool that is a
      // navigation the user may not open is left out, by the same rule the application menu follows.
      internal IReadOnlyList<StaticTool> GetStaticTools() {
         var tools = new List<StaticTool> {
            new() {
               Name = "tools.apisettings",
               Title = "Connection Config",
               Subtitle = "Configure API Server",
               Description = "Configure the API server connection used by this client.",
               Icon = EFontAwesomeIcon.Solid_Plug.CreateImageSource(System.Windows.Media.Brushes.Gray),
               Invoke = owner => {
                  new Dialogs.ConnectionConfig(this) { Owner = owner }.ShowDialog();
                  return Task.CompletedTask;
               }
            }
         };

         foreach (var name in (string[])["admin.users", "admin.roles", "admin.cdn", "admin.tasks", "admin.release", "admin.container", "admin.nupak"]) {
            var nav = Navigations.Single(r => r.Name == name);
            if (!CanOpen(nav)) continue;

            tools.Add(new StaticTool {
               Name = nav.Name,
               Title = nav.Title,
               Subtitle = nav.Subtitle,
               Description = nav.Description,
               Icon = nav.NavigationIcon,
               Navigation = nav
            });
         }

         if (Navigations.FirstOrDefault(n => n.Name == ApprovalManagerNavigationPayload.NavigationName) is { } approval &&
             Em.Ui.Wpf.Navigations.ApprovalManagerVm.CanOpenManager(this)) {
            tools.Add(new StaticTool { Name = "approval.manager", Title = approval.Title,
               Subtitle = approval.Subtitle, Description = approval.Description, Navigation = approval,
               Icon = EFontAwesomeIcon.Solid_Check.CreateImageSource(System.Windows.Media.Brushes.Gray) });
         }
         // Kept as a placeholder: its place in the list is settled, what it does is not yet.
         tools.Add(new StaticTool {
            Name = "tools.simulatelogin",
            Title = "Simulate Login",
            Subtitle = "Simulate Login",
            Description = "Go to Login Screen.",
            Icon = EFontAwesomeIcon.Solid_RightToBracket.CreateImageSource(System.Windows.Media.Brushes.Gray),
            Invoke = _ => Task.CompletedTask
         });

         return tools;
      }

      public ApiClient? GetActiveApiClient() {
         if (ActiveConnection == null)
            return null;

         if (!_apiClients.TryGetValue(ActiveConnection, out var result)) {
            result = ActiveConnection.CreateApiClient();
            _apiClients.Add(ActiveConnection, result);
         }

         // Disetel di sini, bukan sekali saat client dibuat: client hidup lebih lama daripada satu
         // pengguna, dan yang harus ikut di setiap request adalah pengguna yang aktif saat itu.
         // Keduanya diisi berbarengan - header identitas membawa keduanya, dan mengisi separuh berarti
         // mengirim header yang menyebut orang yang berbeda dari yang dimaksud.
         result.ActiveUserId = ActiveUser?.cUserId;
         result.ActiveUserAccount = ActiveUser?.cUserAccount;
         return result;
      }

      /// <summary>
      /// Window utama aplikasi, dibuat oleh <see cref="Run"/>. Kelas yang sama dipakai kedua layout:
      /// di layout multi-tab ia menampilkan tab-tab <see cref="MainStack"/>, di layout satu halaman ia
      /// menampilkan host navigasi <see cref="MainStack"/>. Menutupnya mengakhiri aplikasi.
      /// </summary>
      public TabbedMainWindow MainWindow { get; private set; } = null!;

      /// <summary>
      /// Registry key dasar aplikasi (<c>HKCU\{ApplicationName}</c>), dibuat otomatis jika belum ada.
      /// </summary>
      public RegistryKey BaseRegKey =>
         Registry.CurrentUser.OpenSubKey(ApplicationName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
         Registry.CurrentUser.CreateSubKey(ApplicationName);

      // Internal, bukan private: penyimpan sesi menumpang subkey yang sama, dan dua tempat yang
      // boleh mengetik namanya sendiri berarti satu salah ketik membuat sesi tersimpan di
      // cabang Registry yang tidak pernah dibaca siapa-siapa.
      internal const string ApiConnectionsSubKey = "Api Connections";

      /// <summary>
      /// Registry key tempat daftar koneksi API (<see cref="ApiConnection"/>) tersimpan, di bawah <see cref="BaseRegKey"/>.
      /// </summary>
      private RegistryKey ApiConnectionsRegKey =>
         BaseRegKey.OpenSubKey(ApiConnectionsSubKey, writable: true) ??
         BaseRegKey.CreateSubKey(ApiConnectionsSubKey);

      /// <summary>
      /// Apakah user mencentang "keep me signed in" di layar login (tersimpan di Registry, jadi
      /// terbawa antar sesi dan hanya berlaku untuk user Windows yang sedang login).
      /// <para>
      /// Yang disimpan cuma pilihannya. Kredensial tidak pernah ikut ditulis ke Registry —
      /// satu-satunya yang diingat selain flag ini adalah <see cref="RememberedUserName"/>.
      /// </para>
      /// </summary>
      public bool RememberSignIn {
         get {
            using var key = BaseRegKey;
            return (int)(key.GetValue(nameof(RememberSignIn)) ?? 0) != 0;
         }
         set {
            using var key = BaseRegKey;
            key.SetValue(nameof(RememberSignIn), value ? 1 : 0, RegistryValueKind.DWord);
         }
      }

      /// <summary>
      /// Nama akun terakhir yang dipakai sign in, supaya layar login bisa mengisikannya kembali selama
      /// <see cref="RememberSignIn"/> menyala. Diisi <c>null</c> (atau teks kosong) untuk melupakannya —
      /// nilainya langsung dibuang dari Registry, bukan disimpan sebagai string kosong.
      /// </summary>
      public string? RememberedUserName {
         get {
            using var key = BaseRegKey;
            return key.GetValue(nameof(RememberedUserName)) as string;
         }
         set {
            using var key = BaseRegKey;
            if (string.IsNullOrWhiteSpace(value)) {
               key.DeleteValue(nameof(RememberedUserName), throwOnMissingValue: false);
               return;
            }

            key.SetValue(nameof(RememberedUserName), value, RegistryValueKind.String);
         }
      }

      /// <summary>
      /// Nama profil koneksi terakhir yang dipakai sign in, sepasang dengan
      /// <see cref="RememberedUserName"/>. Diperlukan karena sesi tersimpan tinggal di subkey profilnya
      /// sendiri: tanpa tahu profil mana, tidak ada yang bisa dipulihkan saat aplikasi dibuka lagi.
      /// Diisi <c>null</c> (atau teks kosong) untuk melupakannya.
      /// </summary>
      public string? RememberedProfileName {
         get {
            using var key = BaseRegKey;
            return key.GetValue(nameof(RememberedProfileName)) as string;
         }
         set {
            using var key = BaseRegKey;
            if (string.IsNullOrWhiteSpace(value)) {
               key.DeleteValue(nameof(RememberedProfileName), throwOnMissingValue: false);
               return;
            }

            key.SetValue(nameof(RememberedProfileName), value, RegistryValueKind.String);
         }
      }

      /// <summary>
      /// Mode tema yang sedang aktif, terang atau gelap (tersimpan di Registry, jadi terbawa antar
      /// sesi). Meng-set nilai ini langsung menerapkan tema baru ke seluruh window dan memicu
      /// <see cref="ThemeChanged"/>. Default: <see cref="ThemeVariant.Dark"/>.
      /// </summary>
      public ThemeVariant CurrentTheme {
         get {
            using var key = BaseRegKey;
            return ParseThemeVariant(key.GetValue(nameof(CurrentTheme)) as string);
         }
         set {
            if (CurrentTheme == value)
               return;

            using (var key = BaseRegKey) {
               key.SetValue(nameof(CurrentTheme), value.ToString(), RegistryValueKind.String);
            }

            ApplyTheme();
         }
      }

      /// <summary>
      /// Tema yang sedang dipakai: <see cref="BrandingInfo.LightTheme"/> atau
      /// <see cref="BrandingInfo.DarkTheme"/> milik <see cref="Branding"/>, sesuai
      /// <see cref="CurrentTheme"/>.
      /// </summary>
      public ThemeBase ActiveTheme => Branding.GetTheme(CurrentTheme);

      /// <summary>
      /// Dipicu setiap kali tema selesai diterapkan ulang karena <see cref="CurrentTheme"/> berganti.
      /// Dipakai layar yang menggambar sebagian warnanya dari kode, bukan dari resource tema, supaya
      /// bisa ikut menggambar ulang dirinya.
      /// </summary>
      public event EventHandler? ThemeChanged;

      // Older builds stored the name of the control library's theme, the MAUI client stores the bare variant
      // name; both still read back as the variant they meant. Anything else falls to the default.
      private static ThemeVariant ParseThemeVariant(string? value) => value switch {
         "Light" or "Win11Light" => ThemeVariant.Light,
         "Dark" or "Win11Dark" => ThemeVariant.Dark,
         _ => DefaultTheme
      };

      // One path for startup and for every switch afterwards, in a fixed order: the engine's own
      // tokens first, so a third-party applier that reads them sees the new values, then the windows
      // that paint part of themselves from code, then whoever listens.
      private void ApplyTheme() {
         var theme = ActiveTheme;

         if (App is not null) ThemeResources.Apply(App.Resources, theme);

         foreach (var applier in ServiceProvider.GetServices<IThemeApplier>()) {
            applier.Apply(theme);
         }

         MainWindow?.OnThemeChanged();
         ThemeChanged?.Invoke(this, EventArgs.Empty);
      }

      #endregion

      #region Methods

      /// <summary>
      /// Menetapkan pengguna yang sedang masuk, atau <c>null</c> untuk mengosongkannya.
      /// </summary>
      /// <param name="user">Pengguna yang masuk, atau <c>null</c> kalau tidak ada lagi yang masuk.</param>
      public void SetActiveUser(User? user) {
         ActiveUser = user;

         // Client yang sudah terlanjur dibuat ikut diperbarui di sini; yang dibuat sesudah ini mendapatkan
         // nilainya lewat GetActiveApiClient. Keduanya diisi dan dikosongkan berbarengan.
         foreach (var client in _apiClients.Values) {
            client.ActiveUserId = user?.cUserId;
            client.ActiveUserAccount = user?.cUserAccount;
         }

         ActiveUserChanged?.Invoke(this, EventArgs.Empty);
      }

      private void SyncBusinessTaskTracker() =>
         ServiceProvider.GetRequiredService<BusinessTaskTracker>().SetUser(ActiveUser?.cUserId);

      /// <summary>
      /// Menjalankan aplikasi WPF: membuat <see cref="System.Windows.Application"/>, menerapkan
      /// <see cref="CurrentTheme"/>, lalu menampilkan <see cref="MainWindow"/>. Method ini blocking
      /// selama aplikasi berjalan (mengikuti siklus hidup WPF <c>Application.Run</c>).
      /// </summary>
      public void Run() {
         App = new Application {
            // Window detach dan window hasil tab yang ditarik keluar sengaja tidak dijadikan owned
            // window supaya bisa berada di belakang window utama. Tanpa ini, menutup window utama
            // tidak mengakhiri aplikasi selama masih ada window semacam itu.
            ShutdownMode = ShutdownMode.OnMainWindowClose
         };
         // Before the main window exists, so it is created against the right tokens already; the
         // MainWindow?.OnThemeChanged() inside is a no-op at this point.
         ApplyTheme();
         // Assigned before InitLayout, so anything the initialisation reaches (theme changes, dialogs)
         // can already find the window through EmApp.MainWindow.
         MainWindow = new TabbedMainWindow(this);
         MainWindow.InitLayout();

         // Satu jalan pulang untuk kedua sebab berakhirnya sesi - user yang keluar sendiri dan sesi
         // yang mati di tengah jalan. Lewat dispatcher wajib: event ini bisa datang dari thread mana
         // pun, karena ia lahir di tengah request HTTP yang gagal diperbarui.
         SessionEnded += (_, e) => App.Dispatcher.InvokeAsync(() => OnSessionEndedAsync(e.Reason));

         // The task hub follows whoever is signed in. Its polling continues on the thread that starts
         // it, so it is always started from the dispatcher; the direct call covers the debug user, who
         // is already signed in before this point.
         ActiveUserChanged += (_, _) => App.Dispatcher.InvokeAsync(SyncBusinessTaskTracker);
         App.Dispatcher.InvokeAsync(SyncBusinessTaskTracker);

         // Mode debug sudah punya penggunanya sejak BuildApp (lihat InitDebugMode), jadi tidak ada
         // yang perlu ditanyakan dan aplikasi langsung terbuka. Di luar itu tidak ada siapa-siapa
         // dulu: layar login yang dipasang.
         // Layar pertama dipasang lewat dispatcher, bukan langsung di sini: memasangnya
         // asynchronous, sedangkan message loop yang menjalankan lanjutannya baru hidup di App.Run di
         // bawah. Prioritas Normal membuatnya tetap selesai sebelum window meng-handle Loaded, jadi
         // yang pertama dilihat user tidak berubah.
         App.Dispatcher.InvokeAsync(ShowFirstScreenAsync);

         App.Run(MainWindow);
      }

      // Tidak ada siapa-siapa lagi di atas method ini - dispatcher yang menjalankannya tidak
      // menunggu hasilnya, dan aplikasi ini tidak punya DispatcherUnhandledException - jadi
      // exception-nya ditangkap dan ditampilkan di sini.
      private async Task ShowFirstScreenAsync() {
         try {
            if (IsDebugMode) await MainWindow.ShowSignedInAsync();
            else await ShowLoginScreenAsync(null);
         }
         catch (Exception x) {
            MainWindow.ShowMboxError(x);
         }
      }

      /// <summary>
      /// Membangun ulang <see cref="UIConnections"/>: <see cref="DebugConnections"/> lebih dulu (kalau ada),
      /// disusul seluruh profil koneksi API yang tersimpan di Registry.
      /// </summary>
      public void RetrieveApiConnections() {
         using var container = ApiConnectionsRegKey;
         UIConnections.Clear();

         // Koneksi debug tidak berasal dari Registry. Instance-nya milik DebugConnections dan sengaja
         // dipakai ulang setiap kali daftar dibangun ulang, supaya referensinya tetap stabil.
         DebugConnections.EachOf(UIConnections.Add);

         container.GetSubKeyNames()
            .Select(name => {
               using var key = container.OpenSubKey(name);
               if (key is null) return null;

               return new ApiConnection {
                  ProfileName = key.GetValue(nameof(ApiConnection.ProfileName)) as string ?? name,
                  Host = key.GetValue(nameof(ApiConnection.Host)) as string ?? string.Empty,
                  Timeout = (int)(key.GetValue(nameof(ApiConnection.Timeout)) ?? 0),
                  IgnoreSslErrors = (int)(key.GetValue(nameof(ApiConnection.IgnoreSslErrors)) ?? 0) != 0,
               };
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .EachOf(UIConnections.Add);
      }

      /// <summary>
      /// Menyimpan profil koneksi API baru ke Registry, sekaligus memasukkannya ke
      /// <see cref="UIConnections"/> supaya daftar yang ditampilkan UI ikut ter-update.
      /// </summary>
      /// <param name="apiConnection">Data koneksi yang akan disimpan.</param>
      /// <exception cref="InvalidOperationException">Kalau koneksinya berasal dari konfigurasi debug.</exception>
      public void AddApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);
         using var container = ApiConnectionsRegKey;
         using var key = container.CreateSubKey(apiConnection.ProfileName);

         key.SetValue(nameof(ApiConnection.ProfileName), apiConnection.ProfileName, RegistryValueKind.String);
         key.SetValue(nameof(ApiConnection.Host), apiConnection.Host, RegistryValueKind.String);
         key.SetValue(nameof(ApiConnection.Timeout), apiConnection.Timeout, RegistryValueKind.DWord);
         key.SetValue(nameof(ApiConnection.IgnoreSslErrors), apiConnection.IgnoreSslErrors ? 1 : 0,
            RegistryValueKind.DWord);

         // Objek yang sama bisa masuk lewat UpdateApiConnection (diedit in-place, jadi sudah ada di
         // koleksi); Contains memakai reference equality karena ApiConnection tidak meng-override Equals.
         if (!UIConnections.Contains(apiConnection)) {
            UIConnections.Add(apiConnection);
         }
      }

      /// <summary>
      /// Memperbarui profil koneksi API. Jika nama profil berubah, entri lama dengan nama sebelumnya
      /// akan dihapus terlebih dulu sebelum entri baru disimpan.
      /// </summary>
      /// <param name="originalProfileName">Nama profil sebelum diubah.</param>
      /// <param name="apiConnection">Data koneksi terbaru.</param>
      public void UpdateApiConnection(string originalProfileName, ApiConnection apiConnection) {
         if (!string.Equals(originalProfileName, apiConnection.ProfileName, StringComparison.OrdinalIgnoreCase))
            DeleteApiConnection(originalProfileName);

         AddApiConnection(apiConnection);
      }

      /// <summary>
      /// Menghapus profil koneksi API berdasarkan objeknya, dari Registry maupun dari
      /// <see cref="UIConnections"/>.
      /// </summary>
      /// <param name="apiConnection">Koneksi yang akan dihapus.</param>
      /// <exception cref="InvalidOperationException">Kalau koneksinya berasal dari konfigurasi debug.</exception>
      public void DeleteApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);
         DeleteApiConnection(apiConnection.ProfileName);
         UIConnections.Remove(apiConnection);
      }

      /// <summary>
      /// Menjaga agar koneksi debug tidak ikut ditulis/dihapus di Registry. Ini pengaman lapis terakhir:
      /// UI sudah lebih dulu mencegahnya, jadi sampai ke sini berarti ada kesalahan pemanggilan.
      /// </summary>
      private static void ThrowIfDebugConnection(ApiConnection apiConnection) {
         if (!apiConnection.IsDebugConnection) {
            return;
         }

         throw new InvalidOperationException(
            $"Connection '{apiConnection.ProfileName}' is defined in the debug configuration, " +
            "so it cannot be saved, changed, or deleted.");
      }

      /// <summary>
      /// Menghapus profil koneksi API berdasarkan nama profilnya. Hanya menyentuh Registry, tidak
      /// mengubah <see cref="UIConnections"/> — dipakai <see cref="UpdateApiConnection"/> untuk membuang
      /// entri lama saat nama profil berubah, sementara objeknya sendiri tetap tinggal di koleksi.
      /// </summary>
      /// <param name="profileName">Nama profil yang akan dihapus.</param>
      public void DeleteApiConnection(string profileName) {
         using var container = ApiConnectionsRegKey;
         container.DeleteSubKeyTree(profileName, throwOnMissingSubKey: false);
      }

      public async Task<DateTime> GetDateStampAsync() {
         var client = GetActiveApiClient();
         if (client is null) throw new InvalidOperationException("There is no active API Client.");
         return await client!.GetServerTimeStampAsync();
      }

      #region Session

      /// <summary>
      /// Dipicu setiap kali sesi berakhir - baik karena user sendiri yang keluar maupun karena sesinya
      /// mati sendiri dan tidak bisa dipulihkan. Aplikasi sendiri mendengarkannya untuk menutup window
      /// lain, melepas layar yang terbuka, lalu kembali ke layar login, sehingga kedua sebab itu
      /// melewati jalan yang sama persis.
      /// </summary>
      public event EventHandler<SessionEndedEventArgs>? SessionEnded;

      // Koneksi yang sesinya sedang hidup, beserta client-nya. Disimpan supaya langganan event-nya
      // bisa dilepas kembali dan token debug-nya bisa dikembalikan saat sesinya berakhir.
      private ApiClient? _sessionClient;
      private ApiConnection? _sessionConnection;
      private string? _suspendedDebugToken;
      private bool _rememberSession;

      private ISessionStorage SessionStorage => ServiceProvider.GetRequiredService<ISessionStorage>();

      private Task? _claimsRefresh;

      /// <summary>Dipakai layar yang ingin menunggu hak selesai dimuat sebelum menggambar dirinya.</summary>
      public Task EnsureClaimsLoadedAsync() => _claimsRefresh ?? Task.CompletedTask;

      /// <summary>
      /// Memuat ulang katalog claim dan - kalau ada pengguna aktif - hak yang benar-benar dimilikinya.
      /// Dipanggil sekali sesudah login berhasil dan sesudah koneksi debug terpasang; tidak perlu
      /// dipanggil saat sign out - <see cref="EndSessionAsync(bool)"/> sudah memanggil
      /// <see cref="SetActiveUser"/> dengan <c>null</c>, dan tanpa pengguna aktif indexer
      /// <see cref="ClaimCollection"/> menjawab <c>false</c> dengan sendirinya.
      /// </summary>
      public async Task RefreshClaimsAsync() {
         if (GetActiveApiClient() is null) return;

         _serverClaims = await ServiceProvider.GetRequiredService<ICredentialServices>()
            .GetMeta_AllClaimActions();
         RebuildClaimCatalog();

         if (ActiveUser is not { } user) return;

         // Administrator tidak perlu dibacakan pemberiannya sama sekali: indexer sudah menjawab true
         // untuknya selama nama claim-nya ada di katalog. Akun debugger dan akun admin bawaan ikut ke
         // cabang ini karena cUserIsAdmin-nya memang true - dan itu sekaligus yang menjaga GetClaims()
         // tidak pernah dipanggil untuk id akun sistem, yang akan melempar SystemAccountException.
         // Hak langsung dan hak yang datang lewat role dibaca terpisah supaya client tahu asal
         // sebuah hak, lalu disatukan di sini. DistinctBy ada karena satu hak bisa datang dua kali -
         // diberikan langsung sekaligus dibawa sebuah role - dan AvailableClaims juga dibaca layar,
         // bukan hanya ClaimCollection yang memang tidak peduli barisnya kembar.
         user.AvailableClaims = user.cUserIsAdmin
            ? []
            : [
               .. (await user.GetClaims())
                  .Concat(await user.GetRoleClaims())
                  .DistinctBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            ];
      }

      /// <summary>
      /// Membuka sesi dari sepasang token yang baru diterbitkan server: memasangnya di client koneksi
      /// aktif, memuat identitas pemiliknya, dan - kalau diminta - menyimpannya supaya restart
      /// berikutnya tidak perlu mengetik password lagi.
      /// </summary>
      /// <param name="token">Pasangan token hasil sign in atau hasil pemulihan sesi.</param>
      /// <param name="remember">
      /// <c>true</c> kalau user memilih tetap masuk: refresh token disimpan, dan setiap hasil rotasi
      /// ikut menimpanya.
      /// </param>
      /// <exception cref="InvalidOperationException">Kalau belum ada koneksi aktif.</exception>
      /// <remarks>
      /// Kalau identitas pemiliknya gagal dimuat, seluruhnya dibatalkan dan exception-nya naik ke
      /// pemanggil: setengah masuk lebih buruk daripada gagal masuk.
      /// </remarks>
      public async Task BeginSessionAsync(TokenResult token, bool remember) {
         ArgumentNullException.ThrowIfNull(token);

         var connection = ActiveConnection
            ?? throw new InvalidOperationException("There is no active API connection to open a session on.");
         var client = GetActiveApiClient()!;

         client.SetSession(token);

         // Gerbang memeriksa token debug paling depan, jadi selama token itu masih menempel, Bearer
         // yang menyertainya tidak akan pernah terbaca - dan alur JWT tidak pernah benar-benar teruji
         // dari build dev. Nilainya disimpan, bukan dibangkitkan ulang nanti saat dikembalikan.
         _suspendedDebugToken = connection.DebugToken;
         connection.DebugToken = null;

         try {
            SetActiveUser(await LoadSessionUserAsync(token.cUserId));
            // Gagal di sini sengaja menggagalkan login-nya (masih di dalam try yang sama): masuk
            // dengan hak yang tidak diketahui lebih buruk daripada tidak jadi masuk - user akan
            // melihat aplikasi yang seluruh tombolnya mati tanpa penjelasan.
            _claimsRefresh = RefreshClaimsAsync();
            await _claimsRefresh;
         }
         catch (Exception) {
            connection.DebugToken = _suspendedDebugToken;
            _suspendedDebugToken = null;
            client.ClearSession();
            SessionStorage.Clear(connection.ProfileName);
            throw;
         }

         _sessionClient = client;
         _sessionConnection = connection;
         _rememberSession = remember;
         client.SessionChanged += OnApiClientSessionChanged;
         client.SessionEnded += OnApiClientSessionEnded;

         // Menyimpan sekali saat login saja tidak cukup: setiap pembaruan menerbitkan refresh token
         // baru dan mematikan yang lama, jadi yang tersimpan harus ikut ditimpa - lihat
         // OnApiClientSessionChanged.
         if (remember) StoreSession();
         else SessionStorage.Clear(connection.ProfileName);
      }

      /// <summary>
      /// Mengakhiri sesi yang sedang berjalan: memberi tahu server kalau diminta, membuang sesinya di
      /// sisi client, mengosongkan identitas, lalu memicu <see cref="SessionEnded"/>.
      /// </summary>
      /// <param name="notifyServer">
      /// <c>true</c> kalau server perlu diberi tahu supaya sesinya ikut berakhir di sana. Kegagalannya
      /// sengaja diabaikan: server yang tidak bisa dihubungi tidak boleh menahan user di dalam aplikasi.
      /// </param>
      public Task EndSessionAsync(bool notifyServer) => EndSessionAsync(notifyServer, null);

      /// <inheritdoc cref="EndSessionAsync(bool)" />
      /// <param name="notifyServer"><inheritdoc cref="EndSessionAsync(bool)" path="/param[@name='notifyServer']" /></param>
      /// <param name="reason">
      /// Kalimat yang ditampilkan layar login, atau <c>null</c> kalau user sendiri yang keluar.
      /// </param>
      public async Task EndSessionAsync(bool notifyServer, string? reason) {
         var client = _sessionClient;
         var connection = _sessionConnection;

         if (client is not null) {
            client.SessionChanged -= OnApiClientSessionChanged;
            client.SessionEnded -= OnApiClientSessionEnded;
         }

         // Jalur token debug tidak punya sesi sama sekali, dan server memang akan menjawab 401 karena
         // sesi pemanggilnya kosong - jadi yang tidak punya sesi tidak perlu memberitahu siapa-siapa.
         if (notifyServer && client is { HasSession: true }) {
            try {
               await ServiceProvider.GetRequiredService<ICredentialServices>().PostMeta_SignOut();
            }
            catch (Exception) {
               // Diabaikan dengan sengaja: user tetap keluar walau server tidak menjawab.
            }
         }

         client?.ClearSession();

         if (connection is not null) {
            SessionStorage.Clear(connection.ProfileName);
            if (_suspendedDebugToken is not null) connection.DebugToken = _suspendedDebugToken;
         }

         _suspendedDebugToken = null;
         _sessionClient = null;
         _sessionConnection = null;
         _rememberSession = false;

         SetActiveUser(null);
         SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
      }

      /// <summary>
      /// Memuat identitas pemilik sesi. Akun administrator bawaan tidak punya baris pengguna, jadi
      /// identitasnya dibuatkan di sini - sepola akun debugger. Diperiksa lebih dulu, bukan dengan
      /// menangkap exception-nya, karena pencarian baris memang melemparkan
      /// <c>SystemAccountException</c> untuk id semacam itu.
      /// </summary>
      private Task<User> LoadSessionUserAsync(string cUserId) =>
         cUserId == Defaults.AdminUserId
            ? Task.FromResult(CreateAdminUser(this))
            : User.GetUser_ByIdAsync(this, cUserId);

      // Disimpan ulang setiap kali isi sesi berganti, termasuk hasil rotasi: refresh token yang lama
      // mati begitu ditukar, jadi yang tersimpan harus selalu yang terbaru - kalau tidak, restart
      // berikutnya memakai token mati dan user terlempar ke layar login tanpa sebab yang kelihatan.
      private void OnApiClientSessionChanged(object? sender, EventArgs e) {
         if (_rememberSession && _sessionClient is { HasSession: true }) StoreSession();
      }

      // Sesi mati sendiri di tengah jalan - refresh gagal, atau sesinya sudah dicabut dari tempat lain.
      private void OnApiClientSessionEnded(object? sender, EventArgs e) {
         // Event ini lahir di tengah request HTTP yang gagal diperbarui, jadi bisa datang dari thread
         // mana pun - sementara yang dikerjakan di bawahnya mengosongkan identitas dan memicu
         // ActiveUserChanged, yang langsung menyentuh binding. Kalau tidak dikembalikan ke thread UI
         // lebih dulu, gejalanya baru muncul justru saat sesi mati sungguhan.
         var dispatcher = App?.Dispatcher;
         if (dispatcher is null || dispatcher.CheckAccess()) {
            _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice);
            return;
         }

         dispatcher.InvokeAsync(() => EndSessionAsync(notifyServer: false, SessionExpiredNotice));
      }

      /// <summary>
      /// Keterangan yang ditampilkan layar login saat sesi berakhir bukan karena user sendiri yang
      /// keluar. Bukan pesan kesalahan: sesi yang habis umurnya bukan kesalahan user.
      /// </summary>
      public const string SessionExpiredNotice = "Your session has ended. Please sign in again.";

      private void StoreSession() {
         if (_sessionConnection is not { } connection || _sessionClient is not { } client) return;
         if (client.RefreshToken is not { Length: > 0 } refreshToken) return;

         SessionStorage.Save(connection.ProfileName, new SavedSession(
            refreshToken,
            client.SessionUserId ?? string.Empty,
            ActiveUser?.cUserAccount ?? string.Empty));
      }

      /// <summary>
      /// Mencoba memulihkan sesi yang tersimpan untuk profil <paramref name="profileName"/>: menukar
      /// refresh token tersimpan dengan sepasang token baru, lalu membuka sesinya. Dipanggil saat
      /// startup, sesudah daftar koneksi termuat.
      /// </summary>
      /// <param name="profileName">Nama profil koneksi yang sesinya dipulihkan.</param>
      /// <returns>
      /// <c>true</c> kalau sesinya benar-benar pulih. <c>false</c> berarti tidak ada yang tersimpan,
      /// atau yang tersimpan sudah tidak berlaku - dan itu jawaban biasa, bukan kegagalan.
      /// </returns>
      public async Task<bool> TryRestoreSessionAsync(string profileName) {
         if (UIConnections.FirstOrDefault(r => r.ProfileName == profileName) is not { } connection) {
            return false;
         }

         if (SessionStorage.Load(profileName) is not { } saved) return false;

         ActiveConnection = connection;

         try {
            // Action-nya publik, jadi tidak butuh apa pun selain token itu sendiri - yang memang satu-
            // satunya yang tersisa: access token dari sesi sebelumnya sudah pasti mati.
            var token = await ServiceProvider.GetRequiredService<ICredentialServices>()
               .PostGetMeta_RefreshToken(saved.RefreshToken);
            await BeginSessionAsync(token, remember: true);
            return true;
         }
         catch (Exception) {
            SessionStorage.Clear(profileName);
            return false;
         }
      }

      #endregion

      #endregion
   }
}