namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// The one order every menu built from navigations follows, so the WPF and MAUI menus always list
   /// the same screens the same way: items by <see cref="INavigation.OrderIndex"/>, items with the same
   /// index by title, and menu groups - which carry no index of their own - by their caption.
   /// </summary>
   public static class MenuOrder
   {
      /// <summary>How titles and group captions are compared: alphabetically, ignoring case.</summary>
      public static StringComparer TextComparer { get; } = StringComparer.OrdinalIgnoreCase;

      /// <summary>
      /// Orders navigations for a menu: by <see cref="INavigation.OrderIndex"/>, then alphabetically by
      /// title for navigations that share the same index.
      /// </summary>
      /// <param name="source">The navigations to order.</param>
      public static IOrderedEnumerable<T> InMenuOrder<T>(this IEnumerable<T> source) where T : INavigation =>
         source.OrderBy(r => r.OrderIndex).ThenBy(r => r.Title ?? "", TextComparer);

      /// <summary>
      /// Returns the position at which a new group with the given caption is inserted so the groups at
      /// that level stay in alphabetical order. Entries that are not groups are skipped when comparing,
      /// so a level that mixes items and groups keeps its items where they are.
      /// </summary>
      /// <param name="level">The entries already at that menu level.</param>
      /// <param name="caption">Caption of the group to insert.</param>
      /// <param name="groupCaption">Caption of an entry, or <c>null</c> when the entry is not a group.</param>
      public static int GroupInsertIndex<T>(IList<T> level, string caption, Func<T, string?> groupCaption) {
         for (var i = 0; i < level.Count; i++) {
            var other = groupCaption(level[i]);
            if (other != null && TextComparer.Compare(caption, other) < 0) return i;
         }

         return level.Count;
      }
   }
}
