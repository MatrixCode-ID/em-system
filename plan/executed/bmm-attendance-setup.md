# Plan: attendance setup in the Business Module Manager

Status: **executed 2026-11-07; see the execution report at the end.** Written 2026-11-07. All decisions below are final (user answers of
2026-11-07); the executor must not stop to ask. If something not covered appears, pick the choice most consistent with
this plan and record it in the execution report.

Idea and history: [tenant-attendance-schedule.md](../../doc/ideas/tenant-attendance-schedule.md). The consumer host has
its own plan in its repository, at `plan/executed/attendance-setup.md`; it installs the schema and verifies the result.
This repository is public: no product, server or secret names in code, docs or commits.

## Goal

Add four setup pages to the Business Module Manager (BMM): **Scheduling settings**, **Schedule templates**,
**Attendance codes** and **Public holidays**. They come with tables, views, services, client proxies and tests.

## Out of scope

- **Schedule assignments, Roster and Attendance log.** They will be discussed again in more detail. Their frames stay on
  page `30 Attendance` until they move to the host's design. No table, service or screen for them here.
- Scheduling settings for one employee. The level is deferred with its screen, so `ta_TenantEmp` is not touched.
- Rules for lateness, overtime and pay, leave, department permissions, MAUI screens, device integration.
- Task scheduler and integration system: neither exists yet and nothing here depends on them.

## Decisions

| # | Decision |
| --- | --- |
| 1 | The four pages live in BMM under the single BMM claim, in module `Defaults.BusinessModuleName`. This revises the 2026-11-06 note that the attendance guide stays out of BMM: setup is BMM, workflows stay in the host. |
| 2 | The UI goes straight to the real screens, without a revised mockup (see Mockup UI). |
| 3 | Each weekday can have its own break; empty uses the template's break. The short last day is one row with break 0. |
| 4 | Work date means the date a shift starts. A weekday row describes the shift that starts on that weekday. The end time is calculated, never stored. |
| 5 | Only the break duration is stored, not its clock time. |
| 6 | Tenant Enable starts as Disabled. A tenant with no setting row is Disabled. |
| 7 | An enabled tenant with no default at any level uses the built-in office schedule from code: 08:00, 60 minutes break, 8 hours Monday to Friday, weekend off. It is shown as **Built-in**. No template row is created. |
| 8 | Levels here are tenant, department and sub-department. A sub-department inherits from its parent chain before the tenant. Effective values are calculated, not stored. |
| 9 | A rotation is part of a setting, not a reusable list. Its Day 1 belongs to the rotation; dates before Day 1 have no rotation result. |
| 10 | Nothing is physically deleted. Surplus rotation steps go to the inactive state and are reused by order. |
| 11 | Public holidays are global, with fixed (month and day) and dated entries. Collective leave is only a marker, with no behaviour. Dated holidays are entered per year; the engine calculates no lunar calendar. |
| 12 | Attendance codes follow Positions: category enum in code, Copy from tenant, no meaning for payroll. |
| 13 | Tests: MySQL integration tests; the SQL Server scripts are installed on a local database, without running the full tests there. Build, view model and XAML text tests run through RunBay. Visual testing stays pending until the user asks. |
| 14 | Commits: a baseline first, then one commit per step below, on `work-bench`. No push. |

## Prerequisite

The earlier BMM work (Positions, drawer, Contact full name, Head of department, Attachments) is uncommitted. Review it and
commit it as the baseline first, separately from this plan. The user approved this on 2026-11-07. Approving execution of
this plan is the explicit approval to change the engine.

## Schema

New files, the same names under `doc/sqlscript/mssql/` and `doc/sqlscript/mysql/`:
`tables/110-attendance.sql` (re-runnable, no seed) and `views/vi_TenantSchedTemplate.sql`, `views/vi_TenantSchedSetting.sql`,
`views/vi_TenantAttCode.sql`, `views/vi_PublicHoliday.sql`. No `updates/` script: every table is new. Column order and
standard columns (`ustamp`, `datestamp`, `json_object`) follow `090-tenant-org.sql` and the Dahlia Convention.

**`ta_TenantSchedTemplate`**: `cTenantSchedTemplateId` char(26) PK, `cTenantId`, `cTenantSchedTemplateCode` varchar(10),
`cTenantSchedTemplateName` varchar(255), `cTenantSchedTemplateStart` time, `cTenantSchedTemplateBreak` int (minutes),
`cTenantSchedTemplateState` int (0 active, -1 inactive), `cTenantSchedTemplateOrder` int default -1,
`cTenantSchedTemplateNote` varchar(500) NULL. Unique `(cTenantId, code)` and `(id, cTenantId)`. FK to `ta_Tenant`.

