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
   /// <summary>The application host: builds the web application, registers engine services, and dispatches actions.</summary>
   public class EmApp : IEmApp
   {
      /// <summary>
      /// Builds the host: creates the web application, registers the engine services, runs the module
      /// <paramref name="appBuilder"/> callback, and freezes the registrations.
      /// </summary>
      /// <param name="args">Command-line arguments passed to the web application builder.</param>
      /// <param name="appBuilder">Callback in which the host registers its modules and settings.</param>
      /// <returns>The built application, ready for <see cref="Run"/>.</returns>
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> appBuilder) {
         var app = new EmApp(WebApplication.CreateBuilder(args));
         // One line per entry, with a timestamp. The default ASP.NET Core console splits every entry into
         // two lines, which makes back-to-back request traces hard to read in pairs.
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
         // Frozen from here on, like _actions: developer public keys may only come from Program.cs,
         // never from the database, and never change while the server is alive.
         app._debugTokenKeys = builder.DebugTokenKeys.ToArray();
         // Frozen as well: the claim catalog may only come from Program.cs through AddClaims, and
         // never changes while the server is alive.
         app._claims = builder.ClaimActions.ToArray();
         EnsureClaimModulesAreRegistered(app._claims, builder.ActionDefinitions);
         app._firstTimeAdminPassword = builder.FirstTimeAdminPassword;
         app.SessionTokenRetentionHour = builder.SessionTokenRetentionHour;
         app._httpRequestTimeout = builder.HttpRequestTimeout;
         app._rateLimiter = CreateRateLimiter(builder.ActionRateLimit);
         // The stack trace is sent to the client only in Development. Otherwise internal details (file paths,
         // assembly names, query structure) are none of the caller's business, authenticated or not.
         app._includeStackTrace = app.Builder.Environment.IsDevelopment();
         // The host is built here, not in Run, so the service registration window closes exactly after
         // the builder callback finishes: from this line on the service collection is locked and the
         // ServiceProvider is guaranteed available to anyone holding the app.
         app._webApplication = app.Builder.Build();
         app._rootServiceProvider = app._webApplication.Services;
         app._httpContextAccessor = app._rootServiceProvider.GetRequiredService<IHttpContextAccessor>();
         return app;
      }

      /// <summary>
      /// Registers the built-in core services that are always available before the module callback runs.
      /// Every service owned by <c>Em.Api.Core</c> is registered here so they are gathered in one place.
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

         // Scoped, so one request = one object: its scope is the request's scope, and the object is
         // immutable so nothing can overwrite another. Whatever is resolved while an action runs always
         // gets the populated one, because the gate runs first; outside a request, ActionRequest.None
         // comes out, which rejects every permission check with 401. A business task's scope has no
         // request, so what comes out there is the task's starter, carried through BusinessTaskStarter.
         builder.Services.AddScoped<BusinessTaskStarter>();
         builder.Services.AddScoped(sp =>
            sp.GetRequiredService<BusinessTaskStarter>().Request ??
            sp.GetRequiredService<EmApp>().CurrentRequest ??
            ActionRequest.None);

         // enforceClaims: false - none of the three is a module, and their sensitive actions are already
         // guarded by checks more precise than "any claim in this module": the right to read one's own
         // data, and the administrator's right to write sensitive data. The full reasoning is in the
         // internal AddService overload that takes this parameter.
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
      /// Prepares file content storage according to <c>EmAppBuilder.AddLocalBinaryStorage</c>: the path
      /// is made absolute (a relative path is resolved from the application content folder, like the
      /// CDN) and the folder is created when missing. When the application does not enable it, nothing
      /// is registered - and consumers fail when they ask for <see cref="IBinaryStorage"/>, not when
      /// they write their first file.
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
      /// Freezes every approval flow registered by modules, then registers them as a single catalog.
      /// Always registered - even when there is no flow at all - so the approval screens and actions can
      /// always be created and answer on their own that there is nothing.
      /// </summary>
      private void ApplyApprovalRegistration(EmAppBuilder builder) {
         ApprovalStartupChecks.VerifyDatabases(builder.ApprovalFlows, builder);
         Services.AddSingleton(new ApprovalRegistry(builder.ApprovalFlows));
      }

      /// <summary>
      /// Prepares reading of the <c>X-Forwarded-*</c> headers, the way a proxy or load balancer tells
      /// the real client address. Without this every request passing through a proxy is recorded with the
      /// proxy's address, so logs and address-based checks lose their meaning.
      /// </summary>
      /// <remarks>
      /// Only proxies registered through <c>EmAppBuilder.TrustProxy</c> are trusted, plus the loopback
      /// that is trusted by default. This is deliberate: the header comes from the caller, so if anyone
      /// could send it, anyone could claim any address just by attaching one header line.
      /// </remarks>
      private void ApplyProxyHeaderRegistration(EmAppBuilder builder) {
         Services.Configure<ForwardedHeadersOptions>(options => {
            // The default is None - the middleware is on but reads nothing - so the headers to read
            // must be named explicitly here.
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
      /// Prepares the CDN according to <c>EmAppBuilder.EnableCdn</c>: the path is made absolute (a
      /// relative path is resolved from the application content folder) and the folder is created when
      /// missing. The server's request body size limit is left alone: uploads arrive as a stream, and
      /// the CDN size limit is enforced while that stream is copied. When the CDN is off a disabled store
      /// is still registered, so its management service can always be created and answer 404 itself.
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
      /// Prepares the container registry according to <c>EmAppBuilder.AddContainerRegistry</c>: the blob
      /// storage folder is made absolute (a relative path is resolved from the application content
      /// folder) and created when missing. Like the CDN, when the registry is off a disabled store is
      /// still registered so <c>CtnServices</c> can always be created and answer 404 itself.
      /// </summary>
      private void ApplyContainerRegistryRegistration(EmAppBuilder builder) {
         _ctnStore = _storageSettings.Active(false).Enabled
            ? CtnBlobStore.Create(_storageSettings.Active(false).Directory, Builder.Environment.ContentRootPath)
            : CtnBlobStore.Disabled;
         Services.AddSingleton(_ctnStore);
      }

      /// <summary>
      /// Prepares the business task runner: its cache folder is made absolute (a relative path is
      /// resolved from the application content folder, like the CDN) and registered as a singleton. Its
      /// contents are only loaded in <see cref="Run"/>, because reading its limits needs the database.
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

      /// <summary>Seeds the core metadata, maps the endpoints, and runs the web application until it stops.</summary>
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
         // Frontmost in the pipeline, because everything that reads the caller's address - the request
         // trace, the identity gate, any action behind them - must already see the correct address.
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
      /// Maps the public <c>/cdn</c> path: files and folder listings, without identity and without a
      /// request quota - both belong to the dispatcher, and this path is deliberately outside it. Mapped
      /// before the fallback, which would otherwise answer 200 for any address under <c>/cdn</c>.
      /// </summary>
      /// <remarks>
      /// Range, <c>If-Range</c>, <c>ETag</c>, <c>HEAD</c>, 206 and 416 are all handled by
      /// <c>StaticFileMiddleware</c>; there is no hand-written Range code here. There is no rate limit
      /// because download managers open many Range connections at once for a single file.
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
      /// Maps the container registry <c>/v2</c> path, outside the action dispatcher like <c>/cdn</c>: it
      /// is not subject to <c>HttpRequestTimeout</c> or <c>ActionRateLimit</c> - large layers and many
      /// parallel connections are its normal use. Authentication is by robot (Basic), checked in
      /// <see cref="CtnRegistryEndpoint"/>. When the registry is off the answer is a plain 404.
      /// </summary>
      private void MapContainerRegistry(WebApplication app) {
         app.Map(CtnBlobStore.PublicRequestPath, branch => branch.Run(CtnRegistryEndpoint.HandleAsync));
      }

      /// <summary>
      /// Seeds the initial core metadata before the first request is served. So far only the built-in
      /// administrator account is seeded - without it a freshly created database has no account that
      /// can sign in.
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

      // Service registration may only happen during BuildApp. Once the host is built, this collection
      // is locked by ASP.NET Core, so it is deliberately not exposed through IEmApp.
      internal IServiceCollection Services => Builder.Services;

      private WebApplication _webApplication = null!;

      private IServiceProvider _rootServiceProvider = null!;

      private IHttpContextAccessor _httpContextAccessor = null!;

      /// <inheritdoc />
      /// <remarks>
      /// EmApp lives as a singleton, so the provider is deliberately re-read every time this property
      /// is accessed instead of being stored once in a field. While a request runs, the request's own
      /// provider is used, so scoped services follow that request's lifetime. Outside a request the root
      /// provider is used, where scoped services indeed cannot be resolved - for such needs create your
      /// own scope through <c>ServiceProvider.CreateScope()</c>.
      /// </remarks>
      public IServiceProvider ServiceProvider =>
         _httpContextAccessor.HttpContext?.RequestServices ?? _rootServiceProvider;

      // Key under which the gate stores the request info in HttpContext.Items. Private: the only writer
      // is ProcessRequest, and readers only need CurrentRequest below.
      private const string RequestItemKey = "Em.ActionRequest";

      /// <summary>
      /// Info about the request being handled, or <c>null</c> when there is no request - startup, seeding,
      /// background work. Read-only: the only thing that fills it is the gate in <see cref="ProcessRequest"/>,
      /// and <see cref="ActionRequest"/> itself is immutable.
      /// </summary>
      /// <remarks>
      /// This is not the route for modules. Module services read it through <c>ServicesBase.Request</c>, and
      /// helper classes ask for it in their constructor through DI; this property exists for the engine and
      /// for things that genuinely cross requests - audit writers, log enrichers, DI registration - and its
      /// type is deliberately nullable so callers are forced to think about the "no request" state.
      /// <para>
      /// It is re-read from <c>HttpContext.Items</c> on every access instead of being stored in a field, for
      /// exactly the same reason as <see cref="ServiceProvider"/> above: EmApp lives as a singleton, so a
      /// field would let two concurrent requests overwrite each other's identity.
      /// </para>
      /// </remarks>
      public ActionRequest? CurrentRequest =>
         _httpContextAccessor.HttpContext?.Items[RequestItemKey] as ActionRequest;

      private ActionDefinition[] _actions = [];

      private DebugTokenKey[] _debugTokenKeys = [];

      private ClaimAction[] _claims = [];

      // Set in BuildApp; a disabled store when the CDN is not enabled, never null afterwards.
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

      // Set in BuildApp, loaded in Run.
      private BusinessTaskRunner _businessTasks = null!;

      /// <summary>
      /// Catalog of every claim registered through <c>EmAppBuilder.AddClaims</c>, frozen since
      /// <see cref="BuildApp"/>. Read by <c>GetMeta_AllClaimActions</c> and by the claim management UI in
      /// a later phase. The name deliberately matches the client's <c>EmApp.AllClaims</c> exactly: two
      /// different types in two different assemblies, one meaning, one name.
      /// </summary>
      public IReadOnlyList<ClaimAction> AllClaims => _claims;

      /// <summary>
      /// Makes sure every registered claim points to a module that is actually installed - otherwise the
      /// claim was registered for a service whose <c>AddService</c> was never called (e.g. its line is
      /// commented out), and rights granted through that claim would never be used.
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

      // Time limit for GET actions, set from EmAppBuilder.HttpRequestTimeout when the application is
      // built. An action that states its own number through [GetAction] uses that number, not this one.
      private TimeSpan _httpRequestTimeout;

      // Request quota per caller address, or null when EmAppBuilder.ActionRateLimit turns it off. One
      // object for the whole application - the quota has to be counted across requests - and it is safe
      // to use from several requests at once.
      private PartitionedRateLimiter<HttpContext>? _rateLimiter;

      /// <summary>
      /// How long - in hours - a dead session row is kept before being discarded. Set from
      /// <c>EmAppBuilder.SessionTokenRetentionHour</c> when the application is built.
      /// </summary>
      public int SessionTokenRetentionHour { get; private set; }

      // First password of the admin account, used only while seeding in Run(): once the value is in the
      // database, the stored one is what applies. Internal, not public - this is the plain text of a
      // password, and no caller outside the engine needs to read it.
      private string _firstTimeAdminPassword = string.Empty;

      #endregion

      private EmApp(WebApplicationBuilder builder) {
         Builder = builder;
      }

      #region Request Processing

      /// <summary>Handles one action request: rate limit, identity gate, routing, binding, execution, and the answer.</summary>
      public async Task<IResult> ProcessRequest(string module, string action, HttpContext http) {
         var routeLabel = $"{module}/{action}";

         // Recorded first, even before the identity gate: a request that is later refused must still show
         // that it arrived, because that is exactly what is looked for when someone is knocking from outside.
         var trace = RequestTrace.Begin(http, routeLabel);

         // The request quota is checked before the identity gate, and therefore before any query runs: when
         // a flood really arrives, the last thing that should happen is every request in it burdening the
         // database before being refused.
         if (IsRateLimited(http)) {
            return Reject(BuildErrorActionResult(TooManyRequestsMessage, 429, null, routeLabel));
         }

         // The identity gate runs first, before the route itself is looked up. That way a request carrying a
         // forged debug token is always answered exactly like a nonexistent action - whatever route it targets
         // - so the mechanism cannot be probed from outside.
         var gate = await ResolveCallerAsync(http, routeLabel);

         // Placed in HttpContext.Items - not in a field - because EmApp lives as a singleton: a field would
         // let two concurrent requests overwrite each other's identity, and the symptom would only show up
         // under load. Written before the refusal is checked, so anything that writes a trace can still read
         // a refused request.
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

         // Checked after the route is found, not before, because "may be called without identity" belongs to
         // the action. The consequence is that an existing but closed action answers 401 while a nonexistent
         // one answers 404, so the two can be told apart from outside. That is accepted: what truly must stay
         // undetectable is the debug token mechanism, and that path keeps answering 404 whatever the route
         // because the gate above runs first.
         if (!actionDef.IsPublicAction && !gate.Request.IsAuthenticated) {
            return Reject(BuildErrorActionResult(
               "This action requires a signed-in caller.", 401, actionDef.Type, routeLabel));
         }

         // Placed after the block above, not replacing it, so the order of failures stays correct: no identity
         // answers 401, an identity with insufficient rights answers 403. A client that refreshes its token
         // on every 401 must not be sent chasing a new token for a request that will never be permitted -
         // exactly the same reason ActionRequest.RequireAdmin already uses.
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

         // One cancellation source for this action. A caller that leaves is always part of it; the time budget
         // is attached on top of it a few lines further down, and only for GET. Created before the two
         // branches below because its content is the same - what differs is only whether the timer is set.
         using var abort = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
         service.AbortToken = abort.Token;

         if (actionDef.HttpMethod == HttpMethod.Get) {
            if (!TryBindArguments(actionDef.MethodInfo, http.Request.Query, out var arguments, out var bindError)) {
               return Reject(BuildErrorActionResult(bindError ?? "Invalid request.", 400, actionDef.Type, routeLabel));
            }

            trace.Processing();

            // Counted from here, not from when the request arrived, so what is limited is really the action's
            // working time - the same as what the request trace measures on the line above. The "> Zero"
            // condition also covers two different forms of "no limit": TimeSpan.Zero from EmAppBuilder and
            // Timeout.InfiniteTimeSpan from [GetAction].
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

            // Deliberately no CancelAfter. Cutting a write off midway does not produce "not done" but "unknown",
            // and the caller has no way to tell how far the write got. All that is left on this token is the
            // caller leaving, and reacting to it is the action author's decision - not the engine's.
            var outcome = await InvokeAsync(service, actionDef.MethodInfo, arguments, actionDef.Type, routeLabel,
               _includeStackTrace, http.RequestAborted, abort.Token);
            return Complete(outcome);
         }

         return Reject(BuildErrorActionResult("Unsupported HTTP method.", 405, actionDef.Type, routeLabel));

         // These two wrappers exist so every exit above is recorded without writing the line each time:
         // what is refused before the action runs goes through Reject, and what did run - successful or
         // not - goes through Complete.
         IResult Reject(ActionResult result) {
            trace.Rejected(result);
            return ToJsonResult(result);
         }

         IResult Complete(ActionOutcome outcome) {
            if (outcome.Abort is { } reason) {
               trace.Aborted(reason);

               // What was cancelled because of the time budget still carries an answer - the caller is still
               // waiting at the other end. What was cancelled because the caller left does not: the socket is
               // gone, and writing to it would only produce a second failure.
               return outcome.Result is { } answer ? ToJsonResult(answer) : Results.Empty;
            }

            // An action may well complete entirely after its caller has left - and for write actions that is
            // what is wanted. The work is still valuable, but the answer no longer has a destination.
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

      // Deliberately does not state the limit or when it recovers: that number belongs to this server's
      // settings, and what the caller should do is the same whatever it is. The actual wait time is still
      // sent, through the Retry-After header, where every client already knows to look.
      private const string TooManyRequestsMessage =
         "Too many requests from this address. Wait a moment before trying again.";

      // Substitute name for requests that arrive without an address - over a unix socket, or from an
      // in-memory host. They all share one quota, which is intended: callers without an address cannot
      // be told apart.
      private const string UnknownCallerPartition = "unknown";

      // Length of the quota window and the number of pieces it is divided into. The more pieces, the
      // smoother the window slides, and the more has to be remembered per address; six is enough to cover
      // a spike at the window edge without getting expensive.
      private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

      private const int RateLimitSegments = 6;

      // One window piece - ten seconds. This is also the shortest time until some quota returns: as soon
      // as the oldest piece leaves the window, the quota used in it recovers.
      private static readonly TimeSpan RateLimitSegment = RateLimitWindow / RateLimitSegments;

      /// <summary>
      /// Builds the request quota counter, one quota per caller address, or <c>null</c> when
      /// <c>EmAppBuilder.ActionRateLimit</c> turns it off.
      /// </summary>
      /// <remarks>
      /// The address is read from the connection, not from a header, and it is already the correct address
      /// even when the request arrives through a proxy: <c>UseForwardedHeaders</c> runs first in the
      /// pipeline, so by the time this line runs the proxy address has been replaced by the real client
      /// address - as long as the proxy is registered through <c>EmAppBuilder.TrustProxy</c>. Otherwise all
      /// requests through that proxy share one quota, and one excessive client exhausts the quota of
      /// everyone behind it.
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
                  // No queue: whatever exceeds the quota is refused immediately, not held waiting. Holding it would
                  // mean requests in flight pile up on the server - exactly the load this limit is meant to avoid.
                  QueueLimit = 0,
                  AutoReplenishment = true
               }));
      }

      /// <summary>
      /// Whether this request exceeds its address's quota. If so, the wait time is also written to the
      /// <c>Retry-After</c> header before the <c>429</c> answer is built.
      /// </summary>
      private bool IsRateLimited(HttpContext http) {
         if (_rateLimiter is not { } limiter) {
            return false;
         }

         // The lease is released immediately, not held while the request runs. What the sliding window counts
         // is the arrival of requests, not how many are being handled - so holding it longer changes nothing
         // except extending the object's lifetime.
         using var lease = limiter.AttemptAcquire(http);
         if (lease.IsAcquired) {
            return false;
         }

         // The limiter does not always state its own wait time, and a caller that is not told when it may
         // return usually retries immediately - exactly what is being refused. One window piece is used
         // instead: not a guess, it really is the earliest moment some quota recovers.
         var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : RateLimitSegment;

         http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
         return true;
      }

      #endregion

      #region Authorization Gate

      // A little slack for the clock difference between the developer machine and the server, following
      // the same number as the access token check: without it a token issued by a machine whose clock is
      // a few seconds ahead reads as "issued in the future".
      private static readonly TimeSpan DebugTokenClockSkew = TimeSpan.FromSeconds(30);

      private const string BearerPrefix = "Bearer ";

      /// <summary>
      /// Result of the gate: the request info together with the caller's identity, or - when
      /// <see cref="Rejection"/> is set - the answer that must be sent back without the action getting
      /// to run.
      /// </summary>
      private readonly record struct CallerGate(ActionRequest Request, ActionResult? Rejection);

      /// <summary>
      /// Whether the caller is entitled to call this action. Called by the gate before the action runs,
      /// not from inside the action - which is why it returns <c>bool</c> instead of throwing like
      /// <c>ActionRequest.RequireAdmin</c>: at this point the pattern is to refuse directly, and making the
      /// two the same would mislead a reader trying to find out whether a rule is checked at the gate or
      /// inside the action.
      /// </summary>
      /// <remarks>
      /// The order of checks mirrors <c>NavigationAccess.CanOpen</c> on the UI side: debug token path first,
      /// administrator second, only then the caller's own rights. One and the same rule must not be checked
      /// in a different order on the two sides - if it differs, the screens that may be opened and the
      /// actions that may be called can stop matching.
      /// </remarks>
      private static bool HasRequiredClaim(ActionRequest request, ActionDefinition actionDef) {
         if (request.IsDebugRequest) return true;

         if (request.IsAdmin) return true;

         if (actionDef.RequiredClaim is { } required) {
            return request.Claims.Any(r =>
               string.Equals(r.ModuleName, actionDef.Module, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(r.Name, required, StringComparison.OrdinalIgnoreCase));
         }

         // Only here does the engine-service check stop, not on the first line: what is lifted is only the
         // default condition below, while explicitly named claims still apply.
         if (!actionDef.EnforcesClaims) return true;

         return request.Claims.Any(r =>
            string.Equals(r.ModuleName, actionDef.Module, StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Determines the caller's identity from the headers it carries. The debug token is checked first,
      /// then the access token; the <c>X-Em-User</c> header never becomes a source of identity by itself,
      /// because if it were ever trusted alone, anyone could become any owner just by naming an id or
      /// account name.
      /// </summary>
      private async Task<CallerGate> ResolveCallerAsync(HttpContext http, string routeLabel) {
         var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<EmApp>();
         var debugToken = http.Request.Headers[Defaults.DebugTokenHeader].ToString();
         UserHeaderProtocol.TryRead(http.Request.Headers[Defaults.UserHeader].ToString(),
            out var headerUserId, out var headerAccount);

         // Info that applies to any request, filled once here so each branch below only has to add its own
         // identity.
         var request = new ActionRequest {
            RouteLabel = routeLabel,
            CallerAddress = http.Connection.RemoteIpAddress?.ToString(),
            ReceivedAtUtc = DateTime.UtcNow
         };

         if (!string.IsNullOrWhiteSpace(debugToken)) {
            if (!TryVerifyDebugToken(debugToken, out var keyName, out var refusal)) {
               // Silent to the client, chatty to the server log: the real reason is written only here, while what
               // is sent back is exactly the same as the answer for a nonexistent action.
               logger.LogWarning(
                  "Debug token refused for '{Route}' from {Caller}: {Reason}.",
                  routeLabel, CallerAddress(http), refusal);
               return new CallerGate(request, ActionNotFoundResult(routeLabel));
            }

            return await ResolveDebugCallerAsync(http, request, keyName, headerUserId, headerAccount, logger);
         }

         // Tripwire. No sign-in path can produce this account, so a request that names it - by account name
         // or by id - without a debug token is certainly forged.
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
      /// Determines the identity for a request whose debug token has passed. Without <c>X-Em-User</c> the
      /// debugger account is used; with another account, the developer impersonates that account - and its
      /// rights are read as-is from its own data, not forced to administrator, because forcing it would
      /// make access-rights testing meaningless.
      /// </summary>
      /// <remarks>
      /// The impersonated account may be named by id, by account name, or both. When both are named, the
      /// id is what is used for the lookup - it is the permanent one - and the account name is then matched
      /// against the row found. A mismatch is refused, not ignored: it means the client composed the header
      /// from two different sources, and guessing which is right is worse than stopping.
      /// </remarks>
      private async Task<CallerGate> ResolveDebugCallerAsync(HttpContext http, ActionRequest request, string keyName,
         string? headerUserId, string? headerAccount, ILogger logger) {
         // After the token has passed, nothing needs hiding anymore: the failures below are answered with a
         // message that states the cause, not 404, so developers do not have to guess.
         request = request with { Source = CallerSource.DebugToken, DebugKeyName = keyName };

         // With no header at all, the debugger account is used. Deliberately the debugger account, not the
         // built-in administrator: the trace must be able to tell a developer tinkering from a real
         // administrator.
         if (headerUserId is null && headerAccount is null) {
            return Accepted(request with {
               cUserId = Defaults.DebuggerUserId,
               cUserAccount = Defaults.DebuggerUserAccount,
               IsAdmin = true
            });
         }

         // System accounts are recognized first and never looked up as user rows: neither has a row. Both
         // match whether named by id or by account name.
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

            // The built-in administrator account has no user row, so its state is read from its switch.
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

         // Whether the header names a particular system account - by id, by account name, or both.
         // consistent is false when both are named but point to different accounts.
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
            // During impersonation, the data rows written carry the impersonated account's trace and cannot be
            // told apart from real work. Until an audit system exists, this log line is the only trace that the
            // one who did it was actually a developer.
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
      /// Reads the identity from the access token, if present. An invalid access token is not refused here,
      /// it just yields no identity - non-public actions are the ones that refuse it themselves. What still
      /// passes is public actions, and that is intended: the refresh token is sent precisely when the access
      /// token is already dead.
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

         // The token is certainly the winner - there are legitimate conditions that make the two differ, for
         // example a request still in flight while the user switches - so the difference is just written to
         // the log. What is compared now is two ids, not account names, so no extra query is needed.
         if (headerUserId is not null && !string.Equals(headerUserId, validation.cUserId, StringComparison.Ordinal)) {
            logger.LogDebug(
               "Header '{Header}' named '{HeaderUserId}' for '{Route}'; identity comes from the access token ({UserId}).",
               Defaults.UserHeader, headerUserId, request.RouteLabel, validation.cUserId);
         }

         // The caller's rights are loaded here, not at the point of checking, so any action that wants to read
         // them itself gets the same content the gate used. Administrators are skipped because they pass
         // without their rights being read - paying one query for an answer that will never be used is
         // pointless. The debug token path never reaches here.
         ClaimAction[] claims = [];
         if (!validation.IsAdmin && validation.cUserId is { } cUserId) {
            claims = await UserClaimLoader.LoadAsync(
               http.RequestServices.GetRequiredService<ApiCoreContext>(),
               cUserId, await GetDateStampAsync(), http.RequestAborted);
         }

         // cUserAccount is deliberately left empty: the token carries only the id, and translating it to an
         // account name would cost one extra query on every request. The name attached in the header is not
         // used to fill it either - it comes from the caller, not from data.
         return new CallerGate(request with {
            Source = CallerSource.AccessToken,
            cUserId = validation.cUserId,
            cUserSessionId = validation.cUserSessionId,
            IsAdmin = validation.IsAdmin,
            Claims = claims
         }, null);
      }

      /// <summary>
      /// Checks the debug token: its shape, key name, signature, and validity period. The reason for a
      /// refusal is returned through <paramref name="refusal"/> to be written to the server log only.
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
      /// The single answer for every refusal on the debug token path - exactly the same, down to the wording,
      /// as the answer for an action that really does not exist. Any difference would announce its own
      /// existence.
      /// </summary>
      private static ActionResult ActionNotFoundResult(string routeLabel) =>
         BuildErrorActionResult($"Action '{routeLabel}' was not found.", 404, null, routeLabel);

      // One source of the address wording for the gate log and the request trace log, so both state exactly
      // the same address - including when the request arrives through a proxy.
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
      /// Result of one action: the answer to send, and - when the action was cancelled - the reason.
      /// Two states that may fall outside the usual path: cancelled without an answer (the caller left), and
      /// cancelled with an answer (time budget exhausted, the caller still waiting).
      /// </summary>
      /// <remarks>
      /// <see cref="Content"/> is set only for actions that return <c>Task&lt;Stream&gt;</c> and completed
      /// successfully: what is sent is then the stream content, and <see cref="Result"/> is used only for the
      /// request trace.
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
      
      /// <inheritdoc />
      public CultureInfo EnUs => CultureInfo.GetCultureInfo("En-US");

      /// <inheritdoc />
      public Task<DateTime> GetDateStampAsync() => Task.FromResult(DateTime.Now);
   }
}