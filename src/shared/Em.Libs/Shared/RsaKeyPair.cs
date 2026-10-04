using System.Security.Cryptography;

namespace Em.Shared
{
   /// <summary>
   /// Pasangan public/private key RSA, dipakai oleh <c>ServicesBase.GetServerRsaKeyAsync</c>. Kelas ini generik
   /// untuk dua mode pemakaian: pasangan lengkap (server, punya <see cref="PrivateKey"/>) atau public key saja
   /// (client/UI, <see cref="PrivateKey"/> null karena private key tidak pernah dikirim ke client).
   /// </summary>
   public class RsaKeyPair
   {
      /// <summary>
      /// Padding standar untuk enkripsi/dekripsi RSA di seluruh aplikasi. Memakai OAEP SHA-256, bukan PKCS#1 v1.5,
      /// karena PKCS#1 v1.5 rentan terhadap padding oracle (serangan Bleichenbacher) kalau pihak yang mendekripsi
      /// membocorkan perbedaan antara "padding valid" dan "padding tidak valid". Kedua sisi (server dan client)
      /// wajib memakai nilai yang sama.
      /// </summary>
      public static RSAEncryptionPadding DefaultPadding => RSAEncryptionPadding.OaepSHA256;

      /// <summary>
      /// Padding standar untuk tanda tangan digital RSA di seluruh aplikasi. Memakai PSS yang lebih kuat dari
      /// PKCS#1 v1.5. Kedua sisi (yang menandatangani dan yang memverifikasi) wajib memakai nilai yang sama.
      /// </summary>
      public static RSASignaturePadding DefaultSignaturePadding => RSASignaturePadding.Pss;

      /// <summary>
      /// Algoritma hash standar yang dipakai bersama <see cref="DefaultSignaturePadding"/> saat menandatangani
      /// dan memverifikasi. Kedua sisi wajib memakai nilai yang sama.
      /// </summary>
      public static HashAlgorithmName DefaultHashAlgorithm => HashAlgorithmName.SHA256;

      /// <summary>
      /// Membuat instance yang hanya membawa public key, untuk pemakaian di sisi client (enkripsi dan verifikasi
      /// tanda tangan saja, tanpa kemampuan dekripsi atau menandatangani).
      /// </summary>
      /// <param name="publicKey">Public key, berupa Base64 dari DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      public static RsaKeyPair Create(string publicKey) => new RsaKeyPair(publicKey);

      /// <summary>
      /// Sama seperti <see cref="Create(string)"/>, tapi menerima public key dalam bentuk raw byte DER PKCS#1
      /// sehingga pemanggil tidak perlu meng-encode Base64 sendiri.
      /// </summary>
      /// <param name="publicKey">Public key berupa raw byte DER PKCS#1.</param>
      public static RsaKeyPair Create(byte[] publicKey) => Create(Convert.ToBase64String(publicKey));

      /// <param name="publicKey">Public key, berupa Base64 dari DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      /// <param name="privateKey">
      /// Private key, berupa Base64 dari DER PKCS#1 (<c>RSA.ExportRSAPrivateKey</c>). Opsional — null berarti
      /// instance ini hanya membawa public key (mis. dipakai di UI untuk enkripsi saja, tanpa kemampuan dekripsi).
      /// </param>
      public RsaKeyPair(string publicKey, string? privateKey = null) {
         PublicKey = publicKey;
         PrivateKey = privateKey;
      }

      /// <summary>
      /// Public key dalam bentuk Base64 dari DER PKCS#1. Aman untuk dikirim ke client atau disimpan di config.
      /// </summary>
      public string PublicKey { get; init; }

      /// <summary>
      /// Private key dalam bentuk Base64 dari DER PKCS#1, atau <c>null</c> kalau instance ini hanya membawa
      /// public key. Nilai ini rahasia dan tidak boleh pernah dikirim keluar dari server.
      /// </summary>
      public string? PrivateKey { get; init; }

      /// <summary>
      /// True kalau instance ini punya private key (mode pasangan lengkap), false kalau hanya public key.
      /// </summary>
      public bool HasPrivateKey => PrivateKey is not null;

