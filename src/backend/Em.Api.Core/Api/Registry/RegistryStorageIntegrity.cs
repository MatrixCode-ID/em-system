using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Storage;
using Em.Shared;

namespace Em.Api.Core.Registry;

internal static class RegistryStorageIntegrity
{
   internal static void VerifyStartup(CtnContext db, string target) => VerifyBlobs(db, target);
   internal static void Verify(CtnContext db, string target) {
      // In-progress uploads cannot safely be copied while writers are active. Operator must finish/cancel first.
      if (db.Uploads.Any()) throw new ActionException("Registry has upload metadata. Finish/cancel uploads before changing its directory.", 409);
      VerifyBlobs(db, target);
   }
   private static void VerifyBlobs(CtnContext db, string target) {
      foreach (var blob in db.Blobs.AsNoTracking()) {
         VerifyBlob(target, blob.cCtnBlobDigest, blob.cCtnBlobSize);
      }
   }
   internal static void VerifyBlob(string target, string digest, long size) {
         if (digest.Length != 71 || !digest.StartsWith("sha256:") || !digest[7..].All(Uri.IsHexDigit))
            throw new ActionException("Registry metadata contains an invalid blob digest.", 409);
         var hex = digest[7..];
         var path = Path.Combine(target, "blobs", "sha256", hex[..2], hex);
         ManagedStorageSettings.RejectLinks(path);
         if (!File.Exists(path) || new FileInfo(path).Length != size
             || CtnBlobStore.ComputeDigestAsync(path, CancellationToken.None).GetAwaiter().GetResult() != digest)
            throw new ActionException($"Registry target is incomplete: missing or corrupt blob {digest}. Prepare a complete manual copy before changing the directory.", 409);
   }
}
