# Business Module Manager

The Business Module Manager provides a small shared tenant directory, a department tree and a position list per
tenant, employee records connecting a person to a tenant, and the setup of attendance: schedule templates, scheduling
settings, attendance codes and public holidays. It also provides global units, tenant product types and a tenant product tree. It is available to administrators and users with
`core.business:Business Module Manager Access`.

## Scope and wrapper modules

The Business Module Manager is a **setup tool for basic information only**: tenants, departments, positions and
employees with their basic contact data (full name, type, communications, addresses, attachments), the four
attendance setup pages, and product setup. It stays one administrator tool under Tools behind one claim. It is not where business
workflows, per-role claims or Apps menu entries live. Schedule assignments, the roster and the attendance log are
workflows, so they belong to the host application.

A library consumer that needs features on top of this data builds its own **wrapper module** (for example
a personnel, customer or vendor module). The wrapper:

- defines the claims that fit its roles, instead of reusing the Business Module Manager claim;
- registers its own navigation so it appears in the **Apps** menu like any other module;
- reads and writes the basic data through the engine services and lookups (in-process calls are not
  claim-gated, so the wrapper authorizes its own operations, see [Use from another module](#use-from-another-module));
- keeps its own extra data in its own tables keyed by the engine ids (for example the employee id), not in
  new columns of the Business Module Manager tables. Extra attributes of one employee may instead live in the
  `json_object` of that employee.

The UI model `TenantEmp` (`Em.Ui.Core`) is the base for such a wrapper. Derive from it, lift `json_object`
fields to properties with `GetJson<T>` in `ReadFrom`, and write them in `BuildJson(patch)`, always calling
`base` first; fields the model does not know are preserved. Because the employee actions need the Business
Module Manager claim over HTTP, a wrapper with its own claim overrides `FetchAsync`, `UpdateAsync` and
`InsertAsync` to go through its own service and ends both saves with `ApplySaved(entity, saved)`, which carries
the returned key and stamp. `WriteTo` sends the last-read stamp, not the server time `SaveAsync` puts in `ustamp`.

The product equivalent is `TenantProduct` in `Em.Ui.Core`. Derive a model for host attributes, read them with
`GetJson<T>` and patch them in `BuildJson(patch)`, calling the base implementation first. Override
`FetchAsync`, `UpdateAsync` and `InsertAsync` to use the wrapper's authorized actions, and call
`ApplySaved(entity, saved)` after each save. Keep the original update stamp for optimistic concurrency.
The wrapper can resolve its type with `PostGetMeta_TenantProductType_Ensure`; existing rows retain their
saved name, kind and state. Hosts own product workflows and extra JSON attributes.

## Enable it

Call `builder.AddBusinessModuleManager()` in the API host. The example host enables it by default;
`modules.business: false` (or `EM_MODULE_BUSINESS=false`) disables it. WPF registers the navigation and all
client proxies automatically; its Tools entry follows the server claim catalogue. MAUI has proxies
but no manager screen. No business menu paths or document enablers are registered.

For SQL Server, install `doc/sqlscript/mssql/sets/000-ulid.sql`, then `tables/010-core.sql`,
`tables/080-tenant.sql`, `tables/090-tenant-org.sql`, then `views/vi_Tenant.sql`, `views/vi_TenantDept.sql`,
`views/vi_TenantPosition.sql` and `views/vi_TenantEmp.sql`. Attendance setup adds `tables/110-attendance.sql` and
`views/vi_TenantSchedTemplate.sql`, `views/vi_TenantSchedSetting.sql`, `views/vi_TenantAttCode.sql` and
`views/vi_PublicHoliday.sql`. MySQL has the same files under `doc/sqlscript/mysql`.
The numbered table scripts are re-runnable and have no tenant or organization seed. There is no startup schema check.

**An existing installation must run the five attendance scripts before starting the new API.** The new tables are not
checked at startup, so without them the first call to an attendance page fails. No update script is needed: every
table is new, and `tables/110-attendance.sql` can run again safely.

An installation from before positions runs `updates/20261107-TenantPosition.sql` and then the two views, before
starting the new API. The update moves any employee position text into positions of the tenant, with the first 50
characters as the code, then drops the text column.

**Product setup requires its schema before the API starts.** For a new installation, run `tables/100-business.sql`
after the organization tables, then `tables/120-product.sql` and the views `vi_Unit.sql`,
`vi_TenantProductType.sql` and `vi_TenantProduct.sql`. For an existing installation, run
`updates/20261112-Unit-Stamp.sql` before `tables/120-product.sql`, then the three views and
`updates/20261112-DropLegacyProduct.sql`. The files have the same names on SQL Server and MySQL.
The legacy cleanup stops with counts if any of the five legacy tables contains rows. It also stops if
another table still references one of them. The SQL Server table script aligns the unit key collation
with `ta_Unit` for the foreign key. The product unit index supports the unit usage lookup.

Attachments need `builder.AddObjectStorage()`. The module declares the managed bucket `contact-attachments`, with a
50 MB limit per file. Without object storage the other features work and an upload answers 503.

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

**Positions** starts with a tenant selector and lists the positions employees can hold, with the active employee
count. Codes are unique per tenant. Deactivate stops offering a position; employees holding it keep it and nothing is
deleted. **Copy from tenant** lists the active positions of another tenant. Codes that already exist here, active or
not, are marked EXISTS and cannot be checked. Copies are saved as active and do not follow the source afterwards.

Transferring or deactivating a department head asks for confirmation and clears the previous head in the
same transaction. Unsaved changes in the editor are confirmed before navigation. Errors leave the draft
available for correction.

Pages are switched from a left drawer with a tree that the module lists itself: **Organization** holds Tenants,
Departments and Positions, **People** holds Employees, **Attendance** holds the four setup pages below, and **Products** holds Units, Product types and Products. The drawer can
be closed to a narrow rail with its toggle button.

**Schedule templates** starts with a tenant selector and lists working-hour patterns. A template has a short **Code**
(up to 10 characters, unique per tenant), a name, a start time and a break, plus hours for each of the seven weekdays.
A weekday means the day a shift starts, so a shift that crosses midnight belongs to the day it started. A day with 0
hours is a day off; a short last day is one row with its own break, which is empty to use the template break. The end
time is calculated, never stored. **Duplicate** copies a template, adding " copy" to the name and a number to the code.
Deactivate stops offering a template; settings that use it keep it, and Scheduling settings warns about it.

**Scheduling settings** shows the tenant with its departments as a tree. Each level has **Enable** and a **Default
schedule**: one template, or a rotation of at least two templates with **Weeks per step** and **Day 1**. Below the
tenant, Inherit follows the level above, and **Use inherited** sets a department back to it. The tenant level starts
Disabled, offers Built-in instead of Inherit, and holds **Period days**. A rotation belongs to its setting; it is not
a reusable list. The Effective result shows the saved values, so save a change to see what it does below.

**Attendance codes** works like Positions: a list per tenant, with **Copy from tenant**, a category and a sort order.
The engine keeps the list; what a code means for payroll belongs to a module.

**Public holidays** is one calendar for every tenant. A **Fixed** entry is a month and day that applies every year,
29 February included. A **Dated** entry is one date, entered per year: the engine calculates no lunar calendar. A year
without dated entries shows a warning, and **Copy dated from year** copies them with the same month and day, skipping
entries already there and 29 February into a non-leap year. The collective leave marker has no behaviour. Remove hides
an entry from the calendar; nothing is deleted.

Employee editing opens in a dedicated **Employee Editor** navigation. The Employees page contains a full-width
list with New employee and Open actions; double-click, Enter and the row context menu open the same editor.
Selecting a row does not load an inline editor. Refresh the manager to see changes saved in another tab.

Search contact includes **all types and lifecycle states**, with linked usernames, including inactive users.
A contact already employed in this tenant remains visible but cannot be selected again. Employee uniqueness
is also enforced by the server. The editor can create a contact or edit an existing contact's type as
**Organization**, **Male** or **Female**. Shared contact changes affect all accounts and tenants using it.

A saved employee keeps its tenant and contact; **Search contact** exists only for a new employee. **Contact full name**
can be edited and is saved with the employee, under the contact's update stamp. It cannot be blank and it changes the
contact everywhere it is used. The linked user account is shown read-only and changes in User Manager.

**Position** offers the active positions of the tenant, plus the employee's own position when it was deactivated.
**Head of department** needs a department and an active employee and shows the current head. Saving it checked makes
this employee the head, after confirmation when that replaces someone; saving it unchecked clears the head. Moving
department with it checked clears the old department and sets the new one in one transaction. **Revert** discards the
unsaved changes.

**Attachments** lists the active files of the contact. Add attachment, Replace file and Remove apply at once: Replace
adds a version to the same object, and Remove hides the record while the file and its versions stay in object storage.
Kind, Name and Note are saved with the employee. A new contact can have files once the employee is saved. Open
downloads a temporary copy, deleted when the editor closes unless another program holds it.

The editor loads all communication and address entries, allows adding entries, and independently selects
the entry being edited and the default entry. A new contact starts with one draft of each kind. The BMM editor
requires an active default communication with a nonblank value and an active default address with a nonblank
location before it saves. This is a UI guard only: the server saves an employee without default communication
or address, so a host UI decides whether its own screens require them (for example only for a new employee).
The server still refuses a default that does not belong to the contact.
Saving preserves other entries and commits employment, type, entries and defaults in one transaction.

### Product setup

**Units** lists shared units by code with their product usage count. Code, description and symbol are required.
A saved unit keeps its code. Delete asks for confirmation and returns 409 if a product or another foreign key
still references the unit.

**Product types** lists the selected tenant's types, their kind and product count. The selectable kinds are
Goods (1), Manufacture Base (2), Product Component (3), Non-stock (4), Service (5) and Financial (6).
A type with products keeps its kind. Deactivation asks for confirmation and leaves existing products in place.
The type editor retains the saved order.

**Products** loads roots first and direct children when a node expands, ordered by order then code. The type
filter applies to each level. Code/name search displays results in place of the tree. Open a result to clear the
type filter and expand its path, or clear the search to return to the tree. The breadcrumb includes the product.
Codes are entered by hand and must be unique across the tenant, including inactive products. The parent stays
fixed after the first save. A child can use a different active type in the same tenant. Deactivation asks for
confirmation and affects the selected product only. A new product uses its supplied order when nonnegative and
otherwise appends to its siblings.

**Add children** accepts one code and name per line, separated by a comma, semicolon or tab. Preview marks
existing, repeated and invalid codes. Batch requests can supply a type and unit or inherit them from the parent.
The selected type must be active. **Copy children from** finds a source with children and copies one level,
including when the source is the destination. Enter the old and replacement prefixes and adjust individual
codes in the preview. Copies keep the source name, unit, active user-defined type and note. Both actions
append active children in row order in one transaction. Copy omits extra JSON attributes.

### System product

The engine reserves code `GENERIC` for the type **Generic Transaction** and its root product
**Generic Transaction Item**. The kind `GenericTransaction` (7) identifies system records. They keep their
code and active state, the type keeps its kind, and the product keeps its type. Their names, notes and order
are editable through the service, as is the product's unit. An empty unit catalogue is supported by creating
unit `UNIT` (description `Unit`, symbol `unit`) when needed.

System setup runs on tenant creation, type listing, root product listing and explicit ensure. Listing by type
only reads the requested type. A unique-key race can be retried on the next ensure. A setup failure during
tenant creation is logged as a warning so tenant creation can finish.

## Service actions

The tenant, department, position, employee, attachment, attendance and product setup actions below belong to `core.business` and require the same manager claim over HTTP. Administrators use the
engine administrator bypass. Inject these interfaces on the server or resolve the shared client proxies.

| Interface | Action | Behavior |
| --- | --- | --- |
| `ITenantServices` | `GetVi_Tenants` | All tenants, ordered by display order and name |
| | `PostGetTa_Tenant_New` / `PostGetTa_Tenant_Update` | Create or update a tenant |
| `ITenantDeptServices` | `GetVi_TenantDepts(tenantId)` | All departments of the tenant |
| | `PostGetTa_TenantDept_New` | Create a department; ignore head until saved |
| | `PostGetTa_TenantDept_Update` | Update hierarchy, fields and active head |
| `ITenantPositionServices` | `GetVi_TenantPositions(tenantId)` | All positions of the tenant, ordered by name |
| | `PostGetTa_TenantPosition_New` / `PostGetTa_TenantPosition_Update` | Create or update a position; the state deactivates it |
| | `PostGetMeta_TenantPosition_Copy(copy)` | Copy active positions to another tenant, skipping existing codes; returns the rows created |
| `ITenantSchedTemplateServices` | `GetVi_TenantSchedTemplates(tenantId)` | All templates, ordered by order and name |
| | `GetMeta_TenantSchedTemplate(templateId)` | Template with its seven days |
| | `PostGetMeta_TenantSchedTemplate_Save(set)` | Create or update a template and its days in one transaction |
| | `PostGetMeta_TenantSchedTemplate_Duplicate(templateId)` | Copy as active; the name gets " copy", the code a number |
| `ITenantSchedSettingServices` | `GetMeta_TenantSchedSettings(tenantId)` | Tenant and departments: own values, steps and effective result |
| | `GetMeta_TenantSchedSetting_Effective(tenantId, deptId)` | Effective result of one level; empty `deptId` is the tenant |
| | `PostGetMeta_TenantSchedSetting_Save(save)` | Create or update a level and its rotation steps |
| | `PostGetMeta_TenantSchedSetting_UseInherited(tenantId, deptId, ustamp)` | Set a department back to inherit, keeping its row |
| `ITenantAttCodeServices` | `GetVi_TenantAttCodes(tenantId)` | All codes, ordered by order and name |
| | `PostGetTa_TenantAttCode_New` / `PostGetTa_TenantAttCode_Update` | Create or update a code; the state deactivates it |
| | `PostGetMeta_TenantAttCode_Copy(copy)` | Copy active codes to another tenant, skipping existing codes |
| `IPublicHolidayServices` | `GetVi_PublicHolidays(year)` | Active fixed entries plus the active dated entries of that year |
| | `PostGetTa_PublicHoliday_New` / `PostGetTa_PublicHoliday_Update` | Create or update an entry; the state removes it |
| | `PostGetMeta_PublicHoliday_CopyDated(fromYear, toYear)` | Copy dated entries; returns the rows created and the count skipped |
| | `GetMeta_PublicHoliday_HasDated(year)` | Whether the year has active dated entries |
| `IUnitServices` | `GetVi_Units` | Global units ordered by code |
| | `PostGetTa_Unit_New` / `PostGetTa_Unit_Update` | Save a unit |
| | `PostMeta_Unit_Delete(unitId, ustamp)` | Delete an unused unit using its update stamp |
| `ITenantProductTypeServices` | `GetVi_TenantProductTypes(tenantId)` | List types after system setup |
| | `PostGetTa_TenantProductType_New` / `PostGetTa_TenantProductType_Update` | Save a type and its state |
| | `PostGetMeta_TenantProductType_Ensure(tenantId, code, name, kind)` | Resolve or create a user-defined type |
| `ITenantProductServices` | `GetVi_TenantProducts_Children(tenantId, parentId, typeId)` | Direct children, with an empty parent for roots and optional type filter |
| | `GetVi_TenantProducts_ByType(tenantId, typeId)` | Flat list for one type |
| | `GetVi_TenantProduct_ById(productId)` | One product or null |
| | `GetMeta_TenantProduct_Search(tenantId, text, typeId)` | Up to 200 matches with paths |
| | `GetMeta_TenantProduct_Path(productId)` | Path from the root through the selected product |
| | `PostGetMeta_TenantProduct_CodesExist(tenantId, codes)` | Existing tenant codes, including inactive products |
| | `PostGetTa_TenantProduct_New` / `PostGetTa_TenantProduct_Update` | Save a product and its state |
| | `PostGetMeta_TenantProduct_AddChildren(batch)` | Add selected children atomically |
| | `PostGetMeta_TenantProduct_CopyChildren(copy)` | Copy selected direct children atomically |
| | `PostMeta_TenantProduct_EnsureSystem(tenantId)` | Ensure the reserved type and product |
| `IContactAttachmentServices` | `GetMeta_ContactAttachments(contactId)` | Active attachments with file name, type, size and date from object storage |
| | `PostGetMeta_ContactAttachment_Add(upload, content)` | Store a file and add its attachment |
| | `PostGetMeta_ContactAttachment_Replace(upload, content)` | Add a version to the file, keeping name, kind and note |
| | `PostMeta_ContactAttachment_Remove(attachmentId, ustamp)` | Deactivate an attachment; the file stays |
| | `GetMeta_ContactAttachment_Download(attachmentId)` | Current file of an active attachment |
| `ITenantEmpServices` | `GetVi_TenantEmps(tenantId)` | All employees, ordered by person name and code |
| | `GetVi_TenantEmp_ById(tenantEmpId)` | One employee, or null |
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

`TenantEmpPerson.FullName` saves the contact name and null preserves it. `TenantEmpPerson.Attachments` saves the name,
kind, note and order of the rows sent, under each row's stamp. `TenantEmpSave.IsDeptHead` true makes the employee the
head of its department, false clears the head when it is this employee, and null leaves the head alone. A wrapper that
leaves these null keeps its behaviour. `Row.cTenantPositionId` must be an active position of the tenant, or the one the
employee already holds. A new contact needs a nonblank name.

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
`tenant.dept.changed`, `tenant.position.created`, `tenant.position.changed`, `tenant.emp.created`, `tenant.emp.changed`,
`contact.attachment.created` and `contact.attachment.changed`.

Product changes use `System.Settings` events `unit.created`, `unit.changed`, `unit.deleted`,
`tenant.product.type.created`, `tenant.product.type.changed`, `tenant.product.created` and
`tenant.product.changed`. Product and type saves check the tenant, references and update stamp.
Invalid values or a changed parent return 400, missing targets return 404, and duplicate codes or stale
stamps return 409. Product setup covers a fixed hierarchy and excludes recipes and transactions.

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

## Effective schedule

A level has two inherited values. **Enable** comes from the nearest level with a non-zero value, the schedule from the
nearest level with a non-zero mode, so a department can take one from its own row and the other from above. A
sub-department walks its parent chain before the tenant. Nothing is stored: the result is calculated on every read and
names the level each value came from.

| Situation | Result |
| --- | --- |
| A tenant without a setting row | Disabled |
| Disabled at the nearest level that sets Enable | `Enabled` is false; the schedule values are not in use |
| Enabled, and no level sets a schedule | The built-in office schedule: 08:00, 60 minutes break, 8 hours Monday to Friday, weekend off |

The built-in schedule lives in code, in `TenantSchedBuiltIn`, and no template row is created for it. `PeriodDays` always
comes from the tenant level and defaults to 14.

A wrapper reads the result in-process. These are direct calls, so the wrapper authorizes its own operation:

```csharp
var effective = await settings.GetMeta_TenantSchedSetting_Effective(tenantId, deptId);
if (!effective.Enabled) return;                         // Not scheduled.
var step = effective.StepOn(workDate);                  // Rotation step on a date; null before Day 1.
var templateId = step?.TemplateId ?? effective.TemplateId;
if (templateId is null) { /* TenantSchedBuiltIn applies when Mode is BuiltIn. */ }
```

A rotation is counted from Day 1: each step lasts `StepWeeks` weeks and the cycle repeats. Dates before Day 1 have no
rotation result. Read hours from `GetMeta_TenantSchedTemplate`, using the weekday of the date the shift starts.

## Limits

This directory does not select a session's active tenant, scope claims by tenant, or maintain user tenant
membership. Each employee has one optional department. Hiring/leaving dates, additional personal data, department
history, multiple appointments, custom employment policy, bulk import and legacy mappings belong to a
consumer's personnel module. There is no department object browser or MAUI screen. Attendance setup has no scheduling
per employee, lateness, overtime, pay or leave rules, department permissions or device integration. Schedule
assignments, the roster and the attendance log do not exist yet.

## Maintainer notes

`MySqlAttendanceSetupTests` (MySQL) covers templates, settings, inheritance, rotation steps, codes, holidays, stale
stamps and tenant boundaries; `BusinessAttendanceTests` covers the four pages, the drawer, the dialog and the spin box
without rendering. The SQL Server scripts are proved by installing them twice on a local database, not by a full test
run.

`TenantPositionServicesTests` (SQL Server) and `MySqlTenantOrgTests` (MySQL) cover positions, copying, the composite
foreign key, full name, head changes and attachments, with an in-memory store for files. `BusinessPositionTests` covers
the page, dialog and editor without rendering. Offline light/dark rendering of the drawer, Positions and Employee Editor
lives in `../.artefacts/em-system/scripts/bmm-drawer-render/`.

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