**`ta_TenantSchedTemplateDay`**: `cTenantSchedTemplateDayId` PK, `cTenantSchedTemplateId`, `cTenantSchedTemplateDayWeekday`
int (1 Monday to 7 Sunday), `cTenantSchedTemplateDayHours` decimal(4,2) (0 is a day off), `cTenantSchedTemplateDayBreak`
int NULL. Unique `(template, weekday)`. Always seven rows, created with the template.

**`ta_TenantSchedSetting`**: `cTenantSchedSettingId` PK, `cTenantId`, `cTenantDeptId` NULL, `cTenantSchedSettingKey`
char(26) (the department id, or the tenant id for the tenant row), `cTenantSchedSettingEnable` int (0 inherit, 1 enabled,
-1 disabled), `cTenantSchedSettingMode` int (0 inherit, or Built-in on the tenant row; 1 template; 2 rotation),
`cTenantSchedTemplateId` NULL, `cTenantSchedSettingStepWeeks` int NULL, `cTenantSchedSettingCycleStart` date NULL,
`cTenantSchedSettingPeriodDays` int NULL (tenant row only; empty means 14). Unique `(cTenantId, key)` and `(id, cTenantId)`.
The key column exists because SQL Server and MySQL treat NULL differently in a unique constraint. Composite FKs
`(cTenantDeptId, cTenantId)` and `(cTenantSchedTemplateId, cTenantId)`.

**`ta_TenantSchedSettingStep`**: `cTenantSchedSettingStepId` PK, `cTenantSchedSettingId`, `cTenantId`,
`cTenantSchedSettingStepOrder` int, `cTenantSchedTemplateId`, `cTenantSchedSettingStepState` int. Unique `(setting, order)`.
Composite FKs to the setting and the template, both with `cTenantId`.

**`ta_TenantAttCode`**: `cTenantAttCodeId` PK, `cTenantId`, `cTenantAttCodeCode` varchar(50), `cTenantAttCodeName`
varchar(255), `cTenantAttCodeCategory` int (1 present, 2 paid absence, 3 unpaid absence, 4 day off),
`cTenantAttCodeState` int, `cTenantAttCodeOrder` int default -1, `cTenantAttCodeNote` varchar(500) NULL.
Unique `(cTenantId, code)`.

**`ta_PublicHoliday`**: `cPublicHolidayId` PK, `cPublicHolidayName` varchar(255), `cPublicHolidayKind` int (1 fixed, 2 dated),
`cPublicHolidayMonth` int NULL, `cPublicHolidayDay` int NULL, `cPublicHolidayDate` date NULL, `cPublicHolidayCollective` bit,
`cPublicHolidayState` int, `cPublicHolidayNote` varchar(500) NULL. Indexes on the date and on `(month, day)`.

**Views.** `vi_TenantSchedTemplate` adds `ccTenantSchedTemplateWeekHours`. `vi_TenantSchedSetting` adds the department and
template names. The other two select the table with no heavy calculation.

## Steps

### 1. Models and contracts

In `src/shared/Em.Libs/Api.Core.Models`: the six `ta_` models, four `vi_` models and the sets below. Use `TimeSpan` for the
start time. In `src/shared/Em.Libs/Shared`: `TenantAttCodeCategory`, `TenantSchedEnable`, `TenantSchedMode`, `PublicHolidayKind`
and `TenantSchedBuiltIn` (decision 7).

| Interface | Action | Behavior |
| --- | --- | --- |
| `ITenantSchedTemplateServices` | `GetVi_TenantSchedTemplates(tenantId)` | All templates, ordered by order and name |
| | `GetMeta_TenantSchedTemplate(templateId)` | Template with its seven days |
| | `PostGetMeta_TenantSchedTemplate_Save(set)` | Create or update a template and its days in one transaction |
| | `PostGetMeta_TenantSchedTemplate_Duplicate(templateId)` | Copy with a new code; the name gets " copy", the code a number |
| `ITenantSchedSettingServices` | `GetMeta_TenantSchedSettings(tenantId)` | Tree rows for tenant and departments: own values, steps, effective result |
| | `GetMeta_TenantSchedSetting_Effective(tenantId, deptId)` | Effective result of one level, for in-process use by a host |
| | `PostGetMeta_TenantSchedSetting_Save(save)` | Create or update a level and its steps |
| | `PostGetMeta_TenantSchedSetting_UseInherited(tenantId, deptId, ustamp)` | Sets a department row back to inherit, keeps the row |
| `ITenantAttCodeServices` | `GetVi_TenantAttCodes(tenantId)` | All codes |
| | `PostGetTa_TenantAttCode_New` / `PostGetTa_TenantAttCode_Update` | Create or update; the state deactivates |
| | `PostGetMeta_TenantAttCode_Copy(copy)` | Active codes to another tenant, skipping existing codes |
| `IPublicHolidayServices` | `GetVi_PublicHolidays(year)` | Fixed entries plus the dated entries of that year |
| | `PostGetTa_PublicHoliday_New` / `PostGetTa_PublicHoliday_Update` | Create or update; the state removes an entry |
| | `PostGetMeta_PublicHoliday_CopyDated(fromYear, toYear)` | Copy dated entries, same month and day. Skips an entry already there, and 29 February into a non-leap year. Returns the rows created and the count skipped |
| | `GetMeta_PublicHoliday_HasDated(year)` | Whether the year has active dated entries, for the warning |

