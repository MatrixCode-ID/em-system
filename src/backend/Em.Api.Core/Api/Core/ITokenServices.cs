using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Hasil pemeriksaan sebuah access token. Token yang tidak sah selalu dijawab dengan
   /// <see cref="IsValid"/> <c>false</c> berikut alasannya, bukan dengan exception - memeriksa token
   /// adalah hal yang rutin gagal (kedaluwarsa, salah tanda tangan, dipalsukan), jadi kegagalannya
   /// bagian dari jawaban, bukan kejadian luar biasa.
   /// </summary>
   /// <param name="IsValid">Benar kalau tokennya sah dan belum kedaluwarsa.</param>
   /// <param name="cUserId">Pemilik token, hanya terisi kalau tokennya sah.</param>
   /// <param name="cUserSessionId">Sesi yang menerbitkan token, hanya terisi kalau tokennya sah.</param>
   /// <param name="IsAdmin">Benar kalau pemilik token administrator saat token diterbitkan.</param>
   /// <param name="Error">Alasan token ditolak, hanya terisi kalau tokennya tidak sah.</param>
   public sealed record TokenValidation(
      bool IsValid,
      string? cUserId,
      string? cUserSessionId,
      bool IsAdmin,
      string? Error);

   /// <summary>
   /// Penerbit dan pemeriksa token akses. Service internal Engine: tidak punya action dan tidak
   /// pernah tersentuh langsung dari luar - yang memanggilnya adalah action kredensial dan, nanti,
   /// gerbang pemeriksa request.
   /// </summary>
   internal interface ITokenServices
   {
      /// <summary>
      /// Menerbitkan sepasang token baru untuk seorang pengguna beserta baris sesinya.
      /// </summary>
      /// <param name="cUserId">Pengguna yang sesinya dibuka.</param>
      /// <returns>Access token, refresh token, dan umur access token dalam detik.</returns>
      Task<TokenResult> IssueAsync(string cUserId);

      /// <summary>
      /// Memeriksa sebuah access token: tanda tangannya, masa berlakunya, dan isinya.
      /// </summary>
      /// <param name="accessToken">Token yang dikirim pemanggil.</param>
      /// <returns>Hasil pemeriksaan; lihat <see cref="TokenValidation"/>.</returns>
      Task<TokenValidation> ValidateAsync(string accessToken);

      /// <summary>
      /// Apakah sebuah sesi masih berlaku: tertulis aktif dan masa berlakunya belum lewat. Dicari di
      /// kedua tempat penyimpanan sesi, karena id sesi saja tidak menyebutkan asalnya.
      /// </summary>
      /// <param name="cUserSessionId">Sesi yang ditanyakan.</param>
      Task<bool> IsSessionActiveAsync(string cUserSessionId);

      /// <summary>
      /// Mengakhiri satu sesi sehingga refresh token-nya tidak bisa dipakai lagi.
      /// </summary>
      /// <param name="cUserSessionId">Sesi yang diakhiri.</param>
      Task RevokeAsync(string cUserSessionId);

      /// <summary>
      /// Mengakhiri seluruh sesi milik seorang pengguna, di perangkat mana pun.
      /// </summary>
      /// <param name="cUserId">Pengguna yang seluruh sesinya diakhiri.</param>
      Task RevokeAllAsync(string cUserId);

      /// <summary>
      /// Seluruh sesi milik satu akun, terbaru lebih dulu. Lewat sini, bukan lewat query langsung,
      /// karena akun sistem menyimpan sesinya di tempat yang berbeda dari pengguna biasa - dan mana
      /// yang dipakai adalah urusan service ini, bukan urusan pemanggilnya.
      /// </summary>
      /// <param name="cUserId">Akun yang sesinya didaftar.</param>
      Task<SessionRecord[]> ListSessionsAsync(string cUserId);

      /// <summary>
      /// Menukar sebuah refresh token dengan sepasang token baru. Refresh token yang ditukar
      /// langsung mati, jadi satu refresh token hanya bisa dipakai sekali.
      /// </summary>
      /// <param name="refreshToken">Refresh token yang dipegang pemanggil.</param>
      /// <returns>Pasangan token yang baru.</returns>
      Task<TokenResult> RefreshAsync(string refreshToken);
   }
}
