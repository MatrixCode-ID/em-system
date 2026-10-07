using System.IO;
using System.IO.Compression;
using System.Text;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Atomic profile storage with path and archive restrictions.</summary>
   public sealed class ReleaseProfileStore
   {
      /// <summary>The profile file name.</summary>
      public const string ProfileFileName = "profile.json";
      /// <summary>The key file name.</summary>
      public const string KeyFileName = "signing.pfx";
      /// <summary>The certificate file name.</summary>
      public const string CertificateFileName = "signing.cer";
      private static readonly string[] ArchiveNames = [ProfileFileName, KeyFileName, CertificateFileName];
      /// <summary>The root.</summary>
      public string Root { get; }

      /// <summary>Creates a new instance of <see cref="ReleaseProfileStore"/>.</summary>
      public ReleaseProfileStore(string root) => Root = Path.GetFullPath(root);

      /// <summary>Gets the folder of a profile.</summary>
      public string DirectoryOf(string id) {
         ReleaseProfile.ValidateId(id);
         return PublishPaths.Inside(Root, id.ToLowerInvariant());
      }

      private ReleaseProfileEntry Read(string directory) {
         var file = Path.Combine(directory, ProfileFileName);
         try {
            PublishPaths.RejectLinks(file);
            var profile = ReleaseProfile.FromJson(File.ReadAllText(file));
            if (!string.Equals(profile.Id, Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase))
               throw new InvalidDataException("Profile id does not match its folder.");
            return new(directory, profile, null, File.GetLastWriteTimeUtc(file));
         }
         catch (Exception x) when (x is IOException or InvalidDataException or UnauthorizedAccessException) {
            return new(directory, null, x.Message, DateTime.MinValue);
         }
      }

      /// <summary>Lists the profiles, including files that could not be read.</summary>
      public IReadOnlyList<ReleaseProfileEntry> List() {
         PublishPaths.RejectLinks(Root);
         Directory.CreateDirectory(Root);
         return Directory.EnumerateDirectories(Root)
            .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _)).Select(Read)
            .OrderBy(entry => entry.Profile is null).ThenBy(entry => entry.Profile?.Name ?? entry.Directory, StringComparer.OrdinalIgnoreCase).ToArray();
      }

      /// <summary>Loads a profile by id.</summary>
      public ReleaseProfile Load(string id) {
         var entry = Read(DirectoryOf(id));
         return entry.Profile ?? throw new InvalidDataException(entry.Error);
      }

      /// <summary>Saves a profile, refusing to overwrite a change made elsewhere.</summary>
      public ReleaseProfileEntry Save(ReleaseProfile profile, DateTime? expectedLastWriteUtc = null) {
         profile.Validate();
         CheckName(profile);
         var directory = DirectoryOf(profile.Id);
         var file = PublishPaths.Inside(directory, ProfileFileName);
         if (expectedLastWriteUtc is { } expected && (!File.Exists(file) || File.GetLastWriteTimeUtc(file) > expected))
            throw new ReleaseProfileChangedException();
         PublishPaths.Atomic(file, ReleaseProfile.ToJson(profile));
         return Read(directory);
      }

      private void CheckName(ReleaseProfile profile) {
         if (List().Any(entry => entry.Profile is { } other && other.Id != profile.Id &&
                               string.Equals(other.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A profile named '{profile.Name}' already exists.");
      }

      /// <summary>Creates a new profile with a name.</summary>
      public ReleaseProfile Create(string name) {
         var profile = new ReleaseProfile { Name = name };
         Save(profile);
         return profile;
      }

      private string UniqueName(string name, string suffix) {
         var names = List().Where(entry => entry.Profile is not null).Select(entry => entry.Profile!.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
         for (var n = 1; ; n++) {
            var ending = suffix == "copy" ? (n == 1 ? " (copy)" : $" (copy {n})") : (n == 1 ? "" : $" ({n})");
            var candidate = name[..Math.Min(name.Length, 100 - ending.Length)] + ending;
            if (!names.Contains(candidate)) return candidate;
         }
      }

      /// <summary>Duplicates a profile.</summary>
      public ReleaseProfile Duplicate(string id) {
         var profile = Load(id).Clone();
         var source = DirectoryOf(id);
         PublishPaths.ValidateTree(source);
         profile.Id = Guid.NewGuid().ToString("N");
         profile.Name = UniqueName(profile.Name, "copy");
         var destination = DirectoryOf(profile.Id);
         Directory.CreateDirectory(destination);
         try {
            foreach (var path in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
               Directory.CreateDirectory(PublishPaths.Inside(destination, Path.GetRelativePath(source, path)));
            foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
               var target = PublishPaths.Inside(destination, Path.GetRelativePath(source, path));
               Directory.CreateDirectory(Path.GetDirectoryName(target)!);
               File.Copy(path, target);
            }
            Save(profile);
            return profile;
         }
         catch {
            Delete(profile.Id);
            throw;
         }
      }

      /// <summary>Renames a profile.</summary>
      public void Rename(string id, string newName) {
         var profile = Load(id);
         profile.Name = newName;
         Save(profile);
      }

      /// <summary>Deletes a profile and its files.</summary>
      public void Delete(string id) {
         var path = DirectoryOf(id);
         if (!Directory.Exists(path)) return;
         PublishPaths.ValidateTree(path);
         Directory.Delete(path, recursive: true);
      }

      /// <summary>Gets the path of the key file of a profile.</summary>
      public string KeyFilePath(string id) => PublishPaths.Inside(DirectoryOf(id), KeyFileName);
      /// <summary>Gets the path of the certificate file of a profile.</summary>
      public string CertificateFilePath(string id) => PublishPaths.Inside(DirectoryOf(id), CertificateFileName);
      /// <summary>Whether a profile has a key file.</summary>
      public bool HasKeyFile(string id) => File.Exists(KeyFilePath(id));

      /// <summary>Exports a profile to a zip file, optionally with its sensitive files.</summary>
      public void Export(string id, string zipPath, bool includeSensitive) {
         var profile = Load(id).Clone();
         if (!includeSensitive) profile.Signing.Password = null;
         var target = Path.GetFullPath(zipPath);
         PublishPaths.RejectLinks(target);
         Directory.CreateDirectory(Path.GetDirectoryName(target)!);
         var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
         try {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create)) {
               using (var writer = new StreamWriter(zip.CreateEntry(ProfileFileName).Open(), new UTF8Encoding(false)))
                  writer.Write(ReleaseProfile.ToJson(profile));
               foreach (var name in ArchiveNames.Skip(1)) {
                  if (name == KeyFileName && !includeSensitive) continue;
                  var file = PublishPaths.Inside(DirectoryOf(id), name);
                  if (File.Exists(file)) zip.CreateEntryFromFile(file, name);
               }
            }
            File.Move(temporary, target, overwrite: true);
         }
         finally {
            if (File.Exists(temporary)) File.Delete(temporary);
         }
      }

      /// <summary>Imports a profile from a zip file.</summary>
      public ReleaseProfile Import(string path, bool newId) {
         PublishPaths.RejectLinks(Root);
         Directory.CreateDirectory(Root);
         var temporary = PublishPaths.Inside(Root, ".import-" + Guid.NewGuid().ToString("N"));
         Directory.CreateDirectory(temporary);
         try {
            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) {
               using var zip = ZipFile.OpenRead(path);
               var seen = new HashSet<string>(StringComparer.Ordinal);
               foreach (var entry in zip.Entries) {
                  if (!ArchiveNames.Contains(entry.FullName, StringComparer.Ordinal) || !seen.Add(entry.FullName))
                     throw new InvalidDataException($"Unexpected entry '{entry.FullName}' in the profile archive.");
                  if (entry.Length > 10 * 1024 * 1024) throw new InvalidDataException("Profile archive entry exceeds 10 MB.");
                  using var input = entry.Open();
                  using var output = File.Create(PublishPaths.Inside(temporary, entry.FullName));
                  var buffer = new byte[8192];
                  long total = 0;
                  int count;
                  while ((count = input.Read(buffer)) > 0) {
                     total += count;
                     if (total > 10 * 1024 * 1024) throw new InvalidDataException("Profile archive entry exceeds 10 MB.");
                     output.Write(buffer, 0, count);
                  }
               }
            }
            else if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) {
               if (new FileInfo(path).Length > 10 * 1024 * 1024) throw new InvalidDataException("Profile exceeds 10 MB.");
               File.Copy(path, Path.Combine(temporary, ProfileFileName));
            }
            else throw new InvalidDataException("Choose a .zip or .json profile.");

            var profile = ReleaseProfile.FromJson(File.ReadAllText(Path.Combine(temporary, ProfileFileName)));
            if (newId) profile.Id = Guid.NewGuid().ToString("N");
            if (Directory.Exists(DirectoryOf(profile.Id))) throw new ReleaseProfileConflictException();
            profile.Name = UniqueName(profile.Name, "");
            PublishPaths.Atomic(Path.Combine(temporary, ProfileFileName), ReleaseProfile.ToJson(profile));
            Directory.Move(temporary, DirectoryOf(profile.Id));
            return profile;
         }
         finally {
            if (Directory.Exists(temporary)) {
               PublishPaths.ValidateTree(temporary);
               Directory.Delete(temporary, recursive: true);
            }
         }
      }
   }

   /// <summary>Thrown when profile.json was changed outside Release Manager.</summary>
   public sealed class ReleaseProfileChangedException() : IOException("profile.json was changed outside Release Manager.");
   /// <summary>Thrown when a profile with the same id already exists.</summary>
   public sealed class ReleaseProfileConflictException() : IOException("A profile with the same id already exists.");
}
