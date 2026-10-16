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
     plus validasi format nama (karakter yang boleh) dan panjang kolom.
   - **Keputusan pengguna 2026-10-16: nama approval maksimal 50 karakter.** Kolom nama approval di skema baru
     `varchar(50)` (lama `varchar(25)`); data request dimulai bersih (pertanyaan terbuka 11), jadi tanpa
     migrasi.
   - **Keputusan pengguna 2026-10-16: format nama** diawali huruf, berisi huruf, angka, dan spasi tunggal
     (mis. `Sales Order`, `Customer 2`); tanpa spasi di awal/akhir dan tanpa karakter lain. Analyzer dan
     startup memeriksa format ini.
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
     **Keputusan pengguna 2026-10-16: pilihan 1, Roslyn analyzer**, dikirim bersama `Em.Api.Core`; error
     tampil di IDE dan menggagalkan build. MSBuild task tidak dibuat.
     Batasnya: kompilasi hanya mengenal modul yang direferensikan host. Modul yang dimuat dinamis, atau nama
     yang dibentuk saat runtime, hanya tertangkap saat startup.
   - **Keputusan pengguna 2026-10-16: definisi approval lewat interface engine.** Engine menyediakan interface
     untuk membangun approval. Developer mengimplementasikannya di modul, satu class per approval (mis. di
     modul data master atau modul penawaran). Saat build dan saat run, engine memeriksa semua registrasi
     approval dan menolak nama yang sama. Ini menggantikan deklarasi lewat lambda di
     `AddDataApproval(docType, claim, flow => …)`.
   - Sketsa (analisa agent, nama belum diputuskan). **Untuk Data Approval, sketsa `Build(...)` di bawah
     digantikan** oleh bagian "Rancangan interface Data Approval".
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
     keduanya tidak mungkin berbeda.
   - **Keputusan pengguna 2026-10-16: satu method registrasi per jenis.** `builder.AddDataApproval<T>()`,
     `builder.AddSystemApproval<TDef, THandler>()` (keputusan System Approval 22), dan nanti
     `AddDocumentApproval…`. Penyebutan `AddApproval<T>()` di bagian lain catatan ini dibaca sebagai
     `AddDataApproval<T>()`.
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

## Rancangan interface Data Approval (pengguna 2026-10-16)

Diskusi lanjutan. Usulan builder (`Build`/`Track`/`OnCreate`) dinilai pengguna **terlalu kompleks**, lalu
diganti bentuk deklaratif. Nama interface yang dipakai pengguna adalah `IEmSysApproval…`, sebagai nama kerja.
Agent sempat mengusulkan `IEmApproval…` karena engine memakai prefix `IEm` (`IEmApp`, `IEmApiCoreServices`);
pilihan akhirnya belum diputuskan.

### Keputusan

1. **Ada base interface** `IEmSysApproval`, dengan turunan per jenis: `IEmSysApprovalData<TEntity>`, nanti
   `IEmSysApprovalSystem…`, dan stub Document. Registry, pengecekan nama ganda, `AddApproval<T>()`, Approval
   Manager, hub, dan analyzer bekerja melalui base.
2. **Satu class definisi untuk satu tabel** yang di-track. Tabelnya diikat lewat parameter generic interface
   (`: IEmSysApprovalData<ta_Customer>`), bukan class generic. `class X<ta_Customer>` justru membuat
   parameter generic baru yang menutupi tipe aslinya.
3. **Kolom ditulis sebagai nama property lewat `nameof`.** Dengan begitu kolom tervalidasi saat kompilasi.
4. **Satu request boleh berisi beberapa tabel.** Action modul (mis. "pelanggan baru") mengirim payload
   berisi beberapa entity. Engine memecahnya per tabel memakai definisi masing-masing, dan semuanya tetap
   satu request yang di-approve atau di-reject sekaligus.
5. ~~**Relasi ke induk dideklarasikan di definisi anak** lewat `ForeignKeyTable` (`Type?`) dan
   `ForeignKeyColumns`. Untuk tabel induk, `ForeignKeyTable` **boleh `null`** dan `ForeignKeyColumns`
   **boleh kosong**.~~ Diganti keputusan nomor 7.
6. **Foreign key dicocokkan dengan key induk berdasarkan urutan** (pengguna 2026-10-16). Kolom ke-n di
   `ForeignKeyColumns` anak berpasangan dengan kolom ke-n di `KeyColumns` induk. Startup memeriksa jumlah dan
   tipenya. Bila nama kolom anak berbeda dari pasangannya di induk, startup memberi **peringatan**, bukan
   error, karena dua kolom bertipe sama yang tertukar urutannya tidak tertangkap oleh pemeriksaan tipe.
7. **Satu induk per tabel, dipastikan lewat tipe** (pengguna 2026-10-16). Tabel anak memakai interface
   sendiri yang membawa tipe induknya sebagai parameter generic: `IEmApprovalDataChild<TEntity, TParent>`.
   Tabel induk memakai `IEmApprovalData<TEntity>`. Property `ForeignKeyTable` (`Type?`) dihapus. Induk
   diketahui saat kompilasi, sehingga kombinasi "induk null + kolom terisi" tidak bisa ditulis lagi.
8. **Satu request adalah satu pohon dengan satu induk, dan hak hanya diperiksa di induk** (pengguna
   2026-10-16). Nama dan judul request diambil dari definisi induk. Pemeriksaan hak
   (`CanView`/`CanDecide`/`CanWithdraw`) hanya ada di definisi induk. Definisi anak hanya mengatur struktur dan
   mewarisi hak induknya, jadi approver tidak perlu lolos pemeriksaan di setiap tabel. Ini menggantikan usulan
   lama "approver harus lolos di semua definisi".
9. **Nama interface memakai prefix `IEm`** (pengguna 2026-10-16): `IEmApproval` (base), `IEmApprovalData<T>`,
   `IEmApprovalDataChild<T, TParent>`, nanti `IEmApprovalSystem…`, dan stub `IEmApprovalDocument…`. Nama kerja
   `IEmSysApproval…` tidak dipakai, karena "EmSys" adalah nama paket NuGet lama.
10. **Definisi dan perilaku dalam satu class di modul backend** (pengguna 2026-10-16, pilihan "A2"). Class
    definisi berisi deklarasi (`KeyColumns`, `TrackedColumns`, dan sebagainya) sekaligus hook yang butuh
    database (pembuatan key baru, validasi). Hook ditulis sebagai *default interface method*, sehingga class
    yang tidak membutuhkannya tidak menulis apa pun. Tidak ada class handler terpisah; registrasi tetap
    `builder.AddApproval<T>()`. Akibatnya:
    - Kontrak approval (interface definisi, `KeyPart`, parsing key) **tetap di `Em.Api.Core`**. Rencana
      memindahkannya ke `Em.Libs` dibatalkan.
    - Hook menerima akses ke context database modul yang ikut transaksi approval, jadi penguncian baris
      (mis. penomoran dengan lock) berjalan di transaksi yang sama.
    - Client tidak membaca definisi. Yang dibutuhkan UI (hak user; nanti kolom yang butuh approval atau khusus
      approver) diambil dari action "kemampuan" di API. Konstanta nama approval dan key record yang dipakai
      client boleh tetap di library kontrak produk.
    - Analyzer nama ganda tetap berjalan, karena host API mereferensikan assembly modul.
