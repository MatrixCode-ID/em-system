using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Em.Api.Core
{
   public abstract class EmDbContext(DbContextOptions options) : DbContext(options)
   {
      protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
         base.OnConfiguring(optionsBuilder);

         // Reads are no-tracking by default because almost every read here ends up as JSON on its
         // way out, and a row that is only being looked at costs nothing to keep a copy of. A read
         // whose row is edited in place and saved afterwards has to ask for tracking with
         // AsTracking() - without it the edit is made to an instance nothing is watching and the
         // save writes nothing at all. Writing a row that was handed in from outside needs no
         // tracking read: UpdateRow and DeleteRow below do that.
         // This is only the default the context starts from - a query that says AsTracking() or
         // AsNoTracking() still decides for itself.
         optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
      }

      protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) {
         base.ConfigureConventions(configurationBuilder);
         // A vi_ entity derives from its ta_ entity only to reuse the columns, it is not a
         // TPH hierarchy. Dropping this convention keeps every entity mapped to its own
         // table/view; real inheritance must be opted into with an explicit HasBaseType.
         configurationBuilder.Conventions.Remove(typeof(BaseTypeDiscoveryConvention));
      }

      /// <summary>
      /// Menandai satu baris untuk ditimpakan ke database, siap disimpan lewat
      /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
      /// <para>
      /// Dipakai menggantikan <c>Attach</c> + <c>State = Modified</c> yang ditulis sendiri: satu
      /// permintaan sering membaca sebuah baris - atau baru saja membuatnya - sebelum menulisnya,
      /// dan baris yang sudah dipegang context tidak boleh dipegang dua kali. Kalau barisnya memang
      /// sudah dipegang, yang dipegang itulah yang diisi ulang dari <paramref name="row"/>; kalau
      /// belum, barisnya dilampirkan seperti biasa.
      /// </para>
      /// </summary>
      /// <typeparam name="T">Jenis baris yang ditulis.</typeparam>
      /// <param name="row">Baris berisi nilai terbaru, lengkap dengan nilai kunci utamanya.</param>
      public void UpdateRow<T>(T row) where T : class {
         var tracked = FindTrackedRow(row);
         if (tracked is not null) {
            tracked.CurrentValues.SetValues(row);
            return;
         }

         Set<T>().Attach(row);
         Entry(row).State = EntityState.Modified;
      }

      /// <summary>
      /// Menandai satu baris untuk dihapus dari database, siap disimpan lewat
      /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. Sama seperti
      /// <see cref="UpdateRow"/>, baris yang sudah dipegang context dihapus lewat yang dipegang itu,
      /// bukan lewat salinan yang diberikan di sini.
      /// </summary>
      /// <typeparam name="T">Jenis baris yang dihapus.</typeparam>
      /// <param name="row">Baris yang ingin dihapus; cukup nilai kunci utamanya yang benar.</param>
      public void DeleteRow<T>(T row) where T : class {
         var tracked = FindTrackedRow(row);
         if (tracked is not null) {
            Remove(tracked.Entity);
            return;
         }

         Set<T>().Attach(row);
         Remove(row);
      }

      // The entry this context is already holding for the same row, or null when it is holding
      // none. Looked up by primary key through the change tracker only - the database is never
      // asked, because what is being avoided here is a second instance of a row in memory, not a
      // row that is missing from storage.
      private EntityEntry<T>? FindTrackedRow<T>(T row) where T : class {
         var key = Model.FindEntityType(typeof(T))?.FindPrimaryKey();
         if (key is null) return null;

         var keyValues = new object?[key.Properties.Count];
         for (var i = 0; i < keyValues.Length; i++) {
            var property = key.Properties[i];
            var value = property.PropertyInfo?.GetValue(row) ?? property.FieldInfo?.GetValue(row);

            // A row that does not carry its own key cannot be the one anything is holding, and
            // asking the tracker for a null key would only ever come back empty anyway.
            if (value is null) return null;

            keyValues[i] = value;
         }

         return Set<T>().Local.FindEntryUntyped(keyValues);
      }
   }
}
