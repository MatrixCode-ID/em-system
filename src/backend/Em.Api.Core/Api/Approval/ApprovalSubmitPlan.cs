using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// One step in the plan of a submission: what the module declared, whether it applies to this
   /// document, and the signers determined for it.
   /// </summary>
   /// <remarks>
   /// The plan is composed in full before anything is written, so every error the submitter can fix
   /// surfaces before the transaction is opened and before the PDF is created.
   /// </remarks>
   internal sealed class ApprovalPlannedStep
   {
      public required string Name { get; init; }

      public required ClaimAction Claim { get; init; }

      public required int Level { get; init; }

      public required int Order { get; init; }

      // False when the condition of the step did not hold: it is still written, as skipped.
      public required bool Applies { get; init; }

      // Whether the module declared how to find the signers of this step.
      public required bool DeclaresSigners { get; init; }

      public string? DistinctFrom { get; init; }

      public required bool RequiresInput { get; init; }

      // Slot and input boxes as they are at submission; frozen into the step row.
      public required ApprovalStepSnapshot Snapshot { get; init; }

      // The signers the module named, once resolved. Null when none were declared, in which case the
      // step is open to every holder of its claim.
      public IReadOnlyList<string>? Signers { get; set; }
   }
}
