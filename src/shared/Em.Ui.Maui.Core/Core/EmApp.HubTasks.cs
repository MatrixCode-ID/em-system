using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Core
{
   public partial class EmApp
   {
      private Task? _hubRefresh;
      private int _hubEpoch;

      /// <summary>
      /// The MY TASKS rows of the signed-in user, as the server last returned them: one row per kind of work
      /// waiting for them (e.g. one approval type). Empty while nobody is signed in.
      /// </summary>
      public IReadOnlyList<HubTaskInfo> HubTasks { get; private set; } = [];

      /// <summary>The number of work items over all <see cref="HubTasks"/> rows.</summary>
      public int HubTaskCount => HubTasks.Sum(x => x.Count);

      /// <summary><c>true</c> when the last attempt to load <see cref="HubTasks"/> failed.</summary>
      public bool HubTaskLoadFailed { get; private set; }

      /// <summary>Raised on the UI thread after <see cref="HubTasks"/> or <see cref="HubTaskLoadFailed"/> changed.</summary>
      public event EventHandler? HubTasksChanged;

      /// <summary>
      /// Reloads <see cref="HubTasks"/> from the server. Calls made while a load is running share it. Without an
      /// active user the list is emptied instead. Never throws: a failure only sets <see cref="HubTaskLoadFailed"/>.
      /// </summary>
      public Task RefreshHubTasksAsync() {
         if (ActiveUser is null || GetActiveApiClient() is null) {
            _hubEpoch++;
            _hubRefresh = null;
            ApplyHubTasks([], false);
            return Task.CompletedTask;
         }

         return _hubRefresh ??= LoadHubTasksAsync(_hubEpoch);
      }

      private async Task LoadHubTasksAsync(int epoch) {
         try {
            var tasks = await ServiceProvider.GetRequiredService<IApprovalServices>().GetMeta_UserHubTasks();
            // A sign-out or user switch during the call makes the answer belong to somebody else.
            if (epoch == _hubEpoch) ApplyHubTasks(tasks, false);
         }
         catch {
            if (epoch == _hubEpoch) ApplyHubTasks(HubTasks, true);
         }
         finally {
            if (epoch == _hubEpoch) _hubRefresh = null;
         }
      }

      private void ApplyHubTasks(IReadOnlyList<HubTaskInfo> tasks, bool failed) {
         HubTasks = tasks;
         HubTaskLoadFailed = failed;
         if (MainThread.IsMainThread) HubTasksChanged?.Invoke(this, EventArgs.Empty);
         else MainThread.BeginInvokeOnMainThread(() => HubTasksChanged?.Invoke(this, EventArgs.Empty));
      }

      /// <summary>
      /// Opens the screen a MY TASKS row points to. A row aimed at the approval manager with a
      /// <c>Kind:Name</c> parameter opens it filtered to that approval and to the user's own decisions.
      /// </summary>
      /// <param name="task">The row that was tapped.</param>
      public async Task<bool> OpenHubTaskAsync(HubTaskInfo task) {
         var target = ServiceProvider.GetRequiredService<ApprovalWrapperCatalog>().Resolve(task);
         if (target is null) return false;
         var navigation = Navigations.FirstOrDefault(x => x.Name == target.NavigationName);
         if (navigation is null || !CanOpen(navigation)) return false;
         var existing = FindEntry((target.Payload as NavigationPayloadBase)?.Title ?? navigation.Title);
         if (!await NavigateTo(target.NavigationName, target.Payload)) return false;
         if (existing?.Body is IApprovalWrapperNavigationTarget wrapper && target.Payload is ApprovalWrapperNavigationPayload filter)
            await wrapper.ApplyApprovalFilterAsync(filter);
         else if (existing?.Body is Em.Ui.Maui.Navigations.ApprovalViewer viewer && target.Payload is ApprovalManagerNavigationPayload engineFilter) {
            viewer.Vm.Configure(engineFilter); await viewer.Vm.RefreshAsync();
         }
         return true;
      }
   }

   /// <summary>The <c>Kind:Name</c> text the task hub sends as the approval manager's parameter.</summary>
   internal static class ApprovalManagerFilter
   {
      public static bool TryParse(string? text, out ApprovalKind kind, out string name) {
         kind = default;
         name = string.Empty;
         if (text is null || text.IndexOf(':') is not (> 0 and var split)) return false;
         if (!Enum.TryParse(text[..split], out kind)) return false;
         name = text[(split + 1)..];
         return name.Length > 0;
      }
   }
}
