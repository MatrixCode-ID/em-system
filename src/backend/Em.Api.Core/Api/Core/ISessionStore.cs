using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Satu baris sesi dalam bentuk yang tidak lagi menyebut tabel asalnya. Hash refresh token-nya
   /// sengaja tidak ikut: yang membutuhkannya hanya pencarian baris, dan itu urusan store, bukan
   /// urusan siapa pun yang menerima catatan ini.
   /// </summary>
   internal sealed record SessionRecord(
      string SessionId,
      string AccountId,
      SessionState State,
      DateTime Expiry,
      DateTime StartedAt,
      DateTime UpdatedAt);

   /// <summary>
   /// Tempat menyimpan sesi masuk. Ada dua: satu untuk pengguna biasa yang punya baris di tabel
   /// pengguna, satu lagi untuk akun sistem yang tidak punya. Keduanya dipisah oleh tabel, bukan
   /// oleh percabangan di dalam penerbit token - dengan begini <see cref="TokenServices"/> memilih
   /// store sekali di depan, lalu memperlakukan sesi siapa pun dengan cara yang sama.
   /// </summary>
   internal interface ISessionStore
   {
      /// <summary>Membuka sesi baru dalam keadaan aktif.</summary>
      Task AddAsync(string sessionId, string accountId, string hash, DateTime expiry);

      /// <summary>Mencari sesi lewat hash refresh token-nya; <c>null</c> kalau tidak ada.</summary>
      Task<SessionRecord?> FindByHashAsync(string hash);

      /// <summary>
      /// Mencari sesi lewat id-nya; <c>null</c> kalau sesinya tidak ada di store ini - dan itulah yang
      /// membedakan sesi yang memang tidak ada dari sesi milik store sebelah, karena id sesi saja tidak
      /// menyebutkan tabel asalnya.
      /// </summary>
      Task<SessionRecord?> FindByIdAsync(string sessionId);

      /// <summary>
      /// Mengubah status satu sesi. Mengembalikan <c>false</c> kalau sesinya tidak ada di store ini -
      /// itulah yang membedakan sesi milik orang lain dari sesi milik store sebelah, karena id sesi
      /// saja tidak menyebutkan tabel asalnya.
      /// </summary>
      Task<bool> SetStateAsync(string sessionId, SessionState state);

      /// <summary>
      /// Mencabut satu sesi sekaligus membuka penggantinya dalam satu kali simpan. Dipisahkan dari
      /// <see cref="SetStateAsync"/> + <see cref="AddAsync"/> justru karena keduanya harus berhasil
      /// atau gagal bersama: kalau pencabutan berhasil tapi penggantinya tidak, pemegang token
      /// kehilangan sesinya tanpa sebab; kalau urutannya dibalik, refresh token lama sempat hidup
      /// berdampingan dengan yang baru.
      /// </summary>
      Task RotateAsync(string oldSessionId, string newSessionId, string accountId, string hash, DateTime expiry);

      /// <summary>Mencabut seluruh sesi yang masih aktif milik satu akun.</summary>
      Task RevokeAllAsync(string accountId);

      /// <summary>Seluruh sesi milik satu akun, terbaru lebih dulu.</summary>
      Task<SessionRecord[]> ListByAccountAsync(string accountId);

      /// <summary>
      /// Membereskan sesi mati dalam dua tahap: yang masa berlakunya sudah lewat tapi masih
      /// tertulis aktif ditandai kedaluwarsa, lalu yang sudah lewat lebih lama dari
      /// <paramref name="retention"/> dibuang.
      /// </summary>
      Task PurgeAsync(TimeSpan retention);
   }
}
