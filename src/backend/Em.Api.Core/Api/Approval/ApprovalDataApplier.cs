using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Approval
{
   /// <summary>
   /// What the engine knows about one entity after comparing its values with the table content: the value
   /// of each column at that moment, and whether any column collides.
   /// </summary>
   /// <param name="ItemId">The proposed entity that was compared.</param>
   /// <param name="Conflicted">Whether any column collides, or the entity no longer exists.</param>
   /// <param name="Fields">The value of each compared column at that moment.</param>
   internal sealed record ApprovalComparedItem(string ItemId, bool Conflicted,
      IReadOnlyList<(string Name, string? Current)> Fields);

   /// <summary>
   /// A data change proposal cannot be applied because the table content has changed outside the request,
   /// or the entity no longer exists.
   /// </summary>
   /// <remarks>
   /// Carries the colliding columns, so a decision that fails because of it can show the approver the old
   /// value, the current value, and the proposed value.
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

      /// <summary>The colliding columns.</summary>
      public IReadOnlyList<ApprovalConflictField> Conflicts { get; }

      /// <summary>Cannot be overwritten: an entity no longer exists.</summary>
      public bool IsFinal { get; }

      /// <summary>Result of comparing all entities, to be recorded after its transaction was rolled back.</summary>
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
   /// Applies a data change proposal: compares each column with the table content, then hands what may be
   /// applied to the module's handler.
   /// </summary>
   /// <remarks>
   /// Each column has three values: <b>old</b> (recorded at submission), <b>current</b> (the table content
   /// right now), and <b>proposed</b>. Current equal to old means the column is applied; current equal to
   /// proposed means the column is skipped because it already matches; anything else is a conflict. What
   /// is compared against is the table content, not other requests, so changes from outside the
   /// application are caught too.
   /// <para>
   /// All of it runs inside the caller's transaction. All entities are compared first, before any is
   /// applied, so a conflicting request is never half applied.
   /// </para>
   /// </remarks>
   internal static class ApprovalDataApplier
   {
      /// <summary>
      /// Reads a request's proposals as typed values, without applying anything.
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
      /// Compares then applies all proposals of a request.
      /// </summary>
      /// <param name="ctx">The core database context, currently inside the transaction.</param>
      /// <param name="flow">The flow of the document type.</param>
      /// <param name="scope">Material for handing over to the module's handler. The request must be tracked.</param>
      /// <param name="overrideConflicts">Still apply columns that collide.</param>
      /// <param name="note">Reason for the decision; required when colliding columns are overwritten.</param>
      /// <param name="cancellationToken">Cancellation token.</param>
      /// <returns>The proposals after being applied; the key of a new entity already holds the actual key.</returns>
      /// <exception cref="ApprovalConflictException">
      /// A column collides and overwriting was not requested, or an entity no longer exists.
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
      /// Records the result of a comparison that ended in a conflict. Called after the decision's transaction
      /// was rolled back, so an approver who opens that request again sees the current values and which
      /// entities collided - the decision itself is not saved.
      /// </summary>
      /// <remarks>
      /// This recording runs after the decision transaction ended, so another decision on the same request
      /// may have come in between. Every change therefore only touches entities that are still waiting on a
      /// request that is still waiting; anything already applied by someone else is left alone. All of it is
      /// one transaction, so the result is whole or nothing at all.
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

         /// <summary>Columns handed to the module's handler.</summary>
         public List<ta_ApprovalRequestItemField> ToApply { get; } = [];

         public List<ta_ApprovalRequestItemField> ConflictedFields { get; } = [];

         /// <summary>Columns that were compared together with their value at that moment.</summary>
         public List<(string Name, string? Current)> Compared { get; } = [];

         public bool Conflicted { get; set; }
         public bool Overridden { get; set; }
      }
   }
}
