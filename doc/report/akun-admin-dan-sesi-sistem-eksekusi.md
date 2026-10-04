# Eksekusi Akun Admin dan Sesi Akun Sistem

Status: **selesai dieksekusi, belum di-commit**
Dibuat: 2026-09-13
Baseline: commit `b46b179` (Perbarui aturan sesi dan refresh token), branch `data-services`
Plan sumber: `plan/executed/akun-admin-dan-sesi-sistem.md`
Lanjutan dari: `doc/report/authorization-fase1-eksekusi.md`
Build: `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` sama-sama hijau, tanpa warning
Runtime: server dijalankan sungguhan, jalur login dan refresh akun admin diuji lewat HTTP

---

## 1. Ringkasan

Fase 1 meninggalkan akun admin di luar semua jalur yang dibangunnya: akun itu tidak punya baris di
`ta_User`, sementara hampir setiap penjaga yang ditulis fase 1 membaca baris tersebut. Berkas ini
menutup lubang itu — dan sekaligus memperbaiki backend yang **tidak kompilasi**, karena `Program.cs`
sudah memanggil `builder.SessionTokenRetentionHour` yang propertinya belum pernah ditulis.

Tiga hal yang berubah secara struktural:

1. **Sesi tidak lagi satu tabel.** Akun sistem menyimpan sesinya di `ta_SystemSession`, tabel tanpa
   foreign key ke mana pun. Yang memilih di antara keduanya cuma satu method di `TokenServices`,
   bukan percabangan yang tersebar.
2. **Akun admin hidup di metadata, bukan di tabel pengguna.** Password, password bawaan, dan
   saklar akun semuanya baris metadata. Saklarnya hanya bisa digerakkan langsung di database.
3. **Sesi mati akhirnya dibereskan.** Sebelumnya tidak ada satu pun yang menghapus baris sesi, dan
   sesi yang kedaluwarsa tanpa pernah dipakai refresh selamanya tertulis `Active`.

Angka perubahan: 5 berkas baru, 7 berkas tersunting, 1 tabel baru di database.

Yang **belum** ada, dan sama seperti fase 1 memang sengaja: gerbang token di `ProcessRequest`.
Konsekuensinya dirinci di bagian 4.

---

## 2. Apa yang berubah

### 2.1 Database

Tabel baru `ta_SystemSession` — bentuknya `ta_UserSession` dikurangi foreign key, ditambah kolom
pemilik yang **sengaja tidak ber-FK**:

```sql
CREATE TABLE [dbo].[ta_SystemSession] (
  [cSystemSessionId]        char(26)      NOT NULL,
  [cSystemSessionAccountId] char(26)      NOT NULL,
  [cSystemSessionHash]      varchar(255)  NOT NULL,
  [cSystemSessionState]     int           NOT NULL,
  [cSystemSessionExpiry]    datetime      NOT NULL,
  [ustamp]                  datetime      NOT NULL,
  [datestamp]               datetime      NOT NULL,
  [json_object]             nvarchar(max) NULL
)
-- PK_ta_SystemSession (cSystemSessionId)
-- UQ_ta_SystemSession_Hash unique nonclustered (cSystemSessionHash)
-- IX_ta_SystemSession_cSystemSessionAccountId
```

Kolom pemilik tanpa FK itu yang membuat akun sistem berikutnya — debugger, dan seterusnya — ikut
tertampung tanpa perlu tabel ketiga.

`doc/MainTable.sql` disinkronkan lewat skill `schemaupdate` **setelah** tabelnya ada di
database. Diff-nya 60 baris tambahan, nol penghapusan — 21 tabel, 145 kolom, 33 index, 20 foreign
key.

### 2.2 Kontrak bersama (`src/shared/Em.Libs`)

| Berkas | Perubahan |
| --- | --- |
| `Defaults.cs` | Tambah `AdminUserAccount = "admin"` dan `AdminUserFullName = "System Administrator"`. Dua-duanya dipakai kedua sisi: server mengenalinya saat login, client menampilkannya. Tidak ada baris pengguna yang menyimpan keduanya, jadi tidak ada tempat lain untuk menaruhnya. |

