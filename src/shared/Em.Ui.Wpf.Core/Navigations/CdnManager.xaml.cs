using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using FontAwesome6;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Clipboard = System.Windows.Clipboard;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// The CDN manager screen: browsing the CDN folders on the server, uploading files, creating and
   /// deleting folders, copying the public link of a file or folder, and creating and extracting zip files.
   /// Creating and extracting zips runs on the server as a business task, so their status - including
   /// those started by another user - is shown too and can be cancelled from this screen.
   /// </summary>
   public partial class CdnManager : UserControl, INavigationBody
   {
      /// <summary>
      /// Creates the CDN manager screen for application <paramref name="app"/>.
      /// </summary>
      public CdnManager(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         settingsCard.Attach(app, true);
         // Archive and extract status is polled only while the screen is actually in a window; there is
         // no bindable equivalent of Loaded/Unloaded, so both are forwarded here.
         Loaded += (_, _) => Vm.StartTaskPolling();
         Unloaded += (_, _) => Vm.StopTaskPolling();
         // Ctrl+A selects rows whose containers were never created, so their IsSelected binding in the row
         // style never runs; SelectionChanged still names them. SelectedItems itself is not bindable.
         entryList.SelectionChanged += (_, e) => Vm.ApplySelection(e.AddedItems, e.RemovedItems);
      }

      /// <summary>The view model of this screen.</summary>
      public CdnManagerVm Vm => (CdnManagerVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (!settingsCard.ConfirmLeave()) args.Cancel = true;
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.StopTaskPolling();
         return Task.CompletedTask;
      }
   }

   /// <summary>
   /// One row of the folder listing in <see cref="CdnManager"/>: the CDN entry as-is from the server,
   /// together with captions that are ready to show. This row is also the drag payload: inside the
   /// application it is moved to another folder, outside the application it becomes a file downloaded
   /// straight to where it is dropped.
   /// </summary>
   public class CdnManagerItem : NotifyPropertyBase, IVirtualFileSource
   {
      private readonly CdnManagerVm _owner;

      internal CdnManagerItem(CdnEntry entry, CdnManagerVm owner) {
         Entry = entry;
         _owner = owner;
      }

      /// <inheritdoc />
      public IReadOnlyList<VirtualFile> ResolveVirtualFiles() => _owner.ResolveVirtualFiles(this);

      /// <summary>The CDN entry this row represents.</summary>
      public CdnEntry Entry { get; }

      /// <summary>Name of the file or folder.</summary>
      public string Name => Entry.Name;

      /// <summary><c>true</c> for a folder.</summary>
      public bool IsFolder => Entry.IsFolder;

      /// <summary>
      /// <c>true</c> when this row is also selected. The list can select many rows at once; only Create
      /// Archive uses the whole selection, other commands work on the focused row.
      /// </summary>
      public bool IsSelected {
         get => Get<bool>();
         set => Set(value, _ => _owner.OnItemSelectionChanged());
      }

      /// <summary><c>true</c> for a <c>.zip</c> file, the only kind that can be extracted in place.</summary>
      public bool IsZip => !IsFolder && Path.GetExtension(Name).Equals(".zip", StringComparison.OrdinalIgnoreCase);

      /// <summary>
      /// The extract task for this file that is still live, or that failed and has not been cleared;
      /// <c>null</c> when there is none.
      /// </summary>
      public BusinessTaskInfo? ExtractTask {
         get => Get<BusinessTaskInfo?>();
         internal set => Set(value, _ => {
            NotifyChanged(nameof(ExtractMode));
            NotifyChanged(nameof(ExtractTooltip));
         });
      }

      /// <summary>State of this row's extract button.</summary>
      public CdnTaskMode ExtractMode => CdnManagerVm.ModeOf(ExtractTask);

      /// <summary>Tooltip of the extract button, according to its state.</summary>
      public string ExtractTooltip => ExtractMode switch {
         CdnTaskMode.Running => $"{CdnManagerVm.ProgressText(ExtractTask!)} - click to cancel",
         CdnTaskMode.Failed => "Extract failed - click for details",
         _ => "Extract here"
      };

      /// <summary>Icon of the row: a folder, or a file kind according to its extension.</summary>
      public EFontAwesomeIcon Icon => IsFolder ? EFontAwesomeIcon.Solid_Folder : IconForFile(Entry.Name);

      /// <summary>Size in a human-friendly form, e.g. <c>"12.4 MB"</c>; a dash for folders.</summary>
      public string SizeCaption => IsFolder ? "-" : CdnManagerVm.FormatSize(Entry.Size);

      /// <summary>Exact size in bytes, for the tooltip.</summary>
      public string SizeTooltip => IsFolder ? "Folder" : $"{Entry.Size:N0} bytes";

      /// <summary>Time of the last change, in local time.</summary>
      public string ModifiedCaption =>
         Entry.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

      private static EFontAwesomeIcon IconForFile(string name) =>
         Path.GetExtension(name).ToLowerInvariant() switch {
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" => EFontAwesomeIcon.Regular_FileZipper,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" => EFontAwesomeIcon.Regular_FileImage,
            ".pdf" => EFontAwesomeIcon.Regular_FilePdf,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => EFontAwesomeIcon.Regular_FileVideo,
            ".mp3" or ".wav" or ".ogg" or ".flac" => EFontAwesomeIcon.Regular_FileAudio,
            ".txt" or ".md" or ".log" or ".csv" or ".json" or ".xml" => EFontAwesomeIcon.Regular_FileLines,
            ".xls" or ".xlsx" => EFontAwesomeIcon.Regular_FileExcel,
            ".doc" or ".docx" => EFontAwesomeIcon.Regular_FileWord,
            ".exe" or ".msi" or ".apk" or ".msix" or ".appx" => EFontAwesomeIcon.Solid_BoxOpen,
            _ => EFontAwesomeIcon.Regular_File
         };
   }

   /// <summary>
   /// State of a button that also serves as the status display of a CDN business task (Create Archive in
   /// the toolbar, Extract on a zip file row).
   /// </summary>
   public enum CdnTaskMode
   {
      /// <summary>No task: the button starts its work.</summary>
      None = 0,

      /// <summary>Task queued or running: the button animates and can only cancel.</summary>
      Running = 1,

      /// <summary>Task failed and not yet cleared: the button shows its error and then clears it.</summary>
      Failed = 2
   }

   /// <summary>
   /// One piece of the folder path in the <see cref="CdnManager"/> breadcrumb.
   /// </summary>
   public class CdnBreadcrumb
   {
      /// <summary>The name shown.</summary>
      public string Name { get; init; } = "";

      /// <summary>Path of the folder that is opened when this piece is clicked; an empty string for the root.</summary>
      public string Path { get; init; } = "";

      /// <summary><c>true</c> for the first piece, which is not preceded by a separator.</summary>
      public bool IsFirst { get; init; }
   }

   /// <summary>
   /// View model for <see cref="CdnManager"/>.
   /// </summary>
   public class CdnManagerVm : MvvmModelBase
   {
      /// <summary>
      /// Creates a new view model and registers all commands of the screen.
      /// </summary>
      public CdnManagerVm() {
         RegisterCommand<CdnManagerItem?>(nameof(OpenFolderCommand), OpenFolderCommand, OpenFolderCommandAllowed);
         RegisterCommand(nameof(GoUpCommand), GoUpCommand, GoUpCommandAllowed);
         RegisterCommand<CdnBreadcrumb?>(nameof(NavigateToSegmentCommand), NavigateToSegmentCommand,
            NavigateToSegmentCommandAllowed);
         RegisterCommand(nameof(UploadCommand), UploadCommand, UploadCommandAllowed);
         RegisterCommand(nameof(CancelTransferCommand), CancelTransferCommand, CancelTransferCommandAllowed);
         RegisterCommand(nameof(NewFolderCommand), NewFolderCommand, NewFolderCommandAllowed);
         RegisterCommand<CdnManagerItem?>(nameof(DeleteCommand), DeleteCommand, DeleteCommandAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand<CdnManagerItem?>(nameof(CopyLinkCommand), CopyLinkCommand, CopyLinkCommandAllowed);
         RegisterCommand<CdnManagerItem?>(nameof(DownloadCommand), DownloadCommand, DownloadCommandAllowed);
         RegisterCommand(nameof(OpenInBrowserCommand), OpenInBrowserCommand, OpenInBrowserCommandAllowed);
         RegisterCommand<object?>(nameof(DropCommand), DropCommand, DropCommandAllowed);
         RegisterCommand(nameof(ArchiveCommand), ArchiveCommand, ArchiveCommandAllowed);
         RegisterCommand<CdnManagerItem?>(nameof(ExtractCommand), ExtractCommand, ExtractCommandAllowed);
         Breadcrumbs.Add(RootCrumb);

         _taskTimer = new DispatcherTimer { Interval = SlowTaskPoll };
         _taskTimer.Tick += async (_, _) => await PollTasksAsync();
      }

      private const string ExtractTaskKeyPrefix = "cdn:extract:";

      // Fast while an archive or extract is under way, slow otherwise - slow still catches somebody
      // else starting an archive, so this screen's button changes with theirs.
      private static readonly TimeSpan FastTaskPoll = TimeSpan.FromSeconds(2);
      private static readonly TimeSpan SlowTaskPoll = TimeSpan.FromSeconds(10);

      private readonly DispatcherTimer _taskTimer;
      private bool _taskPolling;
      private bool _visible;

      // Keys of the tasks that were alive at the last look, to notice the ones that have finished since.
      private HashSet<string> _aliveTaskKeys = new(StringComparer.OrdinalIgnoreCase);

      private static readonly CdnBreadcrumb RootCrumb = new() { Name = "cdn", Path = "", IsFirst = true };

      private ICdnServices Service => EmApp!.ServiceProvider.GetRequiredService<ICdnServices>();

      /// <summary>The storage caption.</summary>
      public string StorageCaption {
         get => Get<string>() ?? "Storage: not checked";
         private set => Set(value);
      }

      // Set only while an upload or a download runs; CancelTransferCommand trips it.
      private CancellationTokenSource? _transferCancel;

      #region Data

      /// <summary>Content of the folder that is open: subfolders first, then files.</summary>
      public ObservableCollection<CdnManagerItem> Items { get; } = [];

      /// <summary>The pieces of the open folder's path, from the root to the folder itself.</summary>
      public ObservableCollection<CdnBreadcrumb> Breadcrumbs { get; } = [];

      /// <summary>Path of the open folder, relative to the CDN root; an empty string for the root.</summary>
      public string CurrentPath {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Public address of the open folder, relative to the server address.</summary>
      public string PublicPath {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>The row that is selected.</summary>
      public CdnManagerItem? SelectedItem {
         get => Get<CdnManagerItem?>();
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Size limit of one uploaded file in bytes, as reported by the server.</summary>
      public long MaxFileSize {
         get => Get<long>();
         private set => Set(value, _ => NotifyChanged(nameof(LimitCaption)));
      }

      /// <summary>Caption of the upload limit for the chip in the toolbar.</summary>
      public string LimitCaption => MaxFileSize > 0 ? $"Max {FormatSize(MaxFileSize)} per file" : "";

      /// <summary>Content count of the folder for the chip in the toolbar, e.g. <c>"2 folders · 5 files"</c>.</summary>
      public string ItemsCaption {
         get {
            var folders = Items.Count(r => r.IsFolder);
            var files = Items.Count - folders;
            return $"{folders:N0} folder{(folders == 1 ? "" : "s")} · {files:N0} file{(files == 1 ? "" : "s")}";
         }
      }

      /// <summary>
      /// <c>true</c> when the server answers that its CDN is not turned on. While that holds, all commands
      /// except Refresh are off and the screen only shows its message.
      /// </summary>
      public bool IsCdnDisabled {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsEmpty)));
      }

      /// <summary><c>true</c> when the open folder was read successfully and is empty.</summary>
      public bool IsEmpty => IsLoaded && !IsCdnDisabled && Items.Count == 0;

      /// <summary>
      /// <c>true</c> since the first answer from the server was received - the folder content or the news
      /// that the CDN is off. Before that all commands except Refresh are off.
      /// </summary>
      public bool IsLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsEmpty)));
      }

      /// <summary>
      /// Caption of upload or download progress, e.g.
      /// <c>"setup.bin — 45% (45.2 MB of 100 MB) · 2 of 5"</c>; empty when nothing is running.
      /// </summary>
      public string TransferProgressCaption {
         get => Get<string>() ?? "";
         private set {
            var wasTransferring = IsTransferring;
            Set(value);
            // The caption changes up to ten times a second while a file moves; only its turning
            // empty or non-empty matters to anything else.
            if (wasTransferring == IsTransferring) return;

            NotifyChanged(nameof(IsTransferring));
            RaiseCommandsChanged();
         }
      }

      /// <summary>
      /// Percentage of the file being uploaded or downloaded that has been transferred, 0 to 100.
      /// </summary>
      public double TransferPercent {
         get => Get<double>();
         private set => Set(value);
      }

      /// <summary><c>true</c> while an upload or download is running.</summary>
      public bool IsTransferring => TransferProgressCaption.Length > 0;

      /// <summary>
      /// The CDN archive task that is still live - whoever's it is - or that failed and has not been cleared;
      /// <c>null</c> when there is none. Only one archive can run on the whole server.
      /// </summary>
      public BusinessTaskInfo? ArchiveTask {
         get => Get<BusinessTaskInfo?>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(ArchiveMode));
            NotifyChanged(nameof(ArchiveCaption));
            NotifyChanged(nameof(ArchiveTooltip));
            Commands[nameof(ArchiveCommand)]?.RaiseCanExecuteChanged();
         });
      }

      /// <summary>State of the Create Archive button.</summary>
      public CdnTaskMode ArchiveMode => ModeOf(ArchiveTask);

      /// <summary>Text of the Create Archive button, according to its state.</summary>
      public string ArchiveCaption => ArchiveTask switch {
         null => "Create Archive",
         { Status: BusinessTaskStatus.Queued } => "Archive Queued",
         { Status: BusinessTaskStatus.Running, Percent: { } percent } => $"Archiving {percent:0}%",
         { Status: BusinessTaskStatus.Running } => "Archiving...",
         _ => "Archive Failed"
      };

      /// <summary>Tooltip of the Create Archive button, according to its state.</summary>
      public string ArchiveTooltip => ArchiveMode switch {
         CdnTaskMode.Running => $"{ProgressText(ArchiveTask!)} - click to cancel",
         CdnTaskMode.Failed => "The archive failed - click for details",
         _ => "Create a zip file from the selected items"
      };

      #endregion

      #region Commands

      /// <summary>Opens the folder in row <paramref name="item"/>.</summary>
      public Task OpenFolderCommand(CdnManagerItem? item) => LoadAsync(item!.Entry.Path);

      /// <summary>Only for a folder row, and while the screen is not busy.</summary>
      public bool OpenFolderCommandAllowed(CdnManagerItem? item) => item is { IsFolder: true } && CanAct;

      /// <summary>Goes up one folder.</summary>
      public Task GoUpCommand() {
         var index = CurrentPath.LastIndexOf('/');
         return LoadAsync(index < 0 ? "" : CurrentPath[..index]);
      }

      /// <summary>Only when the open folder is not the root.</summary>
      public bool GoUpCommandAllowed() => CurrentPath.Length > 0 && CanAct;

      /// <summary>Opens the folder represented by breadcrumb piece <paramref name="crumb"/>.</summary>
      public Task NavigateToSegmentCommand(CdnBreadcrumb? crumb) => LoadAsync(crumb!.Path);

      /// <summary>Only for a piece that is not the folder currently open.</summary>
      public bool NavigateToSegmentCommandAllowed(CdnBreadcrumb? crumb) =>
         crumb is not null && crumb.Path != CurrentPath && CanAct;

      /// <summary>Chooses one or several files, then uploads them one by one to this folder.</summary>
      public async Task UploadCommand() {
         var dialog = new OpenFileDialog {
            Title = "Upload to CDN",
            Multiselect = true,
            CheckFileExists = true
         };
         if (dialog.ShowDialog(DialogOwner) != true) return;

         var folder = CurrentPath;
         await UploadBatchAsync([],
            dialog.FileNames.Select(r => new UploadItem(r, folder, Path.GetFileName(r))).ToList(), []);
      }

      /// <summary>Only when the CDN is on and the screen is not busy.</summary>
      public bool UploadCommandAllowed() => CanAct;

      /// <summary>
      /// Stops the upload or download that is running. The file being transferred is cut off midway without
      /// leaving a remnant - the server does not keep an interrupted upload, and an interrupted download is
      /// removed from disk - and the rest of the queue is not carried out.
      /// </summary>
      public void CancelTransferCommand() {
         _transferCancel?.Cancel();
         RaiseCommandsChanged();
      }

      /// <summary>Only while an upload or download is running and has not yet been asked to stop.</summary>
      public bool CancelTransferCommandAllowed() => IsTransferring && _transferCancel is { IsCancellationRequested: false };

      /// <summary>Asks for a name, then creates a new subfolder in this folder.</summary>
      public async Task NewFolderCommand() {
         var dialog = new TextInputDialog("New Folder", "Name of the folder to create in this folder.",
            "Folder name", "Create", EFontAwesomeIcon.Solid_FolderPlus) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.Result;
         await RunBusyAsync("Creating folder...", async () => {
            await Service.PostGetMeta_CdnCreateFolder(CurrentPath, name);
            await ReadFolderAsync(CurrentPath, select: name);
         });
      }

      /// <summary>Only when the CDN is on and the screen is not busy.</summary>
      public bool NewFolderCommandAllowed() => CanAct;

      /// <summary>
      /// Deletes row <paramref name="item"/> after confirmation. For a folder, its content is counted first
      /// so the confirmation states how much will be deleted with it.
      /// </summary>
      public async Task DeleteCommand(CdnManagerItem? item) {
         if (item is null || DialogOwner is not { } owner) return;

         string caption;
         if (item.IsFolder) {
            CdnItemCount? count = null;
            await RunBusyAsync("Counting folder content...", async () =>
               count = await Service.GetMeta_CdnItemCount(item.Entry.Path));
            if (count is null) return;

            caption = $"Delete folder '{item.Name}' and the {count.Files:N0} file(s) and {count.Folders:N0} folder(s) inside it?";
         }
         else {
            caption = $"Delete file '{item.Name}'?";
         }

         if (owner.ShowMboxDecideWarning(caption + "\n\nThis cannot be undone.", "Delete") != MessageBoxResult.Yes)
            return;

         await RunBusyAsync("Deleting...", async () => {
            await Service.PostMeta_CdnDelete(item.Entry.Path);
            await ReadFolderAsync(CurrentPath);
         });
      }

      /// <summary>Only for a row, and while the screen is not busy.</summary>
      public bool DeleteCommandAllowed(CdnManagerItem? item) => item is not null && CanAct;

      /// <summary>Reads the open folder again.</summary>
      public Task RefreshCommand() => ReloadAsync();

      /// <summary>Always allowed while the screen is not busy, including when the CDN is off.</summary>
      public bool RefreshCommandAllowed() => IsNotBusy;

      /// <summary>
      /// Copies the public link of row <paramref name="item"/> to the clipboard. The link of a folder ends
      /// with <c>/</c>.
      /// </summary>
      public void CopyLinkCommand(CdnManagerItem? item) {
         if (item is null) return;

         var host = EmApp?.ActiveConnection?.Host.TrimEnd('/') ?? "";
         var link = $"{host}/{PublicPath}{Uri.EscapeDataString(item.Name)}{(item.IsFolder ? "/" : "")}";
         try {
            Clipboard.SetText(link);
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Only for a row, when the CDN is on and the folder has been read.</summary>
      public bool CopyLinkCommandAllowed(CdnManagerItem? item) => item is not null && CanAct && PublicPath.Length > 0;

      /// <summary>
      /// Downloads row <paramref name="item"/> to this computer, with progress in the toolbar and able to be
      /// stopped. A file is saved to the place chosen through the save dialog; a folder is downloaded with all
      /// its content into the chosen folder. The download runs inside the application, so it is cut off when
      /// the application is closed.
      /// </summary>
      public Task DownloadCommand(CdnManagerItem? item) =>
         item is null ? Task.CompletedTask
         : item.IsFolder ? DownloadFolderAsync(item)
         : DownloadFileAsync(item);

      /// <summary>Only for a row, and while the screen is not busy.</summary>
      public bool DownloadCommandAllowed(CdnManagerItem? item) => item is not null && CanAct;

      /// <summary>
      /// Opens the open folder in the default browser through its public address. The browser then shows its
      /// listing and downloads files with its own download manager, independent of this application.
      /// </summary>
      public void OpenInBrowserCommand() {
         var host = EmApp?.ActiveConnection?.Host.TrimEnd('/') ?? "";
         try {
            Process.Start(new ProcessStartInfo($"{host}/{PublicPath}") { UseShellExecute = true });
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Only when the CDN is on and the folder has been read.</summary>
      public bool OpenInBrowserCommandAllowed() => CanAct && PublicPath.Length > 0;

      /// <summary>
      /// Accepts a drop on this screen. A dropped CDN row is moved; files and folders from Explorer are
      /// uploaded, folders with all their content, and merged with an existing folder of the same name. The
      /// target is the folder row or breadcrumb piece it was dropped on (<see cref="DropRequest"/>), or the
      /// open folder when dropped on the list.
      /// </summary>
      public Task DropCommand(object? payload) {
         var (dropped, target) = ResolveDrop(payload);
         return dropped switch {
            CdnManagerItem item => MoveAsync(item, target!),
            DroppedFiles files => UploadDroppedAsync(files.Paths, target!),
            _ => Task.CompletedTask
         };
      }

      /// <summary>
      /// A CDN row may be dropped on any folder except its origin folder, itself, or a folder inside itself;
      /// files from Explorer may be dropped on any folder.
      /// </summary>
      public bool DropCommandAllowed(object? payload) {
         if (!CanAct) return false;

         var (dropped, target) = ResolveDrop(payload);
         if (target is null) return false;

         return dropped switch {
            DroppedFiles => true,
            CdnManagerItem item => target != ParentOf(item.Entry.Path) && target != item.Entry.Path &&
                                   !target.StartsWith(item.Entry.Path + "/", StringComparison.OrdinalIgnoreCase),
            _ => false
         };
      }

      /// <summary>
      /// One button for the whole archive cycle. Without an archive: creates a zip from the selected rows.
      /// While an archive runs - started by anyone - cancels it. After an archive failed: shows its error and
      /// then clears it.
      /// </summary>
      public Task ArchiveCommand() => ArchiveMode switch {
         CdnTaskMode.Running => CancelArchiveAsync(),
         CdnTaskMode.Failed => ClearArchiveAsync(),
         _ => StartArchiveAsync()
      };

      /// <summary>
      /// Creating an archive needs at least one selected row; cancelling and clearing follow the rights
      /// reported by the server.
      /// </summary>
      public bool ArchiveCommandAllowed() => ArchiveMode switch {
         CdnTaskMode.Running => IsNotBusy && ArchiveTask!.CanCancel,
         CdnTaskMode.Failed => IsNotBusy && ArchiveTask!.CanClear,
         _ => CanAct && Items.Any(r => r.IsSelected)
      };

      /// <summary>
      /// One button for the whole extract cycle on row <paramref name="item"/>. Without a task: extracts that
      /// zip in this folder, asking about overwriting when a file collides. While running: cancels it. After
      /// failing: shows its error and then clears it.
      /// </summary>
      public Task ExtractCommand(CdnManagerItem? item) =>
         item is null ? Task.CompletedTask
         : item.ExtractMode switch {
            CdnTaskMode.Running => CancelExtractAsync(item),
            CdnTaskMode.Failed => ClearExtractAsync(item),
            _ => StartExtractAsync(item)
         };

      /// <summary>Only for a zip file row; cancelling and clearing follow the rights from the server.</summary>
      public bool ExtractCommandAllowed(CdnManagerItem? item) =>
         item is { IsZip: true } && item.ExtractMode switch {
            CdnTaskMode.Running => IsNotBusy && item.ExtractTask!.CanCancel,
            CdnTaskMode.Failed => IsNotBusy && item.ExtractTask!.CanClear,
            _ => CanAct
         };

      #endregion

      #region Business tasks

      /// <summary>Starts monitoring archive and extract; called when the screen is shown.</summary>
      public void StartTaskPolling() {
         _visible = true;
         _taskTimer.Start();
      }

      /// <summary>Stops monitoring archive and extract; called when the screen is no longer shown.</summary>
      public void StopTaskPolling() {
         _visible = false;
         _taskTimer.Stop();
      }

      internal void OnItemSelectionChanged() => Commands[nameof(ArchiveCommand)]?.RaiseCanExecuteChanged();

      internal void ApplySelection(System.Collections.IList added, System.Collections.IList removed) {
         foreach (var item in added.OfType<CdnManagerItem>()) item.IsSelected = true;
         foreach (var item in removed.OfType<CdnManagerItem>()) item.IsSelected = false;
      }

      internal static CdnTaskMode ModeOf(BusinessTaskInfo? task) =>
         task switch {
            null => CdnTaskMode.None,
            { IsAlive: true } => CdnTaskMode.Running,
            { Status: BusinessTaskStatus.Failed } => CdnTaskMode.Failed,
            _ => CdnTaskMode.None
         };

      internal static string ProgressText(BusinessTaskInfo task) {
         var state = task.Status == BusinessTaskStatus.Queued ? "Queued"
            : task.Percent is { } percent ? $"{percent:0}%"
            : "Running";
         var caption = task.Caption.Length > 0 ? $" · {task.Caption}" : "";
         return $"{task.Title}: {state}{caption} (started by {task.OwnerName})";
      }

      // A task that was alive at the last look and is now neither alive nor failed has succeeded or been
      // canceled; only then may the folder have changed, so only then is it read again.
      private async Task PollTasksAsync() {
         if (!_visible || _taskPolling || IsBusy || !IsLoaded || IsCdnDisabled) return;

         _taskPolling = true;
         try {
            if (await ReadTasksAsync(detectFinished: true))
               await RunBusyAsync("Loading...", () => ReadFolderAsync(CurrentPath), showOverlay: false);
         }
         catch (Exception) {
            // A background poll has nobody to show an error to; the next one tries again.
         }
         finally {
            _taskPolling = false;
         }
      }

      private async Task<bool> ReadTasksAsync(bool detectFinished) {
         var folder = CurrentPath;
         var archive = await Service.GetMeta_CdnArchiveTask();
         var extracts = await Service.GetMeta_CdnExtractTasks(folder);
         // The folder changed while the answers were out; they describe the one left behind.
         if (folder != CurrentPath) return false;

         var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         if (archive is { IsAlive: true }) alive.Add(archive.Key);
         foreach (var task in extracts.Where(r => r.IsAlive)) alive.Add(task.Key);

         var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         if (archive is not null) known.Add(archive.Key);
         foreach (var task in extracts) known.Add(task.Key);
         var finished = detectFinished && _aliveTaskKeys.Any(r => !known.Contains(r));

         _aliveTaskKeys = alive;
         ArchiveTask = archive;
         foreach (var item in Items) {
            item.ExtractTask = item.IsZip
               ? extracts.FirstOrDefault(r =>
                  string.Equals(r.Key, ExtractTaskKeyPrefix + item.Entry.Path, StringComparison.OrdinalIgnoreCase))
               : null;
         }

         _taskTimer.Interval = alive.Count > 0 ? FastTaskPoll : SlowTaskPoll;
         RaiseCommandsChanged();
         return finished;
      }

      private async Task StartArchiveAsync() {
         var selected = Items.Where(r => r.IsSelected).ToArray();
         if (selected.Length == 0 || DialogOwner is not { } owner) return;

         var dialog = new TextInputDialog("Create Archive",
            $"Name of the zip file to create from the {selected.Length:N0} selected item(s). It is written to this folder.",
            "archive.zip", "Create", EFontAwesomeIcon.Solid_FileZipper) { Owner = owner };
         dialog.Vm.Value = DefaultArchiveName(selected);
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.Result;
         if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";

         var overwrite = false;
         if (Items.Any(r => !r.IsFolder && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) {
            if (owner.ShowMboxDecideWarning($"'{name}' already exists in this folder. Replace it?", "File Exists") !=
                MessageBoxResult.Yes) return;
            overwrite = true;
         }

         var request = new CdnArchiveRequest {
            Folder = CurrentPath,
            Names = [.. selected.Select(r => r.Name)],
            ArchiveName = name,
            Overwrite = overwrite
         };
         await RunTaskActionAsync("Starting archive...", "Create Archive",
            async () => ArchiveTask = await Service.PostGetMeta_CdnArchive(request));
      }

      // One item is named after itself (a file without its extension, as Explorer does); several are named
      // after the folder they are in.
      private string DefaultArchiveName(IReadOnlyList<CdnManagerItem> selected) {
         if (selected.Count == 1) {
            var item = selected[0];
            return (item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name)) + ".zip";
         }

         return CurrentPath.Length == 0 ? "archive.zip" : CurrentPath[(CurrentPath.LastIndexOf('/') + 1)..] + ".zip";
      }

      private async Task CancelArchiveAsync() {
         if (ArchiveTask is not { } task || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel '{task.Title}' started by {task.OwnerName}?", "Cancel Archive") !=
             MessageBoxResult.Yes) return;

         await RunTaskActionAsync("Canceling archive...", "Cancel Archive", () => Service.PostMeta_CdnArchiveCancel());
      }

      private async Task ClearArchiveAsync() {
         if (ArchiveTask is not { } task || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"'{task.Title}' failed:\n\n{task.ErrorMessage}\n\nClear this error?",
                "Archive Failed") != MessageBoxResult.Yes) return;

         await RunTaskActionAsync("Clearing archive...", "Clear Archive", () => Service.PostMeta_CdnArchiveClear());
      }

      private async Task StartExtractAsync(CdnManagerItem item) {
         if (DialogOwner is not { } owner) return;

         string[]? conflicts = null;
         await RunBusyAsync("Checking archive...", async () =>
            conflicts = await Service.GetMeta_CdnExtractConflicts(item.Entry.Path));
         if (conflicts is null) return;

         var overwrite = false;
         if (conflicts.Length > 0) {
            var list = string.Join("\n", conflicts.Take(10)) +
                       (conflicts.Length > 10 ? $"\n... and {conflicts.Length - 10:N0} more" : "");
            if (owner.ShowMboxDecideWarning(
                   $"Overwrite {conflicts.Length:N0} existing file(s)?\n\n{list}", "Extract Here") != MessageBoxResult.Yes)
               return;
            overwrite = true;
         }

         await RunTaskActionAsync("Starting extract...", "Extract Here",
            async () => item.ExtractTask = await Service.PostGetMeta_CdnExtract(item.Entry.Path, overwrite));
      }

      private async Task CancelExtractAsync(CdnManagerItem item) {
         if (item.ExtractTask is not { } task || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"Cancel '{task.Title}' started by {task.OwnerName}?", "Cancel Extract") !=
             MessageBoxResult.Yes) return;

         await RunTaskActionAsync("Canceling extract...", "Cancel Extract",
            () => Service.PostMeta_CdnExtractCancel(item.Entry.Path));
      }

      private async Task ClearExtractAsync(CdnManagerItem item) {
         if (item.ExtractTask is not { } task || DialogOwner is not { } owner) return;
         if (owner.ShowMboxDecideWarning($"'{task.Title}' failed:\n\n{task.ErrorMessage}\n\nClear this error?",
                "Extract Failed") != MessageBoxResult.Yes) return;

         await RunTaskActionAsync("Clearing extract...", "Clear Extract",
            () => Service.PostMeta_CdnExtractClear(item.Entry.Path));
      }

      // A 409 is the everyday answer here - somebody else started the archive first, or the task has just
      // finished - so it is shown as a notice rather than an error, and the status is read again either way.
      private async Task RunTaskActionAsync(string waiterText, string title, Func<Task> action) {
         var owner = DialogOwner;
         await RunBusyAsync(waiterText, async () => {
            try {
               await action();
            }
            catch (ActionException x) when (x.StatusCode == 409) {
               owner?.ShowMboxWarning(x.Message.Replace("Server Error: ", ""), title);
            }
            finally {
               await ReadTasksAsync(detectFinished: false);
            }
         });
      }

      #endregion

      #region Methods

      /// <summary>
      /// Reads the open folder again. Called by the host every time the screen is opened through navigation
      /// and from the Refresh button.
      /// </summary>
      public Task ReloadAsync() => LoadAsync(CurrentPath);

      private bool CanAct => IsNotBusy && !IsCdnDisabled && IsLoaded;

      private Task LoadAsync(string path) =>
         RunBusyAsync("Loading...", () => ReadFolderAsync(path));

      // Read explicit active status first; a missing folder never implies the service is disabled.
      private async Task ReadFolderAsync(string path, string? select = null) {
         var status = await Service.GetMeta_CdnStatus();
         if (!status.ActiveEnabled) { ShowDisabled(); return; }
         CdnFolderContent content;
         try {
            content = await Service.GetMeta_CdnFolder(path);
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            if (path.Length > 0) {
               await ReadFolderAsync("", select);
               return;
            }

            throw; // The explicit status was active: a missing folder is not a disabled store.
         }

         IsCdnDisabled = false;
         CurrentPath = content.Path;
         PublicPath = content.PublicPath;
         MaxFileSize = content.MaxFileSize;
         RebuildBreadcrumbs();

         Items.Clear();
         content.Entries.EachOf(r => Items.Add(new CdnManagerItem(r, this)));
         SelectedItem = select is null ? null : Items.FirstOrDefault(r => r.Name == select);
         IsLoaded = true;
         NotifyChanged(nameof(ItemsCaption));
         NotifyChanged(nameof(IsEmpty));
         // The rows are new objects, so the extract status of the zips in this folder is read again too.
         await ReadTasksAsync(detectFinished: false);
         await ReadStorageCardAsync();
      }

      /// <summary>Indicates storage refreshing.</summary>
      public bool IsStorageRefreshing { get => Get<bool>(); private set { Set(value); RaiseCommandsChanged(); } }
      /// <summary>Whether the refresh storage command may run now.</summary>
      public bool RefreshStorageCommandAllowed() => !IsStorageRefreshing;
      /// <summary>Runs the refresh storage command.</summary>
      public async Task RefreshStorageCommand() {
         try { await ReadStorageCardAsync(); } catch (Exception ex) { AlertError(ex); }
      }
      private async Task ReadStorageCardAsync() {
         if (IsStorageRefreshing) return;
         IsStorageRefreshing = true;
         try {
            var status = await Service.GetMeta_CdnStatus();
            if (!status.ActiveEnabled) { StorageCaption = "Storage: CDN disabled"; return; }
            var storage = await Service.GetMeta_CdnStorageSize();
            StorageCaption = $"Total CDN storage: {FormatSize(storage.TotalBytes)} · {storage.FileCount:N0} files";
         }
         catch { StorageCaption = "Storage: unavailable"; throw; }
         finally { IsStorageRefreshing = false; }
      }

      private void ShowDisabled() {
         StorageCaption = "Storage: CDN disabled";
         Items.Clear();
         SelectedItem = null;
         CurrentPath = "";
         PublicPath = "";
         MaxFileSize = 0;
         RebuildBreadcrumbs();
         IsLoaded = true;
         IsCdnDisabled = true;
         NotifyChanged(nameof(ItemsCaption));
      }

      private void RebuildBreadcrumbs() {
         Breadcrumbs.Clear();
         Breadcrumbs.Add(RootCrumb);
         if (CurrentPath.Length == 0) return;

         var path = "";
         foreach (var segment in CurrentPath.Split('/')) {
            path = path.Length == 0 ? segment : $"{path}/{segment}";
            Breadcrumbs.Add(new CdnBreadcrumb { Name = segment, Path = path });
         }
      }

      #region Drag and drop

      private sealed record UploadItem(string LocalPath, string Folder, string Name);

      private sealed record FolderToCreate(string Parent, string Name);

      // What was dropped, and the folder it was dropped on: a folder row or a breadcrumb segment
      // names itself through DropRequest, the list itself stands for the folder that is open.
      private (object? Dropped, string? Target) ResolveDrop(object? payload) =>
         payload switch {
            DropRequest { Target: CdnManagerItem { IsFolder: true } folder } request => (request.Payload, folder.Entry.Path),
            DropRequest { Target: CdnBreadcrumb crumb } request => (request.Payload, crumb.Path),
            DropRequest => (null, null),
            null => (null, null),
            _ => (payload, CurrentPath)
         };

      private static string ParentOf(string path) {
         var index = path.LastIndexOf('/');
         return index < 0 ? "" : path[..index];
      }

      private static string JoinPath(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

      private static string FolderLabel(string path) => path.Length == 0 ? "cdn" : path;

      private async Task MoveAsync(CdnManagerItem item, string target) {
         var owner = DialogOwner;
         await RunBusyAsync($"Moving {item.Name}...", async () => {
            try {
               await MoveOnceAsync(item, target, owner);
            }
            finally {
               await ReadFolderAsync(CurrentPath);
            }
         });
      }

      // A file that already exists at the target may be replaced once the user agrees; a folder is
      // never merged by a move, so that conflict is reported as it is.
      private async Task MoveOnceAsync(CdnManagerItem item, string target, Window? owner) {
         try {
            await Service.PostGetMeta_CdnMove(item.Entry.Path, target, false);
         }
         catch (ActionException x) when (x.StatusCode == 409 && !item.IsFolder) {
            var answer = owner?.ShowMboxDecideWarning(
               $"'{item.Name}' already exists in '{FolderLabel(target)}'. Replace it?", "File Exists");
            if (answer != MessageBoxResult.Yes) return;

            await Service.PostGetMeta_CdnMove(item.Entry.Path, target, true);
         }
      }

      // Folders are walked here, off the UI thread, and turned into the folders to create and the
      // files to send. Dot-prefixed, hidden and system entries are left out - the server refuses
      // dot-prefixed names anyway, and the other two are rarely meant to be published.
      private async Task UploadDroppedAsync(IReadOnlyList<string> paths, string target) {
         var folders = new List<FolderToCreate>();
         var files = new List<UploadItem>();
         var skipped = new List<string>();

         await Task.Run(() => {
            foreach (var path in paths) {
               if (Directory.Exists(path)) Collect(new DirectoryInfo(path), target);
               else if (File.Exists(path)) {
                  var file = new FileInfo(path);
                  if (IsSkipped(file)) skipped.Add(file.Name);
                  else files.Add(new UploadItem(file.FullName, target, file.Name));
               }
            }
         });

         await UploadBatchAsync(folders, files, skipped);
         return;

         void Collect(DirectoryInfo folder, string parent) {
            if (IsSkipped(folder)) {
               skipped.Add(folder.Name + "\\");
               return;
            }

            folders.Add(new FolderToCreate(parent, folder.Name));
            var remote = JoinPath(parent, folder.Name);

            foreach (var sub in folder.EnumerateDirectories()) Collect(sub, remote);

            foreach (var file in folder.EnumerateFiles()) {
               if (IsSkipped(file)) skipped.Add(Path.Combine(folder.Name, file.Name));
               else files.Add(new UploadItem(file.FullName, remote, file.Name));
            }
         }

         static bool IsSkipped(FileSystemInfo info) =>
            info.Name.StartsWith('.') || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
      }

      // One request per file, in order, so a conflict can be answered for that file alone and the
      // remaining queue can still be stopped. Files over the limit are refused here, before any of
      // their bytes are read, since the server would refuse them anyway after the whole transfer.
      // Folders are created first, parents before children; one that already exists is simply used,
      // which is what merging a dropped folder into an existing one amounts to.
      private async Task UploadBatchAsync(IReadOnlyList<FolderToCreate> folders, IReadOnlyList<UploadItem> files,
         IReadOnlyList<string> skipped) {
         var owner = DialogOwner;
         var tooLarge = files.Where(r => new FileInfo(r.LocalPath).Length > MaxFileSize).ToArray();

         var notices = new List<string>();
         if (tooLarge.Length > 0) {
            notices.Add($"Larger than the {FormatSize(MaxFileSize)} limit:\n" +
                        string.Join("\n", tooLarge.Take(15).Select(r => r.Name)) + More(tooLarge.Length));
         }

         if (skipped.Count > 0) {
            notices.Add("Hidden or starting with '.':\n" + string.Join("\n", skipped.Take(15)) + More(skipped.Count));
         }

         if (notices.Count > 0) {
            owner?.ShowMboxWarning("These items will be skipped.\n\n" + string.Join("\n\n", notices), "Upload");
         }

         var queue = files.Except(tooLarge).ToArray();
         if (queue.Length == 0 && folders.Count == 0) return;

         var failures = new List<string>();
         string? lastUploaded = null;
         using var cancel = new CancellationTokenSource();
         // No wait overlay here: it would cover the progress chip and the Cancel button beside it.
         // IsBusy still holds every other command off.
         await RunBusyAsync("Uploading...", async () => {
            _transferCancel = cancel;
            try {
               foreach (var folder in folders) {
                  cancel.Token.ThrowIfCancellationRequested();
                  TransferProgressCaption = $"Creating folder {folder.Name}...";
                  try {
                     await Service.PostGetMeta_CdnCreateFolder(folder.Parent, folder.Name);
                  }
                  catch (ActionException x) when (x.StatusCode == 409) {
                     // Already there: merge into it.
                  }
               }

               for (var index = 0; index < queue.Length; index++) {
                  cancel.Token.ThrowIfCancellationRequested();
                  var item = queue[index];
                  var position = $"{index + 1} of {queue.Length}";

                  try {
                     await SendFileAsync(item, false, position, cancel.Token);
                     if (item.Folder == CurrentPath) lastUploaded = item.Name;
                  }
                  catch (ActionException x) when (x.StatusCode == 409) {
                     var answer = owner?.ShowMboxDecideCancel(
                        $"'{item.Name}' already exists in '{FolderLabel(item.Folder)}'. Overwrite it?\n\n" +
                        "Yes: overwrite · No: skip this file · Cancel: stop uploading",
                        "File Exists") ?? MessageBoxResult.Cancel;
                     if (answer == MessageBoxResult.Cancel) break;
                     if (answer == MessageBoxResult.No) continue;

                     // A new stream from the start: the refused attempt may have read part of the old one.
                     await SendFileAsync(item, true, position, cancel.Token);
                     if (item.Folder == CurrentPath) lastUploaded = item.Name;
                  }
                  catch (ActionException x) when (x.StatusCode is 400 or 404 or 413) {
                     // One refused name must not cost the rest of a dropped folder.
                     failures.Add($"{JoinPath(item.Folder, item.Name)}: {x.Message.Replace("Server Error: ", "")}");
                  }
               }
            }
            catch (Exception) when (cancel.IsCancellationRequested) {
               // Stopped by the user. The transfer is cut off whichever way HttpClient reports it,
               // and a cancelled upload is not an error to show.
            }
            finally {
               _transferCancel = null;
               TransferProgressCaption = "";
               TransferPercent = 0;
               await ReadFolderAsync(CurrentPath, lastUploaded);
            }
         }, showOverlay: false);

         if (failures.Count > 0) {
            owner?.ShowMboxWarning("Some files were not uploaded:\n\n" +
                                   string.Join("\n", failures.Take(15)) + More(failures.Count), "Upload");
         }

         static string More(int count) => count > 15 ? $"\n... and {count - 15:N0} more" : "";
      }

      // Streams one file from disk, so its size costs neither memory nor a request timeout. The
      // progress callback is created here, on the UI thread, so its reports land back on it.
      private async Task SendFileAsync(UploadItem item, bool overwrite, string position, CancellationToken token) {
         await using var file = new FileStream(item.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
         var total = file.Length;
         ShowProgress(0);

         var progress = new Progress<long>(ShowProgress);
         await using var body = new UploadProgressStream(file, progress, token);
         await Service.PostGetMeta_CdnUpload(new CdnUploadRequest {
            Path = item.Folder,
            FileName = item.Name,
            Overwrite = overwrite
         }, body);
         ShowProgress(total);
         return;

         void ShowProgress(long sent) {
            var percent = total == 0 ? 100 : sent * 100d / total;
            TransferPercent = percent;
            TransferProgressCaption =
               $"{item.Name} — {percent:0}% ({FormatSize(sent)} of {FormatSize(total)}) · {position}";
         }
      }

      /// <summary>
      /// Wraps the file being uploaded: reports how far it has been read, at most ten times a second,
      /// and stops the upload by throwing from Read once the token is cancelled - HttpClient then cuts
      /// the request off, and the server discards what it had received. Seeking is passed through, so
      /// the request still carries a Content-Length and can be sent again after a token refresh.
      /// </summary>
      private sealed class UploadProgressStream(Stream inner, IProgress<long> progress, CancellationToken token)
         : Stream
      {
         private const long ReportIntervalMs = 100;
         private readonly System.Diagnostics.Stopwatch _sinceReport = System.Diagnostics.Stopwatch.StartNew();

         public override bool CanRead => true;
         public override bool CanSeek => inner.CanSeek;
         public override bool CanWrite => false;
         public override long Length => inner.Length;

         public override long Position {
            get => inner.Position;
            set => inner.Position = value;
         }

         public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

         public override int Read(Span<byte> buffer) {
            token.ThrowIfCancellationRequested();
            return Report(inner.Read(buffer));
         }

         public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

         public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            token.ThrowIfCancellationRequested();
            return Report(await inner.ReadAsync(buffer, cancellationToken));
         }

         private int Report(int read) {
            if (read == 0 || _sinceReport.ElapsedMilliseconds >= ReportIntervalMs) {
               _sinceReport.Restart();
               progress.Report(inner.Position);
            }

            return read;
         }

         public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

         public override void Flush() { }

         public override void SetLength(long value) => throw new NotSupportedException();

         public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      }

      /// <summary>
      /// Composes the files and folders handed to Explorer when <paramref name="item"/> is dragged out of the
      /// application. For a folder, all its content is read from the server in a single request. The content
      /// itself is only downloaded when Explorer asks for it, straight to the destination folder.
      /// </summary>
      internal IReadOnlyList<VirtualFile> ResolveVirtualFiles(CdnManagerItem item) {
         var connection = EmApp?.ActiveConnection
                          ?? throw new InvalidOperationException("No active connection to download from.");
         var client = DownloadClients.Get(connection.IgnoreSslErrors);
         var host = connection.Host.TrimEnd('/');

         if (!item.IsFolder) return [ToVirtualFile(item.Entry, item.Name)];

         // Run off the UI thread and waited for here: this is called from inside the drag loop,
         // where there is nothing to await with.
         var service = Service;
         var tree = Task.Run(() => service.GetMeta_CdnTree(item.Entry.Path)).GetAwaiter().GetResult();
         var prefix = item.Entry.Path + "/";

         var files = new List<VirtualFile> { ToVirtualFile(item.Entry, item.Name) };
         files.AddRange(tree.Select(r =>
            ToVirtualFile(r, item.Name + "\\" + r.Path[prefix.Length..].Replace('/', '\\'))));
         return files;

         VirtualFile ToVirtualFile(CdnEntry entry, string relativePath) => new() {
            RelativePath = relativePath,
            IsFolder = entry.IsFolder,
            Size = entry.Size,
            LastWriteTimeUtc = entry.LastModified.UtcDateTime,
            OpenRead = entry.IsFolder ? null : offset => OpenDownload(client, PublicUrl(host, entry.Path), offset)
         };
      }

      private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

      // Called by the stream on an MTA thread when Explorer asks for bytes, so blocking here is fine.
      // The public route is used: it streams, it needs no session, and it answers Range, which is
      // what lets the stream start anywhere.
      private static Stream OpenDownload(HttpClient client, string url, long offset) {
         var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
         if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

         var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
         response.EnsureSuccessStatusCode();
         if (offset > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) {
            response.Dispose();
            throw new IOException($"The server did not honour the byte range for '{url}'.");
         }

         return response.Content.ReadAsStream();
      }

      // One client per certificate setting for the life of the application, as HttpClient is meant
      // to be used; downloads have no timeout of their own, a large file may take as long as it takes.
      private static class DownloadClients
      {
         private static readonly Lazy<HttpClient> Validating = new(() => Create(false));
         private static readonly Lazy<HttpClient> Lenient = new(() => Create(true));

         public static HttpClient Get(bool ignoreSslErrors) => ignoreSslErrors ? Lenient.Value : Validating.Value;

         private static HttpClient Create(bool ignoreSslErrors) =>
            new(Defaults.CreateHttpClientHandler(ignoreSslErrors)) { Timeout = Timeout.InfiniteTimeSpan };
      }

      #endregion

      #region Download

      private sealed record DownloadItem(CdnEntry Entry, string LocalPath);

      private async Task DownloadFileAsync(CdnManagerItem item) {
         var dialog = new SaveFileDialog {
            Title = "Download from CDN",
            FileName = item.Name,
            Filter = "All files|*.*"
         };
         if (dialog.ShowDialog(DialogOwner) != true) return;

         await DownloadBatchAsync([], [new DownloadItem(item.Entry, dialog.FileName)]);
      }

      // The folder is recreated under its own name inside the one picked, as a copy in Explorer
      // would; one that is already there is merged into once the user agrees.
      private async Task DownloadFolderAsync(CdnManagerItem item) {
         var dialog = new OpenFolderDialog { Title = $"Download '{item.Name}' into" };
         if (dialog.ShowDialog(DialogOwner) != true) return;

         var root = Path.Combine(dialog.FolderName, item.Name);
         if (Directory.Exists(root) && DialogOwner?.ShowMboxDecideWarning(
                $"'{item.Name}' already exists in '{dialog.FolderName}'.\n\n" +
                "Files with the same name inside it will be overwritten. Continue?", "Folder Exists") != MessageBoxResult.Yes)
            return;

         CdnEntry[]? tree = null;
         await RunBusyAsync("Reading folder...", async () => tree = await Service.GetMeta_CdnTree(item.Entry.Path));
         if (tree is null) return;

         var prefix = item.Entry.Path + "/";
         await DownloadBatchAsync(
            [root, .. tree.Where(r => r.IsFolder).Select(LocalPathOf)],
            tree.Where(r => !r.IsFolder).Select(r => new DownloadItem(r, LocalPathOf(r))).ToArray());
         return;

         string LocalPathOf(CdnEntry entry) =>
            Path.Combine(root, entry.Path[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
      }

      // One file at a time, so the queue can be stopped between files and one failure in a folder
      // does not cost the rest. A lone file that fails is reported as the error it is.
      private async Task DownloadBatchAsync(IReadOnlyList<string> folders, IReadOnlyList<DownloadItem> files) {
         if (EmApp?.ActiveConnection is not { } connection) return;

         var client = DownloadClients.Get(connection.IgnoreSslErrors);
         var host = connection.Host.TrimEnd('/');
         var owner = DialogOwner;
         var failures = new List<string>();
         using var cancel = new CancellationTokenSource();
         // No wait overlay, as for uploads: it would cover the progress chip and its Cancel button.
         await RunBusyAsync("Downloading...", async () => {
            _transferCancel = cancel;
            try {
               foreach (var folder in folders) Directory.CreateDirectory(folder);

               for (var index = 0; index < files.Count; index++) {
                  cancel.Token.ThrowIfCancellationRequested();
                  var item = files[index];
                  try {
                     await ReceiveFileAsync(client, host, item, $"{index + 1} of {files.Count}", cancel.Token);
                  }
                  catch (Exception x) when (files.Count > 1 && !cancel.IsCancellationRequested &&
                                            x is HttpRequestException or IOException or UnauthorizedAccessException) {
                     failures.Add($"{item.Entry.Path}: {x.Message}");
                  }
               }
            }
            catch (Exception) when (cancel.IsCancellationRequested) {
               // Stopped by the user; the file in progress has already been thrown away.
            }
            finally {
               _transferCancel = null;
               TransferProgressCaption = "";
               TransferPercent = 0;
            }
         }, showOverlay: false);

         if (failures.Count > 0) {
            owner?.ShowMboxWarning("Some files were not downloaded:\n\n" + string.Join("\n", failures.Take(15)) +
                                   (failures.Count > 15 ? $"\n... and {failures.Count - 15:N0} more" : ""), "Download");
         }
      }

      // Streamed into a ".partial" file beside the target, which takes the target's name only once
      // complete: a download cut off - by Cancel, a lost connection or an error - never leaves a
      // half file under the real name, nor damages the file it was meant to replace. Only a closed
      // application can leave the ".partial" behind, and its name says what it is. The copy loop
      // runs off the UI thread; its progress reports come back through Progress, created here.
      private async Task ReceiveFileAsync(HttpClient client, string host, DownloadItem item, string position,
         CancellationToken token) {
         var total = item.Entry.Size;
         var partial = item.LocalPath + ".partial";
         ShowProgress(0);
         var progress = new Progress<long>(ShowProgress);

         try {
            using var response = await client.GetAsync(PublicUrl(host, item.Entry.Path),
               HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            total = response.Content.Headers.ContentLength ?? total;

            await Task.Run(async () => {
               await using var source = await response.Content.ReadAsStreamAsync(token);
               await using var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                  81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

               var buffer = new byte[81920];
               var received = 0L;
               var sinceReport = Stopwatch.StartNew();
               int read;
               while ((read = await source.ReadAsync(buffer, token)) > 0) {
                  await target.WriteAsync(buffer.AsMemory(0, read), token);
                  received += read;
                  if (sinceReport.ElapsedMilliseconds < 100) continue;

                  sinceReport.Restart();
                  ((IProgress<long>)progress).Report(received);
               }
            }, token);

            File.SetLastWriteTimeUtc(partial, item.Entry.LastModified.UtcDateTime);
            File.Move(partial, item.LocalPath, overwrite: true);
            ShowProgress(total);
         }
         catch {
            try {
               File.Delete(partial);
            }
            catch (Exception) {
               // Best effort: the original error is the one worth reporting.
            }

            throw;
         }

         return;

         void ShowProgress(long received) {
            var percent = total == 0 ? 100 : received * 100d / total;
            TransferPercent = percent;
            TransferProgressCaption =
               $"{item.Entry.Name} — {percent:0}% ({FormatSize(received)} of {FormatSize(total)}) · {position}";
         }
      }

      private static string PublicUrl(string host, string path) => $"{host}/cdn/{EscapePath(path)}";

      #endregion

      // showOverlay: false keeps IsBusy - every command except CancelTransfer stays off - without the wait
      // overlay, for work that shows its own progress.
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

      // Every command here depends on the same few facts - busy, CDN off, a row selected - so they
      // are re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      internal static string FormatSize(long bytes) {
         string[] units = ["B", "KB", "MB", "GB", "TB"];
         double value = bytes;
         var unit = 0;
         while (value >= 1024 && unit < units.Length - 1) {
            value /= 1024;
            unit++;
         }

         return unit == 0
            ? $"{bytes:N0} B"
            : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + units[unit];
      }

      #endregion
   }
}
