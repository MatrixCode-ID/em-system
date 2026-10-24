# Approval engine

The engine provides **Data Approval** for deferred row changes and **System Approval** for module-owned
subjects with a signing flow. **Document Approval** is reserved for physical files; registration throws
`NotImplementedException`. The engine does not own an application's document-type catalog or render
signature stamps. A module owns its data, business rules, source screen and printable document.

This redesign is a breaking change. Its implementation is being integrated; builds, SQL integration,
HTTP smoke checks and UI rendering are deliberately deferred until the engine and consuming application
code are both complete.

## Database upgrade

For a new SQL Server database, run `tables/010-core.sql`, then `tables/020-approval.sql`, followed by
`views/vi_ApprovalRequestStatus.sql`. Existing installations use
`updates/20261017-ApprovalRedesign.sql`, then the status view.

The upgrade **discards old approval requests and their children**, removes their document-type foreign
key, and creates the new schema. It preserves the application's document-type table. Re-running the
upgrade against the new schema preserves new requests. Back up anything that must be retained before
the first upgrade. Old clients and modules must be updated before deploying the new API.

The core schema has requests, Data items and fields, System steps and candidate snapshots, signatures,
comments, attachments, signer settings and ordered backups. A filtered unique index reserves a System
subject while its request is pending, approved or rejected. Data subjects may have several pending
requests. `vi_ApprovalRequestStatus` is a lightweight, stable join target for module databases on the same
SQL Server; it exposes the request identity, kind, name, opaque key, status, summary, sub-summary, active
level/steps and completion time.

## Names and registration

Definitions implement `IEmApproval`. Root Data and System classes declare `[Approval("Name")]`.
Names start with an ASCII letter, contain letters, digits and single spaces, and have at most 50
characters. The pair `(kind, name)` is unique, case-insensitively. Child Data definitions have no
attribute. Each entity has exactly one Data definition. Definitions are registered explicitly; there is
no assembly scanning at runtime.

The `EmSys.Api.Core` package includes the metadata analyzer under `analyzers/dotnet/cs`. A host consuming
the engine through project references also adds:

```xml
<ProjectReference Include="..\Em.Api.Core.Analyzers\Em.Api.Core.Analyzers.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Adjust the relative path to your checkout. The analyzer checks source and referenced assemblies:

| Error | Meaning |
| --- | --- |
| EMAPV001 | A named definition lacks its attribute |
| EMAPV002 | Invalid name format or length |
| EMAPV003 | Duplicate kind/name pair |
| EMAPV004 | Multiple Data definitions for one entity |
| EMAPV005 | A child definition declares a name |

Startup validates registrations again, including EF ownership, keys, columns, supported property types,
parent relations, deletion policy and System step names/levels. Modules loaded outside the host's
compilation cannot be checked by the analyzer; runtime registration remains authoritative.

## Data Approval

Definitions live in the module backend, where their optional database hooks can join the apply transaction.

```csharp
[Approval("Customer")]
public sealed class CustomerApproval : IEmApprovalData<CustomerRow>
{
    public string[] KeyColumns => [nameof(CustomerRow.Id)];
    public string[] TrackedColumns => [nameof(CustomerRow.Name)];
    public string[] ApproverOnlyColumns => [];
    public bool CanView(ActionRequest caller) => HasModuleAccess(caller);
    public bool CanDecide(ActionRequest caller) => HasApprovalAccess(caller);
}

public sealed class AddressApproval : IEmApprovalDataChild<AddressRow, CustomerRow>
{
    public string[] KeyColumns => [nameof(AddressRow.CustomerId), nameof(AddressRow.Number)];
    public string[] ForeignKeyColumns => [nameof(AddressRow.CustomerId)];
    public string[] TrackedColumns => [nameof(AddressRow.Description)];
}

builder.AddDataApproval<CustomerApproval>();
builder.AddDataApproval<AddressApproval>();

var result = await approval.SubmitDataAsync(
    DataChange.Create(customer).With(DataChange.Create(address)), note);
