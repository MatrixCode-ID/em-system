using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Em.Api.Core;

/// <summary>Renders CDN pages from components with a strongly typed Page parameter.</summary>
public abstract class EmRazorCdnSkin<TDirectory, TNotFound> : IEmCdnSkin
   where TDirectory : IComponent where TNotFound : IComponent {
   /// <summary>Logical resource prefix in the directory component assembly, or null.</summary>
   protected virtual string? AssetPrefix => null;
   /// <inheritdoc />
   public EmSkinAssets? Assets => AssetPrefix is { } prefix ? new(typeof(TDirectory).Assembly, prefix) : null;
   /// <inheritdoc />
   public Task RenderDirectoryAsync(CdnDirectoryPage page, HttpContext context) => SkinRenderer.RenderAsync<TDirectory>(page, context);
   /// <inheritdoc />
   public Task RenderNotFoundAsync(CdnNotFoundPage page, HttpContext context) => SkinRenderer.RenderAsync<TNotFound>(page, context);
}

/// <summary>Renders home pages from components with a strongly typed Page parameter.</summary>
public abstract class EmRazorHomeSkin<THome, TNotFound> : IEmHomeSkin
   where THome : IComponent where TNotFound : IComponent {
   /// <summary>Embedded icon loaded during startup, or null.</summary>
   protected virtual EmbeddedFile? Favicon => null;
   EmbeddedFile? IEmHomeSkin.Favicon => Favicon;
   /// <inheritdoc />
   public virtual string RenderPlainText(HomePage page) => $"Welcome to {page.ApplicationName}";
   /// <inheritdoc />
   public Task RenderHomeAsync(HomePage page, HttpContext context) => SkinRenderer.RenderAsync<THome>(page, context);
   /// <inheritdoc />
   public Task RenderNotFoundAsync(SiteNotFoundPage page, HttpContext context) => SkinRenderer.RenderAsync<TNotFound>(page, context);
}

internal static class SkinRenderer {
   internal static async Task RenderAsync<T>(object page, HttpContext context) where T : IComponent {
      await using var renderer = new HtmlRenderer(context.RequestServices,
         context.RequestServices.GetRequiredService<ILoggerFactory>());
      var html = await renderer.Dispatcher.InvokeAsync(async () => {
         var output = await renderer.RenderComponentAsync(typeof(T), ParameterView.FromDictionary(new Dictionary<string, object?> { ["Page"] = page }));
         return output.ToHtmlString();
      });
      await context.Response.WriteAsync(html, context.RequestAborted);
   }
}
