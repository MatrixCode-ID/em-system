# Eksekusi Authorization Fase 4 — sistem role

Status: **langkah 1–10 selesai, belum di-commit; uji jalan (8.1) baru sebagian**
Dibuat: 2026-09-18
Baseline: commit `ed687c9` (Async navigation lifecycle support), branch `data-services`, berikut
perubahan kerja yang sudah ada di working tree sebelum eksekusi
Plan sumber: `plan/executed/authorization-fase4-sistem-role.md` — sudah dipindah dari
`plan/unexecuted/`, status di dalamnya sudah diperbarui
Build: `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` sama-sama hijau, tanpa warning
Database: `vi_Role` dan dua unique constraint sudah berdiri di `EmDb` (<server-dev>)

Angka perubahan: 12 berkas tersunting (+760 / −15), 9 berkas kode baru, 1 script view baru.

---

## 1. Ringkasan

Fase 3 memberi hak satu per satu ke satu orang. Fase ini membuat hak bisa diberikan **per jabatan**:
role adalah kumpulan claim yang bisa dipegang banyak user dengan masa berlaku per penugasan, dan
hak yang mengalir dari role terbaca `ClaimCollection` persis seperti hak langsung.

Sebelum eksekusi yang ada baru kerangka — empat class model dan dua class UI yang seluruh methodnya
`throw new NotImplementedException()`, plus empat tabel yang sudah berdiri di database tanpa satu pun
action. Sesudahnya: 30 action baru di `ICredentialServices`, keduanya terimplementasi di backend dan
client, `Role`/`RoleCollection` terisi penuh, dan `EmApp.RefreshClaimsAsync` menyatukan hak langsung
dengan hak dari role.

`ClaimCollection` sendiri **tidak berubah satu baris pun** — ia hanya membaca `User.AvailableClaims`,
dan yang berubah adalah apa yang mengisi property itu. Itu memang yang diharapkan rencana.

---

## 2. Empat temuan penghalang — semuanya diperbaiki

Rencana bagian 2.3 menyebut empat hal yang harus beres sebelum apa pun bisa diuji. Keempatnya
ditutup, dan dua di antaranya adalah bug yang akan mematikan aplikasi seluruhnya:

| # | Temuan | Perbaikan |
| --- | --- | --- |
| 1 | `ta_Role` memakai `[Table("ta_User")]` — salin-tempel; EF memetakan dua entity ke satu tabel dan seluruh query role membaca tabel user | `[Table("ta_Role")]` |
| 2 | `ta_UserRole` tidak punya key sama sekali; EF gagal membangun model, jadi aplikasi mati saat `DbContext` **pertama kali dipakai** — bukan hanya saat role dibaca | `HasKey(k => new { k.cUserId, k.cRoleId })` di `OnModelCreating`, bersebelahan dengan `ta_RoleClaim` |
| 3 | `PostMeta_AddUserClaim` tidak mengisi `cUserClaimStart`/`cUserClaimExpiry`/`datestamp` yang `NOT NULL`; tanpa diisi nilainya `0001-01-01`, di luar jangkauan `datetime` SQL Server — setiap pemberian claim gagal insert | Ketiganya diisi: `stamp` dari `App.GetDateStampAsync()`, expiry `Defaults.NoExpiry` |
| 4 | Indexer `RoleCollection[string]` kondisinya terbalik — melempar justru ketika role **ketemu**, dan mengembalikan `null!` ketika tidak; pesannya pun masih milik `ClaimCollection` | Kondisi dibalik, pesan tentang role, ditemani `bool Contains(string)` |

Temuan 2 adalah alasan kenapa urutan eksekusi rencana menaruh perbaikan entity di langkah 1: tanpa
itu tidak ada satu pun action — role maupun bukan — yang bisa dijalankan.

---

## 3. Apa yang berubah

### 3.1 Kontrak bersama (`src/shared/Em.Libs`)

