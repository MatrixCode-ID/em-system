using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// The single rule "may it be opened or not" for a navigation, used both by the home menu and by
   /// <c>NavigateTo</c> - so what is hidden in the menu and what is refused when opened directly never
   /// differ. Debug mode is not checked here: that belongs to each side's <c>EmApp</c>, because
   /// <c>IsDebugMode</c> lives there.
   /// </summary>
   public static class NavigationAccess
   {
      /// <summary>
      /// Answers whether the module of <paramref name="navigation"/> is declared in
      /// <paramref name="catalog"/> (the active server's claim catalog merged with the client's built-in
      /// claims). A navigation with no module binding is always considered present; a navigation with a
      /// required claim needs that claim's key; otherwise one claim in the same module is enough. Unlike
      /// <see cref="CanOpen"/>, this rule also applies to administrators and debug mode: a module that the
      /// server has turned off (e.g. the test module through <c>modules</c> in the API configuration) has no
      /// usable screen, so its menu is hidden.
      /// </summary>
      /// <param name="navigation">The navigation about to be opened.</param>
      /// <param name="catalog">The claim catalog known to the application.</param>
      public static bool IsDeclared(INavigation navigation, IReadOnlyList<ClaimAction> catalog) {
         if (navigation.ModuleName is null) return true;

         if (navigation.RequiredClaim is { } claim) {
            return catalog.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));
         }

         return catalog.Any(r => string.Equals(r.ModuleName, navigation.ModuleName, StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Answers whether <paramref name="user"/> may open <paramref name="navigation"/>, outside debug mode.
      /// The order of checks is binding: a navigation with no module binding is always allowed, no active
      /// user means not allowed, an administrator user is always allowed, and only then is the required claim
      /// checked (if any), or any claim in the same module is enough.
      /// <para>
      /// It deliberately does not use the <see cref="ClaimCollection"/> indexer: that indexer throws in DEBUG
      /// for a claim that is not in the server catalog, while here a key that does not exist simply means
      /// "does not have it" - a module missing on the server is not the caller's mistake.
      /// </para>
      /// </summary>
      /// <param name="navigation">The navigation about to be opened.</param>
      /// <param name="user">The currently active user, or <c>null</c> when nobody has signed in.</param>
      public static bool CanOpen(INavigation navigation, User? user) {
         if (navigation.ModuleName is null) return true;

         if (user is null) return false;

         if (user.cUserIsAdmin) return true;

         if (navigation.RequiredClaim is { } claim) {
            return user.AvailableClaims.Any(r =>
               string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));
         }

         return user.AvailableClaims.Any(r =>
            string.Equals(r.ModuleName, navigation.ModuleName, StringComparison.OrdinalIgnoreCase));
      }
   }
}
