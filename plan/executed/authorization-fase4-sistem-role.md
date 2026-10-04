# Plan — Authorization Fase 4: sistem role

Status: **sudah dieksekusi** (2026-09-18) — seluruh kode bagian 4–7 terpasang dan kedua solution
build bersih. Yang belum: menjalankan `vi_Role.sql` ke database, DDL milik user di bagian 4.2, dan
uji jalan di bagian 8.1.
Dibuat: 2026-09-18
Baseline: commit `ed687c9` (Async navigation lifecycle support), branch `data-services`, **berikut
perubahan kerja yang belum di-commit** — daftarnya di bagian 2.1.
Lanjutan dari `plan/executed/authorization-fase3-claim-system.md` (bagian 6 butir 2).

Cakupan: role sebagai kumpulan claim, penugasan role ke user, dan hak yang ikut mengalir dari role
ke pengguna aktif — dari DDL view sampai model UI `Role`/`RoleCollection`.
**Di luar cakupan: view model dan XAML.** `RoleManager.xaml` dipakai sebagai *acuan kebutuhan
data*, bukan sebagai layar yang dikerjakan di fase ini.

> **Dokumen ini ditulis untuk dikerjakan tanpa konteks percakapan sebelumnya.** Keputusan yang
> sudah diambil ada di bagian 3; jangan dibuka ulang tanpa alasan baru.

---

## 1. Kenapa ada berkas ini

Fase 3 memberi hak **satu per satu ke satu orang**. Itu tidak terpakai di lapangan: hak diberikan
per jabatan, dan orang berganti jabatan. Role adalah kumpulan claim yang bisa dipegang banyak user,
dengan masa berlaku per penugasan.

Yang ada sekarang baru kerangka: empat class model dan dua class UI yang seluruh methodnya
`throw new NotImplementedException()`, plus empat tabel yang sudah berdiri di database tapi belum
punya satu pun action.

Sesudah fase ini: role bisa dibuat, diisi claim, diberikan ke user dengan periode, dan hak dari
role ikut terbaca `ClaimCollection` persis seperti hak langsung — tanpa satu baris pun berubah di
`ClaimCollection` sendiri.

---

## 2. Kondisi awal

### 2.1 Yang sudah ada di working tree (belum di-commit)

| Berkas | Isi | Keadaan |
| --- | --- | --- |
| `Em.Libs/Api.Core.Models/ta_Role.cs` | entity role | ada, **salah `[Table]`** (lihat 2.3) |
| `Em.Libs/Api.Core.Models/ta_RoleClaim.cs` | entity claim milik role | ada, tanpa `[Key]` (sudah ditangani `OnModelCreating`) |
| `Em.Libs/Api.Core.Models/ta_UserRole.cs` | entity penugasan | ada, **tanpa key sama sekali** (lihat 2.3) |
| `Em.Libs/Api.Core.Models/vi_Role.cs` | view role | ada, turunan `ta_Role` tanpa kolom tambahan |
| `Em.Libs/Shared/AddressState.cs` | `enum RoleState { Disabled = 0, Active = 1 }` | menumpang berkas `AddressState` |
| `Em.Libs/Api.Core.Models/ta_UserClaim.cs` | +`cUserClaimStart`, `cUserClaimExpiry`, `datestamp` | kolom baru **belum diisi siapa pun** (lihat 2.3) |
| `Em.Api.Core/Api/Core/Models/ApiCoreContext.cs` | 4 `DbSet` baru + `HasKey` `ta_RoleClaim` | `ta_UserRole` belum dapat `HasKey` |
| `Em.Ui.Core/Api.Core.Models/Role.cs` | model UI role | seluruh method `NotImplementedException` |
| `Em.Ui.Core/Api.Core.Models/RoleCollection.cs` | wadah role | indexer **kondisinya terbalik** (lihat 2.3) |

### 2.2 Yang sudah siap dan tidak perlu disentuh