| Berkas | Perubahan |
| --- | --- |
| `Api.Core.Models/ta_Role.cs` | `[Table]` diperbaiki; `cRoleState` jadi `RoleState` (bukan `int`, mengikuti `ta_User.cUserState`); `cRoleDescription` jadi `string?` karena kolomnya `NULL` |
| `Api.Core.Models/ta_UserRole.cs` | Komentar yang menjelaskan kenapa key kompositnya tidak bisa ditulis `[Key]` dan di mana ia didaftarkan |
| `Shared/RoleState.cs` | **Baru.** `RoleState` dipindah keluar dari `AddressState.cs`, mengikuti satu-enum-satu-berkas seperti `UserState.cs` |
| `Shared/RoleCounter.cs` | **Baru.** `cRoleId` + `MemberCount` + `ClaimCount`, payload penghitung seluruh daftar |
| `Shared/ClaimUsage.cs` | **Baru.** `ClaimKey` + `RoleCount`, untuk lembar katalog claim |
| `Defaults.cs` | `NoExpiry = new DateTime(9999, 12, 31)` — satu tempat yang menyatakan "tanpa akhir" untuk kolom yang tidak menerima `null` |
| `Api.Core.Models/ICredentialServices.cs` | 30 action baru dalam empat region: `ta_Role`, `ta_RoleClaim`, `ta_UserRole`, `vi_Role`, plus satu tambahan di `vi_User` dan tiga di `Meta's` |

Penamaan action mengikuti `CLAUDE.md`: yang menempel pada satu tabel/view tetap berbentuk tabel
(`GetTa_Role_ById`, `GetVi_Roles_InPage`), yang tidak memakai awalan `Meta` dan tinggal di region
`Meta's` di bawah `Views` — `GetMeta_UserRoleClaims`, `GetMeta_RoleCounters`, `GetMeta_ClaimUsage`.

`GetVi_Users_ByRoleId` sengaja ditaruh di region `vi_User`, bukan `vi_Role`: yang dikembalikannya
baris user, bukan role.

### 3.2 Backend (`Em.Api.Core/Api/Core/CredentialServices.cs`)

Tiga region tabel baru, satu region view baru, tiga action `Meta's`. Pemeriksaan hak jadi baris
pertama setiap action baru — `Request.RequireUserId()` untuk baca, `RequireAdmin()` untuk tulis,
`RequireSelfOrAdmin(cUserId)` untuk yang menyebut satu user.

**`PostTa_Role_Delete` menghapus penugasan lebih dulu.** Foreign key `ta_UserRole` → `ta_Role`
adalah `NO ACTION`, jadi menghapus role yang masih dipegang seseorang akan gagal sebagai pelanggaran
foreign key mentah dari SQL Server — pesan yang tidak berarti apa-apa di layar. `ta_RoleClaim` ikut
sendiri lewat `CASCADE`. Semuanya dalam satu `SaveChangesAsync`, jadi satu transaksi.

Varian batch-nya **sengaja bukan `BulkDeleteAsync`**: operasi bulk melewati change tracker dan
berjalan sebagai perintahnya sendiri, sehingga penghapusan penugasan dan penghapusan role tidak lagi
berada dalam satu transaksi.

**`PostTa_RoleClaim_New` memverifikasi kunci.** Kunci yang tidak dideklarasikan module mana pun
ditolak `400` — kalau boleh lewat, satu salah ketik akan diam-diam menjadi hak yang tidak pernah
menyala dan tidak pernah kelihatan salah. Baris yang sudah ada dilewati diam-diam, idempoten seperti
pemberian claim langsung. Pemeriksaan ini dan milik `PostMeta_AddUserClaim` sekarang berbagi satu
helper `EnsureClaimIsDeclared`.

Varian batch-nya menyaring baris kembar di sisi server — termasuk kembar di dalam kiriman yang sama —
supaya satu hak yang kebetulan sudah dipegang tidak menggagalkan sebelas perubahan lain yang dikirim
bersamanya oleh tombol simpan yang sama.

**`GetMeta_UserRoleClaims`** menggabungkan `ta_UserRole` → `ta_Role` → `ta_RoleClaim`, menyaring role
yang `Active` dan penugasan yang masa berlakunya sedang jalan, lalu `Distinct`. `now` diambil dari
`App.GetDateStampAsync()`, bukan dari client: masa berlaku yang bisa diputuskan mesin pemanggil bukan
masa berlaku. Hasilnya **tidak** disaring terhadap katalog, alasannya sama dengan `GetMeta_UserClaims`
— pemberian yatim harus tetap terlihat supaya bisa dicabut.

