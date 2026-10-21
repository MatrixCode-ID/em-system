# Plan — Managed CDN, Release Manager membangun launcher, update dari aplikasi, dan cab (untuk Codex)

Status: **selesai dieksekusi 2026-10-21 — build/tes console lulus; GUI dan smoke HTTP penuh tertunda sesuai laporan**

Laporan: [release-manager-cdn-launcher.md](../../doc/report/release-manager-cdn-launcher.md).
Dibuat: 2026-10-21
Pembaca: Codex. Berkas ini memuat semua keputusan yang dibutuhkan; jangan bertanya ke pengguna selama
eksekusi. Hal yang tidak tercakup diputuskan dengan pilihan yang paling konsisten dengan plan ini, lalu
dicatat di laporan eksekusi (bagian 11).

Sebelum mulai, baca versi terbaru di disk:

- `CLAUDE.md` di root repo (aturan bersama Claude Code dan Codex).
- `src/frontend/Launcher/CLAUDE.md` sebelum menyentuh launcher (aturan Rust, product neutral, gaya kode).
- `doc/release-format.md` (kontrak format rilis; diubah **lebih dulu** sebelum kodenya).
- Guide yang akan diubah: `doc/engine/engine-cdn-storage.md`, `doc/engine/engine-storage-settings.md`,
  `doc/engine/engine-release-manager.md`, `doc/engine/build.md`.

Plan ini menggabungkan tiga topik yang saling bergantung. Urutan seksinya wajib diikuti: **managed CDN →
Release Manager + launcher + update dari aplikasi → cab**.

---

## 1. Tujuan

1. **Managed CDN.** Folder CDN yang dimiliki satu aplikasi dan hanya bisa diubah Release Manager, susunan
   folder CDN baru (`files/`, `temp/`, `recycle/`, `settings/`), recycle bin, Clear Temporary Files, kuota per
   managed folder, dan pelepasan action CDN dari batas laju.
2. **Release Manager membangun launcher per produk.** Identitas produk (nama, publisher, `app_id`, ikon, logo)
   dibaca dari solution, disimpan di profile, dan dipakai untuk build aplikasi **dan** launcher sehingga
   keduanya konsisten. Launcher dibangun dari source lokal hanya bila inputnya berubah, dengan versi semi
   otomatis yang terpisah dari versi aplikasi. Profile disederhanakan menjadi satu JSON dengan kunci PEM.
3. **Update dari dalam aplikasi.** Aplikasi Em bertanya ke launcher (`--prepare`) secara berkala; status
   update tampil di status bar, dan restart hanya ditawarkan setelah update siap.
4. **Cab.** Launcher milik IT bisa mengunduh rilis ke berkas Microsoft Cabinet (`<app_id>.cab`) di sampingnya
   dan memasang dari situ, untuk dibawa atau dikirim ke PC tanpa akses CDN yang baik.

---

## 2. Aturan eksekusi

- **Kode semua seksi dulu, tes dan review di akhir** (aturan root `CLAUDE.md`). Per seksi cukup pastikan
  build yang murah lolos (`dotnet build` solution terkait, `cargo build` untuk launcher).
- **Commit per seksi** di branch `work-bench`, setelah build seksi itu lolos. Periksa branch aktif sebelum
  commit; bila bukan `work-bench`, jangan commit dan catat di laporan. **Tanpa push.** Pesan commit bahasa
  Inggris boleh (repo publik). Perbaikan hasil tes akhir di-commit terpisah. Commit terakhir memindahkan plan
  ini ke `plan/executed/` beserta laporannya.
- **Uji visual (render GUI) tidak dijalankan.** Layar/jendela baru (recycle bin, pratinjau logo, item status
  update, logo launcher) dicatat sebagai verifikasi visual tertunda. Uji console boleh langsung: `dotnet build`,
  `dotnet test`, `cargo test`, `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, smoke console
  di `..\.artefacts\em-system\scripts\<nama>-smoke\`.
- **Tindakan yang ditolak policy:** jangan diakali; siapkan skrip `.ps1` manual di
  `plan/release-manager-cdn-launcher-manual/` dan catat di laporan (aturan root `CLAUDE.md`).
- **Bahasa:** kode, komentar, XML comment, teks UI, log, dan pesan error berbahasa Inggris. Pengecualian
  launcher: help `///` pada item `pub` berbahasa Indonesia sesuai `src/frontend/Launcher/CLAUDE.md`. Plan,
  laporan, dan `CLAUDE.md` berbahasa Indonesia.
- **Product neutral:** tidak ada nama produk turunan di kode, dokumen, contoh, atau test. Contoh identitas di
  dokumen memakai `Contoso.Inventory` / `Contoso Inventory` / `inventory-installer.exe`.
- **Launcher:** setiap perubahan source launcher wajib diikuti `build-dist.ps1` dan commit
  `dist/launcher/launcher.exe` (aturan Launcher `CLAUDE.md`, tetap berlaku untuk produk bawaan engine).
- **Guide:** setiap fitur yang berubah memperbarui guide-nya di `doc/engine/` (bahasa Inggris) dan tautan di
  `doc/engine/README.md` bila ada guide baru.
- **Tanpa migrasi dan tanpa kompatibilitas mundur** kecuali disebut: belum ada client produksi yang memakai
  launcher, folder rilis lama, atau profile lama.

---

## 3. Keputusan final: managed CDN

### 3.1 Susunan folder CDN

```
data/cdn/                 ← path CDN di Settings (tetap ./data/cdn), sekarang folder induk
├── files/                ← root publik /cdn/... dan yang dikelola CDN Manager
├── temp/                 ← folder kerja engine (upload, archive, extract), tidak dilayani dan tidak terlihat
├── recycle/              ← item yang dihapus
└── settings/             ← data milik engine CDN (mis. managed.json), tidak dilayani dan tidak terlihat
```

