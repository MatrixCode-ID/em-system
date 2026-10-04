using Microsoft.Data.SqlClient;
using Em.Api.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Pemeriksaan sekali jalan atas alur-alur approval yang baru saja didaftarkan, supaya kesalahan
   /// konfigurasi yang baru akan terasa saat sebuah keputusan diambil justru menggagalkan aplikasi sejak
   /// dinyalakan.
   /// </summary>
   internal static class ApprovalStartupChecks
   {
      /// <summary>
      /// Memastikan setiap database modul pemakai approval bisa ikut dalam satu transaksi dengan database
      /// inti: SQL Server, satu server, satu login.
      /// </summary>
      /// <param name="flows">Seluruh alur yang didaftarkan.</param>
      /// <param name="builder">Builder aplikasi, sumber registrasi koneksi dan context.</param>
      /// <exception cref="InvalidOperationException">Dilempar kalau ada context modul yang tidak bisa ikut serta.</exception>
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
