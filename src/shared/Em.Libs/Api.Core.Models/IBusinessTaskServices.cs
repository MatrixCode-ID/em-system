using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Pemantauan business task: pekerjaan panjang yang dijalankan server di luar request, supaya
   /// hasilnya tidak bergantung pada aplikasi client yang tetap terbuka. Task dimulai oleh action module
   /// masing-masing, bukan lewat sini; kontrak ini hanya untuk melihat task, membatalkan, membersihkan,
   /// mengambil hasil, dan mengatur batas jumlah task yang berjalan bersamaan.
   /// </summary>
   /// <remarks>
   /// Yang bisa dilihat lewat sini adalah task <see cref="BusinessTaskScope.Personal"/> milik pemanggil,
   /// atau seluruh task bagi pemegang claim <see cref="ManagerClaim"/> di module
   /// <see cref="Defaults.AdministrativeToolsModuleName"/>. Status task
   /// <see cref="BusinessTaskScope.Global"/> milik sebuah layar module ditanyakan lewat service module itu
   /// sendiri, bukan lewat sini. Flag <see cref="BusinessTaskInfo.CanCancel"/>,
   /// <see cref="BusinessTaskInfo.CanClear"/>, dan <see cref="BusinessTaskInfo.CanReadResult"/> pada
   /// setiap hasil sudah dihitung untuk pemanggilnya, jadi UI tidak perlu menebak hak.
   /// </remarks>
   public interface IBusinessTaskServices : IServices
   {
      /// <summary>
      /// Nama claim yang membuka layar Business Task Manager, ditulis tanpa nama module-nya. Pemegangnya
      /// yang bukan administrator hanya bisa melihat.
      /// </summary>
      const string ManagerClaim = "Business Task Manager Access";

      #region Meta's

      /// <summary>
      /// Seluruh task di server, personal maupun global, termasuk yang sudah selesai dan masih
      /// tersimpan, lengkap dengan nama pemiliknya. Mensyaratkan claim <see cref="ManagerClaim"/>.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_BusinessTasks();

      /// <summary>
      /// Task personal milik pemanggil, yang masih hidup maupun yang sudah selesai dan masih tersimpan.
      /// Cukup login.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks();

      /// <summary>
      /// Satu task berdasarkan id-nya. Terlihat oleh pemiliknya, administrator, dan pemegang claim
      /// <see cref="ManagerClaim"/>; selain itu dijawab 404, sama seperti task yang tidak ada.
      /// </summary>
      Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id);

      /// <summary>
      /// Membatalkan task yang masih hidup. Hanya pemiliknya atau administrator (403); task yang sudah
      /// selesai dijawab 409. Task yang dibatalkan hilang sendiri tidak lama kemudian.
      /// </summary>
      Task PostMeta_BusinessTaskCancel(string id);

      /// <summary>
      /// Membersihkan task yang sudah selesai beserta hasilnya yang tersimpan. Hanya pemiliknya atau
      /// administrator (403); task yang masih hidup dijawab 409.
      /// </summary>
      Task PostMeta_BusinessTaskClear(string id);

      /// <summary>
      /// Hasil task yang berupa data JSON, sebagai teks JSON apa adanya. Hanya pemiliknya atau
      /// administrator; dijawab 400 kalau hasil task itu bukan JSON, dan 409 kalau task-nya belum sukses.
      /// </summary>
      Task<string> GetMeta_BusinessTaskJsonResult(string id);

      /// <summary>
      /// Isi file hasil task, sebagai stream yang dibaca sampai habis lalu ditutup pemanggilnya. Hanya
      /// pemiliknya atau administrator; dijawab 400 kalau hasil task itu bukan file, dan 409 kalau
      /// task-nya belum sukses.
      /// </summary>
      Task<Stream> GetMeta_BusinessTaskFileResult(string id);

      /// <summary>Batas jumlah task yang boleh berjalan bersamaan. Mensyaratkan claim <see cref="ManagerClaim"/>.</summary>
      Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit();

      /// <summary>
      /// Mengubah batas jumlah task yang boleh berjalan bersamaan. Hanya administrator. Berlaku seketika:
      /// task yang antri langsung dijalankan kalau batas barunya mengizinkan. Task yang sudah berjalan
      /// tidak dihentikan walau batas barunya lebih kecil.
      /// </summary>
      Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit);

      #endregion
   }
}
