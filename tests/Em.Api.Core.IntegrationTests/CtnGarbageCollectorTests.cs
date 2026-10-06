using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Registry;
using Em.Shared;

namespace Em.Api.Core.IntegrationTests
{
   public class CtnGarbageCollectorTests(SqlServerDatabase database) : IAsyncDisposable
   {
      private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
      private static readonly DateTime Old = Now.AddHours(-48);
      private static readonly DateTime Young = Now.AddHours(-1);
      private const string OciManifest = "application/vnd.oci.image.manifest.v1+json";

      private RegistryFixture? fixture;

      public async ValueTask DisposeAsync() {
         if (fixture is not null) await fixture.DisposeAsync();
      }

      private async Task<RegistryFixture> CreateAsync(CancellationToken ct) =>
         fixture = await RegistryFixture.CreateAsync(database, ct);

      private static CtnGarbageCollector Collector(RegistryFixture f) => new(f.Db, f.Store);

      [Fact]
      public async Task DryRun_ReportsOrphan_WithoutDeleting() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var blob = await f.AddBlobAsync(Old, ct);

         var report = await Collector(f).RunAsync(24, true, Now, ct);

         Assert.True(report.DryRun);
         Assert.Equal(1, report.BlobCount);
         Assert.Equal(blob.Size, report.BlobBytes);
         Assert.Equal(blob.Digest, Assert.Single(report.Blobs).Digest);
         Assert.True(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
         Assert.True(File.Exists(f.Store.BlobPath(blob.Digest)));
      }

      [Fact]
      public async Task Run_DeletesOrphanRowLinkAndFile() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var blob = await f.AddBlobAsync(Old, ct);
         await f.AddLinkAsync(image, blob.Id, Old, ct);

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.False(report.DryRun);
         Assert.Equal(1, report.BlobCount);
         Assert.Equal(blob.Size, report.BlobBytes);
         Assert.False(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
         Assert.False(await f.Db.BlobLinks.AnyAsync(l => l.cCtnBlobId == blob.Id, ct));
         Assert.False(File.Exists(f.Store.BlobPath(blob.Digest)));
      }

      [Fact]
      public async Task Run_KeepsBlobReferencedByManifest() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var blob = await f.AddBlobAsync(Old, ct);
         await f.AddLinkAsync(image, blob.Id, Old, ct);
         await f.AddManifestAsync(image, OciManifest, "{\"m\":1}", [blob.Id], ["latest"], Old, ct);

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.Equal(0, report.BlobCount);
         Assert.True(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
         Assert.True(File.Exists(f.Store.BlobPath(blob.Digest)));
      }

      [Fact]
      public async Task Run_KeepsYoungBlob() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var blob = await f.AddBlobAsync(Young, ct);

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.Equal(0, report.BlobCount);
         Assert.True(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
         Assert.True(File.Exists(f.Store.BlobPath(blob.Digest)));
      }

      [Fact]
      public async Task Run_KeepsOldBlobWithFreshLink() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var blob = await f.AddBlobAsync(Old, ct);
         await f.AddLinkAsync(image, blob.Id, Young, ct);

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.Equal(0, report.BlobCount);
         Assert.True(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
         Assert.True(await f.Db.BlobLinks.AnyAsync(l => l.cCtnBlobId == blob.Id, ct));
      }

      [Fact]
      public async Task DryRun_ListsLinkedImages() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var blob = await f.AddBlobAsync(Old, ct);
         await f.AddLinkAsync(image, blob.Id, Old, ct);

         var report = await Collector(f).RunAsync(24, true, Now, ct);

         Assert.Equal(["acme/api"], Assert.Single(report.Blobs).LinkedImages);
      }

      [Fact]
      public async Task Run_RemovesStaleUpload_KeepsFreshUpload() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var stale = await f.AddUploadAsync(image, Old, true, ct);
         var fresh = await f.AddUploadAsync(image, Young, true, ct);

         var dry = await Collector(f).RunAsync(24, true, Now, ct);
         Assert.Equal(1, dry.StaleUploadCount);
         Assert.True(await f.Db.Uploads.AnyAsync(u => u.cCtnUploadId == stale, ct));

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.Equal(1, report.StaleUploadCount);
         Assert.Equal(4, report.StaleUploadBytes);
         Assert.False(await f.Db.Uploads.AnyAsync(u => u.cCtnUploadId == stale, ct));
         Assert.False(File.Exists(f.Store.UploadPath(stale)));
         Assert.True(await f.Db.Uploads.AnyAsync(u => u.cCtnUploadId == fresh, ct));
         Assert.True(File.Exists(f.Store.UploadPath(fresh)));
      }

      [Fact]
      public async Task Run_RemovesOldOrphanFiles_KeepsYoungAndUnexpected() {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);
         var oldFile = await f.AddBlobAsync(Old, ct, writeRow: false);
         var youngFile = await f.AddBlobAsync(Young, ct, writeRow: false);
         var weird = Path.Combine(f.Folder, "blobs", "sha256", "zz", "not-a-digest");
         Directory.CreateDirectory(Path.GetDirectoryName(weird)!);
         await File.WriteAllBytesAsync(weird, [1], ct);
         File.SetLastWriteTimeUtc(weird, Old);
         var oldUpload = await f.AddLooseUploadFileAsync(Old, ct);
         var youngUpload = await f.AddLooseUploadFileAsync(Young, ct);

         var dry = await Collector(f).RunAsync(24, true, Now, ct);
         Assert.Equal(1, dry.OrphanBlobFileCount);
         Assert.Equal(1, dry.OrphanUploadFileCount);
         Assert.True(File.Exists(f.Store.BlobPath(oldFile.Digest)));

         var report = await Collector(f).RunAsync(24, false, Now, ct);

         Assert.Equal(1, report.OrphanBlobFileCount);
         Assert.Equal(oldFile.Size, report.OrphanBlobFileBytes);
         Assert.False(File.Exists(f.Store.BlobPath(oldFile.Digest)));
         Assert.True(File.Exists(f.Store.BlobPath(youngFile.Digest)));
         Assert.True(File.Exists(weird));
         Assert.Contains(report.Warnings, w => w.StartsWith("Unexpected file skipped", StringComparison.Ordinal));
         Assert.Equal(1, report.OrphanUploadFileCount);
         Assert.False(File.Exists(oldUpload));
         Assert.True(File.Exists(youngUpload));
         // Peringatan tidak boleh membocorkan path absolut mesin.
         Assert.DoesNotContain(report.Warnings, w => w.Contains(f.Folder, StringComparison.OrdinalIgnoreCase));
      }

      [Theory]
      [InlineData(0)]
      [InlineData(721)]
      public async Task Run_RejectsGraceOutOfRange(int hours) {
         var ct = TestContext.Current.CancellationToken;
         var f = await CreateAsync(ct);

         var ex = await Assert.ThrowsAsync<ActionException>(() => Collector(f).RunAsync(hours, true, Now, ct));

         Assert.Equal(400, ex.StatusCode);
      }
   }
}
