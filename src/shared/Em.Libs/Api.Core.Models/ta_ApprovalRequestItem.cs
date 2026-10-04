using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu entitas yang disentuh request data approval: entitas apa, kuncinya yang mana, apa yang
   /// diusulkan atasnya, dan apa yang akhirnya terjadi padanya.
   /// </summary>
   /// <remarks>
   /// Hanya dipakai jenis approval yang data usulannya menumpang di request. Request gerbang status tidak
   /// punya baris di sini.
   /// </remarks>
   [Table("ta_ApprovalRequestItem")]
   public class ta_ApprovalRequestItem
   {
      [Key]
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>
      /// Entitas yang disentuh, ditulis dengan awalan nama modulnya supaya tidak bertabrakan antar modul.
      /// </summary>
      public string cApprovalRequestItemEntity { get; set; } = string.Empty;

      /// <summary>
      /// Kunci entitasnya dalam bentuk kanonik, sama bentuknya dengan kunci dokumen di header. Untuk
      /// entitas yang baru dibuat, isinya kunci sementara sampai bagian kunci yang sebenarnya terbentuk
      /// saat usulan diterapkan.
      /// </summary>
      public string cApprovalRequestItemKey { get; set; } = string.Empty;

      /// <summary>Apa yang diusulkan atas entitas ini.</summary>
      public ApprovalItemOperation cApprovalRequestItemOperation { get; set; }

      /// <summary>Urutan penerapan, untuk entitas yang harus diterapkan setelah entitas lain.</summary>
      public int cApprovalRequestItemOrder { get; set; }

      /// <summary>Apa yang benar-benar terjadi pada entitas ini saat usulan diterapkan.</summary>
      public ApprovalItemStage cApprovalRequestItemStage { get; set; }

      /// <summary>Keterangan tambahan, misalnya sebab sebuah entitas berakhir sebagai konflik.</summary>
      public string? cApprovalRequestItemNote { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
