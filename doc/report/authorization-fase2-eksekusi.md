# Eksekusi Authorization Fase 2 — flow login di client dan penegakan di server

Status: **langkah 1–11 selesai, belum di-commit; langkah 12 (uji jalur) baru sebagian**
Dibuat: 2026-09-14
Baseline: commit `b6cfa7c` (Create authorization-fase2-flow-login.md), branch `data-services`
Plan sumber: `plan/executed/authorization-fase2-flow-login.md` — dipindah ke `plan/executed/` pada
2026-09-22 berikut pembaruan statusnya; saat laporan ini ditulis ia masih di `plan/unexecuted/`
Build: `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` sama-sama hijau, tanpa warning
Diagram alur: `doc/RequestFlow.drawio` — 4 halaman (request end-to-end, gerbang identitas,
refresh 401, siklus sesi di client)

Angka perubahan: 20 berkas tersunting, 5 berkas baru, 1075 baris masuk, 227 baris keluar.

---

## 1. Ringkasan

Fase 1 membuat lapisan service-nya; fase ini membuat **alurnya**. Sebelum ini sign in sudah
benar-benar diperiksa server, tapi hasilnya dibuang — `LoginControl` menulis
`_ = await services.PostGetMeta_SignIn(...)` dengan TODO yang menunjuk fase ini, dan tidak ada satu
pun request sesudahnya yang membawa bukti bahwa login itu pernah terjadi. `IsPublicAction` tercatat
rapi di setiap action tapi tidak pernah dibaca `ProcessRequest`, jadi seluruh API terbuka untuk siapa
pun yang bisa menyentuh host-nya.

Sesudah fase ini: layar login menerbitkan sesi sungguhan, setiap request membawanya, access token
yang mati diperbarui sendiri tanpa mengganggu user, sign out benar-benar mengakhiri sesi di kedua
sisi, dan action non-publik menolak pemanggil tanpa identitas.

Urutan pengerjaannya mengikat dan diikuti apa adanya: penegakan (langkah 10) baru dinyalakan sesudah
client benar-benar membawa token (langkah 1–9). Kalau dibalik, aplikasi WPF mati total kecuali
handshake dan login.

---

## 2. Apa yang berubah

### 2.1 Kontrak bersama (`src/shared/Em.Libs`)

| Berkas | Perubahan |
| --- | --- |
| `Shared/UserHeaderProtocol.cs` | **Baru.** Penyusun dan pembaca isi header `X-Em-User`, bersebelahan dengan `ProbeProtocol` dan `DebugTokenProtocol` dengan alasan yang sama: client yang menyusunnya dan server yang membacanya harus menyebut kata yang sama persis. |
| `Defaults.cs` | `DefaultHttpClientHandler` berhenti mematikan validasi sertifikat; tambah `CreateHttpClientHandler(bool ignoreSslErrors)`; tambah `CredentialModuleName = "core.credential"`; XML doc `UserHeader` diperbarui. |

**Bentuk baru `X-Em-User`** — objek JSON yang membawa keduanya:

```
X-Em-User: {"cUserId":"01K5Z0X9P7QW3M8V2ND6TJHFAB","cUserAccount":"budi"}
```

`Create` melewatkan properti yang kosong, jadi header tidak pernah memuat `"cUserId":null`.
Serialisasinya memakai encoder bawaan `System.Text.Json` yang meng-escape karakter non-ASCII jadi
`\uXXXX`, sehingga nilainya selalu aman dikirim apa adanya berapa pun isi nama akunnya. `TryRead`
menerima **dua bentuk**: objek di atas, dan — kalau teksnya tidak diawali `{` — satu nama akun polos
yang mengisi `cUserAccount` saja, supaya pengembang tetap bisa mengetik `X-Em-User: budi` di
curl/Postman. Header yang ada tapi tidak terbaca dianggap tidak menyebut siapa-siapa.

Aturan yang **tidak** berubah: header ini tidak pernah jadi sumber identitas tanpa token debug yang
lolos. Menambahkan `cUserId` tidak melonggarkannya.

### 2.2 Backend — objek keterangan request (`ActionRequest`)

`ActionRequest.cs` **baru**, di `Em.Api.Core/Api/Core/` — bukan di `Em.Libs`, karena client
tidak pernah mengirim objek ini; ia disusun server dari header yang masuk.

Isinya: `cUserId`, `cUserAccount`, `cUserSessionId`, `IsAdmin`, `Source` (`CallerSource.None` /
`AccessToken` / `DebugToken`), `DebugKeyName`, `IsImpersonating`, `RouteLabel`, `CallerAddress`,
`ReceivedAtUtc`, plus turunan `IsDebugRequest` dan `IsAuthenticated`. Immutable (record ber-`init`).

