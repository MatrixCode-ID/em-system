using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu langkah di dalam sebuah request: siapa yang harus memutuskan, apa keputusannya, dan di mana
   /// tanda tangannya digambar pada PDF.
   /// </summary>
   /// <remarks>
   /// Seluruh langkah dibuat sekaligus saat request diajukan, termasuk yang belum tiba gilirannya, supaya
   /// alurnya terlihat utuh sejak awal dan tidak berubah di tengah jalan.
   /// </remarks>
   [Table("ta_ApprovalRequestStep")]
   public class ta_ApprovalRequestStep
   {
      [Key]
      public string cApprovalRequestStepId { get; set; } = string.Empty;

      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>
      /// Nama langkah, seperti yang dideklarasikan modul. Nama ini juga menjadi nama claim yang
      /// dipersyaratkan untuk memutuskannya.
      /// </summary>
      public string cApprovalRequestStepName { get; set; } = string.Empty;

      /// <summary>Claim yang harus dipegang untuk memutuskan langkah ini.</summary>
      public string cApprovalRequestStepClaim { get; set; } = string.Empty;

      /// <summary>
      /// Level tempat langkah ini berada. Beberapa langkah bisa berada di level yang sama, dan semuanya
      /// harus disetujui sebelum level berikutnya dimulai.
      /// </summary>
      public int cApprovalRequestStepLevel { get; set; }

      /// <summary>Urutan langkah ini di dalam levelnya, untuk tampilan yang stabil.</summary>
      public int cApprovalRequestStepOrder { get; set; }

      /// <summary>Keadaan langkah ini.</summary>
      public ApprovalStepStatus cApprovalRequestStepStage { get; set; }

      /// <summary>Siapa yang membubuhkan tanda tangan, atau kosong kalau belum diputuskan.</summary>
      public string? cApprovalRequestStepSignerId { get; set; }

      /// <summary>Kapan tanda tangannya dibubuhkan.</summary>
      public DateTime? cApprovalRequestStepSignedDate { get; set; }

      /// <summary>Atas dasar apa tanda tangannya sah: penanda tangan sendiri, pengganti, atau penembus.</summary>
      public ApprovalSignerRole cApprovalRequestStepSignerRole { get; set; }

      /// <summary>
      /// Penanda tangan utama yang diwakili, kalau langkah ini ditandatangani pengganti atau penembus.
      /// Kosong untuk langkah yang penanda tangannya tidak ditetapkan per orang - dalam hal itu yang
      /// dicetak pada tanda tangan adalah nama langkahnya.
      /// </summary>
      public string? cApprovalRequestStepOnBehalfId { get; set; }

      /// <summary>
      /// Alasan keputusan. Boleh kosong saat menyetujui, tapi wajib saat menolak, saat menandatangani
      /// sebagai pengganti, dan saat menembus blokir.
      /// </summary>
      public string? cApprovalRequestStepNote { get; set; }

      /// <summary>
      /// Kode verifikasi tanda tangan ini, dibuat engine saat langkah diputuskan. Tercetak pada tanda
      /// tangan dan di tepi setiap halaman PDF.
      /// </summary>
      public string? cApprovalRequestStepVerificationCode { get; set; }

      /// <summary>
      /// Alasan blokir yang berlaku saat keputusan diambil, disimpan sebagai jejak audit. Terisi hanya
      /// kalau langkah ini memang sedang terblokir dan keputusannya menembusnya.
      /// </summary>
      public string? cApprovalRequestStepGuardReason { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      /// <summary>
      /// Salinan posisi tanda tangan dan isian langkah ini, dibekukan saat request diajukan, ditambah
      /// isian yang diberikan penanda tangan. Dibekukan supaya perubahan rancangan dokumen hanya berlaku
      /// untuk request yang diajukan sesudahnya.
      /// </summary>
      public string? json_object { get; set; }
   }
}
