using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;
namespace Em.Api.Core;
/// <summary>Client proxy for approval actions.</summary>
[Module(Defaults.ApprovalModuleName)]
public class ApprovalService(EmApp app) : ServiceWpfBase(app), IApprovalServices {
   /// <inheritdoc />
   public Task<ApprovalCapability[]> GetMeta_ApprovalCapabilities() => GetAsync<ApprovalCapability[]>(nameof(GetMeta_ApprovalCapabilities));
   /// <inheritdoc />
   public Task<ApprovalPage> PostGetMeta_ApprovalRequests(ApprovalQuery filter) => PostAsync<ApprovalPage>(nameof(PostGetMeta_ApprovalRequests), (object)filter);
   /// <inheritdoc />
   public Task<ApprovalRequestDetail> GetMeta_ApprovalRequest(string requestId) => GetAsync<ApprovalRequestDetail>(nameof(GetMeta_ApprovalRequest), requestId);
   /// <inheritdoc />
   public Task<ta_ApprovalRequest[]> PostGetMeta_ApprovalRequestsBySubject(ApprovalKind kind, string name, string subjectKey) => PostAsync<ta_ApprovalRequest[]>(nameof(PostGetMeta_ApprovalRequestsBySubject), kind, name, subjectKey);
   /// <inheritdoc />
   public Task<ApprovalDecisionResult> PostGetMeta_ApprovalDecide(ApprovalDecision decision) => PostAsync<ApprovalDecisionResult>(nameof(PostGetMeta_ApprovalDecide), decision);
   /// <inheritdoc />
   public Task PostMeta_ApprovalWithdraw(string requestId, string reason) => PostAsync(nameof(PostMeta_ApprovalWithdraw), requestId, reason);
   /// <inheritdoc />
   public Task<string> PostGetMeta_ApprovalSystemRequest(string name, string subjectKey, string summary) => PostAsync<string>(nameof(PostGetMeta_ApprovalSystemRequest), name, subjectKey, summary);
   /// <inheritdoc />
   public Task<string> PostGetMeta_ApprovalSystemReinstate(string requestId, string summary, string reason) => PostAsync<string>(nameof(PostGetMeta_ApprovalSystemReinstate), requestId, summary, reason);
   /// <inheritdoc />
   public Task PostMeta_ApprovalSystemVoid(string requestId, string reason) => PostAsync(nameof(PostMeta_ApprovalSystemVoid), requestId, reason);
   /// <inheritdoc />
   public Task<ta_ApprovalRequest?> PostGetMeta_ApprovalSystemFindByKey(string name, string subjectKey) => PostAsync<ta_ApprovalRequest?>(nameof(PostGetMeta_ApprovalSystemFindByKey), name, subjectKey);
   /// <inheritdoc />
   public Task PostMeta_ApprovalComment(string requestId, string text) => PostAsync(nameof(PostMeta_ApprovalComment), requestId, text);
   /// <inheritdoc />
   public Task<ApprovalSignerSettings> GetMeta_ApprovalSignerSettings(string name) => GetAsync<ApprovalSignerSettings>(nameof(GetMeta_ApprovalSignerSettings), name);
   /// <inheritdoc />
   public Task PostMeta_ApprovalSignerSettings(string name, ApprovalSignerSetting[] settings) => PostAsync(nameof(PostMeta_ApprovalSignerSettings), name, settings);
   /// <inheritdoc />
   public Task<ta_ApprovalRequestAttachment> PostGetMeta_ApprovalAttachment(ApprovalAttachmentUpload upload, Stream content) => PostStreamAsync<ta_ApprovalRequestAttachment>(nameof(PostGetMeta_ApprovalAttachment), content, upload);
   /// <inheritdoc />
   public Task<Stream> GetMeta_ApprovalAttachment(string attachmentId) => GetStreamAsync(nameof(GetMeta_ApprovalAttachment), attachmentId);
}
