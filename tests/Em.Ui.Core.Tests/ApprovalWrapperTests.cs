using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core.Tests;
public sealed class ApprovalWrapperTests {
   [Fact]
   public void ResolverKeepsWrapperFallbackAndUnrelatedSourcesSeparate() {
      var catalog = new ApprovalWrapperCatalog(); catalog.Add(ApprovalKind.System, "Example", "example.approval");
      var task = new HubTaskInfo { NavigationTarget = "em.approval.manager", NavigationParameter = "System:Example" };
      var wrapped = catalog.Resolve(task)!;
      Assert.Equal("example.approval", wrapped.NavigationName);
      Assert.True(Assert.IsType<ApprovalWrapperNavigationPayload>(wrapped.Payload).WaitingForMeOnly);
      task.NavigationParameter = "Data:Other";
      var fallback = catalog.Resolve(task)!;
      Assert.Equal("em.approval.editor", fallback.NavigationName);
      Assert.Null(Assert.IsType<ApprovalManagerNavigationPayload>(fallback.Payload).ApprovalRequestId);
      task.NavigationTarget = "other.screen"; task.NavigationParameter = "opaque";
      Assert.Equal(new ApprovalHubNavigation("other.screen", "opaque"), catalog.Resolve(task));
   }
}
