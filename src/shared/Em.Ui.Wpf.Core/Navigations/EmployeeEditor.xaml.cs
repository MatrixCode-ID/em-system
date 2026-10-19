using System.Windows;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

/// <summary>Reusable employee editor opened with a tenant and optional employee identity.</summary>
public partial class EmployeeEditor : UserControl, INavigationBody
{
   private EmployeeNavigationPayload? _payload;
   private bool _initialized;
   /// <summary>Creates an editor for designer and console XAML checks.</summary>
   public EmployeeEditor() { InitializeComponent(); }
   /// <summary>Creates the editor and attaches its view-owned dialogs.</summary>
   public EmployeeEditor(EmApp app) : this() {
      Vm.EmApp = app;
      Vm.ConfirmDiscard = () => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning("Discard the unsaved changes of this employee?", "Unsaved Changes") == MessageBoxResult.Yes;
      Vm.ConfirmHeadChange = () => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning("This department will lose its head. Continue?", "Department Head") == MessageBoxResult.Yes;
      Vm.PickContact = _ => {
         var dialog = new ContactPickerDialog(app, filter: Vm.BuildContactPickerFilter(), selectionBlockReason: Vm.GetContactSelectionBlockReason) { Owner = Window.GetWindow(this) ?? app.MainWindow };
         return dialog.ShowDialog() == true ? dialog.Vm.SelectedContact : null;
      };
   }
   /// <summary>State of this employee editor.</summary>
   public BusinessModuleManagerEmpVm Vm => (BusinessModuleManagerEmpVm)DataContext;
   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
      if (args.Data is not EmployeeNavigationPayload payload) { args.Cancel = true; args.Message = "Open the employee editor with a tenant and employee payload."; }
      else _payload = payload;
      return Task.CompletedTask;
   }
   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
      args.Cancel = Vm.IsBusy || !Vm.CanLeave(); return Task.CompletedTask;
   }
   /// <inheritdoc />
   public async Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
      if (!_initialized && _payload is { } payload) {
         await Vm.LoadEditorAsync(payload); _initialized = Vm.IsLoaded;
         if (Vm.IsSaved) Vm.NavigationEntry?.SetTitle("Employee: " + Vm.SelectedRow!.cTenantEmpId);
      }
      else await Vm.RefreshCommand();
   }
   /// <inheritdoc />
   public Task OnRelease(INavigation sender) => Task.CompletedTask;
}
