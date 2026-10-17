using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core.Approval;

internal sealed partial class ApprovalEngine(ApiCoreContext db, ApprovalRegistry registry,
   IServiceProvider services, ActionRequest caller, ILogger<ApprovalEngine> logger) : IApprovalEngine {
   private static string Id() => Ulid.NewUlid().ToString();
   private Task<string> RealUserAsync(CancellationToken ct) => new ApprovalAccess(db).RequireRealUserAsync(caller, ct);
   private static void RequireText(string? value, string label, int maximum) {
      if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
         throw new ActionException($"{label} must contain 1 to {maximum} characters.", 400);
   }
   private static void OptionalText(string? value, string label, int maximum) {
      if (value?.Length > maximum) throw new ActionException($"{label} must contain at most {maximum} characters.", 400);
   }
   private ta_ApprovalRequest NewRequest(ApprovalKind kind, string name, string? key, string summary, string requester, string? note = null) {
      RequireText(name, "Approval name", 50); OptionalText(summary, "Summary", 400); OptionalText(note, "Note", 1000);
      if (key is not null) RequireText(key, "Subject key", 400);
      var now = DateTime.Now;
      var row = new ta_ApprovalRequest {
         cApprovalRequestId = Id(), cApprovalRequestKind = kind, cApprovalName = name,
         cSubjectKey = key, cSummary = summary, cApprovalRequestRequesterId = requester,
         cApprovalRequestStage = ApprovalStage.Pending, cApprovalRequestNote = note,
         datestamp = now, ustamp = now
      };
      db.ta_ApprovalRequests.Add(row);
      return row;
   }
   private async Task<ta_ApprovalRequest> LoadAsync(string id, bool locked, CancellationToken ct) {
      IQueryable<ta_ApprovalRequest> query = locked
         ? db.ta_ApprovalRequests.FromSqlInterpolated($"SELECT * FROM dbo.ta_ApprovalRequest WITH (UPDLOCK,HOLDLOCK) WHERE cApprovalRequestId = {id}")
         : db.ta_ApprovalRequests;
      return await query.SingleOrDefaultAsync(x => x.cApprovalRequestId == id, ct) ?? throw new ActionException("Approval request was not found.", 404);
   }
   private async Task<bool> CanViewAsync(ta_ApprovalRequest request, CancellationToken ct) {
      caller.RequireUserId();
      if (request.cApprovalRequestKind == ApprovalKind.Document) throw new NotImplementedException("Document Approval is not implemented.");
      if (caller.IsAdmin) return true;
      if (request.cApprovalRequestKind == ApprovalKind.Data)
         return registry.DataRoot(request.cApprovalName).Resolve(services).CanView(caller);
      registry.System(request.cApprovalName);
      if (request.cApprovalRequestRequesterId == caller.cUserId ||
          ApprovalAccess.HoldsClaim(caller, ClaimAction.Create<IApprovalServices>($"View {request.cApprovalName}"))) return true;
      return await (from step in db.ta_ApprovalRequestSteps
                    join candidate in db.ta_ApprovalRequestStepCandidates on step.cApprovalRequestStepId equals candidate.cApprovalRequestStepId
                    where step.cApprovalRequestId == request.cApprovalRequestId && candidate.cUserId == caller.cUserId
                    select candidate).AnyAsync(ct);
   }
   internal async Task<ta_ApprovalRequest> RequireViewAsync(string requestId, CancellationToken ct) {
      var row = await LoadAsync(requestId, false, ct);
      if (!await CanViewAsync(row, ct)) throw new ActionException("You cannot view this approval request.", 403);
      return row;
   }
   private bool CanVoid(ta_ApprovalRequest row) => row.cApprovalRequestKind == ApprovalKind.System &&
      (caller.IsAdmin || row.cApprovalRequestRequesterId == caller.cUserId ||
       registry.System(row.cApprovalName).Definition.VoidClaims.Any(c => ApprovalAccess.HoldsClaim(caller, c)));
   private bool CanWithdraw(ta_ApprovalRequest row) => row.cApprovalRequestKind == ApprovalKind.Data &&
      (caller.IsAdmin || row.cApprovalRequestRequesterId == caller.cUserId || registry.DataRoot(row.cApprovalName).Resolve(services).CanWithdraw(caller));
   private bool CanDecideData(ta_ApprovalRequest row) => caller.IsAdmin || registry.DataRoot(row.cApprovalName).Resolve(services).CanDecide(caller);
   private async Task SignatureAsync(ta_ApprovalRequest request, string? stepId, string userId, bool approved,
      string? note, bool admin, bool backup, CancellationToken ct) {
      OptionalText(note, "Decision note", 1000);
      db.ta_ApprovalRequestSignatures.Add(new ta_ApprovalRequestSignature {
         cApprovalRequestSignatureId = Id(), cApprovalRequestId = request.cApprovalRequestId,
         cApprovalRequestStepId = stepId, cUserId = userId, cApprovalRequestSignatureApproved = approved,
         cApprovalRequestSignatureNote = note, cApprovalRequestSignatureAdmin = admin,
         cApprovalRequestSignatureBackup = backup,
         cApprovalRequestSignatureCode = await ApprovalVerificationCode.CreateUnusedAsync(db, ct),
         datestamp = DateTime.Now, ustamp = DateTime.Now
      });
   }
   private static void Complete(ta_ApprovalRequest row, bool approved) {
      row.cApprovalRequestStage = approved ? ApprovalStage.Approved : ApprovalStage.Rejected;
      row.cApprovalRequestCompletedDate = DateTime.Now; row.cApprovalRequestLevel = 0; row.ustamp = DateTime.Now;
   }
   internal async Task WithdrawAsync(string requestId, string reason, CancellationToken ct) {
      var user = await RealUserAsync(ct); RequireText(reason, "Withdrawal reason", 990);
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      var row = await LoadAsync(requestId, true, ct);
      if (!CanWithdraw(row)) throw new ActionException("You cannot withdraw this Data request.", 403);
      if (row.cApprovalRequestStage != ApprovalStage.Pending) throw new ActionException("Only a pending Data request can be withdrawn.", 409);
      row.cApprovalRequestStage = ApprovalStage.Cancelled; row.ustamp = DateTime.Now;
      AddComment(row.cApprovalRequestId, user, $"Withdrawn: {reason}");
      await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
   }
   private void AddComment(string requestId, string userId, string text) {
      RequireText(text, "Comment", 1000);
      db.ta_ApprovalRequestComments.Add(new ta_ApprovalRequestComment {
         cApprovalRequestCommentId = Id(), cApprovalRequestId = requestId, cUserId = userId,
         cApprovalRequestCommentText = text, datestamp = DateTime.Now, ustamp = DateTime.Now
      });
   }
   internal async Task CommentAsync(string requestId, string text, CancellationToken ct) {
      var user = await RealUserAsync(ct); await RequireViewAsync(requestId, ct);
      AddComment(requestId, user, text); await db.SaveChangesAsync(ct);
   }
   internal async Task<ApprovalDecisionResult> DecideAsync(ApprovalDecision decision, CancellationToken ct) {
      var user = await RealUserAsync(ct); OptionalText(decision.Note, "Decision note", 1000);
      if (!decision.Approve) RequireText(decision.Note, "Rejection reason", 1000);
      var header = await LoadAsync(decision.RequestId, false, ct);
      if (header.cApprovalRequestKind == ApprovalKind.Document) throw new NotImplementedException("Document Approval is not implemented.");
      return header.cApprovalRequestKind == ApprovalKind.Data
         ? await DecideDataAsync(decision, user, ct) : await DecideSystemAsync(decision, user, ct);
   }
}
