# Eksekusi Authorization Fase 1 — lapisan service kredensial

Status: **selesai dieksekusi, belum di-commit**
Dibuat: 2026-09-13
Baseline: commit `b46b179` (Perbarui aturan sesi dan refresh token), branch `data-services`
Plan sumber: `plan/executed/authorization-fase1-service-layer.md`
Diperbarui: 2026-09-13 — temuan (f) diperbaiki (bagian 2.5), temuan (e) diturunkan setelah
ditinjau ulang
Build: `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` sama-sama hijau, tanpa warning

---

## 1. Ringkasan

Fase ini menutup satu lubang: **verifikasi password yang terjadi di sisi client**. Sebelumnya
`LoginControl` menarik baris kredensial milik user — berikut hash Argon2-nya — lewat
`GetTa_UserCredential_ByType`, lalu membandingkannya di dalam proses WPF. Karena `IsPublicAction`
belum ditegakkan di mana pun, siapa pun yang bisa menyentuh API bisa menarik seluruh tabel
kredensial dan membruteforce hash-nya offline.

Sekarang jalur itu tidak ada lagi. Baris kredensial tidak punya URL sama sekali, dan satu-satunya
cara memeriksa password adalah menyerahkannya ke server lewat `PostGetMeta_SignIn`.

Yang **belum** ada, dan memang sengaja: gerbang yang memeriksa token di `ProcessRequest`. Tanpa itu
`IsPublicAction` masih belum menegakkan apa pun, dan `CallerUserId` masih kosong. Konsekuensinya
dirinci di bagian 3.

Angka perubahan: 23 berkas tersunting, 5 berkas baru, 1 berkas dihapus, 4 berkas dipindah dari
`Em.Ui.Core` ke `Em.Api.Core`.

---

## 2. Apa yang berubah

### 2.1 Kontrak bersama (`src/shared/Em.Libs`)

| Berkas | Perubahan |
| --- | --- |
| `Api.Core.Models/ICredentialServices.cs` | Region `ta_UserCredential` dan `vi_UserCredential` **dihapus seluruhnya**. Region `Meta's` jadi tujuh action final; `PostGetMeta_GetSessions` kini `Task<ta_UserSession[]>`, bukan `Task`. |
| `Api.Core.Models/ta_UserSession.cs` | **Baru.** Entity polos tanpa navigation property, mengikuti bentuk `ta_UserCredential`. |
| `Shared/SessionState.cs` | **Baru.** `Deleted = -3`, `Revoked = -2`, `Expired = -1`, `Active = 1` — mengikuti konvensi enum `*State` yang berlaku (negatif = tidak terpakai). |
| `Shared/TokenResult.cs` | Tambah `cUserId`, supaya client tahu sesi yang baru terbit milik siapa tanpa perlu membongkar isi access token. |
| `Defaults.cs` | Tambah `PasswordCredentialType = "PASSWORD"`. |

Bentuk akhir region `Meta's`:

```csharp
Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password);
Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken);
Task PostMeta_SignOut();
Task PostMeta_SignOutAll();
Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword);
Task PostMeta_ResetPassword(string cUserId, string newPassword);
Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId);
```

### 2.2 Backend (`src/backend/Em.Api.Core`)

**Empat class pindah dari client ke server** (`git mv`, jadi riwayatnya utuh):
`CredentialProvider.cs`, `PasswordCredential.cs`, `ICredentialEnrollment.cs`,
`ICredentialProviderFactory.cs`. `CredentialProviderBase.User` yang dulu bertipe `User` (UiModel)
sekarang `ta_User`, dan resolusi DI-nya lewat `ServicesBase` alih-alih `User.App.ServiceProvider`.
`CredentialProviderBase` juga memegang `CredentialServices` langsung, karena di server itulah yang
memegang DbContext milik request.

**`CredentialServices`:**

- Seluruh CRUD `ta_UserCredential` jadi `internal` **tanpa atribut action**. `EmAppBuilder` hanya
  mendaftarkan method yang beratribut, jadi method-method ini tidak punya URL — tapi tetap bisa
  dipakai `PasswordCredential` di dalam assembly yang sama.
- Region `vi_UserCredential` **dihapus dari backend juga**, bukan cuma dari kontrak. View itu
  memuat kolom rahasia yang sama dan tidak ada lagi yang membacanya.
- Region `Meta's` diimplementasikan, ditaruh di bawah region `Views` sesuai konvensi:

