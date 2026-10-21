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
      private readonly Func<RegistryKey> _baseKey;
      /// <summary>Creates a new instance of <see cref="ReleaseManagerPreferences"/>.</summary>
      public ReleaseManagerPreferences(EmApp app) : this(() => app.BaseRegKey) { }
      /// <summary>Creates a new instance of <see cref="ReleaseManagerPreferences"/>.</summary>
      public ReleaseManagerPreferences(Func<RegistryKey> baseKey) => _baseKey = baseKey;
      /// <summary>Local source checkout of the Rust launcher.</summary>
      public string LauncherSourceFolder { get => Local(nameof(LauncherSourceFolder)); set => SaveLocal(nameof(LauncherSourceFolder), value); }
      /// <summary>Local or IT share folder for built product launchers; empty uses the per-product default.</summary>
      public string LauncherOutputFolder { get => Local(nameof(LauncherOutputFolder)); set => SaveLocal(nameof(LauncherOutputFolder), value); }
      private string Local(string name) { using var root = _baseKey(); using var key = root.OpenSubKey(SubKey); return key?.GetValue(name) as string ?? ""; }
      private void SaveLocal(string name, string value) { using var root = _baseKey(); using var key = root.CreateSubKey(SubKey); key.SetValue(name, value); }
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

   }
}
