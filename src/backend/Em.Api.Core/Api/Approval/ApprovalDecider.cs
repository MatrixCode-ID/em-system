using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Memutuskan langkah-langkah request dokumen: menyetujui atau menolak, dengan seluruh pemeriksaan
   /// haknya, lalu memajukan atau menghentikan requestnya.
   /// </summary>
   /// <remarks>
   /// Setiap keputusan berjalan di transaksinya sendiri, jadi satu yang gagal tidak menyeret yang lain.
   /// Satu instance dipakai untuk satu permintaan: ia memegang service engine yang sedang berjalan, yang
   /// jadi sumber keterangan pemanggil untuk handler modul.
   /// </remarks>
   internal sealed class ApprovalDecider(ApiCoreContext ctx, ApprovalRegistry registry, ServicesBase host)
   {
      private const int MaxNoteLength = 500;

      private CancellationToken Token => host.AbortToken;

      /// <summary>
      /// Memutuskan seluruh keputusan yang dikirim, satu per satu.
      /// </summary>
      /// <param name="decisions">Keputusan yang diambil.</param>
      /// <returns>Satu hasil untuk setiap keputusan, dalam urutan yang sama.</returns>
      public async Task<ApprovalDecisionResult[]> DecideAsync(ApprovalDecision[] decisions) {
         ArgumentNullException.ThrowIfNull(decisions);
         host.Request.RequireUserId();

         var results = new List<ApprovalDecisionResult>(decisions.Length);
         foreach (var decision in decisions) {
            results.Add(await DecideOneAsync(decision));
         }

         return [.. results];
      }

      private async Task<ApprovalDecisionResult> DecideOneAsync(ApprovalDecision decision) {
         var result = new ApprovalDecisionResult {
            cApprovalRequestId = decision.cApprovalRequestId,
            StepName = decision.StepName
         };

         Outcome? outcome = null;
         ApprovalConflictException? conflict = null;
         ctx.ChangeTracker.Clear();

         try {
            outcome = await DecideCoreAsync(decision, result);
            result.Success = true;
         }
         catch (ApprovalConflictException ex) {
            // The values on the table moved under the request: the screen is told which ones, so the
            // approver can apply it anyway (unless the entity is gone) or reject it.
            conflict = ex;
            result.ErrorMessage = ex.Message;
            result.Conflicts = [.. ex.Conflicts];
            result.ConflictIsFinal = ex.IsFinal;
         }
         catch (ActionException ex) {
            result.ErrorMessage = ex.Message;
         }
         catch (Exception ex) when (ex is not OperationCanceledException || !Token.IsCancellationRequested) {
            // Whatever this was, the transaction is gone with it, so the decision was not recorded.
            host.Logger.LogError(ex, "Decision on approval request {RequestId}, step {Step} failed unexpectedly.",
               decision.cApprovalRequestId, decision.StepName);
            result.ErrorMessage = "The decision could not be recorded because of an unexpected error. Nothing was changed.";
         }
         finally {
            ctx.ChangeTracker.Clear();
         }

         if (conflict is not null) {
            // The decision was rolled back with everything else, but what was found is worth keeping: the
            // approver who opens the request again sees the values as they are now.
            try {
               await ApprovalDataApplier.RecordConflictsAsync(ctx, conflict.Compared, Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
               host.Logger.LogWarning(ex, "The conflicts found on approval request {RequestId} could not be recorded.",
                  decision.cApprovalRequestId);
            }
            finally {
               ctx.ChangeTracker.Clear();
            }
         }

         if (outcome is not null) {
            // The decision is stored for good by now; a hook that fails after that has nothing to undo.
            if (outcome.Finished) await RunAfterCommitAsync(outcome.Flow.RunFinishedAsync(outcome.Scope), "OnFinished", decision);
            if (outcome.Rejected) await RunAfterCommitAsync(outcome.Flow.RunRejectedAsync(outcome.Scope), "OnRejected", decision);
         }

         result.Request = await TryReadInfoAsync(decision.cApprovalRequestId, result.Success);
         return result;
      }

      private async Task<Outcome> DecideCoreAsync(ApprovalDecision decision, ApprovalDecisionResult result) {
         // 1. A real, active user. The built-in accounts hold every claim but cannot sign.
         var me = await new ApprovalAccess(ctx).RequireRealUserAsync(host.Request, Token);

         if (string.IsNullOrWhiteSpace(decision.cApprovalRequestId)) {
            throw new ActionException("A decision has to say which approval request it is about.", 400);
         }

         var request = await ctx.ta_ApprovalRequests.AsNoTracking()
                          .FirstOrDefaultAsync(r => r.cApprovalRequestId == decision.cApprovalRequestId, Token) ??
                       throw new ActionException($"Approval request '{decision.cApprovalRequestId}' was not found.", 404);

         var flow = registry.Get(request.cApprovalRequestDocType);

         // Whoever may see the request may decide it, with one exception: the claim that overrides a
         // block is declared by the guard of a step, not by the flow, so its holder need not hold any
         // claim the flow knows about. The override path checks that claim itself, further down.
         if (!(decision.Approve && decision.GuardOverride)) {
            ApprovalAccess.RequireView(host.Request, flow);
         }

         // 2. Still pending.
         if (request.cApprovalRequestStage != ApprovalStage.Pending) {
            throw new ActionException(
               $"This approval request is not waiting for a decision any more: it is {request.cApprovalRequestStage}.", 409);
         }

         // 3. The step is in the running level and still waiting.
         var steps = await ctx.ta_ApprovalRequestSteps.AsNoTracking()
            .Where(r => r.cApprovalRequestId == request.cApprovalRequestId)
            .OrderBy(r => r.cApprovalRequestStepLevel).ThenBy(r => r.cApprovalRequestStepOrder)
            .ToListAsync(Token);

         var step = FindStep(request, steps, decision.StepName);
         result.StepName = step.cApprovalRequestStepName;

         if (step.cApprovalRequestStepLevel != request.cApprovalRequestLevel ||
             step.cApprovalRequestStepStage != ApprovalStepStatus.Waiting) {
            throw new ActionException($"Step '{step.cApprovalRequestStepName}' is not waiting for a decision.", 409);
         }

         var rule = flow.StepRules.First(r => string.Equals(r.Name, step.cApprovalRequestStepName, StringComparison.OrdinalIgnoreCase));
         var note = string.IsNullOrWhiteSpace(decision.Note) ? null : decision.Note.Trim();
         if (note is { Length: > MaxNoteLength }) {
            throw new ActionException($"The note is {note.Length} characters long; the limit is {MaxNoteLength}.", 400);
         }

         var scope = new ApprovalRunScope(host, host.App.ServiceProvider, request);

         // 4. Claim, signer list, strictness and the guard, which together say in what capacity the
         //    caller signs: as the assigned signer, in place of one, or past a block.
         var holdsStepClaim = ApprovalAccess.HoldsClaim(host.Request, rule.Claim);
         var overrideRequested = decision.Approve && decision.GuardOverride;
         if (!holdsStepClaim && !overrideRequested) {
            throw new ActionException(
               $"You are not allowed to decide step '{rule.Name}': it needs the claim '{rule.Claim.Name}'.", 403);
         }

         var assigned = await ctx.ta_ApprovalRequestStepSigners.AsNoTracking()
            .Where(r => r.cApprovalRequestId == request.cApprovalRequestId && r.cApprovalRequestStepName == step.cApprovalRequestStepName)
            .Select(r => r.cUserId)
            .ToListAsync(Token);

         var role = ApprovalSignerRole.Assigned;
         string? guardReason = null;

         ApprovalGuard? guard = null;
         if (decision.Approve) {
            guard = await flow.EvaluateGuardAsync(scope, step.cApprovalRequestStepName, me);
         }

         if (guard is { IsAllowed: false }) {
            if (!decision.GuardOverride) {
               var hint = guard.OverridableBy is null
                  ? string.Empty
                  : " A holder of the override claim can approve it anyway, with a reason.";
               throw new ActionException($"Step '{rule.Name}' cannot be approved yet: {guard.Reason}{hint}", 409);
            }

            if (guard.OverridableBy is not { } overrideName) {
               throw new ActionException(
                  $"Step '{rule.Name}' cannot be approved yet and nobody can override that: {guard.Reason}", 409);
            }

            var overrideClaim = new ClaimAction { ModuleName = flow.ModuleName, Name = overrideName };
            if (!ApprovalAccess.HoldsClaim(host.Request, overrideClaim)) {
               throw new ActionException(
                  $"You are not allowed to approve step '{rule.Name}' past its block: it needs the claim '{overrideClaim.Name}'.", 403);
            }

            role = ApprovalSignerRole.Override;
            guardReason = guard.Reason;
         }
         else if (!holdsStepClaim) {
            // The override was asked for, but nothing is blocking, and the claim of the step itself is
            // what the caller is missing.
            throw new ActionException(
               $"You are not allowed to decide step '{rule.Name}': it needs the claim '{rule.Claim.Name}'.", 403);
         }
         else if (assigned.Count > 0 && !assigned.Contains(me, StringComparer.OrdinalIgnoreCase)) {
            if (rule.Strict) {
               throw new ActionException(
                  $"Step '{rule.Name}' can only be signed by the person it names, not by somebody in their place.", 403);
            }

            role = ApprovalSignerRole.Substitute;
         }

         // The signature is made on behalf of the assigned signer when there is exactly one to name.
         var onBehalfId = role != ApprovalSignerRole.Assigned && assigned.Count == 1 &&
                          !string.Equals(assigned[0], me, StringComparison.OrdinalIgnoreCase)
            ? assigned[0]
            : null;

         // 5. A reason is required wherever the signature is not the plain one.
         if (note is null && (!decision.Approve || role != ApprovalSignerRole.Assigned)) {
            var why = !decision.Approve
               ? "rejecting a step"
               : role == ApprovalSignerRole.Substitute
                  ? "signing in place of somebody else"
                  : "approving past a block";
            throw new ActionException($"A reason is required for {why}.", 400);
         }

         await EnforceDistinctAsync(flow, rule, steps, me);

         decision.Note = note;
         var stamp = DateTime.UtcNow;
         var now = DateTime.Now;

         // 6. Everything that writes goes through one transaction.
         await using var transaction = await ApprovalTransaction.BeginAsync(ctx, flow, host.App.ServiceProvider, Token);
         try {
            // The module checks the input and the decision itself, before anything is written.
            IReadOnlyList<ApprovalInputValue>? values = null;
            if (flow.StepRequiresInput(step.cApprovalRequestStepName)) {
               if (decision.Approve) {
                  values = await flow.ValidateInputAsync(scope, step.cApprovalRequestStepName, me, decision.Payload);
               }
               else if (!string.IsNullOrWhiteSpace(decision.Payload)) {
                  // A rejection is never held back by its input boxes: whatever was filled in is frozen and
                  // printed with the refusal, and a payload the input cannot accept just leaves the boxes empty.
                  // What makes a rejection acceptable is still the module's own signing check, below.
                  try {
                     values = await flow.ValidateInputAsync(scope, step.cApprovalRequestStepName, me, decision.Payload);
                  }
                  catch (ActionException) {
                  }
               }
            }

            await flow.RunSigningAsync(scope, step.cApprovalRequestStepName, me, decision);

            var json = step.json_object;
            if (values is not null) {
               var snapshot = ApprovalStepSnapshot.Read(step.json_object);
               if (snapshot.Fields.Count != values.Count) {
                  throw new InvalidOperationException(
                     $"Step '{step.cApprovalRequestStepName}' of '{flow.DocType}' froze {snapshot.Fields.Count} input boxes, but its input gave {values.Count} values.");
               }

               for (var i = 0; i < values.Count; i++) {
                  snapshot.Fields[i].Text = values[i].Text;
                  snapshot.Fields[i].Checked = values[i].Checked;
               }

               json = snapshot.Write();
            }

            var code = await ApprovalVerificationCode.CreateUnusedAsync(ctx, Token);
            var stage = decision.Approve ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected;

            // The decision is written only while the step is still waiting and the request still
            // pending: two people deciding at once, or a request pulled back in the meantime, leave
            // this with no row to update, and the loser reads a plain 409.
            var stepId = step.cApprovalRequestStepId;
            var written = await ctx.ta_ApprovalRequestSteps
               .Where(r => r.cApprovalRequestStepId == stepId &&
                           r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting &&
                           ctx.ta_ApprovalRequests.Any(q => q.cApprovalRequestId == r.cApprovalRequestId &&
                                                            q.cApprovalRequestStage == ApprovalStage.Pending))
               .ExecuteUpdateAsync(s => s
                  .SetProperty(r => r.cApprovalRequestStepStage, stage)
                  .SetProperty(r => r.cApprovalRequestStepSignerId, me)
                  .SetProperty(r => r.cApprovalRequestStepSignedDate, now)
                  .SetProperty(r => r.cApprovalRequestStepSignerRole, role)
                  .SetProperty(r => r.cApprovalRequestStepOnBehalfId, onBehalfId)
                  .SetProperty(r => r.cApprovalRequestStepNote, note)
                  .SetProperty(r => r.cApprovalRequestStepVerificationCode, code)
                  .SetProperty(r => r.cApprovalRequestStepGuardReason, guardReason)
                  .SetProperty(r => r.json_object, json)
                  .SetProperty(r => r.ustamp, stamp), Token);

            if (written == 0) {
               throw new ActionException(
                  $"Step '{step.cApprovalRequestStepName}' was decided by somebody else, or the request was pulled back, a moment ago.", 409);
            }

            // What was read before the write is stale now; read it again, this time to change it. The
            // context does not track what it reads unless asked to, hence AsTracking.
            ctx.ChangeTracker.Clear();
            var tracked = await ctx.ta_ApprovalRequests.AsTracking().FirstAsync(r => r.cApprovalRequestId == request.cApprovalRequestId, Token);
            var trackedSteps = await ctx.ta_ApprovalRequestSteps.AsTracking()
               .Where(r => r.cApprovalRequestId == request.cApprovalRequestId)
               .OrderBy(r => r.cApprovalRequestStepLevel).ThenBy(r => r.cApprovalRequestStepOrder)
               .ToListAsync(Token);
            var live = scope with { Request = tracked };

            // A data request carries its proposal with it; the hooks of the module are shown it.
            var dataFlow = flow as IApprovalDataFlow;
            if (dataFlow is not null) {
               live = live with { Items = await ApprovalDataApplier.ReadItemsAsync(ctx, dataFlow, request.cApprovalRequestId, Token) };
            }

            if (decision.Approve) {
               await flow.RunInputSignedAsync(live, step.cApprovalRequestStepName, me, decision.Payload);
            }

            await flow.RunSignedAsync(live, step.cApprovalRequestStepName, me, decision);

            bool finished = false, rejected = false;
            if (decision.Approve) {
               // The proposal is compared and applied here, before the request finishes, so what the
               // finishing hook sees is already on the tables - and a conflict stops the decision as a whole.
               if (dataFlow is not null) {
                  live = live with {
                     Items = await ApprovalDataApplier.ApplyAsync(ctx, dataFlow, live, decision.Override, note, Token)
                  };
               }

               finished = await ApprovalProgress.AdvanceAsync(ctx, flow, live, tracked, trackedSteps);
            }
            else {
               // A rejection stops the whole request, whichever step it came from: what was still open
               // is skipped, and the document is free to be edited again.
               foreach (var open in trackedSteps.Where(r => r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting)) {
                  open.cApprovalRequestStepStage = ApprovalStepStatus.Skipped;
                  open.ustamp = stamp;
               }

               tracked.cApprovalRequestStage = ApprovalStage.Rejected;
               tracked.cApprovalRequestCompletedDate = now;
               tracked.ustamp = stamp;

               await flow.RunRejectingAsync(live);
               await ctx.SaveChangesAsync(Token);
               rejected = true;
            }

            await transaction.CommitAsync(Token);
            return new Outcome(flow, live, finished, rejected);
         }
         catch {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
         }
      }

      private static ta_ApprovalRequestStep FindStep(ta_ApprovalRequest request, IReadOnlyList<ta_ApprovalRequestStep> steps,
         string? stepName) {
         if (!string.IsNullOrWhiteSpace(stepName)) {
            return steps.FirstOrDefault(r => string.Equals(r.cApprovalRequestStepName, stepName.Trim(), StringComparison.OrdinalIgnoreCase)) ??
                   throw new ActionException($"Approval request '{request.cApprovalRequestId}' has no step named '{stepName}'.", 404);
         }

         // A step name is only optional when there is no choice to make.
         var open = steps.Where(r => r.cApprovalRequestStepLevel == request.cApprovalRequestLevel &&
                                     r.cApprovalRequestStepStage == ApprovalStepStatus.Waiting).ToList();
         return open.Count == 1
            ? open[0]
            : throw new ActionException(
               $"The decision does not name a step, and {open.Count} steps are waiting; say which one it is for.", 400);
      }

      // Four-eyes at signing time, against whoever really signed the other step. The rule is symmetric,
      // so it is looked up both ways: the step this one names, and the steps that name this one. A step
      // that has not been signed yet is not compared - it is checked when its own turn comes.
      private async Task EnforceDistinctAsync(ApprovalFlowDeclaration flow, ApprovalStepRule rule,
         IReadOnlyList<ta_ApprovalRequestStep> steps, string me) {
         var others = flow.StepRules
            .Where(r => string.Equals(r.Name, rule.DistinctFrom, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.DistinctFrom, rule.Name, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name);

         foreach (var name in others) {
            var other = steps.FirstOrDefault(r => string.Equals(r.cApprovalRequestStepName, name, StringComparison.OrdinalIgnoreCase));
            if (other is not { cApprovalRequestStepStage: ApprovalStepStatus.Approved }) continue;
            if (!string.Equals(other.cApprovalRequestStepSignerId, me, StringComparison.OrdinalIgnoreCase)) continue;

            // A user with the administrator switch on counts as holding every claim, and the rule is not
            // meant to be stricter than that.
            if (await ctx.ta_Users.AnyAsync(r => r.cUserId == me && r.cUserIsAdmin, Token)) return;

            throw new ActionException(
               $"Step '{rule.Name}' and step '{other.cApprovalRequestStepName}' have to be signed by different people, " +
               "but you already signed the other one.", 403);
         }
      }

      private async Task RunAfterCommitAsync(Task hook, string name, ApprovalDecision decision) {
         try {
            await hook;
         }
         catch (Exception ex) {
            host.Logger.LogError(ex, "Hook {Hook} of approval request {RequestId} failed after the decision was stored.",
               name, decision.cApprovalRequestId);
         }
      }

      private async Task<ApprovalRequestInfo?> TryReadInfoAsync(string approvalRequestId, bool viewProven) {
         try {
            return await new ApprovalRequestReader(ctx, registry, host.Request, Token).InfoAsync(approvalRequestId, viewProven);
         }
         catch (Exception ex) when (ex is not OperationCanceledException) {
            // The state of the request is a courtesy to the screen; the decision stands without it. A failed
            // decision shows it only to somebody who may see the request anyway.
            host.Logger.LogWarning(ex, "The state of approval request {RequestId} could not be read after a decision.",
               approvalRequestId);
            return null;
         }
      }

      private sealed record Outcome(ApprovalFlowDeclaration Flow, ApprovalRunScope Scope, bool Finished, bool Rejected);
   }
}
