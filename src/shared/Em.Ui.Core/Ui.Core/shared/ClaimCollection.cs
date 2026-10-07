using System.Collections;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// A per-module window onto the claim catalog and the active user's rights - not a container. It has no
   /// list of its own to fill: the module name, the catalog, and the source of rights are all received from
   /// outside when it is formed, and it is formed again every time it is asked for through the
   /// <c>Claims()</c> extension method on <c>IServices</c>. The object is single-use - do not keep it in a
   /// field, since the catalog and rights may change whenever the active user changes.
   /// </summary>
   public class ClaimCollection(string moduleName, IReadOnlyList<ClaimAction> catalog, User? user)
      : IEnumerable<ClaimAction>
   {
      /// <summary>Name of the module this window looks at.</summary>
      public string ModuleName { get; } = moduleName;

      /// <summary>The catalog of this module - used by the management screen to draw its checkbox list.</summary>
      public IEnumerator<ClaimAction> GetEnumerator() =>
         catalog
            .Where(r => string.Equals(r.ModuleName, ModuleName, StringComparison.OrdinalIgnoreCase))
            .GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      /// <summary>
      /// <c>true</c> when the active user may run the claim named <paramref name="name"/> on this module. The
      /// order of checks is binding - see each step.
      /// </summary>
      public bool this[string name] {
         get {
            // 1. Compose the key through ClaimAction - only here may the shape of the key be assembled.
            var key = new ClaimAction { ModuleName = ModuleName, Name = name }.Key;

            // 2. The catalog is not loaded yet (a new session was just opened, RefreshClaimsAsync has not finished) -
            // a conservative answer: a button whose right is not yet clear is disabled first.
            if (catalog.Count == 0) return false;

            // 3. The name must exist in this module's catalog - otherwise it is a programmer's typo, not "no
            // right". Thrown in DEBUG so it is found right away; in release it is answered false, because this
            // indexer is called from XxxCommandAllowed - a path WPF executes constantly, and an exception there
            // would take the application down in front of the user.
            if (!catalog.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))) {
#if DEBUG
               throw new InvalidOperationException(
                  $"Claim '{key}' is not declared by any module. Check the spelling, or declare it with 'AddClaims'.");
#else
               return false;
#endif
            }

            // 4. No active user - false. This is what replaces clearing rights on sign out:
            // SetActiveUser(null) is enough.
            if (user is null) return false;

            // 5. An administrator answers true for every claim that exists in the catalog - placed after step 3,
            // not before it, so a typo is still found even when the one asking is an administrator (e.g. a
            // developer in debug mode).
            if (user.cUserIsAdmin) return true;

            // 6. Otherwise: look up the key among the rights actually granted to this user.
            return user.AvailableClaims.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
         }
      }
   }
}
