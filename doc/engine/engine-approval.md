# Engine approval — cara memakai dari modul

Panduan untuk penulis modul pemakai. Dokumen ini hanya menjelaskan permukaan publiknya.

Engine menyediakan dua jenis persetujuan. Keduanya memakai satu set tabel request, satu set action
(`core.approval`), satu Approval Manager, dan satu hub MY TASKS.

| | Document approval | Data approval |
| --- | --- | --- |
| Untuk | dokumen transaksi yang ditandatangani beberapa pihak (pesanan, kontrak, …) | perubahan data induk (customer, vendor, …) |
| Dokumen/datanya | **sudah ada**; request menjadi gerbang statusnya | **belum berubah**; usulannya tinggal di request sampai disetujui |
| Langkah | beberapa level, tiap level berisi langkah paralel, tiap langkah punya claim | satu keputusan: pemegang claim persetujuan |
| Hasil | PDF ber-stamp tanda tangan | perubahan diterapkan ke tabel modul, dengan perbandingan tiga nilai |
| Daftar di builder | `AddDocumentApproval<TServices, TKey>(docType, flow)` | `AddDataApproval<TServices>(docType, approveClaim, flow)` |

Semua registrasi hanya di fase builder (`EmAppBuilder`), tidak pernah saat runtime.

## Yang disediakan engine, yang disediakan modul

Engine **tidak tahu tabel modul**. Kunci (termasuk composite key), pemuatan data, penerapan perubahan,
dan validasi disuplai modul lewat deklarasi di `flow`. Jadi modul yang datanya di tabel warisan — tanpa
kolom standar, tanpa id ULID, dengan composite key — bisa memakainya. Kunci dokumen/entitas adalah
`record` yang tiap bagiannya bertanda `[KeyPart(n)]`; engine menerjemahkannya ke bentuk tersimpan dan
kembali, handler modul menerima record apa adanya.

### Document approval

```csharp
builder.AddDocumentApproval<OrderServices, OrderKey>("SalesOrder", flow => {
   flow.Level(1, l => l.Step("Prepared By", slot));              // peminta menandatangani otomatis saat mengajukan
   flow.Level(2, l => l.Step("Checked By", slot, distinctFrom: "Prepared By",
                             signers: async c => [...]));         // id user penanda tangan, dibaca dari isi dokumen
   flow.Level(3, l => {                                           // langkah satu level berjalan paralel
         l.Step("Approved By A", slotA);
         l.Step("Approved By B", slotB, guard: GuardAsync, input: BInput());
      },
      onCompleted: c => Task.CompletedTask);                      // dipanggil saat seluruh level selesai
   flow.Pdf(c => c.Services.RenderAsync(c.DocKey))                // Task<Stream>: PDF dasar yang akan di-stamp
       .PdfLayout((c, pdf) => LocateAsync(pdf))                   // opsional: posisi kotak per dokumen (lihat PDF)
       .Summary(c => c.Services.SummarizeAsync(c.DocKey))         // ringkasan untuk daftar & hub
       .RequireOpen()                                             // dokumen harus masih terbuka saat diajukan
       .OnSigning(...).OnSigned(...).OnFinishing(...).OnFinished(...)
       .OnRejecting(...).OnRejected(...).OnReinstating(...);
});
```

- **Claim** setiap langkah dan claim pembaca (`View {docType}`, lihat `EmAppBuilder.ApprovalViewClaimName`)
  didaftarkan otomatis pada modul `TServices`; jangan mendaftarkannya lagi. Nama langkah menjadi nama claim.
- **Langkah pertama** ditandatangani peminta saat pengajuan dan tidak boleh meminta isian.
- **`signers:`** menetapkan penanda tangan saat pengajuan (`when:` membuat sebuah langkah ikut hanya
  bila syaratnya terpenuhi untuk dokumen itu). Selain penanda tangan itu, pemegang claim yang
  sama boleh menggantikan (wajib beralasan, stamp memuat *a.n.*), kecuali langkah `strict`.
- **`distinctFrom:`** four-eyes: dua langkah tidak boleh ditandatangani orang yang sama (saklar
  administrator mengecualikan).
- **`guard:`** menahan persetujuan sebuah langkah dengan alasan, dan bisa menyebut claim *penembus* —
  pemegangnya boleh menyetujui dengan alasan; stamp memberi tanda OVERRIDE.
