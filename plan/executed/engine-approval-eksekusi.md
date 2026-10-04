# Eksekusi engine approval

Status: **selesai dieksekusi** (ditutup 2026-10-01; hasilnya di [../../doc/report/engine-approval-eksekusi.md](../../doc/report/engine-approval-eksekusi.md),
cara memakainya di [../../doc/engine-approval.md](../../doc/engine-approval.md)). Ditulis ulang 2026-10-01. Rancangan — **apa** yang dibangun dan **kenapa** —
ada di [request-perubahan-dan-approval.md](request-perubahan-dan-approval.md); baca seluruhnya dulu.
Dokumen ini hanya memecah rancangan itu menjadi **tahap engine** beserta batas, kepemilikan berkas, dan
cara memverifikasinya.

## Apa yang berubah dari versi sebelumnya

Versi 2026-09-29 dokumen ini ditulis waktu engine dan modul bisnis masih satu repo. Isinya bercampur:
orkestrasi dua agent, tahap modul, nama produk, nama database aplikasi, dan nomor dokumen uji. Setelah
engine pindah ke repo ini, semua itu bukan urusan repo ini lagi.

Yang **tetap** di sini: tahap engine E1–E7 di bawah, kepemilikan berkas `Em.*`, dan kontrak skema yang
engine harapkan dari database aplikasi.

Yang **pindah** ke plan eksekusi milik repo aplikasi pemakai (repo privat, tidak dirujuk dari sini):
orkestrasi dua agent dan pembagian tahapnya, tahap modul pemakai (kendaraan uji data approval dan document
approval), skrip SQL tabel approval, data uji dan daftar akunnya, skenario uji end-to-end, serta gerbang
user. Plan aplikasi itulah yang menjadwalkan tahap-tahap di bawah; dokumen ini tidak mengatur urutan
antar-agent.

## Cakupan

Dibangun di repo ini:

