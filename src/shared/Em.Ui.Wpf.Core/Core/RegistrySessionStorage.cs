using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Stores the saved session in the Registry, riding on the subkey of its connection profile
   /// (<c>HKCU\{ApplicationName}\Api Connections\{ProfileName}</c>), as a single binary value already
   /// encrypted with DPAPI for the Windows account that is running.
   /// </summary>
   /// <remarks>
   /// A deliberate consequence of riding on the connection's subkey: renaming or deleting a profile also
   /// discards its saved session - deleting a profile discards its whole subtree - while merely editing a
   /// profile does not, because connection storage writes its fields one by one. Both are correct
   /// behavior: a session belongs to one particular server.
   /// </remarks>
   public sealed class RegistrySessionStorage(EmApp app) : ISessionStorage
   {
      private const string SessionValueName = "Session";

      // Not a secret and does not pretend to be one. Its purpose is to make sure this application's blob is
      // not also opened by another application that happens to call Unprotect with empty entropy.
      private static readonly byte[] Entropy = "em.session.v1"u8.ToArray();

      /// <summary>Stores (or overwrites) the session of a connection profile.</summary>
      public void Save(string profileName, SavedSession session) {
         ArgumentNullException.ThrowIfNull(session);

         using var key = OpenProfileKey(profileName, writable: true);
         if (key is null) return;

         var payload = JsonSerializer.SerializeToUtf8Bytes(session);
         var protectedPayload = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
         key.SetValue(SessionValueName, protectedPayload, RegistryValueKind.Binary);
      }

      /// <summary>Reads the stored session of a connection profile, or <c>null</c> when there is none.</summary>
      public SavedSession? Load(string profileName) {
         using var key = OpenProfileKey(profileName, writable: true);
         if (key?.GetValue(SessionValueName) is not byte[] protectedPayload) return null;

         try {
            var payload = ProtectedData.Unprotect(protectedPayload, Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<SavedSession>(payload);
            return string.IsNullOrEmpty(session?.RefreshToken) ? null : session;
         }
         catch (Exception x) when (x is CryptographicException or JsonException) {
            // A blob that cannot be opened can never be opened again - it was encrypted for another Windows
            // account, or its content is corrupt. It is discarded now so it is not tried again on every start.
            key!.DeleteValue(SessionValueName, throwOnMissingValue: false);
            return null;
         }
      }

      /// <summary>Discards the stored session of a connection profile.</summary>
      public void Clear(string profileName) {
         using var key = OpenProfileKey(profileName, writable: true);
         key?.DeleteValue(SessionValueName, throwOnMissingValue: false);
      }

      // The connection's own subkey is never created from here: a session may only ride on a profile that is
      // already stored. A debug profile has no subkey at all, and that is right - no session is stored for it.
      private RegistryKey? OpenProfileKey(string profileName, bool writable) {
         if (string.IsNullOrWhiteSpace(profileName)) return null;

         using var baseKey = app.BaseRegKey;
         using var container = baseKey.OpenSubKey(EmApp.ApiConnectionsSubKey, writable);
         return container?.OpenSubKey(profileName, writable);
      }
   }
}