Pemeriksa hak ikut tinggal di sana sebagai method pada record-nya: `IsSelf`, `RequireUserId`,
`RequireSessionId`, `RequireAdmin`, `RequireSelfOrAdmin`. Beda 401 dan 403 ikut pindah beserta
alasannya — belum terbukti siapa → 401; sudah jelas siapa tapi tidak berhak → 403. Kalau yang kedua
ikut dijawab 401, client yang memperbarui token setiap kena 401 akan mengejar token baru
terus-menerus untuk permintaan yang memang tidak akan pernah diizinkan.

`ActionRequest.None` adalah satu instance statis yang seluruh identitasnya kosong, dipakai sebagai
nilai awal `ServicesBase.Request` dan sebagai jawaban di luar request. Jadi tidak ada jalur yang
memaksa pemanggil memeriksa null lebih dulu, dan setiap pemeriksa hak menjawab 401 untuk semuanya —
tidak ada request berarti tidak ada yang terbukti.

**Tiga jalan mengambilnya**, ditentukan bentuk kelas yang membutuhkannya:

1. Di dalam service module — `ServicesBase.Request`. Tidak pernah null di dalam action.
2. Kelas pembantu lewat DI — minta `ActionRequest` di konstruktor. Didaftarkan **scoped** di
   `EmApp.InitInternalServices`:
   `Services.AddScoped(sp => sp.GetRequiredService<EmApp>().CurrentRequest ?? ActionRequest.None)`.
3. Method statis / extension / fungsi murni — dioper sebagai parameter biasa.

`EmApp.CurrentRequest` **bukan** jalur untuk module; ia untuk Engine dan hal yang melintasi request
(penulis audit, log enricher, pendaftaran DI di atas), dan tipenya sengaja nullable.

> **Jebakan yang dihindari, dan ini bukan urusan sepele:** `EmApp` di backend hidup sebagai
> **singleton**. Menyimpan objek ini di field berarti dua request yang berjalan bersamaan saling
> menimpa identitas — satu request bisa dikerjakan atas nama pemanggil request yang lain, dan
> gejalanya hanya muncul saat ada beban. Karena itu **tidak ada field**: gerbang menaruhnya di
> `HttpContext.Items[RequestItemKey]` sekali, dan `CurrentRequest` membacanya ulang setiap diakses —
> mekanisme yang sama persis dengan `EmApp.ServiceProvider` di kelas yang sama.

Di `ServicesBase`, tiga properti lama **dipertahankan sebagai penerus**, bukan dihapus:

```csharp
public ActionRequest Request { get; internal set; } = ActionRequest.None;
public string? CallerUserId  => Request.cUserId;
public string? CallerSessionId => Request.cUserSessionId;
public bool    CallerIsAdmin => Request.IsAdmin;
```

Yang hilang hanya setter-nya — `ProcessRequest` sekarang mengisi satu properti, bukan tiga.
`CallerGate` menyusut jadi `(ActionRequest Request, ActionResult? Rejection)`, dan record struct
`CallerIdentity` yang private **dihapus**.

### 2.3 Backend — gerbang identitas (`EmApp.ResolveCallerAsync` dkk.)

| Method | Perubahan |
| --- | --- |
| `ResolveCallerAsync` | Header dibaca lewat `UserHeaderProtocol.TryRead`, bukan `.ToString().Trim()`. Tripwire ikut memeriksa id: request tanpa token debug yang menyebut akun `debugger` **atau** `Defaults.DebuggerUserId` dijawab 404. Keterangan yang berlaku untuk request apa pun (`RouteLabel`, `CallerAddress`, `ReceivedAtUtc`) diisi sekali di sini. |
| `ResolveDebugCallerAsync` | Matriks penuh: header kosong → akun debugger; hanya nama akun → dicari lewat nama; hanya id → dicari lewat id; keduanya → dicari lewat **id**, lalu nama akun dicocokkan dengan baris yang ketemu, tidak cocok → 401 berpesan jelas. Akun sistem (`debugger`, `admin`) dikenali lebih dulu dan tidak pernah dicari sebagai baris pengguna — sekarang cocok baik disebut lewat id maupun lewat nama akun. |
| `ResolveBearerCallerAsync` | `cUserId` pada header dibandingkan dengan pemilik access token. Access token tetap yang menang; selisihnya cukup `LogDebug`, yang sekarang bisa menyebut kedua id yang sesungguhnya. Perbandingan ini akhirnya bebas biaya — dulu dilewati justru karena menerjemahkan id jadi nama akun berarti satu query tambahan per request. |

Id yang menang saat keduanya ada, karena itu yang permanen. Ketidakcocokan ditolak, bukan didiamkan:
ia berarti client menyusun header dari dua sumber yang berbeda, dan menebak mana yang benar lebih
buruk daripada berhenti.

`cUserAccount` pada `ActionRequest` **diisi hanya kalau terbukti**. Jalur token debug mengisinya
sesudah baris penggunanya ketemu; jalur access token membiarkannya `null` — token hanya membawa id,
dan nama akun yang menempel di header datang dari pemanggil, bukan dari data.

### 2.4 Backend — penegakan dan keaktifan sesi

**Penegakan `IsPublicAction`** menyala di `ProcessRequest`, tepat sesudah route-nya ketemu dan
sebelum pemeriksaan method:

