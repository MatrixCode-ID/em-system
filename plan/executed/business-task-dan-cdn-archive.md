# Plan — Business task engine, Business Task Manager, hub task personal, dan archive di CDN Manager

Status: **sudah dieksekusi — 2026-09-27** (tiga sesi; catatan eksekusi di akhir berkas)
Dibuat: 2026-09-27 — dari permintaan "tombol create archive dan extract archive di CDN Manager,
dengan server yang bisa menjalankan background task yang dimonitor UI dan bisa di-cancel".
Direvisi: 2026-09-27 — hasil review user (nama Business Task, cakupan Personal/Global, hub task
personal, status task global lewat service module, tombol archive beranimasi).

---

## Kenapa ada berkas ini

Membuat atau membongkar zip di CDN bisa memakan waktu jauh lebih lama dari batas request, dan
hasilnya tidak boleh bergantung pada aplikasi client yang tetap terbuka. Server belum punya cara
menjalankan pekerjaan di luar request: setiap action hidup dan mati bersama request-nya
(`ServicesBase.AbortToken`, `HttpRequestTimeout`). Pernyataan "server punya fitur background task
yang bisa dimonitor UI dan bisa di-cancel" **belum ada** di repo, jadi plan ini membangunnya
sebagai fitur engine. CDN archive menjadi pemakai pertamanya, dan module nantinya memakai jalur
yang sama, misalnya untuk "load data invoice setahun" atau "hitung lalu kirim email".

Satu jebakan teknis yang menentukan bentuk engine-nya: `EmApp.ServiceProvider` dan
`EmApp.CurrentRequest` dibaca dari `IHttpContextAccessor`, yang berbasis `AsyncLocal`. Task yang
dimulai dengan `Task.Run` biasa dari dalam action **ikut membawa `HttpContext` request itu**,
bahkan setelah request-nya selesai dan konteksnya didaur ulang. Karena itu business task wajib
dimulai dengan `ExecutionContext.SuppressFlow()`, memakai scope DI sendiri, dan membawa identitas
pemulainya secara eksplisit (langkah 3).

## Keputusan final

Semua keputusan di bawah sudah diambil bersama user. Saat eksekusi tidak ada lagi pertanyaan ke
user. Hal yang tidak tercakup diputuskan menurut pilihan yang paling konsisten dengan tabel ini,
lalu dicatat di laporan eksekusi.

**Nama fitur: Business Task** (BTM = Business Task Manager), bukan "background task". Istilah
background task rancu dengan pekerjaan async di sisi UI dan dengan `BackgroundService`/`IHostedService`
ASP.NET. Semua identifier, claim, kunci metadata, dan judul layar memakai `BusinessTask`. Kutipan
permintaan awal di atas sengaja dibiarkan apa adanya.

### Engine (server)

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Dua jenis output | **Jenis 1, tanpa output** (contoh: archive CDN, kirim email). **Jenis 2, dengan output**: hasilnya berupa **JSON** atau **file binary** (contoh: data invoice sebagai JSON atau Excel). |
| 2 | Cakupan task | Ditentukan penulis action saat memulai task. **Personal** (contoh: load data invoice): milik pemulainya, tampil di **hub task personal** di main window. **Global** (contoh: archive CDN): milik layar module yang memintanya, tampil di **layar itu** (CDN Manager). Keduanya juga tampil di **BTM**. |
| 3 | Pembuat task | Hanya di **backend**: method `protected` `StartBusinessTask(...)` di `ServicesBase`. Tidak ada action "create" di kontrak bersama; client memulai task lewat action module (mis. `PostGetMeta_CdnArchive`). |
| 4 | Penyimpanan | **Tidak memakai database.** Task yang hidup dicatat di registry in-memory (singleton). Hasil jenis 2 dan catatannya disimpan di **folder cache lokal** di server. |
| 5 | Masa tampil | Jenis 1 yang **sukses**: tampil selama grace time lalu hilang. **Gagal/exception** (jenis apa pun): tampil terus sampai di-clear. Jenis 2 yang sukses: tampil terus sampai di-clear. |
| 6 | Server restart | Entri yang gagal, termasuk yang terputus karena restart, dibersihkan dari cache. Hasil jenis 2 yang **sukses** dipertahankan sampai dihapus. |
| 7 | Grace time | `EmAppBuilder.BusinessTaskGracePeriod`, default **5 menit**. |
| 8 | Pemantauan UI | **Polling** GET. Tidak ada SignalR/WebSocket. |
| 9 | Batas konkurensi | Diatur dari BTM dan disimpan di **`ta_Meta`**, berisi mode dan **satu angka**. Mode **Global** = maksimal N task berjalan di seluruh server; mode **By User** = maksimal N task berjalan per user (task global dihitung ke pemulainya). Kelebihannya menunggu dengan status `Queued`. Nilai awal (sebelum ada di `ta_Meta`) diambil dari `EmAppBuilder`. |
| 10 | Kunci task | Setiap task membawa **kunci**. Kunci task **global** unik di **seluruh server**; kunci task **personal** unik **per pemilik** (user 1 dan user 2 boleh sama-sama menjalankan "load invoice 2025"). Memulai kunci yang masih hidup (Queued/Running) ditolak **409**, dengan pesan yang menyebut nama pemulai task yang sedang hidup. |
| 11 | Hak atas task personal | Cancel, clear, dan ambil hasil: **pemilik** (dari hub) dan **admin** (dari BTM). |
| 12 | Hak atas task global | Ditentukan **module pemiliknya**: siapa pun yang lolos claim action module itu boleh melihat, cancel, dan clear (contoh: semua pemegang claim CDN, admin maupun bukan). Admin juga bisa dari BTM. |
| 13 | Akses BTM | Wajib claim **`Administrative Tools:Business Task Manager Access`**; tanpa claim, layar BTM tidak bisa dibuka. BTM menampilkan **semua** task, personal dan global, **beserta nama pemiliknya**. Pemegang claim yang bukan admin **hanya melihat**. Cancel, clear, dan ubah batas dari BTM hanya admin. |
| 14 | Kontrak `IBusinessTaskServices` | Hanya untuk **memeriksa data (BTM)** dan **mengambil task personal user** (hub), plus cancel/clear/hasil/batas berdasarkan id. **Tidak ada** pencarian berdasarkan kunci/prefiks dari client (`GetMeta_BusinessTasksByKey` dihapus): status task global ditanyakan lewat service module-nya, dan kuncinya tidak pernah keluar ke client sebagai alat cari. |
| 15 | Email | **Di luar cakupan.** Dicatat sebagai item TODO baru. |

### UI

| # | Topik | Keputusan |
| --- | --- | --- |
| 16 | Cakupan UI plan ini | Layar **BTM**, **hub task personal** di main window, dan tombol **Create Archive** / ikon **Extract** di CDN Manager. |
| 17 | Hub task personal | Ikon di title bar `TabbedMainWindow` (dipakai layout Tabbed maupun SPA), **di kiri tombol Color Theme** (ikon half-stroke), di kanan tombol Tools. Klik → popup daftar task personal milik user. Kosong → teks **"No Running Task Available"**. Klik satu task → **dialog status**: saat berjalan progress + **Cancel**; saat sukses **Download** (hasil file) atau **Open** (hasil JSON); saat gagal info error + **Clear**. |
| 18 | Open hasil JSON | **Stub** dulu: command dan tombolnya ada, isinya memberi tahu bahwa fitur belum tersedia. Kebutuhannya belum terlihat; dikembangkan nanti. Field `NavigationName` tetap disiapkan di data. |
| 19 | Sinyal ikon hub | Dipilih yang cocok dan rapi (lihat langkah 7): badge jumlah task hidup, ikon beranimasi selama ada yang berjalan, titik penanda kalau ada task yang selesai dan belum dilihat. **Tanpa toast.** |
| 20 | Polling hub | **2 detik** selama ada task personal yang hidup, **±30 detik** selama tidak ada, dan refresh saat popup dibuka. |
| 21 | Status task global di layar module | Module menyediakan action sendiri untuk menanyakan status task-nya (CDN: `GetMeta_CdnArchiveTask()`, `GetMeta_CdnExtractTasks(folder)`). Tidak ada control generik `BusinessTaskButton`: tampilannya urusan layar module. |
| 22 | MAUI | **Stub saja.** `BusinessTaskService` di `Em.Ui.Maui.Core` dibuat dan didaftarkan supaya struktur kedua core tetap sejajar, tetapi setiap method-nya hanya melempar `NotImplementedException` (tidak memanggil API). `BusinessTaskTracker` (di `Em.Ui.Core`) **tidak didaftarkan dan tidak dijalankan** di MAUI. Client service yang sebenarnya, tracker, hub, dialog status, dan layar BTM di MAUI menyusul lewat **plan terpisah** (dicatat sebagai TODO). CDN Manager memang hanya ada di WPF. Selisih ini disebut di laporan eksekusi. |

