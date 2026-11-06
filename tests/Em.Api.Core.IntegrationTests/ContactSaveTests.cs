using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public sealed class ContactSaveTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
   private ContactServices Svc() => new(Db());
   private static ta_Comm Comm(string value = "a@example.test") => new() { cCommId = Ulid.NewUlid().ToString(), cCommValue = value, cCommState = ContactCommunicationState.Active };
   private static ta_Address Address(string value = "Street") => new() { cAddressId = Ulid.NewUlid().ToString(), cAddressLocation = value, cAddressState = AddressState.Active };
   private Task<ta_Comm[]> Comms(string id) => Svc().GetTa_Comms_ByContactId(id);

   [Fact]
   public async Task CreatesAContactWithItsDefaultCommAtomically() {
      var comm = Comm();
      var saved = await Svc().PostGetMeta_Contact_Save(new() { FullName = "Contact one", ContactType = ContactType.Female, Comms = [comm], DefaultCommId = comm.cCommId });
      Assert.Equal(comm.cCommId, saved.cContactDefaultComm_cCommId);
      Assert.Equal(ContactCommunicationState.ActiveAsDefault, (await Comms(saved.cContactId)).Single().cCommState);
   }

   [Fact]
   public async Task ADraftIdCanBeRepeatedWithTheReturnedStamps() {
      var id = Ulid.NewUlid().ToString(); var comm = Comm();
      var first = await Svc().PostGetMeta_Contact_Save(new() { ContactId = id, FullName = "Draft", ContactType = ContactType.Male, Comms = [comm], DefaultCommId = comm.cCommId });
      Assert.Equal(id, first.cContactId);
      await Status(409, () => Svc().PostGetMeta_Contact_Save(new() { ContactId = id, FullName = "Draft", ContactType = ContactType.Male }));
      var stored = await Comms(id); stored[0].cCommValue = "b@example.test";
      var second = await Svc().PostGetMeta_Contact_Save(new() { ContactId = id, ContactUstamp = first.ustamp, FullName = "Draft two", ContactType = ContactType.Male, Comms = stored });
      Assert.Equal("Draft two", second.cContactFullName); Assert.Equal(comm.cCommId, second.cContactDefaultComm_cCommId);
      Assert.Equal("b@example.test", (await Comms(id)).Single().cCommValue);
      await Status(409, () => Svc().PostGetMeta_Contact_Save(new() { ContactId = id, ContactUstamp = first.ustamp, FullName = "Stale", ContactType = ContactType.Male }));
   }

   [Fact]
   public async Task InvalidInputAndDefaultsAreRefusedWithoutPartialWrites() {
      var before = await Db().ta_Contacts.CountAsync(TestContext.Current.CancellationToken);
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = " ", ContactType = ContactType.Male }));
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "x", ContactType = (ContactType)99 }));
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { ContactId = "not-a-ulid", FullName = "x", ContactType = ContactType.Male }));
      var empty = Comm(" ");
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "x", ContactType = ContactType.Male, Comms = [empty], DefaultCommId = empty.cCommId }));
      var inactive = Comm(); inactive.cCommState = ContactCommunicationState.Inactive;
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "x", ContactType = ContactType.Male, Comms = [inactive], DefaultCommId = inactive.cCommId }));
      var inactiveAddress = Address(); inactiveAddress.cAddressState = AddressState.Inactive;
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "x", ContactType = ContactType.Male, Addresses = [inactiveAddress], DefaultAddressId = inactiveAddress.cAddressId }));
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "x", ContactType = ContactType.Male, DefaultCommId = Ulid.NewUlid().ToString() }));
      var comm = Comm();
      var owner = await Svc().PostGetMeta_Contact_Save(new() { FullName = "Owner", ContactType = ContactType.Male, Comms = [comm], DefaultCommId = comm.cCommId });
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "Foreign", ContactType = ContactType.Male, DefaultCommId = comm.cCommId }));
      var foreign = (await Comms(owner.cContactId)).Single();
      await Status(400, () => Svc().PostGetMeta_Contact_Save(new() { FullName = "Foreign", ContactType = ContactType.Male, Comms = [foreign] }));
      Assert.Equal(before + 1, await Db().ta_Contacts.CountAsync(TestContext.Current.CancellationToken));
   }

   [Fact]
   public async Task EntriesNotSentStayAndAStoredEntryCanBecomeTheDefault() {
      var first = Comm("first@example.test"); var second = Comm("second@example.test"); var address = Address();
      var saved = await Svc().PostGetMeta_Contact_Save(new() { FullName = "Keeper", ContactType = ContactType.Male, Comms = [first, second], Addresses = [address], DefaultCommId = first.cCommId, DefaultAddressId = address.cAddressId });
      saved = await Svc().PostGetMeta_Contact_Save(new() { ContactId = saved.cContactId, ContactUstamp = saved.ustamp, FullName = "Keeper", ContactType = ContactType.Male, Note = "Note" });
      Assert.Equal(first.cCommId, saved.cContactDefaultComm_cCommId); Assert.Equal(address.cAddressId, saved.cContactDefaultAddress_cAddressId);
      Assert.Equal(2, (await Comms(saved.cContactId)).Length);
      saved = await Svc().PostGetMeta_Contact_Save(new() { ContactId = saved.cContactId, ContactUstamp = saved.ustamp, FullName = "Keeper", ContactType = ContactType.Male, DefaultCommId = second.cCommId });
      var comms = await Comms(saved.cContactId);
      Assert.Equal(second.cCommId, saved.cContactDefaultComm_cCommId);
      Assert.Equal(ContactCommunicationState.ActiveAsDefault, comms.Single(x => x.cCommId == second.cCommId).cCommState);
      Assert.Equal(ContactCommunicationState.Active, comms.Single(x => x.cCommId == first.cCommId).cCommState);
   }

   [Fact]
   public async Task AFailedSaveRollsEverythingBack() {
      var comm = Comm();
      var saved = await Svc().PostGetMeta_Contact_Save(new() { FullName = "Rollback", ContactType = ContactType.Male, Comms = [comm], DefaultCommId = comm.cCommId });
      var stored = (await Comms(saved.cContactId)).Single(); stored.cCommValue = "changed@example.test"; stored.ustamp = stored.ustamp.AddDays(-1);
      var added = Comm("added@example.test");
      await Status(409, () => Svc().PostGetMeta_Contact_Save(new() { ContactId = saved.cContactId, ContactUstamp = saved.ustamp, FullName = "Changed", ContactType = ContactType.Male, Comms = [added, stored] }));
      Assert.Equal("Rollback", (await Svc().GetVi_Contact_ById(saved.cContactId))!.cContactFullName);
      Assert.Equal("a@example.test", (await Comms(saved.cContactId)).Single().cCommValue);
   }

   [Fact]
   public async Task EmployeeSaveStillRequiresDefaultsAndGetByIdReturnsTheRow() {
      var tenant = await Tenant("contact-save-regression"); var employee = await Employee(tenant.cTenantId, "regression");
      var read = await Emp().GetVi_TenantEmp_ById(employee.cTenantEmpId);
      Assert.Equal(employee.cTenantEmpCode, read!.cTenantEmpCode);
      Assert.Null(await Emp().GetVi_TenantEmp_ById(Ulid.NewUlid().ToString()));
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() {
         Row = new() { cTenantId = tenant.cTenantId, cTenantEmpCode = "no-default" }, NewContactName = "No default", NewContactType = ContactType.Male
      }));
   }
}
