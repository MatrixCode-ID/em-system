using System.IO;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Api.Core;

[Module(Defaults.ApprovalModuleName)]
public class ApprovalService(EmApp app) : ServiceMauiBase(app), IApprovalServices
{
   #region Meta's
   public Task<string[]> GetMeta_ApprovalDocumentTypes() => GetAsync<string[]>(nameof(GetMeta_ApprovalDocumentTypes));
   public Task<PagedResult<ApprovalRequestInfo>> GetMeta_ApprovalRequests(ApprovalQuery query) =>
      GetAsync<PagedResult<ApprovalRequestInfo>>(nameof(GetMeta_ApprovalRequests), JsonSerializer.Serialize(query, Defaults.ResponseJsonOptions));
   public Task<ApprovalRequestInfo[]> GetMeta_ApprovalRequestsByDoc(string docType, string docKey) =>
      GetAsync<ApprovalRequestInfo[]>(nameof(GetMeta_ApprovalRequestsByDoc), docType, docKey);
   public Task<ApprovalRequestDetail?> GetMeta_ApprovalRequest(string approvalRequestId) =>
      GetAsync<ApprovalRequestDetail?>(nameof(GetMeta_ApprovalRequest), approvalRequestId);
   public Task<Stream> GetMeta_ApprovalRequestPdf(string approvalRequestId) =>
      GetStreamAsync(nameof(GetMeta_ApprovalRequestPdf), approvalRequestId);
   public Task<ApprovalGuardResult> GetMeta_ApprovalGuard(string approvalRequestId, string stepName) =>
      GetAsync<ApprovalGuardResult>(nameof(GetMeta_ApprovalGuard), approvalRequestId, stepName);
   public Task<ApprovalDecisionResult[]> PostGetMeta_ApprovalDecide(ApprovalDecision[] decisions) =>
      PostAsync<ApprovalDecisionResult[]>(nameof(PostGetMeta_ApprovalDecide), (object)decisions);
   public Task PostMeta_ApprovalCancel(string approvalRequestId, string reason) =>
      PostAsync(nameof(PostMeta_ApprovalCancel), approvalRequestId, reason);
   public Task PostMeta_ApprovalComment(string approvalRequestId, string note) =>
      PostAsync(nameof(PostMeta_ApprovalComment), approvalRequestId, note);
   public Task<HubTaskInfo[]> GetMeta_UserHubTasks() => GetAsync<HubTaskInfo[]>(nameof(GetMeta_UserHubTasks));
   public Task<Stream> GetMeta_ApprovalSlotCalibration(string docType, string docKey, string docVersion) =>
      GetStreamAsync(nameof(GetMeta_ApprovalSlotCalibration), docType, docKey, docVersion);
   #endregion
}
