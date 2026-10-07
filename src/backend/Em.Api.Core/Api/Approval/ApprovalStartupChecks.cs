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
      /// <param name="flows">All the flows that were registered.</param>
      /// <param name="builder">The application builder, the source of connection and context registrations.</param>
      /// <exception cref="InvalidOperationException">Thrown when a module context cannot take part.</exception>
      public static void VerifyDatabases(IEnumerable<ApprovalFlowDeclaration> flows, EmAppBuilder builder) {
         foreach (var flow in flows) {
            foreach (var type in flow.ModuleDbContextTypes) {
               if (!builder.DbContextConnectionNames.TryGetValue(type, out var connectionName)) {
                  throw new InvalidOperationException(
                     $"Approval flow '{flow.DocType}' uses service {flow.ServicesType.Name}, which asks for {type.Name}, " +
                     $"but {type.Name} was not registered through AddDbContext, so its database cannot join the approval transaction.");
               }

               var module = builder.DbConnections[connectionName];
               if (!builder.DbConnections.TryGetValue(EmAppBuilder.DefaultConnectionName, out var core) ||
                   core.Provider != DatabaseProvider.MicrosoftSqlServer ||
                   module.Provider != DatabaseProvider.MicrosoftSqlServer) {
                  throw new InvalidOperationException(
                     $"Approval flow '{flow.DocType}' needs {type.Name} (connection '{connectionName}') and the core database in one transaction, " +
                     "which is only available when both are on SQL Server.");
               }

               var problem = Mismatch(new SqlConnectionStringBuilder(core.ConnectionString),
                  new SqlConnectionStringBuilder(module.ConnectionString));
               if (problem is not null) {
                  throw new InvalidOperationException(
                     $"Approval flow '{flow.DocType}' needs {type.Name} (connection '{connectionName}') and the core database in one " +
                     $"transaction on one connection, but {problem}. Put them on one server and one login.");
               }
            }
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
