# Plan — Authorization Fase 1: lapisan service dan pembenahan class

Status: **sudah dieksekusi** — 2026-09-13
Dibuat: 2026-09-13
Diperbarui: 2026-09-13 — bentuk akhir action refresh token dan pembacaan sesi (lihat bagian 1)
Dieksekusi: 2026-09-13, di atas commit `b46b179` (branch `data-services`). Kedua solution build bersih.
Baseline: commit `94c82e4` (Siapkan fondasi otorisasi JWT), branch `data-services`

> **Keputusan yang diambil saat eksekusi** (yang plan ini tinggalkan terbuka):
>
> - **4.10 — LoginControl:** dipilih varian "login tetap hidup". `SignInCommand` memanggil
>   `PostGetMeta_SignIn(UserName, Password)` dan me-raise `SignInSucceeded` kalau tidak melempar;
>   token yang kembali sengaja dibuang, dengan TODO yang menunjuk fase 2.
> - **5.1 — Akun sistem:** **ditunda**, tidak ditangani fase ini. `IssueAsync` tidak memberi
>   perlakuan khusus, jadi login akun admin/debugger akan gagal di foreign key
>   `FK_ta_UserSession_ta_User` sampai fase berikutnya memutuskan (a) atau (b).
> - **5.2 — `PostTa_User_New`:** tetap ditunda sesuai tulisan plan.
> - **5.3 — Rename action lama:** **tidak dikerjakan sekarang**, dipindah ke berkas sendiri supaya
>   tidak hilang: `plan/unexecuted/rename-action-meta-convention.md`.
>
> **Yang dikerjakan di luar daftar langkah, karena jadi konsekuensi langsungnya:**
>
> - `SessionState` (`Em.Libs/Shared/SessionState.cs`) dibuat untuk kolom `cUserSessionState`,
>   mengikuti pola `CredentialState`/`UserState`.
> - `Defaults.PasswordCredentialType` dibuat karena `PasswordCredential.CredentialType` pindah ke
>   backend, sementara `User.InsertAsync` di client masih perlu menyebut jenis kredensialnya.
> - `IStringHasher`/`Argon2Hashing` dan `ICredentialProviderFactory`/`PasswordCredentialFactory`
>   didaftarkan di DI backend — sebelumnya hanya ada di client.
> - `ModelExtensions.cs` region `For Credential` dihapus (bergantung pada `UserCredential` yang
>   dihapus di 4.9).
> - `UserEditor.SetPasswordCommand` dialihkan ke `PostMeta_ResetPassword`. Command ini akan
>   melempar sampai gerbang mengisi `CallerUserId` — perilaku yang sama seperti disebut di 4.8.
> - `CredentialServices` region `vi_UserCredential` ikut dihapus di backend, bukan cuma dari
>   kontrak: view itu memuat kolom rahasia yang sama dan tidak ada lagi yang membacanya.

> **Dokumen ini ditulis untuk dikerjakan tanpa konteks percakapan sebelumnya.** Semua keputusan
> yang sudah diambil ada di bagian 1. Nomor baris yang disebut adalah kondisi pada baseline di
> atas — kalau sudah bergeser, cari berdasarkan nama method/region, bukan nomornya.

---

## 1. Keputusan yang sudah final

Jangan dibuka ulang tanpa alasan baru.

- **Teknologi: JWT Bearer RS256 + refresh token opaque.** Bukan OpenIddict, bukan Duende, bukan
  ASP.NET Core Identity — ketiganya membawa skema user sendiri yang bentrok dengan `ta_User`,
  ULID char(26), dan Argon2 yang sudah jalan.
- **Kunci penandatangan memakai RSA key server yang sudah ada**, dibaca lewat
  `ServicesBase.GetServerRsaKeyAsync()` (`Em.Api.Core/Api/Core/ServicesBase.cs:108`). Kuncinya
  2048 bit dan tersimpan permanen di `ta_Meta`, jadi stabil lintas restart dan sudah memenuhi
  syarat minimum RS256. **Tidak ada sertifikat atau key baru yang perlu dibuat.**
- **Tidak ada middleware autentikasi.** Tidak ada `UseAuthentication()`, tidak ada paket
  `Microsoft.AspNetCore.Authentication.JwtBearer`. Yang dipakai hanya
  `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`) untuk menerbitkan dan
  memvalidasi token.
