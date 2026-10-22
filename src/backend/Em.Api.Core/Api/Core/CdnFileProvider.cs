using Em.Api.Core.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Em.Api.Core;

// Returning no PhysicalPath forces StaticFileMiddleware to use the tracked stream,
// including for Range downloads, instead of bypassing the store through sendfile.
// Folder settings files are never served: ResolveInside refuses them by their real name on disk.
internal sealed class CdnFileProvider(CdnStore store) : IFileProvider {
   private readonly PhysicalFileProvider _inner = new(store.RootPath, ExclusionFilters.None);
   public IFileInfo GetFileInfo(string subpath) {
      string physical;
      try { physical = store.ResolveInside(subpath, allowSkin: true); }
      catch { return new NotFoundFileInfo(subpath); }
      var info = _inner.GetFileInfo(subpath);
      if (info.IsDirectory && CdnStore.IsReservedRootName(subpath)) return new NotFoundFileInfo(subpath);
      if (!info.Exists || info.IsDirectory) return info;
      var noIndex = store.EffectiveSettings(Path.GetDirectoryName(physical)!) is { PublicListing: false };
      return new Download(info, store, noIndex);
   }
   public IDirectoryContents GetDirectoryContents(string subpath) {
      if (CdnStore.IsReservedRootName(subpath)) return NotFoundDirectoryContents.Singleton;
      try {
         var physical = store.ResolveInside(subpath, allowSkin: true);
         var settings = Directory.Exists(physical) ? store.EffectiveSettings(physical) : null;
         if (settings is { PublicListing: false }) return NotFoundDirectoryContents.Singleton;
         return new Contents(_inner.GetDirectoryContents(subpath), subpath, settings, store);
      }
      catch { return NotFoundDirectoryContents.Singleton; }
   }
   // A subfolder whose own listing is off is left out of its parent's web listing too; CDN Manager still lists it.
   private sealed class Contents(IDirectoryContents contents, string subpath, CdnFolderSettings? settings, CdnStore store) : IDirectoryContents {
      public bool Exists => contents.Exists;
      public IEnumerator<IFileInfo> GetEnumerator() => contents.Where(i =>
         (subpath.Trim('/').Length != 0 || !CdnStore.IsReservedRootName(i.Name)) &&
         !CdnStore.IsMetaName(i.Name) && !CdnFolderSettingsFormat.IsHidden(settings, i.Name) &&
         !(i.IsDirectory && i.PhysicalPath is { } path && store.EffectiveSettings(path) is { PublicListing: false })).GetEnumerator();
      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
   }
   public IChangeToken Watch(string filter) => _inner.Watch(filter);
   /// <summary>A served file; <see cref="NoIndex"/> marks files of folders whose public listing is off.</summary>
   internal sealed class Download(IFileInfo info, CdnStore store, bool noIndex) : IFileInfo {
      public bool NoIndex => noIndex;
      public bool Exists => info.Exists;
      public long Length => info.Length;
      public string? PhysicalPath => null;
      public string Name => info.Name;
      public DateTimeOffset LastModified => info.LastModified;
      public bool IsDirectory => false;
      public Stream CreateReadStream() => store.OpenRead(info.PhysicalPath!);
   }
}
