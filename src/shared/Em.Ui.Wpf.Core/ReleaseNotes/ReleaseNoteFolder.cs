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
   /// <summary>The release filename and status shown by the container version selector.</summary>
   public string FileDisplay => $"{Path.GetFileName(File)} · {Status}";
   /// <summary>Whether the target already holds this version (or a newer one), whatever the note content.</summary>
   public bool Released { get; init; }
}

/// <summary>The result of evaluating a release note lock.</summary>
public sealed record ReleaseNoteState(bool Allowed, string Message, string? File, string Content, IReadOnlyList<ReleaseNoteEntry> Entries) {
   /// <summary>Whether a valid existing folder can supply history.</summary>
   public bool FolderValid { get; init; }
}

/// <summary>Machine-local note paths, validation, release ordering and history without WPF or a database.</summary>
public static class ReleaseNoteFolder {
   /// <summary>Validates and normalizes an absolute machine-local path without a profile or working-directory basis.</summary>
   public static string Resolve(string folder) {
      if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("No release notes folder is configured.");
      if (!Path.IsPathFullyQualified(folder)) throw new InvalidDataException("Release notes folder must be an absolute path on this machine. Relative paths from older profiles must be selected again.");
      return Path.GetFullPath(folder);
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
   public static ReleaseNoteState Evaluate(string notesFolder, string appVersion, IEnumerable<string> serverVersions,
      bool serverKnown, bool exactReleased = false) {
      try {
         var folder = Resolve(notesFolder);
         var versions = serverVersions.Where(v => ReleaseManifestSerializer.IsStandardVersion(v)).Select(v => ContainerVersion.TryParseFull(v, out var p) ? (ContainerReleaseVersion?)p : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
         ContainerReleaseVersion? highest = versions.Length == 0 ? null : versions.Max();
         var entries = new List<ReleaseNoteEntry>();
         foreach (var file in Directory.EnumerateFiles(folder, "*.md")) {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!ReleaseManifestSerializer.IsStandardVersion(name) || !ContainerVersion.TryParseFull(name, out var parsed)) continue;
            var content = File.ReadAllText(file); var valid = HasSummary(content);
            var released = exactReleased ? versions.Contains(parsed) : highest is { } h && parsed.CompareTo(h) <= 0;
            entries.Add(new(parsed, file, content, valid, !valid ? "Invalid" : released ? "Released" : "Pending") { Released = released });
         }
         var sorted = entries.OrderByDescending(e => e.ParsedVersion).ToArray();
         if (!ContainerVersion.TryParseFull(appVersion, out var app)) return new(false, "Blocked: release note lock requires a standard application version.", null, "", sorted) { FolderValid = true };
         var note = sorted.FirstOrDefault(e => e.Version == app.Full);
         var error = note is null ? $"{app.Full}.md not found in {folder}" : !note.Valid ? $"{app.Full}.md has no nonempty ## Summary" :
            !serverKnown ? "The release target could not be read." : highest is { } top && app.CompareTo(top) <= 0 ? $"{app.Full} is not newer than {top.Full} on the server" : null;
         return new(error is null, error is null ? $"Release note {app.Full}.md found" : "Blocked: " + error,
            note?.File, note?.Content ?? "", sorted) { FolderValid = true };
      }
      catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) {
         return new(false, $"Blocked: Cannot read release notes folder '{notesFolder}': {ex.Message} Open profile settings and update Release notes folder.", null, "", []);
      }
   }
   /// <summary>The note to release next: the highest one the target does not hold yet, an unfilled one included.</summary>
   public static ReleaseNoteEntry? Active(IEnumerable<ReleaseNoteEntry> entries) =>
      entries.Where(e => !e.Released).OrderByDescending(e => e.ParsedVersion).FirstOrDefault();
   /// <summary>The version after a released one: N + 1 on a prerelease channel, the next patch after a stable release.</summary>
   public static string Next(ContainerReleaseVersion released) =>
      released.Channel == ContainerVersion.Release ? $"{released.Major}.{released.Minor}.{released.Patch + 1}"
         : $"{released.Major}.{released.Minor}.{released.Patch}-{released.Channel}.{released.Number + 1}";
   /// <summary>
   /// When every note in a readable folder is released, creates the empty note of the next version so there is
   /// always one note to fill. Returns the created file, or <c>null</c> when nothing was created.
   /// </summary>
   public static string? EnsureNext(string notesFolder, ReleaseNoteState state) {
      if (!state.FolderValid || state.Entries.Count == 0 || state.Entries.Any(e => !e.Released)) return null;
      var released = state.Entries.Max(e => e.ParsedVersion);
      var file = Path.Combine(Resolve(notesFolder), Next(released) + ".md");
      if (File.Exists(file)) return null;
      var source = state.Entries.First(e => e.ParsedVersion.Equals(released));
      using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
      using (var writer = new StreamWriter(stream)) writer.Write(Template(source.Content, released.Full, Next(released)));
      return file;
   }
   /// <summary>
   /// An empty note shaped like the previous one: its title with the new version, its metadata keys (Subject kept,
   /// Previous set to the released version, the rest empty) and its second-level headings without content.
   /// The Summary stays empty, so the note is Invalid until it is written.
   /// </summary>
   public static string Template(string previousNote, string previous, string next) {
      var nl = Environment.NewLine; var title = $"# {next}"; var meta = new List<string>(); var headings = new List<string>();
      var lines = previousNote.Replace("\r\n", "\n").Split('\n');
      var titled = false;
      foreach (var line in lines) {
         if (!titled && line.StartsWith("# ")) { title = line.Contains(previous) ? line.Replace(previous, next) : title; titled = true; }
         else if (line.StartsWith("## ")) headings.Add(line.TrimEnd());
         else if (headings.Count == 0 && Regex.Match(line, @"^(?<key>[A-Za-z][A-Za-z ]*):(?<value>.*)$") is { Success: true } m) {
            var key = m.Groups["key"].Value;
            meta.Add(key + ":" + (key == "Subject" ? m.Groups["value"].Value.TrimEnd() : key == "Previous" ? " " + previous : ""));
         }
      }
      if (!headings.Contains("## Summary")) headings.Insert(0, "## Summary");
      var text = title + nl + nl;
      if (meta.Count > 0) text += string.Join(nl, meta) + nl + nl;
      return text + string.Join(nl + nl, headings) + nl;
   }
   /// <summary>Combines notes in ascending order, excluding From and including To.</summary>
   public static string Range(IEnumerable<ReleaseNoteEntry> entries, ContainerReleaseVersion from, ContainerReleaseVersion to) =>
      string.Join("\n\n", entries.Where(e => e.ParsedVersion.CompareTo(from) > 0 && e.ParsedVersion.CompareTo(to) <= 0)
         .OrderBy(e => e.ParsedVersion).Select(e => $"# {e.Version}.md\n\n{e.Content}"));
}
