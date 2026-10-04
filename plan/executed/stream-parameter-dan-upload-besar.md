# Parameter Stream di dispatcher + upload besar CDN

## Latar belakang

Upload CDN sekarang (`ICdnServices.PostGetMeta_CdnUpload(string? path, string fileName, byte[] content, bool overwrite)`)
mengirim isi file sebagai `byte[]` di dalam body JSON (`PostMethodPayload`, Base64). Akibatnya:

- File lebih dari ~119 MB gagal dengan **error JSON**: `Utf8JsonWriter` menolak nilai Base64 di atas 125.000.000 byte.
- Seluruh file (plus string Base64-nya, +33%) ditampung di memori, di client maupun server.
- Request kena timeout koneksi client (`ApiConnection.Timeout`, bawaan 30 detik).
- `EmApp.ApplyCdnRegistration` menaikkan `KestrelServerOptions.Limits.MaxRequestBodySize` **global** demi Base64.

Plan ini menambahkan dukungan parameter `Stream` di dispatcher (`EmApp`) dan `PostStreamAsync` di client, lalu memindahkan
upload CDN ke jalur itu.

Plan ini juga membawa **satu perbaikan lama yang perlu diverifikasi ulang** (lihat langkah 1).

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Bentuk action | Action ber-stream: POST, **tepat satu** parameter `System.IO.Stream`, dan **paling banyak satu** parameter lain (DTO payload). Urutan parameter bebas. Dilanggar → `InvalidOperationException` saat startup (registrasi action). |
| 2 | Transport | Body request = isi stream mentah (`application/octet-stream`). Payload dikirim di **header `Em-X-StreamPayload`**. |
| 3 | Bentuk payload | **Satu DTO bernama** (bukan `PostMethodPayload[]` posisional). Tipe tujuan diambil dari signature method di server, bukan dari string kiriman client. |
| 4 | Encoding header | **Base64Url dari JSON UTF-8**. |
| 5 | Batas header | **4 KB** (4096 karakter setelah encoding). Client menolak sebelum mengirim; server menjawab 400. |
| 6 | Signature client | `ApiClient.PostStreamAsync(controller, action, Stream content, object? payload)` + versi `<T>`; di `ServiceUiBase`: `PostStreamAsync(actionName, Stream, object? payload)` + `<T>` (protected, sejajar `PostAsync`). |
| 7 | Kontrak CDN | `byte[]` **dihapus**. Menjadi `Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content)`. Penaikan `MaxRequestBodySize` Kestrel global di `ApplyCdnRegistration` **dihapus**. |
| 8 | Batas ukuran file | Tetap `EnableCdn(path, maxFileSizeMb)`, bawaan **tetap 20 MB**. Ditegakkan saat menyalin stream (hitung byte), bukan lewat batas Kestrel. |
| 9 | Progres UI | CDN Manager menampilkan **persen + byte terkirim per file** dan **tombol Cancel** yang menghentikan file yang sedang dikirim beserta sisa antrean. |
| 10 | Issue lama | Perbaikan exception drag-out (`VirtualFileDataObject` tanpa throw) **sudah di-commit bersama plan ini**; langkah 1 memverifikasinya ulang, dan perbaikan tambahan (kalau ada) ikut commit akhir. |
| 11 | `.gitignore` | Tambahkan `src/backend/Em.Api/data/`. |
| 12 | Commit | **Satu commit di akhir**, pesan Bahasa Indonesia, diakhiri `Co-Authored-By`. Jangan ikutkan `src/backend/.idea/.idea.Em.Api/.idea/inspectionProfiles/`. |

### Keputusan turunan (diambil saat menyusun plan)

- **Gerbang dulu, body belakangan.** Rate limit, identitas, claim, dan verb sudah diperiksa sebelum binding. Batas body
  dilepas (`IHttpMaxRequestBodySizeFeature.MaxRequestBodySize = null`) **hanya** untuk action ber-stream dan **sesudah**
  gerbang lolos. Request yang ditolak tidak pernah menerima body.
- **Body tidak dibaca dispatcher.** `http.Request.Body` diserahkan apa adanya ke parameter `Stream`. Action yang menjawab
  409/400 tanpa membaca body tidak menerima isi file (lihat Expect-100 di bawah).
