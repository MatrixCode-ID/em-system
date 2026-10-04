using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Threading;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// Layar uji business task dan CDN: menjalankan task personal dan global dengan progres, batal, gagal, dan
   /// hasil berkas atau JSON; mengunggah, mendaftar, mengarsip, dan menghapus berkas di CDN.
   /// </summary>
   public partial class TestTasks : UserControl, INavigationBody
   {
      public TestTasks() {
         InitializeComponent();
         Loaded += (_, _) => Vm.StartPolling();
         Unloaded += (_, _) => Vm.StopPolling();
      }

      public TestTasksVm Vm => (TestTasksVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      public Task OnRelease(INavigation sender) {
         Vm.StopPolling();
         return Task.CompletedTask;
      }
   }

   public class TestTasksVm : TestVmBase
   {
      private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
      private bool _loaded;
      private bool _polling;

      public TestTasksVm() {
         RegisterCommand(nameof(StartTaskCommand), StartTaskCommand, IdleAllowed);
         RegisterCommand(nameof(StartTwiceCommand), StartTwiceCommand, IdleAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, IdleAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(CancelTaskCommand), CancelTaskCommand, CancelTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(ClearTaskCommand), ClearTaskCommand, ClearTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(DownloadTaskCommand), DownloadTaskCommand, DownloadTaskCommandAllowed);
         RegisterCommand<BusinessTaskItem?>(nameof(ReadJsonTaskCommand), ReadJsonTaskCommand, ReadJsonTaskCommandAllowed);
         RegisterCommand<string?>(nameof(OpenNavigationCommand), OpenNavigationCommand);
         RegisterCommand(nameof(CdnUploadCommand), CdnUploadCommand, IdleAllowed);
         RegisterCommand(nameof(CdnListCommand), CdnListCommand, IdleAllowed);
         RegisterCommand(nameof(CdnArchiveCommand), CdnArchiveCommand, IdleAllowed);
         RegisterCommand(nameof(CdnDeleteCommand), CdnDeleteCommand, IdleAllowed);

         _timer.Tick += async (_, _) => await PollAsync();
      }

      private IBusinessTaskServices TaskService => EmApp!.ServiceProvider.GetRequiredService<IBusinessTaskServices>();

      private ICdnServices Cdn => EmApp!.ServiceProvider.GetRequiredService<ICdnServices>();

      #region Properties

      public ObservableCollection<BusinessTaskItem> Tasks { get; } = [];

      public IReadOnlyList<TestTaskOutput> Outputs { get; } = Enum.GetValues<TestTaskOutput>();

      public int TaskSeconds {
         get => Get(10);
         set => Set(value);
      }

      public TestTaskOutput TaskOutput {
         get => Get(TestTaskOutput.File);
         set => Set(value);
      }

      public bool TaskGlobal {
         get => Get(false);
         set => Set(value);
      }

      public bool TaskFail {
         get => Get(false);
         set => Set(value);
      }

      public string CdnFolder {
         get => Get("em-test") ?? "em-test";
         set => Set(value);
      }

      public int CdnKb {
         get => Get(256);
         set => Set(value);
      }

      public bool IsEmpty => _loaded && Tasks.Count == 0;

      public string SummaryCaption {
         get {
            var running = Tasks.Count(r => r.Status == BusinessTaskStatus.Running);
            var queued = Tasks.Count(r => r.Status == BusinessTaskStatus.Queued);
            return $"{running:N0} running - {queued:N0} queued - {Tasks.Count:N0} total";
         }
      }

      #endregion

      #region Polling

      public Task ReloadAsync() => RunSafeAsync("Loading tasks", ReadTasksAsync);

      public void StartPolling() {
         if (_loaded) _timer.Start();
      }

      public void StopPolling() => _timer.Stop();

      private async Task PollAsync() {
         if (EmApp is null || _polling) return;

         _polling = true;
         try {
            await ReadTasksAsync();
         }
         catch (Exception) {
            // A failed background read is not worth a line every two seconds; Refresh reports it.
            _timer.Stop();
         }
         finally {
            _polling = false;
         }
      }

      private async Task ReadTasksAsync() {
         var personal = (await TaskService.GetMeta_UserBusinessTasks())
            .Where(r => r.Key.StartsWith(ITestServices.TaskKeyPrefix, StringComparison.Ordinal));
         var global = await Service.GetMeta_TestGlobalTasks();

         BusinessTaskItem.Sync(Tasks, personal.Concat(global)
            .OrderByDescending(r => r.IsAlive).ThenByDescending(r => r.QueuedAt));
         _loaded = true;
         NotifyChanged(nameof(SummaryCaption));
         NotifyChanged(nameof(IsEmpty));
         RaiseCommandsChanged();
         _timer.Start();
      }

      private async Task RunSafeAsync(string waiterText, Func<Task> work) {
         if (EmApp is null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            Log(waiterText, Describe(x), true);
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      private bool IdleAllowed() => IsNotBusy;

      #endregion

      #region Business tasks

      public Task StartTaskCommand() => RunSafeAsync("Starting the task...", StartAsync);

      private async Task StartAsync() {
         try {
            var info = await Service.PostGetMeta_TestStartTask(new TestTaskRequest {
               Seconds = TaskSeconds,
               Fail = TaskFail,
               Global = TaskGlobal,
               Output = TaskOutput
            });

            // A personal task is handed to the tracker so the title-bar hub shows it at once instead of at
            // its next poll; a global one belongs to this screen only.
            if (info.Scope == BusinessTaskScope.Personal) {
               EmApp!.ServiceProvider.GetRequiredService<BusinessTaskTracker>().Track(info);
            }

            Log("Start task", $"'{info.Title}' started ({info.Scope}), id {info.Id}.");
         }
         catch (Exception x) {
            Log("Start task", Describe(x), true);
         }

         await ReadTasksAsync();
      }

      public Task StartTwiceCommand() =>
         RunSafeAsync("Starting the task twice...", async () => {
            await StartAsync();
            try {
               await Service.PostGetMeta_TestStartTask(new TestTaskRequest {
                  Seconds = TaskSeconds, Fail = TaskFail, Global = TaskGlobal, Output = TaskOutput
               });
               Log("Start the same task again", "Started a second time - the server should have refused this with 409.", true);
            }
            catch (ActionException x) when (x.StatusCode == 409) {
               Log("Start the same task again", $"Refused as expected: {Describe(x)}");
            }
            catch (Exception x) {
               Log("Start the same task again", Describe(x), true);
            }
         });

      public Task RefreshCommand() => ReloadAsync();

      public Task CancelTaskCommand(BusinessTaskItem? item) =>
         RunSafeAsync("Canceling...", async () => {
            if (item is null) return;
            try {
               if (item.Info.Scope == BusinessTaskScope.Global) await Service.PostMeta_TestGlobalTaskCancel(item.Key);
               else await TaskService.PostMeta_BusinessTaskCancel(item.Id);
               Log("Cancel task", $"'{item.Title}' was asked to stop; it stops at its next step.");
            }
            catch (Exception x) {
               Log("Cancel task", Describe(x), true);
            }

            await ReadTasksAsync();
         });

      public bool CancelTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanCancel: true };

      public Task ClearTaskCommand(BusinessTaskItem? item) =>
         RunSafeAsync("Clearing...", async () => {
            if (item is null) return;
            try {
               if (item.Info.Scope == BusinessTaskScope.Global) await Service.PostMeta_TestGlobalTaskClear(item.Key);
               else await TaskService.PostMeta_BusinessTaskClear(item.Id);
               Log("Clear task", $"'{item.Title}' was cleared together with its stored result.");
            }
            catch (Exception x) {
               Log("Clear task", Describe(x), true);
            }

            await ReadTasksAsync();
         });

      public bool ClearTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanClear: true };

      public Task DownloadTaskCommand(BusinessTaskItem? item) =>
         RunSafeAsync("Downloading...", async () => {
            if (item is null) return;
            try {
               await using var stream = await TaskService.GetMeta_BusinessTaskFileResult(item.Id);
               using var reader = new StreamReader(stream, Encoding.UTF8);
               Log("Task file result", (await reader.ReadToEndAsync()).Trim());
            }
            catch (Exception x) {
               Log("Task file result", Describe(x), true);
            }
         });

      public bool DownloadTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanDownload: true };

      public Task ReadJsonTaskCommand(BusinessTaskItem? item) =>
         RunSafeAsync("Reading the result...", async () => {
            if (item is null) return;
            try {
               Log("Task JSON result", await TaskService.GetMeta_BusinessTaskJsonResult(item.Id));
            }
            catch (Exception x) {
               Log("Task JSON result", Describe(x), true);
            }
         });

      public bool ReadJsonTaskCommandAllowed(BusinessTaskItem? item) => IsNotBusy && item is { CanOpen: true };

      public async Task OpenNavigationCommand(string? name) {
         if (NavigationEntry is null || string.IsNullOrEmpty(name)) return;
         if (!await NavigationEntry.NavigateTo(name)) Log($"Open '{name}'", "Not opened: no access to it.", true);
      }

      #endregion

      #region CDN

      private string Folder => CdnFolder.Trim().Trim('/');

      public Task CdnUploadCommand() =>
         RunSafeAsync("Uploading to the CDN...", async () => {
            try {
               await EnsureFolderAsync();
               var name = $"sample-{DateTime.Now:HHmmss}.txt";
               var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
                  $"Em.Test CDN sample {name}\n", Math.Max(1, CdnKb * 1024 / 32))));
               await using var stream = new MemoryStream(content);
               var entry = await Cdn.PostGetMeta_CdnUpload(
                  new CdnUploadRequest { Path = Folder, FileName = name, Overwrite = false }, stream);
               Log("CDN upload", $"{entry.Path} ({entry.Size:N0} byte(s)) was stored.");
            }
            catch (Exception x) {
               Log("CDN upload", Describe(x), true);
            }
         });

      public Task CdnListCommand() =>
         RunSafeAsync("Reading the CDN folder...", async () => {
            try {
               var content = await Cdn.GetMeta_CdnFolder(Folder);
               var lines = content.Entries.Select(r => $"{(r.IsFolder ? "[dir] " : "")}{r.Name}  {r.Size:N0} B");
               Log("CDN folder", $"Public path {content.PublicPath}, upload limit {content.MaxFileSize / 1024 / 1024} MB.\n" +
                                 (content.Entries.Length == 0 ? "(empty)" : string.Join("\n", lines)));
            }
            catch (Exception x) {
               Log("CDN folder", Describe(x), true);
            }
         });

      public Task CdnArchiveCommand() =>
         RunSafeAsync("Starting the archive...", async () => {
            try {
               var content = await Cdn.GetMeta_CdnFolder(Folder);
               var names = content.Entries.Where(r => !r.IsFolder && !r.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                  .Select(r => r.Name).ToArray();
               if (names.Length == 0) {
                  Log("CDN archive", "The folder has no file to archive. Upload a sample first.", true);
                  return;
               }

               var info = await Cdn.PostGetMeta_CdnArchive(new CdnArchiveRequest {
                  Folder = Folder, Names = names, ArchiveName = "em-test.zip", Overwrite = true
               });
               EmApp!.ServiceProvider.GetRequiredService<BusinessTaskTracker>().Track(info);
               Log("CDN archive", $"Archive task '{info.Title}' started for {names.Length} file(s); watch the hub or the task list.");
            }
            catch (Exception x) {
               Log("CDN archive", Describe(x), true);
            }
         });

      public Task CdnDeleteCommand() {
         if (DialogOwner is not { } owner) return Task.CompletedTask;
         if (owner.ShowMboxDecideWarning($"Delete the CDN folder '{Folder}' with everything in it?", "Delete CDN folder") !=
             System.Windows.MessageBoxResult.Yes) return Task.CompletedTask;

         return RunSafeAsync("Deleting from the CDN...", async () => {
            try {
               await Cdn.PostMeta_CdnDelete(Folder);
               Log("CDN delete", $"'{Folder}' was removed.");
            }
            catch (Exception x) {
               Log("CDN delete", Describe(x), true);
            }
         });
      }

      // Uploading into a folder that does not exist is refused, so the folder is made first; "already
      // exists" is the answer to ignore, anything else is real.
      private async Task EnsureFolderAsync() {
         try {
            await Cdn.PostGetMeta_CdnCreateFolder(null, Folder);
         }
         catch (ActionException x) when (x.StatusCode == 409) {
         }
      }

      #endregion
   }
}
