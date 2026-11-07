using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

public sealed partial class BusinessModuleManagerEmpVm
{
   private string _personSnapshot = "";
   private readonly List<string> _openedFolders = [];
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
   /// <summary>Editable full name of the contact, shared across all its uses.</summary>
   public string EditContactName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }
   /// <summary>Whether the contact name can be saved.</summary>
   public bool ContactNameValid => !ShowPersonDetails || !string.IsNullOrWhiteSpace(EditContactName);
   /// <summary>The linked user accounts, which are read-only here.</summary>
   public string LinkedUserCaption {
      get {
         var accounts = IsSaved ? SelectedRow!.cvTenantEmpUserAccounts : string.Join(", ", SelectedCandidate?.UserAccounts ?? []);
         return string.IsNullOrEmpty(accounts) ? "No linked user" : $"Linked user: {accounts} · read-only, managed in User Manager";
      }
   }
   /// <summary>Whether a draft has no contact yet.</summary>
   public bool ShowNoContact => ShowExistingPerson && SelectedCandidate is null;
   /// <summary>Host confirmation before replacing the head of the selected department; receives the current head.</summary>
   public Func<string, bool>? ConfirmHeadReplace { get; set; }
   /// <summary>Whether the head checkbox accepts input: it needs a department and an active employee.</summary>
   public bool CanEditHead => CanEdit && EditDepartment?.Id is not null && SelectedRow?.cTenantEmpState == ta_TenantEmp.StateActive;
   /// <summary>Who heads the selected department now.</summary>
   public string CurrentHeadCaption => EditDepartment?.Id is null ? "" : EditDepartment.HeadCaption is { } head ? "current head: " + head : "no current head";
   private bool ReplacesHead => EditIsHead && EditDepartment?.HeadId is { } head && !string.Equals(head, SelectedRow?.cTenantEmpId, StringComparison.OrdinalIgnoreCase);
   /// <summary>Caption of the Communications tab.</summary>
   public string CommunicationsCaption => $"Communications ({Communications.Count})";
   /// <summary>Caption of the Addresses tab.</summary>
   public string AddressesCaption => $"Addresses ({Addresses.Count})";
   /// <summary>Caption of the Attachments tab.</summary>
   public string AttachmentsCaption => $"Attachments ({Attachments.Count})";
   /// <summary>Attachment kinds shown by their readable captions.</summary>
   public EmployeeAttachmentKindOption[] AttachmentKinds => EmployeeAttachmentVm.Kinds;
   /// <summary>Active attachments of the shared contact.</summary>
   public ObservableCollection<EmployeeAttachmentVm> Attachments { get; } = [];
   /// <summary>Attachment selected for editing.</summary>
   public EmployeeAttachmentVm? SelectedAttachment { get => Get<EmployeeAttachmentVm>(); set => Set(value, _ => UpdateState()); }
   /// <summary>Whether an attachment is selected.</summary>
   public bool HasSelectedAttachment => SelectedAttachment is not null;
   /// <summary>Whether files can be managed: the contact must exist on the server.</summary>
   public bool CanManageAttachments => CanEdit && !NewPerson && _person is not null;
   /// <summary>Explains why a new contact cannot have files yet.</summary>
   public string AttachmentHint => NewPerson ? "Save the employee first to add attachments to the new contact." : "";
   /// <summary>Host dialog that picks a local file; <c>null</c> = cancelled.</summary>
   public Func<string?>? PickAttachmentFile { get; set; }
   /// <summary>Host action that opens a local copy of an attachment.</summary>
   public Action<string>? OpenLocalFile { get; set; }
   /// <summary>Host confirmation before removing an attachment.</summary>
   public Func<EmployeeAttachmentVm, bool>? ConfirmRemoveAttachment { get; set; }
   /// <summary>Whether a file can be attached.</summary>
   public bool AddAttachmentCommandAllowed() => CanManageAttachments;
   /// <summary>Uploads a file as a new attachment of the contact.</summary>
   public Task AddAttachmentCommand() {
      if (!AddAttachmentCommandAllowed() || PickAttachmentFile?.Invoke() is not { } path || AttachmentService is not { } service) return Task.CompletedTask;
      return RunAsync(async () => {
         await using var content = File.OpenRead(path);
         var saved = await service.PostGetMeta_ContactAttachment_Add(new() { ContactId = _person!.ContactId, FileName = Path.GetFileName(path) }, content);
         var item = Watch(new EmployeeAttachmentVm(saved));
         Attachments.Add(item); SelectedAttachment = item;
         Status = "Attachment added.";
      });
   }
   /// <summary>Whether the selected attachment can be opened.</summary>
   public bool OpenAttachmentCommandAllowed() => CanEdit && SelectedAttachment is not null;
   /// <summary>Downloads a temporary copy of the selected attachment and opens it.</summary>
   public Task OpenAttachmentCommand() {
      if (!OpenAttachmentCommandAllowed() || SelectedAttachment is not { } item || AttachmentService is not { } service) return Task.CompletedTask;
      return RunAsync(async () => {
         var folder = Path.Combine(Path.GetTempPath(), "em-attachments", Ulid.NewUlid().ToString());
         Directory.CreateDirectory(folder); _openedFolders.Add(folder);
         var path = Path.Combine(folder, Path.GetFileName(item.FileName));
         await using (var source = await service.GetMeta_ContactAttachment_Download(item.Id))
         await using (var target = File.Create(path)) await source.CopyToAsync(target);
         OpenLocalFile?.Invoke(path);
      });
   }
   /// <summary>Deletes the temporary copies; a file still open elsewhere stays until the system clears it.</summary>
   public void ReleaseOpenedFiles() {
      foreach (var folder in _openedFolders.ToArray()) {
         try { Directory.Delete(folder, true); _openedFolders.Remove(folder); }
         catch (IOException) { }
         catch (UnauthorizedAccessException) { }
      }
   }
   /// <summary>Whether the selected attachment's file can be replaced.</summary>
   public bool ReplaceAttachmentCommandAllowed() => CanManageAttachments && SelectedAttachment is not null;
   /// <summary>Uploads a new version of the selected attachment's file.</summary>
   public Task ReplaceAttachmentCommand() {
      if (!ReplaceAttachmentCommandAllowed() || SelectedAttachment is not { } item || PickAttachmentFile?.Invoke() is not { } path || AttachmentService is not { } service) return Task.CompletedTask;
      return RunAsync(async () => {
         await using var content = File.OpenRead(path);
         item.ApplyFile(await service.PostGetMeta_ContactAttachment_Replace(new() { AttachmentId = item.Id, Ustamp = item.Ustamp, FileName = Path.GetFileName(path) }, content));
         Status = "Attachment file replaced.";
      });
   }
   /// <summary>Whether the selected attachment can be removed.</summary>
   public bool RemoveAttachmentCommandAllowed() => CanManageAttachments && SelectedAttachment is not null;
   /// <summary>Hides the selected attachment; the file stays in object storage.</summary>
   public Task RemoveAttachmentCommand() {
      if (!RemoveAttachmentCommandAllowed() || SelectedAttachment is not { } item || ConfirmRemoveAttachment?.Invoke(item) == false || AttachmentService is not { } service) return Task.CompletedTask;
      return RunAsync(async () => {
         await service.PostMeta_ContactAttachment_Remove(item.Id, item.Ustamp);
         Attachments.Remove(item); SelectedAttachment = Attachments.FirstOrDefault();
         Status = "Attachment removed.";
      });
   }
   /// <summary>Whether the editor can return to the saved values.</summary>
   public bool RevertCommandAllowed() => CanEdit && IsDirty;
   /// <summary>Discards the unsaved changes after confirmation.</summary>
   public void RevertCommand() {
      if (!RevertCommandAllowed() || ConfirmDiscard?.Invoke() == false) return;
      Open(IsDraft ? new vi_TenantEmp { cTenantId = SelectedRow!.cTenantId } : SelectedRow);
      Status = "";
   }
   private void FillPositionOptions(vi_TenantEmp? row) {
      PositionOptions.Clear(); PositionOptions.Add(new(null, "(none)"));
      foreach (var p in _positionRows.Where(p => p.cTenantPositionState == ta_TenantPosition.StateActive || p.cTenantPositionId == row?.cTenantPositionId))
         PositionOptions.Add(new(p.cTenantPositionId, p.cTenantPositionState == ta_TenantPosition.StateActive ? $"{p.cTenantPositionName} ({p.cTenantPositionCode})" : $"{p.cTenantPositionName} ({p.cTenantPositionCode}) - inactive"));
   }
   private TenantEmpPerson BuildSavePerson() {
      var person = BuildPerson();
      person.Attachments = [.. Attachments.Where(x => x.IsDirty).Select(x => x.ToRow())];
      return person;
   }
   /// <summary>Whether both defaults are selected, active and contain their required content.</summary>
   public bool DefaultsValid => DefaultComm is not null && Communications.Contains(DefaultComm) && DefaultComm.State != ContactCommunicationState.Inactive &&
      !string.IsNullOrWhiteSpace(DefaultComm.Value) && DefaultAddress is not null && Addresses.Contains(DefaultAddress) && DefaultAddress.State != AddressState.Inactive && !string.IsNullOrWhiteSpace(DefaultAddress.Location);
   /// <summary>Explains the required defaults before saving.</summary>
   public string DefaultGuardMessage => !ContactNameValid ? "Enter the contact full name." : ShowPersonDetails && !DefaultsValid ? "Choose a default communication with a value and a default address with a location. Both must be active." : "";
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
