# Plan — Authorization Fase 3: sistem claim (katalog, pemberian, dan pembacaan hak di client)

Status: **selesai dieksekusi** (2026-09-16) - lihat bagian 5 untuk keputusan yang masih terbuka dan
bagian 6 untuk fase berikutnya.
Dibuat: 2026-09-15 · Direvisi: 2026-09-16 (lihat bagian 0)
Baseline: commit `b4bd313` (Kerangka awal sistem klaim), branch `data-services`, **berikut perubahan
kerja yang belum di-commit** — daftarnya di bagian 3.
Cakupan: katalog claim di server, empat action (baca katalog, baca hak seorang user, beri hak,
cabut hak), penyimpanan hak di sesi client, dan `ClaimCollection` sebagai cara module membacanya.
Penegakan hak di server dan UI pengelola **bukan** bagian fase ini.

> **Dokumen ini ditulis untuk dikerjakan tanpa konteks percakapan sebelumnya.** Semua keputusan
> yang sudah diambil ada di bagian 1; jangan dibuka ulang tanpa alasan baru. Nomor baris yang
> disebut adalah kondisi pada baseline di atas — kalau sudah bergeser, cari berdasarkan nama
> method/region, bukan nomornya.

---

## 0. Yang berubah di revisi 2026-09-16

Perubahan datang dari kerja yang sudah dimulai di working tree, dan semuanya memperlebar fase ini
ke sisi client:

1. **`GetMeta_AllClaimActions` masuk kontrak** — katalog claim ikut dikirim ke client. Sebelumnya
   katalog hanya hidup di server dan baru dibagikan pada fase UI pengelola.
2. **`GetMeta_UserClaims` mengembalikan `ClaimAction[]`, bukan `string[]`** — pemecahan kunci
   pindah ke server.
3. **`ClaimCollection` lahir** (`Em.Ui.Core/Ui.Core/shared/ClaimCollection.cs`) sebagai jalan
   module membaca hak: `Services.Claims()["CreateNewItem"]`.
4. **`ServiceUiBase._moduleName` jadi `public string ModuleName { get; private set; }`**, supaya
   `ClaimCollection` bisa menyusun kunci `module:claimname` tanpa menebak.
5. **Administrator menjawab `true` untuk seluruh claim di client** — akun debugger, akun admin
   bawaan, dan user ber-`cUserIsAdmin` — dan indexer-nya hanya menerima nama yang benar-benar ada
   di katalog (1.13, 4.8).
6. **Katalog di client disimpan di `EmApp.AllClaims`**, bukan statis di `User` dan bukan di
   class baru. `IEmApp` sengaja **tidak** dibuka untuk ini (1.14).
7. **Module menjangkaunya lewat extension method `Claims()` pada `IServices`**, karena view model
   memegang service-nya sebagai interface — bukan sebagai `ServiceWpfBase` (1.14).
8. **Hak milik seorang user disimpan di `User.AvailableClaims`**, bukan di `ApiClient`. Hak adalah
   milik orang, bukan milik koneksi (4.9).

Akibat langsungnya: **"pemuatan hak sekali saat login" yang tadinya sengaja ditunda ke fase
berikutnya sekarang masuk ke fase ini** (bagian 2). Tanpa itu `ClaimCollection` tidak punya apa pun
untuk dibaca, dan indexer-nya akan selalu menjawab `false` — bentuk kegagalan yang paling sulit
dilihat, karena layarnya tidak error, cuma mati tombolnya.

---

## 1. Keputusan yang sudah final

**1.1 Katalog tinggal di kode, database hanya menyimpan pemberian.** Tidak ada tabel module dan
tidak ada tabel daftar claim. Module dipasang, claim-nya ada; module dilepas, claim-nya hilang
sendiri. Konsekuensinya tidak pernah ada dua sumber kebenaran yang harus disinkronkan, dan tidak
ada penyemaian yang harus jalan sebelum sistem bisa dipakai.

**1.2 Claim ditautkan ke module lewat service.** `ClaimAction.Create<T>(name)` membaca
`[Module]` dari `T`. `T` adalah **class implementasi** (mis. `SampleServices`), bukan interface-nya —
atribut `[Module]` memang menempel di class. Karena nama module datang dari atribut dan bukan dari
catatan registrasi, urutan pemanggilan `AddClaims` terhadap `AddService` tidak berpengaruh.

**1.3 `ClaimAction` adalah deklarasi murni.** Isinya hanya `ModuleName` dan `Name`. Tidak ada
`Id` (database tidak menyimpannya), tidak ada `Allowed` (itu jawaban, bukan deklarasi), dan tidak
ada `ClaimType` (dibuang saat konsepnya dipisah).

**1.4 Kunci tersimpan berbentuk satu string `module:claimname`.** Contoh:
`SampleServices:CreateNewItem`. Separatornya **titik dua**, dipilih karena tidak sah di dalam
identifier C# sehingga tidak akan pernah muncul tanpa sengaja — berbeda dengan titik, yang sudah
dipakai nama module sendiri (`core.contact`), dan dengan tanda hubung, yang wajar saja muncul di
nama seperti `Read-Only`.

**1.5 Nama adalah kunci permanen.** Sesudah sebuah claim pernah diberikan ke user, mengganti
namanya membuat baris pemberiannya yatim tanpa pesan apa pun. Nama claim tidak pernah didaur ulang
untuk arti yang berbeda.

**1.6 Tidak ada baris berarti tidak punya hak.** `ta_UserClaim` hanya memuat hak yang diberikan.
Tidak ada baris "ditolak".

**1.7 Pemeriksaan di client hanya soal tampilan.** Yang benar-benar menolak tetap server. Fase ini
belum memasang penolakan itu — lihat bagian 6. Ini juga yang membuat 1.13 aman: bypass admin di
client tidak memberi hak apa pun, ia hanya membuat tombolnya terlihat.

**1.8 Perbandingan kunci selalu case-insensitive.** Struktur tabelnya sudah berlaku di database dan
sudah tercermin di `doc/MainTable.sql`:

```sql
CREATE TABLE [dbo].[ta_UserClaim] (
  [cUserClaimId]   char(26)     NOT NULL,   -- PK, ULID
  [cUserId]        char(26)     NOT NULL,   -- FK ke ta_User, ON DELETE/UPDATE CASCADE
  [cUserClaimName] varchar(255) NOT NULL    -- "module:claimname"
)
```

Collation kolomnya `SQL_Latin1_General_CP1_CI_AS` — case-insensitive. Semua perbandingan di kode
harus mengikuti itu, bukan `Ordinal`. Berlaku sampai ke indexer `ClaimCollection`: kalau client
membandingkan `Ordinal` sementara database `CI`, ada pemberian yang diterima server tapi dibaca
"tidak punya" oleh layar yang memberikannya.

**1.9 Katalog ikut dikirim ke client lewat `GetMeta_AllClaimActions`.** Client memerlukannya untuk
dua hal yang tidak bisa dijawab daftar pemberian saja: membedakan "tidak punya hak" dari "nama
claim-nya salah ketik" (4.8), dan menyediakan daftar centang untuk UI pengelola nanti. Sumbernya
tetap satu — `EmApp.AllClaims` di server (4.3).

