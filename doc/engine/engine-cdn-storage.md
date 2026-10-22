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
Unknown file types download as application/octet-stream. GET browsers accepting HTML see a directory
listing through the registered skin; non-HTML clients receive no listing.
Dot-prefixed, hidden and system entries are ordinary public files, including `.htaccess`. The one
exception is the folder settings file `.meta` (see [Folder settings](#folder-settings-meta)), which is never
listed or served.
Never place confidential content in `files/`. Symlinks/reparse points and traversal are refused.
Missing paths and disabled CDN return 404; GET clients accepting HTML receive the CDN skin not-found page,
while other clients receive no body. The other three directories are never public.

Public downloads and both CDN management services are exempt from ActionRateLimit. Authentication,
claims and path validation still apply to actions. Existing downloads keep their tracked read handle
with FileShare.Delete, so removing/replacing a file does not interrupt that download.

## CDN Manager

`ICdnServices` requires Administrative Tools:CDN Manager Access. Storage settings use the separate
CDN Settings Manage claim. Paths use `/` relative to `files/`; empty means its root.
The main toolbar browses, uploads, moves, creates folders, downloads and creates/extracts zip archives.
Archive, extraction and copy remain global business tasks with cancellation/status actions.

- **Folder Settings** edits the settings of the open folder (read-only in managed folders) and exports
  the canonical `.meta` file.
- **Show hidden files** (off by default, kept for the session) also lists rows hidden from the public
  listing, dimmed with a crossed-eye marker. Display only: sizes, copy and move always include them.
- **Rename** (row button or `F2`) renames a file or folder inside its folder; 409 for a taken name or a
  managed folder, 400 for an invalid name.
- **Copy**: Ctrl+drag a row onto a folder (a plain drag moves). The copy is written to `temp/` and moved
  into place when complete, carries every `.meta` with it, refuses a folder copied into itself, a
  duplicate name (no automatic "copy of"), and a managed destination. Replacing a file sends the old one to
  the recycle bin, like Move; folders are never merged. The ordinary Max file size does not apply. There
  is no Copy to... command yet because no CDN folder picker exists.

**More** opens maintenance:

- Recycle bin: list original paths, Restore, Delete permanently, Empty Recycle Bin.
- Clear Temporary Files: review garbage bytes/count, then remove garbage only. Active operations and
  open retired downloads are excluded. There is no automatic startup cleanup.
- Managed folders: owner app_id, usage/quota, optional byte quota, Release by typing the folder name.

Delete and ordinary overwrite rename the previous payload into `recycle/<id>/payload/<original-name>`;
`item.json` records original path, UTC deletion time, authenticated user, size and file/folder kind.
A directory is one item. Retention is manual. Purge skips items still read by a tracked download.
Restore recreates parents but refuses existing paths and destinations inside/containing managed folders;
there is no Replace or Keep both. Storage totals include `files/` + `temp/` + `recycle/`, including dot files
but never `.meta`.

## Folder settings (.meta)

A folder can carry settings in a file named exactly `.meta` (compared case-insensitively on Windows),
similar to `.htaccess`. They apply **to that folder only**; subfolders do not inherit them.

```json
{
  "version": 1,
  "publicListing": false,
  "hiddenFiles": ["*"],
  "shownFiles": ["release.json", "release.json.sig"]
}
```

- `version` is required and must be `1`. Unknown keys are refused. The file is UTF-8 JSON (a BOM is
  allowed) of at most 64 KiB.
- `publicListing` (default `true`): `false` makes the web listing of the folder 404. Files directly in it
  keep downloading by direct link and are sent with `X-Robots-Tag: noindex`; folders whose listing is on
  get no such header.
- `hiddenFiles` / `shownFiles` (default empty, at most 256 each): name patterns for entries directly in
  the folder, files and subfolders alike. `*` matches any run of characters, `?` one character; no `/`,
  `\` or characters invalid in a file name. Shown wins over hidden. Hidden entries are left out of the
  web listing but are still served by direct link; a hidden subfolder still lists through its own URL
  unless it has its own `.meta` with `publicListing: false`.

`.meta` is never visible: not in the web listing, CDN Manager or Release Manager listings (even with Show
hidden files), trees, counts, sizes or quotas. Every `/cdn` path to it - GET, HEAD, Range or listing -
answers 404, checked on the real name on disk, so `.META`, `%2emeta`, `.meta.`, `.meta%20`,
`.meta::$DATA` and Windows 8.3 short names are refused too. Manager actions given a path to `.meta`
answer 404. A folder named `.meta` cannot be created, renamed to, extracted or copied (400).

Settings always replace the previous ones as a whole and take effect at once. The server stores the
canonical form (camelCase, indented); Export returns the same text. Ways in:

1. **Folder Settings** in CDN Manager (`GetMeta_CdnFolderSettings`, `PostMeta_CdnFolderSettingsSave`,
   `GetMeta_CdnFolderSettingsExport`).
2. Uploading a file named `.meta`: valid content becomes the folder settings (the overwrite flag is
   ignored, the folder entry is returned); invalid content answers 400 and keeps the old settings.
3. Renaming a file to `.meta`: valid content becomes the settings and the file is removed (not
   recycled); invalid content answers 400 and the file keeps its name.
4. Extracting a zip: each valid `.meta` entry replaces the settings of its folder regardless of the
   overwrite choice and is never a conflict; an invalid one is skipped, the rest is extracted, and the
   task's final caption names the skipped entries and why.
5. Release Manager through `PostMeta_CdnReleaseFolderSettings` (owner only).

Writes are atomic (temp file then replace) and subject to the same managed-folder rules as other writes:
CDN Manager can only read the settings of managed folders. Other operations: Move and Copy carry `.meta`
along; Archive never includes it; Delete removes every `.meta` in the folder tree before the folder goes
to the recycle bin, so a restored folder comes back without settings.

Settings are cached per folder and keyed by the file's write time and length, so manual edits on disk
are picked up. A `.meta` that predates this feature and is invalid stays hidden and unreachable, counts as
no settings, and is logged as a warning once per file version; there is no startup scan.

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
Move / Rename, PostMeta_CdnDelete, PostGetMeta_CdnCopy with GetMeta_CdnCopyTask and
PostMeta_CdnCopyCancel / CopyClear. Folder settings: GetMeta_CdnFolderSettings / FolderSettingsExport,
PostMeta_CdnFolderSettingsSave. Maintenance: GetMeta_CdnRecycle, PostMeta_CdnRestore / RecycleDelete /
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

## CDN skin assets

See [Home and CDN skins](engine-skin.md) for `UseCdnSkin<T>()`, Razor components and page models.
The root name `.skin` is always reserved for embedded CDN skin assets, even with the default skin.
At startup with CDN and its service enabled, `files/.skin` is cleared and rewritten directly, or removed
when no assets are declared. Failure aborts startup. Disabled CDN/service leaves it untouched.

The directory and descendants are hidden from public browsing, Manager listings/tree/count/storage
usage and root archives. Management and release operations targeting it are rejected with 400;
known asset files remain downloadable at `/cdn/.skin/<path>`. Other dot-prefixed names remain public.
Home and favicon do not use this directory. Use one instance per CDN storage parent.
