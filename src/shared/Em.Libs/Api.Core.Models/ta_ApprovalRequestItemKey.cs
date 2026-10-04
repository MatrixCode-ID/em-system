using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu bagian kunci dari sebuah entitas usulan, disimpan terpisah supaya handler modul bisa menerima
   /// kuncinya sebagai nilai bertipe, bukan string yang harus dipecah sendiri.
   /// </summary>
   /// <remarks>
   /// Kunci entitas juga disimpan dalam bentuk kanonik di entitasnya; yang di sini adalah bentuk per
   /// bagiannya. Keduanya sengaja ada: bentuk kanonik untuk pencarian dan pencocokan, bentuk per bagian
   /// untuk diserahkan ke modul.
   /// <para>
   /// Barisnya ramping dengan sengaja: tanpa kolom standar, kuncinya gabungan entitas dan nama bagian.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestItemKey")]
   public class ta_ApprovalRequestItemKey
   {
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      /// <summary>Nama bagian kunci, seperti yang dideklarasikan modul.</summary>
      public string cApprovalRequestItemKeyName { get; set; } = string.Empty;

      /// <summary>
      /// Nilai bagian kunci itu sebagai teks. Formatnya dibakukan per tipe supaya nilai yang sama selalu
      /// menghasilkan teks yang sama.
      /// </summary>
      public string? cApprovalRequestItemKeyValue { get; set; }

      /// <summary>Urutan bagian ini di dalam kuncinya.</summary>
      public int cApprovalRequestItemKeyOrder { get; set; }
   }
}