| # | Bagian | Tahap |
| --- | --- | --- |
| 1 | Kontrak publik approval (entitas, DTO, interface, tipe deklarasi, kontrak client dan panel UI) | [E1](#e1--kontrak) |
| 2 | Engine inti approval: pengajuan, penentuan penanda tangan, keputusan, hook, transaksi, reinstate | [E2](#e2--engine-inti-approval) |
| 3 | Data approval: item/kunci/kolom, perbandingan tiga nilai, konflik dan override | [E3](#e3--data-approval) |
| 4 | Local binary storage | [E4](#e4--local-binary-storage) |
| 5 | Stamp PDF, lembar pengesahan, PDF kalibrasi slot | [E5](#e5--stamp-pdf) |
| 6 | Approval Manager (control approval bersama, WPF) + client service di kedua UI core | [E6](#e6--approval-manager) |
| 7 | Hub MY TASKS generik (sumber hub + tampilan WPF) | [E7](#e7--hub-my-tasks-generik) |

**Tidak** dibangun sekarang: [data snapshot](../unexecuted/data-snapshot.md), notifikasi dan bagian MY REQUESTS (poin
terbuka 7 rancangan), layar cek kode verifikasi, menyimpan filter Approval Manager per user, control
approval MAUI dan hub MAUI (client service MAUI tetap dibuat), QR pada stamp, lampiran sebagai fitur
engine (bukan fitur engine — lihat rancangan), kompresi respons.

## Prinsip yang mengikat seluruh tahap

- **Engine netral produk.** Tidak ada nama produk, nama perusahaan, nama database aplikasi, atau nama modul
  bisnis di kode maupun dokumen repo ini. Rujuk pemakainya sebagai *modul pemakai*, *aplikasi*, atau *jenis
  dokumen*. Rancangan di repo ini masih memuat contoh dari aplikasi pertamanya sebagai bahan pembahasan;
  kode baru tidak mengikuti gaya itu.
- **Engine tidak tahu tabel modul.** Semua yang table-specific — kunci (termasuk composite key), memuat
  data, menerapkan perubahan, validasi — disuplai modul lewat handler/deklarasi. Engine approval harus bisa
  dipakai modul yang datanya ada di tabel warisan: tanpa kolom standar, tanpa id ULID, dengan composite
  key. Konsekuensinya dicatat per tahap.
- **Database inti** yang dipakai engine adalah koneksi `EmAppBuilder.DefaultConnectionName` (`"Default"`).
  Tabel approval ada di situ. Database modul pemakai adalah koneksi lain yang didaftarkan aplikasi.
- **Registrasi hanya di fase builder** (`EmAppBuilder`), tidak pernah saat runtime. Hasil resolusi
  `ServiceProvider` tidak disimpan melewati batas request.
- **Nama action wajib beratribut.** Method bernama `Get.../Post.../Meta...` tanpa `[GetAction]` /
  `[PostAction]` menghasilkan 404 tanpa error kompilasi. Periksa sebelum setiap commit.
- **Nama action non-tabel**: `PostGetMeta_{Nama}` (POST yang mengembalikan nilai), `PostMeta_{Nama}` (POST
  tanpa nilai), `GetMeta_{Nama}` (GET), di region `Meta's` di bawah region `Views`.
- **XML doc Bahasa Indonesia** untuk member `public` di library bersama; **tanpa nama objek database** di
  doc comment atau help. Pakai istilah konseptual yang sudah dipakai rancangan ("request approval",
  "langkah", "jenis dokumen"). Selain XML doc, seluruh teks di kode tetap Bahasa Inggris.
- **Akun sistem tidak boleh approve.** Akun admin bawaan dan akun debugger sengaja **bukan** id ULID valid,
  jadi validator/parser id wajib mem-bypass keduanya, dan keduanya **error saat approve** karena penanda
  tangan ber-FK ke tabel user. Ini pengecualian dari aturan "admin bisa semua": pemeriksaan hak approve
  tidak memakai urutan debug → admin → claim. Debugger menguji approval dengan men-set *active user* ke
  user nyata. User nyata dengan saklar admin tetap boleh approve, dan pada langkah ber-`signers:` yang
  bukan dirinya tercatat sebagai pengganti.
- **Enum `*State` dan kolom `Stage`**: negatif = tidak dipakai/dihapus/ditolak, 0 ke atas = aktif.
- **Library open source berlisensi permisif (MIT/Apache/BSD) boleh langsung di core**; hanya yang
  berlisensi berbayar dipisah lewat bridge tersendiri. PdfSharp (MIT) boleh di `Em.Api.Core`.
- **Butuh varian method → tambah overload bernama sama**, jangan rename yang sudah ada.
- **UI**: layar yang dinavigasi di `Navigations/`, `Controls/` hanya untuk control primitif, style bersama
  di `Styles/`. Semua layar MVVM dengan `MvvmModelBase`: command lewat
  `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, can-execute berupa method
  `<Command>Allowed` (bukan lambda), properti bindable wajib `public` (properti `private` lolos build tapi
  gagal runtime), dan getter yang di-binding TwoWay tidak menormalkan nilai. Material design mengikuti
  `Navigations/RoleManager.xaml`, `Navigations/UserManager.xaml`, `Navigations/UserEditor.xaml`,
  `Dialogs/DisplayExceptionData.xaml` — salin nama token dan bentuk template dari situ.
- **Kedua UI core sejajar.** `Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core` adalah implementasi paralel engine yang
  sama dan strukturnya harus tetap seiring. Untuk fitur ini yang sejajar adalah **client service**-nya;
  control dan hub MAUI sengaja belum dibuat dan dicatat begitu.

## Kepemilikan berkas

Tahap E1–E3 saling terikat (kontrak dan engine inti); E4–E7 menempel padanya lewat interface yang
ditetapkan E1 dan bisa dikerjakan agent lain secara paralel setelah E1 selesai.

| Area | Tahap |
| --- | --- |
| `Em.Libs/Api.Core.Models/Approval*`, `Hub*`, `IBinaryStorage`, seluruh DTO | E1 |
| `Em.Api.Core/Api/Approval/**` kecuali `Pdf/` | E2, E3 |
| `Em.Api.Core/Api/Shared/EmAppBuilder.cs`, `Api/Core/EmApp.cs` kecuali `GetStaticTools` | E1, E2 |
| `Em.Ui.Core` kontrak client approval dan kontrak panel UI untuk modul | E1 |
| `Em.Api.Core/Api/Storage/**` | E4 |
| `Em.Api.Core/Api/Approval/Pdf/**` | E5 |
| `Em.Ui.Wpf.Core/Navigations/Approval*` + control pendukungnya, client service approval di **kedua** core | E6 |
| `Em.Ui.Wpf.Core/Windows/TabbedMainWindow.*`, `EmApp.GetStaticTools` | E7 |
| `Em.Api.Core/Api/Hub/**` | E7 |

Folder `Api/Approval`, `Api/Storage`, dan `Api/Hub` belum ada — dibuat sejajar `Api/Core` dan `Api/Shared`
yang sudah ada.

Pengerjaan E4–E7 **menambah** member di kontrak E1 bila perlu (menambah, bukan mengubah atau menghapus),
dan mencatatnya. Paket baru (PdfSharp untuk E5) ditambahkan di `.csproj` yang memakainya dan dicatat.

## Kontrak skema yang engine harapkan

Tabelnya ada di database inti aplikasi, jadi **skripnya milik repo aplikasi**, bukan repo ini. Yang
ditetapkan engine adalah bentuknya. Rinciannya di bagian
[Tabel](request-perubahan-dan-approval.md#tabel-nama-approvalrequest) rancangan; yang wajib ada:

- `ta_ApprovalRequest` — header: jenis (1 data / 2 document), jenis dokumen, kunci dokumen kanonik, versi
  dokumen (`nvarchar(50)`, disimpan apa adanya karena versi dokumen warisan bisa bukan angka), peminta,
  level berjalan, waktu selesai, referensi PDF dasar (nullable), tautan `ReinstateOf` (nullable), `Stage`
  (0 draft, 1 pending, 2 approved, -1 rejected, -2 ditarik, -3 ditarik setelah selesai), dan kolom standar.
- `ta_ApprovalRequestStep` — satu baris per langkah: nama langkah, claim, level, penanda tangan dan
  waktunya, atas-nama, status (0 menunggu, 1 approved, -1 rejected, -2 dilewati), komentar keputusan, kode
  verifikasi, penanda override, salinan slot, dan kolom standar.
- `ta_ApprovalRequestStepSigner` — daftar penanda tangan yang ditentukan saat submit untuk langkah
  ber-`signers:`.
- `ta_ApprovalRequestItem` — data approval: entitas, kunci kanonik, operasi, `Stage` hasil penerapan.
- `ta_ApprovalRequestItemKey` dan `ta_ApprovalRequestItemField` — **ramping** (tanpa kolom standar, PK
  komposit), penyimpangan yang disengaja dari aturan penamaan.
- `ta_ApprovalRequestComment` — komentar bebas.

Index dan FK yang menjadi bagian perilaku engine, bukan sekadar optimasi:

- **Unique filtered index** `(jenis dokumen, kunci dokumen, versi) WHERE Stage = 1` — penjaga balapan dua
  pengajuan berbarengan; engine mengandalkannya, bukan hanya memeriksa di kode.
- **FK penanda tangan, peminta, atas-nama, dan daftar penanda tangan ke tabel user** — ini yang membuat
  akun sistem gagal saat approve.
- **FK jenis dokumen ke tabel jenis dokumen** aplikasi, jadi jenis dokumen yang tidak terdaftar ditolak
  database. Barisnya diisi admin aplikasi lewat SQL; engine tidak membuatnya.

Nilai dan kunci memakai `nvarchar` (tabel warisan `nvarchar`); kunci kanonik `nvarchar(450)` supaya bisa
di-index. Nama entitas item diberi namespace modul.

---

## E1 — Kontrak

Tujuan: **semua** tipe publik dan interface yang dipakai tahap lain, **ter-build**, dengan implementasi
stub (`throw new NotImplementedException()`), supaya E4–E7 bisa mulai paralel tanpa menunggu E2.

`Em.Libs/Api.Core.Models` (namespace `Em.Api.Core.Models`):

- Entitas `ta_ApprovalRequest`, `ta_ApprovalRequestStep`, `ta_ApprovalRequestStepSigner`,
  `ta_ApprovalRequestItem`, `ta_ApprovalRequestItemKey`, `ta_ApprovalRequestItemField`,
  `ta_ApprovalRequestComment`, beserta `DbSet`-nya.
- DTO: `ApprovalDecision` (request, nama langkah — wajib untuk level paralel, approve, catatan, `Payload`
  JSON milik modul, `Override` untuk konflik data, `GuardOverride` + alasan), `ApprovalDecisionResult`
  (berhasil, pesan error, detail konflik, status request terbaru), `ApprovalRequestInfo` (baris daftar:
  kolom standar engine + ringkasan modul), `ApprovalRequestDetail` (langkah, penanda tangan, isian,
  timeline), `ApprovalTimelineEntry`, `ApprovalQuery` + `PagedResult<T>` (mode *perlu tindakan saya* /
  *semua*, jenis dokumen, dokumen tertentu, teks, status, paging), `ApprovalGuardResult` (lolos/blokir,
  alasan, boleh ditembus), `ApprovalConflictField` (lama / sekarang / usulan), `HubTaskInfo` (sumber, id,
  judul, keterangan, jenis, jumlah, umur tertua, progres, aksi, target navigasi).
- `IApprovalServices : IServices`: `GetMeta_ApprovalRequests(ApprovalQuery)`,
  `GetMeta_ApprovalRequestsByDoc`, `GetMeta_ApprovalRequest`, `GetMeta_ApprovalRequestPdf`
  (`Task<Stream>`), `GetMeta_ApprovalGuard`, `PostGetMeta_ApprovalDecide(ApprovalDecision[])`,
  `PostMeta_ApprovalCancel` (tarik kembali, alasan wajib), `PostMeta_ApprovalComment`,
  `GetMeta_UserHubTasks`, dan `GetMeta_ApprovalSlotCalibration(docType, ...)`.

`Em.Api.Core` publik:

- `IApprovalEngine`: `SubmitAsync`, jalur data approval (pengajuan dan simpan langsung oleh pemegang claim
  approve), `EnsureNotInApprovalAsync`, `ReinstateAsync`.
- Builder `AddDocumentApproval<TServices, TKey>` dan `AddDataApproval<TServices>` beserta tipe
  deklarasinya: `flow.Level(n, ...)`, `l.Step(name, slot, signers:, strict:, distinctFrom:, when:, guard:,
  input:)`, `onCompleted:` per level, `flow.Pdf`, `flow.Summary`, `flow.RequireOpen`,
  `flow.ApprovalSheet`, dan hook `OnSubmitted` / `OnSigning` / `OnSigned` / `OnFinishing` / `OnFinished` /
  `OnRejecting` / `OnRejected` / `OnReinstating`.
- `ApprovalGuard.Allow` dan `ApprovalGuard.Block(reason, overridableBy:)`.
- `StepInput<TServices, TKey, TPayload>` (`Validate`, `OnSigned`, `Fields`; keadaan awal panel diambil panelnya sendiri lewat action modul — A10 menghapus `Prepare`/`TState` yang tak pernah dipakai engine) dan `Slot(page?, x, y, w, h)`.
- Atribut `KeyPart` dan pembaca kunci kanonik: kunci bertipe milik modul, engine yang menerjemahkan ke
  array JSON sesuai urutan bagian. Format per tipe dibakukan (tanggal-jam `yyyy-MM-ddTHH:mm:ss.fff`).
- `IBinaryStorage`: `PutAsync(key, stream)` yang **tidak menimpa**, `OpenReadAsync`, `DeleteAsync`,
  `ExistsAsync`, plus `builder.AddLocalBinaryStorage(path)` (isinya E4).
- `IApprovalPdfRenderer` (jembatan engine ↔ E5): `RenderStampedAsync(basePdf, stepsSnapshot)`,
  `RenderCalibrationAsync(basePdf, slots)`.
- `IHubTaskSource` + `builder.AddHubTaskSource<T>()`, dan `IApprovalHubQuery` — query "perlu tindakan user
  X" yang dipakai sumber hub E7.

Client dan UI:

- Kontrak client di `Em.Ui.Core`; implementasi `ApprovalService : IApprovalServices` di
  `Em.Ui.Wpf.Core/Api.Core` **dan** `Em.Ui.Maui.Core/Api.Core` (E6).
- Kontrak host untuk modul (WPF): `builder.AddApprovalStepPanel<TView, TVm>(docType, step)`,
  `builder.AddApprovalInfoPanel<TView>(docType, steps:, input:, claim:, order:)`,
  `builder.AddApprovalDocumentOpener(docType, ...)`, dan antarmuka `IApprovalPanelHost` (`RefreshAsync()`,
  payload isian, dokumen aktif).

Bentuk API boleh menyimpang dari contoh rancangan bila compiler atau konvensi memaksa; catat
penyimpangannya. **Signature `IBinaryStorage` dan `IApprovalPdfRenderer` dicatat apa adanya** begitu
selesai — E4 dan E5 mengimplementasinya tanpa bisa bertanya.

Selesai bila: seluruh solution ter-build, dan E4–E7 bisa mulai terhadap kontrak ini.

## E2 — Engine inti approval

`Em.Api.Core/Api/Approval/**`, mengikuti rancangan bagian *Rancangan sisi server*, *Isian per langkah*,
*Prepared By*, *Reinstate*, *Komentar dan timeline*, *Hak melihat*, *Atomisitas*, dan *Hook module*.

- **Registrasi deklarasi**: claim otomatis per langkah, claim lihat `{jenis dokumen}:View`, dan claim
  penembus yang dideklarasikan `guard:`. Pengaman startup: database modul pemakai harus satu server dengan
  database inti — kalau tidak, aplikasi gagal start dengan pesan jelas (syarat transaksi lintas database).
  Slot wajib: langkah pada dokumen ber-PDF tanpa slot → gagal saat build app.
- **`SubmitAsync`** urut seperti rancangan: identitas user nyata dan aktif → claim langkah pertama → tidak
  ada request pending untuk dokumen + versi yang sama (409, dijaga unique filtered index) → tentukan
  **semua** penanda tangan dinamis sekaligus → render PDF dasar dan simpan ke `IBinaryStorage` **di luar
  transaksi** (key `approval/{yyyy}/{MM}/{requestId}/v{versi}.pdf`) → satu transaksi: header, semua langkah
  dengan salinan slot, daftar penanda tangan, tanda tangan otomatis level 1 oleh peminta, maju ke level
  berikutnya → sesudah commit: hook `OnSubmitted`. Gagal di transaksi → rollback dan PDF dihapus.
- **Penentuan penanda tangan**: `signers:` sebagai penentu daftar user (bukan predikat), helper engine
  untuk memetakan identitas karyawan modul ke user aktif, `when:` (langkah tidak berlaku → `-2`),
  four-eyes `distinctFrom:` dicek saat submit terhadap daftar dan saat sign terhadap penanda tangan
  sebenarnya termasuk pengganti, dengan pengecualian user ber-saklar admin. `flow.Summary` dihitung saat
  submit dan reinstate, disimpan di `json_object` request.
- **`PostGetMeta_ApprovalDecide`**: setiap keputusan dalam transaksinya sendiri — yang gagal tidak
  membatalkan yang lain. Urutan pemeriksaan per keputusan persis rancangan: user nyata → request masih
  pending → langkah ada di level berjalan dan masih menunggu → claim → daftar penanda tangan atau
  pengganti (alasan wajib) → `strict:` menolak pengganti → `guard:` dijalankan **ulang** di server, dengan
  penembus lewat claim override (alasan wajib, langkah ditandai override, tercatat sebagai atas-nama
  penanda tangan langkah) → alasan wajib saat reject → `input:` `Validate` → hook `OnSigning` → tulis
  keputusan dengan `WHERE Stage = 0` supaya dua orang berbarengan menghasilkan satu 409 → `input:`
  `OnSigned` di dalam transaksi → reject: langkah terbuka jadi `-2`, request `-1`; approve: level lengkap →
  `onCompleted:` level → maju, habis → selesai → `OnFinishing` / `OnRejecting` di dalam transaksi → commit
  → `OnFinished` / `OnRejected` sesudah commit.
- **Kode verifikasi**: dibuat engine saat langkah ditandatangani, disimpan di kolom langkah, unik, 10
  karakter Crockford Base32 acak.
- **Transaksi lintas database satu koneksi**: engine membuka koneksi ke database inti, memulai transaksi,
  lalu `ChangeDatabase` saat menyerahkan giliran ke modul; context modul dipasang ke koneksi dan transaksi
  itu oleh engine (`SetDbConnection` + `UseTransaction`). Author modul tidak mengurus koneksi. Tanpa MSDTC,
  jadi sisi penerapan hanya SQL Server — catat batasan ini.
- **Tarik kembali dan reinstate**: pemegang claim langkah pertama dan user ber-saklar admin; pending →
  `Stage -2`; sudah selesai → `Stage -3` plus hook `OnReinstating` di dalam transaksi yang boleh menolak;
  pengajuan ulang menyimpan tautan `ReinstateOf`.
- **Komentar dan timeline**: komentar keputusan di kolom langkah (opsional saat approve, wajib saat reject
  dan saat jadi pengganti, tidak dicetak di stamp), komentar bebas di tabel komentar (tidak bisa diedit
  atau dihapus, boleh setelah request selesai), timeline gabungan aksi + komentar yang tersambung lintas
  reinstate lewat `ReinstateOf`. Hak berkomentar = hak melihat.
- **Hak melihat**: pemegang claim langkah mana pun di alur jenis dokumen itu, pemegang `{jenis
  dokumen}:View`, dan user ber-saklar admin. Berlaku untuk **semua** action baca engine — daftar, detail,
  PDF, timeline.
- **Daftar ber-paging** dengan filter di server, termasuk sort/filter pada kolom ringkasan modul lewat
  `JSON_VALUE`.
- **`EnsureNotInApprovalAsync`** untuk dipanggil action simpan modul: dokumen terkunci selama ada request
  pending. Engine **tidak** menulis status ke tabel modul.
- **`GetMeta_ApprovalRequestPdf`**: cek hak → ambil PDF dasar → `IApprovalPdfRenderer.RenderStampedAsync`.
  Sampai E5 selesai, renderer stub mengembalikan PDF dasar apa adanya. PDF ber-stamp dibuat **saat
  diminta** dari PDF dasar + data langkah, tidak disimpan per keputusan.
- **`IApprovalHubQuery`** diisi di sini karena query-nya milik engine inti; E7 memakainya.

Catat bentuk `ApprovalQuery`, `ApprovalRequestInfo`, `ApprovalRequestDetail`, dan `IApprovalHubQuery`
begitu selesai — E6 dan E7 membangun UI di atasnya.

Verifikasi: build; action debug sementara boleh dipakai untuk uji manual, **dihapus sebelum commit**.

## E3 — Data approval

Bagian engine yang membuat request bisa membawa **data usulan**, bukan hanya gerbang status.

- Item, bagian kunci, dan kolom: satu item per entitas yang disentuh, dengan operasi (baru / ubah / hapus /
  aktifkan ulang) dan kolom yang berubah.
- **Perbandingan per kolom tiga nilai** saat approve: **lama** (dicatat saat diajukan), **sekarang** (isi
  tabel saat itu), **usulan**. sekarang = lama → diterapkan; sekarang = usulan → kolom dilewati, bukan
  konflik; selain itu → **konflik**. Pembandingnya isi tabel, bukan request lain, supaya perubahan dari
  luar aplikasi ikut tertangkap. Nilai "sekarang" disimpan di kolom kolom-item.
- **Konflik**: request gagal sendiri dengan hasil `Conflict`; keputusan lain dalam batch tetap jalan.
  Approver memilih *tetap terapkan* (flag `Override`, tercatat sebagai timpa sadar) atau *tolak*. Entitas
  yang sudah tidak ada → konflik yang **tidak bisa di-override**.
- **`Stage` per item** mencatat hasil penerapan (diterapkan, dilewati, di-override), bukan keputusan
  approver. Approve tetap **utuh** per request; skema menampung approve sebagian bila kelak dibutuhkan.
- **Simpan langsung oleh pemegang claim approve**: tercatat sebagai request yang otomatis disetujui, dengan
  pengecekan konflik yang sama, sehingga berfungsi sebagai optimistic concurrency.
- Penerapan dijalankan handler modul pada tahap yang sama dengan `OnFinishing`, di dalam transaksi
  [E2](#e2--engine-inti-approval).
- Entitas baru memakai kunci sementara di dalam request; bagian kunci yang baru ada saat diterapkan diisi
  saat itu. **Bagian kunci tidak bisa diubah lewat request** — perubahan kunci = hapus lalu buat baru.

**Dibangun (catatan implementasi, 2026-10-01).** Keputusan yang diambil saat membangunnya, di luar yang
tertulis di atas:

- **Satu request data punya tepat satu langkah**, bernama sama dengan claim persetujuannya. Itulah yang
  membuat daftar request, hub, riwayat, penarikan, dan keputusan memakai kode yang sama dengan request
  dokumen, tanpa rangkaian query tersendiri. Pemegang claim persetujuan = satu-satunya penanda tangan.
- **Penanda versi request data adalah id request itu sendiri.** Unique index yang menjaga satu request
  menunggu per dokumen dan versi akan melarang tepat yang diizinkan rancangan (beberapa usulan menunggu
  untuk satu dokumen); versi yang tidak pernah sama menyingkirkannya tanpa mengubah skema. Daftar request
  menampilkan versinya kosong.
- **Pembandingan semua entitas dilakukan lebih dulu, baru penerapan** (urut menurut urutan deklarasi
  entitas). Request yang konflik tidak pernah setengah diterapkan; transaksinya dibatalkan, lalu nilai
  "sekarang" dan tahap `Conflicted` tiap entitas dicatat di luar transaksi itu supaya approver yang
  membukanya lagi melihat apa yang bertabrakan. Hasil keputusannya memuat kolom yang bertabrakan, dan
  apakah konfliknya final.
- **Menimpa konflik wajib beralasan** (catatan keputusan). Entitas yang sudah tidak ada tidak bisa ditimpa.
  Kolom yang nilainya sudah sama dengan usulan dilewati; entitas yang semua kolomnya dilewati berstatus
  dilewati, yang ditimpa berstatus ditimpa dengan catatan kolom mana.
- **Kunci sementara diganti kunci sebenarnya**, bukan hanya di entitasnya: bagian kunci yang berubah
  (menurut nama bagian) diganti juga di entitas berikutnya dalam request yang sama, dan nama dokumen
  request ikut berganti kalau dokumennya adalah entitas yang baru terbentuk. Induk baru dan anak-anaknya
  bisa diajukan dalam satu request; yang ditulis modul hanya urutan penerapan.
- **Usulan yang tidak mengubah apa pun dibuang** (kolom ber-nilai-lama sama dengan nilai-baru, lalu
  entitas yang kosong); kalau tidak ada yang tersisa, 400. Entitas tanpa kolom (hapus, aktifkan ulang)
  tampil di rincian request sebagai satu baris bernama operasinya.
- **Simpan langsung yang konflik** tidak menyimpan apa pun dan tidak meninggalkan request; pemanggilnya
  mendapat 409 yang menyebut kolomnya.
- **Penarikan request data** boleh oleh pengusulnya dan pemegang claim persetujuan, hanya selagi menunggu.
  Request yang sudah disetujui sudah tertulis di tabel modul dan tidak bisa ditarik; membalikkannya
  adalah usulan baru.
- **Akun sistem tidak bisa mengusulkan** (sama seperti pengajuan dokumen), karena peminta ber-FK ke user.

## E4 — Local binary storage

`Em.Api.Core/Api/Storage/LocalBinaryStorage.cs`, implementasi `IBinaryStorage` sesuai bagian
[Penyimpanan biner](request-perubahan-dan-approval.md#penyimpanan-biner). CDN engine tidak dipakai untuk
ini karena CDN bersifat publik tanpa password.

- Key berbentuk path yang juga sah sebagai key S3. Validasi: hanya `[a-z0-9/._-]`, tanpa `..`, tanpa `/` di
  awal.
- **Tidak pernah menimpa** — 409 bila key sudah ada.
- Tulis ke file sementara lalu pindah atomik. **Tanpa endpoint publik**: isinya diunduh lewat action
  pemakainya yang memeriksa hak.
- Semua path fisik lewat satu pintu, mengikuti pola `CdnStore.ResolveInside` yang sudah ada.
- `builder.AddLocalBinaryStorage("./data/binary")` dipanggil host aplikasi; stub-nya sudah ada dari E1.

Implementasi S3-compatible menyusul tanpa mengubah pemakainya.

## E5 — Stamp PDF

`Em.Api.Core/Api/Approval/Pdf/**` dengan **PdfSharp** (MIT, versi terbaru yang mendukung `net10.0`).

- `RenderStampedAsync(basePdf, stepsSnapshot)`: untuk tiap langkah yang sudah diputuskan, gambar stamp di
  slotnya — status APPROVED hijau / REJECTED merah, nama penanda tangan, *a.n.* bila pengganti atau
  penembus, tanggal-jam, kode verifikasi, dan tanda **OVERRIDE** bila blokir ditembus. Gambar juga slot
  isian dari payload (checkbox / teks). Kode verifikasi dicetak di margin **setiap** halaman. Bila
  `flow.ApprovalSheet()` dideklarasikan, tambahkan lembar pengesahan: halaman berisi tabel semua langkah.
- Langkah tanpa `signers:` yang ditembus: stamp menulis nama langkahnya (*a.n. {nama langkah}*), kolom
  atas-nama kosong.
- `RenderCalibrationAsync(basePdf, slots)` + action `GetMeta_ApprovalSlotCalibration(docType, ...)`: PDF
  contoh dengan kotak slot tergambar dan diberi label, **hanya** untuk debugger atau user ber-saklar admin.
  Ini alat developer untuk mengukur slot pada layout dokumen yang sebenarnya.
- Validasi slot wajib idealnya di registrasi ([E2](#e2--engine-inti-approval)); kalau letaknya harus di
  sisi engine inti, koordinasikan lewat catatan.
- **QR tidak dikerjakan** (`QRCoder` menyusul).

## E6 — Approval Manager

`Em.Ui.Wpf.Core/Navigations/ApprovalManager.xaml` + VM, sesuai bagian *Control approval bersama* dan
*Keputusan lanjutan* rancangan. **Satu control bersama**, bukan beberapa layar.

- Parameter: mode (*perlu tindakan saya* / *semua* yang boleh dilihat), filter jenis dokumen, filter
  dokumen tertentu. `ListView` + material design mengikuti `UserManager.xaml` (toolbar, quick filter,
  filter sheet + chip filter aktif, pager; paging dan filter di server). Kolom standar engine + kolom
  ringkasan modul. **Tanpa** grouping dan tanpa pemilihan kolom oleh user. **Tanpa DevExpress.**
- **Kartu kanan**: PDF lewat viewer core (print boleh, **save as tidak**), timeline + komentar bebas, kartu
  informasi modul (`AddApprovalInfoPanel` — bisa dilipat, urutan dari developer, claim per kartu), panel
  isian langkah (`AddApprovalStepPanel`), dan tampilan konflik data approval (lama / sekarang / usulan
  dengan pilihan *tetap terapkan* atau *tolak*).
- **Tombol keputusan** aktif hanya bila baris menunggu user aktif. `guard:` memblokir → Approve mati dan
  alasannya tampil, Reject tetap aktif; tombol *Approve (override)* muncul bila user memegang claim
  penembus, dengan dialog alasan **wajib**. Pengganti dan reject juga beralasan wajib. `guard:`
  dievaluasi ulang setiap layar dibuka atau dimuat ulang, sehingga blokir yang syaratnya sudah terpenuhi
  membuka sendiri. Kartu boleh meminta layar memuat ulang (`RefreshAsync()`).
- **Approve banyak sekaligus** dari daftar hanya untuk jenis dokumen tanpa `RequireOpen` dan langkah tanpa
  isian wajib. `RequireOpen` dijaga **di UI saja**.
- **Mode buka dokumen**: PDF besar, Approve/Reject di dalam viewer, Sebelumnya/Berikutnya menjelajah daftar
  yang difilter, bisa dibuka sebagai tab sendiri.
- **Panel status approval untuk layar dokumen**: control yang sama, difilter ke satu dokumen, supaya modul
  bisa menanamnya di tab Approval layarnya.
- **Tarik kembali** dengan alasan wajib.
- **Menu Tools**: `approval.manager` di `EmApp.GetStaticTools`, sejajar User Manager dan Role Manager,
  disembunyikan bila user tidak boleh membukanya. Selain dari Tools, dibuka dari hub (sudah difilter) dan
  ditanam di layar modul.
- **Client service** approval dibuat di **kedua** core; **control dan layar MAUI tidak dikerjakan**.

## E7 — Hub MY TASKS generik

Hub di baris judul jendela utama berubah dari penampung business task menjadi **wadah umum daftar pekerjaan
user aktif**. Hub hanya etalase: keputusan tidak diambil di hub.

- **Server** (`Em.Api.Core/Api/Hub/**`): `IHubTaskSource` untuk document approval dan data approval, memakai
  `IApprovalHubQuery` dari E2. Item *perlu tindakan* **dihitung**, tidak disimpan, sehingga hilang sendiri
  dari hub approver lain begitu satu orang memutuskan. Langkah ber-`signers:` hanya muncul ke penanda
  tangan tercatat; pemegang claim lain membukanya dari Approval Manager sebagai pengganti. Semuanya
  dikumpulkan satu action `GetMeta_UserHubTasks`, dikelompokkan per jenis approval, satu baris per jenis
  dokumen, dengan jumlah dan umur request tertua.
- **WPF** (`Em.Ui.Wpf.Core/Windows/TabbedMainWindow.*`, sekarang masih placeholder "No Running Task
  Available"): bagian RUNNING (business task — **tetap memakai jalur polling-nya sendiri**, jangan
  disatukan), DOCUMENT NEED APPROVAL, dan DATA NEED APPROVAL. Bagian yang kosong disembunyikan. **Open**
  membuka Approval Manager terfilter ke jenis dokumen itu.
- **Tanpa** action aksi hub di server. **Hub MAUI tidak dikerjakan.**

---

## Verifikasi

Belum ada test project di repo ini, jadi verifikasi tiap tahap adalah:

1. Build `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, dan `src/frontend/Em.Ui.Maui.slnx` tanpa
   error. Tahap yang dipakai dari repo aplikasi juga dibangun dari sisi aplikasi.
2. Setiap method bernama action punya `[GetAction]` / `[PostAction]`.
3. Tidak ada registrasi service saat runtime, dan tidak ada nama objek database di XML doc comment / help.
4. Tidak ada kata bernuansa produk atau nama modul bisnis yang masuk ke kode maupun dokumen repo ini.
5. Stub `NotImplementedException` dari E1 sudah habis di jalur yang dipakai.

**Skenario uji end-to-end dijalankan dari sisi aplikasi** (lewat modul pemakai, akun dan claim sungguhan),
bukan dari repo ini — engine tidak punya dokumen uji sendiri.

## Aturan yang belum pasti

Yang masih menunggu pihak luar tidak menghalangi engine, dan engine tetap menyediakan mekanismenya apa pun
jawabannya nanti: aturan blokir `guard:` yang sebenarnya pada langkah tertentu, apakah langkah terakhir
alur butuh `readyWhen:` supaya hub tidak penuh dokumen yang belum siap, dan apakah kondisi yang mematikan
panel isian suatu langkah tetap di tempatnya. Aturan seperti ini diputuskan modul pemakai, bukan engine;
di sisi modul diberi komentar `// PENDING CONFIRMATION` dan dicatat.

Poin rancangan yang sengaja ditunda ada di bagian [Masih
terbuka](request-perubahan-dan-approval.md#masih-terbuka) rancangan.

## Penutup

Setelah seluruh tahap selesai dan lulus uji dari sisi aplikasi:

1. Dokumentasikan engine approval di `claude.md` repo ini dan `README.md` project yang terpengaruh:
   deklarasi alur, hook, transaksi lintas database, binary storage, hub, Approval Manager.
2. Tandai di rancangan bagian mana yang sudah dibangun dan apa yang berubah saat eksekusi.
3. Tulis report di `doc/report/engine-approval-eksekusi.md` — netral produk: apa yang dibangun, yang berubah
   dari plan, batasan yang diterima (transaksi hanya SQL Server, MAUI hanya client service), dan sisa
   pekerjaan.
4. Pindahkan dokumen ini dan rancangannya ke `plan/executed/`.
