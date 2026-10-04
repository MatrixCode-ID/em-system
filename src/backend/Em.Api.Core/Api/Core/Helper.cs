using System.Globalization;

public static class Helper
{
      public static CultureInfo EnUs { get; } = new("en-US");
      public static CultureInfo IdID { get; } = new("id-ID");

      // Splits a free-text search term on whitespace and strips the characters LIKE treats as wildcards.
      // Dropping them beats escaping them: an ESCAPE clause is written differently per database, while a
      // plain pattern is translated the same way by every provider.
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
