using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Em.Shared;
using NuGet.Versioning;
namespace Em.Api.Core.NuPak;

public sealed partial class NuPakStore(string root, int maxPackageMb)
{
   public string Root { get; } = root;
   public long MaxBytes { get; } = maxPackageMb * 1024L * 1024L;
   // A single API instance owns this store. Serialize mutations, including purge and state changes.
   public SemaphoreSlim Gate { get; } = new(1, 1);
   [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
   private static partial Regex IdPattern();
   [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){2,3}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant)]
   private static partial Regex VersionPattern();
   public static string Id(string id) {
      if (id.Length is < 1 or > 100 || !IdPattern().IsMatch(id)) throw new ActionException("Invalid package id.", 400);
      return id.ToLowerInvariant();
   }
   public static string Version(string version) {
      if (version.Length > 64 || !NuGetVersion.TryParse(version, out var parsed)) throw new ActionException("Invalid package version.", 400);
      var normalized = parsed.ToNormalizedString().Split('+')[0].ToLowerInvariant();
      if (normalized.Length > 64 || !VersionPattern().IsMatch(normalized)) throw new ActionException("Invalid package version.", 400);
      return normalized;
   }
   public string PackagePath(string feedId,string id, string version) {
      id = Id(id); version = Version(version);
      var first = id.Split('.')[0].ToUpperInvariant();
      if (OperatingSystem.IsWindows() && (first is "CON" or "PRN" or "AUX" or "NUL" ||
          (first.Length == 4 && (first.StartsWith("COM") || first.StartsWith("LPT")) && first[3] is >= '1' and <= '9')))
         throw new ActionException("Package id uses a reserved Windows storage name.", 400);
      var path = Path.Combine(FeedPath(feedId), "packages", id, version, $"{id}.{version}.nupkg");
      RejectLinks(path); return path;
   }
   public string TempPath() { var path = Path.Combine(Root, "temp", $"{Ulid.NewUlid()}.tmp"); RejectLinks(path); return path; }
   public static string Slug(string slug) {
      if(string.IsNullOrEmpty(slug)||!System.Text.RegularExpressions.Regex.IsMatch(slug,"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",RegexOptions.CultureInvariant)
         || slug is "v2" or "v3" or "feeds" or "packages" or "temp" or "registration" or "flatcontainer" or "search")
         throw new ActionException("Invalid or reserved feed slug. Use 1–64 lowercase ASCII letters/digits and internal hyphens.",400);
      return slug;
   }
   public string FeedPath(string feedId) {
      if(!Ulid.TryParse(feedId,out var parsed)||parsed.ToString()!=feedId) throw new ActionException("Invalid feed storage ID.",400);
      var path=Path.GetFullPath(Path.Combine(Root,"feeds",feedId));
      if(!path.StartsWith(Path.GetFullPath(Path.Combine(Root,"feeds"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid feed path.");
      RejectLinks(path);return path;
   }
   public void EnsureFeedEmpty(string feedId) {
      var dir=FeedPath(feedId);
      if(Directory.Exists(dir)&&SafeFiles(dir).Any()) throw new ActionException("Feed store contains orphan artifacts. Reconcile them before deleting the feed.",409);
   }
   public void DeleteEmptyFeed(string feedId) {
      EnsureFeedEmpty(feedId);var dir=FeedPath(feedId);if(Directory.Exists(dir)) DeleteEmptyDirectories(dir);
   }
   private void DeleteEmptyDirectories(string dir) {
      RejectLinks(dir);foreach(var child in Directory.EnumerateDirectories(dir)) DeleteEmptyDirectories(child);
      if(!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
   }
   private IEnumerable<string> SafeFiles(string dir) {
      RejectLinks(dir);foreach(var file in Directory.EnumerateFiles(dir)) {RejectLinks(file);yield return file;}
      foreach(var child in Directory.EnumerateDirectories(dir)) foreach(var file in SafeFiles(child)) yield return file;
   }
   public void PreflightLegacyArtifacts() {
      RejectLinks(Root);var legacy=Path.Combine(Root,"packages");RejectLinks(legacy);
      if(Directory.Exists(legacy)&&SafeFiles(legacy).Any()) throw new InvalidOperationException("Legacy NuPak store contains artifacts. Stop deployment and determine destination feeds; no automatic adoption or deletion is allowed.");
   }
   public void Initialize() {
      PreflightLegacyArtifacts();RejectLinks(Path.Combine(Root,"feeds"));RejectLinks(Path.Combine(Root,"temp"));
      Directory.CreateDirectory(Path.Combine(Root,"feeds"));Directory.CreateDirectory(Path.Combine(Root,"temp"));
      foreach(var file in Directory.EnumerateFiles(Path.Combine(Root,"temp"),"*.tmp")) {RejectLinks(file);File.Delete(file);}
   }
   public async Task<Upload> ReceiveAsync(Stream input, CancellationToken ct) {
      var path = TempPath();
      try {
         long size = 0;
         using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
         await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
            var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0) {
               size += count;
               if (size > MaxBytes) throw new ActionException("Package exceeds the upload limit.", 413);
               hash.AppendData(buffer, 0, count);
               await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
         }
         string nuspec;
         using (var zip = ZipFile.OpenRead(path)) {
            var specs = zip.Entries.Where(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
               && !e.FullName.Contains('/') && !e.FullName.Contains('\\')).ToArray();
            if (specs.Length != 1 || specs[0].Length > 1024 * 1024) throw new ActionException("Package requires exactly one root nuspec, at most 1 MB.", 400);
            using var stream = specs[0].Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
               XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var xml = XDocument.Load(reader);
            nuspec = xml.ToString();
         }
         var metadata = ParseMetadata(nuspec);
         string Value(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
         var id = Value("id"); Id(id);
         var original = Value("version"); var version = Version(original);
         static string? Limited(string value, int max) => value.Length == 0 ? null : value[..Math.Min(value.Length, max)];
         return new(path, id, version, original, NuGetVersion.Parse(version).IsPrerelease, size,
            Convert.ToBase64String(hash.GetHashAndReset()), nuspec, Limited(Value("title"), 255),
            Limited(Value("description"), 4000), Limited(Value("authors"), 500), Limited(Value("tags"), 1000));
      } catch (Exception ex) {
         File.Delete(path);
         if (ex is not ActionException && ex is (InvalidDataException or XmlException or InvalidOperationException)) throw new ActionException("Invalid NuGet package or nuspec.", 400);
         throw;
      }
   }
   public static XElement ParseMetadata(string nuspec) {
      using var reader = XmlReader.Create(new StringReader(nuspec), new XmlReaderSettings {
         DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
      var doc = XDocument.Load(reader);
      if (doc.Root?.Name.LocalName != "package") throw new ActionException("Invalid nuspec root.", 400);
      return doc.Root.Elements().SingleOrDefault(e => e.Name.LocalName == "metadata") ?? throw new ActionException("Missing nuspec metadata.", 400);
   }
   public async Task RecoverPurgesAsync(NuPakDbContext db, CancellationToken ct) {
      foreach (var file in SafeFiles(Path.Combine(Root,"feeds")).Where(f=>f.EndsWith(".nupkg.purge",StringComparison.Ordinal))) {
         RejectLinks(file);
         var target = file[..^6];
         var version = Path.GetFileName(Path.GetDirectoryName(target))!;
         var id = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(target)))!;
         var relative=Path.GetRelativePath(Path.Combine(Root,"feeds"),target).Split(Path.DirectorySeparatorChar);
         if(relative.Length!=5||relative[1]!="packages") throw new InvalidOperationException("Invalid purge recovery path.");
         var feedId=relative[0];
         if (!string.Equals(PackagePath(feedId,id, version), target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid purge recovery path.");
         var exists = await (from p in db.Packages join v in db.Versions on p.cNuPakPackageId equals v.cNuPakPackageId
            where p.cNuPakFeedId==feedId && p.cNuPakPackageName == id && v.cNuPakVersionNumber == version select v.cNuPakVersionId).AnyAsync(ct);
         if (exists) File.Move(file, target); else { File.Delete(file); CleanDirectories(target); }
      }
   }
   private void RejectLinks(string path) {
      for (var dir = path; dir is not null; dir = Path.GetDirectoryName(dir)) {
         if ((File.Exists(dir) || Directory.Exists(dir)) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            throw new ActionException("The NuGet store must not contain symbolic links or junctions.", 400);
         if (string.Equals(dir, Root, StringComparison.OrdinalIgnoreCase)) break;
      }
   }
   public void CleanDirectories(string path) {
      var dir = Path.GetDirectoryName(path)!;
      for (var i = 0; i < 2; i++) {
         if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
         Directory.Delete(dir); dir = Path.GetDirectoryName(dir)!;
      }
   }
   public sealed record Upload(string Path, string Id, string Version, string Original, bool Prerelease,
      long Size, string Hash, string Nuspec, string? Title, string? Description, string? Authors, string? Tags);
}
