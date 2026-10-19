using System.Windows;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
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
         "Discard the unsaved changes of this tenant?", "Unsaved Changes") == MessageBoxResult.Yes;
   }

   /// <summary>Screen view model.</summary>
   public BusinessModuleManagerVm Vm => (BusinessModuleManagerVm)DataContext;

   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.IsLoaded ? Task.CompletedTask : Vm.ReloadAsync();

   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

   /// <inheritdoc />
   public Task OnRelease(INavigation sender) => Task.CompletedTask;
}
