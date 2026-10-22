using System.IO.Compression;
using System.Text;
using Em.Api.Core;
using Em.Api.Shared;
using Em.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Em.Api.Core.Tests;

public sealed class SkinTests : IDisposable {
   private readonly string _root = Path.Combine(Path.GetTempPath(), "em-skin-test-" + Guid.NewGuid().ToString("N"));
   private CdnStore Store() => CdnStore.Create(_root, _root, 200 * 1024 * 1024);
   private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
   private static DefaultHttpContext Context(string path = "/", string method = "GET", string accept = "text/html") {
      var context = new DefaultHttpContext();
      context.Request.Path = path;
      context.Request.Method = method;
      context.Request.Headers.Accept = accept;
      context.Response.Body = new MemoryStream();
      context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
      return context;
   }
   private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
   private static SkinPages Pages(IEmHomeSkin? home = null, IEmCdnSkin? cdn = null) {
      var pages = new SkinPages(cdn ?? new DefaultCdnSkin(), home ?? new DefaultHomeSkin(), "Sample API", "v2.3.4");
      pages.Initialize();
      return pages;
   }
   [Theory]
   [InlineData("GET", "text/html", true)]
   [InlineData("GET", "application/json, text/html;q=0.8", true)]
   [InlineData("GET", "text/html;q=0", false)]
   [InlineData("GET", "*/*", false)]
   [InlineData("HEAD", "text/html", false)]
   [InlineData("POST", "text/html", false)]
   public void BrowserNegotiation(string method, string accept, bool expected) =>
      Assert.Equal(expected, SkinPages.IsBrowser(Context(method: method, accept: accept).Request));