- Keempat subfolder dibuat otomatis. Semuanya di volume yang sama sehingga pemindahan tetap rename atomik, dan
  satu volume container `/data/cdn` mencakup semuanya.
- **Tidak ada migrasi.** Isi lama di luar keempat subfolder tidak dilayani `/cdn`, tidak terlihat di CDN
  Manager, dan tidak dihapus engine; admin mengosongkannya manual. Catat ini di guide.
- **Ringkasan ukuran CDN** menghitung `files/` + `temp/` + `recycle/`.
- **Satu folder `data/cdn` dipakai satu instance API.** Status di memori proses (operasi aktif, handle terbuka)
  boleh diandalkan, tanpa lock lintas proses. Catat batasan ini di guide.

### 3.2 Berkas berawalan titik dan atribut

- `CdnStore` **tidak lagi menolak** nama berawalan titik. Berkas berawalan titik, hidden, dan system Windows
  diperlakukan seperti berkas biasa: dilayani publik, terlihat dan bisa dihapus di CDN Manager (kecuali di
  managed folder), ikut archive/extract dan hitungan ukuran. Tidak ada pilihan "Show hidden files".
- Semua isi CDN dianggap publik; kerahasiaan tanggung jawab pemakai (catat di guide, termasuk contoh
  `.htaccess` ikut bisa diunduh).

### 3.3 Berkas sementara dan Clear Temporary Files

- Berkas sementara engine (upload, archive, extract) ditulis di `temp/`, bukan di folder tujuan.
- Engine menyimpan **daftar operasi aktif di memori** (upload, archive, extract, serta berkas yang dipindah ke
  `temp/` dan masih dibuka), masing-masing mencatat berkasnya. Berkas di `temp/` yang tidak tercatat adalah
  sampah.
- Action **Clear Temporary Files** (claim `CDN Manager Access`) menghapus sampah saja, tidak pernah berkas yang
  sedang dipakai. UI menampilkan ukuran dan jumlah sampah sebelum menghapus. **Tidak ada pembersihan otomatis
  saat API start.**

### 3.4 Delete, timpa, dan recycle bin

- **Delete = pindah ke `recycle/`** lewat rename. Setiap item mendapat `recycle/<id>/` berisi berkas/folder
  aslinya (nama tidak diubah) dan `item.json` (path asal, waktu hapus, user penghapus, ukuran, jenis
  berkas/folder). Folder dihapus utuh sebagai satu item, termasuk berkas berawalan titik.
- Berkas yang sedang diunduh tetap bisa dihapus/dipindah: hilang dari UI dan bagi pengunduh baru, unduhan
  berjalan tetap membaca handle-nya. **Catatan implementasi:** berkas yang dilayani dibuka dengan
  `FileShare.Delete` (Windows) agar rename tidak gagal; di Linux/container selalu boleh.
- **Menimpa berkas** (upload ulang ke nama yang sama): berkas baru ditulis di `temp/` lalu menempati namanya
  lewat rename. Di luar managed folder, berkas lama yang ditimpa dari CDN Manager masuk `recycle/`.
- **Recycle bin di CDN Manager:** daftar isi, **Restore**, **Delete permanently** (item terpilih), dan
  **Empty Recycle Bin** (semua, action terpisah dari Clear Temporary Files). Semua claim `CDN Manager Access`.
  Empty dan Delete permanently melewati item yang handle-nya masih terbuka (item tetap ada). UI menampilkan
  ukuran dan jumlah item sebelum Empty.
- **Retensi manual saja.**
- **Restore ditolak bila bentrok:** bila path asal (atau sebagian isi folder yang dipulihkan) sudah terisi,
  tolak dengan pesan yang menyebut path bentrok. Tidak ada Replace/Keep both. Restore membuat ulang folder
  induk yang hilang.

### 3.5 Managed folder

- **Fitur engine CDN yang dipakai Release Manager.** Folder dijadikan managed saat runtime: **saat Release
  Manager memilih folder di CDN internal, folder itu dikonversi menjadi managed folder.** Tidak dideklarasikan
  di API builder.
- **Pemilik adalah aplikasi** (`app_id` di profile). Developer lain yang meng-import profile aplikasi yang sama
  tetap pemilik; profile aplikasi lain ditolak menulis.
- **Penanda disimpan di `data/cdn/settings/managed.json`.** Format entri:
  `{ "path": "inventory-release", "owner": { "kind": "release", "id": "Contoso.Inventory" }, "quotaBytes": null }`.
  `owner.kind` disiapkan agar pemakai lain kelak cukup menambah jenis baru; **versi ini hanya `release`**.
- **Hak tulis cukup claim Release Manager** (`Administrative Tools:Release Manager Access`), tanpa claim CDN
  Manager. Claim ini sekarang hanya dikenal sisi WPF (`EmApp.Statics.cs`); daftarkan juga di sisi API agar
  action baru bisa memeriksanya. Pemegang `CDN Manager Access` tidak bisa mengubah isi managed folder.
- **Isi managed folder tidak bisa diubah atau dihapus lewat UI CDN Manager**, apa pun nama berkasnya. Admin
  hanya melihat dan mengunduh.
- **Hanya folder kosong yang bisa dipilih** (yang sudah ada atau dibuat baru dari Release Manager). Kosong
  diperiksa dari isi fisik: tidak ada entri apa pun, termasuk subfolder kosong dan berkas berawalan
  titik/hidden/system.
- **Tidak bertingkat dua arah:** folder di dalam managed folder (dan semua turunannya) tidak bisa dijadikan
  managed folder, dan folder yang memuat managed folder juga tidak bisa.
