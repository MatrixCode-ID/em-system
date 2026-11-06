using System.Text;
using System.Text.RegularExpressions;
using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Api.Core.ObjectStorage.Volumes;
using Em.Api.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Em.Api.Core.IntegrationTests;

/// <summary>
/// Object storage on an empty database created from the real <c>tables/060-objectstore.sql</c> (plus a minimal
/// <c>ta_Meta</c>) and a temporary folder holding two volumes, <c>A</c> and <c>B</c>. Each test creates its own
/// policy and bucket, so tests in one class do not share counters.
/// </summary>
internal sealed class ObjectStoreFixture : IAsyncDisposable
{
   private readonly List<ObjectStorageDbContext> _contexts = [];

   private readonly bool _mySql;

   private ObjectStoreFixture(string connectionString, string folder, bool mySql = false) {
      _mySql = mySql;
      ConnectionString = connectionString;
      Folder = folder;
      Runtime = new ObjectStorageRuntime([new LocalFolderVolumeProvider(folder)]);
   }

   public string ConnectionString { get; }
   public string Folder { get; }
   public ObjectStorageRuntime Runtime { get; }
   public string VolumeA { get; private set; } = "";
   public string VolumeB { get; private set; } = "";

   public static async Task<ObjectStoreFixture> CreateAsync(SqlServerDatabase database, CancellationToken ct) {
      var connectionString = await database.CreateExtraDatabaseAsync(ct);
      await using (var connection = new SqlConnection(connectionString)) {
         await connection.OpenAsync(ct);
         await ExecuteBatchesAsync(connection, """
            CREATE TABLE dbo.ta_Meta(cMetaKey varchar(50) NOT NULL PRIMARY KEY, cMetaValue nvarchar(max) NOT NULL,
               cMetaDescription varchar(500) NULL, ustamp datetime NOT NULL);
            """, ct);
         var sql = Path.Combine(RepoRoot(), "doc", "sqlscript", "mssql");
         await ExecuteBatchesAsync(connection, await File.ReadAllTextAsync(Path.Combine(sql, "tables", "060-objectstore.sql"), ct), ct);
         // Running the script twice checks that it is idempotent.
         await ExecuteBatchesAsync(connection, await File.ReadAllTextAsync(Path.Combine(sql, "tables", "060-objectstore.sql"), ct), ct);
         foreach (var view in new[] { "vi_StoreBucket", "vi_StoreObject", "vi_StoreVolume" })
            await ExecuteBatchesAsync(connection, await File.ReadAllTextAsync(Path.Combine(sql, "views", view + ".sql"), ct), ct);
      }

      return await WithVolumesAsync(connectionString, false, ct);
   }

   /// <summary>The same fixture on a MySQL database that already holds the engine schema.</summary>
   public static Task<ObjectStoreFixture> CreateOnMySqlAsync(string connectionString, CancellationToken ct) =>
      WithVolumesAsync(connectionString, true, ct);

   private static async Task<ObjectStoreFixture> WithVolumesAsync(string connectionString, bool mySql, CancellationToken ct) {
      var folder = Path.Combine(Path.GetTempPath(), "em-objectstore-it-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(folder);
      var fixture = new ObjectStoreFixture(connectionString, folder, mySql);
      var db = fixture.NewContext();
      var now = DateTime.UtcNow;
      fixture.VolumeA = Ulid.NewUlid().ToString();
      fixture.VolumeB = Ulid.NewUlid().ToString();
      db.Volumes.Add(new ta_StoreVolume { cStoreVolumeId = fixture.VolumeA, cStoreVolumeName = "A", cStoreVolumeState = 1, cStoreVolumeType = 1, cStoreVolumePath = "A", ustamp = now, datestamp = now });
      db.Volumes.Add(new ta_StoreVolume { cStoreVolumeId = fixture.VolumeB, cStoreVolumeName = "B", cStoreVolumeState = 1, cStoreVolumeType = 1, cStoreVolumePath = "B", ustamp = now, datestamp = now });
      await db.SaveChangesAsync(ct);
      return fixture;
   }

   private static string RepoRoot() {
      for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
         if (File.Exists(Path.Combine(dir.FullName, "doc", "sqlscript", "mssql", "tables", "060-objectstore.sql"))) return dir.FullName;
      throw new InvalidOperationException("Repository root with doc/sqlscript was not found.");
   }

   private static async Task ExecuteBatchesAsync(SqlConnection connection, string script, CancellationToken ct) {
      foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)) {
         if (string.IsNullOrWhiteSpace(batch)) continue;
         await using var command = new SqlCommand(batch, connection);
         await command.ExecuteNonQueryAsync(ct);
      }
   }

   public ObjectStorageDbContext NewContext() {
      var options = new DbContextOptionsBuilder<ObjectStorageDbContext>();
      if (_mySql) options.UseEmProvider(DatabaseProvider.MySql, ConnectionString);
      else options.UseSqlServer(ConnectionString);
      var db = new ObjectStorageDbContext(options.Options);
      lock (_contexts) _contexts.Add(db);
      return db;
   }

   public ObjectStore NewStore() => new(NewContext(), Runtime);

   public ObjectStorageMaintenance NewMaintenance() => new(NewStore(), NullLogger<ObjectStorageMaintenance>.Instance);

   /// <summary>Creates a policy over the given volumes and a bucket using it; returns the bucket name.</summary>
   public async Task<string> AddBucketAsync(CancellationToken ct, int replicaMin = 1, int replicaTarget = 1, long? quotaBytes = null,
      long? quotaObjects = null, long? maxObjectBytes = null, int? keepVersions = null, params string[] volumes) {
      if (volumes.Length == 0) volumes = [VolumeA];
      var db = NewContext();
      var now = DateTime.UtcNow;
      var policyId = Ulid.NewUlid().ToString();
      db.Policies.Add(new ta_StorePolicy {
         cStorePolicyId = policyId, cStorePolicyName = "p-" + policyId, cStorePolicyState = 1, cStorePolicyReplicaMin = replicaMin,
         cStorePolicyReplicaTarget = replicaTarget, cStorePolicyQuotaBytes = quotaBytes, cStorePolicyQuotaObjects = quotaObjects,
         cStorePolicyMaxObjectBytes = maxObjectBytes, cStorePolicyKeepVersions = keepVersions, cStorePolicyAccess = 1, ustamp = now, datestamp = now
      });
      for (var i = 0; i < volumes.Length; i++)
         db.PolicyVolumes.Add(new ta_StorePolicyVolume { cStorePolicyId = policyId, cStoreVolumeId = volumes[i], cStorePolicyVolumeOrder = i + 1 });
      var name = "b-" + Guid.NewGuid().ToString("N")[..20];
      db.Buckets.Add(new ta_StoreBucket { cStoreBucketId = Ulid.NewUlid().ToString(), cStoreBucketName = name, cStorePolicyId = policyId, cStoreBucketState = 1, ustamp = now, datestamp = now });
      await db.SaveChangesAsync(ct);
      return name;
   }

   public static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

   public static async Task<string> ReadAsync(Stream stream, CancellationToken ct) {
      await using (stream) {
         using var reader = new StreamReader(stream);
         return await reader.ReadToEndAsync(ct);
      }
   }

   /// <summary>Path of a blob file on a volume of this fixture.</summary>
   public string BlobPath(string volumeFolder, string hashHex) => Path.Combine(Folder, volumeFolder, "blobs", hashHex[..2], hashHex[2..4], hashHex);

   public async ValueTask DisposeAsync() {
      foreach (var db in _contexts) await db.DisposeAsync();
      try {
         foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
         Directory.Delete(Folder, true);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
   }
}
