namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Application-level navigation router: it knows every registered navigation, holds the main stack, and
   /// enforces the rule "one title appears in only one place".
   /// <para>
   /// <see cref="NavigateTo(string,object?)"/> here opens a screen relative to
   /// <see cref="MainStack"/> - used from outside a body, e.g. the home menu or the login screen. From
   /// inside a body, use <see cref="INavigationEntry.NavigateTo(string,object?)"/> of its own entry.
   /// </para>
   /// </summary>
   public interface INavigationHost
   {
      /// <summary>Raised every time a navigation move succeeds.</summary>
      event EventHandler<NavigationEventArgs>? Navigated;

      /// <summary>All navigations known to the application.</summary>
      IEnumerable<INavigation> Navigations { get; }

      /// <summary>The application's main stack, shown by its main window or page.</summary>
      INavigationStack MainStack { get; }

      /// <summary>Finds the entry titled <paramref name="title"/> in all stacks, home included.</summary>
      /// <param name="title">Title of the entry being looked for.</param>
      /// <returns>The entry, or <c>null</c> when that title is not used anywhere yet.</returns>
      INavigationEntry? FindEntry(string title);

      /// <summary>
      /// Opens <paramref name="navigation"/> relative to <see cref="MainStack"/>. When the resulting title is
      /// already used by an entry, the display is just moved to that entry - without reloading and without
      /// replacing its data.
      /// </summary>
      /// <param name="navigation">The target navigation.</param>
      /// <param name="data">Parameter for the target screen, or <c>null</c> when there is none.</param>
      /// <returns><c>false</c> when the user is not entitled to open it, or the move is refused.</returns>
      Task<bool> NavigateTo(INavigation navigation, object? data = null);

      /// <inheritdoc cref="NavigateTo(INavigation,object?)" />
      /// <param name="navigationName">Name of the target navigation.</param>
      /// <param name="data"><inheritdoc cref="NavigateTo(INavigation,object?)" path="/param[@name='data']" /></param>
      Task<bool> NavigateTo(string navigationName, object? data = null);
   }
}