**`GetMeta_UserClaims` ikut menyaring periode** (rencana 6.5). Ini satu-satunya perubahan perilaku
yang menyentuh hasil fase 3, dan memang disengaja: sesudah kolom periodenya diisi, pemberian yang
sudah lewat masa berlakunya bukan lagi hak.

**`GetMeta_RoleCounters`** memakai tiga query, bukan satu per role. Daftar role-nya yang menentukan
baris mana yang keluar — bukan hasil pengelompokan — supaya role yang belum punya anggota maupun hak
tetap dijawab dengan nol, bukan dengan tidak ada barisnya sama sekali.

### 3.3 Client (`src/shared/Em.Ui.Core`, `Em.Ui.Wpf.Core`)

| Berkas | Perubahan |
| --- | --- |
| `Em.Ui.Wpf.Core/Api.Core/CredentialService.cs` | 30 baris satu-per-action, region sama persis dengan interface, tanpa logika |
| `Em.Ui.Core/Api.Core.Models/Role.cs` | Stub terisi penuh — statics, properti, implementasi `UiModel`, region `Claims`, `Members`, `Methods` |
| `Em.Ui.Core/Api.Core.Models/RoleCollection.cs` | `ObservableCollection` + `INotifyCollectionChanged`, `LoadAsync`/`ReloadAsync`/`GoToPageAsync`/`NewRoleAsync`/`RemoveAsync`, empat properti angka |
| `Em.Ui.Core/ModelExtensions.cs` | Region `For Role` baru, plus `User.GetRolesAsync()` |
| `Em.Ui.Core/Api.Core.Models/User.cs` | `GetRoleClaims()` di sebelah `GetClaims()`, dengan penjaga akun sistem yang sama |
| `Em.Ui.Wpf.Core/Core/EmApp.cs` | `RefreshClaimsAsync` menggabungkan keduanya |

Seluruh method `Role` yang menyentuh server berubah jadi `Task` — yang di stub tertulis
`ClaimAction[] GetRoleClaims()` dan `void AddMember(User)` tidak bisa tetap sinkron karena di
baliknya ada HTTP.

`AddMember`/`RemoveMember`/`SetMemberPeriod` menolak akun sistem lewat `SystemAccountException`, dan
setiap method yang menyentuh isi role dijaga `EnsureSaved()`: isi role tinggal di baris lain yang
menunjuk id role, jadi tidak satu pun bisa ditulis sebelum id itu ada.

**Penggabungan hak** di `RefreshClaimsAsync`:

```csharp
user.AvailableClaims = user.cUserIsAdmin
   ? []
   : [.. (await user.GetClaims())
        .Concat(await user.GetRoleClaims())
        .DistinctBy(r => r.Key, StringComparer.OrdinalIgnoreCase)];
```

`DistinctBy` ada karena satu claim bisa datang dua kali — langsung dan lewat role — dan
`AvailableClaims` juga dibaca layar, bukan hanya `ClaimCollection` yang memang tidak peduli barisnya
kembar. Cabang administrator tidak berubah: ia sudah menjawab `true` untuk seluruh katalog.

### 3.4 Database

| Objek | Keadaan |
| --- | --- |
| `doc/sqlscript/mssql/views/vi_Role.sql` | **Baru**, passthrough `ta_Role` mengikuti bentuk `vi_User.sql`. Dijalankan user sendiri; sudah berdiri di database |
| `UQ_ta_Role_cRoleName` | **Dijalankan** — `RoleCollection[string]` mencari role lewat namanya, dan tanpa unique, dua role bernama sama membuat lookup itu melempar |
| `UQ_ta_UserClaim_cUserId_cUserClaimName` | **Dijalankan** — tertinggal dari fase 3 |
| `UNIQUE (cRoleId, cClaimName)` | Sudah terpenuhi `PK_ta_RoleClaim`, tidak perlu apa-apa |
| `doc/MainTable.sql` | Disinkronkan ulang lewat skill `schemaupdate` (24 tabel, 162 kolom, 38 index, 23 FK) |

Kedua tabel diperiksa kosong lebih dulu — 0 baris, 0 duplikat — jadi tidak ada baris lama yang
menghalangi constraint-nya.

---

## 4. Tiga penyimpangan dari rencana

### 4.1 `AddClaim(string claimName)` tidak jadi ada

