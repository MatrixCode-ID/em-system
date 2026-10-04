# Plan — Debug token: akses dev tanpa password dan JWT

Status: **sudah dieksekusi** — 2026-09-13
Dibuat: 2026-09-13 — hasil pembahasan `builder.AddDebugToken`, lanjutan dari
`plan/executed/authorization-fase1-service-layer.md` dan
`plan/executed/akun-admin-dan-sesi-sistem.md`.

---

## Kenapa ada berkas ini

Saat mengembangkan modul, harus ada jalan masuk yang tidak menuntut password maupun access token —
untuk memanggil satu action lewat curl/Postman, dan untuk menjalankan client tanpa melewati layar
login. Berkas ini merancang jalan itu.

Tiga kondisi yang jadi titik berangkat:

1. **Backend saat ini tidak kompilasi.** `src/backend/Em.Api/Program.cs:10` sudah memanggil
   `builder.AddDebugToken(...)`, tapi method-nya belum ada di `EmAppBuilder`.
2. **Gerbang otorisasi belum ada sama sekali.** `EmApp.ProcessRequest`
   (`src/backend/Em.Api.Core/Api/Core/EmApp.cs:157`) langsung invoke action; `IsPublicAction`
   direkam di `ActionDefinition` tapi tidak pernah dibaca, dan `ServicesBase.CallerUserId` /
   `CallerSessionId` tidak pernah diisi. Debug token bukan fitur berdiri sendiri — ia satu cabang
   dari gerbang yang memang belum dibangun.
3. **Sisi client separuh jalan.** `DebugBuilder.DebugKey` sudah ada dan sudah diisi di
   `src/frontend/Em.Ui.Wpf/Program.cs:26`, tapi tidak pernah dipakai — `ApiClient` belum pernah
   menyentuh request header.

Yang sudah siap menyambut: `Defaults.DebuggerUserId` (`999…9`), `TokenServices` sudah mengenalinya
sebagai akun sistem, `SystemSessionStore`/`ta_SystemSession` sudah jalan, dan `RsaKeyPair` sudah
menstandarkan RSA PSS + SHA-256 lewat `SignData`/`VerifyData`.

## Bentuk akhirnya, singkat

Dev memegang **private key** di mesinnya. Ia menandatangani satu payload kecil; hasilnya sebuah
token yang ditempel ke header `X-Em-Debug-Token`. Server hanya menyimpan **public key** milik
tiap dev dan memverifikasi tanda tangan itu di setiap request. Header kedua, `X-Em-User`,
menyebut akun yang sedang aktif di client — dan hanya berarti sebagai identitas kalau token debug
di atas lolos verifikasi.

## Keputusan desain yang sudah diambil

| Hal | Keputusan |
| --- | --- |
| Bentuk mekanisme | header bypass **per-request** — bukan penukaran token jadi JWT |
| Isi `X-Em-Debug-Token` | token bertanda tangan, diverifikasi server tiap request |
| Kriptografi | RSA 2048, PSS + SHA-256, lewat `RsaKeyPair` yang sudah ada — tidak ada primitif baru |
| Letak private key | `opt.DebugKey` di `src/frontend/Em.Ui.Wpf/Program.cs`, di dalam `#if DEBUG`. Tidak pernah dikirim ke server |
| Letak public key | `Program.cs` backend, aman di-commit |
| Yang menandatangani token client | aplikasi sendiri, sekali saat startup, dari `DebugKey` — tidak ada token jadi yang ditempel ke source |
| Nama header token | `X-Em-Debug-Token` |
| Nama header user | `X-Em-User` — bukan `X-Em-Debug-User`, karena dikirim juga di Release sesudah login |
| Isi header user | `cUserAccount` |
| Tempat nama header ditulis | `Em.Libs/Defaults.cs`, supaya server dan client menyebut kata yang sama persis |
| Identitas default | `Defaults.DebuggerUserId`, **bukan** `AdminUserId` — jejak audit harus bisa membedakan dev yang mengoprek dari admin sungguhan |
| Hak akses default | `IsAdmin = true` |
| Saat menyamar jadi user lain | `IsAdmin` **dibaca dari baris user itu**, tidak dipaksa true — kalau dipaksa, tes claim kehilangan artinya |
| Sesi | tidak ada; `CallerSessionId` tetap `null`. Request debug bukan proses masuk |
| Semua penolakan di jalur ini | **404**, bentuk responsnya sama persis dengan "action not found" |
| Alasan sebenarnya | ditulis ke log server, tidak pernah ke client |
| Sesudah token lolos | berhenti menyembunyikan — error berikutnya dijawab dengan pesan jelas |
| Masa berlaku | ditentukan server lewat parameter `days` pada `AddDebugToken`; default 60, `-1` = tanpa batas |
| Pemeriksaan environment | **tidak ada**. Server tidak memeriksa `IsDevelopment()` |
| `#if DEBUG` di backend | **tidak dipakai, disengaja** — lihat bagian di bawah |
| `#if DEBUG` di frontend | tetap: `AddDebug` hanya ada di build Debug |
| Kapan client mengirim header token | saat `IsDebugMode == true` |
| Penyimpanan key di server | hanya di memori, dibekukan setelah `BuildApp`. Tidak pernah masuk `ta_Meta` |
| Jumlah key | jamak — satu dev satu keypair, dicabut satu-satu |
| Penamaan di client | `DebugBuilder.DebugKey` **tetap** — isinya memang private key, bukan token. Rename yang sempat direncanakan dibatalkan |
| Urutan cabang di gerbang | token debug diperiksa **paling depan**, sebelum Bearer |

