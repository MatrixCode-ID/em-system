using System.Globalization;
using System.Windows.Data;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Turns a name (e.g. a contact's full name or an account name) into short initials to show inside a
   /// round avatar in a list. At most two letters are taken: the first letter of the first word and of the
   /// last word, so the initials stay readable even when the name has many words.
   /// </summary>
   /// <remarks>
   /// When the name is empty or contains no letters at all, the result is a question mark ("?"), so the
   /// avatar still has content and the row size does not change.
   /// </remarks>
   public class InitialsConverter : IValueConverter
   {
      /// <summary>
      /// Produces the initials (at most two letters, uppercase) from the given text value.
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
         var words = (value as string ?? string.Empty)
            .Split([' ', '\t', '.', ',', '-'], StringSplitOptions.RemoveEmptyEntries);

         if (words.Length == 0) return "?";

         var first = words[0][0];
         var last = words[^1][0];

         return words.Length == 1
            ? char.ToUpper(first, culture).ToString()
            : string.Concat(char.ToUpper(first, culture), char.ToUpper(last, culture));
      }

      /// <summary>
      /// Not supported: initials cannot be turned back into the original name.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("Initials cannot be converted back to a name.");
   }
}