**Nama key metadata tetap di backend.** `AdminPassword`, `DefaultAdminFirstPassword`, dan
`AdminUserEnable` semuanya `private const` di `AdminAccount` — client tidak perlu tahu di key mana
apa disimpan, dan yang tahu namanya tinggal selangkah dari memintanya.

### 2.3 Backend — berkas baru

| Berkas | Isi |
| --- | --- |
| `Api/Core/Models/ta_SystemSession.cs` (35 baris) | Entity `internal`. **Sengaja di backend, bukan di `Em.Libs`** seperti `ta_UserSession`: barisnya tidak pernah dikirim apa adanya ke client — selalu dipetakan dulu — jadi bentuk internalnya tidak perlu ikut jadi bagian kontrak publik. |
| `Api/Core/ISessionStore.cs` (61 baris) | `SessionRecord` + kontrak `ISessionStore`. |
| `Api/Core/SessionStores.cs` (189 baris) | `UserSessionStore` dan `SystemSessionStore`. |
| `Api/Core/AdminAccount.cs` (134 baris) | Saklar, password, dan penyemaian akun admin. |

**`ISessionStore`** — satu kontrak, dua implementasi, supaya `TokenServices` tidak penuh cabang
`if (cUserId == Defaults.AdminUserId)` di lima tempat:

```csharp
Task AddAsync(sessionId, accountId, hash, expiry);
Task<SessionRecord?> FindByHashAsync(hash);
Task<bool> SetStateAsync(sessionId, state);
Task RotateAsync(oldSessionId, newSessionId, accountId, hash, expiry);
Task RevokeAllAsync(accountId);
Task<SessionRecord[]> ListByAccountAsync(accountId);
Task PurgeAsync(retention);
```

Dua penyimpangan dari sketsa kontrak di plan, keduanya disengaja:

- **`RotateAsync` ditambahkan.** Plan mendaftar `SetStateAsync` + `AddAsync` sebagai cara merotasi.
  Dipakai begitu, rotasi jadi dua kali `SaveChanges` — dan kode sebelum fase ini melakukannya dalam
  satu transaksi. Kalau pencabutan berhasil tapi penggantinya gagal, pemegang token kehilangan
  sesinya tanpa sebab; kalau urutannya dibalik, refresh token lama sempat hidup berdampingan dengan
  yang baru. `RotateAsync` mengembalikan sifat atomik itu.
- **`SetStateAsync` mengembalikan `bool`.** `PostMeta_SignOut` hanya memegang id sesi, dan id sesi
  tidak menyebutkan tabel asalnya. Nilai baliknya yang membedakan "sesi milik orang lain" dari "sesi
  milik store sebelah".

**`SessionRecord` tidak membawa hash refresh token.** Yang membutuhkannya cuma pencarian baris, dan
itu urusan store. Ini sekaligus menutup temuan (e) report fase 1 — lihat bagian 3.3.

**`AdminAccount`** mengumpulkan segala hal soal akun admin di satu tempat karena ketiga pemakainya
masuk lewat pintu yang berbeda-beda: penerbit token (`TokenServices`), action kredensial
(`CredentialServices`), dan startup aplikasi (`EmApp.Run`). Ia bekerja langsung di atas
`ApiCoreContext`, bukan lewat `ServicesBase.SetMetaValue`, justru karena pemakai ketiga itu berjalan
di luar request dan tidak punya `App` untuk dipinjam.

| Anggota | Kelakuan |
| --- | --- |
| `IsAdminAccount(cUserAccount)` | `OrdinalIgnoreCase`, sama seperti nama akun pengguna biasa |
| `IsEnabledAsync(ctx)` | Key hilang atau bukan boolean → `false`. Aman secara bawaan. |
| `IsPasswordValidAsync(ctx, hasher, password)` | Argon2 langsung terhadap nilai tersimpan |
| `SetPasswordAsync(ctx, hasher, newPassword)` | Menyimpan hash; menolak password kosong |
| `SeedAsync(ctx, hasher, firstTimePassword)` | Tiga key, masing-masing hanya kalau belum ada |

