using System.Windows;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
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
      Vm.Employees.PickContact = tenantId => {
         var dialog = new ContactPickerDialog(app, filter: Vm.Employees.BuildContactPickerFilter(), selectionBlockReason: Vm.Employees.GetContactSelectionBlockReason) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.SelectedContact : null;
      };
      Vm.Employees.ConfirmHeadChange = () => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         "This department will lose its head. Continue?", "Department Head") == MessageBoxResult.Yes;
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
