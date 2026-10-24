# Approval wrapper: data untuk layar approval milik modul, Approval Manager khusus admin, sign all dan void all

Status: **dieksekusi 2026-10-24** atas perintah pengguna. Build/tes/HTTP lulus; uji visual tertunda.

Plan ini adalah bagian **engine** dari satu topik lintas repo. Bagian produk pemakai (layar approval
milik modul di repo host privat) memakai plan pasangannya sendiri; plan itu menautkan ke berkas ini.
Seluruh isi plan ini generik: tanpa nama modul bisnis, produk, server, atau database dari repo host.

## 1. Bacaan wajib sebelum mulai

- `CLAUDE.md` root em-system (versi terbaru di disk), terutama "Rancang ulang approval — selesai
  (2026-10-17)", aturan action GET per field, komentar kode bahasa Inggris, uji visual atas konfirmasi,
  lokasi harness di `..\.artefacts\em-system\scripts\`, commit hanya di `work-bench`, push manual.
- `CLAUDE.md` subfolder pada jalur berkas yang disentuh.
- `doc/engine/engine-approval.md` (kontrak approval yang berlaku).
- Konvensi nama di `doc/convention/`.

## 2. Aturan yang tidak tertulis di CLAUDE.md (wajib diikuti Codex)

- Bahasa: plan, laporan, dan percakapan Bahasa Indonesia; kode, komentar, XML doc, teks UI, pesan
  error, dan guide `doc/engine/` bahasa Inggris.
- Nama action non-tabel: `PostGetMeta_{Name}` (POST yang mengembalikan nilai), `PostMeta_{Name}`,
  `GetMeta_{Name}`; letakkan di region `Meta's` di bawah `Views`. Setiap method action wajib
  `[GetAction]`/`[PostAction]`; tanpa atribut action menjadi 404 tanpa error kompilasi.
- GET hanya parameter sederhana; objek/array lewat POST.
- Akun bawaan admin dan debugger tidak bisa sign/void/submit (signature butuh user persisted aktif).
  User nyata dengan switch administrator (`cUserIsAdmin`) boleh semua. Cek hak di client memakai
  `IsDebugBypass`, bukan `IsDebugMode`/`IsDebugActive`.
- Backend menerima dan memberi data; tidak menambah whitelist kolom bisnis. Aturan yang memang milik
  engine (hak sign/void, panjang alasan) tetap ditegakkan di server.
