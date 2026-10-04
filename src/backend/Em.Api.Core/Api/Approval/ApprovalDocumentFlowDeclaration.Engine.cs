using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   // The half of a document flow that only the engine uses. It is the typed end of the bridge: the
   // engine hands in a scope, this builds the typed context for the delegates of the module and gives
   // back plain values, so the engine never needs to know the service type or the key type of the module.
   public partial class ApprovalDocumentFlowDeclaration<TServices, TKey>
   {
      internal override bool HasPdf => Pdf is not null;

      internal override async Task<IReadOnlyList<ApprovalPlannedStep>> PlanStepsAsync(ApprovalRunScope scope) {
         var context = NewContext(scope);
         var planned = new List<ApprovalPlannedStep>();

         foreach (var step in Steps) {
            var snapshot = new ApprovalStepSnapshot {
               Slot = step.Slot is null ? null : ApprovalSlotSnapshot.From(step.Slot)
            };

            if (step.Input is { } input) {
               for (var i = 0; i < input.FieldSlots.Count; i++) {
                  snapshot.Fields.Add(new ApprovalFieldSnapshot {
                     Kind = input.FieldKinds[i],
                     Slot = ApprovalSlotSnapshot.From(input.FieldSlots[i])
                  });
               }
            }

            planned.Add(new ApprovalPlannedStep {
               Name = step.Name,
               Claim = step.Claim,
               Level = step.Level,
               Order = step.Order,
               Applies = step.When is null || await step.When(context),
               DeclaresSigners = step.Signers is not null,
               DistinctFrom = step.DistinctFrom,
               RequiresInput = step.Input is not null,
               Snapshot = snapshot
            });
         }

         return planned;
      }

      internal override async Task<IReadOnlyList<string>?> ResolveSignersAsync(ApprovalRunScope scope,
         string stepName) {
         var step = Steps.FirstOrDefault(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase));
         if (step?.Signers is null) return null;

         return await step.Signers(NewContext(scope));
      }

      internal override async Task<IReadOnlyDictionary<string, string?>?> SummaryAsync(ApprovalRunScope scope) =>
         Summary is null ? null : await Summary(NewContext(scope));

      internal override async Task<Stream?> OpenPdfAsync(ApprovalRunScope scope) =>
         Pdf is null ? null : await Pdf(NewContext(scope));

      internal override async Task LocateSlotsAsync(ApprovalRunScope scope, IReadOnlyList<ApprovalPlannedStep> plan, Stream pdf) {
         if (PdfLayout is null) return;

         var map = await PdfLayout(NewContext(scope), pdf);

         // A box the module says is not on this document keeps its place in the list, so the answers
         // that are written into it later still line up, but it points at a page that does not exist and
         // is never drawn.
         ApprovalSlotSnapshot Move(ApprovalSlotSnapshot slot) {
            var moved = map(slot.ToSlot());
            return moved is null ? slot with { Page = 0 } : ApprovalSlotSnapshot.From(moved);
         }

         foreach (var step in plan) {
            if (step.Snapshot.Slot is { } slot) step.Snapshot.Slot = Move(slot);
            foreach (var field in step.Snapshot.Fields) field.Slot = Move(field.Slot);
         }
      }

      internal override Task RunSubmittedAsync(ApprovalRunScope scope) =>
         OnSubmitted is null ? Task.CompletedTask : OnSubmitted(NewContext(scope));

      internal override Task RunLevelCompletedAsync(ApprovalRunScope scope, int level) =>
         Levels.FirstOrDefault(r => r.Level == level)?.OnCompleted is { } hook
            ? hook(NewContext(scope))
            : Task.CompletedTask;

      internal override Task RunFinishingAsync(ApprovalRunScope scope) =>
         OnFinishing is null ? Task.CompletedTask : OnFinishing(NewContext(scope));

      internal override Task RunFinishedAsync(ApprovalRunScope scope) =>
         OnFinished is null ? Task.CompletedTask : OnFinished(NewContext(scope));

      internal override IReadOnlyList<ApprovalStepRule> StepRules =>
         [.. Steps.Select(r => new ApprovalStepRule(r.Name, r.Claim, r.Strict, r.DistinctFrom))];

      internal override async Task<IReadOnlyList<ApprovalInputValue>?> ValidateInputAsync(ApprovalRunScope scope,
         string stepName, string? signerId, string? payloadJson) {
         var input = StepOf(stepName)?.Input;
         if (input is null) return null;

         return await input.ValidateAsync(NewStepContext(scope, stepName, signerId), payloadJson);
      }

      internal override Task RunInputSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         string? payloadJson) =>
         StepOf(stepName)?.Input is { } input
            ? input.SignedAsync(NewStepContext(scope, stepName, signerId), payloadJson)
            : Task.CompletedTask;

      internal override Task RunSigningAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) =>
         OnSigning is null ? Task.CompletedTask : OnSigning(NewStepContext(scope, stepName, signerId), decision);

      internal override Task RunSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) =>
         OnSigned is null ? Task.CompletedTask : OnSigned(NewStepContext(scope, stepName, signerId), decision);

      internal override Task RunRejectingAsync(ApprovalRunScope scope) =>
         OnRejecting is null ? Task.CompletedTask : OnRejecting(NewContext(scope));

      internal override Task RunRejectedAsync(ApprovalRunScope scope) =>
         OnRejected is null ? Task.CompletedTask : OnRejected(NewContext(scope));

      internal override Task RunReinstatingAsync(ApprovalRunScope scope, ApprovalStage stage) =>
         OnReinstating is null ? Task.CompletedTask : OnReinstating(NewContext(scope), stage);

      private ApprovalStepDeclaration<TServices, TKey>? StepOf(string stepName) =>
         Steps.FirstOrDefault(r => string.Equals(r.Name, stepName, StringComparison.OrdinalIgnoreCase));

      private ApprovalStepContext<TServices, TKey> NewStepContext(ApprovalRunScope scope, string stepName,
         string? signerId) => new() {
         Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
         DocKey = ApprovalKey.FromCanonical<TKey>(scope.Request.cApprovalRequestDocKey),
         DocVersion = scope.Request.cApprovalRequestDocVersion,
         ApprovalRequestId = scope.Request.cApprovalRequestId,
         RequesterId = scope.Request.cApprovalRequestRequesterId,
         StepName = StepOf(stepName)?.Name ?? stepName,
         SignerId = signerId
      };

      private static ApprovalContext<TServices, TKey> NewContext(ApprovalRunScope scope) => new() {
         Services = ApprovalModuleService.Resolve<TServices>(scope.Engine, scope.Provider),
         DocKey = ApprovalKey.FromCanonical<TKey>(scope.Request.cApprovalRequestDocKey),
         DocVersion = scope.Request.cApprovalRequestDocVersion,
         ApprovalRequestId = scope.Request.cApprovalRequestId,
         RequesterId = scope.Request.cApprovalRequestRequesterId
      };
   }
}
