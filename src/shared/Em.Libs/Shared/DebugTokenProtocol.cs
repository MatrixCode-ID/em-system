using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Aturan penyusunan token debug - jalan masuk pengembang yang tidak menuntut password maupun access token.
   /// Pengembang memegang private key di mesinnya, menandatangani sebuah payload kecil, lalu menempelkan
   /// hasilnya ke header <see cref="Defaults.DebugTokenHeader"/>; server hanya menyimpan public key-nya dan
   /// memverifikasi tanda tangan itu di setiap request. Kelas ini dipakai kedua sisi - client menyusun token,
   /// server membacanya - supaya byte yang ditandatangani dan yang diverifikasi persis sama.
   /// </summary>
   /// <remarks>
   /// Bentuk token: <c>&lt;payload&gt;.&lt;signature&gt;</c>, keduanya Base64Url.
   /// <para>
   /// Yang ditandatangani adalah byte ASCII dari segmen payload itu sendiri, bukan JSON mentahnya. Dengan
   /// begitu tidak ada urusan kanonikalisasi JSON: yang dibuktikan tanda tangan persis deretan byte yang
   /// muncul di header, sehingga perbedaan sekecil apa pun pada payload membuat verifikasi gagal.
   /// </para>
   /// <para>
   /// Nama key ada di dalam payload, bukan di luarnya, supaya ikut tertutup tanda tangan - kalau ditaruh di
   /// luar, siapa pun bisa menukarnya. Server mem-parse payload dulu untuk tahu public key mana yang harus
   /// dipakai, baru memverifikasi tanda tangannya dengan key tersebut.
   /// </para>
   /// </remarks>
   public static class DebugTokenProtocol
   {
      /// <summary>
      /// Panjang maksimum token yang mau diproses. Token yang sah panjangnya sekitar 400 karakter (tanda
      /// tangan RSA-2048 = 256 byte), jadi batas ini longgar; gunanya supaya pemanggil yang belum terbukti
      /// apa-apa tidak bisa memaksa server men-decode blob besar.
      /// </summary>
      public const int MaxTokenLength = 2048;

      /// <summary>
      /// Menyusun token debug yang sudah ditandatangani.
      /// </summary>
      /// <param name="name">
      /// Nama key, harus sama persis dengan nama yang didaftarkan di server. Nama inilah yang dipakai server
      /// untuk mencari public key pasangannya.
      /// </param>
      /// <param name="key">Pasangan key milik pengembang; wajib membawa private key karena dipakai menandatangani.</param>
      /// <param name="issuedAtUtc">Waktu penerbitan token dalam UTC, dipakai server untuk menghitung masa berlakunya.</param>
      /// <exception cref="ArgumentException">Dilempar kalau <paramref name="name"/> kosong.</exception>
      /// <exception cref="InvalidOperationException">Dilempar kalau <paramref name="key"/> tidak punya private key.</exception>
      public static string Create(string name, RsaKeyPair key, DateTime issuedAtUtc) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Debug token key name must not be empty.", nameof(name));
         }

         var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            new TokenPayload(name, new DateTimeOffset(issuedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds()));
         var payloadSegment = Base64Url.EncodeToString(payloadJson);
         var signature = key.SignData(Encoding.ASCII.GetBytes(payloadSegment));

         return $"{payloadSegment}.{Base64Url.EncodeToString(signature)}";
      }

      /// <summary>
      /// Memecah token menjadi bagian-bagiannya dan membaca isi payload-nya. Method ini sengaja tidak
      /// memverifikasi tanda tangan: hanya server yang tahu public key mana milik nama tersebut, jadi
      /// verifikasinya dilakukan di sana dengan <paramref name="signedPayload"/> dan <paramref name="signature"/>
      /// yang dikembalikan di sini.
      /// </summary>
      /// <param name="token">Isi header token debug apa adanya.</param>
      /// <param name="name">Nama key yang disebut token, kosong kalau token tidak terbaca.</param>
      /// <param name="issuedAtUtc">Waktu penerbitan token dalam UTC.</param>
      /// <param name="signedPayload">Byte yang benar-benar ditandatangani, siap diserahkan ke <c>RsaKeyPair.VerifyData</c>.</param>
      /// <param name="signature">Tanda tangan atas <paramref name="signedPayload"/>.</param>
      /// <returns><c>true</c> kalau token bisa dipecah dan payload-nya terbaca utuh.</returns>
      public static bool TryRead(string token, out string name, out DateTime issuedAtUtc,
         out byte[] signedPayload, out byte[] signature) {
         name = string.Empty;
         issuedAtUtc = default;
         signedPayload = [];
         signature = [];

         if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) {
            return false;
         }

         var separator = token.IndexOf('.');
         if (separator <= 0 || separator == token.Length - 1) {
            return false;
         }

         var payloadSegment = token[..separator];
         var signatureSegment = token[(separator + 1)..];

         // Satu titik dan tidak lebih. Segmen ketiga berarti bentuknya bukan token ini, dan menerimanya
         // diam-diam akan membuat byte yang diverifikasi berbeda dari byte yang ditandatangani.
         if (signatureSegment.Contains('.')) {
            return false;
         }

         TokenPayload? payload;
         try {
            payload = JsonSerializer.Deserialize<TokenPayload>(Base64Url.DecodeFromChars(payloadSegment));
            signature = Base64Url.DecodeFromChars(signatureSegment);
         }
         catch (Exception x) when (x is FormatException or JsonException) {
            signature = [];
            return false;
         }

         if (payload is null || string.IsNullOrWhiteSpace(payload.Name)) {
            signature = [];
            return false;
         }

         try {
            issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt).UtcDateTime;
         }
         catch (ArgumentOutOfRangeException) {
            signature = [];
            return false;
         }

         name = payload.Name;
         signedPayload = Encoding.ASCII.GetBytes(payloadSegment);
         return true;
      }

      private sealed record TokenPayload(
         [property: JsonPropertyName("name")] string Name,
         [property: JsonPropertyName("iat")] long IssuedAt);
   }
}
