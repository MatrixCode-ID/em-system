using System.Text;

namespace Em.Shared
{
   /// <summary>
   /// Perhitungan CRC32 (polinomial IEEE standar) atas sebuah teks atau deretan byte. Hasilnya
   /// selalu sama untuk masukan yang sama - di proses mana pun, di komputer mana pun, dan di
   /// bahasa pemrograman mana pun yang memakai polinomial yang sama.
   /// <para>
   /// Sifat itulah alasan kelas ini ada. <c>string.GetHashCode()</c> sengaja diacak ulang setiap
   /// kali aplikasi dijalankan sebagai pengamanan, sehingga tidak bisa dipakai untuk apa pun yang
   /// hasilnya harus tetap sama di lain waktu - misalnya memilih warna dari sebuah nama. CRC32 ini
   /// menjawab kebutuhan itu, dan bukan alat pengamanan: jangan dipakai untuk password atau tanda
   /// tangan (untuk itu ada <see cref="Argon2Hashing"/> dan <see cref="RsaKeyPair"/>).
   /// </para>
   /// </summary>
   public static class Crc32
   {
      private const uint Polynomial = 0xEDB88320u;

      // Dibangun sekali saat kelas ini pertama dipakai: 256 entri yang memungkinkan CRC dihitung
      // satu byte sekaligus, bukan satu bit sekaligus.
      private static readonly uint[] Table = BuildTable();

      private static uint[] BuildTable() {
         var table = new uint[256];
         for (uint i = 0; i < 256; i++) {
            var entry = i;
            for (var bit = 0; bit < 8; bit++) {
               entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;
            }

            table[i] = entry;
         }

         return table;
      }

      /// <summary>
      /// Menghitung CRC32 dari sebuah teks, dibaca sebagai byte UTF-8. Teks kosong menghasilkan 0.
      /// </summary>
      /// <param name="value">Teks yang dihitung; <c>null</c> diperlakukan sama seperti teks kosong.</param>
      /// <returns>Nilai CRC32 dari teks tersebut.</returns>
      public static uint Compute(string value) {
         if (string.IsNullOrEmpty(value)) return 0u;

         var bytes = Encoding.UTF8.GetBytes(value);
         return Compute(bytes);
      }

      /// <summary>
      /// Menghitung CRC32 dari deretan byte apa adanya, tanpa penafsiran encoding.
      /// </summary>
      /// <param name="bytes">Byte yang dihitung; deretan kosong menghasilkan 0.</param>
      /// <returns>Nilai CRC32 dari byte tersebut.</returns>
      public static uint Compute(ReadOnlySpan<byte> bytes) {
         if (bytes.IsEmpty) return 0u;

         var crc = 0xFFFFFFFFu;
         foreach (var b in bytes) {
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
         }

         return crc ^ 0xFFFFFFFFu;
      }
   }
}
