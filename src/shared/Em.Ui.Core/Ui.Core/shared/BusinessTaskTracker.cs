using Em.Api.Core.Models;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Monitor of the personal tasks of the signed-in user, the data source of the task hub in the main
   /// window. It asks the server for the task list periodically: fast while there is a live task, slow
   /// while there is none, so screens do not need to run their own polling.
   /// </summary>
   /// <remarks>
   /// All its members are called from one thread, the UI thread. Polling runs as an async continuation of
   /// the thread that called <see cref="SetUser"/>, so <see cref="Changed"/> is also raised on that thread
   /// and its handler may touch bindings directly.
   /// </remarks>
   public sealed class BusinessTaskTracker(IBusinessTaskServices services)
   {
      private readonly HashSet<string> _wasAlive = new(StringComparer.Ordinal);
      private readonly HashSet<string> _unseen = new(StringComparer.Ordinal);
      private BusinessTaskInfo[] _tasks = [];
      private string? _userId;
      private CancellationTokenSource? _runCts;
      private CancellationTokenSource? _wakeCts;
      private int _generation;

      /// <summary>Polling interval while there are live tasks.</summary>
      public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2);

      /// <summary>Polling interval while there are no live tasks.</summary>
      public TimeSpan SlowInterval { get; set; } = TimeSpan.FromSeconds(30);

      /// <summary>
      /// The current user's personal tasks, live ones first and then the newest. Empty while no user is
      /// signed in.
      /// </summary>
      public IReadOnlyList<BusinessTaskInfo> Tasks => _tasks;

      /// <summary>Number of tasks that are still queued or running.</summary>
      public int AliveCount => _tasks.Count(r => r.IsAlive);

      /// <summary>
      /// <c>true</c> when a task has finished since <see cref="MarkAllSeen"/> was last called, that is, since
      /// the user last opened their task list.
      /// </summary>
      public bool HasUnseen => _unseen.Count > 0;

      /// <summary><c>true</c> when one of the tasks not yet seen has failed.</summary>
      public bool HasUnseenFailure =>
         _tasks.Any(r => _unseen.Contains(r.Id) && r.Status == BusinessTaskStatus.Failed);

      /// <summary>
      /// Raised every time the task list or the "not yet seen" marker changes, on the thread that called
      /// <see cref="SetUser"/>.
      /// </summary>
      public event EventHandler? Changed;

      /// <summary>
      /// Sets the user whose tasks are monitored. A user different from before empties the list and starts a
      /// new polling; <c>null</c> (logout) stops polling and empties the list; the same user changes nothing.
      /// Call from the UI thread.
      /// </summary>
      /// <param name="userId">Id of the signed-in user, or <c>null</c> when there is none anymore.</param>
      public void SetUser(string? userId) {
         if (string.Equals(userId, _userId, StringComparison.Ordinal)) return;

         _runCts?.Cancel();
         _runCts = null;
         _generation++;
         _userId = userId;
         _tasks = [];
         _wasAlive.Clear();
         _unseen.Clear();

         if (userId is not null) {
            _runCts = new CancellationTokenSource();
            _ = RunAsync(_runCts.Token);
         }

         OnChanged();
      }

      /// <summary>
      /// Reloads the task list right now, without waiting for the next polling turn. Called when the task
      /// list is opened. A failure to reach the server is ignored; the old list stays in use.
      /// </summary>
      public Task RefreshAsync() => RefreshCoreAsync();

      /// <summary>
      /// Takes in a task that a module screen has just started, so it appears in the hub right away and fast
      /// polling begins without waiting for the slow polling turn. A global task is ignored, because its
      /// place is on that module's own screen.
      /// </summary>
      /// <param name="task">The snapshot of the task returned by the action that started it.</param>
      public void Track(BusinessTaskInfo task) {
         if (_userId is null || task.Scope != BusinessTaskScope.Personal) return;

         _tasks = Order(_tasks.Where(r => r.Id != task.Id).Append(task));
         if (task.IsAlive) _wasAlive.Add(task.Id);
         OnChanged();
         _wakeCts?.Cancel();
      }

      /// <summary>Marks all tasks that have finished as seen.</summary>
      public void MarkAllSeen() {
         if (_unseen.Count == 0) return;
         _unseen.Clear();
         OnChanged();
      }

      private async Task RunAsync(CancellationToken token) {
         while (!token.IsCancellationRequested) {
            await RefreshCoreAsync();

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
            _wakeCts = wake;
            try {
               await Task.Delay(AliveCount > 0 ? FastInterval : SlowInterval, wake.Token);
            }
            catch (OperationCanceledException) {
               // Woken up by Track, or stopped by SetUser; the loop condition tells the two apart.
            }
            finally {
               if (ReferenceEquals(_wakeCts, wake)) _wakeCts = null;
            }
         }
      }

      private async Task RefreshCoreAsync() {
         if (_userId is null) return;

         var generation = _generation;
         BusinessTaskInfo[] fresh;
         try {
            fresh = await services.GetMeta_UserBusinessTasks();
         }
         catch {
            // A background poll has nobody to show an error to: the old list stays, and the next poll
            // tries again. A dead session is handled by the API client, which ends it (and SetUser(null)).
            return;
         }

         // The user changed while the request was out; its answer belongs to somebody else.
         if (generation != _generation) return;

         foreach (var task in fresh) {
            if (!task.IsAlive && _wasAlive.Contains(task.Id)) _unseen.Add(task.Id);
         }

         _wasAlive.Clear();
         foreach (var task in fresh.Where(r => r.IsAlive)) _wasAlive.Add(task.Id);
         _unseen.IntersectWith(fresh.Select(r => r.Id));

         _tasks = Order(fresh);
         OnChanged();
      }

      private static BusinessTaskInfo[] Order(IEnumerable<BusinessTaskInfo> tasks) =>
         [.. tasks.OrderByDescending(r => r.IsAlive).ThenByDescending(r => r.QueuedAt)];

      private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
   }
}