- **`input:`** isian per langkah (`StepInput<TServices, TKey, TPayload>`): `Check(slot, ...)` dan
  `Text(slot, ...)` menentukan apa yang digambar ke PDF; `Validate` memeriksa; `OnSigned` menerapkan
  akibatnya ke dokumen **di transaksi yang sama**. Untuk penolakan, isian yang bisa diterima tetap
  digambar; payload yang tidak bisa diterima tidak menggagalkan penolakan. Yang menentukan sah-tidaknya
  penolakan adalah `OnSigning` milik modul.
- **Hook** `OnSigning`/`OnSigned`/`OnFinishing`/`OnRejecting`/`OnReinstating` berjalan di dalam transaksi
  keputusan (gagal = semuanya dibatalkan). `OnFinished`/`OnRejected` berjalan sesudah commit; kegagalannya
  hanya dicatat.
- **Reinstate** (tarik kembali, lalu ajukan ulang): request pending → `-2`, request selesai → `-3`;
  pengajuan ulang tersambung lewat `ReinstateOf`. `OnReinstating` boleh menolak.

### Data approval

```csharp
builder.AddDataApproval<CustomerServices>("Customer", approveClaim: "Approve Update", flow => {
   // urutan `order` = urutan penerapan: induk sebelum anaknya
   flow.Entity<CustomerKey>("Customer", (c, key) => c.Services.LoadAsync(key),
         (c, request) => c.Services.ApplyAsync(request), order: 0)
      .Entity<ContactKey>("Contact", (c, key) => c.Services.LoadContactAsync(key),
         (c, request) => c.Services.ApplyContactAsync(request), order: 1)
      .Summary(c => c.Services.SummarizeAsync(c))
      .OnFinishing(...).OnFinished(...).OnRejecting(...).OnRejected(...);
});
```

- Pemegang claim persetujuan **menyimpan langsung** tanpa menunggu siapa pun (request tercatat sebagai
  disetujui sendiri); yang lain meninggalkan request yang menunggu mereka.
- **Perbandingan tiga nilai** per kolom: *lama* (saat diajukan), *sekarang* (isi tabel saat disetujui),
  *usulan*. Sekarang = lama → diterapkan; sekarang = usulan → dilewati; selain itu **konflik**. Seluruh
  entitas dibandingkan dulu sebelum satu pun diterapkan. Konflik membatalkan keputusan itu dan dicatat
  supaya approver melihat nilai sekarang; approver boleh **menerapkan tetap** (`Override`, wajib
  beralasan, item bertanda *Overridden*) — kecuali entitasnya sudah tidak ada (hanya bisa ditolak).
- Entitas baru: modul mengembalikan kunci sebenarnya dari `apply`; entitas lain dalam request yang masih
  memakai kunci sementara diganti otomatis, jadi induk dan anak boleh berbagi satu request.

## Transaksi lintas database

Satu keputusan = satu koneksi + satu transaksi SQL Server yang mencakup database inti dan database
modul, **tanpa MSDTC** (`ApprovalTransaction`). Context modul yang ikut serta adalah yang diminta service
modulnya lewat **constructor**; context yang diambil lewat `GetService` atau lewat service modul lain
**tidak ikut** transaksi dan tidak diperiksa startup. Semua database yang ikut harus satu server dengan satu
login; `ApprovalStartupChecks` menolak aplikasi yang melanggarnya sebelum request pertama.

## Tabel

Tujuh tabel di database inti aplikasi: request, langkah, penanda tangan langkah, item, kunci item, kolom
item, komentar. Skripnya `doc/sqlscript/mssql/tables/020-approval.sql`, termasuk tabel jenis dokumen
(`ta_Doc`) yang dirujuk FK; baris jenis dokumennya tetap diisi aplikasi/modul pemakai. Dua hal yang merupakan perilaku engine, bukan optimasi:

1. Unique index tersaring pada (jenis dokumen, kunci, versi) `WHERE Stage = Pending` — penjaga balapan
   dua pengajuan bersamaan.
2. Foreign key peminta, penanda tangan, atas-nama, dan daftar penanda tangan ke tabel user — inilah yang
   membuat **akun sistem** (admin bawaan, debugger) gagal menandatangani. Approval adalah pengecualian
   dari aturan "admin bisa semua"; user nyata dengan saklar administrator menyala tetap boleh.

Jenis dokumen harus sudah terdaftar di daftar jenis dokumen aplikasi; yang tidak terdaftar ditolak
database saat request pertama.

## Action (`core.approval`)

Semua `GetMeta_`/`PostMeta_`/`PostGetMeta_` — claim-nya dinamis (per jenis dokumen dan langkah), jadi
service engine didaftarkan `enforceClaims: false` dan memeriksa sendiri.

