using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Jenis tujuan rilis di Release Manager.</summary>
   public enum ReleaseTargetKind
   {
      /// <summary>CDN bawaan server yang sedang tersambung.</summary>
      Cdn = 0,
      /// <summary>Folder biasa, lokal atau jaringan, yang ditulis langsung.</summary>
      Folder = 1
   }

   /// <summary>Lokasi private key untuk menandatangani rilis.</summary>
   public enum ReleaseSigningSource { Store = 0, ProfileFile = 1 }
   /// <summary>Cara menyimpan password berkas key.</summary>
   public enum ReleasePasswordStorage { Separate = 0, Plaintext = 1 }

   /// <summary>Pengaturan signing key milik satu profile.</summary>
   public sealed class ReleaseProfileSigning
   {
      public ReleaseSigningSource Source { get; set; }
      public string Thumbprint { get; set; } = "";
      public ReleasePasswordStorage PasswordStorage { get; set; }
      public string? Password { get; set; }
   }

   /// <summary>Sumber, tujuan, dan signing key satu aplikasi; disimpan sebagai profile.json.</summary>
   public sealed class ReleaseProfile
   {
      public static readonly JsonSerializerOptions JsonOptions = new() {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         WriteIndented = true,
         DefaultIgnoreCondition = JsonIgnoreCondition.Never,
         Converters = { new JsonStringEnumConverter() }
      };

      public int FormatVersion { get; set; } = 1;
      public string Id { get; set; } = Guid.NewGuid().ToString("N");
      public string Name { get; set; } = "";
      public string SolutionPath { get; set; } = "";
      public string HostProject { get; set; } = "";
      public string PublishFolder { get; set; } = "";
      public ReleaseTargetKind TargetKind { get; set; }
      public string TargetFolder { get; set; } = "";
      public string ReleaseFolder { get; set; } = ReleaseLayout.DefaultReleaseFolder;
      public ReleaseProfileSigning Signing { get; set; } = new();

      /// <summary>Salinan dalam, termasuk pengaturan signing.</summary>
      public ReleaseProfile Clone() => FromJson(JsonSerializer.Serialize(this, JsonOptions));

      public void Validate() {
         if (FormatVersion != 1) throw new InvalidDataException($"Unsupported profile format version {FormatVersion}.");
         ValidateId(Id);
         Id = Id.ToLowerInvariant();
         Name = Name?.Trim() ?? "";
         if (Name.Length is 0 or > 100) throw new InvalidDataException("Profile name must contain 1 to 100 characters.");
         if (Signing is null || !Enum.IsDefined(TargetKind) || !Enum.IsDefined(Signing.Source) || !Enum.IsDefined(Signing.PasswordStorage))
            throw new InvalidDataException("Invalid profile signing settings or target kind.");
         SolutionPath ??= "";
         HostProject ??= "";
         PublishFolder ??= "";
         TargetFolder ??= "";
         Signing.Thumbprint ??= "";
         if (string.IsNullOrWhiteSpace(ReleaseFolder)) ReleaseFolder = ReleaseLayout.DefaultReleaseFolder;
         if (Signing.PasswordStorage == ReleasePasswordStorage.Separate) Signing.Password = null;
      }

      public static void ValidateId(string id) {
         if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid profile id; expected a GUID in N format.");
      }

      public static string ToJson(ReleaseProfile profile) {
         profile.Validate();
         return JsonSerializer.Serialize(profile, JsonOptions);
      }

      public static ReleaseProfile FromJson(string json) {
         try {
            var profile = JsonSerializer.Deserialize<ReleaseProfile>(json, JsonOptions)
                          ?? throw new InvalidDataException("The profile is empty.");
            profile.Validate();
            return profile;
         }
         catch (JsonException x) {
            throw new InvalidDataException("Invalid profile JSON.", x);
         }
      }
   }

   /// <summary>Profile atau kesalahan satu folder; kesalahan tidak menghalangi daftar lain.</summary>
   public sealed record ReleaseProfileEntry(string Directory, ReleaseProfile? Profile, string? Error, DateTime LastWriteUtc);
}
