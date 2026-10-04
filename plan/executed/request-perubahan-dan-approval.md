# Request perubahan dan approval (engine)

Status: **sudah dibangun** (eksekusi ditutup 2026-10-01). Dokumen ini dipertahankan sebagai arsip
rancangan: **apa** yang dibangun dan **kenapa**. Cara memakai hasilnya ada di
[../../doc/engine-approval.md](../../doc/engine-approval.md); laporan eksekusinya di
[../../doc/report/engine-approval-eksekusi.md](../../doc/report/engine-approval-eksekusi.md). Bagian
**usulan** dan **masih terbuka** di bawah ditulis sebelum eksekusi — jawabannya ada di report, bukan di sini.
Contoh-contoh yang menyebut modul pemakai (customer, Sales Order) berasal dari aplikasi pertama pemakai engine
ini dan tetap dipakai sebagai bahan pembahasan.

Status awal: **sedang dirancang**. Hasil pembahasan 2026-09-29 (dimulai saat import customer), bahan untuk
menyusun plan. Isinya dibagi tiga: **diputuskan** (sudah dikonfirmasi user), **usulan** (diajukan dan tidak
dibantah, tapi belum dikonfirmasi eksplisit — pastikan sebelum plan ditulis), dan **masih terbuka**.

## Latar belakang

- Gen 1 punya aplikasi `810-SCO Change Customer Data` (CDL:CCD,
  modul perubahan data customer aplikasi generasi sebelumnya): user mengajukan perubahan data
  customer, lalu pemegang role `Approve CDL:CCD` menyetujui, baru data disalin ke tabel asli. Caranya rumit:
  tabel `* Temp`, `CML * Validation Data` (kolom lama/baru), `Customer Data Validation Master` (status
  `UPD CUST`, `NEW CUST`, `NEW CP`, `UPD CP`, `NEW DA`, `UPD DA`, `DEL CUST`).
- Pemakaian nyata (DB lokal, 2026-09-29): ±84 ribu request sejak 2015, 6–11 ribu per tahun, **hampir semua
  disetujui** (void belasan per tahun). Jadi "setujui banyak sekaligus" harus mudah.
- Tabel `* Update Audit` legacy **kosong**; trigger di tabel customer hanya mengisi `ustamp` /
  `msrepl_tran_version`. Request adalah satu-satunya jejak perubahan.
- OBM belum punya fitur audit/snapshot (arsip isi dokumen per revisi dirancang terpisah di
  [data-snapshot.md](../unexecuted/data-snapshot.md)). Koneksi audit PostgreSQL terdaftar di host aplikasi tapi
  belum dipakai. Logger (`PgLogger`) bukan tempat riwayat data bisnis.

### DAS (Document Approval System) Gen 1

Approval dokumen generasi sebelumnya ada di database terpisah, schema `das` (`ta_DasFlow`, `ta_DasFlowAction`,
`ta_DasFlowActionSign`, `ta_DasTransaction`, `ta_DasTransactionFlow`, …), viewer-nya
viewer DAS pada library database aplikasi generasi sebelumnya.

- Hanya **satu alur** ("Sales Order Approval"), dipakai dua dokumen (SOR & SOW), 7 langkah berurutan:
  Prepared By, Checked By, Approved By NSM, Approved By ARE, Approved By GAM, Implemented By, Delivered By.
  Penanda tangan didaftarkan **per orang** per langkah, dengan prioritas, take-over, dan *virtual signee*
  (langkah 2: 12 orang).
- ±58 ribu transaksi sejak 2019. Satu langkah selalu ditandatangani tepat satu orang. Sejak 2025 tidak ada
  yang ditolak. ±8% direvisi (`ReinstateOf`, alur diulang). Lampiran dipakai berat (36 ribu), log/chat
  praktis tidak (55).
- Tanda tangan ditulis dengan mengubah `TextObject` `DasFlowActionId_{n}` di definisi Crystal lalu
  me-render ulang (`Services.Sign`) — terikat Crystal, setiap layout menyiapkan kotaknya sendiri, isinya
  hanya teks. Perilaku per langkah di-hardcode di library bersama (`SorProcessor`, `ActionPanel2..7`).
- Kesimpulan: **tidak ditiru**. OBM membangun engine approval yang baik dari awal.

## Diputuskan

### Cakupan

- **Fitur engine OBM**, bukan bagian import dan bukan khusus CML. Pemakai pertama: CML
  (tahap 5 import customer di aplikasi pemakai), lalu Sales Order
  dan dokumen lain.
- Ada **dua jenis approval** dalam **satu engine**:
  - **Data approval** — data usulan tinggal di request; tabel asli belum berubah sampai disetujui, lalu
    engine menerapkannya. Contoh: CCD (customer, contact, alamat kirim, CFS, relasi, hapus/aktifkan ulang).
  - **Document approval** — data sudah ada di tabel dokumen; request hanya gerbang untuk statusnya.
    Contoh: Sales Order.
- Request yang disetujui sekaligus menjadi jejak audit (peminta, penyetuju, waktu, nilai lama → baru).
- Data approval: pemegang claim approve menyimpan langsung dari layar edit biasa (mis. editor CML), **tanpa
  masuk layar approval**; tercatat sebagai request yang otomatis disetujui. Ditegaskan user 2026-09-29: harus
  masuk layar approval dulu untuk update sendiri pernah dikeluhkan user yang mengerjakan.
- Untuk CML, **semua** perubahan lewat request.
- Boleh lebih dari satu request pending untuk dokumen yang sama; approver bisa menyetujui semuanya atau
  menolak sebagian.
- Arsip CDL:CCD dan DAS lama dibiarkan di database legacy; cara menampilkannya di OBM dipikirkan nanti.

### Tabel (nama `ApprovalRequest`)

Nama tabel memakai `ApprovalRequest`, bukan `ChangeRequest`, karena subjeknya tidak selalu perubahan data.
Tabel ada di database inti aplikasi.

| Tabel | Isi |
| --- | --- |
| `ta_ApprovalRequest` | header: jenis (1 data / 2 document), `DocType`, `DocKey` (kanonik), `DocVersion`, peminta, tahap berjalan, waktu selesai, referensi PDF dasar (nullable), `ReinstateOf` (nullable), `Stage` (0 draft, 1 pending, 2 approved, -1 rejected, -2 cancelled/ditarik, -3 ditarik setelah selesai) + kolom standar |
| `ta_ApprovalRequestStep` | satu baris per langkah: claim, level, penanda tangan & waktu, status (0 menunggu, 1 approved, -1 rejected, -2 dilewati), komentar, kode verifikasi, salinan slot stamp + kolom standar |
| `ta_ApprovalRequestStepSigner` | daftar penanda tangan yang ditentukan untuk langkah ber-`signers` (request, langkah, user) |
| `ta_ApprovalRequestItem` | data approval: entitas yang disentuh, kunci kanonik, operasi (baru/ubah/hapus/aktifkan ulang), status per item + kolom standar |
| `ta_ApprovalRequestItemKey` | bagian-bagian composite key, satu nilai per bagian |
| `ta_ApprovalRequestItemField` | kolom yang berubah: nilai lama, baru, dan nilai di tabel saat di-approve |

- `ItemKey` dan `ItemField` **ramping**: tanpa set kolom standar, PK komposit (item + nama) seperti
  `ta_RoleClaim`. Penyimpangan dari aturan penamaan butir 4 yang disengaja.
