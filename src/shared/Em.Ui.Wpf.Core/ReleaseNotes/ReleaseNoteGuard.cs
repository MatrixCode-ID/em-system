using System.IO;
using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Core.Release;

namespace Em.Ui.Wpf.Core.ReleaseNotes;

/// <summary>Release Manager lock checks shared by Prepare, Sync and status refresh.</summary>
public static class ReleaseNoteGuard {
   /// <summary>Evaluates against the current target, failing closed when it cannot be read.</summary>
   public static async Task<ReleaseNoteState> ReadAsync(ReleaseProfile profile, ReleaseTarget target, string profileFolder,
      CancellationToken token, bool syncing = false) {
      ReleaseManifest? manifest = null; var known = true;
      try {
         var bytes = target is FolderReleaseTarget folder ? await ReadFolderManifest(folder.Description, token) : await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token);
         if (bytes is not null) manifest = ReleaseManifestSerializer.Deserialize(bytes);
         if (bytes is null && target is CdnReleaseTarget cdn && await cdn.HasManifestOnServerAsync(token)) known = false;
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception) { known = false; }
      var android = profile.Platform == ReleasePlatform.Android;
      var version = android && syncing ? profile.Android.PreparedVersionName : profile.Product.Version;
      var remote = ReleaseNoteFolder.TargetVersion(manifest, android);
      var state = ReleaseNoteFolder.Evaluate(profileFolder, profile.ReleaseNotesFolder, version, remote is null ? [] : [remote], known);
      if (android && syncing && profile.Android.PreparedVersionName.Split('+')[0] != profile.Product.Version.Split('+')[0])
         return state with { Allowed = false, Message = "Blocked: application version changed after Prepare. Prepare again before Sync." };
      return state;
   }
   private static async Task<byte[]?> ReadFolderManifest(string folder, CancellationToken token) {
      try { return await File.ReadAllBytesAsync(Path.Combine(folder, ReleaseLayout.ManifestFileName), token); }
      catch (FileNotFoundException) { return null; }
      catch (DirectoryNotFoundException) { return null; }
   }
   /// <summary>Throws before any release mutation when the lock blocks the operation.</summary>
   public static async Task RequireAsync(ReleaseProfile profile, ReleaseTarget target, string profileFolder, CancellationToken token, bool syncing = false) {
      if (!profile.ReleaseNoteLock) return;
      var state = await ReadAsync(profile, target, profileFolder, token, syncing);
      if (!state.Allowed) throw new InvalidDataException(state.Message);
   }
}
