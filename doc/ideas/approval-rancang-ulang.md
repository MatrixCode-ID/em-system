# Rancang ulang engine approval: tiga jenis approval, engine tanpa konsep dokumen

- **Tanggal:** 2026-10-16
- **Status:** diskusi
- **Plan turunan:** belum ada
- **Catatan terkait:** [approval-display-modul.md](approval-display-modul.md),
  [approval-qr-verifikasi.md](approval-qr-verifikasi.md), [approval-user-lookup.md](approval-user-lookup.md),
  [get-parameter-per-field.md](get-parameter-per-field.md), [object-storage.md](object-storage.md);
  panduan engine yang berlaku sekarang: [doc/engine/engine-approval.md](../engine/engine-approval.md)

Isinya gagasan yang masih dibahas, bukan perintah kerja; agent tidak mengeksekusinya sebelum dijadikan plan di
`plan/unexecuted/`. Catatan ini hanya membahas engine generik. Kebutuhan dan pemakaian di repo produk turunan
dicatat di repo produk itu.

## Gagasan pengguna (2026-10-16)

Approval dibagi menjadi **tiga jenis**:

1. **Data Approval**: approval atas INSERT/UPDATE/DELETE data. Berbasis claim dan **mudah diimplementasikan**
   oleh modul.
2. **Document Approval**: approval atas **dokumen fisik** (docx, xlsx, pdf, dwg, dan sebagainya). Butuh flow
   dengan **step yang dinamis**. UI menyediakan **viewer** dokumen yang di-approve dan **comment**. Cocok untuk
   proyek engineering yang punya banyak dokumen untuk di-approve, dengan **progres dokumen**.
3. **System Approval**: approval yang dipakai aplikasi seperti ERP yang punya UI approval sendiri. UI bisa
   berbeda per entity, jadi **developer membangun UI** di samping flow dan step. UI harus memvalidasi apakah
   user yang login boleh approve atau view.

Prinsip: **engine tidak mengenal "dokumen"**. Engine saat ini dibangun di atas konsep dokumen (jenis dokumen,
kunci dokumen, versi dokumen), padahal yang tahu apa itu dokumen adalah **implementor**. Produk turunan yang
memakai engine yang tahu dokumen apa saja yang dimilikinya; engine tidak.

## Kondisi engine sekarang (dibaca dari kode 2026-10-16)

Ringkasan untuk pegangan diskusi. Rinciannya di [engine-approval.md](../engine/engine-approval.md) dan
`src/backend/Em.Api.Core/Api/Approval/` (sekitar 6.000 baris).

- Dua jenis (`ApprovalKind`): **Data** dan **Document**. Keduanya memakai tabel request, action `core.approval`,
  Approval Manager (WPF), dan hub MY TASKS yang sama.
- **Data approval** (`AddDataApproval<TServices>(docType, approveClaim, flow)`): usulan per entity berupa daftar
  kolom `string?` (old/new) yang disimpan di request. Ada satu keputusan oleh pemegang claim approve, lalu
  pembandingan tiga nilai (original/current/proposed) dengan konflik dan override. Pemegang claim approve
  langsung tersimpan sebagai request self-approved. Modul menyediakan handler `load`/`apply` per entity,
  termasuk kunci sementara untuk entity baru.
- **Document approval** (`AddDocumentApproval<TServices, TKey>(docType, flow)`): flow **dideklarasikan di kode
  saat startup**. Level berurutan, step dalam satu level paralel dan semuanya wajib selesai, satu claim per step
  (**nama step = nama claim**). Tersedia `signers:` (dibekukan saat submit), substitute *on behalf*, `strict`,
  `distinctFrom` (four-eyes), `when:`, `guard` + override claim, dan `input` per step. Dokumennya sudah ada di
  tabel modul; request hanya mengunci statusnya. PDF dasar dirender saat submit lalu diberi stempel lewat slot
  milimeter dan `PdfLayout`.
