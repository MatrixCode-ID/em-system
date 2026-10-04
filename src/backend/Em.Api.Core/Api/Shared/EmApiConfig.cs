using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Api.Shared;

/// <summary>
/// Konfigurasi host API yang berbeda per lingkungan, dalam satu berkas JSON standar
/// (<see cref="FileName"/>). Berkasnya disimpan di luar repo (folder artefak lokal) karena memuat secret;
/// fitur dan modul yang dinyalakan tetap ditulis di kode host.
/// Terapkan ke builder dengan <see cref="EmAppBuilder.ApplyConfig"/>.
/// </summary>
/// <remarks>
/// Nilai angka yang dibiarkan <c>null</c> memakai bawaan <see cref="EmAppBuilder"/>, jadi berkas cukup memuat
/// yang memang ingin diubah.
/// </remarks>
public sealed class EmApiConfig
{
   /// <summary>Nama berkas standar konfigurasi host API.</summary>
   public const string FileName = "emapi-config.json";

   private static readonly JsonSerializerOptions JsonOptions = new() {
      PropertyNameCaseInsensitive = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      Converters = { new JsonStringEnumConverter() },
   };

   public DatabaseSection Database { get; set; } = new();
   public AdminSection Admin { get; set; } = new();
   public List<DebugTokenSection> DebugTokens { get; set; } = [];
   public HttpSection Http { get; set; } = new();
   public SessionSection Session { get; set; } = new();
   public StorageSection Storage { get; set; } = new();

   /// <summary>Membaca berkas konfigurasi; komentar dan koma di akhir diperbolehkan.</summary>
   /// <exception cref="InvalidOperationException">Dilempar kalau isi berkas bukan JSON yang bisa dibaca.</exception>
   public static EmApiConfig Load(string path) {
      try {
         using var stream = File.OpenRead(path);
         return JsonSerializer.Deserialize<EmApiConfig>(stream, JsonOptions) ?? new EmApiConfig();
      } catch (JsonException ex) {
         throw new InvalidOperationException($"'{path}' is not a valid {FileName}: {ex.Message}", ex);
      }
   }

   /// <summary>
   /// Memeriksa nilai wajib dan nilai yang jelas salah, supaya kekeliruan ketahuan saat startup dengan
   /// pesan yang menyebut kuncinya. Format public key debug dan alamat proxy diperiksa oleh builder.
   /// </summary>
   /// <exception cref="InvalidOperationException">Dilempar pada nilai pertama yang tidak valid.</exception>
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

   public sealed class DatabaseSection
   {
      public DatabaseProvider Provider { get; set; } = DatabaseProvider.MicrosoftSqlServer;
      public string? ConnectionString { get; set; }

      /// <summary>Koneksi tambahan per nama, didaftarkan lewat <see cref="EmAppBuilder.AddExtraDbConn"/>.</summary>
      public Dictionary<string, ExtraDatabaseSection> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
   }

   public sealed class ExtraDatabaseSection
   {
      public DatabaseProvider Provider { get; set; } = DatabaseProvider.MicrosoftSqlServer;
      public string? ConnectionString { get; set; }
   }

   public sealed class AdminSection
   {
      /// <summary>Lihat <see cref="EmAppBuilder.FirstTimeAdminPassword"/>.</summary>
      public string? InitialPassword { get; set; }
   }

   public sealed class DebugTokenSection
   {
      public string Name { get; set; } = "Development Token";

      /// <summary>Public key RSA Base64 DER PKCS#1, bukan private key atau token HTTP.</summary>
      public string? PublicKey { get; set; }

      public int Days { get; set; } = EmAppBuilder.DefaultDebugTokenDays;
   }

   public sealed class HttpSection
   {
      public int? RequestTimeoutSeconds { get; set; }

      /// <summary>Lihat <see cref="EmAppBuilder.ActionRateLimit"/>; <c>-1</c> berarti tanpa batas.</summary>
      public int? ActionRateLimit { get; set; }

      public ProxySection Proxy { get; set; } = new();
   }

   public sealed class ProxySection
   {
      /// <summary>Alamat IP atau CIDR yang dipercaya; lihat <see cref="EmAppBuilder.TrustProxy"/>.</summary>
      public List<string> Trusted { get; set; } = [];

      /// <summary>Lihat <see cref="EmAppBuilder.TrustAnyProxy"/>; hanya untuk API yang tidak bisa dihubungi selain lewat proxy.</summary>
      public bool TrustAny { get; set; }

      /// <summary>Lihat <see cref="EmAppBuilder.ProxyHopLimit"/>.</summary>
      public int? HopLimit { get; set; }
   }

   public sealed class SessionSection
   {
      public int? TokenRetentionHours { get; set; }
   }

   /// <summary>
   /// Path penyimpanan. Path relatif dihitung dari folder konten aplikasi. Selain
   /// <see cref="TaskCachePath"/>, path ini hanya dipakai bila host menyalakan fiturnya.
   /// </summary>
   public sealed class StorageSection
   {
      public string TaskCachePath { get; set; } = "./data/tasks";
      public string BinaryPath { get; set; } = "./data/binary";
      public string CdnPath { get; set; } = "./data/cdn";
      public int CdnMaxFileSizeMb { get; set; } = EmAppBuilder.DefaultCdnMaxFileSizeMb;
      public string NuPakPath { get; set; } = "./data/nuget";
      public int NuPakMaxPackageMb { get; set; } = 250;
      public string RegistryPath { get; set; } = "./data/container-registry";
   }
}