| Aset | Letak | Catatan |
| --- | --- | --- |
| Tabel `ta_Role`, `ta_RoleClaim`, `ta_UserRole` | database + `doc/MainTable.sql` | sudah lengkap dengan PK dan FK |
| Katalog claim | `EmApp.AllClaims` (server & client) | role hanya menunjuk ke kunci yang sudah ada di sini |
| `ClaimAction` + `Key`/`FromKey` | `Em.Libs/Shared/ClaimAction.cs` | bentuk kunci `module:name` dipakai apa adanya oleh `ta_RoleClaim.cClaimName` |
| `ClaimCollection` | `Em.Ui.Core/Ui.Core/shared/ClaimCollection.cs` | **tidak berubah**: ia hanya membaca `User.AvailableClaims` |
| `UiModel<TEntity, TService>` | `Em.Ui.Core/Ui.Core/shared/UiModel.cs` | `Role` mengikuti pola `User`/`Contact` |
| Pemeriksa hak di action | `Request.RequireAdmin()` / `RequireSelfOrAdmin()` | dipakai apa adanya oleh action baru |

### 2.3 Empat temuan yang menghalangi, diperbaiki di fase ini

1. **`ta_Role` memakai `[Table("ta_User")]`** (`ta_Role.cs:7`). Salin-tempel dari `ta_User`. EF akan
   memetakan dua entity ke tabel yang sama dan seluruh query role membaca tabel user.
2. **`ta_UserRole` tidak punya key.** PK-nya di database komposit `(cUserId, cRoleId)`, tapi
   entity-nya tidak diberi `[Key]` dan `OnModelCreating` hanya menyebut `ta_RoleClaim`. EF gagal
   membangun model — aplikasi mati saat `DbContext` pertama kali dipakai, bukan hanya saat role
   dibaca.
3. **`PostMeta_AddUserClaim` belum mengisi kolom baru `ta_UserClaim`.** `cUserClaimStart`,
   `cUserClaimExpiry`, dan `datestamp` `NOT NULL` di database; tanpa diisi nilainya `0001-01-01`,
   di luar jangkauan tipe `datetime` SQL Server — setiap pemberian claim langsung gagal insert.
4. **Indexer `RoleCollection[string]` kondisinya terbalik** (`RoleCollection.cs:24`): melempar
   justru ketika role-nya **ketemu**, dan mengembalikan `null!` ketika tidak. Pesannya juga masih
   pesan milik `ClaimCollection` (menyebut `AddClaims`).

---

## 3. Keputusan yang sudah diambil

1. **Member dibaca sebagai `User[]`, periode dibaca terpisah.** `Role.GetMembers()` mengembalikan
   `User[]` lewat view user yang sudah ada; `Role.GetAssignments()` mengembalikan `ta_UserRole[]`.
   Penjodohan keduanya urusan view model nanti. Konsekuensinya: **tidak ada `vi_UserRole`** dan
   tidak ada class `RoleMember` di fase ini.
2. **Hak dari role dibaca lewat action sendiri, digabung di client.** `GetMeta_UserClaims` tetap
   hak langsung; ditambah `GetMeta_UserRoleClaims`. `EmApp.RefreshClaimsAsync` yang menyatukan
   keduanya ke `User.AvailableClaims`. Keuntungannya client tahu asal sebuah hak; harganya aturan
   periode hidup di dua action — keduanya tetap **disaring di server**, client tidak pernah
   memutuskan masa berlaku.
3. **Keempat temuan 2.3 diperbaiki di fase ini**, termasuk pengisian kolom periode `ta_UserClaim`
   yang sebetulnya milik fase 3.
4. **Action role menumpang `ICredentialServices`**, mengikuti stub yang sudah ditulis
   (`Role : UiModel<vi_Role, ICredentialServices>`). Argumen sebaliknya di bagian 9.
5. **`vi_Role` tetap cerminan `ta_Role` apa adanya** — tidak ada kolom hitungan di dalamnya, sesuai
   aturan view ringan di `doc/Struktur penamaan object database..md`. Angka member dan claim per
   role datang dari satu action penghitung (5.3).
