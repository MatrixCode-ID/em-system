using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

public partial class ApprovalManager : UserControl, INavigationBody
{
   public ApprovalManager() { InitializeComponent(); Vm.ColumnsChanged += RebuildColumns; Vm.InitializeColumns(); }
   public ApprovalManager(EmApp app) : this() => Vm.EmApp = app;
   public ApprovalManagerVm Vm => (ApprovalManagerVm)DataContext;
   public async Task InitializeAsync(EmApp app, ApprovalManagerNavigationPayload payload) {
      Vm.EmApp = app; Vm.MainWindow = Window.GetWindow(this); Vm.Configure(payload); await Vm.RefreshAsync();
   }
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
      Vm.Configure(args.Data as ApprovalManagerNavigationPayload ?? new()); return Task.CompletedTask;
   }
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshAsync();
   public Task OnRelease(INavigation sender) { Vm.Release(); return Task.CompletedTask; }
   // GridView.Columns is not bindable; only the column projection belongs to the view.
   private void RebuildColumns() {
      var view = new System.Windows.Controls.GridView { AllowsColumnReorder = false };
      var check = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
      check.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty, new Binding(nameof(ApprovalRowVm.IsChecked)) { Mode = BindingMode.TwoWay });
      if (!Vm.IsPanelMode)
         view.Columns.Add(new System.Windows.Controls.GridViewColumn { Width = 38, CellTemplate = new System.Windows.DataTemplate { VisualTree = check } });
      foreach (var column in Vm.Columns) {
         var header = new System.Windows.Controls.Button { Content = column.Caption,
            Command = Vm.Commands[nameof(Vm.SortCommand)], CommandParameter = column.SortKey,
            Style = (Style)FindResource("textButtonStyle"), Padding = new Thickness(4, 0, 4, 0), Height = 32 };
         header.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "themeWindowForegroundBrush");
         var cell = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
         cell.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new Binding($"Cells[{column.Index}]") { Mode = BindingMode.OneWay });
         cell.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
         view.Columns.Add(new System.Windows.Controls.GridViewColumn { Header = header, Width = column.Width,
            CellTemplate = new System.Windows.DataTemplate { VisualTree = cell } });
      }
      RequestsList.View = view;
   }
}