      /// <summary>
      /// Mengembalikan <see cref="PublicKey"/> dalam bentuk raw byte DER PKCS#1, siap dipakai
      /// <c>RSA.ImportRSAPublicKey</c>.
      /// </summary>
      public byte[] GetPublicBytes() => Convert.FromBase64String(PublicKey);

      /// <summary>
      /// Mengembalikan <see cref="PrivateKey"/> dalam bentuk raw byte DER PKCS#1.
      /// </summary>
      /// <exception cref="InvalidOperationException">Dilempar kalau instance ini hanya membawa public key.</exception>
      public byte[] GetPrivateBytes() {
         if (PrivateKey is null) {
            throw new InvalidOperationException("This RsaKeyPair only has a public key; no private key is set.");
         }
         return Convert.FromBase64String(PrivateKey);
      }

      /// <summary>
      /// Membuat instance <see cref="RSA"/> baru yang sudah diisi key dari pasangan ini: private key kalau ada
      /// (sehingga bisa dekripsi dan menandatangani), atau public key saja kalau tidak. Pemanggil bertanggung
      /// jawab men-dispose hasilnya.
      /// </summary>
      public RSA CreateRsa() {
         var rsa = RSA.Create();
         if (HasPrivateKey) {
            // PKCS#1 RSAPrivateKey already contains the public components, so no separate import is needed.
            rsa.ImportRSAPrivateKey(GetPrivateBytes(), out _);
         }
         else {
            rsa.ImportRSAPublicKey(GetPublicBytes(), out _);
         }
         return rsa;
      }

      /// <summary>
      /// Mengenkripsi data dengan public key memakai <see cref="DefaultPadding"/>. Ukuran data yang bisa
      /// dienkripsi terbatas oleh ukuran key (untuk key 2048 bit dengan OAEP SHA-256: maksimal 190 byte).
      /// </summary>
      public byte[] EncryptValue(ReadOnlySpan<byte> data) {
         using var rsa = CreateRsa();
         return rsa.Encrypt(data, DefaultPadding);
      }

      /// <summary>
      /// Mendekripsi data memakai <see cref="DefaultPadding"/>. Butuh private key.
      /// </summary>
      /// <remarks>
      /// Hati-hati mengekspos method ini lewat endpoint yang bisa diakses bebas: endpoint yang mendekripsi
      /// ciphertext arbitrer dari luar lalu membocorkan hasilnya (atau bahkan hanya membocorkan berhasil/gagal)
      /// berfungsi sebagai decryption oracle. Untuk membuktikan kepemilikan key, pakai
      /// <see cref="SignData"/>/<see cref="VerifyData"/>, bukan dekripsi.
      /// </remarks>
      public byte[] DecryptValue(ReadOnlySpan<byte> data) {
         using var rsa = CreateRsa();
         return rsa.Decrypt(data, DefaultPadding);
      }

      /// <summary>
      /// Menandatangani data dengan private key memakai <see cref="DefaultHashAlgorithm"/> dan
      /// <see cref="DefaultSignaturePadding"/>. Tidak ada batas ukuran data karena yang ditandatangani
      /// adalah hash-nya.
      /// </summary>
      /// <exception cref="InvalidOperationException">Dilempar kalau instance ini tidak punya private key.</exception>
      public byte[] SignData(ReadOnlySpan<byte> data) {
         if (!HasPrivateKey) {
            throw new InvalidOperationException("Signing requires a private key; this RsaKeyPair only has a public key.");
         }
         using var rsa = CreateRsa();
         return rsa.SignData(data, DefaultHashAlgorithm, DefaultSignaturePadding);
      }

      /// <summary>
      /// Memverifikasi bahwa <paramref name="signature"/> memang tanda tangan atas <paramref name="data"/> oleh
      /// pemegang private key dari <see cref="PublicKey"/>. Cukup dengan public key saja, jadi bisa dipanggil
      /// di sisi client.
      /// </summary>
      /// <returns><c>true</c> kalau tanda tangan valid, <c>false</c> kalau tidak.</returns>
      public bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) {
         using var rsa = CreateRsa();
         return rsa.VerifyData(data, signature, DefaultHashAlgorithm, DefaultSignaturePadding);
      }
   }
}
