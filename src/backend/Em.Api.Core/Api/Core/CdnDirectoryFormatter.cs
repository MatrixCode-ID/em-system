using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Em.Api.Core;

internal sealed class CdnDirectoryFormatter(SkinPages pages, CdnStore store) : IDirectoryFormatter {
   public Task GenerateContentAsync(HttpContext context, IEnumerable<IFileInfo> contents) {
      if (!SkinPages.IsBrowser(context.Request)) {
         context.Response.StatusCode = StatusCodes.Status404NotFound;
         return Task.CompletedTask;
      }
      var requestPath = SkinPages.RequestPath(context);
      var segments = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
      var href = "/";
      var crumbs = new List<CdnBreadcrumb>();
      foreach (var segment in segments) {
         href += Uri.EscapeDataString(segment) + "/";
         crumbs.Add(new(segment, href));
      }
      var entries = contents.Select(info => new CdnPageEntry(info.Name,
         Uri.EscapeDataString(info.Name) + (info.IsDirectory ? "/" : ""), info.IsDirectory,
         info.IsDirectory ? null : info.Length, info.LastModified)).ToArray();
      var page = new CdnDirectoryPage {
         RequestPath = requestPath, Breadcrumbs = crumbs.AsReadOnly(), HasParent = segments.Length > 1,
         Folders = Array.AsReadOnly(entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray()),
         Files = Array.AsReadOnly(entries.Where(e => !e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray()),
         ApplicationName = pages.ApplicationName, HostVersion = pages.HostVersion, MaxFileSize = store.MaxFileSize,
         AssetUrls = pages.AssetUrls
      };
      return pages.DirectoryAsync(page, context);
   }
}
