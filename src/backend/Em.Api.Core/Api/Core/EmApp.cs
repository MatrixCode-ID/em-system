using System.Reflection;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Em;
using Em.Api.Core.Approval;
using Em.Api.Core.Hub;
using Em.Api.Core.Models;
using Em.Api.Core.Registry;
using Em.Api.Core.Storage;
using Em.Api.Shared;
using Em.Shared;
using HttpMethod = Em.Shared.HttpMethod;

namespace Em.Api.Core
{
   public class EmApp : IEmApp
   {
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> appBuilder) {
         var app = new EmApp(WebApplication.CreateBuilder(args));
         // Satu baris per entri, berjam. Bawaan console ASP.NET Core memecah setiap entri jadi dua
         // baris, dan jejak request yang datang beruntun jadi sulit dibaca berpasang-pasangan.
         app.Builder.Logging.AddSimpleConsole(options => {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
         });
         app.Services.AddHttpContextAccessor();
         app.Services.AddSingleton(app);
         app.Services.AddSingleton<IEmApp>(app);
         var builder = new EmAppBuilder() {
            Services = app.Services
         };
         InitInternalServices(builder);
         builder.AddDbContextFactory<ApiCoreContext>();
         builder.AddDbContext<RobotContext>();
         builder.RegisterDbConnection();
         builder.RegisterDbCommand();
         appBuilder(builder);
         app.ApplyProxyHeaderRegistration(builder);
         app.ApplyDbContextRegistrations(builder);
         app.ApplyStorageSettingsRegistration(builder);
         app.ApplyNuPakRegistration(builder);
         app.ApplyCdnRegistration(builder);
         app.ApplyContainerRegistryRegistration(builder);
         app.ApplyBusinessTaskRegistration(builder);
         app.ApplyBinaryStorageRegistration(builder);
         app.ApplyApprovalRegistration(builder);
         app._publicEndpoints = builder.PublicEndpoints.ToArray();
         app._actions = builder.ActionDefinitions.ToArray();
         // Beku sejak di sini, sama seperti _actions: public key pengembang hanya boleh datang dari
         // Program.cs, tidak pernah dari database, dan tidak pernah berubah selama server hidup.
         app._debugTokenKeys = builder.DebugTokenKeys.ToArray();
         // Beku juga: katalog claim hanya boleh datang dari Program.cs lewat AddClaims, dan tidak
         // pernah berubah selama server hidup.
         app._claims = builder.ClaimActions.ToArray();
         EnsureClaimModulesAreRegistered(app._claims, builder.ActionDefinitions);
         app._firstTimeAdminPassword = builder.FirstTimeAdminPassword;
         app.SessionTokenRetentionHour = builder.SessionTokenRetentionHour;
         app._httpRequestTimeout = builder.HttpRequestTimeout;
         app._rateLimiter = CreateRateLimiter(builder.ActionRateLimit);
         // Stack trace hanya dikirim ke client saat Development. Di luar itu detail internal (path file, nama
         // assembly, struktur query) tidak perlu diketahui pemanggil, terautentikasi maupun tidak.
         app._includeStackTrace = app.Builder.Environment.IsDevelopment();
         // Membangun host di sini, bukan di Run, supaya jendela registrasi service persis sehabis
         // callback builder selesai: sesudah baris ini service collection sudah terkunci dan
         // ServiceProvider dijamin tersedia untuk siapa pun yang memegang app.
         app._webApplication = app.Builder.Build();
         app._rootServiceProvider = app._webApplication.Services;
         app._httpContextAccessor = app._rootServiceProvider.GetRequiredService<IHttpContextAccessor>();
         return app;
      }

      /// <summary>
      /// Mendaftarkan service inti bawaan SDK yang selalu tersedia sebelum callback module dijalankan.
      /// Semua service milik <c>Em.Api.Core</c> didaftarkan di sini supaya terkumpul di satu tempat.
      /// </summary>
      private static void InitInternalServices(EmAppBuilder builder) {
         builder.Services.AddSingleton<IStringHasher, Argon2Hashing>();

         // Every credential type the engine knows is registered here, one entry each. What is
         // registered is the way to build one, not the credential itself: a credential belongs to a
         // user, so it can only be built once there is a user to build it for.
         builder.Services.AddSingleton<ICredentialProviderFactory, PasswordCredentialFactory>();

         // Registered by hand rather than through AddService: that method is for module services,
         // and it refuses anything without a [Module] attribute and at least one action - which the
         // token service, having no action at all, would silently fail.
         builder.Services.AddScoped<ITokenServices, TokenServices>();

         // Scoped, jadi satu request = satu objek: scope-nya adalah scope milik request, dan objeknya
         // immutable sehingga tidak ada yang bisa saling menimpa. Yang diresolve saat action berjalan
         // selalu mendapat yang sudah terisi, karena gerbang berjalan lebih dulu; di luar request yang
         // keluar adalah ActionRequest.None, yang menolak setiap pemeriksaan hak dengan 401. Scope milik
         // business task tidak punya request, jadi di sana yang keluar adalah pemulai task-nya, dibawa
         // lewat BusinessTaskStarter.
         builder.Services.AddScoped<BusinessTaskStarter>();
         builder.Services.AddScoped(sp =>
            sp.GetRequiredService<BusinessTaskStarter>().Request ??
            sp.GetRequiredService<EmApp>().CurrentRequest ??
            ActionRequest.None);

         // enforceClaims: false - ketiganya bukan module, dan aksi sensitifnya sudah dijaga pemeriksaan
         // yang lebih tepat daripada "claim apa pun di module ini": hak atas diri sendiri untuk membaca
         // data sendiri, hak administrator untuk tulis yang sensitif. Alasan lengkapnya di overload
         // internal AddService yang menerima parameter itu.
         builder.AddService<IEmApiCoreServices, ApiCoreServices>(enforceClaims: false);
         builder.AddService<IContactServices, ContactServices>(enforceClaims: false);
         builder.AddService<ICredentialServices, CredentialServices>(enforceClaims: false);

         // enforceClaims: false for a different reason than the three above: every CDN action names
         // its claim explicitly, and an explicit claim is enforced regardless. What is dropped is only
         // the "any claim in this module" default, which would add nothing on top of that.
         builder.AddService<ICdnServices, CdnServices>(enforceClaims: false);

         // enforceClaims: false for the same reason as the CDN: every action names its claim explicitly
         // (one claim, Container Manager Access). The service answers 404 itself while the registry is off.
         builder.AddService<ICtnServices, CtnServices>(enforceClaims: false);
         // Robot management belongs to User Manager and works without container registry.
         builder.AddService<IRobotServices, RobotServices>(enforceClaims: false);

         // enforceClaims: false for the reason of the first three: the task hub is every signed-in user's
         // own, and its id-based actions check owner or administrator themselves. The manager-only actions
         // name their claim explicitly, which is enforced regardless.
         builder.AddService<IBusinessTaskServices, BusinessTaskServices>(enforceClaims: false);

         // enforceClaims: false because the claim that applies here is not this module's at all: it
         // belongs to the module owning the document being looked at, and which document that is only
         // becomes known once the request has been read. Every action checks it itself.
         builder.AddService<IApprovalServices, ApprovalServices>(enforceClaims: false);

         // Scoped because it answers for the caller of the request it runs in. The approval hub source
         // goes through the same builder method a module's own source does.
         builder.Services.AddScoped<IApprovalHubQuery, ApprovalHubQuery>();
         builder.AddHubTaskSource<ApprovalHubTaskSource>();

         // Scoped for the same reason: it acts for the caller of the request it runs in, and hands that
         // caller on to the handlers of the module.
         builder.Services.AddScoped<IApprovalEngine, ApprovalEngine>();

         // Stateless (its font setup is guarded by a lock), so one instance serves every request. Without it
         // the PDF action returns the base PDF as it was frozen, and the calibration action answers 501.
         builder.Services.AddSingleton<IApprovalPdfRenderer, Em.Api.Core.Approval.Pdf.ApprovalPdfRenderer>();
      }