   [Fact]
   public async Task HomePlainTextHeadAndNotFound() {
      var pages = Pages();
      var html = Context(); await pages.HomeAsync(html, true);
      Assert.Equal(200, html.Response.StatusCode);
      Assert.Contains("<svg", Body(html)); Assert.Contains("v2.3.4", Body(html)); Assert.Contains("Open downloads", Body(html));
      Assert.Equal(((MemoryStream)html.Response.Body).Length, html.Response.ContentLength);
      var disabled = Context(); await pages.HomeAsync(disabled, false); Assert.DoesNotContain("Open downloads", Body(disabled));
      var plain = Context(accept: "application/json"); await pages.HomeAsync(plain, true);
      Assert.Equal("Welcome To Matrix-Code API System", Body(plain));
      var head = Context(method: "HEAD"); await pages.HomeAsync(head, true);
      Assert.Equal("", Body(head)); Assert.True(head.Response.ContentLength > 0);
      var missing = Context("/unknown.ext"); await pages.SiteNotFoundAsync(missing);
      Assert.Equal(404, missing.Response.StatusCode); Assert.Contains("Page not found", Body(missing));
      var nonBrowser = Context("/unknown", accept: ""); await pages.SiteNotFoundAsync(nonBrowser);
      Assert.Equal(404, nonBrowser.Response.StatusCode); Assert.Equal("", Body(nonBrowser));
   }
   [Fact]
   public async Task FaviconPresentAbsentAndHead() {
      var pages = Pages(); var icon = Context("/favicon.ico"); await pages.FaviconAsync(icon);
      Assert.Equal(200, icon.Response.StatusCode); Assert.True(icon.Response.ContentLength > 0);
      Assert.Equal("image/x-icon", icon.Response.ContentType);
      var head = Context("/favicon.ico", "HEAD"); await pages.FaviconAsync(head); Assert.Equal("", Body(head));
      var absent = Context("/favicon.ico"); await Pages(new TestHome()).FaviconAsync(absent); Assert.Equal(404, absent.Response.StatusCode);
   }
   [Fact]
   public async Task DirectoryPreservesEscapingSortingAndParent() {
      var store = Store();
      File.WriteAllText(store.ResolveInside("z & file.txt"), "ok"); Directory.CreateDirectory(store.ResolveInside("A folder"));
      var context = Context("/cdn/");
      await new CdnDirectoryFormatter(Pages(), store).GenerateContentAsync(context, new PhysicalFileProvider(store.RootPath).GetDirectoryContents("/"));
      var html = Body(context);
      Assert.Contains("z &amp; file.txt", html); Assert.Contains("z%20%26%20file.txt", html);
      Assert.True(html.IndexOf("A folder", StringComparison.Ordinal) < html.IndexOf("z &amp; file.txt", StringComparison.Ordinal));
      Assert.DoesNotContain("href=\"../\"", html);
      var nested = Context("/cdn/A folder/"); await new CdnDirectoryFormatter(Pages(), store).GenerateContentAsync(nested, []);
      Assert.Contains("href=\"../\"", Body(nested)); Assert.Contains("This folder is empty.", Body(nested));
      var head = Context("/cdn/", "HEAD"); await new CdnDirectoryFormatter(Pages(), store).GenerateContentAsync(head, []);
      Assert.Equal(404, head.Response.StatusCode); Assert.Equal("", Body(head));
   }
   [Fact]
   public async Task RazorBaseClassesRenderEncodedParameters() {
      var pages = Pages(new TestHome(), new TestCdn());
      var home = Context(); await pages.HomeAsync(home, false); Assert.Contains("Sample API", Body(home));
      var missing = Context("/cdn/<bad>"); await pages.CdnNotFoundAsync(missing, false);
      Assert.Contains("&lt;bad&gt;", Body(missing)); Assert.Equal(404, missing.Response.StatusCode);
      var site = Context("/<bad>"); await pages.SiteNotFoundAsync(site); Assert.Contains("&lt;bad&gt;", Body(site));
      var directory = Context("/cdn/"); await new CdnDirectoryFormatter(pages, Store()).GenerateContentAsync(directory, []);
      Assert.Contains("Sample API", Body(directory));
   }
   [Fact]
   public void RegistrationsAreIndependentAndRejectDuplicates() {
      var services = new ServiceCollection().AddLogging(); var builder = new EmAppBuilder { Services = services };
      builder.UseCdnSkin<TestCdn>(); builder.UseHomeSkin<TestHome>(); builder.RegisterDefaultSkins();
      Assert.Throws<InvalidOperationException>(() => builder.UseCdnSkin<TestCdn>());
      Assert.Throws<InvalidOperationException>(() => builder.UseHomeSkin<TestHome>());
      using var provider = services.BuildServiceProvider();
      Assert.IsType<TestCdn>(provider.GetRequiredService<IEmCdnSkin>());
      Assert.IsType<TestHome>(provider.GetRequiredService<IEmHomeSkin>());
      Assert.Throws<InvalidOperationException>(() => builder.AddPublicEndpoint("/favicon.ico/child", _ => Task.CompletedTask));
      Assert.Throws<InvalidOperationException>(() => EmbeddedFile.From<SkinTests>("missing.svg"));
   }
   private static EmSkinAssets Assets => new(typeof(SkinTests).Assembly, "skin-test/");
   [Fact]
   public async Task AssetsAreReplacedHiddenReservedAndExcludedFromCountsAndArchives() {
      var store = Store(); var urls = store.PrepareSkinAssets(Assets);
      var css = store.ResolveInside(".skin/site.css", allowSkin: true);
      Assert.Equal("body{color:red}", File.ReadAllText(css).Trim()); Assert.Contains("?v=", urls["site.css"]);
      File.WriteAllText(css, "old"); File.WriteAllText(store.ResolveInside(".skin/stale", true), "old");
      store.PrepareSkinAssets(Assets); Assert.DoesNotContain("old", File.ReadAllText(css)); Assert.False(File.Exists(store.ResolveInside(".skin/stale", true)));
      Assert.Empty(store.List(null).Entries); Assert.Empty(store.Tree("")); Assert.Equal(0, store.CountInside("").Files);
      Assert.Equal(0, store.StorageSize(Token).TotalBytes);
      var provider = new CdnFileProvider(store);
      Assert.Empty(provider.GetDirectoryContents("/")); Assert.False(provider.GetDirectoryContents("/.skin/").Exists);
      Assert.False(provider.GetFileInfo("/.skin").Exists); Assert.True(provider.GetFileInfo("/.skin/site.css").Exists);
      using (var reader = new StreamReader(provider.GetFileInfo("/.skin/site.css").CreateReadStream())) Assert.Contains("color:red", reader.ReadToEnd());
      Assert.Throws<ActionException>(() => store.List(".skin")); Assert.Throws<ActionException>(() => store.CreateFolder(null, ".skin"));
      await Assert.ThrowsAsync<ActionException>(() => store.UploadAsync(null, ".skin", new MemoryStream(), true, Token));
      Assert.Throws<ActionException>(() => store.Delete(".skin")); Assert.Throws<ActionException>(() => store.Move(".skin", "", false));
      Assert.Throws<ActionException>(() => store.Manage(".skin", "org.sample"));
      Directory.CreateDirectory(store.ResolveInside("normal")); Directory.CreateDirectory(store.ResolveInside("normal/.skin"));
      Assert.Throws<ActionException>(() => store.Move("normal/.skin", "", false));
      File.WriteAllText(store.ResolveInside("visible.txt"), "ok");
      await store.ArchiveAsync("", store.List("").Entries.Select(e => e.Name).ToArray(), "root.zip", false, (_, _) => { }, Token);
      using (var zip = ZipFile.OpenRead(store.ResolveInside("root.zip"))) {
         Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith(".skin/", StringComparison.OrdinalIgnoreCase));
         Assert.Contains(zip.Entries, e => e.FullName == "normal/.skin/");
      }
      using (var zip = ZipFile.Open(store.ResolveInside("attack.zip"), ZipArchiveMode.Create)) zip.CreateEntry(".skin/empty/");
      Assert.Throws<ActionException>(() => store.ReadExtractPlan("attack.zip"));
      Assert.Empty(store.RecycleItems());
      store.PrepareSkinAssets(null); Assert.False(Directory.Exists(Path.GetDirectoryName(css)));
   }
   [Fact]
   public void StartupFailureAndDisabledStore() {
      var store = Store(); File.WriteAllText(store.ResolveInside(".skin", true), "occupied");
      Assert.Throws<InvalidOperationException>(() => store.PrepareSkinAssets(Assets));
      Assert.Empty(CdnStore.Disabled.PrepareSkinAssets(Assets));
      Assert.Throws<InvalidOperationException>(() => store.PrepareSkinAssets(new(typeof(SkinTests).Assembly, "missing/")));
      Assert.Throws<ActionException>(() => store.ResolveInside(".SKIN/site.css"));
   }
   [Fact]
   public async Task CdnUnavailableHasNoAssetsAndNoReasonAndNonBrowserIsEmpty() {
      var pages = Pages(cdn: new AssetProbe());
      pages.AssetUrls = new Dictionary<string, string> { ["site.css"] = "/cdn/.skin/site.css?v=1" };
      var missing = Context("/cdn/missing"); await pages.CdnNotFoundAsync(missing, true); Assert.Contains("site.css", Body(missing));
      var disabled = Context("/cdn/missing"); await pages.CdnNotFoundAsync(disabled, false); Assert.Equal("unavailable", Body(disabled));
      var head = Context("/cdn/missing", "HEAD"); await pages.CdnNotFoundAsync(head, true); Assert.Equal(404, head.Response.StatusCode); Assert.Equal("", Body(head));
   }
   public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
   public sealed class TestHome : EmRazorHomeSkin<HomeComponent, SiteComponent> { }
   public sealed class TestCdn : EmRazorCdnSkin<DirectoryComponent, CdnComponent> { }
   public sealed class HomeComponent : ComponentBase {
      [Parameter] public HomePage Page { get; set; } = null!;
      protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Page.ApplicationName);
   }
   public sealed class SiteComponent : ComponentBase {
      [Parameter] public SiteNotFoundPage Page { get; set; } = null!;
      protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Page.RequestPath);
   }
   public sealed class DirectoryComponent : ComponentBase {
      [Parameter] public CdnDirectoryPage Page { get; set; } = null!;
      protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Page.ApplicationName);
   }
   public sealed class CdnComponent : ComponentBase {
      [Parameter] public CdnNotFoundPage Page { get; set; } = null!;
      protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Page.RequestPath);
   }
   private sealed class AssetProbe : IEmCdnSkin {
      public EmSkinAssets? Assets => null;
      public Task RenderDirectoryAsync(CdnDirectoryPage page, HttpContext context) => Task.CompletedTask;
      public Task RenderNotFoundAsync(CdnNotFoundPage page, HttpContext context) => context.Response.WriteAsync(page.Asset("site.css") ?? "unavailable");
   }
}
