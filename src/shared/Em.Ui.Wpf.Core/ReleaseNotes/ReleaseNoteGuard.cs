using System.IO;
using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Core.Release;

namespace Em.Ui.Wpf.Core.ReleaseNotes;

/// <summary>
/// Release Manager lock checks shared by Prepare, Sync and status refresh. Prepare and Build launcher only need the
/// note file; an empty Summary blocks Sync, so a release is never published without one.
/// </summary>
public static class ReleaseNoteGuard {
   /// <summary>The explicitly selected note version, with legacy profile compatibility.</summary>
   public static string Version(ReleaseProfile profile) => profile.ReleaseNoteLock && profile.ReleaseNoteVersion.Length > 0 ? profile.ReleaseNoteVersion : profile.Product.Version;
   /// <summary>
   /// Evaluates against the current target, failing closed when it cannot be read. <paramref name="requireSummary"/>
   /// off lets a note without a Summary pass (Prepare); Sync and the status keep requiring it.
   /// </summary>
   public static async Task<ReleaseNoteState> ReadAsync(ReleaseProfile profile, ReleaseTarget target,
      CancellationToken token, bool syncing = false, bool requireSummary = true) {
      ReleaseManifest? manifest = null; var known = true;
      try {
         var bytes = target is FolderReleaseTarget folder ? await ReadFolderManifest(folder.Description, token) : await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token);
         if (bytes is not null) manifest = ReleaseManifestSerializer.Deserialize(bytes);
         if (bytes is null && target is CdnReleaseTarget cdn && await cdn.HasManifestOnServerAsync(token)) known = false;
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception) { known = false; }
      var android = profile.Platform == ReleasePlatform.Android;
      var version = Version(profile);
      var remote = !android ? manifest?.Launcher?.Version ?? ReleaseNoteFolder.TargetVersion(manifest, false) : ReleaseNoteFolder.TargetVersion(manifest, true);
      var state = ReleaseNoteFolder.Evaluate(profile.ReleaseNotesFolder, version, remote is null ? [] : [remote], known, requireSummary: requireSummary);
      if (android && syncing && profile.Android.PreparedVersionName.Split('+')[0] != version.Split('+')[0])
         return state with { Allowed = false, Message = "Blocked: application version changed after Prepare. Prepare again before Sync." };
      return state;
   }
   private static async Task<byte[]?> ReadFolderManifest(string folder, CancellationToken token) {
      try { return await File.ReadAllBytesAsync(Path.Combine(folder, ReleaseLayout.ManifestFileName), token); }
      catch (FileNotFoundException) { return null; }
      catch (DirectoryNotFoundException) { return null; }
   }
   /// <summary>Throws before any release mutation when the lock blocks the operation.</summary>
   public static async Task RequireAsync(ReleaseProfile profile, ReleaseTarget target, CancellationToken token, bool syncing = false,
      bool requireSummary = true) {
      if (!profile.ReleaseNoteLock) return;
      var state = await ReadAsync(profile, target, token, syncing, requireSummary);
      if (!state.Allowed) throw new InvalidDataException(state.Message);
   }
}
