using Em.Api.Core.Approval;

namespace Em.Test.Api
{
   /// <summary>The key of a test item in data approval.</summary>
   public record TestItemKey([property: KeyPart(1)] string ItemId);

   /// <summary>The key of a test document in document approval.</summary>
   public record TestDocKey([property: KeyPart(1)] string DocId);

   /// <summary>
   /// The place of the signature boxes and input on the test document PDF, in millimeters from the top-left
   /// of the page. Used twice: by the approval flow declaration, and by the PDF builder that draws the
   /// boxes.
   /// </summary>
   internal static class TestSlots
   {
      public static readonly ApprovalSlot PreparedBy = ApprovalSlot.At(15, 235, 55, 20);

      public static readonly ApprovalSlot QaCheck = ApprovalSlot.At(75, 235, 55, 20);

      public static readonly ApprovalSlot ApprovedByA = ApprovalSlot.At(135, 235, 55, 20);

      public static readonly ApprovalSlot ApprovedByB = ApprovalSlot.At(135, 262, 55, 20);

      public static readonly ApprovalSlot QaPassedBox = ApprovalSlot.At(15, 205, 6, 6);

      public static readonly ApprovalSlot QaRemarks = ApprovalSlot.At(25, 204, 165, 8);
   }
}
