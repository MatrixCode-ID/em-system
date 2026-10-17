namespace Em.Api.Core.Models;
/// <summary>Caller capabilities for one registered approval.</summary>
public sealed record ApprovalCapability(ApprovalKind Kind, string Name, string Title, bool CanView,
   bool CanDecide, int WaitingCount, string[] ApproverOnlyColumns, ApprovalSystemStep[] Steps);
/// <summary>Paged approval filter. Complex filters are sent only by POST.</summary>
public sealed record ApprovalQuery(ApprovalKind? Kind = null, string? Name = null, string? SubjectKey = null,
   ApprovalStage? Stage = null, bool WaitingForMeOnly = false, string? Search = null, int Page = 1, int PageSize = 50);
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
