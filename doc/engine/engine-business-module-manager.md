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
department locally. For a new employee, choose **Existing person**, search by name or active user account,
and select an available person; already-employed people are marked and disabled. Alternatively choose
**New person**, enter a name and select Male or Female. Existing person details are read-only. Set Code,
Department (optional), Position and Note, then Save. A person can be employed once per tenant and in several
tenants. Employee numbers are unique within a tenant. User accounts are connected through the person;
there is no direct employee-to-user column.

Transferring a head to another department or deactivating the head asks for confirmation. Saving clears
the old department head automatically. Switching records, tenants or tabs asks before discarding unsaved
changes. Server errors stay below the toolbar, leaving the draft available for correction.

## Service actions

All actions belong to `core.business` and require the same manager claim over HTTP. Administrators use the
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
| | `PostGetMeta_TenantEmpSave(save)` | Save an employee and optionally create a person atomically |
| | `GetMeta_TenantEmpByUser(tenantId, userId)` | Active employee for an active user, or null |
| | `GetMeta_TenantEmpUsers(tenantEmpId)` | Active user IDs belonging to the employee's person |
| | `GetMeta_TenantEmpHead(tenantEmpId)` | Active head of this department, or null; never searches parents |

`TenantEmpSave.Row` contains employee values and the update stamp. For a create, leave its employee ID
empty. To create a person in the same transaction, also leave the contact ID empty and provide
`NewContactName` / `NewContactType`. Updating employment cannot change its tenant or person.

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
membership. Each employee has one optional department. Hiring/leaving dates, personal data, department
history, multiple appointments, custom employment policy, bulk import and legacy mappings belong to a
consumer's personnel module. There is no department object browser or MAUI screen.

## Maintainer notes

SQL integration tests cover tenant boundaries, hierarchy, duplicate codes/people, stale writes, head
transfers/deactivation, rollback and contact/account lookups. WPF tests load XAML without rendering and
exercise editor choices and guards. Local HTTP smoke lives in
`../.artefacts/em-system/scripts/tenant-org-smoke/`; offline light/dark rendering lives in
`../.artefacts/em-system/scripts/business-manager-render/`. Real-window mouse interaction is separate.
