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
