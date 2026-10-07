namespace Em.Api.Shared
{
   /// <summary>Database providers the engine can connect to.</summary>
   public enum DatabaseProvider
   {
      /// <summary>Microsoft SQL Server.</summary>
      MicrosoftSqlServer = 0,
      /// <summary>MySQL.</summary>
      MySql = 1,
      /// <summary>PostgreSQL.</summary>
      PostgreSql = 2,
   }
}