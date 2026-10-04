using System.Collections;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Jendela per module ke katalog claim dan ke hak pengguna aktif - bukan wadah. Tidak punya daftar
   /// sendiri untuk diisi: nama module, katalog, dan sumber hak seluruhnya diterima dari luar saat ia
   /// dibentuk, dan ia dibentuk ulang setiap kali ditanya lewat extension method <c>Claims()</c> pada
   /// <c>IServices</c>. Objeknya sekali pakai - jangan disimpan di field, katalog dan hak bisa berganti
   /// kapan pun pengguna aktif berganti.
   /// </summary>
   public class ClaimCollection(string moduleName, IReadOnlyList<ClaimAction> catalog, User? user)
      : IEnumerable<ClaimAction>
   {
      public string ModuleName { get; } = moduleName;

      /// <summary>Katalog milik module ini - dipakai layar pengelola untuk menggambar daftar centangnya.</summary>
      public IEnumerator<ClaimAction> GetEnumerator() =>
         catalog
            .Where(r => string.Equals(r.ModuleName, ModuleName, StringComparison.OrdinalIgnoreCase))
            .GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      /// <summary>
      /// <c>true</c> kalau pengguna yang sedang aktif boleh menjalankan claim bernama <paramref name="name"/>
      /// pada module ini. Urutan pemeriksaannya mengikat - lihat masing-masing langkah.
      /// </summary>
      public bool this[string name] {
         get {
            // 1. Susun kuncinya lewat ClaimAction - hanya di sini bentuk kuncinya boleh dirangkai.
            var key = new ClaimAction { ModuleName = ModuleName, Name = name }.Key;

            // 2. Katalog belum termuat (sesi baru dibuka, RefreshClaimsAsync belum selesai) - jawaban
            // konservatif: tombol yang belum jelas haknya dimatikan dulu.
            if (catalog.Count == 0) return false;

            // 3. Nama harus ada di katalog module ini - kalau tidak, itu salah ketik programmer,
            // bukan "tidak punya hak". Dilempar di DEBUG supaya ketahuan sekarang; di rilis dijawab
            // false, karena indexer ini dipanggil dari XxxCommandAllowed - jalur yang dieksekusi WPF
            // terus-menerus, dan exception di sana akan menjatuhkan aplikasi di depan user.
            if (!catalog.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))) {
#if DEBUG
               throw new InvalidOperationException(
                  $"Claim '{key}' is not declared by any module. Check the spelling, or declare it with 'AddClaims'.");
#else
               return false;
#endif
            }

            // 4. Tidak ada pengguna aktif - false. Ini yang menggantikan pembersihan hak saat sign
            // out: SetActiveUser(null) sudah cukup.
            if (user is null) return false;

            // 5. Administrator menjawab true untuk setiap claim yang ada di katalog - ditaruh sesudah
            // langkah 3, bukan sebelumnya, supaya salah ketik tetap ketahuan walau yang bertanya
            // seorang administrator (mis. developer di mode debug).
            if (user.cUserIsAdmin) return true;

            // 6. Selebihnya: cari kuncinya di hak yang benar-benar diberikan ke user ini.
            return user.AvailableClaims.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
         }
      }
   }
}
