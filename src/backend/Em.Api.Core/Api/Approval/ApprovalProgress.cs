using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Moves a request forward after a step is approved: when the level is complete, that level is closed
   /// and the request moves to the next level, or finishes when there is none.
   /// </summary>
   /// <remarks>
   /// Used by submission (the submitter's automatic signature can complete its level right away) and by
   /// decisions, so the rule "when is a level complete" is written once. All its hooks run inside the
   /// caller's transaction; running hooks after the commit is also the caller's job.
   /// </remarks>
   internal static class ApprovalProgress
   {
      /// <summary>
      /// Advances the request as long as its current level is complete, then saves its state.
      /// </summary>
      /// <param name="ctx">The core database context, currently inside the transaction.</param>
      /// <param name="flow">The flow of the document type.</param>
      /// <param name="scope">Material for handing over to the module's handler.</param>
      /// <param name="request">The request being advanced. Must be tracked by <paramref name="ctx"/>.</param>
      /// <param name="steps">All steps of that request. Must be tracked by <paramref name="ctx"/>.</param>
      /// <returns><c>true</c> when the request completed in full because of this advance.</returns>
      public static async Task<bool> AdvanceAsync(ApiCoreContext ctx, ApprovalFlowDeclaration flow,
         ApprovalRunScope scope, ta_ApprovalRequest request, IReadOnlyList<ta_ApprovalRequestStep> steps) {
         var finished = false;

         while (true) {
            var level = request.cApprovalRequestLevel;

            // A level is complete when nothing in it is still waiting. A skipped step never waits, and a
            // level made only of skipped steps is never entered, because the next level is always picked
            // from the steps that are still waiting.
            if (steps.Any(r => r.cApprovalRequestStepLevel == level &&
                               r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)) {
               break;
            }

            await flow.RunLevelCompletedAsync(scope, level);

            var nextLevels = steps
               .Where(r => r.cApprovalRequestStepLevel > level &&
                           r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)
               .Select(r => r.cApprovalRequestStepLevel)
               .ToList();

            if (nextLevels.Count == 0) {
               request.cApprovalRequestStage = ApprovalStage.Approved;
               request.cApprovalRequestCompletedDate = DateTime.Now;
               await flow.RunFinishingAsync(scope);
               finished = true;
               break;
            }

            request.cApprovalRequestLevel = nextLevels.Min();
         }

         request.ustamp = DateTime.UtcNow;
         await ctx.SaveChangesAsync();
         return finished;
      }
   }
}
