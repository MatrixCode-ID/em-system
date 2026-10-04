# Plan — Membuat skill `scaffold-sdktables`

Status: **sudah dieksekusi — skill tertulis di `.claude/skills/scaffold-sdktables/`**
Dibuat: 2026-09-08 · Diperbarui: 2026-09-08 · Dieksekusi: 2026-09-08
Baseline: commit `86dd99d` (Perbarui dokumen scaffold skill, tambah pola service)
Sumber requirement: `doc/Persiapan buat skill.txt` (10 rule dari user)

> **Cara memakai dokumen ini kalau percakapan sudah di-clear.** Bagian 3 memuat keputusan yang
> sudah final (jangan dibuka ulang). Bagian 4 memuat titik yang **masih terbuka** dengan kode
> `K1`–`K7` (Keputusan) dan `M1`–`M9` (Masalah); yang sudah terjawab ditandai SELESAI dan
> jawabannya pindah ke bagian 3. Bagian 6 adalah
> alur skill versi terbaru; tiap langkah diberi tanda `[FINAL]` atau `[TERBUKA: kode]`.

## 0. Status terkini (per 2026-09-08)

**Desain sudah lengkap.** Seluruh keputusan (K1–K7) dan masalah (M1–M9) di bagian 4 sudah terjawab;
tidak ada langkah bertanda TERBUKA. Yang belum dikerjakan: menulis berkas skill-nya sendiri —
lihat bagian 5 (struktur berkas) dan bagian 9 (checklist).

**Perubahan kode yang sudah dilakukan sambil menyusun plan ini** (bukan bagian dari skill, muncul
sebagai temuan saat menelusuri pola). Semua sudah lolos `dotnet build Em.Api.slnx`:

| Berkas | Perubahan |
| --- | --- |
| `Em.Libs/Api.Core.Models/IContactServices.cs`, `Em.Api.Core/Api/Core/ContactServices.cs`, `Em.Ui.Wpf.Core/Api.Core/ContactService.cs` | `GetVi_Address_Count` → `GetVi_Addresses_Count` (satu-satunya `_Count` yang masih bentuk tunggal). |
| `Em.Api.Core/Api/Core/EmDbContext.cs` | Constructor jadi `EmDbContext(DbContextOptions options)` non-generik supaya context selain `ApiCoreContext` bisa mewarisinya. |
| `Em.Api.Core/Api/Core/Models/ApiCoreContext.cs` | Override `ConfigureConventions` yang kembar dihapus — sudah ada di base class. |
| `Em.Api.Core/Api/Core/EmApp.cs` | Registrasi service inti dikelompokkan ke method `InitInternalServices(EmAppBuilder builder)`, senama dengan padanannya di frontend. |

**Tiga salah tulis di `doc/service_pattern.cs`** yang ditemukan saat audit dan sudah dikoreksi di
plan ini — jangan disalin mentah-mentah saat menulis skill: `IService` seharusnya `IServices`,
`SetValue` seharusnya `SetField`, dan `GO;` di berkas view seharusnya `GO` polos.

---

## 1. Tujuan

Membuat skill Claude Code bernama `scaffold-sdktables` yang meng-generate lapisan boilerplate
(view SQL, model entity, kontrak interface, registrasi EF Core, service backend, service client
WPF, UI model) untuk satu atau beberapa tabel database sekaligus, mengikuti pola yang sudah
terbukti di tabel `Contact` / `Address` / `Comm`.