### CDN archive

| # | Topik | Keputusan |
| --- | --- | --- |
| 23 | Format | Hanya **`.zip`** (`System.IO.Compression`). |
| 24 | Sumber archive | **Item terpilih** (multi-select): `SelectionMode` CDN Manager menjadi `Extended`. Hanya Create Archive yang memakai semua item terpilih. Tombol per baris dan perintah lain tetap bekerja pada satu item. Nama zip ditanyakan lewat dialog. |
| 25 | Satu archive saja | Hanya **satu** task archive yang boleh hidup di seluruh server (task global berkunci tetap). |
| 26 | Tombol Create Archive | Di toolbar, **di kanan "Open in Browser"**. Saat archive berjalan (milik siapa pun), tombol berubah jadi **animasi + progress**, dan kliknya hanya bisa **Cancel**. Admin/pemegang claim lain yang membuka CDN Manager melihat tombol yang sama selama task belum selesai. Saat gagal: tombol menampilkan status error, klik → **pesan error lalu Clear**. |
| 27 | Extract | **Ikon di baris** file `.zip` (aksi per baris). Animasi selama extract berjalan juga di baris itu, dan kliknya hanya bisa Cancel; gagal → pesan error lalu Clear. Isi zip ditaruh di folder tempat zip itu berada ("Extract Here"). Kalau ada nama yang bentrok, user ditanya timpa atau batal. |
| 28 | Keamanan extract | **Ketat**: zip-slip, nama berawalan titik, atau nama yang tidak sah menggagalkan **seluruh** extract. Setiap entri dibatasi batas ukuran unggahan CDN, dan total hasil extract juga dibatasi. |
| 29 | Hak CDN | Pemegang claim `CDN Manager Access` (admin maupun bukan) boleh melakukan semuanya, termasuk **cancel archive/extract** milik user lain. |
| 30 | Commit | **Satu commit di akhir**, pesan berbahasa Indonesia, diakhiri `Co-Authored-By`. |

### Keputusan turunan (diambil saat menyusun plan)

- **Nama enum status.** Siklus hidup task memakai `BusinessTaskStatus` (`Queued = 0, Running = 1,
  Succeeded = 2, Failed = 3, Canceled = 4`). Namanya sengaja bukan `*State`: konvensi `*State`
  (negatif = tidak dipakai/dihapus) mengatur aktif/nonaktif, sedangkan ini tahap proses. Task yang
  terputus karena restart tidak punya status sendiri, karena ia dibuang saat startup (keputusan 6).
- **Enum cakupan** bernama `BusinessTaskScope` (`Personal = 0, Global = 1`). Holder DI internal
  yang membawa identitas pemulai ke dalam task karena itu bernama `BusinessTaskStarter`.
- **Task yang dibatalkan** (jenis apa pun) diperlakukan seperti sukses jenis 1: tampil selama grace
  time lalu hilang. Pembatalan adalah keinginan user, bukan kegagalan yang perlu diselidiki. Output
  parsial jenis 2 yang dibatalkan dibuang.
- **Kunci tidak peka huruf besar/kecil** (`StringComparer.OrdinalIgnoreCase`). Registry kunci hidup
  memakai kunci gabungan: `G|{key}` untuk global, `P|{ownerUserId}|{key}` untuk personal. Entri yang
  sudah selesai tidak menahan kuncinya: kunci yang sama boleh dimulai lagi walaupun hasil lamanya
  masih tersimpan.
- **Flag per pemanggil.** `BusinessTaskInfo` membawa `CanCancel`, `CanClear`, dan `CanReadResult`,
  jadi UI tidak menebak hak. Lewat `IBusinessTaskServices` flag dihitung dari aturan pemilik/admin
  (keputusan 11, 13). Lewat pembantu `FindBusinessTask*` yang dipanggil action module, `CanCancel`
  dan `CanClear` bernilai `true`, karena pemanggil sudah lolos claim module (keputusan 12);
  `CanReadResult` tetap pemilik/admin.
- **Cancel/clear task global** dilakukan lewat action module (CDN: `PostMeta_CdnArchiveCancel`, dst.)
  yang memanggil pembantu `CancelBusinessTask`/`ClearBusinessTask` berbasis kunci di `ServicesBase`.
  Pembantu itu tidak memeriksa kepemilikan; penjaganya claim action module.
- **Hasil file diunduh lewat action yang mengembalikan `Stream`.** Dispatcher saat ini hanya bisa
  menjawab JSON, jadi plan ini menambahkan dukungan return `Task<Stream>` (langkah 2). Ini
  pasangan dari parameter `Stream` yang sudah ada.
- **Folder cache**: `EmAppBuilder.BusinessTaskCachePath`, default `./data/tasks` (sudah
  tercakup `.gitignore` lewat `src/backend/Em.Api/data/`). Satu subfolder per task:
  `<taskId>/task.json` + `result.json` atau `result.bin`. `task.json` hanya ditulis saat task
  jenis 2 **sukses**, sehingga saat startup cukup membuang setiap subfolder yang tidak punya
  `task.json` berstatus `Succeeded`.
- **Shutdown server** (`IHostApplicationLifetime.ApplicationStopping`) membatalkan token semua
  task yang hidup. Karena tidak ada catatan gagal yang dipertahankan saat restart, task itu
  otomatis hilang.
- **Kunci task CDN**: archive memakai satu kunci tetap `cdn:archive` (keputusan 25). Extract memakai
  `cdn:extract:{path zip}`, jadi beberapa zip boleh diekstrak bersamaan (tetap tunduk pada batas
  konkurensi), tetapi zip yang sama tidak dua kali.
- **Archive CDN**: default nama untuk 1 item adalah `<nama item>.zip`, untuk banyak item
  `<nama folder saat ini>.zip` (di akar: `archive.zip`), dan nama bisa diubah lewat
  `TextInputDialog`. Kalau nama sudah ada, user ditanya timpa lewat `EmMessageBox`. Isi ditulis ke
  file sementara berawalan titik lalu dipindah saat sukses; kalau batal atau gagal, file sementara
  dihapus. Entri berawalan titik di dalam folder sumber tidak ikut, sama seperti `Tree`. Ukuran zip
  hasilnya tidak dibatasi `CdnMaxFileSize`, karena file itu dibuat server, bukan diunggah.
- **Extract Here**: sebelum task dimulai, client memanggil `GetMeta_CdnExtractConflicts`. Action
  itu sekaligus memvalidasi zip dengan aturan ketat (400 lebih awal) dan mengembalikan path yang
  akan tertimpa. Kalau ada, `EmMessageBox` menanyakan "Overwrite N existing file(s)?". Jawaban
  Ya memulai task dengan `overwrite: true`, Tidak membatalkan. Bentrok file-lawan-folder selalu
  gagal (400), dengan atau tanpa overwrite. Extract ditulis ke folder sementara berawalan titik di
  folder yang sama; setelah seluruh isi lolos batas dan tertulis, entri dipindah satu per satu ke
  tempatnya. Tahap pindah itu singkat dan **tidak** mengamati token, sehingga pembatalan hanya
  berlaku sebelum tahap pindah dan tidak meninggalkan sisa.
