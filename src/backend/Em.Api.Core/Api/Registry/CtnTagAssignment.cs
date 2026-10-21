using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Points a tag at an existing manifest of a container without a push. A tag points at one manifest per
   /// container: when it is on another manifest it moves, like a push to an existing tag does.
   /// </summary>
   internal static class CtnTagAssignment
   {
      /// <summary>What happened to the tag.</summary>
      public enum Outcome { Unchanged, Added, Moved }

      /// <summary>The outcome, the digest the tag now points at, and the digest it pointed at before a move.</summary>
      public readonly record struct Result(Outcome Outcome, string Digest, string? PreviousDigest);

      /// <summary>
      /// Adds or moves <paramref name="tag"/> to the manifest <paramref name="manifestId"/>. 400 for an invalid tag,
      /// 404 when the manifest is not in the container, 409 when the same tag is created at the same moment.
      /// </summary>
      public static async Task<Result> AssignAsync(CtnContext db, string imageId, string manifestId, string tag, DateTime now, CancellationToken ct) {
         if (!CtnNames.IsValidTag(tag)) throw new ActionException($"'{tag}' is not a valid tag.", 400);
         var digest = await db.Manifests.Where(m => m.cCtnManifestId == manifestId && m.cCtnImageId == imageId)
                         .Select(m => m.cCtnManifestDigest).SingleOrDefaultAsync(ct) ??
                      throw new ActionException("Manifest was not found.", 404);

         var current = await db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == tag)
            .Select(t => t.cCtnManifestId).SingleOrDefaultAsync(ct);
         if (current == manifestId) return new Result(Outcome.Unchanged, digest, null);

         if (current is null) {
            db.Tags.Add(new ta_CtnTag {
               cCtnImageId = imageId, cCtnTagName = tag, cCtnManifestId = manifestId, ustamp = now, datestamp = now
            });
            try {
               await db.SaveChangesAsync(ct);
            } catch (DbUpdateException) {
               throw new ActionException($"Tag '{tag}' was just added by someone else; refresh and try again.", 409);
            }

            return new Result(Outcome.Added, digest, null);
         }

         var previous = await db.Manifests.Where(m => m.cCtnManifestId == current)
            .Select(m => m.cCtnManifestDigest).SingleOrDefaultAsync(ct);
         // The same statement a push to an existing tag uses.
         await db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == tag)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.cCtnManifestId, manifestId).SetProperty(t => t.ustamp, now), ct);
         return new Result(Outcome.Moved, digest, previous);
      }
   }
}
