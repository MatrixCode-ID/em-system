using System.IO;
using Microsoft.Win32;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Small preferences and the migration of the old Registry; the content of a profile lives in files.</summary>
   public sealed class ReleaseManagerPreferences
   {
      /// <summary>The sub key.</summary>
      public const string SubKey = "ReleaseManager";
      /// <summary>The last profiles sub key.</summary>
      public const string LastProfilesSubKey = "LastProfiles";
      private static readonly string[] LegacyNames = ["SolutionPath", "HostProject", "PublishFolder", "TargetKind", "TargetFolder", "ReleaseFolder", "SigningThumbprint"];
      private readonly Func<RegistryKey> _baseKey;
      /// <summary>Creates a new instance of <see cref="ReleaseManagerPreferences"/>.</summary>
      public ReleaseManagerPreferences(EmApp app) : this(() => app.BaseRegKey) { }
      /// <summary>Creates a new instance of <see cref="ReleaseManagerPreferences"/>.</summary>
      public ReleaseManagerPreferences(Func<RegistryKey> baseKey) => _baseKey = baseKey;
      /// <summary>The default profiles folder.</summary>
      public static string DefaultProfilesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Em", "ReleaseManager", "Profiles", "Release");

      /// <summary>The profiles folder.</summary>
      public string ProfilesFolder {
         get {
            using var root = _baseKey();
            using var key = root.OpenSubKey(SubKey);
            return key?.GetValue(nameof(ProfilesFolder)) is string { Length: > 0 } folder ? folder : DefaultProfilesFolder;
         }
         set {
            using var root = _baseKey();
            using var key = root.CreateSubKey(SubKey);
            if (string.IsNullOrWhiteSpace(value) || string.Equals(Path.GetFullPath(value), DefaultProfilesFolder, StringComparison.OrdinalIgnoreCase))
               key.DeleteValue(nameof(ProfilesFolder), false);
            else key.SetValue(nameof(ProfilesFolder), Path.GetFullPath(value));
         }
      }

      /// <summary>Composes the key under which preferences of a server are stored.</summary>
      public static string ServerKey(ApiConnection? connection) {
         if (connection is null) return "(none)";
         var host = connection.Host ?? "";
         return Uri.TryCreate(host, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.Authority.ToLowerInvariant() : host.Trim().TrimEnd('/').ToLowerInvariant();
      }

      /// <summary>Gets the last used profile id of a server.</summary>
      public string? GetLastProfile(string serverKey) {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey + "\\" + LastProfilesSubKey);
         return key?.GetValue(serverKey) as string;
      }

      /// <summary>Stores the last used profile id of a server.</summary>
      public void SetLastProfile(string serverKey, string id) {
         ReleaseProfile.ValidateId(id);
         using var root = _baseKey();
         using var key = root.CreateSubKey(SubKey + "\\" + LastProfilesSubKey);
         key.SetValue(serverKey, id);
      }

      /// <summary>Whether settings in the old Registry format exist.</summary>
      public bool HasLegacySettings() {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey);
         return key is not null && LegacyNames.Any(name => key.GetValue(name) is not null);
      }

      /// <summary>Reads the old Registry settings as a profile.</summary>
      public ReleaseProfile? ReadLegacyProfile() {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey);
         if (key is null || !LegacyNames.Any(name => key.GetValue(name) is not null)) return null;
         string Read(string name) => key.GetValue(name) as string ?? "";
         return new ReleaseProfile {
            Name = "Default", SolutionPath = Read("SolutionPath"), HostProject = Read("HostProject"),
            PublishFolder = Read("PublishFolder"), TargetFolder = Read("TargetFolder"),
            TargetKind = Enum.TryParse<ReleaseTargetKind>(Read("TargetKind"), out var kind) && Enum.IsDefined(kind) ? kind : ReleaseTargetKind.Cdn,
            ReleaseFolder = Read("ReleaseFolder") is { Length: > 0 } folder ? folder : ReleaseLayout.DefaultReleaseFolder,
            Signing = new() { Thumbprint = Read("SigningThumbprint") }
         };
      }

      /// <summary>Deletes the old Registry settings.</summary>
      public void DeleteLegacySettings() {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey, writable: true);
         foreach (var name in LegacyNames) key?.DeleteValue(name, false);
      }
   }
}
