using Em.Api.Core.Approval;
using Em.Api.Core.Models;

namespace Em.Api.Core.Hub
{
   /// <summary>
   /// Task list source for approval: one row per document type that has requests waiting for the active
   /// user's decision, separately for document approval and data approval.
   /// </summary>
   /// <remarks>
   /// Each row offers one action, opening the approval screen already filtered to that document type. The
   /// decision itself is not taken here.
   /// </remarks>
   internal sealed class ApprovalHubTaskSource(IApprovalHubQuery query) : IHubTaskSource
   {
      /// <summary>Source for the document approval rows.</summary>
      public const string DocumentSource = "approval.document";

      /// <summary>Source for the data approval rows.</summary>
      public const string DataSource = "approval.data";

      /// <summary>Name of the action offered by each row.</summary>
      public const string OpenAction = "Open";

      // Has to equal ApprovalManagerNavigationPayload.NavigationName in Em.Ui.Core, which the server
      // cannot reference. The parameter that goes with it is the document type, as plain text.
      private const string ApprovalManagerNavigation = "em.approval.manager";

      /// <inheritdoc />
      public async Task<IReadOnlyList<HubTaskInfo>> GetTasksAsync(CancellationToken cancellationToken = default) {
         var groups = await query.GetWaitingForCallerAsync(cancellationToken);
         var now = DateTime.Now;

         // Request dates are written in the server's local time, so the age is measured against the same.
         return [.. groups.Select(group => new HubTaskInfo {
            Source = group.Kind == ApprovalKind.Data ? DataSource : DocumentSource,
            Id = group.DocType,
            Title = group.DocType,
            Count = group.Count,
            OldestAge = now > group.OldestRequestDate ? now - group.OldestRequestDate : TimeSpan.Zero,
            ActionName = OpenAction,
            NavigationTarget = ApprovalManagerNavigation,
            NavigationParameter = group.DocType
         })];
      }
   }
}