      /// <summary>
      /// Menyiapkan penyimpanan isi berkas sesuai <c>EmAppBuilder.AddLocalBinaryStorage</c>: path-nya
      /// dijadikan absolut (relatif dihitung dari folder konten aplikasi, sama seperti CDN) dan
      /// foldernya dibuat kalau belum ada. Kalau aplikasi tidak menyalakannya, tidak ada yang
      /// didaftarkan - dan pemakainya gagal saat meminta <see cref="IBinaryStorage"/>, bukan saat
      /// menulis berkas pertamanya.
      /// </summary>
      private void ApplyBinaryStorageRegistration(EmAppBuilder builder) {
         if (builder.BinaryStorageRootPath is not { } configuredPath) return;

         var rootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Builder.Environment.ContentRootPath, configuredPath));
         Directory.CreateDirectory(rootPath);
         Services.AddSingleton<IBinaryStorage>(new LocalBinaryStorage(rootPath));
      }

      /// <summary>
      /// Membekukan seluruh alur persetujuan yang didaftarkan module, lalu mendaftarkannya sebagai satu
      /// katalog. Selalu didaftarkan - juga saat tidak ada satu alur pun - supaya layar dan action
      /// approval selalu bisa dibuat dan menjawab sendiri bahwa tidak ada apa-apa.
      /// </summary>
      private void ApplyApprovalRegistration(EmAppBuilder builder) {
         ApprovalStartupChecks.VerifyDatabases(builder.ApprovalFlows, builder);
         Services.AddSingleton(new ApprovalRegistry(builder.ApprovalFlows));
      }

      /// <summary>
      /// Menyiapkan pembacaan header <c>X-Forwarded-*</c>, yaitu cara sebuah proxy atau load balancer
      /// memberi tahu alamat client yang sebenarnya. Tanpa ini setiap request yang lewat proxy tercatat
      /// beralamat proxy-nya, sehingga log maupun pemeriksaan berbasis alamat kehilangan artinya.
      /// </summary>
      /// <remarks>
      /// Yang dipercaya hanya proxy yang memang didaftarkan lewat <c>EmAppBuilder.TrustProxy</c>,
      /// ditambah loopback yang sudah dipercaya sejak bawaan. Itu disengaja: header ini datang dari
      /// pemanggil, jadi kalau siapa pun boleh mengirimnya, siapa pun juga bisa mengaku beralamat apa
      /// saja hanya dengan menempelkan satu baris header.
      /// </remarks>
      private void ApplyProxyHeaderRegistration(EmAppBuilder builder) {
         Services.Configure<ForwardedHeadersOptions>(options => {
            // Bawaannya None - middleware-nya menyala tapi tidak membaca apa-apa - jadi header yang
            // hendak dibaca harus disebut sendiri di sini.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = builder.ProxyHopLimit < 0 ? null : builder.ProxyHopLimit;

            if (builder.TrustAnyProxyAddress) {
               options.KnownProxies.Clear();
               options.KnownIPNetworks.Clear();
               return;
            }

            foreach (var address in builder.TrustedProxies) {
               options.KnownProxies.Add(address);
            }

            foreach (var network in builder.TrustedProxyNetworks) {
               options.KnownIPNetworks.Add(network);
            }
         });
      }

      /// <summary>
      /// Menyiapkan CDN sesuai <c>EmAppBuilder.EnableCdn</c>: path-nya dijadikan absolut (relatif
      /// dihitung dari folder konten aplikasi) dan foldernya dibuat kalau belum ada. Batas ukuran body
      /// request server tidak disentuh: unggahan datang sebagai stream, dan batas ukuran CDN ditegakkan
      /// sendiri saat stream itu disalin. Saat CDN mati tetap didaftarkan sebuah store yang mati, supaya
      /// service pengelolanya selalu bisa dibuat dan menjawab 404 sendiri.
      /// </summary>
      private void ApplyCdnRegistration(EmAppBuilder builder) {
         var config = _storageSettings.Active(true);
         if (!config.Enabled) {
            _cdn = CdnStore.Disabled;
            Services.AddSingleton(_cdn);
            return;
         }

         _cdn = CdnStore.Create(config.Directory, Builder.Environment.ContentRootPath, config.MaxUploadMb * 1024L * 1024L);
         Services.AddSingleton(_cdn);
      }

      /// <summary>
      /// Menyiapkan container registry sesuai <c>EmAppBuilder.AddContainerRegistry</c>: folder
      /// penyimpanan blob dijadikan absolut (relatif dihitung dari folder konten aplikasi) dan dibuat
      /// kalau belum ada. Seperti CDN, saat registry mati tetap didaftarkan store yang mati supaya
      /// <c>CtnServices</c> selalu bisa dibuat dan menjawab 404 sendiri.
      /// </summary>
      private void ApplyContainerRegistryRegistration(EmAppBuilder builder) {
         _ctnStore = _storageSettings.Active(false).Enabled
            ? CtnBlobStore.Create(_storageSettings.Active(false).Directory, Builder.Environment.ContentRootPath)
            : CtnBlobStore.Disabled;
         Services.AddSingleton(_ctnStore);
      }

      /// <summary>
      /// Menyiapkan penjalan business task: folder cache-nya dijadikan absolut (relatif dihitung dari
      /// folder konten aplikasi, sama seperti CDN) dan didaftarkan sebagai singleton. Isinya baru dimuat di
      /// <see cref="Run"/>, karena membaca batasnya butuh database.
      /// </summary>
      private void ApplyBusinessTaskRegistration(EmAppBuilder builder) {
         if (string.IsNullOrWhiteSpace(builder.BusinessTaskCachePath)) {
            throw new InvalidOperationException(
               $"'{nameof(EmAppBuilder.BusinessTaskCachePath)}' must not be empty.");
         }

         if (builder.BusinessTaskDefaultLimit is not { Limit: >= 1 } defaultLimit) {
            throw new InvalidOperationException(
               $"'{nameof(EmAppBuilder.BusinessTaskDefaultLimit)}' must allow at least one task.");
         }

         var cachePath = Path.GetFullPath(Path.IsPathRooted(builder.BusinessTaskCachePath)
            ? builder.BusinessTaskCachePath
            : Path.Combine(Builder.Environment.ContentRootPath, builder.BusinessTaskCachePath));
         _businessTasks = new BusinessTaskRunner(cachePath, builder.BusinessTaskGracePeriod, defaultLimit);
         Services.AddSingleton(_businessTasks);
      }

      private void ApplyDbContextRegistrations(EmAppBuilder builder) {
         if (string.IsNullOrWhiteSpace(builder.ConnectionString)) {
            throw new InvalidOperationException(
               $"Database connection string was not configured. Call '{nameof(EmAppBuilder.SetDbProvider)}' inside the '{nameof(BuildApp)}' builder callback before using the app.");
         }

         foreach (var register in builder.DbContextRegistrations) {
            register(Services, builder.DbConnections);
         }
      }

      private (string Prefix, Microsoft.AspNetCore.Http.RequestDelegate Handler)[] _publicEndpoints = [];

      public void Run() {
         var app = _webApplication;
         SeedCoreMetadata();
         if (_ctnStore.IsEnabled || _storageSettings.Managed) CtnStartupChecks.VerifyTables(_rootServiceProvider);
         if (_storageSettings.Managed && _ctnStore.IsEnabled) {
            using var scope = _rootServiceProvider.CreateScope();
            RegistryStorageIntegrity.CheckStartup(
               scope.ServiceProvider.GetRequiredService<CtnContext>(), _ctnStore,
               scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<CtnBlobStore>());
         }
         // Before the first request, like the seeding above: results of the previous run are loaded and
         // leftovers wiped before anyone can ask for them. On shutdown every live task is cancelled.
         _businessTasks.Initialize(_rootServiceProvider);
         app.Lifetime.ApplicationStopping.Register(_businessTasks.Stop);
         // Paling depan di pipeline, karena segala yang membaca alamat pemanggil - jejak request,
         // gerbang identitas, action mana pun di belakangnya - harus sudah melihat alamat yang benar.
         app.UseForwardedHeaders();
         MapCdn(app);
         MapContainerRegistry(app);
         foreach (var endpoint in _publicEndpoints) {
            app.Map(endpoint.Prefix, branch => branch.Run(endpoint.Handler));
         }
         // Called explicitly, and after the CDN: WebApplication otherwise puts routing first, the
         // fallback below then claims every extension-less path, and the static-file and directory
         // middlewares step aside for any request that already has an endpoint - /cdn/ included.
         app.UseRouting();
         app.MapMethods("/api/{module}/{action}", ["GET", "POST"], ProcessRequest);
         app.MapFallback(() => "Welcome to PT. EM ASIA API! This path is empty!");
         app.Run();
      }
      
      /// <summary>
      /// Memasang jalur publik <c>/cdn</c>: file dan daftar isi folder, tanpa identitas dan tanpa
      /// jatah request - keduanya milik dispatcher, dan jalur ini sengaja di luarnya. Dipasang sebelum
      /// fallback, yang kalau tidak akan menjawab 200 untuk alamat apa pun di bawah <c>/cdn</c>.
      /// </summary>
      /// <remarks>
      /// Range, <c>If-Range</c>, <c>ETag</c>, <c>HEAD</c>, 206 dan 416 semuanya ditangani
      /// <c>StaticFileMiddleware</c>; tidak ada kode Range buatan sendiri di sini. Tanpa rate limit
      /// karena download manager membuka banyak koneksi Range sekaligus untuk satu file.
      /// </remarks>
      private void MapCdn(WebApplication app) {
         if (_cdn.IsEnabled) {
            // ExclusionFilters.Sensitive (the default) hides dot-prefixed, hidden and system entries,
            // which is also what keeps an upload's temporary file off the public side.
            var provider = new PhysicalFileProvider(_cdn.RootPath);
            app.UseStaticFiles(new StaticFileOptions {
               RequestPath = CdnStore.PublicRequestPath,
               FileProvider = provider,
               // A CDN is not a web site: .msi, .apk, .7z and anything else must download too.
               ServeUnknownFileTypes = true,
               DefaultContentType = "application/octet-stream"
            });
            app.UseDirectoryBrowser(new DirectoryBrowserOptions {
               RequestPath = CdnStore.PublicRequestPath,
               FileProvider = provider,
               Formatter = new CdnDirectoryFormatter()
            });
         }

         // Whatever is left under /cdn - a missing path while the CDN is on, everything while it is
         // off - is a plain 404, not an ActionResult envelope: this is not an API action.
         app.Map(CdnStore.PublicRequestPath, branch => branch.Run(ctx => {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
         }));
      }

      /// <summary>
      /// Memasang jalur <c>/v2</c> container registry, di luar dispatcher action seperti <c>/cdn</c>:
      /// tidak terkena <c>HttpRequestTimeout</c> maupun <c>ActionRateLimit</c> - layer besar dan banyak
      /// koneksi paralel adalah pemakaian normalnya. Autentikasinya robot (Basic), diperiksa di
      /// <see cref="CtnRegistryEndpoint"/>. Saat registry mati jawabannya 404 polos.
      /// </summary>
      private void MapContainerRegistry(WebApplication app) {
         app.Map(CtnBlobStore.PublicRequestPath, branch => branch.Run(CtnRegistryEndpoint.HandleAsync));
      }

      /// <summary>
      /// Mengisi nilai awal metadata inti sebelum request pertama dilayani. Sejauh ini yang disemai
      /// hanya milik akun administrator bawaan - tanpa itu database yang baru dibuat tidak punya
      /// satu pun akun yang bisa dipakai masuk.
      /// </summary>
      private void SeedCoreMetadata() {
         // A scope of its own: the context is scoped, and outside a request there is no scope to
         // borrow. Run blocking on purpose - nothing may serve a request before this has finished.
         using var scope = _rootServiceProvider.CreateScope();
         var ctx = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
         var hasher = scope.ServiceProvider.GetRequiredService<IStringHasher>();
         AdminAccount.SeedAsync(ctx, hasher, _firstTimeAdminPassword).GetAwaiter().GetResult();
      }

      #region Fields and Properties

      internal WebApplicationBuilder Builder { get; }

      // Registrasi service hanya boleh terjadi selama BuildApp. Setelah host dibangun, koleksi ini
      // dikunci oleh ASP.NET Core, jadi ia sengaja tidak ikut dibuka lewat IEmApp.
      internal IServiceCollection Services => Builder.Services;

      private WebApplication _webApplication = null!;

      private IServiceProvider _rootServiceProvider = null!;

      private IHttpContextAccessor _httpContextAccessor = null!;

      /// <inheritdoc />
      /// <remarks>
      /// EmApp hidup sebagai singleton, jadi provider-nya sengaja dibaca ulang setiap kali properti
      /// ini diakses, bukan disimpan sekali di field. Selama sebuah request berjalan yang dipakai
      /// adalah provider milik request tersebut, sehingga service scoped ikut siklus hidup request
      /// itu. Di luar request yang dipakai adalah provider akar, dan di sana service scoped memang
      /// tidak bisa diresolve - untuk kebutuhan semacam itu buat scope sendiri lewat
      /// <c>ServiceProvider.CreateScope()</c>.
      /// </remarks>
      public IServiceProvider ServiceProvider =>
         _httpContextAccessor.HttpContext?.RequestServices ?? _rootServiceProvider;

      // Kunci tempat gerbang menyimpan keterangan request di HttpContext.Items. Private: satu-satunya
      // yang menulis adalah ProcessRequest, dan yang membaca cukup lewat CurrentRequest di bawah.
      private const string RequestItemKey = "Em.ActionRequest";

      /// <summary>
      /// Keterangan request yang sedang dikerjakan, atau <c>null</c> kalau sedang tidak ada request -
      /// startup, penyemaian, pekerjaan latar. Read-only: satu-satunya yang mengisinya adalah gerbang
      /// di <see cref="ProcessRequest"/>, dan <see cref="ActionRequest"/> sendiri immutable.
      /// </summary>
      /// <remarks>
      /// Ini bukan jalur untuk module. Service module membacanya lewat <c>ServicesBase.Request</c>,
      /// dan kelas pembantu memintanya di konstruktor lewat DI; properti ini disediakan untuk Engine
      /// dan untuk hal yang memang melintasi request - penulis audit, log enricher, pendaftaran DI -
      /// dan tipenya sengaja nullable supaya pemakainya dipaksa memikirkan keadaan "sedang tidak ada
      /// request".
      /// <para>
      /// Dibaca ulang dari <c>HttpContext.Items</c> setiap kali diakses, bukan disimpan di field,
      /// dengan alasan yang sama persis seperti <see cref="ServiceProvider"/> di atas: EmApp hidup
      /// sebagai singleton, jadi sebuah field akan membuat dua request yang berjalan bersamaan saling
      /// menimpa identitas.
      /// </para>
      /// </remarks>
      public ActionRequest? CurrentRequest =>
         _httpContextAccessor.HttpContext?.Items[RequestItemKey] as ActionRequest;

      private ActionDefinition[] _actions = [];

      private DebugTokenKey[] _debugTokenKeys = [];

      private ClaimAction[] _claims = [];

      // Diisi di BuildApp; store yang mati kalau CDN tidak dinyalakan, tidak pernah null sesudahnya.
      private ManagedStorageSettings _storageSettings = null!;
      private void ApplyStorageSettingsRegistration(EmAppBuilder builder) {
         var hostKey = builder.StorageSettingsHostId is null ? null : MetaStorageSettingsPersistence.CreateHostKey(
            builder.StorageSettingsHostId, Environment.MachineName, Builder.Environment.ContentRootPath);
         IStorageSettingsPersistence? persistence = hostKey is null ? null : new MetaStorageSettingsPersistence(hostKey,
            () => {
               var options = new DbContextOptionsBuilder<ApiCoreContext>();
               options.UseEmProvider(builder.DatabaseProvider, builder.ConnectionString);
               return new ApiCoreContext(options.Options);
            });
         _storageSettings = new ManagedStorageSettings(Builder.Environment.ContentRootPath, persistence,
            builder.CdnDefaults ?? new StorageFeatureSettings { Enabled = builder.CdnRootPath is not null, Directory = builder.CdnRootPath ?? "", MaxUploadMb = builder.CdnRootPath is null ? 200 : (int)(builder.CdnMaxFileSize / (1024L * 1024L)) },
            builder.RegistryDefaults ?? new StorageFeatureSettings { Enabled = builder.ContainerRegistryPath is not null, Directory = builder.ContainerRegistryPath ?? "" },
            new[] { builder.BinaryStorageRootPath, builder.BusinessTaskCachePath }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!),
            builder.NuPakManaged ? builder.NuGetDefaults : new StorageFeatureSettings { Enabled = builder.NuPakRegistered, Directory = builder.NuPakPath ?? "./data/nuget", MaxUploadMb = builder.NuPakMaxPackageMb });
         Services.AddSingleton(_storageSettings);
      }
      private void ApplyNuPakRegistration(EmAppBuilder builder) {
         if (!builder.NuPakRegistered) return;
         if (builder.NuPakManaged && builder.StorageSettingsHostId is null)
            throw new InvalidOperationException("Managed AddNuPak requires AddManagedStorageSettings.");
         var settings = _storageSettings.Active(2);
         Services.AddSingleton(new Em.Api.Core.NuPak.NuPakStore(_storageSettings.Resolve(settings.Directory), settings.MaxUploadMb));
         if (settings.Enabled) builder.AddPublicEndpoint("/nuget", Em.Api.Core.NuPak.NuPakEndpoint.HandleAsync);
      }
      private CdnStore _cdn = CdnStore.Disabled;
      private CtnBlobStore _ctnStore = CtnBlobStore.Disabled;

      // Diisi di BuildApp, dimuat di Run.
      private BusinessTaskRunner _businessTasks = null!;

      /// <summary>
      /// Katalog seluruh claim yang terdaftar lewat <c>EmAppBuilder.AddClaims</c>, dibekukan sejak
      /// <see cref="BuildApp"/>. Dibaca oleh <c>GetMeta_AllClaimActions</c> dan oleh UI pengelola claim
      /// di fase berikutnya. Namanya sengaja sama persis dengan <c>EmApp.AllClaims</c> milik client:
      /// dua tipe berbeda di dua assembly berbeda, satu arti, satu nama.
      /// </summary>
      public IReadOnlyList<ClaimAction> AllClaims => _claims;

      /// <summary>
      /// Memastikan setiap claim yang terdaftar menunjuk module yang benar-benar terpasang - kalau
      /// tidak, itu berarti claim didaftarkan untuk service yang <c>AddService</c>-nya tidak pernah
      /// dipanggil (mis. barisnya sedang dikomentari), dan hak yang diberikan lewat claim itu tidak
      /// akan pernah terpakai.
      /// </summary>
      private static void EnsureClaimModulesAreRegistered(ClaimAction[] claims, List<ActionDefinition> actions) {
         foreach (var claim in claims) {
            if (!actions.Any(r => string.Equals(r.Module, claim.ModuleName, StringComparison.OrdinalIgnoreCase))) {
               throw new InvalidOperationException(
                  $"Claim '{claim.Key}' names module '{claim.ModuleName}', but no service was registered for that module. " +
                  "Register the service with 'AddService' before (or after) registering its claims.");
            }
         }
      }

      private bool _includeStackTrace;

      // Batas waktu kerja action GET, diisi dari EmAppBuilder.HttpRequestTimeout saat aplikasi
      // dibangun. Action yang menyebutkan angkanya sendiri lewat [GetAction] memakai angka itu,
      // bukan yang ini.
      private TimeSpan _httpRequestTimeout;

      // Jatah request per alamat pemanggil, atau null kalau EmAppBuilder.ActionRateLimit
      // mematikannya. Satu objek untuk seluruh aplikasi - jatahnya memang harus dihitung lintas
      // request - dan aman dipakai beberapa request sekaligus.
      private PartitionedRateLimiter<HttpContext>? _rateLimiter;

      /// <summary>
      /// Berapa lama - dalam jam - baris sesi yang sudah mati masih disimpan sebelum dibuang. Diisi
      /// dari <c>EmAppBuilder.SessionTokenRetentionHour</c> saat aplikasi dibangun.
      /// </summary>
      public int SessionTokenRetentionHour { get; private set; }

      // Password pertama akun admin, hanya dipakai saat penyemaian di Run(): sesudah nilainya masuk
      // ke database, yang berlaku adalah yang tersimpan di sana. Internal, bukan publik - ini teks
      // polos sebuah password, dan tidak ada satu pun pemanggil di luar Engine yang perlu membacanya.
      private string _firstTimeAdminPassword = string.Empty;

      #endregion

      private EmApp(WebApplicationBuilder builder) {
         Builder = builder;
      }

      #region Request Processing

      public async Task<IResult> ProcessRequest(string module, string action, HttpContext http) {
         var routeLabel = $"{module}/{action}";

         // Dicatat paling depan, bahkan sebelum gerbang identitas: request yang nanti ditolak pun
         // harus kelihatan pernah datang, karena justru itu yang dicari saat ada yang mengetuk-ngetuk
         // dari luar.
         var trace = RequestTrace.Begin(http, routeLabel);

         // Jatah request diperiksa sebelum gerbang identitas, dan karena itu sebelum satu pun query
         // dijalankan: kalau yang datang memang banjir, yang paling tidak boleh terjadi adalah
         // setiap request di dalamnya sempat membebani database dulu sebelum ditolak.
         if (IsRateLimited(http)) {
            return Reject(BuildErrorActionResult(TooManyRequestsMessage, 429, null, routeLabel));
         }

         // Gerbang identitas dijalankan paling depan, sebelum route-nya sendiri dicari. Dengan begitu
         // request yang membawa token debug palsu selalu dijawab persis seperti action yang tidak ada -
         // apa pun route yang dituju - sehingga mekanismenya tidak bisa diraba dari luar.
         var gate = await ResolveCallerAsync(http, routeLabel);

         // Ditaruh di HttpContext.Items - bukan di sebuah field - karena EmApp hidup sebagai
         // singleton: sebuah field akan membuat dua request yang berjalan bersamaan saling menimpa
         // identitas, dan gejalanya baru muncul saat ada beban. Ditulis sebelum penolakan diperiksa,
         // supaya apa pun yang menulis jejak tetap bisa membaca request yang ditolak.
         http.Items[RequestItemKey] = gate.Request;
         trace.Identified(gate.Request);

         if (gate.Rejection is { } rejection) {
            return Reject(rejection);
         }

         var actionDef =
            _actions.SingleOrDefault(r =>
               string.Equals(r.Module, module, StringComparison.InvariantCultureIgnoreCase) &&
               string.Equals(r.ActionName, action, StringComparison.InvariantCultureIgnoreCase));

         if (actionDef is null) {
            return Reject(ActionNotFoundResult(routeLabel));
         }

         // Diperiksa sesudah route-nya ketemu, bukan sebelum, karena "boleh dipanggil tanpa identitas"
         // memang milik action-nya. Konsekuensinya action yang ada tapi tertutup dijawab 401 sementara
         // action yang tidak ada dijawab 404, sehingga dari luar keduanya bisa dibedakan. Itu diterima:
         // yang benar-benar harus tidak terdeteksi adalah mekanisme token debug, dan jalur itu tetap
         // menjawab 404 apa pun route-nya karena gerbang di atas berjalan lebih dulu.
         if (!actionDef.IsPublicAction && !gate.Request.IsAuthenticated) {
            return Reject(BuildErrorActionResult(
               "This action requires a signed-in caller.", 401, actionDef.Type, routeLabel));
         }

         // Ditaruh sesudah blok di atas, bukan menggantikannya, supaya urutan kegagalannya tetap benar:
         // tidak ada identitas dijawab 401, identitas yang ada tapi haknya kurang dijawab 403. Client
         // yang memperbarui token setiap kali kena 401 tidak boleh dikirim mengejar token baru untuk
         // permintaan yang memang tidak akan pernah diizinkan - alasan yang sama persis dengan yang
         // sudah dipakai ActionRequest.RequireAdmin.
         if (!actionDef.IsPublicAction && gate.Request.IsAuthenticated && !HasRequiredClaim(gate.Request, actionDef)) {
            return Reject(BuildErrorActionResult(
               actionDef.RequiredClaim is { } required
                  ? $"This action requires the claim '{actionDef.Module}{ClaimAction.Separator}{required}'."
                  : $"This action requires a claim on module '{actionDef.Module}'.",
               403, actionDef.Type, routeLabel));
         }

         switch (http.Request.Method) {
            case "POST" when actionDef.HttpMethod != HttpMethod.Post:
               return Reject(BuildErrorActionResult($"Method Not Allowed! Expecting POST!", 405, null, routeLabel));
            case "GET" when actionDef.HttpMethod != HttpMethod.Get:
               return Reject(BuildErrorActionResult($"Method Not Allowed! Expecting GET!", 405, null, routeLabel));
         }

         var service = (ServicesBase)http.RequestServices.GetRequiredService(actionDef.Type);
         service.App = this;
         service.HttpContext = http;
         service.Logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(actionDef.Type);
         service.Request = gate.Request;

         // Satu sumber pembatalan untuk action ini. Pemanggil yang pergi selalu ikut di dalamnya;
         // anggaran waktu baru dipasang di atasnya beberapa baris lagi, dan hanya untuk GET. Dibuat
         // sebelum kedua cabang di bawah karena isinya sama - yang berbeda cuma apakah timernya jadi
         // dipasang.
         using var abort = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
         service.AbortToken = abort.Token;

         if (actionDef.HttpMethod == HttpMethod.Get) {
            if (!TryBindArguments(actionDef.MethodInfo, http.Request.Query, out var arguments, out var bindError)) {
               return Reject(BuildErrorActionResult(bindError ?? "Invalid request.", 400, actionDef.Type, routeLabel));
            }

            trace.Processing();

            // Dihitung sejak di sini, bukan sejak request masuk, supaya yang dibatasi benar-benar
            // lama kerja action-nya - sama dengan yang diukur jejak request di baris atas. Syarat
            // "> Zero" sekaligus menutup dua bentuk "tanpa batas" yang berbeda: TimeSpan.Zero dari
            // EmAppBuilder dan Timeout.InfiniteTimeSpan dari [GetAction].
            var timeout = actionDef.RequestTimeout ?? _httpRequestTimeout;
            if (timeout > TimeSpan.Zero) {
               abort.CancelAfter(timeout);
            }

            var outcome = await InvokeAsync(service, actionDef.MethodInfo, arguments, actionDef.Type, routeLabel,
               _includeStackTrace, http.RequestAborted, abort.Token);
            return Complete(outcome);
         }

         if (actionDef.HttpMethod == HttpMethod.Post) {
            // A streamed action hands the body over untouched, so it never goes through the JSON binder.
            // Everything above - rate limit, identity, claim, verb - has already passed at this point,
            // which is what makes it safe to lift the body limit inside: a refused request never gets
            // that far.
            var (isBound, arguments, bindError) = actionDef.StreamParameterIndex is not null
               ? (TryBindStreamArguments(actionDef, http, out var streamArguments, out var streamError),
                  streamArguments, streamError)
               : await TryBindArguments(actionDef.MethodInfo, http.Request);
            if (!isBound) {
               return Reject(BuildErrorActionResult(bindError ?? "Invalid request body.", 400, actionDef.Type,
                  routeLabel));
            }

            trace.Processing();

            // Sengaja tanpa CancelAfter. Memutus sebuah penulisan di tengah jalan tidak menghasilkan
            // "tidak jadi" melainkan "entah", dan pemanggilnya tidak punya cara tahu sejauh mana
            // tulisannya sudah sampai. Yang tersisa di token ini hanya pemanggil yang pergi, dan
            // menanggapinya adalah keputusan penulis action - bukan keputusan engine.
            var outcome = await InvokeAsync(service, actionDef.MethodInfo, arguments, actionDef.Type, routeLabel,
               _includeStackTrace, http.RequestAborted, abort.Token);
            return Complete(outcome);
         }

         return Reject(BuildErrorActionResult("Unsupported HTTP method.", 405, actionDef.Type, routeLabel));

         // Dua pembungkus ini ada supaya setiap jalan keluar di atas ikut tercatat tanpa harus
         // menulis barisnya satu-satu: yang ditolak sebelum action-nya berjalan lewat Reject, yang
         // sempat berjalan - berhasil maupun gagal - lewat Complete.
         IResult Reject(ActionResult result) {
            trace.Rejected(result);
            return ToJsonResult(result);
         }

         IResult Complete(ActionOutcome outcome) {
            if (outcome.Abort is { } reason) {
               trace.Aborted(reason);

               // Yang batal karena anggaran waktu tetap membawa jawaban - pemanggilnya masih
               // menunggu di ujung sana. Yang batal karena pemanggilnya pergi tidak: socket-nya
               // sudah tidak ada, dan menulis ke sana hanya melahirkan kegagalan kedua.
               return outcome.Result is { } answer ? ToJsonResult(answer) : Results.Empty;
            }

            // Sebuah action bisa selesai utuh justru sesudah pemanggilnya pergi - dan untuk action
            // tulis memang itu yang diinginkan. Pekerjaannya tetap berharga, jawabannya tidak lagi
            // punya tujuan.
            if (http.RequestAborted.IsCancellationRequested) {
               outcome.Content?.Dispose();
               trace.Aborted(AbortReason.CallerGone);
               return Results.Empty;
            }

            trace.Completed(outcome.Result!);

            // The time budget of a GET covered the action only up to here, where it handed its stream
            // over: the linked source above is disposed as soon as this method returns, so copying the
            // content to the response observes nothing but RequestAborted. Results.Stream closes the
            // stream once it has been written, whether the copy finished or not.
            if (outcome.Content is { } content) {
               return Results.Stream(content, "application/octet-stream");
            }

            return ToJsonResult(outcome.Result!);
         }
      }

      #endregion

      #region Rate Limit

      // Sengaja tidak menyebut berapa batasnya maupun kapan ia pulih: angka itu milik pengaturan
      // server ini, dan yang perlu dilakukan pemanggilnya sama saja apa pun isinya. Waktu tunggu
      // yang sebenarnya tetap dikirim, lewat header Retry-After, tempat setiap client sudah tahu
      // mencarinya.
      private const string TooManyRequestsMessage =
         "Too many requests from this address. Wait a moment before trying again.";

      // Sebutan pengganti untuk request yang datang tanpa alamat - lewat unix socket, atau dari host
      // in-memory. Semuanya berbagi satu jatah, dan itu memang yang diinginkan: yang tidak punya
      // alamat tidak bisa dibedakan satu sama lain.
      private const string UnknownCallerPartition = "unknown";

      // Panjang jendela jatah dan berapa potong ia dibagi. Makin banyak potongannya makin halus
      // geseran jendelanya, dan makin banyak pula yang harus diingat per alamat; enam sudah cukup
      // untuk menutup lonjakan di batas jendela tanpa jadi mahal.
      private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

      private const int RateLimitSegments = 6;

      // Satu potong jendela - sepuluh detik. Ini pula waktu tersingkat sampai ada jatah yang kembali:
      // begitu potongan tertua lepas dari jendela, jatah yang terpakai di dalamnya ikut pulih.
      private static readonly TimeSpan RateLimitSegment = RateLimitWindow / RateLimitSegments;

      /// <summary>
      /// Menyusun penghitung jatah request, satu jatah per alamat pemanggil, atau <c>null</c> kalau
      /// <c>EmAppBuilder.ActionRateLimit</c> mematikannya.
      /// </summary>
      /// <remarks>
      /// Alamat dibaca dari koneksi, bukan dari header, dan itu sudah alamat yang benar walau
      /// request datang lewat proxy: <c>UseForwardedHeaders</c> berjalan paling depan di pipeline,
      /// jadi saat baris ini dijalankan alamat proxy sudah digantikan alamat client aslinya - selama
      /// proxy-nya memang didaftarkan lewat <c>EmAppBuilder.TrustProxy</c>. Kalau tidak, seluruh
      /// request yang lewat proxy itu berbagi satu jatah, dan satu client yang berlebihan menghabiskan
      /// jatah semua orang di belakangnya.
      /// </remarks>
      private static PartitionedRateLimiter<HttpContext>? CreateRateLimiter(int perMinute) {
         if (perMinute < 0) {
            return null;
         }

         return PartitionedRateLimiter.Create<HttpContext, string>(http =>
            RateLimitPartition.GetSlidingWindowLimiter(
               http.Connection.RemoteIpAddress?.ToString() ?? UnknownCallerPartition,
               _ => new SlidingWindowRateLimiterOptions {
                  PermitLimit = perMinute,
                  Window = RateLimitWindow,
                  SegmentsPerWindow = RateLimitSegments,
                  // Tidak ada antrean: yang melebihi jatah ditolak seketika, bukan ditahan menunggu.
                  // Menahannya berarti request yang sedang berlangsung menumpuk di server - persis
                  // beban yang hendak dihindari batas ini.
                  QueueLimit = 0,
                  AutoReplenishment = true
               }));
      }

      /// <summary>
      /// Apakah request ini melebihi jatah alamatnya. Kalau ya, waktu tunggunya ikut ditulis ke
      /// header <c>Retry-After</c> sebelum jawaban <c>429</c> disusun.
      /// </summary>
      private bool IsRateLimited(HttpContext http) {
         if (_rateLimiter is not { } limiter) {
            return false;
         }

         // Lease-nya langsung dilepas, tidak dipegang selama request berjalan. Yang dihitung jendela
         // bergeser adalah kedatangan request, bukan berapa yang sedang dikerjakan - jadi memegangnya
         // lebih lama tidak mengubah apa pun selain memperpanjang umur objeknya.
         using var lease = limiter.AttemptAcquire(http);
         if (lease.IsAcquired) {
            return false;
         }

         // Limiter-nya tidak selalu menyebutkan waktu tunggunya sendiri, dan pemanggil yang tidak
         // diberi tahu kapan boleh kembali biasanya mencoba lagi seketika - tepat hal yang sedang
         // ditolak. Yang dipakai sebagai gantinya satu potong jendela: bukan tebakan, itu memang
         // saat paling awal ada jatah yang pulih.
         var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : RateLimitSegment;

         http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
         return true;
      }

      #endregion

      #region Authorization Gate

      // Sedikit kelonggaran untuk selisih jam antara mesin pengembang dan server, mengikuti angka yang
      // sama dengan pemeriksaan access token: tanpa ini token yang diterbitkan mesin yang jamnya maju
      // beberapa detik terbaca "diterbitkan di masa depan".
      private static readonly TimeSpan DebugTokenClockSkew = TimeSpan.FromSeconds(30);

      private const string BearerPrefix = "Bearer ";

      /// <summary>
      /// Hasil gerbang: keterangan request beserta identitas pemanggilnya, atau - kalau
      /// <see cref="Rejection"/> terisi - jawaban yang harus dikirim balik tanpa action-nya sempat
      /// dijalankan.
      /// </summary>
      private readonly record struct CallerGate(ActionRequest Request, ActionResult? Rejection);

      /// <summary>
      /// Apakah pemanggil berhak memanggil action ini. Dipanggil gerbang sebelum action-nya dijalankan,
      /// bukan dari dalam action - karena itu ia mengembalikan <c>bool</c> alih-alih melempar seperti
      /// <c>ActionRequest.RequireAdmin</c>: di titik ini pola yang dipakai adalah menolak langsung, dan
      /// menyamakan keduanya akan menyesatkan pembaca yang sedang mencari tahu sebuah aturan diperiksa
      /// di gerbang atau di dalam action.
      /// </summary>
      /// <remarks>
      /// Urutan pemeriksaannya meniru <c>NavigationAccess.CanOpen</c> di sisi UI: jalur token debug dulu,
      /// administrator kedua, baru haknya sendiri. Satu aturan yang sama tidak boleh diperiksa dengan
      /// urutan berbeda di dua sisi - kalau berbeda, layar yang boleh dibuka dan action yang boleh
      /// dipanggil bisa tidak lagi cocok.
      /// </remarks>
      private static bool HasRequiredClaim(ActionRequest request, ActionDefinition actionDef) {
         if (request.IsDebugRequest) return true;

         if (request.IsAdmin) return true;

         if (actionDef.RequiredClaim is { } required) {
            return request.Claims.Any(r =>
               string.Equals(r.ModuleName, actionDef.Module, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(r.Name, required, StringComparison.OrdinalIgnoreCase));
         }

         // Baru di sini service engine berhenti diperiksa, bukan di baris pertama: yang dilepas darinya
         // hanya syarat default di bawah, sementara claim yang disebut eksplisit tetap berlaku.
         if (!actionDef.EnforcesClaims) return true;

         return request.Claims.Any(r =>
            string.Equals(r.ModuleName, actionDef.Module, StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Menentukan identitas pemanggil dari header yang dibawanya. Token debug diperiksa paling depan,
      /// baru access token; header <c>X-Em-User</c> tidak pernah menjadi sumber identitas dengan
      /// sendirinya, karena kalau pernah dipercaya sendirian siapa pun cukup menyebut id atau nama akun
      /// mana saja untuk menjadi pemiliknya.
      /// </summary>
      private async Task<CallerGate> ResolveCallerAsync(HttpContext http, string routeLabel) {
         var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<EmApp>();
         var debugToken = http.Request.Headers[Defaults.DebugTokenHeader].ToString();
         UserHeaderProtocol.TryRead(http.Request.Headers[Defaults.UserHeader].ToString(),
            out var headerUserId, out var headerAccount);

         // Keterangan yang berlaku untuk request apa pun, terisi sekali di sini supaya setiap cabang
         // di bawah tinggal menambahkan identitasnya sendiri.
         var request = new ActionRequest {
            RouteLabel = routeLabel,
            CallerAddress = http.Connection.RemoteIpAddress?.ToString(),
            ReceivedAtUtc = DateTime.UtcNow
         };

         if (!string.IsNullOrWhiteSpace(debugToken)) {
            if (!TryVerifyDebugToken(debugToken, out var keyName, out var refusal)) {
               // Diam ke client, cerewet ke log server: alasan sebenarnya hanya ditulis di sini, sementara
               // yang dikirim balik sama persis dengan jawaban untuk action yang tidak ada.
               logger.LogWarning(
                  "Debug token refused for '{Route}' from {Caller}: {Reason}.",
                  routeLabel, CallerAddress(http), refusal);
               return new CallerGate(request, ActionNotFoundResult(routeLabel));
            }

            return await ResolveDebugCallerAsync(http, request, keyName, headerUserId, headerAccount, logger);
         }

         // Tripwire. Tidak ada satu pun jalur login yang bisa menghasilkan akun ini, jadi request yang
         // menyebutnya - lewat nama akun maupun lewat id - tanpa token debug sudah pasti palsu.
         if (string.Equals(headerAccount, Defaults.DebuggerUserAccount, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(headerUserId, Defaults.DebuggerUserId, StringComparison.Ordinal)) {
            logger.LogWarning(
               "Request for '{Route}' from {Caller} claimed the '{Account}' account without a debug token.",
               routeLabel, CallerAddress(http), Defaults.DebuggerUserAccount);
            return new CallerGate(request, ActionNotFoundResult(routeLabel));
         }

         return await ResolveBearerCallerAsync(http, request, headerUserId, logger);
      }

      /// <summary>
      /// Menentukan identitas untuk request yang token debug-nya sudah lolos. Tanpa <c>X-Em-User</c>
      /// yang dipakai adalah akun debugger; dengan akun lain, pengembang menyamar menjadi akun itu -
      /// dan haknya dibaca apa adanya dari datanya sendiri, tidak dipaksa menjadi administrator, karena
      /// kalau dipaksa maka pengujian hak akses kehilangan artinya.
      /// </summary>
      /// <remarks>
      /// Akun yang disamar boleh disebut lewat id, lewat nama akun, atau keduanya. Kalau keduanya
      /// disebut, yang dipakai mencari adalah id - itu yang permanen - lalu nama akunnya dicocokkan
      /// dengan baris yang ketemu. Ketidakcocokan ditolak, bukan didiamkan: ia berarti client menyusun
      /// header dari dua sumber yang berbeda, dan menebak mana yang benar lebih buruk daripada berhenti.
      /// </remarks>
      private async Task<CallerGate> ResolveDebugCallerAsync(HttpContext http, ActionRequest request, string keyName,
         string? headerUserId, string? headerAccount, ILogger logger) {
         // Sesudah token lolos, tidak ada lagi yang perlu disembunyikan: kegagalan di bawah ini dijawab
         // dengan pesan yang menyebut sebabnya, bukan 404, supaya pengembang tidak menebak-nebak.
         request = request with { Source = CallerSource.DebugToken, DebugKeyName = keyName };

         // Tanpa header sama sekali yang dipakai adalah akun debugger. Sengaja akun debugger, bukan
         // administrator bawaan: jejaknya harus bisa membedakan pengembang yang sedang mengoprek dari
         // administrator sungguhan.
         if (headerUserId is null && headerAccount is null) {
            return Accepted(request with {
               cUserId = Defaults.DebuggerUserId,
               cUserAccount = Defaults.DebuggerUserAccount,
               IsAdmin = true
            });
         }

         // Akun sistem dikenali lebih dulu dan tidak pernah dicari sebagai baris pengguna: keduanya
         // memang tidak punya baris. Keduanya cocok baik disebut lewat id maupun lewat nama akun.
         if (NamesSystemAccount(Defaults.DebuggerUserId, Defaults.DebuggerUserAccount, out var isConsistent)) {
            if (!isConsistent) return Mismatched(Defaults.DebuggerUserId, Defaults.DebuggerUserAccount);

            return Accepted(request with {
               cUserId = Defaults.DebuggerUserId,
               cUserAccount = Defaults.DebuggerUserAccount,
               IsAdmin = true
            });
         }

         if (NamesSystemAccount(Defaults.AdminUserId, Defaults.AdminUserAccount, out isConsistent)) {
            if (!isConsistent) return Mismatched(Defaults.AdminUserId, Defaults.AdminUserAccount);

            // Akun administrator bawaan tidak punya baris pengguna, jadi keadaannya dibaca dari saklarnya.
            var adminCtx = http.RequestServices.GetRequiredService<ApiCoreContext>();
            if (!await AdminAccount.IsEnabledAsync(adminCtx)) {
               return Rejected(
                  $"The '{Defaults.AdminUserAccount}' account is currently disabled and cannot be acted as.");
            }

            return Accepted(request with {
               cUserId = Defaults.AdminUserId,
               cUserAccount = Defaults.AdminUserAccount,
               IsAdmin = true,
               IsImpersonating = true
            });
         }

         var ctx = http.RequestServices.GetRequiredService<ApiCoreContext>();
         ta_User? user;

         if (headerUserId is not null) {
            user = await ctx.ta_Users.AsNoTracking().SingleOrDefaultAsync(r => r.cUserId == headerUserId);
            if (user is null) {
               return Rejected(
                  $"There is no account with id '{headerUserId}' to act as; check the '{Defaults.UserHeader}' header.");
            }

            if (headerAccount is not null &&
                !string.Equals(user.cUserAccount, headerAccount, StringComparison.OrdinalIgnoreCase)) {
               return Mismatched(user.cUserId, user.cUserAccount);
            }
         }
         else {
            user = await ctx.ta_Users.AsNoTracking().SingleOrDefaultAsync(r => r.cUserAccount == headerAccount);
            if (user is null) {
               return Rejected(
                  $"There is no account named '{headerAccount}' to act as; check the '{Defaults.UserHeader}' header.");
            }
         }

         if (user.cUserState < UserState.Inactive) {
            return Rejected($"The account '{user.cUserAccount}' is {user.cUserState} and cannot be acted as.");
         }

         return Accepted(request with {
            cUserId = user.cUserId,
            cUserAccount = user.cUserAccount,
            IsAdmin = user.cUserIsAdmin,
            IsImpersonating = true
         });

         // Apakah header menyebut akun sistem tertentu - lewat id, lewat nama akun, atau keduanya.
         // consistent bernilai false kalau keduanya disebut tapi menunjuk akun yang berbeda.
         bool NamesSystemAccount(string systemUserId, string systemAccount, out bool consistent) {
            var byId = string.Equals(headerUserId, systemUserId, StringComparison.Ordinal);
            var byAccount = string.Equals(headerAccount, systemAccount, StringComparison.OrdinalIgnoreCase);
            consistent = headerUserId is null || headerAccount is null || (byId && byAccount);
            return byId || byAccount;
         }

         CallerGate Mismatched(string actualUserId, string actualAccount) =>
            Rejected(
               $"The '{Defaults.UserHeader}' header names id '{headerUserId}' together with account " +
               $"'{headerAccount}', but that id belongs to '{actualAccount}' (id '{actualUserId}'). " +
               "Send one of the two, or send a pair that matches.");

         CallerGate Accepted(ActionRequest resolved) {
            // Selama penyamaran, baris data yang tertulis membawa jejak akun yang disamar dan tidak bisa
            // dibedakan dari pekerjaan aslinya. Sampai sistem audit ada, baris log inilah satu-satunya jejak
            // bahwa yang mengerjakannya sebenarnya seorang pengembang.
            logger.LogWarning(
               "Debug token '{Key}' from {Caller} accepted for '{Route}', acting as '{Account}' ({UserId}, admin: {IsAdmin}).",
               keyName, CallerAddress(http), resolved.RouteLabel, resolved.cUserAccount, resolved.cUserId,
               resolved.IsAdmin);
            return new CallerGate(resolved, null);
         }

         CallerGate Rejected(string message) {
            logger.LogWarning(
               "Debug token '{Key}' from {Caller} was accepted for '{Route}', but it could not act as the account it named: {Reason}",
               keyName, CallerAddress(http), request.RouteLabel, message);
            return new CallerGate(request, BuildErrorActionResult(message, 401, null, request.RouteLabel));
         }
      }

      /// <summary>
      /// Membaca identitas dari access token, kalau ada. Access token yang tidak sah tidak ditolak di
      /// sini, hanya tidak menghasilkan identitas - action non-publiklah yang menolaknya sendiri. Yang
      /// tetap lewat adalah action publik, dan itu memang yang diinginkan: refresh token dikirim justru
      /// ketika access token-nya sudah mati.
      /// </summary>
      private async Task<CallerGate> ResolveBearerCallerAsync(HttpContext http, ActionRequest request,
         string? headerUserId, ILogger logger) {
         var authorization = http.Request.Headers.Authorization.ToString();
         if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) {
            return new CallerGate(request, null);
         }

         var tokens = http.RequestServices.GetRequiredService<ITokenServices>();
         var validation = await tokens.ValidateAsync(authorization[BearerPrefix.Length..].Trim());

         if (!validation.IsValid) {
            logger.LogWarning("Access token presented for '{Route}' from {Caller} was not accepted: {Reason}",
               request.RouteLabel, CallerAddress(http), validation.Error ?? "no reason given");
            return new CallerGate(request, null);
         }

         // Yang menang sudah pasti token-nya - ada kondisi sah yang membuat keduanya berbeda, misalnya
         // request yang masih di udara saat pengguna berganti - jadi selisihnya cukup ditulis ke log.
         // Sekarang yang dibandingkan dua id, bukan nama akun, sehingga tidak ada query tambahan.
         if (headerUserId is not null && !string.Equals(headerUserId, validation.cUserId, StringComparison.Ordinal)) {
            logger.LogDebug(
               "Header '{Header}' named '{HeaderUserId}' for '{Route}'; identity comes from the access token ({UserId}).",
               Defaults.UserHeader, headerUserId, request.RouteLabel, validation.cUserId);
         }

         // Hak pemanggil dimuat di sini, bukan di titik pemeriksaannya, supaya action mana pun yang
         // hendak membacanya sendiri mendapat isi yang sama dengan yang dipakai gerbang. Administrator
         // dilewati karena ia lolos tanpa dibaca haknya - membayar satu query untuk jawaban yang tidak
         // akan pernah dipakai tidak ada gunanya. Jalur token debug tidak pernah sampai ke sini.
         ClaimAction[] claims = [];
         if (!validation.IsAdmin && validation.cUserId is { } cUserId) {
            claims = await UserClaimLoader.LoadAsync(
               http.RequestServices.GetRequiredService<ApiCoreContext>(),
               cUserId, await GetDateStampAsync(), http.RequestAborted);
         }

         // cUserAccount sengaja dibiarkan kosong: token hanya membawa id, dan menerjemahkannya jadi
         // nama akun berarti satu query tambahan di setiap request. Nama yang menempel di header pun
         // tidak dipakai mengisinya - ia datang dari pemanggil, bukan dari data.
         return new CallerGate(request with {
            Source = CallerSource.AccessToken,
            cUserId = validation.cUserId,
            cUserSessionId = validation.cUserSessionId,
            IsAdmin = validation.IsAdmin,
            Claims = claims
         }, null);
      }

      /// <summary>
      /// Memeriksa token debug: bentuknya, nama key-nya, tanda tangannya, dan masa berlakunya. Alasan
      /// penolakan dikembalikan lewat <paramref name="refusal"/> untuk ditulis ke log server saja.
      /// </summary>
      private bool TryVerifyDebugToken(string token, out string keyName, out string refusal) {
         keyName = string.Empty;

         if (_debugTokenKeys.Length == 0) {
            refusal = "no debug token key is registered on this server";
            return false;
         }

         if (!DebugTokenProtocol.TryRead(token, out var name, out var issuedAtUtc, out var signedPayload,
                out var signature)) {
            refusal = "the token could not be read";
            return false;
         }

         var registered =
            _debugTokenKeys.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
         if (registered is null) {
            refusal = $"no key named '{name}' is registered";
            return false;
         }

         if (!registered.Key.VerifyData(signedPayload, signature)) {
            refusal = $"the signature does not match key '{registered.Name}'";
            return false;
         }

         var now = DateTime.UtcNow;
         if (issuedAtUtc > now.Add(DebugTokenClockSkew)) {
            refusal = $"the token from key '{registered.Name}' is dated {issuedAtUtc:O}, which is in the future";
            return false;
         }

         if (registered.HasExpiry && registered.ExpiresAtUtc(issuedAtUtc) < now) {
            refusal =
               $"the token from key '{registered.Name}' expired on {registered.ExpiresAtUtc(issuedAtUtc):O}; restart the client to have it sign a new one";
            return false;
         }

         keyName = registered.Name;
         refusal = string.Empty;
         return true;
      }

      /// <summary>
      /// Jawaban tunggal untuk setiap penolakan di jalur token debug - sama persis, sampai ke kata-katanya,
      /// dengan jawaban untuk action yang memang tidak ada. Beda sedikit saja, ia mengumumkan keberadaan
      /// dirinya.
      /// </summary>
      private static ActionResult ActionNotFoundResult(string routeLabel) =>
         BuildErrorActionResult($"Action '{routeLabel}' was not found.", 404, null, routeLabel);

      // Satu sumber sebutan alamat untuk log gerbang dan log jejak request, supaya keduanya menyebut
      // alamat yang sama persis - termasuk saat request datang lewat proxy.
      private static string CallerAddress(HttpContext http) => RequestTrace.DescribeCaller(http);

      #endregion

      #region Validation

      private static void EnsureAsyncAction(MethodInfo method, Type serviceType) {
         var returnType = method.ReturnType;
         var isTask = typeof(Task).IsAssignableFrom(returnType);
         if (!isTask) {
            throw new InvalidOperationException(
               $"Action '{serviceType.FullName}.{method.Name}' must return Task or Task<T>.");
         }
      }

      #endregion

      #region Binding

      private static bool TryBindArguments(MethodInfo method, IQueryCollection query, out object?[] arguments,
         out string? error) {
         var parameters = method.GetParameters();
         arguments = new object?[parameters.Length];

         for (var index = 0; index < parameters.Length; index++) {
            var parameter = parameters[index];
            var queryKey = $"par{index + 1}";

            if (query.TryGetValue(queryKey, out var values) && values.Count > 0) {
               if (!TryConvert(values[0]!, parameter.ParameterType, out var converted, out error)) {
                  arguments = [];
                  return false;
               }

               arguments[index] = converted;
               continue;
            }

            if (parameter.HasDefaultValue) {
               arguments[index] = parameter.DefaultValue;
               continue;
            }

            if (!parameter.ParameterType.IsValueType ||
                Nullable.GetUnderlyingType(parameter.ParameterType) is not null) {
               arguments[index] = null;
               continue;
            }

            error = $"Missing required query parameter '{queryKey}'.";
            arguments = [];
            return false;
         }

         error = null;
         return true;
      }

      private static async Task<(bool IsBound, object?[] Arguments, string? Error)> TryBindArguments(MethodInfo method,
         HttpRequest request) {
         PostMethodPayload[]? payloads;

         try {
            payloads = await JsonSerializer.DeserializeAsync<PostMethodPayload[]>(
               request.Body,
               new JsonSerializerOptions {
                  PropertyNameCaseInsensitive = true
               });
         }
         catch (Exception ex) {
            return (false, [], $"Request body must be a JSON array of PostMethodPayload: {ex.Message}");
         }

         if (payloads is null) {
            return (false, [], "Request body must be a JSON array of PostMethodPayload.");
         }

         var isBound = TryBindArguments(method, payloads, out var arguments, out var error);
         return (isBound, arguments, error);
      }

      /// <summary>
      /// Binds a streamed action: the request body goes to the <see cref="Stream"/> parameter as it is,
      /// and the other parameter - if the action has one - is decoded from
      /// <see cref="Defaults.StreamPayloadHeader"/> into the type the method declares. The body is not
      /// read here; with <c>Expect: 100-continue</c> the client only starts sending it once the action
      /// does, so an action that refuses early never receives the file.
      /// </summary>
      private static bool TryBindStreamArguments(ActionDefinition actionDef, HttpContext http,
         out object?[] arguments, out string? error) {
         var parameters = actionDef.MethodInfo.GetParameters();
         arguments = new object?[parameters.Length];

         var header = http.Request.Headers[Defaults.StreamPayloadHeader].ToString();
         var hasHeader = !string.IsNullOrWhiteSpace(header);

         if (actionDef.StreamPayloadParameterIndex is { } payloadIndex) {
            var payloadParameter = parameters[payloadIndex];
            var payloadType = payloadParameter.ParameterType;
            // Unlike the JSON binder, a reference type counts as required unless it is declared
            // nullable: the payload is one object the action reads fields from, and handing it null
            // for a missing header only moves the 400 into a NullReferenceException.
            var acceptsNull = payloadType.IsValueType
               ? Nullable.GetUnderlyingType(payloadType) is not null
               : new NullabilityInfoContext().Create(payloadParameter).WriteState != NullabilityState.NotNull;

            if (hasHeader) {
               if (!StreamPayloadProtocol.TryDecode(header, payloadType, out var payload, out error)) {
                  arguments = [];
                  return false;
               }

               if (payload is null && !acceptsNull) {
                  error = $"The {Defaults.StreamPayloadHeader} header must not be null for {payloadType.Name}.";
                  arguments = [];
                  return false;
               }

               arguments[payloadIndex] = payload;
            }
            else if (acceptsNull) {
               arguments[payloadIndex] = null;
            }
            else {
               error = $"Missing required {Defaults.StreamPayloadHeader} header.";
               arguments = [];
               return false;
            }
         }
         else if (hasHeader) {
            error = $"This action does not take a {Defaults.StreamPayloadHeader} header.";
            arguments = [];
            return false;
         }

         // Only now, with the gate behind and the arguments in order: the action enforces its own size
         // limit while it reads, which is the only place that knows what the limit should be.
         if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize) {
            bodySize.MaxRequestBodySize = null;
         }

         arguments[actionDef.StreamParameterIndex!.Value] = http.Request.Body;
         error = null;
         return true;
      }

      private static bool TryBindArguments(MethodInfo method, PostMethodPayload[] payloads, out object?[] arguments,
         out string? error) {
         var parameters = method.GetParameters();
         arguments = new object?[parameters.Length];

         for (var index = 0; index < parameters.Length; index++) {
            var parameter = parameters[index];
            var payload = payloads.FirstOrDefault(r => r.ParameterOrdinal == index);

            if (payload is null) {
               if (parameter.HasDefaultValue) {
                  arguments[index] = parameter.DefaultValue;
                  continue;
               }

               if (!parameter.ParameterType.IsValueType ||
                   Nullable.GetUnderlyingType(parameter.ParameterType) is not null) {
                  arguments[index] = null;
                  continue;
               }

               error = $"Missing required body parameter for ordinal '{index}'.";
               arguments = [];
               return false;
            }

            if (!TryConstructBodyValue(payload, parameter.ParameterType, out var value, out error)) {
               arguments = [];
               return false;
            }

            arguments[index] = value;
         }

         error = null;
         return true;
      }

      private static bool TryConstructBodyValue(PostMethodPayload payload, Type targetType, out object? value,
         out string? error) {
         value = null;

         var constructMethod = typeof(PostMethodPayload).GetMethod(nameof(PostMethodPayload.ConstructObject));
         if (constructMethod is null) {
            error = "Cannot access payload constructor.";
            return false;
         }

         try {
            var genericMethod = constructMethod.MakeGenericMethod(targetType);
            var parameters = new object?[] { null, null };
            var success = (bool)(genericMethod.Invoke(payload, parameters) ?? false);

            if (!success) {
               error = parameters[1] as string ?? "Invalid body parameter.";
               return false;
            }

            value = parameters[0];
            error = null;
            return true;
         }
         catch (Exception ex) {
            error = ex.InnerException?.Message ?? ex.Message;
            return false;
         }
      }

      private static bool TryConvert(string rawValue, Type targetType, out object? value, out string? error) {
         var nullableType = Nullable.GetUnderlyingType(targetType);
         var actualType = nullableType ?? targetType;

         try {
            if (actualType == typeof(string)) {
               value = rawValue;
               error = null;
               return true;
            }

            if (actualType.IsEnum) {
               value = Enum.Parse(actualType, rawValue, ignoreCase: true);
               error = null;
               return true;
            }

            if (actualType == typeof(Guid)) {
               value = Guid.Parse(rawValue);
               error = null;
               return true;
            }

            var converter = TypeDescriptor.GetConverter(actualType);
            if (converter.CanConvertFrom(typeof(string))) {
               value = converter.ConvertFromInvariantString(rawValue);
               error = null;
               return true;
            }

            // A parameter that is a record or class (a query object, say) travels as JSON in the query
            // string, the same JSON the client writes for it. Anything convertible - numbers, dates -
            // still goes through Convert below, so only what could not be bound at all lands here.
            if (!typeof(IConvertible).IsAssignableFrom(actualType)) {
               value = JsonSerializer.Deserialize(rawValue, actualType, Defaults.ResponseJsonOptions);
               error = null;
               return true;
            }

            value = Convert.ChangeType(rawValue, actualType, CultureInfo.InvariantCulture);
            error = null;
            return true;
         }
         catch (Exception ex) {
            value = null;
            error = $"Cannot convert '{rawValue}' to {targetType.Name}: {ex.Message}";
            return false;
         }
      }

      #endregion

      #region Invocation and Results

      /// <summary>
      /// Hasil satu action: jawaban yang harus dikirim, dan - kalau action-nya batal - alasannya.
      /// Dua keadaan yang mungkin di luar jalur biasa: batal tanpa jawaban (pemanggilnya pergi), dan
      /// batal dengan jawaban (anggaran waktu habis, pemanggilnya masih menunggu).
      /// </summary>
      /// <remarks>
      /// <see cref="Content"/> terisi hanya untuk action yang mengembalikan <c>Task&lt;Stream&gt;</c> dan
      /// selesai dengan sukses: yang dikirim lalu isi stream itu, dan <see cref="Result"/> hanya dipakai
      /// untuk jejak request.
      /// </remarks>
      private readonly record struct ActionOutcome(ActionResult? Result, AbortReason? Abort, Stream? Content = null);

      private static async Task<ActionOutcome> InvokeAsync(object service, MethodInfo method, object?[] arguments,
         Type serviceType, string serviceAction, bool includeStackTrace, CancellationToken callerGone,
         CancellationToken abortToken) {
         try {
            var result = method.Invoke(service, arguments);

            if (result is Task task) {
               await task;

               var taskType = task.GetType();
               if (taskType.IsGenericType) {
                  var data = taskType.GetProperty("Result")?.GetValue(task);

                  if (method.ReturnType == typeof(Task<Stream>)) {
                     if (data is not Stream content) {
                        return new ActionOutcome(
                           BuildErrorActionResult("The action returned no content.", 500, serviceType, serviceAction),
                           null);
                     }

                     return new ActionOutcome(BuildSuccessActionResult(null, serviceType, serviceAction), null, content);
                  }

                  return new ActionOutcome(BuildSuccessActionResult(data, serviceType, serviceAction), null);
               }

               return new ActionOutcome(BuildSuccessActionResult(null, serviceType, serviceAction), null);
            }

            return new ActionOutcome(BuildSuccessActionResult(result, serviceType, serviceAction), null);
         }
         catch (Exception ex) {
            var actualException = ex is TargetInvocationException && ex.InnerException is not null
               ? ex.InnerException
               : ex;

            // An action that was cancelled did not fail, and calling it a fault would bury a routine
            // event - someone closing their application - under an Error line and a stack trace.
            // Which of the two cancellations it was decides whether anything is owed in reply.
            if (actualException is OperationCanceledException) {
               // Nobody is listening any more, so there is nothing to answer.
               if (callerGone.IsCancellationRequested) {
                  return new ActionOutcome(null, AbortReason.CallerGone);
               }

               // The time budget ran out while the caller was still waiting; silence would leave it
               // waiting for a reply that is never coming.
               if (abortToken.IsCancellationRequested) {
                  return new ActionOutcome(
                     BuildErrorActionResult(TimedOutMessage, 504, serviceType, serviceAction),
                     AbortReason.TimedOut);
               }

               // Neither of this dispatcher's two signals fired, so the action cancelled itself over
               // something of its own. Reporting that as a server timeout would blame the clock for
               // a decision the action made, so it falls through and is handled as any other fault.
            }

            // An action that knows why it failed says so with its own status code; everything else
            // is a fault this dispatcher has no reading of, and 500 is the honest answer for that.
            // Without the distinction a wrong password and a broken server look identical from the
            // outside, and the client has nothing to act on but the message text.
            var statusCode = actualException is ActionException actionException
               ? actionException.StatusCode
               : 500;

            return new ActionOutcome(
               BuildErrorActionResult(actualException, statusCode, serviceType, serviceAction, includeStackTrace),
               null);
         }
      }

      // Deliberately says nothing about which limit was reached or how long it was: the caller can
      // act on "it did not finish" alone, and the number is a property of this server's settings.
      private const string TimedOutMessage = "This action took too long and the server stopped waiting for it.";

      private static ActionResult BuildSuccessActionResult(object? data, Type serviceType, string serviceAction) {
         return new ActionResult {
            HasData = data is not null,
            Data = data,
            ValidResult = true,
            StatusCode = 200,
            ResultTypeFullName = data?.GetType().FullName ?? string.Empty,
            ServiceType = serviceType.FullName ?? serviceType.Name,
            ServiceAction = serviceAction
         };
      }

      private static ActionResult BuildErrorActionResult(string message, int statusCode, Type? serviceType,
         string serviceAction) {
         return new ActionResult {
            HasData = false,
            Data = null,
            ValidResult = false,
            ErrorMessage = message,
            StatusCode = statusCode,
            ResultTypeFullName = typeof(string).FullName ?? string.Empty,
            ServiceType = serviceType?.FullName ?? string.Empty,
            ServiceAction = serviceAction
         };
      }

      private static ActionResult BuildErrorActionResult(Exception exception, int statusCode, Type serviceType,
         string serviceAction, bool includeStackTrace) {
         return new ActionResult {
            HasData = false,
            Data = null,
            ValidResult = false,
            ErrorMessage = exception.Message,
            ErrorStackTrace = includeStackTrace ? exception.ToString() : null,
            StatusCode = statusCode,
            ResultTypeFullName = exception.GetType().FullName ?? exception.GetType().Name,
            ServiceType = serviceType.FullName ?? serviceType.Name,
            ServiceAction = serviceAction
         };
      }

      private static IResult ToJsonResult(ActionResult result) {
         return Results.Json(result, Defaults.ResponseJsonOptions, statusCode: result.StatusCode);
      }

      #endregion
      
      public CultureInfo EnUs => CultureInfo.GetCultureInfo("En-US");

      public Task<DateTime> GetDateStampAsync() => Task.FromResult(DateTime.Now);
   }
}