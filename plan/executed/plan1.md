# Plan 1 — Ganti probe koneksi dari decrypt-nonce menjadi sign-nonce

Status: **belum dieksekusi**
Dibuat: 2026-07-25
Baseline: commit `69d537a` (Tambah handshake RSA public key dan probe koneksi API)

Keputusan yang sudah ditetapkan:

- `Handshake` ditambahkan sebagai satu endpoint gabungan; `ProbeTest` dihapus.
- `GetServerPublicKey` **tidak** dihapus — dipertahankan sebagai action non-publik.
- `IsPublicAction` disiapkan untuk layer authorization nanti, default `false` (tertutup).
- Produksi wajib HTTPS (lihat bagian 10).

---

## 1. Latar belakang

`ProbeTest` yang ada sekarang bekerja begini: client mengenkripsi Ulid dengan public key
server, server mendekripsinya dengan private key, lalu **plaintext-nya dikembalikan** ke
client untuk dibandingkan. Tujuannya benar — membuktikan server memegang private key yang
cocok — tapi mekanismenya menimbulkan tiga masalah:

1. **Decryption oracle.** Endpoint publik tanpa autentikasi mendekripsi ciphertext arbitrer
   dan mengembalikan hasilnya. Selama plaintext-nya tepat 16 byte, `new Ulid(bytes)`
   menerimanya. Kalau nanti ada bagian protokol lain yang mengenkripsi 16 byte dengan public
   key server (API key, session id, symmetric key), ciphertext-nya bisa dibuka lewat endpoint
   ini.
2. **Padding oracle.** `DefaultPadding` memakai PKCS#1 v1.5. Endpoint publik tanpa rate limit
   yang membedakan padding valid/tidak valid adalah bahan serangan Bleichenbacher.
3. **Plaintext tidak perlu dikembalikan.** Client sudah tahu nilai yang ia kirim, jadi
   membandingkan nilai kirim vs balik tidak menuntut server membocorkan hasil dekripsi.

Solusinya: ganti primitifnya dari **enkripsi-lalu-dekripsi** menjadi **tanda tangan**. Client
mengirim nonce apa adanya, server menandatanganinya, client memverifikasi signature dengan
public key. Round trip sama, tapi server tidak pernah lagi mendekripsi input dari luar,
sehingga masalah 1 dan 2 hilang seluruhnya.

## 2. Perubahan kontrak

| | Sebelum | Sesudah |
|---|---|---|
| Endpoint probe | `GET api/core/ProbeTest?par1=<ciphertext>` | `GET api/core/Handshake?par1=<nonce base64url>` |
| Arah kripto | client enkrip, server **dekrip** | client kirim nonce polos, server **tanda tangani** |
| Balasan | plaintext Ulid | `{ PublicKey, Signature, KeySize }` |
| Verifikasi client | bandingkan Ulid kirim vs balik | `VerifyData(nonce, signature)` dengan `PublicKey` dari respons |

Dipilih **satu endpoint gabungan** (bukan dua endpoint terpisah) agar hanya satu round trip
dan tidak ada celah race kalau key server ter-rotate di antara dua panggilan.

`ProbeTest` **dihapus tanpa shim kompatibilitas** — baru masuk di commit terakhir dan belum
pernah dipakai di luar repo ini.

`GetServerPublicKey` **dipertahankan**, tapi statusnya diubah menjadi non-publik dan XML
doc-nya menyatakan batasannya. Public key bukan rahasia, jadi menyimpannya bukan risiko
kerahasiaan; satu-satunya keberatan adalah endpoint itu membuat jalur tanpa verifikasi jadi
jalur termudah, dan itu ditutup lewat status non-publik plus dokumentasi. Kasus pakai yang
diantisipasi: provisioning/pinning, yaitu menyalin key server ke konfigurasi client oleh
caller yang sudah terautentikasi.

---

## 3. Langkah 1 — `src/shared/Em.Libs` (fondasi bersama)

