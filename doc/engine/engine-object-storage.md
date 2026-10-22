# Object storage

[Bahasa Indonesia](engine-object-storage.id.md)

A generic object storage engine inside the Em API: **buckets** hold **objects** (keys), every change of an
object is a **version**, every version points to a content **blob** identified by its SHA-256, and every blob
has one or more physical **copies** on **volumes**. A **policy** per bucket decides placement and replicas,
version retention, quotas and public access.

This is an internal Em service: server-side modules use `IObjectStore`, administrators use the
**Object Storage Manager** in the WPF client, and public-read buckets are served at `/store`. The S3 protocol
is **not** implemented; the design only keeps the door open for an S3 adapter later (see
[S3 readiness](#s3-readiness)).

## Enabling it

1. Run on the core database (SQL Server), after `tables/010-core.sql`:
   - [`doc/sqlscript/mssql/tables/060-objectstore.sql`](../sqlscript/mssql/tables/060-objectstore.sql)
   - then the views [`views/vi_StoreBucket.sql`](../sqlscript/mssql/views/vi_StoreBucket.sql),
     [`views/vi_StoreObject.sql`](../sqlscript/mssql/views/vi_StoreObject.sql) and
     [`views/vi_StoreVolume.sql`](../sqlscript/mssql/views/vi_StoreVolume.sql).

   All scripts can be run again safely. No migration of existing data is needed: the CDN, container
   registry, NuGet server and approval storage are not moved into object storage.
2. In the API host, call `builder.AddObjectStorage()`. The sample host `Em.Api` does this unless the module
   switch `"modules": { "objectStorage": false }` (or `EM_MODULE_OBJECTSTORAGE=false`) turns it off.
3. Give administrators the claims below, open **Object Storage Manager**, create at least one volume, one
   policy and one bucket. Buckets that modules declare as [managed buckets](#managed-buckets) need none of
   this: they are created at startup on a default volume.

Startup fails with a clear message when the tables or the schema version row (`ObjectStoreSchemaVersion` in
`ta_Meta`) are missing.

Volumes, policies and buckets live in the database, not in `emapi-config.json`; saving them takes effect
immediately, without a restart.

### Claims

Both in module `Administrative Tools`:

| Claim | Allows |
| --- | --- |
| `Object Storage Manager Access` | Open the manager; list buckets, objects, versions, volumes, policies, status; upload, download, delete (delete marker), restore |
| `Object Storage Settings Manage` | Volumes, policies, create/edit/delete buckets, permanently delete versions, garbage collection, retention and repair runs |

### One instance only

Object storage assumes **one API instance per database and volume set**. Blob locking and garbage collection
use an in-process lock. At startup and every few minutes the instance renews a lease row
`ObjectStore.Lease` in `ta_Meta`; when another instance renewed it within ten minutes a warning is logged. This
is a warning, not a guarantee.

## Concepts

| Item | Rules |
| --- | --- |
| Bucket name | S3 rules: 3–63 lowercase letters, digits, `-` and `.`, starting and ending with a letter or digit, no `..`, not an IP address, no `xn--` prefix or `-s3alias` suffix. Fixed after creation. |
| Object key | UTF-8, case-sensitive, 1–1024 bytes, no control characters, no `.` or `..` segment. `/` is only a convention for prefix listing; there are no real folders. Keys never become file paths. |
| Version | Always on. An upload to an existing key adds a new current version. Version numbers come from a per-object counter that never goes back. |
| Delete | Adds a **delete marker** as the current version: the object disappears from listings but its history stays. Deleting an already deleted object changes nothing. |
| Restore | Adds a new version that points to the content of an older version; no bytes are copied. |
| Permanent delete (purge) | Removes one version. When it was current, the newest remaining version that is not a delete marker becomes current; when no version is left the object is removed. |
| Blob | Content identified by SHA-256 and shared across buckets (global deduplication). A CRC32 is kept for quick checks of copies. |
| MIME type and metadata | Per version. Metadata: up to 32 entries, keys of letters, digits, `.`, `_`, `-` (max 128), values up to 1024 characters. |

### Policies

| Field | Meaning |
| --- | --- |
| Volumes (ordered) | Volumes the policy may place blobs on; the first active volume receives uploads first. At least as many volumes as the replica target. |
| Minimum replicas | Copies written before an upload succeeds (at least 1). |
| Target replicas | Copies background replication aims for (≥ minimum, at most 16). |
| Keep old versions | Non-current versions kept per object; empty keeps all, `0` keeps none. |
| Keep old versions for (days) | Maximum age of non-current versions; empty keeps them forever. |
| Quota / object quota | Per bucket: logical bytes of **every kept version** (not physical bytes after deduplication) and number of objects (a deleted object with history still counts). Exceeding returns 409. |
| Max object size | Larger uploads are stopped while streaming and answer 413. |
| Access | `Private`, or `PublicRead` to serve objects at `/store/<bucket>/<key>`. |
| State | A `Disabled` policy keeps its buckets but cannot be given to new buckets. |

Changing a policy's volume list does not move existing data. New uploads follow the new list; existing
blobs get missing copies on the new volumes through repair. There is no automatic rebalancing.

### Volumes

Version 1 has one volume type: **LocalFolder**, a folder on the API server (a local disk or a mounted NAS
share). The path is absolute or relative to the API content root and is checked (reachable, writable, free
space) before saving. A volume that already holds copies keeps its path. States:

- `Active`: read and written.
- `ReadOnly`: read only; its copies still count toward replicas.
- `Offline`: skipped for reads and writes; garbage collection waits for it.

Layout on disk:

```text
<volume>/blobs/<hex[0..2]>/<hex[2..4]>/<sha256-hex>   blob content, read-only after it is written
<volume>/.staging/<ulid>                              uploads in progress, removed at startup
```

## Using it from a module

`IObjectStore` is scoped; take it from dependency injection in a service or a request handler. Buckets must
already exist, except [managed buckets](#managed-buckets), which the module declares and the engine creates at
startup. A module that owns its files should declare a managed bucket instead of asking an administrator to
create one and checking it at runtime.

```csharp
public sealed class InvoiceArchive(IObjectStore store)
{
   public async Task<string> SaveAsync(string invoiceNo, Stream pdf, CancellationToken ct)
   {
      var info = await store.PutAsync("invoices", $"2026/{invoiceNo}.pdf", pdf, "application/pdf",
         new Dictionary<string, string> { ["invoice"] = invoiceNo }, ct);
      return $"{info.Key} v{info.Version}";
   }

   public async Task<Stream> OpenAsync(string invoiceNo, CancellationToken ct) =>
      (await store.OpenAsync("invoices", $"2026/{invoiceNo}.pdf", ct: ct)).Content;
}
```

| Method | Does |
| --- | --- |
| `PutAsync(bucket, key, content, mime, metadata)` | Stores a new current version (streamed; content is hashed while it is written). |
| `OpenAsync(bucket, key, version?)` | Opens the current or a given version; the stream is seekable. 404 for a delete marker. |
| `GetAsync(bucket, key)` | Current version info, also when it is a delete marker; `null` when the key has no history. |
| `DeleteAsync(bucket, key)` | Adds a delete marker. |
| `RestoreAsync(bucket, key, version)` | New version with the content of `version`. |
| `PurgeVersionAsync(bucket, key, version)` | Permanently deletes one version. |
| `ListAsync(bucket, prefix, delimiter, continuation, limit)` | Keys in order; with `delimiter` (`/`) keys are grouped into common prefixes. Use `NextContinuation` for the next page. |
| `VersionsAsync(bucket, key)` | Every version, newest first. |

Errors are `ActionException` with an HTTP status: 400 invalid name or key, 404 missing bucket/object/version,
409 quota exceeded or bucket read-only, 413 object too large, 503 not enough active volumes or no readable copy.

### IBinaryStorage adapter

`ObjectStoreBinaryStorage(IObjectStore store, string bucket)` implements `IBinaryStorage` on one bucket. It
keeps the no-overwrite rule (a key that already has any version is refused with 409) and `DeleteAsync` purges
every version. It is not registered by the engine and does not replace the binary storage of the approval
engine; create it yourself where a module wants it.

## Managed buckets

A **managed bucket** belongs to the application layer (a module) instead of an administrator. The module
declares it in the API builder; at startup object storage creates it, gives it its own policy and locks it.
The module then reads and writes it through `IObjectStore` with zero setup. Administrators keep the
infrastructure side and can look at and download the contents, but cannot change them.

### Declaring one

```csharp
public static void AddInvoiceModule(this EmAppBuilder builder)
{
   builder.AddManagedBucket("invoices", "Invoice",
      "PDF copies of issued invoices, written by the Invoice module when an invoice is posted. " +
      "Moving it to another volume does not move existing files; a smaller quota stops new invoices.",
      o => {
         o.KeepVersions = 10;
         o.MaxObjectBytes = 50L * 1024 * 1024;
      });
}
```

| Parameter | Rule |
| --- | --- |
| `name` | Bucket name (S3 rules, see [Concepts](#concepts)). Declaring the same name twice throws `InvalidOperationException`. |
| `owner` | Application or module shown as "Managed by &lt;owner&gt;", 1–100 characters. |
| `description` | Required, 10–500 characters, written for administrators: what the bucket holds, which module fills it and what changing its volumes or quotas affects. Shown in the manager and not editable there. |
| `configure` | Starting values of the bucket's policy (`ManagedBucketOptions`): `Access` (default `Private`), `MaxObjectBytes`, `KeepVersions`, `KeepDays`, `QuotaBytes`, `QuotaObjects`, `Note`. They follow the policy rules of the manager; an invalid value throws `ArgumentException` while the host is built. |

Declarations may come before or after `AddObjectStorage()` in `Program.cs`. When the host does not call
`AddObjectStorage()`, declarations are ignored without an error; a module that wants to run without object
storage handles a missing `IObjectStore` as before.

### What happens at startup

After the schema check, for every declaration, in one serializable transaction each:

1. **Bucket missing**: the engine makes sure the default volume exists, creates the policy
   `<bucket> (managed)` from the declared values on that volume (one replica), then the bucket.
2. **Ordinary bucket with that name already exists**: it is **adopted**. Its contents stay. When its policy is
   used by other buckets too, the policy is copied (every value and the ordered volume list) to
   `<bucket> (managed)` and the bucket moves to the copy, so locking it does not lock the other buckets.
3. **Already managed**: owner and description are made equal to the declaration (a changed owner is logged as
   information).
4. **Always**: when the policy's access differs from the declared `Access`, it is set back, with a warning in
   the log and an audit entry.

Every other policy value is used **only when the policy is created**. Changes an administrator makes later
(volumes, replicas, retention, quotas, maximum object size) are never overwritten. A failure stops startup with
a message that names the bucket.

The **default volume** is created once, only when a managed bucket needs a new policy: name
`managed-default` (with `-2`, `-3`, … when taken), folder `ObjectStorageOptions.DefaultVolumePath`. Its ID is
kept in `ta_Meta` (`ObjectStore.DefaultVolume`); when an administrator deletes it, a new one is created the
next time it is needed. Set the folder in the host:

```csharp
builder.AddObjectStorage(o => o.DefaultVolumePath = @"D:\EmData\objectstore");
```

Without it the folder is `data/objectstore`, relative to the API content root. When the folder cannot be used
(not writable, a drive root, …) startup fails with a message naming `DefaultVolumePath`.

### What is locked

| Who / what | Allowed | Refused (409) |
| --- | --- | --- |
| The module (`IObjectStore`) | Everything: put, delete, restore, purge | — |
| Background work | Retention, garbage collection, replication, repair | — |
| Administrator: bucket | State, note | Moving it to another policy, deleting it |
| Administrator: its policy | Name, state, note, volumes, replicas, retention, quotas, maximum object size | Changing access, deleting it, giving it to another bucket |
| Administrator: contents | Browse, object info, version list, download | Upload, delete, restore, permanent version delete |

The upload refusal happens before the request body is read. The public `/store` endpoint follows the policy's
access as for any bucket.

### When a declaration disappears

A managed bucket whose module no longer declares it (module removed, host turned it off) **stays locked** and
is shown as **NOT DECLARED** in the manager. An administrator can then **Release** it
(`PostMeta_StoreBucketRelease`, claim `Object Storage Settings Manage`): it becomes an ordinary bucket, its
policy an ordinary policy, its contents and description stay, and it can be changed or deleted afterwards.
Release refuses a bucket that is still declared or not managed (409).

### Audit

Group `System.ObjectStorage`: `bucket.provisioned` (created at startup), `bucket.adopted`,
`bucket.access.enforced` and `bucket.released`. Entries written at startup carry the actor type `system`.

### Upgrade notes

- **No schema change.** Owner and description are kept in `ta_StoreBucket.json_object` (`owner`,
  `description`), following the Dahlia Convention for columns added after a table is in use. Re-running
  [`views/vi_StoreBucket.sql`](../sqlscript/mssql/views/vi_StoreBucket.sql) adds `cvStoreBucketOwner` and
  `cvStoreBucketDescription` to the view; the engine itself does not read the view. `ObjectStoreSchemaVersion`
  stays `1`.
- **Contract additions in `Em.Libs`**: `StoreBucketInfo` gets `Owner`, `Description`, `IsDeclared` (plus the
  computed `IsManaged` and `IsUndeclared`), `StorePolicyInfo` gets `ManagedBucket` (plus `IsManaged`), and
  `IObjectStorageServices` gets `PostMeta_StoreBucketRelease`. The new record parameters are optional, so
  existing constructor calls still compile; another implementation of `IObjectStorageServices` must add the
  new action.
- **Builder**: `AddObjectStorage()` is unchanged; the overload `AddObjectStorage(Action<ObjectStorageOptions>)`
  and `AddManagedBucket(...)` are new.
- A module that used to require an administrator-made bucket and check it at runtime can declare it instead;
  an existing bucket of that name is adopted with its contents.

## Public read endpoint

`GET` and `HEAD /store/<bucket>/<key>` serve the current version of an object in a bucket whose policy is
`PublicRead`, without identity, outside the action dispatcher (no action timeout or rate limit), like `/cdn`.
Range requests, `ETag` (derived from object ID and version, never the internal hash), `If-None-Match` and
`Last-Modified` are supported. Responses carry `X-Content-Type-Options: nosniff` and a sandboxing
`Content-Security-Policy`, because the content is user-uploaded and served on the API origin. Anything else
— private buckets, deleted objects, invalid names, other methods — is a plain 404.

Keys are taken from the raw request path and percent-decoded once, so `a%20b/c.txt` reads key `a b/c.txt`.

## Management actions

`IObjectStorageServices` (module `Administrative Tools`):

| Area | Actions |
| --- | --- |
| Status | `GetMeta_StoreStatus` (counters, volumes with problems, missing copies, under-replicated and orphan blobs, maintenance tasks, last background pass) |
| Volumes | `GetMeta_StoreVolumes`, `PostGetMeta_StoreVolumeValidate`, `PostGetMeta_StoreVolumeSave`, `PostMeta_StoreVolumeDelete` (only without copies and outside every policy) |
| Policies | `GetMeta_StorePolicies`, `PostGetMeta_StorePolicySave`, `PostMeta_StorePolicyDelete` (only when no bucket uses it) |
| Buckets | `GetMeta_StoreBuckets`, `PostGetMeta_StoreBucketSave`, `PostMeta_StoreBucketDelete` (only when it holds no objects, including deleted ones, and is not managed), `PostMeta_StoreBucketRelease` (a managed bucket that is no longer declared) |
| Objects | `GetMeta_StoreObjects(bucketId, prefix, delimiter, continuation, limit)`, `GetMeta_StoreObject`, `PostGetMeta_StoreObjectUpload(request, content)` (streamed), `GetMeta_StoreObjectDownload(bucketId, key, version)` (streamed), `PostMeta_StoreObjectDelete` |
| Versions | `GetMeta_StoreObjectVersions`, `PostGetMeta_StoreObjectRestore`, `PostMeta_StoreObjectVersionPurge` |
| Maintenance | `GetMeta_StoreGcReview(graceHours)`, `PostGetMeta_StoreGcRun(graceHours)`, `PostGetMeta_StoreRetentionRun`, `PostGetMeta_StoreRepairRun`, `GetMeta_StoreTasks` |

For managed buckets the locks in [What is locked](#what-is-locked) apply. All GET actions take simple parameters
only. Maintenance runs start global business tasks (keys
`objectstorage:gc`, `objectstorage:retention`, `objectstorage:repair`), visible in the manager and in the
Business Task Manager.

## How it works

**Upload.** Name, key and size checks; an early quota check against the bucket counters. The content is
streamed to `.staging` on the first active volume of the policy while its SHA-256, CRC32 and size are
computed (the maximum object size is enforced during streaming). Under a per-hash lock: if the blob already
has enough ready copies on the policy's volumes the staged file is discarded (deduplication); otherwise copies
are written (other volumes first, verified by size and CRC32, then the staged file is moved into place
atomically). A new blob row starts as an orphan. Then one serializable transaction locks the bucket row and
the object row, allocates the next version number, inserts the version, sets it current, updates the bucket
counters, enforces the quota (409 and rollback when exceeded) and turns the blob ready. When the blob has
fewer copies than the replica target, background replication is signaled.

A crash at any point leaves at most a staging file (removed at startup) and an orphan blob (removed by
garbage collection after the grace period). No version ever points to a blob without a ready copy.

**Read.** The first ready copy on a non-offline volume, in the policy's volume order, whose file exists with
the expected size. A missing or wrong-sized file is marked `Missing` and replication is signaled; the next
copy is tried.

**Retention** (every six hours and on request): per bucket, non-current versions beyond *Keep old versions*
or older than *Keep … days* are purged; an object whose only remaining version is a delete marker is removed.

**Garbage collection** (on request, review first): blobs no version refers to are marked orphan; orphans that
are referenced again return to ready; orphans older than the grace period (default 24 hours, 1–720) are
deleted from every volume, then their rows. Each blob is deleted under its hash lock after checking again
that no version refers to it. A blob with a copy on an offline volume is skipped.

**Replication and repair.** In the background (signaled after uploads and at least hourly), blobs with fewer
ready copies than the highest replica target of the policies referring to them get new copies on those
policies' active volumes. **Repair** (on request) additionally checks every copy's file and size, marks lost
copies missing and replaces them. Copies interrupted while writing are treated as missing.

## Object Storage Manager (WPF)

Navigation `admin.objectstorage` (Administrative Tools, claim `Object Storage Manager Access`), registered by
the core like the other managers. MAUI has no screen.

- **Buckets**: bucket table (policy, objects, logical size, quota, access). Double-click opens the bucket in the
  bucket editor on a new tab (see below). **Browse** opens
  the object browser: breadcrumb path, pseudo-folders from `/`, upload (toolbar or drag-drop of files),
  download, delete, **Versions** side sheet (restore, download a version, delete a version permanently),
  **Load more** for long listings. **New bucket**/**Edit** open a side sheet (name, policy, state, note).
  **Open in tab** opens the selected bucket (at the current folder while browsing) in the bucket editor.
  A managed bucket shows a **MANAGED** chip (hover: owner and description) and, when it is no longer declared,
  **NOT DECLARED**; its side sheet shows the owner and description read-only above the note, the policy list is
  locked, **Delete** is disabled and **More › Release managed bucket** releases an undeclared one after
  confirmation. Its object browser and version sheet keep **Download** and **Versions** and disable upload
  (also drag-drop), delete, restore and permanent delete, with a line saying the contents are read-only.
  Policies of managed buckets are not offered for new or other buckets.
- **Volumes**: volume table with copies, size, missing copies and a live folder check; editor with **Check**.
- **Policies**: policy table; editor with replicas, retention, quotas (MB), access and the ordered volume list
  (check to include, arrows to reorder). A policy of a managed bucket shows a **MANAGED** chip; in its editor
  **Access** is disabled (hover help explains why) and every other field stays editable.
- **Maintenance**: status counters, garbage collection (grace period, **Review**, then **Run**), retention
  and repair runs, and the running task list.
- Home screen card: logical versus physical size and the number of volumes with problems, with its own
  refresh button.

### Bucket editor

Navigation `admin.objectstorage.bucket` (`EmApp.ObjectStorageBucketNavigationName`, editor kind, claim
`Object Storage Manager Access`) shows one bucket on its own tab: its settings (name, policy, state, note,
with hover help, and size/quota/access figures) above its object browser (path, upload and drag-drop,
download, delete, versions side sheet, **Load more**). It must be opened with a `StoreBucketNavigationPayload`,
so any screen can hand a bucket over by name and optionally a folder:

```csharp
// From a view model built through navigation (MvvmModelBase.NavigationEntry):
await NavigationEntry!.NavigateTo(EmApp.ObjectStorageBucketNavigationName,
   StoreBucketNavigationPayload.Open("invoices", "2026/"));

// A new bucket; its objects can be managed once it is saved.
await NavigationEntry!.NavigateTo(EmApp.ObjectStorageBucketNavigationName, StoreBucketNavigationPayload.New());
```

The tab title is `Bucket: <name>` (`New Bucket` until a new bucket is saved), so opening a bucket that is
already open switches to its tab. Saving settings and deleting versions permanently still need
`Object Storage Settings Manage` on the server. An unknown bucket name leaves the page empty with a message.
The editor uses the same view model as the manager (`ObjectStorageManagerVm` in bucket page mode). For a
managed bucket it shows the MANAGED chip, owner and description above the settings, locks the policy and
makes the objects read-only like the manager.

## Limits of version 1

Not included: the S3 protocol and presigned URLs, remote volume providers, per-bucket rights for users or
roles (managed buckets lock contents for every administrator alike), draining volumes and automatic
rebalancing, multiple API instances, encryption at rest, object lock,
lifecycle rules other than version retention, moving the CDN/registry/NuGet/approval storage into object
storage, and a MAUI screen. The engine uses SQL Server-specific locking hints; other database providers are
not supported for object storage.

## S3 readiness

Kept in version 1 so an S3 adapter will not change the core schema: all logic goes through `IObjectStore`
(an adapter only handles transport, SigV4, XML and error codes); bucket names and key limits follow S3;
keys are case-sensitive; versions carry their own MIME type and metadata; delete markers exist and restore
is a new version; listing uses prefix, delimiter and an opaque continuation token. Not yet present: an
external version ID (a stable encoding of object ID and version number), ETags (MD5 for single uploads and
the multipart rule, added with the adapter; the SHA-256 is never an ETag) and multipart upload tables.

## Maintainer notes

- Unit tests: `tests/Em.Api.Core.Tests/ObjectStorage/` (names, keys, CRC32, hash lock, local volumes,
  policy validation, retention rule, managed bucket declarations and their JSON). Integration tests against SQL
  Server: `tests/Em.Api.Core.IntegrationTests/ObjectStoreTests.cs` and `ManagedBucketTests.cs` (provisioning,
  adoption, access enforcement, the 409 locks, release; they create the schema from the real SQL scripts and
  skip without a reachable server). View-model tests: `tests/Em.Ui.Wpf.Core.Tests/ObjectStorageManagerTests.cs`.
- The test module `Em.Test` declares the managed bucket `emtest-files` (owner `Em Test`) as an example.
- Live smoke against a local Em.Api (starts the API itself on a local database, cleans its rows afterwards):
  `dotnet run --project ..\.artefacts\em-system\scripts\managed-bucket-smoke`. Render check of the screens,
  light and dark: `dotnet run --project ..\.artefacts\em-system\scripts\managed-bucket-render`.
- Listing uses the binary collation of `cStoreObjectKey` and the indexed computed column
  `cStoreObjectKeyPrefix` (first 400 characters). Listing performance with large buckets has not been
  measured yet.

The manager header provides the shared administrator-only runtime service switch. See [Runtime service availability](engine-storage-settings.md#runtime-service-availability) for default state, immediate effect, persistence and public 404 versus internal 503 behavior. Management remains available while the service is disabled.
