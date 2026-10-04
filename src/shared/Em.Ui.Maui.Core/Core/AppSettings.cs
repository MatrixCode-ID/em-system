using Microsoft.Maui.Storage;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Tempat pengaturan aplikasi disimpan di perangkat - padanan sisi MAUI dari subkey Registry yang
   /// dipakai client desktop. Isinya pengaturan biasa saja (nama profil terakhir, pilihan tema, daftar
   /// profil koneksi); yang rahasia tidak ditaruh di sini, melainkan di
   /// <see cref="SecureStorageSessionStorage"/>.
   /// </summary>
   /// <remarks>
   /// Setiap kunci diberi awalan nama aplikasi supaya dua aplikasi yang dibangun dari engine yang sama
   /// tidak saling menimpa pengaturan - persis alasan client desktop menaruh miliknya di bawah satu
   /// subkey bernama aplikasi.
   /// </remarks>
   public sealed class AppSettings(string applicationName)
   {
      private readonly IPreferences _preferences = Preferences.Default;

      private string KeyOf(string name) => $"{applicationName}:{name}";

      /// <summary>Membaca nilai teks, atau <c>null</c> kalau belum pernah disimpan.</summary>
      /// <param name="name">Nama pengaturan.</param>
      public string? GetString(string name) => _preferences.Get<string?>(KeyOf(name), null);

      /// <summary>
      /// Menyimpan nilai teks. Nilai kosong atau <c>null</c> membuang pengaturannya, bukan menyimpan
      /// teks kosong - supaya "belum pernah diisi" dan "sengaja dikosongkan" tidak perlu dibedakan
      /// pembacanya.
      /// </summary>
      /// <param name="name">Nama pengaturan.</param>
      /// <param name="value">Nilai yang disimpan, atau <c>null</c> untuk membuangnya.</param>
      public void SetString(string name, string? value) {
         if (string.IsNullOrWhiteSpace(value)) {
            _preferences.Remove(KeyOf(name));
            return;
         }

         _preferences.Set(KeyOf(name), value);
      }

      /// <summary>Membaca nilai benar/salah, atau <paramref name="defaultValue"/> kalau belum pernah disimpan.</summary>
      /// <param name="name">Nama pengaturan.</param>
      /// <param name="defaultValue">Nilai yang dipakai kalau pengaturannya belum ada.</param>
      public bool GetBool(string name, bool defaultValue = false) => _preferences.Get(KeyOf(name), defaultValue);

      /// <summary>Menyimpan nilai benar/salah.</summary>
      /// <param name="name">Nama pengaturan.</param>
      /// <param name="value">Nilai yang disimpan.</param>
      public void SetBool(string name, bool value) => _preferences.Set(KeyOf(name), value);
   }
}