```csharp
if (!actionDef.IsPublicAction && !gate.Request.IsAuthenticated) {
   return ToJsonResult(BuildErrorActionResult(
      "This action requires a signed-in caller.", 401, actionDef.Type, routeLabel));
}
```

Letaknya sesudah pencarian route, bukan sebelum, karena `IsPublicAction` memang milik action-nya.
Konsekuensinya action yang ada tapi tertutup dijawab 401 sementara action yang tidak ada dijawab 404
— dari luar keduanya bisa dibedakan. Itu diterima: yang benar-benar harus tidak terdeteksi adalah
mekanisme token debug, dan jalur itu tetap menjawab 404 apa pun route-nya karena `ResolveCallerAsync`
berjalan lebih dulu.

**Inventaris action publik sesudah fase ini** — ini daftar permukaan anonim aplikasi:

| Action | Sebelum | Sesudah |
| --- | --- | --- |
| `core/Handshake` | publik | **tetap publik** — mendahului login; dipakai probe koneksi |
| `core/GetServerPublicKey` | tertutup | tetap tertutup |
| `core/GetTimeStamp` | publik | **ditutup** |
| `core/GetUlid`, `core/GetUlidMany` | publik | **ditutup** |
| `core.credential/PostGetMeta_SignIn` | publik | tetap publik |
| `core.credential/PostGetMeta_RefreshToken` | publik | tetap publik |

Ketiga yang ditutup tidak pernah dipanggil sebelum login. Kalau nanti ada kebutuhan pra-login yang
membutuhkannya, buka lagi satu per satu berikut alasannya — jangan membuka blok.

**Keaktifan sesi diperiksa di setiap request yang membawa token.** Tanpa ini `PostMeta_SignOut` baru
berlaku sesudah access token kedaluwarsa sendiri — sampai 15 menit sesudah tombolnya ditekan.

- `ISessionStore.FindByIdAsync(string sessionId)` — baru, diimplementasi di `UserSessionStore` dan
  `SystemSessionStore` sejajar dengan `FindByHashAsync`.
- `ITokenServices.IsSessionActiveAsync(string cUserSessionId)` — baru; mencari di kedua store, `true`
  hanya kalau state-nya `Active` **dan** `Expiry` belum lewat.
- Dipanggil dari dalam `TokenServices.ValidateAsync`, **bukan** dari gerbang, supaya tidak ada
  pemanggil yang bisa lupa melakukannya.

Biayanya satu query per request yang membawa token. Jalur token debug tidak terpengaruh — ia tidak
punya `sid` dan tidak menyentuh `ValidateAsync`.

> **Jebakan yang sudah diperhitungkan:** `RefreshAsync` mengganti baris sesi (rotasi), jadi `sid` di
> access token lama menunjuk baris yang sudah `Revoked`. Itu perilaku yang benar — access token lama
> ikut mati begitu refresh berhasil — tapi artinya client **tidak boleh** memakai access token lama
> lagi sesudah menerima yang baru. Itu yang dijaga `EnsureRefreshedAsync` di sisi client (2.6).

### 2.5 Backend — pemeriksa hak berkumpul, satu query hilang

Empat helper private di `CredentialServices` region `Meta helpers` dialihkan ke `ActionRequest`:

| Sebelum | Sesudah |
| --- | --- |
| `RequireCallerUserId()` | `Request.RequireUserId()` |
| `RequireCallerSessionId()` | `Request.RequireSessionId()` |
| `RequireSelfOrAdminAsync(cUserId)` | `Request.RequireSelfOrAdmin(cUserId)` — **tidak lagi async** |
| `RequireAdminAccountAccessAsync()` | menyusut jadi `Request.RequireAdmin()` + pemeriksaan saklar `AdminAccount.IsEnabledAsync(ctx)` |

Yang **tetap tinggal** di `CredentialServices`, karena menyentuh database atau urusan module-nya
sendiri: `RequireUserAsync`, `SignInAdminAsync`, `EnsureAccountNameIsNotReserved`, `Tokens`,
`Hasher`.

**Yang ikut hilang: satu query per pemanggilan.** `RequireSelfOrAdminAsync` dan
`RequireAdminAccountAccessAsync` dulu menarik baris pengguna hanya untuk membaca `cUserIsAdmin`,
padahal gerbang sudah membawanya dari claim `adm` di token. `PostMeta_ResetPassword` dan
`PostGetMeta_GetSessions` sekarang memanggilnya tanpa `await`.

**Harganya, dan cara menutupnya:** claim `adm` adalah potret saat sesi dibuka, jadi admin yang
dicabut haknya di tengah jalan masih terbaca admin sampai access token-nya kedaluwarsa. Karena itu
`PostTa_User_Update` dan `PostTa_User_UpdateBatch` sekarang **mencabut seluruh sesi milik user itu**
(`Tokens.RevokeAllAsync`) saat `cUserIsAdmin` berubah — persis alasan yang membuat ganti password
mencabut sesi. Nilai lamanya dibaca lebih dulu (`AsNoTracking`) supaya yang berubah bisa diketahui;
pada jalur batch, satu query untuk seluruh id sekaligus.

