using Em.Api.Core.Models;

namespace Em.Api.Core.Hub
{
   /// <summary>
   /// Satu sumber daftar pekerjaan user aktif. Daftar pekerjaan di baris judul aplikasi adalah gabungan
   /// semua sumber yang terdaftar, jadi jenis pekerjaan baru ditambahkan dengan mendaftarkan sumber
   /// baru - bukan dengan mengubah daftar itu.
   /// </summary>
   /// <remarks>
   /// Daftarnya selalu <b>dihitung</b> dari data sumbernya, tidak disimpan sebagai pekerjaan tersendiri.
   /// Itu yang membuat sebuah pekerjaan hilang sendiri dari daftar orang lain begitu satu orang
   /// mengerjakannya.
   /// <para>
   /// Sumber dipanggil di dalam permintaan user yang bersangkutan, jadi identitas pemanggil dibaca dari
   /// permintaan itu seperti di action biasa. Sumber yang gagal tidak menggagalkan sumber lain:
   /// kegagalannya dicatat dan bagiannya kosong.
   /// </para>
   /// </remarks>
   public interface IHubTaskSource
   {
      /// <summary>
      /// Pekerjaan yang perlu diketahui user aktif menurut sumber ini, atau daftar kosong kalau tidak
      /// ada.
      /// </summary>
      /// <param name="cancellationToken">Token pembatalan.</param>
      Task<IReadOnlyList<HubTaskInfo>> GetTasksAsync(CancellationToken cancellationToken = default);
   }
}
