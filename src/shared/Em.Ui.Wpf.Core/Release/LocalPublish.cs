using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Kelompok tampilan sebuah file rilis di Release Manager.</summary>
   public enum ReleaseGroup
   {
      /// <summary>Assembly project sendiri beserta file pendampingnya (<c>.pdb</c>, <c>.exe</c>, <c>.deps.json</c>, ...).</summary>
      MainModules = 0,

      /// <summary>Package NuGet dan file lain yang tidak dikenal.</summary>
      ExtraLibraries = 1,

      /// <summary>Runtime .NET yang ikut dibawa aplikasi self-contained.</summary>
      DotNetRuntime = 2
   }

   /// <summary>
   /// Menentukan kelompok setiap file hasil publish dari <c>&lt;host&gt;.deps.json</c>: aset milik library
   /// bertipe <c>project</c> masuk <see cref="ReleaseGroup.MainModules"/>, milik runtime pack masuk
   /// <see cref="ReleaseGroup.DotNetRuntime"/>, dan sisanya <see cref="ReleaseGroup.ExtraLibraries"/>.
   /// Tanpa <c>deps.json</c>, semuanya masuk <see cref="ReleaseGroup.ExtraLibraries"/>.
   /// </summary>
   public sealed class ReleaseGrouping
   {
      private static readonly string[] CompanionSuffixes =
         [".dll", ".exe", ".pdb", ".xml", ".runtimeconfig.json", ".deps.json", ".dll.config"];

      private const string ResourcesSuffix = ".resources.dll";

      private readonly Dictionary<string, ReleaseGroup> _claimed = new(StringComparer.OrdinalIgnoreCase);
      private readonly HashSet<string> _projectNames = new(StringComparer.OrdinalIgnoreCase);

      private ReleaseGrouping() { }

      /// <summary>Kelompok kosong: semua file masuk <see cref="ReleaseGroup.ExtraLibraries"/>.</summary>
      public static ReleaseGrouping Empty { get; } = new();

      /// <summary>Nama file <c>deps.json</c> yang dipakai, atau <c>null</c> kalau tidak ada.</summary>
      public string? DepsFile { get; private init; }

      /// <summary>
      /// Membaca <c>deps.json</c> milik aplikasi di <paramref name="publishFolder"/>: yang punya
      /// <c>.runtimeconfig.json</c> bernama sama, atau satu-satunya <c>deps.json</c> di sana.
      /// <see cref="Empty"/> kalau tidak ada atau tidak bisa dibaca.
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

      /// <summary>Kelompok file <paramref name="path"/> (relatif terhadap folder publish, dipisah <c>/</c>).</summary>
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
   /// Potret local publish folder pada satu saat: setiap file dengan ukuran dan SHA-256-nya, siap
   /// dibandingkan dengan tujuan dan dijadikan <c>release.json</c>.
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

      /// <summary>Setiap file, path-nya relatif terhadap <see cref="Folder"/> dan dipisah <c>/</c>.</summary>
      public IReadOnlyList<ReleaseFile> Files { get; }

      /// <summary>Pengelompokan file menurut <c>deps.json</c> di folder ini.</summary>
      public ReleaseGrouping Grouping { get; }

      /// <summary>Total ukuran seluruh file dalam byte.</summary>
      public long TotalSize { get; }

      /// <summary>Path lengkap di disk untuk file <paramref name="path"/>.</summary>
      public string FullPathOf(string path) => Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));

      /// <summary>
      /// Path pertama yang berubah sejak potret ini dibuat - ukuran atau waktu tulisnya lain, hilang, atau
      /// file baru muncul - atau <c>null</c> kalau folder masih sama. Dipakai tepat sebelum Sync, supaya
      /// yang diterbitkan tetap sama dengan yang dibandingkan.
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
   /// Membaca local publish folder: memeriksa apakah ada isinya, lalu menghitung ukuran dan SHA-256 setiap
   /// file di latar belakang dengan kemajuan yang bisa dibatalkan.
   /// </summary>
   public static class LocalPublish
   {
      /// <summary><c>true</c> kalau <paramref name="folder"/> ada dan berisi setidaknya satu file.</summary>
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
      /// Membuat potret <paramref name="folder"/>: seluruh isinya apa adanya, tanpa filter. Path yang tidak
      /// sah menurut format rilis (mis. berawalan titik) membuat seluruh potret ditolak.
      /// </summary>
      /// <exception cref="ReleaseFormatException">Ada file yang path-nya tidak bisa diterbitkan.</exception>
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