**1.10 `GetMeta_UserClaims` mengembalikan `ClaimAction[]`.** Pemecahan kunci jadi module + nama
terjadi sekali di server, lewat `ClaimAction.FromKey` (4.2). Client tidak pernah menyusun maupun
memecah kunci sendiri, jadi tidak ada kesempatan kedua sisi menafsirkannya berbeda.

**1.11 Client menyimpan hak sekali, tidak bertanya ulang setiap dipakai.** `XxxCommandAllowed`
sinkron dan dipanggil sangat sering oleh WPF; satu saja pemanggilan jaringan di jalur itu membuat
UI tersendat atau jalan buntu deadlock. Hak dimuat sekali ke `User.AvailableClaims` saat pengguna
aktif ditetapkan, dan ikut hilang bersama pengguna itu saat sesi berakhir (4.9).

**1.12 `ClaimCollection` adalah jendela per module, bukan wadah.** Ia tidak punya daftar sendiri
untuk diisi: nama module, katalog, dan sumber hak seluruhnya diterima dari luar saat ia dibentuk,
dan ia dibentuk ulang setiap kali ditanya (4.8). Alasannya: service UI module terdaftar `Scoped`,
sehingga wadah yang menempel per-instance akan lahir kosong di scope baru dan membuat seluruh claim
module itu terbaca `false` — tanpa error, tanpa petunjuk.

**1.13 Di client, administrator menjawab `true` untuk setiap claim yang ada di katalog.** Yang
dimaksud administrator: akun debugger, akun admin bawaan, dan user biasa yang `cUserIsAdmin`-nya
`true`. Ketiganya sudah dibedakan oleh satu tanda yang sama — `cUserIsAdmin`, yang memang bernilai
`true` pada `CreateDebuggerUser` maupun `CreateAdminUser` — jadi tidak perlu mendaftar id akun
sistem satu per satu.

Dua batasnya, dan keduanya penting:

- **Bypass ini tetap tunduk pada katalog.** Nama yang tidak dideklarasikan module mana pun tetap
  ditolak sebagai salah ketik, bukan dijawab `true`. Tanpa urutan ini, salah ketik justru paling
  tidak terlihat oleh orang yang paling mungkin membuatnya: developer yang menjalankan mode debug
  selalu masuk sebagai administrator, dan akan melihat setiap nama — termasuk yang salah eja —
  menjawab `true`.
- **Ini keputusan client, bukan keputusan server.** Server belum memutuskan apakah admin melewati
  pemeriksaan (bagian 5). Dan karena syaratnya `cUserIsAdmin` milik pengguna aktif — bukan
  `IsDebugMode` — penyamaran lewat token debug ke user non-admin tetap membaca hak asli, persis
  yang dijaga `plan/executed/debug-token-dev-bypass.md`.

**1.14 Katalog tinggal di `EmApp.AllClaims`, dan module menjangkaunya lewat extension method.**
Dua keputusan yang saling mengunci, jadi ditulis bersama.

*Tempatnya `EmApp`* — bukan statis di `User` (katalog bukan milik entitas mana pun), bukan class
baru, dan **`IEmApp` sengaja tidak dibuka untuk ini**: kontrak itu dijaga tipis, dan menambahkan
katalog claim ke sana membuat setiap module memegang satu hal lagi yang bukan infrastruktur.

*Jalan masuknya extension method* — karena view model menerima service-nya lewat **interface**
(`IEmSampleServices`), bukan lewat `ServiceWpfBase`. Properti apa pun di kelas dasar tidak
terlihat dari sana, jadi satu-satunya jalan yang tidak memaksa module melakukan cast sendiri adalah
extension pada `IServices`:

```csharp
Services.Claims()["CreateNewItem"]
```

Extension-nya tinggal di **`Em.Ui.Wpf.Core`**, bukan di `Em.Ui.Core`. Justru di situ nilainya:
ia satu-satunya potongan yang perlu tahu `EmApp`, sehingga `Em.Ui.Core` tidak perlu diberi seam,
properti virtual, maupun statis apa pun untuk menjangkau katalog. Bentuknya di 4.8.

Yang ditolak dan alasannya, supaya tidak dibuka ulang:

- **Member claim pada `IServices`.** Interface itu dipakai backend juga, jadi service server akan
  ikut dipaksa mengimplementasikan hal yang tidak berlaku di sana.
- **`NavigationBody.GetClaims()` yang mencari sendiri service dan penggunanya.** Dua sebab:
  `INavigationBody` diimplementasikan code-behind `UserControl`, sementara pemeriksaan claim hidup
  di view model — VM harus menjangkau balik ke control-nya, berlawanan dengan seluruh aturan
  binding repo ini. Dan "otomatis cari service-nya" tidak punya jawaban benar begitu satu layar
  memakai lebih dari satu service, yang di ERP adalah keadaan biasa, bukan kekecualian. Lewat
  extension, yang menentukan module adalah service yang disebut di call site — tidak bisa tertukar.

---

## 2. Batas fase ini

**Yang dikerjakan:**

- `ClaimAction` dirapikan: kunci gabungan, `FromKey`, validasi, pesan kesalahan.
- `EmAppBuilder.AddClaims` diimplementasikan, katalognya dibekukan di `EmApp`.
- Model `ta_UserClaim` dan `DbSet`-nya, ditulis tangan (alasannya di 4.4).
- Empat action di region `Meta's`: baca katalog, baca hak seorang user, beri satu hak, cabut satu
  hak — berikut implementasi client-nya di `CredentialService`.
- `User.GetClaims()` di client diimplementasikan.
- Penyimpanan hak di sesi client, `ClaimCollection`, dan pemuatannya saat sesi terbentuk —
  termasuk jawaban untuk mode debug.

Keempat action itu satu paket dengan sengaja: tanpa jalur tulis, `GetMeta_UserClaims` selamanya
mengembalikan array kosong karena tidak ada apa pun yang bisa memasukkan baris — dan fase ini jadi
tidak bisa dibuktikan berjalan. Dengan keempatnya, alurnya bisa diuji utuh: beri hak, baca, lihat
tombolnya hidup, cabut, baca lagi, lihat tombolnya mati.

**Yang sengaja tidak dikerjakan:**

- **Penegakan hak di server.** Tidak ada action yang menolak karena claim di fase ini. Menambahkan
  penegakan sebelum ada cara memberi hak berarti mengunci semua orang dari fiturnya sendiri —
  urutan yang sama seperti fase 1 dan 2 (gerbang menyusul sesudah client bisa membawa buktinya).
  Sampai gerbang itu terpasang, `ClaimCollection` **hanya menyembunyikan tombol**; siapa pun yang
  memanggil action-nya langsung tetap dilayani.
- **UI pengelola claim.** Action tulisnya ada, layarnya belum. Pemberian hak untuk sementara
  dilakukan lewat pemanggilan action langsung (token debug, atau klien HTTP biasa).
- **Konstanta kunci per module.** Pemanggilan tetap berupa literal string; pengamannya untuk
  sementara adalah pemeriksaan katalog di 4.8. Lihat bagian 5.