public partial class ApprovalManagerVm : MvvmModelBase, IApprovalPanelHost
{
   private ApprovalManagerNavigationPayload _payload = new();
   private string? _directRequestId;
   private int _generation;
   private bool _suspended, _released, _reloadPending;
   private PdfViewer? _pdf;
   private ApprovalDecision? _conflictingDecision;
   private readonly Dictionary<string, ApprovalGuardResult> _guards = new(StringComparer.OrdinalIgnoreCase);
   private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();
   private ApprovalPanelRegistry Panels => EmApp!.ServiceProvider.GetRequiredService<ApprovalPanelRegistry>();
   public ApprovalManagerVm() {
      Steps.CollectionChanged += (_, _) => { NotifyChanged(nameof(StepNames)); NotifyChanged(nameof(ShowStepPicker)); };
      Rows.CollectionChanged += (_, _) => { NotifyChanged(nameof(ShowRequestPicker)); NotifyChanged(nameof(ShowEmptyState)); NotifyChanged(nameof(ShowRequestDetail)); };
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(ApplyFiltersCommand), ApplyFiltersCommand, ApplyFiltersCommandAllowed);
      RegisterCommand(nameof(ClearFiltersCommand), ClearFiltersCommand, ClearFiltersCommandAllowed);
      RegisterCommand<string>(nameof(RemoveFilterCommand), RemoveFilterCommand, RemoveFilterCommandAllowed);
      RegisterCommand<string>(nameof(SortCommand), SortCommand, SortCommandAllowed);
      RegisterCommand(nameof(FirstPageCommand), FirstPageCommand, FirstPageCommandAllowed);
      RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
      RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
      RegisterCommand(nameof(LastPageCommand), LastPageCommand, LastPageCommandAllowed);
      RegisterCommand(nameof(ApproveCommand), ApproveCommand, ApproveCommandAllowed);
      RegisterCommand(nameof(RejectCommand), RejectCommand, RejectCommandAllowed);
      RegisterCommand(nameof(GuardOverrideCommand), GuardOverrideCommand, GuardOverrideCommandAllowed);
      RegisterCommand(nameof(ConflictOverrideCommand), ConflictOverrideCommand, ConflictOverrideCommandAllowed);
      RegisterCommand(nameof(ConflictRejectCommand), ConflictRejectCommand, ConflictRejectCommandAllowed);
      RegisterCommand(nameof(ApproveSelectedCommand), ApproveSelectedCommand, ApproveSelectedCommandAllowed);
      RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
      RegisterCommand(nameof(CommentCommand), CommentCommand, CommentCommandAllowed);
      RegisterCommand(nameof(OpenDocumentCommand), OpenDocumentCommand, OpenDocumentCommandAllowed);
      RegisterCommand(nameof(CloseDocumentCommand), CloseDocumentCommand, CloseDocumentCommandAllowed);
      RegisterCommand(nameof(OpenTabCommand), OpenTabCommand, OpenTabCommandAllowed);
      RegisterCommand(nameof(OpenOriginalCommand), OpenOriginalCommand, OpenOriginalCommandAllowed);
      RegisterCommand(nameof(PreviousRequestCommand), PreviousRequestCommand, PreviousRequestCommandAllowed);
      RegisterCommand(nameof(NextRequestCommand), NextRequestCommand, NextRequestCommandAllowed);
   }
   public static bool CanOpenManager(EmApp app) => app.ServiceProvider.GetRequiredService<ApprovalAccessCatalog>().CanOpen;
   public event Action? ColumnsChanged;
   public ObservableCollection<ApprovalRowVm> Rows { get; } = [];
   public List<ApprovalColumn> Columns { get; } = [];
   public ObservableCollection<string> DocTypes { get; } = [""];
   public string[] Modes { get; } = ["Needs my action", "All visible", "As substitute"];
   public ApprovalStage?[] Stages { get; } = [null, .. Enum.GetValues<ApprovalStage>()];
   public int[] PageSizes { get; } = [25, 50, 100, 200];
   public ObservableCollection<ApprovalFilterChip> FilterChips { get; } = [];
   public ObservableCollection<ApprovalStepInfo> Steps { get; } = [];
   public string[] StepNames => Steps.Select(s => s.StepName).ToArray();
   public string? SelectedStepName { get => SelectedStep?.StepName; set => SelectedStep = Steps.FirstOrDefault(s => s.StepName == value); }
   public ObservableCollection<ApprovalConflictField> Changes { get; } = [];
   public ObservableCollection<ApprovalConflictField> Conflicts { get; } = [];
   public ObservableCollection<ApprovalTimelineVm> Timeline { get; } = [];
   public ObservableCollection<ApprovalPanelCard> InfoCards { get; } = [];
   public ObservableCollection<ApprovalResultVm> Results { get; } = [];
   public bool IsWorking { get => Get(false); set => Set(value, _ => RaiseState()); }
   public bool IsReading { get => Get(false); set => Set(value, _ => RaiseState()); }
   public bool IsReady => !IsWorking && !IsReading;
   public string ErrorMessage { get => Get(""); set => Set(value, _ => NotifyChanged(nameof(HasError))); }
   public bool HasError => ErrorMessage.Length > 0;
   public bool IsPanelMode => !string.IsNullOrWhiteSpace(_payload.DocKey);
   // Compact mode: the screen is embedded in a narrow place (a flyout), so it is one column of
   // proposed changes and the decision instead of the list beside a fixed 380px detail.
   public bool IsCompact { get => Get(false); set => Set(value, _ => RaiseLayout()); }
   public bool ShowLeftColumn => !IsCompact;
   public bool ShowRequestLinks => !IsCompact;
   public bool ShowInfoCards => !IsCompact;
   public bool ShowRequestPicker => IsCompact && Rows.Count > 1;
   public bool ShowStepPicker => !IsCompact || Steps.Count > 1;
   public GridLength ListColumnWidth => IsCompact ? new(0) : new(1, GridUnitType.Star);
   public GridLength GapColumnWidth => IsCompact ? new(0) : new(12);
   public GridLength DetailColumnWidth => IsCompact ? new(1, GridUnitType.Star) : new(380);
   public bool TimelineOpen { get => Get(true); set => Set(value); }
   // Compact mode with no request at all: say so, instead of an empty request card.
   public bool ShowEmptyState => IsCompact && Rows.Count == 0 && IsReady;
   public bool ShowRequestDetail => !IsCompact || Rows.Count > 0;
   public Thickness RootMargin => IsCompact ? new(16, 6, 16, 16) : new(20);
   public bool HasGuardReason => GuardReason.Length > 0;
   private void RaiseLayout() {
      foreach (var name in new[] { nameof(IsCompact), nameof(ShowLeftColumn), nameof(ShowRequestLinks), nameof(ShowInfoCards), nameof(ShowRequestPicker),
         nameof(ShowStepPicker), nameof(ListColumnWidth), nameof(GapColumnWidth), nameof(DetailColumnWidth), nameof(RootMargin), nameof(ShowEmptyState), nameof(ShowRequestDetail) }) NotifyChanged(name);
   }
   public bool ShowToolbar => !IsPanelMode;
   public bool ShowPager => !IsPanelMode && !IsDocumentMode;
   public bool ShowList => !IsDocumentMode;
   public GridLength ListRowHeight => IsDocumentMode ? new(0) : new(1, GridUnitType.Star);
   public GridLength PdfRowHeight => IsDocumentMode ? new(1, GridUnitType.Star) : GridLength.Auto;
   public bool IsDocumentMode { get => Get(false); set => Set(value, _ => { NotifyChanged(nameof(ShowList)); NotifyChanged(nameof(ShowPager)); NotifyChanged(nameof(ListRowHeight)); NotifyChanged(nameof(PdfRowHeight)); RaiseState(); }); }
   public bool FilterSheetOpen { get => Get(false); set => Set(value); }
   public string SearchText { get => Get(""); set => Set(value); }
   public int Mode { get => Get(0); set => Set(value, _ => FilterChanged()); }
   public string DocType { get => Get(""); set => Set(value, _ => FilterChanged()); }
   public ApprovalStage? Stage { get => Get<ApprovalStage?>(); set => Set(value); }
   public int PageSize { get => Get(50); set => Set(value, _ => FilterChanged()); }
   public int Page { get => Get(1); set => Set(value, _ => NotifyChanged(nameof(PageCaption))); }
   public int TotalCount { get => Get(0); set => Set(value, _ => NotifyChanged(nameof(PageCaption))); }
   public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
   public string PageCaption => $"Page {Page:N0} of {TotalPages:N0} · {TotalCount:N0} requests";
   public string? SortBy { get; private set; }
   public bool SortDescending { get; private set; }
   public string CommentText { get => Get(""); set => Set(value, _ => RaiseState()); }
   public string DecisionNote { get => Get(""); set => Set(value); }
   public string GuardReason { get => Get(""); set => Set(value, _ => NotifyChanged(nameof(HasGuardReason))); }
   public bool ShowGuardOverride => Guard is { Allowed: false, CanBeOverridden: true, CallerCanOverride: true };
   public bool HasConflicts => Conflicts.Count > 0;
   public bool ShowConflictOverride => HasConflicts && !ConflictIsFinal;
   public bool ConflictIsFinal { get => Get(false); set => Set(value, _ => RaiseState()); }
   public object? PdfContent { get => Get<object?>(); set => Set(value); }
   public object? StepPanel { get => Get<object?>(); set => Set(value); }
   public string? InputPayload { get => Get<string?>(); set => Set(value); }
   public bool IsInputValid { get => Get(true); set => Set(value, _ => RaiseState()); }
   public ApprovalRequestInfo Request => SelectedRow?.Info ?? new();
   public string? StepName => SelectedStep?.StepName;
   public ApprovalRowVm? SelectedRow { get => Get<ApprovalRowVm?>(); set => Set(value, changed => { RaiseState(); if (!_suspended) _ = LoadDetailAsync(); }); }
   public ApprovalStepInfo? SelectedStep { get => Get<ApprovalStepInfo?>(); set => Set(value, changed => { RaiseState(); if (!_suspended) _ = LoadPanelsSafelyAsync(); }); }
   private ApprovalGuardResult? Guard => StepName is { } name ? _guards.GetValueOrDefault(name) : null;
   public bool HasSelection => SelectedRow != null;
   public bool HasPdf => Request.HasPdf;
   public bool ShowDocumentSurface => HasPdf || IsDocumentMode;
   public bool ShowChangesInDocument => IsDocumentMode && !HasPdf;
   public void Configure(ApprovalManagerNavigationPayload payload) {
      _payload = payload; _suspended = true;
      _directRequestId = payload.ApprovalRequestId;
      Mode = payload.CanSignAsSubstituteOnly ? 2 : payload.WaitingForMeOnly ? 0 : 1;
      DocType = payload.DocType ?? ""; Page = Math.Max(1, payload.Page); PageSize = Math.Clamp(payload.PageSize, 1, 200);
      SearchText = payload.Search ?? ""; Stage = payload.Stage; SortBy = payload.SortBy; SortDescending = payload.SortDescending;
      IsDocumentMode = payload.ApprovalRequestId != null; _suspended = false;
      IsCompact = payload.Compact; TimelineOpen = !payload.Compact;
      NotifyChanged(nameof(IsPanelMode)); NotifyChanged(nameof(ShowToolbar)); NotifyChanged(nameof(ShowPager));
   }
   private void RaiseState() {
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
      NotifyChanged(nameof(HasSelection)); NotifyChanged(nameof(HasPdf)); NotifyChanged(nameof(ShowGuardOverride));
      NotifyChanged(nameof(IsReady)); NotifyChanged(nameof(ShowEmptyState)); NotifyChanged(nameof(Request));
      NotifyChanged(nameof(ShowDocumentSurface)); NotifyChanged(nameof(ShowChangesInDocument));
      NotifyChanged(nameof(SelectedStepName));
      NotifyChanged(nameof(ShowConflictOverride));
   }
   private void FilterChanged() { if (!_suspended) { Page = 1; _ = RefreshAsync(); } }
   public Task RefreshCommand() => RefreshAsync();
   public bool RefreshCommandAllowed() => !IsWorking;
   public async Task RefreshAsync() {
      if (EmApp == null || _released) return;
      if (IsWorking) { _reloadPending = true; return; }
      IsWorking = true; ErrorMessage = "";
      RebuildChips();
      var selectedId = _directRequestId ?? SelectedRow?.Info.cApprovalRequestId;
      try {
         var types = await Service.GetMeta_ApprovalDocumentTypes();
         DocTypes.Clear(); DocTypes.Add("");
         foreach (var type in types) DocTypes.Add(type);
         ApprovalRequestInfo[] items;
         if (IsPanelMode) {
            if (string.IsNullOrWhiteSpace(_payload.DocType)) throw new InvalidOperationException("A document type is required for an approval status panel.");
            items = await Service.GetMeta_ApprovalRequestsByDoc(_payload.DocType, _payload.DocKey!);
            if (_payload.DocVersion != null) items = [.. items.Where(r => r.DocVersion == _payload.DocVersion)];
            TotalCount = items.Length;
         }
         else {
            var page = await Service.GetMeta_ApprovalRequests(new ApprovalQuery {
               WaitingForMeOnly = Mode == 0, CanSignAsSubstituteOnly = Mode == 2,
               DocType = string.IsNullOrWhiteSpace(DocType) ? null : DocType,
               Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
               DocVersion = _payload.DocVersion, Stages = Stage is { } s ? [s] : [],
               Page = Page, PageSize = PageSize, SortBy = SortBy, SortDescending = SortDescending });
            items = page.Items; TotalCount = page.TotalCount; Page = Math.Max(1, page.Page);
         }
         _suspended = true; Rows.Clear();
         foreach (var info in items) {
            var row = new ApprovalRowVm(info); row.PropertyChanged += (_, _) => RaiseState(); Rows.Add(row);
            if (!DocTypes.Contains(info.DocType)) DocTypes.Add(info.DocType);
         }
         if (!DocTypes.Contains(DocType)) DocTypes.Add(DocType);
         BuildColumns();
         SelectedRow = Rows.FirstOrDefault(r => r.Info.cApprovalRequestId == selectedId) ?? Rows.FirstOrDefault();
         _suspended = false; RebuildChips();
      }
      catch (Exception ex) { ShowError("Unable to load approval requests", ex); }
      finally { _suspended = false; IsWorking = false; }
      if (_directRequestId is { } directId && !Rows.Any(r => r.Info.cApprovalRequestId == directId)) {
         try {
            var direct = await Service.GetMeta_ApprovalRequest(directId);
            if (direct == null) throw new InvalidOperationException("This approval request is no longer available.");
            _suspended = true; SelectedRow = new ApprovalRowVm(direct.Info); _suspended = false;
         }
         catch (Exception ex) { ShowError("Unable to open the requested approval", ex); }
         finally { _suspended = false; }
      }
      _directRequestId = null;
      await LoadDetailAsync();
      if (_reloadPending) { _reloadPending = false; await RefreshAsync(); }
   }
   public void InitializeColumns() => BuildColumns();
   private void BuildColumns() {
      Columns.Clear();
      var standard = new (string Caption, string? Sort, double Width)[] {
         ("Document", "DocType", 130), ("Number", "DocKeyDisplay", 165), ("Version", "DocVersion", 70),
         ("Reinstate", null, 75), ("Requested", "RequestDate", 145), ("Progress", null, 75),
         ("Waiting steps", null, 180), ("Requester", "RequesterName", 150), ("Stage", "Stage", 100),
         ("Last action", null, 170), ("Last actor", null, 140), ("Action date", null, 145) };
      foreach (var (caption, sort, width) in standard) Columns.Add(new(caption, sort, Columns.Count, width));
      var names = Rows.SelectMany(r => r.Summary.Keys).Distinct(StringComparer.Ordinal).ToArray();
      foreach (var name in names) Columns.Add(new(name, name, Columns.Count, 150));
      foreach (var row in Rows) row.Project(names);
      ColumnsChanged?.Invoke();
   }
   private async Task LoadDetailAsync() {
      var generation = ++_generation; var row = SelectedRow; IsReading = true;
      _suspended = true;
      SelectedStep = null; Steps.Clear(); Changes.Clear(); Conflicts.Clear(); Timeline.Clear(); InfoCards.Clear();
      _guards.Clear(); StepPanel = null; InputPayload = null; IsInputValid = false;
      GuardReason = ""; _conflictingDecision = null; NotifyChanged(nameof(HasConflicts));
      _pdf?.Vm.Release(); _pdf = null; PdfContent = null; _suspended = false; RaiseState();
      if (row == null || EmApp == null || _released) { IsReading = false; return; }
      try {
         var detail = await Service.GetMeta_ApprovalRequest(row.Info.cApprovalRequestId);
         if (generation != _generation || _released) return;
         if (detail == null) throw new InvalidOperationException("This approval request is no longer available.");
         row.Info = detail.Info;
         row.Project(Columns.Skip(12).Select(c => c.Caption).ToArray());
         foreach (var step in detail.Steps) Steps.Add(step);
         foreach (var field in detail.Changes) Changes.Add(field);
         foreach (var entry in detail.Timeline) Timeline.Add(new(entry));
         foreach (var step in detail.Steps.Where(s => s.Status == ApprovalStepStatus.Waiting && s.Level == detail.Info.Level)) {
            var guard = await Service.GetMeta_ApprovalGuard(row.Info.cApprovalRequestId, step.StepName);
            if (generation != _generation || _released) return;
            _guards[step.StepName] = guard;
         }
         _suspended = true;
         SelectedStep = Steps.FirstOrDefault(s => s.WaitingForMe || s.CanSignAsSubstitute) ??
            Steps.FirstOrDefault(s => _guards.GetValueOrDefault(s.StepName)?.CallerCanOverride == true) ?? Steps.FirstOrDefault();
         _suspended = false; await LoadPanelsSafelyAsync();
         if (generation != _generation || _released) return;
         if (row.Info.HasPdf) {
            _pdf = new PdfViewer(EmApp); _pdf.Vm.AllowSaveAs = false; _pdf.Vm.ShowErrorDetails = false;
            _pdf.Vm.Payload = new PdfViewerNavigationPayload($"Approval {row.Info.DocKeyDisplay}", _ => Service.GetMeta_ApprovalRequestPdf(row.Info.cApprovalRequestId));
            PdfContent = _pdf; await _pdf.Vm.LoadAsync();
         }
      }
      catch (Exception ex) { if (generation == _generation) ShowError("Unable to load approval details", ex); }
      finally { _suspended = false; if (generation == _generation) IsReading = false; RaiseState(); }
   }
   private async Task LoadPanelsSafelyAsync() {
      var generation = _generation; var step = SelectedStep;
      if (SelectedRow == null || EmApp == null) return;
      var ownsReading = !IsReading;
      if (ownsReading) IsReading = true;
      try {
         GuardReason = Guard?.Allowed == false ? Guard.Reason ?? "Approval is currently blocked." : "";
         InputPayload = null; IsInputValid = step?.RequiresInput != true; StepPanel = null; InfoCards.Clear();
         if (step != null && Panels.FindStepPanel(Request.DocType, step.StepName) is { } registration) {
            var view = (FrameworkElement)ActivatorUtilities.CreateInstance(EmApp.ServiceProvider, registration.ViewType);
            var vm = (IApprovalPanel)ActivatorUtilities.CreateInstance(EmApp.ServiceProvider, registration.ViewModelType);
            if (vm is MvvmModelBase model) { model.EmApp = EmApp; model.MainWindow = DialogOwner; model.NavigationEntry = NavigationEntry; }
            vm.Host = this; view.DataContext = vm; StepPanel = view; await vm.LoadAsync();
            if (generation != _generation) return;
         }
         foreach (var card in Panels.FindInfoPanels(Request.DocType, step?.StepName)) {
            if (card.Claim != null && !HoldsClaim(card.Claim)) continue;
            var view = (FrameworkElement)ActivatorUtilities.CreateInstance(EmApp.ServiceProvider, card.ViewType);
            var input = card.Input?.Invoke(this);
            var inputHost = view.DataContext as IApprovalPanelInput ?? view as IApprovalPanelInput;
            if (inputHost != null) inputHost.Input = input;
            else if (input != null) view.DataContext = input;
            if (view.DataContext is MvvmModelBase model) { model.EmApp = EmApp; model.MainWindow = DialogOwner; model.NavigationEntry = NavigationEntry; }
            var panel = view.DataContext as IApprovalPanel ?? view as IApprovalPanel;
            if (panel != null) { panel.Host = this; await panel.LoadAsync(); }
            if (generation != _generation) return;
            InfoCards.Add(new(view.Tag as string ?? "Document information", view));
         }
      }
      catch (Exception ex) { IsInputValid = false; ShowError("Unable to load the approval panel", ex); }
      finally { if (ownsReading && generation == _generation) IsReading = false; RaiseState(); }
   }
   private bool HoldsClaim(ClaimAction claim) => EmApp?.IsDebugMode == true || EmApp?.ActiveUser?.cUserIsAdmin == true ||
      EmApp?.ActiveUser?.AvailableClaims.Any(c => c.Key.Equals(claim.Key, StringComparison.OrdinalIgnoreCase)) == true;
   private void ShowError(string context, Exception ex) => ErrorMessage = $"{context}. {FriendlyError(ex)}";
   private static string FriendlyError(Exception ex) {
      if (ex is NotImplementedException || ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase))
         return "This action is not available on the server yet. Please try again after the server has been updated.";
      return ex.Message.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "The action could not be completed.";
   }
   private string? AskReason(string title) {
      var dialog = new TextInputDialog(title, "A reason is required.", "Reason", "Confirm") { Owner = DialogOwner };
      return dialog.ShowDialog() == true ? dialog.Vm.Result : null;
   }
   public Task ApplyFiltersCommand() { FilterSheetOpen = false; Page = 1; return RefreshAsync(); }
   public bool ApplyFiltersCommandAllowed() => !IsWorking;
   public Task ClearFiltersCommand() {
      _suspended = true; SearchText = ""; Stage = null; DocType = ""; Mode = 1; Page = 1; _suspended = false; return RefreshAsync();
   }
   public bool ClearFiltersCommandAllowed() => !IsWorking;
   public Task RemoveFilterCommand(string key) {
      _suspended = true;
      if (key == "search") SearchText = ""; if (key == "stage") Stage = null;
      if (key == "type") DocType = ""; if (key == "mode") Mode = 1;
      _suspended = false; Page = 1; return RefreshAsync();
   }
   public bool RemoveFilterCommandAllowed(string key) => !IsWorking;
   private void RebuildChips() {
      FilterChips.Clear();
      if (Mode != 1) FilterChips.Add(new("mode", Modes[Mode]));
      if (!string.IsNullOrWhiteSpace(DocType)) FilterChips.Add(new("type", DocType));
      if (Stage != null) FilterChips.Add(new("stage", Stage.ToString()!));
      if (!string.IsNullOrWhiteSpace(SearchText)) FilterChips.Add(new("search", SearchText));
   }
   public Task SortCommand(string key) { SortDescending = SortBy == key && !SortDescending; SortBy = key; Page = 1; return RefreshAsync(); }
   public bool SortCommandAllowed(string key) => !IsWorking && !IsPanelMode && !string.IsNullOrWhiteSpace(key);
   public Task FirstPageCommand() { Page = 1; return RefreshAsync(); }
   public bool FirstPageCommandAllowed() => !IsWorking && Page > 1;
   public Task PreviousPageCommand() { Page--; return RefreshAsync(); }
   public bool PreviousPageCommandAllowed() => FirstPageCommandAllowed();
   public Task NextPageCommand() { Page++; return RefreshAsync(); }
   public bool NextPageCommandAllowed() => !IsWorking && Page < TotalPages;
   public Task LastPageCommand() { Page = TotalPages; return RefreshAsync(); }
   public bool LastPageCommandAllowed() => NextPageCommandAllowed();
}

