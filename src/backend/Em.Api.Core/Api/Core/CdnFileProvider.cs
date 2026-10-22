using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Em.Api.Core;

// Returning no PhysicalPath forces StaticFileMiddleware to use the tracked stream,
// including for Range downloads, instead of bypassing the store through sendfile.
internal sealed class CdnFileProvider(CdnStore store) : IFileProvider {
   private readonly PhysicalFileProvider _inner = new(store.RootPath, ExclusionFilters.None);
   public IFileInfo GetFileInfo(string subpath) {
      try { store.ResolveInside(subpath, allowSkin: true); }
      catch { return new NotFoundFileInfo(subpath); }
      var info = _inner.GetFileInfo(subpath);
      if (info.IsDirectory && CdnStore.IsReservedRootName(subpath)) return new NotFoundFileInfo(subpath);
      return info.Exists && !info.IsDirectory ? new Download(info, store) : info;
   }
   public IDirectoryContents GetDirectoryContents(string subpath) {
      if (CdnStore.IsReservedRootName(subpath)) return NotFoundDirectoryContents.Singleton;
      try { store.ResolveInside(subpath, allowSkin: true); return new Contents(_inner.GetDirectoryContents(subpath), subpath); }
      catch { return NotFoundDirectoryContents.Singleton; }
   }
   private sealed class Contents(IDirectoryContents contents, string subpath) : IDirectoryContents {
      public bool Exists => contents.Exists;
      public IEnumerator<IFileInfo> GetEnumerator() => contents.Where(i =>
         subpath.Trim('/').Length != 0 || !CdnStore.IsReservedRootName(i.Name)).GetEnumerator();
      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
   }
   public IChangeToken Watch(string filter) => _inner.Watch(filter);
   private sealed class Download(IFileInfo info, CdnStore store) : IFileInfo {
      public bool Exists => info.Exists;
      public long Length => info.Length;
      public string? PhysicalPath => null;
      public string Name => info.Name;
      public DateTimeOffset LastModified => info.LastModified;
      public bool IsDirectory => false;
      public Stream CreateReadStream() => store.OpenRead(info.PhysicalPath!);
   }
}
