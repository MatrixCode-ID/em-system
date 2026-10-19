using System.Collections.ObjectModel;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

public sealed partial class BusinessModuleManagerEmpVm
{
   private string _personSnapshot = "";
   /// <summary>Whether this instance edits one employee in a dedicated navigation.</summary>
   public bool IsEditorPage { get; set; }
   /// <summary>Opener supplied by a manager host or test.</summary>
   public Func<EmployeeNavigationPayload, Task>? OpenEditor { get; set; }
   /// <summary>All communication entries of the shared contact.</summary>
   public ObservableCollection<EmployeeCommunicationVm> Communications { get; } = [];
   /// <summary>All address entries of the shared contact.</summary>
   public ObservableCollection<EmployeeAddressVm> Addresses { get; } = [];
   /// <summary>Communication selected for editing.</summary>
   public EmployeeCommunicationVm? SelectedComm { get => Get<EmployeeCommunicationVm>(); set { Set(value); NotifyLegacyFields(); } }
   /// <summary>Address selected for editing.</summary>
   public EmployeeAddressVm? SelectedAddress { get => Get<EmployeeAddressVm>(); set { Set(value); NotifyLegacyFields(); } }
   /// <summary>Required default communication.</summary>
   public EmployeeCommunicationVm? DefaultComm { get => Get<EmployeeCommunicationVm>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Required default address.</summary>
   public EmployeeAddressVm? DefaultAddress { get => Get<EmployeeAddressVm>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Editable type of an existing contact, shared across all its uses.</summary>
   public ContactType EditContactType { get => Get<ContactType>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Whether both defaults are selected, active and contain their required content.</summary>
   public bool DefaultsValid => DefaultComm is not null && Communications.Contains(DefaultComm) && DefaultComm.State != ContactCommunicationState.Inactive &&
      !string.IsNullOrWhiteSpace(DefaultComm.Value) && DefaultAddress is not null && Addresses.Contains(DefaultAddress) && DefaultAddress.State != AddressState.Inactive && !string.IsNullOrWhiteSpace(DefaultAddress.Location);
   /// <summary>Explains the required defaults before saving.</summary>
   public string DefaultGuardMessage => ShowPersonDetails && !DefaultsValid ? "Choose a default communication with a value and a default address with a location. Both must be active." : "";
   /// <summary>Whether the selected employee can be opened.</summary>
   public bool OpenEmployeeCommandAllowed(vi_TenantEmp? row) => CanBrowse && (row ?? SelectedRow) is { cTenantEmpId.Length: > 0 };
   /// <summary>Opens the selected employee through the reusable editor navigation.</summary>
   public Task OpenEmployeeCommand(vi_TenantEmp? row) => OpenEmployeeCommandAllowed(row) ? NavigateEditorAsync(EmployeeNavigationPayload.Open(SelectedTenant!.cTenantId, (row ?? SelectedRow)!.cTenantEmpId)) : Task.CompletedTask;
   private Task NavigateEditorAsync(EmployeeNavigationPayload payload) => OpenEditor?.Invoke(payload) ?? NavigationEntry?.NavigateTo(Em.Ui.Wpf.Core.EmApp.EmployeeEditorNavigationName, payload) ?? Task.CompletedTask;
   /// <summary>Whether a communication can be added.</summary>
   public bool AddCommCommandAllowed() => CanEditPerson;
   /// <summary>Adds an editable communication, without replacing existing entries.</summary>
   public void AddCommCommand() { if (AddCommCommandAllowed()) AddComm(); }
   private void AddComm() {
      var item = Watch(new EmployeeCommunicationVm(new() { cCommId = Ulid.NewUlid().ToString(), cCommState = ContactCommunicationState.Active }));
      Communications.Add(item); SelectedComm = item; DefaultComm ??= item; UpdateState();
   }
   /// <summary>Whether an address can be added.</summary>
   public bool AddAddressCommandAllowed() => CanEditPerson;
   /// <summary>Adds an editable address, without replacing existing entries.</summary>
   public void AddAddressCommand() { if (AddAddressCommandAllowed()) AddAddress(); }
   private void AddAddress() {
      var item = Watch(new EmployeeAddressVm(new() { cAddressId = Ulid.NewUlid().ToString(), cAddressState = AddressState.Active }));
      Addresses.Add(item); SelectedAddress = item; DefaultAddress ??= item; UpdateState();
   }
   private T Watch<T>(T item) where T : MvvmModelBase {
      item.PropertyChanged += (_, _) => { UpdateState(); NotifyLegacyFields(); };
      return item;
   }
   private void NotifyLegacyFields() {
      foreach (var name in new[] { nameof(EditCommType), nameof(EditCommValue), nameof(EditCommNote), nameof(EditAddressName), nameof(EditAddressLocation), nameof(EditAddressZip) }) NotifyChanged(name);
   }
   private string PersonSnapshot() => JsonSerializer.Serialize(BuildPerson());
   /// <summary>Loads an existing employee or a tenant-scoped new draft for any navigation caller.</summary>
   public async Task LoadEditorAsync(EmployeeNavigationPayload payload) {
      IsEditorPage = true;
      var tenant = TenantChoices.FirstOrDefault(x => x.cTenantId == payload.TenantId);
      if (tenant is null && EmApp is { } app) tenant = (await app.ServiceProvider.GetRequiredService<ITenantServices>().GetVi_Tenants()).FirstOrDefault(x => x.cTenantId == payload.TenantId);
      if (tenant is null) { Status = "The tenant was not found."; IsLoaded = false; UpdateState(); return; }
      if (SelectedTenant?.cTenantId != tenant.cTenantId) { SelectedTenant = tenant; await Loading; }
      else await ReloadAsync();
      if (!IsLoaded) return;
      if (payload.EmployeeId is null) await NewEmployeeCommand();
      else {
         var row = _rows.FirstOrDefault(x => x.cTenantEmpId == payload.EmployeeId);
         if (row is null) { Status = "The employee was not found in this tenant."; Open(null); return; }
         Open(row); await PersonLoading;
      }
   }
}
