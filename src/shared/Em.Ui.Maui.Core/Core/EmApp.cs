using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Navigations;
using Em.Ui.Maui.Shared;
using Application = Microsoft.Maui.Controls.Application;

namespace Em.Ui.Maui.Core
{
   public partial class EmApp : IEmAppUi
   {
      private const ThemeVariant DefaultTheme = ThemeVariant.Dark;
      private Dictionary<ApiConnection, ApiClient> _apiClients { get; } = [];

      /// <summary>Dipicu setiap kali koneksi API yang aktif berganti.</summary>
      public event EventHandler? ActiveConnectionChanged;

      /// <summary>
      /// Dipicu setiap kali <see cref="ActiveUser"/> berganti. Dipakai UI yang menampilkan identitas
      /// pengguna yang sedang aktif (mis. tombol akun di toolbar navigasi) supaya bisa menggambar ulang
      /// dirinya - <see cref="EmApp"/> sendiri bukan sumber binding ber-notifikasi, jadi tanpa event
      /// ini tampilan akan tertinggal pada pengguna yang lama.
      /// </summary>
      public event EventHandler? ActiveUserChanged;

      /// <summary>
      /// Dipicu sesudah tema aplikasi berganti. Layar yang warnanya dihitung sendiri per tema - panel
      /// branding, penanda terang/gelap - mendengarkannya untuk menggambar ulang dirinya.
      /// </summary>
      public event EventHandler? ThemeChanged;

      private EmApp(string[] args) {
         Args = args;
         // Mode debug tidak punya momen login: penggunanya sudah diangkat sinkron di dalam BuildApp
         // (InitDebugMode), sebelum ada koneksi aktif untuk ditumpangi satu pun await. Momen yang
         // benar-benar tersedia adalah saat koneksi aktif terpasang - yaitu ketika layar pertama
         // memilihkan DefaultDebugConnection (lihat ShowFirstScreenAsync).
         ActiveConnectionChanged += (_, _) => {
            if (IsDebugMode && ActiveConnection is not null) {
               _claimsRefresh = RefreshClaimsAsync();
            }
         };
      }

      #region Properties

      /// <summary>
      /// Pengaturan tampilan brand aplikasi (logo, teks-teks layar login, dan tema terang/gelap).
      /// Diisi dari <see cref="EmAppBuilder.ApplyBranding"/> saat <see cref="BuildApp"/>;
      /// kalau aplikasi tidak pernah memanggilnya, tetap berupa <see cref="BrandingInfo"/> kosong,
      /// jadi setiap anggotanya sudah otomatis jatuh ke nilai bawaan generik - tidak perlu null-check
      /// di sisi pemanggil.
      /// </summary>
      public BrandingInfo Branding { get; private set; } = null!;

      /// <summary><c>true</c> kalau aplikasi dijalankan dengan konfigurasi debug.</summary>
      public bool IsDebugMode { get; private set; }

      /// <summary>
      /// Aturan kata sandi yang berlaku di aplikasi ini, dibaca layar yang menerima kata sandi baru.
      /// Diisi dari <see cref="EmAppBuilder.UsePasswordPolicy"/> saat <see cref="BuildApp"/>; kalau
      /// aplikasi tidak pernah memanggilnya, berisi <see cref="PasswordPolicy"/> dengan nilai bawaan -
      /// jadi tidak pernah <c>null</c> dan pemakainya tidak perlu null-check.
      /// </summary>
      public PasswordPolicy PasswordPolicy { get; private set; } = null!;

      /// <summary>Daftar profil koneksi yang ditampilkan UI: koneksi debug lebih dulu, lalu yang tersimpan.</summary>
      public ObservableCollection<ApiConnection> UIConnections { get; } = [];

      /// <summary>Koneksi yang berasal dari konfigurasi debug, bukan dari penyimpanan perangkat.</summary>
      public ApiConnection[] DebugConnections { get; private set; } = [];

      /// <summary>Koneksi debug yang dipilihkan sendiri saat aplikasi dibuka, kalau ada.</summary>
      public ApiConnection? DefaultDebugConnection { get; private set; }

      /// <summary>
      /// Nama aplikasi, dipakai sebagai judul halaman utama dan sebagai awalan kunci penyimpanan
      /// pengaturan aplikasi (mis. koneksi API, tema).
      /// </summary>
      public string ApplicationName { get; private set; } = null!;

      /// <summary>
      /// Argumen command-line yang diterima aplikasi saat startup. Di Android hampir selalu kosong,
      /// dan tetap ada supaya bentuk startup-nya sama dengan client desktop.
      /// </summary>
      public string[] Args { get; init; }