- **Tidak ada atribut `[Authorize]` di service.** Boleh-tidaknya sebuah action ditentukan
  semata-mata oleh `IsPublicAction` pada `[GetAction]`/`[PostAction]` yang sudah ada, diperiksa
  di `EmApp.ProcessRequest`. Defaultnya `false` — lupa menulis apa pun berarti action tertutup,
  bukan terbuka.
- **Token dikirim lewat header `Authorization` standar.** Bukan query string: query string
  tercatat di log akses Kestrel/IIS/nginx, dan token yang masih hidup di berkas log adalah
  kredensial terbuka.
- **Login Google/Microsoft menyusul, dan tidak butuh tabel baru.** Nanti cukup satu baris di
  `ta_UserCredential` dengan `cCredentialType = "GOOGLE"` dan `cCredentialKey` berisi id permanen
  akun (Google: `sub`; Microsoft: gabungan `tid`+`oid`, **bukan** `sub` yang bersifat per-aplikasi).
  Tidak dikerjakan sekarang.
- **Refresh token selalu dikirim eksplisit: `PostGetMeta_RefreshToken(refreshToken)`.** Tidak ada
  action "perbarui sesi saya" tanpa parameter, dan `PostMeta_RefreshToken()` yang sekarang ada di
  interface dihapus.
- **Pembacaan sesi memakai `PostGetMeta_GetSessions(cUserId)`**, dengan `cUserId` sebagai
  parameter. Tidak ada `GetMeta_MySessions()` terpisah; membaca sesi sendiri berarti memanggil
  action ini dengan id sendiri, dan implementasinya yang memastikan id orang lain hanya boleh
  diminta oleh admin.

---

## 2. Batas fase ini

**Termasuk:** kontrak `ICredentialServices`, implementasi service backend, entity dan DbSet,
pemindahan class kredensial ke backend, pembersihan class client yang jadi rusak, dan kedua
solution harus compile bersih.

**Tidak termasuk — jangan dikerjakan di fase ini:**

- Gate di `EmApp.ProcessRequest`. Kalau gate dipasang sekarang, setiap action non-publik
  langsung menolak semua pemanggil, dan tidak ada jalan untuk login — aplikasi jadi tidak
  terpakai sama sekali. Gate menyusul bersamaan dengan flow login.
- Flow login di client: `ApiClient` menyimpan token, header `Authorization`, auto-refresh saat
  401, perubahan `LoginControl`.
- Lapisan izin per action (`ta_UserClaim`).
- Provider eksternal (Google/Microsoft).
- Perbaikan validasi TLS di `Defaults.DefaultHttpClientHandler`.

---

## 3. Kondisi awal yang perlu diketahui

### 3.1 Lubang keamanan yang sedang ditutup fase ini

Verifikasi password sekarang terjadi **di sisi client**:

- `Em.Ui.Wpf.Core/Controls/LoginControl.xaml.cs:232` memanggil `credential.IsValidAsync(Password)`
- `Em.Ui.Core/Ui.Core/shared/PasswordCredential.cs` → `ValidateAsync` membandingkan hash Argon2
  yang ditarik client lewat `GetTa_UserCredential_ByType`

Karena `IsPublicAction` belum dipakai di `ProcessRequest`, siapa pun yang bisa menyentuh API bisa
menarik seluruh tabel kredensial berikut hash-nya lalu membruteforce offline. Menghapus region
`ta_UserCredential`/`vi_UserCredential` dari kontrak client adalah inti dari fase ini.

### 3.2 Yang sudah ada

| Aset | Letak |
| --- | --- |
| Tabel `ta_UserSession` | sudah ada di database, tercatat di `doc/MainTable.sql` |
| `TokenResult` | `Em.Libs/Shared/TokenResult.cs` — AccessToken, RefreshToken, ExpiresIn |
| Region `Meta's` | sudah dirintis di `ICredentialServices.cs:67-81`, perlu dirapikan (lihat 4.1) |
| RSA key server | `ta_Meta`, lewat `GetServerRsaKeyAsync()` |
| Argon2 | `Em.Libs/Shared/Argon2Hashing.cs` (`IStringHasher`) |
| Konvensi nama action | `CLAUDE.md` root, section **Action naming convention** |