### 2.6 Client — sesi tinggal di `ApiClient`

`ApiClient` adalah transport resmi dan sudah dipegang satu per koneksi oleh
`EmApp.GetActiveApiClient`, jadi di sanalah sesi tinggal — bukan variabel global. Satu aplikasi
bisa memegang beberapa koneksi, dan token satu server tidak pernah boleh terkirim ke server lain.

Anggota baru (seluruhnya ikut masuk `IApiClient`):

```csharp
string? AccessToken { get; }                    // private set
string? RefreshToken { get; }                   // private set
DateTimeOffset AccessTokenExpiresAtUtc { get; } // private set
string? SessionUserId { get; }                  // private set
bool HasSession => !string.IsNullOrEmpty(AccessToken);
string? ActiveUserId { get; set; }              // di samping ActiveUserAccount yang sudah ada
void SetSession(TokenResult token);
void ClearSession();
event EventHandler? SessionChanged;   // setiap isi sesi berganti, termasuk hasil rotasi
event EventHandler? SessionEnded;     // sekali, saat sesi mati dan tidak bisa dipulihkan
```

`ClearSession` sengaja **tidak** memicu `SessionEnded`; yang memicunya adalah `EndSession` private,
supaya sign out yang diminta user — yang mengurus tampilannya sendiri — tidak mengumumkan hal yang
sama dua kali.

**Header.** Di `BuildRequest`, sesudah header token debug: `Authorization: Bearer {AccessToken}`.
Tetap per-request, dengan alasan yang sama seperti dua header sebelumnya — isinya berubah saat
aplikasi berjalan, dan `DefaultRequestHeaders` akan membuat token lama ikut terkirim oleh request
yang disusun sebelum rotasi. Di baris yang sama, header identitas jadi
`UserHeaderProtocol.Create(ActiveUserId, ActiveUserAccount)`; kalau keduanya kosong, header tidak
dikirim sama sekali.

**Refresh otomatis.** Ketiga jalur request (`GetAsync<T>`, `PostAsync`, `PostAsync<T>`) dialihkan
lewat satu pembungkus `SendAsync<T>(Func<HttpRequestMessage> requestFactory)`:

1. Belum ada sesi → kirim sekali, apa adanya. Tidak ada yang bisa di-refresh.
2. Catat `AccessToken` yang dipakai (`attemptedToken`), kirim, kembalikan hasilnya.
3. Kalau keluar `ActionException` ber-`StatusCode == 401`: jalankan `EnsureRefreshedAsync`; berhasil
   → **susun ulang** request dari factory dan kirim sekali lagi; gagal atau 401 berulang → sesi
   dibuang, `SessionEnded` dipicu, exception terakhir dilempar ke pemanggil.

Percobaan ulang **maksimal satu kali**. Tidak ada loop: 401 kedua sesudah token baru berarti
masalahnya bukan token.

Bentuknya factory, bukan objek, karena `HttpRequestMessage` tidak bisa dipakai dua kali — dan itu
pula alasan `StringContent` pada jalur POST dibangun **di dalam** factory. Isi body disusun sekali
oleh helper `BuildPostBody` yang dipakai bersama jalur refresh.

**Satu refresh untuk semua.** `EnsureRefreshedAsync` dijaga `SemaphoreSlim(1, 1)`, dan intinya ada
pada perbandingan ini:

```csharp
if (!string.Equals(AccessToken, attemptedToken, StringComparison.Ordinal)) return HasSession;
```

Refresh token dirotasi setiap dipakai. Tanpa pengaman ini, lima request yang kena 401 bersamaan akan
mengirim lima refresh: yang pertama berhasil, empat sisanya memakai token yang sudah mati dan
**mencabut sesinya sendiri**.

**Rute refresh.** Dikirim `ApiClient` sendiri lewat jalur mentah yang tidak melewati `SendAsync` dan
tidak memasang `Authorization` — 401 di dalamnya akan memicu refresh lagi, dan rekursi itu tidak
punya dasar berhenti. Module-nya `Defaults.CredentialModuleName`, action-nya
`nameof(ICredentialServices.PostGetMeta_RefreshToken)`, jadi tidak ada nama yang diketik ulang.

### 2.7 Client — penyimpanan sesi lintas restart

Dua bagian, karena DPAPI hanya ada di Windows sementara `Em.Ui.Core` sengaja bebas framework.

**Kontrak** — `Em.Ui.Core/Ui.Core/shared/ISessionStorage.cs` (baru):

```csharp
public sealed record SavedSession(string RefreshToken, string cUserId, string cUserAccount);
public interface ISessionStorage {
   void Save(string profileName, SavedSession session);
   SavedSession? Load(string profileName);
   void Clear(string profileName);
}
```

**Implementasi** — `Em.Ui.Wpf.Core/Core/RegistrySessionStorage.cs` (baru):