**`AdminPassword` tidak boleh kosong.** Key-nya boleh dihapus orang; isinya tidak. Kalau ditemukan
kosong saat login, ia diisi ulang dari `DefaultAdminFirstPassword` di tempat, bukan dipakai apa
adanya — hash kosong cocok dengan apa pun yang tidak, dan itu mengunci akunnya tanpa jalan keluar
selain menyunting database.

### 2.4 Backend — berkas tersunting

**`TokenServices`** (256 baris):

- Memegang dua store, memilih di satu tempat (`StoreFor`). `IsSystemAccount` mencakup
  `Defaults.AdminUserId` dan `Defaults.DebuggerUserId`.
- **Claim `adm` dipaksa `true` untuk id admin.** Tidak ada baris untuk membaca flag itu, dan akun
  ini tidak ada gunanya selain jadi administrator.
- **`RefreshAsync` membaca ulang saklar** untuk sesi milik akun admin, lalu mencabut sesinya kalau
  saklarnya mati. Akibatnya mematikan akun lewat database berlaku paling lama satu umur access
  token (15 menit), bukan satu umur sesi (30 hari).
- **`RevokeAsync` mencari di dua store**, karena sign-out cuma punya id sesi.
- **`ListSessionsAsync` baru di `ITokenServices`** — supaya `PostGetMeta_GetSessions` tidak perlu
  tahu tabel mana yang dipakai akun mana.

**Pembersihan sesi mati, dua tahap** — alasan tabel ini dipilih ketimbang penyimpanan di memory
adalah jejaknya, jadi baris tidak dibuang tepat saat kedaluwarsa:

1. **Tandai** — baris `Active` yang expiry-nya sudah lewat jadi `Expired`. Ini memperbaiki cacat
   yang sudah ada: satu-satunya yang pernah mengubah status adalah `RefreshAsync`, jadi sesi yang
   kedaluwarsa tanpa pernah dipakai refresh lagi selamanya tertulis `Active`.
2. **Buang** — baris yang expiry-nya lewat lebih lama dari `SessionTokenRetentionHour` dihapus.

Pemicunya hanya saat sesi dibuat (sign-in dan refresh), **bukan** saat validasi token — validasi
jalan di setiap request. Ditambah throttle di memory lewat `Interlocked.CompareExchange`: maksimal
sekali per 10 menit per proses, dan yang kalah bertukar cukup jalan terus. Keduanya disapu
sekaligus, `ta_UserSession` maupun `ta_SystemSession`, supaya tabel yang jarang dipakai tidak jadi
tabel yang tidak pernah dibereskan.

**`CredentialServices`:**

| Bagian | Perubahan |
| --- | --- |
| `PostGetMeta_SignIn` | Cabang akun admin di paling depan, tidak menyentuh `ta_User` sama sekali |
| `PostTa_User_New` / `PostTa_User_NewBatch` | `EnsureAccountNameIsNotReserved` — nama `admin` ditolak di server, bukan cuma di `UserEditor` |
| `PostGetMeta_GetSessions` | Lewat `Tokens.ListSessionsAsync`, kedua jenis sesi dipetakan ke bentuk `ta_UserSession` yang sama |
| `PostMeta_ResetAdminPassword` | **Baru.** Kembaran `PostMeta_ResetPassword`; langsung menetapkan password baru |
| `PostMeta_ChangeAdminPassword` | **Baru.** Kembaran `PostMeta_ChangeMyPassword`; password lama ditanyakan, salah → **400**, bukan 401 |
| `RequireSelfOrAdminAsync` | Akun admin lolos tanpa baris — sebelumnya `RequireUserAsync` akan menjawab 404 untuknya |

Hak akses kedua action admin dijaga `RequireAdminAccountAccessAsync`, yang menanyakan tiga hal:
pemanggil sudah login, pemanggil administrator (akun `admin` sendiri termasuk, tanpa baris yang
mengatakannya), dan `AdminUserEnable = true`. Yang terakhir itu satu-satunya yang tidak bisa
diakali pemanggil mana pun — saklarnya hanya bergerak dari database.