### 2. Backend

In `src/backend/Em.Api.Core/Api/Business`, following `TenantPositionServices`: a serializable transaction, `TenantOrg.Save`,
`TenantOrg.Stamp`, `ActionException` with 400/404/409, `[Module(Defaults.BusinessModuleName)]` and
`claim: ITenantServices.ManagerClaim` on every action. Copy Attendance codes from the Positions copy. Register the services
in `AddBusinessModuleManager` and add the sets to `ApiCoreContext`, including the provider metadata list.

Audit in group `System.Settings`: `tenant.sched.template.created/changed`, `tenant.sched.setting.changed`,
`tenant.attcode.created/changed`, `public.holiday.created/changed`.

Validation:

- Template: code and name required, state valid, exactly weekdays 1 to 7, hours from 0 to 24, breaks at least 0, and hours
  plus break at most 24 for each day. A template in use can be deactivated; settings keep it, and the page warns.
- Setting: the department belongs to the tenant. Tenant Enable is 1 or -1, department Enable is 0, 1 or -1. Mode 1 needs a
  template of this tenant, active unless it is the one already saved. Mode 2 needs at least two active steps, step weeks of
  at least 1 and Day 1. Fields of an unused mode are cleared. Period days from 1 to 366, on the tenant row only.
- Effective result: load the departments once and walk the parent chain in memory. Enable comes from the nearest non-zero
  value, the schedule from the nearest non-zero mode. The result names the source level, for the Effective result text.
- Holiday: name required; fixed needs a valid month and day (29 February allowed); dated needs a date. The same active name
  on the same date or month and day answers 409.

### 3. Client

Four client services in `src/shared/Em.Ui.Core`, following `TenantPositionClientService`, registered in `EmApp.Statics.cs` for
WPF and MAUI. MAUI gets proxies only.

### 4. WPF

- Add a reusable spin box to `src/shared/Em.Ui.Wpf.Core/Controls`, with Material styling: value, minimum, maximum, step, text
  entry and spin buttons. The core has no such control and stays free of DevExpress. Used for hours, breaks, weeks per step
  and period days.
- `BusinessModuleManagerVm`: new tabs and a third drawer folder, **Attendance**, with the four pages. Extend the discard,
  refresh, tenant choice and busy handling already there.
- New view models beside `BusinessModuleManagerPositionVm.cs`: `...SchedSettingVm.cs`, `...SchedTemplateVm.cs`,
  `...AttCodeVm.cs`, `...HolidayVm.cs`. New pages go in `BusinessModuleManager.xaml`, in the shape of Positions. Add
  `Dialogs/CopyAttCodesDialog`, following `CopyPositionsDialog`.
- Follow the WPF rules: `RegisterCommand`, `Commands[...]`, `...Allowed` methods, shared styles, no inline palette, confirmed
  discard of unsaved changes, en-US date and time.

### 5. Documentation

- `claude.md` section "Cakupan BMM dan modul wrapper": the four setup pages are BMM; assignments, roster and log stay in the
  host. Update `doc/design/CLAUDE.md` and `doc/design/README.md` the same way, with the corrections below as a known gap.
- `doc/engine/engine-business-module-manager.md`: scope, install scripts, desktop workflow, service table, audit kinds and the
  note that an existing installation must run the scripts before starting the new API. The first call fails without the
  tables, since there is no startup schema check.
- Add a guide section on the effective result, the built-in schedule and how a wrapper reads them in-process.

### 6. Tests and review, after all code is written

Register RunBay actions with verbose console output, using no filter or redirect that hides progress:

- `test-bmm-attendance-setup`: build the backend and frontend, run `Em.Ui.Wpf.Core.Tests` for the BMM classes and the new
  MySQL tests through `EM_TEST_MYSQL`.
- `install-attendance-sql-server`: run the five scripts twice on the local SQL Server test database, to prove they install
  and re-run. No full SQL Server test run.

New tests: `MySqlAttendanceSetupTests` for templates, days and duplicate, settings, inheritance through a parent chain,
built-in fallback, rotation validation, step reuse, code copy, holidays, stale stamps, tenant boundaries and rollback.
`BusinessAttendanceTests` for the four view models, the drawer and XAML loading without rendering, and a spin box test.
Then review the diff and fix what it finds.

## Mockup UI

