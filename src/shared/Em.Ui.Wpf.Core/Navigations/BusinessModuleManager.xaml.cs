using System.Windows;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

/// <summary>Business Module Manager: set up tenants (and, in later stages, business documents).</summary>
public partial class BusinessModuleManager : UserControl, INavigationBody
{
   /// <summary>Creates the Business Module Manager.</summary>
   public BusinessModuleManager(EmApp app) {
      InitializeComponent();
      Vm.EmApp = app;
      Vm.ConfirmDiscard = () => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         "Discard the unsaved changes of this record?", "Unsaved Changes") == MessageBoxResult.Yes;
      Vm.Positions.PickCopy = () => {
         var positions = Vm.Positions;
         if (positions.SelectedTenant is not { } target) return null;
         var service = app.ServiceProvider.GetRequiredService<ITenantPositionServices>();
         var dialog = new CopyPositionsDialog(service, target, positions.TenantChoices, positions.Rows) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.Result : null;
      };
      Vm.AttCodes.PickCopy = () => {
         var codes = Vm.AttCodes;
         if (codes.SelectedTenant is not { } target) return null;
         var service = app.ServiceProvider.GetRequiredService<ITenantAttCodeServices>();
         var dialog = new CopyAttCodesDialog(service, target, codes.TenantChoices, codes.Rows) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.Result : null;
      };
      Vm.Holidays.ConfirmRemove = holiday => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         $"Remove {holiday.cPublicHolidayName} from the calendar?", "Remove Holiday") == MessageBoxResult.Yes;
      Vm.Units.ConfirmDelete = unit => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         $"Delete unit {unit.cUnitId}? This cannot be undone.", "Delete Unit") == MessageBoxResult.Yes;
      Vm.ProductTypes.ConfirmDeactivate = type => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         $"Deactivate product type {type.cTenantProductTypeName}? Its products keep it; it is no longer offered to new products.", "Deactivate Product Type") == MessageBoxResult.Yes;
      Vm.Products.ConfirmDeactivate = product => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         $"Deactivate product {product.cTenantProductName} ({product.cTenantProductCode})? Its children keep their own state.", "Deactivate Product") == MessageBoxResult.Yes;
      Vm.Products.PickChildren = parent => {
         var service = app.ServiceProvider.GetRequiredService<ITenantProductServices>();
         var dialog = new AddProductChildrenDialog(service, parent) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.Result : null;
      };
      Vm.Products.PickCopy = target => {
         var service = app.ServiceProvider.GetRequiredService<ITenantProductServices>();
         var dialog = new CopyProductChildrenDialog(service, target) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.Result : null;
      };
      Vm.Employees.OpenEditor = payload => Vm.NavigationEntry?.NavigateTo(EmApp.EmployeeEditorNavigationName, payload) ?? Task.CompletedTask;
   }

   // Mouse selection is view-owned; the editor action itself remains one bound VM command.
   private void SelectEmployeeRow(object sender, System.Windows.Input.MouseButtonEventArgs args) => ((System.Windows.Controls.ListBoxItem)sender).IsSelected = true;

   // A right click selects the product first, so the context menu acts on the node under the pointer.
   private void SelectProductNode(object sender, System.Windows.Input.MouseButtonEventArgs args) {
      if (sender is FrameworkElement { DataContext: ProductNode { IsPlaceholder: false } node }) node.IsSelected = true;
   }

   // Mouse selection is view-owned; opening the product in the tree remains one bound VM command.
   private void OpenProductHit(object sender, System.Windows.Input.MouseButtonEventArgs args) {
      if (sender is FrameworkElement { DataContext: TenantProductSearchHit hit } && Vm.Products.Commands[nameof(BusinessModuleManagerProductVm.OpenHitCommand)] is { } command && command.CanExecute(hit))
         command.Execute(hit);
   }

   /// <summary>Screen view model.</summary>
   public BusinessModuleManagerVm Vm => (BusinessModuleManagerVm)DataContext;

   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.IsLoaded ? Task.CompletedTask : Vm.ReloadAsync();

   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) { args.Cancel = !Vm.CanSwitchTabs || !Vm.CanLeave(); return Task.CompletedTask; }

   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshCommand();

   /// <inheritdoc />
   public Task OnRelease(INavigation sender) => Task.CompletedTask;
}
