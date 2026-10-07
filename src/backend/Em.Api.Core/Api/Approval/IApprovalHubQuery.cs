using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// The question "what is waiting for the decision of the user who is calling", answered directly from
   /// the approval tables without loading the documents. Used by the task list source in the application
   /// title bar.
   /// </summary>
   /// <remarks>
   /// The result is already grouped - by approval kind, then by document type - because that is the shape
   /// the task list shows: one row per document type, with the count and age of the oldest request, not
   /// one row per request.
   /// <para>
   /// Only requests that are truly waiting for that person are counted: a step in the level that is
   /// currently running, and - for a step whose signer is fixed per person - only when they are recorded
   /// as its signer. Holders of other claims do not see it here; they find it on the approval screen.
   /// </para>
   /// </remarks>
   public interface IApprovalHubQuery
   {
      /// <summary>
      /// Summary of the requests waiting for the decision of the caller of the running request, or an empty
      /// list when there are none.
      /// </summary>
      /// <remarks>
      /// The caller is read from the request, not given as a parameter: what is waiting for them is decided
      /// by the rights they hold right now, and that is only known through their request. The built-in system
      /// accounts never have anything waiting.
      /// </remarks>
      /// <param name="cancellationToken">Cancellation token.</param>
      Task<IReadOnlyList<ApprovalHubGroup>> GetWaitingForCallerAsync(CancellationToken cancellationToken = default);
   }

   /// <summary>
   /// One row of the approval task list: one document type under one approval kind, together with the
   /// count and age of its oldest request.
   /// </summary>
   /// <param name="Kind">The approval kind, which decides in which part this row appears.</param>
   /// <param name="DocType">The document type.</param>
   /// <param name="Count">How many requests of that document type are waiting for this user.</param>
   /// <param name="OldestRequestDate">
   /// When the oldest of them was submitted. The task list computes its age from this.
   /// </param>
   public record ApprovalHubGroup(ApprovalKind Kind, string DocType, int Count, DateTime OldestRequestDate);
}
