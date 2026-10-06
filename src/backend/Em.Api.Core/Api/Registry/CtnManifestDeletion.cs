using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Penghapusan manifest yang dipakai bersama oleh layanan manajemen dan jalur <c>/v2</c>: menolak manifest
   /// yang masih dirujuk manifest list/index, lalu menghapus tag, daftar blob, dan manifest-nya. Tautan blob
   /// tidak disentuh; blob yang tak terpakai lagi dibersihkan garbage collection.
   /// </summary>
   internal static class CtnManifestDeletion
   {
      /// <summary>Digest index di container yang sama yang merujuk <paramref name="digest"/>, atau <c>null</c>.</summary>
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

      /// <summary>Menghapus tag, daftar blob, dan manifest dalam satu transaksi.</summary>
      public static async Task DeleteAsync(CtnContext db, string manifestId, CancellationToken ct) {
         await using var tx = await db.Database.BeginTransactionAsync(ct);
         await db.Tags.Where(t => t.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.ManifestBlobs.Where(b => b.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await db.Manifests.Where(m => m.cCtnManifestId == manifestId).ExecuteDeleteAsync(ct);
         await tx.CommitAsync(ct);
      }

      // Isi index sudah divalidasi saat push; JSON yang tetap rusak dianggap tidak merujuk apa pun.
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
