using System.Data;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.Approval;

internal sealed partial class ApprovalEngine {
   private const int MinimumSystemSummary = 10;
   public Task<string> RequestAsync(string name, string subjectKey, string summary, CancellationToken ct = default) =>
      RequestAsync(name, subjectKey, summary, null, ct);
   public async Task<string> RequestAsync(string name, string subjectKey, string summary, string? subSummary, CancellationToken ct = default) {
      var user = await RealUserAsync(ct); registry.System(name);
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      try {
         var request = await BuildSystemAsync(name, subjectKey, summary, subSummary, user, ct);
         await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
         await NotifySystemFinishedAsync(request, ct);
         return request.cApprovalRequestId;
      }
      catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) {
         await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
         throw new ActionException("This subject already has a System approval request. Open it or reinstate it.", 409);
      }
      catch {
         await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw;
      }
   }
   private async Task<ta_ApprovalRequest> BuildSystemAsync(string name, string subjectKey, string summary, string? subSummary, string user, CancellationToken ct) {
      var registration = registry.System(name);
      RequireText(subjectKey, "Subject key", 400);
      summary = summary?.Trim() ?? string.Empty;
      if (summary.Length is < MinimumSystemSummary or > 400)
         throw new ActionException($"Summary must contain {MinimumSystemSummary} to 400 characters.", 400);
      subSummary = string.IsNullOrWhiteSpace(subSummary) ? null : subSummary.Trim();
      OptionalText(subSummary, "Sub-summary", 400);
      if (await db.ta_ApprovalRequests.AnyAsync(x => x.cApprovalRequestKind == ApprovalKind.System &&
          x.cApprovalName == name && x.cSubjectKey == subjectKey && x.cApprovalRequestStage >= ApprovalStage.Rejected, ct))
         throw new ActionException("This subject already has a System approval request. Open it or reinstate it.", 409);
      var settings = await db.ta_ApprovalSignerSettings.Where(x => x.cApprovalName == name).ToArrayAsync(ct);
      var settingIds = settings.Select(x => x.cApprovalSignerSettingId).ToArray();
      var backups = await db.ta_ApprovalSignerSettingBackups.Where(x => settingIds.Contains(x.cApprovalSignerSettingId))
         .OrderBy(x => x.cApprovalSignerSettingBackupOrder).ToArrayAsync(ct);
      var request = NewRequest(ApprovalKind.System, name, subjectKey, summary, user);
      request.cSubSummary = subSummary;
      var steps = new List<ta_ApprovalRequestStep>();
      var firstLevel = registration.Definition.Steps.Min(x => x.Level);
      foreach (var declaration in registration.Definition.Steps.OrderBy(x => x.Level)) {
         var setting = settings.SingleOrDefault(x => x.cApprovalSignerSettingStep == declaration.Name) ??
            throw new ActionException($"Approval '{name}', step '{declaration.Name}' has no signer. Configure it in System Approval Editor.", 409);
         ApprovalDynamicSigner? dynamic = null;
         if (setting.cApprovalSignerSettingDynamic) {
            dynamic = registration.Handler is null ? null : await registration.Handler.ResolveAsync(services, declaration, subjectKey, caller, ct);
            if (dynamic is null || !await IsActiveAsync(dynamic.UserId, ct))
               throw new ActionException($"Approval '{name}', step '{declaration.Name}' has an unresolved Dynamic Signer. The module handler must supply one active user.", 409);
         }
         var primary = dynamic?.UserId ?? setting.cUserId;
         if (primary is null || !await IsActiveAsync(primary, ct))
            throw new ActionException($"Approval '{name}', step '{declaration.Name}' has no active primary signer.", 409);
         if (dynamic?.SignImmediately == true && (primary != user || declaration.Level != firstLevel))
            throw new ActionException($"Approval '{name}', step '{declaration.Name}' can sign immediately only for its requester in the first level.", 409);
         var step = new ta_ApprovalRequestStep {
            cApprovalRequestStepId = Id(), cApprovalRequestId = request.cApprovalRequestId,
            cApprovalRequestStepLevel = declaration.Level, cApprovalRequestStepOrder = steps.Count,
            cApprovalRequestStepName = declaration.Name,
            cApprovalRequestStepKind = setting.cApprovalSignerSettingDynamic ? ApprovalStepKind.Dynamic : ApprovalStepKind.User,
            cApprovalRequestStepStage = dynamic?.SignImmediately == true ? ApprovalStepStatus.Approved : ApprovalStepStatus.Waiting,
            datestamp = DateTime.Now, ustamp = DateTime.Now
         };
         steps.Add(step); db.ta_ApprovalRequestSteps.Add(step);
         void Candidate(string id, bool backup, int order) => db.ta_ApprovalRequestStepCandidates.Add(new ta_ApprovalRequestStepCandidate {
            cApprovalRequestStepCandidateId = Id(), cApprovalRequestStepId = step.cApprovalRequestStepId,
            cUserId = id, cApprovalRequestStepCandidateBackup = backup, cApprovalRequestStepCandidateOrder = order,
            datestamp = DateTime.Now, ustamp = DateTime.Now
         });
         Candidate(primary, false, 0);
         var order = 1;
         foreach (var backup in backups.Where(x => x.cApprovalSignerSettingId == setting.cApprovalSignerSettingId && x.cUserId != primary))
            Candidate(backup.cUserId, true, order++);
         if (dynamic?.SignImmediately == true) await SignatureAsync(request, step.cApprovalRequestStepId, user, true, null, false, false, ct);
      }
      Advance(request, steps);
      return request;
   }
   private Task<bool> IsActiveAsync(string id, CancellationToken ct) => db.ta_Users.AnyAsync(x => x.cUserId == id && x.cUserState == UserState.Active, ct);
   private static void Advance(ta_ApprovalRequest request, IEnumerable<ta_ApprovalRequestStep> steps) {
      var remaining = steps.Where(x => x.cApprovalRequestStepStage == ApprovalStepStatus.Waiting).Select(x => (int?)x.cApprovalRequestStepLevel).Min();
      if (remaining is null) Complete(request, true);
      else { request.cApprovalRequestLevel = remaining.Value; request.ustamp = DateTime.Now; }
   }
   private Task<ApprovalDecisionResult> DecideSystemAsync(ApprovalDecision decision, string user, CancellationToken ct) =>
      DecideSystemTransactionAsync(decision.RequestId, [decision.StepId!], user, decision.Approve, decision.Note, ct);
   private Task<ApprovalDecisionResult> DecideSystemBatchAsync(string id, string[] steps, string user, string? note, CancellationToken ct) =>
      DecideSystemTransactionAsync(id, steps, user, true, note, ct);
   private async Task<ApprovalDecisionResult> DecideSystemTransactionAsync(string id, string[] stepIds, string user, bool approve, string? note, CancellationToken ct) {
      ta_ApprovalRequest request;
      await using (var transaction = await db.Database.BeginTransactionAsync(ct)) {
         request = await LoadAsync(id, true, ct);
         if (request.cApprovalRequestStage != ApprovalStage.Pending) throw new ActionException("This request is no longer pending.", 409);
         var steps = await db.ta_ApprovalRequestSteps.AsTracking().Where(x => x.cApprovalRequestId == request.cApprovalRequestId).ToArrayAsync(ct);
         foreach (var stepId in stepIds) {
            var step = steps.SingleOrDefault(x => x.cApprovalRequestStepId == stepId) ?? throw new ActionException("Choose a step of this request.", 400);
            if (step.cApprovalRequestStepLevel != request.cApprovalRequestLevel || step.cApprovalRequestStepStage != ApprovalStepStatus.Waiting)
               throw new ActionException("The selected step is not active.", 409);
            var candidate = await db.ta_ApprovalRequestStepCandidates.SingleOrDefaultAsync(x => x.cApprovalRequestStepId == step.cApprovalRequestStepId && x.cUserId == user, ct);
            if (candidate is null && !caller.IsAdmin) throw new ActionException("You are not a candidate for this active step.", 403);
            step.cApprovalRequestStepStage = approve ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected;
            step.ustamp = DateTime.Now;
            await SignatureAsync(request, step.cApprovalRequestStepId, user, approve, note,
               caller.IsAdmin && candidate is null, candidate?.cApprovalRequestStepCandidateBackup == true, ct);
         }
         if (approve) Advance(request, steps); else Complete(request, false);
         await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
      }
      await NotifySystemFinishedAsync(request, ct);
      return new ApprovalDecisionResult(true);
   }
   private async Task NotifySystemFinishedAsync(ta_ApprovalRequest request, CancellationToken ct) {
      if (request.cApprovalRequestStage is not (ApprovalStage.Approved or ApprovalStage.Rejected)) return;
      var handler = registry.System(request.cApprovalName).Handler;
      if (handler is null) return;
      try { await handler.FinishAsync(services, request.cApprovalRequestId, request.cSubjectKey!, request.cApprovalRequestStage == ApprovalStage.Approved, ct); }
      catch (Exception e) { logger.LogError(e, "Approval handler failed after commit for {RequestId}.", request.cApprovalRequestId); }
   }
   public async Task VoidAsync(string requestId, string reason, CancellationToken ct = default) {
      var user = await RealUserAsync(ct); reason = ValidateVoidReason(reason);
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      var request = await LoadAsync(requestId, true, ct);
      Void(request, user, reason);
      await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
   }
   private void Void(ta_ApprovalRequest request, string user, string reason) {
      if (!CanVoid(request)) throw new ActionException("You cannot void this System approval request.", 403);
      if (request.cApprovalRequestStage < ApprovalStage.Rejected) throw new ActionException("This request is already void.", 409);
      request.cApprovalRequestStage = request.cApprovalRequestStage == ApprovalStage.Pending ? ApprovalStage.Cancelled : ApprovalStage.ReinstatedAfterFinish;
      request.ustamp = DateTime.Now; AddComment(request.cApprovalRequestId, user, $"Voided: {reason}");
   }
   public async Task<string> ReinstateAsync(string requestId, string summary, string reason, CancellationToken ct = default) {
      var user = await RealUserAsync(ct); reason = ValidateVoidReason(reason);
      ta_ApprovalRequest replacement;
      await using (var transaction = await db.Database.BeginTransactionAsync(ct)) {
         var old = await LoadAsync(requestId, true, ct);
         if (old.cApprovalRequestKind != ApprovalKind.System) throw new ActionException("Only System approvals support reinstate.", 400);
         Void(old, user, reason);
         await db.SaveChangesAsync(ct);
         replacement = await BuildSystemAsync(old.cApprovalName, old.cSubjectKey!, summary, old.cSubSummary, user, ct);
         replacement.cReinstateOf = old.cApprovalRequestId;
         await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
      }
      await NotifySystemFinishedAsync(replacement, ct);
      return replacement.cApprovalRequestId;
   }
   public async Task<ta_ApprovalRequest?> FindByKeyAsync(string name, string subjectKey, CancellationToken ct = default) {
      registry.System(name); caller.RequireUserId();
      var request = await db.ta_ApprovalRequests.SingleOrDefaultAsync(x => x.cApprovalRequestKind == ApprovalKind.System &&
         x.cApprovalName == name && x.cSubjectKey == subjectKey && x.cApprovalRequestStage >= ApprovalStage.Rejected, ct);
      if (request is not null && !await CanViewAsync(request, ct)) throw new ActionException("You cannot view this approval request.", 403);
      return request;
   }
   private void RequireSignerSettings() {
      caller.RequireUserId();
      if (!caller.IsDebugRequest && !ApprovalAccess.HoldsClaim(caller, new ClaimAction { ModuleName = "Administrative Tools", Name = "Approval Signer Settings" }))
         throw new ActionException("Approval Signer Settings is required.", 403);
   }
   internal async Task<ApprovalSignerSettings> SignerSettingsAsync(string name, CancellationToken ct) {
      RequireSignerSettings(); var definition = registry.System(name).Definition;
      var rows = await db.ta_ApprovalSignerSettings.Where(x => x.cApprovalName == name).ToArrayAsync(ct);
      var ids = rows.Select(x => x.cApprovalSignerSettingId).ToArray();
      var backups = await db.ta_ApprovalSignerSettingBackups.Where(x => ids.Contains(x.cApprovalSignerSettingId)).OrderBy(x => x.cApprovalSignerSettingBackupOrder).ToArrayAsync(ct);
      var users = await db.ta_Users.OrderBy(x => x.cUserAccount).Select(x => new ApprovalSignerUser(x.cUserId, x.cUserAccount, x.cUserState == UserState.Active)).ToArrayAsync(ct);
      return new ApprovalSignerSettings(name, definition.Steps.ToArray(), rows.Select(x => new ApprovalSignerSetting(x.cApprovalSignerSettingStep,
         x.cApprovalSignerSettingDynamic, x.cUserId, backups.Where(b => b.cApprovalSignerSettingId == x.cApprovalSignerSettingId).Select(b => b.cUserId).ToArray())).ToArray(), users);
   }
   internal async Task SaveSignerSettingsAsync(string name, ApprovalSignerSetting[] settings, CancellationToken ct) {
      RequireSignerSettings(); var definition = registry.System(name).Definition;
      if (settings.Length != definition.Steps.Count || !settings.Select(x => x.StepName).Order().SequenceEqual(definition.Steps.Select(x => x.Name).Order()))
         throw new ActionException("Provide exactly one setting for every declared step.", 400);
      foreach (var setting in settings) {
         if (setting.Dynamic ? setting.UserId is not null : string.IsNullOrWhiteSpace(setting.UserId))
            throw new ActionException($"Step '{setting.StepName}' requires exactly one primary user or Dynamic Signer.", 400);
         if (setting.Backups.Distinct().Count() != setting.Backups.Length || setting.Backups.Contains(setting.UserId))
            throw new ActionException("Backups must be distinct from each other and from the primary signer.", 400);
         foreach (var user in setting.Backups.Concat(setting.UserId is null ? [] : new[] { setting.UserId }))
            if (!await IsActiveAsync(user, ct)) throw new ActionException($"Signer '{user}' is not active.", 400);
      }
      await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
      var old = await db.ta_ApprovalSignerSettings.Where(x => x.cApprovalName == name).ToArrayAsync(ct);
      var ids = old.Select(x => x.cApprovalSignerSettingId).ToArray();
      await db.ta_ApprovalSignerSettingBackups.Where(x => ids.Contains(x.cApprovalSignerSettingId)).ExecuteDeleteAsync(ct);
      db.ta_ApprovalSignerSettings.RemoveRange(old); await db.SaveChangesAsync(ct);
      foreach (var setting in settings) {
         var id = Id();
         db.ta_ApprovalSignerSettings.Add(new ta_ApprovalSignerSetting {
            cApprovalSignerSettingId = id, cApprovalName = name, cApprovalSignerSettingStep = setting.StepName,
            cApprovalSignerSettingDynamic = setting.Dynamic, cUserId = setting.UserId, datestamp = DateTime.Now, ustamp = DateTime.Now
         });
         foreach (var (user, order) in setting.Backups.Select((u, i) => (u, i))) db.ta_ApprovalSignerSettingBackups.Add(new ta_ApprovalSignerSettingBackup {
            cApprovalSignerSettingBackupId = Id(), cApprovalSignerSettingId = id, cUserId = user,
            cApprovalSignerSettingBackupOrder = order, datestamp = DateTime.Now, ustamp = DateTime.Now
         });
      }
      await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
   }
}
