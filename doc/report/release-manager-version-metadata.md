# Release Manager versions and stable Prepare metadata

Date: 2026-10-26.

## Result

Correction requested on 2026-10-26: the main summary is now **Server release**,
loaded from the target independently of local preparation. Windows shows the
build-assigned `launcher.version`, keeping the application version separate;
Android shows its published name/code. The local prepared chip was removed.
Profile selection/refresh reads the published manifest with a 30-second timeout;
Compare updates the same summary. The original result below records the earlier
implementation and is superseded by this correction for the page summary.

Compare shows the actual local prepared application version and the target application
version with its UTC publication time. Old manifests explicitly show `Version not recorded`.
Windows reads ProductVersion from the published host DLL. Android requires the APK hash
to match its prepared state. Reading versions never uses the profile's current version
as a substitute for a binary or uses an engine/launcher version for the application.

Sync writes a supported artifact version to the signed optional manifest field even
with release note lock off. With lock on, a missing or mismatching version fails before
uploading release files. Windows Prepare refreshes its source version before checking
the note lock. Existing manifests without a version remain readable.

Distribution Prepare disables SourceLink, preserving deterministic compilation,
normalized paths and the project's debugging symbols. Development builds are unchanged.
Full SHA-256 comparisons are unchanged. The first release under the new policy can
change binaries once; actual source/compiler/dependency changes can still change them.

Successful Windows and Android Prepare write `<publish-folder>.build.json` outside the
publish directory. The record contains application version, host repository HEAD when
available, preparation time, host project filename, artifact filename and SHA-256. It
is informational, not signed or uploaded; comparison does not trust this sidecar.
The revision identifies the host repository HEAD, not a claim that the tree is clean
or a complete inventory of all referenced repositories.

## Validation

- Product host RunBay runner, action `test-release-version-metadata`, run
  `20261026-061856-test-release-version-metadata-5eac`: 105 release-related tests passed,
  zero failures/skips. Tests build a real WPF fixture in a disposable Git repository:
  an unrelated commit plus clean recompilation leaves DLL and PDB byte-identical;
  changing source changes both. Portable PDB metadata confirms SourceLink is absent.
- Version tests cover edited profile versus actual binary, unlocked signed version,
  missing host without version fallback, legacy target captions, Android hash match,
  locked mismatch before manifest publication, and provenance outside the distribution.
- Earlier attempts exposed a fixture constructor error and read-only Git fixture cleanup;
  both were fixed. The initial `dotnet test` runner left build-server processes after
  completion; the action now builds with servers/shared compilation disabled, then
  runs tests without rebuilding. A direct executable runner attempt rejected an MTP
  filter; the final action uses `dotnet test --no-build` and completes normally.
- Runs `20261026-061450-test-release-version-metadata-fd9f` and
  `20261026-061639-test-release-version-metadata-42c3` were stopped through RunBay after
  their console output completed. The second had 105 passing tests and exit code 0.
  No active application run was stopped.
- Host build: RunBay action `build-release-version-metadata`, run
  `20261026-061952-build-release-version-metadata-d3b2`, succeeded with zero errors
  and 409 warnings (including existing XML documentation warnings), in 32.32 seconds.
  Output is isolated, so the user's already running application was not replaced.
- No visual/GUI test, real product Prepare, Android APK rebuild, or production Sync.

Logs: `../.artefacts/em-system/scripts/release-version-metadata/out/`.
Host output/log: stored in the product repository artefact folder (outside this repo).
The action scripts are stored on the existing product RunBay runner. Existing
uncommitted engine work was preserved. No implementation commit or push was made.

Guide: [Release Manager](../engine/engine-release-manager.md#compared-versions-and-stable-build-metadata).

The project rules were updated in `claude.md`. Reload the application through its
normal Build + Run action to use the new UI/Prepare behavior. Existing production
manifests can show their recorded launcher release number already; a missing
application version remains missing until a new release records it.

## Server summary correction validation

Follow-up requested 2026-10-26: Release Manager now has the locked note-file ComboBox
and Release Notes side sheet matching Container Manager. The selected exact version is
persisted, used during Prepare for the host/launcher or Android display version, and
rechecked at Sync. Unlocked automatic version behavior is retained. The existing shared
note history and Material sheet styles are reused; close/scrim/Escape, refresh and
maximize/restore follow the same CM pattern. The prior inline preview was removed.

Locked selector validation: `test-release-version-metadata`, run
`20261026-063523-test-release-version-metadata-ff95`, passed 108 tests with no
failures/skips, including exact version/channel selection, JSON persistence,
view-model selection, deleted selected file without fallback, and unlocked automatic
numbering. Host action `build-release-version-metadata`, run
`20261026-063546-build-release-version-metadata-a976`, passed with 0 errors and
409 warnings in 32.78 seconds. No GUI test, app restart or production Sync was run.

Product host RunBay action `test-release-version-metadata`, run
`20261026-062546-test-release-version-metadata-f10e`: 107 tests passed, zero
failures/skips. A fixture reads an actual published folder manifest without any
local prepared folder or comparison, then verifies the missing-release status.
Other cases verify the build-assigned launcher release number, separate application
version, legacy manifest and connection-error captions without local fallback.
The new fixture initially tried to assign the internal application setter directly;
its setup now uses reflection like other isolated view-model fixtures.

The server summary is canceled on profile changes and view release. Successful
Compare replaces it with the target state read by that comparison. Local artifact
version checks in Sync and metadata handling remain unchanged.

Host validation after this correction: RunBay `build-release-version-metadata`, run
`20261026-062621-build-release-version-metadata-a731`, succeeded (0 errors,
409 warnings; 32.75 seconds). No active application was restarted and no production
release was changed. Visual verification remains unrun under the project workflow.
