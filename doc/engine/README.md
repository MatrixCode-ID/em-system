# Engine guides

Guides for the shared `Em.*` engine: what each feature does, how a host enables it, and how modules use
it. Build and configuration of the hosts themselves are covered in [src/backend/README.md](../../src/backend/README.md)
and [src/frontend/README.md](../../src/frontend/README.md);
solutions, tests and NuGet packaging are covered in [Build and packaging](build.md).

## Server features

| Guide | Covers |
| --- | --- |
| [Business Module Manager](engine-business-module-manager.md) | Tenant directory, department hierarchy, employees and contact-based lookups |
| [Approval engine](engine-approval.md) | Deferred Data changes, server-built System flows, signer settings, module editors, attachments and MY TASKS |
| [Home and CDN skins](engine-skin.md) | Builder skin registration, Razor components, reserved assets, favicon and fallback upgrade |
| [CDN](engine-cdn-storage.md) | Public `/cdn` downloads, CDN Manager actions, storage size |
| [Container registry](engine-registry.md) | OCI `/v2` registry, roots and containers, Container Manager, Docker testing |
| [NuGet server (NuPak)](engine-nupak.md) | NuGet V3 feeds, prefixes, recycle bin, NuGet Manager |
| [Object storage](engine-object-storage.md) ([Indonesia](engine-object-storage.id.md)) | Buckets, versions, deduplicated blobs on volumes, policies, `IObjectStore`, `/store`, Object Storage Manager |
| [NuPak multi-feed upgrade](engine-nupak-multifeed-upgrade.md) | Runbook for older single-feed installations |
| [Storage settings](engine-storage-settings.md) | Managing CDN, registry and NuGet stores from the UI |
| [SMTP](engine-smtp.md) | Named SMTP profiles, default email service, compatibility and tabular SMTP Manager |
| [Robot identities](engine-robots.md) | Robot accounts, tokens, owners and grant providers in User Manager |
| [Module protocol endpoints](engine-public-endpoints.md) | `AddPublicEndpoint`, module infrastructure, robot authentication |
| [In-process calls](engine-in-process-calls.md) | Calling another service's actions from an action on the caller's behalf, and which checks apply |
| [Log engine](engine-log.md) | Data audit (`IAuditWriter`, `AddAudit`), engine audit `System.*`, telemetry and client logs, log stores, retention, Log Manager |

## Desktop (WPF) features

| Guide | Covers |
| --- | --- |
| [Contact picker](engine-contact-picker.md) | Reusable contact search dialog, caller filters and server paging |
| [Publish](engine-publish.md) ([Indonesia](engine-publish.id.md)) | Publishing NuGet packages and container images from NuGet/Container Manager |
| [Container registry user guide](engine-registry-guide.md) ([Indonesia](engine-registry-guide.id.md)) | Step by step through Container Manager, Robots and Publish, every option on annotated screenshots |
| [Release Manager](engine-release-manager.md) | Preparing, signing and verifying desktop client releases |
| [Launcher and updates](engine-launcher.md) | Product installer, in-app update preparation, IT Cabinet packages |
| [Login branding](engine-login-branding.md) | Material/Classic login and background images |
| [User Manager roles](engine-user-manager.md) | The Roles tab of the user editor: giving roles, periods, saving |
| [Debug mode](engine-debug-mode.md) | Switch User, Simulate Login and the debug switches of `EmApp` |
| [Tab strip](engine-tab-strip.md) | Reorderable panel tabs, optional leading content and cascading menu styles |

## Reference module

| Guide | Covers |
| --- | --- |
| [Em.Test test module](engine-test-module.md) | Sample module exercising almost every engine feature |

## Conventions used in these guides

- **Claims** are written without their module prefix when the module is obvious; engine administration
  claims belong to module `Administrative Tools`.
- **SQL scripts** live in `doc/sqlscript/mssql/` and are run in order: `sets/`, `tables/` by number, then
  `views/`. `updates/` is only for migrating older databases.
- **Screenshots** are rendered offline from the actual WPF controls with sample data, in the light theme.
  Every screen also supports the dark theme.
- **Maintainer notes** sections refer to local verification harnesses kept outside this repository; they
  are not needed to use the engine.
