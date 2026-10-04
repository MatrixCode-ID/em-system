using System.Text.RegularExpressions;
using Em.Shared;

namespace Em.Api.Core;

internal static partial class RobotNames
{
   [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,99}$")]
   private static partial Regex NamePattern();
   public static string ValidateName(string? name) => name != null && NamePattern().IsMatch(name)
      ? name : throw new ActionException("Robot name must be 1-100 lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.", 400);
   public static string? ValidateDescription(string? description) {
      var text = description?.Trim();
      if (string.IsNullOrEmpty(text)) return null;
      if (text.Length > 500) throw new ActionException("Description must not be longer than 500 characters.", 400);
      return text;
   }
}