Rencana 7.2 menuliskannya sebagai overload yang "merangkai kunci lewat `ClaimAction`". Itu tidak
bisa: `ClaimAction.Create<T>` butuh tipe service untuk tahu nama module-nya, dan nama claim saja
tidak cukup. Sempat dibuat sebagai `AddClaim<TService>(string)`, lalu **dihapus atas keputusan user**.
Yang tersisa hanya `AddClaim(ClaimAction)` — `ClaimAction` tetap satu-satunya tempat bentuk kunci
boleh dirangkai.

### 4.2 DDL bagian 4.2 dijalankan di sini, bukan diserahkan ke user

Rencana menyebut DDL sebagai milik user dan hanya minta daftarnya disampaikan. Atas permintaan user
di tengah eksekusi, kedua unique constraint dijalankan langsung lewat `sqlcmd`, didahului pemeriksaan
duplikat. `vi_Role.sql` tetap dijalankan user sendiri.

### 4.3 `Defaults.cs` sempat berganti line ending

Penyuntingan lewat script Python mengubah CRLF berkas itu jadi LF, membuat diff-nya terbaca 232 baris
untuk tambahan 8 baris. Sudah dikembalikan ke CRLF dan diverifikasi ulang — bukan bagian dari
perubahan fase ini, dicatat supaya tidak dicari-cari di riwayat.

---

## 5. Status uji (rencana 8.1)

### 5.1 Sudah terbukti

| Yang diuji | Cara | Hasil |
| --- | --- | --- |
| Model EF terbangun (temuan 2.3 butir 2) | API dijalankan, `PostGetMeta_SignIn` dipanggil dengan akun yang tidak ada | Jawabannya `401 Incorrect username or password` dari `CredentialServices.cs:928` — logika aplikasi, bukan kegagalan membangun model. Dengan `ta_UserRole` tanpa key, pemakaian `DbContext` pertama mati sebelum mencapai baris itu |
| Tidak ada nama action kembar | API start bersih | `EmApp.BuildApp` melempar kalau ada tabrakan nama; lolosnya startup membuktikan ke-30 action baru unik |
| Action baru terdaftar dan penjaganya menyala | 10 action GET di-probe satu per satu tanpa token | Semuanya `401 This action requires a signed-in caller`, bukan `404` |
| `vi_Role` cocok dengan entity | `sys.columns` dibandingkan dengan `vi_Role.cs` | 7 kolom, urutan sama, `cRoleDescription` nullable, `cRoleState` int |
| Kedua unique constraint berdiri | `sys.key_constraints` | `UQ_ta_Role_cRoleName` dan `UQ_ta_UserClaim_cUserId_cUserClaimName` terdaftar |
| Kedua solution build | `dotnet build` | Hijau, 0 warning |

### 5.2 Belum teruji — perlu WPF menyala

Semuanya butuh user non-admin yang benar-benar bisa login. Token debug perlu private key yang ada di
mesin pengembang, dan sign in `admin/admin` memang dimatikan, jadi tidak ada jalan mengujinya dari
sisi server saja:

- Buat role, isi dua claim, berikan ke user non-admin, login sebagai user itu → tombol yang dijaga
  claim tersebut menyala tanpa claim langsung diberikan.
- `cRoleState = Disabled`, login ulang → tombolnya mati lagi.
- Penugasan dengan `cUserRoleStart` besok → haknya belum berlaku hari ini.
- Penugasan dengan `cUserRoleExpiry` kemarin → haknya sudah tidak berlaku.
- Hapus role yang masih punya member → berhasil, barisnya hilang dari `ta_UserRole` dan `ta_RoleClaim`.
- Beri satu claim langsung lewat jalur fase 3 → insert berhasil (pembuktian temuan 2.3 butir 3).
- Satu claim yang dimiliki langsung **dan** lewat role → muncul sekali di `AvailableClaims`.

---

## 6. Issue yang dihasilkan eksekusi ini

Bukan bug yang menghalangi, tapi utang yang lahir dari cara fase ini dikerjakan. Dicatat di sini
karena tidak ada satu pun yang disebut rencana.

### 6.1 `Role` yang dibaca sendirian tidak punya angka

