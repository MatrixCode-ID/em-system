using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Satu langkah dalam rencana sebuah pengajuan: apa yang dideklarasikan modul, apakah ia berlaku untuk
   /// dokumen ini, dan penanda tangan yang ditentukan untuknya.
   /// </summary>
   /// <remarks>
   /// Rencana disusun penuh sebelum apa pun ditulis, supaya setiap kesalahan yang bisa diperbaiki
   /// pengaju muncul sebelum transaksi dibuka dan sebelum PDF-nya dibuat.
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
