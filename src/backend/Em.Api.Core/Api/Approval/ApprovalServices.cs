using Em.Api.Core.Models;
using Em.Shared;
namespace Em.Api.Core.Approval;
/// <summary>Approval actions; each operation authorizes the caller against its registered definition.</summary>
[Module(Defaults.ApprovalModuleName)]
public sealed class ApprovalServices(IApprovalEngine engine) : ServicesBase, IApprovalServices {
   private ApprovalEngine Engine => (ApprovalEngine)engine;
   #region Meta's
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
   public Task<string> PostGetMeta_ApprovalSystemRequest(string name, string subjectKey, string summary) => engine.RequestAsync(name, subjectKey, summary, AbortToken);
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
   #endregion
}
