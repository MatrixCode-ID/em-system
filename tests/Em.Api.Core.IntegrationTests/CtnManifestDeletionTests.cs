using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Registry;

namespace Em.Api.Core.IntegrationTests
{
   public class CtnManifestDeletionTests(SqlServerDatabase database) : IAsyncDisposable
   {
      private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
      private const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
      private const string OciIndex = "application/vnd.oci.image.index.v1+json";

      private RegistryFixture? fixture;

      public async ValueTask DisposeAsync() {
         if (fixture is not null) await fixture.DisposeAsync();
      }

      [Fact]
      public async Task FindReferencingIndex_FindsIndexUntilItIsDeleted() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var child = await f.AddManifestAsync(image, OciManifest, "{\"child\":1}", [], [], Now, ct);
         var index = await f.AddManifestAsync(image, OciIndex,
            "{\"schemaVersion\":2,\"mediaType\":\"" + OciIndex + "\",\"manifests\":[{\"digest\":\"" + child.Digest + "\"}]}",
            [], ["multi"], Now, ct);

         Assert.Equal(index.Digest, await CtnManifestDeletion.FindReferencingIndexAsync(f.Db, image, child.Digest, ct));
         Assert.Null(await CtnManifestDeletion.FindReferencingIndexAsync(f.Db, image, index.Digest, ct));

         await CtnManifestDeletion.DeleteAsync(f.Db, index.Id, ct);

         Assert.Null(await CtnManifestDeletion.FindReferencingIndexAsync(f.Db, image, child.Digest, ct));
      }

      [Fact]
      public async Task Delete_RemovesTagsAndManifestBlobs_KeepsBlobLinks() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var blob = await f.AddBlobAsync(Now, ct);
         await f.AddLinkAsync(image, blob.Id, Now, ct);
         var manifest = await f.AddManifestAsync(image, OciManifest, "{\"m\":1}", [blob.Id], ["latest", "1.0"], Now, ct);

         await CtnManifestDeletion.DeleteAsync(f.Db, manifest.Id, ct);

         Assert.False(await f.Db.Manifests.AnyAsync(m => m.cCtnManifestId == manifest.Id, ct));
         Assert.False(await f.Db.Tags.AnyAsync(t => t.cCtnManifestId == manifest.Id, ct));
         Assert.False(await f.Db.ManifestBlobs.AnyAsync(b => b.cCtnManifestId == manifest.Id, ct));
         Assert.True(await f.Db.BlobLinks.AnyAsync(l => l.cCtnBlobId == blob.Id, ct));
         Assert.True(await f.Db.Blobs.AnyAsync(b => b.cCtnBlobId == blob.Id, ct));
      }
   }
}