### Kenapa dev yang memegang private key, bukan server

Yang dibuktikan di sini adalah "pemanggil ini benar-benar dev yang berhak", dan bukti semacam itu
hanya bisa dibuat pemegang private key. Kalau susunannya dibalik, client hanya bisa mengenkripsi —
dan itu bukan pembuktian identitas, karena siapa pun yang memegang public key bisa membuat
ciphertext yang sah. Public key pun jadi harus dirahasiakan, sehingga kembali menjadi rahasia
bersama yang justru tersimpan di sisi client. Ditambah, private key PKCS#1 memuat komponen
publiknya (lihat komentar di `RsaKeyPair.CreateRsa`), jadi server yang dibobol langsung bisa
memalsukan request dev — persis untung yang mau dibeli lewat keypair.

XML doc `RsaKeyPair.DecryptValue` sudah menuliskan aturannya: untuk membuktikan kepemilikan key,
pakai `SignData`/`VerifyData`, bukan dekripsi.

### Kenapa backend sengaja tidak dibungkus `#if DEBUG`

Supaya aplikasi dev bisa dijalankan terhadap server produksi saat dibutuhkan.

Karena private key ditaruh di `Program.cs` frontend, ia ikut masuk repo — jadi **akses baca repo
tetap sama dengan akses admin produksi**, sama seperti rancangan rahasia-bersama sebelumnya. Ini
diputuskan sadar demi kemudahan: tidak ada key yang harus disiapkan per mesin dev. Yang tetap
didapat dari keypair adalah sisi server: `Program.cs` backend, binary server, dan memori prosesnya
hanya memuat public key, sehingga server produksi yang dibobol tidak bisa dipakai memalsukan
request dev.

Konsekuensi praktisnya: kalau akses repo melebar, keypair harus diganti — dan yang lama sudah
terlanjur ada di git history.

### Apa yang sebenarnya dibatasi oleh `days`

Dev memegang private key, jadi ia selalu bisa menerbitkan token baru — `days` tidak membatasi dev.
Yang dibatasi adalah **token yang bocor**: string yang tertinggal di history Postman, di log, atau
di catatan yang terkirim ke orang lain, mati dengan sendirinya setelah lewat batas itu. Karena itu
`-1` berarti token yang bocor berlaku selamanya sampai barisnya dihapus dan backend di-deploy ulang;
pakai hanya kalau memang itu yang diinginkan.

### Kenapa semua penolakan 404

Token ini juga berlaku di produksi, jadi mekanismenya tidak boleh bisa dideteksi dari luar. Supaya
benar-benar tidak terdeteksi, bentuk responsnya harus sama persis dengan `ActionResult`
"action not found" yang sudah ada — beda sedikit saja, ia mengumumkan keberadaan dirinya.
Kebutuhan dev ditutup dari sisi lain: diam ke client, cerewet ke log server.

## Rancangan

### 1. `src/shared/Em.Libs/Defaults.cs`

```csharp
public const string DebugTokenHeader = "X-Em-Debug-Token";
public const string UserHeader        = "X-Em-User";
public const string DebuggerUserAccount = "debugger";
```

`DebuggerUserAccount` sepasang dengan `AdminUserAccount` yang sudah ada, dan alasannya sama: akun
ini tidak punya baris pengguna, jadi namanya tidak bisa dibaca dari sana.