Keduanya mencabut seluruh sesi akun admin setelah password berganti.

**Pesan yang sama untuk saklar mati dan password salah.** Login akun admin saat saklarnya mati
dijawab `InvalidCredentialsMessage` yang sama persis dengan password salah. Pesan yang berbeda akan
membuat layar login jadi alat untuk mengetahui bahwa akun admin ada tapi sedang dimatikan — yang
justru ingin disembunyikan dengan mematikannya.

**`EmAppBuilder` / `EmApp`:**

- `SessionTokenRetentionHour` (`public`, default `24 * 30`) → `EmApp.SessionTokenRetentionHour`.
  Ini yang membuat backend kompilasi lagi.
- `FirstTimeAdminPassword` sekarang benar-benar dibaca — sebelumnya propertinya ada tapi tidak ada
  satu pun yang memakainya. Disimpan di field `private` di `EmApp`: ini teks polos sebuah
  password, dan tidak ada pemanggil di luar Engine yang perlu membacanya.
- `SeedCoreMetadata()` dipanggil di `Run()` sebelum `app.Run()`, lewat `CreateScope()` karena
  `ApiCoreContext` scoped dan di luar request tidak ada scope untuk dipinjam. Blocking, disengaja —
  tidak boleh ada request yang dilayani sebelum ini selesai.

**`ApiCoreContext`:** tambah `internal DbSet<ta_SystemSession>` **plus** `OnModelCreating` yang
menyebut entitasnya eksplisit. EF hanya menemukan entity dari property `DbSet` yang publik; tanpa
baris itu modelnya tidak akan mengenal tipe ini sama sekali.

### 2.5 Client (`src/shared/Em.Ui.Wpf.Core`)

| Berkas | Perubahan |
| --- | --- |
| `Api.Core/CredentialService.cs` | Dua action admin ditambahkan di region `Meta's` |

Tidak ada perubahan UI di fase ini — itu ditunda, lihat bagian 4.2.

---

## 3. Verifikasi

### 3.1 Build

Kedua solution hijau, nol warning. Backend yang sebelum fase ini tidak kompilasi sekarang kompilasi.

### 3.2 Runtime — diuji sungguhan lewat HTTP

Berbeda dari fase 1, kali ini servernya dijalankan dan jalurnya dipanggil betulan:

| Uji | Hasil |
| --- | --- |
| Startup pertama | Ketiga key tersemai: `AdminPassword` (hash Argon2, 97 karakter), `DefaultAdminFirstPassword`, `AdminUserEnable=False` |
| Saklar mati, password benar | `401 Incorrect username or password.` — identik dengan salah password |
| Saklar hidup, password salah | `401 Incorrect username or password.` |
| Saklar hidup, password benar, nama diketik `ADMIN` | `200`, `cUserId=000…0`, `ExpiresIn=900`, access token 558 karakter |
| Baris sesi | `ta_SystemSession` terisi, state `Active`, pemilik id admin |
| Refresh, saklar hidup | `200`; rotasi jalan — baris lama `Revoked`, baris baru `Active` |
| Refresh, saklar baru dimatikan | `401`, dan sesinya ikut dicabut jadi `Revoked` |

Baris uji dihapus sesudahnya dan `AdminUserEnable` dikembalikan ke `False`.

### 3.3 Yang ikut terbukti dari report fase 1

**Temuan (e) — `PostGetMeta_GetSessions` mengirim kolom hash ke client — tertutup.** Bukan dengan
DTO baru: `SessionRecord` memang tidak punya kolom itu, jadi `ta_UserSession` yang dipetakan keluar
selalu ber-`cUserSessionHash` kosong. Jebakan yang disebut report fase 1 — baris yang masih dilacak
change tracker akan menulis hash kosong itu balik ke database — tidak berlaku di sini, karena yang
dikembalikan objek baru, bukan baris tersimpan; `ListByAccountAsync` juga membacanya
`AsNoTracking()`.

