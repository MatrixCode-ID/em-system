using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Penggambar PDF untuk approval: menempelkan tanda tangan dan isian ke atas PDF dokumen, dan
   /// menggambar kotak-kotaknya untuk keperluan pengukuran.
   /// </summary>
   /// <remarks>
   /// Jembatan antara engine approval dan pustaka PDF yang dipakai. Engine tidak pernah menyentuh
   /// pustaka itu sendiri; ia hanya menyerahkan PDF dasar beserta potret keadaan request, dan menerima
   /// PDF jadinya. Karena itu PDF ber-tanda tangan tidak pernah disimpan: ia dibuat saat diminta dari
   /// PDF dasar yang dibekukan waktu pengajuan, sehingga selalu mencerminkan keadaan terakhir.
   /// </remarks>
   public interface IApprovalPdfRenderer
   {
      /// <summary>
      /// Menempelkan tanda tangan dan isian ke atas sebuah PDF.
      /// </summary>
      /// <param name="basePdf">
      /// Isi PDF dasar dokumennya. Dibaca dari posisinya saat ini; pemanggil yang menutupnya.
      /// </param>
      /// <param name="snapshot">Keadaan request yang digambar.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <returns>Isi PDF jadinya, siap dikirim ke pemanggil.</returns>
      Task<Stream> RenderStampedAsync(Stream basePdf, ApprovalStampSnapshot snapshot,
         CancellationToken cancellationToken = default);

      /// <summary>
      /// Menggambar kotak tanda tangan dan kotak isian beserta namanya di atas sebuah PDF, tanpa isi
      /// apa pun. Dipakai developer modul untuk mengukur posisi kotak terhadap rancangan dokumen yang
      /// sebenarnya.
      /// </summary>
      /// <param name="basePdf">
      /// Isi PDF dokumen nyata yang dipakai sebagai dasar gambarnya. Dibaca dari posisinya saat ini;
      /// pemanggil yang menutupnya.
      /// </param>
      /// <param name="slots">Kotak-kotak yang digambar beserta namanya.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <returns>Isi PDF contohnya.</returns>
      Task<Stream> RenderCalibrationAsync(Stream basePdf, IReadOnlyList<ApprovalSlotLabel> slots,
         CancellationToken cancellationToken = default);
   }

   /// <summary>
   /// Potret keadaan sebuah request saat PDF-nya digambar: langkah mana saja yang sudah diputuskan,
   /// isian apa yang diberikan, dan apa yang dicetak di tepi halaman.
   /// </summary>
   /// <remarks>
   /// Semuanya sudah berupa nilai siap cetak - nama orang, bukan id; teks isian, bukan payload mentah -
   /// supaya penggambar tidak perlu menyentuh database sama sekali.
   /// </remarks>
   public class ApprovalStampSnapshot
   {
      /// <summary>Judul dokumennya, dicetak di lembar pengesahan kalau lembar itu dinyalakan.</summary>
      public string? DocumentTitle { get; set; }

      /// <summary>
      /// Langkah-langkah yang digambar. Langkah yang belum diputuskan ikut disertakan dengan keadaan
      /// menunggu, sehingga penggambar bisa memilih sendiri apakah kotaknya dibiarkan kosong.
      /// </summary>
      public IReadOnlyList<ApprovalStampStep> Steps { get; set; } = [];

      /// <summary>Isian yang digambar, satu entri per kotak isian yang terisi.</summary>
      public IReadOnlyList<ApprovalStampInput> Inputs { get; set; } = [];

      /// <summary>
      /// Kode yang dicetak di tepi setiap halaman supaya dokumen tercetak bisa dirunut kembali ke
      /// request-nya, atau kosong kalau belum ada tanda tangan sama sekali.
      /// </summary>
      public string? PageMarginCode { get; set; }

      /// <summary>
      /// <c>true</c> kalau jenis dokumen ini meminta halaman tambahan berisi tabel seluruh langkah.
      /// </summary>
      public bool IncludeApprovalSheet { get; set; }
   }

   /// <summary>Satu langkah seperti yang digambar pada PDF.</summary>
   public class ApprovalStampStep
   {
      /// <summary>Nama langkahnya, dicetak kalau penanda tangannya tidak ditetapkan per orang.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Di mana tanda tangannya digambar.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>Keadaan langkahnya: menunggu, disetujui, ditolak, atau dilewati.</summary>
      public ApprovalStepStatus Status { get; set; }

      /// <summary>Nama orang yang menandatangani, atau kosong kalau belum ada.</summary>
      public string? SignerName { get; set; }

      /// <summary>
      /// Nama penanda tangan utama yang diwakili, kalau yang menandatangani adalah pengganti atau
      /// penembus. Kosong untuk langkah yang penanda tangannya tidak ditetapkan per orang - dalam hal
      /// itu yang dicetak sebagai "atas nama" adalah nama langkahnya.
      /// </summary>
      public string? OnBehalfName { get; set; }

      /// <summary>Atas dasar apa tanda tangannya sah.</summary>
      public ApprovalSignerRole SignerRole { get; set; }

      /// <summary>Kapan tanda tangannya dibubuhkan.</summary>
      public DateTime? SignedDate { get; set; }

      /// <summary>Kode verifikasi tanda tangan ini.</summary>
      public string? VerificationCode { get; set; }
   }

   /// <summary>Satu kotak isian beserta nilai yang digambar di dalamnya.</summary>
   public class ApprovalStampInput
   {
      /// <summary>Langkah pemilik isian ini.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Jenis kotaknya: tanda centang atau teks.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Di mana kotaknya digambar.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>Teks yang digambar, untuk kotak teks.</summary>
      public string? Text { get; set; }

      /// <summary>Apakah kotaknya tercentang, untuk kotak centang.</summary>
      public bool Checked { get; set; }
   }

   /// <summary>Satu kotak beserta namanya, dipakai saat menggambar kotak untuk pengukuran.</summary>
   /// <param name="Label">Nama kotaknya, dicetak di dekat kotak itu.</param>
   /// <param name="Slot">Posisi kotaknya.</param>
   /// <param name="Kind">
   /// Jenis kotaknya: kotak tanda tangan sebuah langkah, atau kotak isian. Keduanya digambar berbeda
   /// supaya mudah dibedakan saat diukur.
   /// </param>
   public record ApprovalSlotLabel(string Label, ApprovalSlot Slot, ApprovalSlotKind Kind = ApprovalSlotKind.Signature);

   /// <summary>Jenis kotak pada PDF dokumen.</summary>
   public enum ApprovalSlotKind
   {
      /// <summary>Kotak tanda tangan sebuah langkah.</summary>
      Signature = 0,

      /// <summary>Kotak isian yang diberikan penanda tangan.</summary>
      Input = 1
   }
}
