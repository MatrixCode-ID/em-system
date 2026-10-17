# Rancang ulang engine approval: Data Approval, System Approval, stub Document Approval

- **Tanggal:** 2026-10-17
- **Status:** dalam eksekusi; build dan tes menunggu penulisan kode kedua plan selesai.
- **Eksekusi 2026-10-17:** diotorisasi pengguna; penulisan kode sedang berjalan.
  Sesuai revisi pengguna, build/tes/render/review menunggu kode plan engine dan produk
  selesai. Mockup editor dan viewer yang ditunjuk pengguna disetujui sebagai acuan UI;
  implementasi sementara sedang diselaraskan ke susunan dan interaksi tersebut.
- **Asal:** [doc/ideas/approval-rancang-ulang.md](../../doc/ideas/approval-rancang-ulang.md) (semua keputusan di sana
  per 2026-10-16 adalah dasar plan ini; nomor "D-n" = keputusan Data Approval n, "S-n" = keputusan System Approval n,
  "PT-n" = pertanyaan terbuka n yang sudah diputuskan)
- **Bagian produk turunan:** pemakai pertama (satu modul data master untuk Data Approval, satu modul dokumen
  penjualan untuk System Approval) punya plan sendiri dengan nama berkas yang sama di repo produk itu (privat). Plan
  ini **tidak** menyebut produk, modul bisnis, server, atau database produk tersebut (repo ini publik).
- **Urutan antar repo (revisi pengguna 2026-10-17):** kode engine lebih dulu, kemudian kode produk;
  build/tes/render/review kedua repo dilakukan setelah keduanya selesai ditulis.

## 1. Bacaan wajib sebelum mulai

- `CLAUDE.md` root em-system (versi terbaru di disk) dan `CLAUDE.md` subfolder pada jalur berkas yang disentuh.
- [doc/ideas/approval-rancang-ulang.md](../../doc/ideas/approval-rancang-ulang.md) lengkap: plan ini merangkum, catatan
  itu yang memuat alasan.
- [doc/convention/dahlia-convention.md](../../doc/convention/dahlia-convention.md) untuk nama tabel/kolom/view baru.
- `doc/engine/engine-approval.md` (perilaku lama yang diganti), `doc/engine/engine-object-storage.md` (attachment),
  `doc/engine/engine-test-module.md`, `tests/README.md`.
- Referensi gaya WPF: `src/shared/Em.Ui.Wpf.Core/Navigations/RoleManager.xaml`, `UserManager.xaml`,
  `UserEditor.xaml`, `Dialogs/DisplayExceptionData.xaml`.

## 2. Aturan yang tidak tertulis di CLAUDE.md (dari memori Claude, wajib diikuti Codex)

- Repo ini publik: kode, komentar, dokumen, dan nama contoh **tidak boleh** menyebut produk turunan, nama perusahaan,
  modul bisnis privat, alamat server, nama database produk, atau secret. Contoh di guide memakai domain netral
  (customer/address, document/order).
- Komentar kode, XML doc, teks UI, pesan exception/log: **bahasa Inggris**. Plan, catatan ide, laporan: Bahasa Indonesia.
- Nama action: tabel tunggal `GetTa_/PostTa_…`; selain itu `GetMeta_/PostMeta_/PostGetMeta_…` di region `Meta's`
  di bawah `Views`. **Setiap method action wajib `[GetAction]`/`[PostAction]`**; tanpa atribut menjadi 404 diam.
