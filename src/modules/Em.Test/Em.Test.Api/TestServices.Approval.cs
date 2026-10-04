using System.Globalization;
using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Test.Api
{
   public partial class TestServices
   {
      private const string FieldCode = "Code";
      private const string FieldName = "Name";
      private const string FieldQty = "Qty";
      private const string FieldPrice = "Price";
      private const string FieldNote = "Note";

      private IApprovalEngine Engine =>
         GetService<IApprovalEngine>() ?? throw new InvalidOperationException("The approval engine is not available.");

      #region Document approval (actions)

      [PostAction]
      public async Task<string> PostGetMeta_TestDocSubmit(string cTestDocId, string? note) {
         var doc = await ctx.ta_TestDocs.Where(r => r.cTestDocId == cTestDocId).SingleOrDefaultAsync(AbortToken);
         if (doc is null) throw new ActionException("Document was not found.", 404);
         if (doc.cTestDocStatus != TestDocStatus.Draft) {
            throw new ActionException($"Only a draft can be submitted; this document is {doc.cTestDocStatus}.", 409);
         }

         // The version is the draft's last edit time, so editing a draft and submitting it again is a new
         // version instead of a duplicate of the one that was withdrawn.
         var version = doc.ustamp.Ticks.ToString(CultureInfo.InvariantCulture);
         return await Engine.SubmitAsync(ITestServices.DocType, new TestDocKey(doc.cTestDocId), version, note);
      }

      [GetAction]
      public async Task<Stream> GetMeta_TestDocPdf(string cTestDocId) => await RenderDocPdfAsync(cTestDocId);

      #endregion

      #region Document approval (flow handlers)

      internal async Task<Stream> RenderDocPdfAsync(string docId) {
         var doc = await ctx.ta_TestDocs.Where(r => r.cTestDocId == docId).SingleOrDefaultAsync(AbortToken)
                   ?? throw new ActionException("Document was not found.", 404);
         return TestPdf.Document(doc);
      }

      internal async Task<IReadOnlyDictionary<string, string?>> SummarizeDocAsync(string docId) {
         var doc = await ctx.ta_TestDocs.Where(r => r.cTestDocId == docId).SingleAsync();
         return new Dictionary<string, string?> {
            ["No"] = doc.cTestDocNo,
            ["Title"] = doc.cTestDocTitle,
            ["Amount"] = doc.cTestDocAmount.ToString("N2", CultureInfo.InvariantCulture)
         };
      }

      internal async Task SetDocStatusAsync(string docId, TestDocStatus status) {
         // The edit stamp is left alone on purpose: it is the version the request was submitted under, and a
         // status change made by the flow itself must not turn it into a different version.
         var rows = await ctx.ta_TestDocs.Where(r => r.cTestDocId == docId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.cTestDocStatus, status));
         if (rows == 0) throw new ActionException("Document was not found.", 404);
      }

      internal async Task SaveQaAsync(string docId, TestQaPayload payload) {
         var rows = await ctx.ta_TestDocs.Where(r => r.cTestDocId == docId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.cTestDocQaPassed, payload.Passed)
               .SetProperty(r => r.cTestDocQaRemarks, payload.Remarks));
         if (rows == 0) throw new ActionException("Document was not found.", 404);
      }

      internal async Task<ApprovalGuard> GuardAmountAsync(string docId) {
         var amount = await ctx.ta_TestDocs.Where(r => r.cTestDocId == docId)
            .Select(r => r.cTestDocAmount).SingleAsync();
         return amount > 0
            ? ApprovalGuard.Allow
            : ApprovalGuard.Block("The amount is zero, so QA has nothing to check yet.", "Approved By A");
      }

      #endregion

      #region Data approval (actions)

      [PostAction]
      public async Task<TestSubmitResult> PostGetMeta_TestItemSubmitChange(TestItemChange change) {
         ArgumentNullException.ThrowIfNull(change);
         var fields = new List<ApprovalDataField>();
         string itemId;

         switch (change.Operation) {
            case TestItemOperation.Create: {
               ValidateChange(change);
               if (await ctx.ta_TestItems.AnyAsync(r => r.cTestItemCode == change.Code.Trim())) {
                  throw new ActionException($"The item code '{change.Code}' is already used.", 409);
               }

               itemId = $"{Ulid.NewUlid()}";
               fields.AddRange(NewFields(change));
               break;
            }
            case TestItemOperation.Update: {
               ValidateChange(change);
               var row = await RequireItemAsync(change.ItemId);
               itemId = row.cTestItemId;
               fields.AddRange(ChangedFields(row, change));
               if (fields.Count == 0) throw new ActionException("Nothing changed.", 400);
               break;
            }
            case TestItemOperation.Delete: {
               var row = await RequireItemAsync(change.ItemId);
               itemId = row.cTestItemId;
               break;
            }
            default:
               throw new ActionException("The operation is not known.", 400);
         }

         var item = new ApprovalDataItem("Item", new TestItemKey(itemId), ToEngineOperation(change.Operation), fields);
         var result = await Engine.SubmitDataAsync(ITestServices.ItemDocType, new TestItemKey(itemId), [item]);
         return new TestSubmitResult {
            ApprovalRequestId = result.ApprovalRequestId,
            AppliedImmediately = result.AppliedImmediately
         };
      }

      #endregion

      #region Data approval (flow handlers)

      internal async Task<IReadOnlyDictionary<string, string?>?> LoadItemAsync(string itemId) {
         var row = await ctx.ta_TestItems.Where(r => r.cTestItemId == itemId).SingleOrDefaultAsync();
         if (row is null) return null;

         return new Dictionary<string, string?> {
            [FieldCode] = row.cTestItemCode,
            [FieldName] = row.cTestItemName,
            [FieldQty] = row.cTestItemQty.ToString(CultureInfo.InvariantCulture),
            [FieldPrice] = row.cTestItemPrice.ToString(CultureInfo.InvariantCulture),
            [FieldNote] = row.cTestItemNote
         };
      }

      internal async Task<TestItemKey> ApplyItemAsync(ApprovalApplyRequest<TestItemKey> request) {
         var itemId = request.Key.ItemId;
         var now = DateTime.UtcNow;

         switch (request.Operation) {
            case ApprovalItemOperation.Create: {
               var row = new ta_TestItem {
                  cTestItemId = itemId,
                  cTestItemState = TestItemState.Active,
                  datestamp = now,
                  ustamp = now
               };
               ApplyFields(row, request.Fields);
               ctx.ta_TestItems.Add(row);
               break;
            }
            case ApprovalItemOperation.Update: {
               var row = await ctx.ta_TestItems.AsTracking().SingleOrDefaultAsync(r => r.cTestItemId == itemId)
                         ?? throw new ActionException("The item no longer exists.", 404);
               ApplyFields(row, request.Fields);
               row.ustamp = now;
               break;
            }
            case ApprovalItemOperation.Delete: {
               await ctx.ta_TestItems.Where(r => r.cTestItemId == itemId).ExecuteDeleteAsync();
               return request.Key;
            }
            default:
               throw new ActionException($"Operation {request.Operation} is not supported by this module.", 400);
         }

         await ctx.SaveChangesAsync();
         return request.Key;
      }

      internal Task<IReadOnlyDictionary<string, string?>> SummarizeItemChangeAsync(
         IApprovalDataContext<TestServices> context) {
         var item = context.Items.FirstOrDefault();
         var code = item?.Fields.FirstOrDefault(r => r.Name == FieldCode)?.NewValue
                    ?? item?.Fields.FirstOrDefault(r => r.Name == FieldCode)?.OldValue;
         IReadOnlyDictionary<string, string?> summary = new Dictionary<string, string?> {
            ["Change"] = item?.Operation.ToString(),
            ["Code"] = code,
            ["Fields"] = item?.Fields.Count.ToString(CultureInfo.InvariantCulture)
         };
         return Task.FromResult(summary);
      }

      #endregion

      #region Approval helpers

      private async Task<ta_TestItem> RequireItemAsync(string? itemId) {
         if (string.IsNullOrWhiteSpace(itemId)) throw new ActionException("The item id is required.", 400);
         return await ctx.ta_TestItems.Where(r => r.cTestItemId == itemId).SingleOrDefaultAsync(AbortToken)
                ?? throw new ActionException("Item was not found.", 404);
      }

      private static void ValidateChange(TestItemChange change) {
         change.Code = (change.Code ?? string.Empty).Trim();
         change.Name = (change.Name ?? string.Empty).Trim();
         ValidateItem(new ta_TestItem {
            cTestItemCode = change.Code,
            cTestItemName = change.Name,
            cTestItemQty = change.Qty,
            cTestItemPrice = change.Price,
            cTestItemNote = change.Note
         });
      }

      private static IEnumerable<ApprovalDataField> NewFields(TestItemChange change) => [
         new(FieldCode, null, change.Code),
         new(FieldName, null, change.Name),
         new(FieldQty, null, change.Qty.ToString(CultureInfo.InvariantCulture)),
         new(FieldPrice, null, change.Price.ToString(CultureInfo.InvariantCulture)),
         new(FieldNote, null, change.Note)
      ];

      // Only the columns that really differ travel in the request: the engine compares old, current and
      // proposed per column, so a column that did not change must not take part in the comparison.
      private static IEnumerable<ApprovalDataField> ChangedFields(ta_TestItem row, TestItemChange change) {
         if (row.cTestItemCode != change.Code) yield return new(FieldCode, row.cTestItemCode, change.Code);
         if (row.cTestItemName != change.Name) yield return new(FieldName, row.cTestItemName, change.Name);
         if (row.cTestItemQty != change.Qty) {
            yield return new(FieldQty, row.cTestItemQty.ToString(CultureInfo.InvariantCulture),
               change.Qty.ToString(CultureInfo.InvariantCulture));
         }

         if (row.cTestItemPrice != change.Price) {
            yield return new(FieldPrice, row.cTestItemPrice.ToString(CultureInfo.InvariantCulture),
               change.Price.ToString(CultureInfo.InvariantCulture));
         }

         if (row.cTestItemNote != change.Note) yield return new(FieldNote, row.cTestItemNote, change.Note);
      }

      private static void ApplyFields(ta_TestItem row, IReadOnlyList<ApprovalDataField> fields) {
         foreach (var field in fields) {
            switch (field.Name) {
               case FieldCode: row.cTestItemCode = field.NewValue ?? string.Empty; break;
               case FieldName: row.cTestItemName = field.NewValue ?? string.Empty; break;
               case FieldQty: row.cTestItemQty = int.Parse(field.NewValue ?? "0", CultureInfo.InvariantCulture); break;
               case FieldPrice:
                  row.cTestItemPrice = decimal.Parse(field.NewValue ?? "0", CultureInfo.InvariantCulture);
                  break;
               case FieldNote: row.cTestItemNote = field.NewValue; break;
            }
         }
      }

      private static ApprovalItemOperation ToEngineOperation(TestItemOperation operation) => operation switch {
         TestItemOperation.Create => ApprovalItemOperation.Create,
         TestItemOperation.Update => ApprovalItemOperation.Update,
         _ => ApprovalItemOperation.Delete
      };

      #endregion
   }
}
