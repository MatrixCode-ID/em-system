using Em.Api.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.ObjectStorage;

/// <summary>Database context of the object storage tables (<c>tables/060-objectstore.sql</c>).</summary>
public sealed class ObjectStorageDbContext(DbContextOptions<ObjectStorageDbContext> options) : EmDbContext(options)
{
   /// <summary>Volumes.</summary>
   public DbSet<ta_StoreVolume> Volumes => Set<ta_StoreVolume>();

   /// <summary>Policies.</summary>
   public DbSet<ta_StorePolicy> Policies => Set<ta_StorePolicy>();

   /// <summary>Volumes of each policy in priority order.</summary>
   public DbSet<ta_StorePolicyVolume> PolicyVolumes => Set<ta_StorePolicyVolume>();

   /// <summary>Buckets.</summary>
   public DbSet<ta_StoreBucket> Buckets => Set<ta_StoreBucket>();

   /// <summary>Objects.</summary>
   public DbSet<ta_StoreObject> Objects => Set<ta_StoreObject>();

   /// <summary>Object versions.</summary>
   public DbSet<ta_StoreObjectVersion> Versions => Set<ta_StoreObjectVersion>();

   /// <summary>Content blobs.</summary>
   public DbSet<ta_StoreObjectBlob> Blobs => Set<ta_StoreObjectBlob>();

   /// <summary>Physical copies of blobs on volumes.</summary>
   public DbSet<ta_StoreObjectBlobVol> Copies => Set<ta_StoreObjectBlobVol>();

   /// <summary>Server metadata (schema version and instance lease).</summary>
   public DbSet<ta_Meta> Meta => Set<ta_Meta>();

   /// <summary>Configures keys, column types and relations to match the SQL schema.</summary>
   protected override void OnModelCreating(ModelBuilder b) {
      base.OnModelCreating(b);
      foreach (var type in new[] { typeof(ta_StoreVolume), typeof(ta_StorePolicy), typeof(ta_StorePolicyVolume), typeof(ta_StoreBucket),
                  typeof(ta_StoreObject), typeof(ta_StoreObjectVersion), typeof(ta_StoreObjectBlobVol) }) {
         foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Id")))
            b.Entity(type).Property(property.Name).IsUnicode(false).HasMaxLength(26).IsFixedLength();
      }

      b.Entity<ta_StoreVolume>(e => {
         e.Property(p => p.cStoreVolumeName).IsUnicode(false).HasMaxLength(100);
         e.Property(p => p.cStoreVolumePath).HasMaxLength(400);
         e.Property(p => p.cStoreVolumeNote).HasMaxLength(500);
         e.HasIndex(p => p.cStoreVolumeName).IsUnique();
      });
      b.Entity<ta_StorePolicy>(e => {
         e.Property(p => p.cStorePolicyName).IsUnicode(false).HasMaxLength(100);
         e.Property(p => p.cStorePolicyNote).HasMaxLength(500);
         e.HasIndex(p => p.cStorePolicyName).IsUnique();
      });
      b.Entity<ta_StorePolicyVolume>(e => {
         e.HasKey(p => new { p.cStorePolicyId, p.cStoreVolumeId });
         e.HasOne<ta_StorePolicy>().WithMany().HasForeignKey(p => p.cStorePolicyId).OnDelete(DeleteBehavior.Cascade);
         e.HasOne<ta_StoreVolume>().WithMany().HasForeignKey(p => p.cStoreVolumeId).OnDelete(DeleteBehavior.Restrict);
      });
      b.Entity<ta_StoreBucket>(e => {
         var name = e.Property(p => p.cStoreBucketName).IsUnicode(false).HasMaxLength(63);
         if (Database.IsSqlServer()) name.UseCollation("Latin1_General_100_BIN2");
         e.Property(p => p.cStoreBucketNote).HasMaxLength(500);
         e.HasIndex(p => p.cStoreBucketName).IsUnique();
         e.HasOne<ta_StorePolicy>().WithMany().HasForeignKey(p => p.cStorePolicyId).OnDelete(DeleteBehavior.Restrict);
      });
      b.Entity<ta_StoreObject>(e => {
         var key = e.Property(p => p.cStoreObjectKey).HasMaxLength(1024);
         if (Database.IsSqlServer()) key.UseCollation("Latin1_General_100_BIN2");
         e.Property(p => p.cStoreObjectKeyHash).HasMaxLength(32).IsFixedLength();
         e.HasIndex(p => new { p.cStoreBucketId, p.cStoreObjectKeyHash }).IsUnique();
         e.HasOne<ta_StoreBucket>().WithMany().HasForeignKey(p => p.cStoreBucketId).OnDelete(DeleteBehavior.Restrict);
      });
      b.Entity<ta_StoreObjectBlob>(e => {
         e.Property(p => p.cStoreObjectBlobHash).HasMaxLength(32).IsFixedLength();
         e.Property(p => p.cStoreObjectBlobCrc32).HasMaxLength(4).IsFixedLength();
      });
      b.Entity<ta_StoreObjectVersion>(e => {
         e.HasKey(p => new { p.cStoreObjectId, p.cStoreObjectVersionId });
         e.Property(p => p.cStoreObjectVersionId).ValueGeneratedNever();
         e.Property(p => p.cStoreObjectBlobHash).HasMaxLength(32).IsFixedLength();
         e.Property(p => p.cStoreObjectVersionMime).IsUnicode(false).HasMaxLength(255);
         e.HasOne<ta_StoreObject>().WithMany().HasForeignKey(p => p.cStoreObjectId).OnDelete(DeleteBehavior.Restrict);
         e.HasOne<ta_StoreObjectBlob>().WithMany().HasForeignKey(p => p.cStoreObjectBlobHash).OnDelete(DeleteBehavior.Restrict);
      });
      b.Entity<ta_StoreObjectBlobVol>(e => {
         e.HasKey(p => new { p.cStoreObjectBlobHash, p.cStoreVolumeId });
         e.Property(p => p.cStoreObjectBlobHash).HasMaxLength(32).IsFixedLength();
         e.HasOne<ta_StoreObjectBlob>().WithMany().HasForeignKey(p => p.cStoreObjectBlobHash).OnDelete(DeleteBehavior.Restrict);
         e.HasOne<ta_StoreVolume>().WithMany().HasForeignKey(p => p.cStoreVolumeId).OnDelete(DeleteBehavior.Restrict);
      });
   }
}
