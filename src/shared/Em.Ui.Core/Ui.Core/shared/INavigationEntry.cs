namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// One place in a navigation stack: an <see cref="INavigation"/> that is currently open, complete with
   /// its own body, data, and title. Every time a screen is opened with a title that does not yet exist, a
   /// new entry is created - so "Edit User: ani" and "Edit User: joni" are two entries of the same
   /// navigation.
   /// <para>
   /// The body receives its own entry through <see cref="NavigationEventArgs.Entry"/>, and from there can
   /// change its title (<see cref="SetTitle"/>), close itself (<see cref="Close"/>), or open another
   /// screen (<see cref="NavigateTo(string,object?)"/>).
   /// </para>
   /// </summary>
   public interface INavigationEntry
   {
      /// <summary>Definition of the screen this entry opens.</summary>
      INavigation Navigation { get; }

      /// <summary>
      /// The title shown, which is also this entry's unique key across the whole application: one title can
      /// only appear in one place. Opening a screen with a title that already exists does not create a new
      /// entry, but moves the display to that existing entry.
      /// </summary>
      string Title { get; }

      /// <summary>The parameter used when this entry was opened, or <c>null</c> when there is none.</summary>
      object? Data { get; }

      /// <summary>The body owned by this entry. Every entry has its own body.</summary>
      INavigationBody Body { get; }

      /// <summary>The stack that holds this entry.</summary>
      INavigationStack Stack { get; }

      /// <summary>
      /// Asks this entry's body to reload its content with the <see cref="Data"/> it currently holds. Nobody
      /// can refuse a reload.
      /// </summary>
      Task Reload();

      /// <summary>
      /// Changes the title of this entry, e.g. from "Create New User" to "Edit User: ani" after its new data
      /// has been saved. The new title is subject to the same uniqueness rule as the initial title.
      /// </summary>
      /// <param name="title">The replacement title.</param>
      /// <returns>
      /// <c>false</c> when that title is already used by another entry; the old title is kept.
      /// </returns>
      bool SetTitle(string title);

      /// <summary>
      /// Removes this entry from its stack and releases its body. The body first gets a chance to refuse
      /// through <see cref="INavigationBody.OnNavigatingAway"/> - e.g. because there are still unsaved
      /// changes - and then <see cref="INavigationBody.OnRelease"/>. When this entry is the one being shown,
      /// the display first moves to the previous entry.
      /// </summary>
      /// <returns><c>false</c> when the closing is refused or this entry is no longer in the stack.</returns>
      Task<bool> Close();

      /// <summary>
      /// Opens the navigation named <paramref name="name"/> relative to this entry's stack. This is how a body
      /// opens another screen, so the destination screen appears in the same place as the calling body.
      /// </summary>
      /// <param name="name">Name of the target navigation.</param>
      /// <param name="data">Parameter for the target screen, or <c>null</c> when there is none.</param>
      /// <returns>
      /// <c>false</c> when the name is unknown, the user is not entitled to open it, or the move is refused.
      /// </returns>
      Task<bool> NavigateTo(string name, object? data = null);

      /// <inheritdoc cref="NavigateTo(string,object?)" />
      /// <param name="navigation">The target navigation.</param>
      /// <param name="data"><inheritdoc cref="NavigateTo(string,object?)" path="/param[@name='data']" /></param>
      Task<bool> NavigateTo(INavigation navigation, object? data = null);
   }
}
