using Microsoft.Extensions.Logging.Abstractions;
using Em.Api.Core.Registry;

namespace Em.Api.Core.IntegrationTests
{
   public class CtnMissingBlobTests(SqlServerDatabase database) : IAsyncDisposable
   {
      private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

      private RegistryFixture? fixture;

      public async ValueTask DisposeAsync() {
         if (fixture is not null) await fixture.DisposeAsync();
      }

      [Fact]
      public async Task BlobIntact_RequiresFileWithRecordedSize() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         var present = await f.AddBlobAsync(Now, ct);
         var absent = await f.AddBlobAsync(Now, ct, writeFile: false);

         Assert.True(f.Store.BlobIntact(present.Digest, present.Size));
         Assert.False(f.Store.BlobIntact(present.Digest, present.Size + 1));
         Assert.False(f.Store.BlobIntact(absent.Digest, absent.Size));
      }

      [Fact]
      public async Task CheckStartup_CountsMissingBlobsWithoutThrowing() {
         var ct = TestContext.Current.CancellationToken;
         var f = fixture = await RegistryFixture.CreateAsync(database, ct);
         await f.AddBlobAsync(Now, ct);
         Assert.Equal(0, RegistryStorageIntegrity.CheckStartup(f.Db, f.Store, NullLogger.Instance));

         await f.AddBlobAsync(Now, ct, writeFile: false);
         await f.AddBlobAsync(Now, ct, writeFile: false);

         Assert.Equal(2, RegistryStorageIntegrity.CheckStartup(f.Db, f.Store, NullLogger.Instance));
      }
   }
}