11. **Create disimpan sebagai JSON baris penuh, lalu di-insert saat apply** (pengguna 2026-10-16). Saat
    submit, engine menyerialisasi seluruh `TEntity` ke JSON di item request; tabel tujuan tidak disentuh.
    Item anak menunjuk item induknya **di dalam request**, bukan nilai key induk. Saat approve, dalam satu
    transaksi engine: deserialisasi JSON → `CreateKeyAsync` induk (hook di definisi, default: key harus sudah
    terisi) → insert induk → salin key induk ke `ForeignKeyColumns` anak → `CreateKeyAsync` anak → insert
    anak. Akibatnya:
    - **Tidak ada key sementara.** Modul tidak lagi membuat key palsu untuk baris baru atau mengganti key anak
      sendiri. Nomor key tidak berlubang bila request ditolak, tetapi nilai key baru baru diketahui setelah
      approve.
    - Semua kolom ikut di JSON, bukan hanya `TrackedColumns`; insert generik lewat context modul.
    - Analisa agent (untuk plan): saat apply, engine menolak JSON yang tidak cocok lagi dengan `TEntity`
      (property hilang/berganti tipe) dengan pesan jelas; nilai tanggal/desimal diserialisasi invarian;
      validasi modul dipanggil saat submit dan saat apply.
    - **Penyimpanan JSON (pengguna 2026-10-16):** JSON disimpan di kolom standar `json_object` pada item
      request (usulan agent memakai kolom khusus tidak dipakai). Setelah **approve**, engine
      **me-null-kan** `json_object` item itu, karena barisnya sudah ada di tabel tujuan. Untuk request yang
      **ditolak atau dibatalkan**, JSON **tetap disimpan**, karena tidak ada baris di tabel tujuan dan JSON
      adalah satu-satunya catatan isi pengajuan. Aturan null-kan ini **hanya untuk Create**.
    - Akibat (analisa agent): request Create yang sudah approved tidak lagi menyimpan isi yang diajukan; detailnya
      di Approval Manager hanya menunjukkan bahwa baris dibuat beserta key-nya. Usulan agent untuk menulis
      `TrackedColumns` ke baris field sebelum JSON di-null-kan tidak diambil.
    - Arti khusus `json_object` pada item Create dicatat di panduan engine.
12. **Request Create tidak bisa diedit** (pengguna 2026-10-16). Selama menunggu, pilihannya hanya **dibatalkan**
    oleh pengaju (withdraw) atau **ditolak** oleh approver. Untuk mengubah isinya, pengaju membatalkan lalu
    mengajukan ulang. Alasannya: datanya baru, jadi tidak perlu alur revisi yang rumit. Ini sejalan dengan
    engine sekarang, yang memang tidak punya edit untuk request yang menunggu.
13. **Update tetap old/new per kolom dengan pembandingan tiga nilai** (pengguna 2026-10-16). Penyimpanan
    (satu baris field per kolom: old, new, dan nilai sekarang saat diputuskan) serta aturan pembandingan
    (sekarang = new → dilewati; sekarang = old → diterapkan; lain → konflik, bisa di-override dengan alasan;
    baris hilang → konflik final) dipertahankan seperti engine sekarang. Yang berubah: **daftar kolom dihitung
    engine** dari dua objek yang dikirim layar, yaitu objek asli saat dimuat dan objek hasil edit
    (mis. `DataChange.Update(original, modified)`), hanya pada `TrackedColumns`. Modul tidak lagi menyusun
    daftar kolom, dan load/apply Update dikerjakan engine secara generik lewat `KeyColumns`.
14. **Delete dideklarasikan di definisi** (pengguna 2026-10-16). `DeleteMode` bernilai `None` (default; request
    Delete ditolak saat submit), `Hard` (engine menghapus baris), atau `Soft`. Untuk `Soft`, definisi
    menyebut `DeleteStateColumn` (wajib) dan `DeleteStampColumn` (opsional, diisi waktu saat Delete) lewat
    `nameof`. Kolom state `bool` diisi `true`. Kolom state enum/angka memakai nilai yang **dideklarasikan di
    definisi** (mis. `DeletedStateValue`); engine tidak menebak dan tidak memakai nilai bawaan. Hook khusus
    (mis. `OnDelete(row)`) hanya cadangan bila pola deklaratif tidak cukup.
15. **Delete induk tidak menyentuh anak** (pengguna 2026-10-16). Engine hanya menghapus/menandai baris yang
    diajukan; baris anak tidak ikut ditandai. Hard delete induk yang masih punya anak gagal oleh FK database.
16. **Data Approval tidak punya reinstate** (pengguna 2026-10-16). Reinstate (membuka ulang request yang sudah
    selesai) hanya ada di System Approval dan Document Approval, karena di sana banyak orang menandatangani;
    request yang di-reinstate **harus ditandatangani ulang dari awal**. Data Approval cukup satu keputusan,
    jadi perubahan setelah approve diajukan sebagai request baru.
17. **Pemulihan baris soft delete lewat operasi item `Restore`** (pengguna 2026-10-16). Operasi item
    `Reinstate` diganti nama menjadi `Restore`, supaya tidak tertukar dengan reinstate request. `Restore`
    adalah request baru dengan satu keputusan, kebalikan dari Delete: kolom state dikembalikan ke nilai aktif
    (`false` untuk `bool`; untuk enum/angka, nilai aktif dideklarasikan di definisi, mis. `ActiveStateValue`)
    dan `DeleteStampColumn` dikosongkan. Hanya tersedia untuk `DeleteMode = Soft`; pada mode lain request
    `Restore` ditolak saat submit.
18. **Kolom khusus approver hanya ditegakkan di UI** (pengguna 2026-10-16). Definisi boleh mengumumkan
    `ApproverOnlyColumns`; engine meneruskan daftar itu lewat action "kemampuan" supaya layar mengunci
    field-nya (read-only) untuk user yang bukan approver. **Server tidak menolak** request yang menyentuh kolom
    itu. Alasannya: request dari non-approver tetap harus di-approve sebelum data berubah, jadi pengecekan server
    tidak melindungi data; sesuai aturan produk turunan "backend tanpa whitelist kolom". (Klaim agent
    sebelumnya bahwa tanpa cek server kolom bisa diubah lewat API langsung keliru.)
19. **Tidak ada aturan khusus kolom berpasangan di engine** (pengguna 2026-10-16). Usulan agent `ColumnGroups`
    (bila satu kolom grup berubah, semua kolom grup ikut diajukan) ditolak. Kolom yang berpasangan diatur oleh
    UI: layar selalu mengganti pasangannya bersama. Engine juga tidak memvalidasi pasangan.
    Catatan agent untuk plan: karena selisih dihitung engine (keputusan 13), kolom pasangan yang nilainya
    kebetulan tidak berubah tidak ikut di request. Akibatnya, bila approver meng-override konflik, kombinasi
    pasangan bisa tidak konsisten. Approver tetap melihat konflik pada kolom yang diajukan sebelum memutuskan.

