using System.Collections.ObjectModel;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Em.Api.Core;

// All skin output is buffered before the engine assigns transport headers and status.
internal sealed class SkinPages(IEmCdnSkin cdn, IEmHomeSkin home, string applicationName, string hostVersion) {
   internal string ApplicationName { get; } = applicationName;
   internal string HostVersion { get; } = hostVersion;
   internal IReadOnlyDictionary<string, string> AssetUrls { get; set; } = ReadOnlyDictionary<string, string>.Empty;
   private byte[]? _favicon;
   private string? _faviconType;
   internal void Initialize() {
      var icon = home.Favicon;
      _favicon = icon?.ReadBytes();
      _faviconType = icon?.ContentType;
      _ = cdn.Assets; // Force lazy resource declarations before the server accepts requests.
   }
   internal static bool IsBrowser(HttpRequest request) => HttpMethods.IsGet(request.Method) &&
      request.GetTypedHeaders().Accept?.Any(a => a.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) && a.Quality != 0) == true;
   internal static string RequestPath(HttpContext context) => (context.Request.PathBase + context.Request.Path).Value ?? "/";
   internal Task DirectoryAsync(CdnDirectoryPage page, HttpContext context) => HtmlAsync(context, 200, c => cdn.RenderDirectoryAsync(page, c));
   internal Task CdnNotFoundAsync(HttpContext context, bool assetsAvailable) {
      context.Response.StatusCode = 404;
      return IsBrowser(context.Request) ? HtmlAsync(context, 404, c => cdn.RenderNotFoundAsync(
         new(RequestPath(context), ApplicationName, HostVersion) { AssetUrls = assetsAvailable ? AssetUrls : ReadOnlyDictionary<string, string>.Empty }, c)) : Task.CompletedTask;
   }
   internal Task HomeAsync(HttpContext context, bool cdnAvailable) {
      var page = new HomePage(ApplicationName, HostVersion, cdnAvailable ? "/cdn/" : null);
      return IsBrowser(context.Request) ? HtmlAsync(context, 200, c => home.RenderHomeAsync(page, c))
         : BytesAsync(context, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(home.RenderPlainText(page)));
   }
   internal Task SiteNotFoundAsync(HttpContext context) {
      context.Response.StatusCode = 404;
      return IsBrowser(context.Request) ? HtmlAsync(context, 404, c => home.RenderNotFoundAsync(
         new(RequestPath(context), ApplicationName, HostVersion), c)) : Task.CompletedTask;
   }
   internal Task FaviconAsync(HttpContext context) {
      if (_favicon is null) { context.Response.StatusCode = 404; return Task.CompletedTask; }
      context.Response.Headers.CacheControl = "public, max-age=3600";
      return BytesAsync(context, 200, _faviconType!, _favicon);
   }
   internal static async Task HtmlAsync(HttpContext context, int status, Func<HttpContext, Task> render) {
      var original = context.Response.Body;
      await using var buffer = new MemoryStream();
      context.Response.Body = buffer;
      try { await render(context); }
      finally { context.Response.Body = original; }
      await BytesAsync(context, status, "text/html; charset=utf-8", buffer.ToArray());
   }
   private static async Task BytesAsync(HttpContext context, int status, string type, byte[] bytes) {
      context.Response.StatusCode = status;
      context.Response.ContentType = type;
      context.Response.ContentLength = bytes.Length;
      if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
   }
}
