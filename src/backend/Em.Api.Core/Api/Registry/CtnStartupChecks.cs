using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// One-time check when the registry starts: every <c>ta_Ctn*</c> table must exist with the mapped columns.
   /// Without it a missing table would only show up when the first <c>docker push</c> fails with 500.
   /// </summary>
   internal static class CtnStartupChecks
   {
      public static void VerifyTables(IServiceProvider rootServices) {
         using var scope = rootServices.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<CtnContext>();

         // Reads at most one row with every mapped column: what is checked is that the table and columns
         // exist, not their content.
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
         Probe("ta_CtnDeploy", () => db.Deploys.OrderBy(r => r.cCtnDeployId).FirstOrDefault(),
            "doc/sqlscript/mssql/updates/20261007-CtnDeploy.sql");
         Probe("ta_CtnDeployRun", () => db.DeployRuns.OrderBy(r => r.cCtnDeployRunId).FirstOrDefault(),
            "doc/sqlscript/mssql/updates/20261007-CtnDeploy.sql");
      }

      private static void Probe(string table, Action read, string? migration = null) {
         try {
            read();
         } catch (Exception ex) {
            // A table added after the first release also names the migration for existing databases.
            throw new InvalidOperationException(
               $"The container registry is enabled but table '{table}' cannot be read. " +
               "Run doc/sqlscript/mssql/tables/030-registry.sql on the core database first" +
               (migration is null ? "." : $" (existing databases: {migration})."), ex);
         }
      }
   }
}
