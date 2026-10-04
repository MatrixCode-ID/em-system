namespace Em.Shared
{
   /// <summary>
   /// Kontrak untuk komponen hashing string (mis. password), sehingga algoritma hashing
   /// yang dipakai (Argon2, dsb.) bisa diganti tanpa mengubah kode yang memanggilnya.
   /// Lihat <see cref="Argon2Hashing"/> untuk implementasi default.
   /// </summary>
   public interface IStringHasher
   {
      /// <summary>
      /// Menghasilkan nilai hash dari teks polos <paramref name="input"/>.
      /// </summary>
      /// <param name="input">Teks polos yang akan di-hash.</param>
      /// <returns>String hash yang bisa disimpan dan nantinya dibandingkan lewat <see cref="CompareHashValue"/>.</returns>
      string HashValue(string input);

      /// <summary>
      /// Membandingkan teks polos dengan nilai hash yang tersimpan.
      /// </summary>
      /// <param name="input">Teks polos yang ingin diverifikasi.</param>
      /// <param name="hashValue">Nilai hash tersimpan, hasil dari <see cref="HashValue"/>.</param>
      /// <returns><c>true</c> jika <paramref name="input"/> cocok dengan <paramref name="hashValue"/>.</returns>
      bool CompareHashValue(string input, string hashValue);
   }
}
