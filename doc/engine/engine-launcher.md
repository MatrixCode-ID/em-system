# Launcher, application updates and IT cabinets

Release Manager builds one product-specific Windows exe for setup, start, update, repair and uninstall.
Identity, icon, PNG logo, public key, default release source, update behavior and versions are embedded.
The built-in engine product is only a development/default build; production products use their own app_id.

## Install and run

Give IT the product launcher exe. On first run it reads or creates `settings.ini` beside itself:

```ini
; Release source of the application. Delete this file to restore the default.
[Launcher]
Source=https://example.test/cdn/inventory-release/
```

Only Source belongs in this file. A read-only installer directory without the file produces a clear
creation error. Install writes Source to the target user's registry, including repeated installs;
settings.ini is never copied into the installation. The installed runner uses HKLM Source policy,
then HKCU Source. It never reads settings.ini. HTTP(S), local and UNC release folders are supported.

```text
%LocalAppData%/<app_id>/
  inventory-installer.exe
  current.json
  launcher.log
  app-<release-id>/
  .staging-<release-id>/
```

Registry: HKCU/Software/<app_id>/Launcher. IT policy:
HKLM/Software/Policies/<app_id>/Launcher. The embedded public-key fingerprint is recorded at install;
another embedded key with the same app_id is rejected. Imported PEM, user keys and policy keys remain
supported. app_name is display-only. Shortcuts, taskbar pins and Apps & Features use the product exe name.
Replacement renames the running exe to `<name>.old.exe`, cleaned up on the next run.

The logo occupies exactly 240 by 48 logical pixels, scaled uniformly, left aligned and vertically
centered, retaining transparent margins. WIC decodes the embedded PNG to premultiplied pixels and GDI
alpha blends it; an empty logo uses the product icon. Setup, maintenance and progress show launcher
and engine versions. `--version` and launcher.log also record them.

## Updates

At start the runner checks the signed source. Profile update behavior is `automatic` by default;
`ask` offers Update now / Later. Every binary is checked by size and SHA-256 after the signature and
manifest have been accepted. Unchanged files are reused; interrupted downloads resume in staging.
Only a complete verified version becomes current. The installed app can keep running during preparation.

Em WPF hosts only poll when LAUNCHER_PATH is set. First check: five minutes after start. Later checks:
sixty minutes with random plus/minus ten minutes per PC. The host can configure these through
EmAppBuilder.LauncherUpdates. Manual Check for update runs immediately without jitter.

The status bar slot to the right of Version shows Up to date (muted check), Checking/downloading
(spinner), Update ready (accent, Restart to update) or Check failed (warning with reason). Its context
menu offers Check for update and disables duplicate checks. Polling stops after a verified update is ready.
Restart confirms, closes through normal unsaved-screen handling, then starts the root launcher
`--apply --pid <pid>`. Declining leaves the prepared update for the next application start. Debugger/IDE
and publish-folder runs have no update slot or periodic checks. Non-Em hosts update through the runner
at start and do not get the in-app slot.

## Cabinet for IT

Download to cab in setup, or `--download-cab`, creates `<app_id>.cab` beside the installer. It is a
Microsoft Cabinet using LZX through Windows FCI/FDI, without encryption or spanning. Releases above
approximately 2 GB fail clearly. Release Manager does not create cabinets.

The cab contains release.json, release.json.sig and binaries/. An unchanged, verified cab is retained;
otherwise unchanged files are reused from the old cab and only changed files are downloaded. The new
cab is built, extracted and verified in temporary storage, then renamed over the old cab atomically.
A failure/cancellation leaves the old cab intact. The command does not install or write registry.

At install, a cab matching the CDN is used; an unreachable CDN allows the verified cab. A differing cab
offers IT the option to update it first. Quiet install uses the CDN when they differ, without changing
the cab. Extraction goes directly into the installation's .staging directory; failure removes staging.
The installed Source remains the CDN address. User PCs use the CDN for future repair and update.

## CLI

| Command | Purpose |
| --- | --- |
| no command | Setup if uninstalled; otherwise check/update and run |
| --install [--source S] [--target T] [--quiet] [--no-run] | First install; quiet uses no GUI |
| --prepare [--quiet] | Check, download and verify without activation, always without GUI |
| --apply --pid PID | Wait for the app to exit, apply and start |
| --download-cab [--source S] [--quiet] | Update the IT cab only |
| --update / --repair / --uninstall [--quiet] | Installed maintenance |
| --maintenance | Maintenance window |
| --import key.pem / --list-keys / --remove-key keyId | Additional trusted keys |
| --version | Launcher and engine versions |

Prepare writes one JSON line: `{"state":"none"}`,
`{"state":"prepared","release":"a1b2c3d4e5f6","publishedAt":"2026-10-21T08:00:00Z"}` or
`{"state":"failed","message":"..."}`. Failures are logged without a dialog.
Cab success exits 0 and reports either "Cab is up to date" or "Cab updated to release ..." with
publication time and downloaded bytes. Errors: 2 source/signature, 3 filesystem, 4 cancelled.
Other launcher commands additionally use 1 for invalid arguments and 5 for an application still running.

For build inputs and versions see [Release Manager](engine-release-manager.md); for the signature and
layout see the [release contract](../release-format.md). GUI/interactions must be verified separately
from console tests; no visual verification was run for this implementation.
