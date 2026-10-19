using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A tab of the Business Module Manager.</summary>
/// <param name="Key">Stable tab key.</param>
/// <param name="Title">Caption shown on the tab strip.</param>
public sealed record BusinessTab(string Key, string Title);

/// <summary>A choice of the Parent field: a tenant, or no parent at all.</summary>
/// <param name="Id">Tenant ID; <c>null</c> = no parent (a root tenant).</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record TenantParentOption(string? Id, string Caption);

/// <summary>One tenant in the tree of the Business Module Manager, or a draft that is not saved yet.</summary>
public sealed class TenantNode : MvvmModelBase
{
   private readonly BusinessModuleManagerVm _owner;

   internal TenantNode(BusinessModuleManagerVm owner, vi_Tenant tenant, TenantNode? parent, bool isDraft) {
      _owner = owner; Tenant = tenant; Parent = parent; IsDraft = isDraft;
   }

   /// <summary>The tenant row as last read; for a draft, its starting values.</summary>
   public vi_Tenant Tenant { get; }

   /// <summary>Parent node; <c>null</c> for a root tenant.</summary>
   public TenantNode? Parent { get; }

   /// <summary>Child nodes in display order.</summary>
   public ObservableCollection<TenantNode> Children { get; } = [];

   /// <summary>Whether this node is a new tenant that has not been saved.</summary>
   public bool IsDraft { get; }

   /// <summary>Name shown in the tree.</summary>
   public string DisplayName => IsDraft ? "(new tenant)" : Tenant.cTenantName;

   /// <summary>Code shown next to the name.</summary>
   public string DisplayCode => IsDraft ? "" : Tenant.cTenantCode;

   /// <summary>Whether the tenant is deactivated.</summary>
   public bool IsInactive => !IsDraft && Tenant.cTenantState < ta_Tenant.StateActive;

   /// <summary>Whether the children are shown.</summary>
   public bool IsExpanded { get => Get<bool>(); set => Set(value); }

   /// <summary>Whether this node is selected in the tree; selecting it opens it in the editor.</summary>
   public bool IsSelected {
      get => Get<bool>();
      set { if (Set(value) && value) _owner.OnNodeSelected(this); }
   }

   /// <summary>This node and every node below it.</summary>
   public IEnumerable<TenantNode> Descendants() {
      yield return this;
      foreach (var child in Children)
         foreach (var node in child.Descendants()) yield return node;
   }

   // Changes the selection mark without opening the node in the editor.
   internal void SelectQuietly(bool value) => Set(value, propertyName: nameof(IsSelected));
}

/// <summary>State and commands of the Business Module Manager; the first tab manages tenants.</summary>
public sealed partial class BusinessModuleManagerVm : MvvmModelBase
{
   /// <summary>Key of the Tenants tab.</summary>
   public const string TenantsTab = "tenants";

   private readonly ITenantServices? _service;
   private TenantNode? _draft;
   private bool _restoringSelection;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerVm(ITenantServices service) { _service = service; RegisterCommands(); }

   private ITenantServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantServices>();

