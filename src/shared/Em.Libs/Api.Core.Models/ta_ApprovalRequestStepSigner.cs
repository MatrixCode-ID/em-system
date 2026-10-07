using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// People assigned as signers of one step, determined when the request is submitted.
   /// </summary>
   /// <remarks>
   /// Only exists for steps whose signers are determined from the document content; steps open to every
   /// holder of their claim have no rows here. The list is built at submission, not looked up again when
   /// signing, for two reasons: errors surface up front to the requester instead of failing someone
   /// else's decision midway, and each user's work list can be computed without loading the documents.
   /// <para>
   /// The row is deliberately lean: no standard columns, keyed by the three columns below.
   /// </para>
   /// </remarks>
   [Table("ta_ApprovalRequestStepSigner")]
   public class ta_ApprovalRequestStepSigner
   {
      /// <summary>Key of the related <c>ta_ApprovalRequest</c> row.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Name of the step whose signers are listed here.</summary>
      public string cApprovalRequestStepName { get; set; } = string.Empty;

      /// <summary>Person assigned as a signer of that step.</summary>
      public string cUserId { get; set; } = string.Empty;
   }
}
