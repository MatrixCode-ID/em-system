using Em.Api.Core;
using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
namespace Em.Test.Api;
/// <summary>Data root demonstrating deferred keys, UI-only columns, and enum soft deletion.</summary>
[Approval(ITestServices.ItemDocType)]
public sealed class TestDocDataApproval : IEmApprovalData<ta_TestDoc> {
   public string[] KeyColumns => [nameof(ta_TestDoc.cTestDocId)];
   public string[] TrackedColumns => [nameof(ta_TestDoc.cTestDocTitle), nameof(ta_TestDoc.cTestDocAmount), nameof(ta_TestDoc.cTestDocQaRemarks)];
   public string[] ApproverOnlyColumns => [nameof(ta_TestDoc.cTestDocAmount)];
   public string Title => "Test data tree";
   public ApprovalDeleteMode DeleteMode => ApprovalDeleteMode.Soft;
   public string DeleteStateColumn => nameof(ta_TestDoc.cTestDocStatus);
   public object DeletedStateValue => TestDocStatus.Deleted;
   public object ActiveStateValue => TestDocStatus.Draft;
   public bool CanView(ActionRequest caller) => new[] { ITestServices.RunClaim, ITestServices.EditItemsClaim, ITestServices.ApproveItemClaim }
      .Any(claim => ApprovalAccess.HoldsClaim(caller, ClaimAction.Create<TestServices>(claim)));
   public bool CanDecide(ActionRequest caller) => ApprovalAccess.HoldsClaim(caller, ClaimAction.Create<TestServices>(ITestServices.ApproveItemClaim));
   public bool CanWithdraw(ActionRequest caller) => CanDecide(caller);
   public Task CreateKeyAsync(ta_TestDoc entity, IEmApprovalDataContext context, CancellationToken ct = default) {
      entity.cTestDocId = Ulid.NewUlid().ToString();
      if (string.IsNullOrWhiteSpace(entity.cTestDocNo)) entity.cTestDocNo = "D-" + entity.cTestDocId;
      entity.datestamp = entity.ustamp = DateTime.Now; return Task.CompletedTask;
   }
}
/// <summary>Child insertion demonstrates propagation of the parent's key in the same transaction.</summary>
public sealed class TestItemDataApproval : IEmApprovalDataChild<ta_TestItem, ta_TestDoc> {
   public string[] KeyColumns => [nameof(ta_TestItem.cTestItemId)];
   public string[] ForeignKeyColumns => [nameof(ta_TestItem.cTestDocId)];
   public string[] TrackedColumns => [nameof(ta_TestItem.cTestItemCode), nameof(ta_TestItem.cTestItemName), nameof(ta_TestItem.cTestItemQty), nameof(ta_TestItem.cTestItemPrice), nameof(ta_TestItem.cTestItemNote)];
   public ApprovalDeleteMode DeleteMode => ApprovalDeleteMode.Hard;
   public Task CreateKeyAsync(ta_TestItem entity, IEmApprovalDataContext context, CancellationToken ct = default) {
      entity.cTestItemId = Ulid.NewUlid().ToString(); entity.datestamp = entity.ustamp = DateTime.Now; return Task.CompletedTask;
   }
}
/// <summary>Example Dynamic Signer resolution and post-commit notification hooks.</summary>
public sealed class TestSystemApprovalHandler(TestDbContext db, ILogger<TestSystemApprovalHandler> logger) : IEmApprovalSystemHandler<TestSystemApproval, string> {
   public async Task<ApprovalDynamicSigner?> ResolveDynamicSignerAsync(ApprovalSystemStep step, string key, ActionRequest requester, CancellationToken ct = default) {
      if (step.Name == "Prepared By") return new ApprovalDynamicSigner(requester.RequireUserId(), true);
      if (step.Name != "QA Check") return null;
      var json = await db.ta_TestDocs.Where(x => x.cTestDocId == key).Select(x => x.json_object).SingleOrDefaultAsync(ct);
      var id = json is null ? null : JsonNode.Parse(json)?["qaUserId"]?.GetValue<string>();
      return id is null ? null : new ApprovalDynamicSigner(id);
   }
   public async Task OnCompletedAsync(string requestId, string key, CancellationToken ct = default) {
      logger.LogInformation("Test System approval {RequestId} completed for {SubjectKey}.", requestId, key);
      await db.ta_TestDocs.Where(x => x.cTestDocId == key).ExecuteUpdateAsync(s => s.SetProperty(x => x.cTestDocStatus, TestDocStatus.Approved), ct);
   }
   public async Task OnRejectedAsync(string requestId, string key, CancellationToken ct = default) {
      logger.LogInformation("Test System approval {RequestId} rejected for {SubjectKey}.", requestId, key);
      await db.ta_TestDocs.Where(x => x.cTestDocId == key).ExecuteUpdateAsync(s => s.SetProperty(x => x.cTestDocStatus, TestDocStatus.Rejected), ct);
   }
}
