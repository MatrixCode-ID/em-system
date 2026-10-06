using System.Text.RegularExpressions;
using Em.Shared;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Tag filter of a deploy target: comma-separated patterns where <c>*</c> matches any run of characters
   /// and <c>?</c> one character, compared case-sensitively like OCI tags. An empty filter matches every tag.
   /// </summary>
   internal static partial class CtnDeployTagFilter
   {
      public const int MaxLength = 256;

      [GeneratedRegex(@"^[A-Za-z0-9_.*?-]{1,128}$")]
      private static partial Regex PatternChars();

      /// <summary>Trims the patterns and joins them with a comma; <c>null</c> for an empty filter.</summary>
      /// <exception cref="ActionException">400 for a pattern with characters a tag cannot contain.</exception>
      public static string? Normalize(string? filter) {
         var patterns = Split(filter);
         foreach (var pattern in patterns) {
            if (!PatternChars().IsMatch(pattern)) {
               throw new ActionException(
                  $"Tag filter pattern '{pattern}' is invalid: use tag characters (letters, digits, '.', '_', '-') with * and ?.", 400);
            }
         }

         var joined = string.Join(",", patterns);
         if (joined.Length > MaxLength) throw new ActionException($"Tag filter must not be longer than {MaxLength} characters.", 400);
         return joined.Length == 0 ? null : joined;
      }

      /// <summary><c>true</c> when <paramref name="tag"/> matches one of the patterns, or the filter is empty.</summary>
      public static bool IsMatch(string? filter, string tag) {
         var patterns = Split(filter);
         return patterns.Length == 0 || patterns.Any(p => Glob(p).IsMatch(tag));
      }

      /// <summary>First tag that matches, or <c>null</c> when none does.</summary>
      public static string? FirstMatch(string? filter, IEnumerable<string> tags) => tags.FirstOrDefault(t => IsMatch(filter, t));

      private static string[] Split(string? filter) =>
         (filter ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

      private static Regex Glob(string pattern) =>
         new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
   }
}
