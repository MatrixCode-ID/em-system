using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core.Models;

namespace Em.Api.Core.Approval
{
   /// <summary>Note of a request's withdrawal: who, when, and why.</summary>
   /// <param name="UserId">The user who withdrew it.</param>
   /// <param name="Date">When it was withdrawn.</param>
   /// <param name="Reason">Reason for the withdrawal.</param>
   internal record ApprovalCancelRecord(string UserId, DateTime Date, string Reason);

   /// <summary>
   /// Content of a request's extra data column: the module's summary columns, plus the engine's own notes.
   /// </summary>
   /// <remarks>
   /// A single flat JSON object. The module's summary columns are written as-is as name and value pairs,
   /// so the request list can filter and sort them directly in the database. The engine's notes use names
   /// starting with <see cref="ReservedPrefix"/> - a prefix summary column names may not use - so the two
   /// never collide and the screen does not display the engine's notes as columns.
   /// </remarks>
   internal static class ApprovalRequestJson
   {
      /// <summary>Name prefix reserved for the engine's notes.</summary>
      public const string ReservedPrefix = "$";

      /// <summary>Name of the withdrawal note.</summary>
      public const string CancelKey = "$cancel";

      // Names of the standard columns of a request in the list. A summary column with one of these names
      // could not be told apart from it when the list is sorted, so the declaration is refused.
      private static readonly HashSet<string> StandardColumns = new(
         typeof(ApprovalRequestInfo).GetProperties().Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Only the module's summary columns, as JSON text. The engine's notes are dropped, because what shows
      /// on screen is only what the module states itself.
      /// </summary>
      /// <param name="json">Content of the request's extra data column.</param>
      /// <returns>The summary JSON object, or <c>null</c> when the module states none.</returns>
      public static string? ReadSummary(string? json) {
         if (Parse(json) is not { } node) return null;

         var summary = new JsonObject();
         foreach (var (name, value) in node) {
            if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal)) continue;
            summary[name] = value?.DeepClone();
         }

         return summary.Count == 0 ? null : summary.ToJsonString();
      }

      /// <summary>The withdrawal note, or <c>null</c> when this request was never withdrawn.</summary>
      /// <param name="json">Content of the request's extra data column.</param>
      public static ApprovalCancelRecord? ReadCancel(string? json) {
         if (Parse(json)?[CancelKey] is not JsonObject cancel) return null;

         var userId = cancel["userId"]?.GetValue<string>();
         var reason = cancel["reason"]?.GetValue<string>();
         if (userId is null || reason is null || cancel["date"] is not { } date) return null;

         return new ApprovalCancelRecord(userId, date.GetValue<DateTime>(), reason);
      }

      /// <summary>
      /// Composes the content of the extra data column from the module's summary and the engine's notes.
      /// </summary>
      /// <param name="summary">The module's summary columns, or <c>null</c> when there are none.</param>
      /// <param name="cancel">The withdrawal note, or <c>null</c>.</param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when a summary column name is empty, starts with <see cref="ReservedPrefix"/>, or equals the name of a standard column of the request list.
      /// </exception>
      public static string? Compose(IReadOnlyDictionary<string, string?>? summary, ApprovalCancelRecord? cancel) {
         var node = new JsonObject();

         foreach (var (name, value) in summary ?? new Dictionary<string, string?>()) {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(ReservedPrefix, StringComparison.Ordinal)) {
               throw new InvalidOperationException(
                  $"Summary column '{name}' is not allowed: a name must not be empty or start with '{ReservedPrefix}'.");
            }

            if (StandardColumns.Contains(name)) {
               throw new InvalidOperationException(
                  $"Summary column '{name}' is not allowed: it is the name of a standard column of the request list.");
            }

            node[name] = value;
         }

         if (cancel is not null) {
            node[CancelKey] = CancelNode(cancel);
         }

         return node.Count == 0 ? null : node.ToJsonString();
      }

      /// <summary>Adds or replaces the withdrawal note in existing column content.</summary>
      /// <param name="json">The request's previous extra data column content.</param>
      /// <param name="cancel">The withdrawal note to write.</param>
      public static string WithCancel(string? json, ApprovalCancelRecord cancel) {
         var node = Parse(json) ?? new JsonObject();
         node[CancelKey] = CancelNode(cancel);
         return node.ToJsonString();
      }

      private static JsonObject CancelNode(ApprovalCancelRecord cancel) => new() {
         ["userId"] = cancel.UserId,
         ["date"] = cancel.Date,
         ["reason"] = cancel.Reason
      };

      private static JsonObject? Parse(string? json) {
         if (string.IsNullOrWhiteSpace(json)) return null;

         try {
            return JsonNode.Parse(json) as JsonObject;
         }
         catch (JsonException) {
            // A malformed column must not take the whole list down; the row just has no summary.
            return null;
         }
      }
   }

   /// <summary>
   /// Copy of a step's box positions, frozen when the request was submitted, together with the input
   /// values given by the signer. This is the content of a step's extra data column.
   /// </summary>
   internal class ApprovalStepSnapshot
   {
      private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

      /// <summary>Position of the signature box, or <c>null</c> when this step has no box.</summary>
      public ApprovalSlotSnapshot? Slot { get; set; }

      /// <summary>The input boxes of this step, in order as declared by the module.</summary>
      public List<ApprovalFieldSnapshot> Fields { get; set; } = [];

      /// <summary>Reads the copy from its column content. An empty or corrupt column means no boxes at all.</summary>
      /// <param name="json">Content of the step's extra data column.</param>
      public static ApprovalStepSnapshot Read(string? json) {
         if (string.IsNullOrWhiteSpace(json)) return new ApprovalStepSnapshot();

         try {
            return JsonSerializer.Deserialize<ApprovalStepSnapshot>(json, Options) ?? new ApprovalStepSnapshot();
         }
         catch (JsonException) {
            return new ApprovalStepSnapshot();
         }
      }

      /// <summary>Writes the copy to the form stored in its column.</summary>
      public string Write() => JsonSerializer.Serialize(this, Options);
   }

   /// <summary>Position of a box, in millimeters from the top-left corner of the page.</summary>
   internal record ApprovalSlotSnapshot(double X, double Y, double Width, double Height, int Page)
   {
      /// <summary>Freezes a position from the module's declaration.</summary>
      /// <param name="slot">The position according to the declaration.</param>
      public static ApprovalSlotSnapshot From(ApprovalSlot slot) =>
         new(slot.X, slot.Y, slot.Width, slot.Height, slot.Page);

      /// <summary>Returns to the position form understood by the PDF drawer.</summary>
      public ApprovalSlot ToSlot() => new(X, Y, Width, Height, Page);
   }

   /// <summary>One input box: its kind and position are frozen at submission, its value is filled in when decided.</summary>
   internal class ApprovalFieldSnapshot
   {
      /// <summary>Kind of the box.</summary>
      public ApprovalInputFieldKind Kind { get; set; }

      /// <summary>Position of the box.</summary>
      public ApprovalSlotSnapshot Slot { get; set; } = new(0, 0, 0, 0, 1);

      /// <summary>The text drawn, for a text box. Empty until its step is decided.</summary>
      public string? Text { get; set; }

      /// <summary>Whether the box is checked, for a checkbox.</summary>
      public bool Checked { get; set; }
   }
}
