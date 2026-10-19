# Business Module Manager

The Business Module Manager provides a small shared tenant directory, a department tree per tenant and
employee records connecting a person to a tenant. It is available to administrators and users with
`core.business:Business Module Manager Access`.

## Enable it

Call `builder.AddBusinessModuleManager()` in the API host. The example host enables it by default;
`modules.business: false` (or `EM_MODULE_BUSINESS=false`) disables it. WPF registers the navigation and all
three client proxies automatically; its Tools entry follows the server claim catalogue. MAUI has proxies
but no manager screen. No business menu paths or document enablers are registered.

For SQL Server, install `doc/sqlscript/mssql/sets/000-ulid.sql`, then `tables/010-core.sql`,
`tables/080-tenant.sql`, `tables/090-tenant-org.sql`, then `views/vi_Tenant.sql`, `views/vi_TenantDept.sql` and
`views/vi_TenantEmp.sql`. The numbered table scripts are re-runnable and have no tenant or organization seed.
There is no startup schema check. Other database providers do not yet have organization scripts.

## Desktop workflow

**Tenants** manages the company tree. Select a node to edit, or choose **New tenant** / **New child**.
Codes are globally unique. Deactivate retains records and does not deactivate descendants.

**Departments** starts with a tenant selector. The tree shows codes, active employee counts and inactive
markers. Create a root or child, edit Code, Name, Abbreviation, Parent, Order and Note, then Save. A saved
department may choose its Head from its own active employees. Parent choices exclude the current node and
its descendants. Inactive departments can remain parents and retain employees.

**Employees** also starts with a tenant selector. The text filter matches name, employee number and
department locally. For a new employee, choose **Existing person**, open **Search contact**, search by contact name or linked username, and choose an available person.
The reusable [Contact picker](engine-contact-picker.md) displays Contact, Username and Contact Status.
The Employee caller requests active people and excludes contacts already employed in the selected tenant. Results are searched and paged on the server (50 per page);
Previous/Next navigate the applied search, and a new Search restarts at page one. Contacts without a user
are included; inactive usernames can also locate their contact. Choosing a contact loads its defaults. Alternatively choose
**New person**, enter a name and select Male or Female. The existing person's name and accounts are read-only; their default communication and address can be edited here. Set Code,
Department (optional), Position and Note, then Save. A person can be employed once per tenant and in several
tenants. Employee numbers are unique within a tenant. User accounts are connected through the person;
there is no direct employee-to-user column.

Transferring a head to another department or deactivating the head asks for confirmation. Saving clears
the old department head automatically. Switching records, tenants or tabs asks before discarding unsaved
changes. Server errors stay below the toolbar, leaving the draft available for correction.

The person section includes **Default Comm** (type, value, note) and **Default Address** (label, location,
ZIP). A new person receives both records and their default links in the same save transaction. Selecting an
existing person by username loads that contact's existing defaults, without copying them per employee or
tenant. Changes to these defaults affect the same person across all their accounts and employments. If
older data lacks a default, saving the edited person details creates and links it. The UI does not invent
contact values from the username.

## Service actions

The tenant, department and employee actions below belong to `core.business` and require the same manager claim over HTTP. Administrators use the
engine administrator bypass. Inject these interfaces on the server or resolve the shared client proxies.

| Interface | Action | Behavior |
| --- | --- | --- |
| `ITenantServices` | `GetVi_Tenants` | All tenants, ordered by display order and name |
| | `PostGetTa_Tenant_New` / `PostGetTa_Tenant_Update` | Create or update a tenant |
| `ITenantDeptServices` | `GetVi_TenantDepts(tenantId)` | All departments of the tenant |
| | `PostGetTa_TenantDept_New` | Create a department; ignore head until saved |
| | `PostGetTa_TenantDept_Update` | Update hierarchy, fields and active head |
| `ITenantEmpServices` | `GetVi_TenantEmps(tenantId)` | All employees, ordered by person name and code |
| | `GetMeta_TenantEmpContacts(tenantId, search)` | Up to 50 people, active accounts and existing employee marker |
| | `GetMeta_TenantEmpPerson(contactId)` | Shared person defaults and their update stamps |
| | `PostGetMeta_TenantEmpSave(save)` | Save an employee and optionally create a person atomically |
| | `GetMeta_TenantEmpByUser(tenantId, userId)` | Active employee for an active user, or null |
| | `GetMeta_TenantEmpUsers(tenantEmpId)` | Active user IDs belonging to the employee's person |
| | `GetMeta_TenantEmpHead(tenantEmpId)` | Active head of this department, or null; never searches parents |