| Action | Atribut | Penjaga di dalamnya |
| --- | --- | --- |
| `PostGetMeta_SignIn` | `[PostAction(IsPublicAction = true)]` | `cUserState >= Inactive`, lalu Argon2 lewat `PasswordCredential` |
| `PostGetMeta_RefreshToken` | `[PostAction(IsPublicAction = true)]` | refresh token itu sendiri buktinya |
| `PostMeta_SignOut` | `[PostAction]` | `CallerSessionId` |
| `PostMeta_SignOutAll` | `[PostAction]` | `CallerUserId` |
| `PostMeta_ChangeMyPassword` | `[PostAction]` | password lama wajib cocok |
| `PostMeta_ResetPassword` | `[PostAction]` | `RequireSelfOrAdminAsync` |
| `PostGetMeta_GetSessions` | `[PostAction]` | `RequireSelfOrAdminAsync` |

**`ITokenServices` / `TokenServices` (baru, 286 baris):**

- RS256 memakai RSA key server yang sudah ada di metadata lewat `GetServerRsaKeyAsync()` — tidak ada
  key atau sertifikat baru. `TokenServices` menurunkan `ServicesBase` **hanya** untuk itu; ia tetap
  bukan `IServices`, tidak ber-`[Module]`, dan tidak punya satu pun action.
- Access token berumur 15 menit, isinya cuma `sub` (cUserId), `sid` (cUserSessionId), `adm`
  (cUserIsAdmin), `exp`. Tidak ada data user di dalamnya.
- Refresh token 32 byte acak, dikirim Base64Url, yang **disimpan hanya SHA-256-nya** di
  `cUserSessionHash`. Dirotasi setiap dipakai: baris lama jadi `Revoked`, baris baru ditulis,
  masa berlakunya **diwarisi** dari sesi lama supaya refresh berulang tidak jadi sesi abadi.
- SHA-256, bukan Argon2, dan itu disengaja: refresh token adalah 32 byte acak buatan server, bukan
  pilihan manusia — tidak ada ruang tebakan yang bisa diperlambat hash lambat, sementara yang
  dibutuhkan justru hash deterministik supaya barisnya bisa dicari.
- Umur refresh token 30 hari. **Nilai ini saya tetapkan sendiri — plan tidak menyebutnya.**
- Didaftarkan lewat `Services.AddScoped<ITokenServices, TokenServices>()` biasa, bukan
  `builder.AddService<,>` — method itu mensyaratkan `[Module]` + minimal satu action dan kalau tidak
  ada hanya menulis warning lalu tidak mendaftarkan apa pun.

**`ServicesBase`:** tambah `CallerUserId` dan `CallerSessionId` (`internal set`), sejajar dengan
`App`/`HttpContext`/`Logger`. Belum diisi siapa pun di fase ini.

**`EmApp.InitInternalServices`:** `IStringHasher`→`Argon2Hashing` dan
`ICredentialProviderFactory`→`PasswordCredentialFactory` sekarang terdaftar di backend. Sebelumnya
keduanya hanya ada di DI client — konsekuensi langsung dari pemindahan di atas.

**`ApiCoreContext`:** tambah `DbSet<ta_UserSession> ta_UserSessions`.

### 2.3 Client (`src/shared/Em.Ui.Core`, `src/shared/Em.Ui.Wpf.Core`)

| Berkas | Perubahan |
| --- | --- |
| `Api.Core.Models/UserCredential.cs` | **Dihapus seluruhnya** — seluruh class ini `UiModel<vi_UserCredential, …>`. |
| `Api.Core.Models/User.cs` | Region `Credentials` dihapus (`PasswordCredential`, `LoadPasswordCredentialAsync`). Pembuatan baris kredensial kosong di `InsertAsync` tetap ada, kini menyebut `Defaults.PasswordCredentialType`. |
| `ModelExtensions.cs` | Region `For Credential` dihapus — bergantung pada `UserCredential`. |
| `Api.Core/CredentialService.cs` | Dua region kredensial dihapus, region `Meta's` ditambahkan. |
| `Core/EmApp.Statics.cs` | `AddSingleton<ICredentialProviderFactory, PasswordCredentialFactory>()` dihapus. |
| `Controls/LoginControl.xaml.cs` | `SignInCommand` memanggil `PostGetMeta_SignIn`. |
| `Controls/UserEditor.xaml.cs` | `SetPasswordCommand` dialihkan ke `PostMeta_ResetPassword`. |

### 2.4 Database

Dua index dijalankan ke `EmDb` (tabel kosong saat itu, tidak ada duplikat hash):

```sql
CREATE UNIQUE INDEX UQ_ta_UserSession_Hash ON ta_UserSession (cUserSessionHash);
CREATE INDEX IX_ta_UserSession_cUserId ON ta_UserSession (cUserId);
```

