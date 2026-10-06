# Approval engine

A guide for module authors. It covers only the public surface of the approval engine.

The engine offers two kinds of approval. Both share one set of request tables, one action service
(`core.approval`), one **Approval Manager** screen and one **MY TASKS** hub.

| | Document approval | Data approval |
| --- | --- | --- |
| Used for | Transaction documents signed by several parties (orders, contracts, …) | Changes to master data (customers, vendors, …) |
| The document/data | **Already exists**; the request gates its status | **Not changed yet**; the proposal stays in the request until approved |
| Steps | Several levels, each with parallel steps; every step has its own claim | One decision by a holder of the approve claim |
| Result | A PDF stamped with the signatures | The change is applied to the module's tables after a three-way comparison |
| Builder call | `AddDocumentApproval<TServices, TKey>(docType, flow)` | `AddDataApproval<TServices>(docType, approveClaim, flow)` |

Everything is registered on the API builder (`EmAppBuilder`) during startup, never at runtime.

## Engine vs. module responsibilities

The engine **does not know the module's tables**. Keys (including composite keys), loading data,
applying changes and validation are supplied by the module through the `flow` declaration. This lets
modules whose data lives in legacy tables (no standard columns, no ULID IDs, composite keys) use it too.
A document or entity key is a `record` whose parts are marked `[KeyPart(n)]`. The engine converts it to
its stored form and back, and module handlers receive the record as is.

### Document approval

```csharp
builder.AddDocumentApproval<OrderServices, OrderKey>("SalesOrder", flow => {
   flow.Level(1, l => l.Step("Prepared By", slot));              // the requester signs automatically on submit
   flow.Level(2, l => l.Step("Checked By", slot, distinctFrom: "Prepared By",
                             signers: async c => [...]));         // signer user IDs, read from the document
   flow.Level(3, l => {                                           // steps within one level run in parallel
         l.Step("Approved By A", slotA);
         l.Step("Approved By B", slotB, guard: GuardAsync, input: BInput());
      },
      onCompleted: c => Task.CompletedTask);                      // called when the whole level is done
   flow.Pdf(c => c.Services.RenderAsync(c.DocKey))                // Task<Stream>: the base PDF to stamp
       .PdfLayout((c, pdf) => LocateAsync(pdf))                   // optional: per-document box positions (see PDF)
       .Summary(c => c.Services.SummarizeAsync(c.DocKey))         // summary for lists and the hub
       .RequireOpen()                                             // the document must still be open on submit
       .OnSigning(...).OnSigned(...).OnFinishing(...).OnFinished(...)
       .OnRejecting(...).OnRejected(...).OnReinstating(...);
});
```

- **Claims.** Every step claim and the reader claim (`View {docType}`, see
  `EmAppBuilder.ApprovalViewClaimName`) are registered automatically on module `TServices`. Do not register
  them again. The step name becomes the claim name.
- **First step.** The requester signs it on submit, so it cannot ask for input.
- **`signers:`** fixes the signers on submit (`when:` includes a step only when its condition holds for
  that document). Other holders of the same claim may sign on their behalf (a reason is required and the
  stamp shows *on behalf of*), unless the step is `strict`.
- **`distinctFrom:`** enforces four-eyes: the two steps cannot be signed by the same person. The
  administrator switch is exempt.
- **`guard:`** blocks approval of a step with a reason, and may name an *override* claim. Holders of that
  claim may approve with a reason; the stamp is marked OVERRIDE.
- **`input:`** defines per-step input (`StepInput<TServices, TKey, TPayload>`). `Check(slot, ...)` and
  `Text(slot, ...)` define what is drawn on the PDF, `Validate` checks it, and `OnSigned` applies its effect
  to the document **in the same transaction**. On rejection, acceptable input is still drawn and an
  unacceptable payload does not fail the rejection. Whether a rejection is allowed is decided by the
  module's `OnSigning`.
- **Hooks.** `OnSigning`, `OnSigned`, `OnFinishing`, `OnRejecting` and `OnReinstating` run inside the
  decision transaction; a failure rolls everything back. `OnFinished` and `OnRejected` run after commit;
  their failures are only logged.
- **Reinstate** (withdraw, then submit again): a pending request becomes `-2`, a finished request `-3`. The
  new submission links to the old one through `ReinstateOf`. `OnReinstating` may refuse.

### Data approval

```csharp
builder.AddDataApproval<CustomerServices>("Customer", approveClaim: "Approve Update", flow => {
   // `order` is the apply order: parents before children
   flow.Entity<CustomerKey>("Customer", (c, key) => c.Services.LoadAsync(key),
         (c, request) => c.Services.ApplyAsync(request), order: 0)
      .Entity<ContactKey>("Contact", (c, key) => c.Services.LoadContactAsync(key),
         (c, request) => c.Services.ApplyContactAsync(request), order: 1)
      .Summary(c => c.Services.SummarizeAsync(c))
      .OnFinishing(...).OnFinished(...).OnRejecting(...).OnRejected(...);
});
```

- A holder of the approve claim **saves directly** without waiting for anyone (the request is recorded as
  self-approved). Anyone else leaves a request that waits for an approver.
- **Three-way comparison** per column: *original* (at submit), *current* (table content at approval) and
  *proposed*. Current = original → applied; current = proposed → skipped; anything else is a **conflict**.
  All entities are compared before any is applied. A conflict cancels that decision and is recorded so the
  approver sees the current values. The approver may **apply anyway** (`Override`, reason required, item
  marked *Overridden*), unless the entity no longer exists, in which case it can only be rejected.
