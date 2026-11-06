using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MySqlConnector;

namespace Em.Api.Shared;

/// <summary>How far an update lock taken by <see cref="EmSqlDialects.LockRows{T}"/> reaches.</summary>
internal enum EmRowLock
{
   /// <summary>The matching row only. The row is expected to exist.</summary>
   Row,

   /// <summary>The matching row, or the place where it would be: a concurrent insert of that key waits too.</summary>
   Range
}

/// <summary>The schema objects a feature needs before it can start.</summary>
internal sealed record EmSchemaRequirement
{
   /// <summary>Base tables that must exist.</summary>
   public IReadOnlyList<string> Tables { get; init; } = [];

   /// <summary>Views that must exist.</summary>
   public IReadOnlyList<string> Views { get; init; } = [];

   /// <summary>Foreign keys that must exist and be enforced.</summary>
   public IReadOnlyList<string> ForeignKeys { get; init; } = [];

   /// <summary>Unique constraints that must exist.</summary>
   public IReadOnlyList<string> UniqueConstraints { get; init; } = [];
}

/// <summary>
/// The SQL the engine cannot leave to the EF Core provider. Feature code states what it needs (a lock, a
/// schema check, the shape of a connection string) and each provider says how. A new database platform is one
/// more implementation here; feature code does not change.
/// </summary>
internal interface IEmSqlDialect
{
   /// <summary>The provider this dialect speaks for.</summary>
   DatabaseProvider Provider { get; }

   /// <summary>Folder of this provider's schema scripts, for messages telling the operator what to run.</summary>
   string ScriptFolder { get; }

   /// <summary>
   /// A statement selecting the rows of <paramref name="table"/> with an update lock, filtered by equality on
   /// <paramref name="columns"/> in order; the values are bound as <c>{0}</c>, <c>{1}</c> and so on. Run it in a
   /// transaction, the lock lasts until the end of that transaction.
   /// </summary>
   string LockRowsSql(string table, EmRowLock reach, IReadOnlyList<string> columns);

   /// <summary>Throws with <paramref name="message"/> when a required object is missing.</summary>
   Task VerifySchemaAsync(DbContext db, EmSchemaRequirement required, string message, CancellationToken ct);

   /// <summary>The database named by a connection string of this provider, empty when it names none.</summary>
   string DatabaseName(string connectionString);

   /// <summary>Why two connection strings cannot share one connection, or <c>null</c> when they can.</summary>
   string? SharedConnectionMismatch(string core, string module);
}

/// <summary>Finds the dialect of a provider or a context, and applies it to EF Core queries.</summary>
internal static class EmSqlDialects
{
   private static readonly IEmSqlDialect SqlServer = new SqlServerDialect();
   private static readonly IEmSqlDialect MySql = new MySqlDialect();

   /// <summary>The dialect of <paramref name="provider"/>, or <c>null</c> when the engine has none yet.</summary>
   public static IEmSqlDialect? TryFor(DatabaseProvider provider) => provider switch {
      DatabaseProvider.MicrosoftSqlServer => SqlServer,
      DatabaseProvider.MySql => MySql,
      _ => null
   };

   /// <summary>The dialect of the provider behind <paramref name="db"/>, or <c>null</c> when the engine has none yet.</summary>
   public static IEmSqlDialect? TryFor(DbContext db) {
      if (db.Database.IsSqlServer()) return SqlServer;
      return db.Database.IsMySql() ? MySql : null;
   }

   /// <summary>The dialect of the provider behind <paramref name="db"/>.</summary>
   /// <exception cref="NotSupportedException">The engine has no dialect for that provider.</exception>
   public static IEmSqlDialect For(DbContext db) => TryFor(db)
      ?? throw new NotSupportedException($"The engine has no SQL dialect for provider '{db.Database.ProviderName}'.");

   /// <summary>
   /// Selects rows of <paramref name="set"/> with an update lock held until the end of the current
   /// transaction. The result composes like any query; the provider keeps the lock when EF Core wraps it.
   /// </summary>
   /// <param name="set">The table.</param>
   /// <param name="reach">How far the lock reaches.</param>
   /// <param name="keys">Column names and values the rows must equal.</param>
   public static IQueryable<T> LockRows<T>(this DbSet<T> set, EmRowLock reach, params (string Column, object Value)[] keys) where T : class {
      var db = set.GetService<ICurrentDbContext>().Context;
      var table = set.EntityType.GetTableName()
         ?? throw new InvalidOperationException($"Entity '{typeof(T).Name}' is not mapped to a table.");
      var sql = For(db).LockRowsSql(table, reach, [.. keys.Select(k => k.Column)]);
      return set.FromSqlRaw(sql, [.. keys.Select(k => k.Value)]);
   }

   private sealed class SqlServerDialect : IEmSqlDialect
   {
      public DatabaseProvider Provider => DatabaseProvider.MicrosoftSqlServer;
      public string ScriptFolder => "doc/sqlscript/mssql";

