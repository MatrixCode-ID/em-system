using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Parent field: a department, or no parent at all.</summary>
/// <param name="Id">Department ID; <c>null</c> = no parent (a root department).</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record DepartmentOption(string? Id, string Caption);

/// <summary>One department in the tree of the Business Module Manager, or a draft that is not saved yet.</summary>
public sealed class DepartmentNode : MvvmModelBase
{
   private readonly BusinessModuleManagerDeptVm _owner;

   internal DepartmentNode(BusinessModuleManagerDeptVm owner, vi_TenantDept department, DepartmentNode? parent, bool isDraft) {
      _owner = owner; Department = department; Parent = parent; IsDraft = isDraft;
   }

   /// <summary>The department row as last read; for a draft, its starting values.</summary>
   public vi_TenantDept Department { get; }

   /// <summary>Parent node; <c>null</c> for a root department.</summary>
   public DepartmentNode? Parent { get; }

   /// <summary>Child nodes in display order.</summary>
   public ObservableCollection<DepartmentNode> Children { get; } = [];

   /// <summary>Whether this node is a new department that has not been saved.</summary>
   public bool IsDraft { get; }

   /// <summary>Name shown in the tree.</summary>
   public string DisplayName => IsDraft ? "(new department)" : Department.cTenantDeptName;

   /// <summary>Code shown next to the name.</summary>
   public string DisplayCode => IsDraft ? "" : Department.cTenantDeptCode;

   /// <summary>Active employee count shown in the tree.</summary>
   public int EmployeeCount => Department.ccTenantDeptEmpCount;

   /// <summary>Whether the department is inactive.</summary>
   public bool IsInactive => !IsDraft && Department.cTenantDeptState < ta_TenantDept.StateActive;

   /// <summary>Whether the children are shown.</summary>
   public bool IsExpanded { get => Get<bool>(); set => Set(value); }

   /// <summary>Whether this node is selected in the tree; selecting it opens it in the editor.</summary>
   public bool IsSelected {
      get => Get<bool>();
      set { if (Set(value) && value) _owner.OnNodeSelected(this); }
   }

   /// <summary>This node and every node below it.</summary>
   public IEnumerable<DepartmentNode> Descendants() {
      yield return this;
      foreach (var child in Children)
         foreach (var node in child.Descendants()) yield return node;
   }

   // Changes the selection mark without opening the node in the editor.
   internal void SelectQuietly(bool value) => Set(value, propertyName: nameof(IsSelected));
}

