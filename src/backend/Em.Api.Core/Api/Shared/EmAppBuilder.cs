using System.Data;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using HttpMethod = Em.Shared.HttpMethod;

namespace Em.Api.Shared
{
   public partial class EmAppBuilder
   {
      // DI container belum dibangun saat AddService dipanggil, jadi dipakai logger bootstrap
      // yang berdiri sendiri (bukan dari ServiceProvider) khusus untuk log proses registrasi.
      private static readonly ILoggerFactory BootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
      private static readonly ILogger Logger = BootstrapLoggerFactory.CreateLogger<EmAppBuilder>();

      internal IServiceCollection Services { get; init; } = null!;
      internal List<ActionDefinition> ActionDefinitions { get; } = [];
      internal List<Action<IServiceCollection, IReadOnlyDictionary<string, DbConnectionInfo>>> DbContextRegistrations { get; } = [];

      /// <summary>
      /// Connection name each context type was registered against, for whoever needs to know where a
      /// context lives before any request runs (the approval startup checks).
      /// </summary>
      internal Dictionary<Type, string> DbContextConnectionNames { get; } = [];

      /// <summary>
      /// The name reserved for the connection configured through <see cref="SetDbProvider"/>. Every
      /// other connection is registered under a name of its own via <see cref="AddExtraDbConn"/>.
      /// </summary>
      public const string DefaultConnectionName = "Default";

      /// <summary>
      /// Every database connection registered so far, <see cref="DefaultConnectionName"/> included once
      /// <see cref="SetDbProvider"/> has run. This is the one place a connection name resolves to a
      /// connection string and provider - <see cref="AddDbContext{TContext}(string)"/> and
      /// <see cref="ServicesBase.GetService{T}(string)"/> both read it, nothing else does.
      /// </summary>
      internal Dictionary<string, DbConnectionInfo> DbConnections { get; } = new(StringComparer.OrdinalIgnoreCase);

      internal DatabaseProvider DatabaseProvider =>
         DbConnections.TryGetValue(DefaultConnectionName, out var info) ? info.Provider : DatabaseProvider.MicrosoftSqlServer;

      internal string ConnectionString =>
         DbConnections.TryGetValue(DefaultConnectionName, out var info) ? info.ConnectionString : string.Empty;

      internal List<DebugTokenKey> DebugTokenKeys { get; } = [];
      internal List<ClaimAction> ClaimActions { get; } = [];

      public void AddService<T1, T2>() where T1 : class, IServices where T2 : ServicesBase, T1 =>
         AddService<T1, T2>(enforceClaims: true);

      /// <summary>
      /// Overload internal untuk service milik engine sendiri, satu-satunya yang boleh berdiri di luar
      /// pemeriksaan claim per action. Sengaja tidak <c>public</c>: kalau module author bisa memanggilnya,
      /// "lupa memberi claim" dan "sengaja tanpa claim" jadi tidak bisa dibedakan lagi dari luar.
      /// </summary>
      /// <param name="enforceClaims">
      /// <c>false</c> hanya untuk service yang didaftarkan <c>UseEm</c> sendiri sebelum callback module
      /// manapun berjalan. Ada empat. Tiga di antaranya - inti, kontak, kredensial - bukan module: aksi
      /// sensitifnya sudah dijaga pemeriksaan yang lebih tepat dari "claim apa pun di module ini" - hak
      /// atas diri sendiri atau hak administrator - dan memaksakan default itu ke sini berarti setiap
      /// pengguna butuh diberi claim hanya supaya bisa masuk dan memuat identitasnya sendiri, yang bukan
      /// otorisasi melainkan syarat login kedua tanpa arti. Yang keempat, pengelola CDN, menyebut claim-nya
      /// sendiri di setiap action, jadi default "claim apa pun" tidak menambah apa-apa di atasnya.
      /// Parameter <c>claim</c> pada <c>[GetAction]</c>/<c>[PostAction]</c> tetap berlaku di sini - yang
      /// dilepas hanya defaultnya.
      /// </param>
      internal void AddService<T1, T2>(bool enforceClaims) where T1 : class, IServices where T2 : ServicesBase, T1 {
         var moduleName = ModuleAttribute.ResolveName(typeof(T2));

         var actionableMethods = GetActionableMethods(typeof(T2));
         if (actionableMethods.Length == 0) {
            Logger.LogWarning(
               "Service '{Interface}' ({Implementation}) in module '{Module}' has no [GetAction]/[PostAction] methods and will not be registered.",
               typeof(T1).Name, typeof(T2).Name, moduleName);
            return;
         }

         Services.AddScoped(typeof(T1), typeof(T2));
         RegisterActions(typeof(T2), typeof(T1), moduleName, actionableMethods, enforceClaims);

         Logger.LogInformation("Service registered: {Interface} -> {Implementation} (Module: {Module})",
            typeof(T1).Name, typeof(T2).Name, moduleName);
      }

      public void SetDbProvider(string connectionString, DatabaseProvider? provider = null) {
         DbConnections[DefaultConnectionName] =
            new DbConnectionInfo(DefaultConnectionName, connectionString, provider ?? DatabaseProvider.MicrosoftSqlServer);
      }

