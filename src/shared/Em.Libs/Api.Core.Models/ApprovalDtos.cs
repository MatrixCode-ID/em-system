namespace Em.Api.Core.Models
{
   /// <summary>
   /// One decision on one step. Sent in an array so several requests can be decided at once, which
   /// matters because almost every request ends up approved.
   /// </summary>
   public class ApprovalDecision
   {
      /// <summary>Request being decided.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>
      /// Step being decided. Required, because one level can hold several steps waiting at the same
      /// time.
      /// </summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary><c>true</c> approves, <c>false</c> rejects.</summary>
      public bool Approve { get; set; }

      /// <summary>
      /// Reason for the decision. May be empty for a plain approval; required when rejecting, when signing
      /// as a substitute, and when overriding a block.
      /// </summary>
      public string? Note { get; set; }

      /// <summary>
      /// Module-owned JSON input for this step, for steps that ask for input. The module validates it; the
      /// engine only passes it on and draws it on the PDF. On rejection the input is drawn when it is
      /// acceptable, but the rejection never depends on it.
      /// </summary>
      public string? Payload { get; set; }

      /// <summary>
      /// Applies the proposal even though the values have changed elsewhere. A deliberate overwrite,
      /// recorded on the entity concerned. Does not apply to entities that no longer exist.
      /// </summary>
      public bool Override { get; set; }

      /// <summary>
      /// Approves even though this step's conditions are not met yet. Only holders of the override claim
      /// declared by the module may use it, and a reason is required.
      /// </summary>
      public bool GuardOverride { get; set; }
   }

   /// <summary>Result of one decision. One result row per decision sent.</summary>
   /// <remarks>
   /// Each decision runs in its own transaction, so one failed decision does not fail the others in the
   /// same batch - which is why the result is a list rather than a single exception.
   /// </remarks>
   public class ApprovalDecisionResult
   {
      /// <summary>Request that was decided.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Step that was decided.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>The decision was recorded successfully.</summary>
      public bool Success { get; set; }

      /// <summary>Why it failed, if it failed.</summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Columns whose values changed elsewhere so the proposal cannot be applied as is. Filled only when
      /// that is the cause of the failure; the screen shows them so the approver can choose to apply
      /// anyway or reject.
      /// </summary>
      public ApprovalConflictField[] Conflicts { get; set; } = [];

      /// <summary>
      /// <c>true</c> when the conflict cannot be overridden - the entity no longer exists, so there is
      /// nothing to apply and rejecting is the only option.
      /// </summary>
      public bool ConflictIsFinal { get; set; }

      /// <summary>State of the request after this decision, so the screen does not need to reload.</summary>
      public ApprovalRequestInfo? Request { get; set; }
   }

   /// <summary>A column whose value changed elsewhere since the request was submitted.</summary>
   public class ApprovalConflictField
   {
      /// <summary>Entity that owns this column.</summary>
      public string Entity { get; set; } = string.Empty;

      /// <summary>Entity key in a user-readable form.</summary>
      public string EntityKey { get; set; } = string.Empty;

      /// <summary>Column name.</summary>
      public string FieldName { get; set; } = string.Empty;

      /// <summary>Value when the request was submitted.</summary>
      public string? OldValue { get; set; }

      /// <summary>Value found now.</summary>
      public string? CurrentValue { get; set; }

      /// <summary>Proposed value.</summary>
      public string? NewValue { get; set; }
   }

   /// <summary>
   /// One row of the request list: the engine's standard columns plus the module's summary, enough to
   /// display and filter without loading the details.
   /// </summary>
   public class ApprovalRequestInfo
   {
      /// <summary>Request ID.</summary>
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Document type.</summary>
      public string DocType { get; set; } = string.Empty;

      /// <summary>
      /// Document key in its canonical form - the same form used when asking for a document's request,
      /// and the one a module splits when it needs the parts.
      /// </summary>
      public string DocKey { get; set; } = string.Empty;

      /// <summary>Document key in a user-readable form.</summary>
      public string DocKeyDisplay { get; set; } = string.Empty;

      /// <summary>Document version.</summary>
      public string DocVersion { get; set; } = string.Empty;

      /// <summary>Whether the proposed data is carried by this request.</summary>
      public ApprovalKind Kind { get; set; }

      /// <summary>Life-cycle stage of this request.</summary>
      public ApprovalStage Stage { get; set; }

      /// <summary>Requester name.</summary>
      public string RequesterName { get; set; } = string.Empty;

      /// <summary>When it was submitted.</summary>
      public DateTime RequestDate { get; set; }

      /// <summary>When it finished, if it has.</summary>
      public DateTime? CompletedDate { get; set; }

      /// <summary>Level currently waiting.</summary>
      public int Level { get; set; }

      /// <summary>Names of the steps waiting for a decision, comma separated when there is more than one.</summary>
      public string WaitingSteps { get; set; } = string.Empty;

      /// <summary>Number of approved steps, out of all applicable steps.</summary>
      public int SignedStepCount { get; set; }

      /// <summary>Number of steps that apply to this request.</summary>
      public int TotalStepCount { get; set; }

      /// <summary>This request is waiting for a decision from the user who asked for this list.</summary>
      public bool WaitingForMe { get; set; }

      /// <summary>
      /// The user who asked for this list may sign the waiting step as a substitute, even though they
      /// are not its recorded signer.
      /// </summary>
      public bool CanSignAsSubstitute { get; set; }

      /// <summary>This request has a document PDF.</summary>
      public bool HasPdf { get; set; }

      /// <summary>How many times this document has been resubmitted for the same version.</summary>
      public int ReinstateCount { get; set; }

      /// <summary>
      /// Decisions on this document type may only be taken after the document has been opened, so the
      /// approval screen does not offer them from the list. Enforced on the screen only - the server does
      /// not record that the document was opened - because the goal is making sure the person actually
      /// sees the document.
      /// </summary>
      public bool RequireOpen { get; set; }

      /// <summary>
      /// Module-owned summary, captured at submission, as a JSON object of column name and value pairs.
      /// The column names are decided by the module, so the screen must not assume they are fixed.
      /// </summary>
      public string? Summary { get; set; }

      /// <summary>
      /// Last action on this request, one of <see cref="ApprovalTimelineAction"/>. Free comments do not
      /// count - this is the last decision or submission.
      /// </summary>
      public string LastAction { get; set; } = string.Empty;

      /// <summary>Name of the actor of the last action.</summary>
      public string LastActorName { get; set; } = string.Empty;

      /// <summary>When the last action happened.</summary>
      public DateTime LastActionDate { get; set; }
   }

   /// <summary>
   /// Action keywords in a request's history, so the screen can map them to icons and text without
   /// guessing from sentences.
   /// </summary>
   public static class ApprovalTimelineAction
   {
      /// <summary>The request was submitted.</summary>
      public const string Submitted = "Submitted";

      /// <summary>The step was approved by its assigned signer.</summary>
      public const string Approved = "Approved";

      /// <summary>The step was approved by a substitute, on behalf of the primary signer.</summary>
      public const string ApprovedAsSubstitute = "ApprovedAsSubstitute";

      /// <summary>The step was approved by overriding its block.</summary>
      public const string ApprovedWithOverride = "ApprovedWithOverride";

      /// <summary>The step was rejected and the request stopped.</summary>
      public const string Rejected = "Rejected";

      /// <summary>The request was withdrawn while still waiting for a decision.</summary>
      public const string Cancelled = "Cancelled";

      /// <summary>The request was withdrawn after all its steps had finished.</summary>
      public const string ReinstatedAfterFinish = "ReinstatedAfterFinish";

      /// <summary>Free comment.</summary>
      public const string Commented = "Commented";
   }

   /// <summary>Details of one request: its steps, proposed changes and history.</summary>
   public class ApprovalRequestDetail
   {
      /// <summary>Summary row of this request.</summary>
      public ApprovalRequestInfo Info { get; set; } = new();

      /// <summary>All steps, ordered by level.</summary>
      public ApprovalStepInfo[] Steps { get; set; } = [];

      /// <summary>
      /// Proposed changes per column, for requests that carry their proposed data. Empty for status gate
      /// requests.
      /// </summary>
      public ApprovalConflictField[] Changes { get; set; } = [];

      /// <summary>Combined history of actions and comments, linked across resubmissions.</summary>
      public ApprovalTimelineEntry[] Timeline { get; set; } = [];
   }

   /// <summary>One step as shown on the screen.</summary>
   public class ApprovalStepInfo
   {
      /// <summary>Step name.</summary>
      public string StepName { get; set; } = string.Empty;

      /// <summary>Level this step belongs to.</summary>
      public int Level { get; set; }

      /// <summary>State of this step.</summary>
      public ApprovalStepStatus Status { get; set; }

      /// <summary>Signer name, once decided.</summary>
      public string? SignerName { get; set; }

      /// <summary>Name of the primary signer represented, when signed by a substitute or an override.</summary>
      public string? OnBehalfName { get; set; }

      /// <summary>On what basis the signature is valid.</summary>
      public ApprovalSignerRole SignerRole { get; set; }

      /// <summary>When it was signed.</summary>
      public DateTime? SignedDate { get; set; }

      /// <summary>Reason for the decision.</summary>
      public string? Note { get; set; }

      /// <summary>Verification code of this signature.</summary>
      public string? VerificationCode { get; set; }

      /// <summary>Names of the people assigned as signers of this step.</summary>
      public string[] AssignedSignerNames { get; set; } = [];

      /// <summary>This step is waiting for a decision from the user who asked for this data.</summary>
      public bool WaitingForMe { get; set; }

      /// <summary>
      /// This step is waiting for a decision and the user who asked for this data holds its claim, but
      /// is not the recorded signer - so they can only sign as a substitute, with a required reason.
      /// </summary>
      public bool CanSignAsSubstitute { get; set; }

      /// <summary>This step asks for input before it can be decided.</summary>
      public bool RequiresInput { get; set; }
   }

   /// <summary>One history row: an action on the request, or a comment.</summary>
   public class ApprovalTimelineEntry
   {
      /// <summary>When it happened.</summary>
      public DateTime Date { get; set; }

      /// <summary>Who did it.</summary>
      public string ActorName { get; set; } = string.Empty;

      /// <summary>What happened, as a keyword the screen can map to its icon and text.</summary>
      public string Action { get; set; } = string.Empty;

      /// <summary>Step concerned, when the action concerns one step.</summary>
      public string? StepName { get; set; }

      /// <summary>Comment text or reason.</summary>
      public string? Note { get; set; }

      /// <summary>
      /// Request this row comes from. May differ from the request being opened, because the history is
      /// linked across resubmissions.
      /// </summary>
      public string cApprovalRequestId { get; set; } = string.Empty;
   }

   /// <summary>Request list filter, sent as is to the server so filtering does not happen on the client.</summary>
   public class ApprovalQuery
   {
      /// <summary>
      /// Only requests waiting for this user's decision. <c>false</c> means every request they may
      /// see.
      /// </summary>
      public bool WaitingForMeOnly { get; set; }

      /// <summary>
      /// Only requests this user can sign as a substitute: they hold the claim of the waiting step, but
      /// that step's signers are assigned per person and they are not one of them. Separate from
      /// <see cref="WaitingForMeOnly"/> because this is not their own work.
      /// </summary>
      public bool CanSignAsSubstituteOnly { get; set; }

      /// <summary>Limits to one document type.</summary>
      public string? DocType { get; set; }

      /// <summary>Limits to one specific document, used by the status panel on the document's screen.</summary>
      public string? DocKey { get; set; }

      /// <summary>Limits to one version of that document.</summary>
      public string? DocVersion { get; set; }

      /// <summary>Limits to specific stages. Empty means all stages.</summary>
      public ApprovalStage[] Stages { get; set; } = [];

      /// <summary>Free-text search over the document key, requester name and module summary.</summary>
      public string? Search { get; set; }

      /// <summary>Requested page, starting at one.</summary>
      public int Page { get; set; } = 1;

      /// <summary>Rows per page.</summary>
      public int PageSize { get; set; } = 50;

      /// <summary>Sort column. Empty means the default order, longest waiting first.</summary>
      public string? SortBy { get; set; }

      /// <summary>Sorts descending.</summary>
      public bool SortDescending { get; set; }
   }

   /// <summary>One page of results plus the total count, so the pager knows how many pages there are.</summary>
   /// <typeparam name="T">Type of the paged rows.</typeparam>
   public class PagedResult<T>
   {
      /// <summary>Rows on this page.</summary>
      public T[] Items { get; set; } = [];

      /// <summary>Total number of rows matching the filter, not only those on this page.</summary>
      public int TotalCount { get; set; }

      /// <summary>Returned page, starting at one.</summary>
      public int Page { get; set; }

      /// <summary>Rows per page used.</summary>
      public int PageSize { get; set; }
   }

   /// <summary>
   /// Result of checking whether a step may be decided now. Re-evaluated every time the screen opens, so
   /// a block whose conditions are now met lifts by itself.
   /// </summary>
   public class ApprovalGuardResult
   {
      /// <summary>This step may be decided.</summary>
      public bool Allowed { get; set; }

      /// <summary>Why it is not allowed yet, shown next to the disabled button.</summary>
      public string? Reason { get; set; }

      /// <summary>
      /// The block may be overridden by holders of a specific claim. The screen uses it to show an
      /// approve button with a required reason.
      /// </summary>
      public bool CanBeOverridden { get; set; }

      /// <summary>
      /// The user who asked for this check holds the override claim. The screen only shows the override
      /// button when this is true.
      /// </summary>
      public bool CallerCanOverride { get; set; }
   }

   /// <summary>
   /// One row of the active user's work list, whatever its source: a long-running job in progress, a
   /// document waiting for their signature, or a data change proposal waiting for their decision.
   /// </summary>
   /// <remarks>
   /// One shape for every source so the list can take new kinds of work without changing. Rows that
   /// mean "action needed" are computed from the data, not stored, so they disappear from everyone
   /// else's list as soon as one person decides.
   /// </remarks>
   public class HubTaskInfo
   {
      /// <summary>Source of this row, used by the screen to group it.</summary>
      public string Source { get; set; } = string.Empty;

      /// <summary>Identifier of this row within its source.</summary>
      public string Id { get; set; } = string.Empty;

      /// <summary>Displayed title.</summary>
      public string Title { get; set; } = string.Empty;

      /// <summary>Short description below the title.</summary>
      public string? Description { get; set; }

      /// <summary>Number of work items this row represents, for rows that summarize several.</summary>
      public int Count { get; set; }

      /// <summary>Age of the oldest work item this row represents.</summary>
      public TimeSpan? OldestAge { get; set; }

      /// <summary>Progress from zero to one hundred, for measurable work.</summary>
      public int? Progress { get; set; }

      /// <summary>Name of the action this row offers, for example opening its screen.</summary>
      public string? ActionName { get; set; }

      /// <summary>Navigation opened by the action, with its filter already applied.</summary>
      public string? NavigationTarget { get; set; }

      /// <summary>Navigation parameter.</summary>
      public string? NavigationParameter { get; set; }
   }
}
