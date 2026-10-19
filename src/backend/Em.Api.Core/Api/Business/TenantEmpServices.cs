using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Employee saves, person candidates and contact-based user lookups.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantEmpServices(ApiCoreContext ctx) : ServicesBase, ITenantEmpServices
{
   #region Views
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) => ctx.vi_TenantEmps.AsNoTracking()
      .Where(x => x.cTenantId == tenantId).OrderBy(x => x.cvContactFullName).ThenBy(x => x.cTenantEmpCode).ThenBy(x => x.cTenantEmpId).ToArrayAsync(AbortToken);
   #endregion

   #region Meta's
   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) {
      var query = ctx.ta_Contacts.AsNoTracking().Where(x => x.cContactType != ContactType.Organization);
      if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.cContactFullName.Contains(search) || ctx.ta_Users.Any(u => u.cContactId == x.cContactId && u.cUserState == UserState.Active && u.cUserAccount.Contains(search)));
      var people = await query.OrderBy(x => x.cContactFullName).ThenBy(x => x.cContactId).Take(50).ToArrayAsync(AbortToken);
      var ids = people.Select(x => x.cContactId).ToArray();
      var users = await ctx.ta_Users.AsNoTracking().Where(u => ids.Contains(u.cContactId) && u.cUserState == UserState.Active).OrderBy(u => u.cUserAccount).ToArrayAsync(AbortToken);
      var employees = await ctx.ta_TenantEmps.AsNoTracking().Where(e => ids.Contains(e.cContactId) && e.cTenantId == tenantId).ToArrayAsync(AbortToken);
      return people.Select(c => new TenantEmpContactCandidate {
         ContactId = c.cContactId, FullName = c.cContactFullName, ContactType = c.cContactType,
         UserAccounts = users.Where(u => TenantOrg.Same(u.cContactId, c.cContactId)).Select(u => u.cUserAccount).ToArray(),
         TenantEmpId = employees.FirstOrDefault(e => TenantOrg.Same(e.cContactId, c.cContactId))?.cTenantEmpId
      }).ToArray();
   }

   /// <inheritdoc />
   [PostAction(claim: ITenantServices.ManagerClaim)]
   public async Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) {
      var row = save.Row;
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var creating = string.IsNullOrWhiteSpace(row.cTenantEmpId);
      var current = creating ? null : await ctx.ta_TenantEmps.AsNoTracking().SingleOrDefaultAsync(x => x.cTenantEmpId == row.cTenantEmpId, AbortToken)
         ?? throw new ActionException("The employee was not found.", 404);
      if (current is not null) {
         if (!TenantOrg.Same(current.cTenantId, row.cTenantId) || !TenantOrg.Same(current.cContactId, row.cContactId))
            throw new ActionException("The tenant and person cannot be changed.", 400);
         if (current.ustamp != row.ustamp) throw new ActionException("The employee changed. Refresh before saving.", 409);
      }
      if (!await ctx.ta_Tenants.AnyAsync(x => x.cTenantId == row.cTenantId, AbortToken)) throw new ActionException("The tenant was not found.", 400);
      var department = TenantOrg.Optional(row.cTenantDeptId);
      if (department is not null && !await ctx.ta_TenantDepts.AnyAsync(x => x.cTenantDeptId == department && x.cTenantId == row.cTenantId, AbortToken))
         throw new ActionException("The department must belong to this tenant.", 400);
      var contactId = row.cContactId;
      if (creating && string.IsNullOrWhiteSpace(contactId)) {
         if (save.NewContactType is not (ContactType.Male or ContactType.Female)) throw new ActionException("Choose a person type.", 400);
         contactId = Ulid.NewUlid().ToString();
         ctx.ta_Contacts.Add(new ta_Contact { cContactId = contactId, cContactFullName = save.NewContactName ?? "", cContactType = save.NewContactType, cContactState = ContactState.Active, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow });
         await ctx.SaveChangesAsync(AbortToken);
      }
      else if (!await ctx.ta_Contacts.AnyAsync(x => x.cContactId == contactId && x.cContactType != ContactType.Organization, AbortToken))
         throw new ActionException("The person was not found.", 400);

      var cleared = new List<string>();
      if (current is not null && (!TenantOrg.Same(current.cTenantDeptId, department) || row.cTenantEmpState != ta_TenantEmp.StateActive)) {
         var heads = await ctx.ta_TenantDepts.AsNoTracking().Where(d => d.cTenantDeptHead_cTenantEmpId == current.cTenantEmpId).ToArrayAsync(AbortToken);
         foreach (var d in heads) {
            await ctx.ta_TenantDepts.Where(x => x.cTenantDeptId == d.cTenantDeptId).ExecuteUpdateAsync(s => s
               .SetProperty(x => x.cTenantDeptHead_cTenantEmpId, (string?)null).SetProperty(x => x.ustamp, TenantOrg.Stamp(d.ustamp)), AbortToken);
            cleared.Add(d.cTenantDeptId);
         }
      }
      var id = creating ? Ulid.NewUlid().ToString() : row.cTenantEmpId;
      await TenantOrg.Save(async () => {
         if (creating) {
            ctx.ta_TenantEmps.Add(new ta_TenantEmp {
               cTenantEmpId = id, cTenantId = row.cTenantId, cContactId = contactId, cTenantDeptId = department,
               cTenantEmpCode = row.cTenantEmpCode, cTenantEmpPosition = row.cTenantEmpPosition, cTenantEmpState = row.cTenantEmpState,
               cTenantEmpNote = row.cTenantEmpNote, json_object = row.json_object, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync(AbortToken);
         }
         else await ctx.ta_TenantEmps.Where(x => x.cTenantEmpId == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cTenantDeptId, department).SetProperty(x => x.cTenantEmpCode, row.cTenantEmpCode)
            .SetProperty(x => x.cTenantEmpPosition, row.cTenantEmpPosition).SetProperty(x => x.cTenantEmpState, row.cTenantEmpState)
            .SetProperty(x => x.cTenantEmpNote, row.cTenantEmpNote).SetProperty(x => x.json_object, row.json_object)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(current!.ustamp)), AbortToken);
      }, "The employee number or person is already used in this tenant.");
      foreach (var dept in cleared) Audit(dept, "tenant.dept.changed", "Cleared department head after employee transfer or deactivation", "department");
      Audit(id, creating ? "tenant.emp.created" : "tenant.emp.changed", "Saved employee '" + row.cTenantEmpCode + "'", "employee");
      await ctx.SaveChangesAsync(AbortToken);
      await tx.CommitAsync(AbortToken);
      return await ctx.vi_TenantEmps.AsNoTracking().SingleAsync(x => x.cTenantEmpId == id, AbortToken);
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) => ctx.vi_TenantEmps.AsNoTracking()
      .Where(e => e.cTenantId == tenantId && e.cTenantEmpState == ta_TenantEmp.StateActive && ctx.ta_Users.Any(u => u.cUserId == userId && u.cUserState == UserState.Active && u.cContactId == e.cContactId)).SingleOrDefaultAsync(AbortToken);

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) => ctx.ta_Users.AsNoTracking()
      .Where(u => u.cUserState == UserState.Active && ctx.ta_TenantEmps.Any(e => e.cTenantEmpId == tenantEmpId && e.cContactId == u.cContactId)).OrderBy(u => u.cUserId).Select(u => u.cUserId).ToArrayAsync(AbortToken);

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) => (from employee in ctx.ta_TenantEmps.AsNoTracking()
      join department in ctx.ta_TenantDepts on employee.cTenantDeptId equals department.cTenantDeptId
      join head in ctx.vi_TenantEmps on department.cTenantDeptHead_cTenantEmpId equals head.cTenantEmpId
      where employee.cTenantEmpId == tenantEmpId && head.cTenantEmpState == ta_TenantEmp.StateActive
      select head).SingleOrDefaultAsync(AbortToken);
   #endregion

   private void Audit(string id, string kind, string summary, string type) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = type
      });
   }
}