- Menulis ke subkey koneksi yang sudah ada (`HKCU\{ApplicationName}\Api Connections\{ProfileName}`)
  sebagai `REG_BINARY` bernama `Session`.
- Isinya `ProtectedData.Protect(payload, entropy, DataProtectionScope.CurrentUser)` — terikat ke akun
  Windows yang sedang berjalan, jadi isi Registry yang disalin ke mesin lain tidak berguna.
- `entropy` konstanta byte milik aplikasi (`"em.session.v1"u8`). Bukan rahasia dan tidak
  berpura-pura jadi rahasia; gunanya memastikan blob aplikasi ini tidak ikut terbuka oleh aplikasi
  lain yang memanggil `Unprotect` dengan entropy kosong.
- `Load` yang gagal membuka blob (`CryptographicException`/`JsonException`) menghapus nilainya lalu
  menjawab `null` — blob yang tidak bisa dibuka tidak akan pernah bisa dibuka lagi.
- Subkey koneksinya sendiri tidak pernah dibuat dari sini: sesi hanya menumpang profil yang memang
  sudah tersimpan.

Konstanta nama subkey tidak diketik dua kali — `EmApp.ApiConnectionsSubKey` dinaikkan dari
`private` jadi `internal` dan dipakai bersama.

Konsekuensi yang disengaja dari menumpang subkey koneksi: mengganti nama atau menghapus profil ikut
membuang sesi tersimpannya (`DeleteApiConnection` menghapus subtree), sementara mengedit profil tidak
(`AddApiConnection` menulis field-nya satu per satu). Keduanya perilaku yang benar.

Registrasi DI: `Services.AddSingleton<ISessionStorage, RegistrySessionStorage>()` di
`InitInternalServices`, mengikuti aturan bahwa service hanya didaftarkan saat aplikasi dibangun.

**Rotasi wajib ikut tersimpan.** Setiap refresh yang berhasil menerbitkan refresh token baru dan
mematikan yang lama. Kalau yang tersimpan tidak ikut ditimpa, restart berikutnya memakai token mati
dan user terlempar ke layar login tanpa sebab yang kelihatan. Itulah kenapa `SessionChanged` ada, dan
kenapa `BeginSessionAsync` berlangganan ke sana — bukan menyimpan sekali saja saat login.

### 2.8 Client — `EmApp` sebagai satu-satunya pintu sesi

`ActiveUser` dilonggarkan dari `User` (`null!`) jadi `User?`, dan `SetActiveUser(User user)` jadi
`SetActiveUser(User? user)`. Sesudah fase ini "belum ada yang masuk" adalah keadaan normal yang bisa
datang dua kali — sebelum login dan sesudah sign out — bukan lagi keadaan sementara saat startup.

Dua tempat yang menulis identitas ke client — `SetActiveUser` dan `GetActiveApiClient` — sekarang
mengisi **keduanya**, `ActiveUserId` dan `ActiveUserAccount`. Diisi dan dikosongkan berbarengan;
mengisi separuh berarti mengirim header yang menyebut orang yang berbeda dari yang dimaksud.

Anggota baru di region `Session`:

```csharp
public event EventHandler<SessionEndedEventArgs>? SessionEnded;
public async Task BeginSessionAsync(TokenResult token, bool remember);
public Task EndSessionAsync(bool notifyServer);
public async Task EndSessionAsync(bool notifyServer, string? reason);   // overload, bukan rename
public async Task<bool> TryRestoreSessionAsync(string profileName);
public const string SessionExpiredNotice = "Your session has ended. Please sign in again.";
```

`BeginSessionAsync`: `SetSession(token)` → kosongkan `DebugToken` koneksi → muat `ActiveUser` →
berlangganan `SessionChanged`/`SessionEnded` → simpan (atau bersihkan) sesi sesuai `remember`. Kalau
pemuatan `ActiveUser` gagal, **seluruhnya dibatalkan** — `DebugToken` dikembalikan, `ClearSession`,
penyimpanan dibersihkan, exception naik ke pemanggil. Setengah masuk lebih buruk daripada gagal
masuk.

`EndSessionAsync`: lepas langganan → `PostMeta_SignOut()` kalau diminta dan sesinya sesi sungguhan,
**dibungkus try/catch dan kegagalannya diabaikan** (server yang tidak bisa dihubungi tidak boleh
menahan user di dalam aplikasi) → `ClearSession` → hapus penyimpanan → kembalikan `DebugToken` →
`SetActiveUser(null)` → picu `SessionEnded`.

**Identitas `ActiveUser` sesudah login.** Akun biasa dibaca lewat `User.GetUser_ByIdAsync` memakai
`TokenResult.cUserId` — action-nya tertutup, dan pada titik itu access token sudah terpasang jadi ia
lolos. Akun administrator bawaan (`Defaults.AdminUserId`) tidak punya baris pengguna, jadi dibuatkan
objek sintetis `CreateAdminUser` di `EmApp.Statics.cs`, sepola `CreateDebuggerUser` yang sudah ada.
Pemeriksaan id sistem dilakukan **sebelum** pemanggilan, bukan dengan menangkap exception-nya, karena
`User.GetUser_ByIdAsync` memang melempar `SystemAccountException` untuk id semacam itu.

