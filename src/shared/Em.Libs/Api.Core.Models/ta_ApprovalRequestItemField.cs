using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// One column proposed to change, with the three values used to decide whether the proposal can still
   /// be applied: the value at submission, the proposed value, and the value found in the table at
   /// approval.
   /// </summary>
   /// <remarks>
   /// Those three values are what catches changes made outside the application: the comparison is
   /// against the table content when the decision is taken, not against other requests. Old value equal
   /// to current value means the proposal still applies; current value already equal to the proposal
   /// means the column can simply be skipped; anything else is a conflict.
   /// <para>
   /// The row is deliberately lean: no standard columns, keyed by entity and column name.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestItemField")]
   public class ta_ApprovalRequestItemField
   {
      /// <summary>Key of the related <c>ta_ApprovalRequestItem</c> row.</summary>
      public string cApprovalRequestItemId { get; set; } = string.Empty;

      /// <summary>Name of the column proposed to change, as used by the module handler.</summary>
      public string cApprovalRequestItemFieldName { get; set; } = string.Empty;

      /// <summary>Value when the request was submitted.</summary>
      public string? cApprovalRequestItemFieldOldValue { get; set; }

      /// <summary>Proposed value.</summary>
      public string? cApprovalRequestItemFieldNewValue { get; set; }

      /// <summary>
      /// Value found in the table when the decision was taken. Empty while the request is waiting.
      /// </summary>
      public string? cApprovalRequestItemFieldCurrentValue { get; set; }

      /// <summary>Display order of this column, so the change list reads like its screen.</summary>
      public int cApprovalRequestItemFieldOrder { get; set; }
   }
}
