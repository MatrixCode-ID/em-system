using Microsoft.AspNetCore.Http;

namespace Em.Api.Core;

/// <summary>Presentation of public CDN pages, independent of file download handling.</summary>
public interface IEmCdnSkin {
   /// <summary>Renders a directory listing for a browser.</summary>
   Task RenderDirectoryAsync(CdnDirectoryPage page, HttpContext context);
   /// <summary>Renders a not-found page without revealing its cause.</summary>
   Task RenderNotFoundAsync(CdnNotFoundPage page, HttpContext context);
   /// <summary>Embedded assets copied at startup, or null for a self-contained skin.</summary>
   EmSkinAssets? Assets { get; }
}

/// <summary>Presentation of the root page, site not-found page, and favicon.</summary>
public interface IEmHomeSkin {
   /// <summary>Renders the browser home page.</summary>
   Task RenderHomeAsync(HomePage page, HttpContext context);
   /// <summary>Renders an unknown site path for a browser.</summary>
   Task RenderNotFoundAsync(SiteNotFoundPage page, HttpContext context);
   /// <summary>Returns the home response for non-browser clients.</summary>
   string RenderPlainText(HomePage page);
   /// <summary>Embedded favicon, or null to return 404.</summary>
   EmbeddedFile? Favicon { get; }
}
