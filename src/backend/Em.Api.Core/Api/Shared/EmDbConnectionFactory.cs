using System.Data;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace Em.Api.Shared
{
   /// <summary>
   /// Creates ad-hoc <see cref="IDbConnection"/> instances for a named connection from the registry
   /// (<see cref="EmAppBuilder.SetDbProvider"/> / <see cref="EmAppBuilder.AddExtraDbConn"/>). Meant
   /// for callers that need their own connection outside the ambient scoped one - e.g. queries run
   /// concurrently via <c>Task.WhenAll</c>, where sharing the per-request connection would collide.
   /// Whoever calls <see cref="Create"/> directly also owns closing what it returns.
   /// </summary>
   public interface IEmDbConnectionFactory
   {
      /// <summary>
      /// Every connection name currently registered, <see cref="EmAppBuilder.DefaultConnectionName"/>
      /// included.
      /// </summary>
      IReadOnlyCollection<string> ConnectionNames { get; }

      /// <summary>
      /// Creates a new, unopened connection for <paramref name="connectionName"/>
      /// (<see cref="EmAppBuilder.DefaultConnectionName"/> when omitted).
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Thrown when <paramref name="connectionName"/> is not a registered connection, naming the
      /// connections that are.
      /// </exception>
      IDbConnection Create(string connectionName = EmAppBuilder.DefaultConnectionName);
   }

   internal sealed class EmDbConnectionFactory(IReadOnlyDictionary<string, DbConnectionInfo> connections)
      : IEmDbConnectionFactory
   {
      public IReadOnlyCollection<string> ConnectionNames => connections.Keys.ToArray();

      public IDbConnection Create(string connectionName = EmAppBuilder.DefaultConnectionName) {
         if (!connections.TryGetValue(connectionName, out var info)) {
            throw new InvalidOperationException(
               $"No database connection named '{connectionName}' is registered. Registered connections: {DescribeKnownConnections(connections.Keys)}.");
         }

         return CreateConnection(info.Provider, info.ConnectionString);
      }

      internal static string DescribeKnownConnections(IEnumerable<string> names) {
         var known = string.Join(", ", names.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
         return known.Length == 0 ? "(none)" : known;
      }

      internal static IDbConnection CreateConnection(DatabaseProvider provider, string connectionString) {
         return provider switch {
            DatabaseProvider.MicrosoftSqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(connectionString),
            DatabaseProvider.PostgreSql => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException($"Unhandled database provider '{provider}'."),
         };
      }
   }
}
