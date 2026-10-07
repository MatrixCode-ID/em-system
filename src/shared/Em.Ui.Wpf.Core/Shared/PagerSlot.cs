using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// One position in the row of page buttons: either a page number that can be clicked, or a gap ("...")
   /// representing pages that do not fit to be shown. The gap itself can be clicked to open the
   /// jump-to-page field.
   /// </summary>
   public sealed class PagerSlot : NotifyPropertyBase
   {
      /// <summary>
      /// Creates a gap ("...") between two runs of page numbers.
      /// </summary>
      public static PagerSlot Gap() => new() { IsGap = true };

      /// <summary>
      /// Creates one page number button.
      /// </summary>
      /// <param name="page">The page number that is represented, starting from 1.</param>
      /// <param name="isCurrent">Whether this is the page being shown.</param>
      public static PagerSlot Of(int page, bool isCurrent) =>
         new() { Page = page, IsCurrent = isCurrent };

      /// <summary>
      /// The page number represented by this position. It means nothing when <see cref="IsGap"/> is
      /// <c>true</c>.
      /// </summary>
      public int Page {
         get => Get<int>();
         set => Set(value);
      }

      /// <summary>
      /// Indicates this position is a gap ("..."), not a page number.
      /// </summary>
      public bool IsGap {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Indicates this page is the active one. Bound two-way to the page button, so its value also changes
      /// when the user clicks the button.
      /// </summary>
      public bool IsCurrent {
         get => Get<bool>();
         set => Set(value);
      }
   }
}
