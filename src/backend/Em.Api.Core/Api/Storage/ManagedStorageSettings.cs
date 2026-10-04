using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Api.Shared;

namespace Em.Api.Core.Storage;

internal sealed record StorageSettingsDocument
{
   public int Version { get; init; } = 1;
   public long Revision { get; init; }
   public required StorageFeatureSettings Cdn { get; init; }
   public required StorageFeatureSettings Registry { get; init; }
   public StorageFeatureSettings NuGet { get; init; } = new() { Enabled = true, Directory = "./data/nuget", MaxUploadMb = 250 };
}

/// <summary>One host, one immutable active snapshot. All feature mutations share the same lock/revision.</summary>
internal sealed class ManagedStorageSettings
{
   private readonly object _gate = new();
   private readonly IStorageSettingsPersistence? _persistence;
   private readonly string _contentRoot;
   private readonly string[] _protectedRoots;
   private StorageSettingsDocument _saved;
   private readonly StorageSettingsDocument _active;
   private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
   private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
      ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

   internal ManagedStorageSettings(string contentRoot, IStorageSettingsPersistence? persistence, StorageFeatureSettings cdn,
      StorageFeatureSettings registry, IEnumerable<string> protectedRoots, StorageFeatureSettings? nuget = null)
   {
      _contentRoot = Path.GetFullPath(contentRoot);
      _persistence = persistence;
      _protectedRoots = protectedRoots.Select(Resolve).ToArray();
      try {
         var json = persistence?.Read();
         _saved = json is not null
            ? JsonSerializer.Deserialize<StorageSettingsDocument>(json, Json)
               ?? throw new InvalidDataException("Storage settings document is null.")
            : new StorageSettingsDocument { Cdn = cdn, Registry = registry, NuGet = nuget ?? new() { Enabled=true, Directory="./data/nuget", MaxUploadMb=250 } };
         if (_saved.Version != 1 || _saved.Revision < 0 || _saved.Cdn is null || _saved.Registry is null)
            throw new InvalidDataException("Unsupported version or invalid storage settings structure.");
         _saved = _saved with { Cdn = Normalize(_saved.Cdn), Registry = Normalize(_saved.Registry), NuGet = Normalize(_saved.NuGet) };
         _active = _saved;
         if (Managed) {
            if (_active.Cdn.Enabled) ValidateCore(0, _active.Cdn, _active, create: true);
            if (_active.Registry.Enabled) ValidateCore(1, _active.Registry, _active, create: true);
            if (_active.NuGet.Enabled) ValidateCore(2, _active.NuGet, _active, create: true);
         }
      }
      catch (Exception ex) {
         throw new InvalidOperationException($"Cannot initialize storage settings '{_persistence?.Description ?? "static"}'. Restore a backup or correct the configuration/storage. {ex.Message}", ex);
      }
   }

   internal StorageFeatureSettings Active(bool cdn) => Active(cdn ? 0 : 1);
   internal StorageFeatureStatus Status(bool cdn) => Status(cdn ? 0 : 1);
   internal StorageSettingsDetail Detail(bool cdn) => Detail(cdn ? 0 : 1);
   internal StorageDirectoryValidation Validate(bool cdn, StorageFeatureSettings draft, Action<string>? verifyRegistry = null) => Validate(cdn ? 0 : 1,draft,verifyRegistry);
   internal StorageSettingsDetail Save(bool cdn, StorageSettingsSave request, Action<string>? verifyRegistry = null) => Save(cdn ? 0 : 1,request,verifyRegistry);
   internal bool Managed => _persistence is not null;
   internal StorageFeatureSettings Active(int feature) => feature == 0 ? _active.Cdn : feature == 1 ? _active.Registry : _active.NuGet;
   private StorageFeatureSettings Saved(int feature) => feature == 0 ? _saved.Cdn : feature == 1 ? _saved.Registry : _saved.NuGet;

   internal StorageFeatureStatus Status(int feature) {
      lock (_gate) {
         RefreshSaved();
         return new(Managed, Active(feature).Enabled, Saved(feature).Enabled, Different(feature));
      }
   }

