using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Database switcher for contexts that share one connection inside an approval transaction: before
   /// each command runs, the connection is moved to the database that belongs to the context running it.
   /// </summary>
   /// <remarks>
   /// One connection is in only one database at a time, while a module's model uses table names without a
   /// database name. That is why the switch is not done once when the turn is handed to the module, but on
   /// every command: a module handler touching two contexts in different databases is still correct, and
   /// a core context command after the module's turn does not go to the wrong place. A context not
   /// registered here is not touched at all, so outside an approval transaction this interceptor does
   /// nothing.
   /// </remarks>
   internal sealed class ApprovalDatabaseSwitch : DbCommandInterceptor
   {
      /// <summary>The single instance; attached to every SQL Server context created by the engine.</summary>
      public static ApprovalDatabaseSwitch Instance { get; } = new();

      // Weak: a context lives as long as its request scope, and nothing here may keep it alive longer.
      private static readonly ConditionalWeakTable<DbContext, Attachment> Attachments = new();

      /// <summary>The database that belongs to that context, and the shared connection it is currently using.</summary>
      internal sealed class Attachment(string database, DbConnection connection)
      {
         /// <summary>Database where this context's model lives.</summary>
         public string Database { get; } = database;

         /// <summary>The shared connection already attached to this context.</summary>
         public DbConnection Connection { get; set; } = connection;
      }

      /// <summary>
      /// The database of a context that was attached before, or <c>null</c> when it never was. Recorded from
      /// the first attachment because afterwards that context uses the shared connection, whose connection
      /// string belongs to the core context.
      /// </summary>
      public static Attachment? Find(DbContext context) =>
         Attachments.TryGetValue(context, out var attachment) ? attachment : null;

      /// <summary>Records that <paramref name="context"/> uses <paramref name="connection"/>.</summary>
      public static void Attach(DbContext context, string database, DbConnection connection) {
         if (Attachments.TryGetValue(context, out var attachment)) {
            attachment.Connection = connection;
            return;
         }

         Attachments.Add(context, new Attachment(database, connection));
      }

      public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<DbDataReader> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<DbDataReader> result,
         CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<int> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
         InterceptionResult<object> result) {
         Ensure(command, eventData);
         return result;
      }

      public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
         CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) {
         await EnsureAsync(command, eventData, cancellationToken);
         return result;
      }

      private static void Ensure(DbCommand command, CommandEventData eventData) {
         if (TargetOf(command, eventData) is { } database) {
            command.Connection!.ChangeDatabase(database);
         }
      }

      private static async Task EnsureAsync(DbCommand command, CommandEventData eventData, CancellationToken ct) {
         if (TargetOf(command, eventData) is { } database) {
            await command.Connection!.ChangeDatabaseAsync(database, ct);
         }
      }

      // The database the command has to run in, or null when the connection is already there or the
      // context is not part of an approval transaction.
      private static string? TargetOf(DbCommand command, CommandEventData eventData) {
         if (eventData.Context is not { } context || command.Connection is not { } connection) return null;
         if (!Attachments.TryGetValue(context, out var attachment)) return null;

         return string.Equals(connection.Database, attachment.Database, StringComparison.OrdinalIgnoreCase)
            ? null
            : attachment.Database;
      }
   }

   /// <summary>
   /// Transaction of one approval decision spanning the core database and the module's database: one
   /// connection, one SQL Server transaction, no MSDTC.
   /// </summary>
   /// <remarks>
   /// Without this, a module hook that writes to its module tables runs on its own connection: a failure
   /// midway leaves the document changed while the request is still waiting, or the request completed
   /// while its change never went in. The contexts that take part are those the module's service asks for
   /// through its constructor (see <see cref="ApprovalFlowDeclaration.ModuleDbContextTypes"/>); module
   /// authors do not deal with connections at all. The limit is SQL Server only, and all databases taking
   /// part must be on one server with one login; <see cref="ApprovalStartupChecks"/> refuses an
   /// application that violates this before it accepts the first request.
   /// <para>
   /// Module contexts keep using that shared connection until the end of the call, including for hooks
   /// after the commit; the database switcher in <see cref="ApprovalDatabaseSwitch"/> keeps every command
   /// landing in the right database. All that is released at the end of the transaction is the transaction.
   /// </para>
   /// </remarks>
   internal sealed class ApprovalTransaction : IAsyncDisposable
   {
      private readonly IDbContextTransaction _transaction;
      private readonly List<DbContext> _enlisted;

      private ApprovalTransaction(IDbContextTransaction transaction, List<DbContext> enlisted) {
         _transaction = transaction;
         _enlisted = enlisted;
      }

      /// <summary>
      /// Opens a transaction on the core database, then brings in the module contexts of
      /// <paramref name="flow"/>.
      /// </summary>
      /// <param name="core">The core database context.</param>
      /// <param name="flow">The flow being worked on; its module contexts are known from it.</param>
      /// <param name="provider">The service provider of this call.</param>
      /// <param name="ct">Cancellation of the work.</param>
      public static async Task<ApprovalTransaction> BeginAsync(ApiCoreContext core, ApprovalFlowDeclaration flow,
         IServiceProvider provider, CancellationToken ct = default) {
         var transaction = await core.Database.BeginTransactionAsync(ct);
         var enlisted = new List<DbContext>();

         try {
            if (core.Database.GetDbConnection() is SqlConnection connection) {
               var coreDatabase = CatalogOf(connection.ConnectionString);
               ApprovalDatabaseSwitch.Attach(core, coreDatabase, connection);

               foreach (var type in flow.ModuleDbContextTypes) {
                  if (provider.GetRequiredService(type) is not DbContext module || ReferenceEquals(module, core)) continue;

                  if (!module.Database.IsSqlServer()) {
                     throw new InvalidOperationException(
                        $"Context {type.Name} of approval flow '{flow.DocType}' is not on SQL Server, so it cannot share the approval transaction.");
                  }

                  // Seen before in this request: its connection string is the shared one by now, so the
                  // database it owns is the one written down the first time.
                  var own = ApprovalDatabaseSwitch.Find(module)?.Database ??
                            CatalogOf(module.Database.GetConnectionString() ?? string.Empty);

                  if (!ReferenceEquals(ApprovalDatabaseSwitch.Find(module)?.Connection, connection)) {
                     module.Database.SetDbConnection(connection);
                  }

                  await module.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
                  ApprovalDatabaseSwitch.Attach(module, own, connection);

                  // Whatever an earlier decision of the same request left tracked is not what is on the
                  // tables now; a decision starts from what it reads itself.
                  module.ChangeTracker.Clear();
                  enlisted.Add(module);
               }
            }
         }
         catch {
            await ReleaseAsync(enlisted);
            await transaction.DisposeAsync();
            throw;
         }

         return new ApprovalTransaction(transaction, enlisted);
      }

      /// <summary>Saves all changes in every database that takes part.</summary>
      public Task CommitAsync(CancellationToken ct = default) => _transaction.CommitAsync(ct);

      /// <summary>
      /// Rolls back all changes in every database that takes part, and discards what the module contexts
      /// track: entities added or changed there no longer exist in the database, and must not be saved by the
      /// next decision in the same call.
      /// </summary>
      public async Task RollbackAsync(CancellationToken ct = default) {
         try {
            await _transaction.RollbackAsync(ct);
         }
         finally {
            foreach (var module in _enlisted) module.ChangeTracker.Clear();
         }
      }

      /// <inheritdoc />
      public async ValueTask DisposeAsync() {
         await ReleaseAsync(_enlisted);
         await _transaction.DisposeAsync();
      }

      // A context left holding a finished transaction would refuse the next command it runs.
      private static async Task ReleaseAsync(List<DbContext> enlisted) {
         foreach (var module in enlisted) {
            await module.Database.UseTransactionAsync(null);
         }
      }

      private static string CatalogOf(string connectionString) =>
         new SqlConnectionStringBuilder(connectionString).InitialCatalog;
   }
}
