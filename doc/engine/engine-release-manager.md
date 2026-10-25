# Release Manager (WPF)

Release Manager (`admin.release`, Administrative Tools:Release Manager Access) prepares Windows .NET
applications, compares output with a release source, signs and synchronizes it, and verifies the result.
Em hosts get full identity/branding/in-app updates; other .NET hosts get basic installer and start-time
update support. The [signed release contract](../release-format.md) is shared with the Rust launcher.

## One product profile, one JSON

Profiles are `Documents/Em/ReleaseManager/Profiles/Release/<id>.json`. The toolbar supports New,
Duplicate, Rename, Delete, Import and Export. The profile contains source/publish/target paths, product
identity, ICO/PNG Base64, the unencrypted P-256 PKCS#8 private PEM, public CDN address and launcher settings.
Export copies the JSON including the key. Anyone holding it can sign releases; protect it accordingly.
There is no certificate store, PFX/CER, password mode or ZIP export. Legacy profile directories are ignored
without migration. One production product uses one permanent signing key. A different key with an
existing app_id is rejected by installers.

Profiles folder preference and last profile per server remain under the host's BaseRegKey/ReleaseManager.
Settings edits a draft; Save checks for an external edit and offers overwrite or continued editing.
Cancel discards the draft, including key edits. Duplicate preserves the product identity/key and gets a
new profile id; it does not create a distinct installation identity.

## Settings form order

Complete the numbered groups from top to bottom:

1. **Application Source:** choose and apply the platform, then choose the solution and host.
2. **Product Identity:** review the product name, publisher and app_id read from the host.
   Android additionally shows Application ID, APK filename and minimum version code;
   Windows shows the executable and expandable branding previews.
3. **Build Environment:** choose the local publish folder. Android shows SDK/JDK paths and
   environment actions; Windows shows launcher build folders and expandable launcher options.
4. **Release Destination:** choose CDN or Folder, then the release folder and public base address.
   Target folder appears for Folder only; managed CDN actions appear for CDN only.
5. **Release Signing:** create/import the manifest key and, for Android, configure its permanent
   keystore. Expand Signing credentials for import before importing a keystore that needs alias/passwords.

Save profile and Cancel remain above the scrolling form. Shared Material styles provide consistent
labels, filled fields, button hierarchy and themed enabled/disabled states. Supporting text explains
the important differences between local build output and release destination; detailed rules remain
available in hover help. A progress line and wait overlay cover asynchronous source/SDK work.
Unapplied platform changes disable Save until the platform is applied or restored to the active one.

## Read product identity

Choose the .slnx with Browse, or press Check solution to read the selected one again. Both list the
host projects (projects without a declared OutputType are evaluated with MSBuild one by one, so a large
solution takes a while; a status line above the form names the project being checked) and then read the
host identity. Opening the settings does not read the solution: it shows the saved host only.

MSBuild evaluates Product, Company, EmAppId, AssemblyName, ApplicationIcon, EmAppLogo and references. The
difference dialog shows profile versus solution values (asset hashes for images); accept solution identity
or keep the profile. Prepare applies this identity to the host project only, through a generated targets
file passed as `CustomBeforeMicrosoftCommonTargets`; global `-p:` properties would also rename every
referenced library.

```xml
<Product>Contoso Inventory</Product>
<Company>Contoso</Company>
<EmAppId>Contoso.Inventory</EmAppId>
<ApplicationIcon>Logo.ico</ApplicationIcon>
<EmAppLogo>Logo.png</EmAppLogo>
```

app_id is two or more alphanumeric dot-separated segments; MatrixCode.EmSystem is reserved in this
manager. Use separate ids such as Contoso.Inventory and Contoso.Inventory.Test for environments that
coexist. Application exe is AssemblyName plus .exe. A reference to Em WPF enables the full-support notice;
other hosts are accepted and show the basic-support notice.

