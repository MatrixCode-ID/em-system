# Em.Test test module

`Em.Test` is a sample module that doubles as a test tool. It uses almost every engine feature, so those
features can be exercised from the UI without writing a business module. It is also a complete reference
for authors of new modules: contracts, server service, client service, UI model, screens, claims and
approval.

## Projects

`src/modules/Em.Test/`

| Project | Contents |
| --- | --- |
| `Em.Test.Models` | Entities, DTOs, enums and the `ITestServices` contract (module name `test`, claim list) |
| `Em.Test.Api` | `TestServices` (server), its own data context, sample PDF, hub source, two approval flows, `AddTestModule` |
| `Em.Test.Models.Ui` | UI model `TestItem` (change tracking, undo, new rows) |
| `Em.Test.Wpf` | `TestService` (client), test screens, approval panels, `AddTestModule` |

It is wired into `Em.Api` (`builder.AddTestModule()`, plus `AddLocalBinaryStorage` for approval PDFs) and
into `Em.Ui.Wpf`. To remove it from a real application, delete the two `AddTestModule` lines and their
`ProjectReference` entries.

## Running

1. Run `doc/sqlscript/mssql/tables/900-emtest.sql`, then `views/vi_TestItem.sql` and
   `views/vi_TestDoc.sql` on the core database (safe to run again). The scripts create the test tables and
   views and two approval document types (`EmTestDoc`, `EmTestItem`).
2. Start `Em.Api` and `Em.Ui.Wpf`, sign in, and open the **Em Test** menu. Every screen requires the claim
   `test:Run Tests` (administrators and debug mode pass).

| Screen | Navigation | Covers |
| --- | --- | --- |
| Em Test Console | `test.home` | Probes, self-test and launcher |
| Test Items | `test.items` | Lists, paging, UiModel, data approval |
| Test Documents | `test.docs` | Document approval and PDF stamp |
| Test Tasks & CDN | `test.tasks` | Business tasks and CDN files |
| Test UI Lab | `test.ui` | PDF viewer, navigation, dialogs, controls |
| Test Item Editor | `test.items.editor` | Editor with payload (opened from Test Items) |
| Test Child | `test.ui.child` | Navigation lifecycle probe (opened from Test UI Lab) |

## Claims

| Claim | Used for |
| --- | --- |
| `Run Tests` | Opening the screens and calling actions without their own claim |
| `Edit Items` | Adding and changing items and documents (including batch) |
| `Delete Items` | Deleting items and documents |
| `Run Probes` | Probe actions narrower than module access |
| `Approve Item Change` | Approving proposed item changes; holders save directly |
| `Prepared By`, `QA Check`, `Approved By A`, `Approved By B`, `View EmTestDoc` | Steps and reader of the document flow (registered automatically) |

To test claim gates, create one non-admin user and one role, grant the claims one at a time in Role
Manager, and run the self-test. The expected results adapt to the current user (an expected 403 counts as
a pass).

## Features and where to test them

| Engine feature | Screen / button |
| --- | --- |
| GET/POST actions, simple parameter binding, DTO in query, positional body, arrays | Console > Actions |
| Public actions, per-action claims, admin-only, self-or-admin | Console > Session & claims |
| Status failures (400–503) and unexpected exceptions (must be a plain 500) | Console > Actions |
| Action timeout (5 seconds) and no timeout | Console > Actions |
| Upload and download streams with hash/byte comparison | Console > Actions > Streams |
| Self-test: every probe with its expected result | Console > Self-test |
| Table CRUD, views, batch, paging, search with DTO | Test Items |
| UiModel: dirty tracking, undo, reload, new rows; editor with payload; refuse leaving with unsaved changes | Test Item Editor |
| Data approval (propose add/change/delete, save directly with the approve claim) | Test Items, Test Item Editor |
| Document approval: levels, parallel steps, four-eyes, guard + override, step input, PDF stamp, withdraw | Test Documents + Approval Manager |
| Approval panels: QA step input, info card, Source document button, hub | Approval Manager, MY TASKS hub |
| Personal/global business tasks, JSON/file results, cancel, failure, 409 conflict, hub | Test Tasks & CDN |
| CDN: upload, list, archive (business task), delete | Test Tasks & CDN > CDN |
| PDF viewer: from server, from disk, load failure, same title twice, from drag and drop | Test UI Lab > PDF viewer |
| Navigation: editor with payload, focus an already open title, missing payload, unknown navigation, manager from editor, title change, lifecycle | Test UI Lab, Test Child |
| Dialogs: message box, text input, password, exception details | Test UI Lab > Dialogs |
| Controls: NumericBox, wait overlay, waiting button | Test UI Lab > Controls |
| Drag and drop: files from Explorer (PDF only), between controls | Test UI Lab > Drag & drop |
| Light/dark theme and branding | Test UI Lab |

## Automated tests

- `python scripts/_py/em-test-http-test.py`: server-side HTTP tests (parameters, failures, timeouts,
  claims, streams, CRUD, business tasks, documents). It needs `EM_PASSWORD` for an account that can sign in;
  details are in the script header.
- The Console **Self-test** runs the client-side equivalent of almost all of them with the signed-in
  session.

## Known limitations

- System accounts (built-in admin, debugger) cannot submit or sign approvals because the engine requires a
  real user. Test approval flows with a regular user.
- No MAUI screens, and no test for a second database connection (`AddExtraDbConn`).
