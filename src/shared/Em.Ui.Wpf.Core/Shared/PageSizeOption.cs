using System.Globalization;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One "rows per page" choice in a paged list, holding its number (<see cref="Size"/>) together with the
   /// text that appears in the combo box (<see cref="Caption"/>). Used so the "ALL" choice can be in the
   /// same list without a separate type.
   /// </summary>
   public sealed class PageSizeOption
   {
      /// <summary>
      /// The <see cref="Size"/> value that means "show all rows on one page". Deliberately very large (not 0
      /// or negative) so ordinary page calculations remain correct without a special rule: the page count
      /// automatically becomes one.
      /// </summary>
      public const int AllRows = int.MaxValue;

      /// <summary>
      /// A ready-made choice for "all rows".
      /// </summary>
      public static PageSizeOption All { get; } = new(AllRows, "ALL");

      /// <summary>
      /// Creates a choice with a particular number of rows, with its own number as the text.
      /// </summary>
      /// <param name="size">The number of rows per page, which must be greater than zero.</param>
      public static PageSizeOption Of(int size) =>
         new(size, size.ToString(CultureInfo.InvariantCulture));

      private PageSizeOption(int size, string caption) {
         Size = size;
         Caption = caption;
      }

      /// <summary>
      /// The number of rows per page that this choice represents.
      /// </summary>
      public int Size { get; }

      /// <summary>
      /// The text shown in the UI for this choice.
      /// </summary>
      public string Caption { get; }

      /// <summary>
      /// Indicates this choice is "all rows" and not a particular number.
      /// </summary>
      public bool IsAll => Size == AllRows;

      /// <inheritdoc />
      public override string ToString() => Caption;
   }
}
