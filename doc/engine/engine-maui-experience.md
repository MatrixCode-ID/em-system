# MAUI experience components

The Android UI uses Open Sans and semantic light/dark palette pairs. Merge
`MaterialDesign.xaml` once. Existing typography aliases remain available; new
roles end in `TextStyle`: headlineMedium, headlineSmall, titleLarge, titleMedium,
bodyLarge, bodyMedium, labelLarge and labelMedium. LineHeight is a multiplier
of the nominal font size, rather than an Android pixel height. Spacing keys
`space4` through `space48` and shape keys `shape8/12/16/28` are project tokens.
Buttons use a minimum 48-unit target and grow with content. A full shape follows
the actual component bounds.

Use `accentChipTextStyle` inside `accentChipStyle`; `chipTextStyle` belongs to
secondary chips. Count badges use error/onError. Branding replaces palette roles;
onError is chosen against the composited error fill for readable badge text.
Other brand pairs must still be checked for contrast by the host.

`MaterialField` provides Text (two-way), Label, Help, Error, IsPassword and
IsReadOnly. It retains a native editor, disables account autocorrection, associates
the Android label/error and offers a native show/hide password action. Read-only
and disabled are separate states. Paste and password-manager support remain native.

`FeedbackView` takes Message, Kind and IsBusy. `FeedbackKind` distinguishes Info,
Success, Partial, Error and Stale in words. `BatchResultPanel.Result` accepts an
immutable `BatchFeedback` snapshot of business-labelled `FeedbackItem` outcomes.
Its bounded virtual list can be closed and reopened during a screen session.
Keep it outside the screen's main scrolling list. A failed post-mutation refresh
must not erase the completed operation result. Never pass private payloads or
internal identifiers as user-facing labels.

Native semantics, focus, IME, insets, large fonts and touch bounds require Android
validation; console checks do not establish device accessibility compliance.

## Shell and lifecycle

Home contains a permission-checked catalogue only. Search matches trimmed title,
subtitle and group path without case sensitivity. Clear restores the hierarchy;
query survives returning within the same identity. A new account/connection clears
that state. Group headings and destinations occupy one bounded CollectionView.

My Tasks is available after sign-in even at zero or after a refresh error. Its
badge caps visual counts at 99+; the action name contains the actual known count.
The shared cache coalesces concurrent refreshes, retains the old snapshot on error,
records only successful times and rejects responses from a previous identity.
The shell polls only while foregrounded and refreshes on resume. Recreated hosts
unsubscribe from app/window/timer events.

Tasks, account and overflow are mutually exclusive. Compact windows below 600
logical units give Tasks the available width; larger windows cap it at 400.
Home/Reload move into labelled overflow in compact windows. Back, Escape and the
scrim close the modal first. The Android background subtree excludes accessibility
and keyboard descendants while modal; focus returns to its invoking button.
Shell modal visibility is synchronous so animation interruption cannot strand a
scrim. MotionPolicy uses interruptible easing and reads Android animation scale.
IconButton now derives from native Button; Glyph, TintColor, IconSize, Command and
CommandParameter remain compatible. It supplies native role and activation; all
consumers must supply a semantic action description rather than reading the glyph.

Login uses permanent labelled fields and a scrolling form/brand/CTA, reports empty
field errors locally, keeps credential failures general, and preserves simulation
exit. Signer selection and account actions have explicit native buttons. These
contracts still require device verification for TalkBack focus, actual hit bounds,
large text, animation scale zero, IME and insets.