`MemberCount` dan `ClaimCount` diisi dari luar oleh `RoleCollection`, sesuai rencana 7.2. Akibatnya
`Role` yang datang lewat `GetRole_ByIdAsync` atau `DuplicateAsync` punya kedua angka itu **0 tanpa
memberi tahu siapa pun** — bukan "belum dimuat", melainkan nol yang terlihat sah. Layar yang
menampilkan satu role di luar daftar akan menampilkan angka yang salah.

Pilihannya: action penghitung untuk satu role, atau menjadikan keduanya nullable supaya "belum tahu"
bisa dibedakan dari "memang nol".

### 6.2 `GetMeta_RoleCounters` selalu menghitung seluruh server

`RoleCollection.ReloadAsync` memuat satu halaman role, lalu meminta angka untuk **semua** role yang
ada. Untuk puluhan role ini lebih murah daripada satu request per baris — itu memang alasannya ada —
tapi ia tidak ikut mengecil bersama halaman. Kalau role tumbuh jadi ribuan, satu halaman berisi 50
baris tetap menyeret seluruh tabel.

### 6.3 `Role.SetMemberPeriod` membaca seluruh penugasan role untuk satu baris

Tidak ada action yang mengambil satu baris `ta_UserRole` lewat pasangan kuncinya, jadi method itu
memanggil `GetTa_UserRoles_ByRoleId` lalu mencari sendiri di client. Baris lamanya memang harus
dibaca — `datestamp`-nya menyimpan kapan penugasan dibuat, dan menulis baris baru di atasnya akan
menggantinya dengan sekarang — tapi membaca seluruh anggota untuk mengubah periode satu orang adalah
harga yang tidak perlu. Obatnya satu action `GetTa_UserRole_ById(cUserId, cRoleId)`.

### 6.4 `Role.DuplicateAsync` tidak transaksional

Role barunya lahir lewat satu request, salinan claim-nya lewat request kedua. Kalau yang kedua gagal,
yang tertinggal adalah role kosong bernama sesuai permintaan — dan tidak ada yang membersihkannya.
Bisa diperbaiki dengan action khusus di server yang melakukan keduanya dalam satu transaksi.

### 6.5 `RoleCollection.RemoveAsync` menaksir `TotalAssignments`

Angka penugasan dikurangi sebanyak `MemberCount` role yang barusan hilang, bukan dibaca ulang. Kalau
ada yang menambah anggota dari sesi lain sesudah halaman ini dimuat, angkanya melenceng sampai
`ReloadAsync` berikutnya. Disengaja — membaca ulang seluruh halaman untuk dua penghitung toolbar
mahal — tapi tetap sebuah taksiran.

### 6.6 `GetRoles_PageCountAsync` mengembalikan jumlah baris, bukan jumlah halaman

Namanya menjanjikan jumlah halaman, isinya `GetTa_Roles_Count()`. Ini menyalin
`User.GetUsers_PageCountAsync` yang persis sama kelirunya, jadi bukan cacat baru — tapi sekarang ada
dua, dan memperbaikinya berarti menyentuh keduanya.

### 6.7 `ICredentialServices` sekarang 117 baris, implementasinya 1318

Rencana 3.4 memutuskan action role menumpang di sini, mengikuti stub yang sudah ditulis. Argumen
memisahkannya jadi `IRoleServices` tetap berlaku dan **makin kuat sesudah fase ini**: otorisasi bukan
autentikasi, dan berkas implementasinya sudah 1318 baris. Memindahkannya masih murah selama belum ada
module lain yang memanggilnya.

---

## 7. Yang belum dikerjakan

### 7.1 Sengaja ditunda oleh rencana (bagian 10)

- **Penegakan hak di server.** Warisan fase 3 yang masih terbuka: role yang dibuat di sini tetap
  hanya menyalakan dan mematikan tombol. Server belum menolak pemanggil berdasarkan claim-nya —
  `RequireAdmin()` dan `RequireSelfOrAdmin()` yang ada memeriksa status admin, bukan claim.
- **Layar `RoleManager`** — view model, binding, dan command. Fase ini berhenti tepat di bawahnya;
  `Role` dan `RoleCollection` adalah yang akan dipegang view model itu.
- **Export dan lembar katalog claim.** `GetMeta_ClaimUsage` sudah lengkap di ketiga lapisan —
  kontrak, implementasi server, transport client — tapi **belum ada satu pun yang memanggilnya**.
  Ia disiapkan untuk pengisi *Carried by N roles* di lembar katalog, dan lembar itu belum ada.
