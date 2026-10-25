using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core.Tests;
public sealed class ApprovalWrapperTests {
   [Fact]
   public void DataWrapperUsesKindAndCaseInsensitiveNameAndRetainsViewerFallback() {
      var catalog = new ApprovalWrapperCatalog();
      catalog.Add(ApprovalKind.Data, "Example", "example.data");
      catalog.Add(ApprovalKind.System, "Example", "example.system");
      var resolved = catalog.Resolve(new HubTaskInfo { NavigationTarget = "em.approval.manager", NavigationParameter = "Data:example" })!;
      Assert.Equal("example.data", resolved.NavigationName);
      Assert.Equal(ApprovalKind.Data, Assert.IsType<ApprovalWrapperNavigationPayload>(resolved.Payload).Kind);
      Assert.Throws<InvalidOperationException>(() => catalog.Add(ApprovalKind.Data, "EXAMPLE", "duplicate"));
      var fallback = catalog.Resolve(new HubTaskInfo { NavigationTarget = "em.approval.manager", NavigationParameter = "Data:Unknown" })!;
      Assert.Equal("em.approval.editor", fallback.NavigationName);
      var payload = Assert.IsType<ApprovalManagerNavigationPayload>(fallback.Payload);
      Assert.Equal(ApprovalKind.Data, payload.Kind); Assert.True(payload.SystemViewer);
   }
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
