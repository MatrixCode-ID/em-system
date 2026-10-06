using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Api.Core.Registry;
using Em.Api.Core.Registry.Deploy;
using Em.Shared;

namespace Em.Api.Core.IntegrationTests
{
   /// <summary>Deploy targets and history in SQL Server: credentials, validation, push resolution, history order and deletion.</summary>
   public class CtnDeployServiceTests(SqlServerDatabase database) : IAsyncDisposable
   {
      private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
      private const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
      private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

      private RegistryFixture? fixture;

      public async ValueTask DisposeAsync() {
         if (fixture is not null) await fixture.DisposeAsync();
      }

      private static CancellationToken Ct => TestContext.Current.CancellationToken;

      private async Task<(RegistryFixture F, CtnDeployStore Store, string ImageId)> SetupAsync() {
         var f = fixture = await RegistryFixture.CreateAsync(database, Ct);
         var imageId = await f.AddImageAsync("acme", "api", Ct);
         return (f, new CtnDeployStore(f.Db, new CtnDeploySecrets(Key)), imageId);
      }

      private static CtnDeployTargetSave Request(string imageId) => new() {
         ImageId = imageId, Kind = CtnDeployKind.Ssh, Mode = CtnDeployMode.Stack, Host = "docker.example.com", User = "deploy",
         Auth = CtnDeployAuth.SshKey, Secret = "-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n", Passphrase = "phrase",
         Stack = "/opt/app", Service = "api", RegistryHost = "registry.example.com", RegistryUser = "robot", RegistrySecret = "token-1",
         TagFilter = "1.*, latest"
      };

      [Fact]
      public async Task Save_StoresCredentialsEncrypted_InfoOnlyHasFlags() {
         var (f, store, imageId) = await SetupAsync();

         await store.SaveAsync(Request(imageId), Ct);

         var info = (await store.GetAsync(imageId, Ct))!;
         Assert.True(info.HasSecret);
         Assert.True(info.HasPassphrase);
         Assert.True(info.HasRegistrySecret);
         Assert.Equal("1.*,latest", info.TagFilter);
         Assert.Equal(22, info.Port);
         var row = await f.Db.Deploys.SingleAsync(d => d.cCtnImageId == imageId, Ct);
         Assert.NotEqual("token-1"u8.ToArray(), row.cCtnDeployRegistrySecret);
         var target = await store.ToTargetAsync(row, Ct);
         Assert.Equal("token-1", target.RegistrySecret);
         Assert.Equal("phrase", target.Passphrase);
      }

      [Fact]
      public async Task Save_NullKeeps_EmptyRemoves_ValueReplaces_OneRowPerContainer() {
         var (f, store, imageId) = await SetupAsync();
         await store.SaveAsync(Request(imageId), Ct);

         var update = Request(imageId);
         update.Secret = null;
         update.Passphrase = "";
         update.RegistrySecret = "token-2";
         await store.SaveAsync(update, Ct);

         Assert.Equal(1, await f.Db.Deploys.CountAsync(d => d.cCtnImageId == imageId, Ct));
         var target = await store.ToTargetAsync((await store.FindAsync(imageId, Ct))!, Ct);
         Assert.StartsWith("-----BEGIN OPENSSH", target.Secret);
         Assert.Null(target.Passphrase);
         Assert.Equal("token-2", target.RegistrySecret);
      }

      [Theory]
      [InlineData("host", "")]
      [InlineData("registry", "https://registry.example.com")]
      [InlineData("folder", "opt/app")]
      [InlineData("variable", "1BAD")]
      [InlineData("filter", "a/b")]
      public async Task Save_InvalidField_Is400(string field, string value) {
         var (_, store, imageId) = await SetupAsync();
         var request = Request(imageId);
         switch (field) {
            case "host": request.Host = value; break;
            case "registry": request.RegistryHost = value; break;
            case "folder": request.Stack = value; break;
            case "variable": request.ImageVariable = value; break;
            case "filter": request.TagFilter = value; break;
         }

         var error = await Assert.ThrowsAsync<ActionException>(() => store.SaveAsync(request, Ct));
         Assert.Equal(400, error.StatusCode);
      }

      [Fact]
      public async Task Save_PortainerTargetNeedsUrlAndToken() {
         var (_, store, imageId) = await SetupAsync();
         var request = Request(imageId);
         request.Kind = CtnDeployKind.Portainer;

         Assert.Equal(400, (await Assert.ThrowsAsync<ActionException>(() => store.SaveAsync(request, Ct))).StatusCode);

         request.Auth = CtnDeployAuth.PortainerToken;
         request.Host = "https://portainer.example.com:9443/";
         request.EndpointId = 2;
         request.StackId = 7;
         await store.SaveAsync(request, Ct);
         var info = (await store.GetAsync(imageId, Ct))!;
         Assert.Equal("https://portainer.example.com:9443", info.Host);
         Assert.Null(info.User);
         Assert.Null(info.Port);
         Assert.False(info.HasPassphrase);
      }

