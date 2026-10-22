# CDN storage and managed release folders

Enable with `builder.AddManagedStorageSettings()` (default `./data/cdn`, 200 MB ordinary upload limit),
or use `builder.EnableCdn(path, maxFileSizeMb)` for static configuration. See [Storage settings](engine-storage-settings.md).

## Filesystem layout

The configured directory is a parent containing four automatically created directories:

```text
data/cdn/
  files/       public /cdn/ root
  temp/        uploads, archives, extraction and retired open files
  recycle/     manually retained deleted items
  settings/    managed.json, cdn.json, service.json
```

Use one API instance per CDN parent. Operations and open downloads are tracked in process memory;
sharing a parent between processes is unsupported. All directories must remain on the same volume.
There is no migration: older content outside these directories is neither served, listed nor removed.
Administrators move or remove it manually.

## Public downloads

GET/HEAD `/cdn/<path>` supports Range, If-Range, ETag, 206 and 416 through ASP.NET Core static files.
Unknown file types download as application/octet-stream. Directories show a public listing.
Dot-prefixed, hidden and system entries are ordinary public files, including `.htaccess`.
Never place confidential content in `files/`. Symlinks/reparse points and traversal are refused.
Missing paths and disabled CDN return a plain 404. The other three directories are never public.

Public downloads and both CDN management services are exempt from ActionRateLimit. Authentication,
claims and path validation still apply to actions. Existing downloads keep their tracked read handle
with FileShare.Delete, so removing/replacing a file does not interrupt that download.

## CDN Manager

`ICdnServices` requires Administrative Tools:CDN Manager Access. Storage settings use the separate
CDN Settings Manage claim. Paths use `/` relative to `files/`; empty means its root.
The main toolbar browses, uploads, moves, creates folders, downloads and creates/extracts zip archives.
Archive and extraction remain global business tasks with cancellation/status actions.

**More** opens maintenance:

- Recycle bin: list original paths, Restore, Delete permanently, Empty Recycle Bin.
- Clear Temporary Files: review garbage bytes/count, then remove garbage only. Active operations and
  open retired downloads are excluded. There is no automatic startup cleanup.
- Managed folders: owner app_id, usage/quota, optional byte quota, Release by typing the folder name.

Delete and ordinary overwrite rename the previous payload into `recycle/<id>/payload/<original-name>`;
`item.json` records original path, UTC deletion time, authenticated user, size and file/folder kind.
A directory is one item. Retention is manual. Purge skips items still read by a tracked download.
Restore recreates parents but refuses existing paths and destinations inside/containing managed folders;
there is no Replace or Keep both. Storage totals include `files/` + `temp/` + `recycle/`, including dot files.

## Managed release folders

`ICdnReleaseServices` requires only Administrative Tools:Release Manager Access, registered by the API.
Selecting an empty folder in Release Manager adopts it; a missing folder can be created at selection.
A physically nonempty folder (even an empty child directory or hidden file) cannot be adopted.
Managed roots cannot be ancestors or descendants of another managed root.

`settings/managed.json` stores entries such as:

```json
{ "path": "inventory-release", "owner": { "kind": "release", "id": "Contoso.Inventory" }, "quotaBytes": null }
```

Only the owning app_id may write through Release Manager actions. CDN Manager can browse/download,
but cannot modify, move, archive, extract or delete managed content or its ancestors.
An admin can Release the root through CDN Manager; the owning product can also release it.
Release removes ownership, not payload. Release Manager deletions bypass recycle; an open file is
retired to temp and deleted when its final reader closes, or by a later Clear Temporary Files.

New roots have unlimited quota. CDN administrators can set any nonnegative byte quota, including below
current usage; no existing content is deleted. Preflight checks the complete final Sync inventory,
including metadata, before the first write, and every managed upload enforces live usage again.
A rejected Sync reports both quota and resulting size. Ordinary Max file size does not apply to managed
uploads. Usage excludes temp/recycle. See [Release Manager](engine-release-manager.md).

## Actions

Browse: GetMeta_CdnFolder / Tree / ItemCount. Ordinary mutations: PostGetMeta_CdnUpload / CreateFolder /
Move, PostMeta_CdnDelete. Maintenance: GetMeta_CdnRecycle, PostMeta_CdnRestore / RecycleDelete /
RecycleEmpty, GetMeta_CdnTemporaryGarbage, PostMeta_CdnTemporaryClear, GetMeta_CdnManagedFolders,
PostMeta_CdnManagedRelease / ManagedQuota. Release actions use the CdnRelease prefix and check ownership.
GET accepts simple per-field parameters; Sync preflight and streamed upload use POST payloads.

## Runtime maximum file size

All engine defaults are **200 MB**, including `EnableCdn(path)` and host configuration. Host values
(`EnableCdn(path, mb)`, `CdnMaxFileSizeMb`, managed `MaxUploadMb`) seed the limit only until the first
runtime save. The persisted `settings/cdn.json` then wins even after restart or host-default changes:

```json
{ "maxFileSizeMb": 200 }
```

In CDN Manager, use the pencil beside **Max … per file**, enter an integer **1–1,048,576 MB** (1 MB =
1,048,576 bytes), then **Save** or **Cancel**. This requires **CDN Manager Access**, not the storage-settings
claim. The numeric field has range/effect help on hover. Save uses `PostMeta_CdnMaxFileSize(int)` and an
atomic same-directory temporary file; a failed write leaves the active limit unchanged. The change is
audited through the engine settings audit when enabled. Cancel discards the draft without a request.

The next upload/extract uses the new value on the same store instance. Each operation snapshots its limit
at entry: an in-flight upload/extract keeps that value. Extracted entries may each use at most the limit,
and their total at most ten times the limit; the 10,000-entry cap is unchanged. Managed folders remain
quota-controlled and show no max-file pencil. Their uploads are exempt from the ordinary per-file limit.

The CDN Settings card has been removed in every host mode. Paths and host feature enablement belong in
host configuration; managed-storage actions/claims remain for compatibility with the other managers.

The header also provides the shared runtime service switch. See [Runtime service availability](engine-storage-settings.md#runtime-service-availability) for admin rights, persistence, 404/503 and host-feature visibility.
