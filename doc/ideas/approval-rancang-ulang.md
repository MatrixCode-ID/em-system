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
   - Nama approval diberi awalan jenisnya, contoh `data.quotation` (pola `<jenis>.<nama>`; awalan untuk
     jenis lain, misalnya `system.`/`document.`, mengikuti bentuk yang sama, menunggu rancangan jenis itu).
     Nama didefinisikan di sisi aplikasi (host dan modulnya), bukan di engine.
   - Nama wajib unik. **Nama ganda membuat API gagal start**; aplikasi tidak boleh jalan dengan nama ganda.
   - Temuan kode: registry dibangun saat startup (`EmApp.cs`, `new ApprovalRegistry(builder.ApprovalFlows)`
     sebelum app jalan), dan `ToDictionary` sudah melempar exception pada nama ganda. Jadi API memang sudah
     gagal start, tetapi pesannya generik (`ArgumentException`, "An item with the same key…"). Yang perlu
     dibuat: pengecekan eksplisit di builder yang menyebut nama ganda beserta kedua modul pendeklarasinya,
     plus validasi format nama (awalan jenis, karakter yang boleh) dan panjang kolom (`varchar(25)`: cukup
     untuk `data.quotation`, tetapi bisa sempit untuk nama lain, jadi panjang kolom juga perlu dipertimbangkan).
   - **Keputusan pengguna 2026-10-16: validasi saat kompilasi.** Nama ganda membuat **build host API gagal**,
     bukan hanya startup. Pengecekan saat startup tetap ada sebagai lapis kedua.
   - Catatan teknis (analisa agent, belum keputusan): nama approval sekarang hanya argumen string pada
     pemanggilan `AddDataApproval(...)` di assembly modul yang sudah dikompilasi. Saat host dikompilasi,
     argumen itu tidak bisa dibaca dari metadata. Supaya bisa dicek saat kompilasi, nama harus terlihat di
     **metadata**. Usulan: modul mendeklarasikan nama lewat attribute, misalnya
     `[assembly: ApprovalName("data.quotation")]` atau attribute pada class/konstanta deklarasinya. Builder di
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
     [Approval("data.quotation")]                       // nama di metadata, bisa dibaca saat kompilasi
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
     (a) setiap implementor interface punya attribute nama, (b) format nama (awalan jenis), dan (c) tidak ada
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
     satu tipe. Awalan nama (`data.`/`system.`/`document.`) dapat dicek terhadap interface yang
     diimplementasikan, mis. class `IDataApprovalDefinition` wajib bernama `data.*`. Apakah
     `builder.AddApproval<T>()` dibuat satu method generik untuk semua jenis, atau satu method per jenis,
     diputuskan saat plan.
3. **Deskripsi/judul jenis.** Engine tidak memakai `cDocDesc`/`cDocAbv`. Bila tampilan butuh judul yang ramah,
   tambahkan sebagai parameter deklarasi, bukan tabel.
4. **Tabel `ta_Doc` keluar dari skrip engine.** `020-approval.sql` tidak lagi membuat `ta_Doc` dan FK-nya.
   Database lama butuh skrip di `updates/` yang hanya men-drop FK, tidak men-drop tabel, karena tabel itu bisa
   dipakai produk turunan untuk keperluannya sendiri.
5. **Dokumen yang ikut diperbarui:** bagian "Tables" di [engine-approval.md](../engine/engine-approval.md)
   ("The document type must already be registered…") dan keterangan seed jenis dokumen.

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
2. **Flow Data Approval.** Selalu satu keputusan berbasis claim, atau boleh multi-step?
3. **"Mudah diimplementasi" untuk Data Approval.** Untuk tabel berkolom standar, engine bisa generik lewat
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
