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
   /// <summary>Creates a new instance of <see cref="RobotManager"/>.</summary>
   public RobotManager() { InitializeComponent(); }
   /// <summary>The vm.</summary>
   public RobotManagerVm Vm => (RobotManagerVm)DataContext;
}

/// <summary>View model of the robot manager screen.</summary>
public partial class RobotManagerVm : MvvmModelBase
{
   /// <summary>Creates a new instance of <see cref="RobotManagerVm"/>.</summary>
   public RobotManagerVm() { RegisterRobotCommands(); }
   private IRobotServices Service => EmApp!.ServiceProvider.GetRequiredService<IRobotServices>();
   private RobotAccessManagerInfo[] _managers = [];
   /// <summary>Indicates loaded.</summary>
   public bool IsLoaded { get; private set; }
   private bool CanAct => IsNotBusy && IsLoaded;
   /// <summary>Indicates there is no resources.</summary>
   public bool HasNoResources => !_managers.Any(m => m.Resources.Length > 0);
   /// <summary>The registry host.</summary>
   public string RegistryHost => CtnInput.RegistryHost(EmApp?.ActiveConnection?.Host);
   /// <summary>Indicates insecure host.</summary>
   public bool IsInsecureHost => CtnInput.IsInsecureRemote(EmApp?.ActiveConnection?.Host);
   internal static string LocalTime(DateTime utc) => CtnInput.AsUtc(utc).ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);
   internal static string ServerMessage(ActionException x) => x.Message.Replace("Server Error: ", "");
   /// <summary>Reads the robots and their access rights again.</summary>
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

      // showOverlay: false keeps IsBusy - all commands stay off - without the wait layer, for work that shows
      // its own progress.
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

      // All commands here depend on the same facts - busy, registry off, selected row - so they are
      // re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

}
