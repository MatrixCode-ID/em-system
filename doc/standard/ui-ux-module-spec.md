# UI/UX standard for module development

Version: **1.0 — 27 October 2026**. Guidance for module UI/UX from discovery to handoff and verification.
Companions: [MAUI experience components](../engine/engine-maui-experience.md), the WPF rules in
[`claude.md`](../../claude.md) (UserManager reference layout, MVVM and Material tokens) and the `CLAUDE.md`
of the module being changed.

## 1. Scope and status

Applies to new modules, imported modules and UI/UX changes in modules built on the Em engine. The mobile
profile is MAUI Android with a Material 3 Expressive direction; the desktop profile follows the WPF rules
and the host product's layout standard. A MAUI module does not have to imitate a desktop grid on a phone.
What must stay equivalent across platforms is the meaning of data, permissions and business results; the
interaction follows the platform.

This document is a design and review contract. Example layouts, token aliases and patterns that the
engine does not provide yet are design targets, not claims that a control or API exists. Resources that do
exist are listed in the [MAUI experience guide](../engine/engine-maui-experience.md). Code changes still
follow the authorized request or plan, the host's mockup status and its workflow. More specific existing
rules take precedence.

A host product may keep its own copy of this standard with product-specific business rules; the generic
contract here stays product-neutral.

## 2. Discovery before design — UX-M01

Document the main tasks, users/roles, frequency, device/network context, source of business rules and
error risks. The deliverable can be a single module specification; avoid empty documents that only copy
this checklist.

| Question | Minimum result |
| --- | --- |
| Who and why? | Roles, goals, when the module is used, limits of their rights |
| Which objects? | Identity understood by the user, data/document type, status and owner |
| Which main tasks? | 1–3 critical journeys with start, end and definition of success |
| What can go wrong? | Wrong data, changed permissions, conflicts, lost network, partial results |
| What already exists? | Shared UI, pickers, formatters, services, approval engine and approved mockups |
| What are the mobile limits? | Available functions, desktop-only functions, preview/download, offline behaviour |

When importing from a legacy application, use it as the source of functions and rules, not as an
aesthetic to copy. Separate source facts, user decisions and assumptions still to be answered. Do not add
business states or fields only because they look common in ERP software.

## 3. Design package and screen contract — UX-M02

Before implementation every screen has the following specification.

| Part | Content |
| --- | --- |
| Identity | Screen name, planned navigation ID, module/owner, platform |
| Entry/exit | Menu, payload, deep link if any, Back/Home/close, navigation result |
| Users/permissions | Screen access and per-action/field permissions, empty/forbidden/session expired |
| Content | Fields/columns, labels, format/units, priority, conceptual source, reading order |
| Actions | Primary/secondary/overflow, enabled conditions, effect, confirmation, retry |
| Data | Scope, default order, filter/search, paging, load/refresh, stale/conflict |
| States | Initial/loading/content/empty/no-result/error/stale/partial/saving |
| Accessibility | Name/role/state, headings, focus, gesture alternatives, targets, scaling |
| Layout | Compact/medium/expanded, short height/IME, overflow, sticky actions |
| Design evidence | Mockup, states covered, approval date/state, exceptions |
| Acceptance | Assessable Given/When/Then scenarios; owner of the pass evidence |

A plan that touches UI records its mockup status as the host workflow requires: an existing mockup with
its path and approval state, or none with the user's decision. Decide the mobile mockup medium during
design; a desktop mockup does not prove Android reflow.

## 4. UI architecture and ownership — UX-M03

- The host provides chrome, account, theme and root navigation; modules do not recreate them.
- Modules register services and navigation through the engine builder pattern, never through ad hoc routes
  that bypass lifecycle or claims.
- View models derive from the engine MVVM base for their platform and register commands through the
  established pattern. All business actions go through commands/services; code-behind is limited to view
  wiring or required native gestures.
- UI thread, cancellation/generation and release follow the engine. A released instance must not receive
  old async results or change the active screen.
- Global needs are discussed as shared engine components. Module helpers serve only their own module.
- WPF and MAUI may differ in arrangement. Do not apply WPF ListBox/grid/window patterns on Android.

## 5. Manager/queue pattern — UX-M04

Suggested mobile information order:

1. Title/context from the host; the module adds a scope summary when needed.
2. Search/filter and the scope of any bulk action.
3. Virtual list, with loading/error/stale states close to its data area.
4. Paging/load more as contracted, result count and last successful refresh time.
5. Main action in a stable position for the task; secondary actions go to overflow when narrow.

Cards show object identity, the most important summary, status, and time/requester context. Summary lines
may be limited when full detail is easy to open. Status and values that drive a decision must never live
only inside truncated text. Row action and selection must be distinguishable; long press/swipe are extra
paths, never the only one.