- **`Expect: 100-continue` di client** untuk setiap `PostStreamAsync`. Kestrel baru mengirim `100 Continue` saat body
  mulai dibaca action, jadi penolakan gerbang dan penolakan awal action (409 file sudah ada, 400 nama tidak sah) tidak
  membuat file ikut terkirim. Karena itu CDN Manager **tetap** mengandalkan 409 dari server untuk dialog timpa
  (tidak perlu pra-cek listing).
- **Timeout.** `ApiClient` memakai `HttpClient` kedua khusus stream: handler yang sama (`disposeHandler: false`),
  `BaseAddress` sama, `Timeout = Timeout.InfiniteTimeSpan`. Dilepas di `ApiClient.Dispose`.
- **Refresh token 401.** `SendAsync` mengulang request sekali setelah refresh. Untuk stream, ulangi hanya kalau
  `content.CanSeek` (posisi awal dicatat, lalu di-seek balik); kalau tidak bisa di-seek, lempar 401 aslinya.
  `StreamContent` dibungkus stream pembungkus yang **tidak** menutup stream pemanggil saat request di-dispose.
- **Cancel** tanpa mengubah kontrak `ICdnServices`: VM membungkus `FileStream` dengan stream progres yang juga memeriksa
  `CancellationToken` di setiap `Read`. Batal → `Read` melempar `OperationCanceledException` → HttpClient memutus
  request → server melihat `RequestAborted`, `CdnStore` menghapus file sementara.
- **Progres tanpa `WaitOverlay`.** Selama upload, `IsBusy = true` (perintah lain mati) tetapi `InWaiting = false`
  supaya chip progres dan tombol Cancel di toolbar tetap bisa diklik. Untuk operasi lain `WaitOverlay` tetap dipakai
  seperti sekarang.
- **Helper protokol bersama** `Em.Libs/Shared/StreamPayloadProtocol.cs` (pola `UserHeaderProtocol`): `Encode(object)`
  dan `TryDecode(string header, Type type, out object? value, out string? error)`, plus konstanta `MaxHeaderLength = 4096`.
  Nama header di `Defaults.StreamPayloadHeader = "Em-X-StreamPayload"`. XML doc Bahasa Indonesia.
- **Header tidak ada** saat action punya parameter payload: nilai `null` kalau tipenya nullable/reference, selain itu 400.
  Header ada tapi action tidak punya parameter payload: 400.
- **MAUI**: `ApiClient`/`ServiceUiBase` ada di `Em.Ui.Core` (dipakai bersama), jadi MAUI otomatis punya
  `PostStreamAsync`. Tidak ada layar/klien CDN di MAUI (keputusan plan CDN sebelumnya). Coba build
  `src/frontend/Em.Ui.Maui.slnx`; kalau mesin tidak punya workload MAUI/Android, catat di laporan, jangan berhenti.

## Keadaan repo saat plan ditulis

- Branch `spa-detach`. Commit terakhir: `7d402e6 Drag & drop di CDN Manager dan perbaikan border hitam dialog`.
- **Di-commit bersama file plan ini** (commit sesudah `7d402e6`):
  - `src/shared/Em.Ui.Wpf.Core/Shared/VirtualFileDataObject.cs`. Ditulis ulang: `IOleDataObject`/`IOleStream`
    `[PreserveSig]` + `MtaExport`, supaya drag-out ke Explorer tidak melempar exception (dulu `COMException 0x80040064
    "Format not available."` dari `GetData` membuat debugger berhenti dan drag gagal). Sudah diuji tanpa debugger:
    drag-out ke `D:\temp` (hash cocok) dan pindah internal lewat proxy OK. **Belum diuji di bawah debugger IDE.**
  - `src/frontend/CLAUDE.md` (satu kalimat tentang HRESULT/MTA di bagian drag and drop).
- `src/backend/Em.Api/data/cdn/428.webp`: file milik user. **Jangan dihapus**. File uji buatan sendiri dihapus
  setelah verifikasi.
- User bisa sedang menjalankan API-nya sendiri di `localhost:5132`. **Jangan mematikan proses yang tidak kamu jalankan.**

## Contoh pemakaian (yang dilihat penulis action)