---

## 3. Kondisi awal yang perlu diketahui

### 3.1 Yang sudah ada di baseline

| Berkas | Kondisi |
| --- | --- |
| `src/shared/Em.Libs/Shared/ClaimAction.cs` | `Create<T>(string name)` membaca `[Module]`, melempar `InvalidOperationException` **tanpa pesan** kalau atributnya tidak ada. Belum punya `Key`/`FromKey`/validasi |
| `src/shared/Em.Libs/Shared/ClaimType.cs` | Sudah tidak dirujuk siapa pun — sisa dari bentuk lama |
| `src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.cs:71` | `AddClaims(ClaimAction claim)` stub, isinya `throw new NotImplementedException()` |
| `src/backend/modules/8XX-NSM/Em.Sample/Extensions.cs:12-13` | Dua pemanggilan `AddClaims` sudah terpasang. **Karena stub-nya melempar, server tidak bisa start sekarang** |
| `doc/sqlscript/mssql/views/` | Tidak ada `vi_UserClaim.sql`, dan memang **tidak akan dibuat** — lihat 4.4 |
| `doc/MainTable.sql` | Sudah mencerminkan bentuk `ta_UserClaim`; tidak perlu `schemaupdate` lagi |
| `ServicesBase` | `Claims` dan `GetClaimsAsync` **sudah dihapus**; tidak perlu dibersihkan lagi |
| UI DI | `ICredentialServices` didaftarkan **Singleton** (`EmApp.Statics.cs:83`), service module lewat `AddServices<T1,T2>` didaftarkan **Scoped** (`EmAppBuilder.cs:86`) |
| `CreateDebuggerUser` / `CreateAdminUser` | Keduanya membuat `User` dengan `cUserIsAdmin = true` (`EmApp.Statics.cs:154`, `:188`) — dasar dari 1.13 |

### 3.2 Yang sudah dikerjakan di working tree (belum di-commit)

| Berkas | Isi |
| --- | --- |
| `Em.Libs/Shared/UserClaimPayload.cs` | **Baru.** `cUserId` + `cUserClaimName`, keduanya `required init`, namespace `Em.Shared` (bukan `Api.Core.Models` seperti rencana lama) |
| `Em.Libs/Api.Core.Models/ICredentialServices.cs` | **Empat kontrak baru** di region `Meta's`: `GetMeta_AllClaimActions`, `GetMeta_UserClaims` (→ `ClaimAction[]`), `PostMeta_AddUserClaim`, `PostMeta_RemoveUserClaim` |
| `Em.Ui.Core/Api.Core.Models/User.cs` | `public static ClaimAction[] AllClaims { get; internal set; } = []` di region `Statics`. `GetClaims()` masih stub `NotImplementedException` (baris 353) |
| `Em.Ui.Core/Ui.Core/shared/ClaimCollection.cs` | **Baru.** Masih menyimpan `List<ClaimAction>` sendiri, indexer `Claims.Any(r => r.Name == key)` |
| `Em.Ui.Core/Ui.Core/shared/ServiceUiBase.cs` | `_moduleName` → `public string ModuleName { get; private set; }`; properti `Claims` lazy (`field ??= new ClaimCollection { ModuleName = ModuleName }`) |
| `Em.Ui.Wpf/Program.cs` | Blok `AddDebug` dibuka kembali (tidak lagi dikomentari) — mode debug aktif di build DEBUG |

### 3.3 Temuan pada kerja di 3.2 — diperbaiki di bagian 4

1. **`ModuleName` belum tentu terisi saat dibaca.** `EnsureModuleName()` hanya dipanggil dari
   `GetAsync`/`PostAsync`, sedangkan claim ditanyakan sebelum ada request apa pun — VM menilai
   `XxxCommandAllowed` begitu layarnya dibangun. Kuncinya jadi `":CreateNewItem"`, tidak pernah
   cocok, dan tidak ada pesan apa pun yang menjelaskannya. Perbaikannya di 4.7.
2. **`ClaimCollection` menyimpan daftarnya sendiri.** Lihat 1.12 dan 4.8.
3. **`ClaimCollection` menyediakan `Add` publik sekaligus `IsReadOnly => true`.** Dua-duanya hilang
   begitu wadahnya dilepas.
4. **Indexer membandingkan `==` (case-sensitive)**, melanggar 1.8.
5. **`User.AllClaims` adalah katalog seluruh sistem, bukan milik `User` mana pun.** Statis pada
   `User` membuat namanya terbaca sebagai "semua claim milik user ini". Pindah ke
   `EmApp.AllClaims` (1.14) — lihat 4.7.
6. **`ServiceUiBase.Claims` tidak terlihat dari view model.** VM menerima service-nya sebagai
   `IEmSampleServices`; properti publik di kelas dasar tidak ikut terbawa interface. Propertinya
   **dihapus**, diganti extension method (1.14, 4.8).
7. **`CredentialService` (client WPF) belum mengimplementasikan empat kontrak baru.** Frontend tidak
   akan compile sampai 4.6 dikerjakan.

### 3.4 Catatan lain

`AddService<T1,T2>` menurunkan nama module dengan
`string.IsNullOrWhiteSpace(attr.Name) ? typeof(T2).Name : attr.Name`, `ClaimAction.Create` memakai
`attr.Name ?? typeof(T).Name`, dan `ServiceUiBase.EnsureModuleName` punya salinan ketiganya
sendiri. Ketiganya berbeda untuk `[Module("")]`, dan langkah 4.1 menyatukannya. Ini bukan
kerapian belaka: kalau client menurunkan nama module berbeda dari server, kunci yang disusunnya
tidak akan pernah cocok dengan yang tersimpan.

---

## 4. Langkah pengerjaan

Urutannya mengikat: 4.1 → 4.2 → 4.3 → 4.4 → 4.5 → 4.6 → 4.7 → 4.8 → 4.9. Sesudah 4.3 server sudah
bisa start lagi (sekarang tidak bisa — lihat 3.1); sesudah 4.6 frontend sudah bisa compile lagi.

### 4.1 Satukan aturan penurunan nama module — `Em.Libs`

Tambahkan method statis di `ModuleAttribute`, karena di situlah aturannya lahir:

```csharp
public static string ResolveName(Type serviceType) {
   var attribute = serviceType.GetCustomAttribute<ModuleAttribute>() ??
      throw new InvalidOperationException(
         $"Type '{serviceType.FullName}' must be decorated with [Module] attribute to take part in claims or action routing.");
   return string.IsNullOrWhiteSpace(attribute.Name) ? serviceType.Name : attribute.Name;
}
```

Lalu pakai di **tiga** tempat, menggantikan yang sekarang:

- `EmAppBuilder.AddService<T1,T2>` di backend (pesan kesalahannya boleh tetap menyebut `AddService`).
- `ClaimAction.Create<T>`.
- `ServiceUiBase.EnsureModuleName` di client — dengan satu perbedaan: di sini atribut yang hilang
  **tidak boleh melempar**, karena service UI tanpa `[Module]` sudah berjalan begitu selama ini dan
  jatuh ke nama class. Pakai bentuk yang tidak melempar (`TryResolveName`, atau `ResolveName` dengan
  parameter `bool required = true`), bukan menyalin ulang aturannya.