- **Batas extract**: per entri ≤ `CdnMaxFileSize`, total ≤ **10 × `CdnMaxFileSize`**, jumlah entri
  ≤ **10.000**. Batas ditegakkan dengan menghitung byte yang benar-benar ditulis, bukan dari header
  zip. Konstanta ditaruh di `CdnStore`.
- **Polling CDN Manager**: selama layar terbuka, 2 detik selama ada task archive/extract yang hidup,
  ±10 detik selama tidak ada, supaya tombol pengguna lain ikut berubah saat seseorang memulai
  archive. Klik yang kalah cepat dijawab 409 dengan nama pemulainya.
- **Menu konteks CDN Manager tidak mendapat entri archive/extract.** Tombol toolbar dan ikon baris
  sekaligus menjadi penampil status; entri menu yang tidak ikut berubah bentuk akan membingungkan.
- **Task "belum dilihat" di hub** dicatat di memori client (per sesi): task yang selesai setelah
  popup terakhir dibuka. Membuka popup menghapus titik penanda.

## Bentuk akhirnya, singkat

```
Client ──PostGetMeta_CdnArchive──► CdnServices ──StartBusinessTask(Global)──► BusinessTaskRunner
   ◄──────── BusinessTaskInfo ─────────┘                                        │ antrian + batas (ta_Meta)
Client ──GetMeta_CdnArchiveTask / PostMeta_CdnArchiveCancel──► CdnServices ──FindBusinessTask/Cancel…─┘

Module ──StartBusinessTask(Personal)──► runner          Hub (BusinessTaskTracker, polling)
Hub    ──GetMeta_UserBusinessTasks / PostMeta_BusinessTaskCancel(id) / …Result(id)──► IBusinessTaskServices
BTM    ──GetMeta_BusinessTasks / …Limit──► IBusinessTaskServices

Hasil jenis 2 ──► ./data/tasks/<id>/{task.json,result.*}
```

---

## Yang dikerjakan

Urutan langkah mengikuti ketergantungan. Setiap XML doc untuk member `public` di `src/shared`
ditulis dalam Bahasa Indonesia, tanpa menyebut nama objek database (`ta_Meta` cukup disebut
"metadata server"). Identifier, pesan exception, log, dan komentar inline ditulis dalam bahasa
Inggris.

### 1. Model dan kontrak bersama — `src/shared/Em.Libs/Api.Core.Models/`

Namespace `Em.Api.Core.Models` (milik engine, lihat CLAUDE.md "Where a model belongs").

- `BusinessTaskStatus.cs`: enum di atas.
- `BusinessTaskScope.cs`: `Personal = 0, Global = 1`.
- `BusinessTaskOutputKind.cs`: `None = 0, Json = 1, File = 2`.
- `BusinessTaskLimitMode.cs`: `Global = 0, PerUser = 1`.
- `BusinessTaskLimit.cs`: DTO `{ BusinessTaskLimitMode Mode; int Limit; }`, dengan `Limit ≥ 1`.
- `BusinessTaskInfo.cs`: potret satu task untuk client, berisi
  `Id` (ULID string), `Key`, `Scope`, `Title`, `ModuleName`, `OwnerUserId`, `OwnerName`,
  `OutputKind`, `Status`, `Percent` (`double?`, `null` = tak tentu), `Caption`, `QueuedAt`,
  `StartedAt?`, `FinishedAt?`, `ErrorMessage?`, `NavigationName?`, `ResultFileName?`,
  `ResultContentType?`, `ResultSize?`, dan flag per pemanggil `CanCancel`, `CanClear`,
  `CanReadResult`.
- `IBusinessTaskServices.cs : IServices`: const `ManagerClaim = "Business Task Manager Access"`.
  Semua action berada di region `Meta's`, dan **setiap action wajib diberi atribut** di
  implementasinya:
  - `Task<BusinessTaskInfo[]> GetMeta_BusinessTasks()`: semua task (personal dan global), termasuk
    yang sudah selesai dan masih tersimpan. Claim `ManagerClaim`.
  - `Task<BusinessTaskInfo[]> GetMeta_UserBusinessTasks()`: task **personal** milik pemanggil,
    hidup maupun selesai. Cukup login.
  - `Task<BusinessTaskInfo?> GetMeta_BusinessTask(string id)`: untuk dialog status hub dan BTM.
    Terlihat oleh pemilik, admin, dan pemegang `ManagerClaim`; selain itu 404.
  - `Task PostMeta_BusinessTaskCancel(string id)`: pemilik atau admin (403 kalau bukan). Task
    yang sudah selesai: 409.
  - `Task PostMeta_BusinessTaskClear(string id)`: pemilik atau admin. Hanya untuk task yang sudah
    selesai (409 kalau masih hidup). Juga menghapus subfolder cache-nya.
  - `Task<string> GetMeta_BusinessTaskJsonResult(string id)`: teks JSON hasil, pemilik atau admin.
    400 kalau `OutputKind` bukan `Json`.
  - `Task<Stream> GetMeta_BusinessTaskFileResult(string id)`: isi file hasil, pemilik atau admin.
    400 kalau `OutputKind` bukan `File`.
  - `Task<BusinessTaskLimit> GetMeta_BusinessTaskLimit()`: claim `ManagerClaim`.
  - `Task PostMeta_BusinessTaskLimit(BusinessTaskLimit limit)`: admin saja.
- `ICdnServices` (berkas yang ada) mendapat action baru di region `Meta's`, semuanya ber-claim
  `CdnClaim`:
  - `Task<BusinessTaskInfo> PostGetMeta_CdnArchive(CdnArchiveRequest request)`
  - `Task<BusinessTaskInfo?> GetMeta_CdnArchiveTask()`: task archive yang hidup, atau yang gagal
    dan belum di-clear; `null` kalau tidak ada.
  - `Task PostMeta_CdnArchiveCancel()` dan `Task PostMeta_CdnArchiveClear()`
  - `Task<string[]> GetMeta_CdnExtractConflicts(string path)`
  - `Task<BusinessTaskInfo> PostGetMeta_CdnExtract(string path, bool overwrite)`
  - `Task<BusinessTaskInfo[]> GetMeta_CdnExtractTasks(string? folder)`: task extract untuk zip di
    folder itu (hidup, atau gagal dan belum di-clear).
  - `Task PostMeta_CdnExtractCancel(string path)` dan `Task PostMeta_CdnExtractClear(string path)`
- `CdnArchiveRequest.cs`: `{ string? Folder; string[] Names; string ArchiveName; bool Overwrite; }`.

### 2. Dispatcher: action yang mengembalikan `Stream` — `Em.Api.Core/Api/Core/EmApp.cs`

- Registrasi action (sekitar `EmApp.cs:944`, tempat `ReturnType` dibaca): `Task<Stream>` diterima
  sebagai bentuk return yang sah. Tandai di `ActionDefinition` (mis. `ReturnsStream`).
- Saat menjawab (sekitar `EmApp.cs:499`): kalau action mengembalikan stream, jawab dengan
  `Results.Stream(stream, "application/octet-stream")`, yang menutup stream setelah ditulis. Jangan
  lewat `ToJsonResult`.
- Anggaran `HttpRequestTimeout` hanya berlaku sampai action mengembalikan stream-nya. Penyalinan ke
  respons hanya mengamati `RequestAborted`. Tulis ini di komentar.
- Client `src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs` + `IApiClient`: tambah
  `Task<Stream> GetStreamAsync(string controller, string action, params object[] args)`, yang
  memakai `StreamHttpClient` (tanpa timeout), `HttpCompletionOption.ResponseHeadersRead`, dan
  mengembalikan stream yang ikut menutup `HttpResponseMessage`-nya. Penanganan 401/refresh token
  dan status gagal harus sama dengan `SendAsync`. `ServiceUiBase` mendapat `protected GetStreamAsync(...)`
  yang sejajar `GetAsync`.

