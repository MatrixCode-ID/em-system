using System.Text.Json;
using System.Text.RegularExpressions;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core;

internal sealed partial class CdnStore {
   private readonly object _gate = new();
   private readonly HashSet<string> _activeTemporary = new(StringComparer.OrdinalIgnoreCase);
   private readonly List<ReadHandle> _readers = [];
   private List<CdnManagedFolder> _managed = [];
   private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
   internal string StoragePath { get; private set; } = "";
   internal string TempPath => Path.Combine(StoragePath, "temp");
   private string RecyclePath => Path.Combine(StoragePath, "recycle");
   private string ManagedPath => Path.Combine(StoragePath, "settings", "managed.json");

   private void InitializeManagement(string storage) {
      StoragePath = storage;
      foreach (var name in new[] { "files", "temp", "recycle", "settings" })
         Directory.CreateDirectory(Path.Combine(storage, name));
      if (File.Exists(ManagedPath))
         _managed = JsonSerializer.Deserialize<List<CdnManagedFolder>>(File.ReadAllBytes(ManagedPath), StorageJson)
            ?? throw new InvalidDataException("Invalid CDN managed folder settings.");
      foreach (var folder in _managed) {
         if (NormalizePath(folder.Path) != folder.Path || folder.Path.Length == 0 || folder.Owner.Kind != "release")
            throw new InvalidDataException("Invalid CDN managed folder path or owner.");
      }
   }

   private static bool Within(string path, string root) =>
      string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
      path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

   internal CdnManagedFolder? ManagedAt(string? path, bool includeUsage = true) {
      lock (_gate) {
         var normalized = NormalizePath(path);
         var item = _managed.FirstOrDefault(m => Within(normalized, m.Path));
         return item is null ? null : Snapshot(item, includeUsage);
      }
   }

   private CdnManagedFolder Snapshot(CdnManagedFolder item, bool includeUsage = true) => new() {
      Path = item.Path, Owner = new() { Kind = item.Owner.Kind, Id = item.Owner.Id },
      QuotaBytes = item.QuotaBytes, UsedBytes = includeUsage ? SizeOf(ResolveInside(item.Path)).TotalBytes : 0
   };

   internal CdnManagedFolder[] ManagedFolders() {
      lock (_gate) return _managed.Select(item => Snapshot(item)).ToArray();
   }

