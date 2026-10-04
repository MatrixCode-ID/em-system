using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Komentar bebas pada sebuah request, dari siapa pun yang boleh membukanya - termasuk yang bukan
   /// penanda tangan.
   /// </summary>
   /// <remarks>
   /// Berbeda dari catatan keputusan, yang menempel pada langkahnya dan hanya bisa ditulis penanda
   /// tangannya. Komentar di sini tidak bisa diubah atau dihapus: koreksi ditulis sebagai komentar baru,
   /// sehingga percakapannya tetap utuh. Boleh ditulis juga setelah request selesai atau ditolak.
   /// </remarks>
   [Table("ta_ApprovalRequestComment")]
   public class ta_ApprovalRequestComment
   {
      [Key]
      public string cApprovalRequestCommentId { get; set; } = string.Empty;

      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Penulis komentar. Selalu user nyata.</summary>
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Isi komentarnya, teks biasa.</summary>
      public string cApprovalRequestCommentNote { get; set; } = string.Empty;

      /// <summary>Kapan komentar ini ditulis.</summary>
      public DateTime cApprovalRequestCommentDate { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