6. **Menghapus role adalah penghapusan sungguhan**, sesuai kalimat di layar acuan (*cannot be
   undone*). `ta_RoleClaim` ikut terhapus lewat `ON DELETE CASCADE`; `ta_UserRole` **tidak**
   (FK-nya `NO ACTION`), jadi barisnya dihapus lebih dulu di dalam transaksi yang sama.
7. **Masa berlaku:** `ta_UserRole.cUserRoleStart`/`cUserRoleExpiry` nullable — `null` pada start
   berarti berlaku sejak dibuat, `null` pada expiry berarti tanpa akhir. `ta_UserClaim` kolomnya
   `NOT NULL`, jadi tanpa akhir ditulis sebagai `Defaults.NoExpiry`
   (`new DateTime(9999, 12, 31)`). Alternatifnya di bagian 9.

---

## 4. Rancangan — database

### 4.1 Berkas view baru

`doc/sqlscript/mssql/views/vi_Role.sql`, mengikuti bentuk `vi_User.sql` (drop, create, lalu
satu `SELECT TOP 100` sebagai contoh baca):

```sql
USE EmDb
DROP VIEW IF EXISTS vi_Role;
GO
CREATE VIEW vi_Role AS
SELECT
   a.cRoleId
   , a.cRoleName
   , a.cRoleState
   , a.cRoleDescription
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Role AS a
GO
```

Passthrough, dan itu disengaja: view adalah tempat kolom gabungan tumbuh nanti (mis. nama pengubah
terakhir) tanpa mengubah tipe yang sudah di-binding layar.

**`doc/sqlscript` satu arah: berkas → database.** Script dijalankan setelah ditulis; jangan
pernah membaca ulang definisi view dari database.

### 4.2 DDL yang diperlukan tapi dimiliki user

Bukan bagian eksekusi — sampaikan ke user sebagai daftar:

- `UNIQUE (cRoleName)` pada `ta_Role`. `RoleCollection[string]` mencari role lewat namanya; tanpa
  unique, dua role bernama sama membuat lookup itu melempar.
- `UNIQUE (cUserId, cUserClaimName)` pada `ta_UserClaim` — masih tertinggal dari fase 3.
- `UNIQUE (cRoleId, cClaimName)` sudah terpenuhi oleh PK `ta_RoleClaim`.

---

## 5. Rancangan — kontrak action

Seluruhnya di `src/shared/Em.Libs/Api.Core.Models/ICredentialServices.cs`, pada region yang
sesuai. Action non-tabel memakai awalan `Meta` dan tinggal di region `Meta's` di bawah `Views`,
sesuai aturan penamaan di `CLAUDE.md`.

### 5.1 Region `Tables`

Tiga region baru, mengikuti bentuk region `ta_User`.

**`#region ta_Role`**

| Action | Hak |
| --- | --- |
| `Task<ta_Role?> GetTa_Role_ById(string cRoleId)` | user masuk |
| `Task<ta_Role[]> GetTa_Roles()` | user masuk |
| `Task<int> GetTa_Roles_Count()` | user masuk |
| `Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize)` | user masuk |
| `Task PostTa_Role_New(ta_Role data)` / `PostTa_Role_NewBatch(ta_Role[] datas)` | admin |
| `Task PostTa_Role_Update(ta_Role data)` / `PostTa_Role_UpdateBatch(...)` | admin |
| `Task PostTa_Role_Delete(ta_Role data)` / `PostTa_Role_DeleteBatch(...)` | admin |

**`#region ta_RoleClaim`**

| Action | Hak |
| --- | --- |
| `Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId)` | user masuk |
| `Task PostTa_RoleClaim_New(ta_RoleClaim data)` / `PostTa_RoleClaim_NewBatch(...)` | admin |
| `Task PostTa_RoleClaim_Delete(ta_RoleClaim data)` / `PostTa_RoleClaim_DeleteBatch(...)` | admin |

