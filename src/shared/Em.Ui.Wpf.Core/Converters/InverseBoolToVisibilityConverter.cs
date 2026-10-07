using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// The opposite of <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>: <c>true</c>
   /// hides, <c>false</c> shows. Used as a pair with the original for two sides of one state - e.g. text
   /// shown while idle and the input that replaces it while editing - so both read one and the same
   /// property instead of two that could disagree.
   /// </summary>
   /// <remarks>
   /// A value that is not a <see cref="bool"/> - including a binding whose path is not found - is taken as
   /// <c>false</c>, so what is seen is the idle state, not an empty screen.
   /// </remarks>
   public class InverseBoolToVisibilityConverter : IValueConverter
   {
      /// <summary>Produces <see cref="Visibility.Collapsed"/> for <c>true</c>, and the opposite.</summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is true ? Visibility.Collapsed : Visibility.Visible;

      /// <summary>Converts <see cref="Visibility"/> back to the boolean value that produced it.</summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is Visibility.Collapsed or Visibility.Hidden;
   }
}
