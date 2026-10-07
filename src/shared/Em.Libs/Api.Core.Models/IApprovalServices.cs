using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// The engine's approval contract: viewing requests, deciding them, withdrawing them, commenting, and
   /// fetching their PDF.
   /// </summary>
   /// <remarks>
   /// Decisions are engine actions, not module actions: one action for every document type, so the
   /// approval screen can decide many requests at once without knowing which module owns them. Modules
   /// provide what only they understand - how to submit, how to load and apply their data, and the input
   /// of each step.
   /// <para>
   /// Who may view a request: holders of the claim of any step in that document type's flow (including
   /// its requester), holders of that document type's view claim, and users whose administrator switch is
   /// on. The right to comment equals the right to view. The same check applies to every read action
   /// here, including the PDF.
   /// </para>
   /// <para>
   /// Approval is an exception to the "administrator can do everything" rule in one respect: the built-in
   /// administrator account and the debugger account <b>cannot</b> sign, because a signature must point to
   /// a real user. The debugger tests it by switching to a real user.
   /// </para>
   /// </remarks>
   public interface IApprovalServices : IServices
   {
      #region Meta's

      /// <summary>Approval document types the caller may view, including those without any request yet.</summary>
      Task<string[]> GetMeta_ApprovalDocumentTypes();

      /// <summary>
      /// Requests the caller may view, filtered and paged on the server.
      /// </summary>
      /// <param name="query">Filter, sort order and requested page.</param>
      Task<PagedResult<ApprovalRequestInfo>> GetMeta_ApprovalRequests(ApprovalQuery query);

      /// <summary>
      /// Every request for one document, including finished, rejected and withdrawn ones. Used by the
      /// approval status panel on the document's screen, and by the client to fetch the latest state after
      /// submitting.
      /// </summary>
      /// <param name="docType">Document type.</param>
      /// <param name="docKey">Document key in canonical form.</param>
      Task<ApprovalRequestInfo[]> GetMeta_ApprovalRequestsByDoc(string docType, string docKey);

      /// <summary>Details of one request: steps, proposed changes and history.</summary>
      /// <param name="approvalRequestId">Requested request.</param>
      Task<ApprovalRequestDetail?> GetMeta_ApprovalRequest(string approvalRequestId);

      /// <summary>
      /// The document PDF of a request, with the signatures applied so far and each step's input. Built on
      /// request from the base PDF frozen at submission, so it always reflects the latest state without
      /// storing one file per decision.
      /// </summary>
      /// <param name="approvalRequestId">Request whose PDF is requested.</param>
      /// <returns>The PDF content.</returns>
      Task<Stream> GetMeta_ApprovalRequestPdf(string approvalRequestId);

      /// <summary>
      /// Checks whether a step may be decided now. Called again every time the screen opens or reloads,
      /// because the conditions are read from the current state - a block whose conditions are now met
      /// lifts by itself.
      /// </summary>
      /// <param name="approvalRequestId">Request to check.</param>
      /// <param name="stepName">Step to check.</param>
      Task<ApprovalGuardResult> GetMeta_ApprovalGuard(string approvalRequestId, string stepName);

      /// <summary>
      /// Decides one step or several steps at once. Each decision runs in its own transaction, so one
      /// failure does not fail the others - which is why the result is a list, one row per decision.
      /// </summary>
      /// <param name="decisions">Decisions taken.</param>
      Task<ApprovalDecisionResult[]> PostGetMeta_ApprovalDecide(ApprovalDecision[] decisions);

      /// <summary>
      /// Withdraws a request so its document can be edited again. Also allowed for a fully finished
      /// request; in that case the owning module gets the chance to revoke the status already written, and
      /// may refuse when the document has been processed further.
      /// </summary>
      /// <param name="approvalRequestId">Request to withdraw.</param>
      /// <param name="reason">Reason for the withdrawal. Required.</param>
      Task PostMeta_ApprovalCancel(string approvalRequestId, string reason);

      /// <summary>Writes a free comment on a request.</summary>
      /// <param name="approvalRequestId">Request to comment on.</param>
      /// <param name="note">Comment text.</param>
      Task PostMeta_ApprovalComment(string approvalRequestId, string note);

      /// <summary>
      /// The active user's complete work list, collected from every registered source: long-running jobs
      /// in progress, documents waiting for their signature, and data change proposals waiting for their
      /// decision.
      /// </summary>
      Task<HubTaskInfo[]> GetMeta_UserHubTasks();

      /// <summary>
      /// Sample PDF with signature boxes and input boxes drawn with their names, for measuring their
      /// position against the actual document layout. Developer tool: only for the debugger and users
      /// whose administrator switch is on.
      /// </summary>
      /// <param name="docType">Document type whose boxes are drawn.</param>
      /// <param name="docKey">One real document used as the drawing base.</param>
      /// <param name="docVersion">Version of that document.</param>
      /// <returns>The sample PDF content.</returns>
      Task<Stream> GetMeta_ApprovalSlotCalibration(string docType, string docKey, string docVersion);

      #endregion
   }
}
