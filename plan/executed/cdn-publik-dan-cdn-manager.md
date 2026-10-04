# CDN publik + CDN Manager

## Tujuan

Menambahkan fitur CDN ke engine API (`Em.Api.Core`) yang dinyalakan dari `Program.cs` lewat
`builder.EnableCdn("./data/cdn")` (stub baris ini sudah ada di `src/backend/Em.Api/Program.cs`, method-nya
belum ada):

1. Saat aktif, `GET /cdn/...` menyajikan file fisik di folder tersebut, **publik tanpa otorisasi**.
2. Dibuka dari browser, path folder menampilkan **directory listing** (seperti CDN pada umumnya).
3. Download mendukung **HTTP Range** (multipart/segmented download dan resume oleh download manager).
4. Saat tidak aktif, semua akses ke `/cdn` dan `/cdn/...` menjawab **404**.
5. Di client WPF ada layar **CDN Manager** untuk menelusuri folder, menambah (upload) serta menghapus file dan folder.

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Hak kelola | Claim baru **`Administrative Tools:CDN Manager Access`**, ditegakkan di gerbang server lewat atribut action; admin dan token debug otomatis lolos (perilaku `HasRequiredClaim` yang sudah ada). Di UI, navigasi CDN Manager diikat ke claim yang sama lewat `RequireClaim`, seperti User/Role Manager. |
| 2 | Struktur | **Mendukung subfolder**: buat folder, masuk ke folder, hapus folder. |
| 3 | Transport upload | **Lewat `PostAsync` biasa**: isi file dikirim sebagai `byte[]` (Base64 di JSON) dalam action dispatcher. Tidak ada endpoint multipart. |
| 4 | Batas ukuran | Parameter kedua **`EnableCdn(path, maxFileSizeMb)`** (megabyte), default **20 MB** lewat overload `EnableCdn(path)`. Tidak ada properti builder terpisah: semua pengaturan CDN ada di satu panggilan. |
| 5 | Listing browser | **HTML buatan engine**: breadcrumb, folder dulu lalu file, ukuran, tanggal ubah, tautan. |
| 6 | Nama file sudah ada | Server menolak **409** kecuali `overwrite = true`; UI minta konfirmasi timpa lalu mengirim ulang dengan `overwrite = true`. |
| 7 | Client | **WPF saja**. MAUI tidak mendapat layar maupun service client CDN (sama dengan User/Role Manager yang juga hanya ada di WPF). |
| 8 | Saat disable | Semua action CDN menjawab **404** `"CDN is not enabled on this server."`. Menu CDN Manager tetap terdaftar; saat dibuka, layar menampilkan keadaan kosong berisi pesan bahwa CDN nonaktif di server. |
| 9 | Path root | **Mengikuti input**: path absolut dipakai apa adanya; path relatif (termasuk `./...`) dihitung dari `ContentRootPath`. Folder dibuat otomatis saat startup kalau belum ada. |
| 10 | Hapus folder berisi | **Rekursif dengan konfirmasi**: UI menyebutkan jumlah file/folder di dalamnya lalu minta konfirmasi; server menghapus beserta isinya. |
| 11 | Commit | **Commit di akhir** (satu commit, pesan Bahasa Indonesia) setelah kedua solution build tanpa error. |

### Keputusan turunan (diambil saat menyusun plan, konsisten dengan keputusan di atas)

- **Rate limit tidak berlaku untuk `/cdn`.** Limiter hidup di `ProcessRequest`, sedangkan `/cdn` di luar dispatcher.
  Download manager membuka banyak koneksi Range sekaligus, jadi limit 300/menit per alamat akan memutus
  unduhan segmented. Action kelola CDN tetap lewat dispatcher, jadi tetap kena limit dan jejak request.
- **File dan folder berawalan titik tidak disajikan dan tidak boleh diunggah.** `PhysicalFileProvider` memang
  menyembunyikannya (`ExclusionFilters.Sensitive`), dan upload/buat folder menolak nama seperti itu. Dengan begitu
  file sementara upload (`.upload-*.tmp`) tidak pernah terlihat publik.
