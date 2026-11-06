namespace Em.Api.Shared;

/// <summary>
/// Recognizes database failures the engine turns into an answer, whichever provider raised them. The failure
/// can arrive as the exception itself or wrapped by EF Core, so the whole chain is searched.
/// </summary>
internal static class EmDbErrors
{
   /// <summary>Whether <paramref name="exception"/> comes from a violated unique key or unique index.</summary>
   public static bool IsUniqueConflict(Exception exception) => Any(exception, error => error switch {
      Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } => true,
      MySqlConnector.MySqlException { Number: 1062 } => true,
      Npgsql.PostgresException { SqlState: "23505" } => true,
      _ => false
   });

   /// <summary>Whether <paramref name="exception"/> comes from a table that does not exist.</summary>
   public static bool IsMissingTable(Exception exception) => Any(exception, error => error switch {
      Microsoft.Data.SqlClient.SqlException { Number: 208 } => true,
      MySqlConnector.MySqlException { Number: 1146 } => true,
      Npgsql.PostgresException { SqlState: "42P01" } => true,
      _ => false
   });

   private static bool Any(Exception exception, Func<Exception, bool> test) {
      for (Exception? error = exception; error is not null; error = error.InnerException)
         if (test(error)) return true;
      return false;
   }
}
