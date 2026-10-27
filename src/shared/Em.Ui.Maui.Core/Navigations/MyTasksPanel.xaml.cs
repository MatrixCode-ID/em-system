using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Core.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Maui.Navigations;

/// <summary>The shell's virtual, adaptive task summary panel.</summary>
public partial class MyTasksPanel : ContentView
{
   /// <summary>Creates the panel.</summary>
   public MyTasksPanel() { InitializeComponent(); }
   /// <summary>The panel state, supplied by the shell from its shared task cache.</summary>
   public MyTasksPanelVm Vm => (MyTasksPanelVm)BindingContext;
   internal void FocusFirst() => CloseButton.Focus();
}

/// <summary>A task type and its business context from the existing hub.</summary>
public sealed class HubTaskRow(HubTaskInfo info, bool canNavigate) : MvvmModelBase
{
   /// <summary>The unmodified server summary used by the existing wrapper route.</summary>
   public HubTaskInfo Info { get; } = info;
   /// <summary>The group title and server-reported work count.</summary>
   public string Caption => $"{Info.Title} · {Info.Count.ToString(CultureInfo.GetCultureInfo("en-US"))} waiting";
   /// <summary>The source-provided explanation below the title.</summary>
   public string Detail => Info.Description ?? Info.Source;
   /// <summary>The source-provided group title.</summary>
   public string Title => Info.Title;
   /// <summary>Stable identity for expansion state within an account and connection.</summary>
   public string Key => Info.Source + ":" + Info.Id;
   /// <summary>Whether a permitted registered destination exists.</summary>
   public bool CanNavigate { get; } = canNavigate;
   /// <summary>Whether the source supports the existing approval preview query.</summary>
   public bool CanPreview => Info.NavigationTarget == ApprovalManagerNavigationPayload.NavigationName && ApprovalManagerFilter.TryParse(Info.NavigationParameter, out _, out _);
   /// <summary>Whether the bounded request preview is disclosed.</summary>
   public bool IsExpanded { get => Get<bool>(); set => Set(value, _ => { NotifyChanged(nameof(Disclosure)); NotifyChanged(nameof(ShowViewAll)); }); }
   /// <summary>Whether the group needs a visible route to its complete list.</summary>
   public bool ShowViewAll => CanNavigate && (!CanPreview || IsExpanded);
   /// <summary>A disclosure mark rather than a right navigation arrow.</summary>
   public string Disclosure => IsExpanded ? FontIcons.ChevronUp : FontIcons.ChevronDown;
   /// <summary>Whether this group's preview is being refreshed.</summary>
   public bool IsLoading { get => Get<bool>(); set => Set(value); }
   /// <summary>Loading, partial-preview or recoverable failure information.</summary>
   public string PreviewStatus { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>The bounded server-authorized preview, never the whole document.</summary>
   public ObservableCollection<HubRequestRow> Requests { get; } = [];
   /// <summary>The complete group count, independent of the preview limit.</summary>
   public string ViewAllCaption => "View all (" + Info.Count.ToString(CultureInfo.GetCultureInfo("en-US")) + ")";
}

/// <summary>A server-authorized pending request, shown without fetching its confidential document.</summary>
public sealed record HubRequestRow(ta_ApprovalRequest Request, bool CanNavigate)
{
   /// <summary>The already-authorized business identity.</summary>
   public string Summary => Request.cSummary;
   /// <summary>Optional business context from the list response.</summary>
   public string Context => Request.cSubSummary ?? "";
   /// <summary>The stage explicitly requested by this preview query.</summary>
   public string Status => "Pending";
   /// <summary>Elapsed wait at the active level.</summary>
   public string Age => Request.Waiting;
   /// <summary>Active step and request timestamp in the established culture.</summary>
   public string Metadata => string.Join(" · ", new[] { Request.CurrentStep, Request.datestamp.ToString("dd-MMM-yyyy HH:mm", CultureInfo.GetCultureInfo("en-US")) }.Where(x => !string.IsNullOrWhiteSpace(x)));
   /// <summary>The native navigation action's spoken name.</summary>
   public string AccessibleName => "Open " + Summary + ", Pending";
}

/// <summary>Presentation only: the existing wrapper catalog owns task routes and permissions.</summary>
public sealed class MyTasksPanelVm : MvvmModelBase
{
   public event EventHandler? CloseRequested;
   public ObservableCollection<HubTaskRow> Tasks { get; } = [];
   /// <summary>The searched and sorted groups displayed by the virtual list.</summary>
   public ObservableCollection<HubTaskRow> VisibleTasks { get; } = [];
   /// <summary>Whether the workspace is visible; hidden panels do not start preview queries.</summary>
   public bool IsActive { get; set; }
   private bool _opening;
   private int _version;
   private bool _released;
   private object? _user, _connection, _snapshot;
   private readonly HashSet<string> _expanded = [];
   public MyTasksPanelVm() {
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(OpenCommand), OpenCommand, OpenCommandAllowed);
      RegisterCommand(nameof(CloseCommand), CloseCommand);
      RegisterCommand(nameof(ToggleGroupCommand), ToggleGroupCommand);
      RegisterCommand(nameof(OpenRequestCommand), OpenRequestCommand, OpenRequestCommandAllowed);
      RegisterCommand(nameof(SortCommand), SortCommand);
      RegisterCommand(nameof(HomeCommand), HomeCommand);
      RegisterCommand(nameof(AllModulesCommand), AllModulesCommand, AllModulesCommandAllowed);
   }
   /// <summary>Searches group names and descriptions, not just a partial document preview.</summary>
   public string SearchText { get => Get<string>() ?? ""; set => Set(value, _ => Filter()); }
   /// <summary>Uses the source's oldest age rather than inventing a due date.</summary>
   public bool OldestFirst { get => Get(true); private set => Set(value, _ => { NotifyChanged(nameof(SortCaption)); Filter(); }); }
   /// <summary>The ordering currently applied to task groups.</summary>
   public string SortCaption => OldestFirst ? "Longest waiting first" : "Module name";
   /// <summary>The hub's real total without fabricated status subtotals.</summary>
   public string AssignedCaption => "Assigned (" + (EmApp?.HubTaskCount ?? 0).ToString(CultureInfo.GetCultureInfo("en-US")) + ")";
   /// <summary>Switches between the two available source-based orderings.</summary>
   public void SortCommand() => OldestFirst = !OldestFirst;
   private void Filter() {
      VisibleTasks.Clear();
      var query = SearchText.Trim();
      var rows = Tasks.Where(x => query.Length == 0 || (x.Caption + " " + x.Detail).Contains(query, StringComparison.OrdinalIgnoreCase));
      rows = OldestFirst ? rows.OrderByDescending(x => x.Info.OldestAge) : rows.OrderBy(x => x.Info.Title, StringComparer.OrdinalIgnoreCase);
      foreach (var row in rows) VisibleTasks.Add(row);
      NotifyChanged(nameof(HasNoMatches));
   }
   /// <summary>Distinguishes a search with no match from an empty task snapshot.</summary>
   public bool HasNoMatches => Tasks.Count > 0 && VisibleTasks.Count == 0;
   public string CountAndTime => EmApp?.HubTaskRefreshedAt is { } time
      ? $"{EmApp.HubTaskCount.ToString(CultureInfo.GetCultureInfo("en-US"))} waiting · updated {time.ToString("dd-MMM-yyyy HH:mm:ss zzz", CultureInfo.GetCultureInfo("en-US"))}"
      : "Task count not loaded yet";
   public bool IsLoading => _opening || EmApp?.HubTaskIsLoading == true;
   public string StatusText { get => Get<string>() ?? ""; private set => Set(value); }
   public FeedbackKind StatusKind { get => Get<FeedbackKind>(); private set => Set(value); }
   public void Update() {
      if (_released) return;
      if (EmApp is { } app && (!ReferenceEquals(_snapshot, app.HubTasks) || !ReferenceEquals(_user, app.ActiveUser) || !ReferenceEquals(_connection, app.ActiveConnection))) {
         var identityChanged = !ReferenceEquals(_user, app.ActiveUser) || !ReferenceEquals(_connection, app.ActiveConnection);
         var firstContent = Tasks.Count == 0;
         _version++; _snapshot = app.HubTasks; _user = app.ActiveUser; _connection = app.ActiveConnection;
         if (identityChanged) { _expanded.Clear(); SearchText = ""; }
         Tasks.Clear();
         foreach (var task in app.HubTasks.Where(x => x.Count > 0)) {
            var target = app.ServiceProvider.GetRequiredService<ApprovalWrapperCatalog>().Resolve(task);
            var navigation = app.Navigations.FirstOrDefault(x => x.Name == target?.NavigationName);
            var row = new HubTaskRow(task, navigation is not null && app.CanOpen(navigation));
            if ((identityChanged || firstContent) && Tasks.Count == 0 && row.CanPreview) _expanded.Add(row.Key);
            row.IsExpanded = _expanded.Contains(row.Key); Tasks.Add(row);
         }
         Filter();
      }
      if (IsActive) foreach (var row in Tasks.Where(x => x.IsExpanded && x.Requests.Count == 0 && !x.IsLoading)) _ = LoadPreviewAsync(row);
      StatusKind = EmApp?.HubTaskLoadFailed == true ? FeedbackKind.Stale : FeedbackKind.Info;
      StatusText = EmApp?.HubTaskLoadFailed == true
         ? (Tasks.Count > 0 ? "Refresh failed. Showing the last successful snapshot. Refresh to retry." : "Could not load tasks. Refresh to retry.")
         : IsLoading ? "Loading your tasks…" : EmApp?.HubTaskRefreshedAt is null ? "Tasks have not been loaded." : Tasks.Count == 0 ? "No tasks waiting for you." : "";
      NotifyChanged(nameof(CountAndTime)); NotifyChanged(nameof(IsLoading));
      NotifyChanged(nameof(AssignedCaption));
      Commands[nameof(RefreshCommand)]?.RaiseCanExecuteChanged();
      Commands[nameof(AllModulesCommand)]?.RaiseCanExecuteChanged();
   }
   /// <summary>Discloses a bounded preview without deciding or opening a request.</summary>
   public async Task ToggleGroupCommand(object? value) {
      if (value is not HubTaskRow row || !row.CanPreview || !Tasks.Contains(row)) return;
      row.IsExpanded = !row.IsExpanded;
      if (row.IsExpanded) { _expanded.Add(row.Key); await LoadPreviewAsync(row); }
      else _expanded.Remove(row.Key);
   }
   private async Task LoadPreviewAsync(HubTaskRow row) {
      if (row.IsLoading || !row.CanPreview || EmApp is not { } app || !ApprovalManagerFilter.TryParse(row.Info.NavigationParameter, out var kind, out var name)) return;
      var version = _version; var user = app.ActiveUser; var connection = app.ActiveConnection;
      row.IsLoading = true; row.PreviewStatus = "Loading preview…";
      try {
         var page = await app.ServiceProvider.GetRequiredService<IApprovalServices>().PostGetMeta_ApprovalRequests(new(kind, name, Stage: ApprovalStage.Pending, WaitingForMeOnly: true, PageSize: 3, OldestFirst: true));
         if (!Current(version, user, connection) || !Tasks.Contains(row)) return;
         row.Requests.Clear();
         var viewer = app.Navigations.FirstOrDefault(x => x.Name == EmApp.ApprovalViewerNavigationName);
         foreach (var request in page.Items) row.Requests.Add(new(request, viewer is not null && app.CanOpen(viewer)));
         row.PreviewStatus = page.Total == 0 ? "No pending requests in this group. Refresh tasks." : page.Total > row.Requests.Count ? "Preview of the oldest requests. Open the group to review all." : "";
      }
      catch { if (Current(version, user, connection) && Tasks.Contains(row)) row.PreviewStatus = "Could not refresh the preview. Open the group or refresh to retry."; }
      finally { if (Current(version, user, connection) && Tasks.Contains(row)) row.IsLoading = false; }
   }
   private bool Current(int version, object? user, object? connection) => !_released && version == _version && ReferenceEquals(user, EmApp?.ActiveUser) && ReferenceEquals(connection, EmApp?.ActiveConnection);
   /// <summary>Rejects pending results and drops visible data when the shell is disposed.</summary>
   public void Release() { _released = true; _version++; Tasks.Clear(); VisibleTasks.Clear(); Commands[nameof(AllModulesCommand)]?.RaiseCanExecuteChanged(); }
   /// <summary>Only live, navigable previews may dispatch an open request.</summary>
   public bool OpenRequestCommandAllowed(object? value) => !_released && !_opening && value is HubRequestRow { CanNavigate: true } && EmApp?.ActiveUser is not null;
   /// <summary>Opens the existing viewer with the exact authorized request identity.</summary>
   public async Task OpenRequestCommand(object? value) {
      if (!OpenRequestCommandAllowed(value) || value is not HubRequestRow row || EmApp is not { } app) return;
      _opening = true; RefreshOpenCommands();
      var version = _version; var user = app.ActiveUser; var connection = app.ActiveConnection;
      try {
         var opened = await app.NavigateTo(EmApp.ApprovalViewerNavigationName, new ApprovalManagerNavigationPayload { Kind = row.Request.cApprovalRequestKind, Name = row.Request.cApprovalName, ApprovalRequestId = row.Request.cApprovalRequestId, WaitingForMeOnly = true, SystemViewer = true });
         if (Current(version, user, connection) && opened) CloseRequested?.Invoke(this, EventArgs.Empty);
         else if (Current(version, user, connection)) { StatusKind = FeedbackKind.Error; StatusText = "This request is no longer available. Refresh and try again."; }
      }
      catch { if (Current(version, user, connection)) { StatusKind = FeedbackKind.Error; StatusText = "Could not open the request. Refresh and try again."; } }
      finally { _opening = false; RefreshOpenCommands(); }
   }
   /// <summary>Returns to the Home shortlist and closes this workspace.</summary>
   public async Task HomeCommand() {
      CloseRequested?.Invoke(this, EventArgs.Empty);
      if (EmApp is not { } app) return;
      if (app.MainStack.Home?.Body is DefaultHomeControl home) home.Vm.BackToHomeCommand();
      await app.MainStack.NavigateHome();
   }
   /// <summary>Closes My Tasks and opens the complete existing module catalogue.</summary>
   public async Task AllModulesCommand() {
      if (!AllModulesCommandAllowed() || EmApp is not { } app) return;
      var user = app.ActiveUser; var connection = app.ActiveConnection;
      CloseRequested?.Invoke(this, EventArgs.Empty);
      await app.MainStack.NavigateHome();
      if (ReferenceEquals(user, app.ActiveUser) && ReferenceEquals(connection, app.ActiveConnection)
          && app.MainStack.Current == app.MainStack.Home && app.MainStack.Home?.Body is DefaultHomeControl home) home.Vm.ViewAllCommand();
   }
   public bool AllModulesCommandAllowed() => !_released && EmApp?.ActiveUser is not null && EmApp.MainStack.Home?.Body is DefaultHomeControl;
   public Task RefreshCommand() => EmApp?.RefreshHubTasksAsync() ?? Task.CompletedTask;
   public bool RefreshCommandAllowed() => !IsLoading && EmApp?.ActiveUser is not null;
   public bool OpenCommandAllowed(object? value) => !_released && !_opening && value is HubTaskRow { CanNavigate: true } && EmApp?.ActiveUser is not null;
   public async Task OpenCommand(object? value) {
      if (!OpenCommandAllowed(value) || EmApp is not { } app || value is not HubTaskRow row) return;
      _opening = true; RefreshOpenCommands();
      var user = app.ActiveUser; var connection = app.ActiveConnection;
      try {
         var opened = await app.OpenHubTaskAsync(row.Info);
         if (!ReferenceEquals(user, app.ActiveUser) || !ReferenceEquals(connection, app.ActiveConnection)) return;
         if (opened) CloseRequested?.Invoke(this, EventArgs.Empty);
         else { StatusKind = FeedbackKind.Error; StatusText = "This task is no longer available or access has changed. Refresh and try again."; }
      }
      catch {
         if (ReferenceEquals(user, app.ActiveUser) && ReferenceEquals(connection, app.ActiveConnection)) {
            StatusKind = FeedbackKind.Error; StatusText = "Could not open the task. Refresh and try again.";
         }
      }
      finally { _opening = false; RefreshOpenCommands(); }
   }
   private void RefreshOpenCommands() { NotifyChanged(nameof(IsLoading)); Commands[nameof(OpenCommand)]?.RaiseCanExecuteChanged(); Commands[nameof(OpenRequestCommand)]?.RaiseCanExecuteChanged(); }
   public void CloseCommand() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
