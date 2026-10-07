using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// One key part of a proposed entity, stored separately so module handlers can receive the key as
   /// typed values instead of a string they must split themselves.
   /// </summary>
   /// <remarks>
   /// The entity key is also stored in canonical form on the entity; this is the per-part form. Both
   /// exist on purpose: the canonical form for searching and matching, the per-part form for handing to
   /// the module.
   /// <para>
   /// The row is deliberately lean: no standard columns, keyed by entity and part name.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestItemKey")]
   public class ta_ApprovalRequestItemKey
   {
      /// <summary>Key of the related <c>ta_ApprovalRequestItem</c> row.</summary>
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      /// <summary>Key part name, as declared by the module.</summary>
      public string cApprovalRequestItemKeyName { get; set; } = string.Empty;

      /// <summary>
      /// Value of that key part as text. The format is fixed per type so the same value always produces
      /// the same text.
      /// </summary>
      public string? cApprovalRequestItemKeyValue { get; set; }

      /// <summary>Order of this part within its key.</summary>
      public int cApprovalRequestItemKeyOrder { get; set; }
   }
}