```

`KeyColumns` match the complete EF primary key in order. Child foreign-key properties pair with the
parent's key properties in order and type; differing names produce a warning so the module author can
check the ordering. The entity's owning context is the only registered context that maps it; when several
modules map the same shared table, the owner is the context from the assembly that declares the definition.
If that still leaves more than one candidate, name the owner with `AddDataApproval<TDefinition, TContext>()`.
Views and key/value stores do not have a manual load/apply fallback in this version.

The root owns `Title`, `CanView`, `CanDecide` and `CanWithdraw`. No Data claim is automatically created.
Real users with the administrator switch bypass those checks. The requester can withdraw their pending
request; a root's `CanWithdraw` may additionally allow other users. The built-in administrator and
debugger identities cannot submit, decide, withdraw or sign. Test by switching to a persisted active user.

### Create

A Create stores the **whole row** in the item's `json_object`; it does not insert module data at submit.
Child items point to their parent item inside the request. On approval, the engine deserializes and
checks the snapshot against the current model, calls `CreateKeyAsync`, inserts the parent, copies its
keys to each child's declared foreign keys, calls the child hook, and inserts the child.

`CreateKeyAsync(entity, context, ct)` and `ValidateAsync(entity, context, ct)` have no-op defaults.
Hooks use `context.DbContext`, already enlisted in the approval transaction. Assign required new keys in
the key hook; the engine does not invent module numbering rules. Validation runs on submit and apply.

After approval, each Create item's key is filled and **`json_object` is set to null**. Rejected or
withdrawn Create snapshots remain intact. Create requests are immutable; withdraw and submit again to
change them. Do not expect an approved Create to retain a second full copy of the inserted row.

### Update, delete and restore

`DataChange.Update(original, modified)` compares only `TrackedColumns`. Supply the original loaded by
the screen; reloading it on the server during submission would lose optimistic conflict detection.
Keys cannot change. Values are normalized according to type using invariant culture.

On approval, for every changed field:

- Current equals proposed: no conflict.
- Current equals original: apply the proposal.
- Otherwise: show a conflict. Each deliberate override needs its own reason.
- The row disappeared: return 409; it cannot be recreated by an override.

The engine checks every item before applying any change. An unresolved conflict records current values
without applying part of the tree. A caller who can decide saves immediately in the same transaction,
still leaving an approved request and signature; stale direct saves return 409.

`DeleteMode` is `None` by default, `Hard` to remove the submitted row, or `Soft` with a required
`DeleteStateColumn` and optional nullable `DateTime` stamp. A boolean state uses true/false. Numeric or
enum state columns require explicit `DeletedStateValue` and `ActiveStateValue`. `DataChange.Restore`
requires Soft mode, resets the state and clears its deletion stamp. Deleting a parent does not
implicitly delete children. Data requests have no reinstate operation.

`ApproverOnlyColumns` is a UI hint returned through capabilities, not a server whitelist. The engine has
no paired-column business rules. Those belong to the module UI.

### Transactions

Data application uses one SQL Server connection and transaction across core and module databases,
without MSDTC. The declared entity contexts are enlisted. Use the supplied context in hooks; unrelated
connections do not participate. Participating databases must use the same server and login. The engine
does not lock a subject for the lifetime of a pending Data request. Concurrent module/external writes
can still race the value comparison; callers must review conflict results.

## System Approval

The definition belongs to a library referenced by both clients and server:

```csharp
[Approval("Order")]
public sealed class OrderApproval : IEmApprovalSystem<OrderKey>
{
    public IReadOnlyList<ApprovalSystemStep> Steps =>
        [new(1, "Prepared By"), new(2, "Checked By"), new(3, "Review A"), new(3, "Review B")];
}

builder.AddSystemApproval<OrderApproval, OrderApprovalHandler>(); // API
builder.AddSystemApproval<OrderApproval, OrderApprovalEditor>();  // WPF

