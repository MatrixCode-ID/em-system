namespace Em.Api.Shared
{
   /// <summary>
   /// One entry in the database connection registry (<see cref="EmAppBuilder.SetDbProvider"/> /
   /// <see cref="EmAppBuilder.AddExtraDbConn"/>): the connection string and provider that
   /// <see cref="EmAppBuilder.AddDbContext{TContext}(string)"/> and friends resolve
   /// <paramref name="Name"/> to.
   /// </summary>
   /// <param name="Name">
   /// The name the connection is registered and resolved by, e.g. <c>"MssqlNsmDb"</c>, or
   /// <see cref="EmAppBuilder.DefaultConnectionName"/> for the one set through
   /// <see cref="EmAppBuilder.SetDbProvider"/>.
   /// </param>
   /// <param name="ConnectionString">The provider-specific connection string.</param>
   /// <param name="Provider">Which database engine <paramref name="ConnectionString"/> connects to.</param>
   internal sealed record DbConnectionInfo(string Name, string ConnectionString, DatabaseProvider Provider);
}
