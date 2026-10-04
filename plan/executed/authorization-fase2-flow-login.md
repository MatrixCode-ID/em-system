# Plan — Authorization Fase 2: flow login di client dan penegakan di server

Status: **sudah dieksekusi** — langkah 1-11 selesai 2026-09-14; langkah 12 (uji jalur) baru sebagian.
Laporan eksekusi: `doc/report/authorization-fase2-eksekusi.md` - berikut dua penyimpangan yang
disengaja dan daftar skenario 8.1 yang belum teruji. Berkas ini semula ditahan di
`plan/unexecuted/` sampai uji 8.1 tuntas, sesuai langkah 12 di bagian 8; ia dipindah ke sini
2026-09-22 karena fase 3, 4, dan 5 sudah dibangun di atasnya, dan sisa uji 8.1 — yang menunggu satu
akun non-admin sungguhan — dilacak sebagai item tersendiri di
[doc/TODO-LIST.md](../../doc/TODO-LIST.md) ("Uji gerbang claim dengan akun non-admin sungguhan").
Dibuat: 2026-09-14
Baseline: commit `e9d7c5e` (Pindahkan debug token ke resources), branch `data-services`

Lanjutan dari `plan/executed/authorization-fase1-service-layer.md`,
`plan/executed/akun-admin-dan-sesi-sistem.md`, dan `plan/executed/debug-token-dev-bypass.md`.
Berkas ini mengerjakan langkah 2 dan 3 pada urutan yang ditulis di akhir rencana debug token:
token dibawa client, lalu penegakan dinyalakan.

> **Dokumen ini ditulis untuk dikerjakan tanpa konteks percakapan sebelumnya.** Nomor baris yang
> disebut adalah kondisi pada baseline di atas — kalau sudah bergeser, cari berdasarkan nama
> method/region, bukan nomornya.

---

## 1. Kenapa ada berkas ini

Lapisan service-nya sudah jadi, alurnya belum. Yang terjadi sekarang:

- Sign in sudah benar-benar diperiksa server, tapi **hasilnya dibuang**:
  `Em.Ui.Wpf.Core/Controls/LoginControl.xaml.cs:235` menulis
  `_ = await services.PostGetMeta_SignIn(UserName, Password)` dengan TODO yang menunjuk fase ini.
  Tidak ada satu pun request sesudahnya yang membawa bukti bahwa login itu pernah terjadi.
- **Penegakan belum menyala.** `IsPublicAction` direkam di `ActionDefinition` dan sudah dipasang
  dengan benar di setiap action, tapi `EmApp.ProcessRequest` tidak pernah membacanya. Artinya
  seluruh API masih terbuka untuk siapa pun yang bisa menyentuh host-nya — termasuk tabel pengguna
  dan seluruh action tulis.
- **`ActiveUser` tidak pernah terisi** sesudah login sungguhan. Satu-satunya yang memanggil
  `SetActiveUser` adalah `EmApp.Statics.cs:142` untuk akun debugger. Di build Release, panel
  identitas di `SpaNavigationHost` kosong dan header `X-Em-User` tidak pernah terkirim.
- **`PerformLogoff` masih stub kosong** (`Windows/MainWindow.xaml.cs:288`): isinya
  `await Task.CompletedTask`. Tidak ada yang mengakhiri sesi, tidak di client maupun di server.
- **Jalur yang membawa token belum aman.** `Defaults.DefaultHttpClientHandler:23` menerima
  sertifikat apa pun, dan `ApiConnection.IgnoreSslErrors` tidak pernah dibaca siapa-siapa — jadi
  saklarnya "selalu menyala" tanpa pernah dipilih user.

Sesudah fase ini: layar login menerbitkan sesi sungguhan, setiap request membawanya, access token
yang mati diperbarui sendiri tanpa mengganggu user, sign out benar-benar mengakhiri sesi, dan
action non-publik menolak pemanggil tanpa identitas.

---

## 2. Kondisi awal yang perlu diketahui

### 2.1 Yang sudah siap dan tidak perlu disentuh

| Aset | Letak | Catatan |
| --- | --- | --- |
| Penerbit/pemeriksa token | `Em.Api.Core/Api/Core/TokenServices.cs` | RS256, access 15 menit, refresh 30 hari, dirotasi tiap dipakai |
| Penyimpan sesi | `ISessionStore` + `UserSessionStore`/`SystemSessionStore` | dua tabel, dipilih di satu tempat |
| Action kredensial | `CredentialServices.cs` region `Meta's` | sign in, refresh, sign out, ganti/reset password — lengkap dengan pemeriksaan hak di dalamnya |
| Rangka gerbang | `EmApp.ProcessRequest` → `ResolveCallerAsync` | token debug + Bearer; sudah mengisi `CallerUserId`/`CallerSessionId`/`CallerIsAdmin` |
| Identitas di service | `ServicesBase.CallerUserId` dkk. | sudah dipakai `RequireCallerUserId()`, `RequireSelfOrAdminAsync()` |
| Status HTTP = status action | `EmApp.ToJsonResult:708` | `Results.Json(..., statusCode: result.StatusCode)`, jadi 401 sungguhan sampai ke client |
| Kegagalan ber-status di client | `Em.Ui.Core/Ui.Core/Extensions.cs` | `ProcessHttpResult` melempar `ActionException` berikut `StatusCode`-nya |
| Header identitas per-request | `ApiClient.BuildRequest` | sudah ada; ditambah `Authorization`, dan header identitas user berganti bentuk (lihat 4.5) |

### 2.2 Yang membuat gerbang belum menolak apa pun

`ResolveBearerCallerAsync` sengaja **tidak** menolak access token yang tidak sah — hanya menulis log
dan mengembalikan identitas kosong. Itu benar selama client belum mengirim token sama sekali.
Begitu langkah 2 (client membawa token) selesai, barulah langkah 3 (penegakan) boleh dinyalakan.
Urutan ini tidak boleh dibalik: menyalakan penegakan lebih dulu mematikan aplikasi WPF seluruhnya
kecuali handshake dan login.

---

## 3. Keputusan yang sudah diambil

Jangan dibuka ulang tanpa alasan baru.

| Hal | Keputusan |
| --- | --- |
| Umur sesi di client | Refresh token **disimpan lintas restart**, dienkripsi DPAPI (`CurrentUser`) dan ditaruh di Registry, aktif hanya kalau "keep me signed in" menyala |
| Access token dari sesi yang dicabut | **Ditolak seketika** — keaktifan sesi ikut diperiksa di setiap request yang membawa token |
| Cakupan | Flow login/logout backend + UI, **plus** pembenahan TLS di client. Lapisan izin per-action (`ta_UserClaim`) tetap fase terpisah |
| Tempat sesi hidup di client | Di `ApiClient`, satu sesi per koneksi — bukan variabel global. Satu aplikasi bisa memegang beberapa koneksi, dan token satu server tidak pernah boleh terkirim ke server lain |
| Pemicu refresh | Reaktif: 401 dari action non-refresh. Tidak ada timer, tidak ada refresh proaktif di fase ini |
| Jumlah refresh serentak | Satu. Beberapa request yang kena 401 bersamaan menunggu satu refresh yang sama |
| Kalau refresh gagal | Sesi dibuang, penyimpanan dibersihkan, aplikasi kembali ke layar login dengan keterangan singkat |
| Identitas `ActiveUser` sesudah login | Dibaca dari `GetVi_User_ById` memakai `cUserId` pada `TokenResult`; akun administrator bawaan memakai objek sintetis, sepadan dengan `CreateDebuggerUser` |
| TLS | `IgnoreSslErrors` mulai dihormati per koneksi; default-nya memvalidasi sertifikat |
| Isi header `X-Em-User` | Objek JSON berisi `cUserId` **dan** `cUserAccount`, bukan lagi nama akun polos. Bentuk nama-akun-polos tetap diterima server demi curl/Postman |
| Keterangan pemanggil di server | Satu objek `ActionRequest` yang dibangun gerbang, dibaca lewat `ServicesBase.Request` dan `EmApp.CurrentRequest` (read-only, dari `HttpContext.Items` — bukan field, karena `EmApp` singleton). `CallerUserId` dkk. tetap ada sebagai penerus |
| Pemeriksa hak | Ikut tinggal di `ActionRequest` (`RequireUserId`, `RequireAdmin`, `RequireSelfOrAdmin`, …), jadi tidak ada lagi query baris pengguna hanya untuk membaca `cUserIsAdmin`. Gantinya, mengubah `cUserIsAdmin` mencabut seluruh sesi user itu |

