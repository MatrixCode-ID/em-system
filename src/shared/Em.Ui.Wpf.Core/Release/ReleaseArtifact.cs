using Em.Ui.Core.Release.Format;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Reads the prepared application identity and records build provenance outside its binaries.</summary>
public static class ReleaseArtifact {
   /// <summary>Reads the actual host binary, or the version of the hash-matched prepared APK.</summary>
   public static string? ReadVersion(LocalSnapshot local, ReleaseProfile profile) {
      if (profile.Platform == ReleasePlatform.Android) {
         return local.Files.Count == 1 && local.Files[0].Path == profile.AndroidApkFileName &&
            local.Files[0].Sha256 == profile.Android.PreparedApkSha256 && profile.Android.PreparedVersionCode > 0
            ? profile.Android.PreparedVersionName : null;
      }
      var name = Path.GetFileNameWithoutExtension(profile.Product.AppExe) + ".dll";
      var file = local.Files.FirstOrDefault(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));
      return file is null ? null : ReadBinaryVersion(local.FullPathOf(file.Path));
   }

   /// <summary>Returns the standard signed version, stripping application build metadata.</summary>
   public static string? ManifestVersion(string? version) {
      var value = version?.Split('+')[0];
      return value is not null && ReleaseManifestSerializer.IsStandardVersion(value) ? value : null;
   }

   /// <summary>Writes a local audit record beside the publish folder; it is never included in the release.</summary>
   public static async Task WriteBuildRecordAsync(ReleaseProfile profile, string project, CancellationToken token) {
      var artifact = profile.Platform == ReleasePlatform.Android ? profile.AndroidApkFileName
         : Path.GetFileNameWithoutExtension(profile.Product.AppExe) + ".dll";
      var file = Path.Combine(profile.PublishFolder, artifact);
      var version = profile.Platform == ReleasePlatform.Android ? profile.Android.PreparedVersionName : ReadBinaryVersion(file);
      var revision = await SourceRevisionAsync(project, token).ConfigureAwait(false);
      await using var stream = File.OpenRead(file);
      var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
      var record = new { appId = profile.Product.AppId, version, preparedAtUtc = DateTime.UtcNow,
         hostProject = Path.GetFileName(project), sourceRevision = revision, artifact, sha256 = hash };
      var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.PublishFolder)) + ".build.json";
      var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try {
         await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
         File.Move(temporary, path, true);
      }
      finally { if (File.Exists(temporary)) File.Delete(temporary); }
   }

   private static string? ReadBinaryVersion(string path) {
      try {
         var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
         return string.IsNullOrWhiteSpace(version) ? null : version;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
   }

   private static async Task<string?> SourceRevisionAsync(string project, CancellationToken token) {
      try {
         var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
         foreach (var argument in new[] { "-C", Path.GetDirectoryName(Path.GetFullPath(project))!, "rev-parse", "HEAD" }) start.ArgumentList.Add(argument);
         using var process = Process.Start(start)!;
         var output = process.StandardOutput.ReadToEndAsync(token);
         var error = process.StandardError.ReadToEndAsync(token);
         try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
         catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
         await error.ConfigureAwait(false);
         var revision = (await output.ConfigureAwait(false)).Trim();
         return process.ExitCode == 0 ? revision : null;
      }
      catch (Win32Exception) { return null; }
   }
}
