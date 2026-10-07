namespace Em.Shared
{
   /// <summary>
   /// Translates between the icon token stored in data and the <see cref="UiIconType"/> used inside
   /// the program. The member name is stored, not its number, so the data stays human-readable and its
   /// meaning does not shift when the order of enum members changes.
   /// </summary>
   public static class UiIcons
   {
      /// <summary>
      /// Translates a stored icon token into a <see cref="UiIconType"/>. A token that is empty,
      /// misspelled, or unknown to this version of the program is not an error - all of them fall back
      /// to <paramref name="fallback"/>. An icon is only presentation: a foreign token carried over
      /// from old data, an import, or another client must never make anything fail.
      /// </summary>
      /// <param name="token">The stored icon token, e.g. <c>"Shield"</c>. May be <c>null</c>.</param>
      /// <param name="fallback">The value used when the token cannot be translated.</param>
      /// <returns>The icon matching the token, or <paramref name="fallback"/>.</returns>
      public static UiIconType Parse(string? token, UiIconType fallback) {
         if (string.IsNullOrWhiteSpace(token)) return fallback;

         // Enum.TryParse also accepts numeric text - including numbers that match no member - and
         // returns a value that has no name. The result is therefore still checked with
         // Enum.IsDefined before it is accepted.
         return Enum.TryParse<UiIconType>(token.Trim(), ignoreCase: true, out var icon)
                && Enum.IsDefined(icon)
            ? icon
            : fallback;
      }

      /// <summary>
      /// Translates a <see cref="UiIconType"/> into the token stored in data.
      /// </summary>
      /// <param name="icon">The icon to store.</param>
      /// <returns>
      /// The icon's token name, or <c>null</c> for <see cref="UiIconType.Unspecified"/> - "not chosen"
      /// has no token and should leave no trace in the data.
      /// </returns>
      public static string? ToToken(UiIconType icon) {
         return icon == UiIconType.Unspecified ? null : icon.ToString();
      }
   }
}