      /// <summary>
      /// Registers an additional named database connection, for data that does not live in the
      /// "Default" connection set by <see cref="SetDbProvider"/>. The name is what
      /// <see cref="AddDbContext{TContext}(string)"/>, <see cref="AddDbContextFactory{TContext}(string)"/>
      /// and <see cref="ServicesBase.GetService{T}(string)"/> resolve the connection by - never the
      /// connection string or provider directly - so a name that is misspelled at any of those call
      /// sites is rejected there instead of silently opening the wrong database.
      /// </summary>
      /// <exception cref="ArgumentException">
      /// Thrown when <paramref name="name"/> or <paramref name="connectionString"/> is empty, when
      /// <paramref name="name"/> equals <see cref="DefaultConnectionName"/> - that name is reserved for
      /// <see cref="SetDbProvider"/> - or when <paramref name="name"/> is already registered (compared
      /// case-insensitively).
      /// </exception>
      public void AddExtraDbConn(string name, string connectionString, DatabaseProvider provider) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A database connection name must not be empty.", nameof(name));
         }

         if (string.IsNullOrWhiteSpace(connectionString)) {
            throw new ArgumentException($"Database connection '{name}' has an empty connection string.",
               nameof(connectionString));
         }

         if (string.Equals(name, DefaultConnectionName, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException(
               $"'{DefaultConnectionName}' is reserved for the connection configured through '{nameof(SetDbProvider)}'; use a different name.",
               nameof(name));
         }

         if (DbConnections.ContainsKey(name)) {
            throw new ArgumentException($"Database connection '{name}' is already registered.", nameof(name));
         }

         DbConnections.Add(name, new DbConnectionInfo(name, connectionString, provider));
         Logger.LogInformation("Database connection registered: {Name} ({Provider})", name, provider);
      }

      /// <summary>
      /// Password pertama untuk akun <c>admin</c>, dipakai hanya saat database masih kosong: nilainya
      /// disemai sebagai password bawaan pada startup pertama dan sesudah itu yang berlaku selalu apa
      /// yang tersimpan di database, bukan lagi nilai ini.
      /// </summary>
      public string FirstTimeAdminPassword { get; set; } = "Admin1234";

      /// <summary>
      /// Berapa lama - dalam jam - baris sesi yang sudah mati masih disimpan sebelum dibuang. Sesi
      /// disimpan di database justru supaya ada jejaknya, jadi barisnya sengaja tidak dihapus tepat
      /// saat kedaluwarsa; angka ini yang menentukan seberapa panjang jejak itu.
      /// </summary>
      public int SessionTokenRetentionHour { get; set; } = 24 * 30;

      /// <summary>
      /// Berapa lama sebuah action <c>GET</c> boleh bekerja sebelum engine menghentikannya sendiri.
      /// Ini jaring pengaman untuk action yang kebablasan - bukan janji lama tanggap kepada
      /// pemanggil: request yang selesai lebih cepat tidak pernah menunggu sampai angka ini.
      /// </summary>
      /// <remarks>
      /// Hanya berlaku untuk <c>GET</c>. Action <c>POST</c> tidak pernah diputus engine, karena
      /// memutus sebuah penulisan di tengah jalan tidak menghasilkan "tidak jadi" melainkan "entah" -
      /// pemanggilnya tidak punya cara tahu sejauh mana tulisannya sudah sampai. Satu action
      /// <c>GET</c> boleh menyebutkan angkanya sendiri lewat
      /// <c>[GetAction(requestTimeoutSecond: ...)]</c>, dan angka itu menggantikan yang di sini.
      /// <para>
      /// <c>TimeSpan.Zero</c> atau nilai negatif mematikannya sama sekali: tidak ada action yang
      /// dibatasi waktu, dan yang tersisa hanya pemutusan saat pemanggilnya benar-benar pergi.
      /// </para>
      /// </remarks>
      public TimeSpan HttpRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

      #region Business Task

      /// <summary>
      /// Folder tempat server menyimpan hasil business task yang berupa data atau file, satu subfolder per
      /// task. Path absolut dipakai apa adanya; path relatif dihitung dari folder konten aplikasi, sama
      /// seperti folder CDN. Foldernya dibuat sendiri saat startup.
      /// </summary>
      /// <remarks>
      /// Hanya hasil yang sukses yang bertahan melewati restart server; sisa task yang gagal atau terputus
      /// dibersihkan saat startup. Jangan arahkan ke folder yang juga dipakai untuk hal lain.
      /// </remarks>
      public string BusinessTaskCachePath { get; set; } = "./data/tasks";

      /// <summary>
      /// Berapa lama business task yang sukses tanpa hasil, atau yang dibatalkan, masih tampil sebelum
      /// hilang sendiri. Task yang gagal, dan task yang hasilnya masih bisa diambil, tidak terpengaruh:
      /// keduanya tampil sampai dibersihkan.
      /// </summary>
      public TimeSpan BusinessTaskGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

      /// <summary>
      /// Batas jumlah business task yang boleh berjalan bersamaan, dipakai selama batas itu belum pernah
      /// diatur lewat layar Business Task Manager. Sesudah diatur di sana, yang berlaku adalah yang
      /// tersimpan di metadata server, bukan nilai ini.
      /// </summary>
      public BusinessTaskLimit BusinessTaskDefaultLimit { get; set; } = new() {
         Mode = BusinessTaskLimitMode.Global,
         Limit = 2
      };

