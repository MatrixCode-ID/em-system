namespace Em.Api.Core.Registry
{
   internal enum CtnRouteKind
   {
      Unknown,
      Ping,
      Manifest,
      Blob,
      UploadStart,
      Upload,
      Tags
   }

   /// <summary>
   /// Result of reading a path under <c>/v2</c>. A container name is always two segments (<c>root/name</c>),
   /// but the parser deliberately reads as many segments as appear before the keyword (<c>manifests</c>,
   /// <c>blobs</c>, <c>tags</c>) so that a name of three or more segments reaches the name checker and is
   /// answered <c>NAME_INVALID</c>, instead of falling into a confusing 404.
   /// </summary>
   internal sealed record CtnRoute(CtnRouteKind Kind, string[] NameSegments, string? Reference = null, string? UploadId = null)
   {
      public static CtnRoute Parse(string? path) {
         var s = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
         if (s.Length == 0) return new CtnRoute(CtnRouteKind.Ping, []);

         if (s.Length >= 3 && s[^2] == "tags" && s[^1] == "list")
            return new CtnRoute(CtnRouteKind.Tags, s[..^2]);

         if (s.Length >= 3 && s[^2] == "manifests")
            return new CtnRoute(CtnRouteKind.Manifest, s[..^2], s[^1]);

         if (s.Length >= 3 && s[^2] == "blobs" && s[^1] == "uploads")
            return new CtnRoute(CtnRouteKind.UploadStart, s[..^2]);

         if (s.Length >= 4 && s[^2] == "uploads" && s[^3] == "blobs")
            return new CtnRoute(CtnRouteKind.Upload, s[..^3], UploadId: s[^1]);

         if (s.Length >= 3 && s[^2] == "blobs")
            return new CtnRoute(CtnRouteKind.Blob, s[..^2], s[^1]);

         return new CtnRoute(CtnRouteKind.Unknown, []);
      }
   }
}
