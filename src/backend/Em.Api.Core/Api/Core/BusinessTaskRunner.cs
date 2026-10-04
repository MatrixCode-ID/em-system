using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Carries the identity of whoever started a business task into the task's own DI scope, so that the
   /// scoped <see cref="ActionRequest"/> registration resolves to the starter instead of to
   /// <see cref="ActionRequest.None"/>. Empty in every request scope.
   /// </summary>
   internal sealed class BusinessTaskStarter
   {
      public ActionRequest? Request { get; set; }
   }

   /// <summary>
   /// Runs business tasks outside any request: an in-memory registry, a FIFO queue behind the concurrency
   /// limit, and a cache folder for the results that outlive the task. Every status transition happens
   /// under one lock; the work itself never runs under it.
   /// </summary>
   /// <remarks>
   /// Work is started with the execution context suppressed. <c>IHttpContextAccessor</c> is backed by an
   /// <c>AsyncLocal</c>, so a plain <c>Task.Run</c> from inside an action would carry that request's
   /// <c>HttpContext</c> into the task - and keep reading it after the request has finished and the
   /// context has been recycled for someone else.
   /// </remarks>
   internal sealed class BusinessTaskRunner : IDisposable
   {
      private const string TaskFileName = "task.json";
      private const string JsonResultFileName = "result.json";
      private const string FileResultFileName = "result.bin";

      internal const string LimitModeMetaKey = "BusinessTaskLimitMode";
      internal const string LimitMetaKey = "BusinessTaskLimit";

      private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

      private readonly object _gate = new();
      private readonly Dictionary<string, TaskEntry> _entries = new(StringComparer.Ordinal);
      private readonly Dictionary<string, TaskEntry> _liveKeys = new(StringComparer.OrdinalIgnoreCase);
      private readonly LinkedList<TaskEntry> _queue = [];

      private IServiceProvider _root = null!;
      private ILogger _logger = null!;
      private Timer? _sweep;
      private BusinessTaskLimit _limit;
      private bool _stopping;

      public BusinessTaskRunner(string cachePath, TimeSpan gracePeriod, BusinessTaskLimit defaultLimit) {
         CachePath = cachePath;
         GracePeriod = gracePeriod;
         _limit = Copy(defaultLimit);
      }

      /// <summary>Absolute path of the result cache.</summary>
      public string CachePath { get; }

      public TimeSpan GracePeriod { get; }

      #region Lifetime

      /// <summary>
      /// Called once before the first request: loads the limit from metadata, keeps the successful results
      /// of the previous run, wipes everything else in the cache, and starts the sweep timer.
      /// </summary>
      public void Initialize(IServiceProvider root) {
         _root = root;
         _logger = root.GetRequiredService<ILoggerFactory>().CreateLogger<BusinessTaskRunner>();

         Directory.CreateDirectory(CachePath);
         LoadLimit();
         LoadCache();

         _sweep = new Timer(_ => Sweep(), null, SweepInterval, SweepInterval);
      }

      private void LoadLimit() {
         using var scope = _root.CreateScope();
         var ctx = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
         var values = ctx.ta_Metas.AsNoTracking()
            .Where(r => r.cMetaKey == LimitModeMetaKey || r.cMetaKey == LimitMetaKey)
            .ToDictionary(r => r.cMetaKey, r => r.cMetaValue);

         var limit = Copy(_limit);
         if (values.TryGetValue(LimitModeMetaKey, out var mode) &&
             Enum.TryParse<BusinessTaskLimitMode>(mode, ignoreCase: true, out var parsedMode)) {
            limit.Mode = parsedMode;
         }

         if (values.TryGetValue(LimitMetaKey, out var raw) && int.TryParse(raw, out var parsedLimit) && parsedLimit >= 1) {
            limit.Limit = parsedLimit;
         }

         _limit = limit;
      }

      private void LoadCache() {
         foreach (var folder in Directory.EnumerateDirectories(CachePath)) {
            var info = TryReadTaskFile(folder);
            if (info is { Status: BusinessTaskStatus.Succeeded } && File.Exists(ResultPath(folder, info.OutputKind))) {
               var entry = new TaskEntry(info, ActionRequest.None, CompositeKey(info.Scope, info.OwnerUserId, info.Key),
                  null);
               _entries[info.Id] = entry;
               continue;
            }

            // Failed, cancelled, cut off by the restart, or never finished writing: nothing to keep.
            DeleteFolder(folder);
         }
      }

      private BusinessTaskInfo? TryReadTaskFile(string folder) {
         try {
            var path = Path.Combine(folder, TaskFileName);
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BusinessTaskInfo>(stream, Defaults.ResponseJsonOptions);
         }
         catch (Exception ex) {
            _logger.LogWarning(ex, "Business task cache folder '{Folder}' could not be read and is discarded.", folder);
            return null;
         }
      }

      /// <summary>Server shutdown: every live task is cancelled, and nothing about it is kept.</summary>
      public void Stop() {
         lock (_gate) {
            _stopping = true;
            foreach (var entry in _entries.Values.Where(r => r.Info.IsAlive)) {
               entry.Cts.Cancel();
            }
         }
      }

      public void Dispose() {
         _sweep?.Dispose();
      }

      #endregion

      #region Limit

      public BusinessTaskLimit Limit {
         get {
            lock (_gate) return Copy(_limit);
         }
      }

      /// <summary>Applies a new limit and starts whatever it now lets through. Persisting it is the caller's job.</summary>
      public void SetLimit(BusinessTaskLimit limit) {
         lock (_gate) _limit = Copy(limit);
         Pump();
      }

      private static BusinessTaskLimit Copy(BusinessTaskLimit limit) => new() { Mode = limit.Mode, Limit = limit.Limit };

      #endregion

      #region Start

      /// <summary>
      /// Registers a task and queues it. <paramref name="work"/> runs later on its own scope and returns the
      /// size of the result it wrote into the folder it is given, or <c>null</c> when it wrote none.
      /// </summary>
      /// <exception cref="ActionException">409 while a task with the same key is still alive.</exception>
      public BusinessTaskInfo Start(BusinessTaskOptions options, BusinessTaskOutputKind outputKind, ActionRequest starter,
         string moduleName, Func<BusinessTaskContext, string, Task<long?>> work) {
         ArgumentNullException.ThrowIfNull(options);
         if (string.IsNullOrWhiteSpace(options.Key)) {
            throw new ArgumentException("A business task needs a key.", nameof(options));
         }

         if (string.IsNullOrWhiteSpace(options.Title)) {
            throw new ArgumentException("A business task needs a title.", nameof(options));
         }

         if (outputKind == BusinessTaskOutputKind.File && string.IsNullOrWhiteSpace(options.ResultFileName)) {
            throw new ArgumentException("A business task with a file result needs a result file name.", nameof(options));
         }

         var ownerId = starter.RequireUserId();
         var info = new BusinessTaskInfo {
            Id = $"{Ulid.NewUlid()}",
            Key = options.Key.Trim(),
            Scope = options.Scope,
            Title = options.Title,
            ModuleName = moduleName,
            OwnerUserId = ownerId,
            OwnerName = starter.cUserAccount ?? ownerId,
            OutputKind = outputKind,
            Status = BusinessTaskStatus.Queued,
            Caption = "Queued",
            QueuedAt = DateTimeOffset.UtcNow,
            NavigationName = options.NavigationName,
            ResultFileName = options.ResultFileName,
            ResultContentType = options.ResultContentType
         };

         var entry = new TaskEntry(info, starter, CompositeKey(info.Scope, ownerId, info.Key), work);

         lock (_gate) {
            if (_stopping) {
               throw new ActionException("The server is shutting down and cannot start new work.", 503);
            }

            if (_liveKeys.TryGetValue(entry.LiveKey, out var live)) {
               throw new ActionException(
                  $"'{live.Info.Title}' is already running, started by {live.Info.OwnerName}. Wait for it to finish or cancel it first.",
                  409);
            }

            _entries[info.Id] = entry;
            _liveKeys[entry.LiveKey] = entry;
            _queue.AddLast(entry);
         }

         ResolveOwnerName(entry);
         Pump();
         return Snapshot(entry, starter, moduleGranted: true);
      }

      private static string CompositeKey(BusinessTaskScope scope, string ownerUserId, string key) =>
         scope == BusinessTaskScope.Global ? $"G|{key}" : $"P|{ownerUserId}|{key}";

      /// <summary>
      /// The display name needs a query, and starting a task must not wait on one: the entry starts with
      /// the account (or id) and the full name is filled in as soon as it is known.
      /// </summary>
      private void ResolveOwnerName(TaskEntry entry) {
         var ownerId = entry.Info.OwnerUserId;
         if (ownerId == Defaults.DebuggerUserId) {
            SetOwnerName(Defaults.DebuggerUserAccount);
            return;
         }

         if (ownerId == Defaults.AdminUserId) {
            SetOwnerName(Defaults.AdminUserAccount);
            return;
         }

         using (ExecutionContext.SuppressFlow()) {
            _ = Task.Run(async () => {
               try {
                  await using var scope = _root.CreateAsyncScope();
                  var ctx = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
                  var user = await ctx.vi_Users.AsNoTracking()
                     .Where(r => r.cUserId == ownerId)
                     .Select(r => new { r.cContactFullName, r.cUserAccount })
                     .FirstOrDefaultAsync();
                  if (user is not null) {
                     SetOwnerName(string.IsNullOrWhiteSpace(user.cContactFullName) ? user.cUserAccount : user.cContactFullName);
                  }
               }
               catch (Exception ex) {
                  _logger.LogWarning(ex, "Owner name of business task {Id} could not be resolved.", entry.Info.Id);
               }
            });
         }

         void SetOwnerName(string name) {
            lock (_gate) entry.Info.OwnerName = name;
         }
      }

      #endregion

      #region Scheduling

      /// <summary>Starts as many queued tasks as the limit allows, oldest first.</summary>
      private void Pump() {
         List<TaskEntry> launch = [];

         lock (_gate) {
            if (_stopping) return;

            while (PickNext() is { } next) {
               _queue.Remove(next);
               next.Info.Status = BusinessTaskStatus.Running;
               next.Info.StartedAt = DateTimeOffset.UtcNow;
               next.Info.Caption = "Starting";
               launch.Add(next);
            }
         }

         foreach (var entry in launch) {
            using (ExecutionContext.SuppressFlow()) {
               _ = Task.Run(() => ExecuteAsync(entry));
            }
         }
      }

      private TaskEntry? PickNext() {
         if (_queue.First is null) return null;

         var running = _entries.Values.Where(r => r.Info.Status == BusinessTaskStatus.Running).ToList();

         if (_limit.Mode == BusinessTaskLimitMode.Global) {
            return running.Count < _limit.Limit ? _queue.First.Value : null;
         }

         foreach (var candidate in _queue) {
            var owner = candidate.Info.OwnerUserId;
            if (running.Count(r => r.Info.OwnerUserId == owner) < _limit.Limit) {
               return candidate;
            }
         }

         return null;
      }

      private async Task ExecuteAsync(TaskEntry entry) {
         var folder = Path.Combine(CachePath, entry.Info.Id);

         try {
            await using var scope = _root.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BusinessTaskStarter>().Request = entry.Starter;

            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
               .CreateLogger($"BusinessTask.{entry.Info.ModuleName}");
            var context = new BusinessTaskContext(entry.Cts.Token, scope.ServiceProvider, entry.Starter, logger,
               (percent, caption) => Report(entry, percent, caption));

            var size = await entry.Work!(context, folder);

            // A result must be on disk, with its task file, before anyone is told it exists: the task file
            // is written last, so a folder without one is never mistaken for a finished result.
            if (entry.Info.OutputKind != BusinessTaskOutputKind.None) {
               BusinessTaskInfo record;
               lock (_gate) {
                  record = Snapshot(entry, ActionRequest.None, moduleGranted: false);
               }

               record.Status = BusinessTaskStatus.Succeeded;
               record.FinishedAt = DateTimeOffset.UtcNow;
               record.Percent = 100;
               record.Caption = "Completed";
               record.ResultSize = size;
               record.CanCancel = record.CanClear = record.CanReadResult = false;
               await using (var file = File.Create(Path.Combine(folder, TaskFileName))) {
                  await JsonSerializer.SerializeAsync(file, record, Defaults.ResponseJsonOptions);
               }
            }

            Finish(entry, BusinessTaskStatus.Succeeded, null, size);
         }
         catch (OperationCanceledException) when (entry.Cts.IsCancellationRequested) {
            DeleteFolder(folder);
            Finish(entry, BusinessTaskStatus.Canceled, null, null);
         }
         catch (Exception ex) {
            var actual = ex is AggregateException { InnerException: { } inner } ? inner : ex;
            _logger.LogError(actual, "Business task {Id} ('{Title}', key '{Key}') failed.", entry.Info.Id,
               entry.Info.Title, entry.Info.Key);
            DeleteFolder(folder);
            Finish(entry, BusinessTaskStatus.Failed, actual.Message, null);
         }
         finally {
            Pump();
         }
      }

      private void Report(TaskEntry entry, double? percent, string caption) {
         lock (_gate) {
            if (!entry.Info.IsAlive) return;

            entry.Info.Percent = percent is { } value ? Math.Clamp(value, 0, 100) : null;
            entry.Info.Caption = caption;
         }
      }

      private void Finish(TaskEntry entry, BusinessTaskStatus status, string? error, long? size) {
         lock (_gate) {
            entry.Info.Status = status;
            entry.Info.FinishedAt = DateTimeOffset.UtcNow;
            entry.Info.ErrorMessage = error;
            entry.Info.Caption = status switch {
               BusinessTaskStatus.Succeeded => "Completed",
               BusinessTaskStatus.Canceled => "Canceled",
               _ => "Failed"
            };

            if (status == BusinessTaskStatus.Succeeded) {
               entry.Info.Percent = 100;
               entry.Info.ResultSize = size;
            }

            ReleaseKey(entry);
         }
      }

      // Under the lock. Only the live entry holds its key, so a finished one never blocks a new start.
      private void ReleaseKey(TaskEntry entry) {
         if (_liveKeys.TryGetValue(entry.LiveKey, out var holder) && ReferenceEquals(holder, entry)) {
            _liveKeys.Remove(entry.LiveKey);
         }
      }

      /// <summary>
      /// Drops successful tasks without a result and cancelled tasks once the grace period has passed.
      /// Failed tasks and tasks with a result stay until someone clears them.
      /// </summary>
      private void Sweep() {
         try {
            var cutoff = DateTimeOffset.UtcNow - GracePeriod;
            lock (_gate) {
               var expired = _entries.Values
                  .Where(r => r.Info.FinishedAt is { } finished && finished <= cutoff &&
                              (r.Info.Status == BusinessTaskStatus.Canceled ||
                               (r.Info.Status == BusinessTaskStatus.Succeeded &&
                                r.Info.OutputKind == BusinessTaskOutputKind.None)))
                  .ToList();
               foreach (var entry in expired) {
                  _entries.Remove(entry.Info.Id);
               }
            }
         }
         catch (Exception ex) {
            _logger.LogError(ex, "Business task sweep failed.");
         }
      }

      #endregion

      #region Lookup and control

      /// <summary>Every entry, as seen by <paramref name="caller"/>; newest first.</summary>
      public BusinessTaskInfo[] List(ActionRequest caller, Func<BusinessTaskInfo, bool>? filter = null) {
         lock (_gate) {
            return _entries.Values
               .Where(r => filter is null || filter(r.Info))
               .OrderByDescending(r => r.Info.QueuedAt)
               .Select(r => Snapshot(r, caller, moduleGranted: false))
               .ToArray();
         }
      }

      /// <summary>The entry with this id, or <c>null</c>.</summary>
      public BusinessTaskInfo? Get(string id, ActionRequest caller) {
         lock (_gate) {
            return _entries.TryGetValue(id, out var entry) ? Snapshot(entry, caller, moduleGranted: false) : null;
         }
      }

      /// <summary>Cancels by id. 404 when unknown, 409 when already finished.</summary>
      public void Cancel(string id) {
         lock (_gate) {
            CancelLocked(RequireLocked(id));
         }
      }

      /// <summary>Clears by id. 404 when unknown, 409 while alive.</summary>
      public void Clear(string id) {
         TaskEntry entry;
         lock (_gate) {
            entry = RequireLocked(id);
            ClearLocked(entry);
         }

         DeleteFolder(Path.Combine(CachePath, entry.Info.Id));
      }

      /// <summary>
      /// The task with this key that is alive, or else the newest one that failed and was not cleared.
      /// Rights are the calling action's business, so cancel and clear are reported as allowed.
      /// </summary>
      public BusinessTaskInfo? Find(string key, BusinessTaskScope scope, ActionRequest caller) {
         var liveKey = CompositeKey(scope, caller.cUserId ?? string.Empty, key.Trim());
         lock (_gate) {
            return FindLocked(liveKey) is { } entry ? Snapshot(entry, caller, moduleGranted: true) : null;
         }
      }

      /// <summary>Same as <see cref="Find"/>, for every key that starts with <paramref name="keyPrefix"/>.</summary>
      public BusinessTaskInfo[] FindMany(string keyPrefix, BusinessTaskScope scope, ActionRequest caller) {
         var livePrefix = CompositeKey(scope, caller.cUserId ?? string.Empty, keyPrefix.Trim());
         lock (_gate) {
            return _entries.Values
               .Where(r => r.LiveKey.StartsWith(livePrefix, StringComparison.OrdinalIgnoreCase))
               .GroupBy(r => r.LiveKey, StringComparer.OrdinalIgnoreCase)
               .Select(r => FindLocked(r.Key))
               .OfType<TaskEntry>()
               .Select(r => Snapshot(r, caller, moduleGranted: true))
               .ToArray();
         }
      }

      /// <summary>Cancels the live task with this key. 404 when there is none, 409 when only a finished one is left.</summary>
      public void CancelByKey(string key, BusinessTaskScope scope, ActionRequest caller) {
         var liveKey = CompositeKey(scope, caller.cUserId ?? string.Empty, key.Trim());
         lock (_gate) {
            if (_liveKeys.TryGetValue(liveKey, out var live)) {
               CancelLocked(live);
               return;
            }

            if (_entries.Values.Any(r => SameKey(r, liveKey))) {
               throw new ActionException("This task has already finished.", 409);
            }
         }

         throw new ActionException("There is no such task.", 404);
      }

      /// <summary>Clears every finished task with this key. 404 when there is none, 409 when only a live one exists.</summary>
      public void ClearByKey(string key, BusinessTaskScope scope, ActionRequest caller) {
         var liveKey = CompositeKey(scope, caller.cUserId ?? string.Empty, key.Trim());
         List<TaskEntry> cleared;
         lock (_gate) {
            var matches = _entries.Values.Where(r => SameKey(r, liveKey)).ToList();
            if (matches.Count == 0) {
               throw new ActionException("There is no such task.", 404);
            }

            cleared = matches.Where(r => !r.Info.IsAlive).ToList();
            if (cleared.Count == 0) {
               throw new ActionException("This task is still running; cancel it first.", 409);
            }

            foreach (var entry in cleared) {
               _entries.Remove(entry.Info.Id);
            }
         }

         foreach (var entry in cleared) {
            DeleteFolder(Path.Combine(CachePath, entry.Info.Id));
         }
      }

      /// <summary>Owner id of a task, for the rights check of the id-based actions. 404 when unknown.</summary>
      public string RequireOwner(string id) {
         lock (_gate) return RequireLocked(id).Info.OwnerUserId;
      }

      /// <summary>
      /// Path of a finished result. 404 when unknown, 409 until it succeeded, 400 when the kind differs.
      /// </summary>
      public (string Path, BusinessTaskInfo Info) RequireResult(string id, BusinessTaskOutputKind kind) {
         BusinessTaskInfo info;
         lock (_gate) {
            info = Snapshot(RequireLocked(id), ActionRequest.None, moduleGranted: false);
         }

         if (info.OutputKind != kind) {
            throw new ActionException($"The result of this task is not {(kind == BusinessTaskOutputKind.Json ? "JSON data" : "a file")}.", 400);
         }

         if (info.Status != BusinessTaskStatus.Succeeded) {
            throw new ActionException("This task has no result yet.", 409);
         }

         var path = ResultPath(Path.Combine(CachePath, info.Id), kind);
         if (!File.Exists(path)) {
            throw new ActionException("The result of this task is no longer available.", 404);
         }

         return (path, info);
      }

      private TaskEntry RequireLocked(string id) =>
         _entries.TryGetValue(id ?? string.Empty, out var entry)
            ? entry
            : throw new ActionException("There is no such task.", 404);

      private void CancelLocked(TaskEntry entry) {
         switch (entry.Info.Status) {
            case BusinessTaskStatus.Queued:
               _queue.Remove(entry);
               entry.Info.Status = BusinessTaskStatus.Canceled;
               entry.Info.FinishedAt = DateTimeOffset.UtcNow;
               entry.Info.Caption = "Canceled";
               ReleaseKey(entry);
               return;
            case BusinessTaskStatus.Running:
               // The status changes once the work observes the token; until then it reads as stopping.
               entry.Info.Caption = "Canceling";
               entry.Cts.Cancel();
               return;
            default:
               throw new ActionException("This task has already finished.", 409);
         }
      }

      private void ClearLocked(TaskEntry entry) {
         if (entry.Info.IsAlive) {
            throw new ActionException("This task is still running; cancel it first.", 409);
         }

         _entries.Remove(entry.Info.Id);
      }

      private TaskEntry? FindLocked(string liveKey) {
         if (_liveKeys.TryGetValue(liveKey, out var live)) return live;

         return _entries.Values
            .Where(r => r.Info.Status == BusinessTaskStatus.Failed && SameKey(r, liveKey))
            .OrderByDescending(r => r.Info.QueuedAt)
            .FirstOrDefault();
      }

      private static bool SameKey(TaskEntry entry, string liveKey) =>
         string.Equals(entry.LiveKey, liveKey, StringComparison.OrdinalIgnoreCase);

      /// <summary>
      /// A copy of the entry for one caller, with the per-caller flags filled in. Must be called under the
      /// lock: the entry keeps changing while the work runs.
      /// </summary>
      private static BusinessTaskInfo Snapshot(TaskEntry entry, ActionRequest caller, bool moduleGranted) {
         var source = entry.Info;
         var isOwner = caller.cUserId is { } callerId && callerId == source.OwnerUserId;
         var isAdmin = caller.IsAdmin;
         var alive = source.IsAlive;

         return new BusinessTaskInfo {
            Id = source.Id,
            Key = source.Key,
            Scope = source.Scope,
            Title = source.Title,
            ModuleName = source.ModuleName,
            OwnerUserId = source.OwnerUserId,
            OwnerName = source.OwnerName,
            OutputKind = source.OutputKind,
            Status = source.Status,
            Percent = source.Percent,
            Caption = source.Caption,
            QueuedAt = source.QueuedAt,
            StartedAt = source.StartedAt,
            FinishedAt = source.FinishedAt,
            ErrorMessage = source.ErrorMessage,
            NavigationName = source.NavigationName,
            ResultFileName = source.ResultFileName,
            ResultContentType = source.ResultContentType,
            ResultSize = source.ResultSize,
            CanCancel = alive && (moduleGranted || isOwner || isAdmin),
            CanClear = !alive && (moduleGranted || isOwner || isAdmin),
            CanReadResult = source.Status == BusinessTaskStatus.Succeeded &&
                            source.OutputKind != BusinessTaskOutputKind.None && (isOwner || isAdmin)
         };
      }

      #endregion

      #region Files

      public static string ResultPath(string folder, BusinessTaskOutputKind kind) =>
         Path.Combine(folder, kind == BusinessTaskOutputKind.Json ? JsonResultFileName : FileResultFileName);

      private void DeleteFolder(string folder) {
         try {
            if (Directory.Exists(folder)) {
               Directory.Delete(folder, recursive: true);
            }
         }
         catch (Exception ex) {
            _logger.LogWarning(ex, "Business task cache folder '{Folder}' could not be deleted.", folder);
         }
      }

      #endregion

      private sealed class TaskEntry(
         BusinessTaskInfo info,
         ActionRequest starter,
         string liveKey,
         Func<BusinessTaskContext, string, Task<long?>>? work)
      {
         public BusinessTaskInfo Info { get; } = info;

         public ActionRequest Starter { get; } = starter;

         public string LiveKey { get; } = liveKey;

         /// <summary><c>null</c> for results loaded from the cache at startup.</summary>
         public Func<BusinessTaskContext, string, Task<long?>>? Work { get; } = work;

         // Never disposed: without CancelAfter it holds no timer, and disposing it would race a late Cancel.
         public CancellationTokenSource Cts { get; } = new();
      }
   }
}