> Catatan konvensi: semua member `public` baru di `src/shared/*` wajib punya XML doc comment
> berbahasa Indonesia (lihat `CLAUDE.md`). Pesan exception dan literal tetap Bahasa Inggris.

### 1a. `Shared/RsaKeyPair.cs` — tambah primitif signature

```csharp
public static RSASignaturePadding DefaultSignaturePadding => RSASignaturePadding.Pss;
public static HashAlgorithmName DefaultHashAlgorithm => HashAlgorithmName.SHA256;

public byte[] SignData(ReadOnlySpan<byte> data) {
   if (!HasPrivateKey)
      throw new InvalidOperationException("Signing requires a private key; this RsaKeyPair only has a public key.");
   using var rsa = CreateRsa();
   return rsa.SignData(data, DefaultHashAlgorithm, DefaultSignaturePadding);
}

public bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) {
   using var rsa = CreateRsa();
   return rsa.VerifyData(data, signature, DefaultHashAlgorithm, DefaultSignaturePadding);
}
```

Sekalian di file yang sama, karena memang disentuh:

- `DefaultPadding` → `RSAEncryptionPadding.OaepSHA256` (belum ada ciphertext tersimpan, jadi
  tidak ada migrasi).
- `EncryptValue`/`DecryptValue`: `Span<byte>` → `ReadOnlySpan<byte>`.
- `<see cref="ServicesBase.GetServerRsaKeyAsync"/>` → `<c>...</c>`; cref itu tidak resolve
  karena `Em.Libs` tidak mereferensikan `Em.Api.Core` (CS1574).
- Lengkapi XML doc member publik yang belum ada: `DefaultPadding`, dua overload `Create`,
  `PublicKey`, `GetPublicBytes`, `GetPrivateBytes`, `CreateRsa`, `EncryptValue`,
  `DecryptValue`.
- Tambahkan newline di akhir file.

### 1b. `Shared/ProbeProtocol.cs` (baru)

Supaya kedua sisi menyusun byte yang identik, dan signature-nya ter-domain-separation.

```csharp
public static class ProbeProtocol
{
   public const int MinNonceLength = 16;
   public const int MaxNonceLength = 64;
   private static readonly byte[] Domain = "em.probe.v1:"u8.ToArray();

   public static byte[] BuildSignaturePayload(ReadOnlySpan<byte> nonce) => [..Domain, ..nonce];

   public static byte[] DecodeNonce(string value) {
      var nonce = Base64Url.DecodeFromChars(value);
      if (nonce.Length is < MinNonceLength or > MaxNonceLength)
         throw new ArgumentOutOfRangeException(nameof(value),
            $"Nonce must be between {MinNonceLength} and {MaxNonceLength} bytes.");
      return nonce;
   }
}
```

**Prefix domain itu esensial.** Server hanya pernah menandatangani byte yang diawali
`em.probe.v1:`, jadi signature dari endpoint ini tidak bisa dipakai ulang sebagai signature
atas pesan protokol lain.

> **Aturan yang harus dipegang ke depan:** setiap payload lain yang ditandatangani server
> wajib memakai prefix domain yang berbeda.

### 1c. `Shared/ServerHandshakeResult.cs` (baru)

DTO dipakai dua sisi, jadi nama property otomatis cocok dengan
`Defaults.ResponseJsonOptions` yang sengaja case-sensitive.

```csharp
public class ServerHandshakeResult
{
   public string PublicKey { get; set; } = "";   // Base64 DER PKCS#1
   public string Signature { get; set; } = "";   // Base64Url
   public int KeySize { get; set; }
}
```

---

## 4. Langkah 2 — Backend: ganti action

**File:** `src/backend/Em.Api.Core/Api/Core/ApiCoreServices.cs`

Hapus `ProbeTest` dan tambahkan `Handshake`:

