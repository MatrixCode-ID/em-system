using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Approval;

internal static class ApprovalValue {
   public static string? Normalize(object? value) => value switch {
      null => null,
      decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
      DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
      DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
      double number => number.ToString("R", CultureInfo.InvariantCulture),
      float number => number.ToString("R", CultureInfo.InvariantCulture),
      Enum item => Convert.ToInt64(item, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
      bool boolean => boolean ? "true" : "false",
      IFormattable item => item.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString()
   };
   public static object? Parse(string? text, Type type) {
      if (text is null) return null;
      type = Nullable.GetUnderlyingType(type) ?? type;
      if (type == typeof(string)) return text;
      if (type.IsEnum) return Enum.ToObject(type, long.Parse(text, CultureInfo.InvariantCulture));
      if (type == typeof(Guid)) return Guid.Parse(text);
      if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
      if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
      if (type == typeof(DateOnly)) return DateOnly.Parse(text, CultureInfo.InvariantCulture);
      if (type == typeof(TimeOnly)) return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
      return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
   }
}

internal sealed partial class ApprovalEngine {
   public async Task<ApprovalSubmitResult> SubmitDataAsync(DataChange root, string? note = null, CancellationToken ct = default) {
      ArgumentNullException.ThrowIfNull(root);
      var user = await RealUserAsync(ct);
      var registration = registry.DataEntity(root.Entity.GetType());
      if (registration.ParentType is not null) throw new ActionException("A Data request must start with a registered root entity.", 400);
      var table = registration.Resolve(services);
      if (!caller.IsAdmin && !table.CanView(caller)) throw new ActionException("You cannot submit changes for this approval.", 403);
      var flattened = new List<(DataChange Change, DataRegistration Definition, int? Parent)>();
      var seen = new HashSet<DataChange>();
      void Visit(DataChange change, int? parent) {
         if (!seen.Add(change)) throw new ActionException("A Data change tree cannot contain a cycle or repeated item.", 400);
         var def = registry.DataEntity(change.Entity.GetType());
         if (parent is not null && def.ParentType != flattened[parent.Value].Definition.EntityType)
            throw new ActionException("A child change does not match its declared parent entity.", 400);
         if (parent is not null && flattened[parent.Value].Change.Operation == ApprovalItemOperation.Create && change.Operation != ApprovalItemOperation.Create)
            throw new ActionException("Children of a new parent must also be Create changes.", 400);
         var index = flattened.Count; flattened.Add((change, def, parent));
         foreach (var child in change.Children) Visit(child, index);
      }
      Visit(root, null);
      await using var transaction = await ApprovalTransaction.BeginAsync(db, flattened.Select(x => x.Definition.ContextType).Distinct(), services, ct);
      try {
         var name = ApprovalSubjectKey.NameOf(registration.DefinitionType);
         var request = NewRequest(ApprovalKind.Data, name,
            root.Operation == ApprovalItemOperation.Create ? null : table.KeyOf(root.Entity),
            string.IsNullOrEmpty(table.Title) ? name : table.Title, user, note);
         var items = new List<ta_ApprovalRequestItem>(); var meaningful = false;
         foreach (var (change, def, parentIndex) in flattened) {
            var currentTable = def.Resolve(services);
            ValidateOperation(currentTable, change.Operation);
            var context = new ApprovalDataContext(currentTable.Context, caller, change.Operation);
            await currentTable.ValidateAsync(change.Entity, context, ct);
            if (change.Operation != ApprovalItemOperation.Create) EnsureKey(currentTable, change.Entity);
            if (change.Operation == ApprovalItemOperation.Update && (change.Original is null ||
                change.Original.GetType() != change.Entity.GetType() || currentTable.KeyOf(change.Original) != currentTable.KeyOf(change.Entity)))
               throw new ActionException("Original and modified entities must have the same type and key.", 400);
            var item = new ta_ApprovalRequestItem {
               cApprovalRequestItemId = Id(), cApprovalRequestId = request.cApprovalRequestId,
               cApprovalRequestItemOrder = items.Count, cApprovalRequestItemEntity = def.EntityName,
               cApprovalRequestItemOperation = change.Operation,
               cSubjectKey = change.Operation == ApprovalItemOperation.Create ? null : currentTable.KeyOf(change.Entity),
               cParentItem = parentIndex is null ? null : items[parentIndex.Value].cApprovalRequestItemId,
               json_object = change.Operation == ApprovalItemOperation.Create ? JsonSerializer.Serialize(change.Entity, def.EntityType, Defaults.ResponseJsonOptions) : null,
               datestamp = DateTime.Now, ustamp = DateTime.Now
            };
            items.Add(item); db.ta_ApprovalRequestItems.Add(item);
            if (change.Operation != ApprovalItemOperation.Update) meaningful = true;
            else foreach (var column in currentTable.Tracked) {
               var property = currentTable.Property(column);
               var oldValue = ApprovalValue.Normalize(property.GetValue(change.Original));
               var newValue = ApprovalValue.Normalize(property.GetValue(change.Entity));
               if (oldValue == newValue) continue;
               meaningful = true;
               db.ta_ApprovalRequestItemFields.Add(new ta_ApprovalRequestItemField {
                  cApprovalRequestItemFieldId = Id(), cApprovalRequestItemId = item.cApprovalRequestItemId,
                  cApprovalRequestItemFieldName = column, cApprovalRequestItemFieldOld = oldValue,
                  cApprovalRequestItemFieldNew = newValue, datestamp = DateTime.Now, ustamp = DateTime.Now
               });
            }
         }
         if (!meaningful) throw new ActionException("No tracked values have changed.", 400);
         await db.SaveChangesAsync(ct);
         var applied = caller.IsAdmin || table.CanDecide(caller);
         if (applied) {
            if (!await ApplyDataAsync(request, [], ct)) throw new ActionException("The data changed after it was loaded. Reload before saving.", 409);
            Complete(request, true); await SignatureAsync(request, null, user, true, note, caller.IsAdmin, false, ct);
            await db.SaveChangesAsync(ct);
         }
         await transaction.CommitAsync(ct);
         return new ApprovalSubmitResult(request.cApprovalRequestId, applied);
      }
      catch { await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
   }
   private static void ValidateOperation(DataTable table, ApprovalItemOperation operation) {
      if (!Enum.IsDefined(operation)) throw new ActionException("Unknown Data operation.", 400);
      if (operation == ApprovalItemOperation.Delete && table.DeleteMode == ApprovalDeleteMode.None)
         throw new ActionException("Deletion is not supported for this entity.", 400);
      if (operation == ApprovalItemOperation.Restore && table.DeleteMode != ApprovalDeleteMode.Soft)
         throw new ActionException("Only soft-deleted entities can be restored.", 400);
   }
   private static void EnsureKey(DataTable table, object row) {
      if (table.Keys.Any(k => table.Property(k).GetValue(row) is null or ""))
         throw new ActionException($"A key was not assigned for '{table.Registration.EntityName}'.", 409);
   }
   private object DeserializeCreate(DataTable table, string? json) {
      try {
         var options = new JsonSerializerOptions(Defaults.ResponseJsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
         using var document = JsonDocument.Parse(json ?? throw new JsonException("Missing Create snapshot."));
         foreach (var property in table.Context.Model.FindEntityType(table.Registration.EntityType)!.GetProperties().Where(p => p.PropertyInfo is not null)) {
            var name = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            if (!document.RootElement.TryGetProperty(name, out _)) throw new JsonException($"Missing property '{name}'.");
         }
         return JsonSerializer.Deserialize(json!, table.Registration.EntityType, options) ?? throw new JsonException("Empty snapshot.");
      }
      catch (JsonException exception) { throw new ActionException($"The stored Create snapshot no longer matches '{table.Registration.EntityName}': {exception.Message}", 409); }
   }
   private async Task<bool> ApplyDataAsync(ta_ApprovalRequest request, ApprovalFieldOverride[] overrides, CancellationToken ct) {
      var items = await db.ta_ApprovalRequestItems.Where(x => x.cApprovalRequestId == request.cApprovalRequestId).OrderBy(x => x.cApprovalRequestItemOrder).ToArrayAsync(ct);
      var ids = items.Select(x => x.cApprovalRequestItemId).ToArray();
      var fields = await db.ta_ApprovalRequestItemFields.Where(x => ids.Contains(x.cApprovalRequestItemId)).ToArrayAsync(ct);
      var rows = new Dictionary<string, object>(); var tables = new Dictionary<string, DataTable>();
      var blocked = false;
      foreach (var item in items) {
         var table = registry.DataEntity(item.cApprovalRequestItemEntity).Resolve(services);
         tables.Add(item.cApprovalRequestItemId, table); ValidateOperation(table, item.cApprovalRequestItemOperation);
         object? row = item.cApprovalRequestItemOperation == ApprovalItemOperation.Create ? DeserializeCreate(table, item.json_object) : await table.FindAsync(item.cSubjectKey!, ct);
         if (row is null) throw new ActionException($"The entity '{item.cApprovalRequestItemEntity}' no longer exists. This request can only be rejected or withdrawn.", 409);
         rows.Add(item.cApprovalRequestItemId, row);
         foreach (var field in fields.Where(x => x.cApprovalRequestItemId == item.cApprovalRequestItemId)) {
            var current = ApprovalValue.Normalize(table.Property(field.cApprovalRequestItemFieldName).GetValue(row));
            field.cApprovalRequestItemFieldCurrent = current;
            field.cApprovalRequestItemFieldConflict = current != field.cApprovalRequestItemFieldOld && current != field.cApprovalRequestItemFieldNew;
            var replacement = overrides.SingleOrDefault(x => x.ItemId == item.cApprovalRequestItemId && x.Column == field.cApprovalRequestItemFieldName);
            field.cApprovalRequestItemFieldOverrideReason = null;
            if (field.cApprovalRequestItemFieldConflict) {
               if (replacement is null || string.IsNullOrWhiteSpace(replacement.Reason)) blocked = true;
               else { RequireText(replacement.Reason, "Override reason", 1000); field.cApprovalRequestItemFieldOverrideReason = replacement.Reason; }
            }
         }
      }
      if (blocked) return false;
      foreach (var item in items) {
         var table = tables[item.cApprovalRequestItemId]; var row = rows[item.cApprovalRequestItemId];
         if (item.cParentItem is { } parentId) {
            var parent = rows[parentId]; var parentTable = tables[parentId];
            for (var i = 0; i < table.ForeignKeys.Length; i++) {
               var property = table.Property(table.ForeignKeys[i]); var parentKey = parentTable.Property(parentTable.Keys[i]).GetValue(parent);
               if (item.cApprovalRequestItemOperation == ApprovalItemOperation.Create) property.SetValue(row, parentKey);
               else if (!Equals(property.GetValue(row), parentKey)) throw new ActionException("The child does not belong to the submitted parent.", 409);
            }
         }
         var context = new ApprovalDataContext(table.Context, caller, item.cApprovalRequestItemOperation);
         switch (item.cApprovalRequestItemOperation) {
            case ApprovalItemOperation.Create:
               await table.CreateKeyAsync(row, context, ct); EnsureKey(table, row);
               await table.ValidateAsync(row, context, ct); table.Context.Add(row); break;
            case ApprovalItemOperation.Update:
               foreach (var field in fields.Where(x => x.cApprovalRequestItemId == item.cApprovalRequestItemId)) {
                  var property = table.Property(field.cApprovalRequestItemFieldName);
                  property.SetValue(row, ApprovalValue.Parse(field.cApprovalRequestItemFieldNew, property.PropertyType));
               }
               await table.ValidateAsync(row, context, ct); break;
            case ApprovalItemOperation.Delete when table.DeleteMode == ApprovalDeleteMode.Hard:
               await table.ValidateAsync(row, context, ct); table.Context.Remove(row); break;
            default:
               var state = table.Property(table.StateColumn!);
               var deleted = item.cApprovalRequestItemOperation == ApprovalItemOperation.Delete;
               object? stateValue = (Nullable.GetUnderlyingType(state.PropertyType) ?? state.PropertyType) == typeof(bool)
                  ? deleted : deleted ? table.DeletedState : table.ActiveState;
               state.SetValue(row, ApprovalValue.Parse(ApprovalValue.Normalize(stateValue), state.PropertyType));
               if (table.StampColumn is { } stamp) table.Property(stamp).SetValue(row, deleted ? DateTime.Now : null);
               await table.ValidateAsync(row, context, ct); break;
         }
         await table.Context.SaveChangesAsync(ct);
         item.cSubjectKey = table.KeyOf(row);
         if (item.cApprovalRequestItemOperation == ApprovalItemOperation.Create) item.json_object = null;
         item.ustamp = DateTime.Now;
      }
      request.cSubjectKey = items[0].cSubjectKey;
      return true;
   }
   private async Task<ApprovalDecisionResult> DecideDataAsync(ApprovalDecision decision, string user, CancellationToken ct) {
      var header = await LoadAsync(decision.RequestId, false, ct);
      var table = registry.DataRoot(header.cApprovalName);
      bool InTree(DataRegistration definition) {
         while (definition.ParentType is not null) definition = registry.DataEntity(definition.ParentType);
         return ReferenceEquals(definition, table);
      }
      await using var transaction = await ApprovalTransaction.BeginAsync(db, registry.Data.Where(InTree).Select(x => x.ContextType).Distinct(), services, ct);
      try {
         var request = await LoadAsync(decision.RequestId, true, ct);
         if (request.cApprovalRequestStage != ApprovalStage.Pending) throw new ActionException("This request is no longer pending.", 409);
         if (!CanDecideData(request)) throw new ActionException("You cannot decide this Data approval.", 403);
         if (decision.Approve && !await ApplyDataAsync(request, decision.Overrides ?? [], ct)) {
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new ApprovalDecisionResult(false, "Current values conflict with the proposal. Review them and supply a reason for each override.");
         }
         Complete(request, decision.Approve);
         await SignatureAsync(request, null, user, decision.Approve, decision.Note, caller.IsAdmin, false, ct);
         await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
         return new ApprovalDecisionResult(true);
      }
      catch { await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
   }
}
