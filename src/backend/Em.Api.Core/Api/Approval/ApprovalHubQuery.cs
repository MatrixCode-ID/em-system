using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Answer of <see cref="IApprovalHubQuery"/>: the rule "waiting for the caller" is shared with the
   /// request list, so the approval screen and the task list cannot disagree about what is waiting.
   /// </summary>
   internal sealed class ApprovalHubQuery(ApiCoreContext ctx, ApprovalRegistry registry, ActionRequest caller)
      : IApprovalHubQuery
   {
      /// <inheritdoc />
      public Task<IReadOnlyList<ApprovalHubGroup>> GetWaitingForCallerAsync(
         CancellationToken cancellationToken = default) =>
         new ApprovalRequestReader(ctx, registry, caller, cancellationToken).HubGroupsAsync();
   }
}
