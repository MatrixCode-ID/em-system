using System.IO.Compression;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Tests;

public sealed class CdnStoreTests : IDisposable {
   private readonly string _parent = Path.Combine(Path.GetTempPath(), "EmCdnTests-" + Guid.NewGuid().ToString("N"));
   private CdnStore Store(long limit = 8) => CdnStore.Create(_parent, AppContext.BaseDirectory, limit);
   private static CancellationToken Token => TestContext.Current.CancellationToken;
   private static Task<CdnEntry> Put(CdnStore store, string folder, string name, string text, string? owner = null) =>
      store.UploadAsync(folder, name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), true, Token, owner);
   public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, true); }

   [Fact]
   public async Task LayoutAndPublicDotHiddenSystemFilesAreOrdinaryContent() {
      Directory.CreateDirectory(_parent); File.WriteAllText(Path.Combine(_parent, "old.txt"), "old");
      var store = Store();
      foreach (var name in new[] { "files", "temp", "recycle", "settings" }) Assert.True(Directory.Exists(Path.Combine(_parent, name)));
      await Put(store, "", ".htaccess", "public");
      if (OperatingSystem.IsWindows()) File.SetAttributes(store.ResolveInside(".htaccess"), FileAttributes.Hidden | FileAttributes.System);
      Assert.Single(store.List("").Entries); Assert.Single(store.Tree(""));
      Assert.DoesNotContain(store.List("").Entries, e => e.Name == "old.txt");
      Assert.Equal(6, store.StorageSize(Token).TotalBytes);
      Assert.Throws<ActionException>(() => store.ResolveInside("../outside"));
   }

   [Fact]
   public async Task DeleteRestoreConflictAndOpenDownloadRetention() {
      var store = Store(); await Put(store, "", "a.txt", "first");
      using var read = store.OpenRead(store.ResolveInside("a.txt"));
      store.Delete("a.txt", deletedBy: "test-user");
      var item = Assert.Single(store.RecycleItems()); Assert.Equal("test-user", item.DeletedBy);
      store.PurgeRecycle(null); Assert.Single(store.RecycleItems());
      using var reader = new StreamReader(read); Assert.Equal("first", await reader.ReadToEndAsync(Token));
      await Put(store, "", "a.txt", "new");
      Assert.Contains("a.txt", Assert.Throws<ActionException>(() => store.Restore(item.Id)).Message);
      store.Delete("a.txt"); store.Restore(item.Id);
      Assert.Equal("first", File.ReadAllText(store.ResolveInside("a.txt")));
   }

   [Fact]
   public async Task OrdinaryOverwriteRecyclesAndFolderRestoreRecreatesParents() {
      var store = Store(); store.CreateFolder("", "parent"); store.CreateFolder("parent", "child");
      await Put(store, "parent/child", ".a", "first"); await Put(store, "parent/child", ".a", "second");
      Assert.Single(store.RecycleItems()); store.Delete("parent/child");
      var item = store.RecycleItems().Single(i => i.IsFolder); store.Delete("parent");
      store.Restore(item.Id); Assert.Equal("second", File.ReadAllText(store.ResolveInside("parent/child/.a")));
   }

   [Fact]
   public async Task ManagedOwnershipNestingQuotaAndPerFileLimit() {
      var store = Store(); var managed = store.Manage("release", "Contoso.Inventory");
      Assert.Null(managed.QuotaBytes); Assert.Equal("Contoso.Inventory", managed.Owner.Id);
      Assert.Throws<ActionException>(() => store.Manage("release/child", "Contoso.Inventory"));
      Assert.Throws<ActionException>(() => store.Manage("release", "Contoso.Other"));
      Assert.Throws<ActionException>(() => store.Delete("release"));
      await Assert.ThrowsAsync<ActionException>(() => Put(store, "release", "a", "payload"));
      await Put(store, "release", "a", "larger-than-ordinary-limit", "Contoso.Inventory");
      Assert.Throws<ActionException>(() => store.Manage("", "Contoso.Inventory"));
      store.SetQuota("release", 2);
      var failure = Assert.Throws<ActionException>(() => store.Preflight(new CdnSyncPlan { Path = "release", AppId = "Contoso.Inventory", Files = [new() { Path = "a", Size = 3 }] }));
      Assert.Contains("3", failure.Message); Assert.Contains("2", failure.Message);
      await Assert.ThrowsAsync<ActionException>(() => Put(store, "release", "b", "x", "Contoso.Inventory"));
      Assert.True(File.Exists(store.ResolveInside("release/a")));
      store.SetQuota("release", null); store.Delete("release/a", "Contoso.Inventory"); Assert.Empty(store.RecycleItems());
   }

   [Fact]
   public void AdoptionRequiresPhysicalEmptinessAndNoManagedDescendant() {
      var store = Store(); store.CreateFolder("", "parent"); store.CreateFolder("parent", "empty-child");
      Assert.Throws<ActionException>(() => store.Manage("parent", "Contoso.Inventory"));
      store.Manage("parent/empty-child", "Contoso.Inventory");
      Assert.Throws<ActionException>(() => store.Manage("parent", "Contoso.Inventory"));
      Assert.Throws<ActionException>(() => store.Move("parent", "", false));
      Assert.Throws<ActionException>(() => store.Unmanage("parent/empty-child", null, "wrong"));
      store.Unmanage("parent/empty-child", null, "empty-child"); Assert.Empty(store.ManagedFolders());
   }

   [Fact]
   public async Task RestoreCannotEnterManagedRootsOrTheirAncestors() {
      var store = Store(); store.CreateFolder("", "release"); await Put(store, "release", "a", "a");
      store.Delete("release/a"); var item = Assert.Single(store.RecycleItems());
      store.Manage("release", "Contoso.Inventory"); Assert.Throws<ActionException>(() => store.Restore(item.Id));
   }

   [Fact]
   public async Task ClearTemporaryPreservesActiveUploadAndRetiredOpenManagedFiles() {
      var store = Store(); store.Manage("release", "Contoso.Inventory");
      await Put(store, "release", "a", "old", "Contoso.Inventory");
      using var stream = store.OpenRead(store.ResolveInside("release/a"));
      store.Delete("release/a", "Contoso.Inventory");
      File.WriteAllText(Path.Combine(_parent, "temp", "orphan"), "garbage");
      Assert.Equal(7, store.TemporaryGarbage().TotalBytes); store.TemporaryGarbage(true);
      Assert.Equal(3, store.StorageSize(Token).TotalBytes);
      stream.Dispose(); Assert.Equal(0, store.StorageSize(Token).TotalBytes);
      using var blocked = new PausedStream();
      var pending = store.UploadAsync("", "new", blocked, true, Token);
      await blocked.Started.Task.WaitAsync(Token);
      Assert.Equal(0, store.TemporaryGarbage().FileCount); store.TemporaryGarbage(true);
      blocked.Release.SetResult(); await pending; Assert.True(File.Exists(store.ResolveInside("new")));
   }

   [Fact]
   public async Task DotFilesArchiveAndExtractButManagedDestinationsAreLocked() {
      var store = Store(100_000); store.CreateFolder("", "normal"); await Put(store, "normal", ".a", "content");
      await store.ArchiveAsync("normal", [".a"], "files.zip", false, (_, _) => { }, Token);
      store.Delete("normal/.a"); await store.ExtractAsync("normal/files.zip", true, (_, _) => { }, Token);
      Assert.Equal("content", File.ReadAllText(store.ResolveInside("normal/.a")));
      store.Manage("release", "Contoso.Inventory");
      using (var zip = ZipFile.Open(store.ResolveInside("attack.zip"), ZipArchiveMode.Create)) {
         using var text = new StreamWriter(zip.CreateEntry("release/a").Open()); text.Write("forbidden");
      }
      await Assert.ThrowsAsync<ActionException>(() => store.ExtractAsync("attack.zip", true, (_, _) => { }, Token));
      Assert.False(File.Exists(store.ResolveInside("release/a")));
   }

   [Fact]
   public async Task RecycleMetadataNeverCollidesWithTheOriginalItemName() {
      var store = Store(); await Put(store, "", "item.json", "payload"); store.Delete("item.json");
      var item = Assert.Single(store.RecycleItems()); store.Restore(item.Id);
      Assert.Equal("payload", File.ReadAllText(store.ResolveInside("item.json")));
   }

   [Fact]
   public async Task CdnDispatcherExemptionPreservesAuthenticationAndOtherRateLimits() {
      Directory.CreateDirectory(_parent);
      var app = EmApp.BuildApp(["--contentRoot", _parent], builder => {
         builder.SetDbProvider("Server=(local);Database=UnusedCdnFixture;Integrated Security=true;TrustServerCertificate=true");
         builder.EnableCdn(Path.Combine(_parent, "cdn"), 1);
         builder.EnableContainerManager = false;
         builder.ActionRateLimit = 1;
      });
      Assert.Contains(app.AllClaims, c => c.ModuleName == Defaults.AdministrativeToolsModuleName && c.Name == ICdnReleaseServices.ReleaseClaim);
      async Task<int?> Status(string action) {
         using var scope = app.ServiceProvider.CreateScope();
         var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
         http.Request.Method = "GET";
         var result = await app.ProcessRequest(Defaults.AdministrativeToolsModuleName, action, http);
         return Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
      }
      Assert.Equal(401, await Status("GetMeta_CdnStatus"));
      Assert.Equal(401, await Status("GetMeta_CdnStatus"));
      Assert.Equal(401, await Status("GetMeta_CdnReleaseFolder"));
      Assert.Equal(401, await Status("GetMeta_CdnReleaseFolder"));
      Assert.Equal(404, await Status("Nonexistent"));
      Assert.Equal(429, await Status("Nonexistent"));
      await ((IAsyncDisposable)app.ServiceProvider).DisposeAsync();
   }

   [Fact]
   public void ManagedSettingsPersistAcrossStoreRecreation() {
      var store = Store(); store.Manage("r", "Contoso.Inventory"); store.SetQuota("r", 123);
      var loaded = Store(); Assert.Equal(123, Assert.Single(loaded.ManagedFolders()).QuotaBytes);
   }

   private sealed class PausedStream : MemoryStream {
      public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
         Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0;
      }
   }
}
