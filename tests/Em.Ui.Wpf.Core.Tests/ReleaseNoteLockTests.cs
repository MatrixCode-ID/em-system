using System.IO;
using System.Text.Json.Nodes;
using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Core.ReleaseNotes;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ReleaseNoteLockTests : IDisposable {
   private readonly string _root = Path.Combine(Path.GetTempPath(), "ReleaseNoteLock-" + Guid.NewGuid().ToString("N"));
   public ReleaseNoteLockTests() => Directory.CreateDirectory(Path.Combine(_root, "notes"));
   public void Dispose() => Directory.Delete(_root, true);
   private void Note(string version, string content = "## Summary\nInventory changes.\n\n## Fixes\n- Fixed.") => File.WriteAllText(Path.Combine(_root, "notes", version + ".md"), content);
   private ReleaseNoteState State(string app = "1.2.0-beta.3", string[]? remote = null, bool known = true, bool exact = false) =>
      ReleaseNoteFolder.Evaluate(_root, "notes", app, remote ?? [], known, exact);

   [Theory]
   [InlineData("1.2.0-prealpha.9", "1.2.0-alpha.1")]
   [InlineData("1.2.0-alpha.99", "1.2.0-beta.1")]
   [InlineData("1.2.0-beta.9", "1.2.0-beta.10")]
   [InlineData("1.2.0-beta.10", "1.2.0-rc.1")]
   [InlineData("1.2.0-rc.99", "1.2.0")]
   [InlineData("1.2.0", "1.3.0-prealpha.1")]
   [InlineData("1.2.0-beta.999999999999999999999", "1.2.0-beta.1000000000000000000000")]
   public void FullVersionOrdersNumerically(string older, string newer) {
      Assert.True(ContainerVersion.TryParseFull(older, out var a)); Assert.True(ContainerVersion.TryParseFull(newer, out var b));
      Assert.True(a.CompareTo(b) < 0); Assert.True(b.CompareTo(a) > 0);
   }
   [Theory] [InlineData("1.2")] [InlineData("1.2.0-dev")] [InlineData("1.2.0-beta.0")] [InlineData("01.2.0")] [InlineData("1.2.0-beta.01")]
   public void FullVersionRejectsNonstandard(string value) => Assert.False(ContainerVersion.TryParseFull(value, out _));
   [Fact] public void MetadataIsRemoved() { Assert.True(ContainerVersion.TryParseFull("1.2.0-beta.12+abc", out var v)); Assert.Equal("1.2.0-beta.12", v.Full); }
   [Fact] public void PathsUseTheProfileFolder() {
      var basis = Path.Combine(_root, "profiles", "Container"); Directory.CreateDirectory(basis);
      Assert.Equal("../../notes", ReleaseNoteFolder.Relative(basis, Path.Combine(_root, "notes")));
      Assert.Equal(Path.Combine(_root, "notes"), ReleaseNoteFolder.Resolve(basis, "..\\..\\notes"));
      Assert.Equal(ReleaseNoteFolder.Resolve(basis, "../../notes"), ReleaseNoteFolder.Resolve(basis, "..\\..\\notes"));
      Assert.Throws<InvalidDataException>(() => ReleaseNoteFolder.Resolve(basis, _root));
      Assert.Throws<InvalidDataException>(() => ReleaseNoteFolder.Resolve(basis, "C:notes"));
      Assert.Throws<InvalidDataException>(() => ReleaseNoteFolder.Relative(basis, "Z:\\notes"));
   }
   [Theory] [InlineData("")] [InlineData("notes-missing")] [InlineData("C:\\notes")]
   public void InvalidFoldersBlock(string folder) => Assert.False(ReleaseNoteFolder.Evaluate(_root, folder, "1.2.0", [], true).Allowed);
   [Theory] [InlineData("# Summary\ntext")] [InlineData("## Summary\n \n## Fixes\ntext")] [InlineData("## Summary")] [InlineData("## Summary\r\n\r\n### Details\r\ntext")]
   public void MissingOrEmptySummaryIsInvalid(string content) { Note("1.2.0-beta.3", content); Assert.False(State().Allowed); Assert.Equal("Invalid", Assert.Single(State().Entries).Status); }
   [Fact] public void NonVersionNotesAreIgnored() { Note("README"); Note("1.2"); Note("1.2.0+build"); Assert.Empty(State().Entries); }
   [Fact] public void MissingNoteAndUnknownTargetBlock() { Assert.False(State().Allowed); Note("1.2.0-beta.3"); Assert.False(State(known: false).Allowed); }
   [Theory] [InlineData(null, true)] [InlineData("1.2.0-beta.2", true)] [InlineData("1.2.0-beta.3", false)] [InlineData("1.2.0", false)]
   public void RequiresStrictlyNewer(string? remote, bool allowed) { Note("1.2.0-beta.3"); Assert.Equal(allowed, State(remote: remote is null ? [] : [remote]).Allowed); }
   [Fact] public void HistoryAndRangeUseVersionOrder() {
      Note("1.2.0-beta.2"); Note("1.2.0-beta.3"); Note("1.2.0-beta.10"); Note("1.2.0", "## Summary\n");
      var entries = State(remote: ["1.2.0-beta.3"]).Entries;
      Assert.Equal(new[] { "Invalid", "Pending", "Released", "Released" }, entries.Select(e => e.Status));
      Assert.Equal("Pending", State(remote: ["1.2.0-beta.3"], exact: true).Entries.Single(e => e.Version == "1.2.0-beta.2").Status);
      ContainerVersion.TryParseFull("1.2.0-beta.2", out var from); ContainerVersion.TryParseFull("1.2.0-beta.10", out var to);
      var range = ReleaseNoteFolder.Range(entries, from, to);
      Assert.DoesNotContain("# 1.2.0-beta.2.md", range); Assert.True(range.IndexOf("# 1.2.0-beta.3.md", StringComparison.Ordinal) < range.IndexOf("# 1.2.0-beta.10.md", StringComparison.Ordinal));
   }
   [Theory] [InlineData("1.2.0", "2.0.0", "1.2.0")] [InlineData(null, "1.2.0-beta.2", "1.2.0-beta.2")] [InlineData(null, "1.2.0-dev", null)] [InlineData(null, "1.2.0+build", null)]
   public void AndroidTargetUsesVersionThenStandardFallback(string? version, string name, string? expected) {
      var manifest = new ReleaseManifest { PublishedAtUtc = DateTime.UtcNow, Files = [], Version = version, Android = new() { FileName = "inventory.apk", ApplicationId = "com.contoso.inventory", VersionName = name, VersionCode = 1 } };
      Assert.Equal(expected, ReleaseNoteFolder.TargetVersion(manifest, true)); Assert.Equal(version, ReleaseNoteFolder.TargetVersion(manifest, false));
   }
   [Theory] [InlineData(ContainerMode.Set, TaggingMode.Standard, false)] [InlineData(ContainerMode.Compose, TaggingMode.Standard, false)]
   [InlineData(ContainerMode.Dockerfile, TaggingMode.Manual, false)] [InlineData(ContainerMode.LocalImage, TaggingMode.Standard, true)] [InlineData(ContainerMode.Template, TaggingMode.Standard, true)]
   public void OnlySingleStandardProfilesCanLock(ContainerMode mode, TaggingMode tagging, bool enabled) {
      var p = PublishProfile.Create(PublishKind.Container); p.ReleaseNoteLock = true; p.Container!.Mode = mode; p.Container.Target.Tagging = tagging;
      Assert.Equal(enabled, p.UsesReleaseNoteLock);
   }
   [Fact] public void OldProfilesAndRunsDefaultOff() {
      var p = PublishProfile.Create(PublishKind.Container); var json = JsonNode.Parse(ProfileJson.Write(p))!; json.AsObject().Remove("releaseNoteLock"); json.AsObject().Remove("releaseNotesFolder");
      Assert.False(ProfileJson.Read(json.ToJsonString()).ReleaseNoteLock);
      var release = new ReleaseProfile { Name = "Contoso.Inventory" }; json = JsonNode.Parse(ReleaseProfile.ToJson(release))!; json.AsObject().Remove("releaseNoteLock"); json.AsObject().Remove("releaseNotesFolder");
      Assert.False(ReleaseProfile.FromJson(json.ToJsonString()).ReleaseNoteLock); Assert.Equal("", new PublishRun().ReleaseNotesFile);
   }
   [Fact] public async Task AndroidSyncRejectsChangedPreparedVersion() {
      Note("1.2.0-beta.3"); var profile = new ReleaseProfile { Name = "Contoso.Inventory", ReleaseNoteLock = true, ReleaseNotesFolder = "notes", Platform = ReleasePlatform.Android };
      profile.Product.Version = "1.2.0-beta.3"; profile.Android.PreparedVersionName = "1.2.0-beta.2";
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "inventory");
      var state = await ReleaseNoteGuard.ReadAsync(profile, target, _root, CancellationToken.None, syncing: true);
      Assert.False(state.Allowed); Assert.Contains("Prepare again", state.Message);
   }
   [Fact] public async Task GuardRechecksFileBeforeSync() {
      Note("1.2.0-beta.3"); var profile = new ReleaseProfile { Name = "Contoso.Inventory", ReleaseNoteLock = true, ReleaseNotesFolder = "notes" }; profile.Product.Version = "1.2.0-beta.3";
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "inventory");
      await ReleaseNoteGuard.RequireAsync(profile, target, _root, CancellationToken.None);
      File.Delete(Path.Combine(_root, "notes", "1.2.0-beta.3.md"));
      await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseNoteGuard.RequireAsync(profile, target, _root, CancellationToken.None, syncing: true));
   }
}