- **FK ke `ta_User`** untuk peminta, penanda tangan, atas-nama, dan daftar penanda tangan: approval hanya
  untuk user nyata (lihat [Siapa yang boleh approve](#siapa-yang-boleh-approve)).
- Nilai dan kunci memakai `nvarchar` (tabel legacy `nvarchar`); kunci kanonik `nvarchar(450)` supaya bisa
  di-index.
- Nama entitas diberi namespace module (mis. `Sco.Cml.ContactPerson`).
- Status DB lokal: lima tabel pertama (tanpa `StepSigner` dan tanpa kolom level) sudah dibuat di
  database inti lokal pada 2026-09-29. Draft `ta_ChangeRequest*` sudah dihapus. Belum masuk
  `doc/MainTable.sql`.

### Kunci dokumen (composite key)

- Kunci Gen 1 umumnya composite: `Customer Table` (`Customer ID`, `ID Extension`), `Contact Person`
  (`Customer ID`, `Date Input`, `ID Extension`), `Sales Order` (`SO Number`), `Purchase Order Master`
  (`PO ID`, `PO Issue`).
- Disimpan sebagai **baris per bagian** (`ItemKey`) **plus kunci kanonik** satu string untuk index, cek
  konflik, dan riwayat.
- **Bagian kunci tidak bisa diubah lewat request.** Perubahan kunci = hapus lalu buat baru (di CML, `ID
  Extension` sudah 5 tahun tidak pernah berubah).
- Entitas baru memakai kunci sementara di dalam request; bagian yang belum ada (mis. `Date Input`) diisi
  saat diterapkan.
- Untuk document approval, `Issue` adalah **versi**: `DocKey` = identitas dokumen (`SO Number`),
  `DocVersion` = `Issue Number`, sehingga riwayat semua issue tetap tersambung.

### Claim

- Claim **milik module** pemilik dokumen (`module:nama`), didaftarkan otomatis oleh engine saat module
  mendaftarkan handler, supaya namanya seragam.
- Document approval: **satu claim per langkah**. Mengajukan dijaga claim langkah pertama; tidak ada claim
  `Request` terpisah.
- Pemeriksaan claim di engine dinamis (claim milik module lain, jenisnya dari `DocType`).

### Siapa yang boleh approve

- Akun **admin** (`Defaults.AdminUserId`) dan **debugger** (`Defaults.DebuggerUserId`) **error saat
  approve**. Approval adalah pengecualian dari aturan "admin bisa semua": pemeriksaan hak approval **tidak**
  memakai urutan `HasRequiredClaim` (debug → admin → claim).
- Debugger menguji approval dengan men-set *active user* di UI (bertindak sebagai user nyata).
- User nyata dengan saklar **`cUserIsAdmin` boleh approve**: saklar admin berlaku seperti memegang semua
  claim langkah. Untuk langkah ber-`signers:` yang bukan dirinya, ia tercatat sebagai pengganti (atas nama).
- Tidak ada "admin sebagai fallback penanda tangan". Penanda tangan yang tidak hadir/nonaktif diatasi lewat
  pengganti (take-over) atau membatalkan lalu mengajukan ulang.

### Hub MY TASKS

- Hub **"MY TASKS"** di baris judul `TabbedMainWindow` menjadi wadah umum **daftar pekerjaan user aktif**,
  bukan hanya business task. Sumbernya: business task, document approval, data approval, dan jenis lain
  yang dibuat developer module.
- Hub hanya etalase; keputusan (approve/reject) tidak diambil di hub. Setiap baris membawa aksinya:
  business task (Download · Cancel), approval (Open).
- Tidak ada Retry untuk business task (contoh sebelumnya hanya ilustrasi).
- Approval **dikelompokkan**: bagian per jenis approval, baris per jenis dokumen, dengan jumlah dan umur
  request tertua. Open membuka layar approval yang sudah difilter ke jenis dokumen itu (approve banyak
  sekaligus di sana). Bagian kosong tidak ditampilkan.

```
┌ MY TASKS ─────────────────────────────────────────┐
│ RUNNING                                           │
│  ⟳ Request Get Invoice Data   62%       [Cancel]  │
│  ✓ Export Customer List       selesai  [Download] │
│                                                   │
│ DOCUMENT NEED APPROVAL                        7   │
│  Sales Order         7   tertua 2 hari   [Open]   │
│                                                   │
│ DATA NEED APPROVAL                           12   │
│  Customer (CML)     12   tertua 1 hari   [Open]   │
└───────────────────────────────────────────────────┘
```

### PDF dan stamp

- PDF adalah **kemampuan opsional per jenis dokumen**. CCD tidak butuh PDF (approver melihat daftar
  perubahan lama → baru); dokumen seperti SO butuh PDF.
- Report tidak tahu soal approval: module menghasilkan PDF lewat `GetReport_*`, engine menempelkan stamp.
- Library: **PdfSharp** (MIT) lebih dulu.
- Posisi stamp **tetap**, dideklarasikan **developer di kode module** pemilik dokumen (mis. SO di
  modul Sales Order), per langkah (slot: halaman, x, y, lebar, tinggi). Semua dokumen aplikasi pertama punya kolom tanda
  tangan.
- PDF dasar **dibekukan saat diajukan**, dan slot **disalin ke request saat diajukan**: perubahan desain
  (layout atau posisi) hanya berlaku untuk approval baru. Revisi = versi baru = layout & slot terbaru.

### Penyimpanan biner

- CDN **tidak dipakai**: CDN OBM publik tanpa password.
- Fitur engine baru **local binary storage**: `builder.AddLocalBinaryStorage("./data/binary")`, sejajar
  `EnableCdn`, tanpa endpoint publik. Isi diunduh lewat action pemakainya yang memeriksa hak.
- Objek diakses lewat key berbentuk path yang juga sah sebagai key S3 (mis.
  `approval/2026/09/{requestId}/v1.pdf`) dan tidak pernah ditimpa. Implementasi S3-compatible menyusul
  (masih lama) tanpa mengubah pemakainya.
- `ObjectStorages` Gen 1 (`storages.ta_BinaryContent`) tidak dipakai.

### Alur Sales Order

| Level | Langkah | Penanda tangan |
| --- | --- | --- |
| 1 | Prepared By | peminta (admin SO), ditandatangani otomatis saat submit |
| 2 | Checked By | **`Sales Initiator`** SO → user OBM |
| 3 | Approved By NSM · ARE · GAM | **paralel**, ketiganya wajib; pemegang claim masing-masing |
| — | level 3 lengkap | **status SO → Approved**; baru boleh turun ke PIC (produksi) |
| 4 | Implemented By (PIC) | pemegang claim; menunggu level 3 lengkap |
| 5 | Delivered By (STR) | pemegang claim; setelah PIC implemented, ditandatangani saat barang dikirim |
| — | selesai | alur approval selesai (SO sudah dikirim) |

- Dikonfirmasi user 2026-09-29 dari PDF SO Gen 1 (kotak tanda tangan: Prepared by SOE, Checked by
  SAC/SAE, Approved by NSM · ARE · GAM, Implemented by PIC, Delivered by STR): SO baru turun ke PIC setelah
  status Approved (100% level approve), dan STR baru bisa tanda tangan setelah PIC implemented. Karena
  Delivered By menunggu pengiriman, request SO bisa pending berbulan-bulan (dokumen tetap terkunci;
  perubahan lewat reinstate). Stamp pengganti Gen 1 tercetak "TPC Signed, by: …" — padanan *a.n.* kita.
- Engine mendukung **hook per level** `onCompleted:` (di dalam transaksi) untuk tonggak di tengah alur;
  SO memakainya di level 3 untuk `MarkSoApproved`.
- Data DB lokal: transaksi DAS yang berhenti di Delivered By (Implemented sudah, Delivered belum) hanya
  satuan per tahun sampai 2022, lalu 103 (2023), 355 (2024), 421 (2025), 555 (2026) — diduga bug sejak
  2023; user mengonfirmasi ke STR (lihat poin terbuka 14).
- Langkah dikelompokkan dalam **level**; level berikutnya mulai setelah semua langkah di level itu
  disetujui.
- Sales **wajib punya user OBM** (jalur `Sales Initiator` → `ta_Emp` → contact → `ta_User`); kalau tidak,
  submit approval **error**.

### Rancangan sisi server (disimulasikan untuk SO, disetujui)

**Penanda tangan**

- Langkah bisa punya `signers:` — **penentu daftar user**, bukan predikat `canSign` — supaya hub cukup join
  SQL tanpa memuat dokumen. Langkah tanpa `signers:` terbuka untuk semua pemegang claim-nya.
- **Semua penanda tangan dinamis ditentukan sekaligus saat submit** dan disimpan di
  `ta_ApprovalRequestStepSigner`. Error muncul di depan (ke peminta), bukan menggagalkan Approve orang lain
  di tengah alur. Aman karena dokumen terkunci selama approval.
- Checked By SO: `signers:` membaca `[Sales Initiator]` lalu memanggil helper engine
  `ctx.Users.ByEmpIdAsync(empId)` (`ta_Emp.cEmpId` → `cContactId` → `ta_User` aktif). Tidak ada user aktif →
  409 dan submit batal; lebih dari satu user → 409.
- Saat sign, engine **membandingkan dengan daftar tersimpan**, tidak membaca ulang dokumen legacy:
  - di daftar + memegang claim → tanda tangan biasa;
  - pemegang claim lain (termasuk user `cUserIsAdmin`) → **pengganti**, tercatat atas nama penanda tangan
    utama (`ta_ApprovalRequestStep.OnBehalf_cUserId`), alasan wajib. Stamp: *APPROVED · user2 a.n. sales-x*;
  - tidak memegang claim → 403, walau tercantum di daftar.
- Opsi `strict: true` per langkah menolak pengganti (403). Delegasi berjangka (tabel + layar) menyusul bila
  dibutuhkan.
- User dinonaktifkan di tengah alur: pengganti, atau request dibatalkan lalu diajukan ulang.
- **Four-eyes per langkah** (`distinctFrom:`, diputuskan 2026-09-29): penanda tangan langkah ini tidak boleh
  sama dengan penanda tangan langkah yang disebut. Dicek saat submit terhadap daftar penanda tangan (sama →
  409, submit batal) dan saat sign terhadap penanda tangan sebenarnya, termasuk pengganti (sama → 403).
  **User `cUserIsAdmin` dikecualikan**: boleh menandatangani kedua langkah (sejalan dengan "saklar admin
  berlaku seperti memegang semua claim"); pengecualian ini berlaku di engine untuk semua `distinctFrom`,
  bukan opsi per langkah. Default tanpa four-eyes. Pemakai pertama: SO Checked By
  `distinctFrom: PreparedBy` — yang menyiapkan SO tidak boleh sekaligus `Sales Initiator`-nya, kecuali
  user `cUserIsAdmin`. Menyalakan `cUserIsAdmin` untuk admin SO adalah keputusan dan tanggung jawab admin
  OBM; engine tidak menambah pemeriksaan lain.

**Kunci dokumen**

- **Kunci bertipe**: module mendeklarasikan record kunci dengan atribut `KeyPart` (mis.
  `CustomerKey(CustomerId, IdExtension)`, `SorKey(SoNumber)`); handler menerima record itu, engine yang
  menerjemahkan. Format kanonik: array JSON sesuai urutan bagian (`["A0001","0620212XX"]`), format per tipe
  dibakukan (tanggal-jam `yyyy-MM-ddTHH:mm:ss.fff`).
- `Issue Number` SO legacy bertipe `nvarchar(2)`, jadi `cApprovalRequestDocVersion` menjadi
  **`nvarchar(50)`** (bukan `int`), disimpan apa adanya.
- **Kunci selama approval dijaga lewat engine**: dokumen dianggap terkunci selama ada request pending;
  action simpan module memanggil `IApprovalEngine.EnsureNotInApprovalAsync(...)`. Module **tidak menulis
  status ke tabel legacy** (kolom legacy `Sales Order.cDasTransactionId` dibiarkan).

**Deklarasi flow** di `Extensions.cs` module:

```csharp
builder.AddDocumentApproval<SorServices, SorKey>(SorApproval.DocType, flow => {
   flow.Level(1, l => l.Step(SorApproval.PreparedBy, Slot(15, 245),
      signers: ctx => [ctx.RequesterId]));
   flow.Level(2, l => l.Step(SorApproval.CheckedBy, Slot(52, 245),
      distinctFrom: SorApproval.PreparedBy,
      signers: async ctx => {
         var so = await ctx.Services.LoadSoAsync(ctx.DocKey.SoNumber);
         return [await ctx.Users.ByEmpIdAsync(so.SalesInitiator)];
      }));
   flow.Level(3, l => {
      l.Step(SorApproval.Nsm, Slot(89, 245));
      l.Step(SorApproval.Are, Slot(126, 245));
      l.Step(SorApproval.Gam, Slot(163, 245));
   // di dalam transaksi approval: gagal → seluruh keputusan di-rollback
   }, onCompleted: ctx => ctx.Services.MarkSoApprovedAsync(ctx.DocKey.SoNumber, ctx.DocVersion));
   flow.Level(4, l => l.Step(SorApproval.Pic, Slot(15, 265)));
   flow.Level(5, l => l.Step(SorApproval.Str, Slot(52, 265)));

   flow.Pdf(ctx => ctx.Services.GetReport_Sor(ctx.DocKey.SoNumber, ctx.DocVersion));
   // SO tidak butuh OnRejecting: bisa diedit lagi karena kunci dari engine
});
```

Nama langkah (konstanta `SorApproval.*` di project model modul pemakai) menjadi sumber nama claim per langkah.

**Pengajuan: action module**

action pengajuan di service modul pemakai (`PostMeta_Sor_RequestApproval(string soId, string soIssue)`), dijaga claim langkah
pertama, timeout panjang seperti report (`ReportTimeoutSecond`, karena render Crystal bisa menit-an).
Module memvalidasi hal yang hanya ia pahami (issue ada, tidak void, issue terakhir), lalu memanggil
`IApprovalEngine.SubmitAsync(this, DocType, new SorKey(soId), soIssue, note)`. Untuk data approval, module
juga yang menghitung perubahan lama → baru.

Isi `SubmitAsync` (engine, `Em.Api.Core`):

1. Identitas: user nyata (bukan admin sistem/debugger), ada dan aktif di `ta_User`.
2. Claim langkah pertama.
3. Tidak ada request pending untuk dokumen + versi yang sama (409). Penjaga balapan: **unique filtered
   index** `(DocType, DocKey, DocVersion) WHERE Stage = 1`.
4. Tentukan semua penanda tangan dinamis.
5. Render PDF dan simpan ke local binary storage **di luar transaksi**.
6. Satu transaksi database inti: header, semua langkah (salinan slot di `json_object`), daftar penanda
   tangan, tanda tangan otomatis level 1 oleh peminta, maju ke level 2. Gagal → rollback dan PDF dihapus.
7. Setelah commit: hook `OnSubmitted` (SO tidak butuh apa-apa).

Hasil: header `Stage` 1 / level 2; 7 baris langkah (Prepared By approved, sisanya menunggu); penanda tangan
Prepared By → peminta, Checked By → user `Sales Initiator`; PDF dasar di
`approval/{yyyy}/{MM}/{id}/v{issue}.pdf`.

**Keputusan (sign): action engine, bukan action module**

Tidak ada `PostMeta_Soe_Sign`. Satu action untuk semua jenis dokumen, dipakai layar approval (banyak
sekaligus):

```csharp
Task<ApprovalDecisionResult[]> PostGetMeta_ApprovalDecide(ApprovalDecision[] decisions);
// ApprovalDecision: cApprovalRequestId, StepName (wajib untuk level paralel), Approve, Note
// ApprovalDecisionResult: cApprovalRequestId, Success, ErrorMessage, Request (status terbaru)
```

- Setiap keputusan diproses dalam transaksinya sendiri; yang gagal tidak membatalkan yang lain.
- Urutan per keputusan: user nyata → request masih pending → langkah ada di level berjalan dan masih
  menunggu → claim → daftar penanda tangan / pengganti (alasan wajib) → alasan wajib saat reject → hook
  module `OnSigning` (validasi) → tulis keputusan dengan `WHERE Stage = 0` (dua orang menekan bersamaan →
  yang kalah 409) → reject: langkah terbuka jadi `-2`, request `-1`; approve: level lengkap → maju, habis
  → selesai → hook `OnFinishing` / `OnRejecting` (di dalam transaksi) → commit → hook `OnFinished` /
  `OnRejected` (sesudah commit). Lihat [Atomisitas](#atomisitas-penerapan-dan-hook).
- Engine lain (`IApprovalServices`, `Em.Libs`): `GetMeta_ApprovalRequestsByDoc`,
  `GetMeta_ApprovalRequest`, `GetMeta_ApprovalRequestPdf` (`Task<Stream>`, cek hak dulu),
  `PostMeta_ApprovalCancel`. Client mengambil status terbaru setelah submit lewat
  `GetMeta_ApprovalRequestsByDoc`.
- **Penolakan** di langkah mana pun (termasuk salah satu langkah paralel) menghentikan seluruh request;
  dokumen kembali bisa diedit; revisi = issue baru = alur dari awal.

### Konflik data approval

Diputuskan 2026-09-29 sebagai **percobaan pertama** (boleh ditinjau ulang setelah dipakai).

- Saat approve, engine membandingkan **per kolom** tiga nilai: **lama** (dicatat saat request diajukan),
  **sekarang** (isi tabel legacy), dan **usulan**. Pembandingnya isi tabel, bukan request lain, sehingga
  perubahan dari luar OBM (mis. aplikasi Gen 1) ikut tertangkap.
  - sekarang = lama → diterapkan;
  - sekarang = usulan → kolom dilewati (sudah sama), bukan konflik;
  - selain itu → **konflik**.
- Dua request yang mengubah kolom berbeda pada entitas yang sama tidak saling konflik.
- Request yang konflik gagal sendiri dengan hasil `Conflict` di `ApprovalDecisionResult`; keputusan lain
  dalam batch yang sama tetap jalan. Layar approval menampilkan nilai lama/sekarang/usulan; approver memilih
  **tetap terapkan** (flag `Override` di `ApprovalDecision`, tercatat sebagai timpa sadar) atau **tolak**.
- Entitas yang diubah/dihapus sudah tidak ada → konflik yang **tidak bisa di-override**, hanya tolak.
- Pengecekan yang sama berlaku untuk simpan langsung oleh pemegang claim approve (request otomatis
  disetujui): nilai lama diambil saat layar edit dimuat, jadi berfungsi sebagai optimistic concurrency.
- Nilai sekarang disimpan di kolom `ItemField` "nilai di tabel saat di-approve".

### Isian per langkah (document approval)

Diputuskan 2026-09-29. DAS Gen 1 punya panel isian di tiga langkah SO (`SorProcessor`, `ActionPanel4..6`):

| Langkah | Isian Gen 1 | Ke mana |
| --- | --- | --- |
| ARE (`ActionPanel4`) | *OK, Process SO* / *DO NOT Process SO*, masing-masing dengan checklist alasan (terms of payment dijelaskan, DP diterima, reputable, good payment, jangan kirim sebelum lunas, lain-lain teks / DP belum, invoice belum dibayar + nominal Rp, bad payment, lain-lain teks) | dicentang di PDF |
| GAM (`ActionPanel5`) | checkbox wajib dijawab: proses SO tapi **jangan kirim** sampai final payment | dicentang di PDF |
| PIC (`ActionPanel6`) | opsional *Change Final Delivery Date* + tanggal; panel mati bila Special Delivery + Penalty | ditulis ke `SO Issue Table.Delivery_Date` dan PDF |

- `ApprovalDecision` mendapat **`Payload`** (JSON milik module); module memvalidasinya di `OnSigning`.
- Hook per langkah **di dalam transaksi** (`OnSigned`) untuk menulis ke legacy (PIC → Delivery Date);
  gagal → tanda tangan ikut batal (sejalan dengan [Atomisitas](#atomisitas-penerapan-dan-hook)).
- Selain slot stamp, module mendeklarasikan **slot isian** (checkbox/teks di posisi tetap) yang digambar
  engine dari payload — pengganti `TextObject` Crystal (`ChkProcessSOByAre`, `txtFinalDeliveryDateByPic`, …).
- **Panel isian UI** disediakan module dan dipasang di layar approval untuk langkah itu.
- **Approve banyak sekaligus** hanya untuk langkah tanpa isian wajib; langkah berisian diputuskan satu per
  satu lewat panelnya.
- "DO NOT Process SO" di ARE = **reject beralasan terstruktur**: checklist alasan masuk payload dan tercetak
  di PDF, request berhenti seperti penolakan biasa.

### Prepared By dan pengganti di hub

Diputuskan 2026-09-29.

- **Prepared By satu tanda tangan**: ditandatangani otomatis oleh yang submit; siapa pun pemegang claim
  Prepared By boleh submit. Tidak ada beberapa tanda tangan per langkah.
- **Hub hanya menampilkan item ke penanda tangan tercatat** untuk langkah ber-`signers:` (mis. Checked By →
  hanya Sales Initiator). Pemegang claim lain (termasuk `cUserIsAdmin`) tidak melihatnya di hub; mereka
  membuka dari layar approval (filter "bisa saya tanda tangani sebagai pengganti") dan menandatangani atas
  nama dengan alasan. Eskalasi setelah N hari menyusul bila dibutuhkan.

### Reinstate (tarik kembali lalu ajukan ulang)

Diputuskan 2026-09-29. Gen 1 (`830-SOE ctl_MasterForm.cs`, role `Reinstate SO after Finished`) mengizinkan
SO diedit selama approval berjalan lalu *reinstate*: PDF dirender ulang untuk **issue yang sama** dan alur
DAS diulang (`ReinstateOf` naik). Di OBM:

- **Kunci dokumen tetap**: approver menandatangani isi yang sama dengan yang mereka lihat.
- Reinstate = dua langkah: **tarik kembali** (batalkan request pending, alasan wajib, `Stage -2`, dokumen
  terbuka) → edit → **ajukan ulang issue yang sama**. Request baru (PDF baru, tanda tangan dari awal)
  menyimpan tautan **`ReinstateOf` → request sebelumnya** (kolom baru di `ta_ApprovalRequest`), jadi riwayat
  tersambung.
- **Siapa**: pemegang claim langkah pertama (SO: Prepared By — semua admin SO, tidak harus yang mengajukan)
  dan user `cUserIsAdmin`. Tidak ada claim `Reinstate` terpisah.
- **Juga setelah approval 100%** (seperti role Gen 1 `Reinstate SO after Finished`): request yang sudah
  approved boleh ditarik untuk issue yang sama. Request lama diberi `Stage` **-3 (ditarik setelah
  selesai)** — negatif sesuai konvensi, tetap tersimpan sebagai riwayat — dan hook module **`OnReinstating`**
  (di dalam transaksi) mencabut status Approved di tabel legacy serta boleh menolak bila dokumen sudah
  diproses lebih lanjut (SO: mis. sudah `Process_To_PSJ_NTM`, void). Isian langkah yang sudah ditulis ke
  legacy (mis. Delivery Date dari PIC) tidak dikembalikan.
- "Revisi = issue baru" tetap tersedia; reinstate adalah jalur untuk memperbaiki issue yang sama.
- Pengecekan module sebelum mengajukan (Gen 1: DP sudah diisi di COP Deposit, diskon dalam batas IDK)
  berjalan sama pada pengajuan ulang.

### Komentar dan timeline

Diputuskan 2026-09-29. Di Gen 1 yang dipakai adalah komentar pada aksi tanda tangan (mis. Checked By: "Delivery
date 18 Maret 2025, thanks"); chat DAS terpisah hampir tak terpakai (55 pesan).

- **Komentar keputusan**: `Note` di `ApprovalDecision`, disimpan di kolom komentar `ta_ApprovalRequestStep`.
  Opsional saat approve, wajib saat reject dan saat tanda tangan sebagai pengganti. Satu komentar opsional
  untuk seluruh batch saat approve banyak. Tidak dicetak di stamp PDF.
- **Komentar bebas**: siapa saja yang bisa membuka request boleh berkomentar, termasuk yang bukan penanda
  tangan (mis. CFO). Disimpan di tabel anak **`ta_ApprovalRequestComment`** (request, user → FK `ta_User`,
  waktu, teks). Teks biasa, tidak bisa diedit/dihapus (koreksi = komentar baru), boleh ditulis setelah
  request selesai/ditolak.
- **Kartu timeline bawaan engine** di layar approval dan panel status approval layar dokumen: aksi
  (diajukan, ditandatangani, ditolak, pengganti, ditarik) + komentar keputusan dari tabel request/langkah,
  digabung dengan komentar bebas; tersambung lintas reinstate lewat `ReinstateOf`.
- Notifikasi komentar baru dibahas bersama poin terbuka 7.

### Control approval bersama (approval + monitor)

Diputuskan 2026-09-29: layar approval, layar monitor (padanan list DAS: dokumen, nomor, reinstate ke-,
tanggal, progress, total/pending tanda tangan, langkah menunggu, aksi terakhir + pelaku + waktu, PIC,
ringkasan) dan panel status approval di layar dokumen adalah **satu control yang dipakai bersama**, bukan
beberapa layar. Harus fleksibel.

- Control diberi parameter: mode (**perlu tindakan saya** / **semua** yang boleh dilihat), filter jenis
  dokumen, filter dokumen tertentu (`DocType` + `DocKey`), kolom/kartu yang tampil. Dipasang di layar
  navigasi sendiri (dibuka dari hub), ditanam di layar module (mis. tab Approval di layar SO, difilter ke
  SO itu), atau di layar module lain yang butuh.
- Isi: grid request (kolom standar engine + **kolom ringkasan module**), kartu di kanan (PDF, timeline,
  kartu informasi module), tombol keputusan (aktif hanya bila baris menunggu user aktif).
- **Kolom ringkasan module** (`flow.Summary(...)`, mis. Customer, Sales, Total) dihitung saat diajukan dan
  saat reinstate, disimpan di `json_object` request; sort/filter di server (`JSON_VALUE`) dengan paging.
  Potret saat diajukan — cocok karena dokumen terkunci selama approval.
- Filter oleh user (grouping dan pilihan kolom oleh user **dibuang**, lihat poin 17).
- **Mode buka dokumen** (padanan tab *Document Approval* Gen 1): PDF besar dengan toolbar viewer core
  (refresh, save as, print, find, halaman, zoom), Approve/Reject di dalam viewer, kartu di kanan (isian
  langkah, timeline, kartu informasi, lampiran), dan Sebelumnya/Berikutnya menjelajah daftar yang difilter
  (setelah keputusan pindah ke berikutnya). Bisa dibuka sebagai tab sendiri.
- **Approve banyak diatur per jenis dokumen**: data approval (CML) boleh dari grid; document approval (SO)
  harus lewat viewer (`flow.RequireOpen()`), supaya dokumen benar-benar dilihat seperti di Gen 1. Dijaga
  **di UI saja** (server tidak mencatat dokumen sudah dibuka). Kelak mungkin ada "approve selected" untuk SO.
- **Daftar request: `ListView` + material design, tanpa DevExpress** (poin 16, diputuskan 2026-09-29) —
  seperti manager lain; ikuti pola User Manager (toolbar, quick filter, filter sheet + chip filter aktif,
  paging dan filter di server).
- **Poin 17, diputuskan 2026-09-29**: grouping dan pilihan kolom oleh user **dibuang** — kolom (standar
  engine + ringkasan module) ditentukan developer; pengelompokan cukup lewat quick filter jenis dokumen atau
  Open dari hub yang sudah difilter. Menyimpan filter terakhir per user **menyusul**, bukan tahap awal.

### Lampiran bukan fitur engine

Diputuskan 2026-09-29. Lampiran adalah milik dokumen (module) dan tampil di control approval lewat **kartu
informasi** module; engine global tidak punya fitur lampiran dan tidak terpengaruh. Tabel
`ta_ApprovalRequestAttachment` tidak dibuat (bisa dipertimbangkan lagi bila kelak ada dokumen tanpa tempat
lampiran sendiri).

- **PDF dokumen** (milik engine): print boleh; save as/unduh tidak — isi approval tidak dibuka di luar
  aplikasi (koreksi bila Save As PDF Gen 1 tetap dibutuhkan).

**Catatan untuk import SO** dipindah 2026-09-29 ke plan import SO
(plan import Sales Order di aplikasi pemakai, bagian lampiran ditunda).
Lampiran SO (viewer, editor, penyimpanan) masih harus dibahas di sana.

### Jenis dokumen (`ta_Doc`)

Diputuskan 2026-09-29 (poin terbuka 8). Aturan Id dokumen OBM sudah masuk `CLAUDE.md` root: `cDocAbv` tepat
7 char (kurang → prefix `#`), Id dokumen = `cDocAbv` + `-` + ULID (`char(34)`).

- `DocType` approval merujuk baris `ta_Doc`. Dokumen legacy hanya memakai **jenisnya** dari `ta_Doc`;
  kuncinya tetap kunci legacy di `DocKey` (tidak dibuatkan Id `abv-ULID`).
- **Sales Order**: `cDocName` = `SalesOrder`, `cDocAbv` = `#SORDER`. SOR/SOW adalah **atribut SO**
  (`SO_Type`), bukan jenis dokumen terpisah — tampil sebagai kolom ringkasan (`flow.Summary`) untuk
  filter/grouping, bukan baris hub terpisah.
- Diputuskan: **unique index di `cDocAbv`**; `DocType` approval = **FK ke `ta_Doc.cDocName`** (seperti
  `ta_ProductTran`).
- Diputuskan: baris `ta_Doc` diisi **manual oleh admin OBM lewat SQL**; engine tidak membuatnya dan belum
  perlu pemeriksaan tambahan (FK sudah menolak `DocType` yang tidak terdaftar). Module merujuk `cDocName`
  di deklarasi flow. Tabel sudah diisi user.
- **Satu jenis dokumen, satu bentuk kunci** (dikonfirmasi user): bila SO ditulis ulang di OBM, setiap SO
  mendapat satu Id (`#SORDER-ULID`, satu per SO, bukan per issue — issue tetap `DocVersion`) dan nomor SO
  legacy menjadi `RefNumber`. Saat migrasi itu, `DocKey` request approval `SalesOrder` lama diganti dari
  nomor legacy ke Id baru (dicocokkan lewat `RefNumber`) oleh helper migrasi module, sehingga riwayat tetap
  satu bentuk kunci. Hanya data yang diubah; aturan dan skema engine tidak.
- Prinsip user untuk dokumen OBM: **satu tabel, satu Id, banyak atribut** — tidak meniru composite id dan
  tabel issue terpisah Gen 1 (SO + SO Issue). Untuk dokumen OBM, `DocVersion` approval = kolom standar
  `Revision`; isi revisi lama diarsip [data snapshot](../unexecuted/data-snapshot.md). Perlu diselaraskan saat dokumen OBM
  pertama memakai approval: data snapshot menaikkan `Revision` setiap kali dokumen kembali ke draft,
  sedangkan reinstate (tarik → edit → ajukan ulang) dirancang untuk revisi yang sama.

### Hak melihat request (Approval Manager)

Diputuskan 2026-09-29. Control approval bersama disebut **Approval Manager**.

- User **tanpa hak approve tidak bisa melihat** request suatu jenis dokumen, **kecuali** memegang claim
  lihat **`{cDocName}:View`** (mis. `SalesOrder:View`) — untuk pembaca seperti CFO.
- Yang bisa melihat request: pemegang claim langkah mana pun di alur jenis dokumen itu (termasuk peminta,
  yang memegang claim langkah pertama), pemegang `{cDocName}:View`, dan user `cUserIsAdmin`.
- Hak berkomentar = hak melihat.
- Claim `View` didaftarkan otomatis oleh engine per jenis dokumen, seperti claim langkah.
- Berlaku untuk semua action baca engine (daftar, detail, PDF, timeline) dan untuk isi Approval Manager.
- Kerahasiaan PDF bagi sebagian pembaca (poin 21) ditunda; untuk sekarang yang boleh melihat request
  melihat seluruh isinya.

### Claim data approval CML

Diputuskan 2026-09-29.

- Claim approve data approval CML: **`CML:Approve Update`** (module CML, nama claim `Approve Update`),
  didaftarkan otomatis oleh engine dari deklarasi module. Pemegangnya menyimpan langsung dan memutuskan
  request orang lain.
- Claim yang sama mencakup **semua** perubahan CML, termasuk hapus dan aktifkan ulang customer (seperti
  role tunggal `Approve CDL:CCD` Gen 1). Tidak ada claim approve kedua.
- Pedoman umum: nama claim **diusahakan singkat**.
- Tidak ada claim `Request` terpisah: pengajuan dijaga claim action simpan milik module (sementara
  `CmlClaims.Access`, lihat poin 16 plan import CML).

### Approve per request utuh

Diputuskan 2026-09-29: satu request disetujui atau ditolak **utuh**; tidak ada approve sebagian (terlalu
rumit — butuh deklarasi ketergantungan antar item, mis. alamat kirim baru yang merujuk contact baru).

- Approver yang hanya setuju sebagian menolak dengan alasan; peminta mengajukan ulang.
- `Stage` per item (`ta_ApprovalRequestItem`) mencatat **hasil penerapan**, bukan keputusan approver:
  diterapkan, dilewati (nilai sudah sama), di-override (konflik yang tetap diterapkan). Skema tetap
  menampung approve sebagian bila kelak dibutuhkan.

### Atomisitas penerapan dan hook

Diputuskan 2026-09-29. Menerapkan perubahan ke tabel legacy dan menulis status request di database inti
dikerjakan dalam **satu koneksi dan satu transaksi SQL Server** (transaksi lokal lintas database di satu
instance, tanpa MSDTC). Backend memakai satu login SQL untuk semua database, jadi syaratnya terpenuhi.

- Engine membuka koneksi ke database inti, memulai transaksi, lalu berpindah database (`ChangeDatabase`) saat
  menyerahkan giliran ke module. Context legacy module dipasang ke koneksi + transaksi itu oleh engine
  (`SetDbConnection` + `UseTransaction`); author module tidak mengurus koneksi.
- Tanpa ini, gagal di tengah membuat data rusak diam-diam: legacy sudah berubah tapi request masih pending
  (approve ulang → insert ganda), atau request approved tapi perubahan tidak pernah masuk.
- **Pengaman startup**: saat handler didaftarkan, engine memeriksa database legacy handler berada di server
  yang sama dengan database inti; kalau tidak, API gagal start dengan pesan jelas.
- **Batasan**: sisi penerapan hanya SQL Server (semua database legacy MSSQL).
- **Hook dipecah dua**:

  | Hook | Kapan | Untuk |
  | --- | --- | --- |
  | `OnFinishing` / `OnRejecting` | di dalam transaksi, sebelum commit | menulis ke legacy (penerapan data CML, `MarkSoApproved`); error → rollback semua, request tetap pending |
  | `OnFinished` / `OnRejected` | sesudah commit | efek samping yang tak bisa di-rollback (notifikasi); gagal tidak merusak data |

  Penerapan data approval (handler terapkan milik module) berjalan di tahap yang sama dengan `OnFinishing`.
  `OnSubmitted` tetap sesudah commit.

## Keputusan lanjutan (dulu usulan, dikonfirmasi 2026-09-29)

- **UI bersama**: control approval bersama (lihat [Control approval bersama](#control-approval-bersama-approval--monitor))
  dan panel status approval di `Em.Ui.Wpf.Core`, viewer PDF yang sudah ada. Kontrak & client service di
  `Em.Ui.Core` (aturan *keep the UI cores in step*); control MAUI menyusul bersama hub MAUI (TODO 17).
- **Approval Manager ada di menu Tools** (keputusan user 2026-09-29): layar navigasi bawaan core yang
  memasang control approval bersama, didaftarkan di daftar tool bawaan (`EmApp.GetStaticTools`, sejajar
  User Manager / Role Manager), jadi tampil di menu Tools layout MultiTab dan di home layout satu halaman.
  Disembunyikan bila user tidak boleh membukanya (aturan yang sama dengan menu aplikasi). Selain dari Tools,
  tetap dibuka dari hub (Open, sudah difilter) dan ditanam di layar module.
- **Hub generik**: bentuk item seragam (`HubTaskInfo`: sumber, id, judul, keterangan, jenis, jumlah,
  progres, aksi, target navigasi); sumber server didaftarkan lewat builder dan dikumpulkan satu action
  `GetMeta_UserHubTasks`; business task tetap jalur polling cepatnya sendiri. Item *perlu tindakan*
  **dihitung** dari data sumber (tidak disimpan), jadi hilang sendiri dari hub approver lain begitu satu
  orang memutuskan. **Tidak ada** action aksi hub di server (`PostMeta_HubTaskAction` dibuang): hub hanya
  etalase, approval cukup Open, business task memakai action-nya sendiri.
- **Slot wajib**: langkah pada dokumen ber-PDF tanpa slot → gagal saat build. Lembar pengesahan (halaman
  tambahan berisi tabel semua langkah) bisa dinyalakan per dokumen (`flow.ApprovalSheet()`), bukan cadangan
  otomatis.
- **Isi stamp**: status (APPROVED hijau / REJECTED merah), nama, tanggal-jam, kode verifikasi, plus *a.n.*
  untuk pengganti/penembus. Kode verifikasi juga di margin setiap halaman; QR (`QRCoder`, MIT) menyusul.
  **Kode verifikasi (diputuskan 2026-09-29)**: engine membuat dan menyimpan kode sejak awal; layar cek
  (mis. tool *Verify Document* di menu Tools: masukkan kode → tampil request + PDF asli) **menyusul**.
  Stamp langkah yang ditembus diberi tanda **OVERRIDE** (lihat butir `guard:`).
- **Alat kalibrasi** untuk developer: action debug yang merender PDF contoh dengan kotak slot tergambar.
- **Urutan pengerjaan**: local binary storage (plan sendiri) → engine approval (data approval untuk CML
  dulu, lalu document approval) → stamp PDF → hub MY TASKS generik.

### Hook module per jenis dokumen (poin 19)

**Status 2026-09-29**: kelima hook di tabel bawah (`when:`, `guard:`, `input:`, kartu informasi,
buka dokumen) **diputuskan masuk rancangan**, begitu juga perilaku blokir `guard:` (menunggu + penembus)
dan data live (lihat butir `guard:` di bawah). Contoh kode dan rincian lain di bagian ini masih usulan.

Latar: permintaan CEO dulu — saat ARE mau approve, ia bisa melihat daftar outstanding customer (dan
mungkin tidak bisa approve) — tidak bisa dibuat di Gen 1 karena `ActionPanel` di-hardcode di library
bersama dan hasilnya hanya ke `TextObject` Crystal. CEO minta control tambahan "macam-macam" di layar
approval. Engine menyediakan hook; isinya kode module biasa (service + MVVM).

| Hook | Sisi | Untuk |
| --- | --- | --- |
| `when:` | server | langkah berlaku atau dilewati (`-2`), dievaluasi saat submit |
| `guard:` | server (+ UI) | boleh approve atau tidak, dengan alasan (`ApprovalGuard.Block("...")`) |
| `input:` | server + UI | isian saat tanda tangan (lihat [Isian per langkah](#isian-per-langkah-document-approval)) |
| kartu informasi | UI (+ action module) | control tambahan di Approval Manager, banyak per jenis dokumen |
| buka dokumen | UI | layar dokumen module, read-only |

```csharp
l.Step(SorApproval.Are, Slot(126, 245),
   guard: async ctx => {
      var osd = await ctx.Services.GetOutstandingAsync(ctx.DocKey.SoNumber);
      return osd.Any(r => r.Overdue) ? ApprovalGuard.Block("...") : ApprovalGuard.Allow;
   });

l.Step(SorApproval.Pic, Slot(15, 265), input: new StepInput<Svc, Key, PicPayload> {
   // keadaan awal panel (mis. Special Delivery + Penalty) bukan urusan engine: panel memanggil action modul sendiri
   Validate = (ctx, p) => ...,           // payload divalidasi ulang di server
   OnSigned = (ctx, p) => ...,           // tulis ke legacy, di dalam transaksi
   Fields   = f => f.Text(p => ..., Slot(...)),   // slot isian di PDF
});

// frontend module
builder.AddApprovalStepPanel<PicPanel, PicPanelVm>("SalesOrder", SorApproval.Pic);
builder.AddApprovalInfoPanel<OutstandingPanel>("SalesOrder",
   steps: [SorApproval.Are, SorApproval.Gam],
   input: doc => new OutstandingPanelInput(doc.CustomerId),
   claim: AreClaims.Outstanding, order: 1);
```

- **`guard:`**: di UI tombol Approve mati + alasan tampil, Reject tetap aktif; di server dijalankan ulang di
  `PostGetMeta_ApprovalDecide` sebelum keputusan ditulis (403/409 + alasan); approve banyak → yang
  terblokir gagal sendiri. Aturan blokir sepenuhnya kode module.
  **Perilaku blokir — diputuskan 2026-09-29: keduanya.** Contoh user: pembayaran bisa saja masuk ke sistem
  1 jam setelah SO sampai di ARE, atau CFO memaksa OK.
  - **Menunggu**: request tetap di langkah itu; `guard:` dievaluasi ulang setiap kali layar approval dibuka
    atau dimuat ulang, jadi begitu syaratnya terpenuhi tombol Approve aktif sendiri.
  - **Penembus**: module mendeklarasikan claim penembus pada aturan blokirnya; **siapa pun pemegang claim
    itu** adalah penembus (CFO hanya contoh, bukan jabatan yang di-hardcode; untuk ARE rencananya claim
    diberikan ke CFO lewat Role Manager). Karena bersifat darurat, stamp langkah yang ditembus
    diberi tanda **OVERRIDE** (diputuskan 2026-09-29). Pemberitahuan ke ARE/peminta dan filter khusus di
    Approval Manager **tidak** dibuat sekarang (pemberitahuan ikut poin 7).
    Pemegangnya boleh tetap approve dengan **alasan wajib**, tercatat di timeline dan tercetak di stamp.
    `cUserIsAdmin` ikut aturan "admin bisa semua". Bentuk API-nya (mis.
    `ApprovalGuard.Block("...", overridableBy: ...)`) masih usulan.
  - **Penembus approve atas nama penanda tangan langkah** (diputuskan 2026-09-29, contoh user: *CFO approve
    a.n. ARE*). Langkah ARE selesai dengan tanda tangan CFO; tidak ada izin terpisah lalu ARE approve. Menembus blokir bersifat **darurat**.
    Memakai mekanisme **pengganti** yang sudah ada (lihat [Penanda tangan](#rancangan-sisi-server-disimulasikan-untuk-so-disetujui)):
    `OnBehalf_cUserId`, alasan wajib, stamp *APPROVED · cfo a.n. are*. Bedanya dengan pengganti biasa:
    penembus tidak perlu memegang claim langkah, cukup claim penembus, dan `strict: true` tidak menolaknya.
    Usulan untuk langkah tanpa `signers:` (penanda tangannya siapa pun pemegang claim, tidak ada nama
    tercatat): stamp menulis nama langkahnya (*a.n. ARE*) dan `OnBehalf_cUserId` kosong.
  **Data live** — disimpulkan dari contoh yang sama (menunggu hanya masuk akal kalau `guard:` membaca
  kondisi saat ini): `guard:` dan kartu informasi membaca data saat itu, bukan data yang dibekukan saat
  submit (Gen 1 menyiapkan outstanding saat submit, `executeOsdInvAsyncTask`, kemungkinan karena Crystal).
  Hasil evaluasi `guard:` (lolos/blokir + alasan) disimpan bersama keputusan sebagai jejak audit (usulan).
  **ARE diblokir** (jawaban user 2026-09-29): ARE tidak boleh tanda
  tangan selama syaratnya belum terpenuhi — **menunggu konfirmasi pembuat aplikasi** (aturan pastinya
  belum ditulis, mis. overdue apa yang memblokir). Engine tetap menyediakan `guard:` apa pun hasilnya.
- **Kartu informasi**: tampil sebagai kartu yang bisa dilipat di samping PDF; per langkah atau semua
  langkah; urutan dari developer (susunan per user menyusul); **dipakai ulang lintas dokumen** — kartu
  dibuat module pemilik datanya (mis. Outstanding oleh ARE, menerima `CustomerId`) dan dipasang dokumen lain
  lewat deklarasi; boleh ber-claim sendiri (tidak tampil bila user tak memegangnya). Contoh pemakaian yang
  sudah disepakati: kartu "Lampiran SO", kartu BDA.
- **Kartu boleh menjalankan action**: action pada data lain (claim action itu tetap berlaku), navigasi ke
  layar lain, dan meminta Approval Manager memuat ulang (`host.RefreshAsync()` → `guard:` dan kartu lain
  dievaluasi ulang). Tidak boleh: mengubah dokumen yang sedang di-approve (terkunci), atau menjadi bagian
  keputusan (itu `input:`). Action kartu berjalan di luar transaksi approval dan dicatat module-nya.
- Hook per level `onCompleted:` (lihat Alur Sales Order) dan `readyWhen:` untuk hub (poin 14) melengkapi
  hook ini.

## Masih terbuka

1. ~~Aturan konflik~~ → diputuskan (dicoba dulu), lihat [Konflik data approval](#konflik-data-approval).
2. ~~Approve utuh atau sebagian~~ → diputuskan utuh, lihat
   [Approve per request utuh](#approve-per-request-utuh).
3. ~~Atomisitas~~ → diputuskan satu transaksi lintas database, lihat
   [Atomisitas penerapan dan hook](#atomisitas-penerapan-dan-hook).
4. ~~Nama claim~~ → satu claim `CML:Approve Update` untuk semua perubahan termasuk hapus/aktifkan ulang,
   lihat [Claim data approval CML](#claim-data-approval-cml).
5. ~~Four-eyes~~ → diputuskan: opsi per langkah `distinctFrom:`, default tanpa; SO Checked By
   `distinctFrom: PreparedBy`; user `cUserIsAdmin` dikecualikan. Lihat [Penanda tangan](#rancangan-sisi-server-disimulasikan-untuk-so-disetujui).
6. ~~"Prepared By 2 orang"~~ → satu tanda tangan, lihat [Prepared By dan pengganti di hub](#prepared-by-dan-pengganti-di-hub).
7. **Notifikasi** ke peminta — **ditunda** (2026-09-29): tahap awal tanpa notifikasi; dibahas lagi setelah
   hub MY TASKS generik selesai. Pilihan saat itu: bagian "MY REQUESTS" yang dihitung dari data, atau item
   *pemberitahuan* yang **disimpan** (status sudah-dibaca lintas perangkat); termasuk komentar baru dan
   pemberitahuan blokir ditembus.
8. ~~`ta_Doc`~~ → diputuskan, lihat [Jenis dokumen](#jenis-dokumen-ta_doc).
9. **DB lokal** perlu disesuaikan: unique index `ta_Doc.cDocAbv`, FK `DocType` → `ta_Doc.cDocName`, kolom level dan `OnBehalf_cUserId` di `ta_ApprovalRequestStep`, tabel
   `ta_ApprovalRequestStepSigner`, FK ke `ta_User`, `DocVersion` → `nvarchar(50)`, dan unique filtered index
   `(DocType, DocKey, DocVersion) WHERE Stage = 1`, kolom `ReinstateOf` di `ta_ApprovalRequest`.
10. ~~Pengganti di hub~~ → hanya tampil ke penanda tangan tercatat, lihat
    [Prepared By dan pengganti di hub](#prepared-by-dan-pengganti-di-hub).
11. ~~Hook sesudah commit tidak atomik~~ → diputuskan: hook dipecah `OnFinishing` (di dalam transaksi) dan
    `OnFinished` (sesudah commit), lihat [Atomisitas penerapan dan hook](#atomisitas-penerapan-dan-hook).
12. ~~Isian khusus per langkah~~ → ada di ARE, GAM, PIC; lihat [Isian per langkah](#isian-per-langkah-document-approval).
    **Ditunda** (2026-09-29, user mengonfirmasi dulu): aturan Special Delivery + Penalty mematikan panel
    PIC tetap dipindah atau tidak. Tidak memengaruhi engine — kalau dipindah, tempatnya action keadaan panel milik
    modul, yang dipanggil panel PIC.
13. ~~Reinstate~~ → pemegang claim langkah pertama + `cUserIsAdmin`, boleh juga setelah 100%; lihat
    [Reinstate](#reinstate-tarik-kembali-lalu-ajukan-ulang).
14. **Delivered By (STR)**: siapa/kapan/lewat layar apa STR menandatangani; apakah SO yang tertahan sejak
    2023–2024 sebenarnya sudah dikirim (bug?); perlukah `readyWhen:` (item STR baru muncul di hub setelah
    ada tanda pengiriman) supaya hub STR tidak penuh SO yang masih produksi. User mengonfirmasi ke STR.
15. **Pool PIC bergantung pada progress DAS** (dibahas nanti; **tidak memengaruhi rancangan engine** — urusan import SO/PIC): aplikasi PIC Gen 1 mengambil data SO beserta
    field progress DAS, dan SO baru muncul di pool PIC bila progress > x%. Kalau approval SO pindah ke OBM,
    sumber progress itu harus disediakan (atau pool PIC ikut pindah). Titik awal pelacakan:
    view `_DT_SO_MASTER_2` (database penjualan aplikasi sebelumnya, merujuk DAS).
16. ~~Kontrol grid~~ → diputuskan 2026-09-29: **tanpa DevExpress**, seperti manager lain — `ListView` +
    material design, pola User Manager. Lihat [Control approval bersama](#control-approval-bersama-approval--monitor).
17. ~~Tampilan tersimpan per user~~ → grouping & pilihan kolom dibuang, simpan filter menyusul; lihat
    [Control approval bersama](#control-approval-bersama-approval--monitor).
18. ~~Hak membuka request~~ → claim `{cDocName}:View`, lihat [Hak melihat request](#hak-melihat-request-approval-manager).
19. **Hook module** `when:` / `guard:` / `input:` / kartu informasi / buka dokumen: ~~masuk rancangan~~ →
    diputuskan masuk; blokir `guard:` = menunggu + penembus (claim dari module, alasan wajib); data live.
    Penembus approve a.n. penanda tangan langkah (CFO a.n. ARE). Lihat [Hook module](#hook-module-per-jenis-dokumen-poin-19).
20. ~~Viewer lampiran~~ → bukan urusan engine; viewer lampiran urusan aplikasi SO, lihat
    [Lampiran bukan fitur engine](#lampiran-bukan-fitur-engine). Tampilan lampiran di Approval Manager
    **dipikirkan setelah control approval jadi** (keputusan user 2026-09-29), bukan sekarang.
21. **Kerahasiaan PDF dokumen** (ditunda; terkait poin 18): apakah PDF dokumen (mis. SO `SO-V2`) bisa berisi data
    rahasia (harga base) yang tidak boleh dilihat sebagian penanda tangan/pembaca (mis. Sales Initiator)?
    Kalau ya, hak melihat PDF dipisah dari hak membuka request. (Special Conditions sudah terjawab: kartu
    lampiran milik SO.)

## Pembahasan berikutnya

**Plan eksekusi sudah ditulis** dan **sedang dieksekusi**:
[engine-approval-eksekusi.md](engine-approval-eksekusi.md) — bagian **engine** saja (tahap E1–E7: kontrak,
engine inti, data approval, binary storage, stamp PDF, Approval Manager, hub), netral produk, ditulis ulang
2026-10-01. Orkestrasi agent, tahap modul pemakai, skrip tabel, data uji, dan skenario uji end-to-end ada
di plan eksekusi milik repo aplikasi pemakai, bukan di sini. Daftar di bawah tinggal riwayat; yang masih
terbuka tidak menghalangi eksekusi.

Disusun ulang 2026-09-29 (akhir sesi kedua). Poin 1–6, 10–13, 20 sudah diputuskan. Urutan mengikuti apa yang
menghalangi pekerjaan pertama (local binary storage → data approval CML → document approval → stamp → hub):

1. ~~Poin 8 — `ta_Doc`~~ — selesai 2026-09-29; tabel DB lokal (poin 9) sudah bisa disesuaikan.
2. ~~Poin 18~~ — selesai 2026-09-29 (`{cDocName}:View`); poin 21 (kerahasiaan PDF) ditunda.
3. ~~**Poin 19 — hook**~~ — selesai 2026-09-29 (kelimanya masuk; blokir = menunggu + penembus a.n.
   penanda tangan langkah; data live).
4. ~~Konfirmasi bagian Usulan~~ — selesai 2026-09-29, lihat
   [Keputusan lanjutan](#keputusan-lanjutan-dulu-usulan-dikonfirmasi-2026-09-29) (Approval Manager di menu
   Tools, `PostMeta_HubTaskAction` dibuang). Kode verifikasi: cetak dulu, cek menyusul.
5. ~~Poin 16–17 — control approval~~ — selesai 2026-09-29.
6. **Poin 7 — notifikasi / MY REQUESTS** — ditunda sampai hub MY TASKS generik selesai.
7. Menunggu pihak luar (bukan penghalang engine): poin 14 (Delivered By — konfirmasi STR), poin 15 (pool PIC),
   aturan blokir ARE (konfirmasi pembuat aplikasi, lihat poin 19), sisa poin 12 (Special Delivery + Penalty
   mematikan panel PIC — konfirmasi user).
8. Data snapshot: poin terbuka di [data-snapshot.md](../unexecuted/data-snapshot.md).

Pekerjaan teknis (bukan keputusan):

- Menyesuaikan tabel di DB lokal (poin 9, ditambah tabel `ta_ApprovalRequestComment`) — setelah poin 8.
  Skrip draft terakhir hanya ada di scratchpad sesi 2026-09-29; susun ulang dari bagian
  [Tabel](#tabel-nama-approvalrequest) dan poin 9.
- Plan terpisah **local binary storage** bisa ditulis kapan saja (keputusannya sudah cukup, lihat
  [Penyimpanan biner](#penyimpanan-biner)).
- Belum di-commit: dokumen ini, `data-snapshot.md`, bagian tentang sistem warisan di
  `claude.md`, dan perubahan dokumen import customer di repo aplikasi pemakai (sudah berubah
  sebelum sesi ini).