   private void RegisterCommands() {
      Departments.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(IsBusy) or nameof(IsNotBusy)) NotifyChanged(nameof(CanSwitchTabs)); };
      Employees.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(IsBusy) or nameof(IsNotBusy)) NotifyChanged(nameof(CanSwitchTabs)); };
      RegisterCommand(nameof(NewTenantCommand), NewTenantCommand, NewTenantCommandAllowed);
      RegisterCommand(nameof(NewChildCommand), NewChildCommand, NewChildCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
   }

   #region Tabs

   /// <summary>Tabs of the screen. Tenants, departments and employees.</summary>
   public ObservableCollection<BusinessTab> Tabs { get; } = [new(TenantsTab, "Tenants"), new("departments", "Departments"), new("employees", "Employees")];

   /// <summary>The tab being shown.</summary>
   public BusinessTab ActiveTab {
      get => Get<BusinessTab>() ?? Tabs[0];
      set {
         if (ReferenceEquals(value, ActiveTab)) return;
         if (!CanSwitchTabs || !CanLeave()) { NotifyChanged(); return; }
         if (IsDirty) {
            if (SelectedNode is { IsDraft: false } node) Open(node);
            else { RemoveDraft(); SelectedNode = null; ClearEditor(); }
         }
         Departments.DiscardChanges(); Employees.DiscardChanges();
         Set(value); NotifyChanged(nameof(IsTenantsTab)); NotifyChanged(nameof(IsDepartmentsTab)); NotifyChanged(nameof(IsEmployeesTab));
      }
   }

   /// <summary>Whether the Tenants tab is shown.</summary>
   public bool IsTenantsTab => ActiveTab.Key == TenantsTab;

   #endregion

   #region Tree

   /// <summary>Every saved tenant as read from the server.</summary>
   public ObservableCollection<vi_Tenant> Tenants { get; } = [];

   /// <summary>Root nodes of the tenant tree, including a root draft.</summary>
   public ObservableCollection<TenantNode> RootNodes { get; } = [];

   /// <summary>The node open in the editor.</summary>
   public TenantNode? SelectedNode { get => Get<TenantNode>(); private set { Set(value); UpdateState(); } }

   /// <summary>Every node of the tree, depth first.</summary>
   public IEnumerable<TenantNode> AllNodes => RootNodes.SelectMany(n => n.Descendants());

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   internal void OnNodeSelected(TenantNode node) {
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

   /// <summary>Code of the edited tenant.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited tenant.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Display order of the edited tenant among its siblings.</summary>
   public int EditOrder { get => Get<int>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited tenant.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Parent choices: no parent, then every tenant except the edited one and its descendants.</summary>
   public ObservableCollection<TenantParentOption> ParentOptions { get; } = [];

   /// <summary>Selected parent choice.</summary>
   public TenantParentOption? EditParent { get => Get<TenantParentOption>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether a node is open in the editor.</summary>
   public bool HasSelection => SelectedNode is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedNode is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedNode?.IsDraft == true;

   /// <summary>Whether the editor differs from the saved tenant; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedNode is { } node && (node.IsDraft ||
      EditCode != node.Tenant.cTenantCode || EditName != node.Tenant.cTenantName || EditOrder != node.Tenant.cTenantOrder ||
      (EditNote ?? "") != (node.Tenant.cTenantNote ?? "") || EditParent?.Id != node.Tenant.cTenantParent_cTenantId);

   /// <summary>Caption of the state button: Deactivate for an active tenant, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedNode?.IsInactive == true ? "Activate" : "Deactivate";

   /// <summary>Whether the toolbar and tree accept input.</summary>
   public bool CanBrowse => IsLoaded && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the first successful read.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   #endregion

   #region Commands

   /// <summary>Whether a new root tenant can be started.</summary>
   public bool NewTenantCommandAllowed() => CanBrowse;

   /// <summary>Starts a new root tenant as a draft node.</summary>
   public void NewTenantCommand() => StartDraft(null);

   /// <summary>Whether a new child of the selected saved tenant can be started.</summary>
   public bool NewChildCommandAllowed() => CanBrowse && SelectedNode is { IsDraft: false };

   /// <summary>Starts a new tenant under the selected tenant as a draft node.</summary>
   public void NewChildCommand() { if (SelectedNode is { IsDraft: false } parent) StartDraft(parent); }

   /// <summary>Whether the editor holds changes that can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty;

   /// <summary>Creates the draft or saves the changes of the selected tenant, then reloads the tree.</summary>
   public Task SaveCommand() => RunAsync("Saving tenant...", async () => {
      if (SelectedNode is not { } node || Service is not { } service) return;
      var row = new ta_Tenant {
         cTenantId = node.IsDraft ? "" : node.Tenant.cTenantId,
         cTenantParent_cTenantId = EditParent?.Id,
         cTenantCode = EditCode,
         cTenantName = EditName,
         cTenantOrder = EditOrder,
         cTenantNote = string.IsNullOrEmpty(EditNote) ? null : EditNote,
         cTenantState = node.IsDraft ? ta_Tenant.StateActive : node.Tenant.cTenantState,
         json_object = node.Tenant.json_object,
         ustamp = node.Tenant.ustamp
      };
      var saved = node.IsDraft ? await service.PostGetTa_Tenant_New(row) : await service.PostGetTa_Tenant_Update(row);
      _draft = null;
      ApplyList(await service.GetVi_Tenants(), saved.cTenantId);
      Status = node.IsDraft ? $"Tenant '{saved.cTenantCode}' created." : $"Tenant '{saved.cTenantCode}' saved.";
   });

   /// <summary>Whether the selected saved tenant, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanBrowse && SelectedNode is { IsDraft: false } && !IsDirty;

   /// <summary>Deactivates an active tenant or activates an inactive one; its children keep their own state.</summary>
   public Task ToggleStateCommand() => RunAsync("Saving tenant...", async () => {
      if (SelectedNode is not { IsDraft: false } node || Service is not { } service) return;
      var t = node.Tenant;
      var activate = node.IsInactive;
      var saved = await service.PostGetTa_Tenant_Update(new ta_Tenant {
         cTenantId = t.cTenantId, cTenantParent_cTenantId = t.cTenantParent_cTenantId, cTenantCode = t.cTenantCode,
         cTenantName = t.cTenantName, cTenantOrder = t.cTenantOrder, cTenantNote = t.cTenantNote, json_object = t.json_object,
         cTenantState = activate ? ta_Tenant.StateActive : ta_Tenant.StateInactive, ustamp = t.ustamp
      });
      ApplyList(await service.GetVi_Tenants(), saved.cTenantId);
      Status = activate ? $"Tenant '{saved.cTenantCode}' activated." : $"Tenant '{saved.cTenantCode}' deactivated.";
   });

   /// <summary>Whether the tree can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;

   /// <summary>Reads the tenants again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() {
      if (IsDepartmentsTab) return Departments.RefreshCommand();
      if (IsEmployeesTab) return Employees.RefreshCommand();
      if (IsDirty && ConfirmDiscard?.Invoke() == false) return Task.CompletedTask;
      return ReloadAsync();
   }

   #endregion

   /// <summary>Reads every tenant and rebuilds the tree, keeping the selected tenant when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading tenants...", async () => {
      if (Service is not { } service) return;
      _draft = null;
      ApplyList(await service.GetVi_Tenants(), SelectedNode is { IsDraft: false } node ? node.Tenant.cTenantId : null);
      Departments.EmApp = Employees.EmApp = EmApp;
      Departments.ConfirmDiscard = Employees.ConfirmDiscard = ConfirmDiscard;
      Departments.TenantChoices.Clear(); Employees.TenantChoices.Clear();
      foreach (var tenant in Tenants) { Departments.TenantChoices.Add(tenant); Employees.TenantChoices.Add(tenant); }
      Status = "";
   });

   private void StartDraft(TenantNode? parent) {
      if (!CanBrowse) return;
      if (IsDirty && ConfirmDiscard?.Invoke() == false) return;
      RemoveDraft();
      var draft = new TenantNode(this, new vi_Tenant {
         cTenantParent_cTenantId = parent?.Tenant.cTenantId,
         cTenantState = ta_Tenant.StateActive,
         cTenantOrder = -1
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

   private void Open(TenantNode node) {
      SelectedNode = node;
      var t = node.Tenant;
      ParentOptions.Clear();
      ParentOptions.Add(new(null, "(none)"));
      // A tenant cannot move under itself or under one of its descendants.
      var excluded = node.Descendants().Select(n => n.Tenant.cTenantId).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var option in AllNodes.Where(n => !n.IsDraft && !excluded.Contains(n.Tenant.cTenantId)))
         ParentOptions.Add(new(option.Tenant.cTenantId, $"{option.Tenant.cTenantName} ({option.Tenant.cTenantCode})"));
      EditCode = t.cTenantCode;
      EditName = t.cTenantName;
      EditOrder = t.cTenantOrder;
      EditNote = t.cTenantNote;
      EditParent = ParentOptions.FirstOrDefault(o => string.Equals(o.Id, t.cTenantParent_cTenantId, StringComparison.OrdinalIgnoreCase)) ?? ParentOptions[0];
      UpdateState();
   }

   private void ApplyList(vi_Tenant[] tenants, string? selectId) {
      Tenants.Clear();
      foreach (var t in tenants) Tenants.Add(t);
      var expanded = AllNodes.Where(n => n is { IsDraft: false, IsExpanded: true })
         .Select(n => n.Tenant.cTenantId).ToHashSet(StringComparer.OrdinalIgnoreCase);
      RootNodes.Clear();
      SelectedNode = null;
      var byId = tenants.ToDictionary(t => t.cTenantId, StringComparer.OrdinalIgnoreCase);
      var children = tenants.ToLookup(t => t.cTenantParent_cTenantId is { } p && byId.ContainsKey(p) ? p.ToUpperInvariant() : null);
      void Add(vi_Tenant tenant, TenantNode? parent, HashSet<string> path) {
         // A broken hierarchy (a cycle written outside this screen) must not hang the screen.
         if (!path.Add(tenant.cTenantId)) return;
         var node = new TenantNode(this, tenant, parent, isDraft: false) { IsExpanded = expanded.Contains(tenant.cTenantId) };
         (parent?.Children ?? RootNodes).Add(node);
         foreach (var child in Order(children[tenant.cTenantId.ToUpperInvariant()])) Add(child, node, path);
         path.Remove(tenant.cTenantId);
      }
      foreach (var root in Order(children[null])) Add(root, null, new(StringComparer.OrdinalIgnoreCase));
      IsLoaded = true;
      if (selectId is not null && AllNodes.FirstOrDefault(n => string.Equals(n.Tenant.cTenantId, selectId, StringComparison.OrdinalIgnoreCase)) is { } selected) {
         for (var up = selected.Parent; up is not null; up = up.Parent) up.IsExpanded = true;
         selected.SelectQuietly(true);
         Open(selected);
      }
      else ClearEditor();
   }

   private static IEnumerable<vi_Tenant> Order(IEnumerable<vi_Tenant> tenants) =>
      tenants.OrderBy(t => t.cTenantOrder).ThenBy(t => t.cTenantName, StringComparer.OrdinalIgnoreCase);

   private void ClearEditor() {
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
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsDirty), nameof(ToggleStateCaption), nameof(CanBrowse), nameof(CanEdit), nameof(CanSwitchTabs)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
