using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pintu masuk modul ke engine approval: mengajukan, memastikan dokumen tidak sedang menunggu
   /// keputusan, dan menyimpan langsung bagi pemegang claim persetujuan.
   /// </summary>
   /// <remarks>
   /// Memutuskan sebuah langkah <b>tidak</b> ada di sini: itu action engine yang sama untuk semua jenis
   /// dokumen, supaya satu layar bisa memutuskan banyak request tanpa tahu modul pemiliknya.
   /// </remarks>
   public interface IApprovalEngine
   {
      /// <summary>
      /// Mengajukan sebuah dokumen untuk disetujui.
      /// </summary>
      /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
      /// <param name="docType">Jenis dokumennya, seperti yang dideklarasikan modul.</param>
      /// <param name="docKey">Kunci dokumen yang diajukan.</param>
      /// <param name="docVersion">Versi dokumen yang diajukan.</param>
      /// <param name="note">Catatan pengaju, opsional.</param>
      /// <returns>Id request yang terbentuk.</returns>
      /// <remarks>
      /// Yang dikerjakan engine, berurutan: memastikan pengajunya user nyata dan aktif, memeriksa claim
      /// langkah pertama, menolak kalau dokumen dan versi itu sudah punya request yang menunggu,
      /// menentukan <b>seluruh</b> penanda tangan sekaligus, membuat PDF dasarnya dan menyimpannya di luar
      /// transaksi, lalu dalam satu transaksi menulis header, semua langkah beserta salinan posisi
      /// kotaknya, daftar penanda tangan, tanda tangan otomatis langkah pertama oleh pengaju, dan maju ke
      /// level berikutnya.
      /// <para>
      /// Semua penanda tangan ditentukan di depan dengan sengaja: kesalahan muncul kepada pengaju yang
      /// bisa memperbaikinya, bukan menggagalkan keputusan orang lain di tengah alur. Itu aman karena
      /// dokumennya terkunci selama request berjalan.
      /// </para>
      /// </remarks>
      Task<string> SubmitAsync<TKey>(string docType, TKey docKey, string docVersion, string? note = null)
         where TKey : notnull;

      /// <summary>
      /// Mengajukan usulan perubahan data, atau menyimpannya langsung kalau pemanggilnya memegang claim
      /// persetujuan jenis dokumen itu.
      /// </summary>
      /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="docKey">Kunci dokumen yang diubah.</param>
      /// <param name="items">Usulan perubahan per entitas, beserta nilai lama dan nilai barunya.</param>
      /// <param name="note">Catatan pengaju, opsional.</param>
      /// <returns>Hasilnya: id request, dan apakah ia langsung diterapkan.</returns>
      /// <remarks>
      /// Pemegang claim persetujuan tidak perlu melewati layar approval untuk perubahannya sendiri -
      /// perubahannya diterapkan langsung, tapi tetap tercatat sebagai request yang otomatis disetujui,
      /// sehingga jejak auditnya sama lengkapnya. Pemeriksaan nilai lama tetap berjalan untuknya, jadi ia
      /// juga terlindung dari menimpa perubahan orang lain tanpa sadar: kalau isi tabelnya sudah berubah
      /// sejak layarnya dibuka, tidak ada yang tersimpan dan ia mendapat 409 yang menyebut kolom mana.
      /// <para>
      /// Kolom yang nilai barunya sama dengan nilai lamanya dibuang, dan entitas yang tidak punya kolom
      /// tersisa ikut dibuang; kalau tidak ada yang tersisa sama sekali, 400. Beberapa request yang
      /// menunggu untuk dokumen yang sama boleh ada bersamaan: tiap request punya penanda versinya
      /// sendiri, yaitu id-nya.
      /// </para>
      /// <para>
      /// Entitas baru memakai kunci sementara. Kunci yang dikembalikan modul saat menerapkannya menggantikan
      /// kunci sementara itu - di entitasnya, di entitas lain dalam request yang sama yang memuat bagian
      /// kunci sementara yang sama, dan di nama dokumen request kalau dokumennya adalah entitas itu
      /// sendiri. Dengan begitu induk baru dan anak-anaknya bisa diajukan dalam satu request.
      /// </para>
      /// </remarks>
      /// <exception cref="Em.Shared.ActionException">
      /// 403 kalau pemanggilnya akun sistem; 400 kalau tidak ada yang berubah; 409 kalau pemanggilnya
      /// pemegang claim persetujuan dan isi tabelnya sudah berubah sejak layarnya dibuka.
      /// </exception>
      Task<ApprovalSubmitResult> SubmitDataAsync<TKey>(string docType, TKey docKey,
         IReadOnlyList<ApprovalDataItem> items, string? note = null)
         where TKey : notnull;

      /// <summary>
      /// Memastikan sebuah dokumen tidak sedang menunggu keputusan. Dipanggil action simpan modul sebelum
      /// menulis apa pun.
      /// </summary>
      /// <typeparam name="TKey">Record kunci dokumennya.</typeparam>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="docKey">Kunci dokumen yang akan ditulis.</param>
      /// <param name="docVersion">Versi dokumen yang akan ditulis, kalau versinya ikut menentukan.</param>
      /// <exception cref="Em.Shared.ActionException">
      /// Dilempar kalau dokumen itu sedang menunggu keputusan. Inilah yang mengunci dokumen selama
      /// approval berjalan - engine tidak menulis penanda apa pun ke tabel modul untuk itu.
      /// </exception>
      Task EnsureNotInApprovalAsync<TKey>(string docType, TKey docKey, string? docVersion = null)
         where TKey : notnull;

      /// <summary>
      /// Menarik kembali sebuah request lalu menyiapkan pengajuan ulang untuk dokumen dan versi yang sama.
      /// </summary>
      /// <param name="approvalRequestId">Request yang ditarik kembali.</param>
      /// <param name="reason">Alasan penarikan. Wajib.</param>
      /// <remarks>
      /// Menarik kembali dan mengajukan ulang adalah dua langkah terpisah: dokumen dibuka dulu, diedit,
      /// lalu diajukan lagi. Request baru menyimpan tautan ke request yang ditarik, sehingga riwayatnya
      /// tersambung. Request yang sudah selesai seluruhnya juga boleh ditarik; dalam hal itu modul
      /// pemiliknya diberi kesempatan mencabut status yang sudah ditulis, dan boleh menolak penarikan
      /// kalau dokumennya sudah diproses lebih lanjut.
      /// <para>
      /// Pengajuan ulangnya memakai <see cref="SubmitAsync{TKey}"/> yang sama: kalau request terbaru
      /// untuk dokumen dan versi itu adalah yang baru ditarik, engine menautkan request baru ke sana
      /// tanpa diminta. Yang boleh menarik adalah pemegang claim langkah pertama dan user yang saklar
      /// administratornya menyala.
      /// </para>
      /// </remarks>
      Task ReinstateAsync(string approvalRequestId, string reason);
   }

   /// <summary>Hasil pengajuan usulan perubahan data.</summary>
   /// <param name="ApprovalRequestId">Id request yang terbentuk.</param>
   /// <param name="AppliedImmediately">
   /// <c>true</c> kalau perubahannya langsung diterapkan karena pengajunya memegang claim persetujuan.
   /// Layar memakainya untuk memilih pesan yang tepat: "diajukan untuk persetujuan" atau "disimpan".
   /// </param>
   public record ApprovalSubmitResult(string ApprovalRequestId, bool AppliedImmediately);

   /// <summary>Satu entitas yang diusulkan berubah, beserta kolom-kolomnya.</summary>
   /// <param name="Entity">
   /// Entitas yang disentuh, ditulis dengan awalan nama modulnya supaya tidak bertabrakan antar modul.
   /// </param>
   /// <param name="Key">Kunci entitasnya, dalam bentuk record ber-bagian kunci.</param>
   /// <param name="Operation">Apa yang diusulkan atas entitas ini.</param>
   /// <param name="Fields">Kolom yang diusulkan berubah. Kosong untuk penghapusan dan pengaktifan ulang.</param>
   public record ApprovalDataItem(string Entity, object Key, ApprovalItemOperation Operation,
      IReadOnlyList<ApprovalDataField> Fields);

   /// <summary>Satu kolom yang diusulkan berubah, beserta nilainya sebelum dan sesudah.</summary>
   /// <param name="Name">Nama kolomnya, seperti yang dipahami handler modul.</param>
   /// <param name="OldValue">
   /// Nilainya saat layar edit dimuat. Inilah yang membuat pemeriksaan konflik berfungsi sebagai pengaman
   /// terhadap perubahan orang lain, jadi jangan mengisinya dengan nilai yang baru saja dibaca.
   /// </param>
   /// <param name="NewValue">Nilai yang diusulkan.</param>
   public record ApprovalDataField(string Name, string? OldValue, string? NewValue);
}
