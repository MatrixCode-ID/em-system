using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Enable field.</summary>
/// <param name="Value">The value.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record SchedEnableOption(TenantSchedEnable Value, string Caption);

/// <summary>A choice of the Default schedule field.</summary>
/// <param name="Value">The mode.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record SchedModeOption(TenantSchedMode Value, string Caption);

/// <summary>A choice of a template field.</summary>
/// <param name="Id">Template ID.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record SchedTemplateOption(string Id, string Caption);

/// <summary>One level in the Scheduling settings tree.</summary>
public sealed class SchedLevelNode : MvvmModelBase
{
   private readonly BusinessModuleManagerSchedSettingVm _owner;

   internal SchedLevelNode(BusinessModuleManagerSchedSettingVm owner, TenantSchedSettingRow row, SchedLevelNode? parent) {
      _owner = owner; Row = row; Parent = parent;
   }

   /// <summary>The level as read.</summary>
   public TenantSchedSettingRow Row { get; }

   /// <summary>Parent node; <c>null</c> for the tenant.</summary>
   public SchedLevelNode? Parent { get; }

   /// <summary>Departments below this level.</summary>
   public ObservableCollection<SchedLevelNode> Children { get; } = [];

   /// <summary>Whether this is the tenant level.</summary>
   public bool IsTenant => Row.DeptId is null;

   /// <summary>Level shown next to the name.</summary>
   public string LevelCaption => IsTenant ? "Tenant" : Parent?.IsTenant == true ? "Dept" : "Sub-dept";

   /// <summary>Short result shown under the name.</summary>
   public string Subtitle => !Row.Effective.Enabled ? "Not scheduled" : Row.Setting is { cTenantSchedSettingMode: not TenantSchedMode.Inherit } || IsTenant
      ? "Enabled · " + ScheduleName(Row.Effective) : "Inherits " + Row.Effective.ScheduleSource;

   /// <summary>Whether the disabled chip is shown.</summary>
   public bool IsDisabled => !Row.Effective.Enabled;

   /// <summary>Whether the children are shown.</summary>
   public bool IsExpanded { get => Get<bool>(); set => Set(value); }

   /// <summary>Whether this node is selected in the tree; selecting it opens it in the editor.</summary>
   public bool IsSelected {
      get => Get<bool>();
      set { if (Set(value) && value) _owner.OnNodeSelected(this); }
   }

   /// <summary>This node and every node below it.</summary>
   public IEnumerable<SchedLevelNode> Descendants() {
      yield return this;
      foreach (var child in Children)
         foreach (var node in child.Descendants()) yield return node;
   }

   private static string ScheduleName(TenantSchedEffective effective) => effective.Mode switch {
      TenantSchedMode.Template => effective.TemplateName ?? "",
      TenantSchedMode.Rotation => "Rotation",
      _ => TenantSchedBuiltIn.Name
   };

   // Changes the selection mark without opening the node in the editor.
   internal void SelectQuietly(bool value) => Set(value, propertyName: nameof(IsSelected));
}

/// <summary>One rotation step in the editor.</summary>
public sealed class SchedStepItem : MvvmModelBase
{
   private readonly Action _changed;

   internal SchedStepItem(int order, SchedTemplateOption? template, Action changed) {
      _changed = changed;
      Order = order;
      Set(template, propertyName: nameof(Template));
   }

   /// <summary>Position in the rotation, from 1.</summary>
   public int Order { get => Get<int>(); internal set => Set(value, _ => NotifyChanged(nameof(Caption))); }

   /// <summary>Caption of the step.</summary>
   public string Caption => "Step " + Order;

   /// <summary>Template of the step.</summary>
   public SchedTemplateOption? Template { get => Get<SchedTemplateOption>(); set => Set(value, _ => _changed()); }
}

