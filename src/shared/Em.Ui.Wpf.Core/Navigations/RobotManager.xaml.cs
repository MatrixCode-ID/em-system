using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

public partial class RobotManager : UserControl
{
   public RobotManager() { InitializeComponent(); }
   public RobotManagerVm Vm => (RobotManagerVm)DataContext;
}

public partial class RobotManagerVm : MvvmModelBase
{
   public RobotManagerVm() { RegisterRobotCommands(); }
   private IRobotServices Service => EmApp!.ServiceProvider.GetRequiredService<IRobotServices>();
   private RobotAccessManagerInfo[] _managers = [];
   public bool IsLoaded { get; private set; }
   private bool CanAct => IsNotBusy && IsLoaded;
   public bool HasNoResources => !_managers.Any(m => m.Resources.Length > 0);
   public string RegistryHost => CtnInput.RegistryHost(EmApp?.ActiveConnection?.Host);
   public bool IsInsecureHost => CtnInput.IsInsecureRemote(EmApp?.ActiveConnection?.Host);
   internal static string LocalTime(DateTime utc) => CtnInput.AsUtc(utc).ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);
   internal static string ServerMessage(ActionException x) => x.Message.Replace("Server Error: ", "");
   public Task ReloadAsync() => RunBusyAsync("Loading robots...", async () => {
      _managers = await Service.GetMeta_RobotManagers();
      await ReadRobotsAsync();
      IsLoaded = true;
      NotifyChanged(nameof(HasNoRobots));
      NotifyChanged(nameof(HasNoResources));
   });
      private async Task RunMutationAsync(string waiterText, string title, Func<Task> action, Func<Task> reread) {
         var owner = DialogOwner;
         await RunBusyAsync(waiterText, async () => {
            try {
               await action();
            }
            catch (ActionException x) when (x.StatusCode is 400 or 404 or 409) {
               owner?.ShowMboxWarning(ServerMessage(x), title);
            }
            finally {
               await reread();
            }
         });
      }

      // showOverlay: false menjaga IsBusy - semua command tetap mati - tanpa lapisan tunggu, untuk
      // pekerjaan yang menampilkan kemajuannya sendiri.
      private async Task RunBusyAsync(string waiterText, Func<Task> work, bool showOverlay = true) {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = true;
            InWaiting = showOverlay;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      // Semua command di sini bergantung pada fakta yang sama - sibuk, registry mati, baris terpilih -
      // jadi dievaluasi ulang sebagai satu himpunan.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

}
