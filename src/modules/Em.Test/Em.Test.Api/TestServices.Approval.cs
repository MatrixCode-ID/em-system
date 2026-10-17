using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;
namespace Em.Test.Api;
public partial class TestServices {
   private IApprovalEngine Engine => GetService<IApprovalEngine>() ?? throw new InvalidOperationException("The approval engine is not available.");
   #region Meta's
   [PostAction]
   public async Task<string> PostGetMeta_TestDocSubmit(string cTestDocId, string? note) {
      var row = await ctx.ta_TestDocs.AsTracking().SingleOrDefaultAsync(x => x.cTestDocId == cTestDocId) ?? throw new ActionException("Document was not found.", 404);
      var id = await Engine.RequestAsync(ITestServices.DocType, cTestDocId, $"{row.cTestDocNo} · {row.cTestDocTitle} · {row.cTestDocAmount:N2}");
      var json = JsonNode.Parse(row.json_object ?? "{}")!.AsObject(); json["approvalRequestId"] = id;
      row.json_object = json.ToJsonString(); row.cTestDocStatus = TestDocStatus.InApproval; await ctx.SaveChangesAsync(); return id;
   }
   [GetAction]
   public async Task<Stream> GetMeta_TestDocPdf(string cTestDocId) => TestPdf.Document(await ctx.ta_TestDocs.SingleOrDefaultAsync(x => x.cTestDocId == cTestDocId) ?? throw new ActionException("Document was not found.", 404));
   [PostAction]
   public async Task<TestSubmitResult> PostGetMeta_TestItemSubmitChange(TestItemChange change) {
      DataChange root;
      var row = new ta_TestItem { cTestItemCode = change.Code, cTestItemName = change.Name, cTestItemQty = change.Qty, cTestItemPrice = change.Price, cTestItemNote = change.Note };
      if (change.Operation == TestItemOperation.Create) {
         var parent = new ta_TestDoc { cTestDocTitle = change.Name, cTestDocAmount = change.Qty * change.Price };
         root = DataChange.Create(parent).With(DataChange.Create(row));
      } else {
         var existing = await ctx.ta_TestItems.AsNoTracking().SingleOrDefaultAsync(x => x.cTestItemId == change.ItemId) ?? throw new ActionException("Item was not found.", 404);
         if (existing.cTestDocId is null) throw new ActionException("This independent CRUD item has no approval parent. Create a Data approval tree first.", 409);
         var parent = await ctx.ta_TestDocs.AsNoTracking().SingleAsync(x => x.cTestDocId == existing.cTestDocId);
         row.cTestItemId = existing.cTestItemId; row.cTestDocId = existing.cTestDocId;
         root = DataChange.Update(parent, parent).With(change.Operation == TestItemOperation.Delete ? DataChange.Delete(existing) :
            DataChange.Update(change.Original ?? throw new ActionException("The screen's original item is required.", 400), row));
      }
      var result = await Engine.SubmitDataAsync(root);
      return new TestSubmitResult { ApprovalRequestId = result.ApprovalRequestId, AppliedImmediately = result.AppliedImmediately };
   }
   [PostAction]
   public Task<ApprovalSubmitResult> PostGetMeta_TestDocDataChange(TestDocDataChange request) {
      var root = request.Operation switch {
         ApprovalItemOperation.Create => DataChange.Create(request.Modified),
         ApprovalItemOperation.Update => DataChange.Update(request.Original ?? throw new ActionException("An original root is required.", 400), request.Modified),
         ApprovalItemOperation.Delete => DataChange.Delete(request.Modified),
         ApprovalItemOperation.Restore => DataChange.Restore(request.Modified),
         _ => throw new ActionException("Unknown Data operation.", 400)
      };
      root.With(request.CreatedItems.Select(DataChange.Create).Concat(request.ChangedItems.Select(x => DataChange.Update(x.Original, x.Modified))).ToArray());
      return Engine.SubmitDataAsync(root);
   }
   [PostAction]
   public async Task PostMeta_TestDocQa(string id, bool passed, string? remarks) {
      var row = await ctx.ta_TestDocs.AsTracking().SingleOrDefaultAsync(x => x.cTestDocId == id) ?? throw new ActionException("Document was not found.", 404);
      row.cTestDocQaPassed = passed; row.cTestDocQaRemarks = remarks; await ctx.SaveChangesAsync();
   }
   [PostAction]
   public async Task PostMeta_TestDocApprovalLink(string id, string requestId) {
      var row = await ctx.ta_TestDocs.AsTracking().SingleOrDefaultAsync(x => x.cTestDocId == id) ?? throw new ActionException("Document was not found.", 404);
      var json = JsonNode.Parse(row.json_object ?? "{}")!.AsObject(); json["approvalRequestId"] = requestId;
      row.json_object = json.ToJsonString(); row.cTestDocStatus = TestDocStatus.InApproval;
      await ctx.SaveChangesAsync();
   }
   #endregion
}
