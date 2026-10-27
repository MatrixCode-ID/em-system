# MAUI experience components

Module paragraphs use `bodyMediumParagraphStyle` (14) or
`bodyLargeParagraphStyle` (16), based on the corresponding body role with
`LineHeight=1`, zero margin and WordWrap. Keep full text and native font scaling.
Use a zero-spacing stack for one identity block or a label/value subject, then
8 units between subjects and 12 between sections, with 16-unit card insets.
Long descriptions and notes remain dense within the paragraph. Queue metadata
such as role lists stays one line with tail ellipsis in a bounded grid column;
its full value remains accessible. These are module control defaults, not a
global change to every typography role. Approval detail rows, proposed changes,
comments and consuming module paragraphs use the compact paragraph styles.

Home shows its Modules heading and View all without a catalogue/filter subtitle.
Filter context captions belong to the full catalogue.
The bottom bar provides Home, All Modules and My Tasks in that order, using
the same icon/label tab geometry in the shell and task workspace. All Modules
opens this existing catalogue after returning home; refused navigation or an
identity change cannot open it. Home's View all button remains available and
updates the same selected indicator. Home restores the pinned shortlist.
Its destination cards contain only modules explicitly pinned for the current
account/server. With no pins, only the My Tasks summary card remains; View all
still opens the catalogue for pinning. Catalogue searches and filters do not
alter the Home shortlist. Unpinning the last module restores this empty state.

The My Tasks summary can appear before or after the entire Pin App List. Touch
and hold the card, drag upward or downward, then release to place it before or
after the pins. No drop target is required. Android held input consumes tap
activation, follows the native long-press timeout and requires a 32-unit vertical
drag; holding without movement never opens the workspace. Normal scrolling before
the hold threshold remains available. A normal tap opens the task workspace. There is no
move arrow. The summary is one real item in the same virtual collection as the
pins, selected by a row template selector and reordered with a collection Move
notification. There are no hidden header/footer slots reserving blank space.
Placement is device-local per account/server, defaults to after the pins and
survives pin changes. Dragging is disabled without pins. Catalogue mode,
external drags and stale account/server actions cannot change placement.
Summary bindings keep the catalogue row as their context and read its live
TaskSummary properties; they do not replace a recycled row's BindingContext.
Successful empty snapshots display 0 for both Assigned and Task groups.

The Home task summary keeps its cached counts and success time while refreshing.
A native 20-unit progress indicator replaces the refresh action inside the same
reserved 48-unit slot; temporary loading text never inserts a new card row.
The refresh command rejects duplicates; opening the task workspace stays enabled.
Failure remains an explicit stale message and does not advance the success time.

The Android UI uses Open Sans and semantic light/dark palette pairs. Merge
`MaterialDesign.xaml` once. Existing typography aliases remain available; new
roles end in `TextStyle`: headlineMedium, headlineSmall, titleLarge, titleMedium,
bodyLarge, bodyMedium, labelLarge and labelMedium. LineHeight is a native text
line-spacing multiplier, rather than an Android pixel height. Spacing keys
`space4` through `space48` and shape keys `shape8/12/16/28` are project tokens.
Buttons use a minimum 48-unit target and grow with content. A full shape follows
the actual component bounds.

Use `accentChipTextStyle` inside `accentChipStyle`; `chipTextStyle` belongs to
secondary chips. Count badges use error/onError. Branding replaces palette roles;
onError is chosen against the composited error fill for readable badge text.
Other brand pairs must still be checked for contrast by the host.

`MaterialField` provides Text (two-way), Label, Help, Error, IsPassword,
IsReadOnly, optional IconGlyph and Placeholder. It retains a native editor, disables account autocorrection, associates
the Android label/error and offers a native show/hide password action. Read-only
and disabled are separate states. Paste and password-manager support remain native.
The optional leading glyph sits in a decorative 40-unit tile; the password
visibility action is a native 48-unit icon button inside the outline, with
changing Show/Hide password semantics. The permanent label and error remain
associated with the Android editor. The outline supplies focus/error feedback
instead of a second native underline.

Login centres the consuming host's logo/title/tagline above outlined fields,
uses one primary sign-in button with a decorative icon, then conditional debug
and simulation-exit actions. Theme, installed version, manual update and copyright
remain available below the form. One scroll owner and a bounded content width
accommodate compact windows and the keyboard. The background and field surfaces
use theme roles, without a host-specific image or hardcoded brand in the engine.