### 3. Engine business task — `src/backend/Em.Api.Core/Api/Core/`

**`EmAppBuilder`** (`Api/Shared/EmAppBuilder.cs`, dekat `HttpRequestTimeout`):
- `BusinessTaskCachePath` (string, default `"./data/tasks"`), di-resolve terhadap
  `ContentRootPath` saat `BuildApp`, dengan cara yang sama seperti CDN.
- `BusinessTaskGracePeriod` (`TimeSpan`, default 5 menit).
- `BusinessTaskDefaultLimit` (`BusinessTaskLimit`, default `Global`, 2), yang hanya dipakai
  kalau metadata server belum punya nilai.

**Tipe publik untuk penulis action**, di `Em.Api.Core` karena hanya dipakai server:
- `BusinessTaskOptions`: `Key` (wajib), `Title` (wajib), `Scope` (wajib, tanpa default supaya
  penulis action memilih dengan sadar — pakai `required`), `NavigationName?`, `ResultFileName?`,
  dan `ResultContentType?`. `ResultFileName` wajib untuk task file.
- `BusinessTaskContext`: `CancellationToken CancellationToken`, `IServiceProvider Services`
  (scope milik task), `ActionRequest Starter` (identitas pemulai), `ILogger Logger`, dan
  `void Report(double? percent, string caption)`.
- `ServicesBase` mendapat pembantu `protected` (varian = overload bernama sama, bukan nama berbeda):
  - `BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options, Func<BusinessTaskContext, Task> work)`: jenis 1.
  - `BusinessTaskInfo StartBusinessTask<TResult>(BusinessTaskOptions options, Func<BusinessTaskContext, Task<TResult>> work)`: jenis 2 JSON; hasilnya diserialisasi dengan `Defaults.ResponseJsonOptions`.
  - `BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options, Func<BusinessTaskContext, Stream, Task> writeResult)`: jenis 2 file; stream-nya adalah `result.bin` di folder cache.
  - `BusinessTaskInfo? FindBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global)`:
    task berkunci itu yang hidup, atau yang gagal dan belum di-clear; untuk `Personal` dicari milik
    pemanggil. Flag `CanCancel`/`CanClear` = `true` (keputusan turunan "Flag per pemanggil").
  - `BusinessTaskInfo[] FindBusinessTasks(string keyPrefix, BusinessTaskScope scope = BusinessTaskScope.Global)`:
    sama, untuk satu keluarga kunci (dipakai `GetMeta_CdnExtractTasks`). Hanya ada di server; tidak
    diekspos sebagai action generik (keputusan 14).
  - `void CancelBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global)` dan
    `void ClearBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global)`: tanpa
    pemeriksaan kepemilikan; 404 kalau tidak ada, 409 kalau statusnya tidak cocok (cancel task
    selesai, clear task hidup).

  XML doc `StartBusinessTask` harus menerangkan: pekerjaan berjalan **di luar request**; `this`,
  `Request`, `AbortToken`, dan `GetService` milik action **tidak boleh** dipakai di dalam `work`,
  jadi pakai `ctx.Services` dan `ctx.Starter`; token hanya menyala saat cancel atau shutdown
  server; arti `Scope` (siapa yang melihat dan mengurus task); dan 409 untuk kunci yang masih hidup.
  XML doc `Find/Cancel/ClearBusinessTask` harus menerangkan bahwa hak diserahkan ke claim action
  yang memanggilnya.

**`BusinessTaskRunner`** (internal sealed, singleton, didaftarkan di `EmApp` bersama service
engine lain):
- Menyimpan registry `ConcurrentDictionary<string, TaskEntry>` (per id), indeks kunci hidup
  (kunci gabungan, lihat keputusan turunan), dan antrian FIFO. Semua transisi status memakai satu
  `lock`.
- `Start(...)`: menolak kunci yang hidup (409, pesan menyebut `OwnerName` task yang hidup), membuat
  `TaskEntry` berstatus `Queued`, lalu memanggil `Pump()`.
- `Pump()`: selama batas mengizinkan (mode Global: total Running < N; mode PerUser: ambil antrian
  pertama yang pemiliknya punya Running < N), jalankan task dengan
  `using (ExecutionContext.SuppressFlow()) Task.Run(...)`.
- Menjalankan satu task: buat `IServiceScope` dari provider akar, lalu isi holder scoped internal
  `BusinessTaskStarter { ActionRequest Request }` dengan identitas pemulai. Registrasi scoped
  `ActionRequest` di `EmApp.cs:92` diubah menjadi
  `sp => sp.GetRequiredService<BusinessTaskStarter>().Request ?? CurrentRequest ?? ActionRequest.None`,
  supaya kelas pembantu yang meminta `ActionRequest` lewat DI di dalam task melihat pemulainya.
  Tangkap `OperationCanceledException` (dengan token task menyala) sebagai `Canceled` dan exception
  lain sebagai `Failed` (`ErrorMessage` = pesan exception, stack ke log). Setelah task selesai,
  `Pump()` lagi.
- Hasil jenis 2 yang sukses: tulis `result.json`/`result.bin`, lalu `task.json` (potret
  `BusinessTaskInfo` tanpa flag per pemanggil). Kalau gagal atau batal, hapus subfolder saat itu
  juga. Entri gagal tetap di memori sampai di-clear.
- Timer sapu (30 detik): membuang entri `Succeeded` jenis 1 dan entri `Canceled` yang
  `FinishedAt + GracePeriod` sudah lewat.
- Startup (di `BuildApp`/sebelum request pertama, sejajar `SeedCoreMetadata`): pindai folder cache,
  muat `task.json` yang berstatus `Succeeded` ke registry, lalu hapus subfolder lain.
- Batas konkurensi: dibaca dari metadata server (kunci `BusinessTaskLimitMode` dan
  `BusinessTaskLimit`) sekali saat startup lewat scope sendiri. Nilai terbaru disimpan di field
  dan diperbarui oleh `PostMeta_BusinessTaskLimit`, yang menyimpan ke metadata lalu memanggil
  `Pump()`.
- `ApplicationStopping` → cancel semua token.
- `ToInfo(entry, ActionRequest caller, bool moduleGranted)` menghitung flag `Can*`
  (pemilik = `OwnerUserId == caller.cUserId`; admin = `caller.IsAdmin`; `moduleGranted` dari
  pembantu `Find*`). Pemeriksaan claim BTM dan claim module dilakukan di action, bukan di sini.
- `OwnerName` diambil dari nama user pemulai saat task dimulai. Pakai sumber nama yang sudah
  dipakai engine; kalau tidak tersedia murah, pakai user id dan catat di laporan.

**`BusinessTaskServices`** (`[Module(Defaults.AdministrativeToolsModuleName)]`, turunan
`ServicesBase`, `IBusinessTaskServices`), didaftarkan di `EmApp.cs` sekitar `:105` dengan
`AddService<IBusinessTaskServices, BusinessTaskServices>(enforceClaims: false)` dan komentar
sejenis CDN. Isinya tipis, mendelegasikan ke runner. Pemeriksaan hak:
- `GetMeta_BusinessTasks` dan `GetMeta_BusinessTaskLimit`: `[GetAction(claim: IBusinessTaskServices.ManagerClaim)]`.
- `PostMeta_BusinessTaskLimit`: `Request.RequireAdmin()` (tanpa claim di atribut, cukup login).
- `Cancel`, `Clear`, `...Result`: `Request.RequireSelfOrAdmin(entry.OwnerUserId)`.
- `GetMeta_BusinessTask`: pemilik, admin, atau pemegang `ManagerClaim` (cek claim di kode).
- `GetMeta_UserBusinessTasks`: `Request.RequireUserId()`, filter `Scope == Personal` dan pemilik =
  pemanggil.

### 4. CDN: archive dan extract — `CdnStore.cs`, `CdnServices.cs`