`ta_UserSession` berisi: `cUserSessionId` (PK, char(26)), `cUserId` (FK ke `ta_User`),
`cUserSessionHash` varchar(255), `cUserSessionState` int, `cUserSessionExpiry` datetime, plus
`ustamp`, `datestamp`, `json_object`.

---

## 4. Langkah pengerjaan

### 4.1 `ICredentialServices` — hapus dua region, rapikan region Meta's

Berkas: `src/shared/Em.Libs/Api.Core.Models/ICredentialServices.cs`

**Hapus seluruh region `ta_UserCredential`** (baris 25-40) dan **seluruh region
`vi_UserCredential`** (baris 55-63). Region `ta_User` dan `vi_User` tetap.

**Region `Meta's`** (baris 67-81) sekarang berisi beberapa kekeliruan yang perlu dibereskan:

- `PostMeta_RefreshToken()` (baris 77) menduplikasi `PostGetMeta_RefreshToken` dan tidak
  mengembalikan apa-apa — **hapus**. Satu-satunya jalan memperbarui token adalah
  `PostGetMeta_RefreshToken(refreshToken)`: client selalu menyerahkan refresh token yang
  dipegangnya. **Tidak ada varian tanpa parameter** yang memperbarui "sesi yang sedang berjalan"
  berdasarkan access token — access token yang sudah kedaluwarsa tidak bisa jadi bukti, dan
  refresh token adalah satu-satunya benda yang boleh menerbitkan token baru.
- `PostGetMeta_GetSessions(string cUserId)` (baris 79) **dipertahankan apa adanya**, termasuk
  parameter `cUserId`-nya. Tidak ada action terpisah semacam `GetMeta_MySessions()` tanpa
  parameter — membaca sesi sendiri pun lewat action ini dengan `cUserId` milik sendiri. Yang
  perlu diperbaiki hanya tipe kembaliannya: sekarang `Task` tanpa nilai, padahal prefix
  `PostGet` berarti POST yang mengembalikan sesuatu. Jadikan `Task<ta_UserSession[]>`.

Bentuk akhir region `Meta's`:

```csharp
#region Meta's

Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password);
Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken);
Task PostMeta_SignOut();
Task PostMeta_SignOutAll();
Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword);
Task PostMeta_ResetPassword(string cUserId, string newPassword);
Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId);

#endregion
```

Atribut action **tidak** ditulis di interface ini — pola yang berlaku di repo adalah atribut
menempel di kelas implementasi backend (`EmAppBuilder.GetActionMarker` membaca dari method
konkret lebih dulu, baru jatuh ke method interface).

**Aturan yang mengikat seluruh region ini:** aksi yang hanya masuk akal atas diri sendiri tidak
berparameter user — `PostMeta_SignOut`, `PostMeta_SignOutAll`, dan `PostMeta_ChangeMyPassword`
mengambil ruang lingkupnya dari token. Dua action menerima `cUserId`:
`PostGetMeta_GetSessions` dan `PostMeta_ResetPassword`. Keduanya wajib memeriksa hal yang sama di
dalam implementasinya: kalau `cUserId` yang diminta bukan `CallerUserId`, pemanggil harus
`cUserIsAdmin` — kalau tidak, tolak. Tanpa pemeriksaan itu siapa pun bisa membaca sesi orang lain
atau menimpa passwordnya, jadi pemeriksaan ini bukan tambahan opsional.

### 4.2 `TokenResult` — tambah `cUserId`

Berkas: `src/shared/Em.Libs/Shared/TokenResult.cs`

Tambah satu properti:

```csharp
public string cUserId { get; set; } = string.Empty;
```

Client butuh ini untuk tahu sesi yang baru diterbitkan milik siapa — terutama setelah aplikasi
restart dan hanya memegang refresh token. Client **tidak boleh** membongkar isi access token
untuk mencarinya; bagi client token itu benda buram.

Jangan menaruh `ta_User` di sini. Data user diambil lewat `GetTa_User_ById` seperti pemanggilan
biasa, supaya pembacaannya nanti ikut diatur lapisan izin dan kontrak login tidak terikat pada
bentuk entity `ta_User`.

