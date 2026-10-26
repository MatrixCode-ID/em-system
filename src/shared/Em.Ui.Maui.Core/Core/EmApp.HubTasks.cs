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
      /// <summary>Whether a refresh is in progress, without treating unknown as zero.</summary>
      public bool HubTaskIsLoading { get; private set; }
      /// <summary>The time of the last successful refresh; null until a response is accepted.</summary>
      public DateTimeOffset? HubTaskRefreshedAt { get; private set; }

      internal void ResetHubTasks() {
         _hubEpoch++;
         _hubRefresh = null;
         HubTasks = [];
         HubTaskIsLoading = false;
         HubTaskLoadFailed = false;
         HubTaskRefreshedAt = null;
         PublishHubTasks();
      }

      public Task RefreshHubTasksAsync() {
         if (!MainThread.IsMainThread) return MainThread.InvokeOnMainThreadAsync(RefreshHubTasksAsync);
         if (ActiveUser is null || GetActiveApiClient() is null) { ResetHubTasks(); return Task.CompletedTask; }
         if (_hubRefresh is not null) return _hubRefresh;
         // Install the shared task before invoking the service, which may complete synchronously.
         var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
         _hubRefresh = completion.Task;
         HubTaskIsLoading = true;
         PublishHubTasks();
         _ = LoadHubTasksAsync(_hubEpoch, ActiveUser, ActiveConnection, completion);
         return completion.Task;
      }

      private async Task LoadHubTasksAsync(int epoch, object user, object? connection, TaskCompletionSource completion) {
         IReadOnlyList<HubTaskInfo>? tasks = null;
         try { tasks = await ServiceProvider.GetRequiredService<IApprovalServices>().GetMeta_UserHubTasks(); }
         catch { /* The old snapshot remains readable and is marked stale. */ }
         await MainThread.InvokeOnMainThreadAsync(() => {
            if (epoch == _hubEpoch && ReferenceEquals(user, ActiveUser) && ReferenceEquals(connection, ActiveConnection)) {
               HubTaskIsLoading = false;
               HubTaskLoadFailed = tasks is null;
               if (tasks is not null) { HubTasks = tasks; HubTaskRefreshedAt = DateTimeOffset.Now; }
               _hubRefresh = null;
               PublishHubTasks();
            }
         });
         completion.TrySetResult();
      }

      private void PublishHubTasks() {
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