`doc/MainTable.sql` disinkronkan ulang lewat skill `schemaupdate`. Diff-nya persis dua blok
index itu, tidak ada yang lain — 20 tabel, 137 kolom, 30 index, 20 foreign key.

### 2.5 Status code per action — perbaikan temuan (f) dan (g)

Ditambahkan setelah report versi pertama, menutup (f) dan (g) sekaligus.

**Masalahnya:** `EmApp.InvokeAsync` membungkus *setiap* exception jadi
`BuildErrorActionResult(ex, 500, …)`. Jadi password salah, token kedaluwarsa, dan database yang mati
sama-sama dijawab 500. Client tidak punya apa pun untuk dipilah selain teks pesan — dan mencocokkan
teks pesan akan putus begitu kata-katanya diubah.

**`ActionException` (baru, `Em.Libs/Shared/ActionException.cs`)** membawa status HTTP-nya sendiri.
Turunan `InvalidOperationException` mengikuti pola `SystemAccountException` yang sudah ada, supaya
pemanggil lama yang menangkap exception secara umum tidak berubah perilakunya.

Dipakai dua arah, dan itu yang membuatnya cukup satu tipe:

1. **Di server** — `InvokeAsync` membaca `StatusCode`-nya kalau ada, dan tetap 500 untuk yang lain.
   Exception biasa berarti "kegagalan yang dispatcher tidak punya bacaannya", dan 500 memang jawaban
   jujur untuk itu.
2. **Di client** — `ProcessHttpResult` melempar ulang `ActionException` dengan status yang sama dari
   envelope `ActionResult`, jadi status pilihan action sampai utuh ke call site di UI.

Status yang dipakai sekarang:

| Tempat | Status | Alasan |
| --- | --- | --- |
| `PostGetMeta_SignIn` — akun tidak ada / tidak boleh masuk / password salah | 401 | semuanya satu pesan yang sama |
| `TokenServices.RefreshAsync` — token tidak dikenal / dicabut / kedaluwarsa | 401 | satu pesan; jalan keluarnya login ulang |
| `RequireCallerUserId` / `RequireCallerSessionId` | 401 | belum terbukti pemanggilnya siapa |
| `RequireSelfOrAdminAsync` — bukan admin | **403** | pemanggilnya sudah jelas siapa, dan token baru tidak akan mengubah jawabannya |
| `PostMeta_ChangeMyPassword` — password lama salah | **400** | yang keliru nilainya, bukan token-nya |
| `RequireUserAsync` — user tidak ada | 404 | |

**Aturan yang lahir dari pembagian ini, dan yang mengikat fase 2:** 401 berarti "access token Anda
tidak berlaku" dan **hanya** itu yang boleh memicu auto-refresh di `ApiClient`. Karena itu password
lama yang salah dijawab 400 dan penolakan izin dijawab 403 — kalau keduanya 401, client akan
mengejar token baru untuk permintaan yang token barunya tidak akan menolong.

**Jebakan yang tersisa untuk fase 2:** `PostGetMeta_SignIn` dan `PostGetMeta_RefreshToken` sendiri
menjawab 401. `ApiClient` tidak boleh mencoba auto-refresh atas keduanya — gagal refresh tidak bisa
diperbaiki dengan refresh lagi, dan gagal login bukan soal token sama sekali.

**`LoginControl`** sekarang menangkap `ActionException` ber-401 secara terpisah dan menampilkan
"Incorrect username or password" tanpa detail; sisanya kembali jadi "Cannot sign in right now. The
server could not be reached." berikut pesan teknisnya di tooltip. Perilaku sebelum fase 1 pulih,
kali ini tanpa membandingkan hash di sisi client.

---

## 3. Issue

### 3.1 Yang memang disengaja fase ini, tapi harus diingat

**(a) `IsPublicAction` masih belum menegakkan apa pun.** Gerbangnya sengaja tidak dipasang: kalau
dipasang sekarang, setiap action non-publik langsung menolak semua pemanggil dan tidak ada jalan
untuk login. Artinya **semua action masih terbuka hari ini**, termasuk yang bertanda `[PostAction]`
polos. Atribut yang sudah ditulis baru jadi dokumentasi niat, belum penjaga.

**(b) Lima action melempar sampai gerbangnya ada.** `CallerUserId`/`CallerSessionId` belum diisi,
jadi `PostMeta_SignOut`, `PostMeta_SignOutAll`, `PostMeta_ChangeMyPassword`, `PostMeta_ResetPassword`
dan `PostGetMeta_GetSessions` semuanya melempar `InvalidOperationException("This action requires a
signed-in caller.")`. Plan menyebut ini perilaku yang benar untuk sekarang.