`FeedbackView` takes Message, Kind and IsBusy. `FeedbackKind` distinguishes Info,
Success, Partial, Error and Stale in words. `BatchResultPanel.Result` accepts an
immutable `BatchFeedback` snapshot of business-labelled `FeedbackItem` outcomes.
Its bounded virtual list can be closed and reopened during a screen session.
Keep it outside the screen's main scrolling list. A failed post-mutation refresh
must not erase the completed operation result. Never pass private payloads or
internal identifiers as user-facing labels.

Native semantics, focus, IME, insets, large fonts and touch bounds require Android
validation; console checks do not establish device accessibility compliance.

Approval screens consume `Styles/Approval.xaml`: section/queue surfaces, a
decorative navigation circle and the icon/label/value row template. Module rows
supply Glyph, Label, Value and IsWarning; private values must be removed before
building those rows. The shared viewer groups proposed changes, signer route
and comments, with a persistent comment composer and decision bar. Direct-request
navigation hides queue paging. Signer selection remains separate from deciding.
These layouts reuse existing capabilities; no shipping, product detail or file
editing feature is implied by their appearance.

Later on 27 October 2026, consuming-host Android build/install and a process
probe passed alongside source-linked module checks. Device visual, touch,
TalkBack and large-font validation remain separate; historical missing-workload
results in the maintainer notes below describe the earlier verification run.

## Shell and lifecycle

Approval screens opt into `IReloadAfterNavigation`: their navigation callback
only validates/configures, and the stack loads through `OnReloadRequested` after
mounting on both new and existing entries. Other bodies retain their lifecycle.
Explicit routing to an existing opted-in entry accepts its latest payload only
after successful validation, then reloads once, even if already current. My Tasks
does not add a second filter refresh for these bodies. Progress is visible before
network I/O. Busy text distinguishes loading from processing a decision;
load timeouts and connection failures explain how to refresh safely. Decision
errors keep their existing handling and never trigger an automatic retry.

The general approval inbox binds its busy message/progress for programmatic loads
and guards navigation across its cards. My Tasks includes opening in its progress
state; switching the current entry still closes the panel immediately. Modules
can use the same post-mount contract without changing their access checks or
approval query scope. No global navigation lock prevents leaving a slow load.

Queue empty states stay hidden during loading, and an unloaded viewer does not
claim that its comments are empty. Android connection failures can arrive as
`WebException`, including `WebExceptionStatus.Timeout`; both these and HTTP
exceptions use the recoverable loading message without exposing server details.
Android emulator checks on 27 October 2026 verified delayed loading, offline
recovery, returning to a cached approval queue and general inbox progress.
Native task-group opening still needs an eligible account; source-linked router
regressions cover its single-reload contract. These checks do not certify TalkBack
or large-text behavior.

Summary and queue cards keep each subject on one line with tail ellipsis,
including address, identity and metadata. Keep the original value intact and
retain the relevant full accessible name/context. This does not truncate
document detail, proposed values, comments or errors that must be read in full.
Header viewports must not cover separate tab/action rows or the virtual list.

Later corrections on 27 October supersede the initial action-sheet design below:
the account stays on the right and the app-bar overflow is removed. Shared
`WorkspaceTab` geometry keeps both icons visible, with a small 64-by-32 selected
indicator and a full native touch target. Home has a real shared-cache task
summary that opens the separate workspace; it does not invent missing status
buckets or a completion meter. Counts remain unknown before the first snapshot.

Catalogue children use smaller icons, right indentation and exactly two text
lines (title/description), with ellipsis instead of repeating the department code.
The module ellipsis opens a themed, anchored popover with only Open and Pin/Unpin.
Outside tap, Back and opening a shell overlay close it. Native background focus
and accessibility are blocked while the local popover is open. A stale identity
or permission change rejects its action. Historical hide/order preferences are
ignored on reload; their old descriptions below are implementation history.

Home separates preferred destinations from the complete grouped catalogue:
pinning creates the Home shortlist, which stays empty when no pins exist.
The My Tasks summary remains available. View all opens the grouped catalogue; Home/Back return to
the shortlist. Both views use one virtual CollectionView. A module may supply
`Navigation.MenuGlyph` when it has no registered image. Group counts come from
the matching subtree; no sample departments or modules are injected.

Native context actions support Open, Pin/Unpin, Module info, Move to top and
Hide/Show. The filter exposes hidden modules for recovery. Preferences are local
AppSettings values under a hash of server address and user identity. Permission
checks happen before building cards and after returning from a context dialog.
Saved names never grant access. Invalid JSON falls back to the permitted catalogue.