Sesudah ini tidak mungkin lagi claim mendarat di nama module yang berbeda dari action-nya, di sisi
mana pun.

### 4.2 Rapikan `ClaimAction` — `Em.Libs`

Yang ditambahkan:

- `public const char Separator = ':';`
- `public string Key => $"{ModuleName}{Separator}{Name}";` — satu-satunya tempat penggabungan
  terjadi, supaya server dan client tidak mungkin menyusunnya berbeda.
- `public static ClaimAction FromKey(string key)` — pasangan dari `Key`. Pecah pada **kemunculan
  pertama** `Separator`. Karena validasi di bawah melarang separator di kedua bagian, kemunculan
  pertama pasti satu-satunya — dan itulah yang membuat pemecahan ini pasti benar. Kunci yang tidak
  memuat separator sama sekali berarti baris lama atau baris rusak; kembalikan sebagai module kosong
  dengan nama apa adanya, jangan melempar, supaya satu baris aneh tidak mematikan seluruh layar.
  Dipakai **server** (4.5), bukan client — lihat 1.10.
- Validasi di `Create<T>`, dilempar sebagai `ArgumentException` dengan pesan yang menyebut nilainya:
  - `name` tidak boleh kosong/whitespace.
  - `name` maupun nama module tidak boleh mengandung `Separator`. Ini yang membuat kunci terbukti
    tidak ambigu, bukan sekadar kebetulan aman.
  - `Key` tidak boleh melebihi 255 karakter (lebar kolomnya). Gagal di sini jauh lebih terbaca
    daripada gagal saat insert.
- Ubah jadi `record` (atau tambahkan `Equals`/`GetHashCode`) supaya kesetaraan strukturalnya bisa
  dipakai mendeteksi deklarasi ganda tanpa menulis pembanding sendiri.
- Perbandingan kunci memakai `StringComparison.OrdinalIgnoreCase`, mengikuti collation kolomnya
  (1.8) dan mengikuti cara `ProcessRequest` mencocokkan nama module untuk action.

**Yang baru di revisi ini: `ClaimAction` sekarang melintasi kabel** (1.9, 1.10), jadi ia harus
tetap bisa dibaca ulang oleh serializer yang dipakai `ApiClient`:

- `ModuleName`/`Name` yang `required init` aman — keduanya wajib ada di JSON, dan itu memang yang
  diinginkan.
- Beri `[JsonIgnore]` pada `Key`: ia turunan dari dua properti lain, jadi mengirimnya berarti
  mengirim data yang sama dua kali dan membuka celah kunci yang tidak konsisten dengan isinya.
- Kalau jadi `record`, pakai `record` dengan property `init` — bukan positional record — supaya
  serializer tetap punya jalan membentuknya.

Hapus `src/shared/Em.Libs/Shared/ClaimType.cs` — sudah tidak dirujuk apa pun. Kalau ternyata
masih ada yang memakainya saat compile, berarti ada pemakai yang terlewat; periksa dulu, jangan
dihidupkan kembali tanpa alasan.

### 4.3 Implementasikan `AddClaims` — `Em.Api.Core`

Di `EmAppBuilder`:

```csharp
internal List<ClaimAction> ClaimActions { get; } = [];

public void AddClaims(ClaimAction claim) { ... }
public void AddClaims(params ClaimAction[] claims) { ... }   // overload, bukan rename
```

Isi yang dikerjakan:

1. Tolak kunci ganda — dua claim dengan `Key` yang sama (case-insensitive) dilempar
   `InvalidOperationException` yang menyebut kunci dan module-nya. Sejajar dengan pemeriksaan
   duplikat action yang sudah ada di `RegisterActions`.
2. Catat ke log lewat `Logger` bootstrap yang sudah dipakai `AddService`, satu baris per claim.

Lalu di `EmApp.BuildApp`, sesudah `appBuilder(builder)` dan sejajar dengan `_actions`:

```csharp
app._claims = builder.ClaimActions.ToArray();
```

Bekukan seperti `_actions` dan `_debugTokenKeys`: katalog claim hanya boleh datang dari `Program.cs`
dan tidak pernah berubah selama server hidup. Sediakan pembacanya sebagai
`public IReadOnlyList<ClaimAction> AllClaims => _claims;` — `GetMeta_AllClaimActions` (4.5) dan UI
pengelola di fase berikutnya membacanya dari sini.

Namanya sengaja **sama persis** dengan milik `EmApp` di client (1.14, 4.9): dua tipe berbeda di
dua assembly berbeda, satu arti, satu nama. Memberi nama berlainan untuk hal yang sama hanya
membuat orang bertanya apa bedanya — dan jawabannya tidak ada.

**Pemeriksaan silang, dijalankan sekali di akhir `BuildApp`:** setiap `ClaimAction.ModuleName`
harus cocok dengan salah satu `ActionDefinition.Module` yang terdaftar. Kalau tidak, lempar dengan
pesan yang menyebut module-nya — itu berarti claim didaftarkan untuk service yang `AddService`-nya
tidak pernah dipanggil (misalnya barisnya sedang dikomentari), dan hak yang diberikan lewat claim
itu tidak akan pernah terpakai.

Sesudah langkah ini `dotnet run` sudah jalan lagi, dan startup log menyebut dua claim milik
`SampleServices`.

### 4.4 Model dan `DbSet` — ditulis tangan, **tanpa** `scaffold-sdktables`

Skill itu sengaja tidak dipakai di sini. Ia meng-generate tujuh lapisan untuk tabel yang
diperlakukan sebagai entitas penuh: view `vi_`, CRUD lengkap berikut paging dan batch, service
client, dan UI model ber-dirty-tracking dengan `RollBack`/`SaveAsync`.

`ta_UserClaim` bukan entitas semacam itu. Ia tabel pemberian tiga kolom yang hanya pernah
di-*insert* dan di-*delete* — tidak pernah disunting di tempat, tidak pernah dibaca sebagai record
berdiri sendiri, dan tidak pernah dipaging. `vi_UserClaim` akan jadi `SELECT` tiga kolom tanpa
join, dan UI model-nya akan memikul pelacakan perubahan untuk baris yang tidak punya perubahan
untuk dilacak. Seluruh lapisan itu akan lahir untuk kemudian tidak dipanggil siapa pun.

Yang dibutuhkan hanya dua hal.

**Model** — `src/shared/Em.Libs/Api.Core.Models/ta_UserClaim.cs`:

```csharp
[Table("ta_UserClaim")]
public class ta_UserClaim {
   [Key] public string cUserClaimId { get; set; } = string.Empty;
   public string cUserId { get; set; } = string.Empty;
   public string cUserClaimName { get; set; } = string.Empty;
}
```

Tabel ini tidak punya `ustamp`/`datestamp`/`json_object`, berbeda dari tabel lain di repo. Ikuti
struktur database apa adanya, jangan ditambahi.

**DbSet** — di `ApiCoreContext`:

```csharp
public DbSet<ta_UserClaim> ta_UserClaims => Set<ta_UserClaim>();
```

