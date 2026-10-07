using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Api.Shared;

/// <summary>
/// Host API configuration that differs per environment, in one standard JSON file
/// (<see cref="FileName"/>). The file is kept outside the repo (the local artifacts folder) because it
/// holds secrets; the features and modules that are turned on are still written in the host code.
/// Apply it to the builder with <see cref="EmAppBuilder.ApplyConfig"/>.
/// </summary>
/// <remarks>
/// A numeric value left <c>null</c> uses the default of <see cref="EmAppBuilder"/>, so the file only
/// needs to contain what is really meant to change.
/// </remarks>
public sealed class EmApiConfig
{
   /// <summary>Standard file name of the host API configuration.</summary>
   public const string FileName = "emapi-config.json";

   private static readonly JsonSerializerOptions JsonOptions = new() {
      PropertyNameCaseInsensitive = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      Converters = { new JsonStringEnumConverter() },
   };

   /// <summary>Database connection settings.</summary>
   public DatabaseSection Database { get; set; } = new();
   /// <summary>Built-in administrator settings.</summary>
   public AdminSection Admin { get; set; } = new();
   /// <summary>Debug token keys to register.</summary>
   public List<DebugTokenSection> DebugTokens { get; set; } = [];
   /// <summary>HTTP, rate limit, and proxy settings.</summary>
   public HttpSection Http { get; set; } = new();
   /// <summary>Session settings.</summary>
   public SessionSection Session { get; set; } = new();
   /// <summary>Storage paths.</summary>
   public StorageSection Storage { get; set; } = new();

   /// <summary>
   /// Switches of optional modules by name (e.g. <c>"modules": { "test": true }</c>). The engine does not
   /// read them itself; the host decides to install a module through <see cref="IsModuleEnabled"/>. Names
   /// are case-insensitive.
   /// </summary>
   public Dictionary<string, bool> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// Whether module <paramref name="name"/> is turned on. A module that is not mentioned uses
   /// <paramref name="defaultValue"/>, so an optional module should stay off unless turned on explicitly.
   /// </summary>
   public bool IsModuleEnabled(string name, bool defaultValue = false) {
      // System.Text.Json replaces the dictionary (and its comparer) on load, so match the name by hand.
      foreach (var (key, enabled) in Modules) {
         if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return enabled;
      }

      return defaultValue;
   }

   /// <summary>Reads the configuration file; comments and trailing commas are allowed.</summary>
   /// <exception cref="InvalidOperationException">Thrown when the file content is not readable JSON.</exception>
   public static EmApiConfig Load(string path) {
      try {
         using var stream = File.OpenRead(path);
         return JsonSerializer.Deserialize<EmApiConfig>(stream, JsonOptions) ?? new EmApiConfig();
      } catch (JsonException ex) {
         throw new InvalidOperationException($"'{path}' is not a valid {FileName}: {ex.Message}", ex);
      }
   }

   /// <summary>
   /// Checks required values and values that are clearly wrong, so mistakes are found at startup with a
   /// message that names the key. The format of the debug public key and the proxy addresses are checked by
   /// the builder.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown at the first invalid value.</exception>
   public void Validate() {
      if (string.IsNullOrWhiteSpace(Database.ConnectionString)) {
         throw new InvalidOperationException("database.connectionString is required.");
      }

      if (!Enum.IsDefined(Database.Provider)) {
         throw new InvalidOperationException("database.provider must be MicrosoftSqlServer, MySql, or PostgreSql.");
      }

      foreach (var (name, extra) in Database.Extra) {
         if (string.IsNullOrWhiteSpace(extra.ConnectionString) || !Enum.IsDefined(extra.Provider)) {
            throw new InvalidOperationException($"database.extra.{name} needs a connectionString and a valid provider.");
         }
      }

      if (string.IsNullOrWhiteSpace(Admin.InitialPassword)) {
         throw new InvalidOperationException("admin.initialPassword is required.");
      }

      if (Http.RequestTimeoutSeconds is <= 0) {
         throw new InvalidOperationException("http.requestTimeoutSeconds must be greater than 0.");
      }

      if (Session.TokenRetentionHours is <= 0) {
         throw new InvalidOperationException("session.tokenRetentionHours must be greater than 0.");
      }

      if (Storage.NuPakMaxPackageMb is <1 or >4096) throw new InvalidOperationException("storage.nuPakMaxPackageMb must be 1–4096.");
      if (Storage.CdnMaxFileSizeMb <= 0) {
         throw new InvalidOperationException("storage.cdnMaxFileSizeMb must be greater than 0.");
      }
   }

