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

## Read product identity

Choose the .slnx and host, then Load solution / identity. MSBuild evaluates Product, Company, EmAppId,
AssemblyName, ApplicationIcon, EmAppLogo and references. The difference dialog shows profile versus
solution values (asset hashes for images); accept solution identity or keep the profile.

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
Compare/Verify and the launcher's default source use the profile CDN address. Empty CDN address derives
`<connection>/cdn/<release-folder>/`. An explicit address can point at a reverse proxy.

Browse CDN / select managed folder browses relative paths and creates/adopts a physically empty root.
Selecting the same root for the same app_id is allowed; another owner, a nonempty root or nesting is
refused. Release owned folder removes this product's ownership. CDN Manager access is not required.
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
