using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Em.Api.Core;

// Returning no PhysicalPath forces StaticFileMiddleware to use the tracked stream,
// including for Range downloads, instead of bypassing the store through sendfile.
internal sealed class CdnFileProvider(CdnStore store) : IFileProvider {
   private readonly PhysicalFileProvider _inner = new(store.RootPath, ExclusionFilters.None);
   public IFileInfo GetFileInfo(string subpath) {
      try { store.ResolveInside(subpath); }
      catch { return new NotFoundFileInfo(subpath); }
      var info = _inner.GetFileInfo(subpath);
      return info.Exists && !info.IsDirectory ? new Download(info, store) : info;
   }
   public IDirectoryContents GetDirectoryContents(string subpath) {
      try { store.ResolveInside(subpath); return _inner.GetDirectoryContents(subpath); }
      catch { return NotFoundDirectoryContents.Singleton; }
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
