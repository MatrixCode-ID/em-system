# Review Android settings in Release Manager

- Date: 2026-10-25.
- Status: selesai; perbaikan diterapkan atas permintaan langsung pengguna.
- Scope: review and subsequent user-requested Material form/input-order fixes.
- Follow-up plan: none.

## Verified findings

1. **P2: platform changes block the UI without progress.**
   `ReleaseAndroidSettingsVm.ChangePlatformCommand` calls `Refresh()` synchronously, then the
   parent dialog's `PlatformChanged` callback calls it again. `JavaCandidates` performs recursive
   JetBrains directory discovery from the binding getter. A console measurement found 2308–2324 ms
   per discovery, compared with 1–9 ms for the platform/key change. Two refreshes can block input
   for roughly 4.6 seconds. Neither the parent progress line nor the VM Busy wrapper covers this path.
2. **P2: editable SDK combos have a noneditable template.**
   The Android and Java SDK fields use `fieldComboStyle` with `IsEditable=True`. The template has
   no `PART_EditableTextBox`. Console inspection after applying the template confirms that selecting
   an item updates Text and the VM while the selected-value presenter is empty. The existing
   `fieldEditableComboStyle` displays the value and supplies the editable part.
3. **P2: applying a platform discards host selection without reloading identity.**
   The parent callback clears `_hosts.ItemsSource` and never calls `LoadSolution` or `ReadIdentity`.
   A newly enabled Android panel therefore keeps an empty Application ID until the user checks
   the solution and chooses the Android host. Saving immediately also clears HostProject because
   Save reads the now-null SelectedItem. Console evaluation of an actual MAUI host confirms that
   ApplicationId is already read correctly and displayed after Refresh when ReadIdentity runs.

## Proposed changes

- Discover SDK candidates asynchronously outside binding getters, retain a snapshot, and display
  progress while loading. Avoid repeating discovery during the same platform change.
- Use `fieldEditableComboStyle` for both SDK fields and verify dropdown selection, typed paths,
  and restored machine preferences.
- Reload compatible hosts after a confirmed platform change, preserve a compatible selection,
  and read the selected host identity through the existing progress path. Keep a released
  application's permanent identity protected by the existing release validation rules.

## Initial review validation and limits

RunBay app `em-system`, action `review-release-settings`, run
`20261025-211216-review-release-settings-047b` completed with exit 0.
Harness and build/output logs are outside the repository at
`../.artefacts/em-system/scripts/release-settings-review/`.
The console applies templates and lays out controls without opening a window or rendering images.
No GUI interaction, environment installation, release publication, or implementation changes occurred.

## Decisions and open questions

- Source review and console reproduction are complete.
- 2026-10-25: the user requested checking the editor against Material 3 and fixing the input order.
  Implementation now groups Source, Identity, Build Environment, Destination and Signing, uses shared
  field/button/checkbox styles, loads compatible host identity on platform changes, and caches SDK
  discovery asynchronously. No separate execution plan was needed for the direct code request.
- Build and all 38 Release Manager console tests passed in RunBay run
  `20261025-213230-review-release-settings-417d`.
- 38 offscreen layout captures cover Windows/Android, light/dark, expanded advanced sections,
  narrow width and busy state. RunBay run `20261025-213246-render-release-settings-8f93` passed;
  images/logs are at `../.artefacts/em-system/scripts/release-settings-render/out/`.
- The render harness's first attempt lacked a dispatcher synchronization context. It was corrected
  before the final successful captures; the WPF tests already ran under the normal WPF context.
- Actual mouse interaction, real environment installation and production releases remain outside
  this verification. No open design question blocks the requested changes.

## Follow-up: built-in CDN address clarity (2026-10-25)

The user asked whether the public CDN base address must be re-entered for the built-in CDN.
Source review confirms that an empty override already derives the release URL from the active
engine connection host plus `/cdn/<release-folder>/`, in both ReleaseManagerVm and CdnReleaseTarget.
The visible field label/supporting text does not explain this optional behavior; only its tooltip does.
Proposed UI clarification: label it as an optional override and show the automatically derived address.
The user subsequently requested updating the explanation. The Cdn field now visibly says optional
override, its placeholder explains automatic engine addressing, and its supporting text describes
when to leave it empty. Folder help separately retains the Android public-address requirement.
No URL resolution or persistence behavior changed; displaying the actual resolved URL remains a proposal.

## Follow-up: dotted panel focus outline (2026-10-25)

The user reported a dotted rectangle around the scrolling form below Save. The form's ScrollViewer
was still using the default WPF focus visual. Its FocusVisualStyle is now explicitly null and its
BorderThickness is zero, as is the status ScrollViewer. Field/button focus indicators and keyboard
scrolling remain available; card outlines retain the shared Material styling.