- New entities: the module returns the real key from `apply`. Other entities in the request that still use
  the temporary key are updated automatically, so a parent and its children can share one request.

## Cross-database transactions

One decision uses one connection and one SQL Server transaction that spans the core database and the
module databases, **without MSDTC** (`ApprovalTransaction`). A module context takes part only when the
module service requests it through its **constructor**. A context obtained through `GetService`, or through
another module's service, is **not** enlisted and is not checked at startup. All participating databases
must be on one server with one login; `ApprovalStartupChecks` rejects the application before the first
request otherwise.

## Tables

Seven tables in the application's core database: request, step, step signer, item, item key, item column
and comment. The script is `doc/sqlscript/mssql/tables/020-approval.sql`. It also creates the document type
table referenced by the foreign keys; the document type rows themselves are inserted by the application or
module. Two parts of the schema are engine behavior, not optimizations:

1. A filtered unique index on (document type, key, version) `WHERE Stage = Pending` guards against two
   concurrent submissions.
2. Foreign keys from requester, signer, on-behalf-of and the signer list to the user table. This is what
   makes **system accounts** (the built-in admin and the debugger) unable to sign. Approval is an exception
   to "admin can do everything"; a real user with the administrator switch turned on can still sign.

The document type must already be registered in the application's document type list. An unregistered
type is rejected by the database on the first request.

## Actions (`core.approval`)

All actions are `GetMeta_`/`PostMeta_`/`PostGetMeta_`. Their claims are dynamic (per document type and
step), so the engine service is registered with `enforceClaims: false` and checks access itself.

| Action | Purpose |
| --- | --- |
| `GetMeta_ApprovalDocumentTypes` | Document types the caller may view |
| `GetMeta_ApprovalRequests(query)` | Paged list, limited on the server to types the caller may view |
| `GetMeta_ApprovalRequestsByDoc(docType, docKey)` | All requests for one document |
| `GetMeta_ApprovalRequest(id)` | Details: steps, changes, timeline |
| `GetMeta_ApprovalRequestPdf(id)` | Stamped PDF, generated on demand |
| `GetMeta_ApprovalGuard(id, step)` | Block status of a step and who may override it |
| `PostGetMeta_ApprovalDecide(decisions[])` | Approve/reject; one transaction **per decision** |
| `PostMeta_ApprovalCancel(id, reason)` | Withdraw |
| `PostMeta_ApprovalComment(id, note)` | Comment |
| `GetMeta_UserHubTasks` | Task list for the current user (hub) |
| `GetMeta_ApprovalSlotCalibration(...)` | Calibration PDF: draws every box over the real document |

**Permission to view** a request means holding any step claim in its flow, the reader claim for that
document type, or the administrator switch. A failed decision does **not** return the request summary to a
caller who may not view it.

## PDF and stamps

- `flow.Pdf(...)` produces the base PDF on submit. The engine stores it through `IBinaryStorage`
  (`builder.AddLocalBinaryStorage(path)`: a folder on the server machine without a public address; keys
  containing `\`, `.`/`..` or an absolute path are rejected).
- Every step has a **slot** (`ApprovalSlot.At(x, y, w, h, page)`, millimeters from the top left of the
  page). The list of boxes is **frozen per request on submit**; changing the declaration does not affect
  existing requests.
- **`PdfLayout`** maps declared positions to the actual positions in this document (for example a
  signature table that moves with the number of rows; `null` means the box is not in this document). It is
  called on submit and by calibration, with an in-memory copy of the PDF.
- `ApprovalSheet()` adds an approval sheet for steps without a box.
- Every signature carries a verification code; the latest code is printed in the page margin. A screen for
  checking the code has not been built yet.

## WPF UI

The **Approval Manager** (`ApprovalManager`, no DevExpress dependency) shows the same data in three
places: from the Tools menu, from the hub (filtered), and embedded in a document screen (Approval tab, with
Previous/Next when opened for a document). Modules extend it through the UI builder:

- `AddApprovalStepPanel<TView, TViewModel>(docType, step)`: input panel for one step.
- `AddApprovalInfoPanel<TView>(docType, steps, input, order)`: additional information panel.
- `AddApprovalDocumentOpener(docType, navigationName, payloadFactory)`: the *Source document* button,
  enabled only when the caller may open the target screen.

**Compact mode.** Set `ApprovalManagerNavigationPayload.Compact = true` to embed the screen in a narrow
space, such as a flyout in a module screen. It shows a single column with the proposed changes and the
decision. The request list appears only as a picker when there is more than one request; links to other
screens (Open, Open tab, Source document) and info cards are hidden; the timeline stays collapsed until
opened. Hosting the flyout is up to the consuming screen (pattern: `sheetScrimStyle` + `sheetToggleStyle`).

The **MY TASKS** hub merges every registered `IHubTaskSource` (`AddHubTaskSource<T>()`); the approval
source is registered automatically. The list is **computed**, not stored, so a task disappears from
everyone else's list as soon as one person completes it. The approval controls and hub are not available
in MAUI yet; the client services are.

## Known limitations

- The conflict comparison reads the values, then the module handler reads the row again without a row
  lock, leaving a small window between the two.
- Module hooks that obtain a context through `GetService` are not part of the decision transaction.
- System accounts (built-in admin, debugger) cannot submit or sign; test with real users.
