using System.Globalization;
using System.Windows.Data;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>What a chip in the roles cell of a user row stands for, which decides its look.</summary>
   public enum RoleBadgeKind
   {
      /// <summary>The account has administrator privileges.</summary>
      Admin,
      /// <summary>One role held in effect now.</summary>
      Role,
      /// <summary>The roles that did not fit, folded into "+N".</summary>
      Overflow,
   }

   /// <summary>One chip in the roles cell of a user row.</summary>
   /// <param name="Caption">Text on the chip.</param>
   /// <param name="Kind">What the chip stands for.</param>
   public sealed record RoleBadge(string Caption, RoleBadgeKind Kind);

   /// <summary>
   /// Turns the role names of one list row into its role chips: an ADMIN chip first when the account has
   /// administrator privileges, then role names up to <see cref="MaxVisible"/> named chips in all, and the
   /// remaining roles folded into a single "+N" chip. All chips come out as one list, so the cell can wrap them
   /// one by one when it is narrow.
   /// </summary>
   /// <remarks>
   /// As a multi-value converter it takes the role names and the administrator flag and returns the chips. As a
   /// single-value converter it takes the role names and returns the tooltip of the cell: every name, one per
   /// line, or <c>null</c> when there is none. Keeping both here keeps the cut-off point in one place.
   /// </remarks>
   public class RoleBadgeConverter : IMultiValueConverter, IValueConverter
   {
      /// <summary>
      /// Number of chips shown by name, the ADMIN chip included, before the remaining roles fold into "+N". Two
      /// keeps a narrow cell at two lines at most, so the rows stay one height.
      /// </summary>
      public int MaxVisible { get; set; } = 2;

      /// <summary>Builds the chips from the role names (first value) and the administrator flag (second value).</summary>
      public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) {
         var names = values.ElementAtOrDefault(0) as IReadOnlyList<string> ?? [];
         var isAdmin = values.ElementAtOrDefault(1) is true;
         var shown = Math.Max(0, MaxVisible - (isAdmin ? 1 : 0));
         var hidden = names.Count - shown;

         var badges = new List<RoleBadge>();
         if (isAdmin) badges.Add(new("ADMIN", RoleBadgeKind.Admin));
         badges.AddRange(names.Take(shown).Select(name => new RoleBadge(name, RoleBadgeKind.Role)));
         if (hidden > 0) badges.Add(new($"+{hidden}", RoleBadgeKind.Overflow));
         return badges;
      }

      /// <summary>Lists every role name, one per line, for the tooltip of the cell.</summary>
      public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is IReadOnlyList<string> { Count: > 0 } names ? string.Join(Environment.NewLine, names) : null;

      /// <summary>Not supported: chips cannot be turned back into role names.</summary>
      public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("Role chips cannot be converted back to role names.");

      /// <summary>Not supported: a tooltip cannot be turned back into role names.</summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("A tooltip cannot be converted back to role names.");
   }
}