`CdnStore`:
- `ValidateArchive(folder, names, archiveName, overwrite)`: nama sah, sumber ada, `archiveName`
  berakhiran `.zip`, tidak termasuk di antara sumbernya sendiri, dan 409 kalau sudah ada tanpa
  overwrite. Dipanggil **sebelum** task dimulai.
- `ArchiveAsync(folder, names, archiveName, overwrite, IProgress<(double?, string)>, CancellationToken)`:
  hitung total byte lebih dulu untuk persen, tulis `ZipArchive` ke file sementara berawalan titik,
  lewati nama berawalan titik, lalu pindah ke nama akhir (timpa kalau diizinkan). Pada batal/gagal,
  hapus file sementara.
- `ReadExtractPlan(zipPath)`: baca daftar entri dan tegakkan aturan ketat (zip-slip, nama
  berawalan titik di segmen mana pun, `ValidateName` per segmen, jumlah entri). Hasilnya daftar
  path relatif + konflik terhadap isi folder tujuan (file yang sudah ada) + bentrok file/folder
  (400).
- `ExtractAsync(zipPath, overwrite, progress, token)`: buat folder sementara berawalan titik, ekstrak
  dengan menghitung byte nyata (per entri ≤ `MaxFileSize`, total ≤ `10 × MaxFileSize`, 413 kalau
  lewat), lalu pindahkan ke tempatnya tanpa mengamati token. Pada batal/gagal sebelum tahap pindah,
  hapus folder sementara.

`CdnServices`, dengan atribut `[PostAction(claim: ICdnServices.CdnClaim)]` / `[GetAction(...)]`:
- `PostGetMeta_CdnArchive`: validasi, lalu `StartBusinessTask(new() { Key = "cdn:archive", Scope = BusinessTaskScope.Global, Title = "Archive {name}" }, ctx => ctx.Services.GetRequiredService<CdnStore>().ArchiveAsync(..., ctx.CancellationToken))`.
- `GetMeta_CdnArchiveTask`: `FindBusinessTask("cdn:archive")`.
- `PostMeta_CdnArchiveCancel` / `PostMeta_CdnArchiveClear`: `CancelBusinessTask("cdn:archive")` /
  `ClearBusinessTask("cdn:archive")`.
- `GetMeta_CdnExtractConflicts`: `ReadExtractPlan`, lalu kembalikan konfliknya.
- `PostGetMeta_CdnExtract`: `ReadExtractPlan` ulang (409 kalau ada konflik tapi `overwrite`
  `false`), lalu mulai task global berkunci `cdn:extract:{path}` (path dinormalisasi seperti
  `CdnStore` menormalkan path).
- `GetMeta_CdnExtractTasks(folder)`: `FindBusinessTasks("cdn:extract:{folder}/")` (akar: tanpa
  garis miring di depan; pastikan prefiks tidak ikut menangkap subfolder — saring hasilnya ke zip
  yang induknya persis `folder`).
- `PostMeta_CdnExtractCancel(path)` / `PostMeta_CdnExtractClear(path)`: pembantu berbasis kunci.
- Perbarui XML doc `ICdnServices` (ringkasan antarmuka menyebut archive/extract dan bahwa semua
  pemegang claim CDN boleh membatalkannya).

### 5. Client service dan tracker — `Em.Ui.Core`, WPF, MAUI

- `src/shared/Em.Ui.Wpf.Core/Api.Core/BusinessTaskService.cs`: `ServiceWpfBase` +
  `IBusinessTaskServices`, mengikuti pola `CdnService.cs`, dengan `GetMeta_BusinessTaskFileResult`
  memakai `GetStreamAsync`. Daftarkan di `Core/EmApp.Statics.cs:76` di samping `CdnService`.
- `CdnService.cs`: tambah action CDN baru.
- `src/shared/Em.Ui.Maui.Core/Api.Core/BusinessTaskService.cs`: **stub** (keputusan 22).
  `ServiceMauiBase` + `IBusinessTaskServices`, setiap method melempar
  `NotImplementedException("Business tasks are not available in the MAUI client yet.")`. Daftarkan
  di `Em.Ui.Maui.Core/Core/EmApp.Statics.cs:113`. Beri komentar singkat bahwa implementasinya
  menyusul lewat plan terpisah. CDN tidak disentuh di MAUI.
- **`BusinessTaskTracker`** di `src/shared/Em.Ui.Core` (UI-agnostic, dipakai kedua core):
  - Memegang daftar task personal user (`GetMeta_UserBusinessTasks`) dan polling-nya
    (keputusan 20): 2 detik selama ada yang hidup, ±30 detik selama tidak ada, `RefreshAsync()`
    dipanggil saat popup dibuka.
  - `Track(BusinessTaskInfo)`: dipanggil layar module setelah memulai task personal, supaya task
    langsung tampil dan polling cepat dimulai tanpa menunggu poll lambat.
  - Menyediakan jumlah task hidup, penanda "ada yang selesai belum dilihat" beserta
    `MarkAllSeen()`, dan event perubahan (dimunculkan di thread UI lewat mekanisme dispatch yang
    sudah dipakai core; kalau belum ada abstraksinya, VM yang men-dispatch).
  - Mulai saat login, berhenti dan dikosongkan saat logout/ganti user.
  - Didaftarkan **hanya di core WPF** (`EmApp.Statics.cs`) lewat jalur registrasi builder yang
    sama dengan service lain. Di MAUI tidak didaftarkan (keputusan 22).

### 6. UI WPF — Business Task Manager

Ikuti aturan MVVM (`RegisterCommand`, `...Allowed`) dan bahasa desain material
(`src/frontend/CLAUDE.md`, referensi `RoleManager.xaml`/`UserManager.xaml`). Baca kedua bagian itu
sebelum menulis XAML.

- `Em.Ui.Wpf.Core/Navigations/BusinessTaskManager.xaml(.cs)` + view model.
- Claim client: tambah `BusinessTaskManagerClaim = "Administrative Tools:Business Task Manager Access"`
  di `EmApp.Statics.cs` (sejajar `CdnManagerClaim`) dan daftarkan di `InitInternalClaims`.
- Navigasi `admin.tasks`, Title "Business Task Manager", Subtitle "Monitor long-running server
  work", `Kind = NavigationKind.Manager`, `IsMenuVisible = false`, ikon
  `EFontAwesomeIcon.Solid_ListCheck`, dibungkus `RequireClaim(..., BusinessTaskManagerClaim)`.
- Isi layar:
  - **Kartu pengaturan**: ComboBox mode (Global Limit / By User Limit), angka limit, dan tombol
    Save. Semua bisa diedit hanya oleh admin (`SaveLimitCommandAllowed` = admin dan nilainya
    berubah); non-admin melihatnya read-only.
  - **Daftar task**: Title, Scope (Personal/Global), Key, Module, **Owner**, Status, progress bar +
    Caption, Started, Finished, dan Error (tooltip/teks). Tombol per baris: **Cancel**
    (`CanCancel`), **Clear** (`CanClear`), dan **Download** untuk `OutputKind == File` dengan
    `CanReadResult` (`SaveFileDialog`, isi disalin dari `GetMeta_BusinessTaskFileResult`).
    Hasil JSON cukup ditampilkan jenis outputnya.
  - Tombol Refresh. Polling 2 detik selama layar terlihat. Hentikan polling saat body tidak aktif,
    mengikuti cara layar lain menangani aktif/nonaktif; kalau belum ada polanya, pakai
    `DispatcherTimer` yang dimulai/dihentikan dari view model saat body dimuat/dilepas.
- Kesalahan action ditampilkan seperti layar admin lain (`EmMessageBox`/`DisplayExceptionData`).

### 7. UI WPF — hub task personal di main window

Berkas: `Windows/TabbedMainWindow.xaml(.cs)` + dialog baru.