      /// <summary>
      /// DI container aplikasi, hanya hidup selama <see cref="BuildApp"/>. Module mendaftarkan service
      /// lewat callback <see cref="EmAppBuilder"/>, bukan langsung ke property ini; setelah
      /// <see cref="ServiceProvider"/> dibangun, penambahan ke koleksi ini tidak lagi berpengaruh.
      /// </summary>
      internal IServiceCollection Services { get; } = new ServiceCollection();

      // Disimpan sebagai tipe konkret supaya bisa di-dispose saat aplikasi berhenti, sementara yang
      // dibuka ke pemanggil cukup IServiceProvider lewat ServiceProvider.
      private ServiceProvider _serviceProvider = null!;

      /// <inheritdoc />
      /// <remarks>
      /// Di sisi UI tidak ada scope per-request seperti di API, jadi yang dikembalikan selalu provider
      /// akar hasil build dari <see cref="Services"/> di akhir <see cref="BuildApp"/>.
      /// </remarks>
      public IServiceProvider ServiceProvider => _serviceProvider;

      /// <summary>Tempat pengaturan aplikasi disimpan di perangkat.</summary>
      public AppSettings Settings { get; private set; } = null!;

      /// <summary>
      /// Halaman utama aplikasi, tersedia setelah <see cref="CreateRootPage"/> dipanggil. Dipakai
      /// sebagai pemilik dialog oleh view model yang tidak punya halaman sendiri.
      /// </summary>
      public Page? RootPage { get; private set; }

      /// <summary>Koneksi API yang sedang dipakai, atau <c>null</c> kalau belum ada yang dipilih.</summary>
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
      // XxxCommandAllowed - a path the UI runs over and over - and merging there would allocate a
      // new list each time a button decides whether it is enabled.
      private ClaimAction[] _allClaims = [];

      private bool _internalClaimsSealed;

      /// <summary>
      /// Katalog seluruh claim yang dikenal aplikasi: claim milik layar bawaan client digabung dengan
      /// katalog milik server aktif (dimuat <see cref="RefreshClaimsAsync"/>). Kalau sebuah kunci ada
      /// di kedua sisi, yang dipakai adalah deklarasi client - ia yang tidak bisa berubah saat
      /// aplikasi berjalan. Bukan milik siapa-siapa: tidak dibersihkan saat sign out, dan dibaca lewat
      /// extension method <c>Claims()</c> pada <c>IServices</c> saat membentuk
      /// <see cref="ClaimCollection"/>.
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

      /// <summary>
      /// Client API untuk koneksi yang sedang aktif, atau <c>null</c> kalau belum ada koneksi aktif.
      /// Satu client dipakai ulang per koneksi.
      /// </summary>
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

      // Nama pengaturan tempat daftar profil koneksi disimpan. Berbeda dengan client desktop yang
      // memberi satu subkey per profil, di sini seluruh daftarnya disimpan sebagai satu teks JSON -
      // penyimpanan pengaturan MAUI memang tidak mengenal susunan bercabang.
      private const string ApiConnectionsSettingName = "Api Connections";

      /// <summary>
      /// Apakah user memilih tetap masuk di layar login. Yang disimpan cuma pilihannya; kredensial
      /// tidak pernah ikut tersimpan - satu-satunya yang diingat selain flag ini adalah
      /// <see cref="RememberedUserName"/>.
      /// </summary>
      public bool RememberSignIn {
         get => Settings.GetBool(nameof(RememberSignIn));
         set => Settings.SetBool(nameof(RememberSignIn), value);
      }

      /// <summary>
      /// Nama akun terakhir yang dipakai sign in, supaya layar login bisa mengisikannya kembali selama
      /// <see cref="RememberSignIn"/> menyala. Diisi <c>null</c> (atau teks kosong) untuk melupakannya.
      /// </summary>
      public string? RememberedUserName {
         get => Settings.GetString(nameof(RememberedUserName));
         set => Settings.SetString(nameof(RememberedUserName), value);
      }

      /// <summary>
      /// Nama profil koneksi terakhir yang dipakai sign in, sepasang dengan
      /// <see cref="RememberedUserName"/>. Diperlukan karena sesi tersimpan dititipkan per profil:
      /// tanpa tahu profil mana, tidak ada yang bisa dipulihkan saat aplikasi dibuka lagi.
      /// </summary>
      public string? RememberedProfileName {
         get => Settings.GetString(nameof(RememberedProfileName));
         set => Settings.SetString(nameof(RememberedProfileName), value);
      }