**Temuan (i) — baris sesi menumpuk tanpa pernah dibersihkan — tertutup**, lihat bagian 2.4.

**Temuan (d) — akun sistem belum bisa login — tertutup untuk akun admin** lewat tabel sesi kedua,
bukan dengan melepas foreign key `ta_UserSession`. Akun debugger sudah ikut diarahkan ke store yang
sama, tapi ia tidak pernah login ke server sama sekali, jadi belum ada yang membuktikannya.

---

## 4. Issue

### 4.1 Temuan baru — di luar plan, sudah diperbaiki

**(m) Penerbitan token kedua dalam satu proses gagal `500`.**
`Cannot access a disposed object: 'System.Security.Cryptography.RSABCrypt'`. Ini **bug yang sudah
ada sejak fase 1**, baru ketahuan sekarang karena fase 1 tidak pernah menjalankan servernya.

Sebabnya: `ValidateAsync` dan `BuildTokenResultAsync` sama-sama memakai pola
`using var rsa = key.CreateRsa()`, sementara cache `CryptoProviderFactory` milik
`Microsoft.IdentityModel` di-key oleh **materi key**, bukan instance. Panggilan berikutnya —
memegang materi yang sama dalam instance baru — dapat kembali provider yang masih menunjuk RSA yang
sudah di-dispose panggilan sebelumnya. Praktisnya: login pertama berhasil, refresh sesudahnya selalu
`500`.

Saya perbaiki karena ini memblokir butir 8 plan ini sendiri (pemeriksaan saklar di `RefreshAsync`
tidak bisa dibuktikan kalau refresh-nya tidak pernah berhasil). Perbaikannya lokal: helper
`SigningKeyFor(RSA)` yang membungkus key dengan
`CryptoProviderFactory { CacheSignatureProviders = false }`, dipakai di kedua tempat.

**Catatan untuk nanti:** ini mematikan cache, bukan memakainya dengan benar. Kalau beban
penandatanganan token pernah jadi masalah, jalan yang lebih baik adalah menyimpan satu instance RSA
selama umur proses dan tidak men-dispose-nya per panggilan — tapi itu menyeret pertanyaan rotasi key
yang belum ada jawabannya di repo ini.

### 4.2 Yang sengaja ditunda

**(n) Gerbang token masih belum ada.** `CallerUserId`/`CallerSessionId` tetap selalu `null`, jadi
`PostMeta_ResetAdminPassword` dan `PostMeta_ChangeAdminPassword` — sama seperti lima action fase 1 —
akan menjawab `401` sampai gerbangnya terpasang. Logikanya tetap ditulis sekarang, persis seperti
yang dilakukan fase 1.

**(o) UI belum dikerjakan.** Objek `User` sintetis untuk `System Administrator` di client (sepadan
dengan `CreateDebuggerUser` yang sudah ada), toolbar "Admin Account Profile" di `UserEditor`, dan
penyembunyian tombolnya saat `AdminUserEnable = false`.

**(p) `AdminUserEnable` hanya lewat database.** Belum bisa lewat konfigurasi atau environment
(docker compose). Untuk sekarang itu justru fiturnya: tidak ada action API sama sekali yang
menyentuh saklar ini.

### 4.3 Temuan baru yang belum ditutup

**(q) Penyemaian dan pengisian ulang password tidak tahan balapan.** `AdminAccount.WriteAsync`
membaca lalu menulis tanpa penguncian. Dua proses yang start bersamaan, atau dua login yang
bersamaan-sama menemukan `AdminPassword` kosong, bisa saling menimpa. Praktisnya kecil — penyemaian
jalan sekali di startup, dan pengisian ulang hanya terjadi kalau ada yang menghapus key-nya — tapi
`GetServerRsaKeyAsync` di `ServicesBase` sudah punya penanganan `DbUpdateException` untuk kasus yang
sama, dan di sini belum ada.

**(r) Throttle purge bersifat per proses, bukan per deployment.** `_lastPurgeTicks` field static.
Dua instance server berarti dua sapuan. Tidak merusak — sapuannya idempoten — tapi angka "sekali per
10 menit" tidak berlaku lagi begitu ada lebih dari satu proses.