public partial class ApprovalManagerVm
{
   private bool CanDecide => IsReady && Request.Stage == ApprovalStage.Pending && SelectedStep is { Status: ApprovalStepStatus.Waiting } s &&
      s.Level == Request.Level && (s.WaitingForMe || s.CanSignAsSubstitute);
   private bool OpenSatisfied => !Request.RequireOpen || IsDocumentMode;
   public bool ApproveCommandAllowed() => CanDecide && Guard?.Allowed == true && IsInputValid && OpenSatisfied;
   public bool RejectCommandAllowed() => CanDecide && OpenSatisfied;
   public bool GuardOverrideCommandAllowed() => IsReady && ShowGuardOverride && Request.Stage == ApprovalStage.Pending && IsInputValid && OpenSatisfied;
   public Task ApproveCommand() => DecideAsync(true);
   public Task RejectCommand() => DecideAsync(false);
   public Task GuardOverrideCommand() => DecideAsync(true, guardOverride: true);
   private async Task DecideAsync(bool approve, bool guardOverride = false) {
      if (SelectedStep == null) return;
      var note = DecisionNote;
      if (!approve || guardOverride || SelectedStep.CanSignAsSubstitute) {
         note = AskReason(approve ? "Approve on behalf / override" : "Reject request");
         if (string.IsNullOrWhiteSpace(note)) return;
      }
      await SendDecisionsAsync([new ApprovalDecision { cApprovalRequestId = Request.cApprovalRequestId,
         StepName = StepName!, Approve = approve, Note = note, Payload = InputPayload, GuardOverride = guardOverride }]);
   }
   private async Task SendDecisionsAsync(ApprovalDecision[] decisions) {
      IsWorking = true; ErrorMessage = ""; Results.Clear();
      var moveNext = false;
      try {
         var results = await Service.PostGetMeta_ApprovalDecide(decisions);
         foreach (var decision in decisions) {
            var result = results.FirstOrDefault(r => r.cApprovalRequestId == decision.cApprovalRequestId && r.StepName == decision.StepName);
            if (result?.Success == true && _conflictingDecision?.cApprovalRequestId == decision.cApprovalRequestId) {
               _conflictingDecision = null; Conflicts.Clear(); NotifyChanged(nameof(HasConflicts));
            }
            Results.Add(new(decision.cApprovalRequestId, decision.StepName, result?.Success == true,
               result == null ? "The server did not return a result for this decision." : result.Success ? "Decision recorded." : result.ErrorMessage ?? "The decision failed."));
            if (result is { Success: false, Conflicts.Length: > 0 }) {
               // Batch conflicts are opened individually before a conscious override can be offered.
               var row = Rows.FirstOrDefault(r => r.Info.cApprovalRequestId == result.cApprovalRequestId);
               if (row != null && _conflictingDecision == null) {
                  _suspended = true; SelectedRow = row; _suspended = false; await LoadDetailAsync();
                  _conflictingDecision = decision; ConflictIsFinal = result.ConflictIsFinal; Conflicts.Clear();
                  foreach (var field in result.Conflicts) Conflicts.Add(field);
                  NotifyChanged(nameof(HasConflicts));
               }
            }
            if (result?.Success == true && decision.cApprovalRequestId == Request.cApprovalRequestId) moveNext = IsDocumentMode;
         }
      }
      catch (Exception ex) {
         ShowError("Unable to record approval decisions", ex);
         foreach (var decision in decisions) Results.Add(new(decision.cApprovalRequestId, decision.StepName, false, FriendlyError(ex)));
      }
      finally { IsWorking = false; }
      if (moveNext && NextRequestCommandAllowed()) await MoveRequestAsync(1);
      else if (Results.Any(r => r.Success) && !HasConflicts) await RefreshAsync();
   }
   public bool ConflictOverrideCommandAllowed() => !IsWorking && _conflictingDecision != null && HasConflicts && !ConflictIsFinal && ApproveCommandAllowed();
   public async Task ConflictOverrideCommand() {
      if (_conflictingDecision == null || ConflictIsFinal) return;
      var reason = AskReason("Apply despite conflicts"); if (string.IsNullOrWhiteSpace(reason)) return;
      _conflictingDecision.Override = true; _conflictingDecision.Note = reason;
      await SendDecisionsAsync([_conflictingDecision]);
   }
   public bool ConflictRejectCommandAllowed() => !IsWorking && _conflictingDecision != null && RejectCommandAllowed();
   public Task ConflictRejectCommand() => DecideAsync(false);
   public bool ApproveSelectedCommandAllowed() => !IsWorking && Rows.Any(r => r.IsChecked && r.Info.WaitingForMe && !r.Info.RequireOpen);
   public async Task ApproveSelectedCommand() {
      var decisions = new List<ApprovalDecision>(); Results.Clear(); IsWorking = true;
      try {
         foreach (var row in Rows.Where(r => r.IsChecked)) {
            if (!row.Info.WaitingForMe || row.Info.RequireOpen) { Results.Add(new(row.Info.cApprovalRequestId, "", false, "Open this request individually before deciding.")); continue; }
            try {
               var detail = await Service.GetMeta_ApprovalRequest(row.Info.cApprovalRequestId);
               var steps = detail?.Steps.Where(s => s.WaitingForMe && s.Status == ApprovalStepStatus.Waiting && s.Level == detail.Info.Level).ToArray() ?? [];
               if (detail?.Info.RequireOpen == true || steps.Length == 0 || steps.Any(s => s.RequiresInput || Panels.FindStepPanel(row.Info.DocType, s.StepName) != null)) {
                  Results.Add(new(row.Info.cApprovalRequestId, "", false, "This request requires an individual decision or input.")); continue;
               }
               foreach (var step in steps) {
                  var guard = await Service.GetMeta_ApprovalGuard(row.Info.cApprovalRequestId, step.StepName);
                  if (!guard.Allowed) { Results.Add(new(row.Info.cApprovalRequestId, step.StepName, false, guard.Reason ?? "Approval is blocked.")); continue; }
                  decisions.Add(new() { cApprovalRequestId = row.Info.cApprovalRequestId, StepName = step.StepName, Approve = true, Note = DecisionNote });
               }
            }
            catch (Exception ex) { Results.Add(new(row.Info.cApprovalRequestId, "", false, FriendlyError(ex))); }
         }
      }
      finally { IsWorking = false; }
      var skipped = Results.ToArray();
      if (decisions.Count > 0) await SendDecisionsAsync([.. decisions]);
      if (decisions.Count > 0) foreach (var result in skipped) Results.Add(result);
   }
   public bool CancelCommandAllowed() => IsReady && HasSelection && Request.Stage is ApprovalStage.Pending or ApprovalStage.Approved;
   public async Task CancelCommand() {
      var reason = AskReason("Withdraw approval request"); if (string.IsNullOrWhiteSpace(reason)) return;
      await RunActionAsync("Unable to withdraw the request", () => Service.PostMeta_ApprovalCancel(Request.cApprovalRequestId, reason));
   }
   public bool CommentCommandAllowed() => IsReady && HasSelection && !string.IsNullOrWhiteSpace(CommentText);
   public async Task CommentCommand() {
      var note = CommentText.Trim();
      if (await RunActionAsync("Unable to add the comment", () => Service.PostMeta_ApprovalComment(Request.cApprovalRequestId, note))) CommentText = "";
   }
   private async Task<bool> RunActionAsync(string context, Func<Task> action) {
      IsWorking = true; ErrorMessage = "";
      try { await action(); }
      catch (Exception ex) { ShowError(context, ex); return false; }
      finally { IsWorking = false; }
      await RefreshAsync(); return true;
   }
   public bool OpenDocumentCommandAllowed() => !IsWorking && HasSelection;
   public void OpenDocumentCommand() { IsDocumentMode = true; }
   public bool CloseDocumentCommandAllowed() => !IsWorking && IsDocumentMode;
   public void CloseDocumentCommand() => IsDocumentMode = false;
   public bool OpenTabCommandAllowed() => !IsWorking && HasSelection;
   public async Task OpenTabCommand() {
      var payload = new ApprovalDocumentPayload { DocType = string.IsNullOrWhiteSpace(DocType) ? null : DocType, DocKey = _payload.DocKey,
         DocVersion = _payload.DocVersion, WaitingForMeOnly = Mode == 0, CanSignAsSubstituteOnly = Mode == 2,
         Search = SearchText, Stage = Stage, SortBy = SortBy, SortDescending = SortDescending, Page = Page, PageSize = PageSize,
         ApprovalRequestId = Request.cApprovalRequestId };
      try {
         if (NavigationEntry != null) await NavigationEntry.NavigateTo(ApprovalManagerNavigationPayload.NavigationName, payload);
         else await EmApp!.NavigateTo(ApprovalManagerNavigationPayload.NavigationName, payload);
      }
      catch (Exception ex) { ShowError("Unable to open the approval tab", ex); }
   }
   // The opener's target screen has its own claim; a reader who cannot open it gets a disabled button, not an error.
   public bool OpenOriginalCommandAllowed() => !IsWorking && HasSelection && EmApp != null &&
      Panels.FindDocumentOpener(Request.DocType) is { } opener &&
      EmApp.Navigations.FirstOrDefault(n => n.Name == opener.NavigationName) is { } target && EmApp.CanOpen(target);
   public async Task OpenOriginalCommand() {
      try {
         if (Panels.FindDocumentOpener(Request.DocType) is not { } opener) return;
         if (NavigationEntry != null) await NavigationEntry.NavigateTo(opener.NavigationName, opener.Parameter?.Invoke(this));
         else await EmApp!.NavigateTo(opener.NavigationName, opener.Parameter?.Invoke(this));
      }
      catch (Exception ex) { ShowError("Unable to open the source document", ex); }
   }
   public bool PreviousRequestCommandAllowed() => !IsWorking && HasSelection && (Rows.IndexOf(SelectedRow!) > 0 || (!IsPanelMode && Page > 1));
   public bool NextRequestCommandAllowed() => !IsWorking && HasSelection && (Rows.IndexOf(SelectedRow!) < Rows.Count - 1 || (!IsPanelMode && Page < TotalPages));
   public Task PreviousRequestCommand() => MoveRequestAsync(-1);
   public Task NextRequestCommand() => MoveRequestAsync(1);
   private async Task MoveRequestAsync(int delta) {
      var index = Rows.IndexOf(SelectedRow!) + delta;
      if (index >= 0 && index < Rows.Count) {
         _suspended = true; SelectedRow = Rows[index]; _suspended = false; await LoadDetailAsync(); return;
      }
      if (IsPanelMode || Page + delta < 1 || Page + delta > TotalPages) return;
      Page += delta; await RefreshAsync();
      if (delta < 0 && Rows.Count > 0) { _suspended = true; SelectedRow = Rows.Last(); _suspended = false; await LoadDetailAsync(); }
   }
   public void Release() { _released = true; _generation++; _pdf?.Vm.Release(); }
}

