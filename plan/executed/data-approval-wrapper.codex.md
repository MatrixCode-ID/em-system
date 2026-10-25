# Data approval wrapper: dukungan engine untuk layar approval Data milik host

Status: **selesai dieksekusi 2026-10-25**. Semua pertanyaan plan sudah
dijawab pengguna (seksi 3.3).

Plan ini adalah bagian **engine** dari satu topik lintas repo. Bagian produk pemakai (layar approval
Data milik modul di repo host privat) memakai plan pasangannya sendiri, yang menautkan ke berkas ini.
Seluruh isi plan ini generik: tanpa nama modul bisnis, produk, server, atau database dari repo host.
Plan ini melanjutkan [approval-wrapper.codex.md](../executed/approval-wrapper.codex.md), yang membuat
kontrak wrapper untuk System Approval.

## 1. Bacaan wajib sebelum mulai

- `CLAUDE.md` root em-system (versi terbaru di disk) dan `CLAUDE.md` subfolder pada jalur berkas yang
  disentuh.
- `doc/engine/engine-approval.md`, terutama "Data Approval", "Approval wrappers", dan batch System.
- `plan/executed/approval-wrapper.codex.md` (pola batch System dan wrapper yang sudah berjalan).
- Konvensi nama di `doc/convention/`.

## 2. Aturan yang tidak tertulis di CLAUDE.md (wajib diikuti Codex)

- Bahasa: plan, laporan, dan percakapan Bahasa Indonesia; kode, komentar, XML doc, teks UI, pesan
  error, dan guide `doc/engine/` bahasa Inggris.
- Nama action non-tabel: `PostGetMeta_{Name}`, `PostMeta_{Name}`, `GetMeta_{Name}`, di region `Meta's`
  di bawah `Views`. Setiap method action wajib `[GetAction]`/`[PostAction]`; tanpa atribut action
  menjadi 404 tanpa error kompilasi. Objek/array hanya lewat POST.
- Akun bawaan admin dan debugger tidak bisa submit/decide/withdraw. User nyata dengan switch
  administrator boleh semua. Cek hak di client memakai `IsDebugBypass`.
