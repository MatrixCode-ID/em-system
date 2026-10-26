using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Shared;

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
public sealed record HubTaskRow(HubTaskInfo Info)
{
   public string Caption => $"{Info.Title} · {Info.Count.ToString(CultureInfo.GetCultureInfo("en-US"))} waiting";
   public string Detail => Info.Description ?? Info.Source;
}

/// <summary>Presentation only: the existing wrapper catalog owns task routes and permissions.</summary>
public sealed class MyTasksPanelVm : MvvmModelBase
{
   public event EventHandler? CloseRequested;
   public ObservableCollection<HubTaskRow> Tasks { get; } = [];
   private bool _opening;
   public MyTasksPanelVm() {
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(OpenCommand), OpenCommand, OpenCommandAllowed);
      RegisterCommand(nameof(CloseCommand), CloseCommand);
   }
   public string CountAndTime => EmApp?.HubTaskRefreshedAt is { } time
      ? $"{EmApp.HubTaskCount.ToString(CultureInfo.GetCultureInfo("en-US"))} waiting · updated {time.ToString("dd-MMM-yyyy HH:mm:ss zzz", CultureInfo.GetCultureInfo("en-US"))}"
      : "Task count not loaded yet";
   public bool IsLoading => EmApp?.HubTaskIsLoading == true;
   public string StatusText { get => Get<string>() ?? ""; private set => Set(value); }
   public FeedbackKind StatusKind { get => Get<FeedbackKind>(); private set => Set(value); }
   public void Update() {
      Tasks.Clear();
      if (EmApp is { } app) foreach (var task in app.HubTasks.Where(x => x.Count > 0)) Tasks.Add(new(task));
      StatusKind = EmApp?.HubTaskLoadFailed == true ? FeedbackKind.Stale : FeedbackKind.Info;
      StatusText = EmApp?.HubTaskLoadFailed == true
         ? (Tasks.Count > 0 ? "Refresh failed. Showing the last successful snapshot. Refresh to retry." : "Could not load tasks. Refresh to retry.")
         : IsLoading ? "Loading your tasks…" : EmApp?.HubTaskRefreshedAt is null ? "Tasks have not been loaded." : Tasks.Count == 0 ? "No tasks waiting for you." : "";
      NotifyChanged(nameof(CountAndTime)); NotifyChanged(nameof(IsLoading));
      Commands[nameof(RefreshCommand)]?.RaiseCanExecuteChanged();
   }
   public Task RefreshCommand() => EmApp?.RefreshHubTasksAsync() ?? Task.CompletedTask;
   public bool RefreshCommandAllowed() => !IsLoading && EmApp?.ActiveUser is not null;
   public bool OpenCommandAllowed(object? value) => !_opening && value is HubTaskRow && EmApp?.ActiveUser is not null;
   public async Task OpenCommand(object? value) {
      if (!OpenCommandAllowed(value) || EmApp is not { } app || value is not HubTaskRow row) return;
      _opening = true;
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
      finally { _opening = false; Commands[nameof(OpenCommand)]?.RaiseCanExecuteChanged(); }
   }
   public void CloseCommand() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
