using System.IO;
using System.Text.RegularExpressions;
using Em.Ui.Core.Release.Format;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.ReleaseNotes;

/// <summary>A versioned note and its folder-derived release status.</summary>
public sealed record ReleaseNoteEntry(ContainerReleaseVersion ParsedVersion, string File, string Content, bool Valid, string Status) {
   /// <summary>The normalized version used as explicit display text.</summary>
   public string Version => ParsedVersion.Full;
   /// <summary>The version and release status.</summary>
   public string Display => $"{Version} · {Status}";
}

/// <summary>The result of evaluating a release note lock.</summary>
public sealed record ReleaseNoteState(bool Allowed, string Message, string? File, string Content, IReadOnlyList<ReleaseNoteEntry> Entries) {
   /// <summary>Whether a valid existing folder can supply history.</summary>
   public bool FolderValid { get; init; }
}

/// <summary>Profile-relative note paths, validation, release ordering and history without WPF or a database.</summary>
public static class ReleaseNoteFolder {
   /// <summary>Resolves a stored relative path; absolute and drive-relative values are invalid.</summary>
   public static string Resolve(string profileFolder, string relative) {
      if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("Choose a release notes folder in the profile.");
      if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Release notes folder must be relative to the profile file folder.");
      return Path.GetFullPath(relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar), Path.GetFullPath(profileFolder));
   }
   /// <summary>Converts a folder selection to a portable profile-relative path, rejecting other drives.</summary>
   public static string Relative(string profileFolder, string folder) {
      var basis = Path.GetFullPath(profileFolder); var full = Path.GetFullPath(folder);
      if (!string.Equals(Path.GetPathRoot(basis), Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
         throw new InvalidDataException("Release notes folder must be on the same drive as the profile file.");
      return Path.GetRelativePath(basis, full).Replace('\\', '/');
   }
   /// <summary>Requires a nonempty Summary section before the next Markdown heading.</summary>
   public static bool HasSummary(string content) {
      var match = Regex.Match(content, @"(?m)^## Summary[ \t]*\r?$\n?(?<body>[\s\S]*?)(?=^#{1,6}[ \t]+|\z)");
      return match.Success && !string.IsNullOrWhiteSpace(match.Groups["body"].Value);
   }
   /// <summary>Gets the target version, with the standard Android versionName fallback.</summary>
   public static string? TargetVersion(ReleaseManifest? manifest, bool android) {
      if (manifest?.Version is { } version) return version;
      var fallback = android ? manifest?.Android?.VersionName : null;
      return fallback is not null && ReleaseManifestSerializer.IsStandardVersion(fallback) ? fallback : null;
   }
   /// <summary>Reads version notes and evaluates one application version against a known target.</summary>
   public static ReleaseNoteState Evaluate(string profileFolder, string relative, string appVersion, IEnumerable<string> serverVersions,
      bool serverKnown, bool exactReleased = false) {
      try {
         var folder = Resolve(profileFolder, relative);
         if (!Directory.Exists(folder)) throw new InvalidDataException($"Release notes folder not found: {relative}");
         var versions = serverVersions.Where(v => ReleaseManifestSerializer.IsStandardVersion(v)).Select(v => ContainerVersion.TryParseFull(v, out var p) ? (ContainerReleaseVersion?)p : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
         ContainerReleaseVersion? highest = versions.Length == 0 ? null : versions.Max();
         var entries = new List<ReleaseNoteEntry>();
         foreach (var file in Directory.EnumerateFiles(folder, "*.md")) {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!ReleaseManifestSerializer.IsStandardVersion(name) || !ContainerVersion.TryParseFull(name, out var parsed)) continue;
            var content = File.ReadAllText(file); var valid = HasSummary(content);
            var released = exactReleased ? versions.Contains(parsed) : highest is { } h && parsed.CompareTo(h) <= 0;
            entries.Add(new(parsed, file, content, valid, !valid ? "Invalid" : released ? "Released" : "Pending"));
         }
         var sorted = entries.OrderByDescending(e => e.ParsedVersion).ToArray();
         if (!ContainerVersion.TryParseFull(appVersion, out var app)) return new(false, "Blocked: release note lock requires a standard application version.", null, "", sorted) { FolderValid = true };
         var note = sorted.FirstOrDefault(e => e.Version == app.Full);
         var error = note is null ? $"{app.Full}.md not found in {relative}" : !note.Valid ? $"{app.Full}.md has no nonempty ## Summary" :
            !serverKnown ? "The release target could not be read." : highest is { } top && app.CompareTo(top) <= 0 ? $"{app.Full} is not newer than {top.Full} on the server" : null;
         return new(error is null, error is null ? $"Release note {app.Full}.md found" : "Blocked: " + error,
            note is null ? null : Relative(profileFolder, note.File), note?.Content ?? "", sorted) { FolderValid = true };
      }
      catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return new(false, "Blocked: " + ex.Message, null, "", []); }
   }
   /// <summary>Combines notes in ascending order, excluding From and including To.</summary>
   public static string Range(IEnumerable<ReleaseNoteEntry> entries, ContainerReleaseVersion from, ContainerReleaseVersion to) =>
      string.Join("\n\n", entries.Where(e => e.ParsedVersion.CompareTo(from) > 0 && e.ParsedVersion.CompareTo(to) <= 0)
         .OrderBy(e => e.ParsedVersion).Select(e => $"# {e.Version}.md\n\n{e.Content}"));
}
