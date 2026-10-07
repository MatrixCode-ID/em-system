using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>The display group of a release file in the Release Manager.</summary>
   public enum ReleaseGroup
   {
      /// <summary>The project's own assemblies together with their companion files (<c>.pdb</c>, <c>.exe</c>, <c>.deps.json</c>, ...).</summary>
      MainModules = 0,

      /// <summary>NuGet packages and other files that are not recognized.</summary>
      ExtraLibraries = 1,

      /// <summary>The .NET runtime carried along by a self-contained application.</summary>
      DotNetRuntime = 2
   }

   /// <summary>
   /// Decides the group of every published file from <c>&lt;host&gt;.deps.json</c>: assets of libraries of
   /// type <c>project</c> go into <see cref="ReleaseGroup.MainModules"/>, those of a runtime pack go into
   /// <see cref="ReleaseGroup.DotNetRuntime"/>, and the rest into <see cref="ReleaseGroup.ExtraLibraries"/>.
   /// Without a <c>deps.json</c>, everything goes into <see cref="ReleaseGroup.ExtraLibraries"/>.
   /// </summary>
   public sealed class ReleaseGrouping
   {
      private static readonly string[] CompanionSuffixes =
         [".dll", ".exe", ".pdb", ".xml", ".runtimeconfig.json", ".deps.json", ".dll.config"];

      private const string ResourcesSuffix = ".resources.dll";

      private readonly Dictionary<string, ReleaseGroup> _claimed = new(StringComparer.OrdinalIgnoreCase);
      private readonly HashSet<string> _projectNames = new(StringComparer.OrdinalIgnoreCase);

      private ReleaseGrouping() { }

      /// <summary>An empty grouping: all files go into <see cref="ReleaseGroup.ExtraLibraries"/>.</summary>
      public static ReleaseGrouping Empty { get; } = new();

      /// <summary>The name of the <c>deps.json</c> file that is used, or <c>null</c> when there is none.</summary>
      public string? DepsFile { get; private init; }

      /// <summary>
      /// Reads the application's <c>deps.json</c> in <paramref name="publishFolder"/>: the one that has a
      /// <c>.runtimeconfig.json</c> of the same name, or the only <c>deps.json</c> there.
      /// <see cref="Empty"/> when there is none or it cannot be read.
      /// </summary>
      public static ReleaseGrouping Load(string publishFolder) {
         var candidates = Directory.Exists(publishFolder)
            ? Directory.GetFiles(publishFolder, "*.deps.json", SearchOption.TopDirectoryOnly)
            : [];
         var deps = candidates.FirstOrDefault(r =>
                       File.Exists(r[..^".deps.json".Length] + ".runtimeconfig.json"))
                    ?? (candidates.Length == 1 ? candidates[0] : null);
         if (deps is null) return Empty;

         try {
            return Parse(File.ReadAllBytes(deps), Path.GetFileName(deps));
         }
         catch (Exception x) when (x is JsonException or IOException or InvalidOperationException or KeyNotFoundException) {
            return Empty;
         }
      }

      /// <summary>The group of file <paramref name="path"/> (relative to the publish folder, separated by <c>/</c>).</summary>
      public ReleaseGroup GroupOf(string path) {
         if (_claimed.TryGetValue(path, out var group)) return group;

         var slash = path.LastIndexOf('/');
         var name = path[(slash + 1)..];

         // Files the SDK writes next to a project's own assembly without listing them: its .pdb, the
         // host .exe, .runtimeconfig.json and .deps.json.
         if (slash < 0) {
            foreach (var suffix in CompanionSuffixes) {
               if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   _projectNames.Contains(name[..^suffix.Length]))
                  return ReleaseGroup.MainModules;
            }
         }

         // A satellite assembly the deps.json leaves out (the WindowsDesktop runtime pack does) follows
         // the assembly it translates.
         if (slash > 0 && path.IndexOf('/') == slash && name.EndsWith(ResourcesSuffix, StringComparison.OrdinalIgnoreCase) &&
             _claimed.TryGetValue(name[..^ResourcesSuffix.Length] + ".dll", out var owner))
            return owner;

         return ReleaseGroup.ExtraLibraries;
      }

      private static ReleaseGrouping Parse(byte[] bytes, string fileName) {
         using var document = JsonDocument.Parse(bytes);
         var root = document.RootElement;
         var targetName = root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
         var target = root.GetProperty("targets").GetProperty(targetName);
         var libraries = root.GetProperty("libraries");

         var result = new ReleaseGrouping { DepsFile = fileName };
         foreach (var library in target.EnumerateObject()) {
            var type = libraries.TryGetProperty(library.Name, out var info) && info.TryGetProperty("type", out var t)
               ? t.GetString() ?? ""
               : "";
            var group = type switch {
               "project" => ReleaseGroup.MainModules,
               "runtimepack" => ReleaseGroup.DotNetRuntime,
               _ when library.Name.StartsWith("runtimepack.", StringComparison.OrdinalIgnoreCase) => ReleaseGroup.DotNetRuntime,
               _ => ReleaseGroup.ExtraLibraries
            };
            if (group == ReleaseGroup.MainModules) result._projectNames.Add(library.Name.Split('/')[0]);

            foreach (var kind in (string[])["runtime", "native", "resources"]) {
               if (!library.Value.TryGetProperty(kind, out var assets)) continue;

               foreach (var asset in assets.EnumerateObject()) {
                  var published = PublishedPath(kind, asset);
                  result._claimed.TryAdd(published, group);
                  if (group == ReleaseGroup.MainModules && kind == "runtime")
                     result._projectNames.Add(Path.GetFileNameWithoutExtension(published));
               }
            }
         }

         return result;
      }

      // Where the SDK puts an asset of a RID-specific publish: a runtime or native asset lands in the root
      // under its file name, a resource in a folder named after its culture. A "localPath", written by
      // newer SDKs, says it outright.
      private static string PublishedPath(string kind, JsonProperty asset) {
         if (asset.Value.ValueKind == JsonValueKind.Object) {
            if (asset.Value.TryGetProperty("localPath", out var local) && local.GetString() is { Length: > 0 } localPath)
               return localPath.Replace('\\', '/');

            if (kind == "resources" && asset.Value.TryGetProperty("locale", out var locale))
               return $"{locale.GetString()}/{Path.GetFileName(asset.Name)}";
         }

         return Path.GetFileName(asset.Name);
      }
   }

   /// <summary>
   /// A snapshot of the local publish folder at one moment: every file with its size and SHA-256, ready to
   /// be compared with the target and made into <c>release.json</c>.
   /// </summary>
   public sealed class LocalSnapshot
   {
      private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _stamps;

      internal LocalSnapshot(string folder, IReadOnlyList<ReleaseFile> files,
         Dictionary<string, (long, DateTime)> stamps, ReleaseGrouping grouping) {
         Folder = folder;
         Files = files;
         _stamps = stamps;
         Grouping = grouping;
         TotalSize = files.Sum(r => r.Size);
      }

      /// <summary>Path lengkap local publish folder.</summary>
      public string Folder { get; }

      /// <summary>Every file, its path relative to <see cref="Folder"/> and separated by <c>/</c>.</summary>
      public IReadOnlyList<ReleaseFile> Files { get; }

      /// <summary>The grouping of files according to the <c>deps.json</c> in this folder.</summary>
      public ReleaseGrouping Grouping { get; }

      /// <summary>The total size of all files in bytes.</summary>
      public long TotalSize { get; }

      /// <summary>The full path on disk of file <paramref name="path"/>.</summary>
      public string FullPathOf(string path) => Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));

      /// <summary>
      /// The first path that has changed since this snapshot was made - its size or write time differs, it is
      /// missing, or a new file appeared - or <c>null</c> when the folder is still the same. Used right before
      /// Sync, so what is published stays the same as what was compared.
      /// </summary>
      public string? FindChange() {
         var current = LocalPublish.EnumerateFiles(Folder);
         foreach (var (path, info) in current) {
            if (!_stamps.TryGetValue(path, out var stamp)) return path;
            if (stamp.Length != info.Length || stamp.LastWriteUtc != info.LastWriteTimeUtc) return path;
         }

         return current.Count == _stamps.Count ? null : _stamps.Keys.First(r => !current.ContainsKey(r));
      }
   }

   /// <summary>
   /// Reads the local publish folder: checks whether it has content, then computes the size and SHA-256 of
   /// every file in the background with cancellable progress.
   /// </summary>
   public static class LocalPublish
   {
      /// <summary><c>true</c> when <paramref name="folder"/> exists and contains at least one file.</summary>
      public static bool HasContent(string? folder) {
         try {
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) &&
                   Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
         }
         catch (Exception x) when (x is IOException or UnauthorizedAccessException) {
            return false;
         }
      }

      /// <summary>
      /// Makes a snapshot of <paramref name="folder"/>: its whole content as-is, without filtering. A path
      /// that is not valid according to the release format (e.g. one starting with a dot) makes the whole
      /// snapshot be refused.
      /// </summary>
      /// <exception cref="ReleaseFormatException">A file has a path that cannot be published.</exception>
      public static Task<LocalSnapshot> ScanAsync(string folder, IProgress<ReleaseProgress>? progress,
         CancellationToken token) =>
         Task.Run(async () => {
            var full = Path.GetFullPath(folder);
            var entries = EnumerateFiles(full);
            var invalid = entries.Keys
               .Select(r => (Path: r, Reason: ReleaseManifestSerializer.CheckPath(r)))
               .Where(r => r.Reason is not null)
               .ToArray();
            if (invalid.Length > 0) {
               throw new ReleaseFormatException("These files cannot be published:\n" +
                  string.Join("\n", invalid.Take(15).Select(r => $"{r.Path}: {r.Reason}")) +
                  (invalid.Length > 15 ? $"\n... and {invalid.Length - 15:N0} more" : ""));
            }

            var ordered = entries.OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
            var total = ordered.Sum(r => r.Value.Length);
            var done = 0L;
            var files = new List<ReleaseFile>(ordered.Length);
            var stamps = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            var sinceReport = Stopwatch.StartNew();

            for (var index = 0; index < ordered.Length; index++) {
               var (path, info) = ordered[index];
               var before = done;
               var fileProgress = new InlineProgress<long>(read => {
                  if (sinceReport.ElapsedMilliseconds < 100) return;
                  sinceReport.Restart();
                  progress?.Report(new ReleaseProgress("Hashing", index + 1, ordered.Length, path, before + read, total));
               });
               var (size, sha256) = await ReleaseHash.ComputeAsync(info.FullName, fileProgress, token);
               done += size;
               files.Add(new ReleaseFile { Path = path, Size = size, Sha256 = sha256 });
               stamps[path] = (info.Length, info.LastWriteTimeUtc);
            }

            progress?.Report(new ReleaseProgress("Hashing", ordered.Length, ordered.Length, null, total, total));
            return new LocalSnapshot(full, files, stamps, ReleaseGrouping.Load(full));
         }, token);

      internal static Dictionary<string, FileInfo> EnumerateFiles(string folder) {
         var root = new DirectoryInfo(folder);
         var options = new EnumerationOptions {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false
         };
         return root.EnumerateFiles("*", options).ToDictionary(
            r => Path.GetRelativePath(root.FullName, r.FullName).Replace(Path.DirectorySeparatorChar, '/'),
            StringComparer.OrdinalIgnoreCase);
      }
   }

   /// <summary>
   /// An <see cref="IProgress{T}"/> that runs its callback on the reporting thread, for reports that are
   /// thinned out before they are passed on to a UI-bound progress.
   /// </summary>
   internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
   {
      public void Report(T value) => report(value);
   }
}
