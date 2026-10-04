using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Menyimpan sesi tersimpan di Registry, menumpang subkey milik profil koneksinya
   /// (<c>HKCU\{ApplicationName}\Api Connections\{ProfileName}</c>), sebagai satu nilai biner yang
   /// sudah dienkripsi DPAPI untuk akun Windows yang sedang berjalan.
   /// </summary>
   /// <remarks>
   /// Konsekuensi yang disengaja dari menumpang subkey koneksi: mengganti nama atau menghapus profil
   /// ikut membuang sesi tersimpannya - penghapusan profil membuang seluruh subtree-nya - sementara
   /// sekadar mengedit profil tidak, karena penyimpanan koneksi menulis field-nya satu per satu.
   /// Keduanya perilaku yang benar: sesi memang milik satu server tertentu.
   /// </remarks>
   public sealed class RegistrySessionStorage(EmApp app) : ISessionStorage
   {
      private const string SessionValueName = "Session";

      // Bukan rahasia dan tidak berpura-pura jadi rahasia. Gunanya memastikan blob milik aplikasi ini
      // tidak ikut terbuka oleh aplikasi lain yang kebetulan memanggil Unprotect dengan entropy kosong.
      private static readonly byte[] Entropy = "em.session.v1"u8.ToArray();

      public void Save(string profileName, SavedSession session) {
         ArgumentNullException.ThrowIfNull(session);

         using var key = OpenProfileKey(profileName, writable: true);
         if (key is null) return;

         var payload = JsonSerializer.SerializeToUtf8Bytes(session);
         var protectedPayload = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
         key.SetValue(SessionValueName, protectedPayload, RegistryValueKind.Binary);
      }

      public SavedSession? Load(string profileName) {
         using var key = OpenProfileKey(profileName, writable: true);
         if (key?.GetValue(SessionValueName) is not byte[] protectedPayload) return null;

         try {
            var payload = ProtectedData.Unprotect(protectedPayload, Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<SavedSession>(payload);
            return string.IsNullOrEmpty(session?.RefreshToken) ? null : session;
         }
         catch (Exception x) when (x is CryptographicException or JsonException) {
            // Blob yang tidak bisa dibuka tidak akan pernah bisa dibuka lagi - ia dienkripsi untuk akun
            // Windows lain, atau isinya rusak. Dibuang sekarang supaya tidak dicoba lagi setiap start.
            key!.DeleteValue(SessionValueName, throwOnMissingValue: false);
            return null;
         }
      }

      public void Clear(string profileName) {
         using var key = OpenProfileKey(profileName, writable: true);
         key?.DeleteValue(SessionValueName, throwOnMissingValue: false);
      }

      // Subkey koneksinya sendiri tidak pernah dibuat dari sini: sesi hanya boleh menumpang profil yang
      // memang sudah tersimpan. Profil debug tidak punya subkey sama sekali, dan itu benar - sesi
      // untuknya tidak ikut tersimpan.
      private RegistryKey? OpenProfileKey(string profileName, bool writable) {
         if (string.IsNullOrWhiteSpace(profileName)) return null;

         using var baseKey = app.BaseRegKey;
         using var container = baseKey.OpenSubKey(EmApp.ApiConnectionsSubKey, writable);
         return container?.OpenSubKey(profileName, writable);
      }
   }
}
