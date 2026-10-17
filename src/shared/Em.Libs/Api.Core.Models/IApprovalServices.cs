using System.IO;
using Em.Shared;
namespace Em.Api.Core.Models;
/// <summary>Server-authorized approval actions. Filters and composite keys use POST bodies.</summary>
public interface IApprovalServices : IServices {
   #region Meta's
   /// <summary>Collects the active user's work from every registered task source.</summary>
   Task<HubTaskInfo[]> GetMeta_UserHubTasks();
   /// <summary>GetMeta_ApprovalCapabilities approval operation.</summary>
   Task<ApprovalCapability[]> GetMeta_ApprovalCapabilities();
   /// <summary>PostGetMeta_ApprovalRequests approval operation.</summary>
   Task<ApprovalPage> PostGetMeta_ApprovalRequests(ApprovalQuery filter);
   /// <summary>GetMeta_ApprovalRequest approval operation.</summary>
   Task<ApprovalRequestDetail> GetMeta_ApprovalRequest(string requestId);
   /// <summary>PostGetMeta_ApprovalRequestsBySubject approval operation.</summary>
   Task<ta_ApprovalRequest[]> PostGetMeta_ApprovalRequestsBySubject(ApprovalKind kind, string name, string subjectKey);
   /// <summary>PostGetMeta_ApprovalDecide approval operation.</summary>
   Task<ApprovalDecisionResult> PostGetMeta_ApprovalDecide(ApprovalDecision decision);
   /// <summary>PostMeta_ApprovalWithdraw approval operation.</summary>
   Task PostMeta_ApprovalWithdraw(string requestId, string reason);
   /// <summary>PostGetMeta_ApprovalSystemRequest approval operation.</summary>
   Task<string> PostGetMeta_ApprovalSystemRequest(string name, string subjectKey, string summary);
   /// <summary>PostGetMeta_ApprovalSystemReinstate approval operation.</summary>
   Task<string> PostGetMeta_ApprovalSystemReinstate(string requestId, string summary, string reason);
   /// <summary>PostMeta_ApprovalSystemVoid approval operation.</summary>
   Task PostMeta_ApprovalSystemVoid(string requestId, string reason);
   /// <summary>PostGetMeta_ApprovalSystemFindByKey approval operation.</summary>
   Task<ta_ApprovalRequest?> PostGetMeta_ApprovalSystemFindByKey(string name, string subjectKey);
   /// <summary>PostMeta_ApprovalComment approval operation.</summary>
   Task PostMeta_ApprovalComment(string requestId, string text);
   /// <summary>GetMeta_ApprovalSignerSettings approval operation.</summary>
   Task<ApprovalSignerSettings> GetMeta_ApprovalSignerSettings(string name);
   /// <summary>PostMeta_ApprovalSignerSettings approval operation.</summary>
   Task PostMeta_ApprovalSignerSettings(string name, ApprovalSignerSetting[] settings);
   /// <summary>PostGetMeta_ApprovalAttachment approval operation.</summary>
   Task<ta_ApprovalRequestAttachment> PostGetMeta_ApprovalAttachment(ApprovalAttachmentUpload upload, Stream content);
   /// <summary>GetMeta_ApprovalAttachment approval operation.</summary>
   Task<Stream> GetMeta_ApprovalAttachment(string attachmentId);
   #endregion
}