```csharp
[GetAction(IsPublicAction = true)]
public async Task<ServerHandshakeResult> Handshake(string nonce) {
   var nonceBytes = ProbeProtocol.DecodeNonce(nonce);
   var key = await GetServerRsaKeyAsync();
   using var rsa = key.CreateRsa();
   return new ServerHandshakeResult {
      PublicKey = key.PublicKey,
      Signature = Base64Url.EncodeToString(key.SignData(ProbeProtocol.BuildSignaturePayload(nonceBytes))),
      KeySize = rsa.KeySize
   };
}
```

Tanpa `try/catch`: `EmApp.InvokeAsync` sudah membungkus exception menjadi `ActionResult`
500, dan pesan dari `DecodeNonce` sudah informatif. Ini juga memperbaiki perilaku lama
`ProbeTest` yang menyeragamkan semua penyebab error menjadi satu pesan menyesatkan
("Decrypted payload is not a valid Ulid").

### `GetServerPublicKey` tetap ada, dengan status non-publik

Tidak dihapus, karena kapabilitasnya kemungkinan dibutuhkan untuk provisioning/pinning. Yang
berubah hanya statusnya dan dokumentasinya:

```csharp
/// <summary>
/// Mengembalikan public key RSA server apa adanya, untuk keperluan provisioning/pinning oleh
/// caller yang sudah terautentikasi (mis. menyalin key server ke konfigurasi client).
/// PERHATIAN: key dari sini belum terbukti benar-benar milik server yang dituju - tidak ada
/// bukti kepemilikan private key yang menyertainya. Pihak yang perlu memvalidasi key wajib
/// memakai <see cref="Handshake"/>, bukan action ini.
/// </summary>
[GetAction]   // IsPublicAction sengaja dibiarkan false: ini bukan endpoint anonim.
public async Task<string> GetServerPublicKey() {
   var key = await GetServerRsaKeyAsync();
   return key.PublicKey;
}
```

**Caveat:** karena layer authorization belum ada, `IsPublicAction = false` belum ditegakkan
apa pun — action ini masih bisa diakses anonim sampai authz masuk. Konsekuensinya kecil
(public key memang bukan rahasia), tapi jangan dianggap sudah tertutup.

Sekalian di **`Api/Core/ServicesBase.cs`**: hapus `try` di baris 103 dan
`catch (Exception x) { throw; }` di baris 155-157 (no-op, dan `x` tidak dipakai → warning).

---

## 5. Langkah 3 — Backend: bawa `IsPublicAction` ke `ActionDefinition`

`IsPublicAction` **disiapkan untuk layer authorization yang akan datang**, bukan untuk
dipakai di plan ini. Default `false` berarti action tertutup kecuali ditandai eksplisit —
default yang benar, karena action baru yang lupa dianotasi otomatis tertutup, bukan terbuka.
Langkah ini hanya menyiapkan jalurnya, tanpa memberi flag itu arti apa pun dulu.

1. **`Em.Libs/Shared/GetActionAttribute.cs`** dan **`PostActionAttribute.cs`** —
   kembalikan `action` ke parameter pertama, ubah flag menjadi property settable. Semantik
   dan default tidak berubah, hanya call-site jadi terbaca dan tidak breaking untuk
   `[GetAction("NamaAction")]` yang sudah ada:

   ```csharp
   public class GetActionAttribute(string? action = null) : Attribute
   {
      public string? Action { get; } = action;
      public bool IsPublicAction { get; set; }
   }
   ```

2. **`Api/Core/ActionDefinition.cs`** — tambah `public bool IsPublicAction { get; set; }`.

3. **`Api/Shared/EmAppBuilder.cs`** — `GetHttpMethod` (baris 115-148) sekarang hanya
   mengembalikan HTTP method; ubah menjadi mengembalikan `(HttpMethod Method, bool IsPublicAction)?`
   supaya attribute-nya ikut terbawa, termasuk lewat jalur interface map. Isi ke
   `ActionDefinition` di `RegisterActions` (baris 158-178).

Setelah ini, ketika authorization masuk, `ProcessRequest` cukup membaca
`actionDef.IsPublicAction` sebelum invoke — tanpa menyentuh reflection lagi.

