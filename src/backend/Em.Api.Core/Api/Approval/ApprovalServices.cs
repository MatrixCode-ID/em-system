using Em.Api.Core.Models;
using Em.Shared;
using Em.Api.Core.Hub;
using Microsoft.Extensions.Logging;
namespace Em.Api.Core.Approval;
/// <summary>Approval actions; each operation authorizes the caller against its registered definition.</summary>
[Module(Defaults.ApprovalModuleName)]
public sealed class ApprovalServices(IApprovalEngine engine) : ServicesBase, IApprovalServices {
   private ApprovalEngine Engine => (ApprovalEngine)engine;
   #region Meta's
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalBatchResult> PostGetMeta_ApprovalDataDecideAll(ApprovalDataBatchRequest request) => Engine.DataDecideAllAsync(request, AbortToken);
   /// <inheritdoc />
   [GetAction]
   public async Task<HubTaskInfo[]> GetMeta_UserHubTasks() {
      Request.RequireUserId();
      var tasks = new List<HubTaskInfo>();
      foreach (var source in GetService<IEnumerable<IHubTaskSource>>() ?? []) {
         try { tasks.AddRange(await source.GetTasksAsync(AbortToken)); }
         catch (Exception ex) when (ex is not OperationCanceledException || !AbortToken.IsCancellationRequested) {
            Logger.LogError(ex, "Hub task source {Source} failed and was left out of the list.", source.GetType().Name);
         }
      }
      return [.. tasks];
   }
   /// <inheritdoc />
   [GetAction]
   public Task<ApprovalCapability[]> GetMeta_ApprovalCapabilities() => Engine.CapabilitiesAsync(AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalPage> PostGetMeta_ApprovalRequests(ApprovalQuery filter) => Engine.ListAsync(filter, AbortToken);
   /// <inheritdoc />
   [GetAction]
   public Task<ApprovalRequestDetail> GetMeta_ApprovalRequest(string requestId) => Engine.DetailAsync(requestId, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ta_ApprovalRequest[]> PostGetMeta_ApprovalRequestsBySubject(ApprovalKind kind, string name, string subjectKey) => Engine.BySubjectAsync(kind, name, subjectKey, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalDecisionResult> PostGetMeta_ApprovalDecide(ApprovalDecision decision) => Engine.DecideAsync(decision, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task PostMeta_ApprovalWithdraw(string requestId, string reason) => Engine.WithdrawAsync(requestId, reason, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<string> PostGetMeta_ApprovalSystemRequest(string name, string subjectKey, string summary, string? subSummary) => engine.RequestAsync(name, subjectKey, summary, subSummary, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<string> PostGetMeta_ApprovalSystemReinstate(string requestId, string summary, string reason) => engine.ReinstateAsync(requestId, summary, reason, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task PostMeta_ApprovalSystemVoid(string requestId, string reason) => engine.VoidAsync(requestId, reason, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ta_ApprovalRequest?> PostGetMeta_ApprovalSystemFindByKey(string name, string subjectKey) => engine.FindByKeyAsync(name, subjectKey, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task PostMeta_ApprovalComment(string requestId, string text) => Engine.CommentAsync(requestId, text, AbortToken);
   /// <inheritdoc />
   [GetAction]
   public Task<ApprovalSignerSettings> GetMeta_ApprovalSignerSettings(string name) => Engine.SignerSettingsAsync(name, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task PostMeta_ApprovalSignerSettings(string name, ApprovalSignerSetting[] settings) => Engine.SaveSignerSettingsAsync(name, settings, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ta_ApprovalRequestAttachment> PostGetMeta_ApprovalAttachment(ApprovalAttachmentUpload upload, Stream content) => Engine.AttachAsync(upload, content, AbortToken);
   /// <inheritdoc />
   [GetAction]
   public Task<Stream> GetMeta_ApprovalAttachment(string attachmentId) => Engine.AttachmentAsync(attachmentId, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalYearMonth[]> PostGetMeta_ApprovalYearMonths(ApprovalQuery filter) => Engine.YearMonthsAsync(filter, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalBatchResult> PostGetMeta_ApprovalSystemSignAll(ApprovalSignAllRequest request) => Engine.SignAllAsync(request, AbortToken);
   /// <inheritdoc />
   [PostAction]
   public Task<ApprovalBatchResult> PostGetMeta_ApprovalSystemVoidAll(ApprovalVoidAllRequest request) => Engine.VoidAllAsync(request, AbortToken);
   #endregion
}