      #endregion

      /// <summary>
      /// Bawaan untuk <see cref="ActionRateLimit"/>: 300 request per menit, atau rata-rata lima per
      /// detik. Angkanya dipilih supaya pemakaian wajar tidak pernah menyentuhnya - satu layar yang
      /// membuka daftar berikut semua lookup-nya menghabiskan puluhan request dalam sekejap, bukan
      /// ratusan - sementara yang mengetuk-ngetuk dari luar tetap tertahan.
      /// </summary>
      public const int DefaultActionRateLimit = 300;

      private int _actionRateLimit = DefaultActionRateLimit;

      /// <summary>
      /// Berapa banyak request yang boleh datang dari satu alamat pemanggil dalam satu menit;
      /// <c>-1</c> berarti tanpa batas. Yang melebihi dijawab <c>429</c> tanpa action-nya sempat
      /// berjalan - bahkan sebelum identitasnya diperiksa, supaya banjir request tidak ikut
      /// membebani database.
      /// </summary>
      /// <remarks>
      /// Yang dibatasi adalah alamat, bukan pengguna: di titik ini engine memang belum tahu siapa
      /// pemanggilnya, dan justru request tanpa identitas - percobaan password beruntun, pemindai
      /// yang menyapu route - yang paling perlu ditahan. Konsekuensinya sekantor di belakang satu
      /// NAT berbagi satu jatah, jadi angkanya perlu dinaikkan kalau banyak client berbagi alamat.
      /// <para>
      /// Jatahnya dihitung dengan jendela bergeser, bukan jendela yang di-reset serentak tiap menit:
      /// tanpa itu sebuah pemanggil bisa menghabiskan jatah penuh di detik terakhir sebuah jendela
      /// dan jatah penuh berikutnya di detik pertama jendela selanjutnya - dua kali lipat batas ini
      /// dalam sekejap, persis di tempat yang seharusnya dijaga.
      /// </para>
      /// </remarks>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Dilempar kalau nilainya <c>0</c> - yang berarti tidak satu pun request boleh masuk, dan itu
      /// tidak pernah yang dimaksud; tulis <c>-1</c> kalau memang hendak dimatikan - atau di bawah
      /// <c>-1</c>.
      /// </exception>
      public int ActionRateLimit {
         get => _actionRateLimit;
         set {
            if (value == 0 || value < -1) {
               throw new ArgumentOutOfRangeException(nameof(value), value,
                  $"'{nameof(ActionRateLimit)}' must be -1 (no limit) or greater than 0.");
            }

            _actionRateLimit = value;
         }
      }