      public string LockRowsSql(string table, EmRowLock reach, IReadOnlyList<string> columns) {
         var hint = reach == EmRowLock.Row ? "UPDLOCK, ROWLOCK" : "UPDLOCK, HOLDLOCK";
         return $"SELECT * FROM dbo.{table} WITH ({hint}) WHERE {string.Join(" AND ", columns.Select((c, i) => $"{c} = {{{i}}}"))}";
      }

      public async Task VerifySchemaAsync(DbContext db, EmSchemaRequirement required, string message, CancellationToken ct) {
         IEnumerable<string> missing = [
            .. required.ForeignKeys.Select(n => $"NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='{n}' AND is_disabled=0 AND is_not_trusted=0)"),
            .. required.Views.Select(n => $"OBJECT_ID('dbo.{n}','V') IS NULL"),
            .. required.Tables.Select(n => $"OBJECT_ID('dbo.{n}','U') IS NULL"),
            .. required.UniqueConstraints.Select(n => $"NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE name='{n}')")
         ];
         // Object names and the message come from engine code, never from a request.
         var sql = $"IF {string.Join(" OR ", missing)} THROW 51000,'{message.Replace("'", "''")}',1;";
         await db.Database.ExecuteSqlRawAsync(sql, ct);
      }

      public string DatabaseName(string connectionString) => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

      public string? SharedConnectionMismatch(string core, string module) {
         var a = new SqlConnectionStringBuilder(core);
         var b = new SqlConnectionStringBuilder(module);
         if (string.IsNullOrWhiteSpace(a.InitialCatalog) || string.IsNullOrWhiteSpace(b.InitialCatalog)) {
            return "a connection string names no database (Initial Catalog)";
         }

         if (!string.Equals(Server(a), Server(b), StringComparison.OrdinalIgnoreCase)) {
            return $"they are on different servers ('{a.DataSource}' and '{b.DataSource}')";
         }

         if (a.IntegratedSecurity != b.IntegratedSecurity ||
             !string.Equals(a.UserID, b.UserID, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(a.Password, b.Password, StringComparison.Ordinal)) {
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

   private sealed class MySqlDialect : IEmSqlDialect
   {
      public DatabaseProvider Provider => DatabaseProvider.MySql;
      public string ScriptFolder => "doc/sqlscript/mysql";

      // InnoDB takes a next-key lock under repeatable read and serializable, so a key that does not exist yet
      // is held as well: both reaches read the same.
      public string LockRowsSql(string table, EmRowLock reach, IReadOnlyList<string> columns) =>
         $"SELECT * FROM `{table}` WHERE {string.Join(" AND ", columns.Select((c, i) => $"`{c}` = {{{i}}}"))} FOR UPDATE";

      public async Task VerifySchemaAsync(DbContext db, EmSchemaRequirement required, string message, CancellationToken ct) {
         var missing = new List<string>();
         await AddMissingAsync("""
            SELECT TABLE_NAME AS Value FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
            """, required.Tables);
         await AddMissingAsync("""
            SELECT TABLE_NAME AS Value FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'VIEW'
            """, required.Views);
         await AddMissingAsync("""
            SELECT CONSTRAINT_NAME AS Value FROM information_schema.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_TYPE = 'FOREIGN KEY'
            """, required.ForeignKeys);
         await AddMissingAsync("""
            SELECT CONSTRAINT_NAME AS Value FROM information_schema.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_TYPE = 'UNIQUE'
            """, required.UniqueConstraints);
         if (missing.Count > 0) throw new InvalidOperationException($"{message} Missing: {string.Join(", ", missing)}.");

         async Task AddMissingAsync(string sql, IReadOnlyList<string> names) {
            if (names.Count == 0) return;
            var found = (await db.Database.SqlQueryRaw<string>(sql).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            missing.AddRange(names.Where(n => !found.Contains(n)));
         }
      }

      public string DatabaseName(string connectionString) => new MySqlConnectionStringBuilder(connectionString).Database;

      public string? SharedConnectionMismatch(string core, string module) {
         var a = new MySqlConnectionStringBuilder(core);
         var b = new MySqlConnectionStringBuilder(module);
         if (string.IsNullOrWhiteSpace(a.Database) || string.IsNullOrWhiteSpace(b.Database)) {
            return "a connection string names no database (Database)";
         }

         if (!string.Equals(a.Server, b.Server, StringComparison.OrdinalIgnoreCase) || a.Port != b.Port) {
            return $"they are on different servers ('{a.Server}:{a.Port}' and '{b.Server}:{b.Port}')";
         }

         if (!string.Equals(a.UserID, b.UserID, StringComparison.Ordinal) || !string.Equals(a.Password, b.Password, StringComparison.Ordinal)) {
            return "they log in differently";
         }

         return null;
      }
   }
}
