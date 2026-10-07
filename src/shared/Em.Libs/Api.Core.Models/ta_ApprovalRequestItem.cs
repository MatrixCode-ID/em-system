using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// One entity touched by a data approval request: which entity, which key, what is proposed for it,
   /// and what finally happened to it.
   /// </summary>
   /// <remarks>
   /// Only used by approval kinds whose proposed data is carried by the request. Status gate requests have
   /// no rows here.
   /// </remarks>
   [Table("ta_ApprovalRequestItem")]
   public class ta_ApprovalRequestItem
   {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_ApprovalRequest</c> row.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>
      /// Entity touched, written with its module name prefix so it does not clash across modules.
      /// </summary>
      public string cApprovalRequestItemEntity { get; set; } = string.Empty;

      /// <summary>
      /// Entity key in canonical form, the same shape as the document key in the header. For newly created
      /// entities it holds a temporary key until the real key parts are formed when the proposal is
      /// applied.
      /// </summary>
      public string cApprovalRequestItemKey { get; set; } = string.Empty;

      /// <summary>What is proposed for this entity.</summary>
      public ApprovalItemOperation cApprovalRequestItemOperation { get; set; }

      /// <summary>Apply order, for entities that must be applied after other entities.</summary>
      public int cApprovalRequestItemOrder { get; set; }

      /// <summary>What actually happened to this entity when the proposal was applied.</summary>
      public ApprovalItemStage cApprovalRequestItemStage { get; set; }

      /// <summary>Additional information, for example why an entity ended up as a conflict.</summary>
      public string? cApprovalRequestItemNote { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }
   }
}
