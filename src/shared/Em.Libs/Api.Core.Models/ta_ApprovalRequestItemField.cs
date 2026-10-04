using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu kolom yang diusulkan berubah, beserta ketiga nilai yang dipakai memutuskan apakah usulan itu
   /// masih bisa diterapkan: nilai saat diajukan, nilai yang diusulkan, dan nilai yang ditemukan di
   /// tabelnya saat disetujui.
   /// </summary>
   /// <remarks>
   /// Ketiga nilai itu yang membuat perubahan dari luar aplikasi ikut tertangkap: pembandingnya isi tabel
   /// saat keputusan diambil, bukan request lain. Nilai lama sama dengan nilai sekarang berarti usulan
   /// masih berlaku; nilai sekarang sudah sama dengan usulan berarti kolom itu cukup dilewati; selain itu
   /// berarti konflik.
   /// <para>
   /// Barisnya ramping dengan sengaja: tanpa kolom standar, kuncinya gabungan entitas dan nama kolom.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestItemField")]
   public class ta_ApprovalRequestItemField
   {
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      /// <summary>Nama kolom yang diusulkan berubah, seperti yang dipakai handler modul.</summary>
      public string cApprovalRequestItemFieldName { get; set; } = string.Empty;

      /// <summary>Nilainya saat request diajukan.</summary>
      public string? cApprovalRequestItemFieldOldValue { get; set; }

      /// <summary>Nilai yang diusulkan.</summary>
      public string? cApprovalRequestItemFieldNewValue { get; set; }

      /// <summary>
      /// Nilai yang ditemukan di tabelnya saat keputusan diambil. Kosong selama request masih menunggu.
      /// </summary>
      public string? cApprovalRequestItemFieldCurrentValue { get; set; }

      /// <summary>Urutan tampilan kolom ini, supaya daftar perubahan terbaca seperti di layarnya.</summary>
      public int cApprovalRequestItemFieldOrder { get; set; }
   }
}