---

## 4. Rancangan — sisi server

### 4.1 Menyalakan penegakan `IsPublicAction`

Berkas: `src/backend/Em.Api.Core/Api/Core/EmApp.cs`, di `ProcessRequest` tepat sesudah
pemeriksaan `actionDef is null` (baris 176) dan sebelum pemeriksaan method:

```csharp
if (!actionDef.IsPublicAction && gate.Identity.UserId is null) {
   return ToJsonResult(BuildErrorActionResult(
      "This action requires a signed-in caller.", 401, actionDef.Type, routeLabel));
}
```

Letaknya sesudah pencarian route, bukan sebelum, karena `IsPublicAction` memang milik action-nya.
Konsekuensinya action yang ada tapi tertutup dijawab 401 sementara action yang tidak ada dijawab
404 — dari luar, keduanya bisa dibedakan. Itu diterima: yang benar-benar harus tidak terdeteksi
adalah mekanisme token debug, dan jalur itu tetap menjawab 404 apa pun route-nya karena
`ResolveCallerAsync` berjalan lebih dulu.

Access token yang rusak/kedaluwarsa tetap **tidak** menolak request di `ResolveBearerCallerAsync`.
Ia hanya tidak menghasilkan identitas — dan sesudah perubahan di atas, action non-publik menolaknya
sendiri. Yang tetap lewat adalah action publik, dan itu memang yang diinginkan: refresh token
dikirim justru ketika access token-nya sudah mati.

### 4.2 Keaktifan sesi diperiksa di setiap request

Tanpa ini, `PostMeta_SignOut` dan `PostMeta_SignOutAll` baru benar-benar berlaku setelah access
token kedaluwarsa sendiri — sampai 15 menit sesudah tombolnya ditekan.

Pemeriksaannya ditaruh di dalam `TokenServices.ValidateAsync` (baris 56), bukan di gerbang, supaya
tidak ada pemanggil yang bisa lupa melakukannya:

1. Tambah di `ISessionStore`: `Task<SessionRecord?> FindByIdAsync(string sessionId);`
   Implementasinya di `UserSessionStore` dan `SystemSessionStore` sejajar dengan `FindByHashAsync`
   yang sudah ada.
2. Tambah di `ITokenServices`: `Task<bool> IsSessionActiveAsync(string cUserSessionId);` — mencari
   di kedua store (yang bukan pemiliknya menjawab `null`), lalu `true` hanya kalau state-nya
   `Active` **dan** `Expiry` belum lewat.
3. Di `ValidateAsync`, sesudah claim `sub`/`sid` terbaca dan sebelum `TokenValidation` yang valid
   dikembalikan, panggil pemeriksaan itu. Kalau gagal:
   `new TokenValidation(false, null, null, false, "The session behind this access token is no longer active.")`

Biayanya satu query per request yang membawa token. Jalur token debug tidak terpengaruh sama
sekali — ia tidak punya `sid` dan tidak menyentuh `ValidateAsync`.

**Jebakan:** `RefreshAsync` mengganti baris sesi (rotasi), jadi `sid` di access token lama menunjuk
baris yang sudah `Revoked`. Itu memang perilaku yang benar — access token lama ikut mati begitu
refresh berhasil — tapi artinya client **tidak boleh** memakai access token lama lagi setelah
menerima yang baru. Lihat 5.3.

### 4.3 Inventaris action publik

Dengan penegakan menyala, daftar action publik menjadi daftar permukaan anonim aplikasi. Yang ada
sekarang:

| Action | Sekarang | Sesudah fase ini |
| --- | --- | --- |
| `core/Handshake` | publik | **tetap publik** — mendahului login; dipakai probe koneksi di layar login dan dialog koneksi |
| `core/GetServerPublicKey` | tertutup | tetap tertutup |
| `core/GetTimeStamp` | publik | **ditutup** |
| `core/GetUlid`, `core/GetUlidMany` | publik | **ditutup** |
| `core.credential/PostGetMeta_SignIn` | publik | tetap publik |
| `core.credential/PostGetMeta_RefreshToken` | publik | tetap publik |

Ketiga action yang ditutup tidak pernah dipanggil sebelum login: `EmApp.GetDateStampAsync`
(baris 348) dan pembuatan id baris baru semuanya berjalan di dalam workspace. Kalau nanti ada
kebutuhan pra-login yang membutuhkannya, buka lagi satu per satu berikut alasannya — jangan
membuka blok.

### 4.4 Pemanggil tanpa sesi

`PostMeta_SignOut` memakai `RequireCallerSessionId()`, dan token debug sengaja tidak punya sesi
(`CallerSessionId` selalu `null`). Jadi sign out lewat jalur debug menjawab 401 — benar apa adanya,
dan sisi UI yang harus tidak memanggilnya (lihat 6.5). Tidak ada perubahan di server untuk ini.

### 4.5 Header identitas: dari nama akun polos jadi objek JSON

`Defaults.UserHeader` (`X-Em-User`) sekarang berisi `cUserAccount` apa adanya. Mulai fase ini
isinya sebuah objek JSON yang membawa **keduanya**:

```
X-Em-User: {"cUserId":"01K5Z0X9P7QW3M8V2ND6TJHFAB","cUserAccount":"budi"}
```

Dua alasannya:

1. **Nama akun bisa berganti, id tidak.** Sekarang server harus menerjemahkan nama jadi baris
   pengguna hanya untuk tahu siapa yang dimaksud; dengan id ikut terbawa, yang dipakai adalah
   identitas yang memang permanen, dan namanya tinggal jadi keterangan yang terbaca manusia di log.
2. **Cabang Bearer akhirnya bisa memeriksa selisihnya tanpa biaya.** Catatan eksekusi
   `plan/executed/debug-token-dev-bypass.md` menulis bahwa perbandingan header dengan pemilik token
   sengaja dilewati justru karena menerjemahkan id jadi nama akun berarti satu query tambahan di
   setiap request. Dengan id ada di header, perbandingan itu jadi sekadar dua string.

**Bentuknya ditulis sekali, dipakai dua sisi** — `src/shared/Em.Libs/Shared/UserHeaderProtocol.cs`,
baru, bersebelahan dengan `ProbeProtocol` dan `DebugTokenProtocol` dengan alasan yang sama: client
yang menyusunnya dan server yang membacanya harus menyebut kata yang sama persis.

```csharp
public static string Create(string? cUserId, string? cUserAccount);
public static bool TryRead(string header, out string? cUserId, out string? cUserAccount);
```