Akibat keputusan 7 dan 8 (analisa agent, untuk plan):

- Hanya definisi induk yang punya nama approval, dan nama itu wajib unik per (jenis, nama). Definisi anak
  tidak butuh nama; registry mengenalinya dari `TEntity`. Analyzer dan startup juga memeriksa bahwa satu
  `TEntity` hanya punya satu definisi.
- Payload yang memuat tabel dari dua pohon berbeda, atau tabel anak tanpa definisi induknya terdaftar,
  ditolak.
- Sisi base: `IEmApprovalData<T>` membawa nama, judul, dan pemeriksaan hak. Interface anak tidak menurunkan
  bagian itu. **Keputusan pengguna 2026-10-16: base struktur bersama.** Satu base struktural (nama kerja
  `IEmApprovalDataTable<T>`, turunan `IEmApproval`) memuat `KeyColumns`, `TrackedColumns`, pengaturan Delete/
  Restore, `CreateKeyAsync`, validasi, dan `ApproverOnlyColumns`. `IEmApprovalData<T>` = base + nama, judul,
  dan pemeriksaan hak; `IEmApprovalDataChild<T, TParent>` = base + `ForeignKeyColumns`.

### Bentuk (revisi 2026-10-16, mengikuti keputusan 6–9)

```csharp
public interface IEmApproval { }                              // penanda untuk registry/analyzer

public interface IEmApprovalData<TEntity> : IEmApproval where TEntity : class
{
   string[] KeyColumns { get; }          // seluruh kolom primary key baris
   string[] TrackedColumns { get; }      // kolom yang boleh diusulkan berubah
   // judul dan CanView/CanDecide/CanWithdraw: hanya di induk
}

public interface IEmApprovalDataChild<TEntity, TParent> : IEmApproval
   where TEntity : class where TParent : class
{
   string[] KeyColumns { get; }
   string[] TrackedColumns { get; }
   string[] ForeignKeyColumns { get; }   // berurutan, berpasangan dengan KeyColumns induk
}

[Approval("Customer")]
public class CustomerApproval : IEmApprovalData<ta_Customer>
{
   public string[] KeyColumns => [nameof(ta_Customer.cCustomerId)];
   public string[] TrackedColumns => [nameof(ta_Customer.cCustomerName), nameof(ta_Customer.cTaxNumber)];
}

public class CustomerAddressApproval : IEmApprovalDataChild<ta_CustomerAddress, ta_Customer>
{
   public string[] KeyColumns => [nameof(ta_CustomerAddress.cCustomerId), nameof(ta_CustomerAddress.cAddressNo)];
   public string[] TrackedColumns => [nameof(ta_CustomerAddress.cDescription)];
   public string[] ForeignKeyColumns => [nameof(ta_CustomerAddress.cCustomerId)];
}
```

### Bentuk (versi awal, diganti revisi di atas)

```csharp
public interface IEmSysApproval { /* judul, CanView, CanWithdraw: lihat bagian claim */ }

public interface IEmSysApprovalData<TEntity> : IEmSysApproval where TEntity : class
{
   string[] KeyColumns { get; }          // seluruh kolom primary key baris
   string[] TrackedColumns { get; }      // kolom yang boleh diusulkan berubah
   Type? ForeignKeyTable => null;        // tabel induk; null untuk tabel induk sendiri
   string[] ForeignKeyColumns => [];     // kolom anak yang merujuk KeyColumns induk
}

// Contoh tabel induk
public class CustomerApproval : IEmSysApprovalData<ta_Customer>
{
   public string[] KeyColumns => [nameof(ta_Customer.cCustomerId)];
   public string[] TrackedColumns => [nameof(ta_Customer.cCustomerName), nameof(ta_Customer.cTaxNumber)];
}

// Contoh tabel anak dengan composite key
public class CustomerAddressApproval : IEmSysApprovalData<ta_CustomerAddress>
{
   public string[] KeyColumns => [nameof(ta_CustomerAddress.cCustomerId), nameof(ta_CustomerAddress.cAddressNo)];
   public string[] TrackedColumns => [nameof(ta_CustomerAddress.cDescription)];
   public Type? ForeignKeyTable => typeof(ta_Customer);
   public string[] ForeignKeyColumns => [nameof(ta_CustomerAddress.cCustomerId)];
}
```

Catatan untuk contoh: `KeyColumns` harus berisi **seluruh** kolom primary key. Nomor baris anak
(`cAddressNo`) termasuk key, bukan kolom yang di-track. `ForeignKeyColumns` berisi kolom anak yang merujuk ke
induk.

### Yang dikerjakan engine dari deklarasi ini (analisa agent)

- **Startup:** memeriksa bahwa setiap nama adalah property `TEntity` dengan tipe yang didukung; mengambil tipe
  dan panjang kolom dari metadata EF (pengganti peta kolom manual di modul); memeriksa bahwa `TEntity`
  terdaftar di salah satu context yang ikut transaksi. Untuk foreign key: `TParent` harus punya definisi Data
  Approval yang terdaftar, dan `ForeignKeyColumns` harus sama jumlah dan tipenya dengan `KeyColumns` induk
  (dipasangkan berdasarkan urutan, dengan peringatan bila namanya berbeda). Bila tidak cocok, API gagal start.
- **Load/compare/apply Update:** generik. Baris dicari dari `KeyColumns`; normalisasi teks per tipe disediakan
  engine; pembandingan tiga nilai seperti sekarang.
- **Urutan apply** diturunkan dari relasi foreign key (induk lebih dulu), jadi `order` tidak perlu ditulis.
- **Create anak dengan induk baru:** setelah induk diterapkan, engine menyalin key asli induk ke
  `ForeignKeyColumns` anak (pengganti mekanisme key sementara yang dikelola modul). Create anak untuk induk
  yang sudah ada: engine memeriksa bahwa baris induk masih ada.

### Pertanyaan terbuka

1. ~~**Pencocokan foreign key dengan key induk**~~ → keputusan 6 (berdasarkan urutan).
2. ~~**Nilai key baru saat Create**~~ → keputusan 11 (`CreateKeyAsync` di definisi, dipanggil saat apply;
   tanpa pola bawaan engine di tahap pertama).
3. ~~**Delete**~~ → keputusan 14–17 (pemulihan lewat `Restore`). Teks pertanyaan awal: soft delete lewat property opsional (mis. `DeleteStateColumn`, beserta kolom waktunya), hard
   delete, atau tidak didukung bila tidak dideklarasikan?
4. ~~**Request gabungan**~~ → keputusan 8 (satu pohon, nama dan hak dari induk).
5. ~~**Payload Update harus membawa nilai lama dari layar**~~ → keputusan 13 (dua objek, selisih dihitung
   engine, tetap old/new per kolom). Teks pertanyaan awal: supaya konflik terdeteksi: `DataChange.Update(original,
   modified)` dengan dua objek, atau daftar kolom old/new seperti sekarang? Membaca ulang nilai lama dari tabel
   saat submit mematikan deteksi konflik.