- Kunci entity berupa record `[KeyPart(n)]` dalam bentuk kanonis, sehingga tabel legacy dengan composite key
  bisa dipakai.
- Satu transaksi SQL lintas database tanpa MSDTC (`ApprovalTransaction`). Syaratnya satu server dan satu login.
- Akun sistem (admin bawaan, debugger) tidak bisa submit atau menandatangani, karena ada FK ke tabel user.

### Tempat konsep "dokumen" menempel di engine

- **Skema:** `020-approval.sql` membuat tabel jenis dokumen (`ta_Doc`: nama, deskripsi, singkatan `char(7)`).
  Kolom jenis dokumen pada request ber-FK ke tabel itu, jadi setiap jenis harus didaftarkan sebagai baris di
  database engine.
- **API:** `docType`, `docKey`, dan `docVersion` ada di `SubmitAsync`, `SubmitDataAsync`,
  `EnsureNotInApprovalAsync`, dan `GetMeta_ApprovalRequestsByDoc`.
- **Claim:** claim pembaca bernama `View {docType}` (`EmAppBuilder.ApprovalViewClaimName`).
- **Hub:** task dikelompokkan per jenis dokumen (`ApprovalHubTaskSource`).
- **Istilah:** `ApprovalKind.Document` berarti dokumen transaksi di tabel modul, bukan file fisik.

### Kelemahan lain yang tercatat

- Mengubah flow atau penanda tangan berarti deploy ulang. Mengganti label step berarti mengganti claim.
- Bentuk flow terbatas: belum ada any-of/kuorum, percabangan selain `when:`, eskalasi/batas waktu,
  delegasi/cuti, dan notifikasi.
- Kunci dokumen bersifat opt-in: modul harus memanggil `EnsureNotInApprovalAsync` sendiri. Aplikasi lain yang
  menulis langsung ke tabel tidak tertahan.
- Approval dan stempel PDF menyatu. Penentuan posisi stempel lewat anchor teks rapuh.
- Data approval berbasis string per kolom. Handler modul berat. Konflik dibandingkan lalu ditulis tanpa kunci
  baris (sudah disebut di "Known limitations").
- `GetMeta_ApprovalRequests(query)` dan `GetMeta_ApprovalRequestsByDoc` masih membawa JSON di query string,
  melanggar aturan GET per field.
- Hub hanya per jenis, belum per item. UI approval dan hub belum ada di MAUI. Layar cek kode verifikasi belum
  dibangun.

## Pemetaan tiga jenis terhadap kode sekarang (analisa agent, belum keputusan)

| Jenis (gagasan) | Objek | Flow | UI | Padanan sekarang |
| --- | --- | --- | --- | --- |
| Data Approval | Operasi INSERT/UPDATE/DELETE | Berbasis claim, sederhana | Generik dari engine | Data approval sekarang. Konsepnya dekat, implementasinya masih berat |
| Document Approval | File fisik | Step dinamis (runtime) | Disediakan engine: viewer, comment, progres | **Belum ada.** Bukan `ApprovalKind.Document` |
| System Approval | Entity aplikasi (transaksi ERP) | Flow + step | Dibangun developer per entity; engine memvalidasi hak | `ApprovalKind.Document` sekarang (flow di kode, input per step, stempel PDF) |

Bacaan agent: Document Approval versi gagasan menjadikan *file* sebagai objek utama. Itu bisa dibangun
**di atas** inti engine sebagai salah satu implementor (bersama object storage), bukan konsep yang tertanam di
inti. Inti engine cukup mengenal: subjek approval (jenis yang didaftarkan implementor + kunci opaque), flow,
step, penanda tangan, keputusan, komentar, timeline, dan task. Ketiga jenis menjadi pintu masuk ke inti yang
sama.

## Keputusan yang sudah jelas

