using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public sealed class TenantEmpServicesTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
   [Fact]
   public async Task ContactDialogSearchPagesAllResultsAndIncludesStatusAndLinkedUsernames() {
      var t = await Tenant("contact-page"); var db = Db(); var now = DateTime.UtcNow;
      var ids = new List<string>();
      for (var i = 0; i < 105; i++) {
         var id = Ulid.NewUlid().ToString(); ids.Add(id);
         db.ta_Contacts.Add(new() { cContactId = id, cContactFullName = "Paged " + i.ToString("D3", System.Globalization.CultureInfo.GetCultureInfo("en-US")), cContactType = ContactType.Male, cContactState = i == 0 ? ContactState.Inactive : ContactState.Active, ustamp = now, datestamp = now });
      }
      db.ta_Users.Add(new() { cUserId = Ulid.NewUlid().ToString(), cContactId = ids[0], cUserAccount = "inactive-linked-user", cUserState = UserState.Inactive, ustamp = now, datestamp = now });
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
      Task<ContactSearchPage> Search(string text = "Paged", int page = 1, ContactSearchFilter? filter = null) => new ContactServices(Db()).PostGetMeta_ContactSearchPage(new() { Search = text, Page = page, Filter = filter ?? new() });
      var first = await Search();
      var second = await Search(page: 2);
      var last = await Search(page: 3);
      Assert.Equal(50, first.Rows.Length); Assert.Equal(50, second.Rows.Length); Assert.Equal(5, last.Rows.Length);
      Assert.True(first.HasMore); Assert.True(second.HasMore); Assert.False(last.HasMore);
      Assert.Equal(105, first.Rows.Concat(second.Rows).Concat(last.Rows).Select(x => x.ContactId).Distinct().Count());
      Assert.Equal(ContactState.Inactive, first.Rows[0].ContactState); Assert.Equal(["inactive-linked-user"], first.Rows[0].UserAccounts);
      Assert.Single((await Search("inactive-linked-user")).Rows);
      Assert.Empty((await Emp().GetMeta_TenantEmpContacts(t.cTenantId, "inactive-linked-user")));
      var filtered = await Search(filter: new() { PeopleOnly = true, State = ContactState.Active, ExcludedContactIds = ids.Take(60).ToArray() });
      Assert.Equal(45, filtered.Rows.Length); Assert.False(filtered.HasMore);
      Assert.DoesNotContain(filtered.Rows, x => ids.Take(60).Contains(x.ContactId));
      Assert.Single((await Search(filter: new() { HasUser = true })).Rows);
      Assert.Empty((await Search(filter: new() { HasUser = true, ActiveUsersOnly = true })).Rows);
      Assert.Equal(50, (await Search(filter: new() { HasUser = false })).Rows.Length);
      Assert.Empty((await Search(filter: new() { Type = ContactType.Organization })).Rows);
      Assert.Single((await Search(filter: new() { State = ContactState.Inactive })).Rows);
      db.ta_Contacts.Add(new() { cContactId = Ulid.NewUlid().ToString(), cContactFullName = "Paged organization", cContactType = ContactType.Organization, cContactState = ContactState.Active, ustamp = now, datestamp = now });
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
      Assert.Single((await Search("Paged organization")).Rows);
      Assert.Empty((await Search("Paged organization", filter: new() { PeopleOnly = true })).Rows);
      Assert.Single((await Search("Paged organization", filter: new() { Type = ContactType.Organization })).Rows);
      await Status(400, () => Search(page: 0));
   }
   [Fact]
   public async Task NewPersonCreatesLinkedDefaultCommAndAddressAndReadsThemBack() {
      var t = await Tenant("defaults");
      var e = await Emp().PostGetMeta_TenantEmpSave(new() {
         Row = new() { cTenantId = t.cTenantId, cTenantEmpCode = "defaults" }, NewContactName = "New Person",
         Person = new() {
            Comm = new() { cCommType = CommunationType.MobilePhone, cCommValue = "+1 555 0100", cCommNote = "Mobile" },
            Address = new() { cAddressName = "Home", cAddressLocation = "10 Example Street", cAddressZip = "12345" }
         }
      });
      var details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      Assert.Equal(e.cContactId, details.Comm!.cContactId); Assert.Equal(e.cContactId, details.Address!.cContactId);
      Assert.Equal("+1 555 0100", details.Comm.cCommValue); Assert.Equal("10 Example Street", details.Address.cAddressLocation);
      Assert.Equal(ContactCommunicationState.ActiveAsDefault, details.Comm.cCommState);
      Assert.Equal(AddressState.ActiveAsDefault, details.Address.cAddressState);
      var contact = await Db().ta_Contacts.AsNoTracking().SingleAsync(x => x.cContactId == e.cContactId, TestContext.Current.CancellationToken);
      Assert.Equal(details.Comm.cCommId, contact.cContactDefaultComm_cCommId);
      Assert.Equal(details.Address.cAddressId, contact.cContactDefaultAddress_cAddressId);
   }
   [Fact]
   public async Task ExistingUsernameUsesSameDefaultsAndEditsThemWithoutDuplicates() {
      var t = await Tenant("original"); var t2 = await Tenant("new-tenant");
      var e = await Employee(t.cTenantId, "original");
      var details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      var now = DateTime.UtcNow; var db = Db();
      db.ta_Users.Add(new() { cUserId = Ulid.NewUlid().ToString(), cContactId = e.cContactId, cUserAccount = "existing-person", cUserState = UserState.Active, ustamp = now, datestamp = now });
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
      var candidate = Assert.Single(await Emp().GetMeta_TenantEmpContacts(t2.cTenantId, "existing-person"));
      Assert.Equal(e.cContactId, candidate.ContactId);
      details = await Emp().GetMeta_TenantEmpPerson(candidate.ContactId);
      var commId = details.Comm!.cCommId; var addressId = details.Address!.cAddressId;
      details.Comm.cCommValue = "person@example.test"; details.Address.cAddressLocation = "Shared address";
      var saved = await Emp().PostGetMeta_TenantEmpSave(new() { Row = new() { cTenantId = t2.cTenantId, cTenantEmpCode = "shared", cContactId = candidate.ContactId }, Person = details });
      Assert.Equal(e.cContactId, saved.cContactId);
      var updated = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      Assert.Equal(commId, updated.Comm!.cCommId); Assert.Equal(addressId, updated.Address!.cAddressId);
      Assert.Equal("person@example.test", updated.Comm.cCommValue); Assert.Equal("Shared address", updated.Address.cAddressLocation);
      Assert.Equal(1, await Db().ta_Comms.CountAsync(x => x.cContactId == e.cContactId, TestContext.Current.CancellationToken));
      Assert.Equal(1, await Db().ta_Addresses.CountAsync(x => x.cContactId == e.cContactId, TestContext.Current.CancellationToken));
   }
   [Fact]
   public async Task StaleOrForeignPersonDetailsAreRejectedAndDuplicateSaveRollsBackAllDefaults() {
      var t = await Tenant("guard"); var e = await Employee(t.cTenantId, "head"); var other = await Employee(t.cTenantId, "occupied");
      var details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      var stale = Copy<TenantEmpPerson>(details);
      details.Comm!.cCommValue = "current@example.test";
      e = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(e), Person = details });
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(e), Person = stale }));
      var foreign = await Emp().GetMeta_TenantEmpPerson(other.cContactId);
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(e), Person = foreign }));
      details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      details.Comm!.cCommValue = "rollback@example.test"; details.Address!.cAddressLocation = "Rollback address";
      var duplicate = Copy<ta_TenantEmp>(e); duplicate.cTenantEmpCode = "occupied";
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = duplicate, Person = details }));
      var kept = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      Assert.Equal("current@example.test", kept.Comm!.cCommValue); Assert.NotEqual("Rollback address", kept.Address!.cAddressLocation);
      var commsBefore = await Db().ta_Comms.CountAsync(TestContext.Current.CancellationToken);
      var addressesBefore = await Db().ta_Addresses.CountAsync(TestContext.Current.CancellationToken);
      await Status(409, () => Employee(t.cTenantId, "occupied"));
      Assert.Equal(commsBefore, await Db().ta_Comms.CountAsync(TestContext.Current.CancellationToken));
      Assert.Equal(addressesBefore, await Db().ta_Addresses.CountAsync(TestContext.Current.CancellationToken));
   }
   [Fact]
   public async Task MissingDefaultsCanBeCreatedAndIndependentDefaultEditsAreDetected() {
      var t = await Tenant("missing"); var e = await Employee(t.cTenantId, "missing");
      var details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      await Db().ta_Contacts.Where(x => x.cContactId == e.cContactId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cContactDefaultComm_cCommId, (string?)null).SetProperty(x => x.cContactDefaultAddress_cAddressId, (string?)null), TestContext.Current.CancellationToken);
      details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      Assert.Null(details.Comm); Assert.Null(details.Address);
      details.Comm = new() { cCommValue = "missing@example.test" }; details.Address = new() { cAddressLocation = "Recovered defaults" };
      e = await Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(e), Person = details });
      details = await Emp().GetMeta_TenantEmpPerson(e.cContactId);
      Assert.Equal("Recovered defaults", details.Address!.cAddressLocation);
      await Db().ta_Comms.Where(x => x.cCommId == details.Comm!.cCommId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ustamp, DateTime.UtcNow.AddSeconds(1)), TestContext.Current.CancellationToken);
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = Copy<ta_TenantEmp>(e), Person = details }));
   }
   [Fact]
   public async Task CreatesPeopleAndEnforcesTenantUniquenessAndIdentity() {
      var t = await Tenant("one"); var t2 = await Tenant("two"); var d = await Department(t.cTenantId, "dept");
      var e = await Employee(t.cTenantId, "new", d.cTenantDeptId);
      Assert.Equal("Person new", e.cvContactFullName); Assert.Equal(ContactType.Female, e.cvContactType);
      await Status(409, () => Employee(t.cTenantId, "another", contact: e.cContactId));
      await Employee(t2.cTenantId, "existing", contact: e.cContactId);
      await Status(409, () => Employee(t.cTenantId, "new"));
      await Status(400, () => Employee(t2.cTenantId, "wrong-dept", d.cTenantDeptId));
      var change = Copy<ta_TenantEmp>(e); change.cTenantId = t2.cTenantId;
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
      change = Copy<ta_TenantEmp>(e); change.cContactId = Ulid.NewUlid().ToString();
      await Status(400, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
      change = Copy<ta_TenantEmp>(e); change.cTenantEmpPosition = "Manager";
      var saved = await Emp().PostGetMeta_TenantEmpSave(new() { Row = change });
      Assert.NotEqual(e.ustamp, saved.ustamp);
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
   }
   [Fact]
   public async Task TransfersAndDeactivationsClearHeadsAndMoveDepartmentStamp() {
      var t = await Tenant("transfer"); var a = await Department(t.cTenantId, "a"); var b = await Department(t.cTenantId, "b");
      var e = await Employee(t.cTenantId, "head", a.cTenantDeptId);
      a = await Head(a, e);
      Assert.Equal(e.cTenantEmpId, (await Emp().GetMeta_TenantEmpHead(e.cTenantEmpId))!.cTenantEmpId);
      var change = Copy<ta_TenantEmp>(e); change.cTenantDeptId = b.cTenantDeptId;
      e = await Emp().PostGetMeta_TenantEmpSave(new() { Row = change });
      var cleared = (await Dept().GetVi_TenantDepts(t.cTenantId)).Single(x => x.cTenantDeptId == a.cTenantDeptId);
      Assert.Null(cleared.cTenantDeptHead_cTenantEmpId); Assert.NotEqual(a.ustamp, cleared.ustamp);
      await Status(409, () => Dept().PostGetTa_TenantDept_Update(Copy<ta_TenantDept>(a)));
      b = await Head(b, e); change = Copy<ta_TenantEmp>(e); change.cTenantEmpState = -1;
      await Emp().PostGetMeta_TenantEmpSave(new() { Row = change });
      Assert.Null((await Dept().GetVi_TenantDepts(t.cTenantId)).Single(x => x.cTenantDeptId == b.cTenantDeptId).cTenantDeptHead_cTenantEmpId);
      Assert.Null(await Emp().GetMeta_TenantEmpHead(e.cTenantEmpId));
   }
   [Fact]
   public async Task CandidateSearchAndLookupsUseActiveAccountsAndNeverWalkParents() {
      var t = await Tenant("lookup"); var parent = await Department(t.cTenantId, "parent"); var child = await Department(t.cTenantId, "child", parent.cTenantDeptId);
      var parentHead = await Employee(t.cTenantId, "parent-head", parent.cTenantDeptId);
      await Head(parent, parentHead);
      var e = await Employee(t.cTenantId, "employee", child.cTenantDeptId);
      var db = Db(); var now = DateTime.UtcNow;
      var user = new ta_User { cUserId = Ulid.NewUlid().ToString(), cContactId = e.cContactId, cUserAccount = "search-account", cUserState = UserState.Active, datestamp = now, ustamp = now };
      db.ta_Users.Add(user);
      db.ta_Users.Add(new() { cUserId = Ulid.NewUlid().ToString(), cContactId = e.cContactId, cUserAccount = "inactive", cUserState = UserState.Inactive, datestamp = now, ustamp = now });
      db.ta_Contacts.Add(new() { cContactId = Ulid.NewUlid().ToString(), cContactFullName = "Organization", cContactType = ContactType.Organization, datestamp = now, ustamp = now });
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
      var candidates = await Emp().GetMeta_TenantEmpContacts(t.cTenantId, "search-account");
      var person = Assert.Single(candidates); Assert.Equal(e.cTenantEmpId, person.TenantEmpId); Assert.Equal(["search-account"], person.UserAccounts);
      Assert.Single(await Emp().GetMeta_TenantEmpContacts(t.cTenantId, "Person employee"));
      Assert.DoesNotContain(await Emp().GetMeta_TenantEmpContacts(t.cTenantId, ""), x => x.ContactType == ContactType.Organization);
      Assert.Equal(e.cTenantEmpId, (await Emp().GetMeta_TenantEmpByUser(t.cTenantId, user.cUserId))!.cTenantEmpId);
      Assert.Equal([user.cUserId], await Emp().GetMeta_TenantEmpUsers(e.cTenantEmpId));
      Assert.Null(await Emp().GetMeta_TenantEmpHead(e.cTenantEmpId));
      var change = Copy<ta_TenantEmp>(e); change.cTenantEmpState = -1;
      await Emp().PostGetMeta_TenantEmpSave(new() { Row = change });
      Assert.Null(await Emp().GetMeta_TenantEmpByUser(t.cTenantId, user.cUserId));
   }
   [Fact]
   public async Task CandidateLimitAndMultipleActiveUsers() {
      var t = await Tenant("many"); var e = await Employee(t.cTenantId, "shared");
      var db = Db(); var now = DateTime.UtcNow;
      for (var i = 0; i < 55; i++) db.ta_Contacts.Add(new() {
         cContactId = Ulid.NewUlid().ToString(), cContactFullName = "Candidate " + i.ToString("D2", System.Globalization.CultureInfo.GetCultureInfo("en-US")),
         cContactType = ContactType.Male, ustamp = now, datestamp = now
      });
      for (var i = 0; i < 2; i++) db.ta_Users.Add(new() {
         cUserId = Ulid.NewUlid().ToString(), cUserAccount = "shared-" + i.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")),
         cContactId = e.cContactId, cUserState = UserState.Active, ustamp = now, datestamp = now
      });
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
      var candidates = await Emp().GetMeta_TenantEmpContacts(t.cTenantId, "Candidate");
      Assert.Equal(50, candidates.Length); Assert.Equal("Candidate 00", candidates[0].FullName); Assert.Equal("Candidate 49", candidates[^1].FullName);
      Assert.Equal(2, (await Emp().GetMeta_TenantEmpUsers(e.cTenantEmpId)).Length);
      Assert.Equal(2, (await Emp().GetMeta_TenantEmpContacts(t.cTenantId, "shared-")).Single().UserAccounts.Length);
   }
   [Fact]
   public async Task FailedSaveRollsBackPersonAndHeadChanges() {
      var t = await Tenant("rollback"); var d = await Department(t.cTenantId, "d");
      var e = await Employee(t.cTenantId, "head", d.cTenantDeptId); await Employee(t.cTenantId, "occupied"); d = await Head(d, e);
      var before = await Db().ta_Contacts.CountAsync(TestContext.Current.CancellationToken);
      await Status(409, () => Employee(t.cTenantId, "occupied"));
      Assert.Equal(before, await Db().ta_Contacts.CountAsync(TestContext.Current.CancellationToken));
      var change = Copy<ta_TenantEmp>(e); change.cTenantEmpCode = "occupied"; change.cTenantEmpState = -1;
      await Status(409, () => Emp().PostGetMeta_TenantEmpSave(new() { Row = change }));
      Assert.Equal(e.cTenantEmpId, (await Dept().GetVi_TenantDepts(t.cTenantId)).Single().cTenantDeptHead_cTenantEmpId);
   }
}
