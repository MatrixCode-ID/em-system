using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

public sealed class TenantEmpServicesTests(SqlServerDatabase database) : TenantOrgTestsBase(database)
{
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