- (2026-10-16) Ada tiga jenis approval: Data, Document, System, dengan arti seperti di "Gagasan pengguna".
- (2026-10-16) Engine tidak boleh memiliki konsep dokumen. Daftar dan arti dokumen adalah milik implementor.
- (2026-10-16) **Inti approval dibuat oleh service yang memakainya.** Contoh: sebuah modul data master yang
  memakai Data Approval menyiapkan sendiri **settings** approval-nya dan **handler**-nya. Engine hanya
  menyediakan mekanisme (runtime flow, keputusan, audit) yang dijalankan atas settings dan handler dari modul,
  tanpa registry jenis dokumen milik engine. Ini menjawab sebagian pertanyaan 1: mekanismenya boleh bersama,
  tetapi definisi tiap approval milik modul.

## Usulan: lepas FK ke tabel jenis dokumen (pengguna 2026-10-16)

Usulan pengguna: FK dari kolom jenis dokumen pada request ke `ta_Doc` dimatikan. Nama jenis hanya ada di
deklarasi developer (`AddDataApproval`/`AddDocumentApproval`), dan engine mencarinya dari situ. Ini langkah
pertama menuju "engine tanpa konsep dokumen" dan bisa dikerjakan terpisah dari rancang ulang besar.

**Temuan kode (2026-10-16):** kode C# engine tidak pernah membaca `ta_Doc`. Satu-satunya keterkaitannya adalah
`FK_ta_ApprovalRequest_ta_Doc` di `020-approval.sql`. Engine sudah mencari jenis lewat `ApprovalRegistry`
yang dibangun dari deklarasi: `Get(docType)` menolak jenis yang tidak dideklarasikan dengan 404, dan
`GetMeta_ApprovalDocumentTypes` membaca dari registry. Jadi melepas FK tidak membutuhkan perubahan kode.

**Konsekuensi dan hal yang perlu diatur:**

1. **Nama jenis menjadi kontrak permanen.** Request lama menyimpan nama jenis sebagai teks. Kalau jenis
   di-rename atau dihapus dari kode, request lama tidak punya flow lagi: tidak terbaca di Approval Manager
   (hak lihat dihitung dari flow) dan tidak bisa diputuskan. Aturannya: nama yang sudah dipakai tidak diubah;
   rename harus disertai migrasi data request.
