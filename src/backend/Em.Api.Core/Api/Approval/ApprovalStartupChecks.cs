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
      /// database: one provider with a dialect, one server, one login.
      /// </summary>
      /// <param name="contextTypes">Only contexts owning registered approval entities.</param>
      /// <param name="builder">The application builder, the source of connection and context registrations.</param>
      /// <exception cref="InvalidOperationException">Thrown when a module context cannot take part.</exception>
      public static void VerifyDatabases(EmAppBuilder builder, Type[] contextTypes) {
         if (contextTypes.Length == 0) return;
         if (!builder.DbConnections.TryGetValue(EmAppBuilder.DefaultConnectionName, out var core))
            throw new InvalidOperationException("Data Approval requires the default database connection.");
         var dialect = EmSqlDialects.TryFor(core.Provider)
            ?? throw new InvalidOperationException($"Data Approval does not support provider '{core.Provider}'.");
         foreach (var type in contextTypes) {
            var connectionName = builder.DbContextConnectionNames[type];
            var module = builder.DbConnections[connectionName];
            if (module.Provider != core.Provider) {
               throw new InvalidOperationException(
                  $"Approval context '{type}' uses {module.Provider} while the core database uses {core.Provider}; one transaction cannot span two providers.");
            }
            var problem = dialect.SharedConnectionMismatch(core.ConnectionString, module.ConnectionString);
            if (problem is not null) throw new InvalidOperationException($"Approval context '{type}' cannot share the core transaction: {problem}.");
         }
      }
   }
}
