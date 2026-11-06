using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Api.Core.Smtp;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Em.Api.Core.IntegrationTests;

/// <summary>
/// What the engine leaves to its dialect, on a real MySQL server: locks that hold while EF Core composes over
/// them, schema checks, error recognition, and the features built on them.
/// </summary>
public sealed class MySqlRuntimeTests(MySqlEngineDatabase database) : IClassFixture<MySqlEngineDatabase>, IAsyncLifetime
{
   private const int LockWaitTimeout = 1205;
   private static CancellationToken Ct => TestContext.Current.CancellationToken;
   private readonly List<ApiCoreContext> _contexts = [];
   private string _connection = "";

   public async ValueTask InitializeAsync() => _connection = await database.CreateFullAsync(Ct);

   public async ValueTask DisposeAsync() {
      foreach (var db in _contexts) await db.DisposeAsync();
   }

   private ApiCoreContext Core() {
      var options = new DbContextOptionsBuilder<ApiCoreContext>();
      options.UseEmProvider(DatabaseProvider.MySql, _connection);
      var db = new ApiCoreContext(options.Options);
      _contexts.Add(db);
      return db;
   }

   private async Task AddMetaAsync(string key, string value = "value") {
      var db = Core();
      db.ta_Metas.Add(new ta_Meta { cMetaKey = key, cMetaValue = value, ustamp = DateTime.UtcNow });
      await db.SaveChangesAsync(Ct);
   }

   // Runs a statement from another connection that gives up waiting after one second; true when it had to.
   private async Task<bool> IsBlockedAsync(string sql) {
      await using var connection = new MySqlConnection(_connection);
      await connection.OpenAsync(Ct);
      await using (var wait = new MySqlCommand("SET innodb_lock_wait_timeout = 1", connection)) await wait.ExecuteNonQueryAsync(Ct);
      try {
         await using var command = new MySqlCommand(sql, connection);
         await command.ExecuteNonQueryAsync(Ct);
         return false;
      }
      catch (MySqlException ex) when (ex.Number == LockWaitTimeout) {
         return true;
      }
   }

   [Fact]
   public async Task LockHoldsWhenEfCoreWrapsItAsTheObjectStoreDoes() {
      await AddMetaAsync("lock.row");
      var db = Core();
      await using var tx = await db.Database.BeginTransactionAsync(Ct);
      var row = await db.ta_Metas.LockRows(EmRowLock.Row, (nameof(ta_Meta.cMetaKey), "lock.row")).AsTracking().SingleOrDefaultAsync(Ct);
      Assert.NotNull(row);
      Assert.True(await IsBlockedAsync("UPDATE ta_Meta SET cMetaValue = 'other' WHERE cMetaKey = 'lock.row'"));
      await tx.RollbackAsync(Ct);
      Assert.False(await IsBlockedAsync("UPDATE ta_Meta SET cMetaValue = 'other' WHERE cMetaKey = 'lock.row'"));
   }

   [Fact]
   public async Task LockHoldsWhenAPredicateIsComposedAsApprovalDoes() {
      await AddMetaAsync("lock.composed");
      var db = Core();
      await using var tx = await db.Database.BeginTransactionAsync(Ct);
      var row = await db.ta_Metas.LockRows(EmRowLock.Range, (nameof(ta_Meta.cMetaKey), "lock.composed"))
         .AsNoTracking().SingleOrDefaultAsync(x => x.cMetaKey == "lock.composed", Ct);
      Assert.NotNull(row);
      Assert.True(await IsBlockedAsync("UPDATE ta_Meta SET cMetaValue = 'other' WHERE cMetaKey = 'lock.composed'"));
   }

   [Fact]
   public async Task LockLeavesOtherRowsFree() {
      await AddMetaAsync("lock.mine");
      await AddMetaAsync("lock.yours");
      var db = Core();
      await using var tx = await db.Database.BeginTransactionAsync(Ct);
      await db.ta_Metas.LockRows(EmRowLock.Row, (nameof(ta_Meta.cMetaKey), "lock.mine")).SingleAsync(Ct);
      Assert.False(await IsBlockedAsync("UPDATE ta_Meta SET cMetaValue = 'other' WHERE cMetaKey = 'lock.yours'"));
   }

   [Fact]
   public async Task RangeLockHoldsAKeyThatDoesNotExistYet() {
      var db = Core();
      await using var tx = await db.Database.BeginTransactionAsync(Ct);
      Assert.Null(await db.ta_Metas.LockRows(EmRowLock.Range, (nameof(ta_Meta.cMetaKey), "lock.absent")).SingleOrDefaultAsync(Ct));
      Assert.True(await IsBlockedAsync("INSERT INTO ta_Meta (cMetaKey, cMetaValue, ustamp) VALUES ('lock.absent', 'x', UTC_TIMESTAMP(3))"));
   }