      /// <summary>
      /// Mendaftarkan satu claim ke katalog. Katalog tinggal di kode - tidak ada tabel module dan
      /// tidak ada tabel daftar claim - jadi module dipasang, claim-nya ada; module dilepas,
      /// claim-nya hilang sendiri.
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau <see cref="ClaimAction.Key"/>-nya sudah terdaftar sebelumnya (dibandingkan
      /// case-insensitive, mengikuti collation kolom <c>cUserClaimName</c>).
      /// </exception>
      public void AddClaims(ClaimAction claim) {
         if (ClaimActions.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException(
               $"Claim '{claim.Key}' is already registered for module '{claim.ModuleName}'.");
         }

         ClaimActions.Add(claim);
         Logger.LogInformation("Claim registered: {Key}", claim.Key);
      }

      /// <inheritdoc cref="AddClaims(ClaimAction)" />
      public void AddClaims(params ClaimAction[] claims) {
         foreach (var claim in claims) {
            AddClaims(claim);
         }
      }

      #region Proxy

      internal List<IPAddress> TrustedProxies { get; } = [];

      internal List<IPNetwork> TrustedProxyNetworks { get; } = [];

      internal bool TrustAnyProxyAddress { get; private set; }

      private int _proxyHopLimit = 1;

      /// <summary>
      /// Berapa banyak proxy berurutan yang boleh dipercaya saat membaca alamat asal request;
      /// <c>-1</c> berarti tanpa batas. Bawaannya <c>1</c>, yang cocok untuk susunan lazim "satu
      /// reverse proxy di depan server".
      /// <para>
      /// Angkanya baru perlu dinaikkan kalau request memang melewati beberapa lapis - misalnya CDN
      /// di depan load balancer di depan nginx - dan harus sama dengan jumlah lapisan itu. Dinaikkan
      /// melebihi jumlah sebenarnya, satu lapis yang tidak ada jadi ikut dipercaya, dan alamat yang
      /// tercatat bisa dikarang pemanggilnya.
      /// </para>
      /// </summary>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Dilempar kalau nilainya <c>0</c> - yang berarti tidak ada header yang dibaca sama sekali,
      /// dan itu lebih jelas ditulis dengan tidak mendaftarkan proxy apa pun - atau di bawah <c>-1</c>.
      /// </exception>
      public int ProxyHopLimit {
         get => _proxyHopLimit;
         set {
            if (value == 0 || value < -1) {
               throw new ArgumentOutOfRangeException(nameof(value), value,
                  $"'{nameof(ProxyHopLimit)}' must be -1 (no limit) or greater than 0.");
            }

            _proxyHopLimit = value;
         }
      }

      /// <summary>
      /// Mendaftarkan proxy atau load balancer yang boleh dipercaya saat menyebutkan alamat client
      /// yang sebenarnya. Tanpa pendaftaran ini, request yang datang lewat proxy tercatat beralamat
      /// proxy-nya - bukan alamat pemakainya - karena secara jaringan memang proxy itulah yang
      /// menghubungi server.
      /// <para>
      /// Proxy yang berjalan di mesin yang sama (<c>127.0.0.0/8</c> dan <c>::1</c>) sudah dipercaya
      /// sejak bawaan, jadi susunan "nginx satu mesin dengan API" tidak perlu mendaftarkan apa-apa.
      /// </para>
      /// </summary>
      /// <param name="addresses">
      /// Alamat IP satu per satu (<c>"10.0.0.100"</c>) atau seluruh jaringan dalam notasi CIDR
      /// (<c>"10.0.0.0/8"</c>). Pakai CIDR kalau alamat proxy-nya bisa berubah-ubah, seperti pada
      /// load balancer di lingkungan awan.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau ada nilai yang kosong atau bukan alamat IP maupun CIDR yang bisa dibaca.
      /// Diperiksa di sini, saat pendaftaran, supaya salah ketik ketahuan waktu server start - bukan
      /// waktu request pertama datang dan alamatnya diam-diam salah.
      /// </exception>
      public void TrustProxy(params string[] addresses) {
         foreach (var address in addresses) {
            if (string.IsNullOrWhiteSpace(address)) {
               throw new ArgumentException("A trusted proxy address must not be empty.", nameof(addresses));
            }

            var value = address.Trim();

            if (value.Contains('/')) {
               if (!IPNetwork.TryParse(value, out var network)) {
                  throw new ArgumentException(
                     $"Trusted proxy network '{value}' is not readable CIDR notation, e.g. '10.0.0.0/8'.",
                     nameof(addresses));
               }

               TrustedProxyNetworks.Add(network);
               Logger.LogInformation("Trusted proxy network registered: {Network}", network);
               continue;
            }

            if (!IPAddress.TryParse(value, out var parsed)) {
               throw new ArgumentException($"Trusted proxy address '{value}' is not a readable IP address.",
                  nameof(addresses));
            }

            TrustedProxies.Add(parsed);
            Logger.LogInformation("Trusted proxy registered: {Address}", parsed);
         }
      }

      /// <summary>
      /// Mempercayai alamat client yang disebutkan header <c>X-Forwarded-For</c> dari mana pun
      /// request itu datang, tanpa memeriksa siapa yang meneruskannya.
      /// <para>
      /// Ini membuka penyamaran alamat: header tersebut datang dari pemanggil, jadi begitu tidak ada
      /// yang memeriksanya, siapa pun bisa mengaku beralamat apa saja hanya dengan menempelkan satu
      /// baris header - dan alamat itulah yang masuk ke log. Pantas dipakai hanya kalau API-nya
      /// benar-benar tidak bisa dihubungi selain lewat proxy, misalnya di dalam jaringan container
      /// tertutup yang alamat proxy-nya berganti-ganti sehingga tidak bisa didaftarkan. Selama
      /// alamat proxy-nya diketahui, <see cref="TrustProxy"/> selalu pilihan yang lebih benar.
      /// </para>
      /// </summary>
      public void TrustAnyProxy() {
         TrustAnyProxyAddress = true;
         Logger.LogWarning(
            "Forwarded headers are now trusted from any address. Any caller can claim any client address; register the proxies with '{Method}' instead where their addresses are known.",
            nameof(TrustProxy));
      }

      #endregion

      #region CDN

      /// <summary>
      /// Batas ukuran bawaan - dalam megabyte - untuk satu file yang diunggah ke CDN, dipakai
      /// <see cref="EnableCdn(string)"/> yang tidak menyebutkan angkanya sendiri.
      /// </summary>
      public const int DefaultCdnMaxFileSizeMb = 20;

      // null = CDN mati. Disimpan mentah seperti yang ditulis di Program.cs; resolusinya ke path
      // absolut menunggu BuildApp, karena ContentRootPath baru diketahui di sana.
      internal string? CdnRootPath { get; private set; }

      // Dalam byte, hasil maxFileSizeMb * 1024 * 1024.
      internal long CdnMaxFileSize { get; private set; }

      /// <inheritdoc cref="EnableCdn(string,int)" />
      public void EnableCdn(string rootPath) =>
         EnableCdn(rootPath, DefaultCdnMaxFileSizeMb);

      /// <summary>
      /// Menyalakan CDN: isi folder <paramref name="rootPath"/> disajikan apa adanya di alamat
      /// <c>/cdn/...</c> kepada siapa pun, tanpa login - lengkap dengan daftar isi folder saat dibuka
      /// dari browser dan dukungan unduhan bersegmen/lanjutan (HTTP Range) untuk download manager.
      /// Tanpa panggilan ini, setiap alamat di bawah <c>/cdn</c> dijawab 404.
      /// <para>
      /// Menambah dan menghapus isinya tidak lewat alamat publik itu, melainkan lewat layar CDN
      /// Manager di aplikasi, yang hanya terbuka bagi pengguna yang diberi hak mengelola CDN.
      /// </para>
      /// </summary>
      /// <param name="rootPath">
      /// Folder yang disajikan. Path absolut dipakai apa adanya; path relatif (termasuk <c>./...</c>)
      /// dihitung dari folder konten aplikasi. Foldernya dibuat sendiri saat startup kalau belum ada.
      /// </param>
      /// <param name="maxFileSizeMb">
      /// Batas ukuran satu file yang boleh diunggah lewat CDN Manager, dalam megabyte
      /// (1 MB = 1024 × 1024 byte). File yang sudah ada di folder tetap disajikan berapa pun ukurannya;
      /// yang dibatasi hanya unggahan.
      /// </param>
      /// <exception cref="ArgumentException">Dilempar kalau <paramref name="rootPath"/> kosong.</exception>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Dilempar kalau <paramref name="maxFileSizeMb"/> <c>0</c> atau negatif.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau CDN sudah dinyalakan sebelumnya - satu aplikasi hanya punya satu folder CDN.
      /// </exception>
      public void EnableCdn(string rootPath, int maxFileSizeMb) {
         if (StorageSettingsHostId is not null) throw new InvalidOperationException("Static CDN cannot be combined with managed storage settings.");
         if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new ArgumentException("The CDN root path must not be empty.", nameof(rootPath));
         }

         if (maxFileSizeMb <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeMb), maxFileSizeMb,
               "The CDN maximum file size must be greater than 0 MB.");
         }

         if (CdnRootPath is not null) {
            throw new InvalidOperationException(
               $"The CDN is already enabled for '{CdnRootPath}'; '{nameof(EnableCdn)}' may only be called once.");
         }

         CdnRootPath = rootPath;
         CdnMaxFileSize = maxFileSizeMb * 1024L * 1024L;
         Logger.LogInformation("CDN enabled: {Path} (max upload {Size} MB)", rootPath, maxFileSizeMb);
      }

      #endregion

      /// <summary>
      /// Masa berlaku bawaan - dalam hari - untuk token debug yang didaftarkan tanpa menyebut angkanya.
      /// </summary>
      public const int DefaultDebugTokenDays = 60;

      // Di bawah ini RSA sudah terlalu lemah untuk dipercaya sebagai bukti identitas, dan menolaknya di
      // sini jauh lebih baik daripada menemukannya saat request pertama.
      private const int MinimumDebugKeySizeBits = 2048;

      /// <inheritdoc cref="AddDebugToken(string,string,int)" />
      public void AddDebugToken(string name, string publicKey) =>
         AddDebugToken(name, publicKey, DefaultDebugTokenDays);

      /// <summary>
      /// Mendaftarkan satu public key milik pengembang, sehingga request yang membawa token debug bertanda
      /// tangan key tersebut diterima tanpa password maupun access token. Satu pengembang satu key, supaya
      /// aksesnya bisa dicabut satu-satu: hapus barisnya lalu deploy ulang.
      /// <para>
      /// Yang ditulis di sini hanya public key, jadi aman ikut tersimpan di source. Private key-nya tidak
      /// pernah sampai ke server - ia tinggal di mesin pengembang dan hanya dipakai menandatangani token.
      /// </para>
      /// </summary>
      /// <param name="name">
      /// Nama key, bebas tapi harus unik dan sama persis dengan nama yang dipakai sisi pengembang saat
      /// menandatangani. Nama ini pula yang muncul di log setiap kali token-nya dipakai.
      /// </param>
      /// <param name="publicKey">Public key RSA minimal 2048 bit, berupa Base64 dari DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      /// <param name="days">
      /// Berapa lama sebuah token masih diterima terhitung sejak diterbitkan; <c>-1</c> berarti tanpa batas.
      /// Angka ini tidak membatasi pengembangnya - ia selalu bisa menerbitkan token baru - yang dibatasi
      /// adalah token yang bocor: string yang tertinggal di history Postman, di log, atau di catatan yang
      /// terkirim ke orang lain, mati sendiri setelah lewat batas ini.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau nama atau public key-nya kosong, namanya sudah dipakai, key-nya bukan public key RSA
      /// yang bisa dibaca, ukurannya di bawah 2048 bit, nilainya sama dengan key yang sudah terdaftar, atau
      /// <paramref name="days"/> di luar nilai yang diizinkan.
      /// </exception>
      public void AddDebugToken(string name, string publicKey, int days) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Debug token key name must not be empty.", nameof(name));
         }

         if (string.IsNullOrWhiteSpace(publicKey)) {
            throw new ArgumentException($"Debug token key '{name}' has an empty public key.", nameof(publicKey));
         }

         if (DebugTokenKeys.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ArgumentException($"Debug token key '{name}' is already registered.", nameof(name));
         }

         // Dua nama dengan key yang sama membuat log tidak bisa dipercaya: tanda tangannya cocok untuk
         // keduanya, jadi nama yang tercatat belum tentu nama pemakainya.
         if (DebugTokenKeys.FirstOrDefault(r => string.Equals(r.Key.PublicKey, publicKey, StringComparison.Ordinal))
             is { } duplicate) {
            throw new ArgumentException(
               $"Debug token key '{name}' uses the same public key as '{duplicate.Name}'. Every key must be unique.",
               nameof(publicKey));
         }

         // 0 mematikan key-nya diam-diam: setiap token yang diterbitkan langsung kedaluwarsa. Kalau memang
         // ingin mati, barisnya yang dihapus - jauh lebih jelas dibaca daripada angka yang tidak kelihatan.
         if (days == 0) {
            throw new ArgumentException(
               $"Debug token key '{name}' cannot use 'days' = 0; remove the registration instead, or pass -1 for no expiry.",
               nameof(days));
         }

         if (days < -1) {
            throw new ArgumentException(
               $"Debug token key '{name}' has an invalid 'days' value of {days}; it must be -1 (no expiry) or greater than 0.",
               nameof(days));
         }

         DebugTokenKeys.Add(new DebugTokenKey(name, ImportDebugPublicKey(name, publicKey), days));
         Logger.LogInformation("Debug token key registered: {Name} (expires after {Days} day(s))", name,
            days < 0 ? "unlimited" : days.ToString());
      }

      /// <summary>
      /// Membaca public key sekali di sini, saat pendaftaran, supaya key yang salah ketik atau rusak
      /// ketahuan waktu server start - bukan waktu request pertama datang.
      /// </summary>
      private static RsaKeyPair ImportDebugPublicKey(string name, string publicKey) {
         byte[] raw;
         try {
            raw = Convert.FromBase64String(publicKey);
         }
         catch (FormatException x) {
            throw new ArgumentException(
               $"Debug token key '{name}' is not valid Base64 text. It must be Base64 of a DER PKCS#1 RSA public key.",
               nameof(publicKey), x);
         }

         using var rsa = RSA.Create();
         try {
            rsa.ImportRSAPublicKey(raw, out _);
         }
         catch (CryptographicException x) {
            throw new ArgumentException(
               $"Debug token key '{name}' is not a readable RSA public key. Note that what belongs here is the public key, not the private one.",
               nameof(publicKey), x);
         }

         if (rsa.KeySize < MinimumDebugKeySizeBits) {
            throw new ArgumentException(
               $"Debug token key '{name}' is only {rsa.KeySize} bits; at least {MinimumDebugKeySizeBits} bits are required.",
               nameof(publicKey));
         }

         return RsaKeyPair.Create(publicKey);
      }

      /// <summary>
      /// Registers a scoped DbContext against the <see cref="DefaultConnectionName"/> connection - the
      /// one configured through <see cref="SetDbProvider"/> - modules never call UseSqlServer/UseMySql
      /// themselves. Equivalent to <see cref="AddDbContext{TContext}(string)"/> with
      /// <see cref="DefaultConnectionName"/>.
      /// <para>
      /// Do not also call <see cref="AddDbContextFactory{TContext}()"/> for the same <typeparamref name="TContext"/> -
      /// EF Core cannot resolve a scoped DbContext registration and a factory registration for the same context type
      /// at once (the factory needs its options resolvable from the root provider, which conflicts with the scoped
      /// registration and throws "Cannot resolve scoped service ... from root provider"). If concurrent DbContext
      /// instances are needed, use <see cref="AddDbContextFactory{TContext}()"/> alone instead - it also registers the
      /// scoped ambient context.
      /// </para>
      /// </summary>
      public void AddDbContext<TContext>() where TContext : DbContext => AddDbContext<TContext>(DefaultConnectionName);

      /// <summary>
      /// Registers a scoped DbContext against the named connection - one registered through
      /// <see cref="AddExtraDbConn"/>, or <see cref="DefaultConnectionName"/> for the one configured
      /// through <see cref="SetDbProvider"/>. Modules never call UseSqlServer/UseMySql themselves; the
      /// provider and connection string come from whatever <paramref name="connectionName"/> resolves to.
      /// <para>
      /// Registration order does not matter: <paramref name="connectionName"/> is only looked up once every
      /// builder callback has finished, so a module may call this before <see cref="AddExtraDbConn"/> has
      /// registered the name it asks for.
      /// </para>
      /// <para>
      /// Do not also call <see cref="AddDbContextFactory{TContext}()"/> for the same <typeparamref name="TContext"/> -
      /// see the parameterless overload's remarks for why combining them breaks DI resolution.
      /// </para>
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Thrown when <see cref="EmApp.BuildApp"/> applies the registrations - not from this call itself -
      /// if <paramref name="connectionName"/> turns out not to be a registered connection; the message names
      /// <typeparamref name="TContext"/>, the requested name, and the connections that are actually registered.
      /// </exception>
      public void AddDbContext<TContext>(string connectionName) where TContext : DbContext {
         DbContextConnectionNames[typeof(TContext)] = connectionName;
         DbContextRegistrations.Add((services, connections) => {
            var info = ResolveConnection(connections, typeof(TContext), connectionName);
            services.AddDbContext<TContext>(opt => opt.UseEmProvider(info.Provider, info.ConnectionString));
         });
      }

      /// <summary>
      /// Registers an <see cref="IDbContextFactory{TContext}"/> for <typeparamref name="TContext"/> against
      /// the <see cref="DefaultConnectionName"/> connection, for actions that need to run multiple queries
      /// concurrently (e.g. via Task.WhenAll) - each concurrent query needs its own DbContext instance
      /// created from the factory, since a single DbContext instance does not support concurrent operations.
      /// Also registers a scoped <typeparamref name="TContext"/> created from the factory, so the ambient
      /// per-request context (e.g. <see cref="ServicesBase.GetService{T}()"/>) keeps working. Do not
      /// additionally call <see cref="AddDbContext{TContext}()"/> for the same <typeparamref name="TContext"/> -
      /// see that method's remarks for why combining them breaks DI resolution.
      /// </summary>
      internal void AddDbContextFactory<TContext>() where TContext : DbContext =>
         AddDbContextFactory<TContext>(DefaultConnectionName);

      /// <inheritdoc cref="AddDbContextFactory{TContext}()" />
      /// <param name="connectionName">
      /// The connection to configure the factory against - one registered through
      /// <see cref="AddExtraDbConn"/>, or <see cref="DefaultConnectionName"/> for the one configured
      /// through <see cref="SetDbProvider"/>.
      /// </param>
      internal void AddDbContextFactory<TContext>(string connectionName) where TContext : DbContext {
         DbContextConnectionNames[typeof(TContext)] = connectionName;
         DbContextRegistrations.Add((services, connections) => {
            var info = ResolveConnection(connections, typeof(TContext), connectionName);
            services.AddDbContextFactory<TContext>(opt => opt.UseEmProvider(info.Provider, info.ConnectionString));
            services.AddScoped<TContext>(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());
         });
      }

      /// <summary>
      /// Registers <see cref="IEmDbConnectionFactory"/> plus a scoped, keyed <see cref="IDbConnection"/> for
      /// every registered connection (key = connection name), general-purpose ADO.NET infrastructure (e.g. for
      /// Dapper) - not tied to any one module, so modules don't call this themselves. The non-keyed
      /// <see cref="IDbConnection"/> only forwards to the keyed <see cref="DefaultConnectionName"/> entry, so
      /// a request that asks for the connection either way - <c>GetService&lt;IDbConnection&gt;()</c> or
      /// <c>GetService&lt;IDbConnection&gt;("Default")</c> - shares one connection instead of opening two to
      /// the same database. Called once automatically by <see cref="EmApp.BuildApp"/>.
      /// </summary>
      internal void RegisterDbConnection() {
         DbContextRegistrations.Add((services, connections) => {
            services.AddSingleton<IEmDbConnectionFactory>(_ => new EmDbConnectionFactory(connections));

            foreach (var name in connections.Keys) {
               services.AddKeyedScoped<IDbConnection>(name,
                  (sp, key) => sp.GetRequiredService<IEmDbConnectionFactory>().Create((string)key!));
            }

            services.AddScoped<IDbConnection>(sp =>
               sp.GetRequiredKeyedService<IDbConnection>(DefaultConnectionName));
         });
      }

      /// <summary>
      /// Registers a transient <see cref="IDbCommand"/> created from the app's scoped <see cref="IDbConnection"/>.
      /// Transient (not scoped) because CommandText/Parameters are mutable per-call state - sharing one instance
      /// across a whole request would let unrelated queries stomp on each other. Called once automatically by
      /// <see cref="EmApp.BuildApp"/>, after <see cref="RegisterDbConnection"/>.
      /// </summary>
      internal void RegisterDbCommand() {
         DbContextRegistrations.Add((services, _) => {
            services.AddTransient<IDbCommand>(sp => sp.GetRequiredService<IDbConnection>().CreateCommand());
         });
      }

      /// <summary>
      /// Looks up <paramref name="connectionName"/> in the registry, or throws with a message naming
      /// <paramref name="contextType"/>, the requested name, and the connections that are actually
      /// registered. Shared by every <c>AddDbContext</c>/<c>AddDbContextFactory</c> overload so a typo'd
      /// name is caught the same way regardless of which one was called.
      /// </summary>
      private static DbConnectionInfo ResolveConnection(IReadOnlyDictionary<string, DbConnectionInfo> connections,
         Type contextType, string connectionName) {
         if (connections.TryGetValue(connectionName, out var info)) {
            return info;
         }

         throw new InvalidOperationException(
            $"'{contextType.Name}' asked for database connection '{connectionName}', but no connection with that name is registered. Registered connections: {EmDbConnectionFactory.DescribeKnownConnections(connections.Keys)}.");
      }

      /// <summary>
      /// Membaca penanda action dari sebuah method: HTTP method-nya beserta apakah action itu publik.
      /// Atribut dicari langsung di method, lalu - kalau tidak ada - di method interface yang diimplementasikannya.
      /// Mengembalikan <c>null</c> kalau method tersebut bukan action.
      /// </summary>
      private static ActionMarker? GetActionMarker(Type serviceType, MethodInfo method) {
         if (ReadActionMarker(method) is { } marker) {
            return marker;
         }

         if (serviceType.IsInterface) {
            return null;
         }

         foreach (var interfaceType in serviceType.GetInterfaces()) {
            var interfaceMap = serviceType.GetInterfaceMap(interfaceType);

            for (var index = 0; index < interfaceMap.TargetMethods.Length; index++) {
               if (interfaceMap.TargetMethods[index] != method) {
                  continue;
               }

               if (ReadActionMarker(interfaceMap.InterfaceMethods[index]) is { } interfaceMarker) {
                  return interfaceMarker;
               }
            }
         }

         return null;
      }

      private static ActionMarker? ReadActionMarker(MethodInfo method) {
         if (method.GetCustomAttribute<GetActionAttribute>(inherit: true) is { } getAction) {
            return new ActionMarker(HttpMethod.Get, getAction.IsPublicAction, getAction.RequestTimeout,
               getAction.Claim);
         }

         if (method.GetCustomAttribute<PostActionAttribute>(inherit: true) is { } postAction) {
            // Tidak ada batas waktu untuk POST: engine tidak pernah memutus action tulis, dan
            // [PostAction] memang tidak menyediakan cara menyebutkannya.
            return new ActionMarker(HttpMethod.Post, postAction.IsPublicAction, null, postAction.Claim);
         }

         return null;
      }

      private readonly record struct ActionMarker(
         HttpMethod HttpMethod,
         bool IsPublicAction,
         TimeSpan? RequestTimeout,
         string? RequiredClaim);

      private static MethodInfo[] GetActionableMethods(Type serviceType) {
         return serviceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(r => !r.IsSpecialName)
            .Where(r => GetActionMarker(serviceType, r) is not null)
            .ToArray();
      }

      private void RegisterActions(Type concreteType, Type serviceType, string moduleName,
         MethodInfo[] actionableMethods, bool enforceClaims) {
         foreach (var method in actionableMethods) {
            var marker = GetActionMarker(concreteType, method)!.Value;
            EnsureAsyncAction(method, serviceType);
            var (streamIndex, streamPayloadIndex) = ResolveStreamParameters(method, serviceType, marker.HttpMethod);
            var action = new ActionDefinition {
               StreamParameterIndex = streamIndex,
               StreamPayloadParameterIndex = streamPayloadIndex,
               // Only the exact Task<Stream> shape; EnsureAsyncAction above refuses stream subtypes.
               ReturnsStream = method.ReturnType == typeof(Task<Stream>),
               HttpMethod = marker.HttpMethod,
               IsPublicAction = marker.IsPublicAction,
               RequestTimeout = marker.RequestTimeout,
               RequiredClaim = marker.RequiredClaim,
               EnforcesClaims = enforceClaims,
               ActionName = method.Name,
               Module = moduleName,
               MethodInfo = method,
               Type = serviceType
            };
            var existingMethod = ActionDefinitions.SingleOrDefault(r =>
               string.Equals(r.Module, moduleName, StringComparison.InvariantCultureIgnoreCase) &&
               string.Equals(r.ActionName, action.ActionName, StringComparison.InvariantCultureIgnoreCase));
            if (existingMethod != null)
               throw new InvalidOperationException(
                  $"Duplicate action name '{existingMethod.ActionName}' found in module '{moduleName}' ('{existingMethod.Type.FullName}'). Action names must be unique within a module.");

            ActionDefinitions.Add(action);
         }
      }

      /// <summary>
      /// Checks the shape of an action that takes a <see cref="Stream"/>: POST only, exactly one stream
      /// parameter, and at most one other parameter - the payload that travels in
      /// <see cref="Defaults.StreamPayloadHeader"/>. Returns the positions of both, or two nulls for an
      /// action without a stream. A wrong shape fails startup instead of the first request.
      /// </summary>
      private static (int? StreamIndex, int? PayloadIndex) ResolveStreamParameters(MethodInfo method,
         Type serviceType, HttpMethod httpMethod) {
         var parameters = method.GetParameters();
         var streamIndexes = parameters
            .Where(r => r.ParameterType == typeof(Stream))
            .Select(r => r.Position)
            .ToArray();

         if (streamIndexes.Length == 0) {
            return (null, null);
         }

         var actionLabel = $"{serviceType.FullName}.{method.Name}";

         if (httpMethod != HttpMethod.Post) {
            throw new InvalidOperationException(
               $"Action '{actionLabel}' takes a Stream parameter, which is only supported on [PostAction].");
         }

         if (streamIndexes.Length > 1) {
            throw new InvalidOperationException(
               $"Action '{actionLabel}' takes {streamIndexes.Length} Stream parameters; a streamed action takes exactly one.");
         }

         if (parameters.Length > 2) {
            throw new InvalidOperationException(
               $"Action '{actionLabel}' takes a Stream and {parameters.Length - 1} other parameters; a streamed action takes at most one other parameter, sent in the '{Defaults.StreamPayloadHeader}' header. Wrap them in one payload class.");
         }

         var streamIndex = streamIndexes[0];
         int? payloadIndex = parameters.Length == 2 ? 1 - streamIndex : null;
         return (streamIndex, payloadIndex);
      }

      private void EnsureAsyncAction(MethodInfo method, Type serviceType) {
         var returnType = method.ReturnType;
         var isTask = typeof(Task).IsAssignableFrom(returnType);
         if (!isTask) {
            throw new InvalidOperationException(
               $"Action '{serviceType.FullName}.{method.Name}' must return Task or Task<T>.");
         }

         // A stream subtype would otherwise be serialised into the JSON envelope instead of sent as
         // content - fail at startup rather than on the first download.
         if (returnType.IsGenericType && returnType.GetGenericArguments()[0] is var resultType &&
             resultType != typeof(Stream) && typeof(Stream).IsAssignableFrom(resultType)) {
            throw new InvalidOperationException(
               $"Action '{serviceType.FullName}.{method.Name}' returns Task<{resultType.Name}>; declare it as Task<Stream> to send the content as a download.");
         }
      }
   }
}