- `Create` melewatkan properti yang kosong, jadi header tidak pernah memuat `"cUserId":null`.
- Serialisasinya memakai encoder bawaan `System.Text.Json`, yang meng-escape karakter non-ASCII jadi
  `\uXXXX` — nilai header karena itu selalu aman dikirim apa adanya, berapa pun isi nama akunnya.
- `TryRead` menerima **dua bentuk**: objek JSON di atas, dan — kalau teksnya tidak diawali `{` — satu
  nama akun polos yang mengisi `cUserAccount` saja. Bentuk kedua dipertahankan supaya pengembang
  tetap bisa mengetik `X-Em-User: budi` di curl/Postman tanpa menyusun JSON, dan supaya tripwire di
  bawah tetap menangkap header berbentuk lama.
- Header yang ada tapi tidak bisa dibaca sama sekali dianggap tidak menyebut siapa-siapa.

**Aturan yang tidak berubah sedikit pun:** header ini tidak pernah menjadi sumber identitas tanpa
token debug yang lolos. Menambahkan `cUserId` tidak melonggarkannya — kalau ia pernah dipercaya
sendirian, siapa pun cukup menyebut id mana saja untuk menjadi pemiliknya.

**Yang berubah di `ResolveCallerAsync`:**

1. Header dibaca lewat `UserHeaderProtocol.TryRead`, bukan `.ToString().Trim()`.
2. Tripwire ikut memeriksa id: request tanpa token debug yang menyebut akun `debugger` **atau**
   `Defaults.DebuggerUserId` dijawab 404, sama seperti sekarang.

**Yang berubah di `ResolveDebugCallerAsync`** — di sini token debug sudah lolos, jadi penolakannya
boleh berpesan jelas:

| Isi header | Yang dipakai |
| --- | --- |
| kosong | akun debugger, seperti sekarang |
| hanya `cUserAccount` | dicari lewat nama akun, seperti sekarang |
| hanya `cUserId` | dicari lewat id |
| keduanya | dicari lewat **`cUserId`**, lalu `cUserAccount` dicocokkan dengan baris yang ketemu; tidak cocok → 401 berpesan jelas |

Id yang menang saat keduanya ada, karena itu yang permanen. Ketidakcocokan ditolak, bukan didiamkan:
ia berarti client menyusun header dari dua sumber yang berbeda, dan menebak mana yang benar lebih
buruk daripada berhenti.

Akun sistem tetap dikenali lebih dulu dan tidak pernah dicari sebagai baris pengguna:
`Defaults.DebuggerUserId`/`DebuggerUserAccount` dan `Defaults.AdminUserId`/`AdminUserAccount`
dijawab dari konstanta dan dari saklar akun admin, persis seperti sekarang — sekarang keduanya
cocok baik disebut lewat id maupun lewat nama akun.

**Yang berubah di `ResolveBearerCallerAsync`:** `cUserId` pada header dibandingkan dengan pemilik
access token. Access token tetap yang menang — ada kondisi sah yang membuat keduanya berbeda,
misalnya request yang masih di udara saat pengguna berganti — dan selisihnya cukup ditulis
`LogDebug`, yang sekarang bisa menyebut kedua id yang sesungguhnya, bukan hanya nama akun di header.

> Bentuk header ini menggantikan yang dirancang di `plan/executed/debug-token-dev-bypass.md` bagian 5.
> Matriks gerbang di sana tetap berlaku apa adanya, dengan baris "`X-Em-User` menyebut akun lain"
> dibaca sebagai "menyebut akun lain lewat id, nama akun, atau keduanya".

### 4.6 Satu objek untuk seluruh keterangan pemanggil

Sekarang gerbang menghasilkan `CallerIdentity` — record struct private di `EmApp` berisi tiga
field — lalu memipihkannya jadi tiga properti di `ServicesBase`, satu baris assignment untuk
masing-masing. Fase ini saja sudah menambah dua keterangan baru (nama akun yang ikut di header, dan
apakah request datang lewat token debug), dan audit `ta_Log` yang sudah tercatat sebagai tindak
lanjut di rencana debug token membutuhkan semuanya sekaligus dalam satu genggaman. Pola "satu
keterangan baru = satu properti baru di kelas dasar + satu baris di `ProcessRequest`" tidak akan
berhenti tumbuh sendiri.

Jadi gerbang membangun satu objek, dan service menerimanya apa adanya:

```csharp
public enum CallerSource { None, AccessToken, DebugToken }

public sealed record ActionRequest
{
   public string? cUserId { get; init; }
   public string? cUserAccount { get; init; }
   public string? cUserSessionId { get; init; }
   public bool IsAdmin { get; init; }

   public CallerSource Source { get; init; }
   public bool IsDebugRequest => Source == CallerSource.DebugToken;
   public bool IsAuthenticated => cUserId is not null;

   // Terisi hanya di jalur token debug: nama key yang dipakai, dan apakah pengembangnya
   // sedang menyamar jadi akun lain alih-alih memakai akun debugger.
   public string? DebugKeyName { get; init; }
   public bool IsImpersonating { get; init; }

   public string RouteLabel { get; init; } = string.Empty;
   public string? CallerAddress { get; init; }
   public DateTime ReceivedAtUtc { get; init; }
}
```

Letaknya `src/backend/Em.Api.Core/Api/Core/ActionRequest.cs` — **bukan** `Em.Libs`. Client tidak
pernah mengirim objek ini; ia disusun server dari header yang masuk, jadi ia tidak punya urusan
dengan kontrak yang dipakai dua sisi.

**Namanya `ActionRequest`, bukan `UiRequest`.** Dua alasan: `Action` sudah jadi kata milik repo ini
untuk satuan yang dipanggil (`ActionDefinition`, `ActionResult`, `ActionException`, `[GetAction]`),
sehingga `ActionRequest` berdiri sebagai pasangan `ActionResult` — satu masuk, satu keluar; dan
pemanggilnya tidak selalu UI (curl, Postman, dan nanti mungkin server lain), jadi `Ui` akan salah
menyebut sejak hari pertama.

Di `ServicesBase`, tiga properti yang sudah ada **dipertahankan sebagai penerus**, bukan dihapus:

```csharp
public ActionRequest Request { get; internal set; } = new();

public string? CallerUserId => Request.cUserId;
public string? CallerSessionId => Request.cUserSessionId;
public bool CallerIsAdmin => Request.IsAdmin;
```

`RequireCallerUserId()`, `RequireSelfOrAdminAsync()`, dan `RequireAdminAccountAccessAsync()` di
`CredentialServices` sudah memakai ketiganya dan tidak perlu disentuh sama sekali, sementara sumber
kebenarannya tetap satu. Yang hilang hanya setter-nya: `ProcessRequest` sekarang mengisi satu
properti, bukan tiga. `CallerGate` ikut menyusut jadi `(ActionRequest Request, ActionResult? Rejection)`
dan `CallerIdentity` yang private dihapus — objek ini menggantikannya seutuhnya.

**`cUserAccount` diisi hanya kalau terbukti.** Jalur token debug mengisinya sesudah baris penggunanya
benar-benar ketemu. Jalur access token **membiarkannya `null`**: token hanya membawa id, dan
menerjemahkannya jadi nama akun berarti satu query tambahan di setiap request — alasan yang sama yang
membuat perbandingan di 4.5 dulu dilewati. Nama akun yang menempel di header **tidak** dipakai
mengisinya, karena ia datang dari pemanggil, bukan dari data; yang boleh dipercaya cuma id yang
dibuktikan token. Kalau nanti ada module yang benar-benar membutuhkan namanya, jawabannya satu
pencarian yang disengaja berikut cache-nya, bukan menyalin isi header.

