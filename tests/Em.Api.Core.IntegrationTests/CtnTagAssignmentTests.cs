using Em.Api.Core.Registry;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests
{
   public class CtnTagAssignmentTests(SqlServerDatabase database) : IAsyncDisposable
   {
      private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
      private const string Media = "application/vnd.oci.image.manifest.v1+json";
      private const string IndexMedia = "application/vnd.oci.image.index.v1+json";

      private RegistryFixture? fixture;

      public async ValueTask DisposeAsync() {
         if (fixture is not null) await fixture.DisposeAsync();
      }

      private async Task<string[]> TagsOf(RegistryFixture f, string manifestId, CancellationToken ct) {
         f.Db.ChangeTracker.Clear();
         return await f.Db.Tags.Where(t => t.cCtnManifestId == manifestId).OrderBy(t => t.cCtnTagName)
            .Select(t => t.cCtnTagName).ToArrayAsync(ct);
      }

      [Fact]
      public async Task Add_KeepsExistingTagsOfTheManifest() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var manifest = await f.AddManifestAsync(image, Media, "{\"m\":1}", [], ["1.0.0-beta.4"], Now, ct);

         var result = await CtnTagAssignment.AssignAsync(f.Db, image, manifest.Id, "1.0.0", Now, ct);

         Assert.Equal(CtnTagAssignment.Outcome.Added, result.Outcome);
         Assert.Equal(manifest.Digest, result.Digest);
         Assert.Equal(["1.0.0", "1.0.0-beta.4"], await TagsOf(f, manifest.Id, ct));
      }

      [Fact]
      public async Task Add_MovesTagFromAnotherManifest() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var old = await f.AddManifestAsync(image, Media, "{\"m\":1}", [], ["1.0.0-beta.3", "latest"], Now, ct);
         var target = await f.AddManifestAsync(image, IndexMedia, "{\"manifests\":[]}", [], ["1.0.0-beta.4"], Now, ct);

         var result = await CtnTagAssignment.AssignAsync(f.Db, image, target.Id, "latest", Now, ct);

         Assert.Equal(CtnTagAssignment.Outcome.Moved, result.Outcome);
         Assert.Equal(old.Digest, result.PreviousDigest);
         Assert.Equal(["1.0.0-beta.3"], await TagsOf(f, old.Id, ct));
         Assert.Equal(["1.0.0-beta.4", "latest"], await TagsOf(f, target.Id, ct));
      }

      [Fact]
      public async Task Add_SameTagOnSameManifestIsUnchanged() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var manifest = await f.AddManifestAsync(image, Media, "{\"m\":1}", [], ["latest"], Now, ct);

         var result = await CtnTagAssignment.AssignAsync(f.Db, image, manifest.Id, "latest", Now, ct);

         Assert.Equal(CtnTagAssignment.Outcome.Unchanged, result.Outcome);
         Assert.Equal(["latest"], await TagsOf(f, manifest.Id, ct));
      }

      [Theory]
      [InlineData("-rc")]
      [InlineData(".1")]
      [InlineData("1.0.0+build")]
      [InlineData("has space")]
      [InlineData("a/b")]
      [InlineData("sha256:abc")]
      public async Task Add_RejectsInvalidTagWith400(string tag) {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var image = await f.AddImageAsync("acme", "api", ct);
         var manifest = await f.AddManifestAsync(image, Media, "{\"m\":1}", [], [], Now, ct);

         var error = await Assert.ThrowsAsync<ActionException>(() => CtnTagAssignment.AssignAsync(f.Db, image, manifest.Id, tag, Now, ct));
         Assert.Equal(400, error.StatusCode);
      }

      [Fact]
      public async Task Add_ManifestOfAnotherContainerIs404() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var api = await f.AddImageAsync("acme", "api", ct);
         var web = await f.AddImageAsync("acme", "web", ct);
         var manifest = await f.AddManifestAsync(web, Media, "{\"m\":1}", [], [], Now, ct);

         var error = await Assert.ThrowsAsync<ActionException>(() => CtnTagAssignment.AssignAsync(f.Db, api, manifest.Id, "latest", Now, ct));
         Assert.Equal(404, error.StatusCode);
      }
   }
}