Import an ICO and an optional transparent PNG logo. The preview uses a fixed 240 by 48 logical-pixel box
at 100% and 200%, uniform scaling, left alignment and vertical centering. Transparent margins are kept.
Empty logo falls back to the icon. Small output dimensions (<24 px) and insufficient resolution at 200%
produce warnings, not rejection. Prepare passes the profile identity and assets to dotnet publish, so
application and launcher share the same identity. See [host build integration](build.md#host-identity).

## Destination and permissions

Folder targets write directly to a local/UNC release folder. CDN writes go through the active connection;
Compare/Verify and the launcher's default source read the release at `<CDN address>/<release-folder>/`.
The CDN address is the base only (e.g. `https://host/cdn/`, or a reverse proxy in front of it); the release
folder is always added to it, so changing the release folder moves every address with it. Empty CDN
address derives `<connection>/cdn/<release-folder>/`. When the server has a `release.json` in the release
folder but that address answers 404, Compare stops with an error: otherwise every Compare would see no
release, every Sync would upload everything again, and the launcher would look in the wrong place.

For the built-in Cdn target, **Public CDN base address (optional override)** can stay empty: the engine
connection supplies the address automatically. The field placeholder and visible supporting text
explain this; enter an override only when clients use a different public CDN address. For Folder
targets, the form instead explains that Android releases require a public base address for downloads.

Under Managed CDN folder, Browse / select folder browses relative paths and creates/adopts a physically empty root.
Selecting the same root for the same app_id is allowed; another owner, a nonempty root or nesting is
refused. Release ownership removes this product's ownership. CDN Manager access is not required.
Only CDN administrators set quota. Sync preflight reports quota/expected final size before any writes.
See [CDN storage](engine-cdn-storage.md) for ownership, quotas and recycle rules.

## Prepare and launcher builds

The local machine stores Launcher source folder and Launcher output folder under BaseRegKey,
not in the profile. Source must contain the Rust launcher Cargo.toml/build.rs. Output defaults to
Documents/Em/ReleaseManager/Launchers/<app_id>/ and can point to an IT share.

Prepare runs self-contained win-x64 dotnet publish, then obtains the product launcher and copies it to
the publish root using the configured exe name. It hashes actual launcher source bytes, build inputs,
product identity/assets/public key, source address and launcher settings. Git commit identity does not
replace content hashing. An adjacent `<launcher-name>.build.json` records input hash, version, engine
version (`git describe --dirty`), build time and exe hash. Matching cached binaries need no Rust/MSVC.
A missing/changed binary requires local source, Rust and Visual Studio x64 C++ tools; failures explain
what is missing. The build uses a target directory per app_id and remaps machine paths out of the exe.
`build-dist.ps1` remains the default-product/local-test build entry point.

**Build launcher** (profile menu) rebuilds the launcher even when the cache matches; it needs the local
source folder. When the publish folder holds a publish result, the new launcher replaces the one in it and
the last Compare is dropped. **Open launcher folder** opens the output folder.

Launcher version is independent from application version. Pick MAJOR.MINOR.PATCH and prealpha/alpha/
beta/rc/release. A changed prerelease input gets the matching target counter plus one; identical target
inputs reuse its version. A changed stable launcher cannot reuse the target's stable version.
The signed manifest records filename/version/inputHash. Re-Prepare after another developer publishes a
conflicting launcher version. Application version still comes from the host Version property.

## Compare, Sync and Verify

Compare hashes local output and reads the public target. Sync confirms uploads/removals, checks that
neither source nor remote manifest changed since Compare, checks final quota, signs metadata, uploads
changed binaries, writes signature then manifest, and removes obsolete content. Readers can observe a
transient mismatch during Sync and must retry later. After the last removal, a CDN Sync writes `.meta` with
`publicListing: false` to the release folder, `binaries/` and every folder inside `binaries/` (settings are
not inherited), so the application components are fetched only by direct link and sent with
`X-Robots-Tag: noindex`; the release folder also gets it when it is adopted. Manual changes are replaced by
the next Sync. Folder targets write no `.meta`. Verify checks signature, sizes and hashes using
the profile key. Overlapping publish/target folders produce a warning that can be accepted or cancelled.

See [Launcher and updates](engine-launcher.md) for installation, in-app preparation and IT cab packages.
## Android profiles

Select Platform Android and Apply platform in Settings. Changing platform asks for confirmation
and creates a separate P-256 manifest key. Old version-two JSON without platform remains Windows.
Applying a different platform reloads compatible hosts from the current solution and reads the selected
host identity. Check solution also reloads the hosts manually. Read identity from the MAUI project (ApplicationTitle,
Company, EmAppId, ApplicationId, ApplicationVersion, Version and Android target framework).
The product's title and MauiIcon stay in the host project; Android Prepare never overrides them.
PNG icons can be previewed; SVG preview is not required.

Use an independent EmAppId such as Contoso.Inventory.Mobile and the permanent applicationId
com.contoso.inventory. APK file name defaults to the lowercase app_id with dots replaced by hyphens.
Launcher settings are hidden for Android, and launcher menu commands are disabled.

Select or type the Android SDK and Java SDK for this machine. Find installed SDKs refreshes the lists
asynchronously; opening an Android profile and applying Android also refresh them with a wait indicator.
Candidates are cached for display, so opening a dropdown never scans the disk. All existing candidates are listed, including
the Visual Studio SDK and LocalAppData/Android/Sdk. Rider android.xml/jdk.table.xml options
under the user JetBrains configuration are inspected; Browse accepts other Rider locations. Machine preferences use the host BaseRegKey/ReleaseManager. Check environment reports
platform-tools, build-tools with apksigner/aapt2, the target Android API, JDK and maui-android workload.
The default target API for an unversioned net10.0-android TFM is 36; Java requires the evaluated
MinimumSupportedJavaVersion/LatestSupportedJavaVersion when supplied, otherwise at least 17.

Prepare Environment displays the commands and asks before installing only missing components.
It uses dotnet workload install maui-android, Microsoft OpenJDK through winget, and
InstallAndroidDependencies with SDK licenses accepted. Workload installation asks for elevation.
If winget is absent, install App Installer or select a JDK manually. New SDK paths must use ASCII
without spaces. User ANDROID_HOME/JAVA_HOME are changed only if the checkbox is selected; builds
always receive SDK and Java directories explicitly. Environment installation is not part of smoke tests.

Create makes a permanent RSA-4096 PKCS12 signing key valid for 10000 days. Import accepts JKS,
keystore, P12 and PFX and checks the alias/passwords immediately. Show signing credentials reveals
the passwords and provides copy buttons; the certificate SHA-256 identifies the signing certificate.
Passwords are passed to keytool/publish through the child environment, never command arguments or targets.
**Export the profile as a protected backup. Losing either the keystore or manifest key prevents
existing installations from receiving accepted updates.** Export includes all credentials.

Prepare builds one signed arm64-v8a APK with the selected environment. It reads the destination first:
an unreadable destination, another applicationId or a Windows release stops preparation.
The first versionCode is ApplicationVersion when valid (otherwise 1); subsequent releases increment
the published Android versionCode. VersionName comes from host Version (SemVer).
Minimum version code 0 permits deferral; a positive value must not exceed the prepared code.
Prepare embeds the complete release URL and public manifest key in the host assembly.
A Folder target requires its public CDN base address. Temporary keystore/targets/publish output are
removed in finally. Prepare only when releasing: each new published version changes versionCode.

Compare, Sync and Verify use the same destinations, ownership/quota and metadata privacy as Windows.
Sync refuses a publish folder changed after Prepare or anything except the one prepared APK.
Verify additionally checks Android applicationId and fileName. Copy install link copies the direct
binaries/<apkFileName> URL for initial installation. Android uses no launcher or IT cabinet.
See [Android updates](engine-android-updates.md) for runtime behavior and limitations.

## Maintainer notes

The settings form uses the project's desktop Material language and shared resources. Design references:
[Material text fields](https://github.com/material-components/material-components-android/blob/master/docs/components/TextField.md)
and [Material progress indicators](https://github.com/material-components/material-components-android/blob/master/docs/components/ProgressIndicator.md).
This is a WPF adaptation; no new UI toolkit dependency is introduced.

RunBay action `review-release-settings` builds and runs the Release Manager tests. Action
`render-release-settings` checks both platforms and themes, expanded fields, narrow width and busy
state. Render harness/output: `../.artefacts/em-system/scripts/release-settings-render/`;
test logs: `../.artefacts/em-system/scripts/release-settings-review/out/`.