Service **tidak pernah** membaca header atau body sendiri. Dispatcher yang men-decode header ke parameter DTO dan
menyerahkan body sebagai parameter `Stream`:

```csharp
// Backend: hanya stream, tanpa payload
[PostAction]
public async Task PostMeta_ImportData(Stream data) { await ReadAllAsync(data); }

// Backend: stream + satu DTO payload (diisi otomatis dari header Em-X-StreamPayload)
[PostAction(claim: ICdnServices.CdnClaim)]
public Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content) =>
   Store.UploadAsync(request.Path, request.FileName, content, request.Overwrite, AbortToken);
```

```csharp
// Client (service turunan ServiceUiBase)
public Task PostMeta_ImportData(Stream data) => PostStreamAsync(nameof(PostMeta_ImportData), data);

public Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content) =>
   PostStreamAsync<CdnEntry>(nameof(PostGetMeta_CdnUpload), content, request);
```

`PostStreamAsync` yang menyerialisasi `request` ke header (JSON → Base64Url), dan dispatcher yang membalikkannya ke
`CdnUploadRequest`. Kalau action tidak punya parameter DTO, `payload` dibiarkan `null` dan header tidak dikirim.

## Desain per file

### 1. Shared (`src/shared/Em.Libs`)

- `Defaults.cs`: `public const string StreamPayloadHeader = "Em-X-StreamPayload";` dengan XML doc.
- `Shared/StreamPayloadProtocol.cs` (baru, `public static`): `Encode`, `TryDecode`, `MaxHeaderLength`. Encoding pakai
  `System.Buffers.Text.Base64Url` + `JsonSerializer` dengan `Defaults.ResponseJsonOptions`.
- `Api.Core.Models/CdnUploadRequest.cs` (baru): `string? Path`, `string FileName`, `bool Overwrite`. XML doc Indonesia.
- `Api.Core.Models/ICdnServices.cs`: signature upload baru + XML doc diperbarui (409/413/400 tetap berlaku).

### 2. Backend (`src/backend/Em.Api.Core`)

- `Api/Core/ActionDefinition.cs`: `int? StreamParameterIndex`, `int? StreamPayloadParameterIndex` (XML doc).
- `Api/Shared/EmAppBuilder.cs` → `RegisterActions`: deteksi parameter bertipe persis `typeof(Stream)`, validasi
  keputusan 1, isi dua properti di atas.
- `Api/Core/EmApp.cs` → cabang POST di `ProcessRequest`: kalau `StreamParameterIndex` terisi, pakai binding stream
  (method baru, mis. `TryBindStreamArguments(actionDef, http, out arguments, out error)`): baca header, decode ke tipe
  parameter payload, lepas batas body, isi `arguments[streamIndex] = http.Request.Body`. Kalau tidak, jalur lama.
- `Api/Core/EmApp.cs` → `ApplyCdnRegistration`: hapus blok `Services.Configure<KestrelServerOptions>` dan using
  `Microsoft.AspNetCore.Server.Kestrel.Core` kalau tidak terpakai lagi; perbarui komentar/XML doc-nya.
- `Api/Core/CdnStore.cs` → `UploadAsync(string? path, string fileName, Stream content, bool overwrite,
  CancellationToken token)`: validasi nama, folder, dan konflik **sebelum** membaca stream; salin ke `.upload-{guid}.tmp`
  dengan buffer 81920 sambil menghitung byte; lewat `MaxFileSize` → hapus temp, `ActionException(413)`; batal/galat →
  hapus temp; selesai → `File.Move(temp, target, overwrite: true)`.
- `Api/Core/CdnServices.cs`: `PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content)` →
  `Store.UploadAsync(request.Path, request.FileName, content, request.Overwrite, AbortToken)`. Atribut
  `[PostAction(claim: ICdnServices.CdnClaim)]` tetap.

### 3. Client (`src/shared/Em.Ui.Core`, `src/shared/Em.Ui.Wpf.Core`)

- `Em.Ui.Core/Ui.Core/shared/ApiClient.cs`: `PostStreamAsync` + `<T>`, `HttpClient` stream (lazy), refactor `SendAsync`
  agar menerima client yang dipakai (dan `CancellationToken` opsional), pembungkus stream tanpa-dispose, header payload
  (tolak > 4 KB sebelum kirim), `ExpectContinue = true`, `Content-Type: application/octet-stream`.