- **Ikon hub** di `StackPanel` title bar (`TabbedMainWindow.xaml` sekitar baris 795–822), di antara
  tombol Tools dan tombol Color Theme, dengan `Visibility="{Binding WorkspaceToolsVisibility}"` dan
  gaya `flatIconButtonStyle` yang sama dengan Color Theme. Sinyal (keputusan 19):
  - Diam: ikon daftar tugas (mis. `Solid_ListCheck`), tooltip "Tasks".
  - Ada yang berjalan: ikon berputar (mis. `Solid_Spinner`/`Solid_CircleNotch`; pakai properti
    spin milik library FontAwesome WPF kalau ada, kalau tidak `RotateTransform` + `Storyboard`)
    dan **badge** berisi jumlah task hidup.
  - Ada yang selesai dan belum dilihat: **titik penanda** kecil di pojok ikon (warna aksen; merah
    kalau ada yang gagal).
  - Pakai token warna dari `Styles/MaterialDesign.xaml`, jangan warna baru. Pastikan terlihat di
    tema terang dan gelap.
- **Popup** (pola `Popup` + `menuPopupBorderStyle` yang sama dengan menu akun): daftar task personal
  dari `BusinessTaskTracker`. Setiap baris: Title, status/caption, progress bar tipis untuk yang
  hidup, dan ikon status untuk yang selesai. Kosong → "No Running Task Available". Membuka popup
  memanggil `RefreshAsync()` lalu `MarkAllSeen()`.
- **Dialog status** `Dialogs/BusinessTaskStatusDialog.xaml(.cs)` (turunan `EmWindow`, gaya
  mengikuti `Dialogs/DisplayExceptionData.xaml`), dibuka dengan klik baris di popup. Memuat ulang
  `GetMeta_BusinessTask(id)` tiap 2 detik selama task hidup. Isinya menurut status:
  - Queued/Running: Title, progress + caption, **Cancel** (`CanCancel`, dengan konfirmasi).
  - Succeeded: jenis 1 → keterangan selesai; `File` → **Download** (`SaveFileDialog` +
    `GetMeta_BusinessTaskFileResult`); `Json` → **Open** (stub, keputusan 18: `EmMessageBox`
    "Opening this result is not available yet."). Plus **Clear** untuk jenis 2.
  - Failed: `ErrorMessage` + **Clear**.
  - Canceled: keterangan dibatalkan.
  Setelah Cancel/Clear, tracker di-refresh.
- Semua command lewat `RegisterCommand` di VM `TabbedMainWindowVm` / VM dialog.

### 8. UI WPF — CDN Manager

Berkas: `Navigations/CdnManager.xaml(.cs)`.

- `SelectionMode="Extended"`. Seleksi banyak item dibaca lewat `CdnManagerItem.IsSelected` yang
  diikat di `ItemContainerStyle` (`IsSelected="{Binding IsSelected}"`). Ini bindable, tanpa
  code-behind. `SelectedItem` tetap menjadi item fokus untuk semua perintah lama (keputusan 24).
- VM memegang `ArchiveTask` (`BusinessTaskInfo?`) dari `GetMeta_CdnArchiveTask()` dan status extract
  per baris (`CdnManagerItem.ExtractTask`) dari `GetMeta_CdnExtractTasks(folder)`. Keduanya dimuat
  saat folder dibuka lalu dipoll (2 detik / ±10 detik, keputusan turunan). Begitu sebuah task
  berubah jadi `Succeeded`, daftar folder di-refresh.
- **Tombol Create Archive** di toolbar, di kanan "Open in Browser" (`CdnManager.xaml` sekitar baris
  201–209), satu tombol dengan satu command `ArchiveCommand` yang bertindak menurut `ArchiveTask`:
  - `null`: ikon zip + "Create Archive". Allowed bila minimal satu item terpilih dan tidak ada
    transfer berjalan. Alurnya: nama default → `TextInputDialog` → kalau nama sudah ada di daftar,
    tanya timpa lewat `EmMessageBox` → `PostGetMeta_CdnArchive`. 409 ditampilkan dengan nama
    pemulai task yang sedang hidup, lalu status dimuat ulang.
  - Queued/Running: ikon berputar + "Archiving 42%" (atau "Queued"), tooltip berisi caption dan
    nama pemulai. Klik → konfirmasi → `PostMeta_CdnArchiveCancel`.
  - Failed: ikon error + "Archive failed". Klik → tampilkan `ErrorMessage` → konfirmasi Clear →
    `PostMeta_CdnArchiveClear`.
  Tampilan tiap keadaan diatur lewat `DataTrigger` pada properti VM (mis. `ArchiveButtonMode`),
  bukan code-behind.
- **Ikon Extract** di aksi per baris (di samping Copy Link/Download/Delete, `CdnManager.xaml` sekitar
  baris 385–410), hanya untuk file `.zip`, satu command `ExtractCommand` (parameter baris) yang
  bertindak menurut `ExtractTask` baris itu:
  - `null`: `GetMeta_CdnExtractConflicts` → kalau ada, tanya "Overwrite N existing file(s)?" →
    `PostGetMeta_CdnExtract`.
  - Queued/Running: ikon berputar di baris itu, tooltip progress + pemulai. Klik → konfirmasi →
    `PostMeta_CdnExtractCancel`.
  - Failed: ikon error. Klik → pesan error → konfirmasi Clear → `PostMeta_CdnExtractClear`.
- Command baru mengikuti aturan `RegisterCommand` (lihat baris `CdnManager.xaml.cs:130-142`).
  Menu konteks tidak mendapat entri baru (keputusan turunan).

### 9. Dokumen

- `doc/TODO-LIST.md`, Prioritas 4: tambah item berikut, lalu perbarui daftar nomor di "Yang
  menunggu keputusan":
  - **Layanan email engine**: `IEmailSender` + konfigurasi SMTP, supaya business task module bisa
    mengirim hasilnya lewat email. Konfigurasi dan penyedianya perlu diputuskan.
  - **Open hasil JSON business task**: saat ini stub; putuskan viewer JSON generik dan/atau
    pembukaan editor module lewat `NavigationName` setelah kebutuhannya terlihat.
  - **Business task di MAUI** (butuh plan terpisah): ganti stub `BusinessTaskService` dengan
    implementasi sebenarnya, daftarkan dan jalankan `BusinessTaskTracker`, lalu hub task personal,
    dialog status, dan layar BTM. Letak hub di layout SPA Android perlu diputuskan.
- Pindahkan berkas plan ini ke `plan/executed/` dan ubah status di kepalanya menjadi "sudah
  dieksekusi — <tanggal>", disertai catatan eksekusi di akhir berkas (apa yang menyimpang dari plan
  dan alasannya, selisih MAUI, hasil build/uji).

### 10. Verifikasi

1. `dotnet build src/backend/Em.Api.slnx`, `dotnet build src/frontend/Em.Ui.Wpf.slnx`, dan
   solution MAUI di `src/frontend` harus bersih dari error. Kalau build MAUI tidak bisa jalan di
   mesin ini karena toolchain, catat itu, jangan diabaikan diam-diam.
2. Uji server (jalankan `src/backend/Em.Api`, panggil lewat debug token atau client):
   - Archive satu folder, lalu `GetMeta_CdnArchiveTask()` harus memperlihatkan Running → Succeeded,
     zip muncul, dan entri hilang setelah grace time. Untuk uji, pakai grace pendek sementara lewat
     `Program.cs`, lalu kembalikan.
   - Archive kedua saat yang pertama masih berjalan (user lain) harus dijawab 409 dengan nama
     pemulai.
   - Pemegang claim CDN non-admin bisa cancel archive milik user lain → `Canceled`, tanpa file
     sementara tersisa.
   - Extract zip berisi `../x` atau `.hidden` harus dijawab 400 tanpa sisa. Extract dengan konflik
     tanpa overwrite harus dijawab 409.
   - Task gagal harus tetap ada setelah grace time. Setelah restart server, task gagal hilang dan
     cache-nya terhapus.
   - Batas: set Global 1, mulai archive dan extract, dan yang kedua harus `Queued` lalu `Running`.
   - Non-admin tanpa claim BTM yang memanggil `GetMeta_BusinessTasks` harus mendapat 403.
     Pemilik yang bukan admin tidak bisa cancel task personal orang lain lewat
     `PostMeta_BusinessTaskCancel` (403).
   - Task personal: dua user memulai kunci yang sama → keduanya jalan; user yang sama dua kali → 409.
     `GetMeta_UserBusinessTasks` hanya mengembalikan task personal milik pemanggil.