### Gating stack trace (terpisah dari `IsPublicAction`)

`BuildErrorActionResult(Exception, ...)` saat ini **selalu** mengisi `ErrorStackTrace` dengan
`exception.ToString()`, termasuk untuk pemanggil yang belum terautentikasi. Kriteria yang
benar untuk menyembunyikannya adalah **environment**, bukan sifat publik action — user
terautentikasi dengan privilege rendah juga tidak seharusnya menerima stack trace:

```csharp
// EmApp.BuildApp: simpan sekali saat startup
app._includeStackTrace = app.Builder.Environment.IsDevelopment();
```

lalu diteruskan ke `InvokeAsync` → `BuildErrorActionResult` (keduanya static, jadi cukup
tambah parameter `bool includeStackTrace`).

---

## 6. Langkah 4 — Client: `HttpClient` dan handshake

**File:** `src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs`

Hapus wrapper client `GetServerPublicKeyAsync` dan kedua overload `ProbeApiServerAsync`.
Action server `GetServerPublicKey` tetap ada, tapi jalur UI hanya perlu `Handshake`; kalau
nanti ada tooling provisioning yang memerlukannya, wrapper client-nya bisa ditambahkan
kembali (non-breaking). Overload publik
`ProbeApiServerAsync(Ulid, byte[]?)` saat ini **pasti NullReferenceException** karena
meneruskan `client: null` ke `client!.GetAsync(...)`; hal yang sama berlaku untuk
`ResetServerPublicKeyAsync()`. Ganti dengan:

```csharp
internal HttpClient CreateHttpClient(ApiConnection connection) {
   var handler = new HttpClientHandler();
   if (connection.IgnoreSslErrors) {
      // Opt-in per profil koneksi, untuk server development dengan sertifikat self-signed.
      handler.ServerCertificateCustomValidationCallback =
         HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
   }
   return new HttpClient(handler) {
      BaseAddress = new Uri(connection.Host),
      Timeout = connection.Timeout > 0 ? TimeSpan.FromSeconds(connection.Timeout) : TimeSpan.FromSeconds(30)
   };
}

/// <returns>Public key server (raw byte PKCS#1) yang sudah terbukti cocok dengan private key-nya.</returns>
public async Task<byte[]> HandshakeAsync(ApiConnection connection) {
   using var client = CreateHttpClient(connection);
   return await HandshakeAsync(client);
}

internal async Task<byte[]> HandshakeAsync(HttpClient client) {
   var nonce = RandomNumberGenerator.GetBytes(32);
   var result = await client
      .GetAsync($"api/core/Handshake?par1={Base64Url.EncodeToString(nonce)}")
      .ProcessHttpResult<ServerHandshakeResult>();

   var key = RsaKeyPair.Create(result.PublicKey);
   if (!key.VerifyData(ProbeProtocol.BuildSignaturePayload(nonce), Base64Url.DecodeFromChars(result.Signature))) {
      throw new InvalidOperationException(
         "Handshake failed: the signature over the client nonce is not valid for the public key the server returned. " +
         "The host may not be an Em API server, or the response was tampered with.");
   }
   return key.GetPublicBytes();
}
```

Catatan tambahan untuk file ini:

- Nonce 32 byte acak dari `RandomNumberGenerator`, bukan Ulid — Ulid punya 48 bit awal berupa
  timestamp yang bisa diprediksi, dan tidak ada alasan challenge harus berbentuk Ulid.
- Hapus semua `catch (Exception) { throw; }` (4 tempat, semuanya no-op).
- `SetServerPublicKey` hanya dipanggil **setelah** verify lolos.
- `CreateHttpClient` sekaligus memasang `IgnoreSslErrors` yang sampai sekarang tersimpan di
  Registry dan terbinding di XAML tapi belum pernah benar-benar dipakai.

### Keputusan yang harus diambil saat implementasi

