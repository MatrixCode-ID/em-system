using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   public partial class EmAppBuilder
   {
      internal ApprovalPanelRegistry ApprovalPanels { get; } = new();

      /// <summary>
      /// Mendaftarkan panel isian sebuah langkah: control milik modul yang tampil di layar approval saat
      /// langkah itu diputuskan, dan isinya dikirim bersama keputusannya.
      /// </summary>
      /// <typeparam name="TView">Tampilan panelnya.</typeparam>
      /// <typeparam name="TViewModel">
      /// View model panelnya. Layar approval mengisi <see cref="IApprovalPanel.Host"/> lalu memanggil
      /// <see cref="IApprovalPanel.LoadAsync"/>, dan memanggilnya lagi setiap kali layarnya dimuat
      /// ulang.
      /// </typeparam>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="stepName">
      /// Langkah yang panelnya dipasang, nama yang sama dengan yang dideklarasikan alurnya di server.
      /// </param>
      /// <remarks>
      /// Langkah yang punya panel isian diputuskan satu per satu lewat panelnya, jadi ia tidak ikut
      /// kalau beberapa request disetujui sekaligus dari daftar.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau jenis dokumen atau nama langkahnya kosong.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau langkah itu sudah punya panel isian.
      /// </exception>
      public void AddApprovalStepPanel<TView, TViewModel>(string docType, string stepName)
         where TViewModel : IApprovalPanel {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
         ApprovalPanels.Add(new ApprovalStepPanelRegistration(docType, stepName, typeof(TView), typeof(TViewModel)));
      }

      /// <summary>
      /// Mendaftarkan kartu informasi: control milik modul yang tampil di samping dokumen di layar
      /// approval, untuk keterangan yang perlu dilihat penanda tangan sebelum memutuskan.
      /// </summary>
      /// <typeparam name="TView">Tampilan kartunya.</typeparam>
      /// <param name="docType">Jenis dokumen tempat kartunya tampil.</param>
      /// <param name="steps">
      /// Langkah-langkah yang kartunya tampil. Kosong berarti kartunya tampil di semua langkah jenis
      /// dokumen itu.
      /// </param>
      /// <param name="input">
      /// Cara menyusun keterangan awal kartunya dari request yang sedang dibuka. Kosong berarti kartunya
      /// tidak butuh apa-apa.
      /// </param>
      /// <param name="claim">
      /// Claim yang harus dipegang user supaya kartunya tampil, atau kosong kalau kartunya terbuka bagi
      /// siapa pun yang boleh melihat request itu.
      /// </param>
      /// <param name="order">Urutan kartunya; yang lebih kecil tampil lebih dulu.</param>
      /// <remarks>
      /// Kartu dibuat modul pemilik datanya dan boleh dipasang oleh jenis dokumen modul lain, jadi satu
      /// kartu bisa tampil di beberapa jenis dokumen tanpa ditulis dua kali.
      /// </remarks>
      /// <exception cref="ArgumentException">Dilempar kalau jenis dokumennya kosong.</exception>
      public void AddApprovalInfoPanel<TView>(string docType, string[]? steps = null,
         Func<IApprovalPanelHost, object?>? input = null, ClaimAction? claim = null, int order = 0) {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ApprovalPanels.Add(new ApprovalInfoPanelRegistration(docType, typeof(TView), steps ?? [],
            input, claim, order));
      }

      /// <summary>
      /// Mendaftarkan cara membuka layar dokumen sebuah jenis dokumen dari layar approval, supaya
      /// penanda tangan bisa melihat dokumennya di layar aslinya - bukan hanya PDF-nya.
      /// </summary>
      /// <param name="docType">Jenis dokumennya.</param>
      /// <param name="navigationName">Nama navigasi layar dokumennya.</param>
      /// <param name="parameter">
      /// Cara menyusun parameter navigasinya dari request yang sedang dibuka. Kosong berarti layarnya
      /// dibuka tanpa parameter.
      /// </param>
      /// <remarks>
      /// Layarnya dibuka untuk dibaca: dokumen yang sedang menunggu keputusan terkunci, jadi
      /// perubahannya tetap ditolak server walaupun layarnya terbuka.
      /// </remarks>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau jenis dokumen atau nama navigasinya kosong.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau jenis dokumen itu sudah punya cara membuka dokumennya.
      /// </exception>
      public void AddApprovalDocumentOpener(string docType, string navigationName,
         Func<IApprovalPanelHost, object?>? parameter = null) {
         ArgumentException.ThrowIfNullOrWhiteSpace(docType);
         ArgumentException.ThrowIfNullOrWhiteSpace(navigationName);
         ApprovalPanels.Add(new ApprovalDocumentOpenerRegistration(docType, navigationName, parameter));
      }
   }
}