3. Uji manual WPF:
   - CDN Manager: Create Archive multi-select, tombol beranimasi, dan client kedua (user lain) yang
     membuka CDN Manager melihat tombol beranimasi dan bisa cancel; archive gagal → pesan error +
     Clear; Extract dengan dialog timpa, animasi di baris zip.
   - BTM: task personal dan global tampil dengan Owner, pengaturan read-only untuk non-admin,
     Cancel/Clear untuk admin.
   - Hub: ikon, badge, titik penanda, popup kosong "No Running Task Available", dialog status per
     keadaan (Cancel, Download, Open stub, error + Clear), di layout Tabbed dan SPA.
   - Tema terang/gelap untuk semua layar di atas.
   Task personal dan jenis 2 (JSON/file) belum punya pemakai di plan ini. Uji jalurnya dengan satu
   action uji sementara (personal, jenis 2 file dan JSON), lalu hapus sebelum commit; atau catat di
   laporan bahwa jalur itu hanya terbukti lewat build.

### 11. Commit

Satu commit di akhir, setelah build lolos. Pesan dalam Bahasa Indonesia (ringkasan + isi), diakhiri
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Jangan ikutkan berkas `.idea` atau
isi `src/backend/Em.Api/data/`.

## Ditunda (bukan bagian plan ini)

- Open hasil JSON yang sebenarnya (viewer generik dan/atau editor module lewat `NavigationName`).
- Business task di MAUI (plan terpisah): client service sebenarnya (sekarang stub), tracker, hub
  task personal, dialog status, dan layar BTM.
- Layanan email.
- Pencatatan riwayat task ke "Bisnis Logger" (disebut user sebagai kemungkinan nanti).
- Multi-select untuk Delete, drag, dan perintah CDN lain.
- Entri archive/extract di menu konteks CDN Manager.

---

## Catatan kemajuan eksekusi (dibagi 3 sesi)

Eksekusi dibagi tiga sesi atas permintaan user: **A** = langkah 1–4 (backend), **B** = langkah 5–8
(client dan UI), **C** = langkah 9–11 (dokumen, uji, commit). Belum ada commit sampai sesi C.

### Sesi A — selesai (2026-09-27)

Langkah 1–4 dikerjakan; `dotnet build src/backend/Em.Api.slnx` bersih (0 error) dan
`Em.Ui.Core.csproj` ikut lolos build. **Solution frontend sengaja belum dibuild dan saat ini gagal
build**: `CdnService` (WPF) belum mengimplementasikan action CDN baru, dan `BusinessTaskService`
belum ada. Keduanya adalah pekerjaan langkah 5 (sesi B).

Berkas baru: `Em.Libs/Api.Core.Models/{BusinessTaskStatus,BusinessTaskScope,BusinessTaskOutputKind,
BusinessTaskLimitMode,BusinessTaskLimit,BusinessTaskInfo,IBusinessTaskServices,CdnArchiveRequest}.cs`,
`Em.Api.Core/Api/Core/{BusinessTaskOptions,BusinessTaskContext,BusinessTaskRunner,BusinessTaskServices}.cs`.
Berkas diubah: `ICdnServices.cs`, `ActionDefinition.cs`, `EmAppBuilder.cs`, `EmApp.cs`,
`ServicesBase.cs`, `CdnStore.cs`, `CdnServices.cs`, dan di `Em.Ui.Core`: `ApiClient.cs`,
`IApiClient.cs`, `ServiceUiBase.cs`, `Extensions.cs` (bagian client dari langkah 2 sudah selesai).

Penyimpangan kecil dari plan (untuk laporan akhir):
- `CdnStore.ArchiveAsync`/`ExtractAsync` menerima `Action<double?, string> report`, bukan
  `IProgress<(double?, string)>`, karena langsung diisi `ctx.Report`.
- `OwnerName` diisi `cUserAccount`, atau user id kalau kosong, saat task dimulai, lalu diganti nama
  lengkap kontak dari data user oleh lookup latar yang tidak menahan start. Akun debugger/admin
  memakai nama akunnya.
- `BusinessTaskInfo.IsAlive` (`[JsonIgnore]`) ditambahkan sebagai pembantu untuk client.
- Dispatcher menolak saat startup action yang mengembalikan turunan `Stream` (mis. `Task<FileStream>`);
  hanya `Task<Stream>` yang dijawab sebagai isi file.
- `GetMeta_BusinessTaskJsonResult`/`FileResult` menjawab 409 kalau task belum sukses (selain 400 untuk
  jenis hasil yang salah).
- Status task extract per baris di client dicocokkan lewat `BusinessTaskInfo.Key` =
  `"cdn:extract:" + path zip` (path relatif CDN yang dinormalisasi).

### Sesi B — selesai (2026-09-27)

Langkah 5–8 dikerjakan. `dotnet build` untuk `src/frontend/Em.Ui.Wpf.slnx`, `src/frontend/Em.Ui.Maui.slnx`,
dan `src/backend/Em.Api.slnx` semuanya bersih (0 error). Belum ada uji manual; itu bagian sesi C.

Berkas baru: `Em.Ui.Core/Ui.Core/shared/BusinessTaskTracker.cs`,
`Em.Ui.Wpf.Core/Api.Core/BusinessTaskService.cs`, `Em.Ui.Maui.Core/Api.Core/BusinessTaskService.cs` (stub),
`Em.Ui.Wpf.Core/Shared/BusinessTaskItem.cs`, `Em.Ui.Wpf.Core/Styles/Progress.xaml`,
`Em.Ui.Wpf.Core/Navigations/BusinessTaskManager.xaml(.cs)`, `Em.Ui.Wpf.Core/Dialogs/BusinessTaskStatusDialog.xaml(.cs)`.
Berkas diubah: `CdnService.cs`, `EmApp.Statics.cs` (WPF dan MAUI), `EmApp.cs` (WPF), `Styles/MaterialDesign.xaml`,
`Windows/TabbedMainWindow.xaml(.cs)`, `Navigations/CdnManager.xaml(.cs)`, `src/frontend/CLAUDE.md`.

Penyimpangan dan keputusan kecil (untuk laporan akhir):
- **Tracker menyala lewat `EmApp.Run`**, bukan dari VM: `ActiveUserChanged` → `BusinessTaskTracker.SetUser(userId)`
  lewat dispatcher, plus satu panggilan awal untuk user debug. Polling-nya loop async yang dimulai dari thread UI,
  jadi event `Changed` otomatis kembali ke thread UI tanpa abstraksi dispatch baru. Kegagalan polling diabaikan
  (daftar lama tetap dipakai).
- **"Belum dilihat"** dihitung dari transisi: task yang pada polling sebelumnya hidup lalu selesai. Task yang sudah
  selesai saat login tidak memunculkan titik penanda.
- **Kelas tampilan bersama `BusinessTaskItem`** (Shared) dipakai BTM, popup hub, dan dialog status: caption status,
  ikon, waktu lokal, flag `CanDownload`/`CanOpen`, `Sync(...)` untuk memperbarui koleksi di tempat, dan
  `DownloadResultAsync(...)` (tulis ke `.partial` lalu pindah).
- **Progress bar hairline** dipindah dari `CdnManager.xaml` (`transferProgressStyle`) ke `Styles/Progress.xaml`
  sebagai `hairlineProgressStyle` (ditambah animasi "bernapas" untuk mode tak tentu), bersama `countBadgeStyle`
  dan `attentionDotStyle` untuk ikon hub. Keduanya dipakai lebih dari satu layar.
