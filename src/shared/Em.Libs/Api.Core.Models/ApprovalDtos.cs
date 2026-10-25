namespace Em.Api.Core.Models;
/// <summary>Caller capabilities for one registered approval.</summary>
public sealed record ApprovalCapability(ApprovalKind Kind, string Name, string Title, bool CanView,
   bool CanDecide, int WaitingCount, string[] ApproverOnlyColumns, ApprovalSystemStep[] Steps, bool AllowBatchSign = true);
/// <summary>Paged approval filter. Complex filters are sent only by POST.</summary>
public sealed record ApprovalQuery(ApprovalKind? Kind = null, string? Name = null, string? SubjectKey = null,
   ApprovalStage? Stage = null, bool WaitingForMeOnly = false, string? Search = null, int Page = 1, int PageSize = 50, DateTime? DateFrom = null, DateTime? DateTo = null, bool OldestFirst = false);
/// <summary>One filtered page.</summary>
public sealed record ApprovalPage(ta_ApprovalRequest[] Items, int Total, int Page, int PageSize);
/// <summary>Server-authorized detail including the request snapshot and live conflict values.</summary>
public sealed record ApprovalRequestDetail(ta_ApprovalRequest Header, ta_ApprovalRequestItem[] Items,
   ta_ApprovalRequestItemField[] Fields, ta_ApprovalRequestStep[] Steps,
   ta_ApprovalRequestStepCandidate[] Candidates, ta_ApprovalRequestSignature[] Signatures,
   ta_ApprovalRequestComment[] Comments, ta_ApprovalRequestAttachment[] Attachments,
   bool CanDecide, bool CanWithdraw, bool CanVoid, bool CanReinstate, bool CanAttach,
   string[] DecidableStepIds, ApprovalSignerUser[] Users);
/// <summary>Outcome of a module's Data submission.</summary>
public sealed record ApprovalSubmitResult(string ApprovalRequestId, bool AppliedImmediately);
/// <summary>One explicit conflict override.</summary>
public sealed record ApprovalFieldOverride(string ItemId, string Column, string Reason);
/// <summary>A decision applies to the whole Data tree or exactly one active System step.</summary>
public sealed record ApprovalDecision(string RequestId, bool Approve, string? StepId = null,
   string? Note = null, ApprovalFieldOverride[]? Overrides = null);
/// <summary>Outcome of a decision; conflicts remain inspectable without applying partial changes.</summary>
public sealed record ApprovalDecisionResult(bool Applied, string? Message = null);
/// <summary>Configured primary signer and ordered four-eyes backups for one step.</summary>
public sealed record ApprovalSignerSetting(string StepName, bool Dynamic, string? UserId, string[] Backups);
/// <summary>Metadata accompanying a raw streamed attachment upload.</summary>
public sealed record ApprovalAttachmentUpload(string RequestId, string? CommentId, string Name);
/// <summary>User offered in the signer pool; inactive users remain visible but cannot be assigned.</summary>
public sealed record ApprovalSignerUser(string UserId, string Name, bool Active);
/// <summary>Settings snapshot and available users for one System definition.</summary>
public sealed record ApprovalSignerSettings(string Name, ApprovalSystemStep[] Steps,
   ApprovalSignerSetting[] Settings, ApprovalSignerUser[] Users);

/// <summary>Visible request count in one calendar month.</summary>
public sealed record ApprovalYearMonth(int Year, int Month, int Count);
/// <summary>Candidate signatures or administrative signatures on the captured active level.</summary>
public enum ApprovalSignAllScope {
   /// <summary>Only active steps for which the caller is a candidate.</summary>
   Mine,
   /// <summary>All captured active steps; requires a persisted administrator.</summary>
   AllActiveSteps
}
/// <summary>Signs captured active System steps; a null ID list discovers the caller's work.</summary>
public sealed record ApprovalSignAllRequest(string? Name, string[]? RequestIds, ApprovalSignAllScope Scope, string? Note);
/// <summary>Voids explicitly selected System requests with a history reason.</summary>
public sealed record ApprovalVoidAllRequest(string[] RequestIds, string Reason);
/// <summary>Outcome of one captured step or request.</summary>
public sealed record ApprovalBatchItem(string RequestId, string? StepId, bool Success, string? Message);
/// <summary>Independent per-request outcomes; partial success is allowed.</summary>
public sealed record ApprovalBatchResult(ApprovalBatchItem[] Items);
/// <summary>Decision applied independently to explicitly selected Data requests.</summary>
public enum ApprovalDataBatchAction {
   /// <summary>Apply proposals without overriding conflicts.</summary>
   Approve,
   /// <summary>Reject proposals with a reason.</summary>
   Reject,
   /// <summary>Withdraw proposals with a reason.</summary>
   Withdraw
}
/// <summary>Explicit Data selection, limited to 200 IDs; no server discovery or conflict overrides.</summary>
public sealed record ApprovalDataBatchRequest(string Name, string[] RequestIds, ApprovalDataBatchAction Action, string? Note);
/// <summary>Decision validation shared by server and clients.</summary>
public static class ApprovalRules {
   /// <summary>Minimum trimmed reason length for every System void, including reinstate.</summary>
   public const int MinVoidReasonLength = 15;
}
