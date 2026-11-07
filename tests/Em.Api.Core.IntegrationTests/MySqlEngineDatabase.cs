using System.Text;
using MySqlConnector;

namespace Em.Api.Core.IntegrationTests;

/// <summary>
/// Temporary MySQL databases built from the real <c>doc/sqlscript/mysql</c> scripts. Opt-in through
/// <c>EM_TEST_MYSQL</c>, an admin connection string without a database, for example
/// <c>Server=127.0.0.1;Port=3306;User ID=root;Password=</c>; without it the tests are skipped. Every database
/// is dropped when the test class finishes.
/// </summary>
public sealed class MySqlEngineDatabase : IAsyncLifetime
{
   /// <summary>The environment variable holding the admin connection string.</summary>
   public const string ServerVariable = "EM_TEST_MYSQL";

   private const string Prefix = "em_integration_";
   private readonly List<string> _created = [];
   private string? _admin;

   public ValueTask InitializeAsync() {
      if (Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } value)
         _admin = new MySqlConnectionStringBuilder(value) { AllowUserVariables = true }.ConnectionString;
      return ValueTask.CompletedTask;
   }

   /// <summary>Creates an empty database and returns its connection string.</summary>
   public async Task<string> CreateEmptyAsync(CancellationToken ct) {
      if (_admin is null) Assert.Skip($"Set {ServerVariable} to run the MySQL engine tests.");
      var name = Prefix + Guid.NewGuid().ToString("N");
      await using (var connection = new MySqlConnection(_admin)) {
         await connection.OpenAsync(ct);
         await using var command = new MySqlCommand($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci", connection);
         await command.ExecuteNonQueryAsync(ct);
      }
      lock (_created) _created.Add(name);
      return new MySqlConnectionStringBuilder(_admin) { Database = name }.ConnectionString;
   }

   /// <summary>Creates a database with the complete engine schema and returns its connection string.</summary>
   public async Task<string> CreateFullAsync(CancellationToken ct) {
      var connectionString = await CreateEmptyAsync(ct);
      var root = Path.Combine(RepoRoot(), "doc", "sqlscript", "mysql");
      var files = Directory.GetFiles(Path.Combine(root, "sets"), "*.sql").Order(StringComparer.Ordinal)
         .Concat(Directory.GetFiles(Path.Combine(root, "tables"), "*.sql").Where(f => !Path.GetFileName(f).StartsWith("900-")).Order(StringComparer.Ordinal))
         .Concat(Directory.GetFiles(Path.Combine(root, "views"), "*.sql").Where(f => !Path.GetFileName(f).StartsWith("vi_Test")).Order(StringComparer.Ordinal));
      await using var connection = new MySqlConnection(connectionString);
      await connection.OpenAsync(ct);
      foreach (var file in files) await RunScriptAsync(connection, await File.ReadAllTextAsync(file, ct), ct);
      return connectionString;
   }

   /// <summary>Runs a script of <c>doc/sqlscript/mysql</c> on an existing database.</summary>
   public async Task RunAsync(string connectionString, string relativeFile, CancellationToken ct) {
      await using var connection = new MySqlConnection(connectionString);
      await connection.OpenAsync(ct);
      await RunScriptAsync(connection, await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "doc", "sqlscript", "mysql", relativeFile), ct), ct);
   }

   // The client's DELIMITER command is not SQL, so the script is split here the way the client would.
   private static async Task RunScriptAsync(MySqlConnection connection, string script, CancellationToken ct) {
      var delimiter = ";";
      var statement = new StringBuilder();
      foreach (var line in script.Split('\n')) {
         var trimmed = line.Trim();
         if (trimmed.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase)) {
            delimiter = trimmed["DELIMITER ".Length..].Trim();
            statement.Clear();
            continue;
         }
         if (!trimmed.EndsWith(delimiter, StringComparison.Ordinal)) {
            statement.Append(line).Append('\n');
            continue;
         }
         statement.Append(line.TrimEnd()[..^delimiter.Length]);
         await using var command = new MySqlCommand(statement.ToString(), connection);
         await command.ExecuteNonQueryAsync(ct);
         statement.Clear();
      }
   }

   private static string RepoRoot() {
      for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
         if (Directory.Exists(Path.Combine(dir.FullName, "doc", "sqlscript", "mysql", "tables"))) return dir.FullName;
      throw new InvalidOperationException("Repository root with doc/sqlscript was not found.");
   }

   public async ValueTask DisposeAsync() {
      if (_admin is null) return;
      MySqlConnection.ClearAllPools();
      await using var connection = new MySqlConnection(_admin);
      await connection.OpenAsync();
      foreach (var name in _created) {
         await using var command = new MySqlCommand($"DROP DATABASE IF EXISTS `{name}`", connection);
         await command.ExecuteNonQueryAsync();
      }
   }
}