- `Em.Ui.Core/Ui.Core/shared/ServiceUiBase.cs`: `protected Task PostStreamAsync(string action, Stream content,
  object? payload = null)` + `<T>`.
- `Em.Ui.Wpf.Core/Api.Core/CdnService.cs`: implementasi signature baru lewat `PostStreamAsync<CdnEntry>`.
- `Em.Ui.Wpf.Core/Navigations/CdnManager.xaml(.cs)`:
  - `UploadBatchAsync`: ganti `File.ReadAllBytesAsync` dengan `FileStream` (`FileOptions.Asynchronous |
    SequentialScan`) dibungkus stream progres (kelas privat di file yang sama, pola kelas pembantu yang sudah ada di
    sana), `IProgress<long>` dibuat di thread UI dan di-throttle (mis. ≤ 10 update/detik).
  - Retry 409 (Yes = overwrite) membuka stream baru dari awal.
  - Properti VM baru (pola `Get`/`Set`, **public** — `MvvmModelBase` hanya menemukan properti public): mis.
    `UploadPercent` (double), `UploadProgressCaption` (sudah ada, isinya jadi mis.
    `"setup.bin — 45% (45.2 MB of 100 MB) · 2 of 5"`), `IsUploading` (sudah ada).
  - Command `CancelUploadCommand` + `CancelUploadCommandAllowed` (hanya saat `IsUploading`), didaftarkan dengan
    `RegisterCommand(nameof(...))`. Tombol Cancel (`textDangerButtonStyle`) + chip progres di toolbar, tampil saat
    `IsUploading`. Selama upload `InWaiting` tetap `false`.
  - Batal → hentikan file berjalan dan sisa antrean, reload folder, tanpa dialog error (pembatalan bukan galat).
  - Batas ukuran (`MaxFileSize`) tetap disaring di client sebelum mengirim.

### 4. Dokumentasi

- `src/backend/CLAUDE.md`: bagian **Request binding**, tambah subbagian parameter `Stream` (aturan signature, header
  `Em-X-StreamPayload` Base64Url JSON ≤ 4 KB, body mentah, batas body dilepas setelah gerbang, Expect-100, action
  wajib menegakkan batas ukurannya sendiri). Bagian **CDN**: upload lewat stream, hapus kalimat tentang penaikan
  `MaxRequestBodySize` Kestrel.
- `src/frontend/CLAUDE.md`: satu kalimat tentang `PostStreamAsync` (tanpa timeout, Expect-100, stream yang bisa di-seek
  untuk retry 401).
- XML doc Bahasa Indonesia untuk semua member `public` baru di `src/shared`. Jangan sebut nama objek database.

## Langkah eksekusi

1. **Issue lama.** Build `src/frontend/Em.Ui.Wpf.slnx`, lalu uji drag-out satu file dan satu folder dari CDN Manager ke
   Explorer (lihat Verifikasi 6) dan pindah internal (seret file ke baris folder). Kalau lolos, lanjut. Kalau gagal, perbaiki dulu dan catat di laporan.
2. Shared: `Defaults`, `StreamPayloadProtocol`, `CdnUploadRequest`, `ICdnServices`.
3. Backend: `ActionDefinition`, `RegisterActions`, binding stream di `ProcessRequest`, `CdnStore`, `CdnServices`,
   hapus penaikan Kestrel. `dotnet build src/backend/Em.Api.slnx` sampai bersih.
4. Client: `ApiClient`, `ServiceUiBase`, `CdnService`, `CdnManager`. `dotnet build src/frontend/Em.Ui.Wpf.slnx`
   sampai bersih; coba `dotnet build src/frontend/Em.Ui.Maui.slnx`.
5. `.gitignore`: tambahkan `src/backend/Em.Api/data/`.
6. Dokumentasi (bagian 4).
7. Verifikasi (di bawah).
8. Pindahkan plan ini ke `plan/executed/` + tulis bagian "Laporan eksekusi", lalu commit (keputusan 12).

## Verifikasi