`ResetServerPublicKeyAsync()` tidak punya parameter, tapi di repo ini **belum ada konsep
"koneksi aktif"** — `EmApp` hanya punya `GetApiConnections()`/`AddApiConnection()` yang
baca-tulis Registry, tanpa properti profil terpilih. Pilih salah satu:

- **(disarankan)** Ubah menjadi `ResetServerPublicKeyAsync(ApiConnection connection)`, ikut
  diubah di `Shared/IEmAppUi.cs`. Lebih kecil, dan konsep koneksi aktif sebaiknya dirancang
  bareng alur login.
- Tambah properti `ActiveConnection` di `EmApp` yang diisi saat user memilih profil di
  `ConnectionConfig`, lalu versi tanpa parameter memakai itu.

---

## 7. Langkah 5 — Dialog test koneksi

**File:** `src/shared/Em.Ui.Wpf.Core/Dialogs/ConnectionConfigEditor.xaml.cs`

```csharp
public async Task TestConnectionCommand() {
   try {
      StatusText = "Testing...";
      StatusBrush = Brushes.Gray;
      var publicKey = await EmApp!.HandshakeAsync(Connection);
      EmApp!.SetServerPublicKey(publicKey);
      StatusText = "Connected. Server key verified.";
      StatusBrush = Brushes.Green;
   }
   catch (Exception x) {
      StatusText = "Failed.";
      StatusBrush = Brushes.Red;
      AlertError(x);
   }
}
```

- `StatusText`/`StatusBrush` sudah ada di ViewModel dan sudah terbinding di XAML
  (`ConnectionConfigEditor.xaml` baris 137 dst.) tapi belum pernah diisi — sekalian dipakai.
- Hapus field `_app` yang di-assign tapi tidak pernah dibaca (yang dipakai `Vm.EmApp`).
- Perbarui XML doc "Belum diimplementasikan" yang sudah tidak berlaku.

**File:** `src/shared/Em.Ui.Wpf.Core/Extensions.cs`

- Perbaiki `??` mati di `ProcessHttpResult`: `$"Server Error: {resultObj.ErrorMessage}" ?? "..."`
  tidak pernah null, jadi fallback-nya dead code dan pesannya jadi `"Server Error: "` kalau
  `ErrorMessage` null. Pindahkan `??` ke dalam interpolasi.
- Dispose `response`.
- Buang `using System.Text.Json.Nodes` yang tidak terpakai.

---

## 8. Langkah 6 — Verifikasi

Repo belum punya project test, jadi verifikasinya manual plus satu cek kripto independen.

1. **Cek round-trip tanpa server**: generate key, `SignData` lalu `VerifyData` — pastikan
   PSS/SHA-256 konsisten dan payload domain-separated cocok byte-per-byte di kedua sisi.
2. **Verifikasi silang independen**: jalankan `Em.Api`, panggil
   `api/core/Handshake?par1=<nonce>`, lalu verifikasi signature-nya di luar kode .NET
   (Python `cryptography` atau `openssl`). Ini memastikan yang lolos bukan karena kedua sisi
   salah dengan cara yang sama.
3. **Negative test** — semuanya harus gagal:
   - signature diubah 1 byte;
   - `PublicKey` di respons ditukar dengan key lain;
   - nonce 8 byte → harus 400, bukan 500;
   - client diarahkan ke instance server berbeda (key berbeda) → harus ditolak.
4. **Regression**: key server tetap ter-generate di run pertama dan tersimpan di `ta_Meta`;
   log `Microsoft.EntityFrameworkCore` tetap Warning.
5. Tambahkan contoh request `Handshake` ke `src/backend/Em.Api/Em.Api.http`.

---

## 9. Urutan commit

1. `Em.Libs`: sign/verify + `ProbeProtocol` + `ServerHandshakeResult` + OAEP + rapikan
   XML doc dan `ReadOnlySpan`.
2. Backend: `Handshake` menggantikan `ProbeTest`/`GetServerPublicKey`; bersihkan
   `ServicesBase`.