**Yang sengaja tidak ikut masuk:** `App`, `HttpContext`, dan `Logger` tetap properti sendiri di
`ServicesBase`. Ketiganya alat kerja service, bukan keterangan tentang request — mencampurnya
membuat objek ini jadi tempat penampungan apa saja, yang justru persoalan yang sedang diselesaikan.

**Objeknya juga dibuka lewat `EmApp`,** supaya kode yang tidak menurunkan `ServicesBase` — service
internal Engine, helper, dan nanti penulis audit — bisa membacanya tanpa dioper-oper sebagai
parameter:

```csharp
public ActionRequest? CurrentRequest =>
   _httpContextAccessor.HttpContext?.Items[RequestItemKey] as ActionRequest;
```

**Jebakan yang harus dihindari di sini, dan ini bukan urusan sepele:** `EmApp` di backend hidup
sebagai **singleton**. Menyimpan objek ini di sebuah field lalu diisi `ProcessRequest` berarti dua
request yang berjalan bersamaan saling menimpa identitas — satu request bisa dikerjakan atas nama
pemanggil request yang lain, dan gejalanya hanya muncul saat ada beban. Karena itu **tidak ada
field**: gerbang menaruh objeknya di `HttpContext.Items` sekali, dan properti di atas membacanya
ulang setiap kali diakses.

Ini persis mekanisme yang sudah dipakai `EmApp.ServiceProvider` di kelas yang sama — properti itu
juga sengaja membaca ulang `_httpContextAccessor.HttpContext` tiap diakses alih-alih menyimpannya,
dengan alasan yang identik, dan alasannya sudah ditulis di XML doc-nya. `_httpContextAccessor` jadi
sudah tersedia dan tidak perlu ditambahkan.

Di luar request — startup, penyemaian `ta_Meta`, pekerjaan latar — `CurrentRequest` bernilai `null`,
dan itu jawaban yang benar: di sana memang tidak ada yang memanggil. Properti ini **read-only**;
satu-satunya yang menulis adalah gerbang, dan `ActionRequest` sendiri immutable (record ber-`init`),
jadi yang menerimanya tidak bisa mengubah isinya.

`ServicesBase.Request` tetap ada dan menunjuk instance yang sama — untuk kode di dalam service, satu
properti tanpa null jauh lebih enak dipakai daripada menembus `App.CurrentRequest` yang nullable.

### 4.7 Pemeriksa hak ikut berkumpul di objek yang sama

Kalau datanya sudah satu objek, pemeriksa yang tidak membaca apa pun selain data itu tempatnya di
sana juga — kalau tidak, keterangannya terkumpul di satu tempat sementara aturannya tetap tersebar.
Sekarang keempatnya tinggal sebagai method private di `CredentialServices` region `Meta helpers`,
dan module berikutnya yang membutuhkan hal yang sama akan menyalinnya.

Yang **pindah** ke `ActionRequest`, di berkas yang sama:

```csharp
public bool IsSelf(string cUserId);
public string RequireUserId();                    // 401 kalau belum masuk
public string RequireSessionId();                 // 401 kalau belum masuk
public void RequireAdmin();                       // 401 kalau belum masuk, 403 kalau bukan admin
public void RequireSelfOrAdmin(string cUserId);   // dirinya sendiri selalu boleh
```

Bentuknya method pada record-nya, bukan kelas extension terpisah, karena inilah yang paling sering
disentuh penulis module: `Request.RequireAdmin()` terbaca apa adanya dan ketemu lewat IntelliSense
tanpa `using` tambahan. `RsaKeyPair` sudah jadi contoh di repo ini bahwa tipe data boleh membawa
kelakuannya sendiri.

Beda 401 dan 403 yang sekarang sudah ditulis di komentar helper lama **wajib ikut pindah beserta
alasannya**: belum terbukti siapa → 401; sudah jelas siapa tapi tidak berhak → 403. Client yang
memperbarui token setiap kali kena 401 (5.3) akan mengejar token baru terus-menerus untuk permintaan
yang memang tidak akan pernah diizinkan, kalau yang kedua ikut dijawab 401.

Yang **tetap tinggal** di `CredentialServices`, karena menyentuh database atau urusan module-nya
sendiri: `RequireUserAsync`, `SignInAdminAsync`, `EnsureAccountNameIsNotReserved`, `Tokens`,
`Hasher`. `RequireAdminAccountAccessAsync` menyusut jadi dua baris — `Request.RequireAdmin()` lalu
pemeriksaan saklar `AdminAccount.IsEnabledAsync(ctx)` — dan bagian yang membaca baris pengguna
hilang seluruhnya.

**Yang ikut hilang: satu query per pemanggilan.** `RequireSelfOrAdminAsync` dan
`RequireAdminAccountAccessAsync` sekarang menarik baris pengguna hanya untuk membaca `cUserIsAdmin`,
padahal gerbang sudah membawanya dari claim `adm` di token. Sesudah pindah, keduanya tidak menyentuh
database sama sekali — `RequireSelfOrAdminAsync` bahkan berhenti jadi `async`, dan
`PostMeta_ResetPassword` serta `PostGetMeta_GetSessions` memanggilnya tanpa `await`.

Harganya: claim `adm` adalah potret saat sesi dibuka, jadi admin yang dicabut haknya di tengah jalan
masih terbaca admin sampai access token-nya kedaluwarsa. Itu ditutup dengan aturan yang sudah
berlaku di repo ini untuk hal yang sama: **mengubah `cUserIsAdmin` mencabut seluruh sesi milik user
itu** (`Tokens.RevokeAllAsync`) di `PostTa_User_Update` dan `PostTa_User_UpdateBatch` — persis
alasan yang membuat ganti password mencabut sesi. Apa yang diakui token tidak boleh berbeda dari
apa yang tertulis di data; kalau datanya berubah, sesinya yang berhenti.

### 4.8 Dari mana module mengambilnya

Ada tiga jalan, dan yang dipakai ditentukan oleh bentuk kelas yang membutuhkannya — bukan selera.
Urutannya dari yang paling sering dipakai:

**1. Di dalam service module — `Request`.** Ini jalur normal dan yang dipakai hampir semua kode
module. Kelas service sudah menurunkan `ServicesBase`, jadi propertinya ada begitu saja:

```csharp
[PostAction]
public async Task PostTa_Order_Approve(ta_Order data) {
   Request.RequireAdmin();
   ...
}
```

Di dalam sebuah action, `Request` **tidak pernah null** — gerbang berjalan sebelum action dipanggil,
dan `ProcessRequest` mengisinya sebelum menyerahkan kendali. Kalau isinya `ActionRequest.None`
(lihat di bawah), itu berarti kodenya dipanggil dari luar jalur request, bukan berarti gerbangnya
bolong.

**2. Kelas pembantu yang dibuat lewat DI — minta di konstruktor.** `ActionRequest` didaftarkan
sebagai **scoped** di `EmApp.InitInternalServices`, sejajar dengan `ITokenServices` yang sudah ada
di sana:

```csharp
Services.AddScoped(sp => sp.GetRequiredService<EmApp>().CurrentRequest ?? ActionRequest.None);
```

sehingga kelas pembantu module cukup menuliskannya seperti dependency lain:

```csharp
public class OrderPolicy(ActionRequest request, EmDbContext ctx) { ... }
```

Scope-nya adalah scope milik request (ASP.NET Core membuat satu per request, dan itu pula yang
dikembalikan `EmApp.ServiceProvider`), jadi satu request = satu objek, dan karena objeknya
immutable tidak ada yang bisa saling menimpa. Yang diresolve saat action berjalan selalu mendapat
yang sudah terisi, karena gerbang berjalan lebih dulu.

**3. Kode yang tidak punya keduanya — dioper sebagai parameter.** Method statis, extension, dan
fungsi murni menerima `ActionRequest` sebagai argumen biasa. Jangan menembus `EmApp.CurrentRequest`
dari tempat semacam itu hanya supaya daftar parameternya pendek: yang terjadi adalah kelas yang
diam-diam bergantung pada ada-tidaknya request, dan itu baru ketahuan saat dipanggil dari luar.

`EmApp.CurrentRequest` sendiri **bukan** jalur untuk module. Ia disediakan untuk Engine dan untuk
hal yang memang melintasi request — penulis audit, log enricher, pendaftaran DI di atas — dan
tipenya sengaja nullable supaya pemakainya dipaksa memikirkan keadaan "sedang tidak ada request".

**`ActionRequest.None`** adalah satu instance statis yang seluruh identitasnya kosong dan
`IsAuthenticated`-nya `false`. Dipakai sebagai nilai awal `ServicesBase.Request` dan sebagai jawaban
di luar request. Dengan begitu tidak ada satu pun jalur yang memaksa pemanggil memeriksa null lebih
dulu, dan pemeriksa hak di 4.7 menjawab 401 untuk semuanya — yang memang benar: tidak ada request
berarti tidak ada yang terbukti.

**Satu larangan:** jangan menyimpan `Request` ke field milik objek yang hidup lebih lama dari
request-nya, dan jangan membawanya ke pekerjaan latar. Isinya aman dibaca kapan saja karena
immutable, tapi ia potret satu request — dipakai di tempat lain, ia menjawab pertanyaan yang tidak
pernah ditanyakan di sana.

---

## 5. Rancangan — sesi di `ApiClient`

Berkas: `src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs` dan `IApiClient.cs`.

### 5.1 Status sesi

`ApiClient` adalah transport resmi dan sudah dipegang satu per koneksi oleh
`EmApp.GetActiveApiClient` (baris 97), jadi di sinilah sesi tinggal:

```csharp
public string? AccessToken { get; private set; }
public string? RefreshToken { get; private set; }
public DateTimeOffset AccessTokenExpiresAtUtc { get; private set; }
public string? SessionUserId { get; private set; }
public bool HasSession => !string.IsNullOrEmpty(AccessToken);

public void SetSession(TokenResult token);
public void ClearSession();

// Dipicu setiap kali isi sesi berganti - termasuk hasil rotasi refresh.
public event EventHandler? SessionChanged;
// Dipicu sekali saat sesi mati dan tidak bisa dipulihkan.
public event EventHandler? SessionEnded;
```

Keempat properti pertama **private set**: yang boleh mengubahnya hanya `SetSession`/`ClearSession`
dan jalur refresh di dalam class ini. Seluruh anggota di atas ikut masuk `IApiClient` — kontraknya
memang kontrak transport, dan pemanggil yang cuma memegang interface tetap harus bisa membuka dan
menutup sesi.

### 5.2 Header `Authorization` dan header identitas

Di `BuildRequest`, sesudah header token debug yang sudah ada:

```csharp
if (!string.IsNullOrWhiteSpace(AccessToken)) {
   request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AccessToken}");
}
```

Tetap per-request, dengan alasan yang sama seperti dua header sebelumnya: isinya berubah saat
aplikasi berjalan, dan `DefaultRequestHeaders` akan membuat token lama ikut terkirim oleh request
yang disusun sebelum rotasi.

Di baris yang sama, header identitas berganti bentuk mengikuti 4.5. `ApiClient` mendapat properti
`ActiveUserId` **di samping** `ActiveUserAccount` yang sudah ada — ditambah, bukan diganti namanya —
dan keduanya disusun jadi satu nilai:

```csharp
if (UserHeaderProtocol.Create(ActiveUserId, ActiveUserAccount) is { Length: > 0 } identity) {
   request.Headers.TryAddWithoutValidation(Defaults.UserHeader, identity);
}
```

Header tetap tidak dikirim sama sekali kalau keduanya kosong, seperti sekarang. XML doc
`ActiveUserAccount` yang berbunyi "dikirim di setiap request lewat header" perlu diperbarui: yang
dikirim sekarang adalah keduanya sebagai satu objek, dan keterangan bahwa nama itu sendiri bukan
bukti apa-apa tetap berlaku kata demi kata.

### 5.3 Refresh otomatis, satu untuk semua

Ketiga jalur request (`GetAsync<T>`, `PostAsync`, `PostAsync<T>`) sekarang memanggil
`HttpClient.SendAsync(...).ProcessHttpResult<T>()` langsung. Semuanya dialihkan lewat satu
pembungkus:

```csharp
private async Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory)
```

Alurnya:

1. Kalau belum ada sesi (`!HasSession`) — kirim sekali, apa adanya. Tidak ada yang bisa di-refresh.
2. Catat `AccessToken` yang dipakai (`attemptedToken`), kirim, kembalikan hasilnya.
3. Kalau yang keluar `ActionException` ber-`StatusCode == 401`:
   a. jalankan `EnsureRefreshedAsync(attemptedToken)`;
   b. kalau berhasil, **susun ulang** request dari factory dan kirim sekali lagi;
   c. kalau gagal, atau 401-nya berulang, sesi dibuang (`ClearSession`), `SessionEnded` dipicu, dan
      `ActionException` terakhir dilempar ke pemanggil.

`HttpRequestMessage` tidak bisa dipakai dua kali — karena itu factory, bukan objek. Ini juga alasan
`StringContent` pada jalur POST harus dibangun **di dalam** factory, bukan sebelum.

Percobaan ulang **maksimal satu kali**. Tidak ada loop: 401 kedua sesudah token baru berarti
masalahnya bukan token.

`EnsureRefreshedAsync`:

```csharp
private readonly SemaphoreSlim _refreshGate = new(1, 1);

private async Task<bool> EnsureRefreshedAsync(string? attemptedToken) {
   await _refreshGate.WaitAsync();
   try {
      // Ada yang sudah memperbarui sementara kita antre. Tidak boleh memakai refresh token lagi -
      // ia sudah dirotasi, dan memakainya kedua kali justru mematikan sesinya sendiri.
      if (!string.Equals(AccessToken, attemptedToken, StringComparison.Ordinal)) return HasSession;
      if (string.IsNullOrEmpty(RefreshToken)) return false;
      // ... kirim refresh, SetSession(hasilnya), return true
   }
   finally { _refreshGate.Release(); }
}
```

Perbandingan `AccessToken != attemptedToken` inilah inti pengamannya. Refresh token dirotasi setiap
dipakai, jadi lima request yang kena 401 bersamaan tanpa pengaman ini akan mengirim lima refresh:
yang pertama berhasil, empat sisanya memakai token yang sudah mati dan **mencabut sesinya sendiri**.

### 5.4 Rute refresh

Refresh dikirim oleh `ApiClient` sendiri, lewat jalur mentah yang tidak melewati pembungkus di 5.3
dan tidak memasang header `Authorization`:

- module: konstanta baru `Defaults.CredentialModuleName = "core.credential"`, dipakai juga oleh
  atribut `[Module(...)]` di kedua sisi supaya tidak ada dua tempat yang bisa berbeda;
- action: `nameof(ICredentialServices.PostGetMeta_RefreshToken)` — interface-nya ada di `Em.Libs`,
  yang memang sudah direferensi `Em.Ui.Core`, jadi nama action-nya tidak perlu diketik ulang.

Alternatif yang sempat dipertimbangkan — menyuntikkan delegate refresh dari luar — ditolak karena
delegate itu akan memanggil `ICredentialServices` yang jalannya kembali lewat `ApiClient`, dan
401 di dalamnya memicu refresh lagi. Rekursinya tidak punya dasar berhenti yang jelas.

### 5.5 Sesi berakhir

`SessionEnded` dipicu tepat sekali, saat refresh gagal atau saat refresh token memang tidak ada.
Yang mendengarkan adalah `EmApp` (lihat 6.6). `ApiClient` tidak tahu apa-apa soal window maupun
layar login — ia hanya mengumumkan bahwa yang dipegangnya sudah tidak berlaku.

---

## 6. Rancangan — sisi aplikasi WPF

### 6.1 `EmApp` sebagai satu-satunya pintu sesi

Berkas: `src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs`. Dua method dan satu event baru; seluruh layar
memakai ketiganya — tidak ada yang menyentuh `ApiClient.SetSession` langsung:

```csharp
public async Task BeginSessionAsync(TokenResult token, bool remember);
public async Task EndSessionAsync(bool notifyServer);
public event EventHandler<SessionEndedEventArgs>? SessionEnded;
```

`BeginSessionAsync`:

1. `GetActiveApiClient()!.SetSession(token)`;
2. memuat `ActiveUser` (6.2) lalu `SetActiveUser(...)`;
3. kalau `remember`, menyimpan refresh token (6.3); kalau tidak, membersihkan yang mungkin tersimpan
   untuk profil itu;
4. berlangganan `SessionChanged` (menyimpan ulang hasil rotasi) dan `SessionEnded`.

Kalau langkah 2 gagal, seluruhnya dibatalkan: `ClearSession`, penyimpanan dibersihkan, exception-nya
naik ke pemanggil. Setengah masuk lebih buruk daripada gagal masuk.

`EndSessionAsync(notifyServer)`:

1. kalau `notifyServer` dan sesinya sesi sungguhan — `PostMeta_SignOut()`, **dibungkus try/catch dan
   kegagalannya diabaikan**: server yang tidak bisa dihubungi tidak boleh menahan user di dalam
   aplikasi;
2. `ClearSession()`, penyimpanan untuk profil itu dihapus;
3. `SetActiveUser(null)`, `ActiveUserChanged` ikut jalan;
4. memicu `SessionEnded` supaya window kembali ke layar login.

`SetActiveUser` (baris 207) sekarang bertipe `SetActiveUser(User user)` dan `ActiveUser` dideklarasi
`null!`. Keduanya dilonggarkan jadi `User?` — sesudah fase ini "belum ada yang masuk" adalah keadaan
normal yang bisa datang dua kali (sebelum login, dan sesudah sign out), bukan lagi keadaan sementara
saat startup.

Dua tempat yang menulis identitas ke client — `SetActiveUser` (baris 213) dan `GetActiveApiClient`
(baris 108) — sekarang mengisi **keduanya**, `ActiveUserId` dan `ActiveUserAccount`, karena header
identitas membawa keduanya (4.5). Keduanya diisi dan dikosongkan berbarengan; mengisi separuh berarti
mengirim header yang menyebut orang yang berbeda dari yang dimaksud.

### 6.2 Memuat `ActiveUser`

`TokenResult.cUserId` sudah membawa pemilik sesi, jadi:

- akun biasa: `GetVi_User_ById(cUserId)` lalu `User.Build(app, data)` — action-nya tertutup, dan
  pada titik ini access token sudah terpasang, jadi ia lolos;
- akun administrator bawaan (`Defaults.AdminUserId`): tidak punya baris pengguna. Dibuatkan objek
  sintetis di client, persis sepola `CreateDebuggerUser` (`EmApp.Statics.cs:147`), dengan
  `cUserAccount = Defaults.AdminUserAccount` dan nama tampilan `System Administrator`. Ini pekerjaan
  yang sudah ditulis "menyusul" di `plan/executed/akun-admin-dan-sesi-sistem.md`;
- akun debugger tidak lewat sini sama sekali — ia tidak pernah punya `TokenResult`.

`User.GetUser_ById` melempar `SystemAccountException` untuk kedua id sistem, jadi pemeriksaan id
sistem harus dilakukan **sebelum** pemanggilan itu, bukan dengan menangkap exception-nya.

### 6.3 Penyimpanan refresh token

Dua bagian, karena DPAPI hanya ada di Windows sementara `Em.Ui.Core` sengaja bebas framework:

**Kontrak** di `Em.Ui.Core` (`Ui.Core/shared/ISessionStorage.cs`, baru):

```csharp
public sealed record SavedSession(string RefreshToken, string cUserId, string cUserAccount);

public interface ISessionStorage
{
   void Save(string profileName, SavedSession session);
   SavedSession? Load(string profileName);
   void Clear(string profileName);
}
```

**Implementasi** di `Em.Ui.Wpf.Core` (`Core/RegistrySessionStorage.cs`, baru):

- menulis ke subkey koneksi yang sudah ada (`HKCU\{ApplicationName}\Api Connections\{ProfileName}`),
  sebagai `REG_BINARY` bernama `Session`;
- isinya `ProtectedData.Protect(payload, entropy, DataProtectionScope.CurrentUser)` — terikat ke akun
  Windows yang sedang berjalan, jadi isi Registry yang disalin ke mesin lain tidak berguna;
- `entropy` sebuah konstanta byte milik aplikasi. Ia bukan rahasia dan tidak berpura-pura jadi
  rahasia; gunanya memastikan blob aplikasi ini tidak ikut terbuka oleh aplikasi lain yang kebetulan
  memanggil `Unprotect` dengan entropy kosong;
- `Load` yang gagal membuka blob (`CryptographicException`) menghapus nilainya lalu menjawab `null` —
  blob yang tidak bisa dibuka tidak akan pernah bisa dibuka lagi;
- butuh `PackageReference System.Security.Cryptography.ProtectedData` di `Em.Ui.Wpf.Core.csproj`.

Konsekuensi yang disengaja dari menumpang subkey koneksi: mengganti nama atau menghapus profil ikut
membuang sesi tersimpannya (`DeleteApiConnection` menghapus subtree), sementara mengedit profil tidak
— `AddApiConnection` menulis field-nya satu per satu. Keduanya perilaku yang benar.

Registrasi DI: `Services.AddSingleton<ISessionStorage, RegistrySessionStorage>()` di `BuildApp`,
mengikuti aturan bahwa service hanya didaftarkan saat aplikasi dibangun.

**Rotasi wajib ikut tersimpan.** Setiap refresh yang berhasil menerbitkan refresh token baru dan
mematikan yang lama. Kalau yang tersimpan tidak ikut ditimpa, restart berikutnya memakai token mati
dan user terlempar ke layar login tanpa sebab yang kelihatan. Itulah kenapa `SessionChanged` ada,
dan kenapa `BeginSessionAsync` berlangganan ke sana — bukan menyimpan sekali saja saat login.

### 6.4 Startup: memulihkan sesi tersimpan