Sesudah dua baris itu, seluruh action di 4.5 sudah bisa mengaksesnya lewat `ctx`.

### 4.5 Empat action di region `Meta's` — server

Seluruhnya di `CredentialServices`, di dalam region `Meta's` — bukan region tabel. Ini bukan
operasi satu tabel yang dipakai module, melainkan pembacaan katalog dan pengubahan hak.
Kontraknya **sudah ada** di `ICredentialServices` (3.2); yang kurang implementasinya.

```csharp
Task<ClaimAction[]> GetMeta_AllClaimActions();
Task<ClaimAction[]> GetMeta_UserClaims(string cUserId);
Task PostMeta_AddUserClaim(UserClaimPayload claim);
Task PostMeta_RemoveUserClaim(UserClaimPayload claim);
```

`UserClaimPayload` sengaja class tersendiri, bukan dua parameter `string` dan bukan `ta_UserClaim`:

- Dua `string` bersebelahan gampang tertukar di call site — `(name, userId)` tetap kompilasi dan
  tetap salah. Untuk pemanggilan yang mengubah hak orang, itu risiko yang tidak perlu ditanggung.
- `ta_UserClaim` membawa `cUserClaimId`, dan client tidak boleh menentukan primary key; ULID-nya
  dibuat server.

#### `GetMeta_AllClaimActions` — `[GetAction]`

- Kembalikan `EmApp.AllClaims` apa adanya. Tidak menyentuh database sama sekali — katalognya hidup
  di memori sejak `BuildApp` (4.3).
- `Request.RequireUserId()` di baris pertama: cukup "harus ada yang masuk", **bukan**
  `RequireAdmin`. Setiap client yang sudah masuk memerlukan katalog ini untuk menggambar layarnya
  sendiri (4.8), jadi mengunci ke admin akan mematikan seluruh pemeriksaan claim untuk user biasa.
  Yang dibocorkan pun hanya daftar nama fitur yang ada di aplikasi — bukan siapa memiliki apa.

#### `GetMeta_UserClaims` — `[GetAction]`

- `Request.RequireSelfOrAdmin(cUserId)` di baris pertama. Tanpa ini siapa pun yang login bisa
  membaca hak orang lain.
- Baca `cUserClaimName` milik user itu, ubah tiap baris dengan `ClaimAction.FromKey` (4.2), dan
  kembalikan hasilnya. **Jangan** disaring terhadap katalog: pemberian yatim harus tetap terlihat,
  karena hanya lewat situ ia bisa dikenali dan dicabut (`PostMeta_RemoveUserClaim`). Penyaringan
  terhadap katalog memang terjadi, tapi di client dan untuk keperluan lain (4.8).
- Baca no-tracking (sudah default di `EmDbContext`).

Akun sistem (`Defaults.AdminUserId`, `Defaults.DebuggerUserId`) tidak punya baris di `ta_User`
sehingga juga tidak punya baris di sini. Kembalikan array kosong, jangan melempar — array kosong
adalah jawaban yang jujur untuk "baris apa yang ada di tabel ini". Yang menangani arti "administrator
boleh apa saja" adalah client di 4.8, bukan action ini.

#### `PostMeta_AddUserClaim` — `[PostAction]`

Empat hal, dan tiga di antaranya wajib:

1. **`Request.RequireAdmin()`, bukan `RequireSelfOrAdmin`.** Ini beda penting dari action baca:
   seorang user boleh melihat haknya sendiri, tapi tidak boleh memberi hak kepada dirinya sendiri.
   Kalau yang dipakai `RequireSelfOrAdmin`, siapa pun bisa mengangkat dirinya sendiri dan seluruh
   sistem claim kehilangan artinya.
2. **Kunci harus ada di katalog.** Cocokkan `cUserClaimName` dengan `EmApp.AllClaims` (4.3) memakai
   `OrdinalIgnoreCase`. Kunci yang tidak dideklarasikan module mana pun ditolak dengan
   `ActionException` 400 — memberi hak yang tidak ada hanya menghasilkan baris yang selamanya tidak
   akan pernah cocok saat diperiksa, dan salah ketiknya baru ketahuan berbulan-bulan kemudian.
   Inilah yang menutup lingkaran antara katalog di 4.3 dan data di sini.
3. **Idempoten.** Kalau barisnya sudah ada, jangan menambah baris kedua dan jangan melempar —
   selesai begitu saja. UI pengelola berupa daftar centang akan mengirim ulang keadaan yang sama
   berkali-kali, dan unique index yang mencegah duplikat belum ada di database (bagian 5).
4. `cUserClaimId` diisi `Ulid.NewUlid()` di server.

#### `PostMeta_RemoveUserClaim` — `[PostAction]`

- `Request.RequireAdmin()`, alasan yang sama.
- Hapus berdasarkan pasangan `cUserId` + `cUserClaimName`, bukan berdasarkan `cUserClaimId` —
  client tidak perlu tahu id barisnya.
- Idempoten juga: baris yang tidak ada berarti haknya memang sudah tidak ada. Itu keadaan akhir
  yang diminta, jadi bukan kesalahan.
- Kunci **tidak** perlu dicocokkan dengan katalog. Justru sebaliknya: pemberian yatim — claim yang
  sudah dihapus dari kode tapi barisnya tertinggal — harus tetap bisa dicabut.

### 4.6 Implementasi client-nya — `Em.Ui.Wpf.Core/Api.Core/CredentialService.cs`

Empat baris, mengikuti pola yang sudah ada di berkas itu; tanpa ini frontend tidak compile (3.3.7):

```csharp
public Task<ClaimAction[]> GetMeta_AllClaimActions() =>
   GetAsync<ClaimAction[]>(nameof(GetMeta_AllClaimActions));

public Task<ClaimAction[]> GetMeta_UserClaims(string cUserId) =>
   GetAsync<ClaimAction[]>(nameof(GetMeta_UserClaims), cUserId);

public Task PostMeta_AddUserClaim(UserClaimPayload claim) =>
   PostAsync(nameof(PostMeta_AddUserClaim), claim);

public Task PostMeta_RemoveUserClaim(UserClaimPayload claim) =>
   PostAsync(nameof(PostMeta_RemoveUserClaim), claim);
```

Catatan: kedua action POST mengirim satu argumen berupa objek, dan jalur `PostAsync` untuk argumen
non-primitif belum pernah dijalankan sungguhan (lihat catatan `ParameterType "System.Object"`).
Kalau di pengujian 4.9 ternyata bermasalah, itu temuan jalur POST — bukan temuan sistem claim;
catat terpisah, jangan dibetulkan dengan mengubah bentuk payload-nya.

### 4.7 Katalog di sisi client — `EmApp.AllClaims` dan `User.GetClaims()`

**`GetClaims()`** (`src/shared/Em.Ui.Core/Api.Core.Models/User.cs:353`) sekarang tinggal
meneruskan, karena server sudah mengirim `ClaimAction`:

```csharp
public Task<ClaimAction[]> GetClaims() {
   EnsureNotSystemAccount(cUserId);
   return Service.GetMeta_UserClaims(cUserId);
}
```