public record ApprovalColumn(string Caption, string? SortKey, int Index, double Width);
public record ApprovalFilterChip(string Key, string Caption);
public record ApprovalPanelCard(string Title, object Content);
public record ApprovalResultVm(string RequestId, string StepName, bool Success, string Message);
public class ApprovalDocumentPayload : ApprovalManagerNavigationPayload { public override string? Title => $"Approval - {ApprovalRequestId}"; }
public class ApprovalRowVm : NotifyPropertyBase
{
   public ApprovalRowVm(ApprovalRequestInfo info) {
      Info = info;
      try {
         if (info.Summary != null) {
            using var json = JsonDocument.Parse(info.Summary);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
               foreach (var field in json.RootElement.EnumerateObject()) Summary[field.Name] = field.Value.ValueKind == JsonValueKind.Null ? "" : field.Value.ToString();
         }
      }
      catch (JsonException) { }
   }
   public ApprovalRequestInfo Info { get; set; }
   public bool IsChecked { get => Get(false); set => Set(value); }
   public Dictionary<string, string> Summary { get; } = new(StringComparer.Ordinal);
   public string[] Cells { get; private set; } = [];
   public void Project(string[] names) {
      Cells = [Info.DocType, Info.DocKeyDisplay, Info.DocVersion, Info.ReinstateCount.ToString(),
         Info.RequestDate.ToString("dd MMM yyyy HH:mm"), $"{Info.SignedStepCount}/{Info.TotalStepCount}",
         Info.WaitingSteps, Info.RequesterName, Info.Stage.ToString(), ApprovalTimelineVm.Caption(Info.LastAction),
         Info.LastActorName, Info.LastActionDate.ToString("dd MMM yyyy HH:mm"), .. names.Select(n => Summary.GetValueOrDefault(n, ""))];
      NotifyChanged(nameof(Cells));
   }
}
public record ApprovalTimelineVm(ApprovalTimelineEntry Entry)
{
   public string Label => Caption(Entry.Action);
   public string Icon => Entry.Action switch {
      ApprovalTimelineAction.Submitted => "↑", ApprovalTimelineAction.Approved => "✓",
      ApprovalTimelineAction.ApprovedAsSubstitute => "⇄", ApprovalTimelineAction.ApprovedWithOverride => "!",
      ApprovalTimelineAction.Rejected => "×", ApprovalTimelineAction.Cancelled => "↶",
      ApprovalTimelineAction.ReinstatedAfterFinish => "↶", ApprovalTimelineAction.Commented => "✎", _ => "•" };
   public static string Caption(string action) => action switch {
      ApprovalTimelineAction.Submitted => "Submitted", ApprovalTimelineAction.Approved => "Approved",
      ApprovalTimelineAction.ApprovedAsSubstitute => "Approved on behalf", ApprovalTimelineAction.ApprovedWithOverride => "Approved with override",
      ApprovalTimelineAction.Rejected => "Rejected", ApprovalTimelineAction.Cancelled => "Withdrawn",
      ApprovalTimelineAction.ReinstatedAfterFinish => "Withdrawn after completion", ApprovalTimelineAction.Commented => "Comment", _ => action };
}