- MVVM WPF: VM turunan `MvvmModelBase`, properti binding `public` `Get<T>()/Set(value)`, command
  `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, XAML `Commands[XxxCommand]`,
  can-execute bukan lambda. Kode yang bisa berjalan di luar UI thread di-marshal.
- WPF dan MAUI core dicerminkan strukturnya; MAUI tetap tanpa void/reinstate (keputusan lama).
- Urutan eksekusi lintas repo (sama dengan rancang ulang approval 2026-10-17): **tulis kode plan
  engine ini lebih dulu, lalu kode plan produk pemakai**. Build, tes, review baru dijalankan setelah
  kode kedua plan selesai; jangan build/tes di antara keduanya.

## 3. Keputusan final

### 3.1 Keputusan pengguna (2026-10-24)

1. Aplikasi engine seperti approval harus bisa **dibungkus (wrapper) oleh host/modul**, agar beban
   Approval Manager terbagi dan pengguna memakai layar di modul yang mereka kenal.
2. **Wrapper hanya mengambil data dari engine; UI dibangun oleh wrapper.** Engine tidak menyediakan
   body layar untuk wrapper. Engine menyediakan data, aksi, dan komponen kecil yang sudah publik
   (mis. dialog keputusan) yang boleh dipakai ulang.
3. Klien: **WPF dan MAUI**.
4. **Tanpa claim baru** untuk wrapper: akses data mengikuti siapa saja yang punya tugas/hak lihat pada
   approval tersebut (aturan visibilitas server yang sudah ada). Menu wrapper **tampil untuk semua user**;
   layar kosong bila tidak ada data. Engine **tidak** menambah aturan visibilitas menu berbasis capability.
5. **Approval Manager umum hanya untuk admin** (`IsAdminUser`/`cUserIsAdmin`), sebagai alat memantau
   approval yang **nyangkut**: step yang sudah giliran signer tetapi lama tidak diputuskan.
   Status nyangkut **diputuskan manual oleh admin**; engine tidak memakai ambang waktu atau penanda
   otomatis. Umur tunggu boleh ditampilkan sebagai informasi.
6. **Pengaturan signer tidak dibungkus**; tetap di engine, hanya admin.
7. **Sign all** dan **void all** berlaku untuk **setiap user sesuai haknya**, bukan hanya admin.
   Contoh: user login bisa sign all semua tanda tangannya yang masih menggantung. Admin dapat
   melakukannya untuk semua request.
8. Daftar mendukung **memilih beberapa baris**: context menu untuk sign/void baris terpilih, serta
   tombol **Sign all** dan **Void all** di toolbar.
9. **Sign all** pada layar wrapper menandatangani **semua step milik user yang sedang giliran**
   pada approval wrapper tersebut, dihitung server, tidak terbatas halaman/rentang yang tampil.
10. Isian: **sign/sign all** memakai input description seperti sign satuan, **opsional**.
    **Void (semua void: satuan, void all, dan void saat reinstate)** wajib alasan **minimal 15 karakter**
    (setelah trim), ditegakkan di server dan UI.
11. Tugas MY TASKS untuk approval yang **punya wrapper** membuka navigasi wrapper. Untuk approval yang
    **belum punya wrapper**, tugas tetap tampil dan dibuka di **editor approval engine**
    (`em.approval.editor`), bukan Approval Manager umum.
12. Satu plan topik untuk perubahan engine dan wrapper pertama; commit di akhir eksekusi (em-system di
    `work-bench`), push menunggu perintah pengguna.

### 3.2 Keputusan turunan agent (koreksi saat review plan bila tidak sesuai)

1. **Registrasi wrapper di client**, bukan di server: `builder.AddApprovalWrapper(ApprovalKind kind,
   string name, string navigationName)` pada `EmAppBuilder` WPF dan MAUI. Server
   (`ApprovalHubTaskSource`) tetap mengirim `Kind:Name`; client yang memetakan ke navigasi wrapper.
   Alasan: navigasi adalah konsep client dan server tidak mengenal nama navigasi modul.
2. Payload ke wrapper: `ApprovalWrapperNavigationPayload` (di `Em.Ui.Core`) berisi `Kind`, `Name`,
   `WaitingForMeOnly`. Wrapper bebas membacanya.
3. MY TASKS WPF disamakan dengan MAUI: `CanOpen` tidak lagi terkunci pada Approval Manager; baris
   approval dibuka lewat resolver bersama (wrapper → navigasi wrapper; tanpa wrapper → editor engine).
   Baris dari sumber lain tetap memakai `NavigationTarget`/`NavigationParameter` apa adanya, dengan
   cek `CanOpen` pada navigasi tujuan.
4. **Tanpa wrapper** → `em.approval.editor` menerima payload filter tanpa `ApprovalRequestId`
   (`Kind`, `Name`, `WaitingForMeOnly = true`): editor memuat request menunggu yang paling lama, dan
   Next/Previous berjalan di dalam filter itu. Bila tidak ada lagi, editor menampilkan pesan kosong.
5. **Approval Manager admin-only** hanya untuk layar manager penuh (Tools, akun panel/inbox MAUI,
   navigasi langsung tanpa `Compact`). Tampilan **Compact** yang disematkan modul (review Data per
   subject) tetap untuk semua user yang berhak lihat. `CanOpenManager` = `IsDebugBypass ||
   cUserIsAdmin`. Navigasi langsung non-admin ke manager penuh menampilkan pesan "Approval Manager is
   available to administrators" tanpa memuat data. MAUI: tombol inbox untuk non-admin membuka home
   (MY TASKS), bukan manager.
6. Hak lihat/putus di server **tidak diubah**; admin-only adalah aturan UI. Data untuk wrapper tetap
   memakai `PostGetMeta_ApprovalRequests` dkk. dengan pemeriksaan visibilitas yang sudah ada.
7. **Sign all/void all hanya untuk System Approval.** Data Approval tidak ikut karena keputusannya
   butuh pemeriksaan konflik per request; void memang hanya ada di System.
8. Aksi baru (server, `core.approval`):
   - `PostGetMeta_ApprovalSystemSignAll(ApprovalSignAllRequest)` → `ApprovalBatchResult`.
     `ApprovalSignAllRequest(string? Name, string[]? RequestIds, ApprovalSignAllScope Scope, string? Note)`.
     `Scope.Mine`: step aktif di mana caller kandidat (primary atau backup); `RequestIds = null` berarti
     semua request menunggu caller pada `Name`. `Scope.AllActiveSteps`: hanya admin; menandatangani
     semua step aktif pada `RequestIds` (wajib diisi) dengan tanda admin. Satu putaran: step pada level
     berikutnya yang terbuka setelah itu tidak ikut ditandatangani.
   - `PostGetMeta_ApprovalSystemVoidAll(ApprovalVoidAllRequest)` → `ApprovalBatchResult`.
     `ApprovalVoidAllRequest(string[] RequestIds, string Reason)`; hanya request yang boleh di-void
     caller (requester, admin, atau pemegang `VoidClaims`).
   - `ApprovalBatchResult(ApprovalBatchItem[] Items)`, `ApprovalBatchItem(string RequestId,
     string? StepId, bool Success, string? Message)`.
   - Setiap request diproses dalam **transaksinya sendiri** (sukses sebagian diperbolehkan); handler
     completion/rejection berjalan setelah commit seperti keputusan satuan. Batas 200 request per
     panggilan; lebih dari itu 400.
9. Validasi client modul (`ValidateSignAsync`) tidak dijalankan pada sign all. Definisi System boleh
   menolak sign all lewat properti default-interface `bool AllowBatchSign => true` pada
   `IEmApprovalSystem`; bila `false`, server melewati request itu dengan pesan dan UI menyembunyikan
   aksinya untuk definisi tersebut (dikirim lewat `ApprovalCapability`).
10. Alasan void minimal 15 karakter: konstanta engine `ApprovalRules.MinVoidReasonLength = 15`
    (di `Em.Libs`, dipakai server dan client), diterapkan di `VoidAsync`, void all, dan reinstate.
11. Data untuk layar Master Doc wrapper: `ApprovalQuery` mendapat `DateFrom`/`DateTo` (tanggal request,
    inklusif kedua ujung) sebagai parameter opsional di akhir record (tidak memutus pemanggil lama), dan
    action baru `PostGetMeta_ApprovalYearMonths(ApprovalQuery filter)` → `ApprovalYearMonth[]`
    (`Year`, `Month`, `Count`) memakai visibilitas yang sama. POST karena filter berupa objek.
12. Approval Manager admin juga mendapat multi-select, context menu Sign/Void terpilih, dan tombol Sign
    all/Void all (`Scope.AllActiveSteps` untuk admin), dengan dialog alasan/description yang sama.
    Kolom umur tunggu ("Waiting") ditambahkan pada tabel manager sebagai informasi.
13. Tidak ada perubahan skema tabel. Bila ternyata diperlukan, berhenti pada bagian itu, catat di
    laporan, dan lanjutkan bagian lain (lihat seksi 5).

## 4. Langkah kerja

### Langkah 1 — Kontrak `Em.Libs`

- `ApprovalDtos.cs`: `ApprovalQuery` + `DateFrom`/`DateTo`; `ApprovalYearMonth`; `ApprovalSignAllScope`;
  `ApprovalSignAllRequest`; `ApprovalVoidAllRequest`; `ApprovalBatchItem`; `ApprovalBatchResult`;
  `ApprovalCapability` + `AllowBatchSign` (parameter baru di akhir dengan default).
- `ApprovalRules` (konstanta alasan void).
- `IEmApprovalSystem`: `bool AllowBatchSign => true`.
- `IApprovalServices`: `PostGetMeta_ApprovalYearMonths`, `PostGetMeta_ApprovalSystemSignAll`,
  `PostGetMeta_ApprovalSystemVoidAll`. XML doc bahasa Inggris.

### Langkah 2 — Server `Em.Api.Core`

- Filter tanggal di `ApprovalEngine.Read` (query daftar) dan grouping tahun/bulan dengan visibilitas yang sama.
- `SignAllAsync`/`VoidAllAsync` di `ApprovalEngine.System` memakai jalur sign/void satuan yang ada per
  request (bukan menyalin logika), transaksi per request, hasil per item.
- Panjang alasan void di `VoidAsync`, reinstate, dan void all.
- Service action di `ApprovalServices` (region `Meta's`), capability `AllowBatchSign`.