`EnsureNotSystemAccount` dipakai konsisten dengan jalur lain di kelas ini: id akun sistem memang
tidak sah untuk pencarian baris. Konsekuensinya **`GetClaims()` tidak boleh dipanggil untuk akun
admin maupun akun debugger** — 4.9 yang menanganinya, dan itu disengaja: percabangannya berada di
satu tempat yang bisa dibaca, bukan tersebar sebagai `try/catch`.

**`User.AllClaims` dihapus** (3.3.5). Katalognya pindah ke `EmApp` (1.14):

```csharp
// Em.Ui.Wpf.Core — EmApp
public IReadOnlyList<ClaimAction> AllClaims { get; internal set; } = [];
```

`internal set` dipertahankan seperti bentuk statisnya: yang mengisinya hanya `RefreshClaimsAsync`
(4.9), dan module tidak boleh menimpanya. Namanya sama dengan milik `EmApp` di server (4.3) —
satu arti, satu nama, meski tipenya dua.

Sesudah pemindahan ini tidak ada lagi state claim yang statis di client. Itu bukan sekadar
kerapian: katalog adalah milik **satu server**, dan statis berarti satu per proses. Sebagai
properti `EmApp` ia ikut berpindah ketika koneksi aktif berpindah, dan tidak ada sisa katalog
server lama yang sempat terbaca sebagai milik server baru.

**Perbaiki juga `ServiceUiBase`** (3.3.1): turunkan `ModuleName` sekali di constructor dan buang
`EnsureModuleName` sama sekali. Tipe konkretnya sudah pasti sejak instance lahir (`GetType()` di
constructor sudah mengembalikan turunannya), jadi tidak ada alasan menundanya — dan penundaan itulah
yang membuat `Claims` bisa terbentuk dengan `ModuleName` kosong.

### 4.8 `ClaimCollection` — jendela per module

Bentuk sekarang (3.2) menyimpan `List<ClaimAction>` sendiri dan diisi lewat `Add` publik. Itu tidak
bisa dipertahankan, karena service UI module terdaftar **`Scoped`** (3.1): setiap scope baru
melahirkan instance service baru, dengan `ClaimCollection` baru yang **kosong** — dan collection
kosong menjawab `false` untuk semua pertanyaan. Layarnya tidak error; tombolnya cuma mati, dan
tidak ada apa pun yang memberi tahu kenapa. Itu bentuk kegagalan yang paling mahal untuk dilacak.

**Properti `Claims` di `ServiceUiBase` (3.2) dihapus, tidak diganti nama.** Begitu jalan masuknya
extension method, properti itu jadi pintu kedua untuk hal yang sama — dan dua pintu berarti cepat
atau lambat ada yang berbeda isinya. Yang tinggal di `ServiceUiBase` cuma `ModuleName` dan
`ApiClient`, keduanya sudah ada.

Karena itu `ClaimCollection` tidak lagi punya pemilik; ia dibentuk saat ditanya, dari tiga bahan
yang seluruhnya datang dari luar:

```csharp
public class ClaimCollection : IEnumerable<ClaimAction>
{
   public ClaimCollection(string moduleName, IReadOnlyList<ClaimAction> catalog, User? user) { ... }

   public string ModuleName { get; }

   /// Katalog milik module ini - dipakai layar pengelola untuk menggambar daftar centangnya.
   public IEnumerator<ClaimAction> GetEnumerator() =>
      _catalog
         .Where(r => string.Equals(r.ModuleName, ModuleName, StringComparison.OrdinalIgnoreCase))
         .GetEnumerator();

   IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

   /// true kalau pengguna yang sedang aktif boleh menjalankan claim bernama
   /// <paramref name="name"/> pada module ini.
   public bool this[string name] { get { ... } }
}
```

`user` boleh `null` — belum ada yang masuk berarti belum punya hak apa pun, dan itu dijawab `false`,
bukan exception. Hak (`AvailableClaims`) dan tanda administrator (`cUserIsAdmin`) dibaca dari objek
`User` itu (4.9), jadi keduanya selalu menyebut orang yang sama.

**Jalan masuk untuk module: extension method** (1.14), di **`Em.Ui.Wpf.Core`**:

```csharp
public static ClaimCollection Claims(this IServices services) =>
   services is ServiceWpfBase svc
      ? new ClaimCollection(svc.ModuleName, svc.App.AllClaims, svc.App.ActiveUser)
      : throw new InvalidOperationException(
           $"Service '{services.GetType().FullName}' is not a WPF client service, so its claims cannot be resolved.");
```

Karena extension-nya berada di layer yang memang tahu `EmApp`, katalognya dibaca langsung dari
`svc.App.AllClaims` — **tidak ada seam, properti virtual, maupun statis apa pun di `Em.Ui.Core`**.
`ClaimCollection` tetap tinggal di `Em.Ui.Core` karena isinya tidak menyentuh WPF sama sekali;
yang tahu WPF hanya lima baris extension ini.

Tiga catatan tentang bentuknya:

- **Objeknya sekali pakai.** Ia dibuat ulang setiap pemanggilan, jadi jangan disimpan di field:
  `AllClaims` diganti seluruhnya oleh `RefreshClaimsAsync` (4.9), dan instance yang ditahan akan
  memegang katalog lama. Ongkosnya satu objek kecil berisi tiga rujukan — murah, dan menukar
  kemungkinan jawaban basi dengan alokasi sebesar itu adalah pertukaran yang benar.
- **Downcast-nya tidak bisa dihindari.** `IServices` kosong, dan menambahkan member claim ke sana
  akan memaksa service *backend* ikut mengimplementasikannya (1.14).
- **Melempar, bukan mengembalikan collection kosong.** Service yang bukan turunan `ServiceWpfBase`
  adalah kesalahan pemasangan; dijawab "tidak punya hak" ia akan menyamar jadi masalah perizinan
  dan dicari di tempat yang salah. Ini alasan yang sama dengan langkah 3 indexer di bawah.

Isi indexer-nya, **berurutan — dan urutannya yang menentukan**:

1. **Susun kuncinya lewat `ClaimAction`** (4.2). Jangan merangkai string di sini; hanya ada satu
   tempat yang boleh tahu bentuk kuncinya.
2. **Katalog belum termuat** (sesi baru dibuka, pemuatan 4.9 belum selesai) → `false`, tanpa
   melempar. Jawaban konservatif: tombol yang belum jelas haknya, dimatikan dulu.
3. **Nama harus ada di katalog module ini.** Kalau tidak, itu salah ketik programmer, bukan "tidak
   punya hak". Bedanya penting: yang satu diperbaiki dengan mengubah kode, yang satu lagi dengan
   memberi hak lewat UI pengelola — kalau keduanya dijawab `false` yang sama, yang pertama akan
   terus dicari di tempat yang salah. Perlakuan yang diusulkan: lempar `InvalidOperationException`
   di build `DEBUG`, dan di luar `DEBUG` kembalikan `false` sambil mencatat sekali ke log. Alasan
   tidak melempar di rilis: indexer ini dipanggil dari `XxxCommandAllowed`, jalur yang dieksekusi
   WPF terus-menerus — exception di sana akan menjatuhkan aplikasi di depan user karena satu tombol.