### 2. Bentuk token

```
X-Em-Debug-Token: <payloadB64url>.<signatureB64url>
```

- `payload` — JSON UTF-8: `{ "name": "<nama key>", "iat": <unix seconds> }`
- `signature` — RSA-PSS SHA-256 atas **byte ASCII dari segmen `<payloadB64url>` itu sendiri**, bukan
  atas JSON mentahnya. Dengan begitu tidak ada urusan kanonikalisasi JSON: yang ditandatangani
  persis deretan byte yang muncul di header.
- `name` ada **di dalam** payload supaya ikut tertutup tanda tangan; kalau di luar, ia bisa ditukar
  orang. Server mem-parse payload dulu untuk tahu public key mana yang dipakai, baru verifikasi.

Panjangnya sekitar 400 karakter (tanda tangan RSA-2048 = 256 byte). Aman untuk header mana pun.
Aplikasi WPF membangkitkannya sendiri, jadi yang perlu di-copy-paste hanya token untuk curl/Postman.

Format ini dipakai kedua sisi — client menyusunnya, server membacanya — jadi tempatnya di
`Em.Libs`, sebagai satu kelas statis `DebugTokenProtocol` bersebelahan dengan `ProbeProtocol`
yang sudah ada untuk handshake:

```csharp
public static string Create(string name, RsaKeyPair key, DateTime issuedAtUtc);
public static bool TryRead(string token, out string name, out DateTime issuedAtUtc, out byte[] payload, out byte[] signature);
```

`TryRead` hanya memecah dan mem-parse; verifikasi tanda tangannya tetap di server, karena hanya
server yang tahu public key mana milik nama itu.

### 3. `EmAppBuilder.AddDebugToken`

Dua overload, bukan parameter opsional — mengikuti kebiasaan repo:

```csharp
public void AddDebugToken(string name, string publicKey);            // days = 60
public void AddDebugToken(string name, string publicKey, int days);  // -1 = tanpa batas
```

Validasi seluruhnya terjadi **saat pendaftaran**, semuanya throw saat startup:

- `name` tidak kosong dan unik (case-insensitive);
- `publicKey` tidak kosong dan **benar-benar public key RSA**: Base64-nya bisa di-decode, hasilnya
  bisa di-import lewat `RSA.ImportRSAPublicKey`, dan ukuran key-nya minimal 2048 bit. Dua kegagalan
  pertama datang sebagai `FormatException` dan `CryptographicException` — keduanya ditangkap dan
  dilempar ulang sebagai pesan yang menyebut nama key-nya, supaya yang salah ketik langsung tahu
  baris mana. Key rusak ketahuan saat start, bukan saat request pertama;
- nilai `publicKey` tidak duplikat — dua nama dengan key yang sama membuat log tidak bisa dipercaya;
- `days >= -1`, dan `days == 0` ditolak: nilai itu mematikan key-nya diam-diam, dan kalau memang
  ingin mati, barisnya yang dihapus.

Public key di-import **sekali** di `BuildApp`, disimpan sebagai `RsaKeyPair` di dalam record beku
(`name`, `key`, `days`) sejajar dengan `_actions` yang sudah ada — bukan di-parse ulang tiap request.

Catatan: `RsaKeyPair.VerifyData` membuat lalu men-dispose satu `RSA` per panggilan. Untuk sekali per
request biayanya kecil, dan justru menghindari persoalan thread-safety serta cache provider yang
sudah harus dikomentari panjang di `TokenServices.SigningKeyFor`.

### 4. Verifikasi di server, berurutan

Semua langkah yang gagal menjawab **404** dan menulis warning berisi alasan sebenarnya:

1. header tidak berbentuk dua segmen → tolak;
2. payload tidak bisa di-decode / bukan JSON yang diharapkan → tolak;
3. `name` tidak terdaftar → tolak;
4. `VerifyData` gagal → tolak;
5. `iat` lebih maju dari sekarang di luar toleransi 30 detik (mengikuti `ClockSkew` di
   `TokenServices`) → tolak;
6. `days >= 0` dan `iat + days` sudah lewat → tolak. `days == -1` melewati langkah ini.

### 5. Matriks gerbang

