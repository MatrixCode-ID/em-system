using Isopoh.Cryptography.Argon2;

namespace Em.Shared
{
   /// <summary>
   /// Implementasi <see cref="IStringHasher"/> menggunakan algoritma Argon2 (via library
   /// <c>Isopoh.Cryptography.Argon2</c>). Dipakai untuk hashing password/credential secara aman.
   /// </summary>
   public class Argon2Hashing : IStringHasher
   {
      /// <summary>
      /// Menghasilkan hash Argon2 dari <paramref name="input"/>. Hasilnya sudah menyertakan salt
      /// dan parameter Argon2 dalam satu string, sehingga bisa langsung disimpan ke database.
      /// </summary>
      /// <param name="input">Teks polos (mis. password) yang akan di-hash.</param>
      /// <returns>String hash Argon2.</returns>
      public string HashValue(string input) {
         return Argon2.Hash(input);
      }

      /// <summary>
      /// Membandingkan teks polos dengan hash Argon2 yang tersimpan, untuk verifikasi
      /// (mis. saat proses login).
      /// </summary>
      /// <param name="input">Teks polos yang ingin diverifikasi.</param>
      /// <param name="hashValue">Hash Argon2 yang tersimpan sebelumnya (hasil dari <see cref="HashValue"/>).</param>
      /// <returns><c>true</c> jika <paramref name="input"/> cocok dengan <paramref name="hashValue"/>.</returns>
      public bool CompareHashValue(string input, string hashValue) {
         return Argon2.Verify(hashValue, input);
      }
   }
}