- **Tipe file tak dikenal tetap disajikan** sebagai `application/octet-stream` (`ServeUnknownFileTypes = true`),
  karena CDN bukan situs web, dan ekstensi apa pun (mis. `.msi`, `.apk`, `.7z`) harus bisa diunduh.
- **Upload ditulis atomik**: ditulis ke file sementara di folder tujuan, lalu `File.Move(..., overwrite)`.
  Download yang sedang berjalan atas file lama tidak menerima campuran isi lama dan baru.
- **Kestrel `MaxRequestBodySize`** dinaikkan otomatis bila perlu supaya muat batas ukuran file CDN setelah inflasi Base64
  (≈ 4/3 × ukuran + 1 MB cadangan untuk JSON). Hanya dinaikkan, tidak pernah diturunkan dari bawaan 30 MB.
- **Upload satu file per request.** Kalau user memilih banyak file sekaligus, UI mengirimnya berurutan, satu request per file.

## Desain

### 1. Builder (`Em.Api.Core/Api/Shared/EmAppBuilder.cs`)

Tambah region `CDN`:

```csharp
public const int DefaultCdnMaxFileSizeMb = 20;

internal string? CdnRootPath { get; private set; }   // null = CDN mati
internal long CdnMaxFileSize { get; private set; }   // byte, hasil maxFileSizeMb * 1024 * 1024

public void EnableCdn(string rootPath) =>
   EnableCdn(rootPath, DefaultCdnMaxFileSizeMb);

public void EnableCdn(string rootPath, int maxFileSizeMb)
   // throw ArgumentException kalau rootPath kosong/whitespace;
   // throw ArgumentOutOfRangeException kalau maxFileSizeMb <= 0;
   // throw InvalidOperationException kalau dipanggil dua kali
```

Semua pengaturan CDN hanya didefinisikan di satu panggilan `EnableCdn`, tanpa properti builder terpisah. Bentuknya
**dua overload** (pola `AddDebugToken(name, publicKey)` → `AddDebugToken(name, publicKey, days)`), bukan parameter
opsional, jadi di `Program.cs` bisa ditulis `builder.EnableCdn("./data/cdn")` atau `builder.EnableCdn("./data/cdn", 50)`.
Satuan `maxFileSizeMb` adalah megabyte (1 MB = 1024 × 1024 byte).

`EnableCdn` hanya menyimpan string mentahnya; resolusi ke path absolut dilakukan di `BuildApp` karena
`ContentRootPath` baru diketahui dari `WebApplicationBuilder.Environment`. Kedua overload `public` karena dipanggil dari
`Program.cs`. XML doc Bahasa Indonesia, tanpa menyebut nama objek database.

### 2. Status CDN di engine (`Em.Api.Core/Api/Core/CdnStore.cs`, baru, `internal sealed`)

Satu objek singleton yang memegang semua logika sistem file, dipakai baik oleh middleware publik maupun oleh
service kelola:

