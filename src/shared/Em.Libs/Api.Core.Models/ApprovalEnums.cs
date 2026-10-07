namespace Em.Api.Core.Models
{
   /// <summary>Approval kind: whether the proposed data is carried by the request or already lives in the document.</summary>
   public enum ApprovalKind
   {
      /// <summary>
      /// The proposed data lives in the request and is applied only after approval. Used for master data
      /// changes, where the original table must not change while the request is waiting.
      /// </summary>
      Data = 1,

      /// <summary>
      /// The data already lives in the document, and the request only gates its status. Used for
      /// transaction documents that need signatures from several parties.
      /// </summary>
      Document = 2
   }

   /// <summary>
   /// Life-cycle stage of an approval request. Follows the stage column convention: negative values mean
   /// the request no longer runs, zero and above mean it is alive or finished successfully.
   /// </summary>
   public enum ApprovalStage
   {
      /// <summary>Withdrawn after all its steps had finished. Kept as history.</summary>
      ReinstatedAfterFinish = -3,

      /// <summary>Withdrawn while still waiting for a decision.</summary>
      Cancelled = -2,

      /// <summary>Rejected at one of its steps. The whole request stops.</summary>
      Rejected = -1,

      /// <summary>Not submitted yet.</summary>
      Draft = 0,

      /// <summary>Submitted and still waiting for a decision.</summary>
      Pending = 1,

      /// <summary>All its steps have been approved.</summary>
      Approved = 2
   }

   /// <summary>
   /// State of one step within a request. Follows the same convention: negative means the step produced
   /// no signature.
   /// </summary>
   public enum ApprovalStepStatus
   {
      /// <summary>Skipped because its applicability condition was not met, or because the request stopped first.</summary>
      Skipped = -2,

      /// <summary>Rejected.</summary>
      Rejected = -1,

      /// <summary>Still waiting for a decision.</summary>
      Waiting = 0,

      /// <summary>Approved and signed.</summary>
      Approved = 1
   }

   /// <summary>What is proposed for one entity within a data approval request.</summary>
   public enum ApprovalItemOperation
   {
      /// <summary>Creates a new entity.</summary>
      Create = 1,

      /// <summary>Changes some columns of an existing entity.</summary>
      Update = 2,

      /// <summary>Marks the entity as deleted without removing it.</summary>
      Delete = 3,

      /// <summary>Reactivates an entity previously marked as deleted.</summary>
      Reinstate = 4
   }

   /// <summary>
   /// Result of applying one proposed entity, recorded after the request is approved. This is not the
   /// approver's decision - a decision always applies to the whole request - but what actually happened
   /// to that entity.
   /// </summary>
   public enum ApprovalItemStage
   {
      /// <summary>Could not be applied because the value changed elsewhere and overwriting was not allowed.</summary>
      Conflicted = -1,

      /// <summary>Not applied yet.</summary>
      Pending = 0,

      /// <summary>Applied as is.</summary>
      Applied = 1,

      /// <summary>Skipped because the value already equals the proposal.</summary>
      Skipped = 2,

      /// <summary>Applied even though the value had changed elsewhere, by the approver's deliberate decision.</summary>
      Overridden = 3
   }

   /// <summary>Why a signature is considered valid, and on what basis it was given.</summary>
   public enum ApprovalSignerRole
   {
      /// <summary>The signer assigned to that step.</summary>
      Assigned = 0,

      /// <summary>
      /// Substitute: a holder of the step's claim who is not the recorded signer. The signature is
      /// recorded on behalf of the primary signer, and a reason is required.
      /// </summary>
      Substitute = 1,

      /// <summary>
      /// Block override: a holder of the override claim who signs even though the step's conditions are
      /// not met yet. It is an emergency measure, so the signature is specially marked.
      /// </summary>
      Override = 2
   }
}