Skill disimpan di dalam repo (`{root}/.claude/skills/...`) supaya ikut ter-commit ke git dan
dipakai semua kontributor, bukan di `~/.claude` (rule #10).

---

## 2. Bahan yang sudah dipelajari

| Berkas | Perannya |
| --- | --- |
| `doc/skills-ref/sqlconnection.md` | Daftar koneksi database. Saat ini hanya 1: `EmDb` @ `<server-dev>`, user `sa`, trust server certificate. Berkasnya memakai escape backslash (`\-`, `\_`) — parser harus toleran. |
| `doc/service_pattern.cs` | Template resmi Tahap0–Tahap8. Guidance utama. |
| `doc/sqlscript/mssql/views/vi_*.sql` | Contoh nyata definisi view (`vi_Contact` pakai join, `vi_Address` polos). |
| `src/shared/Em.Libs/Api.Core.Models/*` | Model `ta_*`, `vi_*`, dan interface `IContactServices`. |
| `src/backend/Em.Api.Core/Api/Core/ContactServices.cs` | Implementasi service backend. |
| `src/backend/Em.Api.Core/Api/Core/Models/ApiCoreContext.cs` | Registrasi `DbSet`; turunan `EmDbContext`. |
| `src/backend/Em.Api.Core/Api/Core/EmDbContext.cs` | Base class untuk semua `DbContext`; memuat `ConfigureConventions`. |
| `src/shared/Em.Ui.Wpf.Core/Api.Core/ContactService.cs` | Service client WPF. |
| `src/shared/Em.Ui.Core/Api.Core.Models/*` | UI model (`Contact`, `ContactAddress`, `ContactCommunication`). |
| `src/backend/Em.Api.Core/Api/Core/EmApp.cs:30` | Registrasi DI backend. |
| `src/shared/Em.Ui.Wpf.Core/Core/EmApp.Statics.cs:74` | Registrasi DI frontend. |
| `src/shared/Em.Models/Sample/IEmSampleServices.cs` | Contoh module non-core, untuk membandingkan konvensi namespace. |

---

## 3. Keputusan yang sudah final

### 3.1 Namespace ditentukan module, bukan project

Pola nama: `{Base}.{Group}.{Module}` — contoh `Em.Api.Core` (SDK) dan `Em.Sample` (module).
Project fisik ditentukan oleh peran lapisan; namespace ditentukan oleh module. Namespace inilah
yang menyatukan tiga project yang tidak saling mereferensi (project UI tidak mereferensi
`Em.Api.Core`).

| Berkas | Project fisik | Namespace |
| --- | --- | --- |
| `ta_Contact.cs`, `IContactServices.cs` | `Em.Libs` | `Em.Api.Core.Models` |
| `Contact.cs` (UI model) | `Em.Ui.Core` | `Em.Api.Core.Models` |
| `ContactService.cs` (WPF) | `Em.Ui.Wpf.Core` | `Em.Api.Core` |
| `ContactServices.cs` (backend) | `Em.Api.Core` | `Em.Api.Core` |
| `ApiCoreContext.cs` | `Em.Api.Core` | `Em.Api.Core.Models` |
| `IEmSampleServices.cs` | `Em.Models` | `Em.Sample` |
| `SampleServices.cs` (backend & frontend) | `Em.Sample` ×2 | `Em.Sample` |

### 3.2 `Em.Libs` vs `Em.Models`

Pembagiannya soal kepemilikan, bukan jenis berkas:

- `Em.Libs` — **closed source**, memuat model milik SDK sendiri (`Contact`, `Comm`, `Address`,
  dan yang menyusul).
- `Em.Models` — model milik module di luar core (mis. kalau `Em.Sample` punya model).

Karena skill ini menyasar SDK, entity dan interface ditulis ke `Em.Libs/Api.Core.Models/`.

### 3.3 Nama folder: bertitik atau bertingkat

Nama folder selalu mencerminkan namespace. Segmen dipecah jadi folder bertingkat **hanya kalau
project itu memang sudah memiliki root tersebut sebagai pengelompok**:

```
Em.Libs/           Api.Core.Models/                    ← tidak ada root Api  → bertitik
Em.Ui.Core/        Api.Core.Models/  Ui.Core/          ← bertitik
Em.Ui.Wpf.Core/    Api.Core/                           ← bertitik
Em.Api.Core/       Api/Core/  Api/Core/Models/  Api/Shared/   ← Api root nyata → bertingkat
Em.Models/         Sample/                            ← Nsm root nyata → bertingkat
```

Aturan praktis untuk skill: kalau folder root segmen pertama sudah ada di project tujuan →
bertingkat; kalau tidak → satu folder bertitik.

### 3.4 Peta tujuan per lapisan (`{Group}`=`Api`, `{Module}`=`Core`)

| Lapis | Project fisik | Folder | Namespace |
| --- | --- | --- | --- |
| Entity `ta_`/`vi_` | `Em.Libs` | `Api.Core.Models/` | `Em.Api.Core.Models` |
| Interface `I…Services` | `Em.Libs` | `Api.Core.Models/` | `Em.Api.Core.Models` |
| `DbContext` | `Em.Api.Core` | `Api/Core/Models/` | `Em.Api.Core.Models` |
| Service backend | `Em.Api.Core` | `Api/Core/` | `Em.Api.Core` |
| Service WPF | `Em.Ui.Wpf.Core` | `Api.Core/` | `Em.Api.Core` |
| UI model | `Em.Ui.Core` | `Api.Core.Models/` | `Em.Api.Core.Models` |

### 3.5 Probing mengambil dua objek

Langkah probing membaca `ta_{Tabel}` **dan** `vi_{Tabel}`. Kolom model `vi_` = kolom view dikurangi
kolom yang diwarisi dari `ta_`. Tanpa ini, `vi_Contact.cs` (7 kolom hasil join) akan kosong saat
ditimpa dan memutus `Contact.cs` di UI.

### 3.6 Model selalu ditimpa

`ta_{Tabel}.cs` dan `vi_{Tabel}.cs` ditulis ulang total dari hasil probing, tanpa membandingkan
dan tanpa bertanya — model murni turunan schema, jadi merge tidak diperlukan dan hanya menambah
risiko. Konsekuensi yang diterima: kolom yang hilang di database ikut hilang dari model, dan kode
pemakainya gagal compile — itu memang yang diinginkan.

### 3.7 Yang diganti adalah method standar, bukan seluruh region

Region `#region ta_{Tabel}` / `#region vi_{Tabel}` **tidak** dihapus utuh. Yang dihapus hanya
method yang memang akan ditulis ulang oleh skill — daftar standar plus method turunan foreign key.
Method lain di dalam region itu, yang ditambahkan user sendiri, dibiarkan apa adanya.

Alurnya per tabel:

1. Temukan region `ta_{Tabel}` di dalam `#region Tables` dan `vi_{Tabel}` di dalam `#region Views`.
   Kalau belum ada, buat di akhir region induk (region induk dibuat kalau belum ada) dan langsung
   tulis seluruh method — tidak ada yang perlu dihapus.
2. Kalau region sudah ada: hapus method yang **namanya** sama dengan yang akan di-generate. Ikut
   terhapus baris yang menempel di atasnya (XML doc comment, atribut) supaya tidak meninggalkan
   komentar yatim.
3. Tulis blok standar yang baru di awal region, sisa method user tetap di posisinya.

Catatan teknis parser:
- Pencocokan cukup nama, tanpa signature — lihat 3.10: di service tidak boleh ada nama method
  kembar, jadi satu nama pasti menunjuk satu method.
- Region bersarang, jadi batas region dicari dengan menghitung pasangan `#region`/`#endregion`,
  bukan `#endregion` pertama yang ketemu. Gaya penutup tidak seragam di repo (`#endregion` polos
  vs `#endregion // region Nama_Tabel`), jadi pencocokan tidak boleh bergantung komentar.
- Di class implementasi (Langkah 9 dan 10), penghapusan per-method harus menyertakan atribut
  `[GetAction]` / `[PostAction]` di atasnya dan menghitung kurung kurawal untuk menemukan akhir
  method.

### 3.8 Titik registrasi DI

Ikuti yang sudah berjalan:
- Backend: `builder.AddService<I{Nama}Services, {Nama}Services>();` di dalam method
  `InitInternalServices` pada `src/backend/Em.Api.Core/Api/Core/EmApp.cs`.
- Frontend: `app.Services.AddSingleton<I{Nama}Services, {Nama}Service>();` di
  `src/shared/Em.Ui.Wpf.Core/Core/EmApp.Statics.cs`, method `InitInternalServices`.
- Dilewati kalau barisnya sudah ada.

### 3.9 Method `_Count` pada view selalu bentuk jamak

Kode lama tidak konsisten (`GetVi_Address_Count()` tunggal vs `GetVi_Comms_Count()` jamak). Skill
selalu men-generate bentuk jamak. Yang lama tidak diubah supaya skill tidak diam-diam mengubah API
yang sudah dipakai.

### 3.10 Service tidak boleh punya overload

Nama method di dalam satu service wajib unik — tidak boleh ada dua method bernama sama meski
signature-nya berbeda. Ini bukan sekadar konvensi: `EmAppBuilder.RegisterActions`
(`src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.cs`) mendaftarkan action berdasarkan
`method.Name` per module dan melempar `"Duplicate action name … Action names must be unique within
a module."` saat `BuildApp`, jadi overload membuat aplikasi gagal start.

Konsekuensi untuk skill:
- Penghapusan method standar cukup dicocokkan dari namanya (3.7).
- Sebelum menulis, skill memeriksa apakah nama yang akan di-generate sudah dipakai method lain di
  interface atau class yang sama; kalau ya, laporkan sebagai konflik dan jangan asal timpa.

### 3.11 Primary key gabungan: `_ById` menerima semua kolom key

Kalau PK terdiri dari lebih dari satu kolom, method `_ById` menerima seluruh kolom key sebagai
parameter, berurutan sesuai posisi ordinal di dalam key (`KEY_COLUMN_USAGE.ORDINAL_POSITION`,
bukan urutan kolom di tabel), dengan nama parameter sama seperti nama kolom:

```csharp
Task<ta_ProductTranData?> GetTa_ProductTranData_ById(string cProductId, string cTranId);
Task<vi_ProductTranData?> GetVi_ProductTranData_ById(string cProductId, string cTranId);
```

Turunannya di tiap lapisan:

- Backend: `Where(r => r.cProductId == cProductId && r.cTranId == cTranId).SingleOrDefaultAsync()`.
- Service WPF: `GetAsync<T>(nameof(...), cProductId, cTranId)`.
- UI model: `FetchAsync() => Service.GetVi_{Tabel}_ById(cProductId, cTranId)`, dan setiap kolom key
  jadi property tersendiri.
- Method `Post*` tidak terpengaruh karena parameternya entity, bukan id.
- Kalau tabel induk sendiri ber-PK gabungan, method turunan FK (`_By{Induk}Id`) ikut menerima
  seluruh kolom FK-nya dengan cara yang sama.

Konfigurasi EF Core untuk kasus ini ada di Langkah 8 (`HasKey`, lihat M8).

### 3.13 View yang belum ada: user yang memutuskan dieksekusi atau tidak

Kalau `vi_{Tabel}` belum ada di database, skill menulis berkas `.sql`-nya lalu **bertanya**: jalankan
script sekarang ke koneksi yang dipakai, atau lewati supaya user menjalankannya manual.

- Dijalankan → probing view bisa diulang di tempat, sehingga model `vi_` langsung berisi kolom yang
  benar dan seluruh lapisan berikutnya konsisten dalam sekali jalan.
- Dilewati → model `vi_` lahir sebagai cerminan `ta_` (tidak ada kolom tambahan). Skill wajib
  menyebut di ringkasan bahwa script perlu dijalankan dan tabel itu perlu di-scaffold ulang setelahnya.

### 3.14 Semua `DbContext` mewarisi `EmDbContext`

`src/backend/Em.Api.Core/Api/Core/EmDbContext.cs` sekarang menjadi base class untuk seluruh
context, dan `ApiCoreContext` sudah mewarisinya. Context baru di kemudian hari juga harus mewarisi
`EmDbContext`, bukan `DbContext` langsung.

Konsekuensi untuk skill: `ConfigureConventions` (yang membuang `BaseTypeDiscoveryConvention`) adalah
urusan base class — skill tidak pernah menulis atau menyentuhnya di context turunan.

### 3.12 Aturan per-method berlaku juga di class implementasi

Langkah 9 (service backend) dan Langkah 10 (service WPF) memakai aturan yang sama dengan 3.7:
ganti hanya method yang di-generate skill (cocok berdasarkan nama), biarkan method lain.

Alasannya bukan sekadar kerapian. `EmAppBuilder.AddService` memanggil
`GetActionableMethods(typeof(T2))` — **class konkret**, bukan interface — sehingga setiap method
publik di implementasi yang membawa `[GetAction]`/`[PostAction]` terdaftar sebagai action API
walau tidak ada di interface. Akibatnya dua arah penyimpangan sangat berbeda:

- Interface punya method yang tidak ada di implementasi → gagal compile (`CS0535`). Berisik, aman.
- Implementasi menyimpan method lama yang sudah hilang dari interface → tetap compile, dan method
  itu **tetap jadi endpoint hidup** yang bisa dipanggil client tanpa kontrak. Ini yang harus
  dicegah.

Aturan parser tambahan untuk class implementasi:
- Ikut hapus baris atribut `[GetAction]` / `[PostAction]` di atas method. Atribut yatim akan
  menempel ke method berikutnya dan diam-diam menjadikannya action.
- Tangani dua bentuk badan method: blok berkurung (hitung pasangan `{`/`}`) dan expression-bodied
  yang diakhiri `;` (mis. `public Task<int> GetTa_Contacts_Count() => ctx.ta_Contacts.CountAsync();`).
- Pencocokan tetap berdasarkan nama saja (3.10). Ini sekaligus menangani perubahan signature:
  `GetTa_X_ById(string a)` yang berubah jadi `(string a, string b)` karena PK jadi gabungan akan
  tergantikan di interface dan implementasi sekaligus.

Di ringkasan akhir, skill mendaftar method ber-`[GetAction]`/`[PostAction]` di class implementasi
yang tidak ada di interface — bukan kesalahan, tapi tiap method seperti itu adalah endpoint hidup
tanpa kontrak, jadi ada gunanya terlihat.

---

## 4. Riwayat titik terbuka — semuanya sudah terjawab

### Keputusan desain

| Kode | Pertanyaan | Konteks |
| --- | --- | --- |
| **K1** (eks-A3) | ~~`SKILL.md` mengunci nilai `Api.Core` atau menulisnya sebagai rumus?~~ | **SELESAI** — ditulis sebagai rumus `{Group}`/`{Module}` yang nilainya (`Api`/`Core`) dikunci di satu tempat di atas `SKILL.md`. Perilaku sekarang identik dengan nilai mati; perluasan ke module lain nanti tinggal melepas kuncinya. |
| **K2** (eks-A4) | ~~Nilai `[Module(...)]` otomatis atau ditanya?~~ | **SELESAI** — **selalu ditanya ke user**, tidak ditebak skill. Nilai itu keputusan desain user (nama controller, mis. `core.contact`), bukan turunan mekanis dari nama tabel — satu interface bisa memuat banyak tabel dengan satu nilai module. |
| **K3** (eks-B) | ~~Nama class backend vs frontend.~~ | **SELESAI** — ikut kode nyata: backend `{X}Services`, frontend `{X}Service`. Diturunkan dari nama interface: buang `I` untuk backend, buang `I` dan `s` terakhir untuk frontend. |
| **K4** (eks-C) | ~~XML doc `InitInternalServices` diperbarui atau dibiarkan?~~ | **SELESAI** — ikut diperbarui saat service baru didaftarkan supaya kalimat "saat ini data kontak" tidak jadi salah. Tetap Bahasa Indonesia sesuai `CLAUDE.md`. |
| **K5** (eks-D) | ~~Script `CREATE VIEW` dieksekusi atau cukup ditulis?~~ | **SELESAI** — lihat 3.13: skill bertanya ke user; dijalankan sekarang atau dilewati untuk dijalankan manual. |
| **K6** (eks-E) | ~~Probing lewat `sqlcmd` atau MCP Rider?~~ | **SELESAI** — `sqlcmd` jalur utama, MCP Rider `execute_sql_query` sebagai cadangan kalau `sqlcmd` gagal. |
| **K7** (eks-F) | ~~Cakupan sampai Tahap8, Tahap6, atau ditanya?~~ | **SELESAI** — cakupan penuh Tahap0–8. Seluruh langkah dijalankan berurutan sampai selesai; skill **tidak** bertanya apakah lanjut ke Tahap7–8. |

### Masalah idempotensi (muncul dari penelusuran kasus `ta_Contact`)

| Kode | Masalah | Usul |
| --- | --- | --- |
| **M1** | ~~Menghapus region interface ikut menghapus method non-standar buatan user.~~ | **SELESAI** — lihat 3.7: yang dihapus hanya method yang akan di-generate ulang (cocok nama + signature); method lain dibiarkan. |
| **M6** | ~~Nama method lama yang tidak konsisten tidak ikut terhapus karena namanya berbeda.~~ | **SELESAI** — dilaporkan di ringkasan sebagai kemungkinan sisa yang perlu user tinjau, tidak pernah dihapus otomatis (bisa saja masih dipakai kode lain). Pemicunya, `GetVi_Address_Count()`, sudah diperbaiki jadi `GetVi_Addresses_Count()` di `IContactServices.cs`, `ContactServices.cs`, dan `ContactService.cs`. |
| **M7** | ~~Posisi blok standar di dalam region yang sudah berisi method user.~~ | **SELESAI** — blok standar selalu ditulis di awal region; method buatan user tetap di urutan aslinya sesudahnya. |
| **M2** | ~~Aturan Langkah 8 saat `DbSet` sudah ada.~~ | **SELESAI** — pengecekan per-artefak: baris `DbSet` yang sudah ada dilewati tanpa bertanya, tapi konfigurasi key (M8) tetap diperiksa dan dilengkapi. |
| **M8** | **SELESAI (lihat Langkah 8 dan 3.11).** Primary key gabungan. `[Key]` di atas property hanya sah untuk PK satu kolom. Kalau probing menemukan PK lebih dari satu kolom, entity-nya harus dikonfigurasi lewat `modelBuilder.Entity<ta_{Tabel}>().HasKey(e => new { e.cA, e.cB })` di `OnModelCreating` — dan `ApiCoreContext` belum punya override `OnModelCreating` sama sekali. Berlaku untuk `ta_` **dan** `vi_`, karena `vi_` mewarisi entity-nya dan tidak punya key sendiri. | Langkah 8 mendeteksi jumlah kolom PK: satu kolom → cukup `[Key]` seperti sekarang; lebih dari satu → jangan tulis `[Key]`, buat/lengkapi `OnModelCreating` dengan `HasKey` untuk kedua entity. **Terbuka:** bentuk method `_ById` untuk key gabungan — lihat M9. |
| **M9** | ~~Method standar mengasumsikan PK satu kolom.~~ | **SELESAI** — lihat 3.11: `_ById` menerima semua kolom key sesuai urutan ordinal key. |
| **M3** | ~~Apakah aturan penggantian per-method berlaku juga di class implementasi?~~ | **SELESAI** — lihat 3.12: ya, berlaku di Langkah 9 dan 10 dengan aturan parser tambahan. |
| **M4** | ~~UI model yang sudah ada memuat kode tulisan tangan di region `Statics`.~~ | **SELESAI** — UI model yang sudah ada **dilewati sepenuhnya**, tidak pernah ditimpa, dan skill memberi tahu user bahwa berkas itu dilewati. |
| **M5** | ~~UI model hasil rename user tidak terdeteksi sebagai duplikat.~~ | **SELESAI** — sebelum menulis, skill mencari class lain di folder yang sama yang mewarisi `UiModel<vi_{Tabel}, …>` (mis. `ContactAddress` untuk `vi_Address`); kalau ada, laporkan sebagai kemungkinan duplikat dan jangan tulis apa pun. |

---

## 5. Struktur berkas skill

```
.claude/
└── skills/
    └── scaffold-sdktables/
        ├── SKILL.md                     # instruksi utama + alur tanya-jawab
        └── references/
            ├── templates.md             # potongan kode per tahap (ta_, vi_, interface, service, UI model)
            ├── naming.md                # aturan singular/jamak, FK, penamaan method
            └── probing.md               # query INFORMATION_SCHEMA + pemetaan tipe SQL → C#
```

`SKILL.md` selalu dibaca penuh saat skill dipanggil, jadi isinya cukup alur keputusan; template
kode delapan tahap yang panjang dipindah ke `references/` dan dibaca hanya saat tahapnya tiba.

`.gitignore` sudah dicek: tidak ada entri `claude`, jadi skill ikut ter-commit.

Frontmatter `SKILL.md`:

```yaml
---
name: scaffold-sdktables
description: >-
  Scaffold lapisan SDK untuk satu atau beberapa tabel database Em: view SQL, model ta_/vi_,
  kontrak interface I{Nama}Services, registrasi DbSet, service backend Em.Api.Core, service
  client WPF, dan UI model. Pakai skill ini ketika user minta menambahkan tabel baru ke SDK,
  "scaffold tabel X", "generate service untuk tabel X", atau membuat interface service tabel.
---
```

---

## 6. Alur skill (versi terbaru)

### Langkah 1 — Pilih koneksi `[FINAL]`
Baca `doc/skills-ref/sqlconnection.md`, parse tiap blok `## <Nama>` jadi satu kandidat koneksi.
Lebih dari satu → tanya user; tepat satu → pakai langsung dan sebutkan di output; tidak ada →
berhenti dan minta user melengkapi.

### Langkah 2 — Tanya daftar tabel `[FINAL]`
Nama tabel dipisah koma, diterima dengan atau tanpa prefiks `ta_`, dinormalkan ke nama polos.
Tiap nama diverifikasi ke database; yang tidak ditemukan dilaporkan dan user diminta konfirmasi.

### Langkah 3 — Tanya interface tujuan `[FINAL]`
Cari `I{Nama}.cs` di `src/shared/Em.Libs/Api.Core.Models/`. Sudah ada → dipakai. Belum ada →
konfirmasi buat baru, sekalian **tanya** `{Module_Name}` untuk atribut `[Module(...)]`. Nilai itu
keputusan desain user (nama controller, mis. `core.contact`) — jangan ditebak dari nama tabel,
karena satu interface bisa memuat banyak tabel dengan satu nilai module (K2).

### Langkah 4 — Probing `[FINAL]`
Query `INFORMATION_SCHEMA` untuk `ta_{Tabel}` **dan** `vi_{Tabel}`: nama kolom, urutan ordinal,
tipe SQL, nullable, primary key berikut posisi ordinal tiap kolom di dalam key. Kolom nullable →
tipe C# nullable; urutan property mengikuti urutan kolom. Kolom model `vi_` = kolom view − kolom
`ta_`. Cara koneksinya lihat K6.

Dua pemeriksaan tambahan di sini:
- **Kolom tabel yang tidak ada di view.** Karena `vi_` mewarisi `ta_`, EF memetakan semua property
  warisan ke view; kolom yang tidak diekspos view bikin query `vi_` gagal saat runtime, dan itu
  tidak ketahuan waktu compile. Skill **memperingatkan** kalau ada selisih seperti itu, tapi tetap
  melanjutkan langkah berikutnya.
- **Primary key.** Setiap tabel diasumsikan punya PK (konvensi `doc/Struktur penamaan object
  database..md`). Kalau ternyata tidak ada, laporkan dan lewati tabel itu — EF tidak bisa
  memetakannya sebagai entity dan seluruh method `_ById` kehilangan dasar.

### Langkah 5 — Tahap0: view `[FINAL]`
Kalau `vi_{Tabel}` belum ada di database, tulis `doc/sqlscript/mssql/views/vi_{Tabel}.sql`
mengikuti gaya berkas yang ada (`USE <db>`, `DROP VIEW IF EXISTS`, `GO;`, `CREATE VIEW … AS SELECT
<kolom eksplisit> FROM ta_{Tabel} AS a`, `GO`, lalu SELECT verifikasi). Kolom disebut satu per
satu, bukan `SELECT *`.

**Penting:** pemisah batch ditulis `GO` polos, bukan `GO;`. Berkas view yang ada sekarang memakai
`GO;` di baris ketiga — `sqlcmd` menolaknya ("Incorrect syntax was encountered while parsing GO"),
jadi gaya itu tidak boleh ditiru untuk berkas yang mungkin dieksekusi skill. Saat menjalankan
script, jalankan bagian DDL-nya saja; SELECT verifikasi di bawah `GO` kedua tidak perlu ikut.

Lalu tanya user: jalankan script sekarang, atau lewati untuk dijalankan manual (3.13). Kalau
dijalankan, ulangi probing view sebelum lanjut ke Langkah 6.

### Langkah 6 — Tahap1: model `[FINAL]`
Tulis ulang total `ta_{Tabel}.cs` dan `vi_{Tabel}.cs` di `src/shared/Em.Libs/Api.Core.Models/`,
namespace `Em.Api.Core.Models`. `vi_{Tabel}` mewarisi `ta_{Tabel}` dan hanya memuat kolom
tambahan hasil join (kosong kalau view cerminan tabel).

### Langkah 7 — Tahap2 & Tahap5: kontrak interface `[FINAL]`
Interface baru dideklarasikan `public interface I{Nama}Services : IServices` dengan
`using Em.Shared;` — `IServices`, bukan `IService` seperti tertulis di `service_pattern.cs`.

Hapus **method standar** yang sudah ada di region (cocok berdasarkan nama saja — lihat 3.7 dan
3.10), biarkan method lain buatan user, lalu tulis blok berikut:

```
#region Tables → #region ta_{Tabel}
   GetTa_{Tabel}_ById, GetTa_{Jamak}, GetTa_{Jamak}_Count, GetTa_{Jamak}_InPage,
   PostTa_{Tabel}_New / _NewBatch / _Update / _UpdateBatch / _Delete / _DeleteBatch
#region Views → #region vi_{Tabel}
   GetVi_{Tabel}_ById, GetVi_{Jamak}, GetVi_{Jamak}_Count, GetVi_{Jamak}_InPage
```

Tambahan otomatis: kalau tabel punya kolom FK `c{Induk}Id` (mis. `cContactId` di `ta_Address`),
tambahkan `GetTa_{Jamak}_By{Induk}Id` dan `GetVi_{Jamak}_By{Induk}Id`. Kolom FK "default" satu arah
(`cContactDefaultAddress_cAddressId`) **tidak** memicu method ini karena arah relasinya terbalik.

### Langkah 8 — Tahap3: `ApiCoreContext` `[FINAL]`
Tambah dua baris (nama property **jamak**) di
`src/backend/Em.Api.Core/Api/Core/Models/ApiCoreContext.cs`, lewati yang sudah ada:

```csharp
public DbSet<ta_{Tabel}> ta_{Jamak} => Set<ta_{Tabel}>();
public DbSet<vi_{Tabel}> vi_{Jamak} => Set<vi_{Tabel}>();
```

Lalu konfigurasi key sesuai jumlah kolom PK hasil probing:

- **PK satu kolom** — cukup `[Key]` di model (Langkah 6), tidak ada yang ditambahkan di context.
- **PK gabungan** — jangan tulis `[Key]` di model; buat override `OnModelCreating` kalau belum ada,
  lalu daftarkan key untuk kedua entity:

  ```csharp
  protected override void OnModelCreating(ModelBuilder modelBuilder) {
     base.OnModelCreating(modelBuilder);
     modelBuilder.Entity<ta_{Tabel}>().HasKey(e => new { e.cA, e.cB });
     modelBuilder.Entity<vi_{Tabel}>().HasKey(e => new { e.cA, e.cB });
  }
  ```

  `vi_` ikut didaftarkan karena ia mewarisi entity `ta_` dan tidak punya key sendiri.

Pengecekan dilakukan per-artefak: `DbSet` yang sudah ada dilewati, tapi konfigurasi key tetap
diperiksa dan dilengkapi kalau belum ada.

`ApiCoreContext` mewarisi `EmDbContext` (3.14), dan `ConfigureConventions` adalah urusan base
class — jangan ditulis atau diubah di context turunan.

### Langkah 9 — Tahap4 & Tahap6: service backend `[FINAL]`
`src/backend/Em.Api.Core/Api/Core/{Nama}Services.cs`, namespace `Em.Api.Core`: class
`[Module("{Module_Name}")]`, primary constructor `(ApiCoreContext ctx)`, turunan `ServicesBase`.
Method Get diberi `[GetAction]`, Post diberi `[PostAction]`, isi persis pola `ContactServices.cs`
(`try { … } catch (Exception) { throw; }`; batch memakai `BulkInsertAsync` / `BulkUpdateAsync` /
`BulkDeleteAsync`).

### Langkah 10 — Tahap7: service client WPF `[FINAL]`
`src/shared/Em.Ui.Wpf.Core/Api.Core/{Nama}Service.cs`, namespace `Em.Api.Core`: class
`[Module("{Module_Name}")]`, primary constructor `(EmApp emApp)`, turunan
`ServiceWpfBase(emApp)`, tiap method satu baris `GetAsync<T>(nameof(…), args)` atau
`PostAsync(nameof(…), data)`.

### Langkah 11 — Tahap8: UI model `[FINAL]`
`src/shared/Em.Ui.Core/Api.Core.Models/{Tabel}.cs`: `class {Tabel} : UiModel<vi_{Tabel},
I{Nama}Services>`, constructor privat, property `set => SetField(ref field, value)` dengan default
value (`string.Empty` untuk string), lalu `ReadFrom`, `WriteTo`, `BuildJson(patch) => patch`,
`FetchAsync`, `UpdateAsync`.

Setter memakai `SetField`, bukan `SetValue` seperti tertulis di `service_pattern.cs` — helper yang
benar-benar ada di `UiModel` adalah `protected bool SetField<T>(ref T field, T value, …)`.

Region `#region Statics` berisi tepat satu baris hasil generate, sisanya placeholder user:

```csharp
public static {Tabel} Build(IEmApp app, vi_{Tabel} data) => new(app, data);
```

Tanpa itu class-nya tidak punya jalan konstruksi yang bisa diakses dari luar — constructor-nya
privat — sehingga lolos compile tapi tidak bisa dipakai siapa pun.

Kolom standar `ustamp` / `datestamp` / `json_object` sudah ada di base `UiModel` — tetap dipetakan
di `ReadFrom`/`WriteTo`, tapi tidak dideklarasikan ulang sebagai property.

Rule #9: nama class mengikuti nama tabel apa adanya (`Address`, `Comm`) — skill tidak mengarang
`ContactAddress` / `ContactCommunication`; user yang me-rename bila perlu.

Dua pemeriksaan sebelum menulis, keduanya berakhir dengan **tidak menulis apa pun** dan memberi tahu
user: berkas `{Tabel}.cs` sudah ada (M4), atau ada class lain di folder itu yang mewarisi
`UiModel<vi_{Tabel}, …>` sehingga kemungkinan duplikat hasil rename user (M5).

### Langkah 12 — Registrasi DI `[FINAL]`
Dua titik di 3.8, dilewati kalau barisnya sudah ada.

### Langkah 13 — Ringkasan `[FINAL]`
Yang dilaporkan:
- Berkas yang dibuat dan diubah.
- Script SQL yang masih perlu dijalankan manual, berikut tabel yang perlu di-scaffold ulang setelahnya (3.13).
- Sisa method lama bernama tidak konsisten yang perlu ditinjau (M6).
- Method ber-`[GetAction]`/`[PostAction]` di class implementasi yang tidak ada di interface — endpoint
  hidup tanpa kontrak (3.12).
- Berkas UI model yang dilewati beserta alasannya: sudah ada (M4) atau kemungkinan duplikat (M5).
- Sisa pekerjaan user: rename UI model, isi region `Statics`, jalankan build.

---

## 7. Aturan penamaan (`references/naming.md`)

- **Jamak** untuk: method yang mengembalikan banyak baris, `_Count`, `_InPage`, dan nama property
  `DbSet`. **Tunggal** untuk `_ById` dan semua method `Post*` (termasuk `…_NewBatch` — yang jamak
  di situ parameternya, bukan nama tabelnya).
- Pembentukan jamak (Inggris): akhiran `s`/`x`/`z`/`ch`/`sh` → `+es`; konsonan + `y` → `ies`;
  selain itu `+s`. Contoh: `Contact→Contacts`, `Address→Addresses`, `Comm→Comms`,
  `Category→Categories`.
- Nama kolom mengikuti `doc/Struktur penamaan object database..md`: `c` + nama tabel + nama kolom.
- FK tabel anak: `c{Induk}Id`. FK "default" satu arah di tabel induk:
  `c{Tabel}Default{Target}_c{Target}Id`.
- Nama berkas: `ta_{Tabel}.cs`, `vi_{Tabel}.cs`, `I{Nama}Services.cs`, `{Nama}Services.cs`
  (backend), `{Nama}Service.cs` (frontend), `{Tabel}.cs` (UI model). Lihat K3.

---

## 8. Batasan — yang **tidak** dilakukan skill

- Tidak membuat atau mengubah tabel `ta_*` di database (hanya membaca struktur).
- Tidak menambahkan komentar XML doc; berkas `src/shared/*` yang butuh doc Bahasa Indonesia
  diserahkan ke permintaan terpisah sesuai `CLAUDE.md`.
- Tidak menyebut nama objek database di help text yang menghadap module author (`CLAUDE.md`);
  nama `ta_*` / `vi_*` hanya muncul di kode implementasi dan dokumentasi internal skill.
- Tidak menyentuh module di `src/frontend/modules/*` dan `src/backend/modules/*`.
- Tidak melakukan commit; hasil scaffold ditinggalkan di working tree untuk direview user.

---

## 9. Checklist eksekusi

1. [x] Jawab K1–K7 dan M1–M9 di bagian 4 — semuanya sudah terjawab.
2. [x] Buat `.claude/skills/scaffold-sdktables/SKILL.md`.
3. [x] Buat `references/templates.md`, `references/naming.md`, `references/probing.md`.
4. [x] Uji kering dengan tabel `Comm` — lolos. Model, blok interface, blok service backend, dan blok
       service WPF hasil template setara persis dengan berkas nyata, termasuk urutan method
       (`_ByContactId` sebelum `_InPage` di blok tabel, sesudahnya di blok view). Satu-satunya selisih:
       `cCommNote` di database nullable sementara `ta_Comm.cs` yang ada menulisnya `string` non-null —
       skill akan mengoreksinya jadi `string?`, persis konsekuensi yang diterima di 3.6. UI model
       `Comm.cs` tidak ditulis karena `ContactCommunication : UiModel<vi_Comm, IContactServices>`
       terdeteksi sebagai kemungkinan duplikat (M5), jadi berkas itu aman.
5. [x] Pindahkan plan ini ke `plan/executed/` setelah selesai.
