using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// One place for the question "which rights apply to this user right now". Used by the gate in
   /// <c>EmApp.ProcessRequest</c> and by the two actions that show it on screen, so the time window rule -
   /// since when it applies, until when, and roles that are no longer active - is written only once.
   /// Written twice, the two would differ as soon as one of them is fixed.
   /// </summary>
   internal static class UserClaimLoader
   {
      /// <summary>
      /// Rights granted directly to the user, whose validity period is running at
      /// <paramref name="now"/>.
      /// </summary>
      /// <remarks>
      /// Not filtered against the claim catalog: an orphaned grant - a right removed from code whose row
      /// remains - must stay visible, because only there can it be recognized and revoked. For the gate this
      /// does no harm: an orphaned right matches no action.
      /// </remarks>
      public static IQueryable<string> QueryDirectNames(ApiCoreContext ctx, string cUserId, DateTime now) =>
         ctx.ta_UserClaims
            .Where(r => r.cUserId == cUserId
                        && r.cUserClaimStart <= now
                        && r.cUserClaimExpiry >= now)
            .Select(r => r.cUserClaimName);

      /// <summary>
      /// Rights that come through roles: only from active roles, and only while the membership is running at
      /// <paramref name="now"/>. An empty membership time limit means no limit from that side.
      /// </summary>
      public static IQueryable<string> QueryRoleNames(ApiCoreContext ctx, string cUserId, DateTime now) =>
         from assignment in ctx.ta_UserRoles
         join role in ctx.ta_Roles on assignment.cRoleId equals role.cRoleId
         join claim in ctx.ta_RoleClaims on role.cRoleId equals claim.cRoleId
         where assignment.cUserId == cUserId
               && role.cRoleState == RoleState.Active
               && (assignment.cUserRoleStart == null || assignment.cUserRoleStart <= now)
               && (assignment.cUserRoleExpiry == null || assignment.cUserRoleExpiry >= now)
         select claim.cClaimName;

      /// <summary>
      /// The union of both in a single trip to the database - this is the shape the gate needs, because the
      /// gate does not care whether a right came directly or through a role.
      /// </summary>
      public static async Task<ClaimAction[]> LoadAsync(ApiCoreContext ctx, string cUserId, DateTime now,
         CancellationToken ct) {
         var names = await QueryDirectNames(ctx, cUserId, now)
            .Concat(QueryRoleNames(ctx, cUserId, now))
            .Distinct()
            .ToArrayAsync(ct);

         return [.. names.Select(ClaimAction.FromKey)];
      }
   }
}
