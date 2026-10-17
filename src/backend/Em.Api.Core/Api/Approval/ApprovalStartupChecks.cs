using Microsoft.Data.SqlClient;
using Em.Api.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// One-time checks of the approval flows that have just been registered, so a configuration mistake
   /// that would only be felt when a decision is taken instead fails the application from the moment it
   /// is started.
   /// </summary>
   internal static class ApprovalStartupChecks
   {
      /// <summary>
      /// Makes sure every module database that uses approval can take part in one transaction with the core
      /// database: SQL Server, one server, one login.
      /// </summary>
      /// <param name="contextTypes">Only contexts owning registered approval entities.</param>
      /// <param name="builder">The application builder, the source of connection and context registrations.</param>
      /// <exception cref="InvalidOperationException">Thrown when a module context cannot take part.</exception>
      public static void VerifyDatabases(EmAppBuilder builder, Type[] contextTypes) {
         if (contextTypes.Length == 0) return;
         if (!builder.DbConnections.TryGetValue(EmAppBuilder.DefaultConnectionName, out var core) || core.Provider != DatabaseProvider.MicrosoftSqlServer)
            throw new InvalidOperationException("Data Approval requires SQL Server.");
         foreach (var type in contextTypes) {
            var connectionName = builder.DbContextConnectionNames[type];
            var module = builder.DbConnections[connectionName];
            if (module.Provider != DatabaseProvider.MicrosoftSqlServer)
               throw new InvalidOperationException($"Approval context '{type}' must use SQL Server.");
            var problem = Mismatch(new SqlConnectionStringBuilder(core.ConnectionString), new SqlConnectionStringBuilder(module.ConnectionString));
            if (problem is not null) throw new InvalidOperationException($"Approval context '{type}' cannot share the core transaction: {problem}.");
         }
      }

      // Why two connection strings cannot share one connection, or null when they can.
      private static string? Mismatch(SqlConnectionStringBuilder core, SqlConnectionStringBuilder module) {
         if (string.IsNullOrWhiteSpace(core.InitialCatalog) || string.IsNullOrWhiteSpace(module.InitialCatalog)) {
            return "a connection string names no database (Initial Catalog)";
         }

         if (!string.Equals(Server(core), Server(module), StringComparison.OrdinalIgnoreCase)) {
            return $"they are on different servers ('{core.DataSource}' and '{module.DataSource}')";
         }

         if (core.IntegratedSecurity != module.IntegratedSecurity ||
             !string.Equals(core.UserID, module.UserID, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(core.Password, module.Password, StringComparison.Ordinal)) {
            return "they log in differently";
         }

         return null;
      }

      // "tcp:host" and "host" are the same server.
      private static string Server(SqlConnectionStringBuilder builder) {
         var source = builder.DataSource.Trim();
         return source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? source[4..] : source;
      }
   }
}
