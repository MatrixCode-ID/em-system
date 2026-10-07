using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// A module's entry point to the approval engine: submit, make sure a document is not awaiting a
   /// decision, and save directly for holders of the approval claim.
   /// </summary>
   /// <remarks>
   /// Deciding a step is <b>not</b> here: that is the same engine action for every document type, so one
   /// screen can decide many requests without knowing the owning module.
   /// </remarks>
   public interface IApprovalEngine
   {
      /// <summary>
      /// Submits a document for approval.
      /// </summary>
      /// <typeparam name="TKey">Key record of the document.</typeparam>
      /// <param name="docType">The document type, as declared by the module.</param>
      /// <param name="docKey">Key of the document being submitted.</param>
      /// <param name="docVersion">Version of the document being submitted.</param>
      /// <param name="note">The submitter's note, optional.</param>
      /// <returns>Id of the request that was created.</returns>
      /// <remarks>
      /// What the engine does, in order: makes sure the submitter is a real, active user, checks the claim of
      /// the first step, refuses when that document and version already has a waiting request, determines
      /// <b>all</b> signers at once, creates the base PDF and stores it outside the transaction, then in one
      /// transaction writes the header, all steps with a copy of their box positions, the signer list, the
      /// automatic signature of the first step by the submitter, and advances to the next level.
      /// <para>
      /// All signers are determined up front on purpose: errors surface to the submitter, who can fix them,
      /// rather than failing someone else's decision midway through the flow. This is safe because the
      /// document is locked while the request runs.
      /// </para>
      /// </remarks>
      Task<string> SubmitAsync<TKey>(string docType, TKey docKey, string docVersion, string? note = null)
         where TKey : notnull;

      /// <summary>
      /// Submits a data change proposal, or saves it directly when the caller holds the approval claim of
      /// that document type.
      /// </summary>
      /// <typeparam name="TKey">Key record of the document.</typeparam>
      /// <param name="docType">The document type.</param>
      /// <param name="docKey">Key of the document being changed.</param>
      /// <param name="items">The change proposals per entity, with old and new values.</param>
      /// <param name="note">The submitter's note, optional.</param>
      /// <returns>The result: the request id, and whether it was applied immediately.</returns>
      /// <remarks>
      /// Holders of the approval claim do not need to go through the approval screen for their own changes -
      /// their changes are applied directly, but still recorded as an automatically approved request, so the
      /// audit trail is just as complete. The old-value check still runs for them, so they are also
      /// protected from unknowingly overwriting someone else's change: if the table content has changed since
      /// their screen was opened, nothing is saved and they get a 409 naming the column.
      /// <para>
      /// Columns whose new value equals the old value are dropped, and entities left with no columns are
      /// dropped too; if nothing at all remains, 400. Several waiting requests for the same document may
      /// exist at once: each request has its own version marker, which is its id.
      /// </para>
      /// <para>
      /// A new entity uses a temporary key. The key the module returns when applying it replaces that
      /// temporary key - in the entity itself, in other entities of the same request that contain the same
      /// temporary key part, and in the request's document name when the document is that entity itself. That
      /// way a new parent and its children can be submitted in one request.
      /// </para>
      /// </remarks>
      /// <exception cref="Em.Shared.ActionException">
      /// 403 when the caller is a system account; 400 when nothing changed; 409 when the caller holds the
      /// approval claim and the table content has changed since their screen was opened.
      /// </exception>
      Task<ApprovalSubmitResult> SubmitDataAsync<TKey>(string docType, TKey docKey,
         IReadOnlyList<ApprovalDataItem> items, string? note = null)
         where TKey : notnull;

      /// <summary>
      /// Makes sure a document is not awaiting a decision. Called by a module's save action before writing
      /// anything.
      /// </summary>
      /// <typeparam name="TKey">Key record of the document.</typeparam>
      /// <param name="docType">The document type.</param>
      /// <param name="docKey">Key of the document about to be written.</param>
      /// <param name="docVersion">Version of the document about to be written, when the version also decides.</param>
      /// <exception cref="Em.Shared.ActionException">
      /// Thrown when the document is awaiting a decision. This is what locks the document while approval
      /// runs - the engine writes no marker to the module's tables for it.
      /// </exception>
      Task EnsureNotInApprovalAsync<TKey>(string docType, TKey docKey, string? docVersion = null)
         where TKey : notnull;

      /// <summary>
      /// Withdraws a request, then prepares a resubmission for the same document and version.
      /// </summary>
      /// <param name="approvalRequestId">The request being withdrawn.</param>
      /// <param name="reason">Reason for the withdrawal. Required.</param>
      /// <remarks>
      /// Withdrawing and resubmitting are two separate steps: the document is opened first, edited, then
      /// submitted again. The new request stores a link to the withdrawn one, so the history stays
      /// connected. A request that has already completed in full may also be withdrawn; in that case the
      /// owning module is given a chance to revoke the status that was written, and may refuse the
      /// withdrawal if the document has been processed further.
      /// <para>
      /// The resubmission uses the same <see cref="SubmitAsync{TKey}"/>: when the latest request for that
      /// document and version is the one just withdrawn, the engine links the new request to it without being
      /// asked. Those who may withdraw are holders of the first step's claim and users whose administrator
      /// switch is on.
      /// </para>
      /// </remarks>
      Task ReinstateAsync(string approvalRequestId, string reason);
   }

   /// <summary>Result of submitting a data change proposal.</summary>
   /// <param name="ApprovalRequestId">Id of the request that was created.</param>
   /// <param name="AppliedImmediately">
   /// <c>true</c> when the change was applied directly because the submitter holds the approval claim.
   /// The screen uses it to choose the right message: "submitted for approval" or "saved".
   /// </param>
   public record ApprovalSubmitResult(string ApprovalRequestId, bool AppliedImmediately);

   /// <summary>One entity proposed for change, with its columns.</summary>
   /// <param name="Entity">
   /// The entity touched, written with its module name as a prefix so it does not collide across modules.
   /// </param>
   /// <param name="Key">Key of the entity, as a record with key parts.</param>
   /// <param name="Operation">What is proposed for this entity.</param>
   /// <param name="Fields">The columns proposed to change. Empty for deletion and reactivation.</param>
   public record ApprovalDataItem(string Entity, object Key, ApprovalItemOperation Operation,
      IReadOnlyList<ApprovalDataField> Fields);

   /// <summary>One column proposed for change, with its value before and after.</summary>
   /// <param name="Name">Name of the column, as understood by the module's handler.</param>
   /// <param name="OldValue">
   /// Its value when the edit screen was loaded. This is what makes the conflict check work as a safeguard
   /// against someone else's change, so do not fill it with a value that was just read.
   /// </param>
   /// <param name="NewValue">The proposed value.</param>
   public record ApprovalDataField(string Name, string? OldValue, string? NewValue);
}
