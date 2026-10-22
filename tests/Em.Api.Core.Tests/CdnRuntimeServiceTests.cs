using System.IO.Compression;
using Em.Api.Core.Models;
using Em.Api.Core.NuPak;
using Em.Api.Core.ObjectStorage;
using Em.Api.Core.Registry;
using Em.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Tests;

public sealed class CdnRuntimeServiceTests : IDisposable {
   private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmRuntime-" + Guid.NewGuid().ToString("N"));
   private static CancellationToken Token => TestContext.Current.CancellationToken;
   public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

   [Fact]
   public void MetadataDefaultsEnabledForAbsentAndEmptyKeys() {
      Assert.True(FeatureServiceSwitch.MetaEnabled(null));
      Assert.True(FeatureServiceSwitch.MetaEnabled(""));
      Assert.True(FeatureServiceSwitch.MetaEnabled(" "));
      Assert.True(FeatureServiceSwitch.MetaEnabled("True"));
      Assert.False(FeatureServiceSwitch.MetaEnabled("False"));
   }

   [Fact]
   public async Task HostSeedPersistedOverrideAndLiveUploadLimit() {
      var store = CdnStore.Create(_folder, AppContext.BaseDirectory, 3);
      Assert.Equal(3, store.MaxFileSize);
      store.SetMaxFileSize(1);
      Assert.Equal(1024 * 1024, store.List("").MaxFileSize);
      await store.UploadAsync("", "small", new MemoryStream(new byte[4]), false, Token);
      var failure = await Assert.ThrowsAsync<ActionException>(() => store.UploadAsync("", "large", new MemoryStream(new byte[1024 * 1024 + 1]), false, Token));
      Assert.Equal(413, failure.StatusCode);
      Assert.Equal(1024 * 1024, CdnStore.Create(_folder, AppContext.BaseDirectory, 99).MaxFileSize);
      Assert.Equal(400, Assert.Throws<ActionException>(() => store.SetMaxFileSize(0)).StatusCode);
      Assert.Equal(400, Assert.Throws<ActionException>(() => store.SetMaxFileSize(CdnStore.MaximumFileSizeMb + 1)).StatusCode);
   }

   [Fact]
   public async Task UploadKeepsLimitCapturedBeforeFirstRead() {
      var store = CdnStore.Create(_folder, AppContext.BaseDirectory, 3);
      using var content = new OnFirstReadStream(() => store.SetMaxFileSize(1));
      Assert.Equal(413, (await Assert.ThrowsAsync<ActionException>(() => store.UploadAsync("", "file", content, false, Token))).StatusCode);
      Assert.False(File.Exists(store.ResolveInside("file")));
      await store.UploadAsync("", "next", new MemoryStream(new byte[4]), false, Token);
   }

   [Fact]
   public async Task ExtractionUsesRuntimeEntryAndTotalLimits() {
      var store = CdnStore.Create(_folder, AppContext.BaseDirectory, 50_000_000);
      void Zip(string name, int count, int size) {
         using var zip = ZipFile.Open(store.ResolveInside(name), ZipArchiveMode.Create);
         for (var i = 0; i < count; i++) { using var stream = zip.CreateEntry($"entry-{i}").Open(); stream.Write(new byte[size]); }
      }
      Zip("entry.zip", 1, 1024 * 1024 + 1);
      Zip("total.zip", 11, 1024 * 1024);
      store.SetMaxFileSize(1);
      Assert.Equal(413, (await Assert.ThrowsAsync<ActionException>(() => store.ExtractAsync("entry.zip", false, (_, _) => { }, Token))).StatusCode);
      Assert.Equal(413, (await Assert.ThrowsAsync<ActionException>(() => store.ExtractAsync("total.zip", false, (_, _) => { }, Token))).StatusCode);
      Assert.False(File.Exists(store.ResolveInside("entry-0")));
   }

   [Theory]
   [InlineData("Cdn")]
   [InlineData("NuPak")]
   [InlineData("Ctn")]
   [InlineData("ObjectStorage")]
   public async Task SwitchPersistsAndPublicReturns404WhileInternalReturns503(string feature) {
      var path = Path.Combine(_folder, feature, "settings", "service.json");
      var state = FeatureServiceSwitch.FileBacked(feature, path);
      var registry = new FeatureServiceRegistry(); registry.Add(feature, state);
      Assert.True(await state.ReadAsync());
      await state.SaveAsync(false);
      Assert.False(await FeatureServiceSwitch.FileBacked(feature, path).ReadAsync());
      var http = new DefaultHttpContext();
      http.RequestServices = new ServiceCollection().AddSingleton(registry).AddSingleton(CtnBlobStore.Disabled).BuildServiceProvider();
      if (feature == "NuPak") await NuPakEndpoint.HandleAsync(http);
      else if (feature == "ObjectStorage") await ObjectStorageEndpoint.HandleAsync(http);
      else if (feature == "Ctn") await CtnRegistryEndpoint.HandleAsync(http);
      else Assert.False(await registry.AllowPublicAsync(feature, http));
      Assert.Equal(404, http.Response.StatusCode);
      var disabled = await Assert.ThrowsAsync<ServiceDisabledException>(() => state.EnsureAvailableAsync());
      Assert.Equal(503, disabled.StatusCode); Assert.Equal($"{feature} is disabled", disabled.Message);
      await state.SaveAsync(true);
      Assert.True(await registry.AllowPublicAsync(feature, new DefaultHttpContext()));
   }

   [Fact]
   public async Task InternalObjectFacadeStopsBeforeDatabaseAccess() {
      var registry = new FeatureServiceRegistry();
      registry.Add("ObjectStorage", new FeatureServiceSwitch("Object Storage", () => Task.FromResult(false), _ => Task.CompletedTask));
      IObjectStore store = new SwitchedObjectStore(new ObjectStore(null!, null!), registry);
      Assert.Equal(503, (await Assert.ThrowsAsync<ServiceDisabledException>(() => store.GetAsync("bucket", "key", Token))).StatusCode);
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.PutAsync("bucket", "key", Stream.Null, ct: Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.OpenAsync("bucket", "key", ct: Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.ListAsync("bucket", ct: Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.VersionsAsync("bucket", "key", Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.DeleteAsync("bucket", "key", Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.RestoreAsync("bucket", "key", 1, Token));
      await Assert.ThrowsAsync<ServiceDisabledException>(() => store.PurgeVersionAsync("bucket", "key", 1, Token));
   }

   [Fact]
   public async Task AllWriteActionsRejectNonAdminBeforeAnyMutation() {
      var service = new FeatureServiceServices { Request = new ActionRequest { cUserId = "user", IsAdmin = false } };
      foreach (var save in new Func<bool, Task<FeatureServiceState>>[] { service.PostMeta_CdnServiceState, service.PostMeta_CtnServiceState,
         service.PostMeta_NuPakServiceState, service.PostMeta_ObjectStorageServiceState })
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => save(false))).StatusCode);
      new ActionRequest { cUserId = "admin", IsAdmin = true }.RequireAdmin();
   }

   private sealed class OnFirstReadStream(Action firstRead) : MemoryStream(new byte[4]) {
      private bool _started;
      public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) {
         if (!_started) { _started = true; firstRead(); }
         return base.ReadAsync(buffer, ct);
      }
   }
}