   /// <summary>Settings of the main database connection.</summary>
   public sealed class DatabaseSection
   {
      /// <summary>Database provider; SQL Server by default.</summary>
      public DatabaseProvider Provider { get; set; } = DatabaseProvider.MicrosoftSqlServer;
      /// <summary>Connection string; may be overridden by <c>EM_DB_CONNECTION_STRING</c>.</summary>
      public string? ConnectionString { get; set; }

      /// <summary>Extra connections by name, registered through <see cref="EmAppBuilder.AddExtraDbConn"/>.</summary>
      public Dictionary<string, ExtraDatabaseSection> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
   }

   /// <summary>Settings of an extra named database connection.</summary>
   public sealed class ExtraDatabaseSection
   {
      /// <summary>Database provider of this connection.</summary>
      public DatabaseProvider Provider { get; set; } = DatabaseProvider.MicrosoftSqlServer;
      /// <summary>Connection string of this connection.</summary>
      public string? ConnectionString { get; set; }
   }

   /// <summary>Settings of the built-in administrator account.</summary>
   public sealed class AdminSection
   {
      /// <summary>See <see cref="EmAppBuilder.FirstTimeAdminPassword"/>.</summary>
      public string? InitialPassword { get; set; }
   }

   /// <summary>One debug token key to register.</summary>
   public sealed class DebugTokenSection
   {
      /// <summary>Name of the key, which tokens signed by it must name.</summary>
      public string Name { get; set; } = "Development Token";

      /// <summary>RSA public key as Base64 DER PKCS#1, not a private key or an HTTP token.</summary>
      public string? PublicKey { get; set; }

      /// <summary>Validity of its tokens in days; <c>-1</c> means no limit.</summary>
      public int Days { get; set; } = EmAppBuilder.DefaultDebugTokenDays;
   }

   /// <summary>HTTP-related settings.</summary>
   public sealed class HttpSection
   {
      /// <summary>Time limit of GET actions in seconds; <c>null</c> keeps the builder default.</summary>
      public int? RequestTimeoutSeconds { get; set; }

      /// <summary>See <see cref="EmAppBuilder.ActionRateLimit"/>; <c>-1</c> means no limit.</summary>
      public int? ActionRateLimit { get; set; }

      /// <summary>Reverse proxy settings.</summary>
      public ProxySection Proxy { get; set; } = new();
   }

   /// <summary>Settings for trusting reverse proxies when reading client addresses.</summary>
   public sealed class ProxySection
   {
      /// <summary>Trusted IP addresses or CIDR; see <see cref="EmAppBuilder.TrustProxy"/>.</summary>
      public List<string> Trusted { get; set; } = [];

      /// <summary>See <see cref="EmAppBuilder.TrustAnyProxy"/>; only for an API that cannot be reached except through a proxy.</summary>
      public bool TrustAny { get; set; }

      /// <summary>See <see cref="EmAppBuilder.ProxyHopLimit"/>.</summary>
      public int? HopLimit { get; set; }
   }

   /// <summary>Session-related settings.</summary>
   public sealed class SessionSection
   {
      /// <summary>Hours a dead session row is kept; <c>null</c> keeps the builder default.</summary>
      public int? TokenRetentionHours { get; set; }
   }

   /// <summary>
   /// Storage paths. A relative path is resolved from the application content folder. Except for
   /// <see cref="TaskCachePath"/>, these paths are only used when the host turns the feature on.
   /// </summary>
   public sealed class StorageSection
   {
      /// <summary>Folder of business task results.</summary>
      public string TaskCachePath { get; set; } = "./data/tasks";
      /// <summary>Folder of local binary storage.</summary>
      public string BinaryPath { get; set; } = "./data/binary";
      /// <summary>Folder served by the CDN.</summary>
      public string CdnPath { get; set; } = "./data/cdn";
      /// <summary>Size limit of one CDN upload, in megabytes.</summary>
      public int CdnMaxFileSizeMb { get; set; } = EmAppBuilder.DefaultCdnMaxFileSizeMb;
      /// <summary>Folder of NuPak package files.</summary>
      public string NuPakPath { get; set; } = "./data/nuget";
      /// <summary>Size limit of one NuPak package, in megabytes.</summary>
      public int NuPakMaxPackageMb { get; set; } = 250;
      /// <summary>Folder of container registry blobs.</summary>
      public string RegistryPath { get; set; } = "./data/container-registry";
   }
}
