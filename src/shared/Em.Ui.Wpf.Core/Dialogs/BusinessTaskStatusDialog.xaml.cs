using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Status dialog of one personal business task, opened from the task hub in the main window. While the
   /// task runs this dialog shows its progress and can cancel it; once it finishes it offers its result
   /// (download the file, or open the JSON data), its error message, and clearing it.
   /// </summary>
   public partial class BusinessTaskStatusDialog : EmWindow
   {
      /// <summary>
      /// Creates the status dialog for task <paramref name="task"/>.
      /// </summary>
      /// <param name="app">The application that owns the dialog.</param>
      /// <param name="task">The last known snapshot of the task; the dialog reloads by itself while the task is live.</param>
      public BusinessTaskStatusDialog(EmApp app, BusinessTaskInfo task) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Show(task);
         Vm.RequestClose += result => DialogResult = result;
         // The dialog polls only while it is open; there is no bindable lifetime to hang that on.
         Loaded += (_, _) => Vm.StartPolling();
         Closed += (_, _) => Vm.StopPolling();
      }

      /// <summary>The view model of this dialog.</summary>
      public BusinessTaskStatusDialogVm Vm => (BusinessTaskStatusDialogVm)DataContext;
   }

   /// <summary>
   /// View model for <see cref="BusinessTaskStatusDialog"/>.
   /// </summary>
   public class BusinessTaskStatusDialogVm : MvvmModelBase
   {
      private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

      private readonly DispatcherTimer _pollTimer;
      private bool _polling;

      /// <summary>
      /// Creates a new view model and registers all commands of the dialog.
      /// </summary>
      public BusinessTaskStatusDialogVm() {
         RegisterCommand(nameof(CancelTaskCommand), CancelTaskCommand, CancelTaskCommandAllowed);
         RegisterCommand(nameof(ClearTaskCommand), ClearTaskCommand, ClearTaskCommandAllowed);
         RegisterCommand(nameof(DownloadResultCommand), DownloadResultCommand, DownloadResultCommandAllowed);
         RegisterCommand(nameof(OpenResultCommand), OpenResultCommand, OpenResultCommandAllowed);

         _pollTimer = new DispatcherTimer { Interval = PollInterval };
         _pollTimer.Tick += async (_, _) => await PollAsync();
      }

      /// <summary>
      /// Raised when the dialog is about to close; <c>true</c> after its task has been cleared.
      /// </summary>
      public event Action<bool>? RequestClose;

      private IBusinessTaskServices Service => EmApp!.ServiceProvider.GetRequiredService<IBusinessTaskServices>();

      #region Data

      /// <summary>The task that is shown.</summary>
      public BusinessTaskItem? Item {
         get => Get<BusinessTaskItem?>();
         private set => Set(value);
      }

      /// <summary>
      /// <c>true</c> when the server no longer knows this task: it was cleared, or it disappeared by itself
      /// after its display period passed. The dialog keeps showing its last snapshot.
      /// </summary>
      public bool IsGone {
         get => Get<bool>();
         private set => Set(value, _ => Refresh());
      }

      /// <summary>The sentence of the state of a finished task; empty while the task is live or failed.</summary>
      public string StateMessage {
         get {
            if (Item is not { } item) return "";
            if (IsGone) return "This task is no longer on the server.";

            return item.Status switch {
               BusinessTaskStatus.Succeeded => item.Info.OutputKind switch {
                  BusinessTaskOutputKind.File =>
                     $"Finished. The result is the file '{item.Info.ResultFileName}'{SizeSuffix(item.Info.ResultSize)}.",
                  BusinessTaskOutputKind.Json => "Finished. The result is a set of data.",
                  _ => "The task finished successfully."
               },
               BusinessTaskStatus.Canceled => "The task was canceled.",
               _ => ""
            };
         }
      }

      /// <summary><c>true</c> when <see cref="StateMessage"/> has content.</summary>
      public bool HasStateMessage => StateMessage.Length > 0;

      #endregion

      #region Commands

      /// <summary>Cancels the task after confirmation.</summary>
      public async Task CancelTaskCommand() {
         if (Item is not { } item || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel task '{item.Title}'?", "Cancel Task") != MessageBoxResult.Yes)
            return;

         await RunBusyAsync(async () => {
            await Service.PostMeta_BusinessTaskCancel(item.Id);
            await ReadAsync();
         });
      }

      /// <summary>While the task is live and the caller may cancel it.</summary>
      public bool CancelTaskCommandAllowed() => IsNotBusy && !IsGone && Item is { CanCancel: true };

      /// <summary>
      /// Clears the task, then closes the dialog. A task that has a stored result is confirmed first, because
      /// its result is deleted with it.
      /// </summary>
      public async Task ClearTaskCommand() {
         if (Item is not { } item || DialogOwner is not { } owner) return;
         if (item.CanDownload || item.CanOpen) {
            if (owner.ShowMboxDecideWarning($"Clear task '{item.Title}'?\n\nIts stored result is deleted as well.",
                   "Clear Task") != MessageBoxResult.Yes) return;
         }

         var cleared = false;
         await RunBusyAsync(async () => {
            await Service.PostMeta_BusinessTaskClear(item.Id);
            cleared = true;
         });
         if (cleared) RequestClose?.Invoke(true);
      }

      /// <summary>For a finished task, if the caller may clear it.</summary>
      public bool ClearTaskCommandAllowed() => IsNotBusy && !IsGone && Item is { CanClear: true };

      /// <summary>Downloads the result file of the task.</summary>
      public Task DownloadResultCommand() =>
         Item is not { } item
            ? Task.CompletedTask
            : RunBusyAsync(() => BusinessTaskItem.DownloadResultAsync(Service, item, DialogOwner));

      /// <summary>For a successful task with a file result that the caller may read.</summary>
      public bool DownloadResultCommandAllowed() => IsNotBusy && !IsGone && Item is { CanDownload: true };

      /// <summary>
      /// Opens the JSON result of the task. Not available yet: how to open it will be decided once the need
      /// is visible, so for now the user is only told so.
      /// </summary>
      public void OpenResultCommand() =>
         DialogOwner?.ShowMboxInfo("Opening this result is not available yet.", "Open Result");

      /// <summary>For a successful task with a JSON result that the caller may read.</summary>
      public bool OpenResultCommandAllowed() => IsNotBusy && !IsGone && Item is { CanOpen: true };

      #endregion

      #region Methods

      /// <summary>Shows the snapshot of task <paramref name="task"/>.</summary>
      public void Show(BusinessTaskInfo task) {
         if (Item is { } item && item.Id == task.Id) item.Update(task);
         else Item = new BusinessTaskItem(task);
         Refresh();
      }

      /// <summary>Starts reloading the status every two seconds while the task is still live.</summary>
      public void StartPolling() {
         if (Item is { IsAlive: true }) _pollTimer.Start();
      }

      /// <summary>Stops the periodic reload.</summary>
      public void StopPolling() => _pollTimer.Stop();

      private async Task PollAsync() {
         if (EmApp is null || IsBusy || _polling) return;

         _polling = true;
         try {
            await ReadAsync();
         }
         catch (Exception) {
            // A failing poll has nobody to tell; the last snapshot stays on screen.
            _pollTimer.Stop();
         }
         finally {
            _polling = false;
         }
      }

      private async Task ReadAsync() {
         if (Item is not { } item) return;

         BusinessTaskInfo? fresh;
         try {
            fresh = await Service.GetMeta_BusinessTask(item.Id);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            fresh = null;
         }

         if (fresh is null) {
            IsGone = true;
            _pollTimer.Stop();
            return;
         }

         Show(fresh);
         if (fresh.IsAlive) _pollTimer.Start();
         else _pollTimer.Stop();
      }

      private async Task RunBusyAsync(Func<Task> work) {
         if (EmApp == null || IsBusy) return;

         try {
            IsBusy = true;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      private void Refresh() {
         NotifyChanged(nameof(StateMessage));
         NotifyChanged(nameof(HasStateMessage));
         RaiseCommandsChanged();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      private static string SizeSuffix(long? size) =>
         size is { } bytes ? $", {Navigations.CdnManagerVm.FormatSize(bytes)}" : "";

      #endregion
   }
}
