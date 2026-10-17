using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.Approval;
/// <summary>Common identity guard for all approval participation.</summary>
public sealed class ApprovalAccess(ApiCoreContext context) {
   /// <summary>Requires an active persisted user; built-in system identities cannot participate.</summary>
   public async Task<string> RequireRealUserAsync(ActionRequest caller, CancellationToken ct = default) {
      var id = caller.RequireUserId();
      if (id is Defaults.AdminUserId or Defaults.DebuggerUserId ||
          !await context.ta_Users.AnyAsync(x => x.cUserId == id && x.cUserState == UserState.Active, ct))
         throw new ActionException("Approval requires a real active user. Switch from the built-in system account first.", 403);
      return id;
   }
   /// <summary>Checks a declared claim, including the real-user administrator switch.</summary>
   public static bool HoldsClaim(ActionRequest caller, ClaimAction claim) => caller.IsAdmin ||
      caller.Claims.Any(x => string.Equals(x.Key, claim.Key, StringComparison.OrdinalIgnoreCase));
}
