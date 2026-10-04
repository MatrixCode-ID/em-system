using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Pemeriksaan sekali jalan saat registry menyala: semua tabel <c>ta_Ctn*</c> harus sudah ada dengan
   /// kolom yang dipetakan. Tanpa ini tabel yang belum dibuat baru ketahuan saat <c>docker push</c>
   /// pertama gagal dengan 500.
   /// </summary>
   internal static class CtnStartupChecks
   {
      public static void VerifyTables(IServiceProvider rootServices) {
         using var scope = rootServices.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<CtnContext>();

         // Membaca paling banyak satu baris dengan semua kolom entitasnya: yang diperiksa ada-tidaknya
         // tabel dan kolom, bukan isinya.
         Probe("ta_CtnRoot", () => db.Roots.OrderBy(r => r.cCtnRootId).FirstOrDefault());
         Probe("ta_CtnFolder", () => db.Folders.OrderBy(r => r.cCtnFolderId).FirstOrDefault());
         Probe("ta_CtnImage", () => db.Images.OrderBy(r => r.cCtnImageId).FirstOrDefault());
         Probe("ta_CtnManifest", () => db.Manifests.OrderBy(r => r.cCtnManifestId).FirstOrDefault());
         Probe("ta_CtnTag", () => db.Tags.OrderBy(r => r.cCtnImageId).FirstOrDefault());
         Probe("ta_CtnBlob", () => db.Blobs.OrderBy(r => r.cCtnBlobId).FirstOrDefault());
         Probe("ta_CtnBlobLink", () => db.BlobLinks.OrderBy(r => r.cCtnImageId).FirstOrDefault());
         Probe("ta_CtnManifestBlob", () => db.ManifestBlobs.OrderBy(r => r.cCtnManifestId).FirstOrDefault());
         Probe("ta_CtnUpload", () => db.Uploads.OrderBy(r => r.cCtnUploadId).FirstOrDefault());
         Probe("ta_Robot", () => db.Robots.OrderBy(r => r.cRobotId).FirstOrDefault());
         Probe("ta_CtnRootRobot", () => db.RobotRoots.OrderBy(r => r.cRobotId).FirstOrDefault());
      }

      private static void Probe(string table, Action read) {
         try {
            read();
         } catch (Exception ex) {
            throw new InvalidOperationException(
               $"The container registry is enabled but table '{table}' cannot be read. " +
               "Run doc/sqlscript/mssql/tables/030-registry.sql on the core database first.", ex);
         }
      }
   }
}
