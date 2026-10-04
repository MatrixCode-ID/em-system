// Run from repo root. Fixtures live inside a transaction that is always rolled back.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Api.Core.Registry;
using Em.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using var config = JsonDocument.Parse(File.ReadAllText("../.artefacts/em-system/config/emapi-config.json"));
var connection = Environment.GetEnvironmentVariable("EM_DB_CONNECTION_STRING") ??
   config.RootElement.GetProperty("database").GetProperty("connectionString").GetString()!;
var assembly = typeof(CtnServices).Assembly;
var contextType = assembly.GetType("Em.Api.Core.Registry.CtnContext")!;
var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
options.UseSqlServer(connection);
await using var db = (DbContext)Activator.CreateInstance(contextType, options.Options)!;
var storeType = assembly.GetType("Em.Api.Core.Registry.CtnBlobStore")!;
// No filesystem writes: instantiate the store with a path only to mark it enabled.
var store = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.NonPublic,
   null, [Path.GetTempPath()], null)!;
var services = new ServiceCollection();
services.AddSingleton(contextType, db);
services.AddSingleton(storeType, store);
using var provider = services.BuildServiceProvider();
var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
typeof(EmApp).GetField("_rootServiceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);
typeof(EmApp).GetField("_httpContextAccessor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, new HttpContextAccessor());
var service = new CtnServices();
typeof(ServicesBase).GetProperty(nameof(ServicesBase.App))!.SetValue(service, app);
var checks = 0;
void Check(string label, bool ok) {
   if (!ok) throw new InvalidOperationException("FAIL " + label);
   checks++; Console.WriteLine("PASS " + label);
}
await using var transaction = await db.Database.BeginTransactionAsync();
try {
   var baseline = await service.GetMeta_CtnStorageSize();
   var expectedBlobs = await db.Database.SqlQuery<long>($"SELECT COALESCE(SUM(cCtnBlobSize),0) AS Value FROM dbo.ta_CtnBlob").SingleAsync();
   var expectedManifests = await db.Database.SqlQuery<long>($"SELECT COALESCE(SUM(cCtnManifestSize),0) AS Value FROM dbo.ta_CtnManifest").SingleAsync();
   Check("metadata totals match SQL", baseline.BlobBytes == expectedBlobs && baseline.ManifestBytes == expectedManifests);
   var root = Ulid.NewUlid().ToString();
   var image1 = Ulid.NewUlid().ToString();
   var image2 = Ulid.NewUlid().ToString();
   var blob = Ulid.NewUlid().ToString();
   var orphan = Ulid.NewUlid().ToString();
   var manifest = Ulid.NewUlid().ToString();
   var name = "storage-smoke-" + Guid.NewGuid().ToString("N")[..10];
   var digest = "sha256:" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
   const long largeSize = 5_000_000_000;
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnRoot(cCtnRootId,cCtnRootName,cCtnRootState,ustamp,datestamp) VALUES({root},{name},1,GETUTCDATE(),GETUTCDATE())");
   foreach (var image in new[] { image1, image2 })
      await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnImage(cCtnImageId,cCtnRootId,cCtnImageName,cCtnImageState,ustamp,datestamp) VALUES({image},{root},{image},1,GETUTCDATE(),GETUTCDATE())");
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnBlob(cCtnBlobId,cCtnBlobDigest,cCtnBlobSize,ustamp,datestamp) VALUES({blob},{digest},{largeSize},GETUTCDATE(),GETUTCDATE()),({orphan},{digest + "-orphan"},37,GETUTCDATE(),GETUTCDATE())");
   foreach (var image in new[] { image1, image2 })
      await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnBlobLink(cCtnImageId,cCtnBlobId,datestamp) VALUES({image},{blob},GETUTCDATE())");
   await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnManifest(cCtnManifestId,cCtnImageId,cCtnManifestDigest,cCtnManifestMediaType,cCtnManifestSize,cCtnManifestContent,ustamp,datestamp) VALUES({manifest},{image1},{digest},{"application/test"},101,{new byte[101]},GETUTCDATE(),GETUTCDATE())");
   foreach (var tag in new[] { "latest", "v1" })
      await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.ta_CtnTag(cCtnImageId,cCtnTagName,cCtnManifestId,ustamp,datestamp) VALUES({image1},{tag},{manifest},GETUTCDATE(),GETUTCDATE())");
   var actual = await service.GetMeta_CtnStorageSize();
   Check("shared blob counted once, retained blob included, 64-bit size", actual.BlobBytes - baseline.BlobBytes == largeSize + 37);
   Check("two tags do not multiply manifest size", actual.ManifestBytes - baseline.ManifestBytes == 101);
   Check("total combines blobs and manifests", actual.TotalBytes - baseline.TotalBytes == largeSize + 138);
   Check("DTO serializes total", JsonSerializer.Serialize(actual).Contains($"\"TotalBytes\":{actual.TotalBytes}"));
   var attribute = typeof(CtnServices).GetMethod(nameof(CtnServices.GetMeta_CtnStorageSize))!.GetCustomAttribute<GetActionAttribute>();
   Check("GET action requires Container Manager Access", attribute?.Claim == ICtnServices.CtnClaim);
   services = new ServiceCollection();
   services.AddSingleton(storeType, storeType.GetProperty("Disabled")!.GetValue(null)!);
   using var disabledProvider = services.BuildServiceProvider();
   typeof(EmApp).GetField("_rootServiceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, disabledProvider);
   try { await service.GetMeta_CtnStorageSize(); throw new InvalidOperationException("Expected 404"); }
   catch (ActionException ex) { Check("disabled registry returns 404", ex.StatusCode == 404); }
   Console.WriteLine($"{checks} checks passed.");
}
finally { await transaction.RollbackAsync(); }
