using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Approval rights checker: who may view a request, who may decide its step, and who may not sign
   /// anything at all.
   /// </summary>
   /// <remarks>
   /// Separated from the approval service so the rules are written once and used by every action that
   /// needs them - list, detail, PDF, decision, withdrawal, comments.
   /// <para>
   /// Approval is an exception to the rule "administrators can do everything" in one respect: the
   /// built-in administrator account and the debugger account <b>cannot sign</b>, because a signature must
   /// point to a person who really exists in the user list. Developers test it by switching to a real
   /// user. Conversely, a real user whose administrator switch is on is still allowed - that switch acts
   /// like holding every step right.
   /// </para>
   /// </remarks>
   public class ApprovalAccess(ApiCoreContext ctx)
   {
      /// <summary>
      /// Makes sure the caller may place a signature, then returns their id.
      /// </summary>
      /// <param name="request">Info about the request being handled.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <returns>The caller's user id.</returns>
      /// <exception cref="ActionException">
      /// 401 when no caller is proven; 403 when the caller is a system account - the built-in administrator
      /// account or the debugger account - or when the user does not exist or is no longer active.
      /// </exception>
      public async Task<string> RequireRealUserAsync(ActionRequest request,
         CancellationToken cancellationToken = default) {
         var userId = request.RequireUserId();

         // The two system accounts carry ids that are deliberately not valid row ids, so there is no
         // row for them to point at - which is also what makes the database refuse their signature.
         // Saying so here turns that refusal into an answer the caller can read.
         if (userId is Defaults.AdminUserId or Defaults.DebuggerUserId) {
            throw new ActionException(
               "A built-in system account cannot take part in an approval, because a signature has to name a real user. " +
               "Switch to a real user first.", 403);
         }

         var isActive = await ctx.ta_Users
            .Where(r => r.cUserId == userId && r.cUserState == UserState.Active)
            .AnyAsync(cancellationToken);

         if (!isActive) {
            throw new ActionException("The calling user is not an active user, so it cannot take part in an approval.",
               403);
         }

         return userId;
      }

      /// <summary>
      /// Whether the caller holds a right. The order is the same as the rights check at the gate - developer
      /// path, then administrator switch, then granted rights - so one rule is not checked in two different
      /// ways.
      /// </summary>
      /// <param name="request">Info about the request being handled.</param>
      /// <param name="claim">The right being checked.</param>
      public static bool HoldsClaim(ActionRequest request, ClaimAction claim) =>
         request.IsDebugRequest || request.IsAdmin ||
         request.Claims.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));

      /// <summary>
      /// Whether the caller may view the requests of a document type: holders of any step right in its flow
      /// - including the submitter, who holds the first step's right - holders of the document type's reader
      /// right, and users whose administrator switch is on.
      /// </summary>
      /// <param name="request">Info about the request being handled.</param>
      /// <param name="flow">The flow of the document type being viewed.</param>
      public static bool CanView(ActionRequest request, ApprovalFlowDeclaration flow) =>
         flow.Claims.Any(r => HoldsClaim(request, r));

      /// <summary>
      /// Makes sure the caller may view the requests of a document type. The right to comment equals the
      /// right to view, so this check is also the one used before a comment is written.
      /// </summary>
      /// <param name="request">Info about the request being handled.</param>
      /// <param name="flow">The flow of the document type being viewed.</param>
      /// <exception cref="ActionException">
      /// 401 when no caller is proven; 403 when they hold none of the rights in that flow.
      /// </exception>
      public static void RequireView(ActionRequest request, ApprovalFlowDeclaration flow) {
         request.RequireUserId();

         if (!CanView(request, flow)) {
            throw new ActionException(
               $"You are not allowed to see approval requests of document type '{flow.DocType}'.", 403);
         }
      }

      /// <summary>
      /// All document types the caller may view. Used by the request list, so its filter is limited on the
      /// server - not by reading everything and then discarding what may not be seen.
      /// </summary>
      /// <param name="request">Info about the request being handled.</param>
      /// <param name="registry">Catalog of all registered flows.</param>
      public static IReadOnlyList<string> ViewableDocTypes(ActionRequest request, ApprovalRegistry registry) =>
         [.. registry.Flows.Where(r => CanView(request, r)).Select(r => r.DocType)];
   }
}