4. **Tidak ada pengguna aktif** (`user is null`) → `false`. Ini yang menggantikan pembersihan hak
   saat sign out: `SetActiveUser(null)` sudah cukup (4.9).
5. **Administrator → `true`** (1.13). Ditaruh **sesudah** langkah 3, bukan sebelumnya, dan itu
   bukan detail: developer di mode debug selalu masuk sebagai administrator, jadi bypass yang
   mendahului pemeriksaan katalog membuat setiap salah ketik menjawab `true` justru pada orang yang
   paling mungkin membuatnya — dan nama yang salah itu baru ketahuan saat dipakai user sungguhan.
   Tandanya satu: `user.cUserIsAdmin` (3.1), yang sudah `true` untuk akun debugger maupun akun admin
   bawaan — jadi ketiga jenis administrator lewat cabang yang sama, dan `AvailableClaims` mereka
   memang tidak pernah diisi (4.9).
6. **Selebihnya**: cari kuncinya di `user.AvailableClaims`, dibandingkan `OrdinalIgnoreCase` (1.8).
   Kalau nanti daftar claim seorang user tumbuh besar, `AvailableClaims` boleh menyimpan
   `HashSet<string>` kuncinya di samping array-nya — indexer ini dipanggil WPF terus-menerus, dan
   pencarian linear di sana yang paling dulu terasa.

Constructor-nya jadi `public` (pemanggilnya ada di assembly lain), jadi jaminan "`ModuleName`-nya
pasti sesuai service" tidak lagi datang dari `internal` melainkan dari satu-satunya pemanggil yang
ada. Kalau nanti muncul pemanggil kedua, pastikan ia juga mengambil `ModuleName` dari service, bukan
mengetiknya sendiri.

**Pemakaian dari module**, dengan service yang diterima VM sebagai interface:

```csharp
public bool CreateNewCommandAllowed() => Services.Claims()["CreateNewItem"];
```

Dua hal yang perlu diketahui module author:

- Pemanggilnya **sinkron dan tanpa jaringan** — aman dipanggil dari `XxxCommandAllowed`.
- Jawabannya bisa **berubah** saat pengguna berganti. WPF tidak tahu itu sendiri, jadi 4.9 harus
  memicu evaluasi ulang command sesudah haknya termuat.

### 4.9 Pemuatan hak: sesi normal dan mode debug

**Di mana haknya disimpan.** Pada `User` yang bersangkutan, bukan di `ApiClient`:

```csharp
// Em.Ui.Core — User (properti instance, bukan statis seperti AllClaims yang lama)
public ClaimAction[] AvailableClaims { get; internal set; } = [];
```

Hak adalah milik orang, bukan milik koneksi — jadi di sinilah ia paling sulit salah tempat. Tiga
akibat baiknya:

- **Tidak ada kode pembersih.** Sign out memanggil `SetActiveUser(null)`; begitu tidak ada pengguna
  aktif, tidak ada `AvailableClaims` untuk dibaca dan semuanya terjawab `false` dengan sendirinya.
  `ApiClient` tidak perlu `GrantedClaims` maupun `ActiveUserIsAdmin` — dua member yang batal dibuat.
- **`cUserIsAdmin` sudah ada di objek yang sama**, jadi pemeriksaan administrator (1.13) dan daftar
  haknya dibaca dari satu sumber. Tidak ada kemungkinan tanda admin dan daftar hak menyebut orang
  yang berbeda.
- **UI pengelola nanti bekerja pada objek yang sama** — ia toh memuat `User` untuk disunting.

Satu aturan yang mengikat karenanya: **`User` yang diangkat jadi `ActiveUser` harus sudah punya
`AvailableClaims`.** Sekarang hanya ada dua jalan yang mengangkatnya (`BeginSessionAsync` dan
`InitDebugMode`), dan keduanya ditangani di bawah. Layar yang nanti mengganti pengguna aktif secara
manual — di luar cakupan fase ini, menurut keputusan user — wajib mengikuti aturan yang sama.

Katalognya sendiri tetap di `EmApp.AllClaims` (1.14, 4.7): ia bukan milik siapa-siapa, dan
extension `Claims()` membacanya langsung dari `App.AllClaims` saat membentuk `ClaimCollection` (4.8).

**Satu method pemuat**, di `EmApp` (WPF):

```csharp
public async Task RefreshClaimsAsync() {
   if (GetActiveApiClient() is null) return;

   AllClaims = await ServiceProvider.GetRequiredService<ICredentialServices>()
      .GetMeta_AllClaimActions();

   if (ActiveUser is not { } user) return;

   // Administrator tidak perlu dibacakan pemberiannya sama sekali: indexer sudah menjawab true
   // untuknya selama nama claim-nya ada di katalog (1.13). Akun debugger dan akun admin bawaan
   // ikut ke cabang ini karena cUserIsAdmin-nya memang true - dan itu sekaligus yang menjaga
   // GetClaims() tidak pernah dipanggil untuk id akun sistem, yang akan melempar (4.7).
   user.AvailableClaims = user.cUserIsAdmin ? [] : await user.GetClaims();
}
```

Perhatikan bahwa syaratnya **milik penggunanya**, bukan `IsDebugMode`. Ini sengaja: saat seorang
user sungguhan disamar-masuki lewat token debug, `ActiveUser` adalah user itu — `cUserIsAdmin`-nya
`false`, jadi haknya dibaca dari database seperti biasa dan pengujian hak tetap berarti (1.13).

**Siapa yang memanggilnya:**

| Kapan | Di mana |
| --- | --- |
| Login berhasil, dan pemulihan sesi tersimpan | akhir blok `try` di `EmApp.BeginSessionAsync`, sesudah `SetActiveUser` |
| Sesi berakhir | tidak perlu memanggil apa pun — `EndSessionAsync` sudah memanggil `SetActiveUser(null)`, dan tanpa pengguna aktif indexer menjawab `false` di langkah 4. Katalognya boleh tetap tinggal; ia bukan milik siapa-siapa |
| **Mode debug** | lihat di bawah |

Gagal memuat saat login sengaja dibiarkan menggagalkan login-nya (ia berada di dalam `try` yang
sudah ada, yang membatalkan sesi lalu melempar ulang). Alasannya sama dengan yang sudah tertulis di
method itu: masuk dengan hak yang tidak diketahui lebih buruk daripada tidak jadi masuk — pengguna
akan melihat aplikasi yang seluruh tombolnya mati tanpa penjelasan.

**Mode debug — usulan tempatnya.** Mode debug tidak punya momen login sama sekali: penggunanya
dibuat di `InitDebugMode` (`EmApp.Statics.cs`) yang berjalan **sinkron di dalam `BuildApp`**,
sebelum window pertama ada dan sebelum ada koneksi aktif — jadi tidak ada satu pun `await` yang bisa
ditumpangi di sana. Momen yang benar-benar tersedia adalah **saat koneksi aktif terpasang**, yaitu
ketika kartu koneksi di home memilihkan `DefaultDebugConnection`.