**Token debug selama sesi hidup.** Saat sesi sungguhan dibuka di atas koneksi debug,
`ApiConnection.DebugToken` untuk koneksi itu dikosongkan selama sesinya hidup lalu dikembalikan saat
sesi berakhir. Nilainya disimpan di field `_suspendedDebugToken`, bukan dibangkitkan ulang. Tanpa ini
alur JWT tidak pernah benar-benar teruji dari build dev: gerbang memeriksa token debug **paling
depan**, jadi Bearer yang menyertainya tidak akan pernah terbaca.

### 2.9 Client — UI

| Berkas | Perubahan |
| --- | --- |
| `Controls/LoginControl.xaml.cs` | `SignInCommand` membuka sesi sungguhan lewat `BeginSessionAsync`, lalu menyimpan `RememberedUserName` **dan** `RememberedProfileName`. Tambah properti `SessionEndedNotice` dan method `SetRestoringSession(bool)`. Mematikan "keep me signed in" sekarang ikut membuang profil yang diingat dan sesi tersimpannya. |
| `Controls/LoginControl.xaml` | Strip pemberitahuan baru (`noticeStripStyle`, warna `warning`, ikon `Solid_CircleInfo`) di atas form, terpisah dari strip error merah. |
| `Windows/MainWindow.xaml.cs` | Berlangganan `EmApp.SessionEnded` (dengan `Dispatcher.Invoke`), `SyncLoginHost` menitipkan `SessionEndedNotice` ke layar login lalu mengosongkannya, `OnLoaded` memanggil `RestoreSessionAsync`, `PerformLogoff` terisi. |
| `Core/SessionEndedEventArgs.cs` | **Baru.** Membawa `Reason`; `null` berarti user sendiri yang keluar. |

**`PerformLogoff`** cukup `await EmApp!.EndSessionAsync(notifyServer: true)`. Tampilan kembali ke
layar login lewat `SessionEnded`, **bukan** dengan menulis `IsSignedIn` di situ — supaya sign out yang
diminta user dan sesi yang mati sendiri melewati jalan yang sama persis.

**Pemulihan sesi saat startup** (`MainWindow.RestoreSessionAsync`): `RememberSignIn` harus menyala dan
`RememberedProfileName` harus menunjuk profil yang masih ada; profil itu dijadikan `ActiveConnection`,
lalu `PostGetMeta_RefreshToken(saved.RefreshToken)` dipanggil — action-nya publik, jadi tidak butuh
apa pun selain token itu. Berhasil → `BeginSessionAsync` + `IsSignedIn = true`. Gagal → penyimpanan
dibersihkan, layar login tampil dengan nama akun terisi dan **tanpa** pesan error merah; sesi
kedaluwarsa bukan kesalahan user. Selama berjalan, layar login digambar dalam keadaan sibuk
("Restoring session...") — bukan window kosong yang menunggu.

`SimulateLogin` tetap apa adanya: murni simulasi tampilan. `EndSessionAsync` sudah memeriksa
`HasSession` sebelum memberi tahu server, jadi logoff di mode debug tidak memanggil
`PostMeta_SignOut` — yang memang akan dijawab 401 karena jalur debug tidak punya sesi.

### 2.10 TLS

`Defaults.DefaultHttpClientHandler` dulu **selalu** memasang
`DangerousAcceptAnyServerCertificateValidator`, dan itu satu-satunya handler yang dipakai
`ApiClient.Create(connection)` — jadi tidak ada satu pun koneksi yang memvalidasi sertifikat,
termasuk koneksi yang mulai membawa token sesudah fase ini.

Sekarang: `DefaultHttpClientHandler` jadi handler biasa; `CreateHttpClientHandler(bool)` satu-satunya
bentuk yang bisa memasang callback berbahaya itu, dan pemanggilnya harus menyebut niatnya;
`ApiConnection.CreateApiClient()` jadi
`ApiClient.Create(this, Defaults.CreateHttpClientHandler(IgnoreSslErrors))` — barulah saklar
`IgnoreSslErrors` yang sudah lama tersimpan di Registry berarti sesuatu. `ConnectionConfigEditor`
diberi keterangan di bawah checkbox-nya bahwa mematikan validasi membuat handshake RSA di atasnya
ikut kehilangan arti.

---

## 3. Dua penyimpangan dari rencana

### 3.1 Paket `System.Security.Cryptography.ProtectedData` tidak jadi ditambahkan

