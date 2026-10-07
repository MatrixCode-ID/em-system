using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Context for the <c>ta_Ctn*</c> tables (registry and deploy). Separate from <c>ApiCoreContext</c> so a host
   /// that does not enable the registry does not carry their model; registered only by
   /// <c>EmAppBuilder.AddContainerRegistry</c> and <c>EmAppBuilder.AddManagedStorageSettings</c>.
   /// </summary>
   internal class CtnContext(DbContextOptions<CtnContext> options) : EmDbContext(options)
   {
      public DbSet<ta_CtnRoot> Roots => Set<ta_CtnRoot>();
      public DbSet<ta_CtnFolder> Folders => Set<ta_CtnFolder>();
      public DbSet<ta_CtnImage> Images => Set<ta_CtnImage>();
      public DbSet<ta_CtnManifest> Manifests => Set<ta_CtnManifest>();
      public DbSet<ta_CtnTag> Tags => Set<ta_CtnTag>();
      public DbSet<ta_CtnBlob> Blobs => Set<ta_CtnBlob>();
      public DbSet<ta_CtnBlobLink> BlobLinks => Set<ta_CtnBlobLink>();
      public DbSet<ta_CtnManifestBlob> ManifestBlobs => Set<ta_CtnManifestBlob>();
      public DbSet<ta_CtnUpload> Uploads => Set<ta_CtnUpload>();
      public DbSet<ta_Robot> Robots => Set<ta_Robot>();
      public DbSet<ta_CtnRootRobot> RobotRoots => Set<ta_CtnRootRobot>();
      public DbSet<ta_CtnDeploy> Deploys => Set<ta_CtnDeploy>();
      public DbSet<ta_CtnDeployRun> DeployRuns => Set<ta_CtnDeployRun>();

      /// <summary>Every entity type, for the startup check.</summary>
      public static readonly Type[] EntityTypes = [
         typeof(ta_CtnRoot), typeof(ta_CtnFolder), typeof(ta_CtnImage), typeof(ta_CtnManifest),
         typeof(ta_CtnTag), typeof(ta_CtnBlob), typeof(ta_CtnBlobLink), typeof(ta_CtnManifestBlob),
         typeof(ta_CtnUpload), typeof(ta_Robot), typeof(ta_CtnRootRobot), typeof(ta_CtnDeploy), typeof(ta_CtnDeployRun)
      ];

      protected override void OnModelCreating(ModelBuilder modelBuilder) {
         base.OnModelCreating(modelBuilder);

         // Internal entities are not found from its DbSets, so they are named one by one.
         foreach (var type in EntityTypes) {
            modelBuilder.Entity(type);
         }

         modelBuilder.Entity<ta_CtnTag>().HasKey(k => new { k.cCtnImageId, k.cCtnTagName });
         modelBuilder.Entity<ta_CtnBlobLink>().HasKey(k => new { k.cCtnImageId, k.cCtnBlobId });
         modelBuilder.Entity<ta_CtnManifestBlob>().HasKey(k => new { k.cCtnManifestId, k.cCtnManifestBlobOrder });
         modelBuilder.Entity<ta_CtnRootRobot>().HasKey(k => new { k.cRobotId, k.cCtnRootId });
      }
   }
}