Usulnya: `EmApp` berlangganan `ActiveConnectionChanged`-nya sendiri, dan saat mode debug, mulai
`RefreshClaimsAsync()` lalu **simpan `Task`-nya**:

```csharp
private Task? _claimsRefresh;

/// Dipakai layar yang ingin menunggu hak selesai dimuat sebelum menggambar dirinya.
public Task EnsureClaimsLoadedAsync() => _claimsRefresh ?? Task.CompletedTask;
```

Menyimpan `Task`-nya, bukan melepasnya sebagai `async void`, penting karena dua hal: exception-nya
punya tempat untuk ditangkap, dan layar yang peduli bisa menunggunya. Sesudah `Task` itu selesai,
picu evaluasi ulang command (`CommandManager.InvalidateRequerySuggested`, atau reload navigasi yang
sedang tampil) — tanpa itu tombol yang sudah dinilai "tidak boleh" saat katalog masih kosong akan
tetap mati sampai ada hal lain yang membuat WPF bertanya lagi.

Catatan: karena pengguna debug adalah administrator, langkah 4 di indexer (1.13) sudah menjawab
`true` begitu katalognya sampai. Yang dimuat untuknya memang hanya katalog — dan katalog itulah
yang tetap menjaring nama yang salah ketik.

Alternatif yang dipertimbangkan dan tidak dipilih:

- **`DefaultHomeControl.OnReloadRequested` dijadikan `async void`** (polanya sudah ada di
  `UserManager`). Lebih sederhana, tapi terikat pada satu control home bawaan — aplikasi yang
  memasang home-nya sendiri kehilangan pemuatan itu tanpa pesan apa pun.
- **`ClaimCollection` memuat sendiri saat pertama ditanya.** Tidak bisa: indexer-nya sinkron (1.11).
- **Mode debug menjawab `true` tanpa memanggil server sama sekali.** Paling murah, tapi katalognya
  tidak pernah sampai ke client, sehingga nama claim yang salah ketik tidak pernah ketahuan sampai
  ada user sungguhan yang memakainya — persis yang dijaga langkah 3 di indexer.

**Bukti fase ini berjalan** (dijalankan berurutan, memakai koneksi debug):

1. Start server — log startup menyebut dua claim `SampleServices`.
2. Start client mode debug — `Services.Claims()["CreateNewItem"]` `true` (administrator),
   `Services.Claims()["Typo"]` melempar di DEBUG walau penggunanya administrator.
3. Sign in sebagai user biasa (non-admin) — kedua claim `false`, tombolnya mati.
4. `PostMeta_AddUserClaim` untuk user itu, sign in ulang — `CreateNewItem` `true`, `DeleteItem` `false`.
5. `PostMeta_RemoveUserClaim`, sign in ulang — kembali `false`.

---

## 5. Keputusan yang masih terbuka

Semuanya boleh diputuskan belakangan; tidak ada yang menghalangi langkah di bagian 4.

- **Unique index `(cUserId, cUserClaimName)`.** Belum ada di database. Tanpa itu satu user bisa
  diberi claim yang sama dua kali. Catatan penting: unique pada `cUserClaimName` **saja** akan
  salah — itu berarti satu claim hanya boleh dimiliki satu orang di seluruh sistem. DDL-nya milik
  user, bukan skill/plan ini.
- **Apakah administrator melewati semua pemeriksaan claim — di server.** Sisi client sudah
  diputuskan (1.13); server belum. Kalau nanti diputuskan "ya", ingat jalur token debug sengaja
  membaca hak asli saat menyamar (`plan/executed/debug-token-dev-bypass.md`) — bypass admin tidak
  boleh ikut berlaku di sana, atau pengujian hak kehilangan artinya. Kalau diputuskan "tidak",
  berarti client dan server menjawab berbeda untuk administrator: tombolnya hidup, action-nya
  ditolak. Itu bukan keadaan yang bisa dibiarkan, jadi 1.13 harus ikut ditinjau bersama keputusan
  ini.
- **Label tampilan terpisah dari kunci.** Sekarang `Name` merangkap keduanya, sehingga memperbaiki
  kata-kata di layar berarti mengganti kunci — dan itu memutus pemberian yang sudah ada (1.5).
  Menambah properti label yang bebas berubah menutup itu. Sesudah 1.9, katalognya sudah sampai ke
  client, jadi label itu tinggal ikut menumpang tanpa action baru.
- **Konstanta kunci per module.** `Claims()["CreateNewItem"]` berupa literal tidak diperiksa compiler;
  salah ketik terbaca sebagai "tidak punya hak" — itulah yang dijaring sementara oleh langkah 3 di
  indexer (4.8). Satu class konstanta per module mengembalikan pemeriksaan itu ke compiler, dan
  sekalian jadi satu tempat yang memuat kosakata claim module. Kalau ini jadi diambil, langkah 3
  boleh dipertimbangkan untuk dilepas.
- **Penyegaran hak tanpa login ulang.** Sekarang hak dimuat sekali dan tidak pernah ditinjau lagi
  selama sesi; user yang baru diberi hak harus keluar-masuk. `RefreshClaimsAsync` sudah publik dan
  idempoten, jadi tombol reload navigasi bisa memanggilnya kapan saja — tinggal diputuskan apakah
  itu perilaku yang diinginkan.
- **`ta_UserClaim` menumpang `ICredentialServices` — bisa dipisah nanti.** Dipilih karena
  tabel itu anak dari `ta_User` dan interface tersebut sudah memiliki seluruh rombongan user.
  Argumen sebaliknya: otorisasi bukan autentikasi, dan `CredentialServices` sudah 744 baris
  sebelum tambahan ini. Memindahkannya ke interface sendiri nanti bukan perubahan yang mahal
  selama belum ada module lain yang memanggilnya.

---

## 6. Fase berikutnya (untuk orientasi, bukan untuk dikerjakan sekarang)

1. **Penegakan di server** — action menyebut claim yang dibutuhkannya, dan dispatcher menolaknya
   sebelum action jalan. Pilihan bentuknya (atribut pada action versus panggilan manual seperti
   `Request.RequireAdmin()`) belum diambil; yang pertama membuat action yang lupa menyebut claim
   tetap tertutup dan bisa diaudit sekaligus saat startup, yang kedua lebih luwes untuk hak yang
   bergantung isi data. Sampai ini ada, `ClaimCollection` hanya menyembunyikan tombol.
2. **UI pengelola claim**: katalognya sudah sampai di client lewat `GetMeta_AllClaimActions` (4.5)
   dan sudah terkelompok per module lewat `ClaimCollection` (4.8), jadi daftar centangnya tinggal
   digambar; centang/lepas-centang memanggil `PostMeta_AddUserClaim`/`PostMeta_RemoveUserClaim`
   yang sudah ada dari fase ini. Tidak ada action baru yang dibutuhkan.
3. **Laporan pemberian yatim** — baris di `ta_UserClaim` yang kuncinya tidak ada lagi di katalog,
   karena claim-nya dihapus atau module-nya dilepas. Datanya sudah lengkap di client: selisih
   antara hasil `GetMeta_UserClaims` dan `GetMeta_AllClaimActions`.