| `X-Em-Debug-Token` | `X-Em-User` | Hasil |
| --- | --- | --- |
| ada, gagal verifikasi | apa pun | 404 |
| tidak ada | `debugger` | 404 |
| tidak ada | lainnya / kosong | header **diabaikan**; identitas murni dari JWT |
| lolos | kosong | identitas = debugger, `IsAdmin = true` |
| lolos | `debugger` | sama dengan di atas |
| lolos | akun lain | identitas = akun itu, `IsAdmin` dari barisnya, JWT diabaikan |

Aturan yang mengikat seluruh tabel ini: **`X-Em-User` tidak pernah menjadi sumber identitas tanpa
token debug yang lolos.** Kalau ia pernah dipercaya sendirian, siapa pun cukup mengirim
`X-Em-User: admin` untuk jadi admin.

Baris "tidak ada / `debugger` → 404" adalah tripwire: di Release `ActiveUser` tidak akan pernah
bernilai debugger, karena tidak ada jalur login yang bisa menghasilkannya. Jadi request semacam itu
sudah pasti palsu.

Kalau JWT sah dan `X-Em-User` menyebut akun lain, **JWT yang menang** dan selisihnya cukup
di-log — ada kondisi sah yang menghasilkannya, misalnya request yang masih di udara saat user
berganti.

Sesudah token lolos, berhenti opaque: akun pada `X-Em-User` yang tidak ditemukan, terhapus, atau
non-aktif dijawab dengan pesan jelas, bukan 404.

### 6. `ServicesBase`

Tambah satu properti, melengkapi `CallerUserId` dan `CallerSessionId` yang sudah ada:

```csharp
public bool CallerIsAdmin { get; internal set; }
```

### 7. Sisi client