   [Fact]
   public async Task SchemaCheckPassesOnTheInstalledSchemaAndNamesWhatIsMissing() {
      var db = Core();
      var dialect = EmSqlDialects.For(db);
      await dialect.VerifySchemaAsync(db, new EmSchemaRequirement {
         Tables = ["ta_Meta"], Views = ["vi_NuPakFeed"], ForeignKeys = ["FK_NuPakAuditFeed"], UniqueConstraints = ["UQ_NuPakFeedSlug"]
      }, "Schema is incomplete.", Ct);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dialect.VerifySchemaAsync(db, new EmSchemaRequirement {
         Tables = ["ta_Missing"], Views = ["ta_Meta"], ForeignKeys = ["FK_Missing"], UniqueConstraints = ["UQ_Missing"]
      }, "Schema is incomplete.", Ct));
      Assert.StartsWith("Schema is incomplete.", ex.Message);
      foreach (var name in new[] { "ta_Missing", "ta_Meta", "FK_Missing", "UQ_Missing" }) Assert.Contains(name, ex.Message);
   }

   [Fact]
   public async Task DuplicateKeyAndMissingTableAreRecognized() {
      await AddMetaAsync("error.duplicate");
      var db = Core();
      db.ta_Metas.Add(new ta_Meta { cMetaKey = "error.duplicate", cMetaValue = "x", ustamp = DateTime.UtcNow });
      var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
      Assert.True(EmDbErrors.IsUniqueConflict(duplicate));
      Assert.False(EmDbErrors.IsMissingTable(duplicate));

      var missing = await Assert.ThrowsAsync<MySqlException>(() => Core().Database.ExecuteSqlRawAsync("SELECT 1 FROM ta_DoesNotExist", Ct));
      Assert.True(EmDbErrors.IsMissingTable(missing));
   }

   private static ta_Tenant Row(string code) => new() { cTenantCode = code, cTenantName = "Tenant " + code, cTenantOrder = -1 };

   private static ta_Tenant Copy(vi_Tenant t, string name) => new() {
      cTenantId = t.cTenantId, cTenantName = name, cTenantCode = t.cTenantCode, cTenantState = t.cTenantState,
      cTenantOrder = t.cTenantOrder, ustamp = t.ustamp, datestamp = t.datestamp
   };

   [Fact]
   public async Task TenantCodeConflictAnswers409() {
      await new TenantServices(Core()).PostGetTa_Tenant_New(Row("dup.example"));
      var ex = await Assert.ThrowsAsync<ActionException>(() => new TenantServices(Core()).PostGetTa_Tenant_New(Row("dup.example")));
      Assert.Equal(409, ex.StatusCode);
   }

   [Fact]
   public async Task StaleCheckRejectsOldStampAndAcceptsTheFreshOne() {
      var tenant = await new TenantServices(Core()).PostGetTa_Tenant_New(Row("stale.example"));
      var saved = await new TenantServices(Core()).PostGetTa_Tenant_Update(Copy(tenant, "First"));
      Assert.Equal("First", saved.cTenantName);

      var ex = await Assert.ThrowsAsync<ActionException>(() => new TenantServices(Core()).PostGetTa_Tenant_Update(Copy(tenant, "Stale")));
      Assert.Equal(409, ex.StatusCode);

      var fresh = await new TenantServices(Core()).PostGetTa_Tenant_Update(Copy(saved, "Second"));
      Assert.Equal("Second", fresh.cTenantName);
   }

   private sealed class Factory(string connection) : IDbContextFactory<ApiCoreContext>
   {
      public ApiCoreContext CreateDbContext() {
         var options = new DbContextOptionsBuilder<ApiCoreContext>();
         options.UseEmProvider(DatabaseProvider.MySql, connection);
         return new ApiCoreContext(options.Options);
      }
   }

   [Fact]
   public async Task SmtpProfilesSaveAndReadBackUnderTheirLock() {
      var store = new SmtpSettingsStore(new Factory(_connection));
      var saved = await store.SaveProfileAsync(new() { Name = "relay", Settings = new() {
         Enabled = true, Host = "relay.example.com", Port = 25, Security = SmtpSecurity.None, Authenticate = false
      } }, Ct);
      var listed = Assert.Single((await store.ProfilesAsync(Ct)).Profiles);
      Assert.Equal(saved.Id, listed.Id);
   }

   [Fact]
   public async Task LikeIgnoresCaseAndAccentsUnderTheDefaultCollation() {
      await AddMetaAsync("like.value", "Alpha value");
      var db = Core();
      Assert.Equal(1, await db.ta_Metas.CountAsync(x => x.cMetaKey == "like.value" && EF.Functions.Like(x.cMetaValue, "%ALPHA%"), Ct));
      Assert.Equal(1, await db.ta_Metas.CountAsync(x => x.cMetaKey == "like.value" && EF.Functions.Like(x.cMetaValue, "%álpha%"), Ct));
   }

   [Fact]
   public async Task ConcurrentUploadsOfOneKeySerializeOnTheBucket() {
      await using var fx = await ObjectStoreFixture.CreateOnMySqlAsync(_connection, Ct);
      var bucket = await fx.AddBucketAsync(Ct, replicaTarget: 2, volumes: [fx.VolumeA, fx.VolumeB]);
      var results = await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
         fx.NewStore().PutAsync(bucket, "same-key", ObjectStoreFixture.Content("content " + i), ct: Ct)));

      Assert.Equal([1, 2, 3, 4, 5, 6], results.Select(r => r.Version).Order());
      var db = fx.NewContext();
      var row = await db.Buckets.SingleAsync(b => b.cStoreBucketName == bucket, Ct);
      Assert.Equal(1, row.cStoreBucketObjects);
      Assert.Equal(6, await db.Versions.CountAsync(Ct));
   }

   [Fact]
   public async Task ReplicationQueriesRunWithoutASchemaPrefix() {
      await using var fx = await ObjectStoreFixture.CreateOnMySqlAsync(_connection, Ct);
      var bucket = await fx.AddBucketAsync(Ct, replicaTarget: 2, volumes: [fx.VolumeA, fx.VolumeB]);
      await fx.NewStore().PutAsync(bucket, "replicate", ObjectStoreFixture.Content("replicate me"), ct: Ct);

      var maintenance = fx.NewMaintenance();
      Assert.Equal(1, await maintenance.CountUnderReplicatedAsync(Ct));
      await maintenance.ReplicateAsync(false, null, Ct);
      Assert.Equal(0, await fx.NewMaintenance().CountUnderReplicatedAsync(Ct));
   }
}
