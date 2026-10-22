using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core;

// Folder settings live in a ".meta" file inside the folder they apply to. The file is never listed, counted,
// archived, recycled or served; ResolveInside refuses any path that names it, checked against the real
// name on disk so case, 8.3 short names and other spellings cannot reach it either.
internal sealed partial class CdnStore {
   private sealed record CachedSettings(DateTime WriteTimeUtc, long Length, CdnFolderSettings? Settings, string? Error);
   private readonly ConcurrentDictionary<string, CachedSettings> _settingsCache = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Receives warnings about invalid settings files; set by the host once logging is available.</summary>
   internal ILogger? Logger { get; set; }

   internal static bool IsMetaName(string? name) => CdnFolderSettingsFormat.IsSettingsName(name);

   private static bool IsMetaEntry(FileSystemInfo info) => IsMetaName(info.Name);

   private static ActionException MetaNotFound() => new("Item not found.", 404);

   private static void EnsureNotMetaFolderName(string name) {
      if (IsMetaName(name)) throw new ActionException($"'{name}' is reserved for folder settings and cannot name a folder.", 400);
   }

   /// <summary>
   /// Refuses (404) a resolved path whose segments name the settings file - as typed, or as the file system
   /// really names an existing entry, which also catches Windows short names such as <c>META~1</c>.
   /// </summary>
   private void EnsureNotMetaPath(string[] segments, string fullPath) {
      if (segments.Any(IsMetaName)) throw MetaNotFound();
      if (!OperatingSystem.IsWindows() || !segments.Any(s => s.Contains('~'))) return;
      if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) return;
      var buffer = new StringBuilder(1024);
      var length = GetLongPathNameW(fullPath, buffer, buffer.Capacity);
      if (length > buffer.Capacity) { buffer.Capacity = (int)length; length = GetLongPathNameW(fullPath, buffer, buffer.Capacity); }
      if (length == 0) throw MetaNotFound();
      if (SplitSegments(Path.GetRelativePath(RootPath, buffer.ToString())).Any(IsMetaName)) throw MetaNotFound();
   }

   [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
   private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, int bufferLength);

   private static string MetaPathOf(string folder) => Path.Combine(folder, CdnFolderSettingsFormat.FileName);

   /// <summary>Settings of a physical folder, or <c>null</c> with the reason when the file is missing or invalid.</summary>
   private (CdnFolderSettings? Settings, string? Error, bool Exists) ReadSettings(string folder) {
      var file = new FileInfo(MetaPathOf(folder));
      if (!file.Exists) return (null, null, false);
      var key = Path.TrimEndingDirectorySeparator(folder);
      if (_settingsCache.TryGetValue(key, out var cached) && cached.WriteTimeUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
         return (cached.Settings, cached.Error, true);
      CdnFolderSettings? settings = null;
      string? error = null;
      try {
         if (file.Length > CdnFolderSettingsFormat.MaxBytes)
            throw new ActionException($"Invalid folder settings: the settings file is larger than {CdnFolderSettingsFormat.MaxBytes:N0} bytes.", 400);
         settings = CdnFolderSettingsFormat.Parse(File.ReadAllBytes(file.FullName));
      }
      catch (ActionException ex) { error = ex.Message; }
      catch (IOException ex) { error = ex.Message; }
      if (error is not null)
         Logger?.LogWarning("CDN folder settings in '{Folder}' are ignored: {Error}", ToRelative(folder), error);
      _settingsCache[key] = new CachedSettings(file.LastWriteTimeUtc, file.Length, settings, error);
      return (settings, error, true);
   }

   /// <summary>Settings that apply to a physical folder; an invalid file counts as no settings.</summary>
   internal CdnFolderSettings? EffectiveSettings(string folder) => ReadSettings(folder).Settings;

   private void InvalidateSettings(string path) {
      var prefix = Path.TrimEndingDirectorySeparator(path);
      foreach (var key in _settingsCache.Keys) {
         if (string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase) ||
             key.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            _settingsCache.TryRemove(key, out _);
      }
   }

   /// <summary>Writes canonical settings atomically; callers hold <c>_gate</c> and have checked writability.</summary>
   private void WriteSettings(string folder, CdnFolderSettings settings) {
      var bytes = Encoding.UTF8.GetBytes(CdnFolderSettingsFormat.Serialize(settings));
      var temp = Path.Combine(TempPath, "meta-" + Guid.NewGuid().ToString("N"));
      try {
         File.WriteAllBytes(temp, bytes);
         var target = MetaPathOf(folder);
         if (Directory.Exists(target)) throw new ActionException("A folder occupies the settings file name.", 409);
         File.Move(temp, target, overwrite: true);
      }
      finally {
         if (File.Exists(temp)) File.Delete(temp);
         InvalidateSettings(folder);
      }
   }

   private static async Task<CdnFolderSettings> ReadSettingsContentAsync(Stream content, CancellationToken token) {
      using var buffer = new MemoryStream();
      var chunk = new byte[8192];
      int read;
      while ((read = await content.ReadAsync(chunk, token)) > 0) {
         if (buffer.Length + read > CdnFolderSettingsFormat.MaxBytes)
            throw new ActionException($"Invalid folder settings: the settings file is larger than {CdnFolderSettingsFormat.MaxBytes:N0} bytes.", 400);
         buffer.Write(chunk, 0, read);
      }
      return CdnFolderSettingsFormat.Parse(buffer.ToArray());
   }

   /// <summary>An upload named like the settings file replaces the folder settings; the overwrite flag does not apply.</summary>
   private async Task<CdnEntry> UploadSettingsAsync(string? relativePath, Stream content, CancellationToken token, string? appId) {
      EnsureWritable(relativePath, appId);
      RequireFolder(relativePath);
      var settings = await ReadSettingsContentAsync(content, token);
      lock (_gate) {
         EnsureWritable(relativePath, appId);
         var folder = RequireFolder(relativePath);
         WriteSettings(folder, settings);
         return ToEntry(new DirectoryInfo(folder), ToRelative(folder));
      }
   }

   internal CdnFolderSettingsDetail FolderSettings(string? relativePath) {
      var folder = RequireFolder(relativePath);
      var (settings, error, exists) = ReadSettings(folder);
      var readOnly = false;
      try { EnsureWritable(relativePath); }
      catch (ActionException) { readOnly = true; }
      return new CdnFolderSettingsDetail {
         Path = ToRelative(folder), Settings = settings ?? new CdnFolderSettings(), Exists = exists,
         Warning = error, IsReadOnly = readOnly
      };
   }

   internal void SaveFolderSettings(string? relativePath, CdnFolderSettings settings, string? appId = null) {
      ArgumentNullException.ThrowIfNull(settings);
      CdnFolderSettingsFormat.Validate(settings);
      lock (_gate) {
         EnsureWritable(relativePath, appId);
         WriteSettings(RequireFolder(relativePath), settings);
      }
   }

   internal string ExportFolderSettings(string? relativePath) {
      var (settings, _, _) = ReadSettings(RequireFolder(relativePath));
      return settings is null
         ? throw new ActionException("This folder has no valid settings to export.", 404)
         : CdnFolderSettingsFormat.Serialize(settings);
   }

   /// <summary>Deletes every settings file in a folder tree, so a recycled folder carries none.</summary>
   private void DropSettingsTree(string folder) {
      foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", EverythingRecursive).Where(IsMetaEntry).ToArray())
         file.Delete();
      InvalidateSettings(folder);
   }

   #region Rename

   public CdnEntry Rename(string relativePath, string newName) {
      lock (_gate) {
         EnsureWritable(relativePath);
         ValidateName(newName);
         var source = ResolveInside(relativePath);
         if (ToRelative(source).Length == 0) throw new ActionException("The CDN root folder cannot be renamed.", 400);
         var isFolder = Directory.Exists(source);
         if (!isFolder && !File.Exists(source)) throw new ActionException($"'{relativePath}' was not found.", 404);
         var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source))!;
         var oldName = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));

         if (IsMetaName(newName)) {
            if (isFolder) EnsureNotMetaFolderName(newName);
            var file = new FileInfo(source);
            if (file.Length > CdnFolderSettingsFormat.MaxBytes)
               throw new ActionException($"Invalid folder settings: the settings file is larger than {CdnFolderSettingsFormat.MaxBytes:N0} bytes.", 400);
            var settings = CdnFolderSettingsFormat.Parse(File.ReadAllBytes(source));
            WriteSettings(parent, settings);
            File.Delete(source);
            return ToEntry(new DirectoryInfo(parent), ToRelative(parent));
         }

         if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return ToEntry(isFolder ? new DirectoryInfo(source) : new FileInfo(source), ToRelative(source));

         var destination = Path.Combine(parent, newName);
         EnsureNotReserved(ToRelative(destination));
         var caseOnly = string.Equals(oldName, newName, PathComparison);
         if (!caseOnly && (File.Exists(destination) || Directory.Exists(destination)))
            throw new ActionException($"'{newName}' already exists in this folder.", 409);

         if (caseOnly && isFolder) {
            // A case-only folder rename goes through a temporary name, which every file system accepts.
            var step = Path.Combine(parent, "." + Guid.NewGuid().ToString("N"));
            MovePhysical(source, step);
            MovePhysical(step, destination);
         }
         else MovePhysical(source, destination);
         InvalidateSettings(source);
         return ToEntry(isFolder ? new DirectoryInfo(destination) : new FileInfo(destination), ToRelative(destination));
      }
   }

   #endregion

   #region Copy

   /// <summary>Everything that can refuse a copy before it starts. Returns the source and destination paths.</summary>
   public (string Source, string Destination, bool IsFolder) ValidateCopy(string relativePath, string? targetFolder, bool overwrite) {
      EnsureWritable(targetFolder);
      var source = ResolveInside(relativePath);
      if (ToRelative(source).Length == 0) throw new ActionException("The CDN root folder cannot be copied.", 400);
      var isFolder = Directory.Exists(source);
      if (!isFolder && !File.Exists(source)) throw new ActionException($"'{relativePath}' was not found.", 404);
      var target = RequireFolder(targetFolder);
      var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
      var destination = Path.Combine(target, name);
      EnsureNotReserved(ToRelative(destination));
      EnsureWritable(ToRelative(destination));

      if (isFolder) {
         var sourceWithSeparator = Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar;
         if ((Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar).StartsWith(sourceWithSeparator, PathComparison))
            throw new ActionException($"Folder '{name}' cannot be copied into itself.", 400);
         if (Directory.Exists(destination) || File.Exists(destination))
            throw new ActionException($"'{name}' already exists in the target folder.", 409);
      }
      else {
         if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(source), PathComparison))
            throw new ActionException($"'{name}' already exists in the target folder.", 409);
         if (Directory.Exists(destination))
            throw new ActionException($"A folder named '{name}' already exists in the target folder.", 409);
         if (File.Exists(destination) && !overwrite)
            throw new ActionException($"'{name}' already exists in the target folder.", 409);
      }
      return (source, destination, isFolder);
   }

   /// <summary>
   /// Copies into the private temp directory first, folder settings included, and moves the result into
   /// place only once complete, so a half-written copy is never public. A replaced file goes to the recycle bin.
   /// </summary>
   public async Task CopyAsync(string relativePath, string? targetFolder, bool overwrite, Action<double?, string> report,
      CancellationToken token) {
      var (source, destination, isFolder) = ValidateCopy(relativePath, targetFolder, overwrite);
      report(null, "Collecting files");
      var files = new List<(string Relative, FileInfo File)>();
      var folders = new List<string>();
      if (isFolder) Walk(new DirectoryInfo(source), "");
      else files.Add(("", new FileInfo(source)));
      var totalBytes = files.Sum(f => f.File.Length);

      using var operation = Temporary("copy");
      var temp = operation.Path;
      var buffer = new byte[CopyBufferSize];
      long written = 0;
      if (isFolder) {
         Directory.CreateDirectory(temp);
         foreach (var folder in folders) Directory.CreateDirectory(Path.Combine(temp, folder));
      }

      foreach (var (relative, file) in files) {
         token.ThrowIfCancellationRequested();
         var label = relative.Length == 0 ? file.Name : relative;
         report(Percent(written, totalBytes), label);
         await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
         await using var output = new FileStream(relative.Length == 0 ? temp : Path.Combine(temp, relative), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
         int read;
         while ((read = await input.ReadAsync(buffer, token)) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            written += read;
            report(Percent(written, totalBytes), label);
         }
      }

      token.ThrowIfCancellationRequested();
      lock (_gate) {
         // Checked again: the destination may have been taken while copying.
         ValidateCopy(relativePath, targetFolder, overwrite);
         if (!isFolder) Retire(destination);
         MovePhysical(temp, destination);
         InvalidateSettings(destination);
      }
      report(100, "Completed");

      void Walk(DirectoryInfo current, string prefix) {
         foreach (var sub in current.EnumerateDirectories("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName))) {
            var entry = prefix.Length == 0 ? sub.Name : Path.Combine(prefix, sub.Name);
            folders.Add(entry);
            Walk(sub, entry);
         }
         // Settings files are copied too: the copy keeps the folder settings.
         foreach (var file in current.EnumerateFiles("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName)))
            files.Add((prefix.Length == 0 ? file.Name : Path.Combine(prefix, file.Name), file));
      }
   }

   #endregion
}
