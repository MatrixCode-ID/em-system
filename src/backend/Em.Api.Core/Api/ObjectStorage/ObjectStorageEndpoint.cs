using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// Public read path of object storage: <c>GET</c>/<c>HEAD /store/&lt;bucket&gt;/&lt;key&gt;</c>, only for
/// buckets whose policy is <see cref="StoreAccess.PublicRead"/>. Outside the action dispatcher like
/// <c>/cdn</c>, with Range, <c>ETag</c> and <c>If-None-Match</c>. Anything else is a plain 404.
/// </summary>
public static class ObjectStorageEndpoint
{
   /// <summary>Path prefix of the public endpoint.</summary>
   public const string PublicRequestPath = "/store";

   /// <summary>Handles one request under <see cref="PublicRequestPath"/>.</summary>
   public static async Task HandleAsync(HttpContext ctx) {
      if (!await ctx.RequestServices.GetRequiredService<FeatureServiceRegistry>().AllowPublicAsync("ObjectStorage", ctx)) { ctx.Response.StatusCode = 404; return; }
      if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)) {
         ctx.Response.StatusCode = StatusCodes.Status404NotFound;
         return;
      }
      if (!TryParse(ctx, out var bucketName, out var key)) {
         ctx.Response.StatusCode = StatusCodes.Status404NotFound;
         return;
      }

      var store = ctx.RequestServices.GetRequiredService<ObjectStore>();
      StoreObjectRead read;
      try {
         var bucket = await store.Db.Buckets.SingleOrDefaultAsync(b => b.cStoreBucketName == bucketName, ctx.RequestAborted);
         var access = bucket is null ? null : await store.Db.Policies.Where(p => p.cStorePolicyId == bucket.cStorePolicyId)
            .Select(p => (int?)p.cStorePolicyAccess).SingleOrDefaultAsync(ctx.RequestAborted);
         if (access != (int)StoreAccess.PublicRead) {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
         }
         read = await store.OpenAsync(bucketName, key, null, ctx.RequestAborted);
      }
      catch (ActionException ex) {
         // Validation and not-found failures look the same as a private bucket: a plain 404 without detail.
         ctx.Response.StatusCode = ex.StatusCode is 503 ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status404NotFound;
         return;
      }

      // Content is uploaded by users and served on the API origin: never let a browser run it as a page.
      ctx.Response.Headers.XContentTypeOptions = "nosniff";
      ctx.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
      var etag = new EntityTagHeaderValue($"\"{read.Info.Id}-{read.Info.Version}\"");
      await Results.Stream(read.Content, read.Info.Mime, lastModified: new DateTimeOffset(DateTime.SpecifyKind(read.Info.ModifiedAt, DateTimeKind.Utc)),
         entityTag: etag, enableRangeProcessing: true).ExecuteAsync(ctx);
   }

   /// <summary>
   /// Splits the raw request target into bucket name and key. The key is taken from the raw, still encoded
   /// path so <c>%2F</c> and other escapes decode exactly once.
   /// </summary>
   internal static bool TryParse(HttpContext ctx, out string bucket, out string key) {
      bucket = key = "";
      var raw = ctx.Features.Get<IHttpRequestFeature>()?.RawTarget ?? (ctx.Request.PathBase + ctx.Request.Path).ToString();
      var query = raw.IndexOf('?');
      if (query >= 0) raw = raw[..query];
      if (!raw.StartsWith(PublicRequestPath + "/", StringComparison.OrdinalIgnoreCase)) return false;
      var rest = raw[(PublicRequestPath.Length + 1)..];
      var slash = rest.IndexOf('/');
      if (slash <= 0 || slash == rest.Length - 1) return false;
      try {
         bucket = Uri.UnescapeDataString(rest[..slash]);
         key = Uri.UnescapeDataString(rest[(slash + 1)..]);
      }
      catch (UriFormatException) { return false; }
      return true;
   }
}
