using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// The Business Task Manager screen: all business tasks on the server, personal and global, with their
   /// owners, their progress, and the limit on how many tasks may run at the same time. A holder of this
   /// screen's claim who is not an administrator can only view; cancelling, clearing, and changing the
   /// limit are for administrators only.
   /// </summary>
   public partial class BusinessTaskManager : UserControl, INavigationBody
   {
      /// <summary>
      /// Creates the Business Task Manager screen for application <paramref name="app"/>.
      /// </summary>
      public BusinessTaskManager(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         // Polling follows whether the screen is actually in a window: a tab that is not selected, or a
         // page covered by another in the single-page stack, is taken out of the visual tree. There is
         // no bindable equivalent of Loaded/Unloaded, so both are forwarded here.
         Loaded += (_, _) => Vm.StartPolling();
         Unloaded += (_, _) => Vm.StopPolling();
      }

      /// <summary>The view model of this screen.</summary>
      public BusinessTaskManagerVm Vm => (BusinessTaskManagerVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.StopPolling();
         return Task.CompletedTask;
      }
   }

   /// <summary>
   /// One limit mode choice in <see cref="BusinessTaskManager"/>.
   /// </summary>
   public class BusinessTaskLimitModeOption
   {
      /// <summary>Its limit mode.</summary>
      public BusinessTaskLimitMode Mode { get; init; }

      /// <summary>Name of the mode as shown.</summary>
      public string Caption { get; init; } = "";
   }

   /// <summary>
   /// View model for <see cref="BusinessTaskManager"/>.
   /// </summary>
   public class BusinessTaskManagerVm : MvvmModelBase
   {
      private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

      private readonly DispatcherTimer _pollTimer;
      private BusinessTaskLimit? _savedLimit;
      private bool _polling;
      private bool _visible;

      /// <summary>
      /// Creates a new view model and registers all commands of the screen.
      /// </summary>
      public BusinessTaskManagerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(SaveLimitCommand), SaveLimitCommand, SaveLimitCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(CancelTaskCommand), CancelTaskCommand, CancelTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(ClearTaskCommand), ClearTaskCommand, ClearTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(DownloadResultCommand), DownloadResultCommand,
            DownloadResultCommandAllowed);

         _pollTimer = new DispatcherTimer { Interval = PollInterval };
         _pollTimer.Tick += async (_, _) => await PollAsync();
      }

      private IBusinessTaskServices Service => EmApp!.ServiceProvider.GetRequiredService<IBusinessTaskServices>();

      #region Data

      /// <summary>All tasks on the server: live ones first, then the newest.</summary>
      public ObservableCollection<BusinessTaskItem> Tasks { get; } = [];

      /// <summary>The limit mode choices.</summary>
      public IReadOnlyList<BusinessTaskLimitModeOption> LimitModes { get; } = [
         new() { Mode = BusinessTaskLimitMode.Global, Caption = "Global Limit" },
         new() { Mode = BusinessTaskLimitMode.PerUser, Caption = "By User Limit" }
      ];

      /// <summary>The limit mode being edited.</summary>
      public BusinessTaskLimitMode LimitMode {
         get => Get<BusinessTaskLimitMode>();
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>The limit number being edited.</summary>
      public int LimitValue {
         get => Get(1);
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>
      /// <c>true</c> when the signed-in user is an administrator, the only one who may change the limit, and
      /// cancel and clear tasks from this screen.
      /// </summary>
      public bool IsAdmin {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>Caption below the limit setting.</summary>
      public string LimitHint => IsAdmin
         ? "Tasks over the limit wait in the queue until a running one finishes."
         : "Only administrators can change the limit.";

      /// <summary>Summary of the task count, e.g. <c>"2 running · 1 queued"</c>.</summary>
      public string SummaryCaption {
         get {
            var running = Tasks.Count(r => r.Status == BusinessTaskStatus.Running);
            var queued = Tasks.Count(r => r.Status == BusinessTaskStatus.Queued);
            return $"{running:N0} running · {queued:N0} queued";
         }
      }

      /// <summary><c>true</c> when the list has been read and there is no task at all.</summary>
      public bool IsEmpty => IsLoaded && Tasks.Count == 0;

      /// <summary><c>true</c> since the task list was first read.</summary>
      public bool IsLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsEmpty)));
      }

      #endregion

      #region Commands

      /// <summary>Reads the limit and the task list again.</summary>
      public Task RefreshCommand() => ReloadAsync();

      /// <summary>While the screen is not busy.</summary>
      public bool RefreshCommandAllowed() => IsNotBusy;

      /// <summary>Saves the new limit. Queued tasks start right away if the new limit allows it.</summary>
      public Task SaveLimitCommand() =>
         RunBusyAsync("Saving limit...", async () => {
            var limit = new BusinessTaskLimit { Mode = LimitMode, Limit = LimitValue };
            await Service.PostMeta_BusinessTaskLimit(limit);
            _savedLimit = limit;
            await ReadTasksAsync();
         });

      /// <summary>Only for an administrator, and only when the value differs from the stored one.</summary>
      public bool SaveLimitCommandAllowed() =>
         IsNotBusy && IsAdmin && LimitValue >= 1 && _savedLimit is not null &&
         (_savedLimit.Mode != LimitMode || _savedLimit.Limit != LimitValue);

      /// <summary>Cancels task <paramref name="item"/> after confirmation.</summary>
      public async Task CancelTaskCommand(BusinessTaskItem? item) {
         if (item is null || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel task '{item.Title}' started by {item.OwnerName}?", "Cancel Task") !=
             MessageBoxResult.Yes) return;

         await RunBusyAsync("Canceling task...", async () => {
            try {
               await Service.PostMeta_BusinessTaskCancel(item.Id);
            }
            finally {
               await ReadTasksAsync();
            }
         });
      }

      /// <summary>Only for an administrator, for a task that is still live.</summary>
      public bool CancelTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && IsAdmin && item is { CanCancel: true };

      /// <summary>
      /// Clears task <paramref name="item"/>. A task that has a stored result is confirmed first, because its
      /// result is deleted with it.
      /// </summary>
      public async Task ClearTaskCommand(BusinessTaskItem? item) {
         if (item is null || DialogOwner is not { } owner) return;
         if (item.CanDownload || item.CanOpen) {
            if (owner.ShowMboxDecideWarning($"Clear task '{item.Title}'?\n\nIts stored result is deleted as well.",
                   "Clear Task") != MessageBoxResult.Yes) return;
         }

         await RunBusyAsync("Clearing task...", async () => {
            try {
               await Service.PostMeta_BusinessTaskClear(item.Id);
            }
            finally {
               await ReadTasksAsync();
            }
         });
      }

      /// <summary>Only for an administrator, for a task that has finished.</summary>
      public bool ClearTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && IsAdmin && item is { CanClear: true };

      /// <summary>Downloads the result file of task <paramref name="item"/>.</summary>
      public Task DownloadResultCommand(BusinessTaskItem? item) =>
         item is null ? Task.CompletedTask
         : RunBusyAsync("Downloading result...", () => BusinessTaskItem.DownloadResultAsync(Service, item, DialogOwner));

      /// <summary>Only for a successful task whose file result the caller may read.</summary>
      public bool DownloadResultCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanDownload: true };

      #endregion

      #region Methods

      /// <summary>
      /// Reads the limit and the task list again. Called by the host every time the screen is opened through
      /// navigation and from the Refresh button.
      /// </summary>
      public Task ReloadAsync() =>
         RunBusyAsync("Loading...", async () => {
            IsAdmin = EmApp!.IsDebugBypass || EmApp.ActiveUser?.cUserIsAdmin == true;
            NotifyChanged(nameof(LimitHint));

            var limit = await Service.GetMeta_BusinessTaskLimit();
            _savedLimit = limit;
            LimitMode = limit.Mode;
            LimitValue = limit.Limit;
            await ReadTasksAsync();
         });

      /// <summary>Starts reloading the task list every two seconds; called when the screen is shown.</summary>
      public void StartPolling() {
         _visible = true;
         if (IsLoaded) _pollTimer.Start();
      }

      /// <summary>Stops the periodic reloading; called when the screen is no longer shown.</summary>
      public void StopPolling() {
         _visible = false;
         _pollTimer.Stop();
      }

      // Only the list is polled: reading the limit again would overwrite what an administrator is typing.
      // A poll is skipped while a command runs, and an error stops polling rather than repeating the same
      // dialog every two seconds; Refresh starts it again through the next ReloadAsync.
      private async Task PollAsync() {
         if (EmApp is null || !IsLoaded || IsBusy || _polling) return;

         _polling = true;
         try {
            await ReadTasksAsync();
         }
         catch (Exception) {
            _pollTimer.Stop();
         }
         finally {
            _polling = false;
         }
      }

      private async Task ReadTasksAsync() {
         var tasks = await Service.GetMeta_BusinessTasks();
         BusinessTaskItem.Sync(Tasks,
            tasks.OrderByDescending(r => r.IsAlive).ThenByDescending(r => r.QueuedAt));
         IsLoaded = true;
         NotifyChanged(nameof(SummaryCaption));
         NotifyChanged(nameof(IsEmpty));
         RaiseCommandsChanged();
      }

      private async Task RunBusyAsync(string waiterText, Func<Task> work) {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = InWaiting = true;
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

         if (IsLoaded && _visible) _pollTimer.Start();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