`MainWindow.InitLayout` (baris 72) sekarang menulis `Vm.IsSignedIn = _app.IsDebugMode`. Ditambah
satu jalur, dijalankan sesudah daftar koneksi termuat:

1. `RememberSignIn` harus menyala, dan `EmApp.RememberedProfileName` (nilai Registry **baru**,
   sepasang dengan `RememberedUserName` yang sudah ada) harus menunjuk profil yang masih ada;
2. `ISessionStorage.Load(profile)` harus memberi sesi tersimpan;
3. profil itu dijadikan `ActiveConnection`, lalu `PostGetMeta_RefreshToken(saved.RefreshToken)`
   dipanggil. Action-nya publik, jadi tidak butuh apa pun selain token itu;
4. berhasil → `BeginSessionAsync(hasil, remember: true)` dan `IsSignedIn = true`;
   gagal → penyimpanan dibersihkan, layar login tampil dengan nama akun terisi dan **tanpa** pesan
   error merah (sesi kedaluwarsa bukan kesalahan user).

Langkah 3 memakan waktu jaringan, jadi layar login tetap digambar lebih dulu dalam keadaan sibuk
("Restoring session…"), bukan window kosong yang menunggu.

### 6.5 Layar login, sign out, dan mode debug

**`LoginControl.SignInCommand`** (baris 214-250) — bagian `_ = await ...` diganti:

```csharp
var token = await services.PostGetMeta_SignIn(UserName, Password);
await EmApp!.BeginSessionAsync(token, RememberMe);
EmApp!.RememberedUserName = RememberMe ? UserName : null;
EmApp!.RememberedProfileName = RememberMe ? SelectedConnection!.ProfileName : null;
SignInSucceeded?.Invoke();
```

Penangkap exception yang sudah ada tetap: 401 → `InvalidCredentialsMessage`, sisanya → pesan
"server tidak bisa dihubungi" berikut detailnya. Yang perlu diperhatikan, `BeginSessionAsync` bisa
gagal **sesudah** password benar (mis. `GetVi_User_ById` gagal); kegagalan itu jatuh ke cabang kedua,
dan itu tepat — yang salah memang bukan kredensialnya.

**`MainWindowVm.PerformLogoff`** (baris 288) diisi:

```csharp
await EmApp!.EndSessionAsync(notifyServer: true);
```

Tampilan kembali ke layar login lewat `SessionEnded` (6.6), bukan dengan menulis `IsSignedIn` di
sini — supaya sign out yang diminta user dan sesi yang mati sendiri melewati jalan yang sama persis.

**Mode debug.** Selama `IsDebugMode`, identitas datang dari token debug dan tidak ada sesi sama
sekali. Karena itu:

- `PerformLogoff` di mode debug tidak memanggil `PostMeta_SignOut` — `EndSessionAsync` sudah
  memeriksa `HasSession` sebelum memberi tahu server, dan server memang akan menjawab 401 karena
  `CallerSessionId` untuk jalur debug selalu `null`;
- `SimulateLogin` (baris 352) tetap apa adanya: murni simulasi tampilan;
- **saat sesi sungguhan dibuka di atas koneksi debug**, `ApiConnection.DebugToken` untuk koneksi itu
  dikosongkan selama sesinya hidup, lalu dikembalikan saat sesi berakhir. Tanpa ini alur JWT tidak
  pernah benar-benar teruji dari build dev: gerbang memeriksa token debug **paling depan**, jadi
  Bearer yang menyertainya tidak akan pernah terbaca. Nilai tokennya disimpan di field `EmApp`
  selama sesi berjalan, bukan dibangkitkan ulang.

### 6.6 Kembali ke layar login

`MainWindow` berlangganan `EmApp.SessionEnded`:

```csharp
_app.SessionEnded += (_, e) => Dispatcher.Invoke(() => {
   Vm.IsSignedIn = false;
   Vm.SessionEndedNotice = e.Reason;   // null kalau user sendiri yang sign out
});
```

`Dispatcher.Invoke` wajib: `SessionEnded` bisa datang dari thread mana pun — ia lahir di tengah
request HTTP yang gagal di-refresh.

`SyncLoginHost` sudah membuang dan membangun ulang `LoginControl` setiap kali layar login muncul,
jadi formnya selalu bersih dan tidak ada sisa state dari sesi sebelumnya. `SessionEndedNotice`
diteruskan ke `LoginControl.Vm` supaya ada keterangan "Your session has ended. Please sign in
again." — dan dibedakan dari `SignInError` yang berwarna merah, karena ini bukan kegagalan user.

Tab yang sedang terbuka **tidak** ditutup di fase ini; workspace hanya tersembunyi. Membersihkan tab
beserta data yang sedang diedit adalah keputusan tersendiri, dan ditulis di bagian 9.

---

## 7. Rancangan — TLS

Berkas: `src/shared/Em.Libs/Defaults.cs` dan `Em.Ui.Core/Ui.Core/shared/ApiConnection.cs`.

`Defaults.DefaultHttpClientHandler` (baris 23) sekarang **selalu** memasang
`DangerousAcceptAnyServerCertificateValidator`. Itu satu-satunya handler yang dipakai
`ApiClient.Create(connection)`, jadi tidak ada satu pun koneksi yang memvalidasi sertifikat —
termasuk koneksi yang mulai membawa token sesudah fase ini.

Perubahannya:

1. `DefaultHttpClientHandler` **berhenti** mematikan validasi; ia jadi handler biasa.
2. Tambah **overload** (bukan rename) di kelas yang sama:
   `public static HttpClientHandler CreateHttpClientHandler(bool ignoreSslErrors)` — hanya bentuk
   inilah yang bisa memasang callback berbahaya itu, dan pemanggilnya harus menyebut niatnya.
3. `ApiConnection.CreateApiClient()` (baris 68) jadi
   `ApiClient.Create(this, Defaults.CreateHttpClientHandler(IgnoreSslErrors))` — barulah saklar
   `IgnoreSslErrors` yang sudah lama tersimpan di Registry berarti sesuatu.
4. `ConnectionConfigEditor` memberi keterangan di sebelah checkbox-nya: mematikan validasi membuat
   handshake RSA di atasnya ikut kehilangan arti, karena pihak di tengah bisa menyisipkan key
   miliknya sendiri. Kalimatnya sudah ada di XML doc `ApiClient.HandshakeAsync`; tinggal diringkas.

XML doc `ApiClient.HandshakeAsync` yang menyebut `IgnoreSslErrors` tetap benar sesudah perubahan ini
dan tidak perlu diubah.

---

## 8. Urutan eksekusi

Urutannya mengikat: langkah 10 tidak boleh dijalankan sebelum 1-9 selesai, kalau tidak aplikasi WPF
mati total di tengah pengerjaan.

1. `Defaults`: konstanta `CredentialModuleName`, `CreateHttpClientHandler(bool)`, dan
   `DefaultHttpClientHandler` yang sudah memvalidasi. Pasang konstanta module di kedua atribut
   `[Module("core.credential")]`.