   internal StorageSettingsDetail Detail(int feature) {
      lock (_gate) {
         RefreshSaved();
         return SnapshotDetail(feature);
      }
   }
   private StorageSettingsDetail SnapshotDetail(int feature) => new(_saved.Revision, Active(feature), Saved(feature),
      ResolveOptional(Active(feature).Directory), ResolveOptional(Saved(feature).Directory), Managed, Different(feature));

   private void RefreshSaved() {
      if (_persistence is null) return;
      var json = _persistence.Read();
      if (json is null) {
         if (_saved.Revision != 0) throw new ActionException("Storage settings row was removed. Restore its backup before editing.", 409);
         return;
      }
      var document = JsonSerializer.Deserialize<StorageSettingsDocument>(json, Json)
         ?? throw new InvalidDataException("Storage settings are null.");
      if (document.Version != 1 || document.Revision < 0) throw new InvalidDataException("Unsupported storage settings version/revision.");
      _saved = document with { Cdn = Normalize(document.Cdn), Registry = Normalize(document.Registry), NuGet = Normalize(document.NuGet) };
   }

   private bool Different(int feature) => !Equivalent(Active(feature), Saved(feature), feature);
   private bool Equivalent(StorageFeatureSettings a, StorageFeatureSettings b, int feature) =>
      a.Enabled == b.Enabled && string.Equals(ResolveOptional(a.Directory), ResolveOptional(b.Directory), PathComparison)
      && (feature == 1 || a.MaxUploadMb == b.MaxUploadMb);

   internal StorageDirectoryValidation Validate(int feature, StorageFeatureSettings draft, Action<string>? verifyRegistry = null) {
      lock (_gate) {
         EnsureManaged();
         RefreshSaved();
         try {
            draft = Normalize(draft);
            ValidateCore(feature, draft, _saved, create: false);
            if (feature == 1 && NeedsRegistryVerification(draft)) verifyRegistry?.Invoke(ResolveOptional(draft.Directory));
            return new(true, ResolveOptional(draft.Directory), "Directory validated. Save creates a missing enabled directory. No data is moved; restart validation is repeated.");
         }
         catch (Exception ex) when (ex is ActionException or IOException or UnauthorizedAccessException or ArgumentException) {
            return new(false, "", ex.Message);
         }
      }
   }

   internal StorageSettingsDetail Save(int feature, StorageSettingsSave request, Action<string>? verifyRegistry = null) {
      ArgumentNullException.ThrowIfNull(request);
      lock (_gate) {
         EnsureManaged();
         RefreshSaved();
         if (request.Revision != _saved.Revision) throw new ActionException("Storage settings changed. Refresh/reload the settings before saving your draft again.", 409);
         var draft = Normalize(request.Settings);
         var next = feature == 0 ? _saved with { Cdn = draft } : feature == 1 ? _saved with { Registry = draft } : _saved with { NuGet = draft };
         // Check both sides, including the active roots which remain in use until restart.
         ValidateCore(feature, draft, next, create: false);
         if (feature == 1 && NeedsRegistryVerification(draft))
            verifyRegistry?.Invoke(ResolveOptional(draft.Directory));
         ValidateCore(feature, draft, next, create: draft.Enabled);
         next = next with { Revision = checked(_saved.Revision + 1) };
         _persistence!.Write(_saved.Revision, JsonSerializer.Serialize(next, Json));
         _saved = next; // Publish only after the database transaction commits.
         return SnapshotDetail(feature);
      }
   }

   private bool NeedsRegistryVerification(StorageFeatureSettings draft) =>
      (draft.Enabled && !Active(1).Enabled)
      || !string.Equals(ResolveOptional(draft.Directory), ResolveOptional(Active(1).Directory), PathComparison);

   private void EnsureManaged() {
      if (!Managed) throw new ActionException("This host uses static storage configuration. UI settings are unavailable.", 409);
   }

   private static StorageFeatureSettings Normalize(StorageFeatureSettings settings) {
      if (settings is null) throw new ActionException("Storage settings are required.", 400);
      if (settings.MaxUploadMb <= 0) throw new ActionException("Max upload MB must be positive.", 400);
      var directory = settings.Directory?.Trim() ?? "";
      if (settings.Enabled && directory.Length == 0) throw new ActionException("Enabled storage requires a directory.", 400);
      return settings with { Directory = directory };
   }