**No approved mockup. User decision 2026-11-07: build the real UI directly, as an exception.** Page `30 Attendance` of
`doc/design/BusinessModuleManager.figcraft` is the content reference, with these corrections, which the real UI follows:

| Screen | Correction |
| --- | --- |
| All | The drawer folder has four pages, not seven |
| Schedule templates | Add the **Code** field and a break per weekday. Remove the Effective result panel and the Enable text, copied from another screen. Days off is derived from 0 h, not entered |
| Scheduling settings | Remove `New position` and `Deactivate`. Add **Period days** on the tenant level. Rotation steps can be added and removed. Tenant mode offers Built-in |
| Attendance codes | No correction |
| Public holidays | No correction |

Updating the frames is separate. Checked on 2026-11-07: the page exists with Light and Dark frames and was marked for review
only, so the executor records this exception in the report instead of treating it as a mismatch.

## Done means

- Six tables and four views exist for both databases, with the same names.
- The four pages work against MySQL through the consumer host plan.
- The consumer host plan's verification has passed, or what remains is listed in the report.
- This plan moves to `plan/executed/` with a report: commits, run ids, tests and pending visual checks.

## Execution report (2026-11-07)

All six steps are done. The prerequisite baseline was already committed as 64c0268, so none was made. No push.

**Commits on `work-bench`:** 20f89c3 schema, models, contracts; 72f6b3e services; 93a3497 client registration; f3f38e7 WPF;
b7465c9 documentation and tests; 827269e tracking fix; plus the closing commit that moves this plan. The client proxy
files landed in 72f6b3e with the services, not in 93a3497.

**RunBay runs:**

| Action | Run id | Result |
| --- | --- | --- |
| `test-bmm-attendance-setup` | `20261107-110115-test-bmm-attendance-setup-c925` | Backend and frontend build; 14 MySQL tests and 48 WPF tests passed |
| `install-attendance-sql-server` | `20261107-105410-install-attendance-sql-server-9725` | Five scripts ran twice on a temporary local database: 6 tables, 4 views, 8 foreign keys; database removed |
| `test-multi-database-regression` | `20261107-105944-test-multi-database-regression-27df` | Added, not required: 44 + 286 + 154 tests passed, 4 of the 154 skipped |
| `build-bmm-attendance-maui` | `20261107-110108-build-bmm-attendance-maui-803a` | Added, not required: MAUI core builds with the proxies |

The first run, `20261107-105246-test-bmm-attendance-setup-2f9f`, failed because the local MySQL server was stopped; it
was started through RunBay and left running. The second, `20261107-105418-test-bmm-attendance-setup-f115`, found a real
defect: 6 tests failed because the core context reads without tracking, so changes to rows read by the new services were
not saved. Fixed in 827269e with `AsTracking()`.

The consumer host verification passed; its report is in that repository's `plan/executed/attendance-setup.md`.

**Decisions the plan did not cover:**

- The plan says the core has no spin box, but `NumericBox` exists. It is integer-only and always holds a value, while
  hours need decimals and a day break may be empty, so `SpinBox` was still added and holds a nullable decimal.
- `vi_TenantSchedTemplate` also has `ccTenantSchedTemplateEndsNextDay`, for the "ends next day" caption of page
  `30 Attendance`.
- `TenantSchedEffective.StepOn(workDate)` gives decision 9 a place in code: it answers empty before Day 1.
- A deactivated template may stay in a rotation that already has it, as in template mode, but cannot be chosen anew. The
  warning is on Scheduling settings, from `HasInactiveTemplate`.
- `GetVi_PublicHolidays` returns active entries only, since Remove should hide an entry. Remove asks for confirmation.
- Copy dated from year uses a year box in the toolbar, not a dialog. The toolbar offers three years either side of today.
- The duplicate holiday check compares entries of the same kind. Years are limited to 1900 to 2199.
- Period days on a department answers 400 rather than being cleared. When a level is disabled, the effective result keeps
  its schedule values, and `Enabled` says they are not in use.
- `UseInherited` for a department without a row changes nothing and answers empty.
- Scheduling settings reloads each time its page opens, so templates changed on their own page appear.
- The seven audit kinds are also listed in `SystemAudits`.

**Mockup check:** status "none", exception of 2026-11-07 confirmed from this plan and the idea note. Page `30 Attendance`
was read from `doc/design/BusinessModuleManager.figcraft`: it has the seven frames, matching what the plan recorded. There
is no mismatch.

**Pending:**

- Visual checks of the four pages, `SpinBox` and `CopyAttCodesDialog`, light and dark, until the user asks. Real-window
  interaction is also unproven: step combo boxes, the date pickers and typing in the spin box.
- The attendance services have not run on SQL Server; its scripts were only installed, by decision 13.
- Updating the frames of page `30 Attendance`, recorded as a known gap in `doc/design/README.md`.
