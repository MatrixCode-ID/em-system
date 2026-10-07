using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>The kind of release target in Release Manager.</summary>
   public enum ReleaseTargetKind
   {
      /// <summary>The built-in CDN of the server that is currently connected.</summary>
      Cdn = 0,
      /// <summary>An ordinary folder, local or on a network, that is written to directly.</summary>
      Folder = 1
   }

   /// <summary>The location of the private key used to sign releases.</summary>
   /// <summary>The release signing source.</summary>
   public enum ReleaseSigningSource
   {
      /// <summary>The key is in the Windows certificate store.</summary>
      Store = 0,
      /// <summary>The key is a file in the profile folder.</summary>
      ProfileFile = 1,
   }
   /// <summary>How the password of the key file is stored.</summary>
   /// <summary>The release password storage.</summary>
   public enum ReleasePasswordStorage
   {
      /// <summary>The password is kept apart from the profile (session or DPAPI).</summary>
      Separate = 0,
      /// <summary>The password is stored in the profile file as plain text.</summary>
      Plaintext = 1,
   }

   /// <summary>The signing key settings that belong to one profile.</summary>
   public sealed class ReleaseProfileSigning
   {
      /// <summary>The source.</summary>
      public ReleaseSigningSource Source { get; set; }
      /// <summary>The thumbprint.</summary>
      public string Thumbprint { get; set; } = "";
      /// <summary>The password storage.</summary>
      public ReleasePasswordStorage PasswordStorage { get; set; }
      /// <summary>The password.</summary>
      public string? Password { get; set; }
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
      public int FormatVersion { get; set; } = 1;
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
      /// <summary>The signing.</summary>
      public ReleaseProfileSigning Signing { get; set; } = new();

      /// <summary>A deep copy, including the signing settings.</summary>
      public ReleaseProfile Clone() => FromJson(JsonSerializer.Serialize(this, JsonOptions));

      /// <summary>Validates the profile, throwing when a value is not acceptable.</summary>
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
