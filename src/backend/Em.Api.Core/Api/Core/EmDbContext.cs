using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Em.Api.Core
{
   /// <summary>Base class of the engine's database contexts: provider selection and shared conventions.</summary>
   public abstract class EmDbContext(DbContextOptions options) : DbContext(options)
   {
      /// <summary>Selects the database provider and connection of the running context.</summary>
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

      /// <summary>Applies the engine's shared model conventions.</summary>
      protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) {
         base.ConfigureConventions(configurationBuilder);
         // A vi_ entity derives from its ta_ entity only to reuse the columns, it is not a
         // TPH hierarchy. Dropping this convention keeps every entity mapped to its own
         // table/view; real inheritance must be opted into with an explicit HasBaseType.
         configurationBuilder.Conventions.Remove(typeof(BaseTypeDiscoveryConvention));
      }

      /// <summary>
      /// Marks one row to be written over the database row, ready to be saved through
      /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
      /// <para>
      /// Used instead of a hand-written <c>Attach</c> + <c>State = Modified</c>: one request often reads a
      /// row - or has just created it - before writing it, and a row the context already holds must not be
      /// held twice. If the row is already held, the held one is refilled from <paramref name="row"/>; if
      /// not, the row is attached as usual.
      /// </para>
      /// </summary>
      /// <typeparam name="T">Kind of the row being written.</typeparam>
      /// <param name="row">Row holding the latest values, complete with its primary key values.</param>
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
      /// Marks one row to be deleted from the database, ready to be saved through
      /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. Like <see cref="UpdateRow"/>, a row
      /// already held by the context is deleted through the held one, not through the copy given here.
      /// </summary>
      /// <typeparam name="T">Kind of the row being deleted.</typeparam>
      /// <param name="row">The row to delete; only its primary key values need to be correct.</param>
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
