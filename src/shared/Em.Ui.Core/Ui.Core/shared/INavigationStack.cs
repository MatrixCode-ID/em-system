namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// The navigation path shown by one host: a row of entries that can be traversed back and forth, plus
   /// one optional home standing in front of that path.
   /// <para>
   /// The rules: home is not part of <see cref="Entries"/> - its position is in front of the path - and
   /// its body is never released. Opening a new screen discards all entries ahead of the current position
   /// and then adds the new entry at the end. Going back from the first entry lands on home without
   /// discarding the path, so going forward can still return there; <see cref="NavigateHome"/> is what
   /// clears the whole path.
   /// </para>
   /// </summary>
   public interface INavigationStack
   {
      /// <summary>The entries of this path, in order from the first opened. Home is not included.</summary>
      IReadOnlyList<INavigationEntry> Entries { get; }

      /// <summary>
      /// The entry currently shown - which may also be <see cref="Home"/> - or <c>null</c> when nothing has
      /// ever been shown.
      /// </summary>
      INavigationEntry? Current { get; }

      /// <summary>The home entry, or <c>null</c> for a stack without home.</summary>
      INavigationEntry? Home { get; }

      /// <summary>Goes forward one step in this path.</summary>
      /// <returns><c>false</c> when there is no entry ahead, or the move is refused.</returns>
      Task<bool> Forward();

      /// <summary>Goes back one step in this path; from the first entry going back lands on home.</summary>
      /// <returns><c>false</c> when there is nowhere to go back to, or the move is refused.</returns>
      Task<bool> Backward();

      /// <summary>
      /// Goes home, then clears and releases all entries of this path. Does nothing for a stack without home.
      /// </summary>
      Task NavigateHome();

      /// <summary>
      /// Discards and releases all entries ahead of the current position. Run automatically every time a new
      /// screen opens a branch.
      /// </summary>
      Task ClearForwardStacks();

      /// <summary>Whether there is an entry titled <paramref name="title"/> in this path.</summary>
      /// <param name="title">Title of the entry being looked for.</param>
      bool IsInStack(string title);
   }
}
