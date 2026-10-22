using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;

namespace Em.Api.Core;

internal sealed class DefaultHomeSkin : IEmHomeSkin {
   private static readonly string Logo = EmbeddedFile.From<DefaultHomeSkin>("Em.Skin.Logo.svg").ReadText();
   public DefaultHomeSkin() { _ = Logo; }
   public EmbeddedFile? Favicon { get; } = EmbeddedFile.From<DefaultHomeSkin>("Em.Skin.favicon.ico");
   public string RenderPlainText(HomePage page) => "Welcome To Matrix-Code API System";
   public Task RenderHomeAsync(HomePage page, HttpContext context) => context.Response.WriteAsync(
      Page("Welcome To Matrix-Code API System", "<p>" + HtmlEncoder.Default.Encode(page.HostVersion) + "</p>" +
         (page.CdnUrl is null ? "" : "<a href=\"/cdn/\">Open downloads</a>")));
   public Task RenderNotFoundAsync(SiteNotFoundPage page, HttpContext context) => context.Response.WriteAsync(
      Page("Page not found", "<p>404 · " + HtmlEncoder.Default.Encode(page.RequestPath) + "</p><a href=\"/\">Back to home</a>"));
   private static string Page(string title, string body) => "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"color-scheme\" content=\"light dark\"><link rel=\"icon\" href=\"/favicon.ico\"><title>" + title + "</title><style>" +
      ":root{--bg:#f7f8fa;--card:#fff;--text:#1f2328;--link:#1a5fb4}@media(prefers-color-scheme:dark){:root{--bg:#16181c;--card:#1f2227;--text:#e6e8eb;--link:#79a8ff}}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 'Segoe UI',system-ui,sans-serif;min-height:100vh;display:grid;place-items:center;padding:16px}main{text-align:center;background:var(--card);padding:32px;border-radius:14px;max-width:640px;width:100%}svg{width:200px;max-width:70vw;height:auto}h1{font-size:24px}a{color:var(--link)}" +
      "</style></head><body><main>" + Logo + "<h1>" + title + "</h1>" + body + "</main></body></html>";
}
