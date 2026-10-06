using Microsoft.Data.SqlClient;

[assembly: AssemblyFixture(typeof(Em.Api.Core.IntegrationTests.SqlServerDatabase))]

namespace Em.Api.Core.IntegrationTests
{
   /// <summary>
   /// Database SQL Server sementara untuk satu kali jalan test integrasi. Dibuat di server lokal
   /// dengan Windows Authentication, jadi tidak ada password di mana pun, dan dihapus lagi setelah
   /// semua test selesai.
   /// <para>
   /// Servernya <c>(local)</c>, atau isi <see cref="ServerVariable"/> untuk instance lain (mis.
   /// <c>.\SQLEXPRESS</c> atau <c>(localdb)\MSSQLLocalDB</c>). Kalau server tidak terjangkau, test yang
   /// memakainya di-skip dengan alasannya, bukan gagal, supaya mesin tanpa SQL Server tetap bisa
   /// menjalankan seluruh test.
   /// </para>
   /// </summary>
   public sealed class SqlServerDatabase : IAsyncLifetime
   {
      /// <summary>Environment variable untuk mengganti server, default <c>(local)</c>.</summary>
      public const string ServerVariable = "EM_TEST_DB_SERVER";

      /// <summary>Awalan nama database, supaya sisa run yang terputus mudah dikenali dan dibersihkan.</summary>
      public const string DatabasePrefix = "EmSystem_IntegrationTest_";

      private string? masterConnectionString;
      private string? databaseName;
      private string? connectionString;
      private string? unavailableReason;
      private readonly List<string> extraDatabases = [];

      /// <summary>
      /// Connection string ke database sementara. Kalau server tidak tersedia, test pemanggilnya
      /// di-skip dengan alasannya.
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

      /// <summary>Membuat database kosong tambahan untuk satu kelas test; ikut dihapus di DisposeAsync.</summary>
      public async Task<string> CreateExtraDatabaseAsync(CancellationToken ct) {
         _ = ConnectionString; // skip bila server tidak tersedia
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
