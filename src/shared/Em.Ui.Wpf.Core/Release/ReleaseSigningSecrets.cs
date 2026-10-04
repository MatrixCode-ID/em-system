using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Password sesi dan DPAPI CurrentUser, terikat id profile serta thumbprint.</summary>
   public sealed class ReleaseSigningSecrets
   {
      private static readonly Dictionary<string, (string Thumbprint, string Password)> Session = new(StringComparer.OrdinalIgnoreCase);
      private readonly string _directory;
      public ReleaseSigningSecrets() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Em", "ReleaseManager", "Secrets")) { }
      public ReleaseSigningSecrets(string directory) => _directory = Path.GetFullPath(directory);
      private string FileOf(string id) {
         ReleaseProfile.ValidateId(id);
         return PublishPaths.Inside(_directory, id.ToLowerInvariant() + ".bin");
      }
      private string CacheKey(string id) => FileOf(id);

      public string? Get(string profileId, string thumbprint) {
         var file = FileOf(profileId);
         lock (Session) {
            if (Session.TryGetValue(CacheKey(profileId), out var item))
               return string.Equals(item.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) ? item.Password : null;
         }
         if (!File.Exists(file)) return null;
         try {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<SecretRecord>(bytes);
            if (record is null || !string.Equals(record.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)) return null;
            lock (Session) Session[CacheKey(profileId)] = (record.Thumbprint, record.Password);
            return record.Password;
         }
         catch (Exception x) when (x is IOException or UnauthorizedAccessException or CryptographicException or JsonException) {
            try { Forget(profileId); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return null;
         }
      }

      public void Put(string profileId, string thumbprint, string password, bool remember) {
         var file = FileOf(profileId);
         if (remember) {
            Directory.CreateDirectory(_directory);
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SecretRecord(thumbprint, password))), null, DataProtectionScope.CurrentUser);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
               File.WriteAllBytes(temporary, bytes);
               File.Move(temporary, file, overwrite: true);
            }
            finally {
               if (File.Exists(temporary)) File.Delete(temporary);
            }
         }
         else if (File.Exists(file)) File.Delete(file);
         lock (Session) Session[CacheKey(profileId)] = (thumbprint, password);
      }

      public bool IsRemembered(string profileId) => File.Exists(FileOf(profileId));
      public void Forget(string profileId) {
         lock (Session) Session.Remove(CacheKey(profileId));
         var file = FileOf(profileId);
         if (File.Exists(file)) File.Delete(file);
      }
      /// <summary>Membersihkan memori sesi untuk verifikasi pemulihan DPAPI pada harness.</summary>
      internal static void ClearSessionForTesting() {
         lock (Session) Session.Clear();
      }
      private sealed record SecretRecord(string Thumbprint, string Password);
   }
}
