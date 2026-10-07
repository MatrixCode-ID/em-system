using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// What the engine needs from a data change proposal flow, seen without its module service type.
   /// </summary>
   internal interface IApprovalDataFlow
   {
      /// <summary>Claim that grants the right to approve proposals of this type.</summary>
      ClaimAction ApproveClaim { get; }

      /// <summary>The entity declared with that name, or <c>null</c> when there is none.</summary>
      ApprovalEntityDeclaration? FindEntity(string name);
   }

   // The half of a data flow that only the engine uses: the typed end of the bridge. The engine hands in
   // a scope, this builds the typed context for the delegates of the module and gives back plain values.
   //
   // A data request has exactly one step, named after the approval claim. It is what lets the request
   // list, the hub, the timeline and the decision itself work for data requests with the same code that
   // serves document requests, instead of a parallel set of queries.
   public partial class ApprovalDataFlowDeclaration<TServices>
   {
      /// <inheritdoc />
      ApprovalEntityDeclaration? IApprovalDataFlow.FindEntity(string name) =>
         Entities.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

      internal override IReadOnlyList<ApprovalStepRule> StepRules =>
         [new ApprovalStepRule(ApproveClaim.Name, ApproveClaim, false, null)];

      internal override async Task<IReadOnlyDictionary<string, string?>?> SummaryAsync(ApprovalRunScope scope) =>
         Summary is null ? null : await Summary(ApprovalDataContext<TServices>.From(scope));

      internal override Task RunSubmittedAsync(ApprovalRunScope scope) =>
         OnSubmitted is null ? Task.CompletedTask : OnSubmitted(ApprovalDataContext<TServices>.From(scope));

      internal override Task RunLevelCompletedAsync(ApprovalRunScope scope, int level) => Task.CompletedTask;

      internal override Task RunFinishingAsync(ApprovalRunScope scope) =>
         OnFinishing is null ? Task.CompletedTask : OnFinishing(ApprovalDataContext<TServices>.From(scope));

      internal override Task RunFinishedAsync(ApprovalRunScope scope) =>
         OnFinished is null ? Task.CompletedTask : OnFinished(ApprovalDataContext<TServices>.From(scope));

      internal override Task RunRejectingAsync(ApprovalRunScope scope) =>
         OnRejecting is null ? Task.CompletedTask : OnRejecting(ApprovalDataContext<TServices>.From(scope));

      internal override Task RunRejectedAsync(ApprovalRunScope scope) =>
         OnRejected is null ? Task.CompletedTask : OnRejected(ApprovalDataContext<TServices>.From(scope));

      // A data request asks nothing of the signer but the decision, and a pending one that is pulled back
      // has written nothing to the tables of the module, so there is nothing to take back.
      internal override Task RunInputSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         string? payloadJson) => Task.CompletedTask;

      internal override Task RunSigningAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => Task.CompletedTask;

      internal override Task RunSignedAsync(ApprovalRunScope scope, string stepName, string? signerId,
         ApprovalDecision decision) => Task.CompletedTask;

      internal override Task RunReinstatingAsync(ApprovalRunScope scope, ApprovalStage stage) => Task.CompletedTask;
   }
}
