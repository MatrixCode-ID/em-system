namespace Em.Api.Core.Models
{
   /// <summary>Jenis approval: apakah data usulannya menumpang di request, atau sudah ada di dokumennya.</summary>
   public enum ApprovalKind
   {
      /// <summary>
      /// Data usulan tinggal di request dan baru diterapkan setelah disetujui. Dipakai perubahan data
      /// master, tempat tabel aslinya belum boleh berubah selama request masih menunggu.
      /// </summary>
      Data = 1,

      /// <summary>
      /// Datanya sudah ada di dokumennya, dan request hanya menjadi gerbang bagi statusnya. Dipakai
      /// dokumen transaksi yang perlu ditandatangani beberapa pihak.
      /// </summary>
      Document = 2
   }

   /// <summary>
   /// Tahap hidup satu request approval. Mengikuti konvensi kolom tahap: nilai negatif berarti request
   /// tidak berjalan lagi, nol ke atas berarti masih hidup atau selesai dengan baik.
   /// </summary>
   public enum ApprovalStage
   {
      /// <summary>Ditarik kembali setelah seluruh langkahnya selesai. Tetap tersimpan sebagai riwayat.</summary>
      ReinstatedAfterFinish = -3,

      /// <summary>Ditarik kembali saat masih menunggu keputusan.</summary>
      Cancelled = -2,

      /// <summary>Ditolak di salah satu langkahnya. Seluruh request berhenti.</summary>
      Rejected = -1,

      /// <summary>Belum diajukan.</summary>
      Draft = 0,

      /// <summary>Sudah diajukan dan masih menunggu keputusan.</summary>
      Pending = 1,

      /// <summary>Seluruh langkahnya sudah disetujui.</summary>
      Approved = 2
   }

   /// <summary>
   /// Keadaan satu langkah di dalam request. Mengikuti konvensi yang sama: negatif berarti langkah itu
   /// tidak menghasilkan tanda tangan.
   /// </summary>
   public enum ApprovalStepStatus
   {
      /// <summary>Dilewati karena syarat berlakunya tidak terpenuhi, atau karena request berhenti lebih dulu.</summary>
      Skipped = -2,

      /// <summary>Ditolak.</summary>
      Rejected = -1,

      /// <summary>Masih menunggu keputusan.</summary>
      Waiting = 0,

      /// <summary>Sudah disetujui dan ditandatangani.</summary>
      Approved = 1
   }

   /// <summary>Apa yang diusulkan atas satu entitas di dalam request data approval.</summary>
   public enum ApprovalItemOperation
   {
      /// <summary>Membuat entitas baru.</summary>
      Create = 1,

      /// <summary>Mengubah sebagian kolom entitas yang sudah ada.</summary>
      Update = 2,

      /// <summary>Menandai entitas sebagai terhapus, tanpa membuangnya.</summary>
      Delete = 3,

      /// <summary>Mengaktifkan kembali entitas yang sebelumnya ditandai terhapus.</summary>
      Reinstate = 4
   }

   /// <summary>
   /// Hasil penerapan satu entitas usulan, dicatat setelah request disetujui. Ini bukan keputusan
   /// approver - keputusan selalu berlaku untuk seluruh request - melainkan apa yang benar-benar terjadi
   /// pada entitas itu.
   /// </summary>
   public enum ApprovalItemStage
   {
      /// <summary>Tidak bisa diterapkan karena nilainya sudah berubah di luar dan penimpaan tidak diizinkan.</summary>
      Conflicted = -1,

      /// <summary>Belum diterapkan.</summary>
      Pending = 0,

      /// <summary>Diterapkan apa adanya.</summary>
      Applied = 1,

      /// <summary>Dilewati karena nilainya sudah sama dengan yang diusulkan.</summary>
      Skipped = 2,

      /// <summary>Diterapkan walaupun nilainya sudah berubah di luar, atas keputusan sadar approver.</summary>
      Overridden = 3
   }

   /// <summary>Kenapa sebuah tanda tangan dianggap sah, dan atas dasar apa ia dibubuhkan.</summary>
   public enum ApprovalSignerRole
   {
      /// <summary>Penanda tangan yang memang ditetapkan untuk langkah itu.</summary>
      Assigned = 0,

      /// <summary>
      /// Pengganti: pemegang claim langkah itu yang bukan penanda tangan tercatat. Tanda tangannya
      /// tercatat atas nama penanda tangan utama, dan alasannya wajib.
      /// </summary>
      Substitute = 1,

      /// <summary>
      /// Penembus blokir: pemegang claim penembus yang menandatangani walaupun syarat langkah itu belum
      /// terpenuhi. Sifatnya darurat, jadi tanda tangannya diberi tanda khusus.
      /// </summary>
      Override = 2
   }
}