- **Release (unmanage):** admin bisa melepas managed folder dari CDN Manager (konfirmasi ketik nama folder,
  claim `CDN Manager Access`); folder menjadi folder biasa. Release Manager juga bisa melepas folder miliknya
  sendiri.
- **Delete di dalam managed folder tidak masuk recycle bin** (mis. Sync menghapus berkas rilis lama). Berkas yang
  masih dibaca dipindah ke `temp/` dan dihapus setelah handle habis; sisanya tersapu Clear Temporary Files.
- **Tidak ada Restore ke managed folder:** item asal managed folder tidak bisa dipulihkan, dan Restore ke path di
  dalam managed folder (atau yang induknya kini managed folder) ditolak.
- **Folder rilis lama tidak dikonversi**; rilis berikutnya ditulis ke managed folder baru.

### 3.6 Kuota dan batas ukuran berkas

- **Kuota hanya untuk managed folder.** Folder biasa selalu tanpa batas, tanpa setelan kuota.
- Managed folder **baru selalu tanpa batas** (`quotaBytes: null`). Admin CDN (claim `CDN Manager Access`)
  mengatur kuota dari CDN Manager.
- **Yang dihitung hanya isi managed folder di `files/`**; berkas yang dipindah ke `temp/` tidak dihitung.
- **Diperiksa di awal Sync, sebelum ada berkas ditulis:** ukuran akhir = isi sekarang + berkas baru − berkas
  yang ditimpa/dihapus. Bila melebihi kuota, Sync ditolak seluruhnya dengan pesan berisi kuota dan ukuran
  hasil (mis. "Quota of folder `inventory-release` is 2 GB; the synced folder would be 2.3 GB."). Upload per
  berkas ke managed folder juga menolak penulisan yang melampaui kuota (pengaman).
- **Kuota boleh diturunkan di bawah isi sekarang:** isi tidak dihapus, Sync berikutnya ditolak sampai muat.
  CDN Manager menampilkan pemakaian terhadap kuota (mis. "1.8 GB / 2 GB") dan menandai yang melampaui.
- **Max file size** (setelan `MaxUploadMb` yang sudah ada di kartu Settings CDN Manager, bawaan 200 MB)
  **tidak berlaku untuk managed folder**; tetap berlaku untuk folder biasa.

### 3.7 Batas laju

- **Action CDN dilepas dari `ActionRateLimit`**, termasuk upload dan pengelolaan (CDN Manager dan Sync
  Release Manager), tidak hanya unduhan `/cdn`. Pemeriksaan batas laju sekarang berjalan sebelum route dicari
  (`EmApp.cs`), jadi pengecualian per action harus menyesuaikan urutan itu, atau action CDN dilayani di luar
  action dispatcher seperti `/cdn` dan `/v2`. Pilih yang paling sederhana dan catat di laporan.

---

## 4. Keputusan final: profile Release Manager

- **Profile = satu JSON** berisi seluruh setting milik produk: identitas, ikon, logo (Base64), kunci, alamat
  CDN, setting launcher, dan versi launcher. Export = salinan JSON, import = membaca JSON. Tidak ada lagi
  subfolder profile, `.pfx`/`.cer`, atau export ZIP.
