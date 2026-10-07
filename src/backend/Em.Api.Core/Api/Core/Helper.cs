using System.Globalization;

/// <summary>Small helpers shared by the engine's services.</summary>
public static class Helper
{
      /// <summary>The English (United States) culture.</summary>
      public static CultureInfo EnUs { get; } = new("en-US");
      /// <summary>The Indonesian (Indonesia) culture.</summary>
      public static CultureInfo IdID { get; } = new("id-ID");

      // Splits a free-text search term on whitespace and strips the characters LIKE treats as wildcards.
      // Dropping them beats escaping them: an ESCAPE clause is written differently per database, while a
      // plain pattern is translated the same way by every provider.
      /// <summary>Splits a free-text search term into distinct tokens that are safe to use in a LIKE pattern.</summary>
      public static string[] BuildSearchTokens(string searchTerm) {
         if (string.IsNullOrWhiteSpace(searchTerm)) return [];
         return [
            .. searchTerm
               .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
               .Select(t => t.Replace("%", "").Replace("_", "").Replace("[", "").Replace("]", ""))
               .Where(t => t.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase)
         ];
      }
}