Rencana bagian 6.3 menyebut `PackageReference` ini wajib. Ternyata tidak: `net10.0-windows` sudah
membawa `ProtectedData`, dan NuGet menolak paketnya dengan **NU1510** ("will not be pruned...
likely unnecessary"). Paketnya sempat ditambahkan, terbukti tidak perlu, lalu dicabut lagi —
`Em.Ui.Wpf.Core.csproj` akhirnya **tidak berubah sama sekali**. `ProtectedData` tetap terpakai di
`RegistrySessionStorage`.

### 3.2 `SessionEnded` dari `ApiClient` dimarshal ke dispatcher

Rencana bagian 6.6 hanya menyebut `Dispatcher.Invoke` di `MainWindow`. Itu tidak cukup:
`EmApp.EndSessionAsync` memanggil `SetActiveUser(null)` → `ActiveUserChanged` →
`SpaNavigationHost.RefreshActiveUser()` → `NotifyChanged`, dan itu berjalan **lebih dulu** daripada
handler window. `ApiClient.SessionEnded` lahir di tengah request HTTP yang gagal di-refresh, jadi
bisa datang dari thread mana pun.

Karena itu `EmApp.OnApiClientSessionEnded` memeriksa `App?.Dispatcher` dan
`dispatcher.CheckAccess()` lebih dulu, baru `InvokeAsync` kalau perlu. `Dispatcher.Invoke` di
`MainWindow` dipertahankan sebagai lapis kedua — murah kalau sudah di thread UI. Tanpa perbaikan ini,
gejalanya justru baru muncul saat sesi mati sungguhan.

---

## 4. Status uji (langkah 12)

### 4.1 Sudah terbukti

Diuji dengan curl ke `http://localhost:5132` pada server yang benar-benar berjalan:

| Skenario | Harapan | Hasil |
| --- | --- | --- |
| `core/Handshake` tanpa token | 200 | **200** ✓ |
| `core/GetTimeStamp` tanpa token | 401 | **401** ✓ |
| `core/GetUlid` tanpa token | 401 | **401** ✓ |
| Action yang tidak ada | 404 | **404** ✓ |
| Tanpa token debug, `X-Em-User: {"cUserId":"999…"}` | 404 | **404** ✓ |
| Tanpa token debug, `X-Em-User: debugger` (bentuk lama) | 404 | **404** ✓ |
| Tanpa token debug, `X-Em-User: budi`, action tertutup | 401 | **401** ✓ |
| Header tidak terbaca (`{not json`), action tertutup | 401 | **401** ✓ |
| `Authorization: Bearer abc.def.ghi`, action publik | 200 | **200** ✓ |

Artinya: penegakan `IsPublicAction` jalan, tripwire menangkap kedua bentuk penyebutan akun debugger,
header yang rusak tidak pernah memberi identitas, dan access token yang tidak sah tidak menutup jalur
publik — yang penting supaya refresh token tetap bisa dikirim justru ketika access token-nya mati.

### 4.2 Belum teruji — terhalang akun

Semua skenario yang berawal dari sign in sungguhan **belum dijalankan sama sekali**:

- Sign in dengan password benar → masuk workspace, identitas tampil di panel user
- Sign in dengan password salah → satu pesan yang sama, tetap di layar login
- Access token dibiarkan kedaluwarsa (>15 menit) lalu aplikasi dipakai lagi → refresh berjalan sendiri
- Beberapa request bersamaan sesudah token kedaluwarsa → hanya satu refresh, semuanya lanjut
- Sign out lalu tekan apa saja di workspace → kembali ke layar login
- Sign out di satu tempat, request dari sesi yang sama → 401 seketika, bukan sesudah 15 menit
- Tutup aplikasi dengan "keep me signed in", buka lagi → langsung masuk tanpa mengetik password
- Sama, tapi sesinya sudah dicabut dari tempat lain → layar login, nama akun terisi, tanpa pesan merah
- Hak admin seorang user dicabut selagi ia login → sesinya berakhir
- Seluruh baris token debug + `X-Em-User` yang butuh baris pengguna sungguhan

**Sebabnya:** akun administrator bawaan di database dev (`<server-dev>`, katalog `EmDb`) sedang
**dimatikan**. `PostGetMeta_SignIn("admin", "admin")` dijawab 401 dari pemeriksaan saklar
(`AdminAccount.IsEnabledAsync`, `CredentialServices.cs:641`), bukan dari pemeriksaan password —
pesannya sengaja sama persis dengan password salah. Saklarnya adalah key metadata `AdminUserEnable`
dan memang hanya bisa diubah langsung di database; tidak ada satu pun action yang menyentuhnya, jadi
saklarnya **tidak disentuh**.

Untuk melanjutkan uji ini dibutuhkan salah satu dari: akun pengguna biasa berikut passwordnya, atau
saklar `AdminUserEnable` dinyalakan berikut password admin yang berlaku.

### 4.3 Belum teruji — perlu dijalankan tangan

Tiga skenario yang memang uji interaktif di mesin ini, bukan sesuatu yang bisa dibuktikan lewat curl:

- Build debug, koneksi debug, tanpa login → tetap masuk sebagai `debugger` seperti sekarang
- Build debug, `SimulateLogin` lalu sign in sungguhan → request memakai Bearer, bukan token debug
- Server HTTPS bersertifikat self-signed, `IgnoreSslErrors` mati → koneksi ditolak

---

## 5. Next step

1. **Selesaikan langkah 12.** Tentukan akun untuk uji sign in (lihat 4.2), lalu jalankan sisa
   matriks 8.1. Sisa uji ini sekarang dilacak di `doc/TODO-LIST.md` ("Uji gerbang claim dengan akun
   non-admin sungguhan").
2. ~~**Perbarui status di berkas plan.**~~ Selesai 2026-09-22: berkas plan dipindah ke
   `plan/executed/` berikut statusnya, tanpa menunggu uji 8.1 — fase 3, 4, dan 5 sudah dibangun di
   atasnya.
3. **Commit.** Belum ada satu pun perubahan fase ini yang di-commit.

### 5.1 Yang sengaja ditunda (dari rencana bagian 9)

- **Lapisan izin per action (`ta_UserClaim`).** Sesudah fase ini gerbang baru menjawab "siapa
  pemanggilnya", belum "apa yang boleh dikerjakannya". `IsAdmin` masih satu-satunya pembeda hak.
- **Refresh proaktif** sebelum access token kedaluwarsa. Reaktif sudah cukup dan lebih sedikit bagian
  yang bisa salah.
- **Menutup tab saat sesi berakhir.** Sekarang workspace hanya tersembunyi; data yang sedang diedit
  tidak ikut hilang. Menyimpannya lebih dulu adalah rancangan tersendiri.
- **Idle timeout di client.**
- **Layar daftar sesi** yang memakai `PostGetMeta_GetSessions`, berikut "sign out dari semua
  perangkat" (`PostMeta_SignOutAll`). Kontraknya sudah ada di kedua sisi, layarnya belum.
- **Panel debug** (saklar token debug, pemilih `ActiveUser`).
- **Provider eksternal** (Google/Microsoft).

### 5.2 Keputusan yang masih terbuka (dari rencana bagian 10)

- **Satu sesi per koneksi, atau satu sesi per aplikasi.** Sesi tinggal di `ApiClient`, jadi bentuk
  datanya sudah mendukung beberapa sesi hidup sekaligus. Yang belum diputuskan: apakah UI boleh
  berganti koneksi **selagi** sesi berjalan (sekarang `ActiveConnection` bisa berubah kapan saja
  lewat dialog koneksi), atau berganti koneksi selalu berarti keluar dulu. Catatan: `EndSessionAsync`
  meresolve `ICredentialServices` yang mengikuti `ActiveConnection`, jadi kalau user berpindah koneksi
  selagi masuk, sign out akan menyapa server yang salah.
- **Dua instance aplikasi di satu mesin.** Keduanya membaca refresh token tersimpan yang sama; yang
  lebih dulu memakainya merotasi token itu, dan yang belakangan jatuh ke layar login.
- **Entropy DPAPI** ditulis sebagai konstanta di kode. Kalau aplikasi di-rebrand per pelanggan,
  entropy yang sama di semua instalasi berarti blob satu instalasi bisa dibuka instalasi lain milik
  user Windows yang sama.

---

## 6. Daftar berkas

**Baru (5):**

```
src/backend/Em.Api.Core/Api/Core/ActionRequest.cs
src/shared/Em.Libs/Shared/UserHeaderProtocol.cs
src/shared/Em.Ui.Core/Ui.Core/shared/ISessionStorage.cs
src/shared/Em.Ui.Wpf.Core/Core/RegistrySessionStorage.cs
src/shared/Em.Ui.Wpf.Core/Core/SessionEndedEventArgs.cs
```

**Tersunting (20):**

```
src/backend/Em.Api.Core/Api/Core/ActionDefinition.cs
src/backend/Em.Api.Core/Api/Core/ApiCoreServices.cs
src/backend/Em.Api.Core/Api/Core/CredentialServices.cs
src/backend/Em.Api.Core/Api/Core/ISessionStore.cs
src/backend/Em.Api.Core/Api/Core/ITokenServices.cs
src/backend/Em.Api.Core/Api/Core/EmApp.cs
src/backend/Em.Api.Core/Api/Core/ServicesBase.cs
src/backend/Em.Api.Core/Api/Core/SessionStores.cs
src/backend/Em.Api.Core/Api/Core/TokenServices.cs
src/shared/Em.Libs/Defaults.cs
src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs
src/shared/Em.Ui.Core/Ui.Core/shared/ApiConnection.cs
src/shared/Em.Ui.Core/Ui.Core/shared/IApiClient.cs
src/shared/Em.Ui.Wpf.Core/Api.Core/CredentialService.cs
src/shared/Em.Ui.Wpf.Core/Controls/LoginControl.xaml
src/shared/Em.Ui.Wpf.Core/Controls/LoginControl.xaml.cs
src/shared/Em.Ui.Wpf.Core/Core/EmApp.Statics.cs
src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs
src/shared/Em.Ui.Wpf.Core/Dialogs/ConnectionConfigEditor.xaml
src/shared/Em.Ui.Wpf.Core/Windows/MainWindow.xaml.cs
```
