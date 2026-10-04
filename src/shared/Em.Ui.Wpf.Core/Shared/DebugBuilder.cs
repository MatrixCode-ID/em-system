using System.Security.Cryptography;
using System.Text;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   public class DebugBuilder
   {
      internal List<ApiConnection> Connections { get; } = [];

      /// <summary>
      /// Nama key debug, sama persis dengan nama yang didaftarkan di server. Kosong kalau
      /// <see cref="SetDebugKey"/> belum pernah dipanggil.
      /// </summary>
      internal string? DebugKeyName { get; private set; }

      /// <summary>
      /// Private key debug milik pengembang. Nilainya tidak pernah dikirim ke server - yang dikirim hanya
      /// token hasil tanda tangannya.
      /// </summary>
      internal string? DebugKey { get; private set; }

      /// <summary>
      /// Menyetel key debug yang dipakai aplikasi ini untuk masuk tanpa melewati layar login. Keduanya
      /// harus sepasang dengan yang didaftarkan di server: nama key yang sama persis, dan private key yang
      /// pasangannya ada di sana sebagai public key.
      /// <para>
      /// Private key hanya dipakai sekali saat aplikasi start, untuk menandatangani satu token; ia sendiri
      /// tidak pernah ikut terkirim ke mana-mana.
      /// </para>
      /// </summary>
      /// <param name="name">Nama key, sama persis dengan yang didaftarkan di server.</param>
      /// <param name="privateKey">Private key RSA, berupa Base64 dari DER PKCS#1 (<c>RSA.ExportRSAPrivateKey</c>).</param>
      /// <exception cref="ArgumentException">Dilempar kalau salah satu argumennya kosong.</exception>
      public void SetDebugKey(string name, string privateKey) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Debug key name must not be empty.", nameof(name));
         }

         if (string.IsNullOrWhiteSpace(privateKey)) {
            throw new ArgumentException($"Debug key '{name}' has an empty private key.", nameof(privateKey));
         }

         DebugKeyName = name.Trim();
         DebugKey = privateKey.Trim();
      }

      public void AddDebugConnection(string cnName, string host, bool isDefault, int timeOut = 30) {
         var conn = new ApiConnection() {
            Host = host,
            Timeout = timeOut,
            IgnoreSslErrors = true,
            ProfileName = cnName,
            IsDebugConnection = true,
         };
         Connections.Add(conn);
         if (isDefault) DefaultConnection = conn;
      }

      /// <summary>
      /// Membuat token debug yang ditandatangani private key pada <see cref="SetDebugKey"/>, atau <c>null</c>
      /// kalau key-nya memang tidak pernah disetel. Dipanggil sekali saat aplikasi dibangun, sehingga tidak
      /// ada token jadi yang perlu ditempelkan ke source dan tidak ada yang perlu diganti berkala.
      /// </summary>
      /// <remarks>
      /// Token yang dihasilkan berumur sesuai batas yang ditentukan server. Kalau aplikasi dibiarkan hidup
      /// melewati batas itu, tokennya mati di tengah jalan - restart akan menerbitkan yang baru.
      /// </remarks>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau private key-nya tidak bisa dipakai menandatangani. Sengaja melempar, bukan diam-diam
      /// mematikan mode debug: kalau dibiarkan senyap, kesalahannya akan dicari di tempat lain.
      /// </exception>
      internal string? CreateDebugToken() {
         if (DebugKey is null || DebugKeyName is null) {
            return null;
         }

         return DebugTokenProtocol.Create(DebugKeyName, LoadSigningKey(DebugKeyName, DebugKey), DateTime.UtcNow);
      }

      /// <summary>
      /// Membaca private key dan membuktikan bahwa ia benar-benar bisa dipakai menandatangani - bukan
      /// sekadar berbentuk benar - lewat satu putaran tanda tangan dan verifikasi atas payload percobaan.
      /// Biayanya sekali saat start, dan hasilnya kesalahan key ketahuan sebelum window pertama muncul.
      /// </summary>
      /// <remarks>
      /// Satu hal yang tidak bisa diperiksa di sini: apakah key ini pasangan dari public key yang terdaftar
      /// di server. Ketidakcocokan baru ketahuan saat request pertama, gejalanya setiap action dijawab
      /// "action not found", dan yang menjelaskan sebabnya adalah log server.
      /// </remarks>
      private static RsaKeyPair LoadSigningKey(string name, string privateKey) {
         // RsaKeyPair tidak punya bentuk "private key saja" - konstruktornya menuntut public key. Jadi
         // private key di-import sekali di sini, public key-nya diturunkan dari situ (DER PKCS#1 private
         // key sudah memuat komponen publiknya), baru pasangan lengkapnya disusun.
         using var rsa = RSA.Create();
         try {
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(privateKey), out _);
         }
         catch (Exception x) when (x is FormatException or CryptographicException) {
            throw new InvalidOperationException(
               $"Debug key '{name}' is not a readable RSA private key. It must be Base64 of a DER PKCS#1 private key " +
               "(RSA.ExportRSAPrivateKey) - note that what belongs here is the private key, not the public one.", x);
         }

         var keyPair = new RsaKeyPair(Convert.ToBase64String(rsa.ExportRSAPublicKey()), privateKey);
         var probe = Encoding.ASCII.GetBytes($"em.debugkey.probe:{name}");

         try {
            if (keyPair.VerifyData(probe, keyPair.SignData(probe))) {
               return keyPair;
            }
         }
         catch (CryptographicException x) {
            throw new InvalidOperationException(
               $"Debug key '{name}' could not be used to sign with the algorithm this application requires.", x);
         }

         throw new InvalidOperationException(
            $"Debug key '{name}' produced a signature that does not verify against its own public key.");
      }

      internal ApiConnection? DefaultConnection { get; set; }
   }
}