- `bool IsEnabled`, `string RootPath` (absolut, dengan separator di akhir), `long MaxFileSize`.
- `string ResolveInside(string relativePath)`: menormalkan `/` dan `\`, menolak segmen `..`, segmen kosong,
  segmen berawalan `.`, karakter `Path.GetInvalidFileNameChars()`, lalu `Path.GetFullPath(Path.Combine(RootPath, rel))`
  dan memastikan hasilnya berawalan `RootPath` (`StringComparison.OrdinalIgnoreCase` di Windows). Pelanggaran →
  `ActionException(..., 400)`. Ini satu-satunya pintu dari input user ke path fisik.
- `ValidateName(string name)`: nama file/folder tunggal (tanpa separator, tidak berawalan `.`, tidak `.`/`..`,
  bukan nama tercadang Windows seperti `CON`, `NUL`, dst., panjang ≤ 255).
- `EnsureEnabled()`: kalau mati, `throw new ActionException("CDN is not enabled on this server.", 404)`.
- Operasi: `List(rel)`, `Upload(rel, name, bytes, overwrite)`, `CreateFolder(rel, name)`, `Delete(rel)`,
  `CountInside(rel)`.

Dibuat di `BuildApp` setelah callback builder: resolve path (absolut → apa adanya, relatif → gabung dengan
`app.Builder.Environment.ContentRootPath`), `Directory.CreateDirectory` kalau aktif, lalu didaftarkan
`Services.AddSingleton(cdnStore)`. Saat CDN mati tetap didaftarkan dengan `IsEnabled = false`, supaya service
kelola selalu bisa diresolve dan menjawab 404 sendiri.

Kalau aktif, di `BuildApp` juga dikonfigurasi `KestrelServerOptions.Limits.MaxRequestBodySize` (lihat keputusan turunan).

### 3. Penyajian publik (`Em.Api.Core/Api/Core/EmApp.cs` → `Run`)

Dipasang **sebelum** `MapFallback` (yang saat ini menjawab 200 teks sambutan untuk path apa pun, jadi `/cdn`
tanpa penanganan eksplisit akan salah menjawab 200):

```csharp
if (cdn.IsEnabled) {
   var provider = new PhysicalFileProvider(cdn.RootPath);   // ExclusionFilters.Sensitive (bawaan)
   app.UseStaticFiles(new StaticFileOptions {
      RequestPath = "/cdn", FileProvider = provider,
      ServeUnknownFileTypes = true, DefaultContentType = "application/octet-stream"
   });
   app.UseDirectoryBrowser(new DirectoryBrowserOptions {
      RequestPath = "/cdn", FileProvider = provider, Formatter = new CdnDirectoryFormatter()
   });
}
app.Map("/cdn", b => b.Run(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; }));
```

- `StaticFileMiddleware` sudah menangani `Range`, `If-Range`, `ETag`, `Last-Modified`, `HEAD`, `206`/`416`, dan
  `Accept-Ranges: bytes`. Tidak ada kode Range buatan sendiri.
- `app.Map("/cdn", ...)` di akhir menangkap sisa request: path yang tidak ada saat CDN aktif, dan **semua** path saat
  CDN mati. Keduanya dijawab 404 polos (bukan envelope `ActionResult`, karena ini bukan API action).
- `UseForwardedHeaders` tetap paling depan (sudah begitu), jadi tautan di listing memakai skema/host yang benar di
  belakang reverse proxy.
- Tidak ada `UseAuthentication`/gerbang identitas di jalur ini: publik.

`Em.Api.Core/Api/Core/CdnDirectoryFormatter.cs` (baru, `internal sealed`, `IDirectoryFormatter`): HTML5 mandiri
(CSS inline, tanpa aset luar), semua nama di-`HtmlEncoder.Default.Encode`, href di-`Uri.EscapeDataString` per segmen.
Isi: judul `Index of /cdn/<path>`, breadcrumb per segmen, baris `..` kecuali di root, folder dulu (urut nama) lalu
file (urut nama), kolom Nama / Ukuran (format manusiawi, `title` berisi byte persis) / Diubah (UTC,
`yyyy-MM-dd HH:mm`). Mendukung light/dark lewat `prefers-color-scheme`. Path tanpa garis miring akhir di-redirect ke
yang bergaris miring (perilaku bawaan `DirectoryBrowserMiddleware`), jadi tautan relatif aman.

### 4. Kontrak service (`src/shared/Em.Libs/Api.Core.Models/`)

Namespace `Em.Api.Core.Models` (milik engine, lihat aturan "Where a model belongs").

`CdnEntry.cs`:

```csharp
public class CdnEntry {
   public string Name { get; set; } = "";
   public string Path { get; set; } = "";          // relatif terhadap root CDN, pemisah '/'
   public bool IsFolder { get; set; }
   public long Size { get; set; }                  // 0 untuk folder
   public DateTimeOffset LastModified { get; set; }
}
```

`CdnFolderContent.cs`: `Path`, `CdnEntry[] Entries`, `long MaxFileSize`, `string PublicPath` (mis. `cdn/foo/`,
dipakai UI untuk menyalin tautan publik, digabung dengan base URL koneksi aktif).

`CdnItemCount.cs`: `int Files`, `int Folders` (untuk konfirmasi hapus rekursif).

`ICdnServices.cs` (`: IServices`). Semua action bukan milik satu tabel, jadi berawalan `Meta` (aturan penamaan action):

| Action | Atribut | Fungsi |
| --- | --- | --- |
| `Task<CdnFolderContent> GetMeta_CdnFolder(string? path)` | `[GetAction(claim: CdnClaim)]` | Isi satu folder (null/kosong = root). |
| `Task<CdnItemCount> GetMeta_CdnItemCount(string path)` | `[GetAction(claim: CdnClaim)]` | Jumlah file & folder di dalam folder (rekursif). |
| `Task<CdnEntry> PostGetMeta_CdnUpload(string? path, string fileName, byte[] content, bool overwrite)` | `[PostAction(claim: CdnClaim)]` | Upload satu file; 409 kalau sudah ada dan `overwrite == false`; 413 kalau melebihi `MaxFileSize`. |
| `Task<CdnEntry> PostGetMeta_CdnCreateFolder(string? path, string folderName)` | `[PostAction(claim: CdnClaim)]` | Buat subfolder; 409 kalau sudah ada. |
| `Task PostMeta_CdnDelete(string path)` | `[PostAction(claim: CdnClaim)]` | Hapus file, atau folder beserta isinya; menolak root (400); 404 kalau tidak ada. |

`CdnClaim = "CDN Manager Access"` sebagai konstanta. Nama module service: `"Administrative Tools"` (konstanta
baru di `Em.Libs/Defaults.cs`, `AdministrativeToolsModuleName`), karena `HasRequiredClaim` mencocokkan module claim
dengan module service. **Periksa atribut setiap action sebelum lanjut** (aturan "The name obliges the attribute").

Wadah `DtoPayload` tidak dipakai: parameter action cukup sedikit dan tipenya berbeda.

### 5. Service backend (`Em.Api.Core/Api/Core/CdnServices.cs`, baru)

`[Module(Defaults.AdministrativeToolsModuleName)] public class CdnServices : ServicesBase, ICdnServices`, region
`Meta's`. Setiap action: `GetService<CdnStore>()!.EnsureEnabled()` dulu, lalu delegasi ke `CdnStore`. Didaftarkan
di `InitInternalServices` dengan `builder.AddService<ICdnServices, CdnServices>(enforceClaims: false)`. Claim
eksplisit di atribut tetap ditegakkan (lihat komentar di `HasRequiredClaim`). Komentar di situ diperbarui dari
"ketiganya" menjadi keempatnya beserta alasannya.

