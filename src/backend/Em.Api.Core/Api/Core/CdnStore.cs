using System.IO.Compression;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Everything the CDN does to the file system, in one place: the public middleware reads its root
   /// from here, and every management action goes through it. <see cref="ResolveInside"/> is the only
   /// door from caller input to a physical path - nothing else may build one.
   /// </summary>
   /// <remarks>
   /// Registered as a singleton even when the CDN is off, so <see cref="CdnServices"/> always resolves
   /// and answers 404 on its own instead of failing to activate.
   /// </remarks>
   internal sealed partial class CdnStore
   {
      internal const string PublicRequestPath = "/cdn";

      private const string NotEnabledMessage = "CDN is not enabled on this server.";

      private const int MaxNameLength = 255;

      // Below the large-object-heap threshold, the same size Stream.CopyTo uses.
      private const int CopyBufferSize = 81920;

      // Names Windows refuses as a file name, with or without an extension ("CON", "con.txt").
      private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) {
         "CON", "PRN", "AUX", "NUL",
         "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
      };

      private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

      private static readonly StringComparison PathComparison =
         OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

      // Dot-prefixed, hidden and system entries are public, except the folder settings file, which callers
      // filter out by name; symbolic links are excluded.
      private static readonly EnumerationOptions VisibleEntries = new() {
         AttributesToSkip = FileAttributes.ReparsePoint,
         IgnoreInaccessible = true
      };

      private static readonly EnumerationOptions EverythingRecursive = new() {
         AttributesToSkip = FileAttributes.ReparsePoint,
         IgnoreInaccessible = true,
         RecurseSubdirectories = true
      };

      public static CdnStore Disabled { get; } = new(null, 0);

      private CdnStore(string? rootPath, long maxFileSize) {
         IsEnabled = rootPath is not null;
         RootPath = rootPath ?? string.Empty;
         _maxFileSize = maxFileSize;
      }

      /// <summary>
      /// Builds the store for <paramref name="configuredPath"/> as written in <c>Program.cs</c>: an
      /// absolute path is taken as is, a relative one is resolved against
      /// <paramref name="contentRootPath"/>. The folder is created when missing.
      /// </summary>
      public static CdnStore Create(string configuredPath, string contentRootPath, long maxFileSize) {
         var fullPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath));
         Directory.CreateDirectory(fullPath);
         var store = new CdnStore(Path.Combine(fullPath, "files") + Path.DirectorySeparatorChar, maxFileSize);
         store.InitializeManagement(fullPath);
         store.LoadRuntimeSettings();
         return store;
      }

      public bool IsEnabled { get; }

      /// <summary>Absolute, always ending with a directory separator.</summary>
      public string RootPath { get; }

      private long _maxFileSize;
      /// <summary>Upload limit in bytes.</summary>
      public long MaxFileSize => Interlocked.Read(ref _maxFileSize);

      public void EnsureEnabled() {
         if (!IsEnabled) {
            throw new ActionException(NotEnabledMessage, 404);
         }
      }

      #region Paths

      /// <summary>
      /// Turns a caller-supplied relative path into a physical path that is guaranteed to sit inside
      /// <see cref="RootPath"/>. <c>null</c> or empty means the root itself.
      /// </summary>
      /// <exception cref="ActionException">
      /// 400 for <c>..</c>, empty traversal segments, invalid characters, or anything that would
      /// land outside the root.
      /// </exception>
      public string ResolveInside(string? relativePath, bool allowSkin = false) {
         if (!allowSkin) EnsureNotReserved(relativePath);
         var segments = SplitSegments(relativePath);
         foreach (var segment in segments) {
            ValidateName(segment);
         }

         var fullPath = Path.GetFullPath(Path.Combine([RootPath, .. segments]));
         var withSeparator = Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar;

         // Belt and braces: the segment check above already rules out every way out, but the one
         // guarantee this method gives is checked on the result, not inferred from the input.
         if (!withSeparator.StartsWith(RootPath, PathComparison)) {
            throw new ActionException("The path points outside the CDN folder.", 400);
         }

         EnsureNotMetaPath(segments, fullPath);

         for (var current = fullPath; current.Length >= RootPath.TrimEnd(Path.DirectorySeparatorChar).Length;
              current = Path.GetDirectoryName(current)!) {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
               throw new ActionException("Symbolic links are not allowed in CDN paths.", 400);
            if (current.Length == RootPath.TrimEnd(Path.DirectorySeparatorChar).Length) break;
         }
         return fullPath;
      }

      /// <summary>
      /// Validates a single file or folder name. The same rules apply to every segment of a path, so a
      /// name that could be uploaded can always be addressed again later.
      /// </summary>
      public static void ValidateName(string? name) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ActionException("A file or folder name must not be empty.", 400);
         }

         if (name.Length > MaxNameLength) {
            throw new ActionException($"A file or folder name must not be longer than {MaxNameLength} characters.", 400);
         }

         if (name is "." or "..") {
            throw new ActionException($"'{name}' is not allowed: relative traversal segments are forbidden.", 400);
         }

         if (name.IndexOfAny(InvalidNameChars) >= 0 || name.Contains('/') || name.Contains('\\')) {
            throw new ActionException($"'{name}' contains characters that are not allowed in a file name.", 400);
         }

         // Windows silently drops a trailing dot or space, so "a." and "a" would be the same entry.
         if (name.EndsWith('.') || name.EndsWith(' ')) {
            throw new ActionException($"'{name}' must not end with a dot or a space.", 400);
         }

         var baseName = name.Split('.')[0].TrimEnd();
         if (ReservedNames.Contains(baseName)) {
            throw new ActionException($"'{name}' is a reserved name and cannot be used.", 400);
         }
      }

      private static string[] SplitSegments(string? relativePath) {
         if (string.IsNullOrWhiteSpace(relativePath)) {
            return [];
         }

         var normalized = relativePath.Replace('\\', '/').Trim('/');
         return normalized.Length == 0 ? [] : normalized.Split('/');
      }

      private string ToRelative(string fullPath) {
         var relative = Path.GetRelativePath(RootPath, fullPath);
         return relative == "." ? string.Empty : relative.Replace(Path.DirectorySeparatorChar, '/');
      }

      private string ToPublicPath(string folderRelative) =>
         folderRelative.Length == 0
            ? PublicRequestPath.TrimStart('/') + "/"
            : $"{PublicRequestPath.TrimStart('/')}/{string.Join('/', folderRelative.Split('/').Select(Uri.EscapeDataString))}/";

      private CdnEntry ToEntry(FileSystemInfo info, string relative, CdnFolderSettings? parentSettings = null) => new() {
         Name = info.Name,
         IsHidden = CdnFolderSettingsFormat.IsHidden(parentSettings, info.Name),
         ManagedFolder = ManagedAt(relative, includeUsage: false),
         Path = relative,
         IsFolder = info is DirectoryInfo,
         Size = info is FileInfo file ? file.Length : 0,
         LastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
      };

      private string RequireFolder(string? relativePath) {
         var folder = ResolveInside(relativePath);
         if (!Directory.Exists(folder)) {
            throw new ActionException($"Folder '{relativePath}' was not found.", 404);
         }

         return folder;
      }

      #endregion

      #region Operations

      public CdnStorageInfo StorageSize(CancellationToken ct) {
         EnsureEnabled();
         var result = new CdnStorageInfo();
         lock (_gate) foreach (var name in new[] { "files", "temp", "recycle" }) {
            ct.ThrowIfCancellationRequested();
            var part = SizeOf(Path.Combine(StoragePath, name));
            result.TotalBytes = checked(result.TotalBytes + part.TotalBytes);
            result.FileCount = checked(result.FileCount + part.FileCount);
         }
         return result;
      }

      public CdnFolderContent List(string? relativePath) {
         var folder = new DirectoryInfo(RequireFolder(relativePath));
         var folderRelative = ToRelative(folder.FullName);
         var settings = EffectiveSettings(folder.FullName);

         var folders = folder.EnumerateDirectories("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName) && !IsMetaEntry(r))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => ToEntry(r, ToRelative(r.FullName), settings));
         var files = folder.EnumerateFiles("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName) && !IsMetaEntry(r))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => ToEntry(r, ToRelative(r.FullName), settings));

         return new CdnFolderContent {
            Path = folderRelative,
            ManagedFolder = ManagedAt(folderRelative),
            Entries = folders.Concat(files).ToArray(),
            MaxFileSize = ManagedAt(folderRelative) is null ? MaxFileSize : long.MaxValue,
            PublicPath = ToPublicPath(folderRelative)
         };
      }

      public CdnItemCount CountInside(string relativePath) {
         var folder = new DirectoryInfo(RequireFolder(relativePath));
         var result = new CdnItemCount();
         foreach (var info in folder.EnumerateFileSystemInfos("*", EverythingRecursive).Where(r => !IsSkinPhysical(r.FullName) && !IsMetaEntry(r))) {
            if (info is DirectoryInfo) {
               result.Folders++;
            }
            else {
               result.Files++;
            }
         }

         return result;
      }

      /// <summary>
      /// Every visible entry below <paramref name="relativePath"/>, depth first, each folder listed
      /// before its content. Dot-prefixed, hidden and system entries are ordinary public content.
      /// </summary>
      public CdnEntry[] Tree(string relativePath) {
         var folder = new DirectoryInfo(RequireFolder(relativePath));
         var result = new List<CdnEntry>();
         Walk(folder);
         return result.ToArray();

         void Walk(DirectoryInfo current) {
            var settings = EffectiveSettings(current.FullName);
            foreach (var sub in current.EnumerateDirectories("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName) && !IsMetaEntry(r))
                                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
               result.Add(ToEntry(sub, ToRelative(sub.FullName), settings));
               Walk(sub);
            }

            foreach (var file in current.EnumerateFiles("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName) && !IsMetaEntry(r))
                                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
               result.Add(ToEntry(file, ToRelative(file.FullName), settings));
            }
         }
      }

      public CdnEntry Move(string relativePath, string? targetFolder, bool overwrite) {
         lock (_gate) {
         EnsureWritable(relativePath);
         EnsureWritable(targetFolder);
         var source = ResolveInside(relativePath);
         if (ToRelative(source).Length == 0) {
            throw new ActionException("The CDN root folder cannot be moved.", 400);
         }

         var isFolder = Directory.Exists(source);
         if (!isFolder && !File.Exists(source)) {
            throw new ActionException($"'{relativePath}' was not found.", 404);
         }

         var target = RequireFolder(targetFolder);
         var name = Path.GetFileName(source);
         var destination = Path.Combine(target, name);
         EnsureNotReserved(ToRelative(destination));

         if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(source), PathComparison)) {
            return ToEntry(isFolder ? new DirectoryInfo(source) : new FileInfo(source), ToRelative(source));
         }

         if (isFolder) {
            var sourceWithSeparator = Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar;
            if ((Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar)
                .StartsWith(sourceWithSeparator, PathComparison)) {
               throw new ActionException($"Folder '{name}' cannot be moved into itself.", 400);
            }

            if (Directory.Exists(destination) || File.Exists(destination)) {
               throw new ActionException($"'{name}' already exists in the target folder.", 409);
            }

            MovePhysical(source, destination);
            InvalidateSettings(source);
            return ToEntry(new DirectoryInfo(destination), ToRelative(destination));
         }

         if (Directory.Exists(destination)) {
            throw new ActionException($"A folder named '{name}' already exists in the target folder.", 409);
         }

         if (File.Exists(destination) && !overwrite) {
            throw new ActionException($"'{name}' already exists in the target folder.", 409);
         }

         Retire(destination);
         MovePhysical(source, destination);
         return ToEntry(new FileInfo(destination), ToRelative(destination));
         }
      }

      /// <summary>
      /// Everything that can refuse an upload without its content - name, folder, conflict - is checked
      /// before the first read, so with <c>Expect: 100-continue</c> a refused file is never sent. The size
      /// limit is counted while copying, since a stream does not say how long it is up front.
      /// </summary>
      public async Task<CdnEntry> UploadAsync(string? relativePath, string fileName, Stream content, bool overwrite,
         CancellationToken token, string? appId = null, string deletedBy = "") {
         var maxFileSize = MaxFileSize;
         EnsureWritable(relativePath, appId);
         ValidateName(fileName);
         if (IsMetaName(fileName)) {
            return await UploadSettingsAsync(relativePath, content, token, appId);
         }

         var folder = RequireFolder(relativePath);
         var target = Path.Combine(folder, fileName);
         EnsureNotReserved(ToRelative(target));

         if (Directory.Exists(target)) {
            throw new ActionException($"A folder named '{fileName}' already exists here.", 409);
         }

         if (File.Exists(target) && !overwrite) {
            throw new ActionException($"'{fileName}' already exists in this folder.", 409);
         }

         // Written in the private same-volume temp directory, then moved into place.
         // An existing downloader keeps its previous read handle.
         using var operation = Temporary("upload");
         var temp = operation.Path;
         try {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
               var buffer = new byte[CopyBufferSize];
               long written = 0;
               int read;
               while ((read = await content.ReadAsync(buffer, token)) > 0) {
                  written += read;
                  if (appId is null && written > maxFileSize) {
                     throw new ActionException(
                        $"'{fileName}' is larger than {maxFileSize:N0} bytes, the most the CDN accepts per file.", 413);
                  }

                  await file.WriteAsync(buffer.AsMemory(0, read), token);
               }
            }

            lock (_gate) {
               EnsureWritable(relativePath, appId);
               if (Directory.Exists(target) || (!overwrite && File.Exists(target)))
                  throw new ActionException($"'{fileName}' already exists.", 409);
               if (ManagedAt(relativePath) is { } managed)
                  CheckQuota(managed, checked(managed.UsedBytes - (File.Exists(target) ? new FileInfo(target).Length : 0) + new FileInfo(temp).Length));
               Retire(target, appId, deletedBy);
               File.Move(temp, target);
            }
         }
         finally {
            if (File.Exists(temp)) {
               File.Delete(temp);
            }
         }

         return ToEntry(new FileInfo(target), ToRelative(target));
      }

      public CdnEntry CreateFolder(string? relativePath, string folderName, string? appId = null) {
         lock (_gate) {
         EnsureWritable(relativePath, appId);
         ValidateName(folderName);
         EnsureNotMetaFolderName(folderName);
         var parent = RequireFolder(relativePath);
         var target = Path.Combine(parent, folderName);
         EnsureNotReserved(ToRelative(target));

         if (Directory.Exists(target) || File.Exists(target)) {
            throw new ActionException($"'{folderName}' already exists in this folder.", 409);
         }

         var created = Directory.CreateDirectory(target);
         return ToEntry(created, ToRelative(created.FullName));
         }
      }

      public void Delete(string relativePath, string? appId = null, string deletedBy = "") {
         lock (_gate) {
            EnsureWritable(relativePath, appId);
            var target = ResolveInside(relativePath);
            if (ToRelative(target).Length == 0 || (appId is not null && ManagedAt(relativePath)?.Path == NormalizePath(relativePath)))
               throw new ActionException("The storage root cannot be deleted.", 400);
            if (!File.Exists(target) && !Directory.Exists(target)) throw new ActionException("Item not found.", 404);
            Retire(target, appId, deletedBy);
         }
      }

      #endregion

      #region Archive

      /// <summary>Most entries an archive may hold to be extracted.</summary>
      internal const int MaxExtractEntries = 10_000;

      /// <summary>Total extracted size, as a multiple of <see cref="MaxFileSize"/>.</summary>
      internal const int MaxExtractTotalFactor = 10;

      private const string ZipExtension = ".zip";

      /// <summary>
      /// The relative form of a caller-supplied path, as every other answer spells it. Used to build task
      /// keys, so two spellings of one path cannot start two tasks.
      /// </summary>
      public string NormalizePath(string? relativePath) => ToRelative(ResolveInside(relativePath));

      /// <summary>
      /// Everything that can refuse an archive before it starts: names, sources, the archive name, and a
      /// conflict with an existing entry. Nothing is written.
      /// </summary>
      public void ValidateArchive(string? folderPath, string[] names, string archiveName, bool overwrite) {
         var folder = RequireFolder(folderPath);
         EnsureWritable(folderPath);
         ValidateName(archiveName);

         if (!archiveName.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase)) {
            throw new ActionException($"'{archiveName}' must end with '{ZipExtension}'.", 400);
         }

         if (names is not { Length: > 0 }) {
            throw new ActionException("Select at least one file or folder to archive.", 400);
         }

         var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         foreach (var name in names) {
            ValidateName(name);
            if (IsMetaName(name)) {
               throw MetaNotFound();
            }
            EnsureNotReserved(ToRelative(Path.Combine(folder, name)));
            if (!seen.Add(name)) {
               throw new ActionException($"'{name}' is listed more than once.", 400);
            }

            if (string.Equals(name, archiveName, StringComparison.OrdinalIgnoreCase)) {
               throw new ActionException($"'{archiveName}' cannot be put inside itself.", 400);
            }

            var source = Path.Combine(folder, name);
            if (!File.Exists(source) && !Directory.Exists(source)) {
               throw new ActionException($"'{name}' was not found.", 404);
            }
         }

         EnsureArchiveTarget(Path.Combine(folder, archiveName), archiveName, overwrite);
      }

      private static void EnsureArchiveTarget(string target, string archiveName, bool overwrite) {
         if (Directory.Exists(target)) {
            throw new ActionException($"A folder named '{archiveName}' already exists here.", 409);
         }

         if (File.Exists(target) && !overwrite) {
            throw new ActionException($"'{archiveName}' already exists in this folder.", 409);
         }
      }

      /// <summary>
      /// Builds a zip in the private temp directory, including dot-prefixed, hidden and system entries,
      /// and publishes it only after completion. Active temporary files are excluded from garbage cleanup.
      /// </summary>
      public async Task ArchiveAsync(string? folderPath, string[] names, string archiveName, bool overwrite,
         Action<double?, string> report, CancellationToken token) {
         ValidateArchive(folderPath, names, archiveName, overwrite);
         var folder = RequireFolder(folderPath);
         var target = Path.Combine(folder, archiveName);

         report(null, "Collecting files");
         var items = CollectArchiveItems(folder, names);
         var totalBytes = items.Sum(r => r.File?.Length ?? 0);

         using var operation = Temporary("archive");
         var temp = operation.Path;
         try {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            CopyBufferSize, FileOptions.Asynchronous)) {
               using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
               var buffer = new byte[CopyBufferSize];
               long written = 0;

               foreach (var item in items) {
                  token.ThrowIfCancellationRequested();

                  if (item.File is null) {
                     zip.CreateEntry(item.EntryName + "/");
                     continue;
                  }

                  report(Percent(written, totalBytes), item.EntryName);
                  var entry = zip.CreateEntry(item.EntryName, CompressionLevel.Optimal);
                  entry.LastWriteTime = item.File.LastWriteTime;

                  await using var source = new FileStream(item.File.FullName, FileMode.Open, FileAccess.Read,
                     FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                  await using var destination = entry.Open();
                  int read;
                  while ((read = await source.ReadAsync(buffer, token)) > 0) {
                     await destination.WriteAsync(buffer.AsMemory(0, read), token);
                     written += read;
                     report(Percent(written, totalBytes), item.EntryName);
                  }
               }
            }

            token.ThrowIfCancellationRequested();

            // Checked again: someone may have taken the name while the archive was being written.
            EnsureArchiveTarget(target, archiveName, overwrite);
            lock (_gate) {
               EnsureWritable(folderPath);
               Retire(target);
               File.Move(temp, target);
            }
            report(100, "Completed");
         }
         finally {
            if (File.Exists(temp)) {
               File.Delete(temp);
            }
         }
      }

      private static double? Percent(long done, long total) =>
         total <= 0 ? null : Math.Min(100d, done * 100d / total);

      private sealed record ArchiveItem(string EntryName, FileInfo? File);

      /// <summary>Files and folders to write, each folder before its content; folders carry no file.</summary>
      private List<ArchiveItem> CollectArchiveItems(string folder, string[] names) {
         var result = new List<ArchiveItem>();
         foreach (var name in names) {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) {
               result.Add(new ArchiveItem(name, new FileInfo(path)));
               continue;
            }

            result.Add(new ArchiveItem(name, null));
            Walk(new DirectoryInfo(path), name);
         }

         return result;

         void Walk(DirectoryInfo current, string prefix) {
            foreach (var sub in current.EnumerateDirectories("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName))
                                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
               if (IsMetaEntry(sub)) continue;
               var entryName = $"{prefix}/{sub.Name}";
               result.Add(new ArchiveItem(entryName, null));
               Walk(sub, entryName);
            }

            foreach (var file in current.EnumerateFiles("*", VisibleEntries).Where(r => !IsSkinPhysical(r.FullName))
                                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
               if (IsMetaEntry(file)) continue;
               result.Add(new ArchiveItem($"{prefix}/{file.Name}", file));
            }
         }
      }

      #endregion

      #region Extract

      /// <summary>
      /// What extracting a zip into its own folder would do, checked strictly before anything is written.
      /// </summary>
      /// <param name="ZipPath">Physical path of the zip.</param>
      /// <param name="Folder">Physical path of the folder the zip sits in, and extracts into.</param>
      /// <param name="Files">Relative paths (inside <paramref name="Folder"/>, <c>/</c>-separated) of every file entry.</param>
      /// <param name="Folders">Relative paths of every folder the extract creates, parents first.</param>
      /// <param name="Conflicts">CDN paths of existing files that would be overwritten.</param>
      /// <param name="Settings">Relative paths of folder settings entries; applied as settings, never as files or conflicts.</param>
      public sealed record ExtractPlan(string ZipPath, string Folder, string[] Files, string[] Folders, string[] Conflicts,
         string[] Settings);

      /// <summary>
      /// Reads the entry list of a zip and refuses it as a whole - 400 - for an entry that would leave the
      /// folder, a traversal segment or otherwise invalid name in any segment, too many entries, an entry that is
      /// both a file and a folder, or a file that would land on an existing folder (or the reverse).
      /// Existing files that would be overwritten are reported, not refused.
      /// </summary>
      public ExtractPlan ReadExtractPlan(string relativePath) {
         var zipPath = ResolveInside(relativePath);
         if (!File.Exists(zipPath)) {
            throw new ActionException($"'{relativePath}' was not found.", 404);
         }

         if (!zipPath.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase)) {
            throw new ActionException($"'{Path.GetFileName(zipPath)}' is not a '{ZipExtension}' file.", 400);
         }

         var folder = Path.GetDirectoryName(zipPath)!;
         var files = new List<string>();
         var folders = new List<string>();
         var settings = new List<string>();
         var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

         using (var zip = OpenZip(zipPath)) {
            if (zip.Entries.Count > MaxExtractEntries) {
               throw new ActionException(
                  $"The archive holds {zip.Entries.Count:N0} entries; at most {MaxExtractEntries:N0} can be extracted.", 400);
            }

            foreach (var entry in zip.Entries) {
               var name = entry.FullName.Replace('\\', '/');
               var isFolder = name.EndsWith('/');
               var segments = SplitEntryName(entry.FullName, name, isFolder);
               if ((isFolder ? segments : segments[..^1]).Any(IsMetaName)) {
                  throw new ActionException($"The archive entry '{entry.FullName}' uses a folder name reserved for folder settings.", 400);
               }

               for (var depth = 1; depth < segments.Length; depth++) {
                  AddFolder(string.Join('/', segments[..depth]));
               }

               var relative = string.Join('/', segments);
               if (isFolder) {
                  AddFolder(relative);
                  continue;
               }

               if (folderSet.Contains(relative) || !fileSet.Add(relative)) {
                  throw new ActionException($"The archive holds '{relative}' more than once.", 400);
               }

               if (IsMetaName(segments[^1])) {
                  settings.Add(relative);
                  continue;
               }

               files.Add(relative);
            }
         }

         var zipRelative = ToRelative(zipPath);
         var folderRelative = ToRelative(folder);
         var conflicts = new List<string>();

         foreach (var relative in folders) {
            EnsureWritable(ToRelative(Path.Combine(folder, relative)));
            if (File.Exists(Path.Combine(folder, relative))) {
               throw new ActionException($"'{relative}' is a folder in the archive but a file in this folder.", 400);
            }
         }

         foreach (var relative in files) {
            EnsureWritable(ToRelative(Path.Combine(folder, relative)));
            var target = Path.Combine(folder, relative);
            if (Directory.Exists(target)) {
               throw new ActionException($"'{relative}' is a file in the archive but a folder in this folder.", 400);
            }

            if (File.Exists(target)) {
               var cdnPath = folderRelative.Length == 0 ? relative : $"{folderRelative}/{relative}";
               if (string.Equals(cdnPath, zipRelative, StringComparison.OrdinalIgnoreCase)) {
                  throw new ActionException("The archive contains a file with its own name and cannot overwrite itself.", 400);
               }

               conflicts.Add(cdnPath);
            }
         }

         foreach (var relative in settings) {
            EnsureWritable(ToRelative(Path.GetDirectoryName(Path.Combine(folder, relative))!));
         }

         return new ExtractPlan(zipPath, folder, files.ToArray(), folders.ToArray(), conflicts.ToArray(), settings.ToArray());

         void AddFolder(string relative) {
            if (fileSet.Contains(relative)) {
               throw new ActionException($"The archive holds '{relative}' both as a file and as a folder.", 400);
            }

            if (folderSet.Add(relative)) {
               folders.Add(relative);
            }
         }
      }

      private static ZipArchive OpenZip(string zipPath) {
         try {
            return ZipFile.OpenRead(zipPath);
         }
         catch (InvalidDataException ex) {
            throw new ActionException($"'{Path.GetFileName(zipPath)}' is not a readable zip file: {ex.Message}", 400);
         }
      }

      /// <summary>
      /// Splits one entry name into segments, each held to the same rules as any other CDN name - which
      /// already rules out <c>..</c>, drive letters and empty segments. A leading separator
      /// (an absolute path) is refused on its own, since trimming it would hide it.
      /// </summary>
      private static string[] SplitEntryName(string original, string name, bool isFolder) {
         if (name.StartsWith('/')) {
            throw new ActionException($"The archive entry '{original}' is an absolute path, which is not allowed.", 400);
         }

         var segments = (isFolder ? name[..^1] : name).Split('/');
         foreach (var segment in segments) {
            try {
               ValidateName(segment);
            }
            catch (ActionException ex) {
               throw new ActionException($"The archive entry '{original}' is not allowed: {ex.Message}", 400);
            }
         }

         return segments;
      }

      /// <summary>
      /// Extracts a zip into the folder it sits in ("Extract Here"). Everything is written into a
      /// private temporary folder first, with every entry held to <see cref="MaxFileSize"/> and the
      /// whole to <see cref="MaxExtractTotalFactor"/> times that, counted on the bytes actually written -
      /// never on what the zip headers claim. Only once all of it is in does a short move phase put each
      /// entry in place; that phase ignores the token, so a cancellation either comes before it and leaves
      /// nothing, or comes too late and changes nothing.
      /// </summary>
      public async Task ExtractAsync(string relativePath, bool overwrite, Action<double?, string> report,
         CancellationToken token) {
         var maxFileSize = MaxFileSize;
         EnsureWritable(relativePath);
         var plan = ReadExtractPlan(relativePath);
         if (plan.Conflicts.Length > 0 && !overwrite) {
            throw new ActionException($"{plan.Conflicts.Length:N0} existing file(s) would be overwritten.", 409);
         }

         var maxTotal = checked(maxFileSize * MaxExtractTotalFactor);
         using var operation = Temporary("extract");
         var temp = operation.Path;
         Directory.CreateDirectory(temp);
         var settingsToApply = new List<(string Folder, CdnFolderSettings Settings)>();
         var skipped = new List<string>();

         try {
            using (var zip = OpenZip(plan.ZipPath)) {
               var claimedTotal = zip.Entries.Sum(r => r.Length);
               var buffer = new byte[CopyBufferSize];
               long total = 0;

               foreach (var entry in zip.Entries) {
                  token.ThrowIfCancellationRequested();

                  var name = entry.FullName.Replace('\\', '/');
                  if (name.EndsWith('/')) continue;

                  var entrySegments = SplitEntryName(entry.FullName, name, isFolder: false);
                  var relative = string.Join('/', entrySegments);
                  if (IsMetaName(entrySegments[^1])) {
                     // A settings entry never becomes a file: valid ones replace the settings of their folder,
                     // invalid ones are skipped while the rest of the archive is extracted.
                     try {
                        await using var settingsSource = entry.Open();
                        settingsToApply.Add((string.Join('/', entrySegments[..^1]), await ReadSettingsContentAsync(settingsSource, token)));
                     }
                     catch (ActionException ex) {
                        skipped.Add($"{relative} ({ex.Message})");
                     }
                     continue;
                  }

                  var destination = Path.Combine(temp, relative);
                  Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                  report(Percent(total, claimedTotal), relative);

                  await using var source = entry.Open();
                  await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                     FileShare.None, CopyBufferSize, FileOptions.Asynchronous);
                  long written = 0;
                  int read;
                  while ((read = await source.ReadAsync(buffer, token)) > 0) {
                     written += read;
                     total += read;
                     if (written > maxFileSize) {
                        throw new ActionException(
                           $"'{relative}' is larger than {maxFileSize:N0} bytes, the most the CDN accepts per file.", 413);
                     }

                     if (total > maxTotal) {
                        throw new ActionException(
                           $"The extracted content is larger than {maxTotal:N0} bytes, the most one archive may extract to.", 413);
                     }

                     await output.WriteAsync(buffer.AsMemory(0, read), token);
                     report(Percent(total, claimedTotal), relative);
                  }
               }
            }

            token.ThrowIfCancellationRequested();

            // Checked again right before the move: a file may have appeared since the plan was read.
            if (!overwrite && plan.Files.Any(r => File.Exists(Path.Combine(plan.Folder, r)))) {
               throw new ActionException("Files that would be overwritten have appeared while extracting.", 409);
            }

            lock (_gate) {
            EnsureWritable(relativePath);
            foreach (var relative in plan.Files.Concat(plan.Folders))
               EnsureWritable(ToRelative(Path.Combine(plan.Folder, relative)));
            report(100, "Moving files into place");
            foreach (var relative in plan.Folders) {
               Directory.CreateDirectory(Path.Combine(plan.Folder, relative));
            }

            foreach (var relative in plan.Files) {
               var target = Path.Combine(plan.Folder, relative);
               Directory.CreateDirectory(Path.GetDirectoryName(target)!);
               Retire(target);
               File.Move(Path.Combine(temp, relative), target);
            }

            foreach (var (settingsFolder, settings) in settingsToApply) {
               var target = settingsFolder.Length == 0 ? plan.Folder : Path.Combine(plan.Folder, settingsFolder);
               EnsureWritable(ToRelative(target));
               Directory.CreateDirectory(target);
               WriteSettings(target, settings);
            }

            report(100, skipped.Count == 0
               ? "Completed"
               : $"Completed; skipped folder settings: {string.Join("; ", skipped)}");
            }
         }
         finally {
            if (Directory.Exists(temp)) {
               Directory.Delete(temp, recursive: true);
            }
         }
      }

      #endregion
   }
}
