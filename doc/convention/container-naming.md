# Container image naming

Decided 2026-10-04. Applies to every image built from the em-system engine and from derived repositories
(such as EmPorium House). Derived repositories **follow this document** instead of defining their own
rules. The registry that hosts the images is described in [Container registry](../engine/engine-registry.md);
the guide for running the EmPorium House image lives in the EmPorium repository (`doc/setup-container.md`).

## Name format

```
<registry>/<image-name>:<tag>
```

Example: `domain.com/em-api:0.1.0-prealpha.1`.

- `<registry>`: the registry address to push to (an Em registry, or `ghcr.io/matrixcode-id` for GHCR).
  The real address comes from the publish configuration and is never hard-coded.
- `<image-name>`: lowercase words separated by `-`, in the form `<product>-<component>`. One image serves
  every channel; channels are distinguished by tag, **not** by separate images or packages.
- `<tag>`: see below.

### Image names

| Image | Source |
| --- | --- |
| `em-api` | The `Em.Api` host in this repository |
| `emporium-server` | The EmPorium House server host (follows this convention) |

New components are named `<product>-<component>` (for example `em-worker`). Names must be lowercase
because GHCR rejects uppercase letters.

## Channels

From least to most stable:

| Channel | Meaning | Floating tag (moves) | Version tag (fixed) |
| --- | --- | --- | --- |
| prealpha | Very early build, may be broken | `:prealpha` | `:0.1.0-prealpha.N` |
| alpha | Incomplete features, internal testing | `:alpha` | `:0.1.0-alpha.N` |
| beta | Feature complete, wider testing | `:beta` | `:0.1.0-beta.N` |
| staging | Release candidate, final testing | `:staging` | `:0.1.0-rc.N` |
| release | Stable for production | `:release` and `:latest` | `:0.1.0`, `:0.1`, `:0` |

- **Version tags are never overwritten.** Only floating tags move to the latest build of their channel.
- `:latest` equals `:release` and never points to a prerelease channel.
- Staging uses `rc` in its version tag because it is the release candidate.
- Every push may also get `:sha-<7-character commit>` to trace a build back to its commit.
- Tags use `-`, not `+` (Docker does not accept semver build metadata).

## Versions

Format `MAJOR.MINOR.PATCH[-channel.N]`, following semver.

- **The first version is `0.1.0-prealpha.1`.** Avoid `0.0.0`, which often reads as a placeholder. The
  `0.x` prefix marks the API as unstable.
- **`1.0.0`** is the first stable release; after that, breaking changes increase the major version.
- The numbers before `-` are the **target version** and stay fixed for the whole cycle. The number after
  the channel (`N`) increases with every push on the same channel, starts at 1, and **restarts at 1 when
  the channel changes**.
- After a release, the next cycle increases the target version (`0.2.0-prealpha.1`) instead of continuing
  `0.1.0-prealpha.N`.
- Always write three numbers (`0.1.0`, not `0.1`) in version tags so semver tools (Renovate, Dependabot,
  sorting scripts) read them correctly. `:0.1` and `:0` are only floating tags on the release channel.

One cycle:

```
0.1.0-prealpha.1 … .N → 0.1.0-alpha.1 … → 0.1.0-beta.1 … → 0.1.0-rc.1 … → 0.1.0
                                                                              ↓
                                                         0.2.0-prealpha.1 … (next cycle)
```

Tags produced by one prealpha push:

```
domain.com/em-api:0.1.0-prealpha.2     ← fixed
domain.com/em-api:prealpha             ← moves to this build
domain.com/em-api:sha-1a2b3c4          ← fixed
```

## Notes

- **Semver ordering.** Semver compares prerelease labels alphabetically, so `prealpha` sorts after `beta`
  (`alpha` < `beta` < `prealpha` < `rc`). This matters only for tools that sort tags automatically. If it
  becomes a problem, prefix prealpha with a numeric identifier (`0.1.0-0.prealpha.N`) so it always sorts
  lowest, as the [NuGet convention](nuget-naming.md) already does.
- **Where `N` comes from.** A per-channel counter (for example derived from git tags
  `v0.1.0-prealpha.N`) so it restarts when the channel changes. CI run numbers are simpler but neither
  contiguous nor reset.
- **GHCR.** Package visibility is set separately from the repository. There is one package per image, so all
  channels share the same access settings.
- Usage: `docker pull domain.com/em-api:beta` follows a channel; `docker pull domain.com/em-api:0.1.0-beta.3`
  pins one build.

## EmPorium House publisher (2026-10-04)

For the EmPorium House `upload-api-ghcr` script, version tags use `X.Y.Z-<channel>.N` on **every** channel,
including release (`0.1.0-release.1`). For that publisher this replaces the suffix-free release tag shown
above. The build number increases while the target version stays the same and starts at 1 for each new
version/channel combination. Version tags are still never overwritten.

- Release updates the floating tags `release`, `latest`, `MAJOR.MINOR` and `MAJOR`.
- Beta, alpha and prealpha update only their own channel's floating tag.
- `latest` is updated only when the user chooses release and confirms the push.
