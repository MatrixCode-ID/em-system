using Microsoft.Data.SqlClient;

[assembly: AssemblyFixture(typeof(Em.Api.Core.IntegrationTests.SqlServerDatabase))]

namespace Em.Api.Core.IntegrationTests
{
   /// <summary>
   /// A temporary SQL Server database for one run of the integration tests. Created on the local server
   /// with Windows Authentication, so there is no password anywhere, and deleted again after all the tests
   /// finish.
   /// <para>
   /// The server is <c>(local)</c>, or fill <see cref="ServerVariable"/> for another instance (e.g.
   /// <c>.\SQLEXPRESS</c> or <c>(localdb)\MSSQLLocalDB</c>). When the server cannot be reached, the tests
   /// that use it are skipped with the reason, not failed, so a machine without SQL Server can still run all
   /// the tests.
   /// </para>
   /// </summary>
   public sealed class SqlServerDatabase : IAsyncLifetime
   {
      /// <summary>The environment variable to change the server, default <c>(local)</c>.</summary>
      public const string ServerVariable = "EM_TEST_DB_SERVER";

      /// <summary>The database name prefix, so leftovers from an interrupted run are easy to recognize and clean up.</summary>
      public const string DatabasePrefix = "EmSystem_IntegrationTest_";

      private string? masterConnectionString;
      private string? databaseName;
      private string? connectionString;
      private string? unavailableReason;
      private readonly List<string> extraDatabases = [];

      /// <summary>
      /// The connection string to the temporary database. When the server is not available, the calling test
      /// is skipped with the reason.
      /// </summary>
      public string ConnectionString {
         get {
            if (connectionString is null) Assert.Skip(unavailableReason ?? "SQL Server test database was not created.");
            return connectionString;
         }
      }

      public async ValueTask InitializeAsync() {
         var server = Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } value ? value : "(local)";
         var builder = new SqlConnectionStringBuilder {
            DataSource = server,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            // Local instances use a self-signed certificate, which SqlClient refuses by default.
            TrustServerCertificate = true,
            ConnectTimeout = 5
         };
         masterConnectionString = builder.ConnectionString;
         var name = DatabasePrefix + Guid.NewGuid().ToString("N");

         try {
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync();
         }
         catch (SqlException ex) {
            unavailableReason = $"SQL Server '{server}' is not available with Windows Authentication " +
               $"({ex.Message}). Set {ServerVariable} to another instance to run integration tests.";
            return;
         }

         databaseName = name;
         builder.InitialCatalog = name;
         connectionString = builder.ConnectionString;
      }

      /// <summary>Creates an extra empty database for one test class; it is also deleted in DisposeAsync.</summary>
      public async Task<string> CreateExtraDatabaseAsync(CancellationToken ct) {
         _ = ConnectionString; // skips the test when the server is not available
         var name = DatabasePrefix + Guid.NewGuid().ToString("N");
         await using (var connection = new SqlConnection(masterConnectionString)) {
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync(ct);
         }

         lock (extraDatabases) extraDatabases.Add(name);
         return new SqlConnectionStringBuilder(connectionString) { InitialCatalog = name }.ConnectionString;
      }

      public async ValueTask DisposeAsync() {
         if (masterConnectionString is null) return;

         // Pooled connections from the tests still hold the database open; drop them first.
         SqlConnection.ClearAllPools();
         string[] names;
         lock (extraDatabases) names = [.. extraDatabases];
         if (databaseName is not null) names = [.. names, databaseName];
         if (names.Length == 0) return;

         await using var connection = new SqlConnection(masterConnectionString);
         await connection.OpenAsync();
         foreach (var name in names) {
            await using var command = new SqlCommand(
               $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];",
               connection);
            await command.ExecuteNonQueryAsync();
         }
      }
   }
}