/// <summary>State and commands of the Scheduling settings page: one tenant and its departments.</summary>
public sealed class BusinessModuleManagerSchedSettingVm : MvvmModelBase
{
   private readonly ITenantSchedSettingServices? _service;
   private readonly ITenantSchedTemplateServices? _templateService;
   private vi_TenantSchedTemplate[] _templates = [];
   private bool _restoringSelection;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the services are taken from the application.</summary>
   public BusinessModuleManagerSchedSettingVm() { RegisterCommands(); }

   /// <summary>Creates a view model with explicit services, for tests and other hosts.</summary>
   public BusinessModuleManagerSchedSettingVm(ITenantSchedSettingServices service, ITenantSchedTemplateServices templates) {
      _service = service; _templateService = templates; RegisterCommands();
   }

   private ITenantSchedSettingServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantSchedSettingServices>();
   private ITenantSchedTemplateServices? TemplateService => _templateService ?? EmApp?.ServiceProvider.GetRequiredService<ITenantSchedTemplateServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(UseInheritedCommand), UseInheritedCommand, UseInheritedCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(AddStepCommand), AddStepCommand, AddStepCommandAllowed);
      RegisterCommand<SchedStepItem>(nameof(RemoveStepCommand), RemoveStepCommand, RemoveStepCommandAllowed);
   }

   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];

   /// <summary>The tenant being managed.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); RootNodes.Clear(); Open(null); IsLoaded = false;
         Loading = ReloadAsync(); UpdateState();
      }
   }

   /// <summary>Most recent load, also awaitable by the owning screen.</summary>
   public Task Loading { get; set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>The tenant, with its departments below it.</summary>
   public ObservableCollection<SchedLevelNode> RootNodes { get; } = [];

   /// <summary>Every node of the tree, depth first.</summary>
   public IEnumerable<SchedLevelNode> AllNodes => RootNodes.SelectMany(n => n.Descendants());

   /// <summary>The level open in the editor.</summary>
   public SchedLevelNode? SelectedNode { get => Get<SchedLevelNode>(); private set => Set(value); }

   /// <summary>Choices of the Enable field for this level.</summary>
   public ObservableCollection<SchedEnableOption> EnableOptions { get; } = [];

   /// <summary>Choices of the Default schedule field for this level.</summary>
   public ObservableCollection<SchedModeOption> ModeOptions { get; } = [];

   /// <summary>Active templates, plus the ones this level already uses.</summary>
   public ObservableCollection<SchedTemplateOption> TemplateOptions { get; } = [];

   /// <summary>Rotation steps of the edited level.</summary>
   public ObservableCollection<SchedStepItem> Steps { get; } = [];

   /// <summary>Enable value of the edited level.</summary>
   public SchedEnableOption? EditEnable { get => Get<SchedEnableOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Default schedule of the edited level.</summary>
   public SchedModeOption? EditMode {
      get => Get<SchedModeOption>();
      set => Set(value, _ => {
         // A rotation needs two steps at least, so it starts with two empty ones.
         if (!_opening && IsRotationMode) while (Steps.Count < 2) Steps.Add(new SchedStepItem(Steps.Count + 1, null, UpdateState));
         UpdateState();
      });
   }

   /// <summary>Template of the template mode.</summary>
   public SchedTemplateOption? EditTemplate { get => Get<SchedTemplateOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Weeks per rotation step.</summary>
   public decimal? EditStepWeeks { get => Get<decimal?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Day 1 of the rotation.</summary>
   public DateTime? EditCycleStart { get => Get<DateTime?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Length of an assignment period, tenant level only; empty means 14 days.</summary>
   public decimal? EditPeriodDays { get => Get<decimal?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether the template field is shown.</summary>
   public bool IsTemplateMode => EditMode?.Value == TenantSchedMode.Template;

   /// <summary>Whether the rotation fields are shown.</summary>
   public bool IsRotationMode => EditMode?.Value == TenantSchedMode.Rotation;

   /// <summary>Whether the tenant level is open.</summary>
   public bool IsTenantLevel => SelectedNode?.IsTenant == true;

   /// <summary>Whether a level is open in the editor.</summary>
   public bool HasSelection => SelectedNode is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedNode is null;

   /// <summary>Caption of the editor.</summary>
   public string EditorCaption => SelectedNode is { } node ? $"{node.Row.Name} · {node.LevelCaption}".ToUpperInvariant() : "SCHEDULE";

   /// <summary>What applies to this level, as last saved.</summary>
   public string EffectiveText => SelectedNode?.Row.Effective.Text ?? "";

   /// <summary>Warning that the schedule of this level refers to a deactivated template.</summary>
   public string TemplateWarning => SelectedNode?.Row.Effective.HasInactiveTemplate == true ? "This schedule uses a template that was deactivated. It stays in use until the setting changes." : "";

   /// <summary>Whether the template warning is shown.</summary>
   public bool HasTemplateWarning => TemplateWarning.Length > 0;

   /// <summary>Whether the editor differs from what is saved for the level.</summary>
   public bool IsDirty {
      get {
         if (SelectedNode is not { } node || EditEnable is null || EditMode is null) return false;
         var saved = node.Row.Setting;
         var enable = saved?.cTenantSchedSettingEnable ?? (node.IsTenant ? TenantSchedEnable.Disabled : TenantSchedEnable.Inherit);
         if (EditEnable.Value != enable || EditMode.Value != (saved?.cTenantSchedSettingMode ?? TenantSchedMode.Inherit)) return true;
         if (node.IsTenant && EditPeriodDays != saved?.cTenantSchedSettingPeriodDays) return true;
         if (IsTemplateMode) return EditTemplate?.Id != saved?.cTenantSchedTemplateId;
         if (!IsRotationMode) return false;
         return EditStepWeeks != saved?.cTenantSchedSettingStepWeeks || EditCycleStart?.Date != saved?.cTenantSchedSettingCycleStart?.Date ||
            !Steps.Select(x => x.Template?.Id).SequenceEqual(node.Row.Steps.Select(x => (string?)x.TemplateId), StringComparer.OrdinalIgnoreCase);
      }
   }

   /// <summary>Whether the toolbar and tree accept input.</summary>
   public bool CanBrowse => IsLoaded && SelectedTenant is not null && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the latest tenant load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() { if (IsDirty) Open(SelectedNode); }

   internal void OnNodeSelected(SchedLevelNode node) {
      if (_restoringSelection || ReferenceEquals(node, SelectedNode)) return;
      var previous = SelectedNode;
      if (IsBusy || !CanLeave()) {
         // Keep the edited node selected: the tree already moved, so move it back.
         _restoringSelection = true;
         try { node.SelectQuietly(false); previous?.SelectQuietly(true); }
         finally { _restoringSelection = false; }
         return;
      }
      foreach (var other in AllNodes.Where(n => !ReferenceEquals(n, node))) other.SelectQuietly(false);
      Open(node);
   }

   /// <summary>Whether the editor holds complete changes.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && (!IsTemplateMode || EditTemplate is not null) &&
      (!IsRotationMode || (Steps.Count >= 2 && Steps.All(x => x.Template is not null) && EditStepWeeks >= 1 && EditCycleStart is not null));

   /// <summary>Saves the level, then reloads the tree, because levels below may inherit from it.</summary>
   public Task SaveCommand() => RunAsync("Saving setting...", async () => {
      if (SelectedNode is not { } node || Service is not { } service) return;
      await service.PostGetMeta_TenantSchedSetting_Save(new TenantSchedSettingSave {
         TenantId = node.Row.TenantId, DeptId = node.Row.DeptId, Enable = EditEnable!.Value, Mode = EditMode!.Value,
         TemplateId = IsTemplateMode ? EditTemplate?.Id : null, StepTemplateIds = IsRotationMode ? [.. Steps.Select(x => x.Template!.Id)] : [],
         StepWeeks = IsRotationMode ? (int?)EditStepWeeks : null, CycleStart = IsRotationMode ? EditCycleStart?.Date : null,
         PeriodDays = node.IsTenant ? (int?)EditPeriodDays : null, ustamp = node.Row.Setting?.ustamp
      });
      await Load(node.Row.DeptId);
      Status = $"Setting of {node.Row.Name} saved.";
   });

   /// <summary>Whether the selected department has its own values to give up.</summary>
   public bool UseInheritedCommandAllowed() => CanEdit && !IsDirty && SelectedNode is { IsTenant: false, Row.Setting: { } setting } &&
      (setting.cTenantSchedSettingEnable != TenantSchedEnable.Inherit || setting.cTenantSchedSettingMode != TenantSchedMode.Inherit);

   /// <summary>Sets the selected department back to inherit.</summary>
   public Task UseInheritedCommand() => RunAsync("Saving setting...", async () => {
      if (SelectedNode is not { IsTenant: false, Row.Setting: { } setting } node || Service is not { } service) return;
      await service.PostGetMeta_TenantSchedSetting_UseInherited(node.Row.TenantId, node.Row.DeptId!, setting.ustamp);
      await Load(node.Row.DeptId);
      Status = $"{node.Row.Name} now inherits.";
   });

   /// <summary>Whether the tree can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the levels again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Whether a step can be added.</summary>
   public bool AddStepCommandAllowed() => CanEdit && IsRotationMode;

   /// <summary>Adds an empty step at the end of the rotation.</summary>
   public void AddStepCommand() {
      if (!AddStepCommandAllowed()) return;
      Steps.Add(new SchedStepItem(Steps.Count + 1, null, UpdateState));
      UpdateState();
   }

   /// <summary>Whether a step can be removed.</summary>
   public bool RemoveStepCommandAllowed(SchedStepItem? step) => CanEdit && step is not null;

   /// <summary>Removes a step and closes the gap in the order.</summary>
   public void RemoveStepCommand(SchedStepItem? step) {
      if (!RemoveStepCommandAllowed(step)) return;
      Steps.Remove(step!);
      for (var i = 0; i < Steps.Count; i++) Steps[i].Order = i + 1;
      UpdateState();
   }

   /// <summary>Reads the selected tenant again, keeping the selected level when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading settings...", async () => {
      await Load(SelectedNode?.Row.DeptId);
      var departments = RootNodes.SelectMany(x => x.Children).SelectMany(x => x.Descendants()).Count();
      Status = $"Loaded {departments + 1} {(departments == 0 ? "level" : "levels")} for {SelectedTenant?.cTenantName}.";
   });

   private async Task Load(string? selectDeptId) {
      if (Service is not { } service || TemplateService is not { } templates || SelectedTenant is null) return;
      var rows = await service.GetMeta_TenantSchedSettings(SelectedTenant.cTenantId);
      _templates = await templates.GetVi_TenantSchedTemplates(SelectedTenant.cTenantId);
      var expanded = AllNodes.Where(n => n.IsExpanded).Select(n => n.Row.DeptId ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
      var hadTree = RootNodes.Count > 0;
      RootNodes.Clear();
      var byId = rows.Where(x => x.DeptId is not null).ToDictionary(x => x.DeptId!, StringComparer.OrdinalIgnoreCase);
      var children = rows.Where(x => x.DeptId is not null).ToLookup(x => x.ParentDeptId is { } p && byId.ContainsKey(p) ? p.ToUpperInvariant() : "");
      void Add(TenantSchedSettingRow row, SchedLevelNode parent, HashSet<string> path) {
         // A broken hierarchy written outside this screen must not hang the screen.
         if (!path.Add(row.DeptId!)) return;
         var node = new SchedLevelNode(this, row, parent) { IsExpanded = !hadTree || expanded.Contains(row.DeptId!) };
         parent.Children.Add(node);
         foreach (var child in children[row.DeptId!.ToUpperInvariant()]) Add(child, node, path);
         path.Remove(row.DeptId!);
      }
      var tenant = new SchedLevelNode(this, rows.First(x => x.DeptId is null), null) { IsExpanded = true };
      RootNodes.Add(tenant);
      foreach (var row in children[""]) Add(row, tenant, new(StringComparer.OrdinalIgnoreCase));
      IsLoaded = true;
      var selected = AllNodes.FirstOrDefault(n => string.Equals(n.Row.DeptId, selectDeptId, StringComparison.OrdinalIgnoreCase)) ?? tenant;
      for (var up = selected.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
      selected.SelectQuietly(true);
      Open(selected);
   }

   private void Open(SchedLevelNode? node) {
      _opening = true;
      try {
         SelectedNode = node;
         var saved = node?.Row.Setting;
         EnableOptions.Clear(); ModeOptions.Clear(); TemplateOptions.Clear(); Steps.Clear();
         if (node is null) { EditEnable = null; EditMode = null; EditTemplate = null; EditStepWeeks = null; EditCycleStart = null; EditPeriodDays = null; return; }
         if (!node.IsTenant) EnableOptions.Add(new(TenantSchedEnable.Inherit, "Inherit"));
         EnableOptions.Add(new(TenantSchedEnable.Enabled, "Enabled"));
         EnableOptions.Add(new(TenantSchedEnable.Disabled, "Disabled"));
         ModeOptions.Add(new(TenantSchedMode.Inherit, node.IsTenant ? TenantSchedBuiltIn.Name : "Inherit"));
         ModeOptions.Add(new(TenantSchedMode.Template, "Template"));
         ModeOptions.Add(new(TenantSchedMode.Rotation, "Rotation"));
         var used = node.Row.Steps.Select(x => x.TemplateId).Append(saved?.cTenantSchedTemplateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
         foreach (var template in _templates.Where(x => x.cTenantSchedTemplateState == ta_TenantSchedTemplate.StateActive || used.Contains(x.cTenantSchedTemplateId)))
            TemplateOptions.Add(new(template.cTenantSchedTemplateId, template.cTenantSchedTemplateName +
               (template.cTenantSchedTemplateState == ta_TenantSchedTemplate.StateActive ? "" : " (inactive)")));
         var enable = saved?.cTenantSchedSettingEnable ?? (node.IsTenant ? TenantSchedEnable.Disabled : TenantSchedEnable.Inherit);
         EditEnable = EnableOptions.FirstOrDefault(x => x.Value == enable);
         EditMode = ModeOptions.FirstOrDefault(x => x.Value == (saved?.cTenantSchedSettingMode ?? TenantSchedMode.Inherit));
         EditTemplate = TemplateOptions.FirstOrDefault(x => string.Equals(x.Id, saved?.cTenantSchedTemplateId, StringComparison.OrdinalIgnoreCase));
         EditStepWeeks = saved?.cTenantSchedSettingStepWeeks; EditCycleStart = saved?.cTenantSchedSettingCycleStart; EditPeriodDays = saved?.cTenantSchedSettingPeriodDays;
         foreach (var step in node.Row.Steps)
            Steps.Add(new SchedStepItem(step.Order, TemplateOptions.FirstOrDefault(x => string.Equals(x.Id, step.TemplateId, StringComparison.OrdinalIgnoreCase)), UpdateState));
      }
      finally {
         _opening = false;
         UpdateState();
      }
   }

   private async Task RunAsync(string caption, Func<Task> action) {
      if (IsBusy || Service is null) return;
      try {
         IsBusy = InWaiting = true; WaiterText = caption; UpdateState();
         await action();
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) {
         Status = "The operation did not complete. Refresh and try again.";
         if (EmApp is not null) AlertError(ex);
      }
      finally { InWaiting = IsBusy = false; UpdateState(); }
   }

   private void UpdateState() {
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDirty), nameof(CanBrowse), nameof(CanEdit), nameof(IsTemplateMode), nameof(IsRotationMode),
         nameof(IsTenantLevel), nameof(EditorCaption), nameof(EffectiveText), nameof(TemplateWarning), nameof(HasTemplateWarning)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