**(s) Purge yang gagal menggagalkan login.** `PurgeDeadSessionsAsync` dipanggil tanpa penangkap
exception, jadi kegagalan pembersihan merambat keluar jadi kegagalan sign-in. Argumen tandingannya:
kalau `ExecuteUpdateAsync` gagal, database sedang bermasalah dan login-nya akan gagal juga. Tapi itu
asumsi, bukan jaminan — dan `TokenServices` tidak punya `Logger` terisi (dispatcher hanya mengisinya
untuk service yang memegang action), jadi menelan exception di sini berarti menelannya diam-diam.

**(t) `SessionTokenRetentionHour` tidak divalidasi.** Nilai `0` berarti baris sesi dibuang begitu
kedaluwarsa — hilang seluruh alasan menyimpannya di tabel. Nilai negatif berarti baris dibuang
sebelum kedaluwarsa. Keduanya diterima diam-diam.

**(u) Zona waktu masih campur** — temuan (k) fase 1 belum tersentuh, dan `ta_SystemSession`
mengikuti `ta_UserSession` memakai `DateTime.UtcNow` sementara seluruh tabel lain memakai waktu
lokal lewat `App.GetDateStampAsync()`. Satu tabel baru lagi yang berbeda sendiri.

**(v) Password admin polos ada di `Program.cs`.** `builder.FirstTimeAdminPassword = "admin"` — dan
nilainya sekarang benar-benar terpakai, tersimpan apa adanya di `DefaultAdminFirstPassword`. Untuk
database development ini disengaja, tapi ia perlu pindah ke konfigurasi sebelum ada deployment
sungguhan, seiring dengan (p).

---

## 5. Next step

**Sebelum apa pun:**

1. **Pasang gerbang `IsPublicAction` di `EmApp.ProcessRequest`** dan isi `CallerUserId` /
   `CallerSessionId` dari `ValidateAsync`. Ini satu-satunya hal yang menghidupkan kedua action admin,
   lima action fase 1, dan tombol set password di `UserEditor` sekaligus. Semua yang lain di daftar
   ini menunggunya.

**Sesudah gerbangnya ada:**

2. **`ApiClient` menyimpan token dan auto-refresh saat 401** — butir 4 next step fase 1, belum
   dikerjakan. Perhatikan jebakan yang sudah dicatat di sana: sign-in dan refresh sendiri menjawab
   401 dan tidak boleh ikut di-auto-refresh.
3. **UI akun admin** — (o). Objek `User` sintetis dulu, baru toolbar-nya.
4. **Uji kedua action password admin lewat HTTP.** Keduanya sama sekali belum pernah berhasil
   dijalankan, karena keduanya butuh pemanggil yang sudah login.
5. **Uji login akun debugger ke server.** Store-nya sudah diarahkan, jalurnya belum pernah dilalui.

**Kebersihan, tidak memblokir apa pun:**

6. **(q)** Tangani `DbUpdateException` di `AdminAccount.WriteAsync`, mengikuti pola
   `GetServerRsaKeyAsync`.
7. **(t)** Validasi `SessionTokenRetentionHour` saat `BuildApp`, bukan diam-diam menerima `0` dan
   nilai negatif.
8. **(s)** Putuskan apakah purge yang gagal boleh menggagalkan login. Kalau tidak boleh,
   `TokenServices` perlu jalan ke logger lebih dulu — menelan exception tanpa mencatatnya lebih buruk
   daripada melemparnya.
9. **(v)** + **(p)** Pindahkan `FirstTimeAdminPassword` dan `AdminUserEnable` ke konfigurasi
   sebelum deployment.
10. **(u)** Samakan zona waktu — keputusan yang tertunda sejak fase 1, dan sekarang menyangkut dua
    tabel.
11. **(m)** Tinjau ulang penanganan RSA kalau penandatanganan token pernah jadi beban — cache yang
    dimatikan sekarang adalah perbaikan, bukan penyelesaian.
