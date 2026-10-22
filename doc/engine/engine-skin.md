# Home and CDN skins

Skins change presentation without changing CDN file transfers. The engine owns `/`, `/favicon.ico`,
site not-found responses and `/cdn` pages. File downloads continue through ASP.NET Core static files,
with the existing Range, If-Range, ETag, HEAD, 206/416 handling and no download rate limit.

## Register a skin

```csharp
builder.ApplicationName = "Sample Business Suite";
builder.UseHomeSkin<SampleHomeSkin>();
builder.UseCdnSkin<SampleCdnSkin>();
```

Each method may be called once; a second call throws `InvalidOperationException`. The two registrations
are independent, singleton services activated by DI, and may receive other registered services in their
constructors. Registering a CDN skin does not enable CDN; use `EnableCdn` or managed storage settings.
There are no factory or Razor-specific registration overloads. `ApplicationName` defaults to the entry
assembly name. `HostVersion` is the **host entry assembly** informational version, with build metadata
removed and a `v` prefix, or `dev` for `0.0.0-dev`, following [Product version](build.md#product-version).

## Contracts and page data

All types are in `Em.Api.Core`:

- `IEmCdnSkin`: `RenderDirectoryAsync(CdnDirectoryPage, HttpContext)`,
  `RenderNotFoundAsync(CdnNotFoundPage, HttpContext)` and optional `EmSkinAssets Assets`.
- `IEmHomeSkin`: `RenderHomeAsync(HomePage, HttpContext)`,
  `RenderNotFoundAsync(SiteNotFoundPage, HttpContext)`, `RenderPlainText(HomePage)` and optional
  `EmbeddedFile Favicon`.
- `CdnDirectoryPage`: request path, breadcrumbs, parent flag, sorted `Folders` and `Files`, host identity,
  maximum upload bytes, folder/file counts and `Asset(path)`. Limits and counts are informational.
  `CdnPageEntry` supplies original name, escaped relative href, directory flag, optional length and UTC
  modification time. `CdnBreadcrumb` supplies display label and escaped href.
- `CdnNotFoundPage`: request path, host identity and `Asset(path)`. The same page covers missing paths,
  host-disabled CDN and the runtime service switch; it does not disclose which condition applied.
- `HomePage`: application name, host version and `CdnUrl` (`/cdn/` only while CDN is available).
- `SiteNotFoundPage`: request path and host identity.

Render names, labels and request paths as encoded text. URLs already have escaped path segments; Razor
attribute encoding must still apply. Never interpret names as markup. Models expose read-only collections.
`Asset(path)` returns a versioned `/cdn/.skin/<path>?v=<sha256>` URL, or null for an unknown/unavailable
asset. Only CDN page models have this helper. Home uses inline SVG or small bitmap data URIs.

## Razor components

Use a Web/Razor SDK project for components. No MVC, `AddRazorComponents` or `MapRazorComponents` is
needed. Each component declares a parameter named `Page` of the matching model type.

`DownloadView.razor`:

```razor
@using Em.Api.Core
@using Microsoft.AspNetCore.Components
<!DOCTYPE html>
<html lang="en"><head><title>Downloads</title>
@if (Page.Asset("site.css") is { } css) { <link rel="stylesheet" href="@css" /> }
</head><body>
@foreach (var entry in Page.Folders.Concat(Page.Files)) {
    <p><a href="@entry.Href">@entry.Name</a></p>
}
</body></html>
@code {
    [Parameter] public CdnDirectoryPage Page { get; set; } = null!;
}
```

`DownloadNotFound.razor` uses `CdnNotFoundPage`; include minimal inline styles because CDN assets are
unavailable while its service is off. A skin class connects both components:

```csharp
public sealed class SampleCdnSkin : EmRazorCdnSkin<DownloadView, DownloadNotFound> {
    protected override string? AssetPrefix => "cdn-skin/";
}
```

Embed its resources with explicit logical paths, for example:

```xml
<ItemGroup>
  <EmbeddedResource Include="Skin/Assets/site.css" LogicalName="cdn-skin/site.css" />
  <EmbeddedResource Include="Skin/Assets/logo.svg" LogicalName="cdn-skin/logo.svg" />
</ItemGroup>
```

`EmRazorCdnSkin` takes the resource assembly from `TDirectory`. Resource separators are normalized from
backslashes to `/`; invalid paths and duplicate normalized names fail startup. A declared prefix must
end in `/` and match at least one resource. `EmSkinAssets` can also be used directly with an assembly
and prefix by an interface implementation.

For home, use `EmRazorHomeSkin<WelcomeView, MissingView>`, with `HomePage` and `SiteNotFoundPage`
parameters respectively. Override `RenderPlainText` when needed (base default: `Welcome to <name>`),
and the protected `Favicon` property:

```csharp
public sealed class SampleHomeSkin : EmRazorHomeSkin<WelcomeView, MissingView> {
    protected override EmbeddedFile? Favicon => EmbeddedFile.From<SampleHomeSkin>("home/favicon.ico");
}
```

`EmbeddedFile.From<T>(logicalName)` validates and reads a resource from the type's assembly;
`ReadText()`, `ReadBytes()` and `ContentType` expose its contents and inferred MIME type. Missing
resources throw a descriptive exception. Load inline logos in the skin constructor/static initialization
forced by that constructor so missing resources fail startup. Favicon is read once at startup, retained
in memory and served with a one-hour public cache, independently of CDN. Null means 404.

Razor uses a shared `HtmlRenderer` helper with its dispatcher and DI logger factory. The engine buffers
skin output and assigns status, UTF-8 content type and byte content length before writing the response.

## Browser negotiation

| Request | Response |
| --- | --- |
| GET `/` accepting `text/html` with nonzero quality | Home HTML, 200 |
| Other GET `/`, HEAD `/` | Plain home text, 200; HEAD has no body |
| GET directory accepting HTML | CDN listing HTML, 200 |
| Non-HTML GET or HEAD directory | 404 without body (normal directory middleware redirects can still apply) |
| GET unknown site/CDN path accepting HTML | Corresponding skin not-found HTML, 404 |
| Other unknown site/CDN requests | 404 without body |
| GET/HEAD CDN file | Existing static-file behavior, irrespective of Accept |

`*/*` alone does not opt in to HTML. HEAD never renders a page. `/favicon.ico` uses GET/HEAD and does
not use HTML negotiation. `/` and `/favicon.ico` cannot be replaced by `AddPublicEndpoint`.

## Reserved `.skin` lifecycle

When CDN and its service are enabled at startup, the engine clears `files/.skin` and writes the embedded
CDN assets directly before accepting requests. No temporary folder, lock or marker file is used. A skin
without assets removes the directory. Failures to validate resources, delete or write abort startup.
Only the exact resolved directory under `files/` can be deleted; symlinks are refused. CDN disabled by
host or service leaves disk untouched. A service toggle during operation does not rewrite assets;
restart with the service enabled after changing skin resources or removing a registration.

`.skin` at the CDN root is always reserved, case-insensitively. CDN Manager and release actions reject
management paths targeting it with 400, including creation, uploads, moves, deletion, extraction,
managed-folder operations and restore. It is excluded from listings, tree/count/storage totals and
root archive input. Nested ordinary folders named `.skin` remain valid outside the root reservation.
Public `.skin` directory browsing returns not-found, including subdirectories; known files remain
public at `/cdn/.skin/<path>`. This directory belongs only to the CDN skin. Shared multi-instance CDN
storage is unsupported. No database migration or new metadata key is required.

## Defaults and upgrade note

Without registration, `DefaultCdnSkin` preserves the existing listing design and escapes names using
`HtmlEncoder`; it has no assets and adds a matching minimal CDN not-found page. `DefaultHomeSkin`
shows the existing embedded Em SVG, **Welcome To Matrix-Code API System**, the host version and Em
favicon. Both defaults support system light/dark preference and phone widths. Their source in
`src/backend/Em.Api.Core/Api/Skin/` provides complete self-contained skin examples.

**Behavior change:** the former welcome fallback returned 200 for unknown extensionless paths.
Unknown paths, including paths with extensions, now return **404**; browsers receive the home skin's
site not-found page. The root welcome is now explicit. Update monitoring/proxy assumptions that
expected the old catch-all 200. No release note file is added by this implementation.