2. **Validasi bergeser ke startup.** Tanpa FK, salah ketik nama tidak lagi ditolak database, tetapi tetap
   tertahan karena submit memakai flow dari registry. Yang perlu ditambahkan di builder: pesan yang jelas bila
   dua modul mendeklarasikan nama yang sama (sekarang `ToDictionary` hanya melempar `ArgumentException`
   generik), dan validasi panjang nama sesuai kolom (`varchar(25)`).
   **Keputusan pengguna 2026-10-16:**
   - ~~Nama approval diberi awalan jenisnya, contoh `data.quotation`.~~ **Direvisi pengguna 2026-10-16:**
     `data.quotation` hanya contoh. Nama bisa polos, misalnya `Quotation`, tanpa awalan. **Jenis approval
     diambil dari enum** (`ApprovalKind`; sekarang `Data`/`Document`, nanti ditambah `System` dan disesuaikan
     dengan tiga jenis), bukan dari teks nama.
     Nama didefinisikan di sisi aplikasi (host dan modulnya), bukan di engine.
   - **Keputusan pengguna 2026-10-16: nama unik per jenis.** `Quotation` boleh ada sebagai Data dan sebagai
     Document sekaligus. Yang wajib unik adalah pasangan **(jenis, nama)**; pengecekan ganda saat kompilasi
     dan startup memakai pasangan itu.
   - Konsekuensi (analisa agent, untuk plan):
     - Kunci registry menjadi (jenis, nama). `ApprovalRegistry.Get(docType)` dan semua pencarian ikut
       membawa jenis.
     - API yang sekarang hanya membawa nama (`GetMeta_ApprovalRequestsByDoc`, filter list, payload
       `ApprovalManagerNavigationPayload`, parameter navigasi hub) perlu membawa jenis juga.
     - Filtered unique index "satu request pending per (jenis dokumen, key, versi)" ditambah kolom jenis.
       Kolom `cApprovalRequestKind` sudah ada.
     - ~~Nama claim bisa bertabrakan~~ → selesai dengan keputusan "engine tidak membuat claim" di bawah.
     - Hub sudah memisahkan sumber per jenis (`approval.data`/`approval.document`). Kelompoknya cukup
       ditambah jenis pada kunci.
   - Nama wajib unik. **Nama ganda membuat API gagal start**; aplikasi tidak boleh jalan dengan nama ganda.
   - Temuan kode: registry dibangun saat startup (`EmApp.cs`, `new ApprovalRegistry(builder.ApprovalFlows)`
     sebelum app jalan), dan `ToDictionary` sudah melempar exception pada nama ganda. Jadi API memang sudah
     gagal start, tetapi pesannya generik (`ArgumentException`, "An item with the same key…"). Yang perlu
     dibuat: pengecekan eksplisit di builder yang menyebut nama ganda beserta kedua modul pendeklarasinya,
     plus validasi format nama (karakter yang boleh) dan panjang kolom (`varchar(25)`; panjang kolom juga perlu
     dipertimbangkan).
   - **Keputusan pengguna 2026-10-16: validasi saat kompilasi.** Nama ganda membuat **build host API gagal**,
     bukan hanya startup. Pengecekan saat startup tetap ada sebagai lapis kedua.
   - Catatan teknis (analisa agent, belum keputusan): nama approval sekarang hanya argumen string pada
     pemanggilan `AddDataApproval(...)` di assembly modul yang sudah dikompilasi. Saat host dikompilasi,
     argumen itu tidak bisa dibaca dari metadata. Supaya bisa dicek saat kompilasi, nama harus terlihat di
     **metadata**. Usulan: modul mendeklarasikan nama lewat attribute, misalnya
     `[assembly: ApprovalName("Quotation")]` atau attribute pada class/konstanta deklarasinya. Builder di
     runtime mencocokkan deklarasi dengan attribute itu, supaya tidak ada nama yang lolos tanpa attribute.
     Pemeriksanya ada dua pilihan:
     1. **Roslyn analyzer** yang dikirim bersama `Em.Api.Core`. Analyzer berjalan saat host dikompilasi,
        membaca attribute di semua assembly yang direferensikan, dan melaporkan nama ganda sebagai error
        (juga tampil di IDE).
     2. **MSBuild task/target** setelah compile host, yang memindai assembly di output (mis. lewat
        `MetadataLoadContext`) lalu menggagalkan build.
     Batasnya: kompilasi hanya mengenal modul yang direferensikan host. Modul yang dimuat dinamis, atau nama
     yang dibentuk saat runtime, hanya tertangkap saat startup.
   - **Keputusan pengguna 2026-10-16: definisi approval lewat interface engine.** Engine menyediakan interface
     untuk membangun approval. Developer mengimplementasikannya di modul, satu class per approval (mis. di
     modul data master atau modul penawaran). Saat build dan saat run, engine memeriksa semua registrasi
     approval dan menolak nama yang sama. Ini menggantikan deklarasi lewat lambda di
     `AddDataApproval(docType, claim, flow => …)`.
   - Sketsa (analisa agent, nama belum diputuskan):
     ```csharp
     [Approval("Quotation")]                            // nama di metadata, bisa dibaca saat kompilasi
     public sealed class QuotationApproval : IDataApprovalDefinition<QuotationServices>
     {
        public string ApproveClaim => "Approve Update";
        public void Build(ApprovalDataFlowBuilder<QuotationServices> flow) =>
           flow.Entity<QuotationKey>("Quotation", LoadAsync, ApplyAsync, order: 0).Summary(SummarizeAsync);
        // handler load/apply/summary sebagai method class ini atau service modul
     }
     // registrasi: builder.AddApproval<QuotationApproval>()  (atau scan assembly modul)
     ```
     Nama ditaruh di attribute, bukan property, karena nilai property tidak terbaca dari metadata saat
     kompilasi, sedangkan attribute dan konstanta bisa. Implementasi interface juga terlihat sebagai tipe di
     metadata. Analyzer di host (dikirim bersama `Em.Api.Core`) bisa memeriksa:
     (a) setiap implementor interface punya attribute nama, (b) format nama, dan (c) tidak ada
     nama ganda di semua assembly yang direferensikan. Saat startup, builder memeriksa ulang ketiganya dengan
     pesan yang menyebut nama dan class-nya.
   - **Keputusan pengguna 2026-10-16: registrasi eksplisit.** Modul mendaftarkan setiap class approval lewat
     builder, misalnya `builder.AddApproval<QuotationApproval>()` di `Add<Modul>()`. Tidak ada scan assembly.
     Hanya approval yang didaftarkan yang aktif. Konsekuensi (analisa agent): class yang mengimplementasikan
     interface tetapi lupa didaftarkan tidak aktif dan tidak terdeteksi saat startup. Analyzer saat kompilasi
     tetap memeriksa nama ganda pada semua implementor, terdaftar atau tidak, supaya sebuah nama tidak bisa
     bertabrakan begitu class itu didaftarkan.
   - **Keputusan pengguna 2026-10-16: satu interface per jenis.** Flow setiap jenis berbeda, jadi interface-nya
     dipisah: `IDataApprovalDefinition<TServices>`, `ISystemApprovalDefinition<…>`, dan
     `IDocumentApprovalDefinition<…>` (nama masih sketsa). Masing-masing punya builder flow sendiri.
   - Analisa agent (belum keputusan): ketiganya bisa diturunkan dari satu interface penanda kosong (mis.
     `IApprovalDefinition`). Dengan penanda itu, registry, analyzer, dan pengecekan nama ganda cukup melihat
     satu tipe. Nilai enum jenis (`ApprovalKind`) bisa diturunkan otomatis dari interface yang
     diimplementasikan (`IDataApprovalDefinition` → `Data`), jadi developer tidak menulisnya dua kali dan
     keduanya tidak mungkin berbeda. Apakah
     `builder.AddApproval<T>()` dibuat satu method generik untuk semua jenis, atau satu method per jenis,
     diputuskan saat plan.
