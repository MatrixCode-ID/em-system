using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Manifest deletion shared by the management service and the <c>/v2</c> path: refuses a manifest that
   /// is still referenced by a manifest list/index, then deletes its tags, blob list, and the manifest.
   /// Blob links are not touched; blobs that are no longer used are cleaned up by garbage collection.
   /// </summary>
   internal static class CtnManifestDeletion
   {
      /// <summary>Digest of an index in the same container that references <paramref name="digest"/>, or <c>null</c>.</summary>
      public static async Task<string?> FindReferencingIndexAsync(CtnContext db, string imageId, string digest, CancellationToken ct) {
         var indexes = await db.Manifests
            .Where(m => m.cCtnImageId == imageId && m.cCtnManifestDigest != digest &&
                        (m.cCtnManifestMediaType == CtnNames.ManifestDockerList || m.cCtnManifestMediaType == CtnNames.ManifestOciIndex))
            .Select(m => new { m.cCtnManifestDigest, m.cCtnManifestContent })
            .ToListAsync(ct);

         foreach (var index in indexes) {
            if (ReferencesChild(index.cCtnManifestContent, digest)) return index.cCtnManifestDigest;
         }

         return null;
      }

      /// <summary>Deletes the tags, blob list, and manifest in one transaction.</summary>
      public static async Task DeleteAsync(CtnContext db, string manifestId, CancellationToken ct) {
         await using var tx = await db.Database.BeginTransactionAsync(ct);
         await db.Tags.Where(t => t.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.ManifestBlobs.Where(b => b.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.Manifests.Where(m => m.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await tx.CommitAsync(ct);
      }

      // The index content was validated at push; JSON that is still corrupt is taken as referencing nothing.
      private static bool ReferencesChild(byte[] content, string digest) {
         try {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("manifests", out var manifests) || manifests.ValueKind != JsonValueKind.Array) return false;
            foreach (var child in manifests.EnumerateArray()) {
               if (child.ValueKind == JsonValueKind.Object && child.TryGetProperty("digest", out var d) &&
                   d.ValueKind == JsonValueKind.String && d.GetString() == digest) return true;
            }
         } catch (JsonException) {
         }

         return false;
      }
   }
}
