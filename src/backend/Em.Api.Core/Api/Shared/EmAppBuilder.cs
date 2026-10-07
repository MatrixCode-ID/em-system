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
      // The DI container is not built yet when AddService is called, so a standalone bootstrap logger
      // (not from the ServiceProvider) is used just for the registration process logs.
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

      /// <summary>Registers a module service and its actions. The service must carry <c>[Module]</c> and expose at least one action.</summary>
      public void AddService<T1, T2>() where T1 : class, IServices where T2 : ServicesBase, T1 =>
         AddService<T1, T2>(enforceClaims: true);

      /// <summary>
      /// Internal overload for the engine's own services, the only ones allowed to stand outside the
      /// per-action claim check. Deliberately not <c>public</c>: if module authors could call it, "forgot to
      /// give a claim" and "deliberately without a claim" could no longer be told apart from outside.
      /// </summary>
      /// <param name="enforceClaims">
      /// <c>false</c> only for services that <c>UseEm</c> itself registers before any module callback runs.
      /// There are four. Three of them - core, contact, credential - are not modules: their sensitive actions
      /// are already guarded by checks more precise than "any claim in this module" - the right over oneself
      /// or the administrator's right - and forcing that default here would mean every user must be given a
      /// claim just to sign in and load their own identity, which is not authorization but a meaningless
      /// second login requirement. The fourth, the CDN manager, names its claim in every action, so the
      /// "any claim" default adds nothing on top of it. The <c>claim</c> parameter of
      /// <c>[GetAction]</c>/<c>[PostAction]</c> still applies here - only the default is lifted.
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

      /// <summary>Sets the main database connection.</summary>
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
      /// First password for the <c>admin</c> account, used only while the database is still empty: the value
      /// is seeded as the default password on first startup, and after that whatever is stored in the
      /// database applies, not this value anymore.
      /// </summary>
      public string FirstTimeAdminPassword { get; set; } = "Admin1234";

      /// <summary>
      /// How long - in hours - a dead session row is kept before being discarded. Sessions are kept in the
      /// database precisely so there is a trace, so the row is deliberately not deleted right at expiry; this
      /// number decides how long that trace is.
      /// </summary>
      public int SessionTokenRetentionHour { get; set; } = 24 * 30;

      /// <summary>
      /// How long a <c>GET</c> action may work before the engine stops it by itself. This is a safety net
      /// for actions that run away - not a promise of response time to the caller: a request that finishes
      /// sooner never waits for this number.
      /// </summary>
      /// <remarks>
      /// Applies to <c>GET</c> only. <c>POST</c> actions are never cut off by the engine, because cutting a
      /// write off midway does not produce "not done" but "unknown" - the caller has no way to tell how far
      /// the write got. A single <c>GET</c> action may state its own number through
      /// <c>[GetAction(requestTimeoutSecond: ...)]</c>, and that number replaces the one here.
      /// <para>
      /// <c>TimeSpan.Zero</c> or a negative value turns it off entirely: no action is time-limited, and the
      /// only thing left is cutting off when the caller actually leaves.
      /// </para>
      /// </remarks>
      public TimeSpan HttpRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

      #region Business Task

      /// <summary>
      /// Folder where the server stores business task results that are data or files, one subfolder per
      /// task. An absolute path is used as-is; a relative path is resolved from the application content
      /// folder, like the CDN folder. The folder is created automatically at startup.
      /// </summary>
      /// <remarks>
      /// Only successful results survive a server restart; leftovers of failed or interrupted tasks are
      /// cleaned up at startup. Do not point it at a folder that is also used for anything else.
      /// </remarks>
      public string BusinessTaskCachePath { get; set; } = "./data/tasks";

      /// <summary>
      /// How long a business task that succeeded without a result, or was cancelled, stays visible before it
      /// disappears on its own. Tasks that failed, and tasks whose result can still be fetched, are not
      /// affected: both stay visible until cleaned up.
      /// </summary>
      public TimeSpan BusinessTaskGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

      /// <summary>
      /// Limit on the number of business tasks that may run at the same time, used as long as the limit has
      /// never been set through the Business Task Manager screen. Once it is set there, what applies is what
      /// is stored in the server metadata, not this value.
      /// </summary>
      public BusinessTaskLimit BusinessTaskDefaultLimit { get; set; } = new() {
         Mode = BusinessTaskLimitMode.Global,
         Limit = 2
      };

      #endregion

      /// <summary>
      /// Default for <see cref="ActionRateLimit"/>: 300 requests per minute, or an average of five per
      /// second. The number is chosen so normal use never touches it - a screen that opens a list together
      /// with all its lookups spends tens of requests in an instant, not hundreds - while anyone knocking
      /// from outside is still held back.
      /// </summary>
      public const int DefaultActionRateLimit = 300;

      private int _actionRateLimit = DefaultActionRateLimit;

      /// <summary>
      /// How many requests may come from one caller address in one minute; <c>-1</c> means no limit.
      /// Anything above that is answered <c>429</c> without its action getting to run - even before the
      /// identity is checked, so a flood of requests does not burden the database.
      /// </summary>
      /// <remarks>
      /// What is limited is the address, not the user: at this point the engine does not yet know who the
      /// caller is, and it is precisely requests without identity - successive password attempts, scanners
      /// sweeping routes - that most need to be held back. The consequence is that a whole office behind a
      /// single NAT shares one quota, so the number needs to be raised when many clients share an address.
      /// <para>
      /// The quota is counted with a sliding window, not a window reset all at once every minute: without
      /// that, a caller could use the full quota in the last second of a window and the next full quota in
      /// the first second of the following window - twice this limit in an instant, exactly where it should
      /// be guarded.
      /// </para>
      /// </remarks>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Thrown when the value is <c>0</c> - which would mean no request may enter, and that is never what
      /// is meant; write <c>-1</c> if the intent is to turn it off - or below <c>-1</c>.
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
      /// Registers one claim in the catalog. The catalog lives in code - there is no module table and no
      /// claim list table - so when a module is installed, its claims exist; when the module is removed, its
      /// claims disappear by themselves.
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the <see cref="ClaimAction.Key"/> is already registered (compared case-insensitively,
      /// following the collation of the <c>cUserClaimName</c> column).
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
      /// How many consecutive proxies may be trusted when reading the origin address of a request;
      /// <c>-1</c> means no limit. The default is <c>1</c>, which suits the usual "one reverse proxy in front
      /// of the server" setup.
      /// <para>
      /// The number only needs raising when requests really pass through several layers - for example a CDN
      /// in front of a load balancer in front of nginx - and it must equal the number of those layers. Raised
      /// beyond the real count, a layer that does not exist is trusted too, and the recorded address can be
      /// made up by the caller.
      /// </para>
      /// </summary>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Thrown when the value is <c>0</c> - which would mean no header is read at all, and that is clearer
      /// written by not registering any proxy - or below <c>-1</c>.
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
      /// Registers a proxy or load balancer that may be trusted when it states the real client address.
      /// Without this registration, a request arriving through a proxy is recorded with the proxy's address -
      /// not the user's - because on the network it really is the proxy that contacts the server.
      /// <para>
      /// Proxies running on the same machine (<c>127.0.0.0/8</c> and <c>::1</c>) are trusted by default, so
      /// a "nginx on the same machine as the API" setup does not need to register anything.
      /// </para>
      /// </summary>
      /// <param name="addresses">
      /// IP addresses one by one (<c>"10.0.0.100"</c>) or whole networks in CIDR notation
      /// (<c>"10.0.0.0/8"</c>). Use CIDR when the proxy address may change, as with load balancers in a
      /// cloud environment.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Thrown when a value is empty or is neither a readable IP address nor CIDR. Checked here, at
      /// registration, so a typo is found when the server starts - not when the first request arrives and
      /// the address is silently wrong.
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
      /// Trusts the client address stated by the <c>X-Forwarded-For</c> header from wherever the request
      /// came, without checking who forwarded it.
      /// <para>
      /// This opens address spoofing: the header comes from the caller, so once nobody checks it, anyone can
      /// claim any address just by attaching one header line - and that address is what ends up in the log.
      /// It is appropriate only when the API truly cannot be reached except through a proxy, for example
      /// inside a closed container network whose proxy address keeps changing so it cannot be registered.
      /// As long as the proxy address is known, <see cref="TrustProxy"/> is always the better choice.
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
      /// Default size limit - in megabytes - for a single file uploaded to the CDN, used by
      /// <see cref="EnableCdn(string)"/> when it does not state its own number.
      /// </summary>
      public const int DefaultCdnMaxFileSizeMb = 20;

      // null = CDN off. Stored raw as written in Program.cs; resolving it to an absolute path waits for
      // BuildApp, because ContentRootPath is only known there.
      internal string? CdnRootPath { get; private set; }

      // In bytes, the result of maxFileSizeMb * 1024 * 1024.
      internal long CdnMaxFileSize { get; private set; }

      /// <inheritdoc cref="EnableCdn(string,int)" />
      public void EnableCdn(string rootPath) =>
         EnableCdn(rootPath, DefaultCdnMaxFileSizeMb);

      /// <summary>
      /// Turns on the CDN: the content of the <paramref name="rootPath"/> folder is served as-is at the
      /// <c>/cdn/...</c> address to anyone, without login - complete with folder listings when opened from a
      /// browser and segmented/resumable download support (HTTP Range) for download managers. Without this
      /// call, every address under <c>/cdn</c> is answered 404.
      /// <para>
      /// Adding and removing its content does not go through that public address, but through the CDN
      /// Manager screen in the application, which is open only to users who are granted the right to manage
      /// the CDN.
      /// </para>
      /// </summary>
      /// <param name="rootPath">
      /// The folder to serve. An absolute path is used as-is; a relative path (including <c>./...</c>) is
      /// resolved from the application content folder. The folder is created automatically at startup if it
      /// does not exist.
      /// </param>
      /// <param name="maxFileSizeMb">
      /// Size limit of a single file that may be uploaded through the CDN Manager, in megabytes
      /// (1 MB = 1024 × 1024 bytes). Files already in the folder are still served whatever their size; only
      /// uploads are limited.
      /// </param>
      /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is empty.</exception>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Thrown when <paramref name="maxFileSizeMb"/> is <c>0</c> or negative.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Thrown when the CDN was already enabled - one application has only one CDN folder.
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
      /// Default validity period - in days - for a debug token registered without stating its own number.
      /// </summary>
      public const int DefaultDebugTokenDays = 60;

      // Below this, RSA is too weak to be trusted as proof of identity, and refusing it here is far better
      // than discovering it on the first request.
      private const int MinimumDebugKeySizeBits = 2048;

      /// <inheritdoc cref="AddDebugToken(string,string,int)" />
      public void AddDebugToken(string name, string publicKey) =>
         AddDebugToken(name, publicKey, DefaultDebugTokenDays);

      /// <summary>
      /// Registers one developer public key, so requests carrying a debug token signed by that key are
      /// accepted without a password or access token. One key per developer, so access can be revoked one by
      /// one: delete its line and redeploy.
      /// <para>
      /// Only the public key is written here, so it is safe to keep in source. The private key never reaches
      /// the server - it stays on the developer's machine and is only used to sign tokens.
      /// </para>
      /// </summary>
      /// <param name="name">
      /// Name of the key, free but must be unique and exactly the same as the name the developer side uses
      /// when signing. This name is also what appears in the log every time its token is used.
      /// </param>
      /// <param name="publicKey">RSA public key of at least 2048 bits, as Base64 of the DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      /// <param name="days">
      /// How long a token is still accepted counted from when it was issued; <c>-1</c> means no limit. This
      /// number does not restrict the developer - they can always issue a new token - what it restricts is a
      /// leaked token: a string left behind in Postman history, in a log, or in notes sent to someone else,
      /// dies by itself after this limit.
      /// </param>
      /// <exception cref="ArgumentException">
      /// Thrown when the name or public key is empty, the name is already used, the key is not a readable
      /// RSA public key, its size is below 2048 bits, its value equals an already registered key, or
      /// <paramref name="days"/> is outside the allowed values.
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

         // Two names with the same key make the log untrustworthy: the signature matches both, so the recorded
         // name is not necessarily the name of the user.
         if (DebugTokenKeys.FirstOrDefault(r => string.Equals(r.Key.PublicKey, publicKey, StringComparison.Ordinal))
             is { } duplicate) {
            throw new ArgumentException(
               $"Debug token key '{name}' uses the same public key as '{duplicate.Name}'. Every key must be unique.",
               nameof(publicKey));
         }

         // 0 would silently turn the key off: every issued token expires immediately. If the intent is to turn
         // it off, delete its line - much clearer to read than a number nobody sees.
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
      /// Reads the public key once here, at registration, so a mistyped or corrupt key is found when the
      /// server starts - not when the first request arrives.
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
      /// Reads the action markers from a method: its HTTP method and whether the action is public. The
      /// attribute is looked up directly on the method, then - if absent - on the interface method it
      /// implements. Returns <c>null</c> when the method is not an action.
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
            // There is no time limit for POST: the engine never cuts off a write action, and [PostAction]
            // provides no way to state one.
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