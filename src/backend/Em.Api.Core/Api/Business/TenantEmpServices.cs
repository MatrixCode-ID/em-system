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

   /// <inheritdoc />
   [GetAction(claim: ITenantServices.ManagerClaim)]
   public Task<vi_TenantEmp?> GetVi_TenantEmp_ById(string tenantEmpId) => ctx.vi_TenantEmps.AsNoTracking()
      .SingleOrDefaultAsync(x => x.cTenantEmpId == tenantEmpId, AbortToken);
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
         ContactId = contact.cContactId, ContactUstamp = contact.ustamp, ContactType = contact.cContactType, FullName = contact.cContactFullName,
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
      var position = TenantOrg.Optional(row.cTenantPositionId);
      if (position is not null) {
         var state = await ctx.ta_TenantPositions.AsNoTracking().Where(x => x.cTenantPositionId == position && x.cTenantId == row.cTenantId).Select(x => (int?)x.cTenantPositionState).SingleOrDefaultAsync(AbortToken)
            ?? throw new ActionException("The position must belong to this tenant.", 400);
         // An employee keeps a position that was deactivated later, but cannot be given one.
         if (state != ta_TenantPosition.StateActive && !TenantOrg.Same(current?.cTenantPositionId, position)) throw new ActionException("Choose an active position.", 400);
      }
      if (save.IsDeptHead == true && (department is null || row.cTenantEmpState != ta_TenantEmp.StateActive))
         throw new ActionException("The head must be an active employee of the department.", 400);
      var contactId = row.cContactId;
      var newPerson = creating && string.IsNullOrWhiteSpace(contactId);
      if (newPerson) {
         if (!Enum.IsDefined(save.NewContactType)) throw new ActionException("Choose a contact type.", 400);
         if (string.IsNullOrWhiteSpace(save.NewContactName)) throw new ActionException("Enter the contact name.", 400);
         contactId = Ulid.NewUlid().ToString();
         ctx.ta_Contacts.Add(new ta_Contact { cContactId = contactId, cContactFullName = save.NewContactName.Trim(), cContactType = save.NewContactType, cContactState = ContactState.Active, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow });
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
               cTenantEmpCode = row.cTenantEmpCode, cTenantPositionId = position, cTenantEmpState = row.cTenantEmpState,
               cTenantEmpNote = row.cTenantEmpNote, json_object = row.json_object, ustamp = DateTime.UtcNow, datestamp = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync(AbortToken);
         }
         else await ctx.ta_TenantEmps.Where(x => x.cTenantEmpId == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cTenantDeptId, department).SetProperty(x => x.cTenantEmpCode, row.cTenantEmpCode)
            .SetProperty(x => x.cTenantPositionId, position).SetProperty(x => x.cTenantEmpState, row.cTenantEmpState)
            .SetProperty(x => x.cTenantEmpNote, row.cTenantEmpNote).SetProperty(x => x.json_object, row.json_object)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(current!.ustamp)), AbortToken);
      }, "The employee number or person is already used in this tenant.");
      foreach (var dept in cleared) Audit(dept, "tenant.dept.changed", "Cleared department head after employee transfer or deactivation", "department");
      if (save.IsDeptHead is { } head && department is not null) await SaveHeadAsync(department, id, head);
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

   // Runs after the employee row, because the head link requires the employee to be in the department.
   private async Task SaveHeadAsync(string departmentId, string employeeId, bool head) {
      var department = await ctx.ta_TenantDepts.AsNoTracking().SingleAsync(x => x.cTenantDeptId == departmentId, AbortToken);
      var isHead = TenantOrg.Same(department.cTenantDeptHead_cTenantEmpId, employeeId);
      if (head == isHead) return;
      var value = head ? employeeId : null;
      await ctx.ta_TenantDepts.Where(x => x.cTenantDeptId == departmentId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cTenantDeptHead_cTenantEmpId, value).SetProperty(x => x.ustamp, TenantOrg.Stamp(department.ustamp)), AbortToken);
      Audit(departmentId, "tenant.dept.changed", head ? "Set department head from the employee editor" : "Cleared department head from the employee editor", "department");
   }

   private async Task SaveAttachmentsAsync(string contactId, ta_ContactAttachment[] attachments) {
      foreach (var item in attachments) {
         var old = await ctx.ta_ContactAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.cContactAttachmentId == item.cContactAttachmentId, AbortToken);
         if (old is null || !TenantOrg.Same(old.cContactId, contactId) || old.cContactAttachmentState != ta_ContactAttachment.StateActive)
            throw new ActionException("The attachment must belong to this contact.", 400);
         if (old.ustamp != item.ustamp) throw new ActionException("An attachment changed. Refresh before saving.", 409);
         if (string.IsNullOrWhiteSpace(item.cContactAttachmentName)) throw new ActionException("Enter the attachment name.", 400);
         if (!Enum.IsDefined(item.cContactAttachmentKind)) throw new ActionException("Choose an attachment kind.", 400);
         await ctx.ta_ContactAttachments.Where(x => x.cContactAttachmentId == item.cContactAttachmentId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.cContactAttachmentName, item.cContactAttachmentName.Trim()).SetProperty(x => x.cContactAttachmentKind, item.cContactAttachmentKind)
            .SetProperty(x => x.cContactAttachmentNote, item.cContactAttachmentNote).SetProperty(x => x.cContactAttachmentOrder, item.cContactAttachmentOrder)
            .SetProperty(x => x.ustamp, TenantOrg.Stamp(old.ustamp)), AbortToken);
      }
   }

   private async Task SavePersonAsync(string contactId, TenantEmpPerson? input, bool creating) {
      var person = await ctx.ta_Contacts.AsNoTracking().SingleAsync(x => x.cContactId == contactId, AbortToken);
      if (input is null) throw new ActionException("Choose a non-empty default communication and address before saving.", 400);
      if (!creating && !TenantOrg.Same(input.ContactId, contactId)) throw new ActionException("The details must belong to this contact.", 400);
      if (!creating && input.ContactUstamp != person.ustamp) throw new ActionException("The contact changed. Refresh before saving.", 409);
      if (creating && !string.IsNullOrEmpty(input.ContactId)) throw new ActionException("A new contact cannot reuse another contact's details.", 400);
      if (input.ContactType is { } type && !Enum.IsDefined(type)) throw new ActionException("Choose a contact type.", 400);
      if (input.FullName is not null && string.IsNullOrWhiteSpace(input.FullName)) throw new ActionException("Enter the contact name.", 400);
      if (input.FullName?.Trim().Length > 255) throw new ActionException("The contact name is too long.", 400);

      var comms = (input.EditCollections ? input.Comms : null) ?? (input.Comm is { } comm ? [comm] : Array.Empty<ta_Comm>());
      var addresses = (input.EditCollections ? input.Addresses : null) ?? (input.Address is { } address ? [address] : Array.Empty<ta_Address>());
      ContactDetails.Prepare(comms, addresses);
      var commId = !input.EditCollections ? input.Comm?.cCommId : input.DefaultCommId;
      var addressId = !input.EditCollections ? input.Address?.cAddressId : input.DefaultAddressId;
      var defaultComm = comms.SingleOrDefault(x => TenantOrg.Same(x.cCommId, commId));
      var defaultAddress = addresses.SingleOrDefault(x => TenantOrg.Same(x.cAddressId, addressId));
      if (defaultComm is null || defaultAddress is null || string.IsNullOrWhiteSpace(defaultComm.cCommValue) ||
          string.IsNullOrWhiteSpace(defaultAddress.cAddressLocation) || defaultComm.cCommState == ContactCommunicationState.Inactive || defaultAddress.cAddressState == AddressState.Inactive)
         throw new ActionException("Choose a non-empty active default communication and address before saving.", 400);

      await ContactDetails.WriteAsync(ctx, contactId, comms, addresses, commId, addressId, creating, AbortToken);
      if (input.Attachments is { Length: > 0 } attachments) await SaveAttachmentsAsync(contactId, attachments);
      var fullName = input.FullName?.Trim() ?? person.cContactFullName;
      await ctx.ta_Contacts.Where(x => x.cContactId == contactId).ExecuteUpdateAsync(s => s
         .SetProperty(x => x.cContactFullName, fullName).SetProperty(x => x.cContactType, input.ContactType ?? person.cContactType)
         .SetProperty(x => x.cContactDefaultComm_cCommId, commId).SetProperty(x => x.cContactDefaultAddress_cAddressId, addressId)
         .SetProperty(x => x.ustamp, TenantOrg.Stamp(person.ustamp)), AbortToken);
   }

   private void Audit(string id, string kind, string summary, string type) {
      if (App is not null) GetService<IAuditWriter>()?.Use(ctx).AddEntry(new AuditEntryDraft {
         Group = "System.Settings", Key = id, Kind = kind, Summary = summary, TargetType = type
      });
   }
}