## 6. Search, filter, paging and selection — UX-M05

| Aspect | Contract to decide |
| --- | --- |
| Search | Searched fields, local/server, case/trim, submit/debounce and clear |
| Filter | Default value, active indicator, reset, persistence and effect on batch actions |
| Sort | Stable default order and user choice when relevant |
| Paging | Page size, meaning of total, data changing during paging, returning to position |
| Selection | Single/multi, selected count, stable identity, effect of page/filter/tab changes |
| Refresh | Selection/context preserved, invalidation of items no longer eligible |

No active Search field without a function. Queries must not include destinations or data the user is not
allowed to see. No-result states name the query/filter and offer clear. Data not loaded yet is not an
empty result.

Decide selection explicitly. The default for new modules is explicit IDs with a visible count; changing
filter/tab clears selection unless the approved design says otherwise and still shows the scope. "Select
all" states whether it means the current page, filtered results or all server results. Total, loaded and
selected counts mean different things.

## 7. Detail/editor pattern — UX-M06

Information order: identity/status → main data → detail groups → history/attachments/comments → actions.
For reviewers, information that affects the decision appears before the action area, without requiring a
PDF as the only way to understand the object.

| Area | Standard |
| --- | --- |
| Identity | Number/code, main object and status always clear; new and saved payloads distinguished |
| Read/write | Mode and editable/read-only fields follow status/permission; not colour alone |
| Fields | Label, help, required, default, format/range, error and conceptual source |
| Repeated detail | Virtualized or bounded; numbers and units stay aligned and readable |
| Actions | Save/Approve/Sign context is clear; the primary action does not compete with many filled buttons |
| Unsaved changes | Back/Home/close/context switch handle unsaved changes as designed |
| Conflict | Show latest and submitted data and the recovery step; the server decides |

In compact windows, label/value columns stack when text is large or space is narrow. Do not force desktop
column ratios. A sticky action bar holds only relevant actions, grows with large fonts and respects the
navigation bar and IME. When content is not ready or the server denies permission, decision actions are
disabled with a readable reason.

## 8. Actions and bulk operations — UX-M07

Every action defines trigger, eligibility, scope, input, confirmation, progress, result and recovery. UI
decisions never replace server eligibility.

| Phase | Required |
| --- | --- |
| Before | Object/action name, count for batches, persistent scope near the button |
| Confirmation | Snapshot of count/scope, impact, reason when required, explicit action and Cancel |
| In progress | Prevent double submission, real or indeterminate progress, navigation/cancel as supported |
| Done | Success/failure/skipped separated; refresh status and list items that need review |
| Timeout/disconnect | The result may already be processed; check status before retrying a mutation |

Confirmation buttons use action words (`Approve 12 requests`), not just `Yes`. A batch does not expand to
items or approval levels that appear later. A partial operation is never reported as full success. Long
result details use a browsable list; a summary dialog is not the only place for results that are easy to
lose. Use the engine `FeedbackView`/`BatchResultPanel` on MAUI.

Preserve the existing business guards of the module and document them in its README. Backend capabilities
and claims decide; do not add claims or loosen guards to fix presentation.

## 9. Pickers and cross-module reuse — UX-M08

Data selection uses the picker owned by the domain module, with the caller's filters/eligibility passed
in. Do not copy a picker dialog into another module. When a platform lacks the picker (for example a
mobile picker that only exists on desktop), design a shared contract and get a decision; do not assume a
desktop dialog can be used directly. The engine [contact picker](../engine/engine-contact-picker.md) is the
WPF reference.

A picker states what is being chosen, active filters, search/paging, why an item is not eligible, the
current choice and the result of Cancel. Cancel does not change the caller's value. The selected value is
the object identity; display text is explicit and never a CLR type name. WPF passes the dialog owner.

## 10. Documents, attachments, comments and history — UX-M09

| Feature | Standard |
| --- | --- |
| Preview/PDF | Loading, document type/name, error and a way back; report rights checked; clear fallback when the device has no viewer |
| Upload/download | Size/type/limit explained, progress/cancel when supported, success only after the stored reference is confirmed |
| Comments | Clear input and send action, double-send prevented; errors keep the text, author/time readable |
| History | Active request distinguished from past decisions, actor/time/reason, stable order |
| Data changes | Old/new values, conflict status and object clear; not only red/green |

Do not show stack traces or database names as user guidance. Technical diagnostics can be separated from
actionable messages. An attachment is not uploaded just because local progress reached 100%.

## 11. States, accessibility and lifecycle — UX-M10

Every data area has these states or records why one does not apply: initial, first load, content,
refreshing (keep old data when safe), empty, no search results, error without data (with Retry), stale
after a failed refresh (timestamp not advanced), forbidden/session expired, partial success and conflict.
Add business-specific states only when the real contract has them; do not force Draft/Submit/Approved on
modules that lack them.