Periksa `EnsureClaimModulesAreRegistered` dan cara client memanggil module bernama dengan spasi:
`api/Administrative Tools/GetMeta_CdnFolder` di-escape `HttpClient` menjadi `%20` dan di-decode lagi oleh routing.
**Uji ini secara nyata** (langkah verifikasi 3). Kalau ternyata gagal, jangan ganti nama claim; perbaiki pencocokan
module-nya dan catat di laporan eksekusi.

### 6. Client WPF (`src/shared/Em.Ui.Wpf.Core`)

- `Api.Core/CdnService.cs`: client untuk `ICdnServices`, mengikuti bentuk `Api.Core/CredentialService.cs` (controller =
  nama module, `GetAsync`/`PostAsync` dengan `nameof(...)`). Tidak dicerminkan ke MAUI (keputusan 7); catat
  pengecualian ini di laporan akhir.
- `Core/EmApp.Statics.cs`: `CdnManagerClaim = "Administrative Tools:CDN Manager Access"`, `AddInternalClaim` di
  `InitInternalClaims`, dan navigasi baru `admin.cdn` (`Title = "CDN Manager"`, `Subtitle = "Manage public files"`,
  `Kind = NavigationKind.Manager`, `IsMenuVisible = false` seperti admin lain, ikon FontAwesome 6
  `Solid_CloudArrowUp`; kalau nama itu tidak ada di `EFontAwesomeIcon`, pakai `Solid_Cloud`) dibungkus `RequireClaim`.
- `Core/EmApp.cs`: tambahkan `"admin.cdn"` ke larik `["admin.users", "admin.roles"]` yang membangun daftar
  `StaticTool` (sekitar baris 209), supaya CDN Manager muncul di menu alat admin dengan saringan `CanOpen` yang sama.
