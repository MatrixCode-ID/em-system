using Em.Api.Core.Approval;
using Em.Api.Shared;
using Em.Shared;
using Em.Test.Models;

namespace Em.Test.Api
{
   /// <summary>Pemasangan module uji ke server.</summary>
   public static class TestModuleExtensions
   {
      /// <summary>
      /// Memasang module uji: context datanya, service, claim, dua alur approval, dan sumber pekerjaan hub.
      /// Prasyarat: skrip tabel module uji sudah dijalankan pada database inti, dan penyimpanan berkas
      /// (<c>AddLocalBinaryStorage</c>) sudah dinyalakan aplikasi untuk PDF approval.
      /// </summary>
      public static void AddTestModule(this EmAppBuilder builder) {
         ArgumentNullException.ThrowIfNull(builder);

         builder.AddDbContext<TestDbContext>();
         builder.AddService<ITestServices, TestServices>();

         // Claims first: the approval flows below register theirs only when the name is still free, so
         // the claim of the data approval ("Approve Item Change") is left to the flow on purpose.
         builder.AddClaims(
            ClaimAction.Create<TestServices>(ITestServices.RunClaim),
            ClaimAction.Create<TestServices>(ITestServices.EditItemsClaim),
            ClaimAction.Create<TestServices>(ITestServices.DeleteItemsClaim),
            ClaimAction.Create<TestServices>(ITestServices.ProbeClaim));

         AddItemApproval(builder);
         AddDocumentApproval(builder);

         builder.AddHubTaskSource<TestHubTaskSource>();
      }

      // Data approval: a change to an item waits in the request and only reaches the table once approved.
      private static void AddItemApproval(EmAppBuilder builder) =>
         builder.AddDataApproval<TestServices>(ITestServices.ItemDocType, ITestServices.ApproveItemClaim, flow =>
            flow.Entity<TestItemKey>("Item",
                  (c, key) => c.Services.LoadItemAsync(key.ItemId),
                  (c, request) => c.Services.ApplyItemAsync(request))
               .Summary(c => c.Services.SummarizeItemChangeAsync(c)));

      // Document approval with the shapes the engine knows: a step signed by the requester, a step with
      // input, a four-eyes rule, a guard that can be overridden, two steps in parallel, and a stamped PDF.
      private static void AddDocumentApproval(EmAppBuilder builder) =>
         builder.AddDocumentApproval<TestServices, TestDocKey>(ITestServices.DocType, flow => {
            var qaInput = new StepInput<TestServices, TestDocKey, TestQaPayload> {
               Validate = (_, payload) => !payload.Passed && string.IsNullOrWhiteSpace(payload.Remarks)
                  ? throw new ActionException("Remarks are required when QA does not pass.", 400)
                  : Task.CompletedTask,
               OnSigned = (c, payload) => c.Services.SaveQaAsync(c.DocKey.DocId, payload)
            }.Check(TestSlots.QaPassedBox, payload => payload.Passed)
               .Text(TestSlots.QaRemarks, payload => payload.Remarks);

            flow.Level(1, level => level.Step("Prepared By", TestSlots.PreparedBy))
               .Level(2, level => level.Step("QA Check", TestSlots.QaCheck,
                  distinctFrom: "Prepared By",
                  guard: c => c.Services.GuardAmountAsync(c.DocKey.DocId),
                  input: qaInput))
               .Level(3, level => {
                  level.Step("Approved By A", TestSlots.ApprovedByA);
                  level.Step("Approved By B", TestSlots.ApprovedByB);
               });

            flow.Pdf(c => c.Services.RenderDocPdfAsync(c.DocKey.DocId))
               .Summary(c => c.Services.SummarizeDocAsync(c.DocKey.DocId))
               .RequireOpen()
               .OnSubmitted(c => c.Services.SetDocStatusAsync(c.DocKey.DocId, TestDocStatus.InApproval))
               .OnFinishing(c => c.Services.SetDocStatusAsync(c.DocKey.DocId, TestDocStatus.Approved))
               .OnRejecting(c => c.Services.SetDocStatusAsync(c.DocKey.DocId, TestDocStatus.Rejected))
               .OnReinstating((c, _) => c.Services.SetDocStatusAsync(c.DocKey.DocId, TestDocStatus.Draft));
         });
   }
}
