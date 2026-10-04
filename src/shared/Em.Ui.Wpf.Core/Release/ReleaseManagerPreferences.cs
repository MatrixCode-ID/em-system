using System.IO;
using Microsoft.Win32;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Preferensi kecil dan migrasi Registry lama; isi profile berada di berkas.</summary>
   public sealed class ReleaseManagerPreferences
   {
      public const string SubKey = "ReleaseManager";
      public const string LastProfilesSubKey = "LastProfiles";
      private static readonly string[] LegacyNames = ["SolutionPath", "HostProject", "PublishFolder", "TargetKind", "TargetFolder", "ReleaseFolder", "SigningThumbprint"];
      private readonly Func<RegistryKey> _baseKey;
      public ReleaseManagerPreferences(EmApp app) : this(() => app.BaseRegKey) { }
      public ReleaseManagerPreferences(Func<RegistryKey> baseKey) => _baseKey = baseKey;
      public static string DefaultProfilesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Em", "ReleaseManager", "Profiles", "Release");

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

      public static string ServerKey(ApiConnection? connection) {
         if (connection is null) return "(none)";
         var host = connection.Host ?? "";
         return Uri.TryCreate(host, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.Authority.ToLowerInvariant() : host.Trim().TrimEnd('/').ToLowerInvariant();
      }

      public string? GetLastProfile(string serverKey) {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey + "\\" + LastProfilesSubKey);
         return key?.GetValue(serverKey) as string;
      }

      public void SetLastProfile(string serverKey, string id) {
         ReleaseProfile.ValidateId(id);
         using var root = _baseKey();
         using var key = root.CreateSubKey(SubKey + "\\" + LastProfilesSubKey);
         key.SetValue(serverKey, id);
      }

      public bool HasLegacySettings() {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey);
         return key is not null && LegacyNames.Any(name => key.GetValue(name) is not null);
      }

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

      public void DeleteLegacySettings() {
         using var root = _baseKey();
         using var key = root.OpenSubKey(SubKey, writable: true);
         foreach (var name in LegacyNames) key?.DeleteValue(name, false);
      }
   }
}