Varian batch bukan hiasan: tombol *Save changes* pada layar acuan mengirim beberapa perubahan claim
sekaligus, dan satu request lebih baik daripada dua belas.

**`#region ta_UserRole`**

| Action | Hak |
| --- | --- |
| `Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId)` | user masuk |
| `Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId)` | diri sendiri atau admin |
| `Task<int> GetTa_UserRoles_Count()` | user masuk |
| `Task PostTa_UserRole_New(ta_UserRole data)` / `PostTa_UserRole_NewBatch(...)` | admin |
| `Task PostTa_UserRole_Update(ta_UserRole data)` | admin |
| `Task PostTa_UserRole_Delete(ta_UserRole data)` / `PostTa_UserRole_DeleteBatch(...)` | admin |

`GetTa_UserRoles_Count()` yang mengisi penghitung *27 assignments* di toolbar acuan.

### 5.2 Region `Views`

**`#region vi_Role`** — `GetVi_Role_ById`, `GetVi_Roles`, `GetVi_Roles_InPage`.

**Tambahan pada region `vi_User` yang sudah ada:**
`Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId)` — inilah yang mengisi `Role.GetMembers()`
(keputusan 3.1). Ditaruh di region `vi_User` karena yang dikembalikannya baris user, bukan role.

### 5.3 Region `Meta's`

| Action | Isi |
| --- | --- |
| `Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId)` | hak yang mengalir dari role aktif milik user itu — diri sendiri atau admin |
| `Task<RoleCounter[]> GetMeta_RoleCounters()` | jumlah member dan jumlah claim per role, sekali jalan untuk seluruh daftar |
| `Task<ClaimUsage[]> GetMeta_ClaimUsage()` | berapa role yang memegang tiap kunci claim — pengisi *Carried by N roles* di lembar katalog |

Dua tipe payload baru di `src/shared/Em.Libs/Shared/`:

```csharp
public class RoleCounter {
   public required string cRoleId { get; init; }
   public required int MemberCount { get; init; }
   public required int ClaimCount { get; init; }
}

public class ClaimUsage {
   public required string ClaimKey { get; init; }
   public required int RoleCount { get; init; }
}
```

Keduanya dinamai tanpa awalan kolom karena bukan baris tabel — sama seperti `TokenResult` dan
`UserClaimPayload`.

---

## 6. Rancangan — sisi server

Berkas: `src/backend/Em.Api.Core/Api/Core/CredentialServices.cs`.

### 6.1 Bentuk umum

Action tabel mengikuti persis pola region `ta_User` yang sudah ada: `[GetAction]`/`[PostAction]`,
`ctx.<DbSet>` dengan `Where`, `OrderBy` pada action berhalaman, `ctx.DeleteRow` untuk hapus, satu
`ctx.SaveChangesAsync()` di akhir.

Pemeriksaan hak jadi baris pertama tiap action: `Request.RequireUserId()` untuk baca,
`Request.RequireAdmin()` untuk tulis, `Request.RequireSelfOrAdmin(cUserId)` untuk action yang
menyebut satu user.

### 6.2 `PostTa_RoleClaim_New` memverifikasi kunci

Persis seperti `PostMeta_AddUserClaim`: kunci yang tidak ada di `App.AllClaims` ditolak `400`. Role
tidak boleh menyimpan claim yang tidak dideklarasikan module mana pun — kalau boleh, salah ketik
akan diam-diam menjadi hak yang tidak pernah menyala. Baris yang sudah ada dilewati diam-diam
(idempoten), sama seperti pemberian claim langsung.

### 6.3 `PostTa_Role_Delete` menghapus penugasannya lebih dulu

```
hapus ta_UserRole WHERE cRoleId = X     -- FK-nya NO ACTION, jadi harus manual
hapus ta_Role     WHERE cRoleId = X     -- ta_RoleClaim ikut lewat CASCADE
satu SaveChangesAsync
```