The authenticated shell has Home/My Tasks bottom navigation and a decorative
notification bell only, without a fake badge or action. My Tasks remains separate
from Home. The workspace searches actual group names and sorts by longest wait
or name. Expandable approval groups query at most three oldest Pending requests
for the caller and offer View all through the existing wrapper route. Preview
queries do not fetch the confidential document. Generation and identity guards
reject late results after refresh/account/server changes or disposal.

The right account drawer leaves a visible strip of the workspace on compact
screens. Information cards have no navigation arrow. Only enabled destination
rows show a right chevron; simulation, sign-out and update actions do not.
Connection and theme choices disclose their actual options vertically. Light
and Dark are available; no unsupported System option is shown. Close and sign-out
remain outside the drawer scroll area. Native state and visual parity still
require device validation.

Home contains a permission-checked catalogue only. Search matches trimmed title,
subtitle and group path without case sensitivity. Clear restores the hierarchy;
query survives returning within the same identity. A new account/connection clears
that state. Group headings and destinations occupy one bounded CollectionView.
Each destination keeps its registered icon (or default app glyph), title, wrapping
subtitle and trailing chevron inside one neutral action card. A native Button
supplies activation and semantics; display content passes input through and does
not create duplicate accessibility nodes. Native group headers toggle whole
subtrees in the same virtual list. Collapse state survives refresh/return for the
same identity. Search starts with matching groups expanded, supports its own
temporary collapse state, and clearing search restores normal collapse choices.
Changing account or connection clears both sets of choices.

My Tasks is available after sign-in even at zero or after a refresh error. Its
badge caps visual counts at 99+; the action name contains the actual known count.
The shell invalidates task and account command availability after identity or
navigation updates, including the initial application assignment after XAML binding.
The shared cache coalesces concurrent refreshes, retains the old snapshot on error,
records only successful times and rejects responses from a previous identity.
The shell polls only while foregrounded and refreshes on resume. Recreated hosts
unsubscribe from app/window/timer events.

Tasks, account and overflow are mutually exclusive. Compact windows below 600
logical units give Tasks the available width; larger windows cap it at 400.
Home/Reload move into labelled overflow in compact windows. Back, Escape and the
scrim close the modal first. The Android background subtree excludes accessibility
and keyboard descendants while modal; focus returns to its invoking button.
The account initials button uses a centred 48-by-48 target with no content padding,
so its circular tonal background does not stretch to the app bar height.
Shell modal input, accessibility and focus state changes synchronously so
animation interruption cannot strand a scrim. Account and task surfaces slide
from the right (220 ms entry, 160 ms exit); a closing surface immediately stops
intercepting input. The scrim fades in and is removed immediately on close.
Mounted page content uses a 240 ms horizontal swipe across the laid-out viewport
width, with no fade or vertical movement. Forward enters from the right;
Back/Home enters from the left, including Home/catalogue mode changes. The
swipe waits for native pre-draw so it cannot finish during first-layout work.
A new destination cancels pending frame readiness as well as active motion.
Navigation is not delayed and
the outgoing view is not retained. MotionPolicy cancels previous animations,
guards completion by generation and reads Android animation scale on each
transition. Scale zero applies final states immediately. Account changes and
shell disposal cancel motion and hide obsolete surfaces.
Theme selection uses aligned icon/label/check columns: Sun for Light, Moon for
Dark and a fixed right-hand check slot. Visible labels stay unchanged; native
actions expose the selected state through their accessible names.
IconButton now derives from native Button; Glyph, TintColor, IconSize, Command and
CommandParameter remain compatible. It supplies native role and activation; all
consumers must supply a semantic action description rather than reading the glyph.

Login uses permanent labelled fields and a scrolling form/brand/CTA, reports empty
field errors locally, keeps credential failures general, and preserves simulation
exit. Signer selection and account actions have explicit native buttons. These
contracts still require device verification for TalkBack focus, actual hit bounds,
large text, animation scale zero, IME and insets.

## Maintainer notes

On 27 October 2026, independent compilation of the controls and 80 UI core,
319 WPF core, 20 source-linked mobile logic and 34 source/contrast checks passed
through RunBay. The native project reported NETSDK1147 (wasm-tools missing).
No workload was installed. This does not certify Android compilation or native
accessibility. Engine harnesses/output are retained under
`../.artefacts/em-system/scripts/maui-ui-ux-smoke/` and
`../.artefacts/em-system/scripts/maui-ui-ux-controls/`.

The default solid error/onError contrast is 6.461:1 light and 12.369:1 dark.
Primary/secondary container pairs also exceed 4.5:1. Revalidate custom branding,
composited states and device rendering; these ratios alone do not certify a UI.