      /// <summary>
      /// Mode tema yang sedang aktif, terang atau gelap (tersimpan di pengaturan aplikasi). Meng-set
      /// nilai ini langsung menerapkan temanya ke aplikasi dan memicu <see cref="ThemeChanged"/>.
      /// Default: <see cref="ThemeVariant.Dark"/>.
      /// </summary>
      public ThemeVariant CurrentTheme {
         get => ParseThemeVariant(Settings.GetString(nameof(CurrentTheme)));
         set {
            if (CurrentTheme == value)
               return;

            Settings.SetString(nameof(CurrentTheme), value.ToString());
            ApplyTheme();
         }
      }

      /// <summary><c>true</c> kalau mode tema yang sedang aktif adalah mode terang.</summary>
      public bool IsLightTheme => CurrentTheme == ThemeVariant.Light;

      /// <summary>
      /// Tema yang sedang dipakai: <see cref="BrandingInfo.LightTheme"/> atau
      /// <see cref="BrandingInfo.DarkTheme"/> milik <see cref="Branding"/>, sesuai
      /// <see cref="CurrentTheme"/>.
      /// </summary>
      public ThemeBase ActiveTheme => Branding.GetTheme(CurrentTheme);

      // The desktop client used to store the name of its control library's theme; both spellings
      // still read back as the variant they meant. Anything else falls to the default.
      private static ThemeVariant ParseThemeVariant(string? value) => value switch {
         "Light" or "Win11Light" => ThemeVariant.Light,
         "Dark" or "Win11Dark" => ThemeVariant.Dark,
         _ => DefaultTheme
      };