- Semua teks yang tampil di UI (label, pesan, judul dialog) ditulis dalam **Bahasa Inggris**, sesuai aturan string
  literal di CLAUDE.md. Hanya XML doc yang berbahasa Indonesia.
- `Navigations/CdnManager.xaml(.cs)` + `CdnManagerVm : MvvmModelBase`, bahasa desain material mengikuti
  `UserManager.xaml` (tool strip + list card), token dari `Styles/`:
  - Tool strip: breadcrumb (klik segmen = pindah folder), tombol **Upload**, **New Folder**, **Refresh**,
    **Copy Link**, **Delete**.
  - List card: ikon folder/file, Nama, Ukuran, Diubah. Double click pada baris folder masuk ke folder
    (`MouseBinding LeftDoubleClick`, bukan code-behind). Seleksi tunggal.
  - Keadaan kosong: "This folder is empty", dan keadaan **CDN nonaktif** (action menjawab 404) berupa pesan besar
    "CDN is not enabled on this server" dengan semua perintah dimatikan.
  - Batas ukuran dari `CdnFolderContent.MaxFileSize` ditampilkan di tool strip, dan file yang melebihinya ditolak di
    client sebelum dikirim.
  - Perintah (semua lewat `RegisterCommand` + method `…Allowed`): `OpenFolderCommand`, `GoUpCommand`,
    `NavigateToSegmentCommand`, `UploadCommand`, `NewFolderCommand`, `DeleteCommand`, `RefreshCommand`,
    `CopyLinkCommand`.
  - `UploadCommand`: `Microsoft.Win32.OpenFileDialog` (`Multiselect = true`) dengan owner `DialogOwner`, dibaca per
    file lalu dikirim berurutan. Kalau kena 409: `DialogOwner.ShowMboxDecideCancel(caption, "File Exists")`, dengan
    caption `"'{name}' already exists in this folder. Overwrite it?\n\nYes: overwrite · No: skip this file · Cancel: stop uploading"`
    (tombolnya tetap Yes/No/Cancel karena helper itu tidak menerima label sendiri). `Yes` mengirim ulang dengan
    `overwrite: true`, `No` melanjutkan ke file berikutnya, dan `Cancel` menghentikan sisa antrean. Progres
    "Uploading n of m" tampil di tool strip, dan perintah lain mati selama upload berjalan.
  - `NewFolderCommand`: dialog input nama kecil. Pakai dialog input yang sudah ada di `Dialogs/` kalau ada; kalau
    tidak ada, buat `Dialogs/TextInputDialog` bermodel `MvvmModelBase` sesuai aturan dialog (`RequestClose`).
  - `DeleteCommand`: file → `ShowMboxDecideWarning` dengan nama file; folder → ambil `GetMeta_CdnItemCount` dulu, lalu
    konfirmasi `"Delete folder '{name}' and the {files} file(s) and {folders} folder(s) inside it?"`.
  - `CopyLinkCommand`: `ActiveConnection.Host` (garis miring akhir dirapikan) + `/` + `PublicPath` + nama (segmen
    di-escape), disalin ke clipboard. Untuk folder, tautannya berakhir `/`.
  - Galat lain lewat `AlertError`.
- Timeout: `HttpRequestTimeout` di client dan server (30 detik) bisa terlalu pendek untuk upload 20 MB di jaringan
  lambat. Plan ini **tidak** mengubahnya. Catat sebagai temuan di laporan kalau uji upload 20 MB mendekati batas itu.

### 7. Dokumentasi

- `src/backend/CLAUDE.md`: subbagian singkat "CDN" di bawah arsitektur. Isinya: route `/cdn` di luar dispatcher,
  publik, Range dari `StaticFileMiddleware`, bebas rate limit (dan alasannya), 404 saat mati, pengelolaan lewat
  `ICdnServices` ber-claim, serta larangan menulis logika path di luar `CdnStore.ResolveInside`.
- `src/frontend/CLAUDE.md`: sebut `CdnManager` di daftar layar bawaan kalau daftar semacam itu ada.
- XML doc Bahasa Indonesia untuk semua member `public` baru di `src/shared` dan untuk kedua overload `EnableCdn`.

