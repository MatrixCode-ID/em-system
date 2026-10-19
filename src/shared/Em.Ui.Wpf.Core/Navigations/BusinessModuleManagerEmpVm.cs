using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>Employee editor and tenant-scoped person picker.</summary>
public sealed class BusinessModuleManagerEmpVm : MvvmModelBase
{
   private readonly ITenantEmpServices? _service;
   private readonly ITenantDeptServices? _departments;
   private vi_TenantEmp[] _rows = [];
   private bool _opening;
   private bool _refreshingRows;
   private ITenantEmpServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantEmpServices>();
   private ITenantDeptServices? DepartmentService => _departments ?? EmApp?.ServiceProvider.GetRequiredService<ITenantDeptServices>();
   /// <summary>Creates navigation state.</summary>
   public BusinessModuleManagerEmpVm() : this(null, null) { }
   /// <summary>Creates state with explicit services for tests.</summary>
   public BusinessModuleManagerEmpVm(ITenantEmpServices? service, ITenantDeptServices? departments) {
      _service = service; _departments = departments;
      RegisterCommand(nameof(NewEmployeeCommand), NewEmployeeCommand, NewEmployeeCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(SearchPeopleCommand), SearchPeopleCommand, SearchPeopleCommandAllowed);
   }
   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];
   /// <summary>Current tenant.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); Open(null); Rows.Clear(); DepartmentOptions.Clear(); Candidates.Clear(); IsLoaded = false;
         Loading = ReloadAsync(); UpdateState();
      }
   }
   /// <summary>Latest tenant load.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;
   /// <summary>Host confirmation before discarding changes.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }
   /// <summary>Host confirmation before clearing a department head.</summary>
   public Func<bool>? ConfirmHeadChange { get; set; }
   /// <summary>Checks whether the editor can be left.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;
   /// <summary>Restores the last saved values after discard was confirmed.</summary>
   public void DiscardChanges() { if (IsDirty) Open(IsDraft ? null : SelectedRow); }
   /// <summary>Rows matching the text filter.</summary>
   public ObservableCollection<vi_TenantEmp> Rows { get; } = [];
   /// <summary>Selected saved employee or draft.</summary>
   public vi_TenantEmp? SelectedRow {
      get => Get<vi_TenantEmp>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Open(value);
      }
   }
   /// <summary>Client-side name, code and department filter.</summary>
   public string Filter { get => Get<string>() ?? ""; set { Set(value); ApplyFilter(); } }
   /// <summary>Editable employee number.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }
   /// <summary>Editable position.</summary>
   public string? EditPosition { get => Get<string>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Editable note.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Department choices, including no department.</summary>
   public ObservableCollection<DepartmentOption> DepartmentOptions { get; } = [];
   /// <summary>Selected department.</summary>
   public DepartmentOption? EditDepartment { get => Get<DepartmentOption>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Whether to create a person instead of choosing an existing one.</summary>
   public bool NewPerson { get => Get<bool>(); set { Set(value); UpdateState(); NotifyChanged(nameof(ExistingPerson)); } }
   /// <summary>Whether the existing-person picker is active.</summary>
   public bool ExistingPerson { get => !NewPerson; set => NewPerson = !value; }
   /// <summary>New person's name.</summary>
   public string NewPersonName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }
   /// <summary>Available person types.</summary>
   public ContactType[] PersonTypes { get; } = [ContactType.Male, ContactType.Female];
   /// <summary>New person's type.</summary>
   public ContactType NewPersonType { get => Get<ContactType>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Person search text, sent on Search.</summary>
   public string PersonSearch { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Person search results.</summary>
   public ObservableCollection<TenantEmpContactCandidate> Candidates { get; } = [];
   /// <summary>Selected available person.</summary>
   public TenantEmpContactCandidate? SelectedCandidate {
      get => Get<TenantEmpContactCandidate>();
      set { if (value?.TenantEmpId is not null) { NotifyChanged(); return; } Set(value, _ => UpdateState()); }
   }
   /// <summary>Name and accounts for the saved person.</summary>
   public string PersonCaption => SelectedRow is { } row ? row.cvContactFullName + "\n" + row.cvTenantEmpUserAccounts : "";
   /// <summary>Whether an employee is open.</summary>
   public bool HasSelection => SelectedRow is not null;
   /// <summary>Whether the empty-selection hint is shown.</summary>
   public bool HasNoSelection => !HasSelection;
   /// <summary>Whether the selected employee has not been saved.</summary>
   public bool IsDraft => SelectedRow is { cTenantEmpId.Length: 0 };
   /// <summary>Whether the saved person details are shown read-only.</summary>
   public bool IsSaved => HasSelection && !IsDraft;
   /// <summary>Whether a new person is being entered.</summary>
   public bool ShowNewPerson => IsDraft && NewPerson;
   /// <summary>Whether existing people can be chosen.</summary>
   public bool ShowExistingPerson => IsDraft && !NewPerson;
   /// <summary>Whether rows can be browsed.</summary>
   public bool CanBrowse => IsLoaded && SelectedTenant is not null && IsNotBusy;
   /// <summary>Whether fields can be edited.</summary>
   public bool CanEdit => CanBrowse && HasSelection;
   /// <summary>Whether the latest tenant load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }
   /// <summary>Server response or error.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }
   /// <summary>Whether the employee has unsaved changes.</summary>
   public bool IsDirty => SelectedRow is { } row && (IsDraft || EditCode != row.cTenantEmpCode ||
      (EditPosition ?? "") != (row.cTenantEmpPosition ?? "") || (EditNote ?? "") != (row.cTenantEmpNote ?? "") || EditDepartment?.Id != row.cTenantDeptId);
   /// <summary>State action caption.</summary>
   public string ToggleStateCaption => SelectedRow?.cTenantEmpState < 0 ? "Activate" : "Deactivate";
   /// <summary>Explains the effect of transferring a department head.</summary>
   public string HeadChangeHint => SelectedRow?.ccTenantEmpIsDeptHead == true && EditDepartment?.Id != SelectedRow.cTenantDeptId ? "Saving this transfer will leave the previous department without a head." : "";
   /// <summary>Whether a draft can be started.</summary>
   public bool NewEmployeeCommandAllowed() => CanBrowse;
   /// <summary>Starts a draft employee.</summary>
   public void NewEmployeeCommand() {
      if (!NewEmployeeCommandAllowed() || !CanLeave()) return;
      Open(new vi_TenantEmp { cTenantId = SelectedTenant!.cTenantId });
   }
   /// <summary>Whether the employee can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && (!IsDraft || NewPerson || SelectedCandidate is not null);
   /// <summary>Saves the employee and refreshes the list.</summary>
   public Task SaveCommand() {
      if (!SaveCommandAllowed() || HeadChangeHint.Length > 0 && ConfirmHeadChange?.Invoke() == false) return Task.CompletedTask;
      return SaveAsync(false);
   }
   /// <summary>Whether state can be changed.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraft && !IsDirty;
   /// <summary>Activates or deactivates the saved employee.</summary>
   public Task ToggleStateCommand() {
      if (!ToggleStateCommandAllowed() || SelectedRow is { ccTenantEmpIsDeptHead: true, cTenantEmpState: 0 } && ConfirmHeadChange?.Invoke() == false) return Task.CompletedTask;
      return SaveAsync(true);
   }
   private Task SaveAsync(bool toggle) => RunAsync(async () => {
      if (SelectedRow is not { } row || Service is not { } service) return;
      var saved = await service.PostGetMeta_TenantEmpSave(new TenantEmpSave {
         Row = new ta_TenantEmp {
            cTenantEmpId = row.cTenantEmpId, cTenantId = row.cTenantId,
            cContactId = IsDraft ? (NewPerson ? "" : SelectedCandidate!.ContactId) : row.cContactId,
            cTenantDeptId = EditDepartment?.Id, cTenantEmpCode = EditCode, cTenantEmpPosition = EditPosition,
            cTenantEmpNote = EditNote, cTenantEmpState = toggle ? (row.cTenantEmpState == 0 ? -1 : 0) : row.cTenantEmpState,
            json_object = row.json_object, ustamp = row.ustamp
         }, NewContactName = NewPersonName, NewContactType = NewPersonType
      });
      await Load(saved.cTenantEmpId); Status = "Employee saved.";
   });
   /// <summary>Whether a refresh can start.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;
   /// <summary>Refreshes after confirming unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;
   /// <summary>Reloads the selected tenant.</summary>
   public Task ReloadAsync() => RunAsync(async () => { await Load(SelectedRow?.cTenantEmpId); Status = ""; });
   /// <summary>Whether candidate search can start.</summary>
   public bool SearchPeopleCommandAllowed() => CanBrowse && IsDraft && !NewPerson;
   /// <summary>Finds available people by name or account.</summary>
   public Task SearchPeopleCommand() => RunAsync(async () => {
      if (Service is not { } service || SelectedTenant is null) return;
      Candidates.Clear(); SelectedCandidate = null;
      foreach (var c in await service.GetMeta_TenantEmpContacts(SelectedTenant.cTenantId, PersonSearch)) Candidates.Add(c);
   });
   private async Task Load(string? selectId) {
      if (Service is not { } service || SelectedTenant is null) return;
      var departments = DepartmentService is { } ds ? await ds.GetVi_TenantDepts(SelectedTenant.cTenantId) : [];
      _rows = await service.GetVi_TenantEmps(SelectedTenant.cTenantId);
      DepartmentOptions.Clear(); DepartmentOptions.Add(new(null, "(none)"));
      foreach (var d in departments) DepartmentOptions.Add(new(d.cTenantDeptId, $"{d.cTenantDeptName} ({d.cTenantDeptCode})"));
      ApplyFilter(); Open(_rows.FirstOrDefault(e => e.cTenantEmpId == selectId)); IsLoaded = true;
   }
   private void ApplyFilter() {
      // Filtering the list must not discard an open editor when WPF clears the list selection.
      _refreshingRows = true;
      try {
         Rows.Clear(); foreach (var e in _rows.Where(e => string.IsNullOrEmpty(Filter) ||
            (e.cvContactFullName + " " + e.cTenantEmpCode + " " + e.cvTenantDeptCode + " " + e.cvTenantDeptName).Contains(Filter, StringComparison.OrdinalIgnoreCase))) Rows.Add(e);
         NotifyChanged(nameof(SelectedRow));
      } finally { _refreshingRows = false; }
   }
   private void Open(vi_TenantEmp? row) {
      _opening = true;
      try {
         SelectedRow = row; EditCode = row?.cTenantEmpCode ?? ""; EditPosition = row?.cTenantEmpPosition; EditNote = row?.cTenantEmpNote;
         EditDepartment = DepartmentOptions.FirstOrDefault(d => d.Id == row?.cTenantDeptId);
         NewPerson = false; NewPersonName = ""; NewPersonType = ContactType.Male; SelectedCandidate = null; Candidates.Clear();
      } finally { _opening = false; UpdateState(); }
   }
   private async Task RunAsync(Func<Task> work) {
      if (IsBusy || Service is null) return;
      try { IsBusy = InWaiting = true; WaiterText = "Loading employees..."; UpdateState(); await work(); }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) { Status = "The operation did not complete. Refresh and try again."; if (EmApp is not null) AlertError(ex); }
      finally { IsBusy = InWaiting = false; UpdateState(); }
   }
   private void UpdateState() {
      foreach (var name in new[] { nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraft), nameof(IsSaved), nameof(ShowNewPerson), nameof(ShowExistingPerson), nameof(CanBrowse), nameof(CanEdit), nameof(IsDirty), nameof(ToggleStateCaption), nameof(PersonCaption), nameof(HeadChangeHint) }) NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}

/// <summary>Organization tab coordination.</summary>
public sealed partial class BusinessModuleManagerVm
{
   /// <summary>Checks all editors before leaving the screen.</summary>
   public bool CanLeave() => (!IsDirty || ConfirmDiscard?.Invoke() != false) && Departments.CanLeave() && Employees.CanLeave();
   /// <summary>Whether tabs can be switched during work.</summary>
   public bool CanSwitchTabs => IsNotBusy && Departments.IsNotBusy && Employees.IsNotBusy;
   /// <summary>Department tab state.</summary>
   public BusinessModuleManagerDeptVm Departments { get; } = new();
   /// <summary>Employee tab state.</summary>
   public BusinessModuleManagerEmpVm Employees { get; } = new();
   /// <summary>Whether departments are shown.</summary>
   public bool IsDepartmentsTab => ActiveTab.Key == "departments";
   /// <summary>Whether employees are shown.</summary>
   public bool IsEmployeesTab => ActiveTab.Key == "employees";
}