      // One path for startup and for every switch afterwards, in the same order as the desktop
      // client: the engine's own theme first, then the registered appliers, then whoever listens.
      // The palette itself needs nothing here - both variants are loaded, and AppThemeBinding picks.
      private void ApplyTheme() {
         if (Application.Current is { } app) {
            app.UserAppTheme = CurrentTheme == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark;
         }

         var theme = ActiveTheme;
         foreach (var applier in ServiceProvider.GetServices<IThemeApplier>()) {
            applier.Apply(theme);
         }

         // Penanda terang/gelap di toolbar maupun di layar login adalah property hitungan biasa, jadi
         // semuanya harus diminta menggambar ulang.
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

      /// <summary>
      /// Membangun halaman utama aplikasi dan memasang layar pertamanya. Dipanggil sekali dari
      /// <c>App.CreateWindow</c> - inilah padanan MAUI dari <c>Run</c> di client desktop, yang di sana
      /// blocking sampai aplikasi berhenti sementara di sini siklus hidupnya dipegang MAUI sendiri.
      /// </summary>
      /// <returns>Halaman yang dipasang ke window aplikasi.</returns>
      public Page CreateRootPage() {
         ApplyTheme();

         var host = new SpaNavigationHost(this);
         RootPage = host;

         // Mode debug sudah punya penggunanya sejak BuildApp (lihat InitDebugMode), jadi tidak ada yang
         // perlu ditanyakan dan aplikasi langsung terbuka di home. Di luar itu tidak ada siapa-siapa
         // dulu: layar login yang dipasang, dan home baru dibangun sesudah ada yang benar-benar masuk.
         // Dititipkan ke dispatcher, bukan dijalankan di sini: memasang layar pertama itu asynchronous,
         // sementara method ini harus mengembalikan halamannya sekarang juga supaya window bisa dibuka.
         host.Dispatcher.Dispatch(() => _ = ShowFirstScreenAsync());
         return host;
      }

      // Tidak ada siapa-siapa lagi di atas method ini - dispatcher yang menjalankannya tidak menunggu
      // hasilnya - jadi exception-nya ditangkap dan ditampilkan di sini.
      private async Task ShowFirstScreenAsync() {
         try {
            if (IsDebugMode) {
               // Mode debug melompati layar login, jadi tidak ada satu pun layar yang sempat
               // memilihkan servernya. Dipilih di sini, tepat sebelum home berdiri: koneksi aktif
               // itulah yang membuka jalan ke katalog claim (lihat constructor), dan home menggambar
               // dirinya dari hak yang ada. Panel account boleh menggantinya kapan saja sesudah ini.
               RetrieveApiConnections();
               ActiveConnection ??= DefaultDebugConnection;

               await GoHomeAsync();
               return;
            }

            // Layar login dipasang lebih dulu, baru sesi tersimpan dicoba: layar itulah yang membangun
            // daftar koneksi, dan tanpa daftar itu tidak ada profil yang bisa dipulihkan. Kalau
            // pemulihannya berhasil, layar login langsung ditinggalkan - NavigateHome melepasnya
            // bersama seluruh jalur navigasi.
            await ShowLoginScreen(null);
            if (await TryRestoreRememberedSessionAsync()) await GoHomeAsync();
         }
         catch (Exception x) {
            if (RootPage is { } page) await page.DisplayAlertAsync("Error", x.SerializedMessagesDefault(), "OK");
         }
      }

      // Pulang ke home selalu berpasangan dengan memuat ulang isinya: home dibangun sekali dan tidak
      // pernah dilepas, jadi tanpa muat ulang ia akan menampilkan keadaan sebelum ada yang masuk.
      private async Task GoHomeAsync() {
         await MainStack.NavigateHome();
         await MainStack.Home!.Reload();
      }

      /// <summary>
      /// Mencoba melanjutkan sesi yang tersimpan dari kali terakhir aplikasi dipakai, supaya user yang
      /// memilih tetap masuk tidak perlu mengetik password lagi. Gagalnya bukan kesalahan user - sesi
      /// yang sudah dicabut atau habis umurnya cukup meninggalkan layar login apa adanya, tanpa pesan
      /// kesalahan.
      /// </summary>
      /// <returns><c>true</c> kalau sesinya benar-benar pulih.</returns>
      public Task<bool> TryRestoreRememberedSessionAsync() {
         if (!RememberSignIn) return Task.FromResult(false);
         if (RememberedProfileName is not { Length: > 0 } profileName) return Task.FromResult(false);

         return TryRestoreSessionAsync(profileName);
      }

      /// <summary>
      /// Memasang layar login sebagai satu-satunya isi jalur navigasi, sehingga tidak ada jalan kembali
      /// ke apa pun yang tadi terbuka. Dipanggil saat aplikasi dibuka dan setiap kali sesi berakhir.
      /// </summary>
      /// <param name="notice">
      /// Keterangan yang ditampilkan layar login, atau <c>null</c> kalau user sendiri yang keluar.
      /// </param>
      public async Task ShowLoginScreen(string? notice) {
         var logon = Navigations.First(r => r.Name == LogonNavigationName);
         if (!await NavigateToRoot(logon)) return;

         // Body-nya sudah dibangun oleh navigasi barusan, jadi membacanya di sini tidak membangun
         // apa-apa lagi. Umurnya seumur entrinya: begitu stack dibersihkan sesudah login berhasil,
         // control ini dilepas dan kunjungan berikutnya mendapat form yang bersih.
         if (MainStack.Current?.Body is LoginControl login) login.Vm.SessionEndedNotice = notice;
      }

      /// <summary>
      /// Membangun ulang <see cref="UIConnections"/>. Di mode debug isinya hanya
      /// <see cref="DebugConnections"/> dan penyimpanan perangkat tidak dibaca sama sekali; di luar itu
      /// isinya seluruh profil koneksi API yang tersimpan di perangkat.
      /// </summary>
      /// <remarks>
      /// Profil tersimpan adalah milik build release - ia yang menulisnya saat login berhasil, dan ia pula
      /// yang membacanya lagi untuk memulihkan sesi. Build debug tidak pernah membutuhkannya: servernya
      /// datang dari kode dan terkunci di layar login. Kalau daftarnya tetap dibaca di debug, profil bekas
      /// login release di perangkat yang sama akan ikut muncul sebagai pilihan server yang bukan miliknya.
      /// </remarks>
      public void RetrieveApiConnections() {
         UIConnections.Clear();

         // Koneksi debug tidak berasal dari penyimpanan. Instance-nya milik DebugConnections dan sengaja
         // dipakai ulang setiap kali daftar dibangun ulang, supaya referensinya tetap stabil.
         if (IsDebugMode) {
            DebugConnections.EachOf(UIConnections.Add);
            return;
         }

         LoadStoredConnections()
            .Select(r => new ApiConnection {
               ProfileName = r.ProfileName,
               Host = r.Host,
               Timeout = r.Timeout,
               IgnoreSslErrors = r.IgnoreSslErrors,
            })
            .EachOf(UIConnections.Add);
      }

      /// <summary>
      /// Menyimpan profil koneksi API baru, sekaligus memasukkannya ke <see cref="UIConnections"/>
      /// supaya daftar yang ditampilkan UI ikut ter-update.
      /// </summary>
      /// <param name="apiConnection">Data koneksi yang akan disimpan.</param>
      /// <exception cref="InvalidOperationException">Kalau koneksinya berasal dari konfigurasi debug.</exception>
      public void AddApiConnection(ApiConnection apiConnection) {
         ThrowIfDebugConnection(apiConnection);

         var stored = LoadStoredConnections()
            .Where(r => !string.Equals(r.ProfileName, apiConnection.ProfileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

         stored.Add(new StoredConnection(
            apiConnection.ProfileName, apiConnection.Host, apiConnection.Timeout, apiConnection.IgnoreSslErrors));
         SaveStoredConnections(stored);

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
      /// Menghapus profil koneksi API berdasarkan objeknya, dari penyimpanan maupun dari
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
      /// Menghapus profil koneksi API berdasarkan nama profilnya, berikut sesi yang tertitip padanya.
      /// Hanya menyentuh penyimpanan, tidak mengubah <see cref="UIConnections"/> - dipakai
      /// <see cref="UpdateApiConnection"/> untuk membuang entri lama saat nama profil berubah,
      /// sementara objeknya sendiri tetap tinggal di koleksi.
      /// </summary>
      /// <param name="profileName">Nama profil yang akan dihapus.</param>
      public void DeleteApiConnection(string profileName) {
         SaveStoredConnections(LoadStoredConnections()
            .Where(r => !string.Equals(r.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .ToList());

         // Sesi tersimpan tidak menumpang profilnya seperti di client desktop, jadi ia harus dibuang
         // di sini - kalau tidak, ia akan tertinggal untuk profil yang sudah tidak ada.
         SessionStorage.Clear(profileName);
      }

      /// <summary>
      /// Menjaga agar koneksi debug tidak ikut ditulis/dihapus di penyimpanan. Ini pengaman lapis
      /// terakhir: UI sudah lebih dulu mencegahnya, jadi sampai ke sini berarti ada kesalahan pemanggilan.
      /// </summary>
      private static void ThrowIfDebugConnection(ApiConnection apiConnection) {
         if (!apiConnection.IsDebugConnection) {
            return;
         }

         throw new InvalidOperationException(
            $"Connection '{apiConnection.ProfileName}' is defined in the debug configuration, " +
            "so it cannot be saved, changed, or deleted.");
      }

      // Bentuk profil koneksi sebagaimana ia tersimpan. Sengaja dipisah dari ApiConnection: yang di
      // sana ada dua anggota yang tidak boleh ikut tersimpan sama sekali - penanda koneksi debug dan
      // token debugnya.
      private sealed record StoredConnection(string ProfileName, string Host, int Timeout, bool IgnoreSslErrors);

      private List<StoredConnection> LoadStoredConnections() {
         var payload = Settings.GetString(ApiConnectionsSettingName);
         if (string.IsNullOrWhiteSpace(payload)) return [];

         try {
            return JsonSerializer.Deserialize<List<StoredConnection>>(payload) ?? [];
         }
         catch (JsonException) {
            // Daftar yang tidak bisa dibaca tidak akan pernah bisa dibaca lagi. Dibuang sekarang supaya
            // tidak dicoba lagi setiap kali aplikasi dibuka.
            Settings.SetString(ApiConnectionsSettingName, null);
            return [];
         }
      }

      private void SaveStoredConnections(List<StoredConnection> connections) =>
         Settings.SetString(ApiConnectionsSettingName, JsonSerializer.Serialize(connections));

      /// <inheritdoc />
      public async Task<DateTime> GetDateStampAsync() {
         var client = GetActiveApiClient();
         if (client is null) throw new InvalidOperationException("There is no active API Client.");
         return await client.GetServerTimeStampAsync();
      }

      #region Session

      /// <summary>
      /// Dipicu setiap kali sesi berakhir - baik karena user sendiri yang keluar maupun karena sesinya
      /// mati sendiri dan tidak bisa dipulihkan. Halaman utama mendengarkannya untuk kembali ke layar
      /// login, sehingga kedua sebab itu melewati jalan yang sama persis.
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
      /// aktif, memuat identitas pemiliknya, dan - kalau diminta - menyimpannya supaya pembukaan
      /// aplikasi berikutnya tidak perlu mengetik password lagi.
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
      // mati begitu ditukar, jadi yang tersimpan harus selalu yang terbaru - kalau tidak, pembukaan
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
         var dispatcher = RootPage?.Dispatcher ?? Application.Current?.Dispatcher;
         if (dispatcher is null || !dispatcher.IsDispatchRequired) {
            _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice);
            return;
         }

         dispatcher.Dispatch(() => _ = EndSessionAsync(notifyServer: false, SessionExpiredNotice));
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
