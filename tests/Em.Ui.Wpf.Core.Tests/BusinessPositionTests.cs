using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public class BusinessPositionTests
{
   private sealed class Positions : ITenantPositionServices
   {
      public List<vi_TenantPosition> Rows { get; } = [
         new() { cTenantPositionId = "A", cTenantId = "T", cTenantPositionCode = "ENG", cTenantPositionName = "Engineer" },
         new() { cTenantPositionId = "B", cTenantId = "T", cTenantPositionCode = "OLD", cTenantPositionName = "Old", cTenantPositionState = ta_TenantPosition.StateInactive },
         new() { cTenantPositionId = "C", cTenantId = "S", cTenantPositionCode = "ENG", cTenantPositionName = "Engineer" },
         new() { cTenantPositionId = "D", cTenantId = "S", cTenantPositionCode = "PM", cTenantPositionName = "Manager" },
         new() { cTenantPositionId = "E", cTenantId = "S", cTenantPositionCode = "OFF", cTenantPositionName = "Inactive", cTenantPositionState = ta_TenantPosition.StateInactive }];
      public TenantPositionCopy? LastCopy { get; private set; }
      public Task<vi_TenantPosition> PostGetTa_TenantPosition_New(ta_TenantPosition row) {
         var saved = new vi_TenantPosition { cTenantPositionId = "N", cTenantId = row.cTenantId, cTenantPositionCode = row.cTenantPositionCode, cTenantPositionName = row.cTenantPositionName };
         Rows.Add(saved); return Task.FromResult(saved);
      }
      public Task<vi_TenantPosition> PostGetTa_TenantPosition_Update(ta_TenantPosition row) {
         var saved = Rows.Single(x => x.cTenantPositionId == row.cTenantPositionId);
         saved.cTenantPositionName = row.cTenantPositionName; saved.cTenantPositionState = row.cTenantPositionState;
         return Task.FromResult(saved);
      }
      public Task<vi_TenantPosition[]> GetVi_TenantPositions(string tenantId) => Task.FromResult(Rows.Where(x => x.cTenantId == tenantId).ToArray());
      public Task<vi_TenantPosition[]> PostGetMeta_TenantPosition_Copy(TenantPositionCopy copy) { LastCopy = copy; return Task.FromResult<vi_TenantPosition[]>([Rows[0]]); }
   }

   [Fact]
   public async Task PositionPageCreatesEditsAndDeactivates() {
      var service = new Positions(); var vm = new BusinessModuleManagerPositionVm(service);
      vm.TenantChoices.Add(new() { cTenantId = "T" });
      vm.SelectedTenant = vm.TenantChoices[0]; await vm.Loading;
      Assert.Equal(2, vm.Rows.Count); Assert.False(vm.CopyFromTenantCommandAllowed());
      vm.NewPositionCommand();
      Assert.True(vm.IsDraftSelected); Assert.Equal(3, vm.Rows.Count); Assert.False(vm.SaveCommandAllowed());
      vm.EditCode = "NEW"; vm.EditName = "New one"; Assert.True(vm.SaveCommandAllowed());
      await vm.SaveCommand();
      Assert.False(vm.IsDirty); Assert.Equal("N", vm.SelectedRow!.cTenantPositionId);
      vm.SelectedRow = vm.Rows[0];
      Assert.Equal("Deactivate", vm.ToggleStateCaption);
      await vm.ToggleStateCommand();
      Assert.Equal("Activate", vm.ToggleStateCaption);
   }

   [Fact]
   public async Task UnsavedPositionBlocksSelectionWhenDiscardIsRefused() {
      var service = new Positions(); var vm = new BusinessModuleManagerPositionVm(service) { ConfirmDiscard = () => false };
      vm.TenantChoices.Add(new() { cTenantId = "T" });
      vm.SelectedTenant = vm.TenantChoices[0]; await vm.Loading;
      vm.NewPositionCommand();
      var draft = vm.SelectedRow;
      vm.SelectedRow = vm.Rows[0];
      Assert.Same(draft, vm.SelectedRow);
   }

   [WpfFact]
   public async Task CopyDialogOffersActiveSourcesAndLocksExistingCodes() {
      var service = new Positions();
      new CopyPositionsDialog().Close();
      var vm = new CopyPositionsDialogVm();
      var target = new vi_Tenant { cTenantId = "T", cTenantName = "Target" }; var source = new vi_Tenant { cTenantId = "S", cTenantName = "Source" };
      vm.Configure(service, target, [target, source], service.Rows.Where(x => x.cTenantId == "T"));
      await vm.Loading;
      Assert.Same(source, Assert.Single(vm.SourceTenants));
      Assert.Equal(["ENG", "PM"], vm.Items.Select(x => x.Position.cTenantPositionCode));
      Assert.True(vm.Items[0].Exists); Assert.False(vm.Items[0].CanCheck);
      Assert.False(vm.CopyCommandAllowed());
      vm.Items[1].IsChecked = true;
      Assert.Equal("Copy 1 position", vm.CopyCaption);
      Assert.Equal("1 selected · 1 already exist in this tenant", vm.SelectionCaption);
      var closed = false; vm.RequestClose += _ => closed = true;
      vm.CopyCommand();
      Assert.True(closed); Assert.Equal(["D"], vm.Result!.PositionIds); Assert.Equal("T", vm.Result.TargetTenantId);
   }

   private sealed class Files : IContactAttachmentServices
   {
      public List<string> Calls { get; } = [];
      private static ContactAttachmentInfo Info(string id, string name) => new() {
         Row = new() { cContactAttachmentId = id, cContactId = "C", cContactAttachmentName = name, cContactAttachmentKind = ContactAttachmentKind.Photo },
         FileName = name, Size = 2048, UploadedAt = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), Version = 1
      };
      public Task<ContactAttachmentInfo[]> GetMeta_ContactAttachments(string contactId) => Task.FromResult<ContactAttachmentInfo[]>([Info("F", "photo.jpg")]);
      public Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Add(ContactAttachmentUpload upload, Stream content) { Calls.Add("add " + upload.FileName); return Task.FromResult(Info("G", upload.FileName)); }
      public Task<ContactAttachmentInfo> PostGetMeta_ContactAttachment_Replace(ContactAttachmentUpload upload, Stream content) { Calls.Add("replace " + upload.AttachmentId); return Task.FromResult(Info(upload.AttachmentId, upload.FileName)); }
      public Task PostMeta_ContactAttachment_Remove(string attachmentId, DateTime ustamp) { Calls.Add("remove " + attachmentId); return Task.CompletedTask; }
      public Task<Stream> GetMeta_ContactAttachment_Download(string attachmentId) => Task.FromResult<Stream>(new MemoryStream([1]));
   }

   private sealed class Org : ITenantEmpServices, ITenantDeptServices
   {
      public TenantEmpSave? LastSave { get; private set; }
      private static readonly ta_Comm Comm = new() { cCommId = "M", cContactId = "C", cCommValue = "a@example.test", cCommState = ContactCommunicationState.ActiveAsDefault };
      private static readonly ta_Address Address = new() { cAddressId = "R", cContactId = "C", cAddressLocation = "Street", cAddressState = AddressState.ActiveAsDefault };
      public vi_TenantEmp[] Rows { get; } = [
         new() { cTenantEmpId = "E", cTenantId = "T", cContactId = "C", cTenantEmpCode = "E1", cvContactFullName = "Citra", cTenantDeptId = "D1", cTenantPositionId = "B", cvTenantEmpUserAccounts = "citra" },
         new() { cTenantEmpId = "H", cTenantId = "T", cContactId = "H", cTenantEmpCode = "H1", cvContactFullName = "Rina", cTenantDeptId = "D1", ccTenantEmpIsDeptHead = true }];
      public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) => Task.FromResult(Rows);
      public Task<vi_TenantEmp?> GetVi_TenantEmp_ById(string tenantEmpId) => Task.FromResult(Rows.FirstOrDefault(x => x.cTenantEmpId == tenantEmpId));
      public Task<TenantEmpPerson> GetMeta_TenantEmpPerson(string contactId) => Task.FromResult(new TenantEmpPerson {
         ContactId = contactId, FullName = "Citra", ContactType = ContactType.Female, Comms = [Comm], Addresses = [Address], DefaultCommId = "M", DefaultAddressId = "R"
      });
      public Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) => Task.FromResult<TenantEmpContactCandidate[]>([]);
      public Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) { LastSave = save; return Task.FromResult(Rows[0]); }
      public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) => Task.FromResult<vi_TenantEmp?>(null);
      public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) => Task.FromResult<string[]>([]);
      public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) => Task.FromResult<vi_TenantEmp?>(null);
      public Task<vi_TenantDept> PostGetTa_TenantDept_New(ta_TenantDept row) => throw new NotSupportedException();
      public Task<vi_TenantDept> PostGetTa_TenantDept_Update(ta_TenantDept row) => throw new NotSupportedException();
      public Task<vi_TenantDept[]> GetVi_TenantDepts(string tenantId) => Task.FromResult<vi_TenantDept[]>([
         new() { cTenantDeptId = "D1", cTenantDeptCode = "ACC", cTenantDeptName = "Accounting", cTenantDeptHead_cTenantEmpId = "H", cvTenantDeptHeadName = "Rina", cvTenantDeptHeadCode = "H1" },
         new() { cTenantDeptId = "D2", cTenantDeptCode = "OPS", cTenantDeptName = "Operations" }]);
   }

   private static async Task<(BusinessModuleManagerEmpVm Vm, Org Service, Files Files)> Open() {
      var org = new Org(); var files = new Files();
      var vm = new BusinessModuleManagerEmpVm(org, org, new Positions(), files) { IsEditorPage = true };
      vm.TenantChoices.Add(new() { cTenantId = "T" });
      await vm.LoadEditorAsync(Em.Ui.Wpf.Shared.EmployeeNavigationPayload.Open("T", "E"));
      return (vm, org, files);
   }

   [Fact]
   public async Task PositionComboOffersActivePositionsAndKeepsTheInactiveOneOfTheEmployee() {
      var (vm, service, _) = await Open();
      Assert.Equal([null, "A", "B"], vm.PositionOptions.Select(x => x.Id));
      Assert.EndsWith("- inactive", vm.EditPosition!.Caption);
      Assert.False(vm.IsDirty);
      vm.EditPosition = vm.PositionOptions[1];
      Assert.True(vm.IsDirty);
      await vm.SaveCommand();
      Assert.Equal("A", service.LastSave!.Row.cTenantPositionId);
   }

   [Fact]
   public async Task FullNameTracksDirtyStateRevertsAndSavesWithThePerson() {
      var (vm, service, _) = await Open();
      Assert.Equal("Citra", vm.EditContactName);
      Assert.Contains("citra", vm.LinkedUserCaption);
      vm.EditContactName = "Citra L"; Assert.True(vm.IsDirty); Assert.True(vm.RevertCommandAllowed());
      vm.RevertCommand(); await vm.PersonLoading;
      Assert.Equal("Citra", vm.EditContactName); Assert.False(vm.IsDirty);
      vm.EditContactName = " "; Assert.False(vm.SaveCommandAllowed());
      vm.EditContactName = "Citra L"; await vm.SaveCommand();
      Assert.Equal("Citra L", service.LastSave!.Person!.FullName);
   }

   [Fact]
   public async Task HeadCheckboxFollowsDepartmentAndAsksBeforeReplacing() {
      var (vm, service, _) = await Open();
      Assert.Equal("current head: Rina (H1)", vm.CurrentHeadCaption); Assert.True(vm.CanEditHead);
      string? asked = null; vm.ConfirmHeadReplace = head => { asked = head; return false; };
      vm.EditIsHead = true;
      await vm.SaveCommand();
      Assert.Equal("Rina (H1)", asked); Assert.Null(service.LastSave);
      vm.ConfirmHeadReplace = _ => true;
      await vm.SaveCommand();
      Assert.True(service.LastSave!.IsDeptHead);
      vm.EditDepartment = vm.DepartmentOptions[0];
      Assert.False(vm.CanEditHead); Assert.False(vm.EditIsHead);
      vm.EditDepartment = vm.DepartmentOptions[2];
      Assert.Equal("no current head", vm.CurrentHeadCaption);
   }

   [Fact]
   public async Task AttachmentEditsSaveWithTheEmployeeAndFileActionsApplyAtOnce() {
      var (vm, service, files) = await Open();
      Assert.Equal("Attachments (1)", vm.AttachmentsCaption);
      Assert.Equal("JPG · 2 KB · 02-Nov-26", vm.SelectedAttachment!.DetailCaption);
      Assert.False(vm.IsDirty);
      vm.SelectedAttachment.Note = "Passport photo"; Assert.True(vm.IsDirty);
      vm.SelectedAttachment.Name = " "; Assert.False(vm.SaveCommandAllowed());
      vm.SelectedAttachment.Name = "Photo"; await vm.SaveCommand();
      var row = Assert.Single(service.LastSave!.Person!.Attachments!);
      Assert.Equal("Passport photo", row.cContactAttachmentNote);

      vm.ConfirmRemoveAttachment = _ => false;
      await vm.RemoveAttachmentCommand();
      Assert.Empty(files.Calls.Where(x => x.StartsWith("remove")));
      vm.ConfirmRemoveAttachment = _ => true;
      await vm.RemoveAttachmentCommand();
      Assert.Equal("Attachments (0)", vm.AttachmentsCaption); Assert.Contains("remove F", files.Calls);
   }

   [Fact]
   public async Task AttachmentsWaitForASavedContact() {
      var org = new Org(); var vm = new BusinessModuleManagerEmpVm(org, org, new Positions(), new Files()) { IsEditorPage = true };
      vm.TenantChoices.Add(new() { cTenantId = "T" });
      await vm.LoadEditorAsync(Em.Ui.Wpf.Shared.EmployeeNavigationPayload.New("T"));
      vm.NewPerson = true;
      Assert.False(vm.AddAttachmentCommandAllowed()); Assert.NotEmpty(vm.AttachmentHint);
      Assert.Equal("Attachments (0)", vm.AttachmentsCaption);
   }

   [WpfFact]
   public void EditorXamlHasTheNewFieldsWithoutRendering() {
      var editor = new EmployeeEditor();
      Assert.IsType<System.Windows.Controls.ComboBox>(editor.FindName("positionPicker"));
      Assert.Equal(3, Assert.IsType<System.Windows.Controls.TabControl>(editor.FindName("personTabs")).Items.Count);
   }
}