- GET hanya parameter sederhana per field; objek/array lewat POST (`PostGetMeta_…`).
- WPF MVVM: VM turunan `MvvmModelBase`, properti binding `public` dengan `Get<T>()/Set(value)` (properti `private`
  gagal saat runtime; jangan `Trim` di getter TwoWay), command lewat
  `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, XAML `Command="{Binding Commands[XxxCommand]}"`.
  Layar di `Navigations/`, `Controls/` hanya control primitif. Merge `Styles/MaterialDesign.xaml`, jangan salin palette.
- Konvensi enum `*State`/status: negatif = tidak dipakai/void, 0 ke atas = aktif.
- Akun sistem (admin bawaan, debugger) id-nya bukan ULID; mereka tidak boleh mengusulkan, memutuskan, menarik,
  me-void, atau menandatangani (FK ke tabel user). Switch **IsAdmin** pada user nyata = boleh semua (tanpa role
  Administrator).
- Service DI hanya didaftarkan lewat builder saat `BuildApp`.
- Bila butuh varian method, tambah overload dengan nama sama, jangan rename.
- Harness/smoke/render di `..\.artefacts\em-system\scripts\<nama>-smoke\` atau `<nama>-render\`, output folder `out`
  milik harness (pakai `..\_shared\HarnessPaths.cs`), tidak menulis apa pun ke repo; kredensial dari
  `..\.artefacts\em-system\config\`.
- Commit hanya di branch `work-bench`, pesan Bahasa Indonesia, **commit setelah setiap langkah selesai** tanpa
  menunggu diminta, **tanpa push**.

## 3. Keputusan final

### 3.1 Dari catatan ide (ringkas)

| Topik | Keputusan |
| --- | --- |
| Jenis | `ApprovalKind`: `Data`, `System`, `Document` (stub: registrasi/pemakaian melempar `NotImplementedException`). |
| Engine tanpa dokumen | Tabel jenis dokumen keluar dari skrip engine; FK request → tabel itu di-drop (tabelnya tidak di-drop). `docType` → `ApprovalName` (`cApprovalName varchar(50)`), `docKey` → `SubjectKey` (`cSubjectKey`), `docVersion` dihapus (PT-9). |
| Nama approval | Didefinisikan aplikasi lewat attribute `[Approval("…")]`, unik per pasangan (jenis, nama), maks 50 karakter, diawali huruf, isi huruf/angka/spasi tunggal, tanpa spasi awal/akhir. Diperiksa **Roslyn analyzer** (build host gagal) dan startup (lapis kedua). Registrasi eksplisit, tanpa scan. |
| Interface | Base penanda `IEmApproval`; Data: `IEmApprovalDataTable<T>` (struktur bersama) → `IEmApprovalData<T>` (induk: nama, judul, hak) dan `IEmApprovalDataChild<T, TParent>` (+`ForeignKeyColumns`); System: `IEmApprovalSystem<TKey>`; stub `IEmApprovalDocument…`. Satu method registrasi per jenis: `AddDataApproval<T>()`, `AddSystemApproval<TDef>()` / `AddSystemApproval<TDef, THandler>()`. |
| Claim | Engine tidak membuat claim untuk Data Approval; hak dari `CanView/CanDecide/CanWithdraw` di definisi induk. System Approval: claim `View {Nama}` dibuat otomatis (S-22). IsAdmin selalu lolos, diterapkan engine sebelum hook modul. |
| Data Approval | D-1…D-19: satu class per tabel, kolom lewat `nameof`, satu request = satu pohon dengan satu induk; Create disimpan JSON penuh di `json_object` item lalu di-insert saat approve (`CreateKeyAsync` hook, JSON di-null-kan setelah approve, tetap ada bila ditolak/batal); request Create tidak bisa diedit; Update old/new per kolom dengan selisih dihitung engine dari dua objek pada `TrackedColumns` dan pembandingan tiga nilai; `DeleteMode` None/Hard/Soft (+`DeletedStateValue`/`ActiveStateValue` untuk non-bool); delete induk tidak menyentuh anak; tanpa reinstate; operasi `Restore`; `ApproverOnlyColumns` hanya diteruskan ke UI; tanpa aturan kolom berpasangan; satu keputusan tanpa multi-step; baris tidak dikunci (PT-10). Tidak ada jalur handler manual untuk tabel non-kolom. |
| System Approval | S-1…S-29: definisi di library bersama, flow disusun **server** dari definisi; step bernomor level (berurutan) dengan step paralel dalam level; semua step diatur di editor penanda tangan engine dengan pola sama: tepat satu penanda tangan berupa user **atau DS (Dynamic Signer)**: satu item statis yang bisa dipakai di step mana pun, usernya ditentukan handler backend modul saat request dibuat (mis. pembuat request yang langsung ditandatangani, atau pemrakarsa dokumen), dengan guard yang menolak request bila DS tidak terisi (S-29), ditambah nol atau lebih **cadangan four-eyes** yang langsung boleh menandatangani dan ditandai khusus (S-28); cukup satu tanda tangan per step; penanda tangan di-snapshot; tidak ada alur usulan penggantian/claim ubah penanda tangan, penggantian request berjalan lewat reinstate atau IsAdmin; IsAdmin boleh sign step mana pun (ditandai khusus); satu key satu request aktif; reinstate membuat request baru, lama void `-2` (pending) / `-3` (selesai) dengan tautan; flow tidak bisa diedit saat berjalan; validasi Sign/Reject di editor modul; data bisnis disimpan modul; notifikasi hub wajib, handler selesai/ditolak opsional setelah keputusan tersimpan (gagal hanya di-log); tanpa `when:`/`guard`/`input`/`distinctFrom`/on-behalf/stempel PDF; penguncian subjek urusan modul; status dibaca modul lewat join. |
| Migrasi | Mulai bersih; kode verifikasi per tanda tangan dipertahankan; FK ke user dan transaksi lintas database tanpa MSDTC dipertahankan (PT-11). |
| Hub | Tetap per jenis (jumlah menunggu per (jenis, nama)); hub per item tidak dibuat (PT-12). |
| Ditunda | Eskalasi/batas waktu, kuorum, UI approval MAUI, email dari engine, Document Approval penuh. |

### 3.2 Keputusan pengguna 2026-10-17 (sesi penyusunan plan)

1. Plan untuk **Codex** (`.codex.md`), dipecah engine (repo ini) dan produk (repo produk).
2. **Tabel approval lama di-drop** (request, item, item key, item field, step, step signer, comment) oleh skrip
   `updates/`; tabel jenis dokumen tidak di-drop, hanya FK-nya.
3. **Kode Document approval lama dihapus seluruhnya**: `AddDocumentApproval`, deklarasi flow level/claim per step,
   `when:`, `guard`, `input`, `signers:` lama, substitute, `distinctFrom`, renderer dan stempel PDF
   (`IApprovalPdfRenderer`, `Pdf/ApprovalPdfRenderer.cs`, `PdfLayout`), `AddApprovalDocumentOpener`,
   `ApprovalPanelRegistry`/`IApprovalPanel*`, `EmAppBuilder.ApprovalViewClaimName`, registrasi claim otomatis Data.
   `ApprovalKind.Document` dipakai ulang untuk stub Document Approval baru.
4. Request lama tidak perlu dipertahankan (data lama hanya data uji).
5. **Void dari modul** untuk System Approval (dipakai modul saat dokumennya diedit ulang): status `-2` bila pending,
   `-3` bila sudah selesai; tanda tangan tetap sebagai histori; sesudahnya key yang sama boleh di-request lagi.
6. **Attachment masuk tahap ini**, disimpan engine lewat object storage, terikat ke request atau komentar.
7. **Uji visual GUI baru disetujui sekarang**: harness render boleh dijalankan saat eksekusi tanpa bertanya lagi.
8. **Tes: unit + integrasi SQL Server + smoke HTTP.**
9. Guide engine `doc/engine/engine-approval.md` ditulis ulang.

### 3.3 Keputusan turunan agent (diambil saat menulis plan; koreksi saat review plan bila tidak sesuai)

- **T-1 Letak kontrak.** `ApprovalAttribute`, `ApprovalKind`, `IEmApproval`, `IEmApprovalSystem<TKey>` beserta tipe
  step (`ApprovalSystemStep`, `ApprovalStepKind`) di `Em.Libs` (dibaca UI dan backend). Kontrak Data
  (`IEmApprovalDataTable<T>`, `IEmApprovalData<T>`, `IEmApprovalDataChild<T,TParent>`, context hook) di `Em.Api.Core`
  (D-10). Kontrak editor client (`IEmApprovalSystemEditor`) di `Em.Ui.Core` tanpa tipe WPF; view per platform.
- **T-2 Hak void.** Definisi System menyebut `VoidClaims` (daftar `ClaimRef` service+claim). Server mengizinkan void
  bagi user nyata pemegang salah satu claim itu, pengaju request, atau IsAdmin. Modul yang menentukan kapan void.
- **T-3 Claim editor penanda tangan.** Layar pengaturan penanda tangan "Diatur" (S-15) memakai claim engine
  `Administrative Tools:Approval Signer Settings`, mengikuti pola manager engine lain. Ini satu-satunya claim engine
  untuk approval selain `View {Nama}` System.
- **T-4 Penggantian id setelah reinstate.** `IEmApprovalSystemEditor` punya `OnReinstatedAsync(oldId, newId, key)`
  supaya modul menyimpan id baru ke tabelnya; kegagalan dipulihkan lewat pencarian per key (S-10).
- **T-5 Hook Data mendapat context.** Hook (`CreateKeyAsync`, `ValidateAsync`, `OnDeleteAsync` cadangan) menerima
  `IEmApprovalDataContext` yang membawa `DbContext` modul tempat `TEntity` terdaftar, sudah ikut `ApprovalTransaction`,
  serta caller dan operasi. Startup menolak `TEntity` yang tidak terdaftar di tepat satu context.
- **T-6 Objek join status.** View `vi_ApprovalRequestStatus` (id, jenis, nama, key, status, ringkasan, level dan nama
  step aktif, waktu selesai) di database inti, stabil untuk di-join modul dari database lain di server yang sama.
- **T-7 Analyzer dikirim.** Project `src/backend/Em.Api.Core.Analyzers` (netstandard2.0). Paket `EmSys.Api.Core`
  memuatnya di `analyzers/dotnet/cs`. Konsumen lewat ProjectReference menambah satu
  `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` di project host API (dicatat di
  guide); host `Em.Api` di repo ini memakainya.
- **T-8 Id diagnostik analyzer:** `EMAPV001` implementor tanpa `[Approval]` (induk Data/System), `EMAPV002` format/
  panjang nama, `EMAPV003` (jenis, nama) ganda, `EMAPV004` satu `TEntity` punya lebih dari satu definisi Data,
  `EMAPV005` `[Approval]` pada definisi anak (tidak boleh).

## 4. Langkah kerja

Ikuti urutan: **tulis seluruh kode semua langkah (4.1–4.10) dulu**, baru build penuh, tes, review, dan perbaikan
(4.11–4.12). Commit setelah tiap langkah selesai ditulis (build boleh belum dijalankan; sebut di pesan commit bila
belum dibangun).

### 4.1 Skema SQL

- Tulis ulang `doc/sqlscript/mssql/tables/020-approval.sql`: tanpa tabel jenis dokumen dan FK-nya.
  Tabel (nama final mengikuti Dahlia Convention; kolom standar engine):
  - `ta_ApprovalRequest`: id ULID, `cApprovalRequestKind` (tinyint, enum), `cApprovalName varchar(50)`,
    `cSubjectKey nvarchar(400)` (null untuk Create Data sebelum apply → diisi key induk setelah apply),
    `cSummary nvarchar(400)`, status (negatif = void), pembuat (FK user), waktu, `cReinstateOf` (FK diri sendiri),
    catatan pengajuan, waktu keputusan akhir.
  - `ta_ApprovalRequestItem` (Data): induk request, nomor urut, nama tabel/definisi, operasi (Create/Update/Delete/
    Restore), `cSubjectKey` baris, `cParentItem` (item induk di request untuk Create anak), `json_object` (Create).
  - `ta_ApprovalRequestItemField` (Data): kolom, old, new, current saat diputuskan, konflik, alasan override.
  - `ta_ApprovalRequestStep` (System): level, urutan, nama, jenis step, status step.
  - `ta_ApprovalRequestStepCandidate` (System): snapshot calon per step (user id, peran: penanda tangan atau cadangan four-eyes, urutan).
  - `ta_ApprovalRequestSignature`: step (null untuk Data), user, waktu, keputusan, catatan, kode verifikasi,
    penanda tanda tangan IsAdmin di luar calon dan penanda tanda tangan oleh cadangan four-eyes.
  - `ta_ApprovalRequestComment`: teks, pembuat, waktu.
  - `ta_ApprovalRequestAttachment`: request, komentar (opsional), referensi objek di object storage, nama berkas,
    ukuran, pembuat, waktu.
  - `ta_ApprovalSignerSetting`: (nama approval, nama step) → mode (tetap/Dynamic), penanda tangan tetap, dan
    daftar cadangan four-eyes beserta urutannya (S-28, S-29).
  - Index: filtered unique "satu request aktif per (jenis, nama, key)" **hanya untuk `System`** (Data tidak
    dikunci, PT-10).
- View `doc/sqlscript/mssql/views/vi_ApprovalRequestStatus.sql` (T-6) dan view baca lain yang dipakai service.
- `doc/sqlscript/mssql/updates/<yyyymmdd>-ApprovalRedesign.sql`, idempotent: drop FK ke tabel jenis dokumen, drop
  semua tabel approval lama, buat struktur baru. Tidak mengonversi data.
- Jalankan di SQL Server lokal pengembangan engine (database inti `Em.Api` lokal). Tes integrasi membuat database
  sendiri.

### 4.2 Kontrak (Em.Libs, Em.Api.Core, Em.Ui.Core)

- `Em.Libs`: `ApprovalAttribute(string name)`, `ApprovalKind`, `IEmApproval`, `IEmApprovalSystem<TKey>` (judul,
  `Steps` berisi `ApprovalSystemStep(level, name)`, `VoidClaims`); tidak ada deklarasi sumber dinamis, stub `IEmApprovalDocument` + DTO API baru (capabilities, list filter,
  state request, data change, tanda tangan, komentar, attachment, signer setting, signer change). Hapus DTO lama
  yang tidak dipakai; ganti `IApprovalServices`.
- `Em.Api.Core`: `IEmApprovalDataTable<T>` (`KeyColumns`, `TrackedColumns`, `DeleteMode`, `DeleteStateColumn`,
  `DeleteStampColumn`, `DeletedStateValue`, `ActiveStateValue`, `ApproverOnlyColumns`, default method
  `CreateKeyAsync`, `ValidateAsync`), `IEmApprovalData<T>` (+`Title`, `CanView`, `CanDecide`, `CanWithdraw` dengan
  default yang menolak kecuali IsAdmin), `IEmApprovalDataChild<T,TParent>` (+`ForeignKeyColumns`),
  `IEmApprovalSystemHandler<TDef, TKey>` (`ResolveDynamicSignerAsync`, `OnCompletedAsync`, `OnRejectedAsync`,
  `ResolveDynamicSignerAsync(step, key, requester)` mengembalikan user DS dan apakah langsung ditandatangani; handler lain opsional), `DataChange` (`Create(entity)`,
  `Update(original, modified)`, `Delete(entity)`, `Restore(entity)`, anak lewat `.With(child changes)`).
- `Em.Ui.Core`: `IEmApprovalSystemEditor` (`LoadAsync(context)`, `ValidateSignAsync`, `ValidateRejectAsync`,
  opsional `GetPreviewAsync` (PDF; tombol disembunyikan bila null), `OpenSourceAsync`, `ToolbarItems`,
  `OnReinstatedAsync` T-4), `ApprovalDecisionCheck(bool Ok, string? Message)`, context editor (id, state,
  kemampuan user, `RefreshAsync`), client `RequestApprovalAsync<TDef>(key, summary)`, `ReinstateAsync`, `VoidAsync`,
  `FindByKeyAsync<TDef>(key)`. Serialisasi key: string apa adanya; objek → JSON dengan urutan property deklarasi.

### 4.3 Registry, startup check, builder

- `ApprovalRegistry` berkunci (jenis, nama); definisi anak dikenali dari `TEntity`. Bangun dari
  `AddDataApproval<T>()` dan `AddSystemApproval<TDef>()`/`<TDef, THandler>()`; `AddDocumentApproval…` melempar
  `NotImplementedException`.
- `ApprovalStartupChecks`: attribute ada (induk/System) dan tidak ada di anak, format dan panjang nama, (jenis,
  nama) ganda dengan pesan menyebut kedua class, satu definisi per `TEntity`, kolom = property `TEntity` bertipe
  didukung (tipe dan panjang dari metadata EF), `TEntity` di tepat satu context, `TParent` terdaftar,
  `ForeignKeyColumns` sama jumlah dan tipe dengan `KeyColumns` induk (peringatan log bila nama berbeda),
  `DeleteMode` konsisten dengan kolom, step System unik namanya dan level ≥ 1, handler wajib ada bila ada step
  nama step unik. Gagal → API tidak start. (Pengaturan DS ada di data runtime, jadi diperiksa saat request dibuat.)
- `System`: daftarkan claim `View {Nama}` otomatis pada service approval engine.
- Hapus `ApprovalFlowBuilder`, `ApprovalFlowDeclaration`, `ApprovalDeclaration`, `*FlowDeclaration.Engine.cs`,
  `ApprovalSubmitPlan`, `ApprovalProgress` (bila tidak dipakai lagi), renderer PDF, `ApprovalKey` lama bila
  diganti serialisasi key baru; sesuaikan `EmAppBuilder.Approval.cs` (API dan WPF).

### 4.4 Analyzer

- Project `src/backend/Em.Api.Core.Analyzers` (netstandard2.0, `Microsoft.CodeAnalysis.CSharp`), masuk
  `Em.Api.slnx`; diagnostik T-8 sebagai Error. Analyzer memindai tipe di compilation dan semua assembly yang
  direferensikan yang mengimplementasikan `IEmApproval`. Pack ke `analyzers/dotnet/cs` paket `EmSys.Api.Core`
  (sesuaikan `scripts/pack-nuget`). Host `Em.Api` mereferensikannya (T-7).

### 4.5 Data Approval runtime

- Submit (`IApprovalEngine.SubmitDataAsync(DataChange root, string? note)`): validasi pohon (satu induk, definisi
  terdaftar, `Restore`/`Delete` sesuai `DeleteMode`), hitung selisih Update dari dua objek pada `TrackedColumns`
  (normalisasi teks per tipe, invarian), panggil `ValidateAsync` modul, simpan request. Bila caller lolos
  `CanDecide` (atau IsAdmin) → apply langsung dalam transaksi yang sama dan simpan sebagai self-approved.
- Apply saat approve, satu `ApprovalTransaction`: urutan induk → anak dari relasi; Create: deserialisasi JSON (tolak
  bila tidak cocok `TEntity`), `CreateKeyAsync`, insert, salin key induk ke `ForeignKeyColumns` anak; Update:
  cari baris lewat `KeyColumns`, pembandingan tiga nilai (sama new → lewati; sama old → terapkan; lain → konflik,
  bisa override dengan alasan; baris hilang → konflik final, 409); Delete Hard/Soft; Restore. Setelah approve,
  null-kan `json_object` item Create dan isi `cSubjectKey` dengan key baru.
- Withdraw oleh pengaju (atau `CanWithdraw`), Reject dengan alasan. Tanpa reinstate.
- Hapus `ApprovalDataApplier` versi string-per-kolom lama setelah penggantinya selesai.

### 4.6 System Approval runtime

- `RequestAsync(name, key, summary)`: tolak bila ada request aktif (bukan void) untuk (System, nama, key); susun
  flow di server dari definisi dan `ta_ApprovalSignerSetting`: step berisi DS → penanda tangan dari
  `ResolveDynamicSignerAsync` milik handler modul (boleh caller dan langsung ditandatangani, mis. Prepared by);
  step berisi user → penanda tangan dari pengaturan; cadangan four-eyes dari pengaturan. **Guard DS:** bila
  handler tidak terdaftar, tidak menangani step itu, atau tidak menghasilkan user aktif, request ditolak dengan
  pesan yang menyebut nama approval dan step (developer lupa mengisi DS). Step tanpa penanda tangan juga ditolak.
  Snapshot calon ke request.
- Sign/Reject: hanya calon di step aktif (level terendah yang belum lengkap) atau IsAdmin (ditandai); akun sistem
  ditolak; catatan keputusan; kode verifikasi per tanda tangan. Level lengkap → buka level berikutnya; semua lengkap
  → selesai → handler `OnCompletedAsync`; Reject → ditolak → `OnRejectedAsync`. Handler dijalankan setelah commit,
  exception hanya di-log.
- Reinstate: buat request baru dari key yang sama dengan flow disusun ulang, request lama `-2`/`-3`, tautan
  `cReinstateOf`. Void (keputusan 3.2-5, T-2). FindByKey (POST) untuk pemulihan.
- Sign oleh cadangan four-eyes langsung diterima (tanpa persetujuan orang kedua) dan ditandai di tanda tangan.
  Tidak ada action penggantian penanda tangan request berjalan; caranya reinstate atau IsAdmin (S-28).
- Pengaturan penanda tangan (get/save per nama approval) dengan claim T-3: per step satu penanda tangan (user atau
  DS) dan cadangan four-eyes; user nonaktif ditandai.
- Komentar dan attachment (object storage engine; batas ukuran dari pengaturan object storage) untuk pemegang hak
  lihat: pengaju, calon, `View {Nama}`, IsAdmin.

### 4.7 API umum, hub, pembersihan GET JSON

- `ApprovalServices` ditulis ulang. Action minimal (nama final mengikuti konvensi; GET per field):
  `GetMeta_ApprovalCapabilities()` (per definisi: jenis, nama, judul, canView, canDecide, jumlah menunggu caller,
  `ApproverOnlyColumns`), `PostGetMeta_ApprovalRequests(filter)`, `GetMeta_ApprovalRequest(id)` (state lengkap),
  `PostGetMeta_ApprovalRequestsBySubject(kind, name, key)`, `PostGetMeta_ApprovalDecide…`, `PostMeta_ApprovalWithdraw`,
  `PostGetMeta_ApprovalSystemRequest`, `…Reinstate`, `PostMeta_ApprovalSystemVoid`, `PostGetMeta_ApprovalSystemFindByKey`,
  komentar, attachment (unggah/unduh lewat endpoint object storage), signer setting, signer change.
- Hapus `GetMeta_ApprovalRequestsByDoc`, `GetMeta_ApprovalDocumentTypes`, dan jalur JSON `EmApp.TryConvert` untuk
  action approval (PT-12); perbarui catatan di `CLAUDE.md` dan `doc/ideas/get-parameter-per-field.md`.
- `ApprovalHubTaskSource`: kelompok per (jenis, nama) dengan jumlah menunggu caller (Data: `CanDecide`; System:
  calon di step aktif). Navigasi hub membuka Approval Manager terfilter.

### 4.8 Client dan WPF

- `Em.Ui.Core`/`Em.Ui.Wpf.Core` `ApprovalService` mengikuti action baru; `ApprovalManagerNavigationPayload` membawa
  jenis + nama (+ id request untuk langsung membuka).
- **Approval Manager** (ditulis ulang): daftar definisi dari capabilities dikelompokkan per jenis, filter status,
  daftar request (layout tabel UserManager: header caption + `ListBox`), detail Data (OLD/CURRENT/PROPOSED, konflik,
  override dengan alasan, Approve/Reject/Withdraw, timeline), mode Compact untuk disematkan layar modul (terfilter
  satu subjek). Detail System membuka ApprovalEditor.
- **ApprovalEditor (System)**: toolbar engine (Document preview bila editor menyediakan, buka dokumen sumber,
  Reinstate, Void bila berhak, item toolbar modul), area utama = view dari editor modul, signee card per step/level
  dengan tombol Sign/Reject (memanggil `ValidateSignAsync/ValidateRejectAsync` dulu), timeline, komentar, attachment.
- **System Approval Editor / Signer Settings** (layar admin, claim T-3): pilih approval System → daftar step
  (penanda tangan user atau Dynamic Signer, cadangan four-eyes, Make signer/Remove) dengan panel kanan User Pool
  drag and drop berisi Dynamic Signer yang ditawarkan definisi dan user (nonaktif ditandai, tidak bisa di-drop) → Save. Acuan desain: mockup yang
  di-approve di produk turunan. Kartu penanda tangan di ApprovalEditor menampilkan penanda tangan, cadangan, dan
  menandai user yang login bila ia cadangan.
- Builder WPF: `AddSystemApproval<TDef, TEditor>()`; hapus registrasi panel/opener lama.
- MAUI: hanya disesuaikan agar build (service client); tanpa UI approval.

### 4.9 Module uji `Em.Test`

- Ganti `TestServices.Approval.cs`, `TestPdf.cs`, panel QA step lama: Data Approval untuk pohon test doc (induk)
  dan test item (anak) dengan `CreateKeyAsync`, Soft delete, `ApproverOnlyColumns`; System Approval untuk test doc
  dengan flow 3 level (Pengaju, Dari dokumen, dua step Diatur paralel), handler selesai/ditolak yang menulis log
  uji, editor WPF contoh dengan validasi Sign dan satu tombol toolbar. Perbarui `900-emtest.sql` bila butuh kolom
  id request.

### 4.10 Dokumen

- Tulis ulang `doc/engine/engine-approval.md` (Inggris): tujuan, tiga jenis, cara mendefinisikan dan mendaftarkan
  Data/System, analyzer dan cara mereferensikannya lewat ProjectReference, hak akses, hub, API, skema dan migrasi
  `updates/`, arti `json_object` pada item Create, batasan dan yang ditunda. Perbarui `engine-test-module.md`,
  `doc/engine/README.md`, `CLAUDE.md` (catatan pembaruan approval).
- Ubah status catatan ide menjadi `jadi plan` dengan tautan ke plan ini (dilakukan saat plan ditulis).

### 4.11 Build dan tes

- `dotnet build src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, `src/frontend/Em.Ui.Maui.slnx` tanpa error;
  warning baru diperbaiki.