## Langkah eksekusi

1. `EmAppBuilder`: dua overload `EnableCdn`, `CdnRootPath`, `CdnMaxFileSize` (internal).
2. `Defaults.AdministrativeToolsModuleName`, lalu `CdnEntry`, `CdnFolderContent`, `CdnItemCount`, `ICdnServices` di `Em.Libs`.
3. `CdnStore`, `CdnDirectoryFormatter`, `CdnServices`; wiring di `BuildApp` (store, Kestrel limit) dan `Run`
   (static files, directory browser, `Map("/cdn")` 404) sebelum `MapFallback`; registrasi service di `InitInternalServices`.
4. `dotnet build src/backend/Em.Api.slnx` sampai tanpa error.
5. Client WPF: `CdnService`, claim + navigasi, `CdnManager` + VM (dan dialog input kalau perlu), lalu masuk ke menu admin.
6. `dotnet build src/frontend/Em.Ui.Wpf.slnx` sampai tanpa error.
7. Dokumentasi (bagian 7).
8. Verifikasi (di bawah).
9. Pindahkan plan ini ke `plan/executed/`, lalu commit (pesan Bahasa Indonesia, diakhiri baris `Co-Authored-By`).
   Yang di-commit: perubahan fitur ini, termasuk baris `EnableCdn` di `Program.cs`. **Jangan ikut sertakan**
   `src/backend/.idea/.idea.Em.Api/.idea/inspectionProfiles/` yang untracked.

## Verifikasi

Jalankan API (`dotnet run --project src/backend/Em.Api/Em.Api.csproj`), taruh beberapa file uji (termasuk satu
≥ 50 MB, satu bernama dengan spasi, dan satu `.hidden`) di `src/backend/Em.Api/data/cdn/` plus satu subfolder.

1. **Listing**: `curl -i http://localhost:5132/cdn/` → 200 HTML, folder dulu, `.hidden` tidak muncul; `/cdn/sub`
   → redirect ke `/cdn/sub/`; nama dengan spasi/karakter HTML ter-escape.
2. **Range/resume**:
   - `curl -I .../cdn/big.bin` → `Accept-Ranges: bytes`, `ETag`, `Last-Modified`.
   - `curl -r 0-1023 -o part .../cdn/big.bin -w "%{http_code}"` → `206` dan 1024 byte.
   - `curl -r 999999999999- ...` → `416`.
   - Unduh dengan `curl -C -` lalu putus di tengah, lanjutkan, dan bandingkan hash dengan aslinya.
   - Sepuluh request Range paralel tidak ada yang kena 429.
3. **Traversal**: `/cdn/../Program.cs`, `/cdn/%2e%2e/...`, dan `/cdn/.hidden` → 404. Action dengan path `../x`
   atau `a/../../x` → 400.
4. **Disable**: komentari `EnableCdn`, lalu `/cdn`, `/cdn/`, `/cdn/apa.txt` → 404, dan `GetMeta_CdnFolder` → 404
   `CDN is not enabled on this server.` Kembalikan barisnya.
5. **Hak kelola** (via WPF atau request langsung): tanpa login → 401; user tanpa claim → 403; user dengan claim
   `Administrative Tools:CDN Manager Access` → OK; admin → OK. Pastikan module bernama dengan spasi ter-route benar.
6. **CDN Manager (WPF)**: telusuri folder, buat folder, upload banyak file (termasuk konflik → Yes/No/Cancel),
   file > batas ditolak di client, hapus file, hapus folder berisi (angka di konfirmasi benar), dan Copy Link bisa
   dibuka di browser. Uji keadaan CDN nonaktif, lalu uji juga di layout SPA dan MultiTab.
7. Upload 20 MB: catat durasinya terhadap timeout 30 detik.

## Di luar cakupan

- Client MAUI (layar dan service client).
- Endpoint upload multipart/streaming dan upload file lebih besar dari batas `maxFileSizeMb`.
- Rename/move file, kuota folder, dan audit log siapa yang upload.
- Cache header khusus (`Cache-Control`) di luar bawaan `StaticFileMiddleware`.
- CORS: tidak relevan, karena `/cdn` hanya GET sederhana tanpa preflight, dan tidak ada UI browser yang memanggil API.

