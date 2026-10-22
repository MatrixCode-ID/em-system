# Plan — Skin CDN dan halaman home yang bisa diganti saat builder (untuk Codex)

Status: **belum dieksekusi**
Dibuat: 2026-10-22
Pembaca: Codex. Baca juga `CLAUDE.md` di root repo ini dan `CLAUDE.md` subfolder pada jalur berkas yang
disentuh. Semua keputusan di bawah adalah keputusan pengguna; bila selama eksekusi muncul hal yang tidak
tercakup, ambil pilihan yang paling konsisten dengan plan ini dan catat di laporan, jangan berhenti untuk
bertanya.

Asal: catatan ide di repo host turunan (privat), dibahas dan diputuskan pengguna 2026-10-22. Plan ini
memuat semua keputusan yang diperlukan; catatan ide tidak perlu dibaca.

Plan pasangan: repo host turunan punya plan skin miliknya sendiri yang memakai API dari plan ini.
**Urutan eksekusi lintas repo (keputusan pengguna):** tulis seluruh kode plan ini lebih dulu, lalu kode plan
host; build, test, dan review baru dijalankan setelah kode kedua plan selesai. Jangan menjalankan test di
antara kedua plan.

---

## 1. Tujuan

Host API bisa mengganti **tampilan** (skin) halaman yang dihasilkan engine, untuk kebutuhan rebranding:

1. **Skin CDN** — halaman daftar folder dan halaman not found di bawah `/cdn`.
2. **Skin home** — halaman `/`, halaman not found situs untuk path tak dikenal, dan `/favicon.ico`.

Cara melayani berkas CDN **tidak berubah** (Range, `If-Range`, ETag, HEAD, 206/416, unduhan paralel,
tanpa rate limit, CDN Manager, batas upload, managed folder). Skin hanya dipanggil untuk halaman, tidak
pernah untuk unduhan berkas.

## 2. Aturan kerja (untuk Codex)

- **Repo publik dan netral produk.** Jangan menulis nama produk/host turunan, modul bisnis privat, alamat
  server, nama database, atau secret di kode maupun dokumen.
- Komentar kode, XML doc, teks UI/halaman, pesan error/exception/log: **bahasa Inggris**. Plan, laporan,
  `doc/ideas/` dan `CLAUDE.md`: Bahasa Indonesia. Member public/protected baru wajib XML doc (CS1591 aktif).
- **Urutan kerja:** tulis seluruh kode plan ini, lalu kode plan host; build, test terfilter, dan review sekali
  di akhir (bagian 8).
- **Commit:** periksa branch aktif harus `work-bench` (bila bukan, jangan commit dan catat di laporan). Stage
  hanya path yang diubah plan ini (working tree mungkin berisi perubahan lain yang bukan milik plan ini);
  jangan `git add -A`, jangan `--no-verify`, **jangan push**. Commit dilakukan setelah verifikasi bagian 8.
- Test yang dirawat masuk `tests/Em.Api.Core.Tests` (xUnit v3/MTP). Tidak ada harness smoke dan tidak ada
  uji visual (keputusan pengguna); pemeriksaan visual dilakukan pengguna.
- Bila perintah ditolak policy: jangan memutar lewat wrapper lain; ikuti aturan "Tindakan terblokir policy"
  di `CLAUDE.md`, kerjakan bagian lain, dan catat di laporan.
- Jangan menambah release note NuGet (menambahkannya ke `main` memicu rilis). Perubahan perilaku dicatat di
  guide dan laporan sebagai bahan release note berikutnya.

## 3. Kondisi sekarang (titik awal)

- `EmApp.MapCdn` (`src/backend/Em.Api.Core/Api/Core/EmApp.cs`) memasang `UseStaticFiles` dan
  `UseDirectoryBrowser` dengan `Formatter = new CdnDirectoryFormatter()` (internal, HTML ditulis lewat
  `StringBuilder`, CSS inline). Sisa path `/cdn` dijawab 404 tanpa body oleh `app.Map("/cdn")`.
- `MapFeatureServiceGate` (`EmApp.FeatureServices.cs`) menjawab 404 tanpa body untuk `/cdn` saat service
  CDN dimatikan (`FeatureServiceRegistry.AllowPublicAsync`).