Siapkan API: kalau `localhost:5132` sudah dipakai proses user, jalankan API uji di port lain
(`dotnet run --project src/backend/Em.Api/Em.Api.csproj --no-build --urls http://localhost:5199`) untuk uji curl.
Uji UI (WPF debug hard-coded ke `localhost:5132`) hanya bisa dijalankan kalau port itu kosong. Kalau tidak, catat
sebagai belum diuji. Untuk uji file besar, **sementara** ubah `Program.cs` ke `builder.EnableCdn("./data/cdn", 500);`,
lalu kembalikan ke `builder.EnableCdn("./data/cdn");` sebelum commit.

**Token debug untuk curl**: buat console kecil di scratchpad yang mereferensi `src/shared/Em.Libs/Em.Libs.csproj`.
Private key = nilai `DevelopmentToken` di `src/frontend/Em.Ui.Wpf/Properties/Resources.resx`. Susun
`RsaKeyPair(publicKeyDariPrivateKey, privateKey)`, lalu `DebugTokenProtocol.Create("Development Token", pair, DateTime.UtcNow)`,
dan kirim di header `X-Em-Debug-Token`. Module di URL: `api/Administrative%20Tools/...`.

1. **Startup**: API start normal (validasi signature tidak menolak `CdnServices`).
2. **Upload besar**: file acak 150 MB, `curl -H "Em-X-StreamPayload: <base64url json>" --data-binary @file` →
   200, hash file di `data/cdn` sama, dan working set proses API tidak naik sebesar ukuran file.
3. **Penolakan tanpa kirim body** (pakai `-H "Expect: 100-continue"` dan file 150 MB, ukur `time_total`/byte terkirim):
   tanpa token → 401 cepat; file sudah ada tanpa overwrite → 409 cepat; nama `.x` → 400.
4. **Batas ukuran**: file > `maxFileSizeMb` → 413; tidak tersisa `.upload-*.tmp` di folder.
5. **Header**: > 4 KB → 400; Base64 rusak → 400; header tidak ada → 400.
6. **Batal di tengah**: `curl --max-time 1` pada file besar → tidak tersisa file sementara maupun file tujuan.
7. **UI** (kalau 5132 bisa dipakai): upload 150 MB dari CDN Manager, progres persen bergerak, Cancel menghentikan dan
   tidak meninggalkan file; konflik → dialog timpa → Yes berhasil; drop folder dari Explorer tetap jalan.
   Otomasi UI yang terbukti jalan di mesin ini: PowerShell + `user32` `SetCursorPos`/`mouse_event` untuk klik dan drag
   bertahap (±30 langkah), `MoveWindow` untuk menata aplikasi di kiri (0,0,1100,1040) dan Explorer di kanan, serta
   UI Automation (`AutomationElement.RootElement` → nama jendela `"<folder> - File Explorer"`). Panggil
   `SetForegroundWindow` sebelum klik pertama, karena klik pertama ke jendela yang tidak aktif sering hanya
   mengaktifkannya.
8. **Issue lama di debugger**: tidak bisa dijalankan dari sini. Tulis di laporan akhir bahwa user perlu menguji
   drag-out di bawah debugger IDE-nya.

Bersihkan semua file uji buatan sendiri (di `data/cdn`, scratchpad, folder tujuan drag). `428.webp` tetap.

## Di luar cakupan

- Upload stream untuk klien MAUI (layar CDN tidak ada di MAUI).
- Resume upload yang terputus, dan beberapa stream per action.
- Parameter stream di action GET atau respons berupa stream (download tetap lewat `/cdn/...` publik).

## Laporan eksekusi (2026-09-26)

Semua langkah dijalankan. `Em.Api.slnx`, `Em.Ui.Wpf.slnx`, dan `Em.Ui.Maui.slnx` build tanpa error dan
tanpa warning.

### Hasil verifikasi

API uji dijalankan di `localhost:5132` (port itu kosong, jadi uji curl dan uji UI memakai API yang sama), dengan
`EnableCdn("./data/cdn", 500)` sementara.

1. **Startup**: normal; `CdnServices` lolos validasi signature.
2. **Upload besar**: file acak 150 MB → 200 dalam 0,43 detik, hash SHA-256 sama. Working set proses API
   sesudahnya 100 MB, dengan puncak juga 100 MB, jadi file tidak ditampung di memori.
