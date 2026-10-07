using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// A free comment on a request, from anyone allowed to open it - including people who are not
   /// signers.
   /// </summary>
   /// <remarks>
   /// Unlike a decision note, which belongs to its step and can only be written by its signer. Comments
   /// here cannot be changed or deleted: corrections are written as new comments, so the conversation
   /// stays intact. They may also be written after the request has finished or been rejected.
   /// </remarks>
   [Table("ta_ApprovalRequestComment")]
   public class ta_ApprovalRequestComment
   {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cApprovalRequestCommentId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_ApprovalRequest</c> row.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Comment author. Always a real user.</summary>
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Comment text, plain text.</summary>
      public string cApprovalRequestCommentNote { get; set; } = string.Empty;

      /// <summary>When this comment was written.</summary>
      public DateTime cApprovalRequestCommentDate { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }
   }
}