Tanpa langkah pertama, menghapus role yang masih dipegang seseorang gagal dengan pelanggaran FK
mentah dari SQL Server — pesan yang tidak berarti apa-apa di layar.

### 6.4 `GetMeta_UserRoleClaims`

```
ta_UserRole r JOIN ta_Role o      ON r.cRoleId = o.cRoleId
              JOIN ta_RoleClaim c ON c.cRoleId = o.cRoleId
WHERE r.cUserId = X
  AND o.cRoleState = RoleState.Active
  AND (r.cUserRoleStart  IS NULL OR r.cUserRoleStart  <= now)
  AND (r.cUserRoleExpiry IS NULL OR r.cUserRoleExpiry >= now)
-> distinct c.cClaimName -> ClaimAction.FromKey
```

`now` diambil dari `App.GetDateStampAsync()`, bukan dari client: masa berlaku yang bisa diputuskan
mesin pemanggil bukan masa berlaku.

Tidak disaring terhadap katalog — alasannya sama dengan `GetMeta_UserClaims`: pemberian yatim harus
tetap terlihat supaya bisa dicabut.

### 6.5 `GetMeta_UserClaims` ikut menyaring periode

Sekarang action ini mengembalikan seluruh baris apa adanya. Dengan adanya kolom periode, ia
menyaring dengan aturan yang sama seperti 6.4 (`cUserClaimStart <= now <= cUserClaimExpiry`). Ini
perubahan perilaku yang disengaja, dan satu-satunya yang menyentuh hasil fase 3.

### 6.6 `PostMeta_AddUserClaim` mengisi kolom baru (temuan 2.3 butir 3)

```csharp
var stamp = await App.GetDateStampAsync();
ctx.ta_UserClaims.Add(new ta_UserClaim {
   cUserClaimId     = $"{Ulid.NewUlid()}",
   cUserId          = claim.cUserId,
   cUserClaimName   = claim.cUserClaimName,
   cUserClaimStart  = stamp,
   cUserClaimExpiry = Defaults.NoExpiry,
   datestamp        = stamp
});
```

`Defaults.NoExpiry = new DateTime(9999, 12, 31)` ditambahkan ke `Em.Libs/Shared` — satu tempat
yang menyatakan "tanpa akhir" untuk kolom yang tidak menerima `null`.

`UserClaimPayload` **tidak** diberi kolom periode di fase ini: belum ada layar yang memilihkannya.

### 6.7 Perbaikan model dan `DbContext`

- `ta_Role.cs`: `[Table("ta_Role")]`; `cRoleState` jadi `RoleState` (bukan `int`, mengikuti
  `ta_User.cUserState`); `cRoleDescription` jadi `string?` (kolomnya `NULL` di database).
- `ta_UserRole.cs`: tetap tanpa `[Key]`, key kompositnya didaftarkan di `OnModelCreating`
  bersebelahan dengan `ta_RoleClaim`:
  `modelBuilder.Entity<ta_UserRole>().HasKey(k => new { k.cUserId, k.cRoleId });`
- `RoleState` dipindah dari `AddressState.cs` ke berkas sendiri `Em.Libs/Shared/RoleState.cs`,
  mengikuti satu-enum-satu-berkas seperti `UserState.cs`.

---

## 7. Rancangan — sisi client

### 7.1 Service WPF

`src/shared/Em.Ui.Wpf.Core/Api.Core/CredentialService.cs` — satu baris per action, bentuknya
sudah baku: `GetAsync<T>(nameof(X), args)` dan `PostAsync(nameof(X), args)`, di region yang sama
persis dengan interface. Tidak ada logika di lapisan ini.

### 7.2 `Role` (`Em.Ui.Core/Api.Core.Models/Role.cs`)

Stub yang ada diisi; **seluruh method yang menyentuh server berubah jadi `Task`** — yang sekarang
tertulis `ClaimAction[] GetRoleClaims()` dan `void AddMember(User)` tidak bisa tetap sinkron karena
di baliknya ada HTTP.

