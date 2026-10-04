using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pertanyaan "apa yang menunggu keputusan user yang sedang memanggil", dijawab langsung dari tabel approval tanpa
   /// memuat dokumennya. Dipakai sumber daftar pekerjaan di baris judul aplikasi.
   /// </summary>
   /// <remarks>
   /// Hasilnya sudah dikelompokkan - per jenis approval, lalu per jenis dokumen - karena itulah bentuk
   /// yang ditampilkan daftar pekerjaan: satu baris per jenis dokumen, dengan jumlah dan umur request
   /// tertua, bukan satu baris per request.
   /// <para>
   /// Yang dihitung hanya request yang benar-benar menunggu orang itu: langkah di level yang sedang
   /// berjalan, dan - untuk langkah yang penanda tangannya ditetapkan per orang - hanya kalau ia
   /// tercatat sebagai penanda tangannya. Pemegang claim lain tidak melihatnya di sini; mereka
   /// menemukannya di layar approval.
   /// </para>
   /// </remarks>
   public interface IApprovalHubQuery
   {
      /// <summary>
      /// Ringkasan request yang menunggu keputusan pemanggil request yang sedang berjalan, atau daftar
      /// kosong kalau tidak ada.
      /// </summary>
      /// <remarks>
      /// Pemanggilnya dibaca dari request, bukan diberikan sebagai parameter: yang menentukan apa yang
      /// menunggunya adalah hak yang ia pegang saat ini, dan itu hanya diketahui lewat request-nya.
      /// Akun sistem bawaan tidak pernah punya yang menunggu.
      /// </remarks>
      /// <param name="cancellationToken">Token pembatalan.</param>
      Task<IReadOnlyList<ApprovalHubGroup>> GetWaitingForCallerAsync(CancellationToken cancellationToken = default);
   }

   /// <summary>
   /// Satu baris daftar pekerjaan approval: satu jenis dokumen pada satu jenis approval, beserta jumlah
   /// dan umur request tertuanya.
   /// </summary>
   /// <param name="Kind">Jenis approval-nya, yang menentukan di bagian mana baris ini tampil.</param>
   /// <param name="DocType">Jenis dokumennya.</param>
   /// <param name="Count">Berapa request jenis dokumen itu yang menunggu user ini.</param>
   /// <param name="OldestRequestDate">
   /// Kapan request tertua di antaranya diajukan. Dari situ daftar pekerjaan menghitung umurnya.
   /// </param>
   public record ApprovalHubGroup(ApprovalKind Kind, string DocType, int Count, DateTime OldestRequestDate);
}