### Langkah 3 — Client bersama `Em.Ui.Core`

- `ApprovalWrapperNavigationPayload`; katalog wrapper `ApprovalWrapperCatalog` (kind+name → navigasi);
  resolver MY TASKS bersama yang dipakai WPF dan MAUI.

### Langkah 4 — WPF `Em.Ui.Wpf.Core`

- `EmAppBuilder.AddApprovalWrapper(...)`.
- `TabbedMainWindow`: `CanOpen`/`OpenHubTaskCommand` lewat resolver (keputusan 3.2-3/4).
- `ApprovalAccessCatalog.CanOpen`/`CanOpenManager` admin-only (3.2-5); `EmApp` Tools mengikuti.
- `ApprovalEditor`/`ApprovalManagerVm`: payload filter tanpa request ID untuk editor (3.2-4).
- Approval Manager: multi-select, context menu, Sign all/Void all, kolom Waiting (3.2-12).
- Dialog void/decision: validasi minimal 15 karakter untuk void; description opsional untuk sign.
- Proxy client `ApprovalService` untuk action baru.

### Langkah 5 — MAUI `Em.Ui.Maui.Core`

- `EmAppBuilder.AddApprovalWrapper(...)`, `EmApp.OpenHubTaskAsync` lewat resolver.
- Approval Manager admin-only; inbox non-admin ke home.
- Viewer menerima payload filter tanpa request ID (3.2-4).
- Proxy `ApprovalService` untuk action baru. Tanpa void di mobile (keputusan lama); sign all boleh
  dipakai wrapper MAUI lewat proxy.

