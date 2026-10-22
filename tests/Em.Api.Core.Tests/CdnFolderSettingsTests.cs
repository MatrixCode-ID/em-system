using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Tests;

public sealed class CdnFolderSettingsTests : IDisposable {
   private readonly string _parent = Path.Combine(Path.GetTempPath(), "EmCdnMetaTests-" + Guid.NewGuid().ToString("N"));
   private CdnStore Store() => CdnStore.Create(_parent, AppContext.BaseDirectory, 100_000);
   private static CancellationToken Token => TestContext.Current.CancellationToken;
   private static Task<CdnEntry> Put(CdnStore store, string folder, string name, string text, string? owner = null) =>
      store.UploadAsync(folder, name, new MemoryStream(Encoding.UTF8.GetBytes(text)), true, Token, owner);
   private static readonly CdnFolderSettings NoListing = new() { PublicListing = false };
   private const string Valid = """{ "version": 1, "publicListing": false }""";
   public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, true); }

   private static int Status(Action action) => Assert.Throws<ActionException>(action).StatusCode;

   [Fact]
   public void ValidationRejectsBadSettingsAndCanonicalFormRoundTrips() {
      var parsed = CdnFolderSettingsFormat.Parse("﻿" + """{"version":1,"publicListing":false,"hiddenFiles":["*"],"shownFiles":["release.json","release.json.sig"]}""");
      Assert.False(parsed.PublicListing); Assert.Equal(["*"], parsed.HiddenFiles);
      var canonical = CdnFolderSettingsFormat.Serialize(parsed);
      Assert.Contains("\"publicListing\": false", canonical);
      Assert.Equal(canonical, CdnFolderSettingsFormat.Serialize(CdnFolderSettingsFormat.Parse(canonical)));
      foreach (var bad in new[] {
         """{"version":2}""", """{"publicListing":true}""", """{"version":1,"other":1}""", """{"version":1,"hiddenFiles":["a/b"]}""",
         """{"version":1,"hiddenFiles":["a\\b"]}""", """{"version":1,"shownFiles":[""]}""", """{"version":1,"hiddenFiles":"*"}""",
         """{"version":1,"hiddenFiles":[1]}""", "[]", "not json",
         "{\"version\":1,\"hiddenFiles\":[" + string.Join(',', Enumerable.Repeat("\"a\"", 257)) + "]}",
         "{\"version\":1,\"hiddenFiles\":[\"" + new string('a', 256) + "\"]}"
      }) Assert.Equal(400, Status(() => CdnFolderSettingsFormat.Parse(bad)));
      Assert.Contains("hiddenFiles[1]", Assert.Throws<ActionException>(() =>
         CdnFolderSettingsFormat.Parse("""{"version":1,"hiddenFiles":["a",""]}""")).Message);
      var large = "{\"version\":1,\"hiddenFiles\":[\"a\"]" + new string(' ', CdnFolderSettingsFormat.MaxBytes) + "}";
      Assert.Equal(400, Status(() => CdnFolderSettingsFormat.Parse(large)));
   }

   [Fact]
   public void PatternsMatchWithShownWinning() {
      var settings = new CdnFolderSettings { HiddenFiles = ["*.dll", "data?"], ShownFiles = ["keep*.dll"] };
      Assert.True(CdnFolderSettingsFormat.IsHidden(settings, "a.dll"));
      Assert.False(CdnFolderSettingsFormat.IsHidden(settings, "keep1.dll"));
      Assert.True(CdnFolderSettingsFormat.IsHidden(settings, "data1"));
      Assert.False(CdnFolderSettingsFormat.IsHidden(settings, "data12"));
      Assert.False(CdnFolderSettingsFormat.IsHidden(settings, "readme.txt"));
      Assert.Equal(OperatingSystem.IsWindows(), CdnFolderSettingsFormat.IsHidden(settings, "A.DLL"));
   }

   [Fact]
   public async Task MetaIsNeverReachableThroughUrlOrManagerPaths() {
      var store = Store(); store.CreateFolder("", "a"); await Put(store, "a", "file.txt", "x");
      store.SaveFolderSettings("", NoListing); store.SaveFolderSettings("a", NoListing);
      var provider = new CdnFileProvider(store);
      // Static files hand the provider a decoded path, so %2emeta and %20 arrive as the variants below.
      foreach (var path in new[] { "/.meta", "/.META", "/.Meta", "/.meta.", "/.meta ", "/.meta::$DATA", "/a/.meta", "/a/.META", ".meta" }) {
         if (!OperatingSystem.IsWindows() && path.Contains("META", StringComparison.Ordinal)) continue;
         var info = provider.GetFileInfo(path);
         Assert.False(info.Exists, path);
         Assert.False(provider.GetDirectoryContents(path).Exists, path);
      }
      Assert.Equal(404, Status(() => store.Delete(".meta")));
      Assert.Equal(404, Status(() => store.Delete("a/.meta")));
      Assert.Equal(404, Status(() => store.Tree("a/.meta")));
      Assert.Equal(404, Status(() => store.Move("a/.meta", "", true)));
      Assert.True(File.Exists(Path.Combine(store.ResolveInside("a"), ".meta")));

      if (!OperatingSystem.IsWindows()) return;
      var full = Path.Combine(store.ResolveInside("a"), ".meta");
      var buffer = new StringBuilder(1024);
      var shortName = GetShortPathNameW(full, buffer, buffer.Capacity) > 0 ? Path.GetFileName(buffer.ToString()) : ".meta";
      if (!shortName.Contains('~')) Assert.Skip("The test volume has no 8.3 short names.");
      Assert.False(provider.GetFileInfo("/a/" + shortName).Exists);
      Assert.Equal(404, Status(() => store.Delete("a/" + shortName)));
   }

   [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
   private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, int bufferLength);

   [Fact]
   public async Task PublicListingOffAnswers404AndMarksFilesNoIndex() {
      var store = Store(); store.CreateFolder("", "rel"); store.CreateFolder("rel", "sub");
      await Put(store, "rel", "release.json", "{}"); await Put(store, "rel", "a.dll", "x");
      store.SaveFolderSettings("rel", NoListing);
      var provider = new CdnFileProvider(store);
      Assert.False(provider.GetDirectoryContents("/rel").Exists);
      Assert.True(provider.GetDirectoryContents("/rel/sub").Exists);
      Assert.DoesNotContain(provider.GetDirectoryContents("/"), i => i.Name == "rel");
      Assert.Contains(store.List("").Entries, e => e.Name == "rel");
      var file =Assert.IsType<CdnFileProvider.Download>(provider.GetFileInfo("/rel/a.dll"));
      Assert.True(file.NoIndex);
      using (var stream = file.CreateReadStream()) Assert.Equal("x", new StreamReader(stream).ReadToEnd());
      Assert.True(Assert.IsType<CdnFileProvider.Download>(provider.GetFileInfo("/rel/release.json")).NoIndex);

      store.CreateFolder("", "open"); await Put(store, "open", "b.txt", "y");
      Assert.False(Assert.IsType<CdnFileProvider.Download>(provider.GetFileInfo("/open/b.txt")).NoIndex);
      Assert.True(provider.GetDirectoryContents("/open").Exists);
      Assert.Contains(provider.GetDirectoryContents("/"), i => i.Name == "open");
   }

   [Fact]
   public async Task HiddenPatternsFilterPublicListingButNotDirectLinks() {
      var store = Store(); store.CreateFolder("", "pub"); store.CreateFolder("pub", "lib");
      foreach (var name in new[] { "a.dll", "keep1.dll", "readme.txt" }) await Put(store, "pub", name, "x");
      store.SaveFolderSettings("pub", new CdnFolderSettings { HiddenFiles = ["*.dll", "li?"], ShownFiles = ["keep?.dll"] });
      var provider = new CdnFileProvider(store);
      var names = provider.GetDirectoryContents("/pub").Select(i => i.Name).OrderBy(n => n).ToArray();
      Assert.Equal(["keep1.dll", "readme.txt"], names);
      Assert.True(provider.GetFileInfo("/pub/a.dll").Exists);
      Assert.True(provider.GetDirectoryContents("/pub/lib").Exists);
      var listed = store.List("pub").Entries;
      Assert.True(listed.Single(e => e.Name == "a.dll").IsHidden);
      Assert.True(listed.Single(e => e.Name == "lib").IsHidden);
      Assert.False(listed.Single(e => e.Name == "keep1.dll").IsHidden);
      Assert.True(store.Tree("").Single(e => e.Path == "pub/a.dll").IsHidden);
   }

   [Fact]
   public async Task EnumerationsAndSizesNeverIncludeMeta() {
      var store = Store(); store.CreateFolder("", "f"); await Put(store, "f", "x.txt", "12345");
      store.SaveFolderSettings("", NoListing); store.SaveFolderSettings("f", NoListing);
      Assert.DoesNotContain(store.List("").Entries, e => e.Name == ".meta");
      Assert.DoesNotContain(store.List("f").Entries, e => e.Name == ".meta");
      Assert.DoesNotContain(store.Tree(""), e => e.Name == ".meta");
      Assert.Equal(1, store.CountInside("f").Files);
      var size = store.StorageSize(Token);
      Assert.Equal(5, size.TotalBytes); Assert.Equal(1, size.FileCount);
   }

   [Fact]
   public async Task UploadedMetaReplacesSettingsAndInvalidKeepsTheOldOnes() {
      var store = Store(); store.CreateFolder("", "f");
      var entry = await Put(store, "f", ".meta", Valid);
      Assert.True(entry.IsFolder); Assert.Equal("f", entry.Path);
      Assert.False(store.FolderSettings("f").Settings.PublicListing);
      var error = await Assert.ThrowsAsync<ActionException>(() => Put(store, "f", ".meta", """{"version":1,"x":1}"""));
      Assert.Equal(400, error.StatusCode);
      Assert.False(store.FolderSettings("f").Settings.PublicListing);
      Assert.Equal(400, Status(() => store.CreateFolder("", ".meta")));
      if (OperatingSystem.IsWindows()) Assert.Equal(400, Status(() => store.CreateFolder("f", ".META")));
   }

   [Fact]
   public async Task RenameMovesNamesAndTurnsFilesIntoSettings() {
      var store = Store(); store.CreateFolder("", "f");
      await Put(store, "f", "good.json", Valid); await Put(store, "f", "bad.json", "{}"); await Put(store, "f", "other.txt", "o");
      var folder = store.Rename("f/good.json", ".meta");
      Assert.True(folder.IsFolder);
      Assert.False(File.Exists(store.ResolveInside("f") + "/good.json"));
      Assert.False(store.FolderSettings("f").Settings.PublicListing);
      Assert.Empty(store.RecycleItems());
      Assert.Equal(400, Status(() => store.Rename("f/bad.json", ".meta")));
      Assert.True(File.Exists(Path.Combine(store.ResolveInside("f"), "bad.json")));
      Assert.Equal("renamed.txt", store.Rename("f/other.txt", "renamed.txt").Name);
      Assert.Equal(409, Status(() => store.Rename("f/renamed.txt", "bad.json")));
      Assert.Equal(400, Status(() => store.Rename("f", ".meta")));
      Assert.Equal("g", store.Rename("f", "g").Name);
      Assert.False(store.FolderSettings("g").Settings.PublicListing);
      store.Manage("release", "Contoso.Inventory");
      Assert.Equal(409, Status(() => store.Rename("release", "other")));
   }

   [Fact]
   public async Task CopyCarriesSettingsAndRefusesItselfAndDuplicates() {
      var store = Store(); store.CreateFolder("", "src"); store.CreateFolder("src", "sub"); store.CreateFolder("", "dst");
      await Put(store, "src", "a.txt", "a"); await Put(store, "src/sub", "b.txt", "b");
      store.SaveFolderSettings("src", NoListing);
      store.SaveFolderSettings("src/sub", new CdnFolderSettings { HiddenFiles = ["*"] });
      await store.CopyAsync("src", "dst", false, (_, _) => { }, Token);
      Assert.Equal("a", File.ReadAllText(store.ResolveInside("dst/src/a.txt")));
      Assert.False(store.FolderSettings("dst/src").Settings.PublicListing);
      Assert.Equal(["*"], store.FolderSettings("dst/src/sub").Settings.HiddenFiles);
      Assert.Equal(400, Status(() => store.ValidateCopy("src", "src/sub", false)));
      Assert.Equal(409, Status(() => store.ValidateCopy("src", "dst", false)));
      Assert.Equal(409, Status(() => store.ValidateCopy("src/a.txt", "src", true)));
      await store.CopyAsync("src/a.txt", "dst", false, (_, _) => { }, Token);
      Assert.Equal(409, Status(() => store.ValidateCopy("src/a.txt", "dst", false)));
      await store.CopyAsync("src/a.txt", "dst", true, (_, _) => { }, Token);
      Assert.Single(store.RecycleItems());
   }

   [Fact]
   public async Task ArchiveSkipsMetaAndExtractAppliesValidSettingsOnly() {
      var store = Store(); store.CreateFolder("", "f"); await Put(store, "f", "x.txt", "x");
      store.SaveFolderSettings("f", NoListing);
      await store.ArchiveAsync("", ["f"], "f.zip", false, (_, _) => { }, Token);
      using (var zip = ZipFile.OpenRead(store.ResolveInside("f.zip")))
         Assert.DoesNotContain(zip.Entries, e => e.Name == ".meta");
      Assert.Equal(404, Status(() => store.ValidateArchive("f", [".meta"], "m.zip", false)));

      using (var zip = ZipFile.Open(store.ResolveInside("in.zip"), ZipArchiveMode.Create)) {
         void Add(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
         Add("x/.meta", Valid); Add("y/.meta", "{}"); Add("y/file.txt", "kept");
      }
      Assert.Empty(store.ReadExtractPlan("in.zip").Conflicts);
      var messages = new List<string>();
      await store.ExtractAsync("in.zip", false, (_, m) => messages.Add(m), Token);
      Assert.False(store.FolderSettings("x").Settings.PublicListing);
      Assert.False(store.FolderSettings("y").Exists);
      Assert.Equal("kept", File.ReadAllText(store.ResolveInside("y/file.txt")));
      Assert.Contains(messages, m => m.Contains("y/.meta", StringComparison.Ordinal));

      // Extracting again replaces existing settings regardless of the overwrite flag and is no conflict.
      using (var zip = ZipFile.Open(store.ResolveInside("again.zip"), ZipArchiveMode.Create)) {
         using var writer = new StreamWriter(zip.CreateEntry("x/.meta").Open()); writer.Write("""{"version":1}""");
      }
      Assert.Empty(store.ReadExtractPlan("again.zip").Conflicts);
      await store.ExtractAsync("again.zip", false, (_, _) => { }, Token);
      Assert.True(store.FolderSettings("x").Settings.PublicListing);

      using (var zip = ZipFile.Open(store.ResolveInside("folder.zip"), ZipArchiveMode.Create)) zip.CreateEntry(".meta/a.txt");
      Assert.Equal(400, Status(() => store.ReadExtractPlan("folder.zip")));
   }

   [Fact]
   public async Task RecycledFoldersLoseTheirSettings() {
      var store = Store(); store.CreateFolder("", "f"); store.CreateFolder("f", "s"); await Put(store, "f/s", "a.txt", "a");
      store.SaveFolderSettings("f", NoListing); store.SaveFolderSettings("f/s", NoListing);
      store.Delete("f");
      var item = Assert.Single(store.RecycleItems());
      Assert.Equal(1, item.Size);
      store.Restore(item.Id);
      Assert.False(store.FolderSettings("f").Exists);
      Assert.False(store.FolderSettings("f/s").Exists);
      Assert.Equal("a", File.ReadAllText(store.ResolveInside("f/s/a.txt")));
   }

   [Fact]
   public async Task ManagedFoldersAreReadOnlyExceptForTheirOwner() {
      var store = Store(); store.Manage("release", "Contoso.Inventory");
      await Put(store, "release", "release.json", "{}", "Contoso.Inventory");
      Assert.Equal(409, Status(() => store.SaveFolderSettings("release", NoListing)));
      Assert.Equal(409, Status(() => store.SaveFolderSettings("release", NoListing, "Contoso.Other")));
      store.SaveFolderSettings("release", NoListing, "Contoso.Inventory");
      var detail = store.FolderSettings("release");
      Assert.True(detail.IsReadOnly); Assert.False(detail.Settings.PublicListing);
      await Assert.ThrowsAsync<ActionException>(() => Put(store, "release", ".meta", Valid));
      Assert.Equal(2, store.ManagedFolders().Single().UsedBytes);
   }

   [Fact]
   public async Task InvalidLegacyMetaIsHiddenAndTreatedAsDefault() {
      var store = Store(); store.CreateFolder("", "old"); await Put(store, "old", "a.txt", "a");
      File.WriteAllText(Path.Combine(store.ResolveInside("old"), ".meta"), "not settings");
      var provider = new CdnFileProvider(store);
      Assert.False(provider.GetFileInfo("/old/.meta").Exists);
      Assert.True(provider.GetDirectoryContents("/old").Exists);
      Assert.DoesNotContain(provider.GetDirectoryContents("/old"), i => i.Name == ".meta");
      Assert.DoesNotContain(store.List("old").Entries, e => e.Name == ".meta");
      var detail = store.FolderSettings("old");
      Assert.True(detail.Exists); Assert.NotNull(detail.Warning); Assert.True(detail.Settings.PublicListing);
      Assert.Equal(404, Status(() => store.ExportFolderSettings("old")));
      store.SaveFolderSettings("old", NoListing);
      Assert.Null(store.FolderSettings("old").Warning);
      Assert.Contains("\"version\": 1", store.ExportFolderSettings("old"));
   }
}
