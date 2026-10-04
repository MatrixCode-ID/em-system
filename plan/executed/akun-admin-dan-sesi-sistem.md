# Plan — Akun admin (`System Administrator`) dan sesi akun sistem

Status: **belum dieksekusi**
Dibuat: 2026-09-13 — hasil pembahasan desain akun admin, lanjutan dari
`plan/executed/authorization-fase1-service-layer.md`.

---

## Kenapa ada berkas ini

Fase 1 otorisasi sudah menaruh sign-in, token, sesi, dan ganti/reset password untuk user biasa.
Akun admin sengaja ditinggalkan: ia tidak punya baris di `ta_User`, jadi hampir setiap jalur yang
sudah ada tidak berlaku untuknya. Berkas ini menutup lubang itu.

Dua kontrak baru sudah lebih dulu ditulis di `ICredentialServices` —
`PostMeta_ResetAdminPassword(string newPassword)` dan
`PostMeta_ChangeAdminPassword(string oldPassword, string newPassword)` — tapi belum ada
implementasinya di kedua sisi, dan `Program.cs` sudah memanggil
`builder.SessionTokenRetentionHour` yang propertinya belum ada. **Backend saat ini tidak
kompilasi**; rencana ini sekaligus memperbaikinya.

## Keputusan desain yang sudah diambil

| Hal | Keputusan |
| --- | --- |
| Identitas akun | id `Defaults.AdminUserId` (`000…0`), nama akun `admin`, nama tampilan `System Administrator` |
| Penyimpanan password | `ta_Meta` key `AdminPassword`, berisi **hash** (Argon2, `IStringHasher`), bukan teks |
| Password default | `ta_Meta` key `DefaultAdminFirstPassword`; hanya dipakai server, tidak pernah dikirim ke client |
| Penyemai default | `builder.FirstTimeAdminPassword` mengisi `DefaultAdminFirstPassword` kalau key-nya belum ada; sesudah itu yang berlaku selalu nilai di database |
| `AdminPassword` kosong/hilang | server otomatis mengisinya kembali dari `DefaultAdminFirstPassword` — nilainya tidak boleh kosong |
| Saklar akun | `ta_Meta` key `AdminUserEnable` (`true`/`false`); kalau key belum ada, dibuat otomatis bernilai `false` |
| Cara mengubah saklar | **langsung di database**, tidak ada action API sama sekali |
| Kapan saklar dibaca | saat sign-in dan saat refresh token saja (bukan setiap request) |
| Sesi akun sistem | tabel baru `ta_SystemSession`, tanpa foreign key ke mana pun |
| Claim `adm` | dipaksa `true` untuk `Defaults.AdminUserId` |
| Daftar sesi | `PostGetMeta_GetSessions` memetakan baris `ta_SystemSession` ke bentuk `ta_UserSession` |
| Retensi sesi mati | `builder.SessionTokenRetentionHour` → `EmApp.SessionTokenRetentionHour`, satuan **jam** (nilai sekarang 90) |
| Hak akses kedua action | pemanggil sudah login, `IsAdmin = true` (termasuk akun `admin` sendiri), dan `AdminUserEnable = true` |

## Rancangan

### 1. Tabel `ta_SystemSession`

Strukturnya `ta_UserSession` minus foreign key, plus kolom pemilik yang sengaja tidak ber-FK
supaya akun sistem berikutnya (debugger, dan seterusnya) ikut tertampung tanpa tabel ketiga.

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

Model `ta_SystemSession` ditaruh di **backend** (`Em.Api.Core/Api/Core/Models/`), bukan di
`Em.Libs` seperti `ta_UserSession`: barisnya tidak pernah dikirim apa adanya ke client — selalu
dipetakan dulu — jadi bentuk internalnya tidak perlu ikut jadi bagian kontrak publik.

`doc/MainTable.sql` diperbarui lewat skill `schemaupdate` **setelah** tabelnya dibuat di
database, bukan dengan menyunting berkasnya duluan.

### 2. Session store

Satu kontrak internal, dua implementasi, supaya `TokenServices` tidak penuh cabang
`if (cUserId == Defaults.AdminUserId)` di lima tempat:

```
internal interface ISessionStore
   AddAsync(sessionId, accountId, hash, expiry)
   FindByHashAsync(hash)          -> bentuk netral (id, accountId, state, expiry)
   SetStateAsync(sessionId, state)
   RevokeAllAsync(accountId)
   ListByAccountAsync(accountId)  -> dipetakan ke ta_UserSession oleh pemanggil
   PurgeAsync(retention)
```

- `UserSessionStore` — di atas `ta_UserSession`.
- `SystemSessionStore` — di atas `ta_SystemSession`.

`TokenServices` memilih store di satu tempat: id akun sistem → `SystemSessionStore`, selain itu
`UserSessionStore`.

### 3. Pembersihan sesi mati

Dua tahap, karena alasan tabel ini dipilih ketimbang penyimpanan di memory adalah jejaknya — baris
tidak dibuang tepat saat kedaluwarsa:

1. **Tandai** — baris `Active` yang expiry-nya sudah lewat diubah jadi `Expired`. Ini sekaligus
   memperbaiki cacat yang ada sekarang: sesi yang kedaluwarsa tanpa pernah dipakai refresh lagi
   selamanya tertulis `Active`, karena satu-satunya yang mengubahnya adalah `RefreshAsync`.
2. **Buang** — baris yang expiry-nya sudah lewat lebih lama dari `SessionTokenRetentionHour` dihapus.

