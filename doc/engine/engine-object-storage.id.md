# Object storage

[English](engine-object-storage.md)

Engine object storage generik di dalam Em API: **bucket** menampung **object** (key), setiap perubahan object
adalah sebuah **version**, setiap version menunjuk ke **blob** konten yang diidentifikasi dengan SHA-256-nya,
dan setiap blob punya satu atau lebih **copy** fisik di **volume**. Sebuah **policy** per bucket menentukan
penempatan dan replika, retensi version, kuota, serta akses publik.

Ini adalah layanan internal Em: modul sisi server memakai `IObjectStore`, administrator memakai
**Object Storage Manager** di klien WPF, dan bucket public-read dilayani di `/store`. Protokol S3 **tidak**
diimplementasikan; desainnya hanya menyisakan jalan untuk adapter S3 di kemudian hari (lihat
[Kesiapan S3](#kesiapan-s3)).

## Mengaktifkan

1. Jalankan di database core (SQL Server), setelah `tables/010-core.sql`:
   - [`doc/sqlscript/mssql/tables/060-objectstore.sql`](../sqlscript/mssql/tables/060-objectstore.sql)
   - lalu view [`views/vi_StoreBucket.sql`](../sqlscript/mssql/views/vi_StoreBucket.sql),
     [`views/vi_StoreObject.sql`](../sqlscript/mssql/views/vi_StoreObject.sql) dan
     [`views/vi_StoreVolume.sql`](../sqlscript/mssql/views/vi_StoreVolume.sql).

   Semua skrip aman dijalankan ulang. Tidak perlu migrasi data yang sudah ada: CDN, container registry,
   server NuGet, dan storage approval tidak dipindahkan ke object storage.
2. Di host API, panggil `builder.AddObjectStorage()`. Host contoh `Em.Api` melakukannya kecuali dimatikan
   lewat saklar modul `"modules": { "objectStorage": false }` (atau `EM_MODULE_OBJECTSTORAGE=false`).
3. Berikan claim di bawah ke administrator, buka **Object Storage Manager**, lalu buat minimal satu volume,
   satu policy, dan satu bucket. Bucket yang dideklarasikan modul sebagai [managed bucket](#managed-bucket)
   tidak memerlukan langkah ini: bucket itu dibuat saat startup di volume default.

Startup gagal dengan pesan yang jelas bila tabel atau baris versi skema (`ObjectStoreSchemaVersion` di
`ta_Meta`) tidak ada.

Volume, policy, dan bucket disimpan di database, bukan di `emapi-config.json`; perubahan langsung berlaku
saat disimpan, tanpa restart.

### Claim

Keduanya ada di modul `Administrative Tools`:

| Claim | Mengizinkan |
| --- | --- |
| `Object Storage Manager Access` | Membuka manager; melihat daftar bucket, object, version, volume, policy, status; upload, download, delete (delete marker), restore |
| `Object Storage Settings Manage` | Volume, policy, membuat/mengubah/menghapus bucket, menghapus version secara permanen, menjalankan garbage collection, retensi, dan repair |

### Hanya satu instance

Object storage mengasumsikan **satu instance API per database dan set volume**. Penguncian blob dan garbage
collection memakai lock di dalam proses. Saat startup dan setiap beberapa menit, instance memperbarui baris
lease `ObjectStore.Lease` di `ta_Meta`; bila instance lain memperbaruinya dalam sepuluh menit terakhir, sebuah
peringatan dicatat di log. Ini hanya peringatan, bukan jaminan.

## Konsep

| Item | Aturan |
| --- | --- |
| Nama bucket | Aturan S3: 3–63 karakter huruf kecil, angka, `-` dan `.`, diawali dan diakhiri huruf atau angka, tanpa `..`, bukan alamat IP, tanpa awalan `xn--` atau akhiran `-s3alias`. Tidak bisa diubah setelah dibuat. |
| Key object | UTF-8, peka huruf besar/kecil, 1–1024 byte, tanpa karakter kontrol, tanpa segmen `.` atau `..`. `/` hanya konvensi untuk listing per prefix; tidak ada folder sungguhan. Key tidak pernah menjadi path file. |
| Version | Selalu aktif. Upload ke key yang sudah ada menambah version current baru. Nomor version berasal dari penghitung per object yang tidak pernah mundur. |
| Delete | Menambah **delete marker** sebagai version current: object hilang dari listing tetapi riwayatnya tetap ada. Menghapus object yang sudah terhapus tidak mengubah apa pun. |
| Restore | Menambah version baru yang menunjuk ke konten version lama; tidak ada byte yang disalin. |
| Hapus permanen (purge) | Menghapus satu version. Bila version itu current, version tersisa terbaru yang bukan delete marker menjadi current; bila tidak ada version tersisa, object dihapus. |
| Blob | Konten yang diidentifikasi dengan SHA-256 dan dipakai bersama lintas bucket (deduplikasi global). CRC32 disimpan untuk pemeriksaan cepat copy. |
| MIME type dan metadata | Per version. Metadata: maksimal 32 entri, key berupa huruf, angka, `.`, `_`, `-` (maks. 128), nilai maksimal 1024 karakter. |

### Policy

| Field | Arti |
| --- | --- |
| Volumes (berurutan) | Volume tempat policy boleh menaruh blob; volume aktif pertama menerima upload lebih dulu. Jumlahnya minimal sebanyak target replika. |
| Minimum replicas | Jumlah copy yang ditulis sebelum upload dianggap berhasil (minimal 1). |
| Target replicas | Jumlah copy yang dituju replikasi di background (≥ minimum, maksimal 16). |
| Keep old versions | Jumlah version non-current yang disimpan per object; kosong berarti simpan semua, `0` berarti tidak ada yang disimpan. |
| Keep old versions for (days) | Umur maksimal version non-current; kosong berarti disimpan selamanya. |
| Quota / object quota | Per bucket: byte logis dari **setiap version yang disimpan** (bukan byte fisik setelah deduplikasi) dan jumlah object (object terhapus yang masih punya riwayat tetap dihitung). Bila terlampaui, hasilnya 409. |
| Max object size | Upload yang lebih besar dihentikan saat streaming dan dijawab 413. |
| Access | `Private`, atau `PublicRead` untuk melayani object di `/store/<bucket>/<key>`. |
| State | Policy `Disabled` tetap dipakai bucket yang sudah ada, tetapi tidak bisa diberikan ke bucket baru. |

Mengubah daftar volume sebuah policy tidak memindahkan data yang sudah ada. Upload baru mengikuti daftar
baru; blob lama mendapat copy yang kurang di volume baru lewat repair. Tidak ada rebalancing otomatis.

### Volume

Versi 1 punya satu jenis volume: **LocalFolder**, sebuah folder di server API (disk lokal atau share NAS yang
di-mount). Path-nya absolut atau relatif terhadap content root API dan diperiksa (bisa dijangkau, bisa
ditulis, ruang kosong) sebelum disimpan. Volume yang sudah menyimpan copy tidak bisa diganti path-nya. State:

- `Active`: dibaca dan ditulis.
- `ReadOnly`: hanya dibaca; copy di dalamnya tetap dihitung sebagai replika.
- `Offline`: dilewati untuk baca dan tulis; garbage collection menunggu volume ini.

Susunan di disk:

```text
<volume>/blobs/<hex[0..2]>/<hex[2..4]>/<sha256-hex>   konten blob, read-only setelah ditulis
<volume>/.staging/<ulid>                              upload yang sedang berjalan, dihapus saat startup
```

## Memakai dari modul

`IObjectStore` berlingkup scoped; ambil dari dependency injection di service atau request handler. Bucket
harus sudah ada, kecuali [managed bucket](#managed-bucket) yang dideklarasikan modul dan dibuat engine saat
startup. Modul yang memiliki file-nya sendiri sebaiknya mendeklarasikan managed bucket, bukan meminta
administrator membuat bucket lalu memeriksanya saat runtime.

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

| Method | Fungsi |
| --- | --- |
| `PutAsync(bucket, key, content, mime, metadata)` | Menyimpan version current baru (di-stream; konten di-hash sambil ditulis). |
| `OpenAsync(bucket, key, version?)` | Membuka version current atau version tertentu; stream-nya seekable. 404 untuk delete marker. |
| `GetAsync(bucket, key)` | Info version current, juga bila berupa delete marker; `null` bila key tidak punya riwayat. |
| `DeleteAsync(bucket, key)` | Menambah delete marker. |
| `RestoreAsync(bucket, key, version)` | Version baru dengan konten dari `version`. |
| `PurgeVersionAsync(bucket, key, version)` | Menghapus satu version secara permanen. |
| `ListAsync(bucket, prefix, delimiter, continuation, limit)` | Key berurutan; dengan `delimiter` (`/`) key dikelompokkan menjadi common prefix. Pakai `NextContinuation` untuk halaman berikutnya. |
| `VersionsAsync(bucket, key)` | Semua version, terbaru lebih dulu. |

Error berupa `ActionException` dengan status HTTP: 400 nama atau key tidak valid, 404 bucket/object/version
tidak ada, 409 kuota terlampaui atau bucket read-only, 413 object terlalu besar, 503 volume aktif tidak cukup
atau tidak ada copy yang bisa dibaca.

### Adapter IBinaryStorage

`ObjectStoreBinaryStorage(IObjectStore store, string bucket)` mengimplementasikan `IBinaryStorage` di atas satu
bucket. Adapter ini mempertahankan aturan tanpa-timpa (key yang sudah punya version apa pun ditolak dengan
409) dan `DeleteAsync` menghapus permanen semua version. Adapter ini tidak didaftarkan oleh engine dan tidak
menggantikan binary storage milik engine approval; buat sendiri di modul yang membutuhkannya.

## Managed bucket

**Managed bucket** adalah bucket milik layer aplikasi (modul), bukan milik administrator. Modul
mendeklarasikannya di builder API; saat startup object storage membuatnya, memberinya policy sendiri, dan
menguncinya. Modul lalu membaca dan menulis lewat `IObjectStore` tanpa setup apa pun. Administrator tetap
mengurus sisi infrastruktur dan boleh melihat serta mengunduh isinya, tetapi tidak bisa mengubahnya.

### Mendeklarasikan

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

| Parameter | Aturan |
| --- | --- |
| `name` | Nama bucket (aturan S3, lihat [Konsep](#konsep)). Mendeklarasikan nama yang sama dua kali melempar `InvalidOperationException`. |
| `owner` | Aplikasi atau modul yang tampil sebagai "Managed by &lt;owner&gt;", 1–100 karakter. |
| `description` | Wajib, 10–500 karakter, ditulis untuk administrator (bahasa Inggris, karena tampil di UI): isi bucket, modul pengisinya, dan dampak bila volume atau kuotanya diubah. Tampil di manager dan tidak bisa diedit di sana. |
| `configure` | Nilai awal policy bucket (`ManagedBucketOptions`): `Access` (bawaan `Private`), `MaxObjectBytes`, `KeepVersions`, `KeepDays`, `QuotaBytes`, `QuotaObjects`, `Note`. Mengikuti aturan policy di manager; nilai tidak valid melempar `ArgumentException` saat host dibangun. |

Deklarasi boleh sebelum atau sesudah `AddObjectStorage()` di `Program.cs`. Bila host tidak memanggil
`AddObjectStorage()`, deklarasi diabaikan tanpa error; modul yang ingin tetap berjalan tanpa object storage
menangani `IObjectStore` yang tidak tersedia seperti sebelumnya.

### Yang terjadi saat startup

Setelah pemeriksaan skema, untuk setiap deklarasi, masing-masing dalam satu transaksi serializable:

1. **Bucket belum ada**: engine memastikan volume default ada, membuat policy `<bucket> (managed)` dari nilai
   deklarasi di volume itu (satu replika), lalu bucket-nya.
2. **Bucket biasa bernama sama sudah ada**: bucket itu **diadopsi**, isinya tetap. Bila policy-nya juga dipakai
   bucket lain, policy disalin (semua nilai dan daftar volume berurutan) menjadi `<bucket> (managed)` dan
   bucket dipindah ke salinan itu, sehingga penguncian tidak ikut mengunci bucket lain.
3. **Sudah managed**: owner dan deskripsi disamakan dengan deklarasi (owner yang berubah dicatat di log level
   information).
4. **Selalu**: bila akses policy berbeda dari `Access` deklarasi, akses dikembalikan, dengan warning di log dan
   entri audit.

Nilai policy lainnya **hanya dipakai saat policy dibuat**. Perubahan administrator sesudahnya (volume, replika,
retensi, kuota, ukuran maksimum object) tidak pernah ditimpa. Kegagalan menghentikan startup dengan pesan yang
menyebut nama bucket.

**Volume default** dibuat sekali saja, hanya ketika ada managed bucket yang perlu policy baru: nama
`managed-default` (dengan `-2`, `-3`, … bila sudah dipakai), folder `ObjectStorageOptions.DefaultVolumePath`.
ID-nya disimpan di `ta_Meta` (`ObjectStore.DefaultVolume`); bila administrator menghapusnya, volume baru dibuat
saat dibutuhkan lagi. Atur folder di host:

```csharp
builder.AddObjectStorage(o => o.DefaultVolumePath = @"D:\EmData\objectstore");
```

Tanpa itu folder-nya `data/objectstore`, relatif terhadap content root API. Bila folder tidak bisa dipakai
(tidak bisa ditulis, root drive, …) startup gagal dengan pesan yang menyebut `DefaultVolumePath`.

### Yang dikunci

| Siapa / apa | Boleh | Ditolak (409) |
| --- | --- | --- |
| Modul (`IObjectStore`) | Semua: put, delete, restore, purge | — |
| Pekerjaan background | Retensi, garbage collection, replikasi, repair | — |
| Administrator: bucket | State, note | Memindah ke policy lain, menghapus |
| Administrator: policy-nya | Nama, state, note, volume, replika, retensi, kuota, ukuran maksimum object | Mengubah akses, menghapus, memakainya untuk bucket lain |
| Administrator: isi | Browse, info object, daftar version, download | Upload, delete, restore, hapus permanen version |

Penolakan upload terjadi sebelum body request dibaca. Endpoint publik `/store` mengikuti akses policy seperti
bucket lain.

### Bila deklarasi hilang

Managed bucket yang tidak lagi dideklarasikan modulnya (modul dicabut, host mematikannya) **tetap terkunci** dan
tampil sebagai **NOT DECLARED** di manager. Administrator lalu bisa **Release**
(`PostMeta_StoreBucketRelease`, claim `Object Storage Settings Manage`): bucket menjadi bucket biasa, policy-nya
menjadi policy biasa, isi dan deskripsinya tetap, dan sesudah itu bisa diubah atau dihapus. Release menolak
bucket yang masih dideklarasikan atau bukan managed (409).

### Audit

Grup `System.ObjectStorage`: `bucket.provisioned` (dibuat saat startup), `bucket.adopted`,
`bucket.access.enforced`, dan `bucket.released`. Entri yang ditulis saat startup berjenis actor `system`.

### Catatan upgrade

- **Tanpa perubahan skema.** Owner dan deskripsi disimpan di `ta_StoreBucket.json_object` (`owner`,
  `description`), sesuai Dahlia Convention untuk kolom tambahan pada tabel yang sudah dipakai. Menjalankan ulang
  [`views/vi_StoreBucket.sql`](../sqlscript/mssql/views/vi_StoreBucket.sql) menambah `cvStoreBucketOwner` dan
  `cvStoreBucketDescription` ke view; engine sendiri tidak membaca view itu. `ObjectStoreSchemaVersion` tetap
  `1`.
- **Tambahan kontrak di `Em.Libs`**: `StoreBucketInfo` mendapat `Owner`, `Description`, `IsDeclared` (plus
  `IsManaged` dan `IsUndeclared` terhitung), `StorePolicyInfo` mendapat `ManagedBucket` (plus `IsManaged`), dan
  `IObjectStorageServices` mendapat `PostMeta_StoreBucketRelease`. Parameter record baru bersifat opsional,
  jadi pemanggilan constructor lama tetap terkompilasi; implementasi lain dari `IObjectStorageServices` harus
  menambah action baru itu.
- **Builder**: `AddObjectStorage()` tidak berubah; overload `AddObjectStorage(Action<ObjectStorageOptions>)` dan
  `AddManagedBucket(...)` baru.
- Modul yang dulu meminta bucket buatan administrator lalu memeriksanya saat runtime dapat mendeklarasikannya;
  bucket bernama sama yang sudah ada diadopsi beserta isinya.

## Endpoint baca publik

`GET` dan `HEAD /store/<bucket>/<key>` melayani version current sebuah object di bucket yang policy-nya
`PublicRead`, tanpa identitas, di luar action dispatcher (tanpa action timeout atau rate limit), seperti
`/cdn`. Range request, `ETag` (diturunkan dari ID object dan version, tidak pernah dari hash internal),
`If-None-Match`, dan `Last-Modified` didukung. Respons membawa `X-Content-Type-Options: nosniff` dan
`Content-Security-Policy` yang bersifat sandbox, karena kontennya diunggah pengguna dan dilayani di origin API.
Selain itu — bucket private, object terhapus, nama tidak valid, method lain — hasilnya 404 biasa.

Key diambil dari path request mentah dan di-percent-decode satu kali, jadi `a%20b/c.txt` membaca key
`a b/c.txt`.

## Action manajemen

`IObjectStorageServices` (modul `Administrative Tools`):

| Area | Action |
| --- | --- |
| Status | `GetMeta_StoreStatus` (penghitung, volume bermasalah, copy yang hilang, blob yang kurang replika dan blob orphan, task pemeliharaan, proses background terakhir) |
| Volume | `GetMeta_StoreVolumes`, `PostGetMeta_StoreVolumeValidate`, `PostGetMeta_StoreVolumeSave`, `PostMeta_StoreVolumeDelete` (hanya bila tanpa copy dan tidak dipakai policy mana pun) |
| Policy | `GetMeta_StorePolicies`, `PostGetMeta_StorePolicySave`, `PostMeta_StorePolicyDelete` (hanya bila tidak dipakai bucket mana pun) |
| Bucket | `GetMeta_StoreBuckets`, `PostGetMeta_StoreBucketSave`, `PostMeta_StoreBucketDelete` (hanya bila tidak berisi object, termasuk yang sudah terhapus, dan bukan managed), `PostMeta_StoreBucketRelease` (managed bucket yang tidak lagi dideklarasikan) |
| Object | `GetMeta_StoreObjects(bucketId, prefix, delimiter, continuation, limit)`, `GetMeta_StoreObject`, `PostGetMeta_StoreObjectUpload(request, content)` (di-stream), `GetMeta_StoreObjectDownload(bucketId, key, version)` (di-stream), `PostMeta_StoreObjectDelete` |
| Version | `GetMeta_StoreObjectVersions`, `PostGetMeta_StoreObjectRestore`, `PostMeta_StoreObjectVersionPurge` |
| Pemeliharaan | `GetMeta_StoreGcReview(graceHours)`, `PostGetMeta_StoreGcRun(graceHours)`, `PostGetMeta_StoreRetentionRun`, `PostGetMeta_StoreRepairRun`, `GetMeta_StoreTasks` |

Untuk managed bucket berlaku penguncian di [Yang dikunci](#yang-dikunci). Semua action GET hanya menerima
parameter sederhana. Proses pemeliharaan memulai business task global (key
`objectstorage:gc`, `objectstorage:retention`, `objectstorage:repair`) yang terlihat di manager dan di
Business Task Manager.

## Cara kerja

**Upload.** Pemeriksaan nama, key, dan ukuran; pemeriksaan kuota awal terhadap penghitung bucket. Konten
di-stream ke `.staging` di volume aktif pertama milik policy sambil SHA-256, CRC32, dan ukurannya dihitung
(ukuran object maksimal ditegakkan selama streaming). Di bawah lock per hash: bila blob sudah punya cukup copy
siap di volume policy, file staging dibuang (deduplikasi); bila belum, copy ditulis (volume lain lebih dulu,
diverifikasi dengan ukuran dan CRC32, lalu file staging dipindahkan ke tempatnya secara atomik). Baris blob
baru dimulai sebagai orphan. Kemudian satu transaksi serializable mengunci baris bucket dan baris object,
mengalokasikan nomor version berikutnya, menyisipkan version, menjadikannya current, memperbarui penghitung
bucket, menegakkan kuota (409 dan rollback bila terlampaui), dan menjadikan blob ready. Bila blob punya copy
lebih sedikit dari target replika, replikasi background diberi sinyal.

Crash di titik mana pun paling banyak meninggalkan file staging (dihapus saat startup) dan blob orphan
(dihapus garbage collection setelah grace period). Tidak pernah ada version yang menunjuk ke blob tanpa copy
yang siap.

**Baca.** Copy siap pertama di volume yang tidak offline, sesuai urutan volume policy, yang file-nya ada
dengan ukuran yang diharapkan. File yang hilang atau ukurannya salah ditandai `Missing` dan replikasi diberi
sinyal; copy berikutnya dicoba.

**Retensi** (setiap enam jam dan saat diminta): per bucket, version non-current yang melebihi *Keep old
versions* atau lebih tua dari *Keep … days* dihapus permanen; object yang version tersisanya hanya sebuah
delete marker dihapus.

**Garbage collection** (saat diminta, review lebih dulu): blob yang tidak dirujuk version mana pun ditandai
orphan; orphan yang dirujuk lagi kembali menjadi ready; orphan yang lebih tua dari grace period (bawaan 24 jam,
1–720) dihapus dari setiap volume, lalu barisnya. Setiap blob dihapus di bawah lock hash-nya setelah
diperiksa lagi bahwa tidak ada version yang merujuknya. Blob yang punya copy di volume offline dilewati.

**Replikasi dan repair.** Di background (diberi sinyal setelah upload dan minimal setiap jam), blob dengan
copy siap lebih sedikit dari target replika tertinggi di antara policy yang merujuknya mendapat copy baru di
volume aktif policy tersebut. **Repair** (saat diminta) juga memeriksa file dan ukuran setiap copy, menandai
copy yang hilang sebagai missing, dan menggantinya. Copy yang terputus saat ditulis dianggap missing.

## Object Storage Manager (WPF)

Navigasi `admin.objectstorage` (Administrative Tools, claim `Object Storage Manager Access`), didaftarkan oleh
core seperti manager lainnya. MAUI tidak punya layar ini.

- **Buckets**: tabel bucket (policy, object, ukuran logis, kuota, akses). Double-click membuka bucket di bucket
  editor pada tab baru (lihat di bawah). **Browse** membuka
  object browser: path breadcrumb, pseudo-folder dari `/`, upload (toolbar atau drag-drop file), download,
  delete, side sheet **Versions** (restore, download sebuah version, hapus permanen sebuah version), dan
  **Load more** untuk listing panjang. **New bucket**/**Edit** membuka side sheet (nama, policy, state, note).
  **Open in tab** membuka bucket terpilih (di folder yang sedang dibuka bila sedang browse) di bucket editor.
  Managed bucket menampilkan chip **MANAGED** (hover: owner dan deskripsi) dan, bila tidak lagi dideklarasikan,
  **NOT DECLARED**; side sheet-nya menampilkan owner dan deskripsi read-only di atas note, daftar policy
  terkunci, **Delete** nonaktif, dan **More › Release managed bucket** melepas bucket yang tidak dideklarasikan
  setelah konfirmasi. Object browser dan side sheet version-nya tetap punya **Download** dan **Versions**, sedangkan
  upload (termasuk drag-drop), delete, restore, dan hapus permanen nonaktif, dengan baris keterangan bahwa isinya
  read-only. Policy milik managed bucket tidak ditawarkan untuk bucket baru atau bucket lain.
- **Volumes**: tabel volume dengan copy, ukuran, copy yang hilang, dan pemeriksaan folder langsung; editor
  dengan tombol **Check**.
- **Policies**: tabel policy; editor dengan replika, retensi, kuota (MB), akses, dan daftar volume berurutan
  (centang untuk menyertakan, panah untuk mengubah urutan). Policy milik managed bucket menampilkan chip
  **MANAGED**; di editornya **Access** nonaktif (help saat hover menjelaskan alasannya) dan field lain tetap bisa
  diubah.
- **Maintenance**: penghitung status, garbage collection (grace period, **Review**, lalu **Run**), menjalankan
  retensi dan repair, serta daftar task yang sedang berjalan.
- Card di layar home: ukuran logis dibanding ukuran fisik dan jumlah volume bermasalah, dengan tombol refresh
  sendiri.

### Bucket editor

Navigasi `admin.objectstorage.bucket` (`EmApp.ObjectStorageBucketNavigationName`, jenis editor, claim
`Object Storage Manager Access`) menampilkan satu bucket di tab sendiri: setting-nya (name, policy, state, note,
dengan help saat hover, serta angka ukuran/kuota/akses) di atas object browser-nya (path, upload dan drag-drop,
download, delete, side sheet versions, **Load more**). Navigasi ini wajib dibuka dengan
`StoreBucketNavigationPayload`, sehingga layar mana pun bisa melempar sebuah bucket berdasarkan nama dan
opsional folder:

```csharp
// Dari view model yang dibangun lewat navigasi (MvvmModelBase.NavigationEntry):
await NavigationEntry!.NavigateTo(EmApp.ObjectStorageBucketNavigationName,
   StoreBucketNavigationPayload.Open("invoices", "2026/"));

// Bucket baru; object-nya bisa dikelola setelah bucket disimpan.
await NavigationEntry!.NavigateTo(EmApp.ObjectStorageBucketNavigationName, StoreBucketNavigationPayload.New());
```

Judul tab adalah `Bucket: <nama>` (`New Bucket` sampai bucket baru disimpan), jadi membuka bucket yang sudah
terbuka akan pindah ke tab-nya. Menyimpan setting dan menghapus version secara permanen tetap butuh
`Object Storage Settings Manage` di server. Nama bucket yang tidak ada membuat halaman kosong dengan pesan.
Editor memakai view model yang sama dengan manager (`ObjectStorageManagerVm` dalam mode bucket page). Untuk
managed bucket, editor menampilkan chip MANAGED, owner, dan deskripsi di atas pengaturan, mengunci policy, dan
membuat object read-only seperti di manager.

## Batasan versi 1

Belum termasuk: protokol S3 dan presigned URL, provider volume remote, hak per bucket untuk user atau role
(managed bucket mengunci isi untuk semua administrator secara sama),
pengosongan (drain) volume dan rebalancing otomatis, banyak instance API, enkripsi at rest, object lock,
lifecycle rule selain retensi version, pemindahan storage CDN/registry/NuGet/approval ke object storage, dan
layar MAUI. Engine memakai locking hint khusus SQL Server; provider database lain tidak didukung untuk object
storage.

## Kesiapan S3

Dipertahankan di versi 1 agar adapter S3 tidak mengubah skema inti: semua logika lewat `IObjectStore` (adapter
hanya menangani transport, SigV4, XML, dan kode error); nama bucket dan batas key mengikuti S3; key peka huruf
besar/kecil; version membawa MIME type dan metadata sendiri; delete marker ada dan restore adalah version
baru; listing memakai prefix, delimiter, dan continuation token yang opaque. Belum ada: ID version eksternal
(encoding stabil dari ID object dan nomor version), ETag (MD5 untuk upload tunggal dan aturan multipart,
ditambahkan bersama adapter; SHA-256 tidak pernah menjadi ETag), dan tabel multipart upload.

## Catatan maintainer

- Unit test: `tests/Em.Api.Core.Tests/ObjectStorage/` (nama, key, CRC32, hash lock, volume lokal, validasi
  policy, aturan retensi, deklarasi managed bucket dan JSON-nya). Test integrasi terhadap SQL Server:
  `tests/Em.Api.Core.IntegrationTests/ObjectStoreTests.cs` dan `ManagedBucketTests.cs` (provisioning, adopsi,
  penegakan akses, penguncian 409, release; membuat skema dari skrip SQL asli dan dilewati bila server tidak
  bisa dijangkau). Test view model: `tests/Em.Ui.Wpf.Core.Tests/ObjectStorageManagerTests.cs`.
- Module uji `Em.Test` mendeklarasikan managed bucket `emtest-files` (owner `Em Test`) sebagai contoh.
- Smoke langsung terhadap Em.Api lokal (harness menjalankan API sendiri di database lokal dan membersihkan
  barisnya): `dotnet run --project ..\.artefacts\em-system\scripts\managed-bucket-smoke`. Pemeriksaan render
  layar terang dan gelap: `dotnet run --project ..\.artefacts\em-system\scripts\managed-bucket-render`.
- Listing memakai collation biner `cStoreObjectKey` dan kolom computed berindeks `cStoreObjectKeyPrefix`
  (400 karakter pertama). Performa listing pada bucket besar belum diukur.

## Runtime service availability

The manager header provides the shared administrator-only **Service enabled / Service disabled** switch.
Changes apply immediately without restart. Manager-claim holders can read status and keep using management
actions while the service is disabled. Public `/store` requests return 404; internal `IObjectStore` and
`ObjectStoreBinaryStorage` calls throw `ServiceDisabledException` (503, `Object Storage is disabled`).
The value is stored in existing `ta_Meta`, key `ObjectStore.ServiceEnabled`; an absent or empty key means enabled.
Host feature disablement hides manager navigation/home cards and makes its actions return 404.
See [Runtime service availability](engine-storage-settings.md#runtime-service-availability).
