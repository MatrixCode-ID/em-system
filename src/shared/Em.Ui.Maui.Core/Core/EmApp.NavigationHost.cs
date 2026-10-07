using Em.Ui.Core.Shared;
// Both namespaces have an INavigation, and the one meant here is always Em's.
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Core
{
   public partial class EmApp : INavigationHost
   {
      private readonly List<Navigation> _allNavigations = [];
      private NavigationStack _mainStack = null!;

      /// <summary>
      /// Raised every time a navigation move succeeds, in any stack. Its sender is the navigation that was
      /// left.
      /// </summary>
      public event EventHandler<NavigationEventArgs>? Navigated;

      #region Explicit INavigationHost Implementation

      IEnumerable<INavigation> INavigationHost.Navigations => Navigations;
      INavigationStack INavigationHost.MainStack => MainStack;
      INavigationEntry? INavigationHost.FindEntry(string title) => FindEntry(title);
      Task<bool> INavigationHost.NavigateTo(string navigationName, object? data) => NavigateTo(navigationName, data);

      Task<bool> INavigationHost.NavigateTo(INavigation navigation, object? data) =>
         NavigateTo((Navigation)navigation, data);

      #endregion

      #region Properties

      /// <summary>All navigations known to the application.</summary>
      public IEnumerable<Navigation> Navigations => _allNavigations;

      /// <summary>The application's main stack, which is shown by its main page.</summary>
      public NavigationStack MainStack => _mainStack;

      #endregion

      #region Methods

      private void AddNavigation(Navigation navigation) {
         // The home navigation is registered explicitly, and an application is free to hand that
         // same instance to the builder as well, so registering twice has to be harmless.
         if (_allNavigations.Contains(navigation)) return;

         navigation.EmApp ??= this;
         _allNavigations.Add(navigation);
      }

      internal void RaiseNavigated(object sender, NavigationEventArgs args) => Navigated?.Invoke(sender, args);

      /// <summary>
      /// Looks for the entry titled <paramref name="title"/> in all stacks, home included. Case is not
      /// distinguished: the title is a key, and two titles that differ only in case point to the same document.
      /// </summary>
      /// <param name="title">The title of the entry being looked for.</param>
      /// <returns>The entry, or <c>null</c> when that title is not used anywhere yet.</returns>
      public NavigationEntry? FindEntry(string title) => MainStack.FindEntry(title);

      /// <summary>
      /// Opens the navigation named <paramref name="name"/> relative to <see cref="MainStack"/>. From inside a
      /// body, use <see cref="NavigationEntry.NavigateTo(string,object?)"/> of its own entry.
      /// </summary>
      /// <param name="name">The name of the target navigation.</param>
      /// <param name="data">The parameter for the target screen, or <c>null</c> when there is none.</param>
      /// <returns>
      /// <c>false</c> when the name is unknown, the user is not entitled to open it, or the move is refused.
      /// </returns>
      public Task<bool> NavigateTo(string name, object? data = null) => NavigateTo(name, data, MainStack);

      /// <summary>
      /// Opens <paramref name="targetNav"/> relative to <see cref="MainStack"/>. When the resulting title is
      /// already used by an entry, the display is only moved to that entry - without reloading and without
      /// replacing its data. From inside a body, use
      /// <see cref="NavigationEntry.NavigateTo(Navigation,object?)"/> of its own entry.
      /// </summary>
      /// <param name="targetNav">The target navigation.</param>
      /// <param name="data">The parameter for the target screen, or <c>null</c> when there is none.</param>
      /// <returns><c>false</c> when the user is not entitled to open it, or the move is refused.</returns>
      public Task<bool> NavigateTo(Navigation targetNav, object? data = null) => NavigateTo(targetNav, data, MainStack);

      internal Task<bool> NavigateTo(string name, object? data, NavigationStack origin) {
         var nav = _allNavigations.FirstOrDefault(r => r.Name == name);
         return nav != null ? NavigateTo(nav, data, origin) : Task.FromResult(false);
      }

      // The one place every navigation request ends up, whichever stack it was asked from. A title is
      // a key: if it is already shown anywhere, that entry is only brought back into view, otherwise a
      // new entry is opened on the stack the request came from.
      internal Task<bool> NavigateTo(Navigation targetNav, object? data, NavigationStack origin) {
         if (!CanOpen(targetNav)) return Task.FromResult(false);

         var title = ResolveTitle(targetNav, data);
         if (FindEntry(title) is { } existing) return existing.Stack.MoveTo(existing);

         return origin.Open(targetNav, data, title);
      }

      private static string ResolveTitle(Navigation navigation, object? data) =>
         (data as NavigationPayloadBase)?.Title is { Length: > 0 } title ? title : navigation.Title;

      /// <inheritdoc cref="NavigateToRoot(Navigation,object?)" />
      /// <param name="name">The name of the target navigation.</param>
      /// <param name="data"><inheritdoc cref="NavigateToRoot(Navigation,object?)" path="/param[@name='data']" /></param>
      public Task<bool> NavigateToRoot(string name, object? data = null) {
         var nav = _allNavigations.FirstOrDefault(r => r.Name == name);
         return nav != null ? NavigateToRoot(nav, data) : Task.FromResult(false);
      }

      /// <summary>
      /// Opens <paramref name="targetNav"/> in <see cref="MainStack"/> then makes it the only content of that
      /// stack, so there is no way back to anything that was open before. Used for a move that restarts the
      /// application flow - the login screen when the application is opened and when the session ends.
      /// <para>
      /// Different from <see cref="NavigationStack.NavigateHome"/>: home is not installed. Before anyone has
      /// signed in, the home body must not be built at all.
      /// </para>
      /// </summary>
      /// <param name="targetNav">The navigation that becomes the root of the new stack.</param>
      /// <param name="data">The parameter for the target navigation, or <c>null</c> when there is none.</param>
      /// <returns><c>false</c> when the move is cancelled; the stack is left as it is.</returns>
      public Task<bool> NavigateToRoot(Navigation targetNav, object? data = null) =>
         MainStack.NavigateToRoot(targetNav, data);

      #endregion
   }
}