- **Role bersarang** (role mewarisi role lain) dan **role bawaan yang tidak bisa dihapus**. Keduanya
  belum pernah diminta.

### 7.2 Keputusan yang masih terbuka (rencana bagian 9)

- **Kolom periode `ta_UserClaim` sebaiknya nullable.** `NOT NULL` memaksa lahirnya sentinel
  `Defaults.NoExpiry`, sementara `ta_UserRole` — tabel bersaudara dengan arti yang sama — memakai
  `NULL` untuk hal yang persis sama. Menyamakan keduanya menghapus sentinel itu. DDL-nya milik user.
- **Penamaan `ta_RoleClaim.cClaimName`.** Konvensi meminta `c` + nama tabel + nama kolom, jadi
  semestinya `cRoleClaimName`. Tabel itu juga tidak memuat kolom standar — pengecualian yang sama
  seperti `ta_UserClaim`, karena barisnya hanya pernah di-insert dan di-delete. Fase ini memakai nama
  yang ada apa adanya.
- **"Last changed by" belum punya sumber.** Layar acuan menampilkan nama pengubah terakhir, tapi
  `ta_Role` hanya menyimpan `ustamp` berupa waktu. Pilihannya kolom baru atau `json_object`.
- **`RoleState` belum punya nilai negatif.** Konvensi enum `*State` memakai nilai negatif untuk
  "tidak dipakai/dihapus". Selama penghapusan role masih penghapusan sungguhan, nilai itu belum
  dibutuhkan — kalau nanti berubah jadi soft delete, `Deleted = -1` yang pertama ditambahkan.
- **Penyegaran hak tanpa login ulang.** Bertambah alasannya sesudah fase ini: role bisa dicabut dari
  seseorang yang sedang bekerja, dan sesinya tidak akan tahu sampai dia keluar.

---

## 8. Daftar berkas

### Baru

```
doc/sqlscript/mssql/views/vi_Role.sql                   24 baris
src/shared/Em.Libs/Shared/RoleState.cs                     8
src/shared/Em.Libs/Shared/RoleCounter.cs                  19
src/shared/Em.Libs/Shared/ClaimUsage.cs                   15
src/shared/Em.Libs/Api.Core.Models/ta_Role.cs             19   (sebelumnya untracked, diperbaiki)
src/shared/Em.Libs/Api.Core.Models/ta_RoleClaim.cs        10   (sebelumnya untracked, tidak berubah)
src/shared/Em.Libs/Api.Core.Models/ta_UserRole.cs         18   (sebelumnya untracked, diberi komentar)
src/shared/Em.Libs/Api.Core.Models/vi_Role.cs              8   (sebelumnya untracked, tidak berubah)
src/shared/Em.Ui.Core/Api.Core.Models/Role.cs            310   (sebelumnya untracked, stub diisi)
src/shared/Em.Ui.Core/Api.Core.Models/RoleCollection.cs  190   (sebelumnya untracked, stub diisi)
```

### Tersunting

```
src/backend/Em.Api.Core/Api/Core/CredentialServices.cs        +523 / −5
src/shared/Em.Ui.Wpf.Core/Api.Core/CredentialService.cs        +99 / −0
src/shared/Em.Libs/Api.Core.Models/ICredentialServices.cs      +51 / −1
src/shared/Em.Ui.Core/ModelExtensions.cs                       +21 / −0
doc/MainTable.sql                                                +18 / −0
src/shared/Em.Ui.Core/Api.Core.Models/User.cs                  +12 / −1
src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs                      +11 / −1
src/backend/Em.Api.Core/Api/Core/Models/ApiCoreContext.cs       +8 / −1
src/shared/Em.Libs/Defaults.cs                                  +8 / −0
src/shared/Em.Libs/Shared/AddressState.cs                       +2 / −2
```

`ta_UserClaim.cs` dan `doc/sqlscript/.idea/data_source_mapping.xml` juga muncul di `git status`,
tapi keduanya perubahan yang sudah ada di working tree sebelum eksekusi ini.

### Dipindah

```
plan/unexecuted/authorization-fase4-sistem-role.md
   -> plan/executed/authorization-fase4-sistem-role.md   (status di dalamnya diperbarui)
```