- Backend menerima dan memberi data; tidak menambah whitelist kolom bisnis.
- Uji di lingkungan lokal/dev; data uji tidak dibuang. Uji visual hanya atas konfirmasi pengguna.
- Harness di `..\.artefacts\em-system\scripts\data-approval-wrapper-smoke\`, tidak membuat berkas di repo.

## 3. Keputusan

### 3.1 Keputusan pengguna (2026-10-25)

1. Host boleh membungkus **Data Approval** dengan layar miliknya sendiri, sama seperti System Approval.
   Engine diperiksa dan dilengkapi agar mendukungnya.
2. Konsep Data Approval **tidak berubah**: perubahan yang diajukan user tanpa hak decide tetap menjadi
   request pending; pemegang hak decide tetap menyimpan langsung (request otomatis disetujui).

### 3.2 Hasil pemeriksaan engine (2026-10-25) dan keputusan turunan agent

| Bagian | Keadaan sekarang | Tindakan |
| --- | --- | --- |
| `AddApprovalWrapper(kind, name, navigation)` | Tidak membatasi jenis; katalog berkunci `(Kind, Name)` | Tidak diubah. Tambah tes registrasi wrapper `Data` |
| MY TASKS | `ApprovalHubTaskSource` mengirim `Data:{Name}`; `ApprovalWrapperCatalog.Resolve` sudah menemukan wrapper Data | Tidak diubah. Tambah tes resolve Data, termasuk fallback ke `em.approval.editor` |
| Viewer `em.approval.editor` | Punya mode Data (`IsData`: field usulan, konflik, override) dan dipanggil dengan `SystemViewer = true` | Verifikasi request Data lewat payload wrapper (console). Bila ada cabang yang hanya System, perbaiki secara generik |
| Daftar (`PostGetMeta_ApprovalRequests`) | Berlaku untuk Data; summary Data = judul definisi, tanpa sub-summary | Lihat 3.3-2 |
| Riwayat per subject (`PostGetMeta_ApprovalRequestsBySubject`) | Dipakai host untuk Editor per subject (permintaan sekarang + history) | Verifikasi: mengembalikan semua stage, dan request Create yang sudah di-approve ikut tercatat dengan subject key root hasil `CreateKeyAsync`. Bila subject key Create tetap kosong setelah apply, isi secara generik saat apply (tanpa mengubah skema) |
| Batch | `PostGetMeta_ApprovalSystemSignAll` / `VoidAll` **hanya System** | Tambah batch Data (3.2-1) |
| Withdraw | `PostMeta_ApprovalWithdraw` satuan | Ikut batch Data (3.2-1) |

1. **Batch Data**: action baru `PostGetMeta_ApprovalDataDecideAll(ApprovalDataBatchRequest)` yang
   mengembalikan `ApprovalBatchResult` yang sudah ada.
   - `ApprovalDataBatchRequest(string Name, string[] RequestIds, ApprovalDataBatchAction Action, string? Note)`;
     `Action` = `Approve`, `Reject`, `Withdraw`.
   - Hanya ID eksplisit (wajib, maksimal 200, memakai `BatchIds`). Tidak ada discovery `RequestIds = null`.
   - Setiap request diproses dengan jalur yang sama dengan keputusan satuan (`PostGetMeta_ApprovalDecide`
     / `PostMeta_ApprovalWithdraw`): cek `CanDecide`/`CanWithdraw` root, kind Data, stage Pending, nama
     cocok. **Satu transaksi per request**, sehingga satu kegagalan tidak membatalkan yang lain.
   - Approve yang menemukan **konflik** tidak di-override: request dilewati dengan pesan konflik dan
     nilai sekarang direkam seperti keputusan satuan. Override hanya lewat viewer satuan. Baris yang
     hilang (409) juga dilewati dan dilaporkan.
   - Note: Approve opsional; Reject dan Withdraw wajib alasan sesuai aturan keputusan satuan yang
     berlaku (tidak menambah aturan panjang baru, keputusan 3.3-1).
   - Capability: `ApprovalCapability.AllowBatchSign` dipakai juga untuk Data (definisi root boleh
     menolak batch dengan `false`); default `true`.
2. Klien `IApprovalServices` di `Em.Ui.Core` (WPF/MAUI) mendapat method yang sama.
3. Guide `doc/engine/engine-approval.md`: bagian "Approval wrappers" menjelaskan wrapper Data
   (registrasi, viewer, MY TASKS, batch Data, batas konflik).

### 3.3 Jawaban pengguna atas pertanyaan plan (2026-10-25)

1. Alasan Reject/Withdraw pada batch Data **ikut aturan keputusan satuan** yang ada; tidak ada aturan
   panjang baru.
2. **Ringkasan request Data tidak diubah.** Engine tidak menambah hook ringkasan; kolom bisnis disuplai
   host lewat action modulnya sendiri, seperti wrapper System.
3. **Withdraw ikut batch** (Approve, Reject, Withdraw).
4. Host menyediakan **Approve all** dari daftarnya: host mengirim ID eksplisit dari daftar yang sedang
   dimuat (per 200). Engine **tidak** menambah discovery `RequestIds = null` untuk Data, sehingga yang
   disetujui hanya request yang sudah dilihat user.

## 4. Langkah kerja

1. DTO `ApprovalDataBatchRequest`, enum `ApprovalDataBatchAction` di `Em.Libs` (`ApprovalDtos.cs`),
   method `PostGetMeta_ApprovalDataDecideAll` di `IApprovalServices`.
2. `ApprovalEngine.Batch.cs`: `DataDecideAllAsync` memakai ulang jalur keputusan Data satuan, satu
   transaksi per request, hasil per item ke `ApprovalBatchItem`.
3. Service backend dan client `Em.Ui.Core` (WPF/MAUI memakai client yang sama).
4. Perbaikan generik viewer bila verifikasi 3.2 menemukan cabang khusus System.
5. Guide engine (3.2-3) dan release note candidate sesuai aturan em-system.
6. Tes: unit/integrasi SQL Server untuk batch Data (approve, konflik dilewati, baris hilang, reject
   tanpa alasan ditolak, withdraw oleh requester, user tanpa `CanDecide` → dilewati/403, akun sistem
   ditolak, batas 200, `AllowBatchSign = false`), tes katalog wrapper Data dan resolve MY TASKS.
   Smoke HTTP lewat `Em.Test` dengan definisi Data uji.

Urutan eksekusi mengikuti plan host pasangan: kode engine lebih dulu, lalu kode host; build, tes, dan
review setelah kode keduanya selesai. Commit di `work-bench` di akhir eksekusi; push manual pengguna.

## 5. Penanganan kegagalan

- Jalur keputusan satuan sulit dipakai ulang tanpa refactor besar: ekstrak method privat bersama,
  tanpa mengubah perilaku satuan; tes satuan yang ada harus tetap lulus.
- Penolakan policy: skrip manual di `plan/data-approval-wrapper-manual/` sesuai workflow.

## Mockup UI

Tidak ada UI baru di engine. Viewer `em.approval.editor` yang sudah ada dipakai apa adanya.

## Hasil eksekusi

Tanggal: 2026-10-25 (Asia/Jakarta). Eksekusi disetujui bersama plan host pasangannya; kode engine
selesai lebih dulu, lalu seluruh kode host, baru build, tes, review dan perbaikan. Branch `work-bench`.

### Implementasi

- Kontrak `ApprovalDataBatchAction` (Approve/Reject/Withdraw), `ApprovalDataBatchRequest` dan action
  `IApprovalServices.PostGetMeta_ApprovalDataDecideAll` ditambahkan. Proxy aktual berada pada service
  WPF dan MAUI; kedua proxy disesuaikan, tanpa menciptakan service client paralel di Ui.Core.
- `DataDecideAllAsync` memakai `BatchIds`, membatasi 1–200 ID eksplisit, tanpa discovery. Kind Data,
  nama, Pending stage, hak decide/withdraw dan user nyata diperiksa; keputusan tetap memakai
  `DecideAsync`/`WithdrawAsync` satuan. Satu transaksi per request dan hasil parsial per ID.
- Konflik dicatat melalui jalur keputusan satuan dan dilewati tanpa override; entity hilang dilaporkan.
  Alasan mengikuti aturan satuan: Approve opsional maksimal 1000, Reject 1–1000, Withdraw 1–990;
  whitespace-only ditolak. Tidak menambahkan panjang minimum baru.
- `IEmApprovalData<TEntity>.AllowBatchSign` default true disalurkan ke capability; false menolak batch.
- Review menemukan kemungkinan entity tracked lama dipakai ulang oleh `FindAsync`. Context core dan
  module dibersihkan sebelum dan sesudah setiap request batch; tes regresi memastikan perubahan
  concurrent tetap terdeteksi, termasuk bila entity sudah dibaca dalam scope yang sama.
- Registrasi wrapper Data, resolve MY TASKS dan fallback viewer sudah mendukung Data. Tes katalog
  ditambahkan; tidak perlu perubahan viewer. Payload Kind Data/SystemViewer dipakai oleh host dan
  diperiksa console. Detail Data tetap memuat changes/konflik/komentar/lampiran sesuai haknya.
- `BySubjectAsync` sudah mengembalikan semua stage tanpa paging; `ApplyDataAsync` sudah mengisi key
  root hasil Create. Keduanya diverifikasi SQL/HTTP dan tidak diubah. Summary Data tidak diubah.
- Guide `doc/engine/engine-approval.md` ditambah wrapper Data, batch, batas konflik dan history Create.
  Candidate `0.1.0-alpha.18` ditulis untuk lima paket dan Version diselaraskan. Tidak pack/publish.
- Tidak ada perubahan skema database aplikasi, claim baru atau hook ringkasan bisnis.

### Build dan tes melalui RunBay

| Action / target | Hasil | Run ID |
| --- | --- | --- |
| `build`, backend solution | Lulus, 0 error / 2 warning xUnit existing | `20261025-185254-build-c666` |
| `build-frontend-approval`, WPF solution | Lulus, 0 error / 11 warning existing | `20261025-185332-build-frontend-approval-1c46` |
| `test-data-approval-rebuild`, perbaikan approval + integration project | Lulus, 0 error / 0 warning | `20261025-190621-test-data-approval-rebuild-2c1b` |
| `build-data-approval-maui`, MAUI Core/proxy | Lulus, 0 error / 2 warning binding existing | `20261025-190917-build-data-approval-maui-83b0` |
| `test-data-approval`, filter `-class '*Approval*'` | 50 lulus / 0 gagal / 0 skip | `20261025-190717-test-data-approval-db9e` |
| `test-data-approval-http`, Em.Test | 14/14 lulus | `20261025-191016-test-data-approval-http-cf2a` |

Suite terfilter: API unit 20, integrasi SQL Server 9, UI core 2, WPF 19. Tambahan tes mencakup
approve, konflik tanpa override dan current values, entity hilang, sukses setelah kegagalan,
reject/withdraw reason, withdraw requester, tanpa CanDecide, akun sistem admin/debugger, batas ID,
null/kosong, nama tidak cocok, stage selesai, AllowBatchSign false, reload entity tracked,
wrapper Data case-insensitive/kind-specific, fallback viewer dan key/history Create.

Smoke HTTP membuat database SQL Server lokal terisolasi dengan Em.Test, user requester dan reviewer
nyata; memeriksa Pending, approve, generated root key/history, konflik dan partial success, reject,
withdraw, 200 limit, penolakan debugger dan resolve wrapper Data. Database smoke dipertahankan;
nama target terakhir dicatat di `out/database.txt`. Database sementara suite integrasi mengikuti
lifecycle fixture suite yang sudah ada. Hanya proses host yang dimulai harness yang dihentikan.

Kegagalan awal runner diperbaiki tanpa mengubah perilaku produk: filter standalone xUnit memakai
`-class`, resource SQL untuk tes ditempatkan pada output terisolasi, dan output client HTTP dipisah
agar tidak menimpa versi dependency milik host. Harness mempunyai warning dependency transitive
PKCS existing; build library/host tidak menambah warning baru pada perubahan approval ini.
Solution dibangun sekali masing-masing; sesudah review hanya project terkait yang dibangun kembali.
Suite lain di luar approval dan bukti wrapper lama tidak dijalankan ulang.

### Artefak, batasan dan penutupan

Source smoke dan output berada di `../.artefacts/em-system/scripts/data-approval-wrapper-smoke/`,
output/log pada `out/`. Script pelaksanaan `.ps1` disimpan sebagai action pada app RunBay
`em-system`, sesuai keputusan pengguna yang dicatat di root CLAUDE.md. Konfigurasi runner dimiliki
workspace host; tidak menyalin konfigurasi privat ke repo engine publik.

Tidak ada UI engine baru; visual/GUI tidak dijalankan sesuai instruksi pengguna. Host memverifikasi
wrapper Data dengan console dan HTTP. Uji visual layar host tetap tertunda pada laporan plan host.
Tidak ada penolakan policy atau skrip manual tertunda. Kuota awal tersisa 50%/92%; pemeriksaan
terakhir sebelum penutupan 28%/89% (5 jam/mingguan), tidak mencapai batas berhenti dan tanpa reset.

Plan dipindah ke `plan/executed/` dan perubahan di-commit lokal di `work-bench`, tanpa push,
deploy atau publish. Commit pelaksanaan adalah commit yang menambahkan plan executed ini.
