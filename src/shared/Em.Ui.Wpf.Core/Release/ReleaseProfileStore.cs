using System.IO;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Release;

/// <summary>Atomic single-JSON profile storage. Version-one directories are ignored.</summary>
public sealed class ReleaseProfileStore {
   /// <summary>Profile storage root.</summary>
   public string Root { get; }
   /// <summary>Creates a profile store.</summary>
   public ReleaseProfileStore(string root) => Root = Path.GetFullPath(root);
   /// <summary>Gets the profile JSON path.</summary>
   public string FileOf(string id) { ReleaseProfile.ValidateId(id); return PublishPaths.Inside(Root, id.ToLowerInvariant() + ".json"); }
   private ReleaseProfileEntry Read(string file) {
      try {
         PublishPaths.RejectLinks(file);
         var profile = ReleaseProfile.FromJson(File.ReadAllText(file));
         if (!string.Equals(profile.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Profile id does not match its filename.");
         return new(file, profile, null, File.GetLastWriteTimeUtc(file));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { return new(file, null, ex.Message, DateTime.MinValue); }
   }
   /// <summary>Lists current profile files, ignoring legacy profile directories.</summary>
   public IReadOnlyList<ReleaseProfileEntry> List() {
      PublishPaths.RejectLinks(Root); Directory.CreateDirectory(Root);
      return Directory.EnumerateFiles(Root, "*.json").Where(f => Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "N", out _)).Select(Read).OrderBy(e => e.Profile?.Name ?? e.Directory, StringComparer.OrdinalIgnoreCase).ToArray();
   }
   /// <summary>Loads a current profile.</summary>
   public ReleaseProfile Load(string id) { var entry = Read(FileOf(id)); return entry.Profile ?? throw new InvalidDataException(entry.Error); }
   /// <summary>Saves a profile with optional concurrent-edit protection.</summary>
   public ReleaseProfileEntry Save(ReleaseProfile profile, DateTime? expectedLastWriteUtc = null) {
      profile.Validate(); var file = FileOf(profile.Id);
      if (List().Any(e => e.Profile is { } other && other.Id != profile.Id && string.Equals(other.Name, profile.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("A profile with this name already exists.");
      if (expectedLastWriteUtc is { } time && (!File.Exists(file) || File.GetLastWriteTimeUtc(file) != time)) throw new ReleaseProfileChangedException();
      PublishPaths.Atomic(file, ReleaseProfile.ToJson(profile)); return Read(file);
   }
   /// <summary>Creates an empty product profile.</summary>
   public ReleaseProfile Create(string name) { var p = new ReleaseProfile { Name = name }; Save(p); return p; }
   /// <summary>Duplicates settings and the product key.</summary>
   public ReleaseProfile Duplicate(string id) { var p = Load(id).Clone(); p.Id = Guid.NewGuid().ToString("N"); p.Name = UniqueName(p.Name + " (copy)"); Save(p); return p; }
   private string UniqueName(string name) { var result = name; for (var i = 2; List().Any(e => string.Equals(e.Profile?.Name, result, StringComparison.OrdinalIgnoreCase)); i++) result = name + " (" + i + ")"; return result; }
   /// <summary>Renames a profile.</summary>
   public void Rename(string id, string name) { var p = Load(id); p.Name = name; Save(p); }
   /// <summary>Deletes only the JSON of this profile.</summary>
   public void Delete(string id) { var file = FileOf(id); PublishPaths.RejectLinks(file); File.Delete(file); }
   /// <summary>Exports all product settings, including its unencrypted key.</summary>
   public void Export(string id, string path) => PublishPaths.Atomic(Path.GetFullPath(path), ReleaseProfile.ToJson(Load(id)));
   /// <summary>Imports a version-two JSON; old formats are refused.</summary>
   public ReleaseProfile Import(string path, bool newId) {
      if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Choose a product profile JSON (maximum 16 MB).");
      var p = ReleaseProfile.FromJson(File.ReadAllText(path));
      if (newId) p.Id = Guid.NewGuid().ToString("N");
      if (File.Exists(FileOf(p.Id))) throw new ReleaseProfileConflictException();
      p.Name = UniqueName(p.Name); Save(p); return p;
   }

}
/// <summary>The profile was modified outside the current editor.</summary>
public sealed class ReleaseProfileChangedException() : IOException("The profile JSON changed outside Release Manager.");
/// <summary>An imported profile id already exists.</summary>
public sealed class ReleaseProfileConflictException() : IOException("A profile with this id already exists.");
