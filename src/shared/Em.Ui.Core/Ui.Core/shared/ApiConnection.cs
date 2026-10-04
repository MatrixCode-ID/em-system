using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Model satu profil koneksi API tersimpan (host, timeout, dsb.), disimpan di Registry lewat
   /// <see cref="Core.EmApp"/>.
   /// </summary>
   public class ApiConnection : NotifyPropertyBase
   {
      /// <summary>
      /// Nama profil koneksi, dipakai sebagai identifier unik antar koneksi.
      /// </summary>
      public string ProfileName {
         get => Get<string>();
         set => Set(value);
      }

      /// <summary>
      /// Alamat/host server API tujuan.
      /// </summary>
      public string Host {
         get => Get<string>();
         set => Set(value);
      }

      /// <summary>
      /// Timeout koneksi dalam detik.
      /// </summary>
      public int Timeout {
         get => Get<int>();
         set => Set(value);
      }

      /// <summary>
      /// Jika <c>true</c>, error validasi sertifikat SSL/TLS diabaikan saat koneksi ke server ini.
      /// </summary>
      public bool IgnoreSslErrors {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> kalau profil ini berasal dari konfigurasi debug yang ditulis di kode
      /// (<c>DebugBuilder</c>), bukan dari Registry. Koneksi debug hanya ikut ditampilkan di daftar UI;
      /// ia tidak boleh disimpan, diubah, maupun dihapus lewat dialog koneksi.
      /// </summary>
      public bool IsDebugConnection {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Token debug yang dikirim di setiap request lewat koneksi ini, atau <c>null</c> kalau koneksinya
      /// bukan koneksi debug. Yang tersimpan di sini adalah token yang sudah jadi - hasil tanda tangan yang
      /// dibuat sekali saat aplikasi start - bukan key-nya, sehingga jalur request tidak menyentuh
      /// kriptografi sama sekali.
      /// </summary>
      /// <remarks>
      /// Property ini tidak pernah ikut tersimpan ke Registry: penyimpanan koneksi menulis field-nya satu
      /// per satu, dan koneksi debug memang sudah ditolak masuk Registry sejak awal.
      /// </remarks>
      public string? DebugToken {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>
      /// Membuat client API untuk profil ini, lengkap dengan handler yang sudah mengikuti
      /// <see cref="IgnoreSslErrors"/>. Handler-nya dibuat per koneksi, bukan dipakai bersama, karena
      /// pilihan mematikan validasi sertifikat berlaku untuk satu server saja - dan karena
      /// <see cref="ApiClient"/> melepas handler-nya sendiri saat di-dispose.
      /// </summary>
      public ApiClient CreateApiClient() =>
         ApiClient.Create(this, Defaults.CreateHttpClientHandler(IgnoreSslErrors));
   }
}