var id = await services.RequestApprovalAsync<OrderApproval>(key, "ORD-0001, Customer A", "12 items, Main Street 1");
```

A System request carries two summary lines. The **summary** is required and must contain 10 to 400
characters after trimming; the **sub-summary** is optional (up to 400 characters). When the sub-summary
is empty the request shows only one line. Reinstate copies the sub-summary of the voided request.
Data requests keep their table title as the summary and have no sub-summary. Existing installations
apply `updates/20261017-ApprovalSubSummary.sql` once, then re-run `views/vi_ApprovalRequestStatus.sql`.

Steps are built on the server. A later level opens only after every step at the preceding active level
has signed. One signature completes a step. The requester cannot supply or edit the flow.

The **System Approval Editor** uses the `Administrative Tools:Approval Signer Settings` claim and is
opened from **Tools**, next to Approval Manager, for administrators. Its
three columns show registered approvals, declared steps and a User Pool. Each step needs exactly one
primary user or **DS · Dynamic Signer**, plus zero or more ordered four-eyes backups. DS stays above the
pool filter and can be dropped on any step. Dropping DS replaces the primary; the previous primary user
becomes a backup. Other users become backups when a primary exists. Double-click adds to the selected
step. Save commits draft settings; Revert/Refresh ask before discarding changes. Inactive users remain
visible and cannot be assigned.

A scoped `IEmApprovalSystemHandler<TDefinition, TKey>` resolves DS through
`ResolveDynamicSignerAsync(step, key, requester, ct)`. Missing handlers, unhandled DS steps or inactive
resolved users fail submission with a message naming the approval and step. `ApprovalDynamicSigner`
may set `SignImmediately` for the requester at the first level. Other DS users sign through the active
step normally.

Primary users and backups are snapshotted into the request. A backup may sign immediately when its
step is active, and the signature is marked as four-eyes backup. A real IsAdmin user can sign any
active step and is marked when outside its candidates. There is no replacement-proposal workflow,
on-behalf signature, per-step claim, PDF stamping or runtime flow editing.

The engine automatically declares `core.approval:View {Name}` for readers. The requester, candidate
users and IsAdmin may also view a System request and its comments. Signing still requires an active
persisted user and an active step.

### Link, void and reinstate

The owning module stores the returned request ID on its own subject. String keys are preserved;
objects use JSON in property declaration order. The serialized key is opaque to the engine and must
have at most 400 characters. Keep its representation stable. `FindByKeyAsync<TDefinition>` is the
recovery route if saving the module link fails after creating a request.

A pending, approved or rejected System request occupies its key. Void makes a pending request `-2`
and a finished/rejected one `-3`; its signatures remain unchanged. Another request may then use the key.
Void is allowed to the requester, IsAdmin or a holder of a definition's `VoidClaims`.

Reinstate voids the current request and builds a new one using current settings and handler resolution.
The new request links to its predecessor. The module editor's
`OnReinstatedAsync(oldId, newId, key)` updates the module link. Failure to update the module link does not
undo a committed engine request; recover it through FindByKey.

Optional completion/rejection handlers run **after commit**. Their failures are logged and do not
undo a decision. The task hub always recomputes waiting counts from the current request state.

### Module editor

Implement `IEmApprovalSystemEditor` in the module's WPF main view. The engine surrounds it with the
toolbar, horizontally scrolling signer cards, decision dialogs and a collapsible right-hand
attachment/comment panel. The toolbar offers optional document preview and source navigation,
module commands, reinstate, void and refresh. Signer and comment panels have independent toggles.

`LoadAsync` receives the server-authorized state and a refresh callback. `ValidateSignAsync` and
`ValidateRejectAsync` must return `ApprovalDecisionCheck` before the signature is sent. The module
saves business input through its own actions. The engine does not validate that business input.
Preview and source buttons are shown only when the editor offers them. `ToolbarItems` supplies extra
module commands. The module is responsible for their permissions.

## API and client

All approval actions belong to `core.approval`; authorization is performed inside each operation.
GET arguments are simple fields. Filters, settings, keys and decisions travel by POST.

| Action | Purpose |
| --- | --- |
| `GetMeta_ApprovalCapabilities` | Registered names, kind, title, caller rights, waiting count and UI hints |
| `PostGetMeta_ApprovalRequests` | Filtered, paged visible requests |
| `GetMeta_ApprovalRequest` | Snapshot, current Data values, caller rights and participants |
| `PostGetMeta_ApprovalRequestsBySubject` | Visible history for one subject |
| `PostGetMeta_ApprovalDecide` | Decide a Data tree or one active System step |
| `PostMeta_ApprovalWithdraw` | Withdraw a pending Data request |
| `PostGetMeta_ApprovalSystemRequest` | Build a System request from server configuration |
| `PostGetMeta_ApprovalSystemReinstate` | Create a replacement linked to the voided request |
| `PostMeta_ApprovalSystemVoid` | Void while preserving history |
| `PostGetMeta_ApprovalSystemFindByKey` | Recover the occupying System request |
| `PostMeta_ApprovalComment` | Add a comment as an authorized active viewer or authenticated built-in administrator/debugger |
| `GetMeta_ApprovalSignerSettings` / `PostMeta_ApprovalSignerSettings` | Administer future signer snapshots |
| `PostGetMeta_ApprovalAttachment` / `GetMeta_ApprovalAttachment` | Stream request/comment files |

WPF registers System editors through `AddSystemApproval<TDefinition, TEditor>()`. Open a full-width
viewer using navigation `em.approval.editor` and `ApprovalManagerNavigationPayload` with the request ID
and `SystemViewer = true`. Approval Manager uses `em.approval.manager`. For embedded Data review,
set `Kind`, `Name`, `SubjectKey`, `Compact = true` and `WaitingForMeOnly = false`.

Open the request list from **Tools > Approval Manager** in the multi-tab layout.
The active SYSTEM DEBUGGER and administrator accounts can see this tool even before
approval capabilities load. Switch User and Simulate Login use the active account's
permissions. When opened without an explicit filter, debugger and administrator
accounts start with **Needs my action** cleared, showing all requests they can view.
Full-manager navigation by an ordinary user shows an administrator-only message without loading data.
Explicit navigation filters are preserved. Clear the checkbox and refresh to include requests awaiting other
users, subject to the server's visibility checks.

Built-in SYSTEM ADMIN and SYSTEM DEBUGGER accounts may comment on requests they can
view. Debugger comments require a debug-token identity; comments retain the actual
system author ID and display its account name. This exception does not enable signing,
submitting, withdrawing or uploading attachments as a built-in account. Existing SQL
Server installations must apply `updates/20261017-ApprovalSystemComments.sql` once.
The update removes only the comment-author user foreign key, keeps the request foreign
key and all comment history, and does not create system user records.

The manager shows a full-width paged request table with type, approval name, summary (with the
sub-summary as a muted second line when present), status, current step and requested date. Selecting a row does not load its detail.
The definition filter always includes **All approvals**, so users can return to Data
and System requests together after selecting a specific definition. Refresh preserves
that selection, including when the manager originally opened with a definition filter.
Double-click a row or choose **Open** from its context menu to open that exact request
in a separate editor tab. Data and System requests both use this flow; each request
has its own tab identity. Compact views embedded by modules continue to show detail.
Reinstating a request from its editor opens the replacement in its own tab and keeps
the original request available as history.

### MAUI (Android)

The MAUI core has its own mobile approval screens; they are designed for a phone, not ported from
the desktop layout.

```csharp
builder.AddSystemApproval<OrderApproval, OrderApprovalView>(); // MAUI; OrderApprovalView : ContentView
```

The editor is a MAUI `View` implementing the same `IEmApprovalSystemEditor`. Keep it a single
column of cards: the engine places it inside a vertical scroll, so it must not scroll itself.
`ToolbarItems` and `OpenSourceAsync` are not shown on mobile.

- **Approval Manager** (`em.approval.manager`, no claim): an engine tool, so like the desktop Tools it
  is not in the home menu of modules. It opens from the account panel, for administrators. One column of request cards with search, an *Only requests waiting for me*
  switch, pull-to-refresh and paging (30 per page). The switch starts off for SYSTEM DEBUGGER and
  administrators, on for other users, unless the navigation payload states it. It also accepts the
  hub's `Kind:Name` text parameter. Tapping a card opens the request.
- **MY TASKS** (home, above the module menu): one card per `GetMeta_UserHubTasks` row with its count
  and the age of the oldest item; tapping opens the registered wrapper or the engine editor filtered to that approval and to the
  user's own decisions. The top bar shows an inbox button with the total count while work is waiting.
  `EmApp.HubTasks` is refreshed when home opens, when the app returns to the foreground, when the
  user changes, after a decision in the viewer, and every 60 seconds while the app is in front.
  There is no notification while the app is closed.
- **Approval** (`em.approval.editor`, `ApprovalManagerNavigationPayload` with the request ID): the
  request header with status, level and requester; the module view (System) or the proposed field
  changes (Data); signer steps top to bottom; comments with a box to add one. A bottom action bar
  appears only while the user may decide: it signs or rejects the first step waiting for them, and
  tapping another step that is the user's turn moves the bar to it. Signing asks an optional note;
  rejecting requires a reason. The module's `ValidateSignAsync`/`ValidateRejectAsync` run before
  the decision is sent.
- **Preview** writes the module's PDF to the app cache and hands it to the device's PDF viewer
  (`Launcher.OpenAsync`); the client has no PDF renderer of its own.
- Not on mobile: void, reinstate, withdraw, attachments, signer settings, and approving a Data
  request whose values changed meanwhile (resolve the conflict on the desktop client).

## Attachments and hub

Enable `AddObjectStorage()` and its database schema to use attachments. The engine declares the private
managed bucket `approval-attachments`. Policy limits and quotas are enforced by object storage while
streaming. Files are linked to a request and optionally one of its comments, with an exact object version.
The upload client sends one `ApprovalAttachmentUpload` payload and a raw stream. Download and PDF
preview check visibility of the owning request. Without object storage, attachment writes return 409
and the upload controls are hidden.

MY TASKS groups waiting requests by `(kind, name)`. Data counts use the root's decision check; System
counts use candidates of active steps. Opening a group resolves its registered wrapper, with an engine-editor fallback. Counts disappear when
the request is decided. Escalation, quorum, email delivery by the engine, push notifications to
mobile devices and physical-document approval are outside this implementation.

## Verification

The redesign was verified on 2026-10-17 with SQL Server integration tests, the backend/frontend
test suites, API/WPF/MAUI builds, and HTTP calls to the actual Em.Api host with Em.Test enabled.
The generic WPF screens were also rendered in light and dark themes within an integrated host.
Direct mouse interaction and external PDF rendering remain unverified. See the
[execution report](../../plan/executed/approval-rancang-ulang-report.md) for results and limits.


## Approval wrappers

A host/module may build its own WPF and MAUI approval screens. Register the same route on each client:

```csharp
builder.AddApprovalWrapper(ApprovalKind.System, "Order", "orders.approval");
```

Register the navigation without a module or claim to make its menu available to signed-in users. The
server still authorizes every list, detail, decision, comment and attachment. An empty list is normal.
A wrapper consumes data and actions; it does not embed the full Approval Manager. Signer settings stay
in the engine. `ApprovalWrapperNavigationPayload` carries Kind, Name and WaitingForMeOnly. Implement
`IApprovalWrapperNavigationTarget` on the wrapper body to reapply this filter and refresh when its
navigation is already open.

MY TASKS resolves registered wrappers on the client. Without a wrapper it opens `em.approval.editor`
with a waiting-work filter; the editor walks the oldest matching requests. Other hub sources preserve
their target and parameter. The full Approval Manager is an administrator tool; authenticated debugger
bypass is also allowed. Compact subject reviews embedded by modules remain available to authorized
viewers. The mobile inbox sends ordinary users to home (MY TASKS).

`ApprovalQuery.DateFrom` and `DateTo` filter request dates inclusively. `OldestFirst` is optional.
`PostGetMeta_ApprovalYearMonths` returns visible year/month counts using the same filters, ignoring the
date range and paging. List rows include active step names and WaitingSince (the previous level's last
signature time, or request creation for the first level), without adding database columns.

System approvals support `PostGetMeta_ApprovalSystemSignAll` and `PostGetMeta_ApprovalSystemVoidAll`.
Mine signs only captured active steps for which the caller is primary or backup, including for admins.
A null RequestIds discovers all waiting work on the named approval independently of list dates/pages.
AllActiveSteps requires a persisted administrator and explicit request IDs. Later levels unlocked by
this call are excluded. A definition may set `AllowBatchSign => false` to require its individual client
validation; capabilities expose that flag. Batch signing does not run module client validators.

Each request commits independently; multiple captured steps on one request share its transaction.
`ApprovalBatchResult.Items` reports successful steps and skipped/failed requests. Completion handlers
run after commit. An invocation accepts at most 200 requests (including discovery); split larger explicit
selections into separate calls. Sign descriptions are optional. Every void, including void all and the
void performed during reinstate, needs 15 to 990 characters after trimming, enforced by the server and
WPF dialog. Mobile provides signing but no void/reinstate actions.