3. **Penolakan tanpa body** (`Expect: 100-continue`, file 150 MB): tanpa token → 401, file sudah ada → 409,
   nama `.x` → 400. Ketiganya `size_upload = 0` dan selesai di bawah 10 ms. Dengan `Overwrite: true` → 200, file
   terkirim penuh.
4. **Batas ukuran**: file 520 MB (batas 500 MB) → 413. Tidak ada `.upload-*.tmp` tersisa.
5. **Header**: > 4 KB → 400, Base64 rusak → 400, JSON rusak → 400, header tidak ada → 400, JSON `null` → 400.
6. **Batal di tengah** (`curl --limit-rate 20M --max-time 2`): sekitar 42 MB terkirim, server mencatat
   `ABORT`, dan tidak ada file sementara maupun file tujuan yang tersisa.
7. **UI**: antrean 3 × 450 MB lewat dialog Upload memperlihatkan persen dan byte yang bergerak per file
   (1 of 3 → 3 of 3), lalu folder dimuat ulang. Cancel di tengah file pertama menghentikan file itu dan sisa
   antrean: tidak ada file tersisa dan tidak ada dialog error. Konflik memunculkan dialog timpa, dan Yes mengirim
   ulang dengan progres sampai berhasil. Drop folder dari Explorer (berisi subfolder) berhasil dengan hash cocok.
8. **Issue lama (langkah 1)**: di luar debugger, drag-out satu file (`428.webp`) dan satu folder (beserta
   subfolder) dari CDN Manager ke Explorer berhasil, dengan hash cocok dan tanpa exception. Pindah internal
   (seret file ke baris folder) juga berhasil. Langkah 1 digabung dengan uji UI di akhir supaya aplikasi cukup
   dibangun dan dijalankan sekali, dan tidak ada perbaikan tambahan yang diperlukan. **Uji di bawah debugger IDE
   belum dijalankan**, jadi user perlu menguji drag-out di bawah debugger-nya sendiri.

Semua file uji (di `data/cdn`, scratchpad, folder tujuan drag) sudah dihapus. `428.webp` tetap ada.

### Keputusan yang diambil saat eksekusi

- **Header tidak ada untuk parameter reference type.** Keputusan turunan menyebut "null kalau nullable/reference",
  sedangkan Verifikasi 5 mengharapkan 400 untuk header yang tidak ada pada `CdnUploadRequest request`. Keduanya
  disatukan lewat `NullabilityInfoContext`: `null` hanya diberikan kalau parameternya `Nullable<T>` atau reference
  type yang dideklarasikan `?`. Reference type tanpa `?` wajib ada (400). Aturan yang sama berlaku untuk JSON
  `null` di header.
- `IApiClient` ikut mendapat `PostStreamAsync` + `<T>`, sejajar dengan `PostAsync`.
- `RunBusyAsync` di CDN Manager mendapat parameter `showOverlay`. Upload memakai `IsBusy = true` dengan
  `InWaiting = false`. Pembuatan folder saat drop kini ditampilkan di chip progres, bukan di overlay.
- `CancelUploadCommand` adalah command sinkron (`void`) supaya bisa diklik saat command async lain sedang berjalan.
- Chip progres diberi `MinWidth="250"` supaya tombol Cancel tidak bergeser saat teksnya berubah. Style progress bar
  memasang `MinHeight = 0`, karena tanpa itu bar tampil setinggi sekitar 20 px walaupun `Height = 3`.
- `UploadProgressCaption` hanya memicu `RaiseCommandsChanged` saat berubah antara kosong dan terisi, bukan pada
  setiap update progres (sampai 10 kali per detik).
- **`Program.cs` tidak ikut commit.** Saat eksekusi dimulai, working copy user sudah berisi
  `EnableCdn("./data/cdn", 200)` (belum di-commit, tidak disebut di plan). Nilai 500 untuk uji sudah dikembalikan
  ke 200 milik user, dan file itu dibiarkan sebagai perubahan user.
- Belum ada action ber-stream tanpa parameter payload, jadi kasus "header dikirim ke action tanpa parameter
  payload → 400" hanya ditinjau lewat kode, tidak diuji dengan curl.