### Langkah 6 — Module uji, test, guide, CLAUDE.md

- `Em.Test`: daftarkan satu wrapper contoh (navigasi uji sederhana yang memakai data engine) agar
  jalur MY TASKS → wrapper teruji tanpa produk host.
- Test backend: filter tanggal, year/month, sign all (Mine/AllActiveSteps, kandidat backup, admin,
  non-kandidat ditolak, `AllowBatchSign=false`), void all (hak, alasan < 15 ditolak), void/reinstate
  satuan dengan alasan pendek ditolak, batas 200.
- Test UI console: resolver MY TASKS (wrapper/tanpa wrapper/sumber lain), `CanOpenManager` admin-only,
  validasi dialog alasan.
- Guide `doc/engine/engine-approval.md`: bagian baru "Approval wrappers" (registrasi, payload, data
  yang tersedia, aturan sign all/void all, admin-only manager, MY TASKS), perbarui bagian MAUI dan
  admin. Perbarui bagian "Rancang ulang approval" di `CLAUDE.md` dengan keputusan 2026-10-24
  (menggantikan "Approval Manager tersedia untuk akun biasa sesuai capabilities").

### Langkah 7 — Setelah kode plan produk pemakai selesai

Build `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, `src/frontend/Em.Ui.Maui.slnx`;
`dotnet test` backend dan WPF; smoke HTTP sign all/void all di `..\.artefacts\em-system\scripts\approval-wrapper-smoke\`.
Uji visual layar manager yang berubah **tidak** dijalankan kecuali pengguna meminta; catat tertunda.

### Langkah 8 — Review, laporan, commit

Review diff, laporan eksekusi di plan ini (seksi "Hasil eksekusi"), pindahkan ke `plan/executed/`,
commit di `work-bench` (periksa branch dulu; bila bukan `work-bench`, jangan commit dan laporkan).
Tanpa push.

## 5. Penanganan kegagalan dan pilihan cadangan

- Bila action ditolak policy, ikuti aturan skrip manual `plan/approval-wrapper-manual/`.
- Bila perubahan `ApprovalQuery` memutus pemanggil (posisi parameter), tambahkan overload/record baru
  alih-alih mengganti nama (aturan overload, bukan rename).
- Bila payload filter tanpa request ID sulit pada editor yang ada, cadangan: resolver mengambil request
  menunggu tertua lewat `PostGetMeta_ApprovalRequests` lalu membuka editor dengan request ID itu; catat.
- Bila uji integrasi SQL Server lokal tidak terjangkau, test dilewati sesuai mekanisme yang ada; catat.

## Mockup UI

Tidak ada mockup. Perubahan pada layar engine yang sudah ada (Approval Manager, editor approval,
dialog keputusan, MY TASKS) tidak melalui mockup; ini mengikuti keputusan desain approval 2026-10-17
dan keputusan pengguna 2026-10-24. Tidak ada layar engine baru.

## Hasil eksekusi — 2026-10-24

Seluruh kode engine ditulis dahulu, kemudian kode aplikasi pemakai. Build, tes dan review dimulai
setelah kode keduanya lengkap; perbaikan hasil review dilakukan pada tahap akhir. Tidak ada perubahan
skema, rilis NuGet, deployment produksi atau push.

Implementasi mencakup kontrak filter tanggal/year-month/oldest-first, batch System sign/void,
AllowBatchSign dan konstanta alasan void 15 karakter. Satu kernel keputusan dipakai single/batch,
beberapa step dalam request berbagi transaksi; request berbeda independen. Target level dibekukan
sebelum mutasi, primary/backup diperiksa ulang saat lock, admin AllActiveSteps memerlukan ID eksplisit.
Mine tetap kandidat saja termasuk bagi admin; null IDs menemukan pekerjaan lintas tanggal/paging,
maksimum 200 request. Kapabilitas mengirim count kandidat dan flag batch. Semua void termasuk reinstate
memvalidasi alasan setelah trim. Keterangan step dan waktu tunggu adalah metadata baca tanpa DDL.

Catalog/payload/resolver wrapper bersama dipakai WPF/MAUI. AddApprovalWrapper mendaftarkan navigasi
modul tanpa menyediakan body UI. Hook IApprovalWrapperNavigationTarget menerapkan ulang filter pada
wrapper yang sudah terbuka. Tanpa wrapper, editor mengambil request tertua dalam filter waiting,
dengan Previous/Next dan pesan kosong. Hub sumber lain mempertahankan target/parameter serta cek CanOpen.
Manager penuh hanya admin/debugger bypass; Compact tetap mengikuti hak server. Pengaturan signer UI
admin-only. Mobile inbox non-admin ke home; mobile tetap tanpa void/reinstate.

Manager WPF memiliki multi-select/context action, batch semua hasil filter atau pilihan, dan Waiting.
Pilihan eksplisit lebih dari 200 dibagi per panggilan; definisi penolak batch menyembunyikan Sign all.
Akun sistem bawaan tidak dapat menjalankan batch mutasi. Em.Test menyediakan contoh wrapper UI sendiri.
Guide engine dan CLAUDE.md diperbarui.

### Verifikasi

- Build `Em.Api.slnx`, `Em.Ui.Wpf.slnx`, `Em.Ui.Maui.slnx` lulus. Build API akhir 0 warning/error;
  warning existing TestService/test analyzer WPF dan dua binding AccountPanel MAUI dicatat di log.
- Suite backend penuh **396 lulus, 8 skip, 0 gagal**. Skip provider log store tanpa konfigurasi.
  Tujuh regresi ApprovalRedesignTests terakhir **7/7 lulus** setelah perbaikan akhir.
- Suite frontend akhir **232 lulus, 0 gagal**, termasuk resolver, fallback tertua + pager,
  batas alasan trim, manager non-admin, dan larangan batch akun bawaan.
- HTTP Em.Api+Em.Test dengan database lokal terisolasi dan user nyata: **11/11 pemeriksaan lulus**,
  meliputi Mine, level baru tidak ikut, parallel steps, completion handler, year/month POST,
  alasan pendek 400 dan hasil void sebagian. Database fixture dipertahankan.
- Build dan verifikasi aplikasi pemakai dijalankan sesudah kode kedua plan lengkap; hasil rinci
  aplikasi dicatat pada plan pemiliknya, tanpa menyalin data privat ke repo ini.
- Log/harness: `../.artefacts/em-system/scripts/approval-wrapper-smoke/` dengan output `out/`.
  Tidak ada kredensial, output build, atau fixture sementara di repo.
- Review diff dan `git diff --check` selesai. Query year/month yang tidak diterjemahkan provider
  SQL diperbaiki dengan mengurutkan group key sebelum proyeksi record. Tes yang mengasumsikan satu
  context action disesuaikan dengan multi-select dan akses admin yang baru.

### Batas dan penutupan

Uji visual/interaksi layar WPF/MAUI tidak dijalankan, menunggu konfirmasi. Tidak ada ambang otomatis
untuk approval nyangkut. Discovery Mine di atas 200 request ditolak 400 sesuai kontrak; pemanggil
menerima pesan, tanpa loop otomatis yang ikut menandatangani level berikutnya. Validasi client modul
tidak dijalankan oleh batch, sesuai keputusan plan. Engine tidak mengubah link subject milik modul.

Plan dipindahkan ke `plan/executed/approval-wrapper.codex.md`; commit lokal di `work-bench` sesuai
izin plan. Tidak ada tindakan terblokir policy atau skrip pemasangan manual.
