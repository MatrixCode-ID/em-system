using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Approval;

internal sealed partial class ApprovalEngine {
   private static string ValidateVoidReason(string? reason) {
      reason = reason?.Trim() ?? string.Empty;
      if (reason.Length < ApprovalRules.MinVoidReasonLength || reason.Length > 990)
         throw new ActionException($"Void reason must contain {ApprovalRules.MinVoidReasonLength} to 990 characters.", 400);
      return reason;
   }
   private static string[] BatchIds(string[]? ids) {
      if (ids is null || ids.Length > 200 || ids.Any(string.IsNullOrWhiteSpace))
         throw new ActionException("Provide at most 200 nonempty request IDs.", 400);
      return ids.Distinct(StringComparer.Ordinal).ToArray();
   }
   internal async Task<ApprovalBatchResult> SignAllAsync(ApprovalSignAllRequest input, CancellationToken ct) {
      var user = await RealUserAsync(ct);
      OptionalText(input.Note, "Decision note", 1000);
      if (!Enum.IsDefined(input.Scope)) throw new ActionException("Unknown sign scope.", 400);
      if (input.Scope == ApprovalSignAllScope.AllActiveSteps && !caller.IsAdmin)
         throw new ActionException("Only administrators may sign all active steps.", 403);
      if (input.Scope == ApprovalSignAllScope.AllActiveSteps && input.RequestIds is null)
         throw new ActionException("Select request IDs for administrative signing.", 400);
      string[] ids;
      if (input.RequestIds is null) {
         // Mine remains candidate-only even for administrators. Discovery ignores list dates and paging.
         var discovery = from request in VisibleQuery()
                         join step in db.ta_ApprovalRequestSteps on request.cApprovalRequestId equals step.cApprovalRequestId
                         join candidate in db.ta_ApprovalRequestStepCandidates on step.cApprovalRequestStepId equals candidate.cApprovalRequestStepId
                         where request.cApprovalRequestKind == ApprovalKind.System && request.cApprovalRequestStage == ApprovalStage.Pending &&
                               (input.Name == null || request.cApprovalName == input.Name) && candidate.cUserId == user &&
                               step.cApprovalRequestStepLevel == request.cApprovalRequestLevel && step.cApprovalRequestStepStage == ApprovalStepStatus.Waiting
                         select request.cApprovalRequestId;
         ids = BatchIds(await discovery.Distinct().Take(201).ToArrayAsync(ct));
      } else ids = BatchIds(input.RequestIds);
      // Capture every target before the first signature; newly unlocked levels never enter this invocation.
      var snapshots = await db.ta_ApprovalRequests.AsNoTracking().Where(x => ids.Contains(x.cApprovalRequestId)).ToArrayAsync(ct);
      var targets = await (from step in db.ta_ApprovalRequestSteps.AsNoTracking()
                           join request in db.ta_ApprovalRequests on step.cApprovalRequestId equals request.cApprovalRequestId
                           where ids.Contains(request.cApprovalRequestId) && request.cApprovalRequestKind == ApprovalKind.System &&
                                 request.cApprovalRequestStage == ApprovalStage.Pending && step.cApprovalRequestStepLevel == request.cApprovalRequestLevel &&
                                 step.cApprovalRequestStepStage == ApprovalStepStatus.Waiting &&
                                 (input.Scope == ApprovalSignAllScope.AllActiveSteps || db.ta_ApprovalRequestStepCandidates.Any(c => c.cApprovalRequestStepId == step.cApprovalRequestStepId && c.cUserId == user))
                           select step).ToArrayAsync(ct);
      var result = new List<ApprovalBatchItem>();
      foreach (var id in ids) {
         ct.ThrowIfCancellationRequested();
         var request = snapshots.FirstOrDefault(x => x.cApprovalRequestId == id);
         var steps = targets.Where(x => x.cApprovalRequestId == id).ToArray();
         if (request is null || request.cApprovalRequestKind != ApprovalKind.System ||
             (input.Name is not null && !string.Equals(input.Name, request.cApprovalName, StringComparison.OrdinalIgnoreCase)) || steps.Length == 0) {
            result.Add(new(id, null, false, "No eligible active System step.")); continue;
         }
         if (!registry.System(request.cApprovalName).Definition.AllowBatchSign) {
            result.Add(new(id, null, false, "This approval requires individual module validation.")); continue;
         }
         try {
            await DecideSystemBatchAsync(id, steps.Select(x => x.cApprovalRequestStepId).ToArray(), user, input.Note, ct);
            result.AddRange(steps.Select(x => new ApprovalBatchItem(id, x.cApprovalRequestStepId, true, null)));
         }
         catch (Exception e) when (e is not OperationCanceledException) {
            db.ChangeTracker.Clear(); result.Add(new(id, null, false, e.Message));
         }
      }
      return new(result.ToArray());
   }
   internal async Task<ApprovalBatchResult> VoidAllAsync(ApprovalVoidAllRequest input, CancellationToken ct) {
      await RealUserAsync(ct);
      var reason = ValidateVoidReason(input.Reason); var ids = BatchIds(input.RequestIds);
      var result = new List<ApprovalBatchItem>();
      foreach (var id in ids) {
         ct.ThrowIfCancellationRequested();
         try { await VoidAsync(id, reason, ct); result.Add(new(id, null, true, null)); }
         catch (Exception e) when (e is not OperationCanceledException) {
            db.ChangeTracker.Clear(); result.Add(new(id, null, false, e.Message));
         }
      }
      return new(result.ToArray());
   }
}