- `DebugBuilder.DebugKey` tetap namanya; isinya **private key** Base64 (DER PKCS#1), dan ia butuh
  pasangannya: **nama key** yang sama persis dengan yang didaftarkan di backend, karena nama itu
  masuk ke payload dan dipakai server untuk mencari public key-nya. Dua bentuk yang mungkin —
  `opt.DebugKey` + `opt.DebugKeyName`, atau satu pemanggilan `opt.SetDebugKey(name, privateKey)`
  yang mencerminkan `AddDebugToken(name, publicKey)` di backend. Bentuk kedua lebih sulit dikonfigur
  setengah jadi; putuskan saat eksekusi.
- **`DebugKey` divalidasi saat `BuildApp`**, sesaat setelah callback `AddDebug` selesai dan sebelum
  window pertama muncul — sejajar dengan cara backend memvalidasi public key-nya. Yang diperiksa
  bukan sekadar "bisa di-parse", tapi bisa dipakai: import lewat `RSA.ImportRSAPrivateKey`, lalu
  satu putaran `SignData` + `VerifyData` atas payload percobaan. Itu membuktikan key-nya sah untuk
  PSS/SHA-256, bukan cuma berbentuk benar, dan biayanya sekali saat startup.
  Catatan teknis: `RsaKeyPair` tidak punya bentuk "private key saja" — konstruktornya menuntut
  public key. Jadi client meng-import private key sekali, menurunkan public key-nya dengan
  `ExportRSAPublicKey`, baru menyusun `RsaKeyPair` lengkap untuk dipakai menandatangani.
  Key yang tidak lolos **melempar exception**, bukan diam-diam mematikan mode debug: kalau
  dibiarkan senyap, dev akan mengira salah di tempat lain.
- Satu hal yang **tidak bisa** diperiksa di sisi client: apakah private key ini pasangan dari public
  key yang terdaftar di backend. Ketidakcocokan baru ketahuan saat request pertama, gejalanya semua
  action dijawab 404, dan yang menjelaskan sebabnya adalah log server (`nama key tidak dikenal`
  atau `verifikasi tanda tangan gagal`). Itu alasan lain kenapa log-nya harus menyebut alasan
  sebenarnya.
- **Aplikasi menandatangani tokennya sendiri saat startup** lewat `DebugTokenProtocol.Create`, dengan
  `iat` = sekarang. Tidak ada token jadi yang ditempel ke source, jadi tidak ada yang kedaluwarsa dan
  perlu diganti berkala. Satu catatan kecil: kalau aplikasi dibiarkan hidup melewati batas `days`,
  tokennya mati di tengah jalan — restart menerbitkan yang baru.
- Hasil tanda tangan itu diteruskan ke tiap `ApiConnection` yang dibuat
  `DebugBuilder.AddDebugConnection`; properti baru `ApiConnection.DebugToken` menampung **token**-nya,
  bukan key-nya, sehingga jalur request tidak menyentuh kriptografi sama sekali. Aman di sana:
  penulisan Registry di `EmApp.AddApiConnection`
  dilakukan per-field secara eksplisit, jadi properti baru tidak ikut tersimpan, dan koneksi debug
  memang sudah ditolak masuk Registry oleh `ThrowIfDebugConnection`.
- **Header dipasang per-request, bukan di `DefaultRequestHeaders`.** Keduanya berubah saat runtime:
  `X-Em-User` mengikuti `ActiveUser`, dan token debug harus bisa dimatikan tanpa restart. Praktisnya
  `ApiClient.GetAsync`/`PostAsync` yang sekarang memakai `HttpClient.GetAsync(url)` dan
  `PostAsync(url, content)` berubah menyusun `HttpRequestMessage` lalu `SendAsync` — menyentuh
  keempat jalur request.
- `X-Em-User` diisi `ActiveUser.cUserAccount` setiap kali `ActiveUser` terisi, di Debug maupun
  Release.
- Saat `IsDebugMode`, `ActiveUser` diisi debugger sejak startup. Objeknya dibuat lokal, bukan dari
  API: `User.GetUser_ByAccountAsync` memanggil `EnsureNotSystemAccount`, jadi akun sistem memang
  tidak bisa diambil lewat jalur itu.

## Yang belum dinyalakan di fase ini

Cabang **"tidak ada identitas dan action-nya bukan public → 401"** harus tetap mati. `ApiClient`
belum menyimpan maupun mengirim access token sama sekali, jadi begitu penegakan dinyalakan,
aplikasi WPF mati total kecuali handshake dan login.

Urutannya:

1. **(berkas ini)** debug token + rangka gerbang; cabang lain lewat tanpa menolak.
2. penyimpanan dan pengiriman access token di `ApiClient`, beserta refresh otomatis.
3. penegakan `IsPublicAction` dinyalakan.

Di fase ini debug token belum "membuka" apa pun — semua action memang masih terbuka. Yang ia berikan
sekarang adalah identitas yang terbaca action, jejak log-nya, dan seam gerbang untuk fase berikutnya.

## Tindak lanjut, di luar fase ini

- **Dialog RSA keypair di UI** (dikerjakan sesudah flow ini jadi): membangkitkan pasangan key dan
  menampilkan keduanya untuk di-copy — public key ke `AddDebugToken` di `Program.cs` backend, private
  key ke `opt.DebugKey` di `Program.cs` frontend. Tidak menyimpan apa pun sendiri. Ditambah satu
  tombol "terbitkan token" yang menandatangani payload dari private key, khusus untuk curl/Postman;
  aplikasi sendiri tidak membutuhkannya karena menandatangani sendiri saat startup.
- **Panel debug di UI**: saklar mematikan token debug, dan pemilih `ActiveUser` untuk simulasi claim.
  Saat saklar dimatikan, `ActiveUser` dikosongkan dan aplikasi kembali ke layar login — tanpa token,
  identitas debugger tidak diakui server, dan membiarkannya terpasang membuat UI menampilkan sesi
  yang seolah hidup padahal setiap request dijawab 404. Ini juga jalan untuk menguji alur JWT
  sungguhan dari build dev.
- **Audit `ta_Log`**: selama penyamaran, baris data yang tertulis membawa jejak akun yang disamar dan
  tidak bisa dibedakan dari pekerjaan aslinya. Sampai sistem audit ada, satu-satunya jejak adalah log
  server — karena itu setiap request yang lolos lewat token debug menulis warning berisi nama key,
  akun yang disamar, action, dan IP pemanggil.

## Catatan saat eksekusi

- Nilai di `src/backend/Em.Api/Program.cs:10` dan di `src/frontend/Em.Ui.Wpf/Program.cs:26`
  sekarang masih string acak yang sama, sisa rancangan rahasia-bersama yang lama. Keduanya **harus
  diganti** oleh pasangan key yang baru — public key di backend, private key di frontend; nilai lama
  tidak dipakai lagi di mana pun.
- Sebelum dialog keypair ada, keypair pertama dibangkitkan sekali lewat PowerShell — formatnya persis
  yang dimengerti `RsaKeyPair` (Base64 dari DER PKCS#1):

  ```powershell
  $rsa = [System.Security.Cryptography.RSA]::Create(2048)
  "PUBLIC : " + [Convert]::ToBase64String($rsa.ExportRSAPublicKey())
  "PRIVATE: " + [Convert]::ToBase64String($rsa.ExportRSAPrivateKey())
  ```

## Berkas yang tersentuh

| Berkas | Perubahan |
| --- | --- |
| `src/shared/Em.Libs/Defaults.cs` | konstanta dua header + `DebuggerUserAccount` |
| `src/shared/Em.Libs/Shared/DebugTokenProtocol.cs` | **baru** — format token, `Create` dan `TryRead`, dipakai kedua sisi |
| `src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.cs` | dua overload `AddDebugToken` + list internal + validasi |
| `src/backend/Em.Api.Core/Api/Core/EmApp.cs` | registry key beku + verifikasi token + gerbang di `ProcessRequest` |
| `src/backend/Em.Api.Core/Api/Core/ServicesBase.cs` | properti `CallerIsAdmin` |
| `src/shared/Em.Ui.Wpf.Core/Shared/DebugBuilder.cs` | `DebugKey` (private key) + nama key; menandatangani token saat startup dan meneruskannya ke koneksi |
| `src/shared/Em.Ui.Core/Ui.Core/shared/ApiConnection.cs` | properti `DebugToken`, berisi token hasil tanda tangan |
| `src/shared/Em.Ui.Core/Ui.Core/shared/ApiClient.cs` | `HttpRequestMessage` + `SendAsync`, pasang kedua header per-request |
| `src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs` | `ActiveUser` = debugger saat `IsDebugMode` |
| `src/frontend/Em.Ui.Wpf/Program.cs` | `opt.DebugKey` diisi private key + nama key, tetap di dalam `#if DEBUG` |
| `src/backend/Em.Api/Program.cs` | ganti nilai jadi public key; barisnya tetap di luar `#if DEBUG` |

Setelah selesai: build kedua solution, lalu pindahkan berkas ini ke `plan/executed/`.

---

## Catatan hasil eksekusi (2026-09-13)

Keputusan yang sengaja ditinggalkan terbuka oleh rancangan, beserta jawabannya:

- **Bentuk konfigurasi key di client:** dipilih `opt.SetDebugKey(name, privateKey)`, bukan sepasang
  property. Nama dan key selalu masuk bersamaan, jadi tidak ada bentuk setengah jadi.
- **`X-Em-User: admin`:** ditangani tersendiri. Akun administrator bawaan tidak punya baris
  pengguna, jadi kalau dibiarkan jatuh ke pencarian baris ia akan dijawab "akun tidak ada" — keliru.
  Identitasnya memakai `Defaults.AdminUserId`, dan saklar akun admin tetap dihormati: saat saklarnya
  mati, penyamaran ditolak dengan pesan yang menyebutkan itu, sejalan dengan aturan "hak dibaca apa
  adanya, tidak dipaksa".
- **Status penolakan sesudah token lolos:** 401, dengan pesan yang menyebut sebabnya. 404 hanya
  dipakai selama mekanismenya masih harus disembunyikan.
- **Cabang Bearer:** sudah dipasang dan mengisi `CallerUserId`/`CallerSessionId`/`CallerIsAdmin`.
  Access token yang tidak sah **tidak menolak request**, hanya ditulis ke log dan tidak menghasilkan
  identitas — penegakannya baru dinyalakan di fase ketiga.
- **Selisih `X-Em-User` vs JWT:** tidak dibandingkan. Token hanya membawa id pengguna, dan
  menerjemahkannya jadi nama akun berarti satu query tambahan di setiap request. Yang ditulis adalah
  satu baris `LogDebug` berisi nama akun pada header dan id pemilik token, jadi mati secara bawaan.

Diverifikasi terhadap server yang berjalan (`http://localhost:5132`), seluruh baris matriks gerbang
cocok: tanpa header → 200; token palsu → 404 dengan bentuk respons sama persis dengan action yang
tidak ada; token sah → 200 sebagai `debugger`; tanpa token dengan `X-Em-User: debugger` → 404;
penyamaran ke akun sungguhan → 200 dengan hak dari barisnya; akun tak dikenal dan akun admin yang
saklarnya mati → 401 berpesan jelas; `Authorization: Bearer` rusak → tetap dilayani, dicatat di log.
Format token diuji terpisah: round-trip, payload yang diubah, bentuk-bentuk cacat, dan token dari
key lain — semuanya sesuai harapan.

**Belum teruji:** jalur access token yang sah (butuh kredensial untuk sign in), dan aplikasi WPF
terhadap server sungguhan.