- **Hub tampil di kedua layout**: visibilitasnya `TaskHubVisibility` (window utama + ada user aktif), bukan
  `WorkspaceToolsVisibility` seperti yang ditulis di langkah 7, karena `WorkspaceToolsVisibility` selalu
  tersembunyi di layout SPA sedangkan keputusan 17 meminta hub di kedua layout. Popup berjudul "MY TASKS".
- **BTM**: polling 2 detik hanya membaca daftar (bukan batas, supaya isian admin tidak tertimpa), dijalankan
  dari `Loaded`/`Unloaded` body (diteruskan dari code-behind) dan berhenti saat error. Tombol Cancel/Clear per
  baris tampil menurut flag server tetapi hanya aktif untuk admin (keputusan 13). Clear dikonfirmasi hanya
  kalau task punya hasil tersimpan. `admin.tasks` ditambahkan ke daftar menu Tools di `EmApp.cs`.
- **Dialog status**: saat server menjawab 404 (task sudah hilang) dialog tetap menampilkan potret terakhir
  dengan keterangan "This task is no longer on the server."; Clear menutup dialog.
- **CDN Manager**:
  - Nama default archive untuk satu **file** membuang ekstensinya (`report.pdf` → `report.zip`, seperti Explorer);
    untuk satu folder tetap `<nama folder>.zip`.
  - Keadaan gagal tombol Create Archive memakai elemen tombol kedua bergaya `dangerOutlinedButtonStyle` (command
    sama, tampil bergantian), karena warna hover state layer tidak bisa ditukar lewat trigger.
  - Tampilkan error + konfirmasi Clear digabung menjadi satu dialog.
  - Selain binding `IsSelected` di `ItemContainerStyle`, `SelectionChanged` diteruskan satu baris ke VM
    (`ApplySelection`): Ctrl+A memilih baris yang container-nya belum dibuat karena virtualisasi, dan baris itu
    tidak pernah sampai ke binding.
  - Status task dibaca ulang setiap folder dibaca, lalu dipoll 2 detik/10 detik. Task yang tadinya hidup lalu
    hilang dari jawaban (sukses atau dibatalkan) memicu baca ulang folder.
  - Kelas `CdnTaskMode` (None/Running/Failed) dipakai sebagai keadaan tombol archive dan ikon extract.
- **Selisih MAUI** (sesuai keputusan 22): hanya `BusinessTaskService` stub yang didaftarkan; tracker, hub,
  dialog, dan BTM tidak ada di MAUI.

### Sesi C — selesai (2026-09-27)

Langkah 9–11 dikerjakan.

**Dokumen.** `doc/TODO-LIST.md` Prioritas 4 mendapat item 15 (layanan email engine), 16 (Open hasil JSON
business task), dan 17 (business task di MAUI). Ketiganya masuk daftar "menunggu keputusan". Berkas plan
dipindah ke `plan/executed/`.

**Build (10.1).** `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, dan
`src/frontend/Em.Ui.Maui.slnx` bersih: 0 warning, 0 error. Build terakhir dijalankan setelah action uji
sementara dihapus.

**Uji server (10.2).** Uji dijalankan lewat harness konsol di luar repo yang memakai `ApiClient` dengan
token debug (identitas `debugger`, admin). Grace time dipendekkan sementara menjadi 20 detik di `Program.cs`,
lalu dikembalikan. Action uji sementara `BtProbe` (task personal/global, jenis 1, JSON, file, dan gagal
sengaja) didaftarkan lewat berkas terpisah di `Em.Api` lalu dihapus sebelum build terakhir. Hasilnya
53 pemeriksaan lolos:
- Archive dua folder (±300 MB): terlihat Running → Succeeded, zip terbentuk, entri berawalan titik tidak
  ikut, tidak ada file sementara tersisa. Entri sukses tampil di `GetMeta_BusinessTasks`, lalu hilang
  setelah grace time. Nama zip yang sudah ada tanpa overwrite dijawab 409.
- Archive kedua saat yang pertama hidup dijawab 409 dengan pesan "...started by debugger".
  `PostMeta_CdnArchiveCancel` → `Canceled`, tanpa zip maupun file sementara tersisa, dan entri itu hilang
  setelah grace time.
- Extract zip berisi `../x.txt` atau `.hidden` dijawab 400, baik lewat `GetMeta_CdnExtractConflicts`
  maupun `PostGetMeta_CdnExtract`, tanpa sisa di luar folder. Konflik dilaporkan (`bttest/src/a.txt`).
  Tanpa overwrite dijawab 409, dengan overwrite isi file tertimpa. Zip bersubfolder terekstrak utuh tanpa
  folder sementara tersisa. `GetMeta_CdnExtractTasks("")` tidak ikut menangkap zip di subfolder.
- Batas Global 1: task kedua mula-mula `Queued`, lalu berjalan setelah yang pertama selesai. Batas 0
  dijawab 400. Batas dikembalikan ke nilai awal (Global 2).
- Task personal: kunci yang sama dua kali oleh user yang sama dijawab 409, dan `GetMeta_UserBusinessTasks`
  hanya berisi task personal. Hasil JSON terbaca (`Starter` di dalam task = pemulainya). Hasil file terbaca
  lewat `GetStreamAsync` (jalur `Task<Stream>` di dispatcher dan client terbukti). Meminta hasil file dari
  task JSON dijawab 400. Clear pada task hidup dijawab 409. Clear menghapus subfolder cache, dan task yang
  sudah di-clear dijawab 404. Cancel pada task selesai dijawab 409.
- Task gagal tetap ada setelah grace time, tanpa folder cache, dan stack-nya tercatat di log. Task JSON sukses
  juga tetap ada.
- Restart (proses dimatikan paksa, jadi `ApplicationStopping` tidak sempat jalan): task gagal dan task file
  yang sedang berjalan saat restart hilang, dan folder cache task yang terputus terhapus saat startup. Task
  JSON sukses tetap ada dan hasilnya masih terbaca. Setelah di-clear, folder cache kosong.

Satu-satunya "gagal" di harness adalah ekspektasi harness yang salah: cancel archive yang sudah dibatalkan
diharapkan 404, padahal server menjawab 409 ("already finished") karena entrinya masih dalam grace time. Itu
sesuai plan langkah 3 (409 untuk cancel task yang sudah selesai).

**Belum terbukti di server:** skenario yang butuh akun kedua atau akun non-admin. Ini mencakup archive kedua
oleh *user lain*, cancel oleh pemegang claim CDN non-admin, 403 `GetMeta_BusinessTasks` tanpa claim BTM,
403 cancel task personal orang lain, dan dua user yang sama-sama memulai kunci personal yang sama. Di
database hanya ada akun administrator (lihat TODO "Uji gerbang claim dengan akun non-admin sungguhan"), dan
pembacaan tabel user untuk mencari akun penyamaran tidak dilakukan. Logika hak-nya terbukti lewat build dan
review kode saja. Shutdown yang rapi (`ApplicationStopping`) juga belum diuji; yang diuji hanya proses mati
paksa.

**Efek samping uji:** batas konkurensi sekarang tersimpan di metadata server (Global 2, sama dengan default
builder), karena harness menyimpannya lalu mengembalikannya.

**Uji manual WPF (10.3) belum dijalankan.** Uji ini butuh interaksi GUI dan dua client login: CDN Manager
(Create Archive multi-select, tombol beranimasi, client kedua melihat dan membatalkan, error + Clear, Extract
dengan dialog timpa), BTM (Owner, read-only non-admin, Cancel/Clear admin), hub (ikon, badge, titik, popup
kosong, dialog status per keadaan, layout Tabbed dan SPA), serta tema terang/gelap. Jalur yang dipakai layar-layar
itu sudah terbukti di sisi server lewat uji di atas.

**Commit (11).** Satu commit berbahasa Indonesia untuk seluruh pekerjaan sesi A–C.