**Statics** (pola `User`):

| Anggota | Isi |
| --- | --- |
| `CreateNewRole(IEmApp)` | role kosong, `IsBlank = true`, `cRoleId` diisi teks penanda dan `cRoleState = Active` pada baris mentah — sama persis dengan `User.CreateNewUser` |
| `GetRole_ByIdAsync(app, cRoleId)` | lewat `GetVi_Role_ById` |
| `GetRoles_InPageAsync(app, page, pageSize)` | lewat `GetVi_Roles_InPage` |
| `GetRoles_PageCountAsync(app)` | lewat `GetTa_Roles_Count` |
| `Build(app, vi_Role)` | pembungkus |

**Properti**: `cRoleId` (setter privat), `cRoleName`, `cRoleState` (`RoleState`),
`cRoleDescription`, plus `ustamp`/`datestamp`/`json_object` dari `UiModel`.

**Implementasi `UiModel`**: `ReadFrom`/`WriteTo` pemetaan lurus; `FetchAsync` → `GetVi_Role_ById`;
`UpdateAsync` → `PostTa_Role_Update`; `InsertAsync` → ULID baru, `datestamp` disamakan dengan
`ustamp` yang baru distempel, lalu `PostTa_Role_New`; `BuildJson` → `patch`.

**Hitungan** (`MemberCount`, `ClaimCount`): property biasa ber-`SetField`, **diisi dari luar** oleh
`RoleCollection` dari hasil `GetMeta_RoleCounters` — bukan dihitung sendiri per role, karena satu
rail berisi belasan role dan itu akan jadi belasan request.

**Method**:

| Method | Isi |
| --- | --- |
| `Task<ClaimAction[]> GetRoleClaims()` | `GetTa_RoleClaims_ByRoleId` → `ClaimAction.FromKey` |
| `Task AddClaim(ClaimAction action)` | `PostTa_RoleClaim_New` |
| `Task AddClaim(string claimName)` | overload: merangkai kunci lewat `ClaimAction`, lalu memanggil overload di atasnya |
| `Task RemoveClaim(ClaimAction action)` | `PostTa_RoleClaim_Delete` |
| `Task<User[]> GetMembers()` | `GetVi_Users_ByRoleId` → `User.Build` |
| `Task<ta_UserRole[]> GetAssignments()` | `GetTa_UserRoles_ByRoleId` |
| `Task AddMember(User member, DateTime? start = null, DateTime? expiry = null)` | `PostTa_UserRole_New`, stempel dari `App.GetDateStampAsync()` |
| `Task SetMemberPeriod(User member, DateTime? start, DateTime? expiry)` | `PostTa_UserRole_Update` — tombol *Change the period of this assignment* |
| `Task RemoveMember(User member)` | `PostTa_UserRole_Delete` |
| `Task DeleteAsync()` | `PostTa_Role_Delete` (`UiModel` tidak punya jalur hapus) |
| `Task<Role> DuplicateAsync(string newName)` | role baru + salinan seluruh claim-nya lewat `PostTa_RoleClaim_NewBatch`; **member tidak ikut disalin** |

`AddMember`/`RemoveMember`/`SetMemberPeriod` menolak akun sistem lewat penjaga yang sama dengan
`User` (`SystemAccountException`): akun debugger dan admin bawaan tidak punya baris user untuk
ditunjuk FK `ta_UserRole`.

### 7.3 `RoleCollection` (`Em.Ui.Core/Api.Core.Models/RoleCollection.cs`)

Layar acuan menuntut wadah ini menjawab enam hal: isi daftar, jumlah total, penyaring
All/In use/Disabled, pencarian nama, halaman, dan dua penghitung toolbar.

- Penyimpan internal jadi `ObservableCollection<Role>` dan class meneruskan
  `INotifyCollectionChanged`, supaya daftar yang di-binding ikut bergerak saat role ditambah atau
  dihapus. `IEnumerable<Role>`, `Count`, dan indexer `[int]` tetap seperti yang sudah ditulis.