/// <summary>State and commands of the Business Module Manager; the first tab manages departments.</summary>
public sealed class BusinessModuleManagerDeptVm : MvvmModelBase
{
   private readonly ITenantDeptServices? _service;
   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];
   /// <summary>The tenant being managed.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); _draft = null; RootNodes.Clear(); SelectedNode = null; ClearEditor(); IsLoaded = false;
         Loading = ReloadAsync(); UpdateState();
      }
   }
   /// <summary>Most recent tenant load, also awaitable by the owning screen.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;
   /// <summary>Employee service used to populate head choices.</summary>
   public ITenantEmpServices? EmployeeService { get; set; }
   private ITenantEmpServices? EmployeesService => EmployeeService ?? EmApp?.ServiceProvider.GetRequiredService<ITenantEmpServices>();
   private vi_TenantEmp[] _employees = [];
   /// <summary>Available active employees of the selected saved department.</summary>
   public ObservableCollection<DepartmentOption> HeadOptions { get; } = [];
   /// <summary>Selected head.</summary>
   public DepartmentOption? EditHead { get => Get<DepartmentOption>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Whether a head can be selected.</summary>
   public bool CanChooseHead => CanEdit && !IsDraftSelected;
   /// <summary>Abbreviation of this department.</summary>
   public string? EditAbbreviation { get => Get<string>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() {
      if (!IsDirty) return;
      if (SelectedNode is { IsDraft: false } node) Open(node);
      else { RemoveDraft(); SelectedNode = null; ClearEditor(); }
   }
   private DepartmentNode? _draft;
   private bool _restoringSelection;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerDeptVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerDeptVm(ITenantDeptServices service) { _service = service; RegisterCommands(); }

   private ITenantDeptServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantDeptServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewDepartmentCommand), NewDepartmentCommand, NewDepartmentCommandAllowed);
      RegisterCommand(nameof(NewChildCommand), NewChildCommand, NewChildCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
   }

   #region Tree

   /// <summary>Every saved department as read from the server.</summary>
   public ObservableCollection<vi_TenantDept> Departments { get; } = [];

   /// <summary>Root nodes of the department tree, including a root draft.</summary>
   public ObservableCollection<DepartmentNode> RootNodes { get; } = [];

   /// <summary>The node open in the editor.</summary>
   public DepartmentNode? SelectedNode { get => Get<DepartmentNode>(); private set { Set(value); UpdateState(); } }

   /// <summary>Every node of the tree, depth first.</summary>
   public IEnumerable<DepartmentNode> AllNodes => RootNodes.SelectMany(n => n.Descendants());

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   internal void OnNodeSelected(DepartmentNode node) {
      if (_restoringSelection || ReferenceEquals(node, SelectedNode)) return;
      var previous = SelectedNode;
      if (IsDirty && ConfirmDiscard?.Invoke() == false) {
         // Keep the edited node selected: the tree already moved, so move it back.
         _restoringSelection = true;
         try { node.SelectQuietly(false); previous?.SelectQuietly(true); }
         finally { _restoringSelection = false; }
         return;
      }
      if (_draft is not null && !ReferenceEquals(node, _draft)) RemoveDraft();
      foreach (var other in AllNodes.Where(n => !ReferenceEquals(n, node))) other.SelectQuietly(false);
      Open(node);
   }

   #endregion

   #region Editor

   /// <summary>Code of the edited department.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited department.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Display order of the edited department among its siblings.</summary>
   public int EditOrder { get => Get<int>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited department.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Parent choices: no parent, then every department except the edited one and its descendants.</summary>
   public ObservableCollection<DepartmentOption> ParentOptions { get; } = [];

   /// <summary>Selected parent choice.</summary>
   public DepartmentOption? EditParent { get => Get<DepartmentOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a node is open in the editor.</summary>
   public bool HasSelection => SelectedNode is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedNode is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedNode?.IsDraft == true;

   /// <summary>Whether the editor differs from the saved department; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedNode is { } node && (node.IsDraft ||
      EditAbbreviation != node.Department.cTenantDeptAbv || EditHead?.Id != node.Department.cTenantDeptHead_cTenantEmpId || EditCode != node.Department.cTenantDeptCode || EditName != node.Department.cTenantDeptName || EditOrder != node.Department.cTenantDeptOrder ||
      (EditNote ?? "") != (node.Department.cTenantDeptNote ?? "") || EditParent?.Id != node.Department.cTenantDeptParent_cTenantDeptId);

   /// <summary>Caption of the state button: Deactivate for an active department, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedNode?.IsInactive == true ? "Activate" : "Deactivate";

   /// <summary>Whether the toolbar and tree accept input.</summary>
   public bool CanBrowse => SelectedTenant is not null && IsLoaded && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the first successful read.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   #endregion

   #region Commands

   /// <summary>Whether a new root department can be started.</summary>
   public bool NewDepartmentCommandAllowed() => CanBrowse;

   /// <summary>Starts a new root department as a draft node.</summary>
   public void NewDepartmentCommand() => StartDraft(null);

   /// <summary>Whether a new child of the selected saved department can be started.</summary>
   public bool NewChildCommandAllowed() => CanBrowse && SelectedNode is { IsDraft: false };

   /// <summary>Starts a new department under the selected department as a draft node.</summary>
   public void NewChildCommand() { if (SelectedNode is { IsDraft: false } parent) StartDraft(parent); }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty;

   /// <summary>Creates the draft or saves the changes of the selected department, then reloads the tree.</summary>
   public Task SaveCommand() => RunAsync("Saving department...", async () => {
      if (SelectedNode is not { } node || Service is not { } service || SelectedTenant is null) return;
      var row = new ta_TenantDept {
         cTenantDeptId = node.IsDraft ? "" : node.Department.cTenantDeptId,
         cTenantDeptParent_cTenantDeptId = EditParent?.Id,
         cTenantId = SelectedTenant!.cTenantId, cTenantDeptAbv = EditAbbreviation, cTenantDeptHead_cTenantEmpId = EditHead?.Id,
         cTenantDeptCode = EditCode,
         cTenantDeptName = EditName,
         cTenantDeptOrder = EditOrder,
         cTenantDeptNote = string.IsNullOrEmpty(EditNote) ? null : EditNote,
         cTenantDeptState = node.IsDraft ? ta_TenantDept.StateActive : node.Department.cTenantDeptState,
         json_object = node.Department.json_object,
         ustamp = node.Department.ustamp
      };
      var saved = node.IsDraft ? await service.PostGetTa_TenantDept_New(row) : await service.PostGetTa_TenantDept_Update(row);
      _draft = null;
      if (EmployeesService is { } employees) _employees = await employees.GetVi_TenantEmps(SelectedTenant!.cTenantId);
      ApplyList(await service.GetVi_TenantDepts(SelectedTenant!.cTenantId), saved.cTenantDeptId);
      Status = node.IsDraft ? $"Department '{saved.cTenantDeptCode}' created." : $"Department '{saved.cTenantDeptCode}' saved.";
   });

   /// <summary>Whether the selected saved department, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanBrowse && SelectedNode is { IsDraft: false } && !IsDirty;

   /// <summary>Deactivates an active department or activates an inactive one; its children keep their own state.</summary>
   public Task ToggleStateCommand() => RunAsync("Saving department...", async () => {
      if (SelectedNode is not { IsDraft: false } node || Service is not { } service || SelectedTenant is null) return;
      var t = node.Department;
      var activate = node.IsInactive;
      var saved = await service.PostGetTa_TenantDept_Update(new ta_TenantDept {
         cTenantId = t.cTenantId, cTenantDeptAbv = t.cTenantDeptAbv, cTenantDeptHead_cTenantEmpId = t.cTenantDeptHead_cTenantEmpId,
         cTenantDeptId = t.cTenantDeptId, cTenantDeptParent_cTenantDeptId = t.cTenantDeptParent_cTenantDeptId, cTenantDeptCode = t.cTenantDeptCode,
         cTenantDeptName = t.cTenantDeptName, cTenantDeptOrder = t.cTenantDeptOrder, cTenantDeptNote = t.cTenantDeptNote, json_object = t.json_object,
         cTenantDeptState = activate ? ta_TenantDept.StateActive : ta_TenantDept.StateInactive, ustamp = t.ustamp
      });
      ApplyList(await service.GetVi_TenantDepts(SelectedTenant!.cTenantId), saved.cTenantDeptId);
      Status = activate ? $"Department '{saved.cTenantDeptCode}' activated." : $"Department '{saved.cTenantDeptCode}' deactivated.";
   });

   /// <summary>Whether the tree can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;

   /// <summary>Reads the departments again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() {
      if (IsDirty && ConfirmDiscard?.Invoke() == false) return Task.CompletedTask;
      return ReloadAsync();
   }

   #endregion

   /// <summary>Reads every department and rebuilds the tree, keeping the selected department when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading departments...", async () => {
      if (Service is not { } service || SelectedTenant is null) return;
      _draft = null;
      if (EmployeesService is { } employees) _employees = await employees.GetVi_TenantEmps(SelectedTenant!.cTenantId);
      ApplyList(await service.GetVi_TenantDepts(SelectedTenant!.cTenantId), SelectedNode is { IsDraft: false } node ? node.Department.cTenantDeptId : null);
      Status = "";
   });

   private void StartDraft(DepartmentNode? parent) {
      if (!CanBrowse) return;
      if (IsDirty && ConfirmDiscard?.Invoke() == false) return;
      RemoveDraft();
      var draft = new DepartmentNode(this, new vi_TenantDept {
         cTenantDeptParent_cTenantDeptId = parent?.Department.cTenantDeptId,
         cTenantDeptState = ta_TenantDept.StateActive,
         cTenantDeptOrder = -1
      }, parent, isDraft: true);
      _draft = draft;
      if (parent is null) RootNodes.Add(draft);
      else { parent.Children.Add(draft); parent.IsExpanded = true; }
      foreach (var other in AllNodes.Where(n => !ReferenceEquals(n, draft))) other.SelectQuietly(false);
      draft.SelectQuietly(true);
      Open(draft);
      Status = "";
   }

   private void RemoveDraft() {
      if (_draft is not { } draft) return;
      _draft = null;
      if (draft.Parent is { } parent) parent.Children.Remove(draft); else RootNodes.Remove(draft);
   }

   private void Open(DepartmentNode node) {
      SelectedNode = node;
      var t = node.Department;
      ParentOptions.Clear();
      ParentOptions.Add(new(null, "(none)"));
      // A department cannot move under itself or under one of its descendants.
      var excluded = node.Descendants().Select(n => n.Department.cTenantDeptId).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var option in AllNodes.Where(n => !n.IsDraft && !excluded.Contains(n.Department.cTenantDeptId)))
         ParentOptions.Add(new(option.Department.cTenantDeptId, $"{option.Department.cTenantDeptName} ({option.Department.cTenantDeptCode})"));
      HeadOptions.Clear(); HeadOptions.Add(new(null, "(none)"));
      if (!node.IsDraft) foreach (var e in _employees.Where(e => e.cTenantDeptId == t.cTenantDeptId && e.cTenantEmpState == ta_TenantEmp.StateActive))
         HeadOptions.Add(new(e.cTenantEmpId, $"{e.cvContactFullName} ({e.cTenantEmpCode})"));
      EditHead = HeadOptions.FirstOrDefault(o => o.Id == t.cTenantDeptHead_cTenantEmpId) ?? HeadOptions[0];
      EditAbbreviation = t.cTenantDeptAbv;
      EditCode = t.cTenantDeptCode;
      EditName = t.cTenantDeptName;
      EditOrder = t.cTenantDeptOrder;
      EditNote = t.cTenantDeptNote;
      EditParent = ParentOptions.FirstOrDefault(o => string.Equals(o.Id, t.cTenantDeptParent_cTenantDeptId, StringComparison.OrdinalIgnoreCase)) ?? ParentOptions[0];
      UpdateState();
   }

   private void ApplyList(vi_TenantDept[] departments, string? selectId) {
      Departments.Clear();
      foreach (var t in departments) Departments.Add(t);
      var expanded = AllNodes.Where(n => n is { IsDraft: false, IsExpanded: true })
         .Select(n => n.Department.cTenantDeptId).ToHashSet(StringComparer.OrdinalIgnoreCase);
      RootNodes.Clear();
      SelectedNode = null;
      var byId = departments.ToDictionary(t => t.cTenantDeptId, StringComparer.OrdinalIgnoreCase);
      var children = departments.ToLookup(t => t.cTenantDeptParent_cTenantDeptId is { } p && byId.ContainsKey(p) ? p.ToUpperInvariant() : null);
      void Add(vi_TenantDept department, DepartmentNode? parent, HashSet<string> path) {
         // A broken hierarchy (a cycle written outside this screen) must not hang the screen.
         if (!path.Add(department.cTenantDeptId)) return;
         var node = new DepartmentNode(this, department, parent, isDraft: false) { IsExpanded = expanded.Contains(department.cTenantDeptId) };
         (parent?.Children ?? RootNodes).Add(node);
         foreach (var child in Order(children[department.cTenantDeptId.ToUpperInvariant()])) Add(child, node, path);
         path.Remove(department.cTenantDeptId);
      }
      foreach (var root in Order(children[null])) Add(root, null, new(StringComparer.OrdinalIgnoreCase));
      IsLoaded = true;
      if (selectId is not null && AllNodes.FirstOrDefault(n => string.Equals(n.Department.cTenantDeptId, selectId, StringComparison.OrdinalIgnoreCase)) is { } selected) {
         for (var up = selected.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
         selected.SelectQuietly(true);
         Open(selected);
      }
      else ClearEditor();
   }

   private static IEnumerable<vi_TenantDept> Order(IEnumerable<vi_TenantDept> departments) =>
      departments.OrderBy(t => t.cTenantDeptOrder).ThenBy(t => t.cTenantDeptName, StringComparer.OrdinalIgnoreCase);

   private void ClearEditor() {
      HeadOptions.Clear(); EditHead = null; EditAbbreviation = null;
      ParentOptions.Clear();
      EditParent = null; EditCode = ""; EditName = ""; EditOrder = 0; EditNote = null;
      UpdateState();
   }

   private async Task RunAsync(string caption, Func<Task> action) {
      if (IsBusy || (_service is null && EmApp is null)) return;
      try {
         IsBusy = InWaiting = true; WaiterText = caption; UpdateState();
         await action();
      }
      catch (ActionException ex) {
         Status = ex.Message.Replace("Server Error: ", "");
      }
      catch (Exception ex) {
         Status = "The operation did not complete. Refresh and try again.";
         if (EmApp is not null) AlertError(ex);
      }
      finally { InWaiting = IsBusy = false; UpdateState(); }
   }

   private void UpdateState() {
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsDirty), nameof(ToggleStateCaption), nameof(CanBrowse), nameof(CanEdit), nameof(CanChooseHead)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}