6. ~~**Satu induk per tabel**~~ → keputusan 7 (dipastikan lewat `IEmApprovalDataChild<T, TParent>`).
7. **Tabel yang tidak cocok dengan pola kolom** (mis. tabel metadata key/value): jalur handler manual sebagai
   cadangan, atau dikeluarkan dari Data Approval? **Status 2026-10-16:** pemakai pertama di produk turunan
   memutuskan tabel key/value-nya disimpan langsung tanpa approval. Jadi tahap pertama **tidak membangun jalur
   cadangan**; Data Approval hanya untuk tabel berpola kolom. Pertanyaan ini dibuka lagi bila ada pemakai yang
   membutuhkannya.
8. ~~Hak per tabel atau hanya di induk~~ → keputusan 8 (hanya di induk, begitu juga judul). Yang masih
   terbuka: aturan per kolom (mis. "hanya pemegang hak approve yang boleh mengubah kolom ini"); apakah perlu
   daftar kedua seperti `ApproverOnlyColumns`? → keputusan 18 (hanya UI). Kolom berpasangan → keputusan 19.
9. ~~Nama interface final~~ → keputusan 9 (`IEmApproval…`).
10. ~~**Pemisahan definisi dan perilaku**~~ → keputusan 10 (satu class di modul backend, hook sebagai
    default interface method). Pilihan yang ditolak: definisi deklaratif di library kontrak ditambah handler
    terpisah di modul (`AddApproval<TDef, THandler>()`).

**Status 2026-10-16:** semua pertanyaan rancangan interface Data Approval sudah terjawab (keputusan 6–19).

## System Approval: agenda pembahasan (dibuka 2026-10-16)

Belum ada keputusan selain yang sudah tercatat: UI dibangun developer per entity, banyak penanda tangan,
reinstate hanya di System/Document dan wajib tanda tangan ulang dari awal (keputusan 16). Padanannya di engine
sekarang adalah `ApprovalKind.Document` (flow di kode, level berurutan, step paralel, nama step = nama claim,
`signers:`, on behalf, `distinctFrom`, `when:`, `guard`, `input` per step, stempel PDF).

Agenda, dari yang paling mendasar:

1. **Penentuan penanda tangan.** Usulan agent: ikuti Data Approval. Step tidak terikat claim; class definisi di
   modul punya hook (mis. `CanSign(user, step)` / `ResolveSignersAsync(step)`) yang menjawab siapa boleh
   menandatangani. Approval Manager dan hub mengetahui task user dengan bertanya ke definisi. **Pertanyaan
   pertama untuk sesi berikutnya.**
2. **Bentuk flow:** dideklarasikan di kode (class definisi di modul, seperti keputusan 10) atau bisa
   dikonfigurasi saat runtime?
3. **Bentuk step:** level berurutan + step paralel cukup, atau perlu any-of/kuorum?
4. **Fitur lama yang dipertahankan:** `when:`, `guard` + override, `input` per step, `distinctFrom`, on behalf.
5. **Penguncian subjek** selama approval: oleh engine atau urusan modul?
6. **Stempel PDF:** dipertahankan sebagai lapisan opsional?
7. **UI dari engine:** komponen yang disediakan engine (panel keputusan, timeline) dibanding yang dibangun
   developer.

Setelah itu: pertanyaan umum engine (istilah netral pengganti `docType`/`docKey`/`docVersion`, tempat data
request, migrasi data request yang ada, GET per field untuk action approval).

### Keputusan System Approval (pengguna 2026-10-16)

Prinsip dari pengguna: di System Approval **hampir semua dimainkan di UI**. Yang tahu kebenaran isi dan aturan
adalah modul; engine menyimpan state dan melanjutkan flow.

1. **Flow adalah data per request, disusun UI.** Saat approval dimulai, UI menyusun flow (step, urutan,
   penanda tangan) lalu **menyimpannya langsung ke engine approval**. Engine mengembalikan id request ULID
   (contoh `u1`). Flow tidak dideklarasikan di kode backend. Ini menjawab agenda 2.
2. **Tautan dibalik: pemakai yang menyimpan id request.** Modul pemakai approval ("ApprovalClient") menyimpan
   `u1` di tabelnya sendiri, mis. kolom pada baris dokumen transaksi. Engine **tidak menyimpan** jenis/key/
   versi subjek untuk System Approval; hanya pemakai yang tahu `u1` milik baris mana. **Direvisi keputusan
   9:** engine ikut menyimpan nama approval dan key subjek sebagai data opaque; kolom `u1` di tabel pemakai
   tetap wajib.
3. **ApprovalEditor membaca state.** Membuka ApprovalEditor untuk `u1` membuat engine mengembalikan state
   request; handler UI yang didaftarkan modul membaca state itu dan menampilkan isinya.
4. **Dua interface di sisi client**, diimplementasikan modul dan didaftarkan lewat builder: satu untuk
   **flow** (menyusun flow dari subjek) dan satu untuk **Editor UI**.
5. **Modul hanya membuat area utama editor.** Selebihnya milik engine: toolbar, signee card beserta tombol
   keputusan, komentar, attachment, Document preview (lihat mockup di
   [approval-display-modul.md](approval-display-modul.md)). Ini menjawab agenda 7.
6. **Sign dan Reject divalidasi modul lebih dulu.** Saat Sign atau Reject diklik, engine memanggil validasi di
   implementasi editor modul. Engine hanya mengirim keputusan dan melanjutkan flow bila jawabannya OK; bila
   tidak, pesan dari modul ditampilkan dan tidak ada yang dikirim.
7. **Pemeriksaan server cukup identitas penanda tangan.** Engine API hanya memastikan user yang memutuskan
   tercantum di step aktif `u1` dan bukan akun sistem (admin bawaan, debugger). Isi dan aturan bisnis tidak
   diperiksa server. Ini menjawab agenda 1 dalam bentuk: penanda tangan ada di flow yang disimpan.
8. **Data bisnis disimpan modul, catatan keputusan disimpan engine.** Isian step yang merupakan data bisnis
   (mis. PIC yang dipakai lagi oleh dokumen) disimpan modul ke tabelnya sendiri, tidak lewat payload engine.
   Catatan keputusan (alasan reject, keterangan saat approve) disimpan engine bersama tanda tangan.
9. **Request dibuat dari key subjek** (revisi keputusan 2). UI memanggil engine dengan class flow yang
   didaftarkan modul dan key subjeknya; engine client menyusun flow lewat class itu, mengirim flow beserta
   nama approval dan key ke server, lalu mengembalikan id request yang dibuat server:

   ```csharp
   var id = await Engine.RequestApprovalAsync<SalesOrderFlow>(so1Key);                               // key ULID
   var id = await Engine.RequestApprovalAsync<SalesOrderFlow>(new { Key1 = "key1", Key2 = "key2" }); // composite
   ```

   Key disimpan sebagai **satu string deskriptif** yang tidak diartikan engine, tanpa FK. Key string (mis.
   ULID dokumen) disimpan apa adanya; key objek (composite key legacy) diubah engine menjadi JSON dengan
   urutan property mengikuti deklarasi. Key bukan id request: id request selalu dibuat engine.
10. **Kolom id request di tabel pemakai tetap wajib.** Pencarian request per nama approval + key di engine
    hanya jalur pemulihan, mis. bila penyimpanan `u1` ke tabel pemakai gagal setelah request dibuat (ini
    menjawab pertanyaan request yatim; status draft dan pembersihan tidak diperlukan). Karena key bisa
    berupa objek, action pencarian itu POST.
