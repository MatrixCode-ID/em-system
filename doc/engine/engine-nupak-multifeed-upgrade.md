# Upgrade an empty single-feed installation

This upgrade supports an **empty** legacy NuPak installation. It never creates a
feed, resets a table, or adopts old package files. Populated legacy installations
need a separate migration design with explicit destination feeds.

1. Stop every old Em System API process/service using this database/store.
   Deploy one API instance per NuPak database and store; shared-store instances
   are not supported. Keep the old binaries for rollback, but do not run them
   against the new schema.
2. Inspect `ta_NuPakPrefix`, `ta_NuPakPackage`, `ta_NuPakVersion`, and
   `ta_NuPakPrefixRobot`. All must be empty on the legacy schema. The update SQL
   repeats this check before changing anything. Legacy audit-only history is
   retained with null feed ID and `Legacy single-feed history` as its snapshot.
3. Inspect the configured NuPak root, especially `packages/`, including hidden
   files and `.purge` recovery files. Any legacy artifact stops the upgrade.
   Do not delete, move, or automatically import these files. Empty old folders
   may remain. The host and upgrade harness also reject legacy artifacts.
4. Back up the **whole core database** (shared with engine identity and registry),
   plus the NuPak store, to a protected location. Record the matched backup pair.
   `NuPakEnable` is preserved; legacy `NuPakAnonymousRead` remains metadata only
   and is never inherited by new feeds.
5. Run `doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql`, followed by
   `doc/sqlscript/mssql/tables/040-nupak.sql`. Both are transactional and idempotent;
   rerunning them on populated multi-feed schema preserves feeds/packages.
   New installations use only the set script. Neither script seeds feeds.
6. Deploy backend and WPF together. Startup requires schema marker
   `ta_Meta.NuPakSchemaVersion=2` and validates tables before store cleanup/recovery.
   Startup does not migrate SQL. Verify zero feeds, the retained server toggle,
   and 404 on old `/nuget/v3/...` and `/nuget/v2/...` for GET/HEAD/PUT/DELETE.
7. Create feeds explicitly in NuGet Manager. New feeds start disabled/private.
   Create prefixes, grant robot R/W per prefix in User Manager, enable the feed,
   and enable the main server. Every feed is ordinary, including a user-created
   slug `default`. The last empty feed can be deleted.

Rollback after any new feed data requires restoring the **matched database and
store backup pair** and redeploying old binaries. Never point old binaries at the
new schema or restore only one side. Empty upgrade rollback should also restore
the backup rather than try to reverse individual constraints manually.

Local automated schema update mode (uses configuration/environment without
printing credentials):

```powershell
dotnet run --project scripts/nuget-smoke -- . --upgrade-schema
dotnet run --project scripts/nuget-smoke -- . --install-schema
```

Stop the old API and take the backups in steps 1–4 **before** these commands.
The commands inspect the standard host store path; for a customized root,
inspect the actual configured root explicitly before running them.