**Yang ikut kena, dan ini yang terlihat user:** `UserEditor.SetPasswordCommand` — tombol "set
password" di layar user — sekarang mati. Sebelum fase ini ia jalan lewat jalur client-side yang
dihapus. Ini regresi fungsional nyata, bukan cuma teori.

**(c) Login berhasil tapi tidak ada yang membawa buktinya.** `LoginControl` memanggil
`PostGetMeta_SignIn` dan membuang `TokenResult` yang kembali (`_ = await …`). Password sudah
benar-benar diperiksa server, tapi request berikutnya tetap tidak membawa token apa pun.

**(d) Akun sistem belum bisa login.** `Defaults.AdminUserId` dan `Defaults.DebuggerUserId` tidak
punya baris di `ta_User`, sementara `FK_ta_UserSession_ta_User` mensyaratkannya — jadi `IssueAsync`
akan gagal di foreign key begitu akun admin mencoba masuk. Ditunda atas keputusan Anda; pilihannya
tetap (a) akun sistem dapat access token saja tanpa baris sesi, atau (b) lepas foreign key-nya.

### 3.2 Temuan baru — belum ada di plan

**(e) `PostGetMeta_GetSessions` mengirim kolom hash ke client.** ~~Perlu DTO sesi tanpa kolom hash.~~
**Diturunkan setelah ditinjau ulang — bukan lubang, dan mengembalikan `ta_UserSession` apa adanya
memang sejalan dengan pola yang berlaku di repo ini.**

Yang dikembalikan `ta_UserSession[]` utuh, termasuk `cUserSessionHash`. Hash itu SHA-256 dari 32 byte
acak buatan `RandomNumberGenerator` — 256 bit entropi, jadi mencari preimage-nya tidak layak
dikerjakan siapa pun. Mengirim hash-nya kembali ke server juga tidak menolong penyerang:
`RefreshAsync` meng-hash apa pun yang masuk lalu mencocokkannya, jadi menyodorkan hash berarti
mencocokkan SHA-256 dari hash itu, yang tidak ada di tabel. Admin yang membaca sesi orang lain pun
tidak mendapat kemampuan baru — ia sudah bisa me-reset password orang itu.

Yang tersisa cuma ini, dan ukurannya kecil: kolomnya tidak punya kegunaan apa pun di client, dan
keamanannya bersandar sepenuhnya pada refresh token yang tetap 32 byte acak. Kalau suatu saat ada
yang memperpendeknya, menurunkannya dari sesuatu yang bisa ditebak, atau mengganti SHA-256 dengan
yang lebih lemah, barulah kolom ini jadi masalah — dan tidak ada apa pun di kode yang memperingatkan
soal itu.

Kalau nanti mau dirapikan tanpa keluar dari pola: jangan bikin DTO, cukup kosongkan satu kolom itu
sebelum dikembalikan — **dengan `AsNoTracking()`**, karena baris yang masih dilacak change tracker
akan menulis hash kosong itu balik ke database begitu ada `SaveChangesAsync` berikutnya di request
yang sama.

**(f) Password salah dijawab HTTP 500.** ✅ **Sudah diperbaiki** — lihat bagian 2.5.

**(g) `LoginControl` tidak bisa lagi membedakan password salah dari server mati.** ✅ **Sudah
diperbaiki** bersama (f) — lihat bagian 2.5.

**(h) Ganti password sendiri ikut mengakhiri sesi yang sedang berjalan.**
`PostMeta_ChangeMyPassword` memanggil `RevokeAllAsync(user.cUserId)` — termasuk sesi si pemanggil.
Itu benar dari sisi keamanan (kalau ganti password karena curiga bocor, semua harus putus), tapi
artinya user langsung terlempar keluar setiap kali ganti password sendiri. **Perlu Anda putuskan:**
biarkan begitu, atau kecualikan `CallerSessionId`.

**(i) Baris sesi menumpuk tanpa pernah dibersihkan.** Setiap refresh menulis baris baru dan
menandai yang lama `Revoked`; tidak ada yang menghapus. Klien yang refresh tiap 15 menit menulis
~96 baris sehari per perangkat. Belum mendesak, tapi butuh job pembersih sebelum produksi.

**(j) Dua refresh bersamaan bisa menerbitkan dua sesi.** Keduanya membaca sesi yang sama sebagai
`Active`, keduanya merotasi, dan tidak ada yang menghalangi. Plan sudah menyebut perlunya satu
refresh yang dipakai bersama (`SemaphoreSlim`) **di sisi client** — tapi itu tidak menutup race
di sisi server. Perlu penguncian baris atau update bersyarat saat rotasi.