3. Backend: `IsPublicAction` terbawa ke `ActionDefinition`; stack trace hanya di
   Development.
4. Client: `CreateHttpClient` + `HandshakeAsync` + dialog test koneksi + fix
   `ProcessHttpResult`.

---

## 10. Kebijakan transport

**Produksi wajib HTTPS.** Ini keputusan yang sudah ditetapkan, dan menjadi anchor keaslian
server: dengan TLS yang tervalidasi, identitas host dijamin sertifikat, sedangkan handshake RSA
di plan ini berperan sebagai bukti kepemilikan key di layer aplikasi — bukan pengganti TLS.
Implikasinya:

- `IgnoreSslErrors` (yang baru benar-benar aktif setelah Langkah 4) hanya untuk server
  development bersertifikat self-signed. Idealnya diberi peringatan eksplisit di UI, dan
  dipertimbangkan untuk ditolak sama sekali kalau host bukan localhost.
- `ApiConnection.Host` berskema `http://` sebaiknya hanya diizinkan untuk localhost/dev.
- Karena TLS yang memikul keaslian host, **pinning public key turun statusnya dari "perlu"
  menjadi defense-in-depth opsional** — berguna kalau nanti ingin bertahan terhadap CA yang
  dikompromikan atau proxy inspeksi TLS di jaringan korporat, tapi bukan prasyarat.

---

## 11. Sisa risiko yang TIDAK diselesaikan plan ini

**Handshake ini sendiri tidak mengautentikasi server; yang mengautentikasi adalah TLS.** Kalau
TLS dilewati (`IgnoreSslErrors` menyala, atau host `http://`), MITM bisa menyisipkan public key
miliknya, menandatangani nonce dengan private key-nya sendiri, dan lolos verifikasi — karena
public key diambil dari koneksi yang sedang divalidasi, jadi memvalidasinya terhadap koneksi
yang sama tidak membuktikan keaslian. Yang diperbaiki plan ini adalah menghapus oracle dekripsi
di sisi server, bukan sifat sirkular itu.

Di atas HTTPS tervalidasi, nilai handshake ini adalah **bukti kepemilikan private key plus
sanity check konfigurasi** (host benar, server hidup, key lokal tidak basi) — cukup untuk
`TestConnectionCommand`. Di luar HTTPS tervalidasi, nilainya turun jadi sekadar cek
konfigurasi.

Kalau nanti ingin lapisan tambahan di atas TLS, **pinning**: `ServerRsaPublicKey` di
`EmAppBuilder` dihidupkan sebagai key/fingerprint harapan, lalu `HandshakeAsync`
membandingkan `result.PublicKey` dengannya. Di luar cakupan, tapi struktur di atas sudah
menyisakan tempat yang jelas: satu perbandingan tepat setelah `VerifyData`. Catatan terkait:

- `ServerRsaPublicKey` saat ini setter-nya `internal`, sedangkan satu-satunya pemanggil ada di
  `Em.Ui.Wpf/Program.cs` (assembly berbeda) — blok yang dikomentari di sana **tidak akan
  compile kalau di-uncomment**. Perlu diputuskan: kembalikan setter publik, atau hapus
  property-nya.
- PEM di komentar `Program.cs` itu format SPKI (`BEGIN PUBLIC KEY`), sedangkan `CreateRsa()`
  memakai `ImportRSAPublicKey` yang menuntut PKCS#1. Formatnya tidak cocok.

**Rate limiting belum ada di repo.** `Handshake` adalah endpoint publik tanpa throttling.
Ketika authorization masuk, `Handshake` dan endpoint login akan menjadi dua action publik yang
wajib tetap terbuka (client belum punya key terverifikasi, jadi belum bisa mengenkripsi
kredensial) — di situlah rate limiting paling relevan dipasang.

**Tidak ada `.gitattributes`.** Churn CRLF berpotensi terulang setiap file disentuh editor
dengan konfigurasi EOL berbeda; `* text=auto eol=crlf` layak dipertimbangkan.