Pemicunya hanya saat sesi dibuat (sign-in dan refresh), bukan saat validasi token — validasi jalan
di setiap request. Ditambah throttle di memory (maksimal sekali per 10 menit per proses) supaya
login beruntun tidak menyapu berulang-ulang.

### 4. Penyemaian `ta_Meta` saat startup

Di `EmApp.Run()` sebelum `app.Run()`, lewat `CreateScope()` (`ApiCoreContext` scoped). Tiga key,
masing-masing hanya kalau belum ada:

| Key | Nilai awal |
| --- | --- |
| `DefaultAdminFirstPassword` | `builder.FirstTimeAdminPassword` |
| `AdminPassword` | hash dari `DefaultAdminFirstPassword` |
| `AdminUserEnable` | `false` |

`AdminPassword` juga diisi ulang dengan cara yang sama kalau nilainya ditemukan kosong saat
sign-in — key-nya boleh dihapus orang, isinya tidak boleh kosong.

### 5. Sign-in akun admin

`PostGetMeta_SignIn` mendapat cabang di paling depan: kalau `cUserAccount` sama dengan nama akun
admin, jalurnya tidak menyentuh `ta_User` sama sekali.

1. `AdminUserEnable` harus `true` — kalau tidak, dijawab `InvalidCredentialsMessage` yang sama
   persis dengan salah password. Pesan yang berbeda akan membuat layar login jadi alat untuk
   mengetahui bahwa akun admin ada tapi sedang dimatikan.
2. Password dicocokkan langsung dengan `IStringHasher` terhadap nilai `AdminPassword`.
   `PasswordCredential` tidak dipakai: ia bekerja di atas `ta_User` + `ta_UserCredential` yang
   tidak dimiliki akun ini.
3. Token diterbitkan lewat `TokenServices.IssueAsync`, yang menulis sesinya ke `ta_SystemSession`.

`RefreshAsync` ikut memeriksa `AdminUserEnable` untuk sesi milik akun admin, supaya mematikan
saklar lewat database berlaku paling lama satu umur access token (15 menit), bukan satu umur sesi.

### 6. Nama akun `admin` dipesan

`ta_User` tidak boleh punya baris ber-`cUserAccount = "admin"` — kalau ada, dua akun berebut satu
nama di layar login. Penolakannya di server (`PostTa_User_New` dan `PostTa_User_NewBatch`), bukan
hanya di UserEditor.

### 7. Kedua action password admin

Sama-sama memeriksa: pemanggil sudah login, `IsAdmin = true` (akun `admin` sendiri termasuk), dan
`AdminUserEnable = true`.

- `PostMeta_ResetAdminPassword(newPassword)` — kembaran `PostMeta_ResetPassword` untuk user biasa;
  dipakai tombol reset di UI, langsung menetapkan password baru tanpa menanyakan yang lama.
- `PostMeta_ChangeAdminPassword(oldPassword, newPassword)` — kembaran `PostMeta_ChangeMyPassword`;
  password lama ditanyakan, dan `400` (bukan `401`) kalau salah, mengikuti alasan yang sudah
  ditulis di action user biasa.

Keduanya mencabut seluruh sesi akun admin setelah password berganti.

## Langkah eksekusi

1. `EmAppBuilder.SessionTokenRetentionHour` + `EmApp.SessionTokenRetentionHour` — ini yang
   membuat backend kompilasi lagi.
2. Buat tabel `ta_SystemSession` di database, lalu perbarui `doc/MainTable.sql` lewat
   skill `schemaupdate`.
3. Model `ta_SystemSession` + `DbSet` di `ApiCoreContext`.
4. Konstanta di `Em.Libs/Defaults.cs`: nama akun `admin` dan nama tampilan `System Administrator`
   (dipakai dua sisi). Nama key `ta_Meta` tetap di backend — client tidak perlu tahu.
5. `ISessionStore` + dua implementasinya; `TokenServices` dialihkan memakainya, claim `adm`
   dipaksa `true` untuk id admin.
6. Purge dua tahap + throttle.
7. Penyemaian tiga key `ta_Meta` di `Run()`.
8. Cabang sign-in admin + pemeriksaan `AdminUserEnable` di `RefreshAsync`.
9. Reservasi nama akun `admin` di kedua action penulisan user.
10. Implementasi `PostMeta_ResetAdminPassword` dan `PostMeta_ChangeAdminPassword` di backend,
    lalu di client (`Em.Ui.Wpf.Core/Api.Core/CredentialService.cs`).
11. `PostGetMeta_GetSessions` memetakan baris sistem ke `ta_UserSession`.
12. Build kedua solution.

## Yang sengaja ditunda

- **Gerbang token belum ada.** `CallerUserId`/`CallerSessionId` masih selalu `null`, jadi setiap
  action yang butuh tahu siapa pemanggilnya — termasuk dua action admin di atas — akan menjawab
  `401` sampai gerbangnya terpasang. Logikanya tetap ditulis sekarang, sama seperti action user
  biasa yang sudah ada.
- **UI.** Objek `User` sintetis untuk `System Administrator` di client (sepadan dengan
  `CreateDebuggerUser`), toolbar "Admin Account Profile" di UserEditor, dan penyembunyian tombolnya
  saat `AdminUserEnable = false` dikerjakan setelah lapisan service ini jalan.
- **`AdminUserEnable` lewat konfigurasi.** Untuk sekarang hanya lewat database. Nanti bisa naik jadi
  parameter builder yang dibaca dari environment (docker compose).
- **Pembersihan `ta_UserSession` milik user biasa** ikut lewat store yang sama, tapi belum ada
  penjadwal tersendiri di luar pemicu sign-in/refresh.
