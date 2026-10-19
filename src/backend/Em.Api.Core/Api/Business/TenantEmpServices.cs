using System.Data;
using Em.Api.Core.Log.Audit;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Employee saves, person candidates and contact-based user lookups.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantEmpServices(ApiCoreContext ctx, IContactServices? contacts = null) : ServicesBase, ITenantEmpServices
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
   public async Task<TenantEmpPerson> GetMeta_TenantEmpPerson(string contactId) {
      // One snapshot prevents mismatched default links and details when another editor saves concurrently.
      await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
      var contact = await ctx.ta_Contacts.AsNoTracking().SingleOrDefaultAsync(x => x.cContactId == contactId, AbortToken)
         ?? throw new ActionException("The person was not found.", 404);
      var result = new TenantEmpPerson {
         ContactId = contact.cContactId, ContactUstamp = contact.ustamp, ContactType = contact.cContactType,
         Comms = await ctx.ta_Comms.AsNoTracking().Where(x => x.cContactId == contactId).OrderBy(x => x.cCommId).ToArrayAsync(AbortToken),
         Addresses = await ctx.ta_Addresses.AsNoTracking().Where(x => x.cContactId == contactId).OrderBy(x => x.cAddressId).ToArrayAsync(AbortToken),
         DefaultCommId = contact.cContactDefaultComm_cCommId, DefaultAddressId = contact.cContactDefaultAddress_cAddressId,
         Comm = await ctx.ta_Comms.AsNoTracking().SingleOrDefaultAsync(x => x.cCommId == contact.cContactDefaultComm_cCommId && x.cContactId == contactId, AbortToken),
         Address = await ctx.ta_Addresses.AsNoTracking().SingleOrDefaultAsync(x => x.cAddressId == contact.cContactDefaultAddress_cAddressId && x.cContactId == contactId, AbortToken)
      };
      await tx.CommitAsync(AbortToken);
      return result;
   }

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public async Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) {
      var page = await (contacts ?? new ContactServices(ctx)).PostGetMeta_ContactSearchPage(new() { Search = search, Page = 1, Filter = new() });
      var ids = page.Rows.Select(x => x.ContactId).ToArray();
      var employees = await ctx.ta_TenantEmps.AsNoTracking().Where(e => ids.Contains(e.cContactId) && e.cTenantId == tenantId).ToArrayAsync(AbortToken);
      return page.Rows.Select(c => new TenantEmpContactCandidate {
         ContactId = c.ContactId, FullName = c.FullName, ContactType = c.ContactType, ContactState = c.ContactState,
         UserAccounts = c.UserAccounts,
         TenantEmpId = employees.FirstOrDefault(e => TenantOrg.Same(e.cContactId, c.ContactId))?.cTenantEmpId
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
      var newPerson = creating && string.IsNullOrWhiteSpace(contactId);
      if (newPerson) {
         if (!Enum.IsDefined(save.NewContactType)) throw new ActionException("Choose a contact type.", 400);
         contactId = Ulid.NewUlid().ToString();
         ctx.ta_Contacts.Add(new ta_Contact { cContactId = contactId, cContactFullName = save.NewContactName ?? "", cContactType = save.NewContactType, cContactState = ContactState.Active, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow });
         await ctx.SaveChangesAsync(AbortToken);
      }
      else if (!await ctx.ta_Contacts.AnyAsync(x => x.cContactId == contactId, AbortToken))
         throw new ActionException("The person was not found.", 400);

      if (newPerson || save.Person is not null) await SavePersonAsync(contactId, save.Person, newPerson);
      else {
         var contact = await ctx.ta_Contacts.AsNoTracking().SingleAsync(x => x.cContactId == contactId, AbortToken);
         if (!await ctx.ta_Comms.AnyAsync(x => x.cContactId == contactId && x.cCommId == contact.cContactDefaultComm_cCommId && x.cCommState != ContactCommunicationState.Inactive && x.cCommValue.Trim() != "", AbortToken) ||
             !await ctx.ta_Addresses.AnyAsync(x => x.cContactId == contactId && x.cAddressId == contact.cContactDefaultAddress_cAddressId && x.cAddressState != AddressState.Inactive && x.cAddressLocation.Trim() != "", AbortToken))
            throw new ActionException("Choose a non-empty default communication and address before saving.", 400);
      }

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

   private async Task SavePersonAsync(string contactId, TenantEmpPerson? input, bool creating) {
      var person = await ctx.ta_Contacts.AsNoTracking().SingleAsync(x => x.cContactId == contactId, AbortToken);
      if (input is null) throw new ActionException("Choose a non-empty default communication and address before saving.", 400);
      if (!creating && !TenantOrg.Same(input.ContactId, contactId)) throw new ActionException("The details must belong to this contact.", 400);
      if (!creating && input.ContactUstamp != person.ustamp) throw new ActionException("The contact changed. Refresh before saving.", 409);
      if (creating && !string.IsNullOrEmpty(input.ContactId)) throw new ActionException("A new contact cannot reuse another contact's details.", 400);
      if (input.ContactType is { } type && !Enum.IsDefined(type)) throw new ActionException("Choose a contact type.", 400);

      var comms = (input.EditCollections ? input.Comms : null) ?? (input.Comm is { } comm ? [comm] : Array.Empty<ta_Comm>());
      var addresses = (input.EditCollections ? input.Addresses : null) ?? (input.Address is { } address ? [address] : Array.Empty<ta_Address>());
      // Legacy default-only payloads receive IDs here; collection callers supply stable draft IDs.
      foreach (var item in comms) if (string.IsNullOrWhiteSpace(item.cCommId)) item.cCommId = Ulid.NewUlid().ToString();
      foreach (var item in addresses) if (string.IsNullOrWhiteSpace(item.cAddressId)) item.cAddressId = Ulid.NewUlid().ToString();
      var commId = !input.EditCollections ? input.Comm?.cCommId : input.DefaultCommId;
      var addressId = !input.EditCollections ? input.Address?.cAddressId : input.DefaultAddressId;
      if (comms.Select(x => x.cCommId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != comms.Length ||
          addresses.Select(x => x.cAddressId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != addresses.Length)
         throw new ActionException("Contact details contain duplicate IDs.", 400);
      var defaultComm = comms.SingleOrDefault(x => TenantOrg.Same(x.cCommId, commId));
      var defaultAddress = addresses.SingleOrDefault(x => TenantOrg.Same(x.cAddressId, addressId));
      if (defaultComm is null || defaultAddress is null || string.IsNullOrWhiteSpace(defaultComm.cCommValue) ||
          string.IsNullOrWhiteSpace(defaultAddress.cAddressLocation) || defaultComm.cCommState == ContactCommunicationState.Inactive || defaultAddress.cAddressState == AddressState.Inactive)
         throw new ActionException("Choose a non-empty active default communication and address before saving.", 400);

      var now = DateTime.UtcNow;
      foreach (var item in comms) {
         var old = await ctx.ta_Comms.AsNoTracking().SingleOrDefaultAsync(x => x.cCommId == item.cCommId, AbortToken);
         if ((!string.IsNullOrEmpty(item.cContactId) && !TenantOrg.Same(item.cContactId, contactId)) || old is not null && (creating || !TenantOrg.Same(old.cContactId, contactId)))
            throw new ActionException("The communication must belong to this contact.", 400);
         if (old is not null && old.ustamp != item.ustamp || old is null && item.ustamp != default)
            throw new ActionException("A communication changed. Refresh before saving.", 409);
         var state = TenantOrg.Same(item.cCommId, commId) ? ContactCommunicationState.ActiveAsDefault :
            item.cCommState == ContactCommunicationState.ActiveAsDefault ? ContactCommunicationState.Active : item.cCommState;
         if (old is null) ctx.ta_Comms.Add(new ta_Comm {
            cCommId = item.cCommId, cContactId = contactId, cCommType = item.cCommType, cCommValue = item.cCommValue,
            cCommNote = item.cCommNote, cCommState = state, json_object = item.json_object, ustamp = now, datestamp = now
         });
         else await ctx.ta_Comms.Where(x => x.cCommId == item.cCommId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cCommType, item.cCommType).SetProperty(x => x.cCommValue, item.cCommValue)
            .SetProperty(x => x.cCommNote, item.cCommNote).SetProperty(x => x.cCommState, state)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(old.ustamp)), AbortToken);
      }
      foreach (var item in addresses) {
         var old = await ctx.ta_Addresses.AsNoTracking().SingleOrDefaultAsync(x => x.cAddressId == item.cAddressId, AbortToken);
         if ((!string.IsNullOrEmpty(item.cContactId) && !TenantOrg.Same(item.cContactId, contactId)) || old is not null && (creating || !TenantOrg.Same(old.cContactId, contactId)))
            throw new ActionException("The address must belong to this contact.", 400);
         if (old is not null && old.ustamp != item.ustamp || old is null && item.ustamp != default)
            throw new ActionException("An address changed. Refresh before saving.", 409);
         var state = TenantOrg.Same(item.cAddressId, addressId) ? AddressState.ActiveAsDefault :
            item.cAddressState == AddressState.ActiveAsDefault ? AddressState.Active : item.cAddressState;
         if (old is null) ctx.ta_Addresses.Add(new ta_Address {
            cAddressId = item.cAddressId, cContactId = contactId, cAddressName = item.cAddressName, cAddressLocation = item.cAddressLocation,
            cAddressZip = item.cAddressZip, cAddressState = state, json_object = item.json_object, ustamp = now, datestamp = now
         });
         else await ctx.ta_Addresses.Where(x => x.cAddressId == item.cAddressId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cAddressName, item.cAddressName).SetProperty(x => x.cAddressLocation, item.cAddressLocation)
            .SetProperty(x => x.cAddressZip, item.cAddressZip).SetProperty(x => x.cAddressState, state)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(old.ustamp)), AbortToken);
      }
      // Demote any previous default omitted by a default-only caller without deleting other details.
      await ctx.ta_Comms.Where(x => x.cContactId == contactId && x.cCommId != commId && x.cCommState == ContactCommunicationState.ActiveAsDefault)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cCommState, ContactCommunicationState.Active).SetProperty(x => x.ustamp, now), AbortToken);
      await ctx.ta_Addresses.Where(x => x.cContactId == contactId && x.cAddressId != addressId && x.cAddressState == AddressState.ActiveAsDefault)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cAddressState, AddressState.Active).SetProperty(x => x.ustamp, now), AbortToken);
      await ctx.SaveChangesAsync(AbortToken);
      await ctx.ta_Contacts.Where(x => x.cContactId == contactId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cContactType, input.ContactType ?? person.cContactType)
         .SetProperty(x => x.cContactDefaultComm_cCommId, commId).SetProperty(x => x.cContactDefaultAddress_cAddressId, addressId)
         .SetProperty(x => x.ustamp, TenantOrg.Stamp(person.ustamp)), AbortToken);
   }

   private void Audit(string id, string kind, string summary, string type) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = type
      });
   }
}
