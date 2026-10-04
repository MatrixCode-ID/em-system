namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Sesi yang disimpan supaya pengguna tidak perlu mengetik password lagi setelah aplikasi ditutup.
   /// Yang disimpan hanya refresh token berikut keterangan pemiliknya - access token tidak ikut,
   /// karena umurnya beberapa menit dan sudah pasti mati sebelum aplikasi dibuka lagi.
   /// </summary>
   /// <param name="RefreshToken">Refresh token yang ditukar saat aplikasi dibuka kembali.</param>
   /// <param name="cUserId">Pemilik sesi, dipakai memuat identitasnya tanpa menebak dari nama akun.</param>
   /// <param name="cUserAccount">Nama akun pemilik sesi, untuk mengisi layar login kalau pemulihannya gagal.</param>
   public sealed record SavedSession(string RefreshToken, string cUserId, string cUserAccount);

   /// <summary>
   /// Tempat sesi tersimpan dititipkan, satu per profil koneksi. Kontraknya ada di sini sementara
   /// pelaksananya ada di layer yang tahu sistem operasinya: yang menyimpan sebuah refresh token wajib
   /// mengikatnya ke akun mesin yang sedang berjalan, dan cara melakukannya berbeda-beda per platform.
   /// </summary>
   public interface ISessionStorage
   {
      /// <summary>Menyimpan (atau menimpa) sesi untuk sebuah profil koneksi.</summary>
      /// <param name="profileName">Nama profil koneksi pemilik sesi.</param>
      /// <param name="session">Sesi yang disimpan.</param>
      void Save(string profileName, SavedSession session);

      /// <summary>
      /// Membaca sesi tersimpan milik sebuah profil koneksi; <c>null</c> kalau tidak ada, atau kalau
      /// yang tersimpan sudah tidak bisa dibuka lagi.
      /// </summary>
      /// <param name="profileName">Nama profil koneksi yang dicari sesinya.</param>
      SavedSession? Load(string profileName);

      /// <summary>Membuang sesi tersimpan milik sebuah profil koneksi.</summary>
      /// <param name="profileName">Nama profil koneksi yang sesinya dibuang.</param>
      void Clear(string profileName);
   }
}