## Laporan eksekusi (2026-09-26)

Semua langkah dijalankan; kedua solution build tanpa error dan tanpa warning baru.

### Hasil verifikasi

1. **Listing**: `/cdn/` → 200 HTML (folder dulu, `.hidden` tidak muncul, `&` ter-escape); `/cdn/sub` dan `/cdn` → 301 ke versi bergaris miring.
2. **Range/resume**: `HEAD` memberi `Accept-Ranges: bytes`, `ETag`, `Last-Modified`; `-r 0-1023` → 206 dan 1024 byte; Range di luar ukuran → 416; unduhan 55 MB yang diputus lalu dilanjutkan `curl -C -` → 206, hash SHA-256 sama dengan aslinya; 10 Range paralel dan 40 request beruntun → semua 206, tidak ada 429.
3. **Traversal**: `/cdn/../Program.cs`, `/cdn/%2e%2e/...`, `/cdn/sub/%2e%2e%2f...`, `/cdn/.hidden` → 404. Action dengan `../x`, `a/../../x`, `.hidden` → 400. `CON` dan nama berawalan titik ditolak 400.
4. **Disable**: `/cdn`, `/cdn/`, `/cdn/big.bin`, `/cdn/sub/` → 404; `GetMeta_CdnFolder` → 404 `CDN is not enabled on this server.`; layar CDN Manager menampilkan keadaan nonaktif.
5. **Hak kelola**: tanpa login → 401; token debug → OK; module `Administrative Tools` (dengan spasi) ter-route benar lewat `%20`. **403 untuk user tanpa claim tidak diuji dengan user nyata** (tidak ada akun uji ber-password yang diketahui di database dev); jalurnya memakai `HasRequiredClaim` yang sudah ada, dengan `RequiredClaim` terisi dari atribut.
6. **CDN Manager (WPF)**, diuji lewat klik nyata di layout MultiTab dan SPA: telusuri folder (double click, breadcrumb, naik), buat folder bernama dengan spasi, upload banyak file dengan konflik (Yes → timpa), file > 20 MB ditolak di client dengan peringatan, hapus folder berisi (konfirmasi menyebut "1 file(s) and 0 folder(s)"), Copy Link (`http://localhost:5132/cdn/sub/deep/`).
7. **Upload 20 MB**: 0,27 detik di localhost, jauh di bawah timeout 30 detik. Di jaringan lambat (mis. 5 Mbps) 20 MB + inflasi Base64 butuh ±45 detik, jadi akan melewati `HttpRequestTimeout` client; timeout tidak diubah sesuai plan.

### Temuan dan penyimpangan

- **Urutan middleware**: `UseStaticFiles`/`UseDirectoryBrowser` tidak jalan untuk path tanpa ekstensi (`/cdn/`), karena `WebApplication` memasang routing di depan dan `MapFallback` sudah memilih endpoint. Diperbaiki dengan memasang middleware CDN sebelum `app.UseRouting()` yang dipanggil eksplisit di `Run`.
- **`MvvmModelBase.Get/Set` hanya untuk properti public**: properti `private` yang memakai pola itu gagal saat runtime (`Member "IsLoaded" is not supported`), jadi `IsLoaded` dibuat public dengan setter private.
- **Trim di getter properti yang di-binding TwoWay** membuang spasi saat mengetik (binding membaca ulang source). `TextInputDialogVm.Value` kini menyimpan apa adanya; hasil yang di-trim dibaca lewat `Result`.
- Atribut `[GetAction]`/`[PostAction]` ditaruh di class backend `CdnServices` (bukan di interface), mengikuti pola service lain.
- Status 404 "CDN nonaktif" dibedakan dari "folder tidak ada" secara struktural, bukan dari teks pesan: 404 di subfolder → kembali ke akar; 404 di akar → CDN nonaktif.
- Tidak dicerminkan ke MAUI (keputusan 7).
- Folder runtime `src/backend/Em.Api/data/` tidak di-ignore git; file yang diunggah saat pengembangan akan muncul sebagai untracked. Belum ditambahkan ke `.gitignore` karena di luar plan.
