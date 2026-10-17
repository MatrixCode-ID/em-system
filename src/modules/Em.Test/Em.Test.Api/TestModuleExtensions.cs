using Em.Api.Core.Approval;
using Em.Api.Shared;
using Em.Shared;
using Em.Test.Models;

namespace Em.Test.Api
{
   /// <summary>Installs the test module on the server.</summary>
   public static class TestModuleExtensions
   {
      /// <summary>
      /// Installs the test module: its data context, service, claims, two approval flows, and the hub task
      /// source. Prerequisites: the test module's table script has been run on the core database, and file
      /// storage (<c>AddLocalBinaryStorage</c>) has been turned on by the application for the approval PDFs.
      /// </summary>
      public static void AddTestModule(this EmAppBuilder builder) {
         ArgumentNullException.ThrowIfNull(builder);

         builder.AddDbContext<TestDbContext>();
         builder.AddService<ITestServices, TestServices>();
         // Audit example (see TestAudit); without AddLogServer the entries are discarded.
         TestAudit.Register(builder);
         builder.AddScoped<TestAudit>();

         // Claims first: the approval flows below register theirs only when the name is still free, so
         // the claim of the data approval ("Approve Item Change") is left to the flow on purpose.
         builder.AddClaims(
            ClaimAction.Create<TestServices>(ITestServices.RunClaim),
            ClaimAction.Create<TestServices>(ITestServices.EditItemsClaim),
            ClaimAction.Create<TestServices>(ITestServices.DeleteItemsClaim),
            ClaimAction.Create<TestServices>(ITestServices.ProbeClaim),
            ClaimAction.Create<TestServices>(ITestServices.ApproveItemClaim));

         builder.AddDataApproval<TestDocDataApproval>();
         builder.AddDataApproval<TestItemDataApproval>();
         builder.AddSystemApproval<TestSystemApproval, TestSystemApprovalHandler>();

         builder.AddHubTaskSource<TestHubTaskSource>();

         // Managed bucket example: created at startup when object storage is on, ignored when it is off.
         builder.AddManagedBucket(ITestServices.FilesBucket, "Em Test",
            "Sample files written by the Em Test module through IObjectStore to exercise managed buckets. Safe to empty " +
            "in a test installation; nothing outside the test module reads it. A small quota is enough.",
            o => {
               o.KeepVersions = 5;
               o.MaxObjectBytes = 20L * 1024 * 1024;
            });
      }

   }
}
