using System.Text.Json;
using Microsoft.Maui.Storage;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Menyimpan sesi tersimpan di penyimpanan aman perangkat, satu entri per profil koneksi. Di
   /// Android isinya dikunci oleh keystore milik sistem dan hanya bisa dibuka aplikasi ini di
   /// perangkat ini - peran yang sama dengan DPAPI di client desktop.
   /// </summary>
   /// <remarks>
   /// Penghapusan profil koneksi tidak otomatis ikut membuang sesinya di sini - beda dengan client
   /// desktop, yang sesinya menumpang subkey milik profil. Karena itu yang menghapus atau mengganti
   /// nama sebuah profil wajib memanggil <see cref="Clear"/> untuk nama lamanya.
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
            // Isi yang tidak bisa dibaca tidak akan pernah bisa dibaca lagi. Dibuang sekarang supaya
            // tidak dicoba lagi setiap kali aplikasi dibuka.
            Clear(profileName);
            return null;
         }
      }

      /// <inheritdoc />
      public void Clear(string profileName) {
         if (string.IsNullOrWhiteSpace(profileName)) return;
         _storage.Remove(KeyOf(profileName));
      }

      // Penyimpanan aman di MAUI hanya punya bentuk asynchronous, sementara ISessionStorage sengaja
      // sinkron - ia dipanggil dari tengah alur membuka dan menutup sesi, yang tidak punya tempat
      // untuk menunggu. Pekerjaannya dilempar ke thread pool lebih dulu, bukan ditunggu langsung,
      // supaya lanjutannya tidak pernah mencoba kembali ke thread UI yang justru sedang menunggu -
      // dan di situlah aplikasi akan berhenti total.
      private static void RunBlocking(Func<Task> work) =>
         Task.Run(work).GetAwaiter().GetResult();

      private static T RunBlocking<T>(Func<Task<T>> work) =>
         Task.Run(work).GetAwaiter().GetResult();
   }
}
