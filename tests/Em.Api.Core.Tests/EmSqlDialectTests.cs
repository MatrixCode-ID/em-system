using Em.Api.Core.Approval;
using Em.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Tests;

/// <summary>The SQL and the connection rules each dialect produces, without a database.</summary>
public class EmSqlDialectTests
{
   private static readonly IEmSqlDialect SqlServer = EmSqlDialects.TryFor(DatabaseProvider.MicrosoftSqlServer)!;
   private static readonly IEmSqlDialect MySql = EmSqlDialects.TryFor(DatabaseProvider.MySql)!;

   private sealed class ModuleContextMarker;

   [Fact]
   public void SqlServerKeepsTheLockHintsOfEachReach() {
      Assert.Equal("SELECT * FROM dbo.ta_Bucket WITH (UPDLOCK, ROWLOCK) WHERE cId = {0}",
         SqlServer.LockRowsSql("ta_Bucket", EmRowLock.Row, ["cId"]));
      Assert.Equal("SELECT * FROM dbo.ta_Object WITH (UPDLOCK, HOLDLOCK) WHERE cBucketId = {0} AND cHash = {1}",
         SqlServer.LockRowsSql("ta_Object", EmRowLock.Range, ["cBucketId", "cHash"]));
   }

   [Fact]
   public void MySqlLocksWithForUpdate() {
      Assert.Equal("SELECT * FROM `ta_Object` WHERE `cBucketId` = {0} AND `cHash` = {1} FOR UPDATE",
         MySql.LockRowsSql("ta_Object", EmRowLock.Range, ["cBucketId", "cHash"]));
   }

   [Fact]
   public void PostgreSqlHasNoDialectYet() => Assert.Null(EmSqlDialects.TryFor(DatabaseProvider.PostgreSql));

   [Fact]
   public void ReadsTheDatabaseNameOfEachProvider() {
      Assert.Equal("core", SqlServer.DatabaseName("Server=tcp:host,1433;Database=core;User ID=a;Password=b"));
      Assert.Equal("core", MySql.DatabaseName("Server=host;Port=3306;Database=core;User ID=a;Password=;"));
   }

   [Theory]
   [InlineData("Server=host;Database=a;User ID=u;Password=p", "Server=tcp:host;Database=b;User ID=u;Password=p", null)]
   [InlineData("Server=host;Database=a;User ID=u;Password=p", "Server=other;Database=b;User ID=u;Password=p", "different servers")]
   [InlineData("Server=host;Database=a;User ID=u;Password=p", "Server=host;Database=b;User ID=v;Password=p", "log in differently")]
   [InlineData("Server=host;User ID=u;Password=p", "Server=host;Database=b;User ID=u;Password=p", "no database")]
   public void SqlServerSharedConnectionRules(string core, string module, string? expected) =>
      AssertMismatch(SqlServer.SharedConnectionMismatch(core, module), expected);

   [Theory]
   [InlineData("Server=host;Database=a;User ID=u;Password=p", "Server=HOST;Database=b;User ID=u;Password=p", null)]
   [InlineData("Server=host;Port=3306;Database=a;User ID=u", "Server=host;Port=3307;Database=b;User ID=u", "different servers")]
   [InlineData("Server=host;Database=a;User ID=u;Password=p", "Server=host;Database=b;User ID=u;Password=q", "log in differently")]
   [InlineData("Server=host;User ID=u", "Server=host;Database=b;User ID=u", "no database")]
   public void MySqlSharedConnectionRules(string core, string module, string? expected) =>
      AssertMismatch(MySql.SharedConnectionMismatch(core, module), expected);

   private static void AssertMismatch(string? actual, string? expected) {
      if (expected is null) Assert.Null(actual);
      else Assert.Contains(expected, actual);
   }

   private static EmAppBuilder Builder(string coreConnection, DatabaseProvider core, string moduleConnection, DatabaseProvider module) {
      var builder = new EmAppBuilder { Services = new ServiceCollection() };
      builder.SetDbProvider(coreConnection, core);
      builder.DbConnections["module"] = new DbConnectionInfo("module", moduleConnection, module);
      builder.DbContextConnectionNames[typeof(ModuleContextMarker)] = "module";
      return builder;
   }

   [Fact]
   public void DataApprovalAcceptsOneProviderOnOneServer() {
      ApprovalStartupChecks.VerifyDatabases(Builder("Server=h;Database=a;User ID=u", DatabaseProvider.MySql,
         "Server=h;Database=b;User ID=u", DatabaseProvider.MySql), [typeof(ModuleContextMarker)]);
      ApprovalStartupChecks.VerifyDatabases(Builder("Server=h;Database=a;User ID=u;Password=p", DatabaseProvider.MicrosoftSqlServer,
         "Server=h;Database=b;User ID=u;Password=p", DatabaseProvider.MicrosoftSqlServer), [typeof(ModuleContextMarker)]);
   }

   [Fact]
   public void DataApprovalRefusesTwoProvidersAtStartup() {
      var ex = Assert.Throws<InvalidOperationException>(() => ApprovalStartupChecks.VerifyDatabases(
         Builder("Server=h;Database=a;User ID=u", DatabaseProvider.MySql, "Server=h;Database=b;User ID=u", DatabaseProvider.MicrosoftSqlServer),
         [typeof(ModuleContextMarker)]));
      Assert.Contains("cannot span two providers", ex.Message);
   }

   [Fact]
   public void DataApprovalRefusesAProviderWithoutDialect() {
      var ex = Assert.Throws<InvalidOperationException>(() => ApprovalStartupChecks.VerifyDatabases(
         Builder("Host=h;Database=a", DatabaseProvider.PostgreSql, "Host=h;Database=b", DatabaseProvider.PostgreSql), [typeof(ModuleContextMarker)]));
      Assert.Contains("does not support", ex.Message);
   }

   [Fact]
   public void RecognizesAConflictWrappedByEfCore() {
      var missing = new InvalidOperationException("outer", new InvalidOperationException("inner"));
      Assert.False(EmDbErrors.IsUniqueConflict(missing));
      Assert.False(EmDbErrors.IsMissingTable(missing));
   }
}
