using Em.Api.Core.Models;
using Em.Shared;
namespace Em.Test.Models;
/// <summary>A shared three-level flow with two parallel final steps.</summary>
[Approval(ITestServices.DocType)]
public sealed class TestSystemApproval : IEmApprovalSystem<string> {
   /// <inheritdoc />
   public string Title => "Test documents";
   /// <inheritdoc />
   public IReadOnlyList<ApprovalSystemStep> Steps => [new(1, "Prepared By"), new(2, "QA Check"), new(3, "Approved By A"), new(3, "Approved By B")];
   /// <inheritdoc />
   public IReadOnlyList<ClaimAction> VoidClaims => [new ClaimAction { ModuleName = ITestServices.ModuleName, Name = ITestServices.EditItemsClaim }];
}
/// <summary>One original and edited child row.</summary>
public sealed record TestItemEdit(ta_TestItem Original, ta_TestItem Modified);
/// <summary>One Data tree submission, including deferred child inserts.</summary>
public sealed record TestDocDataChange(ta_TestDoc? Original, ta_TestDoc Modified,
   ta_TestItem[] CreatedItems, TestItemEdit[] ChangedItems, ApprovalItemOperation Operation);