### 4.3 Entity `ta_UserSession`

Buat `src/shared/Em.Libs/Api.Core.Models/ta_UserSession.cs`, mengikuti bentuk
`ta_UserCredential.cs` yang ada di folder yang sama (properti polos, tanpa navigation property —
EF di repo ini tidak pernah diberi tahu soal foreign key).

Daftarkan DbSet-nya di `src/backend/Em.Api.Core/Api/Core/EmDbContext.cs` (atau
`Models/ApiCoreContext.cs`, ikuti tempat `ta_UserCredentials` didaftarkan).

**Jangan** membuat `vi_UserSession`, dan **jangan** menambahkan CRUD `ta_UserSession` ke
`ICredentialServices`. Client tidak pernah menulis sesi; `PostGetMeta_GetSessions` sudah cukup.

### 4.4 Index database

Dua index ini belum ada dan perlu dijalankan di database, lalu `doc/MainTable.sql` disinkronkan
ulang dengan skill `schemaupdate`:

```sql
CREATE UNIQUE INDEX UQ_ta_UserSession_Hash ON ta_UserSession (cUserSessionHash);
CREATE INDEX IX_ta_UserSession_cUserId ON ta_UserSession (cUserId);
```

`cUserSessionHash` adalah jalur terpanas — setiap refresh token yang masuk dicari lewat kolom
ini. Tanpa index, tiap refresh jadi table scan. `cUserId` dibutuhkan untuk `PostMeta_SignOutAll`
dan `PostGetMeta_GetSessions`.

Index untuk `ta_UserCredential (cCredentialType, cCredentialKey)` baru diperlukan saat login
eksternal dipasang — boleh sekalian, boleh nanti.

### 4.5 Pindahkan class kredensial ke backend

Pindah dari `src/shared/Em.Ui.Core/Ui.Core/shared/` ke `src/backend/Em.Api.Core/Api/Core/`:

- `CredentialProvider.cs` (`CredentialProviderBase`, `CredentialProviderBase<TPayload>`)
- `PasswordCredential.cs` (`PasswordCredential`, `PasswordCredentialFactory`)
- `ICredentialEnrollment.cs`, `ICredentialProviderFactory.cs`

Alasannya: ketiganya memanggil method yang dihapus di 4.1, dan memang tempatnya di server —
hash password tidak boleh keluar dari backend.

Setelah pindah, `CredentialProviderBase.User` yang sekarang bertipe `User` (UiModel client) harus
diganti jadi `ta_User`, dan `User.App.ServiceProvider` diganti dengan cara resolve DI yang berlaku
di backend (`ServicesBase.GetService<T>()`).

### 4.6 Backend `CredentialServices` — implementasi

Berkas: `src/backend/Em.Api.Core/Api/Core/CredentialServices.cs`

**CRUD kredensial tetap dibutuhkan server**, tapi tidak boleh lagi jadi action. Ubah method
`GetTa_UserCredential_*` dan `PostTa_UserCredential_*` yang ada jadi method `private`/`internal`
tanpa atribut `[GetAction]`/`[PostAction]`. Method tanpa atribut tidak didaftarkan sebagai action
oleh `EmAppBuilder`, jadi tidak punya URL.

Implementasikan region `Meta's` dengan atribut:

| Method | Atribut | Catatan |
| --- | --- | --- |
| `PostGetMeta_SignIn` | `[PostAction(IsPublicAction = true)]` | publik — belum ada token saat dipanggil |
| `PostGetMeta_RefreshToken` | `[PostAction(IsPublicAction = true)]` | publik — refresh token itu sendiri buktinya |
| `PostMeta_SignOut` | `[PostAction]` | session id dari token |
| `PostMeta_SignOutAll` | `[PostAction]` | |
| `PostMeta_ChangeMyPassword` | `[PostAction]` | |
| `PostMeta_ResetPassword` | `[PostAction]` | periksa `cUserIsAdmin` di dalam |
| `PostGetMeta_GetSessions` | `[PostAction]` | tolak `cUserId` selain milik pemanggil, kecuali `cUserIsAdmin` |