11. **Satu key hanya boleh di-request sekali.** `RequestApprovalAsync` untuk nama approval + key yang sudah
    punya request yang belum void ditolak; siklus approval baru untuk key yang sama hanya lewat **reinstate**.
12. **Reinstate membuat request baru.** Reinstate `u1` membuat request baru `u2` dengan nama approval dan key
    yang sama, lalu state `u1` dibuat **negatif (void)**, mengikuti konvensi nilai negatif = tidak dipakai.
    Tanda tangan dan catatan keputusan `u1` tidak diubah dan menjadi histori; `u2` ditandatangani dari awal
    (sejalan dengan keputusan 16 Data Approval). Pemakai mengganti id request di tabelnya menjadi `u2`; bila
    penyimpanan itu gagal, pemulihan lewat pencarian per key (keputusan 10) menemukan request yang tidak void.
    Sempat diputuskan lalu dibatalkan pengguna di hari yang sama: reset `u1` dengan tanda tangan lama dipindah
    ke histori.
13. **Flow `u2` disusun ulang.** Saat reinstate, engine client memanggil lagi class flow modul
    (`BuildAsync`) dengan key yang sama; hasilnya menjadi flow `u2`. Penanda tangan yang tidak aktif lagi atau
    aturan modul yang berubah ikut terbarui. Flow `u1` tetap tersimpan utuh sebagai histori. Pilihan yang
    ditolak: menyalin flow `u1`.
14. **Flow tetap selama request berjalan; tidak ada edit flow saat runtime.** Perubahan aturan flow (mis.
    keputusan manajemen mengganti urutan atau penanda tangan) adalah pekerjaan developer: class flow modul diubah
    lalu modul di-compile dan dirilis ulang. Request yang sedang berjalan tetap memakai flow yang tersimpan;
    request baru dan reinstate (keputusan 13) memakai aturan baru. Engine tidak menyediakan action untuk
    mengubah flow request yang sudah dimulai. Usulan agent yang ditolak: mengubah step yang belum
    ditandatangani lewat action engine dengan alasan wajib.
15. **"Flow" = susunan step; penanda tangan diatur terpisah di engine** (memperjelas keputusan 1 dan 14).
    Susunan step milik modul dan ada di kode, mis. Sales Order: Create, Check By PIC, Check By GAM, Approve By
    NSM, Acknowledge by STR. Mengubah susunan step berarti mengubah dan meng-compile ulang modul. **Siapa** yang
    menandatangani tiap step diatur saat runtime lewat **manager/editor milik engine**, tanpa rilis modul.
16. **Penanda tangan di-snapshot ke request.** Perubahan pengaturan penanda tangan di editor engine berlaku
    untuk request baru dan baru aktif di request yang sedang berjalan setelah **reinstate**. Mengganti penanda
    tangan request yang sedang berjalan tanpa reinstate hanya boleh oleh pemegang **IsAdmin** (switch
    administrator) atau lewat **four-eyes** (perubahan disetujui orang kedua). Four-eyes: satu orang
    mengajukan penggantian dengan alasan, orang kedua yang berbeda menyetujui; keduanya pemegang claim "ubah
    penanda tangan" yang dideklarasikan modul; penanda tangan yang diganti tidak boleh menjadi salah satu dari
    keduanya; hanya step yang belum ditandatangani. IsAdmin cukup satu orang.

    Praktik di produk turunan (pengguna 2026-10-16): calon penanda tangan berurutan, "bila A tidak ada maka
    B, bila tidak ada maka C"; bila A, B, dan C tidak ada, IsAdmin menjadi fallback terakhir (mengganti
    penanda tangan step seperti di atas). **Keputusan:** engine **tidak** menegakkan urutan itu, karena
    menambah kompleksitas. Calon A, B, dan C sama-sama boleh menandatangani kapan saja (keputusan 17); urutan
    hanya prioritas tampilan (A utama, B dan C cadangan). Aturan "tanda tangani menggantikan A hanya bila A
    tidak masuk atau berhalangan" menjadi **policy lapangan**, bukan aturan sistem. Engine tidak perlu tahu
    apakah A "ada" (cuti, dinas, nonaktif).
17. **Tiga jenis step, ditandai modul di deklarasi step.**
    - **Pengaju** (mis. Create/Prepare By): penanda tangannya pembuat request; tanda tangannya tercatat
      **otomatis** saat request dibuat, tanpa klik Sign.
    - **Dari dokumen** (mis. Check By PIC): penanda tangan diisi modul dari data dokumen saat request dibuat.
    - **Diatur** (mis. Check By GAM, Approve By NSM, Acknowledge by STR): penanda tangan dipilih di editor
      engine.

    Satu step boleh punya **beberapa calon penanda tangan; cukup satu yang menandatangani**. Editor penanda
    tangan engine membaca daftar dan urutan step dari deklarasi modul, menampilkan step Pengaju dan Dari
    dokumen terkunci, dan hanya mengizinkan pengisian user pada step Diatur. Usulan sebelumnya (pilihan
    "(Active User)" di editor) tidak dipakai, digantikan jenis Pengaju.

    ```text
    Approval: Sales Order                                         [Save]
     1  Create              Pengaju request                   (terkunci)
     2  Check By PIC        Diisi modul dari dokumen          (terkunci)
     3  Check By GAM        [Budi x] [Sari x]  + Tambah user
     4  Approve By NSM      [Andi x]           + Tambah user
     5  Acknowledge by STR  [Rina x]           + Tambah user
    Perubahan berlaku untuk request baru; request berjalan setelah reinstate.
    ```
18. **Status dibaca modul lewat join, seperti sistem lama.** Tabel/view modul men-join id request yang
    disimpannya (keputusan 10) ke status request di engine, sehingga daftar dokumen bisa menampilkan status
    approval tanpa memanggil engine per baris. Konsekuensi (analisa agent): engine perlu objek baca yang
    stabil untuk di-join (id request, nama approval, key, status, step aktif), termasuk dari database modul
    lain di server yang sama. Notifikasi dan handler: keputusan 20.
19. **IsAdmin boleh menandatangani step mana pun secara langsung**, termasuk semua step dokumen yang ia buat
    sendiri. Ini pengecualian keputusan 7 (penanda tangan harus tercantum di step aktif) bagi user nyata
    pemegang switch IsAdmin; akun sistem (admin bawaan, debugger) tetap ditolak. Dalam praktik dokumen
    selalu berpindah ke departemen lain, jadi penyalahgunaannya ditangani **policy lapangan**, bukan sistem.
    Tanda tangan tetap mencatat user yang sebenarnya menandatangani.
20. **Notifikasi hub wajib dari engine; tindakan lain lewat handler opsional modul.** Engine selalu
    memberi notifikasi di hub (MY TASKS) kepada penanda tangan step berikutnya. Engine tidak mengirim email
    sendiri untuk System Approval. Modul boleh mendaftarkan handler untuk request **ditolak** dan **selesai**;
    bila handler terdaftar, engine memanggilnya. Isi handler bebas, mis. kirim email, pesan WhatsApp, atau
    mengubah data modul. Handler dijalankan **setelah** keputusan tersimpan; kegagalannya hanya dicatat di
    log dan tidak mempengaruhi keputusan (dikonfirmasi pengguna).
