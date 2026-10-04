using Em.Api.Core.Models;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Layar approval dilihat dari sisi panel milik modul: request mana yang sedang dibuka, isian apa
   /// yang akan dikirim bersama keputusannya, dan cara meminta layarnya dimuat ulang.
   /// </summary>
   /// <remarks>
   /// Panel modul tidak pernah memegang layar approval-nya sendiri, hanya antarmuka ini. Itu yang
   /// membuat panel yang sama bisa dipasang di layar approval, di tab approval pada layar dokumennya,
   /// maupun di mode buka dokumen - tanpa tahu di mana ia sedang berada.
   /// <para>
   /// Panel <b>tidak boleh</b> mengubah dokumen yang sedang diputuskan: dokumennya terkunci selama
   /// request berjalan. Yang boleh dilakukannya adalah menampilkan keterangan, menjalankan action atas
   /// data lain, membuka layar lain, mengisi <see cref="InputPayload"/>, dan meminta layarnya dimuat
   /// ulang lewat <see cref="RefreshAsync"/>.
   /// </para>
   /// </remarks>
   public interface IApprovalPanelHost
   {
      /// <summary>Request yang sedang dibuka.</summary>
      ApprovalRequestInfo Request { get; }

      /// <summary>
      /// Langkah yang sedang diputuskan, atau kosong kalau panelnya tidak terikat satu langkah
      /// tertentu.
      /// </summary>
      string? StepName { get; }

      /// <summary>
      /// Isian yang akan dikirim bersama keputusan, berupa teks JSON milik modul. Panel isian menulisnya
      /// setiap kali isinya berubah; layar approval mengirimkannya apa adanya, dan server menyerahkannya
      /// kembali ke modul untuk diperiksa.
      /// </summary>
      string? InputPayload { get; set; }

      /// <summary>
      /// Apakah isian panel ini sudah lengkap sehingga keputusannya boleh dikirim. Dipakai layar
      /// approval untuk mematikan tombol keputusan selama isiannya belum sah.
      /// </summary>
      bool IsInputValid { get; set; }

      /// <summary>
      /// Meminta layar approval memuat ulang keadaannya: rincian request-nya dibaca ulang, pemeriksaan
      /// "boleh diputuskan sekarang" dijalankan ulang, dan panel-panel lain ikut dimuat ulang. Dipakai
      /// panel yang baru mengubah sesuatu di luar dokumennya - misalnya mencatat pembayaran yang
      /// sedang ditunggu langkah itu.
      /// </summary>
      Task RefreshAsync();
   }

   /// <summary>
   /// Panel milik modul yang dipasang di layar approval. Diimplementasikan view model panelnya.
   /// </summary>
   /// <remarks>
   /// Layar approval membuat panelnya, mengisi <see cref="Host"/>, lalu memanggil
   /// <see cref="LoadAsync"/> - dan memanggil <see cref="LoadAsync"/> lagi setiap kali keadaannya dimuat
   /// ulang, jadi method itu harus bisa dipanggil berulang.
   /// </remarks>
   public interface IApprovalPanel
   {
      /// <summary>Layar approval yang memasang panel ini.</summary>
      IApprovalPanelHost Host { get; set; }

      /// <summary>
      /// Memuat isi panel. Dipanggil saat panelnya dipasang dan setiap kali layarnya dimuat ulang.
      /// </summary>
      Task LoadAsync();
   }
}