`PostGetMeta_SignIn` memverifikasi password lewat `PasswordCredential` yang sudah dipindah di 4.5
— inilah tempat Argon2 dibandingkan sekarang.

### 4.7 `ITokenServices` / `TokenServices` — service internal

Buat di `src/backend/Em.Api.Core/Api/Core/`. Ini bukan `IServices` dan tidak punya action:

```csharp
Task<TokenResult> IssueAsync(string cUserId);
Task<TokenValidation> ValidateAsync(string accessToken);
Task RevokeAsync(string cUserSessionId);
Task RevokeAllAsync(string cUserId);
Task<TokenResult> RefreshAsync(string refreshToken);
```

Isi access token seminimal mungkin: `sub` (cUserId), `sid` (cUserSessionId), `adm`
(cUserIsAdmin), `exp`. Umur 15 menit. Jangan menaruh data user di dalamnya — token ikut di setiap
request, dan data yang ditaruh di sana jadi basi tanpa cara memperbaruinya.

Refresh token: 32 byte acak (`RandomNumberGenerator`), dikirim ke client Base64Url, yang
**disimpan hanya hash-nya** di `cUserSessionHash`. Dirotasi setiap dipakai — baris lama
di-`Revoked`, baris baru ditulis.

**Jebakan pendaftaran DI:** jangan pakai `builder.AddService<,>` untuk service ini.
`EmAppBuilder.AddService` (`Api/Shared/EmAppBuilder.cs:39`) mensyaratkan atribut `[Module]`
dan minimal satu method ber-action; kalau tidak ada, ia hanya menulis warning lalu **tidak
mendaftarkan apa pun**. Pakai `Services.AddScoped<ITokenServices, TokenServices>()` biasa di
`EmApp.InitInternalServices`.

**Zona waktu:** `cUserSessionExpiry` bertipe `datetime` tanpa offset, sedangkan `exp` di JWT
selalu UTC epoch. Tetapkan UTC untuk kolom itu dan konsisten di semua pembacaan.

### 4.8 `ServicesBase` — identitas pemanggil

Berkas: `src/backend/Em.Api.Core/Api/Core/ServicesBase.cs`

Tambah properti untuk menampung identitas pemanggil yang sudah tervalidasi, sejajar dengan `App`,
`HttpContext`, dan `Logger` yang sudah di-assign `ProcessRequest`:

```csharp
public string? CallerUserId { get; internal set; }
public string? CallerSessionId { get; internal set; }
```

Di fase ini properti ini belum diisi siapa pun (gate belum dipasang). Action yang membutuhkannya
— `PostMeta_SignOut`, `PostGetMeta_GetSessions`, `PostMeta_ChangeMyPassword` — boleh melempar
`InvalidOperationException` kalau `CallerUserId` masih null, dan itu memang perilaku yang benar
sampai gate menyusul.

### 4.9 Bersihkan sisi client

Berkas yang rusak akibat 4.1 dan harus dibereskan:

| Berkas | Tindakan |
| --- | --- |
| `Em.Ui.Wpf.Core/Api.Core/CredentialService.cs` | Hapus region `ta_UserCredential` (baris 44-79) dan `vi_UserCredential` (baris 105-120). Tambah implementasi region `Meta's` yang memanggil `GetAsync`/`PostAsync` seperti method lain di berkas itu. |
| `Em.Ui.Core/Api.Core.Models/UserCredential.cs` | **Hapus seluruh berkas.** Seluruh class ini adalah `UiModel<vi_UserCredential, ICredentialServices>` yang bergantung pada method yang dihapus. |
| `Em.Ui.Core/Api.Core.Models/User.cs` | Hapus region `Credentials` (sekitar baris 163-210): properti `PasswordCredential`, `LoadPasswordCredentialAsync`, dan pembantu terkait. |
| `Em.Ui.Wpf.Core/Core/EmApp.Statics.cs:80` | Hapus `AddSingleton<ICredentialProviderFactory, PasswordCredentialFactory>()` — factory-nya sudah pindah ke backend. |

`User.cs:282-312` tetap membangun `ta_UserCredential` kosong untuk payload `PostTa_User_New`.
Itu **boleh dibiarkan**: barisnya dibuat dengan `cCredentialSecret = null` dan state `Pending`,
jadi tidak ada rahasia yang ditentukan client. Perubahan signature `PostTa_User_New` masuk fase
berikutnya (lihat bagian 6).