      [Fact]
      public async Task ResolvePush_SkipsWithoutTarget_WhenInactive_AndWhenFilterDoesNotMatch() {
         var (f, store, imageId) = await SetupAsync();
         var manifest = await f.AddManifestAsync(imageId, OciManifest, "{\"m\":1}", [], ["1.2.0"], Now, Ct);
         var push = new CtnDeployPushRequest { Repository = "acme/api", Tags = ["2.0.0", "2.0"], Digest = manifest.Digest };

         Assert.Contains("no deploy target", (await store.ResolvePushAsync(push, Ct)).SkipReason);

         var request = Request(imageId);
         request.IsActive = false;
         await store.SaveAsync(request, Ct);
         Assert.Contains("turned off", (await store.ResolvePushAsync(push, Ct)).SkipReason);

         request.IsActive = true;
         await store.SaveAsync(request, Ct);
         Assert.Contains("tag filter", (await store.ResolvePushAsync(push, Ct)).SkipReason);

         push.Tags = ["1.3.0", "latest"];
         var resolved = await store.ResolvePushAsync(push, Ct);
         Assert.Null(resolved.SkipReason);
         Assert.NotNull(resolved.Row);
         Assert.Equal("1.3.0", resolved.Tag);
         Assert.Equal("acme/api", resolved.Repository);
      }

      [Fact]
      public async Task ResolvePush_UnknownRepositoryOrDigest_Is404() {
         var (_, store, _) = await SetupAsync();
         var digest = "sha256:" + new string('c', 64);

         Assert.Equal(404, (await Assert.ThrowsAsync<ActionException>(() =>
            store.ResolvePushAsync(new CtnDeployPushRequest { Repository = "acme/web", Digest = digest }, Ct))).StatusCode);
         Assert.Equal(404, (await Assert.ThrowsAsync<ActionException>(() =>
            store.ResolvePushAsync(new CtnDeployPushRequest { Repository = "acme/api", Digest = digest }, Ct))).StatusCode);
         Assert.Equal(400, (await Assert.ThrowsAsync<ActionException>(() =>
            store.ResolvePushAsync(new CtnDeployPushRequest { Repository = "acme/api", Digest = "latest" }, Ct))).StatusCode);
      }

      [Fact]
      public async Task Runs_LatestFirst_RollbackOnlyToEarlierSuccess() {
         var (f, store, imageId) = await SetupAsync();
         var row = await store.SaveAsync(Request(imageId), Ct);
         var success1 = AddRun(f, row, Now, CtnDeployResult.Success);
         var failed = AddRun(f, row, Now.AddMinutes(1), CtnDeployResult.Failed);
         var success2 = AddRun(f, row, Now.AddMinutes(2), CtnDeployResult.Success);
         await f.Db.SaveChangesAsync(Ct);

         var runs = await store.RunsAsync(row, 10, Ct);

         Assert.Equal([success2, failed, success1], runs.Select(r => r.Id).ToArray());
         Assert.Equal([false, false, true], runs.Select(r => r.CanRollback).ToArray());
         Assert.Equal(success2, (await store.GetAsync(imageId, Ct))!.LastRun!.Id);
         Assert.Single(await store.RunsAsync(row, 1, Ct));
      }

      [Fact]
      public async Task DeleteForImage_RemovesTargetAndHistory_OnlyOfThatContainer() {
         var (f, store, imageId) = await SetupAsync();
         var otherId = await f.AddImageAsync("acme", "worker", Ct);
         var row = await store.SaveAsync(Request(imageId), Ct);
         var other = await store.SaveAsync(Request(otherId), Ct);
         AddRun(f, row, Now, CtnDeployResult.Success);
         AddRun(f, other, Now, CtnDeployResult.Success);
         await f.Db.SaveChangesAsync(Ct);

         Assert.True(await store.DeleteAsync(imageId, Ct));

         Assert.False(await f.Db.Deploys.AnyAsync(d => d.cCtnImageId == imageId, Ct));
         Assert.False(await f.Db.DeployRuns.AnyAsync(r => r.cCtnDeployId == row.cCtnDeployId, Ct));
         Assert.True(await f.Db.DeployRuns.AnyAsync(r => r.cCtnDeployId == other.cCtnDeployId, Ct));
         Assert.False(await store.DeleteAsync(imageId, Ct));
      }

      [Fact]
      public async Task AbandonedRunningRun_IsClosedAsFailed() {
         var (f, store, imageId) = await SetupAsync();
         var row = await store.SaveAsync(Request(imageId), Ct);
         var stale = AddRun(f, row, DateTime.UtcNow.AddHours(-1), CtnDeployResult.Running);
         var fresh = AddRun(f, row, DateTime.UtcNow, CtnDeployResult.Running);
         await f.Db.SaveChangesAsync(Ct);

         var info = await store.GetAsync(imageId, Ct);
         var runs = await store.RunsAsync(row, 10, Ct);

         Assert.Equal(CtnDeployResult.Running, info!.LastRun!.Result);
         Assert.Equal(CtnDeployResult.Failed, runs.Single(r => r.Id == stale).Result);
         Assert.Contains("Interrupted", runs.Single(r => r.Id == stale).Output);
         Assert.Equal(CtnDeployResult.Running, runs.Single(r => r.Id == fresh).Result);
      }

      private static string AddRun(RegistryFixture f, ta_CtnDeploy row, DateTime started, CtnDeployResult result) {
         var id = $"{Ulid.NewUlid()}";
         f.Db.DeployRuns.Add(new ta_CtnDeployRun {
            cCtnDeployRunId = id, cCtnDeployId = row.cCtnDeployId, cCtnDeployRunTrigger = (int)CtnDeployTrigger.Manual,
            cCtnDeployRunDigest = "sha256:" + new string('d', 64), cCtnDeployRunResult = (int)result, cCtnDeployRunStarted = started,
            cCtnDeployRunFinished = result == CtnDeployResult.Running ? null : started, ustamp = started, datestamp = started
         });
         return id;
      }
   }
}