   private void ValidateCore(int feature, StorageFeatureSettings draft, StorageSettingsDocument snapshot, bool create) {
      if (feature == 2 && draft.MaxUploadMb is <1 or >4096) throw new ActionException("NuGet max package MB must be 1–4096.",400);
      if (draft.Directory.Length == 0) return;
      var path = Resolve(draft.Directory);
      RejectLinks(path);
      // Payloads inside the application are permitted only below data; application binaries/config/secrets are forbidden.
      if (Contains(path, _contentRoot) || (Contains(_contentRoot, path) && !Contains(Resolve("data"), path)))
         throw new ActionException("Storage must not overlap application/configuration directories. Use a dedicated payload directory (under data or outside the application).", 400);
      foreach (var root in _protectedRoots) {
         RejectLinks(root);
         if (Overlaps(path, root)) throw new ActionException("Storage overlaps internal/binary/task storage.", 400);
      }
      var others = new[] {snapshot.Cdn,snapshot.Registry,snapshot.NuGet};
      var active = new[] {_active.Cdn,_active.Registry,_active.NuGet};
      foreach (var candidate in others.Where((_,i)=>i!=feature).Concat(active.Where((_,i)=>i!=feature))) {
         if (candidate.Directory.Length == 0) continue;
         var otherPath = Resolve(candidate.Directory); RejectLinks(otherPath);
         if (Overlaps(path, otherPath)) throw new ActionException("Storage directories must not overlap, including active directories.",400);
      }
      var probeDirectory = path;
      while (!Directory.Exists(probeDirectory)) {
         if (File.Exists(probeDirectory)) throw new ActionException("Storage path refers to a file.", 400);
         probeDirectory = Path.GetDirectoryName(probeDirectory) ?? throw new ActionException("No accessible parent directory.", 400);
      }
      Probe(probeDirectory);
      if (feature == 0 && Directory.Exists(path)) ValidatePublicTree(path);
      if (create) { Directory.CreateDirectory(path); RejectLinks(path); Probe(path); }
   }

   private static void Probe(string directory) {
      _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToArray();
      var file = Path.Combine(directory, ".em-storage-probe-" + Guid.NewGuid().ToString("N"));
      bool created = false;
      try {
         using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
            created = true;
            stream.WriteByte(73); stream.Flush(true); stream.Position = 0;
            if (stream.ReadByte() != 73) throw new IOException("Storage read/write probe failed.");
         }
      }
      finally { if (created) File.Delete(file); } // Cleanup errors propagate instead of silently leaving probes.
   }

   private static void ValidatePublicTree(string root) {
      var pending = new Stack<string>(); pending.Push(root);
      while (pending.Count > 0) {
         foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop())) {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
               throw new ActionException("Public CDN storage must not contain symbolic links/reparse points.", 400);
            var name = Path.GetFileName(entry);
            if (name.Equals(EmApiConfig.FileName, StringComparison.OrdinalIgnoreCase)
                || name.Equals("em.local.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("secrets.local.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".env", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
               throw new ActionException("Public CDN storage contains known application/secret files. Choose a dedicated payload directory.", 400);
            if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
         }
      }
   }
   internal string Resolve(string path) {
      if (OperatingSystem.IsWindows() && (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")))
         throw new ActionException("Device paths are not allowed for storage settings.", 400);
      var resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, _contentRoot));
      foreach (var segment in resolved[Path.GetPathRoot(resolved)!.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
         if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ActionException("Invalid directory path component.", 400);
         if (OperatingSystem.IsWindows()) {
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.Contains('~') || stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
               throw new ActionException("Reserved/ambiguous Windows directory names and short-name aliases are not allowed.", 400);
         }
      }
      return resolved;
   }
   private string ResolveOptional(string path) => string.IsNullOrWhiteSpace(path) ? "" : Resolve(path);
   private static bool Contains(string parent, string child) => string.Equals(parent, child, PathComparison)
      || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, PathComparison);
   private static bool Overlaps(string a, string b) => Contains(a, b) || Contains(b, a);
   internal static void RejectLinks(string path) {
      for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) {
         // Inspect attributes directly: dangling reparse points also must not be accepted.
         try {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
               throw new ActionException("Storage/config paths must not contain symbolic links or reparse points.", 400);
         }
         catch (FileNotFoundException) { }
         catch (DirectoryNotFoundException) { }
      }
   }
}