The contact dialog calls `IContactServices.PostGetMeta_ContactSearchPage` in `core.contact`, which is
available to authenticated callers without the business manager claim. Employee supplies the filters; the
contact service owns the shared search and paging. The older Employee candidate action delegates to that
service and adds its employee marker for compatibility.

`TenantEmpSave.Row` contains employee values and the update stamp. For a create, leave its employee ID
empty. To create a person in the same transaction, also leave the contact ID empty and provide
`NewContactName` / `NewContactType`. `TenantEmpSave.Person` contains the edited defaults. Existing person
saves must send the contact stamp and last-read default IDs/stamps. Stale contact/default changes return
409; details for a different person return 400. Omitting `Person` preserves existing defaults for earlier
clients. New people always receive both default records, even when an earlier client omits their values.
Updating employment cannot change its tenant or person. No database migration is required.

Updates use a serializable transaction and require the last read `ustamp`. A stale stamp or a duplicate
code/person returns 409; a missing employee or department returns 404. Invalid hierarchy, tenant changes,
foreign-tenant departments or nonmember/inactive heads return 400. Stamps move by at least 10 milliseconds
before SQL datetime rounding. Composite foreign keys protect tenant boundaries and head membership even
for direct SQL. A head must be cleared before direct SQL transfers its employee. The service performs both
changes and their `System.Settings` audit entries in one transaction. Audit kinds are `tenant.dept.created`,
`tenant.dept.changed`, `tenant.emp.created` and `tenant.emp.changed`.

## Use from another module

```csharp
public sealed class AssignmentService(ITenantEmpServices employees)
{
   public async Task<string[]> GetDepartmentHeadUsers(string tenantId, string userId) {
      var employee = await employees.GetMeta_TenantEmpByUser(tenantId, userId);
      if (employee is null) return [];
      var head = await employees.GetMeta_TenantEmpHead(employee.cTenantEmpId);
      return head is null ? [] : await employees.GetMeta_TenantEmpUsers(head.cTenantEmpId);
   }
}
```

These are direct in-process calls: the HTTP claim gate is not executed. The calling module must authorize
its own operation. See [In-process calls](engine-in-process-calls.md). A person can have multiple active
accounts; a module requiring exactly one signer must resolve that ambiguity itself. This stage does not
connect the lookups to approval or transactions.

## Limits

This directory does not select a session's active tenant, scope claims by tenant, or maintain user tenant
membership. Each employee has one optional department. Hiring/leaving dates, additional personal data, department
history, multiple appointments, custom employment policy, bulk import and legacy mappings belong to a
consumer's personnel module. There is no department object browser or MAUI screen.

## Maintainer notes

SQL integration tests cover tenant boundaries, hierarchy, duplicate codes/people, stale writes, head
transfers/deactivation, rollback and contact/account lookups. WPF tests load XAML without rendering and
exercise editor choices and guards. Local HTTP smoke lives in
`../.artefacts/em-system/scripts/tenant-org-smoke/`; offline light/dark rendering lives in
`../.artefacts/em-system/scripts/business-manager-render/`. Real-window mouse interaction is separate.

The contact-default and Search contact update was verified with SQL Server integration tests, WPF
view-model/XAML tests (including status text without rendering), and live HTTP calls through the shared
client proxy. Logs for this update are `person-*.log` under the smoke harness above. Consumer host builds
used isolated output while a desktop client was running. No schema migration was needed. Rendering and
real-window interaction of the new contact dialog remain unverified.

The shared contact-picker refactor and caller filters passed backend/WPF/MAUI builds, consumer API/WPF
host builds, 329 backend tests (8 optional-provider skips), 181 frontend tests and 23 live HTTP checks.
The filter regression covers status, type, user presence, exclusion before paging and retained caller
filters across navigation. Current logs are `out/contact-*.log` in the smoke harness above.
