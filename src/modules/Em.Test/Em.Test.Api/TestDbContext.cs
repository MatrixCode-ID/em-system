using Em.Api.Core;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Test.Api
{
   /// <summary>
   /// Context data module uji. Didaftarkan lewat <c>AddDbContext</c> pada koneksi bawaan, jadi module tidak
   /// menyunting context milik engine dan tetap ikut transaksi approval satu server.
   /// </summary>
   public class TestDbContext(DbContextOptions<TestDbContext> options) : EmDbContext(options)
   {
      public DbSet<ta_TestItem> ta_TestItems => Set<ta_TestItem>();

      public DbSet<vi_TestItem> vi_TestItems => Set<vi_TestItem>();

      public DbSet<ta_TestDoc> ta_TestDocs => Set<ta_TestDoc>();

      public DbSet<vi_TestDoc> vi_TestDocs => Set<vi_TestDoc>();

      protected override void OnModelCreating(ModelBuilder modelBuilder) {
         base.OnModelCreating(modelBuilder);

         // Without an explicit precision EF warns and may truncate the decimals at the provider's default.
         modelBuilder.Entity<ta_TestItem>().Property(r => r.cTestItemPrice).HasPrecision(18, 2);
         modelBuilder.Entity<vi_TestItem>().Property(r => r.cTestItemPrice).HasPrecision(18, 2);
         modelBuilder.Entity<ta_TestDoc>().Property(r => r.cTestDocAmount).HasPrecision(18, 2);
         modelBuilder.Entity<vi_TestDoc>().Property(r => r.cTestDocAmount).HasPrecision(18, 2);
      }
   }
}