Check navigation while a request is pending, returning from a viewer, rotation, background/resume and
activity recreation. Old load results never overwrite a new payload. Refresh metadata advances only after a
successful load. Old data after a failed refresh is marked; batches are limited or revalidated for
eligibility.

A card's accessibility name contains at least identity, status and action. A checkbox names the object it
selects; a tab states its selected state; selection counts are announced without reading the whole list.
Business buttons stay available to accessibility services when the user cannot long press. Test text at
100/150/200%, contrast, TalkBack, keyboard and gesture alternatives; respect the system animation scale.

## 12. Module specification template

Copy this template into the **module design document**. Undecided recommendations belong in the host's
idea notes; plans only where its workflow requires.

```markdown
# UI/UX specification <module>

Date / version / status:
Business owner / UI-UX / engineer:
Platform and scope / out of scope:
Rule sources and user decisions:

## Goals and users
Roles, context, critical journeys, definition of success.

## Screen map and navigation
Screens, entry/payload, permissions, Back/Home/close, lifecycle.

## Screen contract <name>
Purpose, object identity, primary/secondary actions.
Fields: label, format/unit, required, read-only, validation, help/error.
List: scope, sort, search/filter, paging, selection and refresh.
States: initial/loading/content/empty/no-result/error/stale/partial/conflict.
Server responses and recovery steps.
Layout: compact/medium/expanded, large text, IME/insets.
Accessibility: name/role/state, reading/focus order, gesture alternatives.

## Action contract <name>
Eligibility, scope, confirmation, input/reason, progress, result, retry.

## UI mockup
Exists: path, states, approval state/date.
Or none: user decision and date.

## Dependencies and reuse
Engine components, picker/domain owner, API capability/claim.
New needs and whether they exist on the platform.

## Acceptance and verification
Given/When/Then per task/state; console and runtime evidence separated.
Build/run tool action and run id, device/build, result and test limits.

## Exceptions / open questions
Standard ID, reason, decision owner, impact, re-evaluation.
```

## 13. Minimum acceptance — UX-M11

| Given / When | Then, to be proven |
| --- | --- |
| A user without rights opens a route directly | Clear refusal; no data or action bypasses the server |
| First load not finished | Loading visible; no "no data" claim |
| Search finds nothing | Query/filter clear; clear restores the original scope |
| Refresh fails with old data | Data marked stale; last success time kept |
| User presses Save/Sign repeatedly | One action guarded; no duplicate success from the UI |
| Server changes status/permission after the list loaded | UI loads the latest state and explains skipped/refused items |
| Batch with an active filter | Processed scope matches the copy/confirmation; itemized result |
| Back while the editor changed or an operation runs | Cancel/unsaved contract runs; late results do not overwrite another context |
| Font 200%, compact window and keyboard open | Identity, values and actions are not hidden or clipped |
| TalkBack without long press | List, detail and decisions remain operable |
| Preview fails or no viewer is available | Error and back/retry available; not claimed as opened |

Run business/VM/service tests for meaningful changes. Runtime/visual tests follow the host's GUI
authorization. Shim and console tests without rendering are never labelled an Android pass. Record pending
evidence specifically.

## 14. Definition of Ready and Done

**Ready:** goal/scope known, business sources and dependencies read, permissions/states/actions defined,
mockup/approval per workflow, design-changing questions resolved, acceptance checkable.

**Done (documentation):** complete specification, consistent sources/decisions, clear mockup or status,
recommendations separated from implementation, links and checklist checked.

**Done (implementation):** UI follows the approved design, uses shared components, guards/lifecycle are
correct, required functional and runtime evidence exists, remaining issues are stated, README/guide and
release note updated. Without GUI validation, state `implementation complete; device validation pending`,
not `UI/UX fully passed`.

Handoff checklist:

- [ ] UX-M01–M03: needs, contracts and ownership complete.
- [ ] UX-M04–M06: manager/search/editor fit platform and scope.
- [ ] UX-M07: bulk actions, permissions, confirmation and partial results clear.
- [ ] UX-M08–M09: pickers/attachments/PDF/history use the right owner.
- [ ] UX-M10–M11: states, accessibility, lifecycle and acceptance have evidence or status.
- [ ] Every exception and pending verification has an owner and closing step.

## 15. References

- [MAUI experience components](../engine/engine-maui-experience.md), [contact picker](../engine/engine-contact-picker.md),
  [approval engine](../engine/engine-approval.md).
- [MAUI accessibility](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0),
  [Android accessibility](https://developer.android.com/guide/topics/ui/accessibility/apps),
  [Android window size classes](https://developer.android.com/develop/ui/compose/layouts/adaptive/use-window-size-classes).
