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
      ReleaseNoteFolder.Evaluate(Path.Combine(_root, "notes"), app, remote ?? [], known, exact);

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
   [Theory] [InlineData("C:\\release-notes")] [InlineData("Z:\\release-notes")] [InlineData("\\\\build-server\\notes\\inventory")]
   public void AbsoluteLocationsDoNotDependOnTheProfileDrive(string folder) {
      Assert.Equal(Path.GetFullPath(folder), ReleaseNoteFolder.Resolve(folder));
   }
   [Theory] [InlineData("")] [InlineData("notes")] [InlineData("../../notes")] [InlineData("C:notes")] [InlineData("\\notes")]
   public void InvalidFoldersRequireTheUserToUpdateTheLocation(string folder) {
      var state = ReleaseNoteFolder.Evaluate(folder, "1.2.0", [], true);
      Assert.False(state.Allowed); Assert.False(state.FolderValid);
      Assert.Contains("Open profile settings and update Release notes folder", state.Message);
   }
   [Fact] public void MissingFolderReportsItsLocationAndRecoversAfterReselection() {
      var missing = Path.Combine(_root, "removed-notes");
      var state = ReleaseNoteFolder.Evaluate(missing, "1.2.0-beta.3", [], true);
      Assert.False(state.Allowed); Assert.False(state.FolderValid); Assert.Contains(missing, state.Message);
      Assert.Contains("update Release notes folder", state.Message);
      Note("1.2.0-beta.3");
      Assert.True(State().Allowed);
      Assert.False(Directory.Exists(missing));
   }
   [Fact] public void AFileInPlaceOfTheFolderReportsAnActionableError() {
      var file = Path.Combine(_root, "not-a-folder"); File.WriteAllText(file, "test");
      var state = ReleaseNoteFolder.Evaluate(file, "1.2.0", [], true);
      Assert.False(state.Allowed); Assert.Contains(file, state.Message); Assert.Contains("update Release notes folder", state.Message);
   }
   [Fact] public void UnreadableNoteBlocksAndReportsTheConfiguredLocation() {
      Note("1.2.0-beta.3");
      using var locked = new FileStream(Path.Combine(_root, "notes", "1.2.0-beta.3.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
      var state = State();
      Assert.False(state.Allowed); Assert.Contains(Path.Combine(_root, "notes"), state.Message);
      Assert.Contains("update Release notes folder", state.Message);
   }
   [Fact] public void NoteAndHistoryKeepAbsolutePathsAcrossProfileRoundTrips() {
      Note("1.2.0-beta.3");
      var profile = PublishProfile.Create(PublishKind.Container); profile.ReleaseNotesFolder = Path.Combine(_root, "notes");
      var imported = ProfileJson.Read(ProfileJson.Write(profile));
      Assert.Equal(profile.ReleaseNotesFolder, imported.ReleaseNotesFolder);
      var state = ReleaseNoteFolder.Evaluate(imported.ReleaseNotesFolder, "1.2.0-beta.3", [], true);
      Assert.True(state.Allowed);
      Assert.Equal(Path.Combine(_root, "notes", "1.2.0-beta.3.md"), state.File);
      Assert.Equal(state.File, Assert.Single(state.Entries).File);
   }
   [Theory] [InlineData("# Summary\ntext")] [InlineData("## Summary\n \n## Fixes\ntext")] [InlineData("## Summary")] [InlineData("## Summary\r\n\r\n### Details\r\ntext")]
   public void MissingOrEmptySummaryIsInvalid(string content) { Note("1.2.0-beta.3", content); Assert.False(State().Allowed); Assert.Equal("Invalid", Assert.Single(State().Entries).Status); }
   [Fact] public void NonVersionNotesAreIgnored() { Note("README"); Note("1.2"); Note("1.2.0+build"); Assert.Empty(State().Entries); }
   [Fact] public void MissingNoteAndUnknownTargetBlock() { Assert.False(State().Allowed); Note("1.2.0-beta.3"); Assert.False(State(known: false).Allowed); }
   [Fact] public void SelectedFileKeepsItsExactVersionWhenNewerNotesAppear() {
      Note("1.2.0-beta.3", "## Summary\nChosen release.");
      Note("1.2.0-beta.10", "## Summary\nAnother release.");
      var state = State();
      Assert.True(state.Allowed); Assert.EndsWith("1.2.0-beta.3.md", state.File!);
      Assert.Contains("Chosen release", state.Content); Assert.DoesNotContain("Another release", state.Content);
      Assert.Equal("1.2.0-beta.10", state.Entries[0].Version);
      Assert.Equal("1.2.0-beta.3.md · Pending", state.Entries[1].FileDisplay);
      Assert.Equal(state.File, State().File);
   }
   [Fact] public void ReleasedSelectionIsBlockedWithoutChoosingANewerFileAutomatically() {
      Note("1.2.0-beta.3"); Note("1.2.0-beta.10");
      var state = State(remote: ["1.2.0-beta.3"], exact: true);
      Assert.False(state.Allowed); Assert.EndsWith("1.2.0-beta.3.md", state.File!);
      Assert.Contains("not newer", state.Message);
      Assert.Equal("1.2.0-beta.3.md · Released", state.Entries.Single(e => e.Version == "1.2.0-beta.3").FileDisplay);
   }
   [Fact] public void InvalidSelectionCanBeInspectedAndReplacedWithAValidFile() {
      Note("1.2.0-beta.3", "## Fixes\nMissing summary."); Note("1.2.0-beta.10");
      var invalid = State();
      Assert.False(invalid.Allowed); Assert.Contains("Missing summary", invalid.Content);
      Assert.Contains(invalid.Entries, e => e.FileDisplay == "1.2.0-beta.3.md · Invalid");
      var selected = State(app: "1.2.0-beta.10");
      Assert.True(selected.Allowed); Assert.EndsWith("1.2.0-beta.10.md", selected.File!);
   }
   [Fact] public void DeletedSelectionDoesNotFallBackToAnotherAvailableFile() {
      Note("1.2.0-beta.3"); Note("1.2.0-beta.10"); Assert.True(State().Allowed);
      File.Delete(Path.Combine(_root, "notes", "1.2.0-beta.3.md"));
      var state = State();
      Assert.False(state.Allowed); Assert.Null(state.File);
      Assert.Contains("1.2.0-beta.3.md not found", state.Message);
      Assert.Single(state.Entries);
   }
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
      Note("1.2.0-beta.3"); var profile = new ReleaseProfile { Name = "Contoso.Inventory", ReleaseNoteLock = true, ReleaseNotesFolder = Path.Combine(_root, "notes"), Platform = ReleasePlatform.Android };
      profile.Product.Version = "1.2.0-beta.3"; profile.Android.PreparedVersionName = "1.2.0-beta.2";
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "inventory");
      var state = await ReleaseNoteGuard.ReadAsync(profile, target, CancellationToken.None, syncing: true);
      Assert.False(state.Allowed); Assert.Contains("Prepare again", state.Message);
   }
   [Fact] public async Task GuardRechecksFileBeforeSync() {
      Note("1.2.0-beta.3"); var profile = new ReleaseProfile { Name = "Contoso.Inventory", ReleaseNoteLock = true, ReleaseNotesFolder = Path.Combine(_root, "notes") }; profile.Product.Version = "1.2.0-beta.3";
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "inventory");
      await ReleaseNoteGuard.RequireAsync(profile, target, CancellationToken.None);
      File.Delete(Path.Combine(_root, "notes", "1.2.0-beta.3.md"));
      await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseNoteGuard.RequireAsync(profile, target, CancellationToken.None, syncing: true));
   }
   [Fact] public async Task EmptySummaryAllowsPrepareButBlocksSync() {
      Note("1.2.0-beta.3", "# 1.2.0-beta.3\n\n## Summary\n\n## Fixes\n"); var profile = new ReleaseProfile { Name = "Contoso.Inventory", ReleaseNoteLock = true, ReleaseNotesFolder = Path.Combine(_root, "notes") }; profile.Product.Version = "1.2.0-beta.3";
      var target = new FolderReleaseTarget(Path.Combine(_root, "target"), "inventory");
      await ReleaseNoteGuard.RequireAsync(profile, target, CancellationToken.None, requireSummary: false);
      var sync = await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseNoteGuard.RequireAsync(profile, target, CancellationToken.None, syncing: true));
      Assert.Contains("Summary", sync.Message);
      File.Delete(Path.Combine(_root, "notes", "1.2.0-beta.3.md"));
      await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseNoteGuard.RequireAsync(profile, target, CancellationToken.None, requireSummary: false));
   }
   [Fact] public void ActiveNoteIsTheHighestUnreleasedFileIncludingAnUnfilledOne() {
      Note("1.2.0-beta.3"); Note("1.2.0-beta.4", "## Summary\n"); Note("1.2.0-beta.2");
      Assert.Equal("1.2.0-beta.4", ReleaseNoteFolder.Active(State(remote: ["1.2.0-beta.3"]).Entries)!.Version);
      Assert.Null(ReleaseNoteFolder.Active(State(remote: ["1.2.0-beta.4"]).Entries));
   }
   [Theory] [InlineData("1.0.0-beta.8", "1.0.0-beta.9")] [InlineData("1.0.0-rc.1", "1.0.0-rc.2")] [InlineData("1.2.0", "1.2.1")]
   public void NextRaisesTheChannelNumberOrThePatch(string released, string next) {
      Assert.True(ContainerVersion.TryParseFull(released, out var v)); Assert.Equal(next, ReleaseNoteFolder.Next(v));
   }
   [Fact] public void AllReleasedCreatesTheNextEmptyNoteOnceShapedLikeThePreviousOne() {
      Note("1.0.0-beta.8", "# app 1.0.0-beta.8\n\nSubject: app\nDate: 2026-01-02\nPrevious: 1.0.0-beta.7\nSource: main abc\n\n## Summary\nShipped.\n\n## Fixes\n\n### Orders\n- Fixed.\n\n## Engine update\nNone.");
      var folder = Path.Combine(_root, "notes");
      var created = ReleaseNoteFolder.EnsureNext(folder, State(remote: ["1.0.0-beta.8"]));
      Assert.EndsWith("1.0.0-beta.9.md", created!);
      var text = File.ReadAllText(created!).Replace("\r\n", "\n");
      Assert.Equal("# app 1.0.0-beta.9\n\nSubject: app\nDate:\nPrevious: 1.0.0-beta.8\nSource:\n\n## Summary\n\n## Fixes\n\n## Engine update\n", text);
      var state = State(app: "1.0.0-beta.9", remote: ["1.0.0-beta.8"]);
      Assert.Equal("1.0.0-beta.9.md · Invalid", ReleaseNoteFolder.Active(state.Entries)!.FileDisplay);
      Assert.False(state.Allowed);
      Assert.Null(ReleaseNoteFolder.EnsureNext(folder, state));
   }
   [Fact] public void NoNoteIsCreatedWhileOneIsUnreleased() {
      Note("1.0.0-beta.8");
      var folder = Path.Combine(_root, "notes");
      Assert.Null(ReleaseNoteFolder.EnsureNext(folder, State(remote: [])));
      Assert.Single(Directory.GetFiles(folder));
   }
}
