using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Navigations;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ReleaseArtifactTests : IDisposable {
   private readonly string _root = Path.Combine(Path.GetTempPath(), "ReleaseArtifact-" + Guid.NewGuid().ToString("N"));
   public ReleaseArtifactTests() => Directory.CreateDirectory(_root);
   public void Dispose() {
      foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
      Directory.Delete(_root, true);
   }

   [Fact]
   public async Task PreparedWindowsVersionWinsOverEditedProfileAndSyncSignsItWithoutLock() {
      var source = typeof(ReleaseArtifactTests).Assembly.Location;
      var folder = Path.Combine(_root, "publish"); Directory.CreateDirectory(folder);
      File.Copy(source, Path.Combine(folder, "Fixture.dll"));
      var profile = new ReleaseProfile { Name = "Fixture", PublishFolder = folder };
      profile.Product.AppExe = "Fixture.exe"; profile.Product.Version = "99.0.0";
      var local = await LocalPublish.ScanAsync(folder, null, TestContext.Current.CancellationToken);
      var actual = FileVersionInfo.GetVersionInfo(source).ProductVersion!;
      Assert.Equal(actual, ReleaseArtifact.ReadVersion(local, profile));
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "release");
      var comparison = await ReleaseComparer.CompareAsync(target, local, TestContext.Current.CancellationToken);
      using var key = ReleasePemKey.Open(ReleasePemKey.Create());
      var manifest = await ReleaseSync.SyncAsync(target, comparison, key, null, TestContext.Current.CancellationToken, profile: profile);
      Assert.Equal(ReleaseArtifact.ManifestVersion(actual), manifest.Version);
      Assert.NotEqual("99.0.0", manifest.Version);
      Assert.Equal(manifest.Version, ReleaseManifestSerializer.Deserialize(await target.ReadFileAsync("release.json", TestContext.Current.CancellationToken) ?? []).Version);
   }

   [Fact]
   public async Task MissingHostDoesNotFallBackToProfileOrEngineVersion() {
      var folder = Path.Combine(_root, "publish"); Directory.CreateDirectory(folder);
      await File.WriteAllTextAsync(Path.Combine(folder, "unrelated.txt"), "data", TestContext.Current.CancellationToken);
      var profile = new ReleaseProfile { Name = "Fixture" };
      profile.Product.AppExe = "Missing.exe"; profile.Product.Version = "9.0.0";
      var local = await LocalPublish.ScanAsync(folder, null, TestContext.Current.CancellationToken);
      Assert.Null(ReleaseArtifact.ReadVersion(local, profile));
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "release");
      var comparison = await ReleaseComparer.CompareAsync(target, local, TestContext.Current.CancellationToken);
      using var key = ReleasePemKey.Open(ReleasePemKey.Create());
      Assert.Null((await ReleaseSync.SyncAsync(target, comparison, key, null, TestContext.Current.CancellationToken, profile: profile)).Version);
   }

   [Fact]
   public async Task LockRejectsProfileVersionThatDoesNotMatchThePreparedHostBeforeWriting() {
      var folder = Path.Combine(_root, "publish"); Directory.CreateDirectory(folder);
      File.Copy(typeof(ReleaseArtifactTests).Assembly.Location, Path.Combine(folder, "Fixture.dll"));
      var notes = Path.Combine(_root, "notes"); Directory.CreateDirectory(notes);
      await File.WriteAllTextAsync(Path.Combine(notes, "99.0.0.md"), "## Summary\nNew release.", TestContext.Current.CancellationToken);
      var profile = new ReleaseProfile { Name = "Fixture", ReleaseNoteLock = true, ReleaseNotesFolder = notes };
      profile.Product.AppExe = "Fixture.exe"; profile.Product.Version = "99.0.0";
      var local = await LocalPublish.ScanAsync(folder, null, TestContext.Current.CancellationToken);
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "release");
      var comparison = await ReleaseComparer.CompareAsync(target, local, TestContext.Current.CancellationToken);
      using var key = ReleasePemKey.Open(ReleasePemKey.Create());
      await Assert.ThrowsAsync<ReleaseConflictException>(() => ReleaseSync.SyncAsync(target, comparison, key, null, TestContext.Current.CancellationToken, profile: profile));
      Assert.Null(await target.ReadFileAsync("release.json", TestContext.Current.CancellationToken));
   }

   [Fact]
   public async Task AndroidVersionRequiresMatchingPreparedBytes() {
      var folder = Path.Combine(_root, "publish"); Directory.CreateDirectory(folder);
      await File.WriteAllTextAsync(Path.Combine(folder, "fixture.apk"), "apk", TestContext.Current.CancellationToken);
      var local = await LocalPublish.ScanAsync(folder, null, TestContext.Current.CancellationToken);
      var profile = new ReleaseProfile { Name = "Fixture", Platform = ReleasePlatform.Android };
      profile.Android.ApkFileName = "fixture.apk"; profile.Android.PreparedVersionName = "1.2.0-beta.3";
      profile.Android.PreparedVersionCode = 3; profile.Android.PreparedApkSha256 = local.Files[0].Sha256;
      Assert.Equal("1.2.0-beta.3", ReleaseArtifact.ReadVersion(local, profile));
      profile.Android.PreparedApkSha256 = new string('a', 64);
      Assert.Null(ReleaseArtifact.ReadVersion(local, profile));
   }

   [Theory]
   [InlineData("1.2.0-beta.3+abc", "1.2.0-beta.3")]
   [InlineData("1.2.0", "1.2.0")]
   [InlineData("1.2.0-dev", null)]
   [InlineData(null, null)]
   public void ManifestUsesOnlySupportedApplicationVersions(string? actual, string? expected) =>
      Assert.Equal(expected, ReleaseArtifact.ManifestVersion(actual));

   [Fact]
   public void LockedFileSelectsExactVersionWithoutAutomaticIncrementAndPersists() {
      var profile = new ReleaseProfile { Name = "Fixture", ReleaseNoteLock = true, ReleaseNoteVersion = "1.2.0-rc.7" };
      profile.Product.Version = "0.1.0-alpha.1";
      profile.Launcher.BaseVersion = "9.0.0"; profile.Launcher.Channel = "beta";
      Assert.Equal("1.2.0-rc.7", LauncherBuild.SelectVersion(profile, "new", new() { Version = "1.2.0-rc.6", InputHash = "old" }));
      Assert.Equal("1.2.0-rc.7", ReleaseProfile.FromJson(ReleaseProfile.ToJson(profile)).ReleaseNoteVersion);
      var root = Path.Combine(_root, "notes"); Directory.CreateDirectory(root);
      File.WriteAllText(Path.Combine(root, "1.2.0-rc.7.md"), "## Summary\nSelected release.");
      File.WriteAllText(Path.Combine(root, "1.2.0-rc.8.md"), "## Summary\nNewer release.");
      var state = Em.Ui.Wpf.Core.ReleaseNotes.ReleaseNoteFolder.Evaluate(root, profile.ReleaseNoteVersion, [], true);
      Assert.EndsWith("1.2.0-rc.7.md", state.File!);
      var vm = new ReleaseManagerVm();
      typeof(ReleaseManagerVm).GetProperty(nameof(vm.Profile))!.SetValue(vm, profile);
      typeof(ReleaseManagerVm).GetProperty(nameof(vm.NoteState))!.SetValue(vm, state);
      vm.SelectedNoteFile = state.Entries.Single(e => e.Version == "1.2.0-rc.8");
      Assert.Equal("1.2.0-rc.8", profile.ReleaseNoteVersion);
      vm.SelectedNoteFile = state.Entries.Single(e => e.Version == "1.2.0-rc.7");
      Assert.Equal("1.2.0-rc.7", profile.ReleaseNoteVersion);
      File.Delete(state.File!);
      Assert.False(Em.Ui.Wpf.Core.ReleaseNotes.ReleaseNoteFolder.Evaluate(root, profile.ReleaseNoteVersion, [], true).Allowed);
      profile.ReleaseNoteLock = false;
      Assert.Equal("9.0.0-beta.1", LauncherBuild.SelectVersion(profile, "new", null));
   }

   [Fact]
   public async Task BuildRecordIsOutsideDistributionAndCarriesVersionAndArtifactHash() {
      var publish = Path.Combine(_root, "publish"); Directory.CreateDirectory(publish);
      var source = typeof(ReleaseArtifactTests).Assembly.Location;
      File.Copy(source, Path.Combine(publish, "Fixture.dll"));
      var profile = new ReleaseProfile { Name = "Fixture", PublishFolder = publish };
      profile.Product.AppId = "Contoso.Fixture"; profile.Product.AppExe = "Fixture.exe";
      await ReleaseArtifact.WriteBuildRecordAsync(profile, Path.Combine(_root, "Fixture.csproj"), TestContext.Current.CancellationToken);
      using var record = JsonDocument.Parse(await File.ReadAllTextAsync(publish + ".build.json", TestContext.Current.CancellationToken));
      Assert.Equal(FileVersionInfo.GetVersionInfo(source).ProductVersion, record.RootElement.GetProperty("version").GetString());
      var local = await LocalPublish.ScanAsync(publish, null, TestContext.Current.CancellationToken);
      Assert.Equal("Fixture.dll", Assert.Single(local.Files).Path);
      Assert.Equal(local.Files[0].Sha256, record.RootElement.GetProperty("sha256").GetString());
   }

   [Fact]
   public void ServerCaptionShowsPublishedBuildVersionWithoutLocalComparison() {
      var vm = new ReleaseManagerVm();
      var show = typeof(ReleaseManagerVm).GetMethod("ShowServerRelease", BindingFlags.NonPublic | BindingFlags.Instance)!;
      var time = new DateTime(2026, 10, 26, 1, 2, 0, DateTimeKind.Utc);
      show.Invoke(vm, [new ReleaseManifest { Version = "0.1.0-alpha.1", PublishedAtUtc = time, Files = [],
         Launcher = new() { FileName = "installer.exe", Version = "1.0.0-beta.7", InputHash = new string('a', 64) } }, null]);
      Assert.False(vm.HasComparison);
      Assert.Contains("Server release: 1.0.0-beta.7", vm.TargetReleaseCaption);
      Assert.Contains("application 0.1.0-alpha.1", vm.TargetReleaseCaption);
      Assert.Contains("2026-10-26 01:02 UTC", vm.TargetReleaseCaption);
      show.Invoke(vm, [new ReleaseManifest { PublishedAtUtc = time, Files = [],
         Launcher = new() { FileName = "installer.exe", Version = "1.0.0-beta.7", InputHash = new string('a', 64) } }, null]);
      Assert.Contains("Server release: 1.0.0-beta.7", vm.TargetReleaseCaption);
      show.Invoke(vm, [new ReleaseManifest { PublishedAtUtc = time, Files = [] }, null]);
      Assert.Contains("Version not recorded", vm.TargetReleaseCaption);
   }

   [Fact]
   public void ServerCaptionReportsMissingAndUnreadableReleasesWithoutLocalFallback() {
      var vm = new ReleaseManagerVm();
      var show = typeof(ReleaseManagerVm).GetMethod("ShowServerRelease", BindingFlags.NonPublic | BindingFlags.Instance)!;
      show.Invoke(vm, [null, null]); Assert.Contains("No release published", vm.TargetReleaseCaption);
      show.Invoke(vm, [null, "Connection failed"]); Assert.Contains("Connection failed", vm.TargetReleaseCaption);
      Assert.DoesNotContain("Local prepared", vm.TargetReleaseCaption);
   }

   [Fact]
   public async Task ServerManifestLoadsWithoutLocalPublishFolderOrComparison() {
      var target = Path.Combine(_root, "target"); Directory.CreateDirectory(Path.Combine(target, "release"));
      var manifest = new ReleaseManifest { PublishedAtUtc = DateTime.UtcNow,
         Files = [new() { Path = "installer.exe", Size = 0, Sha256 = new string('a', 64) }],
         Launcher = new() { FileName = "installer.exe", Version = "1.0.0-beta.7", InputHash = new string('b', 64) } };
      await File.WriteAllBytesAsync(Path.Combine(target, "release", "release.json"), ReleaseManifestSerializer.Serialize(manifest), TestContext.Current.CancellationToken);
      var vm = new ReleaseManagerVm();
      typeof(ReleaseManagerVm).GetProperty(nameof(vm.EmApp))!.SetValue(vm, RuntimeHelpers.GetUninitializedObject(typeof(EmApp)));
      void Set(string name, object value) => typeof(ReleaseManagerVm).GetProperty(name)!.SetValue(vm, value);
      Set(nameof(vm.Profile), new ReleaseProfile { Name = "Fixture" });
      Set(nameof(vm.TargetKind), ReleaseTargetKind.Folder); Set(nameof(vm.TargetFolder), target); Set(nameof(vm.ReleaseFolder), "release");
      var refresh = typeof(ReleaseManagerVm).GetMethod("RefreshServerReleaseAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
      await (Task)refresh.Invoke(vm, null)!;
      Assert.False(vm.HasComparison); Assert.False(vm.HasPublishContent);
      Assert.Contains("Server release: 1.0.0-beta.7", vm.TargetReleaseCaption);
      File.Delete(Path.Combine(target, "release", "release.json"));
      await (Task)refresh.Invoke(vm, null)!;
      Assert.Contains("No release published", vm.TargetReleaseCaption);
   }

   [Fact]
   public async Task UnrelatedCommitDoesNotChangePreparedWpfModuleButSourceChangeDoes() {
      var repo = Path.Combine(_root, "repo"); Directory.CreateDirectory(repo);
      var project = Path.Combine(repo, "Fixture.csproj");
      await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Version>1.2.0-beta.3</Version></PropertyGroup></Project>", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(Path.Combine(repo, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(Path.Combine(repo, "View.xaml"), "<UserControl x:Class=\"Fixture.View\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><TextBlock Text=\"Fixture\"/></UserControl>", TestContext.Current.CancellationToken);
      var code = Path.Combine(repo, "View.xaml.cs");
      const string original = "namespace Fixture; public partial class View : System.Windows.Controls.UserControl { public View() { InitializeComponent(); } public static int Value => 1; }";
      await File.WriteAllTextAsync(code, original, TestContext.Current.CancellationToken);
      await Run(repo, "git", "init", "--quiet");
      await Run(repo, "git", "remote", "add", "origin", "https://github.com/example/release-fixture.git");
      async Task Commit() {
         await Run(repo, "git", "add", ".");
         await Run(repo, "git", "-c", "user.name=Release Fixture", "-c", "user.email=fixture@example.test", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Fixture");
      }
      async Task<(byte[] Dll, byte[] Pdb)> Build(string name) {
         var output = Path.Combine(_root, name);
         await Run(repo, "dotnet", ["build", project, "-c", "Release", "-o", output, "--disable-build-servers", "-v:q", .. ReleaseBuilder.DeterminismArguments]);
         return (await File.ReadAllBytesAsync(Path.Combine(output, "Fixture.dll"), TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(Path.Combine(output, "Fixture.pdb"), TestContext.Current.CancellationToken));
      }
      await Commit(); var first = await Build("first");
      await File.WriteAllTextAsync(Path.Combine(repo, "notes.txt"), "Unrelated commit", TestContext.Current.CancellationToken);
      await Commit(); Directory.Delete(Path.Combine(repo, "obj"), true);
      var second = await Build("second");
      Assert.Equal(first.Dll, second.Dll); Assert.Equal(first.Pdb, second.Pdb);
      var profile = new ReleaseProfile { Name = "Fixture", PublishFolder = Path.Combine(_root, "second") };
      profile.Product.AppExe = "Fixture.exe"; profile.Product.AppId = "Contoso.Fixture";
      await ReleaseArtifact.WriteBuildRecordAsync(profile, project, TestContext.Current.CancellationToken);
      using (var record = JsonDocument.Parse(await File.ReadAllTextAsync(profile.PublishFolder + ".build.json", TestContext.Current.CancellationToken))) {
         Assert.Equal("1.2.0-beta.3", record.RootElement.GetProperty("version").GetString());
         Assert.Matches("^[0-9a-f]{40}$", record.RootElement.GetProperty("sourceRevision").GetString()!);
      }
      using (var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(second.Pdb))) {
         var metadata = provider.GetMetadataReader();
         Assert.DoesNotContain(metadata.CustomDebugInformation, handle =>
            metadata.GetGuid(metadata.GetCustomDebugInformation(handle).Kind) == new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a"));
      }
      await File.WriteAllTextAsync(code, original.Replace("Value => 1", "Value => 2"), TestContext.Current.CancellationToken);
      await Commit(); Directory.Delete(Path.Combine(repo, "obj"), true);
      var changed = await Build("changed");
      Assert.False(first.Dll.AsSpan().SequenceEqual(changed.Dll));
      Assert.False(first.Pdb.AsSpan().SequenceEqual(changed.Pdb));
   }

   private static async Task Run(string directory, string executable, params string[] arguments) {
      var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false,
         CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
      foreach (var argument in arguments) start.ArgumentList.Add(argument);
      start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
      using var process = Process.Start(start)!;
      var token = TestContext.Current.CancellationToken;
      var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
      try { await process.WaitForExitAsync(token); }
      catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
      Assert.True(process.ExitCode == 0, (await output) + (await error));
   }
}
