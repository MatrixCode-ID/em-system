using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using System.Xml.Linq;
using Em.Api.Core;
using Em.Shared;
using Em.Api.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NuGet.Versioning;
namespace Em.Api.Core.NuPak;

/// <summary>The public NuGet protocol endpoint of NuPak: service index, search, download, and push.</summary>
public static class NuPakEndpoint
{
   private static Dictionary<string, object?> Json(params (string Key, object? Value)[] fields) => fields.ToDictionary(f => f.Key, f => f.Value);
   /// <summary>Handles one request under the NuPak path.</summary>
   public static async Task HandleAsync(HttpContext ctx) {
      var db = ctx.RequestServices.GetRequiredService<NuPakDbContext>();
      var store = ctx.RequestServices.GetRequiredService<NuPakStore>();
      NuPakActor? actor = null;
      ta_NuPakFeed? feed=null;
      var write = HttpMethods.IsPut(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method);
      string? auditPackage = null, auditVersion = null;
      try {
         var rawPath=ctx.Request.Path.Value??"";
         var segments=rawPath.Split('/',StringSplitOptions.None);
         if(segments.Length<4||segments[0]!=""||segments[2] is not ("v2" or "v3")) {ctx.Response.StatusCode=404;return;}
         string slug;
         try {slug=NuPakStore.Slug(segments[1]);} catch(ActionException) {ctx.Response.StatusCode=404;return;}
         var rawTarget=ctx.Features.Get<IHttpRequestFeature>()?.RawTarget;
         if(rawTarget?.Split('?')[0].Contains('%')==true) {ctx.Response.StatusCode=404;return;}
         feed=await db.Feeds.SingleOrDefaultAsync(f=>f.cNuPakFeedSlug==slug,ctx.RequestAborted);
         if(feed is null||!feed.cNuPakFeedEnabled) {ctx.Response.StatusCode=404;return;}
         var settings = await ctx.RequestServices.GetRequiredService<NuPakSettings>().ReadAsync(db, ctx.RequestAborted);
         if (!settings.Enabled) { ctx.Response.StatusCode = 404; return; }
         var read = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method);
         if (!read && !write) { ctx.Response.StatusCode = 404; return; }
         var identities = ctx.RequestServices.GetRequiredService<RobotContext>();
         var credentials = ctx.Request.Headers.ContainsKey("Authorization") || ctx.Request.Headers.ContainsKey("X-NuGet-ApiKey");
         ta_Robot? robot = null;
         if (ctx.Request.Headers.ContainsKey("Authorization")) robot = await RobotAuth.AuthenticateAsync(ctx, identities);
         else if (ctx.Request.Headers.TryGetValue("X-NuGet-ApiKey", out var key)) robot = await RobotAuth.AuthenticateAsync(key.ToString(), identities, ctx.RequestAborted);
         if (robot is null && (credentials || !feed.cNuPakFeedAnonymousRead || !read)) {
            ctx.Response.StatusCode = 401; ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Em NuGet\""; return;
         }
         if (robot is not null) actor = new("Robot", robot.cRobotId, robot.cRobotName, ctx.Connection.RemoteIpAddress?.ToString());
         var path = rawPath[(slug.Length+1)..].TrimEnd('/');
         var address = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}/{feed.cNuPakFeedSlug}";
         if (read && path == "/v3/index.json") {
            var resources = new[] {
               ("PackageBaseAddress/3.0.0", "/v3/flatcontainer/"), ("PackagePublish/2.0.0", "/v2/package"),
               ("SearchQueryService", "/v3/search"), ("SearchQueryService/3.0.0-beta", "/v3/search"), ("SearchQueryService/3.0.0-rc", "/v3/search"), ("SearchQueryService/3.5.0", "/v3/search"),
               ("RegistrationsBaseUrl", "/v3/registration/"), ("RegistrationsBaseUrl/3.6.0", "/v3/registration/") };
            await Reply(ctx, Json(("version", "3.0.0"), ("resources", resources.Select(r => Json(("@id", address + r.Item2), ("@type", r.Item1)))))); return;
         }
         if (HttpMethods.IsPut(ctx.Request.Method) && path == "/v2/package") {
            if (!MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var type) || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
               throw new ActionException("A multipart NuGet package is required.", 400);
            var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128) throw new ActionException("Invalid multipart boundary.", 400);
            if (ctx.Request.ContentLength > store.MaxBytes + 1024 * 1024) throw new ActionException("Package exceeds upload limit.", 413);
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = store.MaxBytes + 1024 * 1024;
            var reader = new MultipartReader(boundary, ctx.Request.Body) { HeadersLengthLimit = 16384 };
            var section = await reader.ReadNextSectionAsync(ctx.RequestAborted) ?? throw new ActionException("Missing package.", 400);
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !(HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value ?? HeaderUtilities.RemoveQuotes(disposition.FileName).Value ?? "").EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
               throw new ActionException("A .nupkg file is required.", 400);
            var upload = await store.ReceiveAsync(section.Body, ctx.RequestAborted);
            auditPackage = upload.Id; auditVersion = upload.Version;
            try {
               if (await reader.ReadNextSectionAsync(ctx.RequestAborted) is not null) throw new ActionException("Only one file is allowed.", 400);
               await NuPakOperations.PushAsync(db, store, feed, upload, robot!.cRobotId, actor!);
            } finally { File.Delete(upload.Path); }
            ctx.Response.StatusCode = 201; return;
         }
         var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
         if (HttpMethods.IsDelete(ctx.Request.Method) && parts is ["v2", "package", var deleteId, var deleteVersion]) {
            auditPackage = NuPakStore.Id(deleteId); auditVersion = NuPakStore.Version(deleteVersion);
            var package = await db.Packages.SingleOrDefaultAsync(p => p.cNuPakFeedId==feed.cNuPakFeedId && p.cNuPakPackageName == auditPackage, ctx.RequestAborted)
               ?? throw new ActionException("Package not found.", 404);
            if (!await db.Grants.AnyAsync(g => g.cNuPakPrefixId == package.cNuPakPrefixId && g.cRobotId == robot!.cRobotId && g.cNuPakPrefixRobotAccess == "W", ctx.RequestAborted))
               throw new ActionException("Write access is required.", 403);
            var version = await db.Versions.SingleOrDefaultAsync(v => v.cNuPakPackageId == package.cNuPakPackageId && v.cNuPakVersionNumber == auditVersion && v.cNuPakVersionState == 1, ctx.RequestAborted)
               ?? throw new ActionException("Version not found.", 404);
            await NuPakOperations.ChangeStateAsync(db, store, feed.cNuPakFeedId,version.cNuPakVersionId, false, actor!); ctx.Response.StatusCode = 204; return;
         }
         var readable = db.Packages.Where(p => p.cNuPakFeedId==feed.cNuPakFeedId && p.cNuPakPackageState == 1);
         if (robot is not null) {
            var robotId = robot.cRobotId;
            readable = readable.Where(p => db.Grants.Any(g => g.cNuPakPrefixId == p.cNuPakPrefixId && g.cRobotId == robotId &&
               (g.cNuPakPrefixRobotAccess == "R" || g.cNuPakPrefixRobotAccess == "W")));
         }
         if (read && path == "/v3/search") {
            var q = ctx.Request.Query["q"].ToString();
            if (q.Length > 1000) throw new ActionException("Search query is too long.", 400);
            var requestedType = ctx.Request.Query["packageType"].ToString();
            var prerelease = bool.TryParse(ctx.Request.Query["prerelease"], out var pre) && pre;
            var semver2 = NuGetVersion.TryParse(ctx.Request.Query["semVerLevel"], out var level) && level >= new NuGetVersion(2, 0, 0);
            var candidates = from p in readable join v in db.Versions on p.cNuPakPackageId equals v.cNuPakPackageId
               where v.cNuPakVersionState == 1 && (prerelease || !v.cNuPakVersionPrerelease)
               select new { p.cNuPakPackageId, p.cNuPakPackageName, Version = new ta_NuPakVersion {
                  cNuPakVersionNumber=v.cNuPakVersionNumber, cNuPakVersionOriginal=v.cNuPakVersionOriginal,
                  cNuPakVersionTitle=v.cNuPakVersionTitle, cNuPakVersionDescription=v.cNuPakVersionDescription,
                  cNuPakVersionAuthors=v.cNuPakVersionAuthors, cNuPakVersionTags=v.cNuPakVersionTags,
                  cNuPakVersionNuspec=requestedType=="" ? "" : v.cNuPakVersionNuspec } };
            if (!string.IsNullOrWhiteSpace(q)) candidates = candidates.Where(r => r.cNuPakPackageName.Contains(q) ||
               (r.Version.cNuPakVersionTitle != null && r.Version.cNuPakVersionTitle.Contains(q)) ||
               (r.Version.cNuPakVersionDescription != null && r.Version.cNuPakVersionDescription.Contains(q)) ||
               (r.Version.cNuPakVersionTags != null && r.Version.cNuPakVersionTags.Contains(q)));
            var found = (await candidates.ToListAsync(ctx.RequestAborted)).Where(r => (semver2 || !IsSemVer2(r.Version)) && (requestedType=="" || HasPackageType(r.Version, requestedType)))
               .GroupBy(r => r.cNuPakPackageId).OrderBy(g => g.First().cNuPakPackageName).ToArray();
            var data = found.Skip(Number(ctx, "skip", 0, int.MaxValue)).Take(Number(ctx, "take", 20, 100)).Select(g => {
               var versions = g.Select(r => r.Version).OrderBy(v => NuGetVersion.Parse(v.cNuPakVersionNumber)).ToArray();
               var latest = versions[^1]; var id = g.First().cNuPakPackageName; var lower = id.ToLowerInvariant();
               return Json(("@id", address + $"/v3/registration/{lower}/index.json"), ("id", id), ("version", latest.cNuPakVersionNumber),
                  ("description", latest.cNuPakVersionDescription ?? ""), ("title", latest.cNuPakVersionTitle ?? id),
                  ("authors", (latest.cNuPakVersionAuthors ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                  ("tags", Tags(latest)), ("totalDownloads", 0), ("verified", false),
                  ("versions", versions.Select(v => Json(("version", v.cNuPakVersionNumber), ("@id", address + $"/v3/registration/{lower}/{v.cNuPakVersionNumber}.json"), ("downloads", 0)))));
            });
            await Reply(ctx, Json(("totalHits", found.Length), ("data", data))); return;
         }
         if (read && parts.Length >= 3 && parts[0] == "v3" && parts[1] is "flatcontainer" or "registration") {
            var id = NuPakStore.Id(parts[2]);
            var package = await readable.SingleOrDefaultAsync(p => p.cNuPakPackageName == id, ctx.RequestAborted);
            if (package is null) { ctx.Response.StatusCode = 404; return; }
            var query = db.Versions.Where(v => v.cNuPakPackageId == package.cNuPakPackageId && v.cNuPakVersionState == 1);
            if (parts[1] == "flatcontainer") {
               if (parts.Length == 4 && parts[3] == "index.json") {
                  var numbers=await query.Select(v=>v.cNuPakVersionNumber).ToArrayAsync(ctx.RequestAborted);
                  if(numbers.Length==0) {ctx.Response.StatusCode=404;return;}
                  await Reply(ctx,new {versions=numbers.OrderBy(v=>NuGetVersion.Parse(v))});return;
               }
               if (parts.Length == 5) {
                  var normalized = NuPakStore.Version(parts[3]);
                  var version = await query.SingleOrDefaultAsync(v=>v.cNuPakVersionNumber==normalized,ctx.RequestAborted);
                  if (version is null) { ctx.Response.StatusCode = 404; return; }
                  if (parts[4].Equals($"{id}.{normalized}.nupkg", StringComparison.OrdinalIgnoreCase)) {
                     var file = store.PackagePath(feed.cNuPakFeedId,id, normalized);
                     if (!File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
                     await Results.File(file, "application/octet-stream", enableRangeProcessing: true,
                        lastModified: new DateTimeOffset(DateTime.SpecifyKind(version.datestamp, DateTimeKind.Utc)),
                        entityTag: new EntityTagHeaderValue('"' + version.cNuPakVersionHash + '"')).ExecuteAsync(ctx); return;
                  }
                  if (parts[4].Equals(id + ".nuspec", StringComparison.OrdinalIgnoreCase)) {
                     ctx.Response.ContentType = "application/xml";
                     if (!HttpMethods.IsHead(ctx.Request.Method)) await ctx.Response.WriteAsync(version.cNuPakVersionNuspec, ctx.RequestAborted); return;
                  }
               }
            } else if (parts.Length == 4) {
               var versions=(await query.ToArrayAsync(ctx.RequestAborted)).OrderBy(v=>NuGetVersion.Parse(v.cNuPakVersionNumber)).ToArray();
               if(versions.Length==0) {ctx.Response.StatusCode=404;return;}
               var index = address + $"/v3/registration/{id}/index.json";
               if (parts[3] == "index.json") {
                  var leaves = versions.Select(v => Leaf(address, package.cNuPakPackageName, v)).ToArray();
                  await Reply(ctx, Json(("@id", index), ("count", 1), ("items", new[] {
                     Json(("@id", index + "#page"), ("parent",index), ("count", versions.Length), ("lower", versions[0].cNuPakVersionNumber),
                        ("upper", versions[^1].cNuPakVersionNumber), ("items", leaves)) }))); return;
               }
               if (parts[3].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                  var normalized = NuPakStore.Version(parts[3][..^5]);
                  var version = versions.SingleOrDefault(v => v.cNuPakVersionNumber == normalized);
                  if (version is not null) {
                     var leaf=(Dictionary<string,object?>)Leaf(address,package.cNuPakPackageName,version);
                     // Inline catalogEntry belongs to a page leaf. The standalone leaf omits the
                     // optional catalog URL because this feed does not expose a catalog resource.
                     leaf.Remove("catalogEntry");await Reply(ctx,leaf);return;
                  }
               }
            }
         }
         ctx.Response.StatusCode = 404;
      } catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
      catch (Exception ex) when (ex is ActionException or BadHttpRequestException or InvalidDataException) {
         var code = ex is ActionException action ? action.StatusCode : ex is BadHttpRequestException bad ? bad.StatusCode : 400;
         if (!ctx.Response.HasStarted) ctx.Response.StatusCode = code;
         if (write && actor is not null && code is 400 or 403 or 409 or 413) {
            try {
               db.ChangeTracker.Clear();
               NuPakOperations.Audit(db, actor, HttpMethods.IsPut(ctx.Request.Method) ? "Push" : "Recycle", auditPackage, auditVersion,
                  code == 403 ? "Denied" : "Failed", ex is ActionException ? ex.Message : "Invalid package request.",feed:feed);
               await db.SaveChangesAsync();
            } catch (Exception auditError) {
               ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NuPakEndpoint").LogError(auditError, "Failed to persist NuGet denial audit");
               if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 500;
            }
         }
      } catch (Exception ex) {
         ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NuPakEndpoint").LogError(ex, "NuGet request failed");
         if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 500;
         if (write && actor is not null) {
            try {
               db.ChangeTracker.Clear();
               NuPakOperations.Audit(db, actor, HttpMethods.IsPut(ctx.Request.Method) ? "Push" : "Recycle", auditPackage, auditVersion, "Failed", "Unexpected operation failure. See server logs.",feed:feed);
               await db.SaveChangesAsync();
            } catch (Exception auditError) { ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NuPakEndpoint").LogError(auditError, "Failed to persist NuGet failure audit"); }
         }
      }
   }
   private static bool HasPackageType(ta_NuPakVersion v, string name) {
      var types=NuPakStore.ParseMetadata(v.cNuPakVersionNuspec).Elements().FirstOrDefault(e=>e.Name.LocalName=="packageTypes");
      if(types is null) return name.Equals("Dependency",StringComparison.OrdinalIgnoreCase);
      return types.Elements().Any(e=>e.Name.LocalName=="packageType" && name.Equals((string?)e.Attribute("name"),StringComparison.OrdinalIgnoreCase));
   }
   private static bool IsSemVer2(ta_NuPakVersion v) => v.cNuPakVersionOriginal.Contains('+') || NuGetVersion.Parse(v.cNuPakVersionNumber).Release.Contains('.');
   private static int Number(HttpContext ctx, string key, int fallback, int max) =>
      int.TryParse(ctx.Request.Query[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, max) : fallback;
   private static string[] Tags(ta_NuPakVersion v) => (v.cNuPakVersionTags ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
   private static object Leaf(string address, string id, ta_NuPakVersion v) {
      var lower = id.ToLowerInvariant(); var version = v.cNuPakVersionNumber;
      var content = address + $"/v3/flatcontainer/{lower}/{version}/{lower}.{version}.nupkg";
      var metadata = NuPakStore.ParseMetadata(v.cNuPakVersionNuspec);
      var dependencies = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");
      static object[] Deps(IEnumerable<XElement> entries) => entries.Where(e => e.Name.LocalName == "dependency")
         .Select(e => (object)Json(("id", (string?)e.Attribute("id") ?? ""), ("range", (string?)e.Attribute("version") ?? "(,)"))).ToArray();
      var groups = dependencies?.Elements().Where(e => e.Name.LocalName == "group").ToArray() ?? [];
      var dependencyGroups = groups.Length > 0 ? groups.Select(g => Json(("targetFramework", (string?)g.Attribute("targetFramework") ?? ""), ("dependencies", Deps(g.Elements())))).ToArray()
         : dependencies is null ? [] : new[] { Json(("targetFramework", ""), ("dependencies", Deps(dependencies.Elements()))) };
      var catalog = Json(("@id", address + $"/v3/registration/{lower}/{version}.json#catalog"), ("id", id), ("version", version),
         ("authors", v.cNuPakVersionAuthors ?? ""), ("description", v.cNuPakVersionDescription ?? ""),
         ("title", v.cNuPakVersionTitle ?? id), ("tags", Tags(v)), ("listed", true), ("packageContent", content),
         ("published", DateTime.SpecifyKind(v.datestamp, DateTimeKind.Utc)), ("dependencyGroups", dependencyGroups));
      return Json(("@id", address + $"/v3/registration/{lower}/{version}.json"), ("catalogEntry", catalog),
         ("packageContent", content), ("registration", address + $"/v3/registration/{lower}/index.json"), ("listed", true),
         ("published", DateTime.SpecifyKind(v.datestamp, DateTimeKind.Utc)));
   }
   private static async Task Reply(HttpContext ctx, object value) {
      ctx.Response.ContentType = "application/json";
      var gzip = (ctx.Request.Path.Value?.Contains("/v3/registration/",StringComparison.Ordinal)==true) &&
         ctx.Request.GetTypedHeaders().AcceptEncoding?.Any(e => e.Value.Equals("gzip",StringComparison.OrdinalIgnoreCase) && (e.Quality??1)>0)==true;
      if(gzip) {ctx.Response.Headers.ContentEncoding="gzip";ctx.Response.Headers.Vary="Accept-Encoding";}
      if(HttpMethods.IsHead(ctx.Request.Method))return;
      if(gzip) {
         await using var compressed=new System.IO.Compression.GZipStream(ctx.Response.Body,System.IO.Compression.CompressionLevel.Fastest,true);
         await System.Text.Json.JsonSerializer.SerializeAsync(compressed,value,cancellationToken:ctx.RequestAborted);
      } else await ctx.Response.WriteAsJsonAsync(value,ctx.RequestAborted);
   }
}