   private void SaveManaged() {
      var temp = Path.Combine(StoragePath, "settings", Guid.NewGuid().ToString("N") + ".tmp");
      try {
         File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(_managed, StorageJson));
         File.Move(temp, ManagedPath, true);
      }
      finally { if (File.Exists(temp)) File.Delete(temp); }
   }

   internal CdnManagedFolder Manage(string path, string appId) {
      EnsureEnabled();
      ValidateAppId(appId);
      lock (_gate) {
         path = NormalizePath(path);
         if (path.Length == 0) throw new ActionException("The CDN root cannot be managed.", 400);
         var existing = _managed.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
         if (existing is not null) {
            RequireOwner(existing, appId);
            return Snapshot(existing);
         }
         if (_managed.Any(m => Within(path, m.Path) || Within(m.Path, path)))
            throw new ActionException("Managed folders cannot contain or be inside another managed folder.", 409);
         var physical = ResolveInside(path);
         if (File.Exists(physical) || (Directory.Exists(physical) && Directory.EnumerateFileSystemEntries(physical).Any()))
            throw new ActionException($"Folder '{path}' must be physically empty before it can be managed.", 409);
         Directory.CreateDirectory(physical);
         var added = new CdnManagedFolder { Path = path, Owner = new() { Id = appId } };
         _managed.Add(added);
         try { SaveManaged(); }
         catch { _managed.Remove(added); throw; }
         return Snapshot(added);
      }
   }

   private static void ValidateAppId(string appId) {
      if (!Regex.IsMatch(appId, @"^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant))
         throw new ActionException("app_id must contain at least two alphanumeric segments separated by dots.", 400);
   }

   private static void RequireOwner(CdnManagedFolder folder, string appId) {
      if (folder.Owner.Kind != "release" || !string.Equals(folder.Owner.Id, appId, StringComparison.Ordinal))
         throw new ActionException($"Folder '{folder.Path}' is owned by '{folder.Owner.Id}'.", 409);
   }

   internal void EnsureWritable(string? path, string? appId = null) {
      lock (_gate) {
         var normalized = NormalizePath(path);
         var folder = _managed.FirstOrDefault(m => Within(normalized, m.Path) || Within(m.Path, normalized));
         if (appId is null) {
            if (folder is not null)
               throw new ActionException($"Folder '{folder.Path}' is managed by '{folder.Owner.Id}' and is read-only in CDN Manager.", 409);
         }
         else {
            if (folder is null || !Within(normalized, folder.Path))
               throw new ActionException("Release writes require an owned managed folder.", 409);
            RequireOwner(folder, appId);
         }
      }
   }

   internal void Unmanage(string path, string? appId, string? confirmation = null) {
      lock (_gate) {
         path = NormalizePath(path);
         var folder = _managed.SingleOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new ActionException("Managed folder not found.", 404);
         if (appId is not null) RequireOwner(folder, appId);
         else if (!string.Equals(confirmation, Path.GetFileName(path), StringComparison.Ordinal))
            throw new ActionException("Type the folder name to release it.", 400);
         _managed.Remove(folder);
         try { SaveManaged(); }
         catch { _managed.Add(folder); throw; }
      }
   }

   internal void SetQuota(string path, long? quota) {
      if (quota < 0) throw new ActionException("Quota must be zero or greater, or empty for unlimited.", 400);
      lock (_gate) {
         path = NormalizePath(path);
         var folder = _managed.SingleOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new ActionException("Managed folder not found.", 404);
         var previous = folder.QuotaBytes;
         folder.QuotaBytes = quota;
         try { SaveManaged(); }
         catch { folder.QuotaBytes = previous; throw; }
      }
   }

   internal void Preflight(CdnSyncPlan plan) {
      lock (_gate) {
         EnsureWritable(plan.Path, plan.AppId);
         var folder = ManagedAt(plan.Path)!;
         if (!string.Equals(folder.Path, NormalizePath(plan.Path), StringComparison.OrdinalIgnoreCase))
            throw new ActionException("Sync must target the managed root.", 400);
         var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         long total = 0;
         foreach (var file in plan.Files) {
            var normalized = NormalizePath($"{folder.Path}/{file.Path}");
            if (!Within(normalized, folder.Path) || file.Size < 0 || !paths.Add(normalized))
               throw new ActionException("Invalid or duplicate Sync file.", 400);
            total = checked(total + file.Size);
         }
         CheckQuota(folder, total);
      }
   }

   private static void CheckQuota(CdnManagedFolder folder, long total) {
      if (folder.QuotaBytes is { } quota && total > quota)
         throw new ActionException($"Quota of folder '{folder.Path}' is {quota:N0} bytes; the synced folder would be {total:N0} bytes.", 413);
   }

   private CdnStorageInfo SizeOf(string path) {
      var result = new CdnStorageInfo();
      if (File.Exists(path)) { result.FileCount = 1; result.TotalBytes = new FileInfo(path).Length; }
      else if (Directory.Exists(path)) {
         foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false
         }).Where(f => !IsSkinPhysical(f.FullName))) { result.FileCount++; result.TotalBytes = checked(result.TotalBytes + file.Length); }
      }
      return result;
   }

   private TempOperation Temporary(string kind) {
      lock (_gate) {
         var path = Path.Combine(TempPath, kind + "-" + Guid.NewGuid().ToString("N"));
         _activeTemporary.Add(path);
         return new TempOperation(this, path);
      }
   }

   private sealed class TempOperation(CdnStore store, string path) : IDisposable {
      public string Path { get; } = path;
      public void Dispose() {
         lock (store._gate) {
            try { store.DeletePhysical(Path); }
            finally { store._activeTemporary.Remove(Path); }
         }
      }
   }

   internal CdnStorageInfo TemporaryGarbage(bool clear = false) {
      lock (_gate) {
         var result = new CdnStorageInfo();
         foreach (var path in Directory.EnumerateFileSystemEntries(TempPath)) {
            if (_activeTemporary.Contains(path) || IsOpen(path)) continue;
            var size = SizeOf(path);
            result.FileCount += Math.Max(1, size.FileCount);
            result.TotalBytes += size.TotalBytes;
            if (clear) { try { DeletePhysical(path); } catch (IOException) { } }
         }
         return result;
      }
   }

   internal CdnRecycleItem[] RecycleItems() {
      lock (_gate) return Directory.EnumerateDirectories(RecyclePath)
         .Select(p => JsonSerializer.Deserialize<CdnRecycleItem>(File.ReadAllBytes(Path.Combine(p, "item.json")), StorageJson)!)
         .OrderByDescending(i => i.DeletedAtUtc).ToArray();
   }

   private string RecycleDirectory(string id) {
      if (!Guid.TryParseExact(id, "N", out _)) throw new ActionException("Invalid recycle item id.", 400);
      var path = Path.Combine(RecyclePath, id);
      if (!Directory.Exists(path)) throw new ActionException("Recycle item not found.", 404);
      return path;
   }

   internal void Restore(string id) {
      lock (_gate) {
         var itemDirectory = RecycleDirectory(id);
         var item = JsonSerializer.Deserialize<CdnRecycleItem>(File.ReadAllBytes(Path.Combine(itemDirectory, "item.json")), StorageJson)!;
         EnsureWritable(item.Path);
         var target = ResolveInside(item.Path);
         if (File.Exists(target) || Directory.Exists(target))
            throw new ActionException($"Restore conflicts with '{item.Path}'.", 409);
         var parent = Path.GetDirectoryName(target)!;
         for (var ancestor = parent; ancestor.StartsWith(RootPath, PathComparison); ancestor = Path.GetDirectoryName(ancestor)!) {
            if (File.Exists(ancestor)) throw new ActionException($"Restore conflicts with '{ToRelative(ancestor)}'.", 409);
         }
         Directory.CreateDirectory(parent);
         MovePhysical(Path.Combine(itemDirectory, "payload", Path.GetFileName(target)), target);
         Directory.Delete(itemDirectory, true);
      }
   }

   internal void PurgeRecycle(string? id) {
      lock (_gate) {
         foreach (var path in id is null ? Directory.GetDirectories(RecyclePath) : new[] { RecycleDirectory(id) }) {
            if (IsOpen(path)) continue;
            try { DeletePhysical(path); } catch (IOException) { }
         }
      }
   }

   private void Retire(string target, string? appId = null, string deletedBy = "") {
      if (!File.Exists(target) && !Directory.Exists(target)) return;
      if (appId is not null) {
         var temp = Path.Combine(TempPath, "retired-" + Guid.NewGuid().ToString("N"));
         MovePhysical(target, temp);
         if (!IsOpen(temp)) DeletePhysical(temp);
         return;
      }
      var item = new CdnRecycleItem {
         Id = Guid.NewGuid().ToString("N"), Path = ToRelative(target), DeletedAtUtc = DateTimeOffset.UtcNow,
         DeletedBy = deletedBy, IsFolder = Directory.Exists(target), Size = SizeOf(target).TotalBytes
      };
      var directory = Path.Combine(RecyclePath, item.Id);
      Directory.CreateDirectory(directory);
      try {
         var payload = Path.Combine(directory, "payload");
         Directory.CreateDirectory(payload);
         File.WriteAllBytes(Path.Combine(directory, "item.json"), JsonSerializer.SerializeToUtf8Bytes(item, StorageJson));
         MovePhysical(target, Path.Combine(payload, Path.GetFileName(target)));
      }
      catch { Directory.Delete(directory, true); throw; }
   }

   private bool IsOpen(string path) => _readers.Any(r =>
      string.Equals(r.Path, path, PathComparison) || r.Path.StartsWith(path + Path.DirectorySeparatorChar, PathComparison));

   private void MovePhysical(string source, string target) {
      if (Directory.Exists(source)) Directory.Move(source, target);
      else File.Move(source, target);
      foreach (var reader in _readers) {
         if (string.Equals(reader.Path, source, PathComparison)) reader.Path = target;
         else if (reader.Path.StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
            reader.Path = target + reader.Path[source.Length..];
      }
   }

   private void DeletePhysical(string path) {
      if (IsOpen(path)) return;
      if (Directory.Exists(path)) Directory.Delete(path, true);
      else if (File.Exists(path)) File.Delete(path);
   }

   internal Stream OpenRead(string fullPath) {
      lock (_gate) {
         var handle = new ReadHandle { Path = fullPath };
         var stream = new TrackedStream(fullPath, () => {
            lock (_gate) {
               _readers.Remove(handle);
               if (handle.Path.StartsWith(TempPath + Path.DirectorySeparatorChar, PathComparison)) {
                  var relative = Path.GetRelativePath(TempPath, handle.Path).Split(Path.DirectorySeparatorChar)[0];
                  var retired = Path.Combine(TempPath, relative);
                  if (!IsOpen(retired)) DeletePhysical(retired);
               }
            }
         });
         _readers.Add(handle);
         return stream;
      }
   }

   private sealed class ReadHandle { public string Path { get; set; } = ""; }
   private sealed class TrackedStream(string path, Action release)
      : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous) {
      private int _disposed;
      protected override void Dispose(bool disposing) {
         base.Dispose(disposing);
         if (Interlocked.Exchange(ref _disposed, 1) == 0) release();
      }
      public override async ValueTask DisposeAsync() {
         await base.DisposeAsync();
         if (Interlocked.Exchange(ref _disposed, 1) == 0) release();
      }
   }
}
