# Upgrade an empty single-feed NuPak installation

Earlier NuPak versions served a single feed. This runbook upgrades an **empty** legacy installation to the
multi-feed schema described in [NuGet server](engine-nupak.md). It never creates a feed, resets a table or
adopts old package files. A populated legacy installation needs a separate migration design with explicit
destination feeds.

New installations do not need this page: run `tables/040-nupak.sql` and the `views/vi_NuPak*.sql` files.

## Steps

1. **Stop** every old Em System API process or service that uses this database or store. Deploy one API
   instance per NuPak database and store; shared stores are not supported. Keep the old binaries for
   rollback, but never run them against the new schema.
2. **Check the tables.** `ta_NuPakPrefix`, `ta_NuPakPackage`, `ta_NuPakVersion` and `ta_NuPakPrefixRobot`
   must all be empty on the legacy schema. The update script repeats this check before changing anything.
   Legacy audit history is kept with a null feed ID and the snapshot `Legacy single-feed history`.
3. **Check the store.** Inspect the configured NuPak root, especially `packages/`, including hidden files
   and `.purge` recovery files. Any legacy artifact stops the upgrade. Do not delete, move or import these
   files automatically; empty old folders may stay. The host also rejects legacy artifacts.
4. **Back up** the whole core database (shared with engine identity and the registry) together with the
   NuPak store, to a protected location, and record them as a matched pair. `NuPakEnable` is preserved;
   the legacy `NuPakAnonymousRead` remains metadata only and is never inherited by new feeds.
5. **Run the scripts:** `doc/sqlscript/mssql/updates/20261003-NuPakMultiFeed.sql`, then
   `doc/sqlscript/mssql/tables/040-nupak.sql` and the `doc/sqlscript/mssql/views/vi_NuPak*.sql` files. All
   are idempotent; running them again on a populated multi-feed schema keeps feeds and packages. Neither
   script creates feeds.
6. **Deploy** the backend and WPF client together. Startup requires `ta_Meta.NuPakSchemaVersion=2` and
   validates the tables before store cleanup or recovery; it does not migrate SQL. Verify that there are
   zero feeds, that the server switch kept its value, and that `/nuget/v3/...` and `/nuget/v2/...` return
   404 for GET, HEAD, PUT and DELETE.
7. **Create feeds** in NuGet Manager. New feeds start disabled and private. Create prefixes, grant robots
   R/W per prefix in User Manager, enable the feed, and enable the server.

## Rollback

After any new feed data exists, rollback means restoring the **matched database and store backup pair**
and redeploying the old binaries. Never point old binaries at the new schema and never restore only one
side. For an empty upgrade, also restore the backup rather than reversing individual constraints by hand.

## Maintainer notes

The local NuPak harness (kept outside the repo) can run steps 5–6 automatically, using the local
configuration without printing credentials. Stop the old API and take the backups in steps 1–4 first.

```powershell
dotnet run --project ..\.artefacts\em-system\scripts\nupak-smoke -- . --upgrade-schema
dotnet run --project ..\.artefacts\em-system\scripts\nupak-smoke -- . --install-schema
```

The harness inspects the standard host store path; for a customized root, inspect the configured root
yourself before running it.
