using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// PDF drawer for approval: places signatures and input onto the document PDF, and draws its boxes for
   /// measurement purposes.
   /// </summary>
   /// <remarks>
   /// The bridge between the approval engine and the PDF library in use. The engine never touches that
   /// library itself; it only hands over the base PDF together with a snapshot of the request state, and
   /// receives the finished PDF. That is why a signed PDF is never stored: it is produced on request from
   /// the base PDF frozen at submission, so it always reflects the latest state.
   /// </remarks>
   public interface IApprovalPdfRenderer
   {
      /// <summary>
      /// Places signatures and input onto a PDF.
      /// </summary>
      /// <param name="basePdf">
      /// Content of the document's base PDF. Read from its current position; the caller closes it.
      /// </param>
      /// <param name="snapshot">The request state to draw.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <returns>Content of the finished PDF, ready to send to the caller.</returns>
      Task<Stream> RenderStampedAsync(Stream basePdf, ApprovalStampSnapshot snapshot,
         CancellationToken cancellationToken = default);

      /// <summary>
      /// Draws the signature boxes and input boxes together with their names over a PDF, without any
      /// content. Used by module developers to measure box positions against the real document design.
      /// </summary>
      /// <param name="basePdf">
      /// Content of the real document PDF used as the base of the drawing. Read from its current position;
      /// the caller closes it.
      /// </param>
      /// <param name="slots">The boxes to draw together with their names.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <returns>Content of the sample PDF.</returns>
      Task<Stream> RenderCalibrationAsync(Stream basePdf, IReadOnlyList<ApprovalSlotLabel> slots,
         CancellationToken cancellationToken = default);
   }

   /// <summary>
   /// Snapshot of a request's state when its PDF is drawn: which steps have been decided, what input was
   /// given, and what is printed at the page edge.
   /// </summary>
   /// <remarks>
   /// Everything is already print-ready values - names of people, not ids; input text, not raw payloads -
   /// so the drawer does not need to touch the database at all.
   /// </remarks>
   public class ApprovalStampSnapshot
   {
      /// <summary>Title of the document, printed on the approval sheet when that sheet is turned on.</summary>
      public string? DocumentTitle { get; set; }

      /// <summary>
      /// The steps to draw. Steps that are not decided yet are included with the waiting state, so the drawer
      /// can choose for itself whether to leave their boxes empty.
      /// </summary>
      public IReadOnlyList<ApprovalStampStep> Steps { get; set; } = [];

      /// <summary>The input to draw, one entry per input box that is filled in.</summary>
      public IReadOnlyList<ApprovalStampInput> Inputs { get; set; } = [];

      /// <summary>
      /// Code printed at the edge of every page so a printed document can be traced back to its request, or
      /// empty when there is no signature at all yet.
      /// </summary>
      public string? PageMarginCode { get; set; }

      /// <summary>
      /// <c>true</c> when this document type asks for an extra page containing a table of all steps.
      /// </summary>
      public bool IncludeApprovalSheet { get; set; }
   }

   /// <summary>One step as drawn on the PDF.</summary>
   public class ApprovalStampStep
   {
      /// <summary>Name of the step, printed when its signer is not fixed per person.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Where its signature is drawn.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>State of the step: waiting, approved, rejected, or skipped.</summary>
      public ApprovalStepStatus Status { get; set; }

      /// <summary>Name of the person who signed, or empty when nobody has yet.</summary>
      public string? SignerName { get; set; }

      /// <summary>
      /// Name of the primary signer being represented, when the one who signed is a substitute or an
      /// override. Empty for a step whose signer is not fixed per person - in that case what is printed as
      /// "on behalf of" is the step name.
      /// </summary>
      public string? OnBehalfName { get; set; }

      /// <summary>On what basis the signature is valid.</summary>
      public ApprovalSignerRole SignerRole { get; set; }

      /// <summary>When the signature was placed.</summary>
      public DateTime? SignedDate { get; set; }

      /// <summary>Verification code of this signature.</summary>
      public string? VerificationCode { get; set; }
   }

   /// <summary>One input box together with the value drawn in it.</summary>
   public class ApprovalStampInput
   {
      /// <summary>The step that owns this input.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Kind of the box: checkmark or text.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Where the box is drawn.</summary>
      public ApprovalSlot Slot { get; set; } = new(0, 0);

      /// <summary>The text drawn, for a text box.</summary>
      public string? Text { get; set; }

      /// <summary>Whether the box is checked, for a checkbox.</summary>
      public bool Checked { get; set; }
   }

   /// <summary>One box together with its name, used when drawing boxes for measurement.</summary>
   /// <param name="Label">Name of the box, printed near the box.</param>
   /// <param name="Slot">Position of the box.</param>
   /// <param name="Kind">
   /// Kind of the box: a step's signature box, or an input box. The two are drawn differently so they are
   /// easy to tell apart when measuring.
   /// </param>
   public record ApprovalSlotLabel(string Label, ApprovalSlot Slot, ApprovalSlotKind Kind = ApprovalSlotKind.Signature);

   /// <summary>Kind of box on the document PDF.</summary>
   public enum ApprovalSlotKind
   {
      /// <summary>A step's signature box.</summary>
      Signature = 0,

      /// <summary>An input box filled in by the signer.</summary>
      Input = 1
   }
}