**(k) Zona waktu campur.** `ta_UserSession` distempel `DateTime.UtcNow` (`ustamp`, `datestamp`,
`cUserSessionExpiry`), konsisten dengan `exp` JWT yang memang UTC epoch. Tapi seluruh tabel lain
memakai `App.GetDateStampAsync()` yang mengembalikan `DateTime.Now` — waktu lokal. Jadi satu tabel
ini berbeda sendiri dari semua tabel lain di database.

**(l) `GetVi_User_ByAccount` tidak punya `[GetAction]`.** Tidak di implementasi
(`CredentialServices`) maupun di method interface-nya, dan `EmAppBuilder.GetActionMarker` mencari
di keduanya. Action itu tidak pernah terdaftar, jadi URL-nya 404. Tidak merusak apa pun hari ini
karena login sudah tidak lewat jalur itu, tapi `User.GetUser_ByAccountAsync` di client mati.
Sudah dicatat juga di `plan/unexecuted/rename-action-meta-convention.md`.

### 3.3 Yang belum teruji

Tidak ada satu pun test project di repo ini, dan saya **tidak menjalankan server**. Yang terbukti
baru: kedua solution compile bersih, dan dua index benar-benar ada di database. Yang belum pernah
dijalankan sekali pun — penerbitan token, verifikasi tanda tangan RS256, rotasi refresh token, dan
ketujuh action `Meta` lewat HTTP sungguhan.

---

## 4. Next step

Urutan di bawah adalah urutan yang saya sarankan, bukan sekadar daftar.

**Sebelum apa pun:**

1. **Jalankan servernya dan coba `PostGetMeta_SignIn` sungguhan.** Seluruh fase 2 berdiri di atas
   asumsi bahwa penerbitan dan verifikasi token ini jalan. Membuktikannya sekarang jauh lebih murah
   daripada men-debugnya sambil mengerjakan gerbang. Sekalian pastikan jawaban 401/403/400 dari
   bagian 2.5 memang sampai ke client dengan status yang benar.

**Fase 2 — gerbang dan flow login (inti):**

2. **Gate `IsPublicAction` di `EmApp.ProcessRequest`**, sekaligus mengisi `CallerUserId` dan
   `CallerSessionId` dari `ValidateAsync`. Ini yang menghidupkan kembali (a) dan (b) — termasuk
   tombol set password di `UserEditor`.
3. **Putuskan (d)** — akun sistem — karena gerbangnya akan langsung menyentuhnya.
4. **`ApiClient`**: simpan token, pasang header `Authorization`, auto-refresh saat 401, dengan satu
   refresh yang dipakai bersama (`SemaphoreSlim`). Status code-nya sudah tersedia lewat
   `ActionException` — perhatikan jebakan di akhir bagian 2.5: sign-in dan refresh sendiri menjawab
   401 dan tidak boleh ikut di-auto-refresh.
5. **`LoginControl`** menyimpan `TokenResult` sungguhan — (c).
6. **Putuskan (h)**: ganti password sendiri memutus sesi sendiri atau tidak.

**Sesudah fase 2:**

7. **Perbaiki `Defaults.DefaultHttpClientHandler`** — sekarang menerima sertifikat apa pun, jadi
   token yang lewat jalur itu bisa dicuri utuh. Selama ini "cuma" data yang bocor; setelah ada
   token, yang bocor adalah kredensial yang bisa dipakai ulang. Prioritasnya naik.
8. **Tutup race rotasi refresh token di server** — (j).
9. **Job pembersih `ta_UserSession`** — (i).
10. **Samakan zona waktu** — (k). Keputusan yang perlu diambil: seluruh repo pindah ke UTC, atau
    `ta_UserSession` mengikuti waktu lokal seperti tabel lain.
11. **Lapisan izin `ta_UserClaim`** — per-action, di atas gerbang yang sudah ada.
12. **`PostTa_User_New` berhenti membawa `ta_UserCredential` dari client** — client mengirim password
    polos, server yang menghash (`plan/executed/…` bagian 5.2).
13. **Provider eksternal Google/Microsoft** — tidak butuh tabel baru, cukup satu baris
    `ta_UserCredential` dengan `cCredentialType = "GOOGLE"`. Index
    `ta_UserCredential (cCredentialType, cCredentialKey)` baru diperlukan di titik ini.
14. **Rename action lama ke konvensi `Meta`** — `plan/unexecuted/rename-action-meta-convention.md`,
    sekalian pasang `[GetAction]` yang hilang di (l).
