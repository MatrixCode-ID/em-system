using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Withdraws a document request: one still waiting for a decision is cancelled, one already completed in
   /// full has its approval revoked.
   /// </summary>
   /// <remarks>
   /// Used by two doors with the same rules: the withdrawal action on the approval screen, and
   /// <see cref="IApprovalEngine.ReinstateAsync"/> for module actions. One instance is used per call; it
   /// holds the running engine service, which is the source of the caller info for the module's handlers.
   /// <para>
   /// A withdrawn request is never discarded. It is marked, with a note of who, when, and why, and a
   /// resubmission for the same document and version links itself to this request.
   /// </para>
   /// </remarks>
   internal sealed class ApprovalCanceller(ApiCoreContext ctx, ApprovalRegistry registry, ServicesBase host)
   {
      private const int MaxReasonLength = 500;

      private CancellationToken Token => host.AbortToken;

      /// <summary>
      /// Withdraws one request.
      /// </summary>
      /// <param name="approvalRequestId">The request being withdrawn.</param>
      /// <param name="reason">Reason for the withdrawal. Required.</param>
      public async Task CancelAsync(string approvalRequestId, string? reason) {
         // 1. A real, active user: the withdrawal is written down under a name.
         var me = await new ApprovalAccess(ctx).RequireRealUserAsync(host.Request, Token);

         if (string.IsNullOrWhiteSpace(approvalRequestId)) {
            throw new ActionException("Say which approval request is to be pulled back.", 400);
         }

         var request = await ctx.ta_ApprovalRequests.AsNoTracking()
                          .FirstOrDefaultAsync(r => r.cApprovalRequestId == approvalRequestId, Token) ??
                       throw new ActionException($"Approval request '{approvalRequestId}' was not found.", 404);

         var flow = registry.Get(request.cApprovalRequestDocType);

         // 2. Who. A document request: whoever holds the claim of the first step that applied to it, and a
         //    user with the administrator switch on, which counts as holding every claim. A data request has
         //    no first step to speak of: whoever proposed it may withdraw it, and so may whoever could have
         //    decided it.
         if (flow is IApprovalDataFlow data) {
            if (!string.Equals(request.cApprovalRequestRequesterId, me, StringComparison.OrdinalIgnoreCase) &&
                !ApprovalAccess.HoldsClaim(host.Request, data.ApproveClaim)) {
               throw new ActionException(
                  "You are not allowed to withdraw this proposal: only the person who made it, or a holder of " +
                  $"the claim '{data.ApproveClaim.Name}', can.", 403);
            }
         }
         else {
            var steps = await ctx.ta_ApprovalRequestSteps.AsNoTracking()
               .Where(r => r.cApprovalRequestId == approvalRequestId)
               .OrderBy(r => r.cApprovalRequestStepLevel).ThenBy(r => r.cApprovalRequestStepOrder)
               .ToListAsync(Token);

            var first = steps.FirstOrDefault(r => r.cApprovalRequestStepStage != ApprovalStepStatus.Skipped) ?? steps.FirstOrDefault();
            var rule = first is null
               ? null
               : flow.StepRules.FirstOrDefault(r => string.Equals(r.Name, first.cApprovalRequestStepName, StringComparison.OrdinalIgnoreCase));
            if (rule is null || !ApprovalAccess.HoldsClaim(host.Request, rule.Claim)) {
               throw new ActionException(
                  $"You are not allowed to pull back this approval request: it needs the claim '{rule?.Claim.Name ?? flow.ViewClaim.Name}', " +
                  "which signs the first step.", 403);
            }
         }

         // 3. Only a request that is still alive can be pulled back.
         var previous = request.cApprovalRequestStage;

         // What an approved proposal wrote is on the tables of the module already, and withdrawing the
         // request would not take it back: reversing it takes a new proposal.
         if (flow.Kind == ApprovalKind.Data && previous == ApprovalStage.Approved) {
            throw new ActionException(
               "This proposal was approved and applied already, so there is nothing to withdraw. To reverse it, propose the opposite change.", 409);
         }

         var next = previous switch {
            ApprovalStage.Pending => ApprovalStage.Cancelled,
            ApprovalStage.Approved => ApprovalStage.ReinstatedAfterFinish,
            ApprovalStage.Rejected =>
               throw new ActionException("This approval request was rejected, so there is nothing to pull back; the document is free to edit.", 409),
            _ => throw new ActionException("This approval request has already been pulled back.", 409)
         };

         reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
         if (reason is null) {
            throw new ActionException("A reason is required for pulling back an approval request.", 400);
         }

         if (reason.Length > MaxReasonLength) {
            throw new ActionException($"The reason is {reason.Length} characters long; the limit is {MaxReasonLength}.", 400);
         }

         var scope = new ApprovalRunScope(host, host.App.ServiceProvider, request);
         var now = DateTime.Now;
         var stamp = DateTime.UtcNow;
         var json = ApprovalRequestJson.WithCancel(request.json_object, new ApprovalCancelRecord(me, now, reason));

         // 4. One transaction. The stage is written only while it is still what was read: a decision that
         //    finished the request in the meantime, or a second withdrawal, leaves no row to update and
         //    reads as a plain 409, before the module is asked anything.
         await using var transaction = await ApprovalTransaction.BeginAsync(ctx, flow, host.App.ServiceProvider, Token);
         try {
            var written = await ctx.ta_ApprovalRequests
               .Where(r => r.cApprovalRequestId == approvalRequestId && r.cApprovalRequestStage == previous)
               .ExecuteUpdateAsync(s => s
                  .SetProperty(r => r.cApprovalRequestStage, next)
                  .SetProperty(r => r.json_object, json)
                  .SetProperty(r => r.ustamp, stamp), Token);

            if (written == 0) {
               throw new ActionException("This approval request changed a moment ago, so it was not pulled back. Open it again and retry.", 409);
            }

            if (previous == ApprovalStage.Pending) {
               // What was still open is skipped, the same as when a step rejects the request.
               await ctx.ta_ApprovalRequestSteps
                  .Where(r => r.cApprovalRequestId == approvalRequestId && r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)
                  .ExecuteUpdateAsync(s => s
                     .SetProperty(r => r.cApprovalRequestStepStage, ApprovalStepStatus.Skipped)
                     .SetProperty(r => r.ustamp, stamp), Token);
            }

            // The module takes back whatever it wrote because of this request, and refuses by throwing
            // when the document has moved on too far for that.
            await flow.RunReinstatingAsync(scope, previous);

            await transaction.CommitAsync(Token);
         }
         catch {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
         }
      }
   }
}
