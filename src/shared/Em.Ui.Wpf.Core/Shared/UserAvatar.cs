using System.Windows.Media;
using Em.Api.Core.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Em.Ui.Wpf.Shared
{
   // How the signed-in user is shown on an account button - name, account, initials and avatar
   // colour - shared by every window that carries one, so the same person looks the same on both
   // layouts.
   internal static class UserAvatar
   {
      internal static string DisplayName(User? user) =>
         user?.cContactFullName is { Length: > 0 } fullName ? fullName
         : user?.cUserAccount is { Length: > 0 } account ? account
         : "Not signed in";

      internal static string Account(User? user) => user?.cUserAccount ?? string.Empty;

      internal static string Initials(User? user) => BuildInitials(
         user?.cContactFullName is { Length: > 0 } fullName ? fullName : Account(user));

      internal static SolidColorBrush Brush(User? user) {
         // Keyed on the account id where there is one: a person who is renamed keeps the colour they
         // are recognised by, which a name-keyed colour would take away from them.
         var key = user?.cUserId is { Length: > 0 } id ? id : Account(user);
         if (key.Length == 0) return NoAvatarBrush;

         // FNV-1a rather than string.GetHashCode: that one is salted per process, so the same
         // account would come back a different colour every time the application starts - which is
         // exactly what an avatar colour must not do.
         var hash = 2166136261u;
         foreach (var c in key) hash = (hash ^ c) * 16777619u;

         return AvatarBrushes[(int)(hash % (uint)AvatarBrushes.Length)];
      }

      // Eight hues far enough apart to tell two people beside each other apart, each laid down at 40%
      // alpha: the disc then tints whatever the theme has put behind it instead of covering it, so one
      // palette works on both themes and the initials stay readable over either.
      private static readonly SolidColorBrush[] AvatarBrushes = [
         Frozen("#66E53935"), Frozen("#668E24AA"), Frozen("#663949AB"), Frozen("#66039BE5"),
         Frozen("#6600897B"), Frozen("#667CB342"), Frozen("#66FB8C00"), Frozen("#666D4C41"),
      ];

      // Nobody signed in is not a person to be told apart, so it keeps the same neutral grey the rest
      // of the toolbar is drawn in.
      private static readonly SolidColorBrush NoAvatarBrush = Frozen("#40808080");

      // Frozen because these live for the lifetime of the application and are shared by every host:
      // an unfrozen brush would be re-checked for changes on every render and pinned to one thread.
      private static SolidColorBrush Frozen(string color) {
         var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
         brush.Freeze();
         return brush;
      }

      // Two letters, because one is too easily shared by half the people in a company and three no
      // longer fits the circle: the first letter of the first name and the first of the last. A name
      // that is a single word has no last name to take from, so it gives up its own second letter
      // instead - "debugger" reads as "DE" rather than as a lone "D".
      private static string BuildInitials(string name) {
         var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
         return words.Length switch {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[^1][0]}".ToUpperInvariant()
         };
      }
   }
}