- `CdnFileProvider` memakai `PhysicalFileProvider(store.RootPath, ExclusionFilters.None)`; entri ber-dot
  **tidak** disembunyikan. Komentar di `MapCdn` ("ExclusionFilters.Sensitive (the default) hides
  dot-prefixed…") dan `CdnStore.VisibleEntries` ("Mirrors … Sensitive") tidak sesuai kode; perbaiki
  komentarnya sekalian.
- Root publik CDN adalah `<storage>/files/` (`CdnStore.RootPath`); `temp/`, `recycle/`, `settings/` di luar
  root publik. `CdnStore.ValidateName` membolehkan nama berawalan titik. Builder archive sengaja
  memasukkan entri ber-dot.
- `app.MapFallback(() => "Welcome to PT. EM ASIA API! This path is empty!")` menangkap setiap path tanpa
  ekstensi yang tidak cocok endpoint lain, status 200 `text/plain`. Tidak ada titik ganti dari host.
- `AddPublicEndpoint` menolak prefiks yang tumpang tindih dengan `/api`, `/cdn`, `/v2`.
- `EmAppBuilder` backend (`Em.Api.Shared`) belum punya `ApplicationName`.
- `Em.Api.Core` memakai `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`, sudah memuat
  `Microsoft.AspNetCore.Components.Web` (`HtmlRenderer`); tidak perlu paket baru.
- Logo dan ikon engine yang sudah ada: `src/shared/Em.Ui.Maui.Core/Resources/Images/em_logo.svg`,
  `doc/assets/logo/Logo.ico`.

## 4. Keputusan final

### 4.1 Istilah dan nama tipe

- Fitur disebut **skin**. Tipe yang diturunkan/diimplementasikan host berawalan `Em`: `IEmCdnSkin`,
  `IEmHomeSkin`, `EmRazorCdnSkin<TDirectory, TNotFound>`, `EmRazorHomeSkin<THome, TNotFound>`.
- **Tanpa** awalan: model halaman (`CdnDirectoryPage`, `CdnPageEntry`, `CdnBreadcrumb`, `CdnNotFoundPage`,
  `HomePage`, `SiteNotFoundPage`), skin bawaan internal (`DefaultCdnSkin`, `DefaultHomeSkin`), helper
  `EmbeddedFile`, dan method builder.
- Semua tipe skin dan model halaman berada di `Em.Api.Core` (tidak dipakai UI client, tidak masuk `Em.Libs`).

### 4.2 Registrasi

- Dua pintu, **terpisah dan tidak sama**, masing-masing satu kali per aplikasi (panggilan kedua melempar
  `InvalidOperationException`, sama seperti `EnableCdn`):
  - `builder.UseCdnSkin<TSkin>() where TSkin : class, IEmCdnSkin`
  - `builder.UseHomeSkin<TSkin>() where TSkin : class, IEmHomeSkin`
- Skin dibuat lewat DI (singleton), jadi boleh menerima service lain di constructor.
- Tidak ada overload factory dan tidak ada overload Razor. Skin `.razor` ditulis sebagai kelas turunan base
  class Razor lalu didaftarkan lewat pintu yang sama.
- Registrasi CDN skin tidak bergantung pada `EnableCdn` (CDN bisa nyala dari managed storage settings).
- Tambah `EmAppBuilder.ApplicationName` (string, default nama entry assembly) untuk diisi host; dipakai
  model halaman.

### 4.3 Kontrak

Bentuk minimal (nama member boleh disesuaikan bila ada alasan, catat di laporan):

```csharp
public interface IEmCdnSkin {
   /// Folder listing under /cdn (browser only).
   Task RenderDirectoryAsync(CdnDirectoryPage page, HttpContext context);
   /// Browser-facing not found page for every 404 under /cdn.
   Task RenderNotFoundAsync(CdnNotFoundPage page, HttpContext context);
   /// Embedded assets copied to /cdn/.skin at startup; null = no assets.
   EmSkinAssets? Assets { get; }
}

public interface IEmHomeSkin {
   Task RenderHomeAsync(HomePage page, HttpContext context);          // GET / for browsers
   Task RenderNotFoundAsync(SiteNotFoundPage page, HttpContext context); // unknown paths for browsers
   string RenderPlainText(HomePage page);                              // GET / for non-browser clients
   EmbeddedFile? Favicon { get; }                                     // served at /favicon.ico; null = 404
}
```

- `EmSkinAssets`: assembly + prefiks resource (mis. `cdn-skin/`); semua embedded resource ber-`LogicalName`
  dengan prefiks itu menjadi berkas di `.skin` (path relatif = sisa nama setelah prefiks, `/` sebagai pemisah).
- `EmbeddedFile`: `From<T>(string logicalName)` (assembly dari `T`), `ReadText()`, `ReadBytes()`,
  `ContentType`. Resource yang hilang melempar exception yang jelas **saat startup**, bukan saat request.
- Model halaman (record, baca saja):
  - `CdnDirectoryPage`: `RequestPath`, `Breadcrumbs` (label + href sudah di-escape), `HasParent`,
    `Folders`, `Files` (`CdnPageEntry`: `Name`, `Href`, `IsDirectory`, `Length?`, `Modified`), serta data
    tambahan **sebagai informasi saja**: `ApplicationName`, `HostVersion`, `MaxFileSize`, jumlah folder/berkas.
    Helper `Asset(string path)` → `/cdn/.skin/<path>?v=<hash>`; `null` bila skin tidak punya aset itu.
  - `CdnNotFoundPage`: `RequestPath`, `ApplicationName`, `HostVersion`, `Asset(...)` seperti di atas (tetap
    `null` saat CDN mati karena `.skin` tidak tersedia).
  - `HomePage`: `ApplicationName`, `HostVersion` (informational version **assembly host/entry**, bukan versi
    engine; diformat seperti `doc/engine/build.md#product-version`: metadata build `+…` dibuang,
    `0.0.0-dev` tampil `dev`), `CdnUrl` (`/cdn/` bila CDN aktif dan service menyala, selain itu `null`). Tanpa helper aset.
  - `SiteNotFoundPage`: `RequestPath`, `ApplicationName`, `HostVersion`.
- Base class Razor (abstrak, mengimplementasikan interface di atas):
  - `EmRazorCdnSkin<TDirectory, TNotFound> where TDirectory : IComponent where TNotFound : IComponent`;
    `protected virtual string? AssetPrefix => null` (assembly = `typeof(TDirectory).Assembly`). Komponen
    menerima `[Parameter] public CdnDirectoryPage Page` / `CdnNotFoundPage Page`.
  - `EmRazorHomeSkin<THome, TNotFound>`; `protected virtual EmbeddedFile? Favicon => null`;
    `public virtual string RenderPlainText(HomePage page) => $"Welcome to {page.ApplicationName}"`.
  - Render memakai satu helper internal bersama berbasis `HtmlRenderer` (`Dispatcher.InvokeAsync`,
    `ILoggerFactory` dari DI), tanpa MVC, tanpa `AddRazorComponents`/`MapRazorComponents`. Hasil ditulis ke
    buffer; adaptor engine yang menetapkan `Content-Type: text/html; charset=utf-8`, `Content-Length`,
    status, dan body (kecuali HEAD).

### 4.4 Negosiasi browser

- Halaman HTML skin (directory listing CDN, not found CDN, home, not found situs) dirender hanya untuk
  request `GET` yang header `Accept`-nya memuat `text/html`. Directory listing mengikuti perilaku
  middleware sekarang untuk metode lain.
- Klien lain (launcher, download manager, `HEAD`, request non-HTML) menerima perilaku lama: 404 tanpa body;
  `/` menjawab teks polos `RenderPlainText(...)` dengan status 200 (HEAD tanpa body).

### 4.5 Skin CDN dan folder `.skin`

- `.skin` adalah **folder sungguhan** di root publik CDN (`<storage>/files/.skin`), khusus untuk aset skin
  CDN. Tidak ada aplikasi atau modul lain (termasuk home) yang memakainya. Dilayani lewat jalur unduhan CDN
  yang ada: `/cdn/.skin/<berkas>`.
- **Ditulis saat API run, sebelum server menerima request** (di `EmApp.Run()` sebelum `app.Run()`):
  - CDN aktif dan skin CDN punya `Assets` → kosongkan `.skin` lalu tulis ulang **langsung** dari embedded
    resource. **Tanpa folder sementara, tanpa kunci, tanpa berkas penanda.**
  - CDN aktif dan tidak ada skin beraset (registrasi dibuang atau skin bawaan) → hapus `.skin` bila ada.
  - Gagal menulis atau menghapus → **startup gagal** dengan exception yang jelas (bukan peringatan).
  - Host tidak memanggil `EnableCdn` → tidak ada yang ditulis; service CDN dimatikan lewat toggle → `.skin`
    di disk tidak disentuh tetapi `/cdn/.skin/...` ikut 404 seperti isi CDN lain.
  - Skenario beberapa instance yang berbagi penyimpanan CDN **ditunda** (tidak ditangani di plan ini).
- **Disembunyikan di semua tempat**:
  - Daftar folder publik `/cdn/`: tidak tampil. Membuka `/cdn/.skin/` (atau subfoldernya) sebagai folder:
    not found (tidak bisa ditelusuri). URL berkas `/cdn/.skin/<berkas>`: dilayani.
  - CDN Manager (`CdnStore` dan action `ICdnServices`/`ICdnReleaseServices`): tidak tampil di daftar, tidak
    dihitung (`CdnItemCount`/info storage), tidak bisa diunggah, dibuat, di-rename, dipindah, dihapus,
    di-extract, atau dijadikan managed folder; tidak masuk recycle bin.
  - Unduh archive folder root: `.skin` dikecualikan.
- **Nama `.skin` di root CDN dicadangkan selalu** (tidak bergantung ada atau tidaknya skin): operasi
  management dengan nama/path `.skin` di root ditolak dengan pesan jelas (400). Buat satu pemeriksaan
  terpusat di `CdnStore` (mis. `IsReservedRootName`) yang dipakai semua jalur di atas.
- `DefaultCdnSkin` = `CdnDirectoryFormatter` sekarang dijadikan skin bawaan: **tampilan daftar folder
  tetap sama**, tanpa aset. Not found CDN bawaan: halaman HTML sederhana bergaya sama (browser saja).
- Halaman not found CDN dipakai untuk **ketiga** penyebab 404 di bawah `/cdn` (path tidak ada, CDN tidak
  diaktifkan host, service dimatikan lewat toggle, termasuk di `MapFeatureServiceGate`), satu halaman tanpa
  alasan, status tetap 404.

### 4.6 Home dan not found situs

- Home dipetakan eksplisit untuk `/` (`GET`/`HEAD`), **tidak bergantung CDN** dan tidak melewati gerbang
  toggle CDN.
- `MapFallback` tidak lagi menjawab 200 teks welcome: path tak dikenal menjawab **404**; untuk browser
  dirender not found situs milik skin home. **Ini perubahan perilaku** (sebelumnya 200); catat di guide
  dan laporan sebagai bahan release note.
- `/favicon.ico`: dilayani dari `IEmHomeSkin.Favicon` (dibaca sekali saat startup, disimpan di memori,
  `Content-Type` dari ekstensi, cache wajar); `null` → 404. Tidak ditulis ke disk atau CDN; tetap ada walau
  CDN mati. Tambah `/favicon.ico` ke daftar prefiks yang ditolak `AddPublicEndpoint`.
- Home tidak memakai aset. Logo home inline (SVG disisipkan sebagai markup; bitmap kecil boleh data URI).
- `DefaultHomeSkin`:
  - Browser: halaman HTML sederhana — logo Em SVG inline, teks **`Welcome To Matrix-Code API System`**, versi
    host; not found situs bergaya sama; ikon Em di `/favicon.ico`.
  - Non-browser: teks polos `Welcome To Matrix-Code API System`.
  - Logo dan ikon diambil dari berkas yang sudah ada lewat `EmbeddedResource Link` di `Em.Api.Core.csproj`
    (pola sama dengan skrip SQL log store), tanpa salinan: `em_logo.svg` dan `doc/assets/logo/Logo.ico`.
  - Halaman bawaan mendukung terang/gelap (`prefers-color-scheme`) dan lebar ponsel.
  - `DefaultHomeSkin` sekaligus menjadi contoh untuk penulis skin (rujuk dari guide).

## 5. Langkah kode

1. **Kontrak dan model** — tipe di 4.3 di `src/backend/Em.Api.Core/Api/Skin/` (namespace `Em.Api.Core`,
   ikuti pola namespace folder yang ada), `EmbeddedFile`, `EmSkinAssets`, helper render `HtmlRenderer`.
2. **Builder** — `UseCdnSkin<T>()`, `UseHomeSkin<T>()`, `ApplicationName` di `EmAppBuilder` (partial baru
   mis. `EmAppBuilder.Skin.cs`); registrasi default (`DefaultCdnSkin`, `DefaultHomeSkin`) bila host tidak
   mendaftarkan; tambah `/favicon.ico` ke daftar tumpang tindih `AddPublicEndpoint`.
3. **Skin bawaan** — pindahkan `CdnDirectoryFormatter` menjadi `DefaultCdnSkin` (perilaku dan HTML sama);
   adaptor `IDirectoryFormatter` internal yang membangun `CdnDirectoryPage` (sorting, breadcrumb, escape href
   seperti sekarang) lalu memanggil skin terdaftar. `DefaultHomeSkin` + embedded logo/ikon.
4. **Pipeline `EmApp`** — not found CDN skin di `MapCdn` dan di `MapFeatureServiceGate` (khusus `/cdn`);
   home di `/`; fallback 404 + not found situs; `/favicon.ico`; negosiasi 4.4.
5. **`.skin`** — penulisan/penghapusan saat `Run()`; `CdnFileProvider` menyembunyikan `.skin` dari listing
   root dan menolak listing `/.skin` tetapi melayani berkasnya; pencadangan nama dan pengecualian di semua
   jalur `CdnStore` (list, count/info, upload, create folder, rename/move, delete, extract, managed folder,
   recycle, archive). Perbaiki komentar `ExclusionFilters` yang keliru.
6. **Test** di `tests/Em.Api.Core.Tests` (bagian 8).
7. **Dokumentasi** (bagian 9).

## 6. Hal yang tidak dikerjakan

- Modul SPA (`/spa`), web portal, membuka pipeline ke host, website penuh — topik terpisah.
- Mengganti favicon saat runtime lewat action API.
- Beberapa instance yang berbagi penyimpanan CDN.
- Perubahan pada WPF/MAUI (tidak ada UI client baru; CDN Manager hanya tidak menampilkan `.skin`).

## 7. Risiko yang perlu dijaga

- Escape: nama berkas berasal dari upload administrator. Href di model sudah di-escape engine; Razor
  meng-encode teks. `DefaultCdnSkin` tetap memakai `HtmlEncoder` seperti sekarang.
- Urutan middleware sekarang dijaga (forwarded headers → gerbang toggle → `/cdn` → `/v2` → endpoint publik →
  routing → `/api` → fallback). Home `/` dan `/favicon.ico` dipetakan tanpa mengganggu urutan itu.
- Penghapusan `.skin` hanya boleh menyentuh `<storage>/files/.skin` (validasi path absolut sebelum hapus
  rekursif).

## 8. Verifikasi (sekali di akhir, setelah kode plan host juga selesai)

Keputusan pengguna: **build + unit test engine**; tanpa smoke HTTP, tanpa uji visual.

- `dotnet build src/backend/Em.Api.slnx` sampai lolos (juga memastikan CS1591 terpenuhi).
- Tambah test dan jalankan **hanya test class yang ditambah/diubah plan ini** dengan filter:
  - negosiasi browser (GET + `Accept: text/html` vs lainnya, HEAD);
  - `DefaultCdnSkin` menghasilkan HTML yang setara dengan formatter lama (nama ter-escape, folder dulu);
  - render Razor lewat base class dengan komponen uji (boleh komponen C# `ComponentBase` dengan
    `BuildRenderTree`, tanpa berkas `.razor` di project test);
  - `.skin`: ditulis dari resource, ditimpa saat startup ulang, dihapus bila skin tanpa aset, gagal tulis →
    exception; tersembunyi dari listing publik dan `CdnStore.List`, listing `/.skin` not found, berkas
    terlayani; nama `.skin` di root ditolak di operasi management; archive root tanpa `.skin`;
  - home: `/` browser vs teks polos, path tak dikenal 404, `/favicon.ico` dengan/tanpa favicon;
  - `UseCdnSkin`/`UseHomeSkin` dipanggil dua kali → exception.
- Tidak menjalankan `dotnet test` seluruh solution, smoke HTTP, maupun uji visual. Catat di laporan bahwa
  pemeriksaan visual dilakukan pengguna.

## 9. Dokumentasi dan penutupan

- Guide baru `doc/engine/engine-skin.md` (bahasa Inggris): tujuan, `UseCdnSkin`/`UseHomeSkin`, kontrak,
  base class Razor (contoh `.razor` + kelas skin + `EmbeddedResource LogicalName`), `.skin` (siklus tulis,
  disembunyikan, nama dicadangkan), negosiasi browser, favicon, skin bawaan, perubahan perilaku fallback
  (200 → 404) sebagai upgrade note. Tautkan dari `doc/engine/README.md`.
- Perbarui `doc/engine/engine-cdn-storage.md` (skin, `.skin`, nama dicadangkan) dan
  `doc/engine/engine-public-endpoints.md` (`/` dan `/favicon.ico` milik engine, fallback 404).
- Tambahkan catatan singkat keputusan di `CLAUDE.md` root (pertahankan isi lama).
- Laporan eksekusi `doc/report/cdn-home-skin.md`: hasil build dan test terfilter, pilihan implementasi yang
  diambil sendiri, bahan release note (perubahan perilaku), verifikasi visual yang tertunda.
- Commit di `work-bench` (bagian 2), lalu pindahkan plan ini ke `plan/executed/` dan perbarui statusnya.