- **Indexer `[string name]` diperbaiki** (temuan 2.3 butir 4): melempar ketika role **tidak**
  ketemu, dengan pesan tentang role — bukan pesan `AddClaims` milik `ClaimCollection`. Ditemani
  `bool Contains(string name)` untuk pemanggil yang "tidak ketemu" bukan kesalahannya.
- `static Task<RoleCollection> LoadAsync(IEmApp app, int page = 1, int pageSize = 50)` — memuat
  satu halaman role, lalu **satu** `GetMeta_RoleCounters` untuk mengisi `MemberCount`/`ClaimCount`
  seluruh isinya, lalu `GetTa_UserRoles_Count` untuk `TotalAssignments`.
- Properti: `TotalCount`, `TotalAssignments`, `Page`, `PageSize`.
- `Task ReloadAsync()`, `Task<Role> NewRoleAsync(string name, string? description)`, dan
  `Task RemoveAsync(Role role)` — supaya daftar tidak pernah perlu dibangun ulang dari nol oleh
  pemanggil sesudah satu perubahan.
- Penyaringan dan pencarian **tidak** dikerjakan di sini: keduanya urusan tampilan, dan
  `CollectionView` di view model sudah punya jalurnya sendiri.

### 7.4 `ModelExtensions`

Region `For Role` baru: `GetRoles_InPageAsync(this IEmApp)`, `GetRole_ById(this IEmApp)`, dan
`GetRolesAsync(this User)` — role yang dipegang seorang user, lewat `GetTa_UserRoles_ByUserId`,
untuk layar editor user nanti.

### 7.5 Hak dari role masuk ke pengguna aktif

`src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs`, `RefreshClaimsAsync` — satu baris berubah:

```csharp
user.AvailableClaims = user.cUserIsAdmin
   ? []
   : [.. (await user.GetClaims())
        .Concat(await user.GetRoleClaims())
        .DistinctBy(r => r.Key, StringComparer.OrdinalIgnoreCase)];
```

`User.GetRoleClaims()` ditambahkan di sebelah `User.GetClaims()` yang sudah ada, dengan penjaga akun
sistem yang sama. `DistinctBy` ada karena satu claim bisa datang dua kali — langsung dan lewat role
— dan `User.AvailableClaims` juga dibaca layar, bukan hanya `ClaimCollection`.

Cabang administrator tidak berubah: ia sudah menjawab `true` untuk seluruh katalog.

---

## 8. Urutan eksekusi

1. **Perbaiki entity dan `DbContext`** (6.7) — `[Table]`, `HasKey` `ta_UserRole`, `RoleState` ke
   berkas sendiri, `cRoleDescription` nullable. Tanpa ini tidak ada yang bisa diuji sama sekali.
2. **Tambah `Defaults.NoExpiry`, `RoleCounter`, `ClaimUsage`** di `Em.Libs/Shared`.
3. **Tulis `doc/sqlscript/mssql/views/vi_Role.sql`** (4.1), lalu tanyakan user apakah script
   dijalankan sekarang atau olehnya sendiri. Sampaikan juga daftar DDL di 4.2.
4. **Tambah seluruh action ke `ICredentialServices`** (bagian 5), lengkap dengan region.
5. **Implementasikan di `CredentialServices`** (bagian 6), termasuk 6.5 dan 6.6.
6. **Implementasikan di `CredentialService` WPF** (7.1).
7. **Isi `Role`** (7.2).
8. **Isi `RoleCollection`** (7.3) dan `ModelExtensions` (7.4).
9. **Gabungkan hak di `RefreshClaimsAsync`** (7.5).
10. **Build kedua solution** — `src/backend/Em.Api.slnx` dan `src/frontend/Em.Ui.Wpf.slnx`.

### 8.1 Yang harus diuji sebelum dianggap selesai

Belum ada project test di repo ini, jadi seluruhnya uji jalan:

