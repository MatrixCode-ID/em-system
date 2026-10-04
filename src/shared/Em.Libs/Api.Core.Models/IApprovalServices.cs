using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Kontrak approval milik engine: melihat request, memutuskannya, menarik kembali, berkomentar, dan
   /// mengambil PDF-nya.
   /// </summary>
   /// <remarks>
   /// Keputusan adalah action engine, bukan action modul: satu action untuk semua jenis dokumen, supaya
   /// layar approval bisa memutuskan banyak request sekaligus tanpa tahu modul mana pemiliknya. Yang
   /// disediakan modul adalah hal-hal yang hanya ia pahami - cara mengajukan, cara memuat dan menerapkan
   /// datanya, dan isian per langkah.
   /// <para>
   /// Siapa yang boleh melihat sebuah request: pemegang claim langkah mana pun di alur jenis dokumen itu
   /// (termasuk pengajunya), pemegang claim lihat jenis dokumen itu, dan user yang saklar
   /// administratornya menyala. Hak berkomentar sama dengan hak melihat. Pemeriksaan yang sama berlaku
   /// untuk semua action baca di sini, termasuk PDF-nya.
   /// </para>
   /// <para>
   /// Approval adalah pengecualian dari aturan "administrator bisa semua" dalam satu hal: akun
   /// administrator bawaan dan akun debugger <b>tidak bisa</b> menandatangani, karena tanda tangan harus
   /// menunjuk user nyata. Debugger mengujinya dengan berpindah menjadi user nyata.
   /// </para>
   /// </remarks>
   public interface IApprovalServices : IServices
   {
      #region Meta's

      /// <summary>Jenis dokumen approval yang boleh dilihat pemanggil, termasuk yang belum memiliki request.</summary>
      Task<string[]> GetMeta_ApprovalDocumentTypes();

      /// <summary>
      /// Daftar request yang boleh dilihat pemanggil, disaring dan dihalamankan di server.
      /// </summary>
      /// <param name="query">Penyaring, pengurut, dan halaman yang diminta.</param>
      Task<PagedResult<ApprovalRequestInfo>> GetMeta_ApprovalRequests(ApprovalQuery query);

      /// <summary>
      /// Seluruh request untuk satu dokumen, termasuk yang sudah selesai, ditolak, dan ditarik kembali.
      /// Dipakai panel status approval di layar dokumennya, dan dipakai client untuk mengambil keadaan
      /// terbaru sesudah mengajukan.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="docKey">Kunci dokumennya dalam bentuk kanonik.</param>
      Task<ApprovalRequestInfo[]> GetMeta_ApprovalRequestsByDoc(string docType, string docKey);

      /// <summary>Rincian satu request: langkah, usulan perubahan, dan riwayatnya.</summary>
      /// <param name="approvalRequestId">Request yang diminta.</param>
      Task<ApprovalRequestDetail?> GetMeta_ApprovalRequest(string approvalRequestId);

      /// <summary>
      /// PDF dokumen sebuah request, lengkap dengan tanda tangan yang sudah dibubuhkan dan isian tiap
      /// langkah. Dibuat saat diminta dari PDF dasar yang dibekukan waktu pengajuan, jadi ia selalu
      /// mencerminkan keadaan terakhir tanpa menyimpan satu berkas per keputusan.
      /// </summary>
      /// <param name="approvalRequestId">Request yang PDF-nya diminta.</param>
      /// <returns>Isi PDF-nya.</returns>
      Task<Stream> GetMeta_ApprovalRequestPdf(string approvalRequestId);

      /// <summary>
      /// Memeriksa apakah sebuah langkah boleh diputuskan sekarang. Dipanggil ulang setiap layar dibuka
      /// atau dimuat ulang, karena syaratnya dibaca dari keadaan saat itu - blokir yang syaratnya sudah
      /// terpenuhi akan membuka sendiri.
      /// </summary>
      /// <param name="approvalRequestId">Request yang diperiksa.</param>
      /// <param name="stepName">Langkah yang diperiksa.</param>
      Task<ApprovalGuardResult> GetMeta_ApprovalGuard(string approvalRequestId, string stepName);

      /// <summary>
      /// Memutuskan satu langkah atau beberapa langkah sekaligus. Setiap keputusan diproses dalam
      /// transaksinya sendiri, jadi satu yang gagal tidak menggagalkan yang lain - karena itu hasilnya
      /// berupa daftar, satu baris per keputusan.
      /// </summary>
      /// <param name="decisions">Keputusan-keputusan yang diambil.</param>
      Task<ApprovalDecisionResult[]> PostGetMeta_ApprovalDecide(ApprovalDecision[] decisions);

      /// <summary>
      /// Menarik kembali sebuah request sehingga dokumennya bisa diedit lagi. Boleh juga untuk request
      /// yang sudah selesai seluruhnya; dalam hal itu modul pemiliknya diberi kesempatan mencabut status
      /// yang sudah ditulis, dan boleh menolak kalau dokumennya sudah diproses lebih lanjut.
      /// </summary>
      /// <param name="approvalRequestId">Request yang ditarik kembali.</param>
      /// <param name="reason">Alasan penarikan. Wajib.</param>
      Task PostMeta_ApprovalCancel(string approvalRequestId, string reason);

      /// <summary>Menulis komentar bebas pada sebuah request.</summary>
      /// <param name="approvalRequestId">Request yang dikomentari.</param>
      /// <param name="note">Isi komentarnya.</param>
      Task PostMeta_ApprovalComment(string approvalRequestId, string note);

      /// <summary>
      /// Seluruh daftar pekerjaan user aktif, dikumpulkan dari semua sumber yang terdaftar: pekerjaan
      /// panjang yang sedang berjalan, dokumen yang menunggu tanda tangannya, dan usulan perubahan data
      /// yang menunggu keputusannya.
      /// </summary>
      Task<HubTaskInfo[]> GetMeta_UserHubTasks();

      /// <summary>
      /// PDF contoh dengan kotak tanda tangan dan kotak isian tergambar beserta namanya, untuk mengukur
      /// posisinya terhadap rancangan dokumen yang sebenarnya. Alat developer: hanya untuk debugger dan
      /// user yang saklar administratornya menyala.
      /// </summary>
      /// <param name="docType">Jenis dokumen yang kotak-kotaknya digambar.</param>
      /// <param name="docKey">Satu dokumen nyata sebagai dasar gambarnya.</param>
      /// <param name="docVersion">Versi dokumen itu.</param>
      /// <returns>Isi PDF contohnya.</returns>
      Task<Stream> GetMeta_ApprovalSlotCalibration(string docType, string docKey, string docVersion);

      #endregion
   }
}
