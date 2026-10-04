# NuGet Server

Em System serves independent NuGet V3 feeds at `/nuget/{slug}/v3/index.json`, with zero feeds on installation and no built-in feed. NuGet Manager controls packages, prefixes, robot access visibility, recycle bin and audit. Enable and anonymous-read settings apply immediately; the default is off with anonymous reads disabled.

## Host setup (developers)

Reference `Em.Api.Core.NuPak` and call `builder.AddNuPak("./data/nuget")` in the API host. The relative path is resolved from ContentRoot and created at startup. Optional `maxPackageMb` defaults to 250. Run `doc/sqlscript/mssql/tables/040-nupak.sql` on the core database after the engine identity schema. Reference `Em.Api.Core` and call `builder.AddNuPak()` in the WPF host. Removing the API registration removes both feed and management actions.

## Using the feed

1. Open NuGet Manager, explicitly create and select a feed, then create a prefix such as `MatrixCode.`. A package must match a registered active prefix before it can be pushed. The longest matching prefix owns new package IDs; adding a more specific prefix later does not move existing packages.
2. In User Manager → Robots, create a robot and assign NuGet access to the prefix: **R** restores; **W** restores, pushes and deletes into the recycle bin. Robot ownership does not inherit user rights. Keep the generated token private.
3. Enable the selected feed and the main server separately. Anonymous read applies only to that feed. An incorrect credential still receives 401.

Use HTTPS in production. For local HTTP, opt in with `allowInsecureConnections`. Package source mapping ensures private IDs resolve only through your feed. Replace `MatrixCode.*` with your actual prefix and configure other sources only if needed.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="Em" value="http://localhost:5232/nuget/alpha/v3/index.json" allowInsecureConnections="true" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="Em"><package pattern="MatrixCode.*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Configure Basic credentials using the robot name as username and its token as password, preferably through `NuGetPackageSourceCredentials_Em` rather than a committed config. For example, in the current PowerShell session:

```powershell
$env:NuGetPackageSourceCredentials_Em = 'Username=build-robot;Password=<robot-token>;ValidAuthenticationTypes=Basic'
dotnet restore --configfile .\nuget.config
dotnet package search MatrixCode --configfile .\nuget.config --format json
dotnet nuget push .\MatrixCode.Example.1.0.0.nupkg --source http://localhost:5232/nuget/alpha/v3/index.json --api-key '<robot-token>' --allow-insecure-connections
dotnet nuget delete MatrixCode.Example 1.0.0 --source http://localhost:5232/nuget/alpha/v3/index.json --api-key '<robot-token>' --non-interactive
```

Delete recycles the version, preserving its file and storage usage. The reserved ID/version cannot be pushed again (409) until purged. Restore makes it visible again. Purge and Empty Recycle Bin permanently remove recycled files and require **NuGet Settings Manage**. Feed creation, editing, deletion and server settings require **NuGet Settings Manage**. Read/list/statistics/audit, prefix management and recycle/restore require **NuGet Manager Access**. Switching off preserves data and leaves the manager available; all feed requests receive 404.

## Scope and limits

Multiple feeds and one API instance per store/database. On Windows, package IDs starting with a reserved device name (such as `CON`) cannot be stored. No public-feed proxy, symbols, autocomplete, signing enforcement, download counters, automatic purge, or authentication failure throttle. Server and feed settings are read directly from committed database metadata; changes apply to new requests immediately. Storage is calculated from package metadata, including recycled versions, rather than filesystem overhead. A crash after a push file move but before SQL commit can leave an orphan file: startup refuses conflicting pushes until an administrator reconciles it. Purge recovery files are reconciled against committed metadata at startup.

Protocol reference: [NuGet Server API](https://learn.microsoft.com/en-us/nuget/api/overview). See the execution plan for actual validation and outstanding checks.

## Multi-feed operation

The former single-feed behavior is superseded. Installation, migration, startup and
server enablement create zero feeds. Explicitly create a feed in NuGet Manager,
select it, create prefixes and grant robots R/W per **Feed / Prefix** in User Manager.
New feeds are disabled and private. Enable the feed and the main server separately.
Anonymous read is per feed; wrong credentials return 401 even on anonymous feeds.
Server/feed off returns 404 before authentication, while management remains available.
Robot ownership does not inherit user rights or access to other feeds.

Every protocol URL requires a slug: `/nuget/{slug}/v3/index.json`. All resources use
that same feed URL. Old `/nuget/v3/...` and `/nuget/v2/...` return 404 for all methods,
without aliases or redirects. A user-created `default` is an ordinary removable feed.
Slugs are immutable, 1–64 lowercase ASCII letters/digits with internal hyphens;
reserved internal paths including v2/v3 are rejected.

Prefix names and package IDs/versions are unique within a feed. Identical ID/version
can contain different artifacts in different feeds. Configure two explicit sources:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="HouseAlpha" value="https://house.example/nuget/alpha/v3/index.json" />
    <add key="HouseBeta" value="https://house.example/nuget/beta/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="HouseAlpha"><package pattern="Alpha.*" /></packageSource>
    <packageSource key="HouseBeta"><package pattern="Beta.*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Use unambiguous source mapping. To test identical ID/version from different feeds,
restore from one source at a time with separate `--packages` folders and
`--no-cache --force`: a shared NuGet cache can conceal artifact provenance.

Home shows total/effective feeds, packages/versions/storage and the main server
toggle, with Open leading to the manager. Each selected feed has its own canonical
URL, refresh, enabled/anonymous settings, Packages, Prefixes, Recycle Bin and Audit.
Entire-server audit includes global events and deleted-feed history.

Storage uses `{root}/feeds/{feedId}/packages/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg`.
Paths use immutable ULIDs, never request slugs. Empty feed deletion requires no
active or recycled packages; it deletes empty prefixes/grants but retains audit
snapshots. The last feed can be deleted. No multi-instance store sharing,
proxy/mirror, symbols, quotas or moving packages between feeds is supported.

Feed create/update/delete, global settings, purge and empty bin require
**NuGet Settings Manage**; read/list/statistics/audit, prefix management and
recycle/restore require **NuGet Manager Access**. There are no per-feed user claims.

Existing empty installations must follow [the upgrade runbook](engine-nupak-multifeed-upgrade.md).
Startup requires marker `NuPakSchemaVersion=2`, validates SQL before store recovery,
and does not migrate automatically. Legacy `NuPakAnonymousRead` remains metadata
only and is not inherited by new feeds. Populated legacy migrations require a
separate explicit destination-feed design; no data is reset or adopted.

## Engine integration

`builder.AddNuPak()` requires `AddManagedStorageSettings()`. The NuGet settings card controls Enabled, Directory, MaxUploadMb (1�4096); Save requires API restart. Older storage documents default to enabled, `./data/nuget`, 250 MB. Managed disabled means no protocol endpoint; runtime NuPakEnable still defaults off. Static hosts use `builder.AddNuPak(path, maxPackageMb)`. WPF navigation admin.nupak is included in core; no module registration is needed.

## Publish from WPF

The manager includes a shared Publish tab and publish history. Configure host-scoped robot credentials independently of the GUI session. See [engine-publish.md](engine-publish.md) for profiles, Prepare/Push/Verify, file sets, Compose and ordered Base/App Sets.
