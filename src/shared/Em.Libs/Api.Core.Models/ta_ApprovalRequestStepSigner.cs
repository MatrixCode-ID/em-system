using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Daftar orang yang ditetapkan sebagai penanda tangan satu langkah, ditentukan saat request diajukan.
   /// </summary>
   /// <remarks>
   /// Hanya ada untuk langkah yang penanda tangannya ditentukan dari isi dokumen; langkah yang terbuka
   /// bagi semua pemegang claim-nya tidak punya baris di sini. Daftarnya dibuat sekaligus saat pengajuan,
   /// bukan dicari ulang saat menandatangani, dengan dua alasan: kesalahan muncul di depan kepada
   /// pengaju alih-alih menggagalkan keputusan orang lain di tengah alur, dan daftar pekerjaan tiap user
   /// bisa dihitung tanpa memuat dokumennya.
   /// <para>
   /// Barisnya ramping dengan sengaja: tanpa kolom standar, dan kuncinya gabungan ketiga kolom di bawah.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestStepSigner")]
   public class ta_ApprovalRequestStepSigner
   {
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Nama langkah yang penanda tangannya didaftar di sini.</summary>
      public string cApprovalRequestStepName { get; set; } = string.Empty;

      /// <summary>Orang yang ditetapkan sebagai penanda tangan langkah itu.</summary>
      public string cUserId { get; set; } = string.Empty;
   }
}