3. **Deskripsi/judul jenis.** Engine tidak memakai `cDocDesc`/`cDocAbv`. Bila tampilan butuh judul yang ramah,
   tambahkan sebagai parameter deklarasi, bukan tabel.
4. **Tabel `ta_Doc` keluar dari skrip engine.** `020-approval.sql` tidak lagi membuat `ta_Doc` dan FK-nya.
   Database lama butuh skrip di `updates/` yang hanya men-drop FK, tidak men-drop tabel, karena tabel itu bisa
   dipakai produk turunan untuk keperluannya sendiri.
5. **Dokumen yang ikut diperbarui:** bagian "Tables" di [engine-approval.md](../engine/engine-approval.md)
   ("The document type must already be registered…") dan keterangan seed jenis dokumen.

## Pengelompokan di Approval Manager dari definisi API (pengguna 2026-10-16)

**Keputusan:** daftar dan pengelompokan approval di Approval Manager (dan hub) diambil dari **definisi yang
terdaftar di API**, bukan dari database.

**Temuan kode (2026-10-16):** sebagian sudah begitu. `ApprovalManager.xaml.cs` mengisi pilihan jenis dari
`GetMeta_ApprovalDocumentTypes`, yang membaca `ApprovalRegistry` (deklarasi), bukan tabel. Hak lihat,
filter list, dan hub juga memakai registry. Yang kurang: action itu hanya mengembalikan `string[]` nama, tanpa
jenis (data/system/document), judul tampilan, atau kelompok. Dalam rancangan baru, action ini (atau
penggantinya) mengembalikan info definisi: nama, jenis dari interface yang diimplementasikan, judul, dan
kelompok bila ada. Dengan begitu UI bisa mengelompokkan per jenis atau per modul tanpa tabel master. Request
yang jenisnya tidak lagi terdaftar tidak muncul (lihat konsekuensi nomor 1 di bagian FK).

