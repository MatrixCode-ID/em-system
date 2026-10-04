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
   /// Hasil membaca path di bawah <c>/v2</c>. Nama container selalu dua segmen (<c>root/nama</c>), tetapi
   /// parser sengaja membaca segmen sebanyak apa pun di depan kata kunci (<c>manifests</c>, <c>blobs</c>,
   /// <c>tags</c>) supaya nama tiga segmen atau lebih sampai ke pemeriksa nama dan dijawab
   /// <c>NAME_INVALID</c>, bukan jatuh jadi 404 yang membingungkan.
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
