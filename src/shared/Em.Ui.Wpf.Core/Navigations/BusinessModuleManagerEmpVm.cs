using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A communication type with a readable caption.</summary>
/// <param name="Value">Stored communication type.</param>
/// <param name="Caption">Text displayed in the picker.</param>
public sealed record EmployeeCommTypeOption(CommunationType Value, string Caption);

/// <summary>Employee editor and tenant-scoped person picker.</summary>
public sealed partial class BusinessModuleManagerEmpVm : MvvmModelBase
{
   private readonly ITenantEmpServices? _service;
   private readonly ITenantDeptServices? _departments;
   private vi_TenantEmp[] _rows = [];
   private bool _opening;
   private bool _refreshingRows;
   private TenantEmpPerson? _person;
   private int _personGeneration;
   private ITenantEmpServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantEmpServices>();
   private ITenantDeptServices? DepartmentService => _departments ?? EmApp?.ServiceProvider.GetRequiredService<ITenantDeptServices>();
   /// <summary>Creates navigation state.</summary>
   public BusinessModuleManagerEmpVm() : this(null, null) { }
   /// <summary>Creates state with explicit services for tests.</summary>
   public BusinessModuleManagerEmpVm(ITenantEmpServices? service, ITenantDeptServices? departments) {
      _service = service; _departments = departments;
      RegisterCommand<vi_TenantEmp>(nameof(OpenEmployeeCommand), OpenEmployeeCommand, OpenEmployeeCommandAllowed);
      RegisterCommand(nameof(AddCommCommand), AddCommCommand, AddCommCommandAllowed);
      RegisterCommand(nameof(AddAddressCommand), AddAddressCommand, AddAddressCommandAllowed);
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
         Set(value); Open(null); Rows.Clear(); DepartmentOptions.Clear(); IsLoaded = false;
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
   public bool CanLeave() => !IsPersonLoading && (!IsDirty || ConfirmDiscard?.Invoke() != false);
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
         if (IsEditorPage) Open(value);
         else { Set(value); UpdateState(); }
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
   /// <summary>The selected person's pending detail load, awaitable without rendering.</summary>
   public Task PersonLoading { get; private set; } = Task.CompletedTask;
   /// <summary>Whether the shared contact defaults are being loaded.</summary>
   public bool IsPersonLoading { get => Get<bool>(); private set => Set(value, _ => UpdateState()); }
   /// <summary>Whether current person details are ready to save.</summary>
   public bool IsPersonReady => NewPerson || _person is not null;
   /// <summary>Whether the default contact fields are displayed.</summary>
   public bool ShowPersonDetails => IsEditorPage && (NewPerson || IsSaved || SelectedCandidate is not null);
   /// <summary>Whether the person's defaults may be edited.</summary>
   public bool CanEditPerson => CanEdit && IsPersonReady;
   /// <summary>Communication types shown by their readable captions.</summary>
   public EmployeeCommTypeOption[] CommTypes { get; } = [new(CommunationType.Email, "Email"), new(CommunationType.MobilePhone, "Mobile phone"), new(CommunationType.OfficePhone, "Office phone"), new(CommunationType.SocialMedia, "Social media")];
   /// <summary>Type of the person's default communication.</summary>
   public EmployeeCommTypeOption? EditCommType { get => CommTypes.FirstOrDefault(x => x.Value == SelectedComm?.Type); set { if (SelectedComm is not null && value is not null) SelectedComm.Type = value.Value; } }
   /// <summary>Default communication value.</summary>
   public string EditCommValue { get => SelectedComm?.Value ?? ""; set { if (SelectedComm is not null) SelectedComm.Value = value; } }
   /// <summary>Optional communication note.</summary>
   public string EditCommNote { get => SelectedComm?.Note ?? ""; set { if (SelectedComm is not null) SelectedComm.Note = value; } }
   /// <summary>Default address label.</summary>
   public string EditAddressName { get => SelectedAddress?.Name ?? ""; set { if (SelectedAddress is not null) SelectedAddress.Name = value; } }
   /// <summary>Default address location.</summary>
   public string EditAddressLocation { get => SelectedAddress?.Location ?? ""; set { if (SelectedAddress is not null) SelectedAddress.Location = value; } }
   /// <summary>Default address postal code.</summary>
   public string EditAddressZip { get => SelectedAddress?.Zip ?? ""; set { if (SelectedAddress is not null) SelectedAddress.Zip = value; } }
   /// <summary>Whether to create a person instead of choosing an existing one.</summary>
   public bool NewPerson {
      get => Get<bool>();
      set {
         if (!Set(value)) return;
         if (!_opening) {
            if (value) { ResetPerson(); AddComm(); AddAddress(); }
            else if (SelectedCandidate is { } candidate) PersonLoading = LoadPersonAsync(candidate.ContactId);
            else ResetPerson();
         }
         UpdateState(); NotifyChanged(nameof(ExistingPerson));
      }
   }
   /// <summary>Whether the existing-person picker is active.</summary>
   public bool ExistingPerson { get => !NewPerson; set => NewPerson = !value; }
   /// <summary>New person's name.</summary>
   public string NewPersonName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }
   /// <summary>Available person types.</summary>
   public ContactType[] PersonTypes { get; } = [ContactType.Organization, ContactType.Male, ContactType.Female];
   /// <summary>New person's type.</summary>
   public ContactType NewPersonType { get => Get<ContactType>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Host dialog used to choose an existing contact.</summary>
   public Func<string, ContactSearchItem?>? PickContact { get; set; }
   /// <summary>Chosen contact name and linked usernames.</summary>
   public string SelectedContactCaption => SelectedCandidate is { } person ? person.FullName + (person.UserAccounts.Length > 0 ? "\n" + string.Join(", ", person.UserAccounts) : "") : "No contact selected.";
   /// <summary>Selected available person.</summary>
   public TenantEmpContactCandidate? SelectedCandidate {
      get => Get<TenantEmpContactCandidate>();
      set {
         if (value?.TenantEmpId is not null) { NotifyChanged(); return; }
         if (!Set(value)) return;
         if (!_opening && !NewPerson) {
            if (value is not null) PersonLoading = LoadPersonAsync(value.ContactId);
            else ResetPerson();
         }
         UpdateState();
      }
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
   public bool CanEdit => IsEditorPage && CanBrowse && HasSelection && !IsPersonLoading;
   /// <summary>Whether the latest tenant load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }
   /// <summary>Server response or error.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }
   /// <summary>Whether the employee has unsaved changes.</summary>
   public bool IsDirty => IsEditorPage && SelectedRow is { } row && (IsDraft || EditCode != row.cTenantEmpCode ||
      (EditPosition ?? "") != (row.cTenantEmpPosition ?? "") || (EditNote ?? "") != (row.cTenantEmpNote ?? "") || EditDepartment?.Id != row.cTenantDeptId || PersonDirty);
   private bool PersonDirty => _person is not null && PersonSnapshot() != _personSnapshot;
   /// <summary>State action caption.</summary>
   public string ToggleStateCaption => SelectedRow?.cTenantEmpState < 0 ? "Activate" : "Deactivate";
   /// <summary>Explains the effect of transferring a department head.</summary>
   public string HeadChangeHint => SelectedRow?.ccTenantEmpIsDeptHead == true && EditDepartment?.Id != SelectedRow.cTenantDeptId ? "Saving this transfer will leave the previous department without a head." : "";
   /// <summary>Whether a draft can be started.</summary>
   public bool NewEmployeeCommandAllowed() => CanBrowse;
   /// <summary>Starts a draft employee.</summary>
   public Task NewEmployeeCommand() {
      if (!NewEmployeeCommandAllowed() || !CanLeave()) return Task.CompletedTask;
      if (!IsEditorPage) return Loading = NavigateEditorAsync(EmployeeNavigationPayload.New(SelectedTenant!.cTenantId));
      Open(new vi_TenantEmp { cTenantId = SelectedTenant!.cTenantId });
      return Task.CompletedTask;
   }
   /// <summary>Whether the employee can be saved.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && IsPersonReady && DefaultsValid && (!IsDraft || NewPerson || SelectedCandidate is not null);
   /// <summary>Saves the employee and refreshes the list.</summary>
   public Task SaveCommand() {
      if (!SaveCommandAllowed() || HeadChangeHint.Length > 0 && ConfirmHeadChange?.Invoke() == false) return Task.CompletedTask;
      return SaveAsync(false);
   }
   /// <summary>Whether state can be changed.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraft && !IsDirty && DefaultsValid;
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
         }, NewContactName = NewPersonName, NewContactType = NewPersonType, Person = toggle ? null : BuildPerson()
      });
      await Load(saved.cTenantEmpId);
      NavigationEntry?.SetTitle("Employee: " + saved.cTenantEmpId);
      Status = "Employee saved.";
   });
   /// <summary>Whether a refresh can start.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;
   /// <summary>Refreshes after confirming unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;
   /// <summary>Reloads the selected tenant.</summary>
   public Task ReloadAsync() => RunAsync(async () => { await Load(SelectedRow?.cTenantEmpId); Status = ""; });
   /// <summary>Whether candidate search can start.</summary>
   public bool SearchPeopleCommandAllowed() => CanBrowse && !IsPersonLoading && IsDraft && !NewPerson;
   /// <summary>Opens Search contact and loads the chosen person's defaults.</summary>
   public async Task SearchPeopleCommand() {
      if (!SearchPeopleCommandAllowed() || SelectedTenant is null) return;
      if (PersonDirty && ConfirmDiscard?.Invoke() == false) return;
      if (PickContact?.Invoke(SelectedTenant.cTenantId) is { } selected) {
         if (GetContactSelectionBlockReason(selected) is not null) return;
         SelectedCandidate = new() { ContactId = selected.ContactId, FullName = selected.FullName, ContactState = selected.ContactState, ContactType = selected.ContactType, UserAccounts = selected.UserAccounts };
         await PersonLoading;
      }
   }
   /// <summary>Employee-owned filters, applied on the server before the contact picker pages results.</summary>
   public ContactSearchFilter BuildContactPickerFilter() => new() {
      PeopleOnly = false
   };
   /// <summary>Employee-owned selection rule supplied to the shared contact picker.</summary>
   public string? GetContactSelectionBlockReason(ContactSearchItem contact) =>
      _rows.Any(e => string.Equals(e.cContactId, contact.ContactId, StringComparison.OrdinalIgnoreCase)) ? "Already an employee here" : null;
   private async Task Load(string? selectId) {
      if (Service is not { } service || SelectedTenant is null) return;
      var departments = DepartmentService is { } ds ? await ds.GetVi_TenantDepts(SelectedTenant.cTenantId) : [];
      _rows = await service.GetVi_TenantEmps(SelectedTenant.cTenantId);
      DepartmentOptions.Clear(); DepartmentOptions.Add(new(null, "(none)"));
      foreach (var d in departments) DepartmentOptions.Add(new(d.cTenantDeptId, $"{d.cTenantDeptName} ({d.cTenantDeptCode})"));
      ApplyFilter();
      var selected = _rows.FirstOrDefault(e => e.cTenantEmpId == selectId);
      if (IsEditorPage) Open(selected); else SelectedRow = selected;
      await PersonLoading; IsLoaded = true;
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
         ResetPerson();
         SelectedRow = row; EditCode = row?.cTenantEmpCode ?? ""; EditPosition = row?.cTenantEmpPosition; EditNote = row?.cTenantEmpNote;
         EditDepartment = DepartmentOptions.FirstOrDefault(d => d.Id == row?.cTenantDeptId);
         NewPerson = false; NewPersonName = ""; NewPersonType = ContactType.Male; SelectedCandidate = null;
      } finally { _opening = false; UpdateState(); }
      if (row is { cContactId.Length: > 0 }) PersonLoading = LoadPersonAsync(row.cContactId);
   }
   private void ResetPerson() {
      _personGeneration++; _person = null; IsPersonLoading = false;
      _personSnapshot = "";
      Communications.Clear(); Addresses.Clear(); SelectedComm = DefaultComm = null; SelectedAddress = DefaultAddress = null;
      PersonLoading = Task.CompletedTask;
      UpdateState();
   }
   private async Task LoadPersonAsync(string contactId) {
      ResetPerson(); var generation = _personGeneration;
      IsPersonLoading = true;
      try {
         if (Service is not { } service) return;
         var person = await service.GetMeta_TenantEmpPerson(contactId);
         if (generation != _personGeneration) return;
         _person = person;
         EditContactType = person.ContactType ?? SelectedCandidate?.ContactType ?? SelectedRow?.cvContactType ?? ContactType.Organization;
         foreach (var item in person.Comms ?? (person.Comm is { } comm ? [comm] : Array.Empty<ta_Comm>())) Communications.Add(Watch(new EmployeeCommunicationVm(item)));
         foreach (var item in person.Addresses ?? (person.Address is { } address ? [address] : Array.Empty<ta_Address>())) Addresses.Add(Watch(new EmployeeAddressVm(item)));
         DefaultComm = Communications.FirstOrDefault(x => x.Id == (person.DefaultCommId ?? person.Comm?.cCommId));
         DefaultAddress = Addresses.FirstOrDefault(x => x.Id == (person.DefaultAddressId ?? person.Address?.cAddressId));
         SelectedComm = DefaultComm ?? Communications.FirstOrDefault(); SelectedAddress = DefaultAddress ?? Addresses.FirstOrDefault();
         _personSnapshot = PersonSnapshot();
      }
      catch (Exception ex) {
         if (generation == _personGeneration) Status = ex is ActionException ? ex.Message.Replace("Server Error: ", "") : "Could not load the person's defaults. Refresh and try again.";
      }
      finally { if (generation == _personGeneration) { IsPersonLoading = false; UpdateState(); } }
   }
   private TenantEmpPerson BuildPerson() => new() {
      ContactId = NewPerson ? "" : _person!.ContactId, ContactUstamp = _person?.ContactUstamp ?? default,
      ContactType = NewPerson ? NewPersonType : EditContactType, EditCollections = true,
      Comms = Communications.Select(x => x.ToRow()).ToArray(), Addresses = Addresses.Select(x => x.ToRow()).ToArray(),
      DefaultCommId = DefaultComm?.Id, DefaultAddressId = DefaultAddress?.Id,
      Comm = DefaultComm?.ToRow(), Address = DefaultAddress?.ToRow()
   };
   private async Task RunAsync(Func<Task> work) {
      if (IsBusy || Service is null) return;
      try { IsBusy = InWaiting = true; WaiterText = "Loading employees..."; UpdateState(); await work(); }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) { Status = "The operation did not complete. Refresh and try again."; if (EmApp is not null) AlertError(ex); }
      finally { IsBusy = InWaiting = false; UpdateState(); }
   }
   private void UpdateState() {
      foreach (var name in new[] { nameof(SelectedContactCaption), nameof(IsPersonReady), nameof(ShowPersonDetails), nameof(CanEditPerson), nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraft), nameof(IsSaved), nameof(ShowNewPerson), nameof(ShowExistingPerson), nameof(CanBrowse), nameof(CanEdit), nameof(IsDirty), nameof(ToggleStateCaption), nameof(PersonCaption), nameof(HeadChangeHint), nameof(DefaultsValid), nameof(DefaultGuardMessage) }) NotifyChanged(name);
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
