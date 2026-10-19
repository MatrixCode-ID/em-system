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

**Employees** starts with a tenant selector. The local text filter matches name, employee number and
department. User accounts link to employment through the shared contact. Each contact can be employed once
per tenant and in several tenants; employee numbers are unique within each tenant.

Transferring or deactivating a department head asks for confirmation and clears the previous head in the
same transaction. Unsaved changes in the editor are confirmed before navigation. Errors leave the draft
available for correction.

Employee editing opens in a dedicated **Employee Editor** navigation. The Employees tab contains a full-width
list with New employee and Open actions; double-click, Enter and the row context menu open the same editor.
Selecting a row does not load an inline editor. Refresh the manager to see changes saved in another tab.

Search contact includes **all types and lifecycle states**, with linked usernames, including inactive users.
A contact already employed in this tenant remains visible but cannot be selected again. Employee uniqueness
is also enforced by the server. The editor can create a contact or edit an existing contact's type as
**Organization**, **Male** or **Female**. Shared contact changes affect all accounts and tenants using it.

The editor loads all communication and address entries, allows adding entries, and independently selects
the entry being edited and the default entry. A new contact starts with one draft of each kind. Save requires
an active default communication with a nonblank value and an active default address with a nonblank location.
Both UI and server enforce these guards. Existing contacts without defaults must be corrected before saving.
Saving preserves other entries and commits employment, type, entries and defaults in one transaction.

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
| | `GetMeta_TenantEmpContacts(tenantId, search)` | Up to 50 contacts of all types/states, linked accounts and existing employee marker |
| | `GetMeta_TenantEmpPerson(contactId)` | Contact type, all communication/address entries, default links and update stamps |
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
`NewContactName` / `NewContactType`. `TenantEmpSave.Person` carries the contact snapshot.
For collection editing, set `EditCollections = true`, provide `Comms` / `Addresses`, and select
`DefaultCommId` / `DefaultAddressId` from those entries. New entries use fresh ULIDs and a default update
stamp; existing entries retain their last-read stamps and contact identity. Set `ContactType` to change
an existing contact's type. Contact and row stamp conflicts return 409; foreign details or missing,
inactive or blank defaults return 400. Omitted entries are preserved rather than deleted.

Earlier callers can leave `EditCollections` false and edit `Comm` / `Address` as default-only fields.
Omitting `Person` preserves contact data only when valid defaults already exist. A new contact requires
nonblank default details. Updating employment cannot change its tenant or contact. No database migration
is required.

Updates use a serializable transaction and require the last read `ustamp`. A stale stamp or a duplicate
code/person returns 409; a missing employee or department returns 404. Invalid hierarchy, tenant changes,
foreign-tenant departments or nonmember/inactive heads return 400. Stamps move by at least 10 milliseconds
before SQL datetime rounding. Composite foreign keys protect tenant boundaries and head membership even
for direct SQL. A head must be cleared before direct SQL transfers its employee. The service performs both
changes and their `System.Settings` audit entries in one transaction. Audit kinds are `tenant.dept.created`,
`tenant.dept.changed`, `tenant.emp.created` and `tenant.emp.changed`.

## Use from another module

Any WPF screen can open the editor with the existing Business Module Manager claim:

```csharp
await NavigationEntry.NavigateTo(EmApp.EmployeeEditorNavigationName,
   EmployeeNavigationPayload.Open(tenantId, employeeId));
// Or start a new employee in the tenant:
await NavigationEntry.NavigateTo(EmApp.EmployeeEditorNavigationName,
   EmployeeNavigationPayload.New(tenantId));
```

Payload titles contain stable identities so employees with the same contact name can open independently.
The editor confirms unsaved changes before navigation and keeps server failures available for correction.


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

The dedicated Employee Editor update (2026-10-19) passed 21 tenant/organization SQL Server integration
tests, all 168 WPF Core tests, and consumer API/WPF builds with no build warnings. A final targeted run
passed 22 Business view-model/XAML/navigation tests after the asynchronous New action was adjusted.
Coverage includes organization contacts, shared-contact type updates, adding entries, switching defaults,
empty/inactive/foreign default rejection, row concurrency, and independent editing/default picker binding.
Consumer build output is isolated under the existing tenant-org smoke folder at
`out/employee-editor-host/`. No schema changes or production data changes were made.
Visual rendering and real-window interaction of the dedicated editor remain unverified.