| Action | Fungsi |
| --- | --- |
| `GetMeta_ApprovalDocumentTypes` | jenis dokumen yang boleh dilihat pemanggil |
| `GetMeta_ApprovalRequests(query)` | daftar berhalaman, dibatasi di server ke jenis yang boleh dilihat |
| `GetMeta_ApprovalRequestsByDoc(docType, docKey)` | seluruh request satu dokumen |
| `GetMeta_ApprovalRequest(id)` | rincian: langkah, perubahan, timeline |
| `GetMeta_ApprovalRequestPdf(id)` | PDF ber-stamp, dibuat saat diminta |
| `GetMeta_ApprovalGuard(id, step)` | status blokir sebuah langkah beserta siapa yang boleh menembus |
| `PostGetMeta_ApprovalDecide(decisions[])` | setujui/tolak; satu transaksi **per keputusan** |
| `PostMeta_ApprovalCancel(id, reason)` | tarik kembali |
| `PostMeta_ApprovalComment(id, note)` | komentar |
| `GetMeta_UserHubTasks` | daftar pekerjaan user aktif (hub) |
| `GetMeta_ApprovalSlotCalibration(...)` | PDF kalibrasi: menggambar seluruh kotak di atas dokumen asli |

**Hak melihat** sebuah request = memegang claim langkah mana pun di alurnya, claim pembaca jenis dokumen
itu, atau saklar administrator. Keputusan yang gagal **tidak** membawa ringkasan request kembali kepada
pemanggil yang tidak boleh melihatnya.

## PDF dan stamp

- `flow.Pdf(...)` menghasilkan PDF dasar saat pengajuan; engine menyimpannya lewat `IBinaryStorage`
  (`builder.AddLocalBinaryStorage(path)` — folder di mesin server, tanpa alamat publik; kunci yang
  mengandung `\`, `.`/`..`, atau path absolut ditolak).
- Tiap langkah punya **slot** (`ApprovalSlot.At(x, y, w, h, page)`, milimeter dari kiri atas halaman).
  Daftar kotak **dibekukan per request saat diajukan**; mengubah deklarasi tidak mengubah request lama.
- **`PdfLayout`** memetakan posisi deklarasi ke posisi nyata di dokumen ini (mis. tabel tanda tangan
  yang bergeser karena jumlah baris; `null` = kotak tidak ada di dokumen ini). Dipanggil saat pengajuan
  dan oleh kalibrasi, dengan salinan PDF di memori.
- `ApprovalSheet()` menambahkan lembar pengesahan untuk langkah yang tidak punya kotak.
- Setiap tanda tangan membawa kode verifikasi; kode terakhir dicetak di tepi halaman. Layar pengecek
  kode belum dibangun.

## UI (WPF)

Approval Manager (`ApprovalManager`, tanpa DevExpress) dipakai tiga cara dengan data yang sama: dari menu
Tools, dari hub (terfilter), dan tertanam di layar dokumen (tab Approval, dengan Sebelumnya/Berikutnya
di mode buka dokumen). Modul pemakai memperluasnya lewat builder UI:

- `AddApprovalStepPanel<TView, TViewModel>(docType, step)` — panel isian untuk satu langkah.
- `AddApprovalInfoPanel<TView>(docType, steps, input, order)` — panel keterangan tambahan.
- `AddApprovalDocumentOpener(docType, navigationName, payloadFactory)` — tombol *Source document*; aktif
  hanya bila pemanggil boleh membuka layar tujuannya.

**Mode ringkas.** `ApprovalManagerNavigationPayload.Compact = true` untuk menanam layar ini di ruang sempit
(mis. flyout di layar modul). Isinya satu kolom: usulan perubahan dan keputusan. Daftar request hanya muncul
sebagai pemilih bila ada lebih dari satu, tautan ke layar lain (Open, Open tab, Source document) dan kartu info
disembunyikan, dan riwayat (timeline) tertutup sampai dibuka. Hosting flyout-nya urusan layar pemakai
(pola `sheetScrimStyle` + `sheetToggleStyle`); contohnya editor CML.

Hub MY TASKS digabung dari semua `IHubTaskSource` yang terdaftar (`AddHubTaskSource<T>()`); sumber
approval sudah terdaftar otomatis. Daftarnya **dihitung**, bukan disimpan, jadi sebuah tugas hilang
sendiri dari daftar orang lain begitu satu orang mengerjakannya. Control approval dan hub belum ada di
MAUI; client service-nya ada.

## Batas yang diketahui

Yang paling perlu diingat
modul pemakai: perbandingan konflik membaca nilai lalu handler membaca ulang barisnya tanpa kunci baris
(ada jendela kecil antara keduanya); dan hook modul yang mengambil context lewat `GetService` tidak ikut
transaksi.
