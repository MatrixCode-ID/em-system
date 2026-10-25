using Em.Ui.Core.Release.Format;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Ui.Wpf.Core.Release
{
   public enum ReleasePlatform { Windows = 0, Android = 1 }

   /// <summary>Android identity, permanent signing credentials and last prepared APK.</summary>
   public sealed class ReleaseAndroid {
      public string ApplicationId { get; set; } = "";
      public string ApkFileName { get; set; } = "";
      public int MinVersionCode { get; set; }
      public string KeystoreBase64 { get; set; } = "";
      public string KeystoreAlias { get; set; } = "";
      public string KeystorePassword { get; set; } = "";
      public string KeyPassword { get; set; } = "";
      public string CertificateSha256 { get; set; } = "";
      public int PreparedVersionCode { get; set; }
      public string PreparedVersionName { get; set; } = "";
      public string PreparedApkSha256 { get; set; } = "";
   }
   /// <summary>The kind of release target in Release Manager.</summary>
   public enum ReleaseTargetKind
   {
      /// <summary>The built-in CDN of the server that is currently connected.</summary>
      Cdn = 0,
      /// <summary>An ordinary folder, local or on a network, that is written to directly.</summary>
      Folder = 1
   }

   /// <summary>The source, target, and signing key of one application; stored as profile.json.</summary>
   public sealed class ReleaseProfile
   {
      /// <summary>The json options.</summary>
      public static readonly JsonSerializerOptions JsonOptions = new() {
         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
         WriteIndented = true,
         DefaultIgnoreCondition = JsonIgnoreCondition.Never,
         Converters = { new JsonStringEnumConverter() }
      };

      /// <summary>The format version.</summary>
      public int FormatVersion { get; set; } = 2;
      /// <summary>The id.</summary>
      public string Id { get; set; } = Guid.NewGuid().ToString("N");
      /// <summary>The name.</summary>
      public string Name { get; set; } = "";
      /// <summary>The solution path.</summary>
      public string SolutionPath { get; set; } = "";
      /// <summary>The host project.</summary>
      public string HostProject { get; set; } = "";
      /// <summary>The publish folder.</summary>
      public string PublishFolder { get; set; } = "";
      /// <summary>The target kind.</summary>
      public ReleaseTargetKind TargetKind { get; set; }
      /// <summary>The target folder.</summary>
      public string TargetFolder { get; set; } = "";
      /// <summary>The release folder.</summary>
      public string ReleaseFolder { get; set; } = ReleaseLayout.DefaultReleaseFolder;

      /// <summary>Identity read from the host project and selected by the developer.</summary>
      public ReleaseProduct Product { get; set; } = new();
      /// <summary>Requires a versioned release note before Prepare or Sync.</summary>
      public bool ReleaseNoteLock { get; set; }
      /// <summary>Notes folder relative to ReleaseProfileStore.Root.</summary>
      public string ReleaseNotesFolder { get; set; } = "";
      /// <summary>Public CDN address used by clients; empty derives from the connection.</summary>
      public string CdnAddress { get; set; } = "";
      /// <summary>Unencrypted PKCS#8 P-256 private PEM.</summary>
      public string PrivateKeyPem { get; set; } = "";
      /// <summary>Product-specific launcher settings.</summary>
      public ReleaseLauncher Launcher { get; set; } = new();
      public ReleasePlatform Platform { get; set; }
      public ReleaseAndroid Android { get; set; } = new();

      /// <summary>Changes the platform of a settings draft after the caller confirms key replacement.</summary>
      public void ChangePlatform(ReleasePlatform platform) {
         if (Platform == platform) return;
         Platform = platform;
         PrivateKeyPem = ReleasePemKey.Create();
         Android.PreparedVersionCode = 0;
         Android.PreparedVersionName = Android.PreparedApkSha256 = "";
         Launcher.InputHash = Launcher.Version = "";
      }

      [JsonIgnore]
      public string AndroidApkFileName => string.IsNullOrEmpty(Android.ApkFileName)
         ? Product.AppId.ToLowerInvariant().Replace('.', '-') + ".apk" : Android.ApkFileName;

      /// <summary>Validates complete credentials for Prepare/Sync; drafts remain saveable.</summary>
      public void ValidateAndroidRelease() {
         Validate();
         Product.Validate(ReleasePlatform.Android);
         if (!ReleaseManifestSerializer.IsApplicationId(Android.ApplicationId)) throw new InvalidDataException("Invalid Android applicationId.");
         if (!System.Text.RegularExpressions.Regex.IsMatch(AndroidApkFileName, @"^[a-z0-9][a-z0-9._-]*\.apk$")) throw new InvalidDataException("Invalid APK file name.");
         if (Android.MinVersionCode < 0 || Android.MinVersionCode > 2100000000) throw new InvalidDataException("Invalid minimum version code.");
         if (string.IsNullOrEmpty(PrivateKeyPem) || string.IsNullOrEmpty(Android.KeystoreBase64) || string.IsNullOrWhiteSpace(Android.KeystoreAlias) ||
             string.IsNullOrEmpty(Android.KeystorePassword) || string.IsNullOrEmpty(Android.KeyPassword) || !ReleaseManifestSerializer.IsValidSha256(Android.CertificateSha256))
            throw new InvalidDataException("Create or import the Android keystore and manifest key first.");
         _ = Convert.FromBase64String(Android.KeystoreBase64);
      }

      /// <summary>A deep copy, including the signing settings.</summary>
      public ReleaseProfile Clone() => FromJson(JsonSerializer.Serialize(this, JsonOptions));

      /// <summary>Validates the profile, throwing when a value is not acceptable.</summary>
      public void Validate() {
         if (FormatVersion != 2) throw new InvalidDataException($"Unsupported profile format version {FormatVersion}.");
         ValidateId(Id);
         Id = Id.ToLowerInvariant();
         Name = Name?.Trim() ?? "";
         if (Name.Length is 0 or > 100) throw new InvalidDataException("Profile name must contain 1 to 100 characters.");
         if (Product is null || Launcher is null || Android is null || !Enum.IsDefined(Platform) || !Enum.IsDefined(TargetKind))
            throw new InvalidDataException("Invalid profile signing settings or target kind.");
         SolutionPath ??= "";
         HostProject ??= "";
         PublishFolder ??= "";
         TargetFolder ??= "";
         CdnAddress ??= "";
         PrivateKeyPem ??= "";
         if (PrivateKeyPem.Length > 0) { using var key = ReleasePemKey.Open(PrivateKeyPem); }
         if (string.IsNullOrWhiteSpace(ReleaseFolder)) ReleaseFolder = ReleaseLayout.DefaultReleaseFolder;
      }

      /// <summary>Validates a profile id.</summary>
      public static void ValidateId(string id) {
         if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid profile id; expected a GUID in N format.");
      }

      /// <summary>Serializes a profile to JSON.</summary>
      public static string ToJson(ReleaseProfile profile) {
         profile.Validate();
         return JsonSerializer.Serialize(profile, JsonOptions);
      }

      /// <summary>Reads a profile from JSON.</summary>
      public static ReleaseProfile FromJson(string json) {
         try {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("formatVersion", out var version) || !version.TryGetInt32(out var number) || number != 2)
               throw new InvalidDataException("Only version-two product profiles are supported. Legacy profiles are not migrated.");
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

   /// <summary>A profile or the error of one folder; an error does not block the other lists.</summary>
   public sealed record ReleaseProfileEntry(string Directory, ReleaseProfile? Profile, string? Error, DateTime LastWriteUtc);
}