## Claim dan hak akses milik modul (pengguna 2026-10-16)

**Lingkup:** keputusan ini dibahas untuk **Data Approval**. System Approval belum dibahas (pengguna
2026-10-16); penerapannya ke System Approval diputuskan saat jenis itu dibahas.

**Keputusan:**

1. **Engine tidak membuat claim khusus approval.** Tidak ada lagi claim yang didaftarkan otomatis (claim
   pembaca `View {nama}`, claim per step dari nama step). Modul yang memakai approval yang memegang claim-nya.
   Contoh pada modul data master: action save modul itu yang dipanggil layar, backend modul itu yang
   menjalankan engine approval, dan claim (mis. "boleh mengusulkan", "boleh approve") didefinisikan dan
   didaftarkan oleh modul sendiri.
2. **UI engine cukup bertanya ke API: user yang login boleh melakukan apa.** Approval Manager terbuka sesuai
   kemampuan user itu (approval mana yang bisa dilihat, request mana yang menunggu keputusannya), tanpa
   mengenal nama claim.

**Konsekuensi (analisa agent, untuk plan):**

- Interface definisi approval perlu menyediakan **pemeriksaan hak** yang diisi modul, misalnya
  `CanView(caller)`, `CanDecide(caller, request/step)`, dan `CanWithdraw(caller, request)`. Modul
  mengisinya dengan claim miliknya. Engine memanggil pemeriksaan ini di server untuk setiap action (list,
  detail, keputusan, withdraw, comment, hub). Jadi pemeriksaan tetap di server, tetapi aturannya milik
  modul.
- Action engine baru (atau perluasan `GetMeta_ApprovalDocumentTypes`) mengembalikan "kemampuan" caller per
  definisi, misalnya `{ jenis, nama, judul, canView, canDecide, jumlahMenunggu }`. Approval Manager dan hub
  dibangun dari jawaban itu.
- Hilang dari engine: `ApprovalAccess.HoldsClaim` terhadap claim step, `EmAppBuilder.ApprovalViewClaimName`,
  dan registrasi claim otomatis di builder, untuk Data Approval. Aturan yang tetap di engine: akun sistem
  tidak bisa mengusulkan atau memutuskan.
- **Keputusan pengguna 2026-10-16:** user nyata dengan **switch administrator selalu lolos**. Engine
  memberlakukannya secara umum sebelum memanggil pemeriksaan hak dari definisi modul, jadi modul tidak perlu
  dan tidak bisa menolaknya. **Akun admin bawaan dan debugger tidak boleh berpartisipasi**: tidak bisa
  mengusulkan, memutuskan, atau menarik. Aturan ini tetap di engine, sama seperti sekarang
  (`RequireRealUserAsync`, FK ke tabel user).

## Cakupan tahap pertama (pengguna 2026-10-16)

**Keputusan:** tahap pertama fokus pada **Data Approval** dan **System Approval**. **Document Approval** hanya
berupa **stub**: interface dan nilai enum jenisnya sudah ada supaya bentuk API lengkap, tetapi registrasi atau
pemakaiannya melempar `NotImplementedException`. Viewer file, flow dinamis, dan progres dokumen ditunda.
Pertanyaan terbuka nomor 4–6 (flow dinamis, progres dokumen, viewer) karenanya tidak memblokir plan tahap
pertama.

Pemakai pertama di produk turunan (2026-10-16): satu modul data master untuk Data Approval dan satu modul
dokumen transaksi penjualan untuk System Approval. Jadi plan engine cukup membuktikan kedua jenis pada dua
pemakai itu, ditambah module uji `Em.Test`.

## Pertanyaan terbuka

