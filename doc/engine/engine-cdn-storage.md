# CDN

The API host can serve a folder on the server as a public, read-only CDN under `/cdn/...`. Content is
managed from the WPF **CDN Manager** screen through `ICdnServices`.

## Enabling

Recommended: call `builder.AddManagedStorageSettings()` in `Program.cs`. The CDN is then enabled by
default at `./data/cdn` with a 200 MB upload limit, and both values can be changed from the CDN Manager
Settings card (applied after an API restart). See [Storage settings](engine-storage-settings.md).

Hosts that prefer fixed configuration can call `builder.EnableCdn(rootPath)` or
`builder.EnableCdn(rootPath, maxFileSizeMb)` instead. The two modes cannot be combined.

## Public download

- `GET`/`HEAD /cdn/<path>` is served by ASP.NET Core static files: Range, `If-Range`, `ETag`, 206 and 416
  are supported, unknown file types download as `application/octet-stream`, and folders show a directory
  listing.
- Names starting with a dot, and hidden or system entries, are never served. This also keeps temporary
  upload files private.
- `/cdn` is outside the action dispatcher: no claims, no action timeout and no rate limit (download
  managers open many parallel Range connections). Anything not found, or everything while the CDN is off,
  returns a plain 404.

## Managing content

All `ICdnServices` actions require the **CDN Manager Access** claim in module `Administrative Tools`, and
return 404 when the CDN is disabled. Paths are relative to the CDN root and separated by `/`; `null` or an
empty string is the root. Paths that escape the root, or that name a dot-prefixed entry, are rejected with
400.

| Action | Purpose |
| --- | --- |
| `GetMeta_CdnFolder(path)`, `GetMeta_CdnTree(path)`, `GetMeta_CdnItemCount(path)` | Browse |
| `PostGetMeta_CdnUpload(request, content)` | Streamed upload |
| `PostGetMeta_CdnCreateFolder`, `PostGetMeta_CdnMove`, `PostMeta_CdnDelete` | Folder and file operations |
| `PostGetMeta_CdnArchive`, `GetMeta_CdnArchiveTask`, `PostMeta_CdnArchiveCancel`, `PostMeta_CdnArchiveClear` | Create a zip as a background task |
| `GetMeta_CdnExtractConflicts`, `PostGetMeta_CdnExtract`, `GetMeta_CdnExtractTasks`, `PostMeta_CdnExtractCancel`, `PostMeta_CdnExtractClear` | Extract a zip as a background task |
| `GetMeta_CdnStorageSize` | Storage size (see below) |
| `GetMeta_CdnStatus`, `GetMeta_CdnSettings`, `PostGetMeta_CdnValidateDirectory`, `PostGetMeta_CdnSettingsSave` | Status and settings (settings require **CDN Settings Manage**) |

Archive and extract run as global business tasks: the action returns immediately and the status is
polled. Every holder of the CDN claim can see, cancel and clear these tasks, including ones started by
another user. Only one archive may run on the whole server, and one zip file cannot be extracted twice at
the same time.

## Storage size

`GetMeta_CdnStorageSize()` returns `CdnStorageInfo` with `TotalBytes` and `FileCount` (both 64-bit).

- It sums the length of public files in the whole CDN tree. Dot-prefixed names (including temporary
  upload/archive files), hidden/system entries and symlinks/reparse points are excluded.
- The size is the file payload according to the filesystem, not physical volume usage, free space or
  overhead. Identical files or hard links at different paths are counted per path.
- The scan reads metadata only, but its cost grows with the number of files and folders. The request's
  cancellation token is honored. An inaccessible folder fails the request instead of returning a partial
  total. Files changing during the scan can change the result or fail the request; refresh to retry.

The **CDN Manager** and **Container Manager** cards on the default home screen show the storage size. Each
card has a small refresh button in its top right corner that reloads only that card, prevents duplicate
requests, and becomes available again after success or failure. On home reload both sizes load
independently, and only cards the user may open request data. Failed or disabled states are shown as a
status, never as zero.

The CDN Manager shows the global total, the file count and a scope note above the toolbar. Refresh, folder
navigation and reloads after upload/delete/move/archive/extract read the size again. The total always
covers the whole CDN, even while a subfolder is open.

## Maintainer notes

Local smoke test (harness kept outside the repo): `dotnet run --project ..\.artefacts\em-system\scripts\cdn-storage-smoke`
from the repo root. It uses an isolated fixture, cleans up in `finally` and does not touch the live CDN.