21. **Definisi System Approval di library bersama, flow disusun server** (revisi keputusan 9). Class
    definisi (nama approval, tipe key, daftar step beserta jenisnya) diletakkan di **library kontrak bersama**
    yang direferensikan UI dan backend, supaya keduanya memakai definisi yang sama; UI memanggil dengan tipe,
    mis. `RequestApprovalAsync<SalesOrderApproval>(key)`. Flow disusun **server** dari definisi itu:
    step, penanda tangan "Dari dokumen", penanda tangan "Diatur" dari pengaturan engine, dan step Pengaju.
    Pengaju tidak bisa mengirim flow sendiri. Editor penanda tangan dan Approval Manager membaca daftar step
    dari definisi yang sama. Perbedaan dengan Data Approval (keputusan 10, definisi dan perilaku di satu
    class modul backend) disengaja. Pembagian perilaku yang butuh database (penentuan penanda tangan "Dari
    dokumen", handler keputusan 20) masih dibahas.
22. **Registrasi di kedua sisi.** Definisi di library bersama mengimplementasikan interface
    `IEmApprovalSystem<TKey>`. Server: `AddSystemApproval<TDef, THandler>()`, handler backend hanya bila
    diperlukan (wajib bila ada step "Dari dokumen" untuk menentukan penanda tangannya; handler keputusan 20
    opsional). Frontend: `AddSystemApproval<TDef, TEditor>()`; editor memuat view area utama dan validasi
    Sign/Reject, ditambah member opsional **preview dokumen** (PDF; tombol Document preview disembunyikan bila
    kosong) dan **buka dokumen sumber** (pengganti `AddApprovalDocumentOpener`).

    - **Ringkasan request** (teks di hub, notifikasi, daftar Approval Manager, mis. "SO-2026-0012 · PT Abc ·
      Rp 120 jt") dikirim **UI sebagai teks** saat `RequestApprovalAsync` (juga saat reinstate). Handler
      backend tetap opsional; tidak diwajibkan hanya untuk ringkasan.
    - **Claim melihat request dibuat otomatis** dari nama approval (mis. `View Sales Order`), tanpa ditulis
      modul. Pemegangnya boleh membuka request dan memberi komentar walaupun bukan pengaju, penanda tangan,
      atau IsAdmin (mis. supervisor yang memantau progres). **Perbedaan dengan Data Approval disengaja**
      (dikonfirmasi pengguna 2026-10-16): Data Approval tidak punya claim otomatis dan memakai `CanView` di
      class definisi modul, karena definisinya ada di backend modul; definisi System Approval ada di library
      bersama tanpa akses database, sehingga claim lihat disediakan engine.
23. **Modul boleh menambah tombol di toolbar editor.** Contoh: modul dokumen penjualan menambah tombol
    "Print". Tombol milik modul; aksinya memeriksa hak user yang login (mis. apakah boleh melihat data
    sensitif) lalu menavigasikan ke datanya. Engine hanya menyediakan slot dan menjalankan perintahnya; hak
    dan isi aksi sepenuhnya urusan modul. Tombol engine (Document preview, buka dokumen sumber) tetap ada.
    Bentuk kontrak slot (mis. daftar item toolbar dari editor) dirumuskan di plan.
24. **Aturan flow standar: level berurutan, step paralel dalam level.** Tiap step punya nomor level; level
    berikutnya baru terbuka setelah semua step di level sebelumnya ditandatangani, dan step dalam satu level
    boleh ditandatangani dalam urutan apa pun (mis. tiga step check di level 2 sebelum Approve di level 3).
    Satu step per level = berurutan biasa. Tidak ada bahasa aturan flow lain di engine; aturan yang bergantung
    pada isi dokumen (mis. nilai dokumen di atas batas tertentu) ditangani validasi Sign milik modul
    (keputusan 6). Kuorum bisa ditambah nanti sebagai opsi per level.
    **Tidak ada penyaringan step per dokumen** (pengganti `when:` lama tidak dibuat): step yang hanya berlaku
    untuk sebagian dokumen (mis. Director hanya untuk nilai besar) diatur **policy lapangan**, mis. "bila
    kondisi xx, yang menandatangani B, bukan A" (calon cadangan, keputusan 16), atau penanda tangan diganti
    lewat **four-eyes** (keputusan 16). Validasi Sign
    modul dipakai untuk aturan pada step yang memang berlaku, mis. "AR Manager tidak bisa Sign bila
    outstanding customer > 60 jt"; setelah kondisinya berubah, Sign bisa dicoba lagi.
25. **Fitur lama engine untuk System Approval:**
    - `when:` tidak dibuat (keputusan 24).
    - `guard` + override claim diganti validasi Sign modul (`OnSign`); tanpa override. Bila aturan harus
      dilanggar: IsAdmin menandatangani (keputusan 19) atau penanda tangan diganti lewat four-eyes.
    - `input` per step diganti keputusan 8 (data bisnis disimpan modul di area utama editor).
    - `distinctFrom` tidak dibuat di tahap pertama; menjadi policy lapangan, atau ditolak modul di `OnSign`
      karena state request memuat siapa yang sudah menandatangani.
    - On behalf tidak dibuat; fungsinya tertutup calon cadangan (keputusan 16).
    - **Penguncian subjek urusan modul**: modul mengetahui status lewat join (keputusan 18) dan mengunci
      dokumennya sendiri selama approval belum selesai/ditolak.
    - **Stempel PDF dibuang**, bukan sekadar ditunda. Bila dokumen perlu menampilkan tanda tangan, modul
      mengaturnya sendiri, mis. saat Print, di `OnSign`, atau di handler selesai (keputusan 20).
26. **Isi state request** (pengguna 2026-10-16, menjawab pertanyaan terbuka 2):
    - **Header:** id ULID, jenis, nama approval, key string, ringkasan teks dari UI (keputusan 22), status
      (negatif = void), pembuat dan waktu, serta tautan reinstate (`u2` mencatat asalnya, `u1`).
    - **Step:** level, urutan, nama, jenis step (Pengaju/Dari dokumen/Diatur), dan snapshot calon penanda
      tangan berupa **user id saja**, tanpa claim.
    - **Tanda tangan:** step, user, waktu, keputusan, catatan keputusan (keputusan 8). Tanda tangan IsAdmin
      yang bukan calon step itu (keputusan 19) **diberi tanda khusus** supaya timeline dan audit bisa
      membedakannya.
    - **Komentar** dan **attachment**. Attachment disimpan **engine lewat object storage**, terikat ke request
      atau komentar; modul tidak mengurusnya.
    - **Audit penggantian penanda tangan** (keputusan 16): pengusul, penyetuju four-eyes atau IsAdmin, alasan,
      calon lama dan baru.
27. **Penanda tangan nonaktif atau cuti tidak butuh aturan engine** (pengguna 2026-10-16). Kasus ini sudah
    tercakup oleh calon cadangan, four-eyes, dan IsAdmin (keputusan 16 dan 19). Editor penanda tangan engine
    cukup menandai user yang sudah nonaktif.

**Usang sejak keputusan 21–23** (flow kini disusun server dari definisi di library bersama); sketsa dan
konsekuensi di bawah dipertahankan sebagai riwayat. Sketsa kontrak client (analisa agent, nama dan bentuk belum final; kontrak di `Em.Ui.Core` tanpa tipe
WPF/MAUI, view per platform):

```csharp
public interface IEmApprovalSystemFlow<TSubject>
{
   Task<ApprovalFlow> BuildAsync(TSubject subject, CancellationToken ct);
}

public interface IEmApprovalSystemEditor
{
   Task LoadAsync(IEmApprovalSystemContext context, CancellationToken ct);
   Task<ApprovalDecisionCheck> ValidateSignAsync(ApprovalStepState step, CancellationToken ct);
   Task<ApprovalDecisionCheck> ValidateRejectAsync(ApprovalStepState step, CancellationToken ct);
}

public record ApprovalDecisionCheck(bool Ok, string? Message = null);

// context dari engine: id request, state (step, penanda tangan, status), kemampuan user dari API,
// Refresh, buka preview. Keputusan dikirim oleh signee card engine, bukan oleh modul.
```

Konsekuensi (analisa agent, untuk plan):

- Backend modul pemakai System Approval **tidak butuh class definisi approval**; berbeda dari Data Approval
  (keputusan 10), karena flow System Approval berupa data, bukan aturan.
- Hub/MY TASKS bisa mendaftar task per user langsung dari penanda tangan step yang tersimpan di request.
- Isi flow ditentukan client, jadi pengaju secara teknis bisa menyusun flow yang tidak semestinya. Risiko ini
  sejalan dengan prinsip "UI yang menentukan"; engine hanya menjamin poin 7.
- Request perlu menyimpan nama approval supaya ApprovalEditor bisa memilih handler UI yang terdaftar.
- Modul menyimpan data bisnis step sendiri, terpisah dari keputusan yang dikirim engine; urutan simpan dan
  penanganan bila salah satu gagal perlu dirumuskan.

Pertanyaan yang masih terbuka:

1. ~~**Request yatim.**~~ → keputusan 9–11 (key subjek disimpan engine, pemulihan lewat pencarian per key).
   Teks pertanyaan awal: UI membuat `u1` lalu menyimpannya ke tabel pemakai; bila langkah kedua gagal, `u1`
   tanpa pemilik. Usulan agent yang ditolak: `u1` dibuat sebagai **draft** yang tidak tampil di hub,
   diaktifkan setelah pemakai menyimpan `u1`; draft yang tidak diaktifkan dibersihkan engine setelah batas
   waktu.
2. ~~**Isi state** yang disimpan engine: step berurutan/paralel, penanda tangan per step (user id saja atau juga
   claim), catatan keputusan, komentar; ada yang lain?~~ → keputusan 26.
3. ~~**Flow setelah dimulai:** tetap, atau boleh diubah saat berjalan (tambah step, ganti penanda tangan)?~~
   → keputusan 13–14 (tetap; perubahan aturan lewat rilis modul, dipakai request baru dan reinstate).
   Penanda tangan nonaktif atau cuti → keputusan 27.
4. ~~**Switch administrator:** boleh menandatangani step mana pun?~~ → keputusan 19 (boleh; policy lapangan).
5. ~~**Sinyal selesai/ditolak ke pemakai:** pemakai membaca state `u1` saat dibutuhkan, atau engine memberi tahu?~~
   → keputusan 18 (status lewat join) dan 20 (hub + handler opsional modul).
6. ~~**Reinstate:** `u1` di-reset dari awal, atau request baru `u2` dan `u1` menjadi histori?~~ → keputusan 12
   (request baru `u2`, `u1` void).
7. ~~**Flow dan editor:** dua class terpisah atau boleh satu class; interface baru atau perluasan
   `IApprovalPanel`/`IApprovalPanelHost`.~~ → keputusan 21–22 (definisi di library bersama, handler backend
   opsional, editor frontend; kontrak baru).
8. ~~**Toolbar:** modul boleh menambah tombol di toolbar engine?~~ → keputusan 22–23 (boleh).
9. ~~Agenda 3–6 di atas (bentuk step, fitur lama, penguncian subjek, stempel PDF) belum dibahas.~~ → keputusan
   24–25.

**Status 2026-10-16:** semua pertanyaan System Approval sudah terjawab (keputusan 1–27).

## Cakupan tahap pertama (pengguna 2026-10-16)

**Keputusan:** tahap pertama fokus pada **Data Approval** dan **System Approval**. **Document Approval** hanya
berupa **stub**: interface dan nilai enum jenisnya sudah ada supaya bentuk API lengkap, tetapi registrasi atau
pemakaiannya melempar `NotImplementedException`. Viewer file, flow dinamis, dan progres dokumen ditunda.
Pertanyaan terbuka nomor 4–6 (flow dinamis, progres dokumen, viewer) karenanya tidak memblokir plan tahap
pertama.

**Document Approval sebagai jenis tersendiri, ditunda** (pengguna 2026-10-16). Document Approval dibuat sebagai
satu jenis lagi, terpisah dari Data Approval dan System Approval, dan bukan varian System Approval. Sifatnya
**viewer**: dokumen menjadi pusat layar approval. Dokumennya mungkin tersusun sebagai **tree dokumen**. Jenis
ini tidak dikerjakan sekarang, mungkin nanti. Pertanyaan terbuka 4–6 dan 8 (flow dinamis, progres, viewer,
stempel PDF untuk Document Approval) dibahas ulang saat jenis ini dibuka.

Pemakai pertama di produk turunan (2026-10-16): satu modul data master untuk Data Approval dan satu modul
dokumen transaksi penjualan untuk System Approval. Jadi plan engine cukup membuktikan kedua jenis pada dua
pemakai itu, ditambah module uji `Em.Test`.

## Pertanyaan terbuka

**Status 2026-10-16:** semua pertanyaan untuk tahap pertama (Data dan System Approval) sudah terjawab. Yang
tersisa (4–6 dan 8) milik Document Approval dan ditunda bersama jenis itu.

1. ~~**Inti bersama.**~~ **Ditutup pengguna 2026-10-16.** Settings berupa kode (Data Approval: class modul,
   keputusan 10; System Approval: definisi di library bersama, keputusan 21, dengan penanda tangan diatur saat
   runtime di engine, keputusan 15); data request tetap di tabel engine dengan key opaque (keputusan System
   Approval 9 dan 26, keputusan Data Approval 11); tampilan lintas modul dari definisi terdaftar di API
   (bagian "Pengelompokan di Approval Manager"). Teks awal: Apakah ketiga jenis berbagi satu runtime flow (step, signer, keputusan, audit, comment,
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
3. ~~Helper Data Approval, dua jalur, string per kolom~~ → **ditutup pengguna 2026-10-16**: keputusan Data
   Approval 10–13 (load/apply generik dari deklarasi, Update tetap old/new per kolom) dan pertanyaan rancangan
   interface 7 (tanpa jalur handler cadangan di tahap pertama). Teks awal:
   **Arah dari pengguna 2026-10-16:** modul pemakai Data Approval tidak boleh butuh banyak berkas pembantu.
   Produk turunan menetapkan aturan modul "satu service class + satu helper statis + satu file definisi
   approval". Konsekuensinya, engine perlu menyediakan helper Data Approval untuk bagian load/apply yang
   berulang: peta kolom dengan normalisasi teks per tipe, baca/tulis kolom ke entity ORM, cari baris
   berdasarkan key record, 409 bila baris hilang, dan penanganan operasi Update/Delete/Reinstate standar.
   Modul hanya menulis bagian khas (pembuatan key baru, soft delete khusus, validasi relasi).
   **"Mudah diimplementasi" untuk Data Approval.** Untuk tabel berkolom standar, engine bisa generik lewat
   metadata ORM, sedangkan tabel tanpa kolom standar atau dengan composite key tetap butuh handler dari modul.
   Apakah dua jalur ini diterima? Apakah nilai tetap string per kolom?
> Pertanyaan 4–6 ditunda bersama Document Approval (lihat "Cakupan tahap pertama"): jenis tersendiri,
> bersifat viewer, kemungkinan tree dokumen.

4. **"Dinamis" di Document Approval.** Siapa yang menyusun step: admin lewat template per proyek atau kategori,
   atau pengaju per dokumen? Apakah step bisa diubah saat request berjalan?
5. **Progres dokumen.** Apakah berupa revisi beserta status/kode review, persentase step, atau keduanya?
   Bagaimana hubungan revisi file dengan request approval (satu request per revisi)?
6. **Viewer.** PDF mudah. docx/xlsx biasanya dikonversi ke PDF. dwg butuh converter atau viewer khusus yang
   mungkin berlisensi, sehingga harus mengikuti aturan lib berlisensi. Format mana yang wajib di tahap awal?
   Apakah comment bisa berupa markup di halaman, atau teks saja?
7. ~~**System Approval.** Flow tetap dideklarasikan di kode oleh developer, atau juga bisa dikonfigurasi saat
   runtime? Berapa banyak UI yang disediakan engine (komponen keputusan, timeline) dibanding UI developer?~~
   → keputusan System Approval 5, 15–17, 21–23.
8. **Stempel PDF.** Masih dibutuhkan? Kalau ya, apakah menjadi lapisan opsional dan dipakai oleh jenis yang
   mana? **System Approval:** dibuang (keputusan System Approval 25); Document Approval belum dibahas.
9. ~~**Istilah netral** pengganti `Doc` (`docType`/`docKey`/`docVersion`) di skema dan API.~~ **Diputuskan
   pengguna 2026-10-16:** `docType` → **`ApprovalName`** (kolom mis. `cApprovalName`), `docKey` →
   **`SubjectKey`** (kolom `cSubjectKey`), dan `docVersion` **dihapus**. Riwayat versi dibentuk rantai reinstate
   (keputusan System Approval 12 dan 26); revisi yang dibutuhkan modul menjadi bagian key subjek.
   Konsekuensi (analisa agent, untuk plan): filtered unique index "satu request pending" menjadi (jenis,
   `ApprovalName`, `SubjectKey`); `GetMeta_ApprovalRequestsByDoc` dan parameter `docType`/`docKey`/`docVersion`
   di API ikut berganti nama.
10. ~~**Kunci subjek.** Apakah engine tetap menyediakan penguncian subjek saat approval berjalan, atau sepenuhnya
    urusan implementor?~~ **System Approval:** urusan modul (keputusan System Approval 25).
    **Data Approval (pengguna 2026-10-16): baris tidak dikunci**, mengikuti pola engine lama. Beberapa request
    untuk baris yang sama boleh pending bersamaan; **request yang lebih dulu di-approve yang lebih dulu
    tersimpan**. Contoh: A mengajukan nama + email, B mengajukan alamat + email untuk baris yang sama. Bila
    request A di-approve lebih dulu, nama dan email A tersimpan. Saat request B diputuskan, alamat B
    diterapkan biasa, sedangkan email B menjadi konflik karena nilai sekarang bukan lagi nilai lama B. Approver
    melihat konflik itu dan bisa meng-override dengan alasan (keputusan Data Approval 13).
    Konsekuensi (analisa agent, untuk plan): filtered unique index "satu request pending per key" tidak berlaku
    untuk Data Approval; simpan langsung oleh approver selama ada request pending juga tidak ditahan, dan
    tertangkap pembandingan tiga nilai yang sama.
11. ~~**Migrasi.** Apa yang dipertahankan dari tabel dan perilaku sekarang (reinstate `-2`/`-3`, verifikasi kode,
    FK ke user, transaksi lintas database), dan bagaimana data request yang sudah ada dipindahkan?~~
    **Diputuskan pengguna 2026-10-16:**
    - **Mulai bersih.** Data request lama tidak dimigrasi ke struktur baru; skrip `updates/` membangun struktur
      baru tanpa konversi data request.
    - **Status void tetap `-2`/`-3`.** Reinstate System Approval (keputusan 12) membuat request lama `-2` bila
      masih pending dan `-3` bila sudah selesai, mengikuti engine sekarang; tautan `u2` → `u1` seperti
      `ReinstateOf`.
    - **Kode verifikasi per tanda tangan dipertahankan** walaupun stempel PDF dibuang. Engine membuat kode dan
      mengeksposnya di state request; modul boleh mencetaknya atau menjadikannya QR (mis. saat Print). Dasar
      untuk [approval-qr-verifikasi.md](approval-qr-verifikasi.md) tetap ada.
    - **FK ke tabel user dan transaksi lintas database tanpa MSDTC dipertahankan.** FK menjaga aturan akun
      sistem tidak bisa berpartisipasi; transaksi dibutuhkan untuk apply Data Approval ke database modul.
    - Analisa agent (untuk plan): nasib tabel request lama (di-drop, di-rename sebagai arsip, atau dibiarkan)
      dan urutan skrip `updates/` dirumuskan di plan.
12. ~~**Hal lama yang ikut dibahas.** Notifikasi, delegasi/cuti, eskalasi, any-of/kuorum, hub per item, UI MAUI,
    dan GET per field untuk action approval.~~ **Diputuskan pengguna 2026-10-16:**
    - **Hub tetap per jenis**: MY TASKS menampilkan jumlah request yang menunggu user per jenis approval; klik
      membuka Approval Manager yang terfilter. Hub per item tidak dibuat. Notifikasi hub keputusan System
      Approval 20 berbentuk pertambahan jumlah ini.
    - **GET per field diperbaiki sekalian** di rancang ulang: action list/filter approval dibuat ulang dengan
      parameter per field atau POST, dan jalur JSON lama di `EmApp.TryConvert` untuk action approval dihapus.
    - **Ditunda (bukan tahap pertama):** eskalasi/batas waktu, kuorum per level, UI approval di MAUI, dan email
      dari engine (engine hanya notifikasi hub; email lewat handler modul bila dibutuhkan).
    - Delegasi/cuti → keputusan System Approval 27. Any-of tercakup calon penanda tangan ganda dalam satu
      step (keputusan System Approval 17).
