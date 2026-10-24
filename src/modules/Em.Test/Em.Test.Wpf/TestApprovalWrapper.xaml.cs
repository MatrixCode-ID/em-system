using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
namespace Em.Test.Wpf;
/// <summary>A module-owned wrapper that consumes approval data without embedding the engine manager.</summary>
public partial class TestApprovalWrapper : UserControl, INavigationBody {
   /// <summary>Creates the sample view.</summary>
   public TestApprovalWrapper() { InitializeComponent(); }
   private TestApprovalWrapperVm Vm => (TestApprovalWrapperVm)DataContext;
   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.RefreshCommand();
   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshCommand();
   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <inheritdoc />
   public Task OnRelease(INavigation sender) => Task.CompletedTask;
}
/// <summary>Sample wrapper state driven by server visibility.</summary>
public sealed class TestApprovalWrapperVm : MvvmModelBase {
   /// <summary>Creates the refresh command.</summary>
   public TestApprovalWrapperVm() { RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed); }
   /// <summary>Visible request rows.</summary>
   public ta_ApprovalRequest[] Rows { get => Get(Array.Empty<ta_ApprovalRequest>()); private set => Set(value); }
   /// <summary>Load status.</summary>
   public string Message { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Whether loading can start.</summary>
   public bool RefreshCommandAllowed() => !IsBusy;
   /// <summary>Loads the example approval's visible requests.</summary>
   public async Task RefreshCommand() {
      if (IsBusy || EmApp is null) return;
      IsBusy = true; Message = "Loading approvals…";
      try { Rows = (await EmApp.ServiceProvider.GetRequiredService<IApprovalServices>().PostGetMeta_ApprovalRequests(new(ApprovalKind.System, ITestServices.DocType, WaitingForMeOnly: true))).Items; Message = $"{Rows.Length} visible requests"; }
      catch (Exception e) { Message = e.Message; }
      finally { IsBusy = false; Commands[nameof(RefreshCommand)]?.RaiseCanExecuteChanged(); }
   }
}