- Aplikasi WPF menyala dan bisa login — membuktikan model EF terbangun (temuan 2.3 butir 2).
- Buat role, isi dua claim, berikan ke satu user non-admin, login sebagai user itu: tombol yang
  dijaga claim tersebut menyala tanpa claim langsung diberikan.
- Set `cRoleState = Disabled` pada role itu, login ulang: tombolnya mati lagi.
- Beri penugasan dengan `cUserRoleStart` besok: haknya belum berlaku hari ini.
- Beri penugasan dengan `cUserRoleExpiry` kemarin: haknya sudah tidak berlaku.
- Hapus role yang masih punya member: berhasil, dan barisnya hilang dari `ta_UserRole` dan
  `ta_RoleClaim`.
- Beri satu claim langsung ke user (jalur fase 3): insert berhasil — pembuktian temuan 2.3 butir 3.
- Satu claim yang dimiliki langsung **dan** lewat role muncul sekali di `AvailableClaims`.

---

## 9. Keputusan yang masih terbuka

- **`ICredentialServices` versus `IRoleServices`.** Fase ini menumpang (3.4), mengikuti stub yang
  sudah ditulis. Argumen memisahkan tetap berlaku dan makin kuat sesudah fase ini: otorisasi bukan
  autentikasi, dan berkas itu sudah sekitar 800 baris sebelum ~20 action baru ini. Memindahkannya
  nanti murah selama belum ada module lain yang memanggilnya.
- **Kolom periode `ta_UserClaim` sebaiknya nullable.** `NOT NULL` memaksa lahirnya sentinel
  `Defaults.NoExpiry`, sementara `ta_UserRole` — tabel bersaudara dengan arti yang sama — memakai
  `NULL` untuk hal yang persis sama. Menyamakan keduanya menghapus sentinel itu. DDL-nya milik user.
- **Penamaan `ta_RoleClaim.cClaimName`.** Konvensi meminta `c` + nama tabel + nama kolom, jadi
  semestinya `cRoleClaimName`. Tabel itu juga tidak memuat kolom standar
  (`ustamp`/`datestamp`/`json_object`) — pengecualian yang sama seperti `ta_UserClaim` dulu, karena
  barisnya hanya pernah di-insert dan di-delete. Keduanya keputusan DDL milik user; fase ini memakai
  nama yang ada apa adanya.
- **"Last changed by" belum punya sumber.** Layar acuan menampilkan nama pengubah terakhir, tapi
  `ta_Role` hanya menyimpan `ustamp` berupa waktu. Pilihannya kolom baru atau `json_object`; sampai
  diputuskan, layar hanya bisa menampilkan waktunya.
- **`RoleState` belum punya nilai negatif.** Konvensi enum `*State` di repo ini memakai nilai negatif
  untuk "tidak dipakai/dihapus". Selama penghapusan role masih penghapusan sungguhan (3.6), nilai itu
  belum dibutuhkan — kalau nanti berubah jadi soft delete, `Deleted = -1` yang pertama ditambahkan.
- **Penyegaran hak tanpa login ulang** — sama seperti catatan fase 3, kini bertambah alasan: role
  bisa dicabut dari seseorang yang sedang bekerja, dan sesinya tidak akan tahu sampai dia keluar.

---

## 10. Yang sengaja ditunda

- **Penegakan hak di server** (fase 3 bagian 6 butir 1) tetap belum ada. Role yang dibuat di sini
  masih hanya menyalakan dan mematikan tombol.
- **Layar `RoleManager`**: view model, binding, dan command. Fase ini berhenti tepat di bawahnya —
  `Role` dan `RoleCollection` adalah yang akan dipegang view model itu.
- **Export** dan lembar katalog claim di layar acuan. `GetMeta_ClaimUsage` sudah disiapkan untuk
  yang kedua, tapi tidak ada pemakainya di fase ini.
- **Role bersarang** (role mewarisi role lain) dan role bawaan yang tidak bisa dihapus. Keduanya
  belum pernah diminta.
