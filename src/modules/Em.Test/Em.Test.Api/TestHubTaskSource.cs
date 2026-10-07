using Em.Api.Core;
using Em.Api.Core.Hub;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Microsoft.EntityFrameworkCore;

namespace Em.Test.Api
{
   /// <summary>
   /// The hub task source of the test module: test documents that are still drafts and have not been
   /// submitted. It tests that a module can add kinds of tasks to the hub without changing the engine.
   /// </summary>
   internal sealed class TestHubTaskSource(TestDbContext ctx, ActionRequest request) : IHubTaskSource
   {
      public const string DraftSource = "test.drafts";

      public async Task<IReadOnlyList<HubTaskInfo>> GetTasksAsync(CancellationToken cancellationToken = default) {
         // The hub is built for everyone who is signed in, so a module has to decide for itself who gets to
         // see what it adds: here, only those who may open the module at all.
         var moduleName = ITestServices.ModuleName;
         var mayOpen = request.IsAdmin || request.IsDebugRequest ||
                       request.Claims.Any(r => string.Equals(r.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
         if (!mayOpen) return [];

         var oldest = await ctx.ta_TestDocs.Where(r => r.cTestDocStatus == TestDocStatus.Draft)
            .OrderBy(r => r.datestamp).Select(r => (DateTime?)r.datestamp).FirstOrDefaultAsync(cancellationToken);
         if (oldest is null) return [];

         var count = await ctx.ta_TestDocs.CountAsync(r => r.cTestDocStatus == TestDocStatus.Draft, cancellationToken);
         var age = DateTime.UtcNow - oldest.Value;

         return [new HubTaskInfo {
            Source = DraftSource,
            Id = "drafts",
            Title = "Test documents waiting to be submitted",
            Description = "Drafts created in the Em Test module that have not been sent for approval.",
            Count = count,
            OldestAge = age < TimeSpan.Zero ? TimeSpan.Zero : age,
            ActionName = "Open",
            NavigationTarget = "test.docs"
         }];
      }
   }
}
