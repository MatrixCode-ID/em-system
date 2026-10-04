using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Apa yang diketahui engine tentang satu entitas sesudah membandingkan nilainya dengan isi tabel:
   /// nilai tiap kolom saat itu, dan apakah ada kolom yang bertabrakan.
   /// </summary>
   /// <param name="ItemId">Entitas usulan yang dibandingkan.</param>
   /// <param name="Conflicted">Apakah ada kolom yang bertabrakan, atau entitasnya sudah tidak ada.</param>
   /// <param name="Fields">Nilai tiap kolom yang dibandingkan saat itu.</param>
   internal sealed record ApprovalComparedItem(string ItemId, bool Conflicted,
      IReadOnlyList<(string Name, string? Current)> Fields);

   /// <summary>
   /// Usulan perubahan data tidak bisa diterapkan karena isi tabelnya sudah berubah di luar request, atau
   /// entitasnya sudah tidak ada.
   /// </summary>
   /// <remarks>
   /// Membawa kolom-kolom yang bertabrakan, supaya keputusan yang gagal karenanya bisa menampilkan nilai
   /// lama, nilai sekarang, dan nilai usulan kepada approver.
   /// </remarks>
   internal sealed class ApprovalConflictException : ActionException
   {
      private const int MaxListedFields = 8;

      public ApprovalConflictException(IReadOnlyList<ApprovalConflictField> conflicts, bool isFinal,
         IReadOnlyList<ApprovalComparedItem> compared) : base(Describe(conflicts, isFinal), 409) {
         Conflicts = conflicts;
         IsFinal = isFinal;
         Compared = compared;
      }

      /// <summary>Kolom-kolom yang bertabrakan.</summary>
      public IReadOnlyList<ApprovalConflictField> Conflicts { get; }

      /// <summary>Tidak bisa ditimpa: ada entitas yang sudah tidak ada.</summary>
      public bool IsFinal { get; }

      /// <summary>Hasil perbandingan seluruh entitas, untuk dicatat sesudah transaksinya dibatalkan.</summary>
      public IReadOnlyList<ApprovalComparedItem> Compared { get; }

      private static string Describe(IReadOnlyList<ApprovalConflictField> conflicts, bool isFinal) {
         var head = isFinal
            ? "An entity this request changes no longer exists, so the request cannot be applied. It can only be rejected."
            : $"{conflicts.Count} value(s) changed outside this request since it was submitted, so it cannot be applied as proposed.";

         var lines = conflicts.Take(MaxListedFields).Select(r =>
            $"- {r.Entity} {r.EntityKey} / {r.FieldName}: expected '{r.OldValue}', found '{r.CurrentValue}', proposed '{r.NewValue}'");
         var more = conflicts.Count > MaxListedFields
            ? $"{Environment.NewLine}- ... and {conflicts.Count - MaxListedFields} more"
            : string.Empty;

         return head + Environment.NewLine + string.Join(Environment.NewLine, lines) + more;
      }
   }

   /// <summary>
   /// Menerapkan usulan perubahan data: membandingkan tiap kolom dengan isi tabel, lalu menyerahkan yang
   /// boleh diterapkan ke handler modul.
   /// </summary>
   /// <remarks>
   /// Per kolom ada tiga nilai: <b>lama</b> (dicatat saat diajukan), <b>sekarang</b> (isi tabel saat ini),
   /// dan <b>usulan</b>. Sekarang sama dengan lama berarti kolomnya diterapkan; sekarang sama dengan usulan
   /// berarti kolomnya dilewati karena sudah sesuai; selain itu berarti konflik. Pembandingnya isi tabel,
   /// bukan request lain, supaya perubahan dari luar aplikasi ikut tertangkap.
   /// <para>
   /// Seluruhnya berjalan di dalam transaksi pemanggil. Pembandingan semua entitas dilakukan lebih dulu,
   /// sebelum satu pun diterapkan, jadi request yang konflik tidak pernah setengah diterapkan.
   /// </para>
   /// </remarks>
   internal static class ApprovalDataApplier
   {
      /// <summary>
      /// Membaca usulan sebuah request sebagai nilai bertipe, tanpa menerapkan apa pun.
      /// </summary>
      public static async Task<IReadOnlyList<ApprovalDataItem>> ReadItemsAsync(ApiCoreContext ctx,
         IApprovalDataFlow flow, string requestId, CancellationToken cancellationToken) {
         var items = await ctx.ta_ApprovalRequestItems.AsNoTracking()
            .Where(r => r.cApprovalRequestId == requestId)
            .OrderBy(r => r.cApprovalRequestItemOrder).ThenBy(r => r.cApprovalRequestItemId)
            .ToListAsync(cancellationToken);
         var itemIds = items.Select(r => r.cApprovalRequestItemId).ToArray();
         var fields = await ctx.ta_ApprovalRequestItemFields.AsNoTracking()
            .Where(r => itemIds.Contains(r.cApprovalRequestItemId))
            .ToListAsync(cancellationToken);

         return Build(flow, items, fields);
      }

      /// <summary>
      /// Membandingkan lalu menerapkan seluruh usulan sebuah request.
      /// </summary>
      /// <param name="ctx">Context database inti, yang sedang berada di dalam transaksi.</param>
      /// <param name="flow">Alur jenis dokumennya.</param>
      /// <param name="scope">Bahan untuk menyerahkan giliran ke handler modul. Requestnya harus terlacak.</param>
      /// <param name="overrideConflicts">Tetap terapkan kolom yang bertabrakan.</param>
      /// <param name="note">Alasan keputusan; wajib kalau kolom yang bertabrakan ditimpa.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <returns>Usulan setelah diterapkan; kunci entitas baru sudah berisi kunci yang sebenarnya.</returns>
      /// <exception cref="ApprovalConflictException">
      /// Ada kolom yang bertabrakan dan penimpaan tidak diminta, atau ada entitas yang sudah tidak ada.
      /// </exception>
      public static async Task<IReadOnlyList<ApprovalDataItem>> ApplyAsync(ApiCoreContext ctx, IApprovalDataFlow flow,
         ApprovalRunScope scope, bool overrideConflicts, string? note, CancellationToken cancellationToken) {
         var request = scope.Request;
         var items = await ctx.ta_ApprovalRequestItems.AsTracking()
            .Where(r => r.cApprovalRequestId == request.cApprovalRequestId)
            .OrderBy(r => r.cApprovalRequestItemOrder).ThenBy(r => r.cApprovalRequestItemId)
            .ToListAsync(cancellationToken);
         var itemIds = items.Select(r => r.cApprovalRequestItemId).ToArray();
         var fields = await ctx.ta_ApprovalRequestItemFields.AsTracking()
            .Where(r => itemIds.Contains(r.cApprovalRequestItemId))
            .ToListAsync(cancellationToken);
         var keyRows = await ctx.ta_ApprovalRequestItemKeys.AsTracking()
            .Where(r => itemIds.Contains(r.cApprovalRequestItemId))
            .ToListAsync(cancellationToken);

         // 1. Compare everything before writing anything.
         var plans = new List<ItemPlan>();
         var conflicts = new List<ApprovalConflictField>();
         var anyFinal = false;

         foreach (var item in items) {
            var entity = flow.FindEntity(item.cApprovalRequestItemEntity) ?? throw new ActionException(
               $"Entity '{item.cApprovalRequestItemEntity}' is not declared by the flow any more, so this request cannot be applied.", 409);
            var itemFields = fields.Where(r => r.cApprovalRequestItemId == item.cApprovalRequestItemId)
               .OrderBy(r => r.cApprovalRequestItemFieldOrder).ToList();
            var plan = new ItemPlan(item, entity, itemFields);
            plans.Add(plan);

            if (item.cApprovalRequestItemOperation == ApprovalItemOperation.Create) {
               plan.ToApply.AddRange(itemFields);
               continue;
            }

            var current = await entity.LoadAsync(scope, item.cApprovalRequestItemKey);
            if (current is null) {
               anyFinal = true;
               plan.Conflicted = true;

               // An entity that is gone has nothing to compare, so every column it was going to change is
               // reported against nothing; one without columns is reported by what was asked of it.
               var missing = itemFields.Count == 0
                  ? new List<ApprovalConflictField> { new() { FieldName = item.cApprovalRequestItemOperation.ToString() } }
                  : [.. itemFields.Select(f => new ApprovalConflictField {
                     FieldName = f.cApprovalRequestItemFieldName,
                     OldValue = f.cApprovalRequestItemFieldOldValue,
                     NewValue = f.cApprovalRequestItemFieldNewValue
                  })];

               foreach (var row in missing) {
                  row.Entity = item.cApprovalRequestItemEntity;
                  row.EntityKey = DisplayKey(item.cApprovalRequestItemKey);
                  conflicts.Add(row);
               }

               continue;
            }

            // Deleting and reinstating compare nothing: the entity exists, and that is all they need.
            if (item.cApprovalRequestItemOperation != ApprovalItemOperation.Update) continue;

            foreach (var field in itemFields) {
               if (!current.TryGetValue(field.cApprovalRequestItemFieldName, out var now)) {
                  throw new InvalidOperationException(
                     $"The loader of entity '{entity.Name}' returned no value for column '{field.cApprovalRequestItemFieldName}', so it cannot be compared.");
               }

               field.cApprovalRequestItemFieldCurrentValue = now;
               plan.Compared.Add((field.cApprovalRequestItemFieldName, now));

               if (Same(now, field.cApprovalRequestItemFieldNewValue)) continue;

               if (Same(now, field.cApprovalRequestItemFieldOldValue)) {
                  plan.ToApply.Add(field);
                  continue;
               }

               plan.Conflicted = true;
               plan.ConflictedFields.Add(field);
               conflicts.Add(new ApprovalConflictField {
                  Entity = item.cApprovalRequestItemEntity,
                  EntityKey = DisplayKey(item.cApprovalRequestItemKey),
                  FieldName = field.cApprovalRequestItemFieldName,
                  OldValue = field.cApprovalRequestItemFieldOldValue,
                  CurrentValue = now,
                  NewValue = field.cApprovalRequestItemFieldNewValue
               });
            }
         }

         if (conflicts.Count > 0) {
            if (anyFinal || !overrideConflicts) {
               throw new ApprovalConflictException(conflicts, anyFinal, [.. plans.Select(r => new ApprovalComparedItem(
                  r.Item.cApprovalRequestItemId, r.Conflicted, r.Compared))]);
            }

            if (string.IsNullOrWhiteSpace(note)) {
               throw new ActionException("A reason is required for applying a request past a conflict.", 400);
            }

            // Applying past a conflict is a deliberate decision, and the item records that it was one.
            foreach (var plan in plans.Where(r => r.Conflicted)) {
               plan.Overridden = true;
               plan.ToApply.AddRange(plan.ConflictedFields);
            }
         }

         // 2. Apply, in order. A key the module returns that differs from the proposed one is the real
         //    key of an entity that did not exist yet; later entities that carried the placeholder in one
         //    of their key parts get the real value, so a new parent and its children can share a request.
         var substitutions = new Dictionary<(string Name, string? Value), string?>();
         var stamp = DateTime.UtcNow;

         foreach (var plan in plans) {
            var item = plan.Item;
            var partNames = ApprovalKey.GetPartNames(plan.Entity.KeyType);
            var original = item.cApprovalRequestItemKey;

            var key = Substitute(original, partNames, substitutions);
            if (!string.Equals(key, original, StringComparison.Ordinal)) {
               SetKey(item, keyRows, key);
            }

            if (item.cApprovalRequestItemOperation == ApprovalItemOperation.Update && plan.ToApply.Count == 0) {
               item.cApprovalRequestItemStage = ApprovalItemStage.Skipped;
               item.ustamp = stamp;
               continue;
            }

            var applied = await plan.Entity.ApplyAsync(scope, key, item.cApprovalRequestItemOperation,
               [.. plan.ToApply.Select(f => new ApprovalDataField(f.cApprovalRequestItemFieldName,
                  f.cApprovalRequestItemFieldOldValue, f.cApprovalRequestItemFieldNewValue))]);

            if (!string.Equals(applied, key, StringComparison.Ordinal)) {
               var before = ApprovalKey.ReadParts(key);
               var after = ApprovalKey.ReadParts(applied);
               for (var i = 0; i < Math.Min(before.Length, after.Length) && i < partNames.Count; i++) {
                  if (!string.Equals(before[i], after[i], StringComparison.Ordinal)) {
                     substitutions[(partNames[i], before[i])] = after[i];
                  }
               }

               SetKey(item, keyRows, applied);

               // The request itself is about the entity that was just created: it is named by it from now on.
               if (string.Equals(request.cApprovalRequestDocKey, original, StringComparison.Ordinal)) {
                  request.cApprovalRequestDocKey = applied;
               }
            }

            item.cApprovalRequestItemStage = plan.Overridden ? ApprovalItemStage.Overridden : ApprovalItemStage.Applied;
            if (plan.Overridden) {
               item.cApprovalRequestItemNote =
                  $"Applied past a conflict on: {string.Join(", ", plan.ConflictedFields.Select(f => f.cApprovalRequestItemFieldName))}";
            }

            item.ustamp = stamp;
         }

         await ctx.SaveChangesAsync(cancellationToken);
         return Build(flow, items, fields);
      }

      /// <summary>
      /// Mencatat hasil perbandingan yang berujung konflik. Dipanggil sesudah transaksi keputusannya
      /// dibatalkan, supaya approver yang membuka request itu lagi melihat nilai sekarang dan entitas mana
      /// yang bertabrakan - keputusannya sendiri tidak tersimpan.
      /// </summary>
      /// <remarks>
      /// Pencatatan ini berjalan sesudah transaksi keputusan berakhir, jadi keputusan lain atas request yang
      /// sama bisa sudah masuk di antaranya. Setiap perubahan karenanya hanya mengenai entitas yang masih
      /// menunggu pada request yang masih menunggu; yang sudah diterapkan orang lain tidak disentuh. Seluruhnya
      /// satu transaksi, supaya hasilnya utuh atau tidak ada sama sekali.
      /// </remarks>
      public static async Task RecordConflictsAsync(ApiCoreContext ctx, IReadOnlyList<ApprovalComparedItem> compared,
         CancellationToken cancellationToken) {
         ctx.ChangeTracker.Clear();
         var stamp = DateTime.UtcNow;

         await using var transaction = await ctx.Database.BeginTransactionAsync(cancellationToken);

         foreach (var item in compared) {
            var itemId = item.ItemId;

            // Still waiting: the item has not been applied or skipped, and its request is still pending.
            var waiting = ctx.ta_ApprovalRequestItems.Where(r => r.cApprovalRequestItemId == itemId &&
               r.cApprovalRequestItemStage <= ApprovalItemStage.Pending &&
               ctx.ta_ApprovalRequests.Any(q => q.cApprovalRequestId == r.cApprovalRequestId &&
                                                q.cApprovalRequestStage == ApprovalStage.Pending));

            foreach (var (name, current) in item.Fields) {
               var fieldName = name;
               await ctx.ta_ApprovalRequestItemFields
                  .Where(r => r.cApprovalRequestItemId == itemId && r.cApprovalRequestItemFieldName == fieldName &&
                              waiting.Any())
                  .ExecuteUpdateAsync(s => s.SetProperty(r => r.cApprovalRequestItemFieldCurrentValue, current), cancellationToken);
            }

            var stage = item.Conflicted ? ApprovalItemStage.Conflicted : ApprovalItemStage.Pending;
            await waiting.ExecuteUpdateAsync(s => s
               .SetProperty(r => r.cApprovalRequestItemStage, stage)
               .SetProperty(r => r.ustamp, stamp), cancellationToken);
         }

         await transaction.CommitAsync(cancellationToken);
      }

      private static IReadOnlyList<ApprovalDataItem> Build(IApprovalDataFlow flow,
         IReadOnlyList<ta_ApprovalRequestItem> items, IReadOnlyList<ta_ApprovalRequestItemField> fields) =>
         [.. items.Select(item => {
            var entity = flow.FindEntity(item.cApprovalRequestItemEntity) ?? throw new ActionException(
               $"Entity '{item.cApprovalRequestItemEntity}' is not declared by the flow any more.", 409);

            return new ApprovalDataItem(item.cApprovalRequestItemEntity,
               ApprovalKey.FromCanonical(entity.KeyType, item.cApprovalRequestItemKey),
               item.cApprovalRequestItemOperation,
               [.. fields.Where(f => f.cApprovalRequestItemId == item.cApprovalRequestItemId)
                  .OrderBy(f => f.cApprovalRequestItemFieldOrder)
                  .Select(f => new ApprovalDataField(f.cApprovalRequestItemFieldName,
                     f.cApprovalRequestItemFieldOldValue, f.cApprovalRequestItemFieldNewValue))]);
         })];

      // Replaces the key parts that are a placeholder an earlier entity turned into a real value.
      private static string Substitute(string canonicalKey, IReadOnlyList<string> partNames,
         IReadOnlyDictionary<(string Name, string? Value), string?> substitutions) {
         if (substitutions.Count == 0) return canonicalKey;

         var parts = ApprovalKey.ReadParts(canonicalKey);
         var changed = false;
         for (var i = 0; i < parts.Length && i < partNames.Count; i++) {
            if (substitutions.TryGetValue((partNames[i], parts[i]), out var real)) {
               parts[i] = real;
               changed = true;
            }
         }

         return changed ? ApprovalKey.FromParts(parts) : canonicalKey;
      }

      private static void SetKey(ta_ApprovalRequestItem item, IReadOnlyList<ta_ApprovalRequestItemKey> keyRows,
         string canonicalKey) {
         item.cApprovalRequestItemKey = canonicalKey;

         var parts = ApprovalKey.ReadParts(canonicalKey);
         foreach (var row in keyRows.Where(r => r.cApprovalRequestItemId == item.cApprovalRequestItemId)) {
            var index = row.cApprovalRequestItemKeyOrder - 1;
            if (index >= 0 && index < parts.Length) row.cApprovalRequestItemKeyValue = parts[index];
         }
      }

      private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

      private static string DisplayKey(string canonicalKey) {
         try {
            return ApprovalKey.ToDisplay(canonicalKey);
         }
         catch (InvalidOperationException) {
            return canonicalKey;
         }
      }

      private sealed class ItemPlan(ta_ApprovalRequestItem item, ApprovalEntityDeclaration entity,
         List<ta_ApprovalRequestItemField> fields)
      {
         public ta_ApprovalRequestItem Item { get; } = item;
         public ApprovalEntityDeclaration Entity { get; } = entity;
         public List<ta_ApprovalRequestItemField> Fields { get; } = fields;

         /// <summary>Kolom yang diserahkan ke handler modul.</summary>
         public List<ta_ApprovalRequestItemField> ToApply { get; } = [];

         public List<ta_ApprovalRequestItemField> ConflictedFields { get; } = [];

         /// <summary>Kolom yang sudah dibandingkan beserta nilainya saat itu.</summary>
         public List<(string Name, string? Current)> Compared { get; } = [];

         public bool Conflicted { get; set; }
         public bool Overridden { get; set; }
      }
   }
}
