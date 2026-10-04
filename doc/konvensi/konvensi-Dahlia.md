# KONVENSI-DAHLIA

**Standard penamaan object database dan model C#**

KONVENSI-DAHLIA adalah standard penamaan untuk skema yang dibangun di atas engine em-system dan repo turunannya (mis. EmPorium House). Isinya dua bagian yang saling terkait:

1. [Object database](#bagian-1--object-database): tabel, kolom, view, fungsi, dan stored procedure.
2. [Model C#](#bagian-2--model-c): class ORM yang memetakan tabel/view, dan class turunannya di API maupun UI.

Objek baru mengikuti standard ini, dan repo turunan tidak membuat aturan sendiri. Skrip SQL Server engine ada di `doc/sqlscript/mssql/`.

Dokumen ini hanya mengatur **penamaan**. Database dan kode yang sudah berjalan bisa saja memuat objek dengan nama atau susunan yang berbeda; objek seperti itu tetap sah dan tidak wajib diganti. Nama constraint (primary key, unique, foreign key) dan collation tidak termasuk cakupan dokumen ini.

## Tujuan

Tujuan utama awalan dan pola nama ini adalah **membantu memilah IntelliSense di IDE**. Dengan beberapa huruf pertama saja, daftar autocomplete sudah tersaring ke jenis objek atau tabel yang dimaksud, baik saat menulis SQL (SSMS, Azure Data Studio, DataGrip/Rider) maupun C#.

- **Awalan jenis objek menyaring daftar.** Mengetik `ta_` hanya menampilkan tabel, `vi_` hanya view, `sp_`/`fs_`/`ft_` hanya procedure dan fungsi. Tabel dan view yang bernama sama (`ta_Product`, `vi_Product`) muncul berurutan dan mudah dibedakan.
- **Nama tabel di nama kolom menyaring per tabel.** Mengetik `cProduct` hanya menampilkan kolom milik `ta_Product`, tidak tercampur dengan `Id`, `Name`, atau `Note` dari ratusan tabel lain.
- **Tabel anak berkumpul dengan induknya.** Karena nama anak mewarisi nama induk, `ta_Product`, `ta_ProductTran`, dan `ta_ProductTranData` tampil bersebelahan; begitu pula kolomnya (`cProduct…`, `cProductTran…`).
- **Procedure dan fungsi berkumpul per tabel.** Pola `<awalan><Tabel>_<Tugas>` membuat semua `sp_Product_…` tampil bersama, dan nama tugas setelah `_` menjelaskan isinya tanpa membuka definisinya.
- **Kolom view terpilah menurut asalnya.** Di view, `c` adalah kolom tabel, `cc` hasil hitung, dan `cv` kolom khusus view, sehingga terlihat dari autocomplete mana yang bisa ditulis balik ke tabel.
- **Berlanjut sampai ke kode C#.** Class ORM memakai nama tabel/view dan nama kolom yang sama persis. Di class turunan, member yang berawalan `c` selalu berasal dari database, sedangkan member tanpa awalan adalah logika aplikasi. Lihat [Bagian 2](#bagian-2--model-c).

Manfaat tambahan dari nama yang unik di seluruh database:

- **Join tanpa ambigu.** Kolom yang namanya unik tidak menimbulkan error *ambiguous column name* dan tidak selalu memerlukan alias tabel.
- **Mudah dicari.** Pencarian teks (`git grep cProductPrice`) langsung menemukan semua pemakaian satu kolom di SQL, C#, dan XAML, tanpa hasil palsu dari tabel lain.
- **Asal data terbaca dari namanya.** Dari `cProductTranEmpRef_cEmpId` terbaca tabel asal, arti kolom, dan kolom yang dirujuk.

---

## Bagian 1 — Object database

### Ringkasan awalan

| Objek | Awalan | Contoh |
|---|---|---|
| Tabel | `ta_` | `ta_Product` |
| View | `vi_` | `vi_Product` |
| Fungsi skalar | `fs_` | `fs_Customer_GetAllInRegion` |
| Fungsi tabel | `ft_` | `ft_Product_ListActive` |
| Stored procedure | `sp_` | `sp_Product_Recalculate` |
| Kolom | `c` + nama tabel | `cProductId` |
| Kolom hitung pada view (opsional) | `cc` + nama tabel | `ccProductTotal` |
| Kolom khusus view (opsional) | `cv` + nama tabel | `cvProductColor` |

Nama objek tidak memakai spasi dan ditulis PascalCase setelah awalannya.

### Tabel

- Nama tabel diawali `ta_`, misalnya `ta_Product`.
- **Tabel anak mewarisi nama induknya.** `ta_ProductTran` adalah turunan `ta_Product`, dan `ta_ProductTranData` adalah turunan `ta_ProductTran`.

### Kolom

Nama kolom diawali `c`, diikuti nama tabel tanpa `ta_`, lalu nama kolomnya. Contoh: `cProductId` adalah kolom `Id` pada `ta_Product`.

#### Kolom standar

Bila tabel memerlukan kolom berikut, gunakan nama, tipe, dan urutan ini. Kolom yang tidak relevan untuk tabel tersebut boleh tidak dibuat.

| Kolom | Tipe | Default | Arti |
|---|---|---|---|
| `c<Tabel>Id` | `char(26)` | | Primary key berisi [ULID](https://github.com/ulid/spec). |
| `c<Tabel>Name` | `varchar(255)` | | Nama data. Dikosongkan bila data tidak memiliki nama. |
| `c<Tabel>RefNumber` | `datetime` | | Dasar nomor data yang ditampilkan ke pengguna; aplikasi memformatnya menjadi nomor tampilan. |
| `c<Tabel>Stage` atau `c<Tabel>State` | `int` | | Tahap dokumen atau keadaan data, lihat [Stage dan State](#stage-dan-state). |
| `c<Tabel>Order` | `int` | `-1` | Urutan, diisi aplikasi bila diperlukan. |
| `c<Tabel>Revision` | `int` | `1` | Nomor revisi, diisi aplikasi bila diperlukan. |
| `c<Tabel>Date` | `date` | | Tanggal data; boleh diisi pengguna. |
| `c<Tabel>Note` | `varchar(500)` | `NULL` | Keterangan data. |
| `ustamp` | `datetime` | | Waktu terakhir diperbarui (update stamp). |
| `datestamp` | `datetime` | | Waktu dibuat (create stamp). |
| `json_object` | `nvarchar(max)` | `NULL` | Data tambahan dalam bentuk JSON. |

`ustamp`, `datestamp`, dan `json_object` ditulis apa adanya, tanpa awalan `c<Tabel>`.

#### Stage dan State

Pilih salah satu sesuai jenis datanya:

- **Stage** untuk tabel **dokumen** (business object yang melewati alur kerja, mis. pesanan, faktur, permintaan). Stage menyatakan tahap dokumen dalam alurnya.
- **State** untuk tabel data **bukan dokumen** (mis. user, robot, master data). Data seperti user tidak melewati tahap draft, submit, atau approve, sehingga cukup menyatakan keadaannya (mis. aktif atau nonaktif). Nilai State ditentukan aplikasi untuk tiap tabel.

Nilai Stage:

| Nilai | Arti |
|---|---|
| `< -1` | status negatif lain, mis. dokumen dihapus |
| `-1` | void |
| `0` | draft |
| `> 0` | dapat diproses; standarnya `1` = publish. Aplikasi boleh memakai urutan sendiri, mis. `1` submit, `2` approve, `3` close |

Nilai Stage diatur oleh business logic aplikasi, sehingga tidak ada tabel master untuknya.

#### Kolom tambahan

Kolom yang muncul belakangan, setelah tabel dipakai, tidak ditambahkan sebagai kolom baru. Datanya di-serialize ke JSON dan disimpan di `json_object`.

### Foreign key

- Kolom foreign key memakai nama kolom key induknya. Contoh: kolom yang merujuk `cEmpId` pada tabel lain juga bernama `cEmpId`.
- Bila kolom foreign key memakai nama sendiri (non-standar), sertakan nama kolom asal setelah `_`: `c<Tabel><Nama>_<kolom induk>`. Contoh: `cProductTranEmpRef_cEmpId` adalah kolom `EmpRef` pada `ta_ProductTran` yang merujuk `cEmpId`.

### View

- Nama view diawali `vi_` dan **sama dengan nama tabelnya**: tabel `ta_Product` memiliki view `vi_Product`.
- Setiap tabel yang berinteraksi dengan UI wajib memiliki view `vi_`.
- View tidak boleh berisi kalkulasi berat. Bila perlu kalkulasi, pakai `sp_` atau `fs_`.
- View sebaiknya memiliki tabel utama. Minimalkan view yang tidak berdasar tabel.
- Isi `json_object` boleh diuraikan menjadi kolom di view.

#### Kolom `cc` dan `cv`

Opsional, dipakai hanya bila diperlukan. Keduanya menandai kolom view yang tidak berasal langsung dari kolom tabel, sehingga pembaca tahu kolom itu tidak bisa ditulis balik ke `ta_`. Kolom yang diambil apa adanya dari tabel tetap memakai nama `c` aslinya.

| Awalan | Nama | Arti | Contoh |
|---|---|---|---|
| `cc` | compute column | Hasil perhitungan atau ekspresi dari kolom lain. | `ccProductTotal` = `cProductQty * cProductPrice` |
| `cv` | column view | Kolom yang hanya ada di view, mis. nilai yang diuraikan dari `json_object` atau kolom yang diberi nama baru. | `cvProductColor` dari `json_object` |

Bentuk namanya mengikuti kolom biasa: awalan, nama tabel tanpa `ta_`, lalu nama kolom.

```sql
CREATE VIEW [dbo].[vi_Product] AS
SELECT p.*,
       p.[cProductQty] * p.[cProductPrice]          AS [ccProductTotal],
       JSON_VALUE(p.[json_object], '$.color')       AS [cvProductColor]
FROM [dbo].[ta_Product] p;
```

### Fungsi dan stored procedure

- Fungsi skalar diawali `fs_`, fungsi tabel `ft_`, stored procedure `sp_`.
- Usahakan setiap fungsi atau procedure terikat pada satu tabel.
- Nama memuat nama tabel, lalu tugasnya setelah `_`: `<awalan><Tabel>_<Tugas>`. Contoh: `fs_Customer_GetAllInRegion`.

### Contoh tabel

```sql
CREATE TABLE [dbo].[ta_Product]
(
   [cProductId]        char(26)      NOT NULL,
   [cProductName]      varchar(255)  NULL,
   [cProductRefNumber] datetime      NULL,
   [cProductStage]     int           NOT NULL,
   [cProductOrder]     int           NOT NULL DEFAULT -1,
   [cProductRevision]  int           NOT NULL DEFAULT 1,
   [cProductDate]      date          NULL,
   [cProductNote]      varchar(500)  NULL,
   [cProductQty]       int           NOT NULL,
   [cProductPrice]     decimal(18,2) NOT NULL,
   [ustamp]            datetime      NOT NULL,
   [datestamp]         datetime      NOT NULL,
   [json_object]       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_Product] PRIMARY KEY CLUSTERED ([cProductId])
);

CREATE TABLE [dbo].[ta_ProductTran]
(
   [cProductTranId]            char(26) NOT NULL,
   [cProductId]                char(26) NOT NULL,  -- FK standar: nama kolom induk
   [cProductTranEmpRef_cEmpId] char(26) NULL,      -- FK non-standar: nama sendiri + kolom induk
   -- ... kolom standar lain ...
);
```

---

## Bagian 2 — Model C#

Kode C# dibagi menjadi dua lapis dengan aturan penamaan yang berbeda:

| Lapis | Nama class | Isi | Contoh di engine |
|---|---|---|---|
| **Class model ORM** | sama dengan tabel/view: `ta_<Tabel>`, `vi_<Tabel>` | hanya property kolom | `ta_User`, `vi_User` |
| **Class turunan** | tanpa awalan: `<Tabel>` atau nama bebas | property cermin kolom, ditambah property dan method aplikasi | `User` |

Pemisahan ini membuat IntelliSense terpilah dengan sendirinya: semua yang berawalan `ta_`, `vi_`, atau `c` berasal dari database, sedangkan yang tanpa awalan adalah logika aplikasi.

### Class model ORM

Class model ORM adalah cermin satu tabel atau view, dipakai oleh EF Core, Dapper, dan sebagai DTO antara API dan UI.

1. **Nama class sama persis dengan nama tabel/view,** termasuk awalannya: `ta_User` untuk tabel `ta_User`, `vi_User` untuk view `vi_User`. Pasang `[Table("ta_User")]` dan `[Key]` pada primary key.
2. **Nama property sama persis dengan nama kolom,** termasuk awalan `c`/`cc`/`cv` dan kolom standar `ustamp`, `datestamp`, `json_object`.
3. **Tidak boleh ada method atau property tambahan.** Isinya hanya property kolom, tanpa method, property hitung, `[NotMapped]`, atau logika lain. Semua tambahan masuk ke class turunan.
4. **Model view mewarisi model tabelnya.** `vi_User : ta_User` hanya menambahkan kolom yang ada di view tetapi tidak di tabel (kolom hasil join, `cc`, `cv`).
5. **`DbSet` di DbContext memakai nama class dalam bentuk jamak:** `DbSet<ta_User> ta_Users`, `DbSet<vi_Contact> vi_Contacts`.

Letak class model ORM: `Em.Libs` (namespace `Em.Api.Core.Models`) untuk tabel engine, dan project `*.Models` untuk tabel module (mis. `Em.Test.Models`).

### Class turunan

Class turunan membawa data dari class model ORM ke lapis lain (model UI, service, laporan) dan boleh berisi logika.

1. **Bentuk turunan bebas.** Class turunan boleh mewarisi class model ORM secara langsung, atau membungkusnya. Model UI engine membungkus lewat `UiModel<vi_X, TService>`, seperti `User : UiModel<vi_User, ICredentialServices>`.
2. **Nama class tanpa awalan `ta_`/`vi_`.** `User`, bukan `vi_User`. Nama bebas dipakai bila satu tabel memiliki beberapa class turunan dengan peran berbeda.
3. **Boleh berisi property dan method lain,** mis. validasi, perintah simpan, data dari service lain, atau penanda status UI.
4. **Member milik class turunan ditulis tanpa awalan `c`,** dalam PascalCase biasa: `AvailableClaims`, `GetClaims()`, `IsDirty`. Dengan begitu, mengetik `c` di IntelliSense hanya menampilkan member yang berasal dari kolom database.
5. **Property yang mencerminkan kolom memakai nama kolomnya** (`cUserId`, `cContactFullName`), sehingga pemetaan dua arah dengan class model ORM bisa dibaca baris per baris. Kolom yang tidak boleh diubah dari class ini, seperti kolom hasil join, `cc`, dan `cv`, diberi `private set`.
6. **Konversi lewat member yang jelas.** Model UI engine memakai `static Build(app, vi_X)` untuk membuat model dari baris view, serta `ReadFrom(vi_X)` dan `WriteTo(vi_X)` untuk memetakan kolom ke dua arah.

Letak class turunan UI: `Em.Ui.Core` untuk engine, dan project `*.Models.Ui` untuk module (mis. `Em.Test.Models.Ui`).

### Contoh model

```csharp
// Class model ORM: hanya kolom, nama sama dengan tabel.
[Table("ta_Product")]
public class ta_Product {
   [Key]
   public string cProductId { get; set; } = string.Empty;
   public string? cProductName { get; set; }
   public int cProductStage { get; set; }
   public int cProductQty { get; set; }
   public decimal cProductPrice { get; set; }
   public DateTime ustamp { get; set; }
   public DateTime datestamp { get; set; }
   public string? json_object { get; set; }
}

// Class model ORM untuk view: mewarisi tabel, menambah kolom view saja.
[Table("vi_Product")]
public class vi_Product : ta_Product {
   public decimal ccProductTotal { get; set; }
   public string? cvProductColor { get; set; }
}

// Class turunan untuk UI: tanpa awalan ta_/vi_.
public class Product : UiModel<vi_Product, IProductServices> {
   public static Product Build(IEmApp app, vi_Product data) => new(app, data);
   private Product(IEmApp app, vi_Product data) : base(app, data) { }

   // Cermin kolom: nama kolom dipertahankan.
   public int cProductQty { get; set => SetField(ref field, value); }
   public decimal ccProductTotal { get; private set => SetField(ref field, value); }

   // Milik class turunan: tanpa awalan c.
   public bool IsLowStock => cProductQty < 10;
   public Task SubmitAsync() => /* ... */;

   protected override void ReadFrom(vi_Product source) { /* source.cX -> cX */ }
   protected override void WriteTo(vi_Product target) { /* cX -> target.cX */ }
}
```
