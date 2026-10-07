using Em.Api.Core;
using Em.Api.Core.Models;
using Microsoft.EntityFrameworkCore;
namespace Em.Api.Core.NuPak;

/// <summary>Database context of the NuPak tables.</summary>
public sealed class NuPakDbContext(DbContextOptions<NuPakDbContext> options) : EmDbContext(options)
{
   /// <summary>Feeds.</summary>
   public DbSet<ta_NuPakFeed> Feeds => Set<ta_NuPakFeed>();
   /// <summary>Package id prefixes reserved per feed.</summary>
   public DbSet<ta_NuPakPrefix> Prefixes => Set<ta_NuPakPrefix>();
   /// <summary>Packages.</summary>
   public DbSet<ta_NuPakPackage> Packages => Set<ta_NuPakPackage>();
   /// <summary>Package versions.</summary>
   public DbSet<ta_NuPakVersion> Versions => Set<ta_NuPakVersion>();
   /// <summary>Access grants of robots to prefixes.</summary>
   public DbSet<ta_NuPakPrefixRobot> Grants => Set<ta_NuPakPrefixRobot>();
   /// <summary>Audit trail.</summary>
   public DbSet<ta_NuPakAudit> Audits => Set<ta_NuPakAudit>();
   /// <summary>Server metadata.</summary>
   public DbSet<ta_Meta> Meta => Set<ta_Meta>();
   /// <summary>Robot identities.</summary>
   public DbSet<ta_Robot> Robots => Set<ta_Robot>();
   /// <summary>Configures the keys, relations, and indexes of the NuPak entities.</summary>
   protected override void OnModelCreating(ModelBuilder b) {
      base.OnModelCreating(b);
      // Match the SQL varchar keys so comparisons use the indexed collation, without an implicit
      // nvarchar conversion over every row. Sizes and stamps are supplied by the module schema.
      foreach (var type in new[] { typeof(ta_NuPakFeed), typeof(ta_NuPakPrefix), typeof(ta_NuPakPackage), typeof(ta_NuPakVersion), typeof(ta_NuPakPrefixRobot), typeof(ta_NuPakAudit) }) {
         foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(string))) {
            var column=b.Entity(type).Property(property.Name);
            if(property.Name is "cNuPakVersionNuspec" or "json_object") continue;
            column.IsUnicode(false);
            if(property.Name.EndsWith("Id")) column.HasMaxLength(26).IsFixedLength();
         }
      }
      b.Entity<ta_NuPakPrefix>().Property(p=>p.cNuPakPrefixName).HasMaxLength(100);
      b.Entity<ta_NuPakPackage>().Property(p=>p.cNuPakPackageName).HasMaxLength(128);
      b.Entity<ta_NuPakVersion>().Property(v=>v.cNuPakVersionNumber).HasMaxLength(64);
      b.Entity<ta_NuPakVersion>().Property(v=>v.cNuPakVersionOriginal).HasMaxLength(64);
      b.Entity<ta_NuPakFeed>().Property(f=>f.cNuPakFeedSlug).HasMaxLength(64).UseCollation("Latin1_General_100_CI_AS");
      b.Entity<ta_NuPakFeed>().Property(f=>f.cNuPakFeedName).HasMaxLength(100);
      b.Entity<ta_NuPakFeed>().Property(f=>f.cNuPakFeedDescription).HasMaxLength(500);
      b.Entity<ta_NuPakFeed>().HasIndex(f=>f.cNuPakFeedSlug).IsUnique();
      b.Entity<ta_NuPakPrefix>().HasAlternateKey(p=>new {p.cNuPakFeedId,p.cNuPakPrefixId});
      b.Entity<ta_NuPakPrefix>().HasOne<ta_NuPakFeed>().WithMany().HasForeignKey(p=>p.cNuPakFeedId).OnDelete(DeleteBehavior.Restrict);
      b.Entity<ta_NuPakPackage>().HasOne<ta_NuPakFeed>().WithMany().HasForeignKey(p=>p.cNuPakFeedId).OnDelete(DeleteBehavior.Restrict);
      b.Entity<ta_NuPakPackage>().HasOne<ta_NuPakPrefix>().WithMany().HasForeignKey(p=>new {p.cNuPakFeedId,p.cNuPakPrefixId}).HasPrincipalKey(p=>new {p.cNuPakFeedId,p.cNuPakPrefixId}).OnDelete(DeleteBehavior.Restrict);
      b.Entity<ta_NuPakAudit>().HasOne<ta_NuPakFeed>().WithMany().HasForeignKey(a=>a.cNuPakFeedId).OnDelete(DeleteBehavior.Restrict);
      b.Entity<ta_NuPakPrefixRobot>().HasKey(r => new { r.cRobotId, r.cNuPakPrefixId });
      b.Entity<ta_NuPakPrefix>().HasIndex(r => new { r.cNuPakFeedId, r.cNuPakPrefixName }).IsUnique();
      b.Entity<ta_NuPakPackage>().HasIndex(r => new { r.cNuPakFeedId, r.cNuPakPackageName }).IsUnique();
      b.Entity<ta_NuPakVersion>().HasIndex(r => new { r.cNuPakPackageId, r.cNuPakVersionNumber }).IsUnique();
   }
}