### 4.10 `LoginControl` — konsekuensi yang harus disepakati

`Em.Ui.Wpf.Core/Controls/LoginControl.xaml.cs:229-235` memanggil `user.GetCredentialAsync(...)`
dan `credential.IsValidAsync(Password)`. Keduanya hilang setelah 4.5 dan 4.9, jadi berkas ini
**tidak akan compile** tanpa perubahan.

Karena flow login sengaja di luar fase ini, ganti isi `SignInCommand` dengan lemparan
`NotImplementedException` berikut komentar TODO yang menunjuk fase berikutnya. Konsekuensinya
**aplikasi tidak bisa login selama fase ini berjalan** — itu disengaja, dan lebih baik daripada
meninggalkan login yang tidak memeriksa apa pun.

Alternatifnya, kalau login perlu tetap hidup: panggil `PostGetMeta_SignIn(UserName, Password)`
dan raise `SignInSucceeded` bila tidak melempar, tanpa menyimpan token sama sekali. Tiga baris,
dan token diurus fase berikutnya. **Putuskan salah satu sebelum mulai.**

### 4.11 Build

Build kedua solution dan pastikan bersih:

```
dotnet build src/backend/Em.Api.slnx
dotnet build src/frontend/Em.Ui.Wpf.slnx
```

---

## 5. Keputusan yang masih terbuka

### 5.1 Akun sistem tidak bisa punya sesi

`FK_ta_UserSession_ta_User` mensyaratkan `cUserId` ada di `ta_User`. Tapi `Defaults.AdminUserId`
(`00000000000000000000000000`) dan `Defaults.DebuggerUserId` (`99999999999999999999999999`)
sengaja tidak punya baris di sana — `Em.Ui.Core/Api.Core.Models/User.cs:226` malah melempar
`SystemAccountException` untuk keduanya.

Artinya `IssueAsync` akan gagal di foreign key begitu akun admin login. Pilihannya:

- **(a)** Akun sistem dapat access token saja, tanpa refresh token dan tanpa baris sesi.
  Sejalan dengan keputusan menaruh akun sistem di luar `ta_User`. *Rekomendasi.*
- **(b)** Lepas foreign key-nya.

### 5.2 `PostTa_User_New` masih membawa `ta_UserCredential` dari client

Signature-nya `DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>`. Sekarang
aman karena client hanya mengirim baris kosong, tapi arah yang benar adalah client mengirim
password polos dan server yang menghash. Perubahan ini menyentuh alur pembuatan user, jadi
ditunda ke fase berikutnya.

### 5.3 Rename action lama ke konvensi `Meta`

`ApiCoreServices` masih memakai nama lama: `Handshake`, `GetServerPublicKey`, `GetTimeStamp`,
`GetUlid`, `GetUlidMany`. Semuanya non-tabel, jadi menurut konvensi di `CLAUDE.md` semestinya
`GetMeta_*`. Nama action ikut jadi URL dan `ApiClient` adalah satu-satunya pemanggil, jadi
sekarang saat paling murah untuk menyeragamkan — tapi belum diputuskan.

---

## 6. Fase berikutnya (untuk orientasi, bukan untuk dikerjakan sekarang)

1. Gate `IsPublicAction` di `EmApp.ProcessRequest` + mengisi `CallerUserId`/`CallerSessionId`.
2. `ApiClient`: header `Authorization`, simpan refresh token, auto-refresh saat 401. **Jebakan:**
   beberapa request yang kena 401 bersamaan akan memicu beberapa refresh sekaligus, dan karena
   refresh token dirotasi, yang belakangan memakai token yang sudah mati. Butuh satu refresh yang
   dipakai bersama (`SemaphoreSlim`), bukan satu per request.
3. `LoginControl` memakai `PostGetMeta_SignIn` sungguhan.
4. Perbaikan `Defaults.DefaultHttpClientHandler` — sekarang menerima sertifikat apa pun, dan token
   yang lewat jalur itu bisa dicuri utuh.
5. Lapisan izin `ta_UserClaim`.
6. Provider eksternal (Google/Microsoft).
