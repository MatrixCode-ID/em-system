# NuGet package naming

**Status: draft** (2026-10-04, updated 2026-10-06). Most rules are decided and in use; the remaining
questions are listed at the end. Applies to the em-system engine libraries and to derived repositories
(such as EmPorium House), which **follow this document** instead of defining their own rules.

Version rules derive from the [container image naming](container-naming.md) convention; this
document records only what differs for NuGet. Packing and pushing: the "NuGet packages" section of the
[README](../../README.md), `scripts/pack-nuget/pack-nuget.ps1` and `scripts/upload-nuget.cmd`.

## Package names

- The PackageId derives from the project name, in PascalCase with dot-separated segments:
  `<Product>.<Component>[.<Sub>]`. It is set in `Directory.Build.props`; do not override it per project.
- Engine packages use the **`EmSys.`** prefix (decided 2026-10-06): project `Em.Libs` becomes package
  `EmSys.Libs`, and so on. Project, assembly and namespace names stay `Em.*`. Reason: nuget.org does not
  reserve prefixes shorter than four characters; `EmSys.*` was requested as an ID prefix reservation for
  the nuget.org organization `MatrixCode-ID`. Derived repositories use their own prefix (for example
  `EmPorium.`) and never publish packages prefixed `Em.` or `EmSys.`.
- Only libraries are packed. Hosts (`Em.Api`, `Em.Ui.Wpf`, `Em.Ui.Maui`) and the test module (`Em.Test.*`)
  set `IsPackable=false`.

| Package | Contents |
| --- | --- |
| `EmSys.Libs` | Shared API and UI contracts |
| `EmSys.Api.Core` | Backend engine |
| `EmSys.Ui.Core` | Themes, branding and the cross-platform UI foundation |
| `EmSys.Ui.Wpf.Core` | WPF UI engine |
| `EmSys.Ui.Maui.Core` | MAUI UI engine (released from `0.1.0-alpha.2`) |

The projects that are actually packed and released are listed in `scripts/pack-nuget/packages.txt`. Every
released package has release notes in `doc/ReleaseNote/<PackageId>/<version>.md`
([format](../ReleaseNote/README.md)).

## Versions

Format `MAJOR.MINOR.PATCH[-channel.N]`, SemVer 2.0, as in the container convention, with these
differences:

| Channel | Package version | Notes |
| --- | --- | --- |
| prealpha | `0.1.0-0.prealpha.N` | The leading `0.` sorts prealpha lowest; **local only**, never pushed to a public feed |
| alpha | `0.1.0-alpha.N` | |
| beta | `0.1.0-beta.N` | |
| staging | `0.1.0-rc.N` | |
| release | `0.1.0` | **No suffix** |

- **Release has no suffix.** NuGet treats any version containing `-` as a prerelease. The
  `0.1.0-release.N` form used by the EmPorium container publisher is **not** used for NuGet: such packages
  do not install without `--prerelease` and Dependabot does not consider them stable.
- **Prealpha gets a `0.` prefix.** NuGet sorts prerelease labels alphabetically, so `0.1.0-prealpha.7`
  would be considered newer than `0.1.0-beta.2` by `dotnet add package --prerelease`, the IDE Update
  button and floating versions. Numeric identifiers always sort below alphanumeric ones, giving the right
  order: `0.1.0-0.prealpha.N` < `0.1.0-alpha.N` < `0.1.0-beta.N` < `0.1.0-rc.N` < `0.1.0`.
- **Prealpha is never published to a public feed** (GitHub Packages or nuget.org); it stays in the local
  `dist/nuget-pack` feed. Public feeds start at **alpha**. `upload-nuget.ps1` refuses to push a version
  containing `prealpha`/`pre-alpha`.
- **One version for all packages.** The engine packages are always built and published together with the
  same version, because `ProjectReference`s between the libraries become dependencies on that same
  version. Never publish a single package on its own.
- **Versions are never reused.** The contents of a version never change, even after it is removed from a
  feed. NuGet caches packages per version (`%USERPROFILE%\.nuget\packages`) and does not download the same
  version again. If something is wrong, increase `N` or `PATCH`.
- **No floating versions in the feed.** NuGet has no equivalent of `:latest`, `:beta` or `:0.1`. Consumers
  who want to follow a channel use a floating version in their project (see Usage).
- **Tracing to a commit.** There is no equivalent of the `:sha-xxxxxxx` tag. The .NET 8+ SDK already
  writes `RepositoryCommit` into the package metadata. Build metadata (`+sha.1a2b3c4`) is not used because
  NuGet ignores it when comparing versions.
- The cycle rules match the container convention: the target version is fixed for the cycle, `N` starts at
  1 and restarts when the channel changes, and the next cycle increases the target version
  (`0.2.0-0.prealpha.1`).

One cycle:

```
0.1.0-0.prealpha.1 … → 0.1.0-alpha.1 … → 0.1.0-beta.1 … → 0.1.0-rc.1 … → 0.1.0
                                                                          ↓
                                                     0.2.0-0.prealpha.1 … (next cycle)
```

### Where `N` comes from

From the git tag `v<version>` of each release (for example `v0.1.0-alpha.3`), as for containers. A
release that publishes NuGet packages and container images from the same commit uses the same target
version and channel; only the prealpha and release spellings differ, as shown above.

## Feeds

| Feed | Used for | Notes |
| --- | --- | --- |
| Local `dist/nuget-pack` | Testing on your own machine | Output of `pack-nuget.ps1`, ignored by Git |
| GitHub Packages `https://nuget.pkg.github.com/MatrixCode-ID/index.json` | Alpha through rc, and release | Consumers must sign in with a `read:packages` PAT, even for public packages |
| nuget.org | Alpha through rc, and release (since `0.1.0-alpha.1`) | No sign-in; versions cannot be deleted, only unlisted; published automatically by `publish-nuget.yml` from release notes |

Package visibility on GitHub Packages is set per package, separately from the repository.

## Usage

```xml
<!-- pin one build -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.0-beta.3" />

<!-- follow the beta channel of target version 0.1.0 -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.0-beta.*" />

<!-- follow stable 0.1.x releases -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.*" />
```

Floating versions are resolved again on every restore, so a build can change without any code change.
Product hosts should pin exact versions.

## Open questions

- Should the container convention also switch to `0.1.0-0.prealpha.N` so image and package versions are
  spelled identically?
- Which channels go to nuget.org (question 2 in [doc/ideas/ci-cd-nuget-org.md](../ideas/ci-cd-nuget-org.md)).
- The default `-Version` of `pack-nuget.ps1` and `upload-nuget.ps1` is still `0.1.0-pre-alpha.1` (the old
  spelling, unlike `0.1.0-0.prealpha.N` above). It only affects local packs, since prealpha is never
  pushed, but the spelling has not been aligned yet.
