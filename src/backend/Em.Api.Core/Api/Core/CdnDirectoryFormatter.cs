using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Em.Api.Core
{
   /// <summary>
   /// The HTML page a browser gets when it opens a CDN folder: breadcrumb, folders first, then files,
   /// with size and modification time. Self-contained - inline CSS, no external assets - so it works
   /// behind any proxy and needs nothing else to be served.
   /// </summary>
   /// <remarks>
   /// Every name goes through <see cref="HtmlEncoder"/> for text and <see cref="Uri.EscapeDataString"/>
   /// per segment for links: file names are whatever an administrator uploaded, and one of them must
   /// never be able to become markup on this page.
   /// </remarks>
   internal sealed class CdnDirectoryFormatter : IDirectoryFormatter
   {
      private static readonly HtmlEncoder Html = HtmlEncoder.Default;

      public Task GenerateContentAsync(HttpContext context, IEnumerable<IFileInfo> contents) {
         // PathBase + Path is the full address as the browser sees it, "/cdn/sub/" for example. The
         // middleware has already redirected anything without a trailing slash, so relative links
         // below resolve against this folder.
         var requestPath = (context.Request.PathBase + context.Request.Path).Value ?? CdnStore.PublicRequestPath + "/";
         var segments = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
         var title = "Index of /" + string.Join('/', segments) + "/";

         var entries = contents.ToArray();
         var folders = entries.Where(r => r.IsDirectory).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
         var files = entries.Where(r => !r.IsDirectory).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

         var html = new StringBuilder();
         html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light dark\">")
            .Append("<title>").Append(Html.Encode(title)).Append("</title>")
            .Append("<style>").Append(Css).Append("</style></head><body><main>");

         AppendBreadcrumb(html, segments);

         html.Append("<table><thead><tr><th class=\"name\">Name</th><th class=\"size\">Size</th>")
            .Append("<th class=\"date\">Modified (UTC)</th></tr></thead><tbody>");

         if (segments.Length > 1) {
            html.Append("<tr><td class=\"name\"><a href=\"../\"><span class=\"icon\">↩</span>..</a></td>")
               .Append("<td class=\"size\"></td><td class=\"date\"></td></tr>");
         }

         foreach (var folder in folders) {
            AppendRow(html, folder, Uri.EscapeDataString(folder.Name) + "/", "📁", folder.Name + "/");
         }

         foreach (var file in files) {
            AppendRow(html, file, Uri.EscapeDataString(file.Name), "📄", file.Name);
         }

         if (!entries.Any()) {
            html.Append("<tr><td class=\"empty\" colspan=\"3\">This folder is empty.</td></tr>");
         }

         html.Append("</tbody></table></main></body></html>");

         var bytes = Encoding.UTF8.GetBytes(html.ToString());
         context.Response.ContentType = "text/html; charset=utf-8";
         context.Response.ContentLength = bytes.Length;
         return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
      }

      private static void AppendBreadcrumb(StringBuilder html, string[] segments) {
         html.Append("<h1>Index of ");
         var href = new StringBuilder("/");
         for (var index = 0; index < segments.Length; index++) {
            href.Append(Uri.EscapeDataString(segments[index])).Append('/');
            html.Append("<a href=\"").Append(Html.Encode(href.ToString())).Append("\">")
               .Append(Html.Encode(segments[index])).Append("</a>");
            html.Append(index < segments.Length - 1 ? "<span class=\"sep\">/</span>" : "/");
         }

         html.Append("</h1>");
      }

      private static void AppendRow(StringBuilder html, IFileInfo info, string href, string icon, string label) {
         var modified = info.LastModified.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
         html.Append("<tr><td class=\"name\"><a href=\"").Append(Html.Encode(href)).Append("\">")
            .Append("<span class=\"icon\">").Append(icon).Append("</span>")
            .Append(Html.Encode(label)).Append("</a></td>");

         if (info.IsDirectory) {
            html.Append("<td class=\"size\">-</td>");
         }
         else {
            html.Append("<td class=\"size\" title=\"")
               .Append(info.Length.ToString("N0", CultureInfo.InvariantCulture)).Append(" bytes\">")
               .Append(FormatSize(info.Length)).Append("</td>");
         }

         html.Append("<td class=\"date\">").Append(modified).Append("</td></tr>");
      }

      internal static string FormatSize(long bytes) {
         string[] units = ["B", "KB", "MB", "GB", "TB"];
         double value = bytes;
         var unit = 0;
         while (value >= 1024 && unit < units.Length - 1) {
            value /= 1024;
            unit++;
         }

         return unit == 0
            ? $"{bytes} B"
            : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[unit];
      }

      private const string Css =
         ":root{--bg:#f7f8fa;--card:#fff;--text:#1f2328;--muted:#656d76;--line:#e4e7eb;--link:#1a5fb4;--hover:#eef3fb}" +
         "@media (prefers-color-scheme:dark){:root{--bg:#16181c;--card:#1f2227;--text:#e6e8eb;--muted:#9aa1a9;--line:#30343a;--link:#79a8ff;--hover:#262b33}}" +
         "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,-apple-system,sans-serif}" +
         "main{max-width:960px;margin:0 auto;padding:24px 16px}" +
         "h1{font-size:18px;font-weight:600;margin:0 0 16px;word-break:break-all}h1 .sep{color:var(--muted);margin:0 2px}" +
         "a{color:var(--link);text-decoration:none}a:hover{text-decoration:underline}" +
         "table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:8px;overflow:hidden}" +
         "th,td{padding:8px 12px;text-align:left;border-bottom:1px solid var(--line)}" +
         "th{font-size:12px;font-weight:600;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}" +
         "tbody tr:last-child td{border-bottom:0}tbody tr:hover{background:var(--hover)}" +
         "td.name{word-break:break-all}.icon{display:inline-block;width:1.6em}" +
         ".size{text-align:right;white-space:nowrap;width:1%}.date{white-space:nowrap;width:1%;color:var(--muted)}" +
         ".empty{color:var(--muted);text-align:center;padding:24px}" +
         "@media (max-width:560px){.date{display:none}}";
   }
}