- **Kunci PEM** (private key PKCS#8, tanpa password) di dalam JSON. Pemegang berkas profile bisa menandatangani
  rilis; konsekuensi ini diterima. **Sumber certificate store dan mode password (Separate/DPAPI/Plaintext)
  dihapus.**
- **Profile lama dihapus tanpa migrasi** (Release Manager tidak membaca format lama; dokumentasikan).
- **Satu kunci per produk di produksi, tanpa rotasi.**
- **Alamat CDN di profile bisa diubah** (alamat yang dilihat client bisa berbeda dari alamat koneksi, mis.
  reverse proxy). Kosong = `<host koneksi>/cdn/<folder>/` seperti sekarang. **Sync tetap menulis lewat koneksi
  aktif**; alamat profile dipakai untuk launcher (nilai default sumber) dan untuk Compare/Verify.
- **Setelan lokal per mesin developer disimpan di registry** (di bawah `BaseRegKey` aplikasi Release Manager),
  bukan di profile, karena tiap developer berbeda: **folder source launcher** dan **folder target launcher**
  (lihat 5.3). Profile tidak menyimpan path milik mesin selain field yang sudah ada.

---

## 5. Keputusan final: identitas, launcher, dan versi

### 5.1 Identitas produk

- Identitas tinggal di **csproj host**, bukan di kode:

  ```xml
  <Product>Contoso Inventory</Product>          <!-- app_name: hanya tampilan -->
  <Company>Contoso</Company>                    <!-- publisher -->
  <EmAppId>Contoso.Inventory</EmAppId>          <!-- app_id: kunci teknis -->
  <ApplicationIcon>Logo.ico</ApplicationIcon>   <!-- ikon -->
  <EmAppLogo>Logo.png</EmAppLogo>               <!-- logo -->
  ```

  `AssemblyName` host menjadi `app_exe`.
- **WPF `EmAppBuilder` membaca identitas dari atribut assembly host**: `ApplicationName` dari `Product`, `app_id`
  dari `EmAppId`, bawaan `BrandingInfo.LogoSource` dari `EmAppLogo` dan `IconSource` dari `ApplicationIcon`
  (berkasnya otomatis menjadi resource). **`builder.ApplicationName` dan branding di kode tetap ada sebagai
  override** agar host lama tetap jalan. Mekanisme properti → atribut: berkas `.targets` milik
  `Em.Ui.Wpf.Core` yang mengubah properti menjadi `AssemblyMetadata`/resource, dikemas `buildTransitive` di
  paket NuGet, dan di-`Import` eksplisit oleh host yang memakai `ProjectReference`. Dokumentasikan di
  `doc/engine/build.md`. MAUI tidak termasuk plan ini.
- **Release Manager membaca identitas saat solution di-load** (`dotnet msbuild <host> -getProperty:Product,
  Company,EmAppId,AssemblyName,ApplicationIcon,EmAppLogo`) **lalu menyimpannya di profile**; ikon dan logo
  disimpan Base64. Bila nilai solution berbeda dari profile, tampilkan perbedaannya dan developer memilih.
- **Saat Prepare, nilai profile dikirim ke `dotnet publish`** (`-p:Product=… -p:Company=… -p:EmAppId=…`, ikon dan
  logo dari profile), sehingga aplikasi dan launcher dalam satu rilis pasti beridentitas sama.
- **Validasi input:** `app_id` berpola `Publisher.Produk` (huruf/angka, minimal dua segmen dipisah titik);
  `app_id` bawaan engine `MatrixCode.EmSystem` ditolak di Release Manager. `EmAppLogo` harus PNG (selain itu
  ditolak dengan keterangan).
- **`app_id` adalah kunci teknis; `app_name` hanya tampilan** (shortcut, Apps & Features, UI):
  - Folder instalasi launcher: `%LocalAppData%\<app_id>`.
  - Registry launcher: `HKCU\Software\<app_id>\Launcher`; policy `HKLM\Software\Policies\<app_id>\Launcher`.
  - `BaseRegKey` aplikasi WPF: `HKCU\Software\<app_id>`; folder log/outbox aplikasi:
    `%LocalAppData%\<app_id>\Logs\...`. **Tanpa migrasi** dari `HKCU\<ApplicationName>`. Bila `app_id` tidak
    ada (host lama tanpa `EmAppId`), pakai perilaku lama (`ApplicationName`).
  - Ubah aturan "Registry mengikuti aplikasi host" di root `CLAUDE.md`: `BaseRegKey` mengikuti `app_id` host
    (jatuh ke `ApplicationName` bila `app_id` tidak ada).
- **Pengaman bentrok `app_id`:** saat install, launcher mencatat fingerprint public key yang tertanam di registry
  produknya; installer dengan `app_id` sama tetapi fingerprint berbeda menolak memasang dengan pesan jelas.
  Tidak ada ULID produk.
- Produk yang sama di dua lingkungan pada satu PC wajib memakai `app_id` berbeda (dokumentasikan, mis.
  `Contoso.Inventory` dan `Contoso.Inventory.Test`).

### 5.2 Dukungan aplikasi

- Release Manager bisa dipakai **aplikasi .NET apa pun** (Windows saja):
  - **Aplikasi Em (dukungan penuh):** semuanya, termasuk `app_id` untuk registry/folder data aplikasi,
    branding otomatis, dan update dari dalam aplikasi.
  - **Aplikasi .NET lain (dukungan dasar):** Prepare/Compare/Sync/Verify, install, update saat start lewat
    launcher, uninstall, shortcut, identitas di metadata exe, ikon dan logo di launcher.
- Saat load solution, host yang tidak mereferensikan `Em.Ui.Wpf.Core` (dibaca dari referensi hasil evaluasi
  msbuild) **tidak ditolak**; tampilkan keterangan bahwa integrasi update di dalam aplikasi tidak tersedia.

### 5.3 Build launcher dari Release Manager

- **Source launcher dari folder lokal yang dipilih pengguna** (setelan registry per mesin). Bila belum dipilih
  atau bukan source launcher (tidak ada `Cargo.toml` launcher), Prepare yang butuh build ulang dinonaktifkan
  dengan keterangan.
- **Identitas dan setting dikirim ke `build.rs` lewat berkas produk yang dibuat Release Manager** (perluasan
  `product.toml` yang ditunjuk `LAUNCHER_PRODUCT`): `app_name`, `publisher`, `app_exe`, `app_id`, ikon, logo,
  public key, nama exe launcher, versi launcher, perilaku update, nilai default sumber, versi source engine.
  `product.toml` bawaan dan `build-dist.ps1` **tetap ada** hanya untuk produk bawaan engine dan uji lokal.
- **Launcher hanya dibangun bila ada perubahan atau bila belum tersedia di folder target launcher.**
  - Perubahan = hash isi semua input: source launcher (`src\`, `build.rs`, `Cargo.toml`, `Cargo.lock`, tanpa
    `target\`), identitas, ikon, logo, public key, dan setting launcher (nama exe, perilaku update, nilai
    default sumber). Dihitung dari isi berkas, bukan commit git.
  - Penanda di samping exe di folder target, mis. `inventory-installer.build.json` (versi launcher, hash input,
    versi source engine dari `git describe` bila tersedia ditandai `dirty` bila ada perubahan belum di-commit,
    waktu build).
  - Folder target launcher: setelan registry per mesin; bawaan bila kosong
    `Documents\Em\ReleaseManager\Launchers\<app_id>\`. Boleh diarahkan ke share IT. Beri nama properti yang
    tidak tertukar dengan `ReleaseProfile.TargetFolder` yang sudah ada (mis. `LauncherOutputFolder`).
  - `cargo build --release` memakai folder `target` khusus per `app_id` agar inkremental, dan
    `--remap-path-prefix` seperti `build-dist.ps1`. Cari Visual Studio x64 seperti `build-dist.ps1`.
  - **Mesin tanpa Rust/MSVC:** Prepare tetap jalan bila launcher tidak perlu dibangun ulang; ditolak dengan
    keterangan hanya bila build ulang diperlukan.
- **Launcher masuk rilis sebagai berkas biasa di root publish folder:** setelah `dotnet publish`, exe dari folder
  target disalin ke root publish folder dengan nama exe dari profile. Satu exe berperan sebagai installer,
  updater, dan runner; tidak ada `updater.exe` terpisah. Hapus entri `Content` `dist/launcher/launcher.exe`
  dari `src/frontend/Em.Ui.Wpf/Em.Ui.Wpf.csproj` (host em-system dirilis lewat Release Manager seperti produk
  lain). Pindahkan juga identitas host itu dari `Program.cs` ke csproj (5.1).
- **Nama exe launcher diatur per produk di profile** (mis. `inventory-installer.exe`), dipakai untuk installer
  IT maupun launcher root di PC user. Nama tetap `launcher.exe` tidak berlaku lagi untuk build dari Release
  Manager; `launcher.old.exe` menjadi `<nama>.old.exe`.

### 5.4 Versi launcher

- **Terpisah dari versi aplikasi.** Versi aplikasi tetap dari host (`<Version>`/`-p:Version`). Versi launcher
  disimpan di profile dan **hanya naik bila hash input berubah**; satu versi launcher bisa dipakai banyak rilis
  aplikasi.
- **Semi otomatis meniru `ContainerVersion`** (`Em.Ui.Wpf.Core/Publish/ContainerVersion.cs`): developer memilih
  `MAJOR.MINOR.PATCH` dan channel (`prealpha`/`alpha`/`beta`/`rc`/`release`). Untuk prerelease, `N` = versi
  launcher tertinggi yang tercatat di `release.json` tujuan + 1 (mis. `1.0.1-beta.3`). Untuk release (`A.B.C`
  tanpa nomor), `A.B.C` yang sudah dipakai ditolak dan developer diminta menaikkan versi.
- Developer lain yang belum punya exe di folder targetnya membangun ulang, tetapi versinya tidak naik karena hash
  sama dengan yang tercatat di `release.json`.
- Version info exe `A.B.C.N` (release: `A.B.C.0`). Versi ditampilkan di jendela setup, maintenance, log, dan
  `--version`, bersama versi source engine (mis. `Contoso Inventory Launcher 1.0.1-beta.3 (engine
  v0.1.0-alpha.9)`).

### 5.5 Ikon dan logo launcher

- Ikon dan logo disimpan Base64 di profile, ditanam ke exe saat build. Logo PNG transparan ditampilkan di bagian
  atas jendela **setup, maintenance, dan progress**. Bila logo kosong, pakai ikon produk.
- **Area logo kotak tetap 240 × 48 px logis** (mengikuti DPI). Logo diskalakan seragam sampai menyentuh salah
  satu sisi, tidak pernah ditarik atau dipotong, dirata kiri dan tengah vertikal. Contoh: 800 × 100 → 240 × 30;
  256 × 256 → 48 × 48; 100 × 300 → 16 × 48; 2000 × 100 → 240 × 12. Bagian transparan mengikuti latar jendela;
  tepi transparan tidak dipangkas otomatis.
- **Release Manager menampilkan pratinjau** area logo pada skala 100% dan 200%, dan **memberi peringatan tanpa
  menolak** bila logo tampil lebih kecil dari 24 px di salah satu sisi, atau resolusi sumber terlalu kecil untuk
  200%.
- Jendela launcher memakai Win32 murni: PNG transparan butuh decoder (WIC lewat crate `windows`, atau crate PNG
  kecil). Pilih yang menambah ukuran exe paling kecil dan catat di laporan.

### 5.6 Kunci terpercaya dan `settings.ini`

- **Public key ditanam di launcher** (dari kunci profile), sehingga setup tidak memerlukan `.pem` terpisah.
  Sumber kunci lain yang sudah ada (import, HKLM, `.pem` di samping exe) tetap berfungsi.
- **`settings.ini` hanya berisi `Source`** (format INI Windows, komentar `;`):

  ```ini
  ; Release source of the application. Delete this file to restore the default.
  [Launcher]
  Source=\\fileserver\releases\inventory
  ```

- Saat exe berjalan sebagai **installer** (di luar folder instalasi): bila `settings.ini` ada di samping exe,
  baca dari sana; bila belum ada, tulis dari nilai default yang tertanam. Nilainya **ditulis ke registry user**
  PC target saat install (termasuk dijalankan ulang di PC yang sudah terpasang). `settings.ini` tidak disalin
  ke folder instalasi. Exe di folder baca saja tanpa `settings.ini` **gagal dengan error** yang menyebut bahwa
  berkas itu tidak bisa dibuat di sana.
- **Launcher terpasang (runner) hanya memakai registry:** policy HKLM `Source` bila ada, lalu HKCU.
- Release Manager tidak menyertakan `settings.ini` saat publish.

### 5.7 Release format

- `release.json` mendapat field baru (pembaca lama mengabaikan field tak dikenal, jadi tanpa naik versi format),
  ikut ditandatangani: nama exe launcher, versi launcher, dan hash input launcher. Contoh:

  ```json
  "launcher": { "fileName": "inventory-installer.exe", "version": "1.0.1-beta.3", "inputHash": "<sha256>" }
  ```

- **Ubah `doc/release-format.md` dan contoh uji di `doc/release-format-samples/` lebih dulu**, baru kode.
- Aplikasi yang dibuka langsung membaca nama exe launcher dari `release.json` di folder versinya untuk
  menyerahkan diri ke launcher (`LauncherIntegration`), menggantikan `..\launcher.exe`; tidak ada pencarian exe
  di folder induk. Sesuaikan juga shortcut, pin, `UninstallString`/`ModifyPath`, `LAUNCHER_PATH`.

### 5.8 Batasan versi pertama

- Tidak ada kemampuan lain yang ditanam saat build selain yang disebut di atas: folder instalasi tetap
  `%LocalAppData%\<app_id>`, shortcut Start menu seperti sekarang, batasan user tetap lewat policy HKLM, semua
  fitur selalu aktif.
- Paket installer untuk IT cukup exe launcher dari folder target (kunci tertanam, `settings.ini` dibuat exe).
- Pindah lokasi rilis massal untuk banyak PC tidak termasuk.

---

## 6. Keputusan final: update dari dalam aplikasi

- **Installer hanya untuk pemasangan pertama;** update berikutnya oleh launcher root.
- **Perilaku update saat start adalah setting profile**: *ask* (dialog Update now / Later seperti sekarang) atau
  *automatic* (pasang tanpa bertanya); **bawaan *automatic***. Tidak ada *mandatory* di versi ini.
- **Perintah launcher baru `--prepare [--quiet]`:** mengecek sumber dan, bila ada rilis baru, mengunduh dan
  memverifikasinya ke `.staging-<id>` tanpa mengaktifkan; berjalan diam tanpa jendela; resume yang sudah ada
  dipakai. Keluaran JSON satu baris di stdout:
  `{"state":"prepared","release":"a1b2c3d4e5f6","publishedAt":"2026-10-21T08:00:00Z"}`, `{"state":"none"}`, atau
  `{"state":"failed","message":"..."}`. Kegagalan cukup dicatat di log launcher.
- **Aplikasi Em bertanya ke launcher**, tidak mengecek sendiri di C#, agar sumber, policy, kunci, dan tanda tangan
  diperiksa satu kode.
- **Hanya aktif bila aplikasi dijalankan oleh launcher** (`LAUNCHER_PATH` terisi). Dari debugger/IDE atau folder
  publish: item status update disembunyikan dan tidak ada cek berkala.
- **Interval:** cek pertama 5 menit setelah aplikasi start, lalu setiap 60 menit dengan acak ±10 menit per PC;
  berhenti setelah update siap sampai restart; **Check for update** manual kapan saja tanpa acak. Nilai bisa
  diubah host lewat `EmAppBuilder`, bukan lewat profile.
- **Item status update di status bar, di sebelah kanan slot versi** (`StatusVersionItem`, slot sistem
  `EmStatusBar`). Keadaan:
  - *Up to date*: ikon centang redup; menu **Check for update**.
  - *Checking / downloading*: ikon berputar, tooltip progres; Check for update nonaktif.
  - *Update ready*: ikon aksen + teks "Restart to update"; klik meminta konfirmasi restart.
  - *Check failed*: ikon peringatan redup, tooltip berisi alasan; menu **Check for update**.
- **Notifikasi restart hanya muncul setelah update siap** (sudah diunduh dan diverifikasi). Konfirmasi restart
  menutup aplikasi secara normal (layar yang belum disimpan tetap bertanya) lalu menjalankan launcher root
  `--apply --pid <pid>`. Bila tidak di-restart, update dipasang saat aplikasi dibuka lagi.
- Aplikasi non-Em tidak mendapat fitur ini; mereka diperbarui saat dibuka lewat launcher.

---

## 7. Keputusan final: cab

- **Konteks:** exe launcher mandiri milik IT, bukan launcher terpasang di PC user. Release Manager tidak
  membuat cab.
- **Bentuk: berkas Microsoft Cabinet `<app_id>.cab`** di samping exe launcher, karena cab bisa dibawa atau
  dikirim (USB, ke user WFH lewat remote desktop):

  ```
  \\fileserver\installer\inventory\
  ├── inventory-installer.exe
  ├── settings.ini
  └── Contoso.Inventory.cab
  ```

  Isinya folder rilis (`release.json`, `release.json.sig`, `binaries\...`), sehingga tetap diverifikasi dengan
  kunci tertanam. Dibuat/dibaca lewat API Cabinet Windows (`cabinet.dll`, FCI/FDI, crate `windows`
  `Win32::Storage::Cabinets`), **kompresi LZX**. **Tidak dienkripsi.**
- **Rilis di atas ±2 GB:** Download to cab gagal dengan pesan jelas; tidak ada pemecahan ke beberapa `.cab`.
- **Download to cab:** (1) bandingkan byte `release.json` CDN dengan yang di `.cab`, sama berarti selesai;
  (2) berkas yang tidak berubah diambil dari `.cab` lama, hanya yang berubah diunduh; (3) semua diverifikasi
  (SHA-256 dan tanda tangan) lalu dibungkus ke `.cab` baru di berkas sementara; (4) berkas sementara menggantikan
  `.cab` lama lewat rename. Gagal di langkah mana pun, `.cab` lama utuh.
- **Alur UI:** di menu install installer ada tombol **Download to cab**. Bila cab sama dengan CDN, install
  **melalui cab**. Cab berbeda dengan CDN: IT diberi pilihan memperbarui cab dulu. CDN tidak terjangkau: install
  dari cab.
- **Install dari cab:** ekstrak langsung ke `.staging-<id>\` di folder instalasi, lalu alur biasa; gagal
  ekstraksi/verifikasi berarti staging dibuang dan PC tidak berubah.
- **Sumber update setelah install dari cab tetap alamat CDN** (nilai default / `settings.ini`, ditulis ke
  registry). **PC user tidak memakai cab**; repair dan update langsung ke CDN.
- **CLI:**
  - `--download-cab [--source S] [--quiet]`. Tanpa `--source`, alamat dari sumber yang sama dengan install.
    Tidak menulis registry dan tidak memasang apa pun. Tanpa `--quiet` tampil jendela progress.
  - Kode keluar: cab sudah terbaru dan cab berhasil diperbarui sama-sama `0`, dibedakan lewat teks
    ("Cab is up to date (release …)." / "Cab updated to release … (published …), 18.4 MB downloaded."). Gagal:
    `2` sumber gagal, `3` IO gagal, `4` dibatalkan; `.cab` lama tidak tersentuh.
  - `--install --quiet` memakai cab otomatis: cab sama dengan CDN atau CDN tidak terjangkau berarti dari cab;
    **cab berbeda dengan CDN berarti install dari CDN** tanpa mengubah cab. Tidak ada opsi `--from-cab`.

---

## 8. Seksi kerja

Kerjakan berurutan. Setiap seksi diakhiri build murah dan commit (bagian 2).

### Seksi A — Kontrak dan dokumen lebih dulu

1. `doc/release-format.md`: field `launcher` (5.7), nama exe launcher tidak lagi tetap, catatan cab (format
   isinya sama dengan folder rilis). Tambah/ubah contoh di `doc/release-format-samples/` beserta hasil yang
   diharapkan.
2. `src/frontend/Launcher/CLAUDE.md`: perbarui tabel identitas (`app_id` sebagai kunci teknis, field baru),
   layout instalasi (`%LocalAppData%\<app_id>`, nama exe per produk), registry (`HKCU\Software\<app_id>`,
   fingerprint, kunci tertanam), CLI (`--prepare`, `--download-cab`, `--version`), `settings.ini`, dan peran
   `build-dist.ps1` (hanya produk bawaan dan uji lokal).
3. Root `CLAUDE.md`: aturan "Registry mengikuti aplikasi host" (5.1).

### Seksi B — CDN backend (`Em.Api.Core`, `Em.Libs`)

Berkas utama: `Em.Api.Core/Api/Core/CdnStore.cs`, `CdnServices.cs`, `CdnDirectoryFormatter.cs`, `EmApp.cs`
(peta `/cdn`, batas laju), `Em.Libs/Api.Core.Models/ICdnServices.cs`.

1. Susunan `files/`/`temp/`/`recycle/`/`settings/` (3.1), ukuran gabungan.
2. Hapus penolakan dan penyembunyian berawalan titik/hidden/system (3.2).
3. Berkas sementara ke `temp/`, daftar operasi aktif, action Clear Temporary Files dengan info ukuran/jumlah
   (3.3).
4. Delete ke `recycle/` dengan `item.json`, timpa lewat `temp/` + rename, `FileShare.Delete` pada berkas yang
   dilayani, action daftar/Restore/Delete permanently/Empty dengan info ukuran/jumlah (3.4).
5. Managed folder: `settings/managed.json`, konversi folder kosong, larangan bertingkat, penguncian terhadap
   action CDN Manager, Release oleh admin dan oleh Release Manager, delete tanpa recycle bin, larangan Restore
   (3.5). Action tulis untuk Release Manager memakai claim `Administrative Tools:Release Manager Access` yang
   didaftarkan di sisi API, dan memeriksa pemilik (`app_id`).
6. Kuota: simpan/ubah (claim CDN Manager), pemeriksaan awal Sync dan per upload, pengecualian Max file size
   (3.6).
7. Lepas action CDN dari `ActionRateLimit` (3.7).
8. Nama action mengikuti konvensi `GetMeta_`/`PostMeta_`/`PostGetMeta_`; GET hanya parameter sederhana.

### Seksi C — CDN Manager WPF (`Em.Ui.Wpf.Core`)

Berkas utama: `Navigations/CdnManager.xaml(.cs)`, `Api.Core/CdnService.cs`.

1. Recycle bin (daftar, Restore, Delete permanently, Empty dengan ringkasan ukuran/jumlah).
2. Clear Temporary Files dengan ringkasan ukuran/jumlah sampah.
3. Tanda managed folder (pemilik `app_id`), isi baca saja (lihat/unduh), Release dengan konfirmasi ketik nama.
4. Kuota per managed folder: ubah, tampilkan pemakaian "x / y", tanda melampaui.
5. Ikuti aturan UI root `CLAUDE.md` (`EmWindow`, help tooltip field, `NumericBox`/`NumericInput` untuk kuota,
   tema enabled/disabled/busy, acuan UserManager).

### Seksi D — Release Manager (`Em.Ui.Wpf.Core/Release`, `Navigations/ReleaseManager.xaml`)

1. Profile satu JSON, kunci PEM, hapus store/password, abaikan profile lama (4).
2. Alamat CDN di profile; Compare/Verify memakai alamat itu (4).
3. Pemilihan folder CDN menjadikannya managed folder; buat folder baru; tolak folder berisi/bertingkat;
   lepas folder milik sendiri; tampilkan pesan kuota saat Sync ditolak (3.5, 3.6). `CdnReleaseTarget` memakai
   action Release Manager, bukan action CDN Manager.
4. Load solution: baca identitas via `dotnet msbuild -getProperty`, simpan ke profile, dialog perbedaan,
   deteksi host Em vs non-Em (5.1, 5.2).
5. Ikon/logo Base64, pratinjau logo 100%/200%, peringatan (5.5); validasi `app_id` dan PNG.
6. Setting launcher di profile: nama exe, perilaku update, versi (A.B.C + channel) (5.3, 5.4, 6).
7. Setelan registry per mesin: folder source launcher, folder target launcher (bawaan
   `Documents\Em\ReleaseManager\Launchers\<app_id>\`).
8. Prepare: `dotnet publish` dengan `-p:` identitas; hitung hash input; build launcher hanya bila perlu
   (berkas produk untuk `LAUNCHER_PRODUCT`, `target` per `app_id`, vcvars seperti `build-dist.ps1`); tulis
   penanda `.build.json`; salin exe ke root publish folder; nonaktifkan dengan keterangan bila butuh build
   tetapi toolchain/source tidak ada (5.3).
9. Sync: tulis field `launcher` di `release.json`; versi `N` dari `release.json` tujuan; tolak versi release
   yang sudah dipakai (5.4, 5.7).

### Seksi E — Aplikasi WPF (`Em.Ui.Wpf.Core`, host `Em.Ui.Wpf`)

1. Berkas `.targets` properti → atribut/resource (`buildTransitive` + `Import` untuk `ProjectReference`); WPF
   `EmAppBuilder` membaca identitas, override di kode tetap ada (5.1).
2. `BaseRegKey` dan folder log/outbox memakai `app_id` (fallback `ApplicationName`) (5.1).
3. `LauncherIntegration`: nama exe dari `release.json`; `--from-app` ke exe itu (5.7).
4. Item status update di status bar kanan slot versi, cek berkala via `--prepare`, Check for update manual,
   restart via `--apply --pid`, hanya bila `LAUNCHER_PATH` terisi; interval bisa diubah lewat `EmAppBuilder`
   (6).
5. Host `src/frontend/Em.Ui.Wpf`: identitas pindah ke csproj (`Product`, `Company`, `EmAppId`,
   `ApplicationIcon`, `EmAppLogo`), hapus `Content` `dist/launcher/launcher.exe`, `Program.cs` tidak lagi
   mengisi `ApplicationName`.

### Seksi F — Launcher Rust (`src/frontend/Launcher`)

1. `build.rs`/`product`: field baru (5.3); `app_id` sebagai kunci folder/registry; nama exe per produk dan
   `<nama>.old.exe`; version info `A.B.C.N`.
2. Kunci tertanam, fingerprint di registry produk, tolak `app_id` sama dengan fingerprint berbeda (5.1, 5.6).
3. `settings.ini` untuk installer dan registry-only untuk runner (5.6).
4. Logo di jendela setup/maintenance/progress dengan aturan 240 × 48 (5.5); versi di jendela, log, dan
   `--version` (5.4).
5. Perilaku update *ask*/*automatic* dari berkas produk (6).
6. `--prepare` dengan keluaran JSON (6).
7. Test integrasi di `tests/` untuk perintah baru (dengan `quiet = true`, registry/folder/lock sementara).
8. `cargo fmt`, `cargo clippy`, `build-dist.ps1`, commit `dist/launcher/launcher.exe`.

### Seksi G — Cab (launcher)

1. Penulis/pembaca `.cab` lewat FCI/FDI, LZX, batas ±2 GB (7).
2. `--download-cab` dengan reuse berkas lama, verifikasi, staging, rename atomik, kode keluar dan teks (7).
3. Tombol Download to cab di menu install; pilihan perbarui cab bila berbeda; install dari cab bila sama atau
   CDN tidak terjangkau; ekstrak ke `.staging-<id>` (7).
4. Aturan `--install --quiet` terhadap cab (7).
5. Test integrasi; `cargo fmt`, `cargo clippy`, `build-dist.ps1`, commit `dist/launcher/launcher.exe`.

### Seksi H — Guide

Perbarui `doc/engine/engine-cdn-storage.md` (susunan folder, berkas berawalan titik, temp, recycle bin, managed
folder, kuota, batas laju, satu instance, tanpa migrasi), `engine-storage-settings.md` (arti path CDN sebagai
folder induk, Max file size tidak berlaku untuk managed folder), `engine-release-manager.md` (profile JSON/PEM,
identitas dari solution, build launcher, versi, logo, dukungan Em vs non-Em, setelan per mesin, managed folder,
kuota), dan `doc/engine/build.md` (properti identitas csproj, `.targets`). Tambah bagian update dari dalam
aplikasi dan cab di guide yang paling sesuai, atau guide baru `doc/engine/engine-launcher.md` yang ditautkan dari
`doc/engine/README.md`.

---

## 9. Pengujian (setelah semua seksi selesai)

1. `dotnet build src/backend/Em.Api.slnx` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx`.
2. `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx`. Tambah test untuk:
   `CdnStore` (susunan folder, berkas berawalan titik, temp/Clear, recycle/Restore/bentrok, managed folder
   kosong/bertingkat/terkunci, kuota awal Sync, Max file size tidak berlaku), hash input launcher, versi semi
   otomatis, pembacaan identitas, `BaseRegKey` dengan `app_id` (pakai factory base key terisolasi).
3. Launcher: `cargo test`, `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`; test terhadap
   vektor `doc/release-format-samples/` yang baru.
4. Smoke console (opsional, di `..\.artefacts\em-system\scripts\release-cdn-smoke\`): Prepare → Sync ke managed
   folder pada API lokal, `--prepare`, `--download-cab` lalu `--install --quiet` dari cab ke folder sementara.
   Bila butuh login atau server yang tidak tersedia, catat sebagai tertunda.
5. **Uji visual tidak dijalankan**; daftar layar/jendela yang tertunda di laporan.

---

## 10. Selesai bila

- Semua seksi A–H selesai dan di-commit di `work-bench`; build dan test pada bagian 9 lolos atau kegagalannya
  dijelaskan.
- `dist/launcher/launcher.exe` dibangun ulang dari source akhir.
- Guide diperbarui dan cocok dengan kode.
- Plan dipindah ke `plan/executed/` dan laporan ditulis.

## 11. Laporan eksekusi

Tulis `doc/report/release-manager-cdn-launcher.md` (bahasa Indonesia): ringkasan per seksi, commit yang dibuat,
hasil build/test (dengan angka), keputusan implementasi yang diambil sendiri (mis. cara pengecualian batas laju,
decoder PNG, mekanisme `.targets`), verifikasi tertunda (visual, smoke yang butuh server), tindakan manual yang
terblokir policy beserta skripnya, serta penjelasan singkat konsep Rust yang dipakai (aturan Launcher
`CLAUDE.md`). Jangan menyatakan verifikasi tertunda sebagai lulus.
