using System.ComponentModel;
using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Which column a list is sorted on and in which direction, readable per column through the indexer so each
   /// column header can bind to its own state - e.g. <c>{Binding Sort[Account]}</c>.
   /// <para>
   /// A header click cycles its column through ascending, descending and back to the list's default order;
   /// clicking another column starts that one ascending.
   /// </para>
   /// </summary>
   /// <typeparam name="TField">Enum naming the sortable columns; its default value means "no column".</typeparam>
   public sealed class ColumnSortState<TField> : NotifyPropertyBase where TField : struct, Enum
   {
      /// <summary>The sorted column, or the enum's default value when the list is in its default order.</summary>
      public TField Field { get; private set; }

      /// <summary>Whether the sorted column runs from the highest value down.</summary>
      public bool Descending { get; private set; }

      /// <summary>
      /// Direction a column is sorted in, or <c>null</c> when the list is not sorted on it. The value a column
      /// header binds to for its arrow.
      /// </summary>
      /// <param name="field">The column asked about.</param>
      public ListSortDirection? this[TField field] =>
         IsSorted && EqualityComparer<TField>.Default.Equals(Field, field)
            ? Descending ? ListSortDirection.Descending : ListSortDirection.Ascending
            : null;

      /// <summary>Whether the list is sorted on any column.</summary>
      public bool IsSorted => !EqualityComparer<TField>.Default.Equals(Field, default);

      /// <summary>Moves a column one step along ascending, descending, default order.</summary>
      /// <param name="field">The column whose header was clicked.</param>
      public void Cycle(TField field) {
         if (!EqualityComparer<TField>.Default.Equals(Field, field))
            Apply(field, descending: false);
         else if (!Descending)
            Apply(field, descending: true);
         else
            Apply(default, descending: false);
      }

      private void Apply(TField field, bool descending) {
         Field = field;
         Descending = descending;

         // "Item[]" is the name WPF listens on for every indexer binding at once.
         OnPropertyChanged("Item[]");
         OnPropertyChanged(nameof(Field));
         OnPropertyChanged(nameof(Descending));
         OnPropertyChanged(nameof(IsSorted));
      }
   }
}
