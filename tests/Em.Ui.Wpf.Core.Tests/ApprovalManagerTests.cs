using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Navigations;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ApprovalManagerTests {
   private static EmApp NewApp(IServiceProvider services) {
      var app = (EmApp)Activator.CreateInstance(typeof(EmApp),
         BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<string>()], null)!;
      typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, services);
      return app;
   }

   private static ta_ApprovalRequest Row(string id, ApprovalKind kind = ApprovalKind.Data) => new() {
      cApprovalRequestId = id, cApprovalRequestKind = kind, cApprovalName = "Example",
      cSummary = "Request " + id, cApprovalRequestStage = ApprovalStage.Pending, datestamp = new(2026, 10, 17)
   };

   private static ApprovalManagerVm NewManager(EmApp app) {
      var vm = new ApprovalManagerVm();
      typeof(ApprovalManagerVm).GetProperty(nameof(vm.EmApp))!.SetValue(vm, app);
      return vm;
   }

   [Fact]
   public async Task ListLoadsPagesAndSelectionWithoutFetchingDetail() {
      var rows = new[] { Row("first"), Row("second", ApprovalKind.System) };
      var filters = new List<ApprovalQuery>();
      var service = DispatchProxy.Create<IApprovalServices, ApprovalServiceProxy>();
      ((ApprovalServiceProxy)(object)service).Handler = (method, args) => method.Name switch {
         nameof(IApprovalServices.GetMeta_ApprovalCapabilities) => Task.FromResult(Array.Empty<ApprovalCapability>()),
         nameof(IApprovalServices.PostGetMeta_ApprovalRequests) => Read((ApprovalQuery)args![0]!),
         _ => throw new InvalidOperationException("The list must not load request detail: " + method.Name)
      };
      Task<ApprovalPage> Read(ApprovalQuery query) { filters.Add(query); return Task.FromResult(new ApprovalPage(rows, 120, query.Page, 50)); }
      using var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      var vm = NewManager(NewApp(services));
      vm.Configure(null);
      await vm.RefreshAsync();
      Assert.Empty(vm.Error);
      Assert.Equal(rows, vm.Rows);
      Assert.Null(vm.Detail);
      Assert.False(vm.ShowDetails);
      vm.SelectedRequest = rows[1];
      Assert.Null(vm.Detail);
      Assert.True(vm.OpenRequestCommandAllowed(rows[1]));
      Assert.False(vm.OpenRequestCommandAllowed(null));
      await vm.NextCommand();
      Assert.Equal(2, filters[^1].Page);
      Assert.Equal(50, filters[^1].PageSize);
      Assert.Contains("120 requests", vm.PageLabel);
      Assert.NotEmpty(vm.RefreshedText);
   }

   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task DefinitionFilterCanReturnToAllAndStayThereAfterRefresh(bool startsFiltered) {
      var rows = new[] { Row("system", ApprovalKind.System), Row("data") };
      var filters = new List<ApprovalQuery>();
      ApprovalCapability[] definitions = [
         new(ApprovalKind.System, "Example", "Example", true, false, 0, [], []),
         new(ApprovalKind.Data, "Example", "Example", true, false, 0, [], [])
      ];
      var service = DispatchProxy.Create<IApprovalServices, ApprovalServiceProxy>();
      ((ApprovalServiceProxy)(object)service).Handler = (method, args) => method.Name switch {
         nameof(IApprovalServices.GetMeta_ApprovalCapabilities) => Task.FromResult(definitions),
         nameof(IApprovalServices.PostGetMeta_ApprovalRequests) => Read((ApprovalQuery)args![0]!),
         _ => throw new InvalidOperationException(method.Name)
      };
      Task<ApprovalPage> Read(ApprovalQuery query) {
         filters.Add(query);
         var items = rows.Where(x => (query.Kind is null || query.Kind == x.cApprovalRequestKind)
            && (query.Name is null || query.Name == x.cApprovalName)).ToArray();
         return Task.FromResult(new ApprovalPage(items, 100, query.Page, 50));
      }
      using var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      var vm = NewManager(NewApp(services));
      vm.Configure(startsFiltered ? new ApprovalManagerNavigationPayload { Kind = ApprovalKind.System, Name = "Example" } : null);
      await vm.RefreshAsync();
      Assert.Equal(startsFiltered ? ApprovalKind.System : (ApprovalKind?)null, filters[^1].Kind);
      foreach (var kind in new[] { ApprovalKind.Data, ApprovalKind.System }) {
         vm.SelectedDefinition = vm.Definitions.Single(x => x.Kind == kind);
         await vm.RefreshAsync();
         Assert.Equal(kind, filters[^1].Kind);
         Assert.Single(vm.Rows);
         await vm.NextCommand();
         vm.SelectedDefinition = vm.Definitions.Single(x => x.Kind is null && x.Name is null);
         await vm.RefreshAsync();
         Assert.Null(filters[^1].Kind);
         Assert.Null(filters[^1].Name);
         Assert.Equal(1, filters[^1].Page);
         Assert.Equal(2, vm.Rows.Count);
         Assert.Equal("All approvals", vm.SelectedDefinition!.Caption);
      }
   }

   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task DetailViewKeepsItsRequestAcrossRefresh(bool compact) {
      var first = Row("first"); var target = Row("target");
      var requested = new List<string>();
      var service = DispatchProxy.Create<IApprovalServices, ApprovalServiceProxy>();
      ((ApprovalServiceProxy)(object)service).Handler = (method, args) => method.Name switch {
         nameof(IApprovalServices.GetMeta_ApprovalCapabilities) => Task.FromResult(Array.Empty<ApprovalCapability>()),
         nameof(IApprovalServices.PostGetMeta_ApprovalRequests) => Task.FromResult(new ApprovalPage([first], 1, 1, 50)),
         nameof(IApprovalServices.GetMeta_ApprovalRequest) => Detail((string)args![0]!),
         _ => throw new InvalidOperationException(method.Name)
      };
      Task<ApprovalRequestDetail> Detail(string id) {
         requested.Add(id);
         return Task.FromResult(new ApprovalRequestDetail(target, [], [], [], [], [], [], [], false, false, false, false, false, [], []));
      }
      using var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
      var vm = NewManager(NewApp(services));
      vm.Configure(new ApprovalManagerNavigationPayload { Kind = ApprovalKind.Data, Name = "Example", Compact = compact, SystemViewer = !compact, ApprovalRequestId = "target", WaitingForMeOnly = false });
      await vm.RefreshAsync();
      await vm.RefreshAsync();
      Assert.Empty(vm.Error);
      Assert.True(vm.ShowDetails);
      Assert.False(vm.ShowFilters);
      Assert.Same(target, vm.Detail!.Header);
      Assert.All(requested, id => Assert.Equal("target", id));
   }

   [WpfFact]
   public async Task ContextMenuAndDoubleClickUseTheClickedRecordWithoutSelectingIt() {
      var view = new ApprovalManager();
      view.Vm.Configure(null);
      var selected = Row("selected"); var clicked = Row("clicked", ApprovalKind.System);
      view.Vm.Rows.Add(selected); view.Vm.Rows.Add(clicked);
      view.Vm.SelectedRequest = selected;
      view.Measure(new Size(1300, 700)); view.Arrange(new Rect(0, 0, 1300, 700)); view.UpdateLayout();
      await Dispatcher.Yield(DispatcherPriority.DataBind);
      var list = Assert.IsType<ListBox>(view.FindName("RequestList"));
      var item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(clicked));
      var menu = item.ContextMenu!;
      menu.PlacementTarget = item;
      menu.GetBindingExpression(FrameworkElement.DataContextProperty)!.UpdateTarget();
      menu.Visibility = Visibility.Visible;
      menu.ApplyTemplate(); menu.Measure(new Size(300, 100)); menu.Arrange(new Rect(0, 0, 300, 100));
      await Dispatcher.Yield(DispatcherPriority.DataBind);
      Assert.Same(view.Vm, item.Tag);
      Assert.Same(view.Vm, menu.DataContext);
      var open = Assert.IsType<MenuItem>(Assert.Single(menu.Items.Cast<object>()));
      // The popup stays closed; supply the ContextMenu ancestor that WPF attaches when it opens.
      foreach (var property in new[] { MenuItem.CommandProperty, MenuItem.CommandParameterProperty }) {
         var binding = BindingOperations.GetBinding(open, property)!;
         if (property == MenuItem.CommandParameterProperty) {
            Assert.Equal(typeof(ContextMenu), binding.RelativeSource.AncestorType);
            Assert.Equal("PlacementTarget.DataContext", binding.Path.Path);
            binding = new Binding(binding.Path.Path) { Source = menu };
         }
         BindingOperations.ClearBinding(open, property);
         BindingOperations.SetBinding(open, property, binding);
      }
      await Dispatcher.Yield(DispatcherPriority.DataBind);
      Assert.Equal("Open", open.Header);
      Assert.Same(menu.DataContext, open.DataContext);
      Assert.Same(view.Vm.Commands[nameof(view.Vm.OpenRequestCommand)], open.Command);
      Assert.Same(clicked, open.CommandParameter);
      var rowGrid = Descendants(item).OfType<Grid>().Single(x => x.InputBindings.Count > 0);
      var gesture = Assert.IsType<MouseBinding>(Assert.Single(rowGrid.InputBindings.Cast<InputBinding>()));
      Assert.Equal(MouseAction.LeftDoubleClick, gesture.MouseAction);
      Assert.Same(open.Command, gesture.Command);
      Assert.Same(clicked, gesture.CommandParameter);
      Assert.Same(selected, view.Vm.SelectedRequest);
      Assert.Equal(Visibility.Collapsed, ((Grid)view.FindName("RequestDetails")).Visibility);
   }

   [WpfTheory]
   [InlineData(ApprovalKind.Data)]
   [InlineData(ApprovalKind.System)]
   public async Task OpenCreatesIndependentEditorTabsForRequestsOfTheSameApproval(ApprovalKind kind) {
      using var services = new ServiceCollection().BuildServiceProvider();
      var app = NewApp(services);
      var stack = (NavigationStack)Activator.CreateInstance(typeof(NavigationStack),
         BindingFlags.Instance | BindingFlags.NonPublic, null, [app, null, true], null)!;
      typeof(EmApp).GetField("_mainStack", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, stack);
      var navigation = new Navigation { Name = "em.approval.editor", Title = "Approval", OrderIndex = 0, RequireParameter = true, Kind = NavigationKind.Editor, BodyType = BodyType.Of<EditorBody>() };
      typeof(EmApp).GetMethod("AddNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [navigation]);
      var vm = NewManager(app);
      vm.SelectedRequest = Row("selected");
      vm.Configure(null);
      var first = Row("first", kind); var second = Row("second", kind);
      await vm.OpenRequestCommand(first);
      await vm.OpenRequestCommand(second);
      Assert.Equal(2, stack.Entries.Count);
      Assert.NotEqual(stack.Entries[0].Title, stack.Entries[1].Title);
      var payload = Assert.IsType<ApprovalManagerNavigationPayload>(stack.Current!.Data);
      Assert.Equal("second", payload.ApprovalRequestId);
      Assert.Equal(kind, payload.Kind);
      Assert.True(payload.SystemViewer);
      Assert.False(payload.WaitingForMeOnly);
      await vm.OpenRequestCommand(first);
      Assert.Equal(2, stack.Entries.Count);
      Assert.Same(stack.Entries[0], stack.Current);
   }

   private static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
      for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
         var child = VisualTreeHelper.GetChild(root, i); yield return child;
         foreach (var nested in Descendants(child)) yield return nested;
      }
   }

   public class ApprovalServiceProxy : DispatchProxy {
      public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
      protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
   }

   public sealed class EditorBody : UserControl, INavigationBody {
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Task.CompletedTask;
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }
}