1. **Inti bersama.** Apakah ketiga jenis berbagi satu runtime flow (step, signer, keputusan, audit, comment,
   hub), atau masing-masing berdiri sendiri? Sebagian terjawab 2026-10-16 (definisi milik modul; lihat
   keputusan). Yang masih terbuka:
   - **Bentuk "settings":** deklarasi di kode saat startup seperti `AddDataApproval` sekarang, atau data
     konfigurasi yang bisa diubah saat runtime? Kalau data, disimpan di database modul atau di tabel engine
     dengan kunci milik modul?
   - **Tempat data request** (header, step, item, comment): tetap di tabel engine dengan kunci subjek opaque,
     atau ikut di database modul?
   - Bagaimana Approval Manager dan hub menampilkan request lintas modul bila definisinya tersebar di modul.
2. ~~**Flow Data Approval.** Selalu satu keputusan berbasis claim, atau boleh multi-step?~~ **Diputuskan
   pengguna 2026-10-16:** Data Approval **tanpa multi-step**. Cukup satu keputusan, dan definisi modul hanya
   menentukan **siapa yang boleh approve** (`CanDecide`). Kebutuhan berlapis masuk System Approval.
3. **Arah dari pengguna 2026-10-16:** modul pemakai Data Approval tidak boleh butuh banyak berkas pembantu.
   Produk turunan menetapkan aturan modul "satu service class + satu helper statis + satu file definisi
   approval". Konsekuensinya, engine perlu menyediakan helper Data Approval untuk bagian load/apply yang
   berulang: peta kolom dengan normalisasi teks per tipe, baca/tulis kolom ke entity ORM, cari baris
   berdasarkan key record, 409 bila baris hilang, dan penanganan operasi Update/Delete/Reinstate standar.
   Modul hanya menulis bagian khas (pembuatan key baru, soft delete khusus, validasi relasi).
   **"Mudah diimplementasi" untuk Data Approval.** Untuk tabel berkolom standar, engine bisa generik lewat
   metadata ORM, sedangkan tabel tanpa kolom standar atau dengan composite key tetap butuh handler dari modul.
   Apakah dua jalur ini diterima? Apakah nilai tetap string per kolom?
4. **"Dinamis" di Document Approval.** Siapa yang menyusun step: admin lewat template per proyek atau kategori,
   atau pengaju per dokumen? Apakah step bisa diubah saat request berjalan?
5. **Progres dokumen.** Apakah berupa revisi beserta status/kode review, persentase step, atau keduanya?
   Bagaimana hubungan revisi file dengan request approval (satu request per revisi)?
6. **Viewer.** PDF mudah. docx/xlsx biasanya dikonversi ke PDF. dwg butuh converter atau viewer khusus yang
   mungkin berlisensi, sehingga harus mengikuti aturan lib berlisensi. Format mana yang wajib di tahap awal?
   Apakah comment bisa berupa markup di halaman, atau teks saja?
7. **System Approval.** Flow tetap dideklarasikan di kode oleh developer, atau juga bisa dikonfigurasi saat
   runtime? Berapa banyak UI yang disediakan engine (komponen keputusan, timeline) dibanding UI developer?
8. **Stempel PDF.** Masih dibutuhkan? Kalau ya, apakah menjadi lapisan opsional dan dipakai oleh jenis yang
   mana?
9. **Istilah netral** pengganti `Doc` (`docType`/`docKey`/`docVersion`) di skema dan API. Tanyakan ke pengguna
   sebelum memilih.
10. **Kunci subjek.** Apakah engine tetap menyediakan penguncian subjek saat approval berjalan, atau sepenuhnya
    urusan implementor?
11. **Migrasi.** Apa yang dipertahankan dari tabel dan perilaku sekarang (reinstate `-2`/`-3`, verifikasi kode,
    FK ke user, transaksi lintas database), dan bagaimana data request yang sudah ada dipindahkan?
12. **Hal lama yang ikut dibahas.** Notifikasi, delegasi/cuti, eskalasi, any-of/kuorum, hub per item, UI MAUI,
    dan GET per field untuk action approval.