2. `ApiConnection.CreateApiClient` memakai handler per koneksi; keterangan di `ConnectionConfigEditor`.
3. **Bentuk identitas pemanggil (4.5 + 4.6).** Satu langkah, karena ketiganya menyentuh method yang
   sama persis dan bentuk headernya harus berganti serentak di kedua sisi:
   a. `ActionRequest` + `CallerSource` berikut pemeriksa haknya (4.7), `ServicesBase.Request` dan
      tiga properti penerusnya, `EmApp.CurrentRequest` lewat `HttpContext.Items`, `CallerGate`
      menyusut dan `CallerIdentity` dihapus. Empat helper di `CredentialServices` region
      `Meta helpers` dialihkan ke sana, `ActionRequest.None` + pendaftaran scoped-nya di
      `EmApp.InitInternalServices` (4.8), dan `PostTa_User_Update`/`PostTa_User_UpdateBatch` mulai
      mencabut sesi saat `cUserIsAdmin` berubah;
   b. `UserHeaderProtocol` di `Em.Libs`, pembacaan baru di `ResolveCallerAsync`,
      `ResolveDebugCallerAsync`, dan `ResolveBearerCallerAsync` — yang sekarang mengisi objek di (a);
   c. `ActiveUserId` di `ApiClient` berikut penyusunan headernya, dan pengisian keduanya di
      `SetActiveUser`/`GetActiveApiClient`. XML doc `Defaults.UserHeader` dan
      `ApiClient.ActiveUserAccount` ikut diperbarui.
4. Server: `ISessionStore.FindByIdAsync` di kedua store, `ITokenServices.IsSessionActiveAsync`,
   pemeriksaan sesi di `TokenServices.ValidateAsync`.
5. `ApiClient`: status sesi, `SetSession`/`ClearSession`, dua event, header `Authorization`.
6. `ApiClient`: pembungkus `SendAsync<T>` + `EnsureRefreshedAsync` + jalur refresh mentah. Ketiga
   jalur request dialihkan ke pembungkus. `IApiClient` diperbarui.
7. `ISessionStorage` + `RegistrySessionStorage` + paket `System.Security.Cryptography.ProtectedData`
   + pendaftaran DI.
8. `EmApp`: `ActiveUser` jadi nullable, `BeginSessionAsync`/`EndSessionAsync`/`SessionEnded`,
   `RememberedProfileName`, `User` sintetis untuk akun administrator, penanganan `DebugToken` selama
   sesi hidup.
9. UI: `LoginControl.SignInCommand`, `MainWindowVm.PerformLogoff`, langganan `SessionEnded` di
   `MainWindow`, `SessionEndedNotice`, pemulihan sesi saat startup.
10. **Server: menyalakan penegakan** `IsPublicAction` di `ProcessRequest` + menutup `GetTimeStamp`,
    `GetUlid`, `GetUlidMany`.
11. Build kedua solution:
    `dotnet build src/backend/Em.Api.slnx` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx`.
12. Uji jalur (8.1), lalu pindahkan berkas ini ke `plan/executed/`.

### 8.1 Yang harus diuji sebelum dianggap selesai

Jalur access token yang sah belum pernah teruji sama sekali — catatan penutup rencana debug token
menulisnya sebagai "belum teruji", dan fase inilah yang pertama kali bisa mengujinya.

| Skenario | Harapan |
| --- | --- |
| Sign in dengan password benar | masuk workspace, identitas tampil di panel user |
| Sign in dengan password salah | satu pesan yang sama, tetap di layar login |
| Action non-publik tanpa token (curl) | 401 |
| Action publik tanpa token (curl) | 200 |
| Access token dibiarkan kedaluwarsa (>15 menit), lalu aplikasi dipakai lagi | refresh berjalan sendiri, user tidak melihat apa pun |
| Beberapa request bersamaan sesudah token kedaluwarsa | hanya satu refresh, semuanya lanjut |
| Sign out, lalu tekan apa saja di workspace | kembali ke layar login |
| Sign out di satu tempat, request dari sesi yang sama | 401 seketika, bukan sesudah 15 menit |
| Tutup aplikasi dengan "keep me signed in", buka lagi | langsung masuk tanpa mengetik password |
| Sama, tapi sesinya sudah dicabut dari tempat lain | layar login, nama akun terisi, tanpa pesan merah |
| Build debug, koneksi debug, tanpa login | tetap masuk sebagai `debugger` seperti sekarang |
| Build debug, `SimulateLogin` lalu sign in sungguhan | request memakai Bearer, bukan token debug |
| Server HTTPS bersertifikat self-signed, `IgnoreSslErrors` mati | koneksi ditolak |
| Token debug lolos, `X-Em-User` berisi id dan nama akun yang tidak sepasang | 401 berpesan jelas |
| Token debug lolos, `X-Em-User: budi` polos (bentuk lama) di curl | tetap bisa menyamar, seperti sebelumnya |
| Token debug lolos, `X-Em-User` hanya berisi `cUserId` | menyamar jadi pemilik id itu, hak dibaca dari barisnya |
| Tanpa token debug, `X-Em-User` menyebut id debugger | 404 |
| User biasa memanggil action yang menyebut id user lain | 403, bukan 401 |
| Hak admin seorang user dicabut selagi ia login | sesinya berakhir, request berikutnya minta login lagi |

---

## 9. Yang sengaja ditunda

- **Lapisan izin per action (`ta_UserClaim`).** Sesudah fase ini gerbang baru menjawab "siapa
  pemanggilnya", belum "apa yang boleh dikerjakannya". `CallerIsAdmin` masih satu-satunya pembeda
  hak, dan action yang memerlukannya memeriksanya sendiri.
- **Refresh proaktif** sebelum access token kedaluwarsa. Reaktif sudah cukup dan lebih sedikit bagian
  yang bisa salah; yang hilang hanya satu request yang terpaksa dikirim dua kali tiap 15 menit.
- **Menutup tab saat sesi berakhir.** Data yang sedang diedit ikut hilang kalau tab dibuang begitu
  saja, dan menyimpannya lebih dulu adalah rancangan tersendiri.
- **Idle timeout di client** (keluar sendiri setelah sekian lama tidak dipakai).
- **Layar daftar sesi** yang memakai `PostGetMeta_GetSessions`, berikut tombol "sign out dari semua
  perangkat" (`PostMeta_SignOutAll`). Kontraknya sudah ada di kedua sisi, layarnya belum.
- **Panel debug** (saklar token debug, pemilih `ActiveUser`) — sudah tercatat di rencana debug token.
- **Provider eksternal** (Google/Microsoft).

---

## 10. Keputusan yang masih terbuka

### 10.1 Satu sesi per koneksi, atau satu sesi per aplikasi

Rancangan ini menaruh sesi di `ApiClient`, jadi aplikasi bisa memegang sesi hidup ke beberapa server
sekaligus dan berganti koneksi tidak membuang sesi yang lain. Yang belum diputuskan: apakah UI boleh
benar-benar berganti koneksi **selagi** sesi berjalan (sekarang `ActiveConnection` bisa berubah kapan
saja lewat dialog koneksi), atau berganti koneksi selalu berarti keluar dulu. Yang kedua lebih mudah
dijelaskan ke user dan lebih sedikit keadaan yang harus dipikirkan; yang pertama sudah didukung
bentuk datanya.

### 10.2 Dua instance aplikasi di satu mesin

Keduanya membaca refresh token tersimpan yang sama. Yang lebih dulu memakainya merotasi token itu,
dan yang belakangan mendapati miliknya sudah mati lalu jatuh ke layar login. Bisa diterima apa
adanya, atau diperbaiki nanti dengan menyimpan sesi per-instance. Belum diputuskan.

### 10.3 Entropy DPAPI

Ditulis sebagai konstanta di kode. Kalau nanti aplikasi di-rebrand per pelanggan, entropy yang sama
di semua instalasi berarti blob satu instalasi bisa dibuka instalasi lain milik user Windows yang
sama. Belum ada kebutuhannya sekarang.
