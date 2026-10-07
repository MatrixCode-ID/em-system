using System.Text.Json;
using Microsoft.Maui.Storage;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Stores the saved session in the device's secure storage, one entry per connection profile. On
   /// Android its content is locked by the system keystore and can only be opened by this application on
   /// this device - the same role as DPAPI in the desktop client.
   /// </summary>
   /// <remarks>
   /// Deleting a connection profile does not automatically discard its session here - unlike the desktop
   /// client, whose session rides on the profile's subkey. So whoever deletes or renames a profile must call
   /// <see cref="Clear"/> for its old name.
   /// </remarks>
   public sealed class SecureStorageSessionStorage(EmApp app) : ISessionStorage
   {
      private readonly ISecureStorage _storage = SecureStorage.Default;

      private string KeyOf(string profileName) => $"{app.ApplicationName}:session:{profileName}";

      /// <inheritdoc />
      public void Save(string profileName, SavedSession session) {
         ArgumentNullException.ThrowIfNull(session);
         if (string.IsNullOrWhiteSpace(profileName)) return;

         var payload = JsonSerializer.Serialize(session);
         RunBlocking(() => _storage.SetAsync(KeyOf(profileName), payload));
      }

      /// <inheritdoc />
      public SavedSession? Load(string profileName) {
         if (string.IsNullOrWhiteSpace(profileName)) return null;

         var payload = RunBlocking(() => _storage.GetAsync(KeyOf(profileName)));
         if (string.IsNullOrEmpty(payload)) return null;

         try {
            var session = JsonSerializer.Deserialize<SavedSession>(payload);
            return string.IsNullOrEmpty(session?.RefreshToken) ? null : session;
         }
         catch (JsonException) {
            // Content that cannot be read will never be readable again. It is discarded now so it is not tried again
            // every time the application is opened.
            Clear(profileName);
            return null;
         }
      }

      /// <inheritdoc />
      public void Clear(string profileName) {
         if (string.IsNullOrWhiteSpace(profileName)) return;
         _storage.Remove(KeyOf(profileName));
      }

      // Secure storage in MAUI only has an asynchronous form, while ISessionStorage is deliberately
      // synchronous - it is called from the middle of opening and closing a session, which has no place to
      // wait. The work is thrown to the thread pool first, not awaited directly, so its continuation never
      // tries to return to the UI thread that is waiting - and that is where the application would stop
      // completely.
      private static void RunBlocking(Func<Task> work) =>
         Task.Run(work).GetAwaiter().GetResult();

      private static T RunBlocking<T>(Func<Task<T>> work) =>
         Task.Run(work).GetAwaiter().GetResult();
   }
}