- **Unit** (`tests/Em.Api.Core.Tests`, `Em.Libs.Tests`): validasi nama, registry (jenis, nama), startup checks,
  diff Update, normalisasi nilai, pembandingan tiga nilai, serialisasi key, analyzer (`Microsoft.CodeAnalysis.Testing`
  atau compilation in-memory) untuk EMAPV001–005.
- **Integrasi** (`tests/Em.Api.Core.IntegrationTests`, SQL Server lokal): Data Create pohon + `CreateKeyAsync`,
  Update konflik/override, Delete Soft/Hard, Restore, self-approved, withdraw/reject, JSON di-null-kan; System
  request → sign per level paralel → selesai + handler, reject, void → request ulang, reinstate (`-2`/`-3`),
  IsAdmin sign ditandai, sign oleh cadangan four-eyes ditandai, DS terisi dan guard DS kosong, akun sistem ditolak, satu key satu request aktif, attachment.
- **Smoke HTTP** `..\.artefacts\em-system\scripts\approval-smoke\` terhadap `Em.Api` lokal dengan module uji aktif.
- **Render** `..\.artefacts\em-system\scripts\approval-render\` (disetujui 3.2-7): Approval Manager, detail Data,
  ApprovalEditor, Signer Settings; tema terang dan gelap; periksa PNG.

### 4.12 Review dan perbaikan

- Review seluruh diff (bahasa komentar, larangan nama produk, konvensi nama action, MVVM), perbaiki temuan, ulangi
  build/tes terkait. Tulis laporan eksekusi `plan/executed/approval-rancang-ulang-report.md`, pindahkan plan ke
  `plan/executed/`, commit.

## 5. Penanganan kegagalan dan pilihan cadangan

- Analyzer tidak bisa membaca attribute dari assembly referensi pada kasus tertentu → tetap kirim analyzer untuk
  kasus yang tertangkap, catat batasnya di guide; startup check tetap lapis kedua.
- Object storage tidak aktif di host → action attachment mengembalikan 409 dengan pesan jelas; UI menyembunyikan
  attachment. Tes attachment memakai object storage lokal.
- SQL Server lokal tidak tersedia → tes integrasi ter-skip (perilaku bawaan); catat sebagai tertunda.
- Tindakan ditolak policy → ikuti aturan "Tindakan terblokir policy" (skrip `.ps1` di `plan/approval-rancang-ulang-manual/`).
- Hal di luar cakupan → ambil pilihan paling konsisten dengan plan, catat di laporan, jangan berhenti untuk bertanya.

## 6. Laporan akhir

Daftar berkas utama, hasil build/tes per jenis (angka lulus/gagal/skip), hasil render, skrip SQL yang dijalankan,
yang tertunda, dan tautan guide.


## Catatan pelaksanaan 2026-10-17

- Kode skema, kontrak, registry/startup check, analyzer, runtime Data/System, API/hub/
  attachment, client WPF/MAUI, layar WPF dan modul contoh telah ditulis.
- Mockup editor dan viewer disetujui pengguna dalam sesi eksekusi dan dipakai untuk
  penyelarasan tiga kolom editor, kartu penanda tangan horizontal, sidebar, toolbar,
  Revert/draft lintas definisi, double-click pool dan dialog keputusan.
- Komit lokal dibuat per langkah; tidak ada push. Revisi instruksi urutan kerja disimpan
  pada `claude.md`.
- **Belum ada build, tes, render atau eksekusi SQL.** Implementasi belum dinyatakan
  tervalidasi. Berikutnya: selesaikan kode plan produk, kemudian integrasi dan seluruh
  pengujian kedua repo, termasuk SQL lokal, HTTP, screenshot dan review.
- Plan tetap di `unexecuted` sampai eksekusi dan verifikasi selesai; jangan menganggap
  penulisan kode sebagai penutupan plan.
