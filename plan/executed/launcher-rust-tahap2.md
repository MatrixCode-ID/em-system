# Plan — Launcher (Rust), tahap 2 update management

Status: **sudah dieksekusi** — 6 sesi selesai (2026-09-28)
Dibuat: 2026-09-28, dari laporan [doc/report/diskusi-update-management.md](../../doc/report/diskusi-update-management.md)
(tahap 2 di bagian 8) dan diskusi lanjutan di sesi yang sama
Baseline: branch `launcher-tahap2` (dari `main` di `5e6cd8a`), commit pertamanya berisi plan ini dan
kerangka project `src/frontend/Launcher` (`Cargo.toml` dengan package `launcher`, `rust-toolchain.toml`,
`src/main.rs`, `CLAUDE.md`, `.gitignore`), `.editorconfig` (`[*.rs]`), dan satu baris di root
`CLAUDE.md`. Kerangka itu semula bernama `Em.Launcher`/`em-launcher`, lalu diganti karena
keputusan 18.

---

## Kenapa ada berkas ini

Tahap 1 sudah menghasilkan Release Manager dan format rilis yang dikunci di
[doc/release-format.md](../../doc/release-format.md): folder rilis berisi `binaries/`, `release.json`
(path + ukuran + SHA-256), dan `release.json.sig` (ECDSA P-256, P1363). Contoh ujinya ada di
`doc/release-format-samples/`.

Plan ini membangun **tahap 2**, yaitu `launcher.exe`. Satu exe ini memegang lima peran:

- **installer**: form setup, unduh, verifikasi, pasang, shortcut, dan registrasi di Apps & Features;
- **launcher**: dijalankan duluan dari shortcut atau pin, cek update, jalankan app, lalu keluar;
- **updater**: menyiapkan versi baru di folder terpisah, lalu memindahkan pointer;
- **maintenance**: Repair, ganti sumber atau key, dan Uninstall (dibuka lewat **Modify** di Settings);
- **CLI untuk IT**: instalasi senyap, import key, dan kebijakan HKLM.

Plan ini juga mencakup sisi WPF: `launcher.exe` ikut di setiap rilis, pin taskbar mengarah ke launcher
(AUMID), dan app yang dijalankan langsung menyerahkan diri ke launcher.

Launcher ditulis dalam **Rust**. Pilihan ini disengaja karena user ingin belajar bahasa baru lewat
project ini. C# NativeAOT sudah dipertimbangkan dan tidak dipilih, jadi jangan diungkit lagi.

Temuan saat menyusun plan:

- Root registry app adalah `HKCU\{ApplicationName}` (`EmApp.BaseRegKey`), **bukan** di bawah
  `HKCU\Software`. `ApplicationName` host saat ini adalah `Em Sample Application`
  (`src/frontend/Em.Ui.Wpf/Program.cs`).
- Ikon app `src/shared/Em.Ui.Wpf.Core/Assets/Icons/Logo.ico` sudah berisi 16/32/48/64/128/256 px,
  32-bit, jadi cukup untuk launcher.
- Private key contoh uji di `doc/release-format-samples/` sudah dibuang. Test yang butuh rilis bertanda
  tangan baru harus membuat key sementara sendiri.
- Di mesin ini `cargo` memilih VS 18 yang hanya punya `link.exe` tanpa library x64
  (`LNK1104: msvcrt.lib`). Build harus dijalankan dengan lingkungan VS 2022 (`vcvars64.bat`), dan
  `vswhere -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64` hanya mengembalikan VS 2022.
- Windows memblokir program yang mem-pin dirinya sendiri ke taskbar (API resminya hanya untuk MSIX).
  Tombol **Repair** bawaan Windows hanya ada untuk MSI.

## Keputusan final

Semua keputusan di bawah sudah diambil bersama user. Saat eksekusi tidak ada lagi pertanyaan ke user.
Hal yang tidak tercakup diputuskan menurut pilihan yang paling konsisten dengan tabel ini, lalu dicatat
di laporan eksekusi.

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Bahasa dan lokasi | **Rust**, di `src/frontend/Launcher` (kerangka sudah ada), di luar kedua `.slnx`, di-build dengan `cargo`. Kontrak format ditulis ulang di Rust (sisi pembaca) dan diuji dengan vektor `doc/release-format-samples/`. |
| 2 | GUI | Win32 native lewat crate **`winsafe`**. Bukan WinUI dan bukan Avalonia. Bagian yang tidak tersedia di `winsafe` memakai crate `windows`. |
| 3 | Distribusi launcher | Di-build terpisah. Exe hasil build release **di-commit** di `dist/launcher/launcher.exe` di **root repo**, lalu masuk `Em.Ui.Wpf.csproj` sebagai **`Content`** (bukan `EmbeddedResource`), sehingga ikut `binaries/`, `release.json`, dan tanda tangan. |
| 4 | Urutan jalan | **Launcher selalu duluan.** Shortcut dan pin menunjuk ke launcher. Launcher menjalankan app lalu keluar, tidak tinggal di memori. |
| 5 | Cek update saat startup | **Cek, lalu tanya user**: "Update now / Later". *Later* langsung menjalankan versi terpasang. Tawaran muncul lagi **setiap kali** launcher dijalankan selama update masih ada. Offline, timeout, atau rilis tidak sah: jalankan versi terpasang tanpa mengganggu user. |
| 6 | Pin taskbar | Pin dari app yang berjalan diarahkan ke launcher lewat **AppUserModelID** (shortcut Start menu dengan AUMID yang sama, ditambah `RelaunchCommand` di jendela). App yang **dijalankan langsung** dari folder instalasi menjalankan launcher lalu keluar. |
| 7 | Form setup | Isian: **sumber rilis** (URL CDN atau folder lokal/jaringan), **folder instalasi**, dan **public key**. Checkbox: **Start Menu item**, **Desktop shortcut**, **Run after install** (ketiganya default dicentang). Taskbar pin **tidak** jadi checkbox (Windows memblokirnya). Penggantinya, halaman akhir menampilkan petunjuk cara pin. |
| 8 | Default folder instalasi | **`%LocalAppData%\<ApplicationName>`**, bisa diganti user. |
| 9 | Public key | **Tanpa key bawaan** di exe. Sumbernya: file `*.pem` di sebelah exe saat setup (mengisi form otomatis), tombol Import di form/maintenance, `--import`, dan kebijakan HKLM. Semua disimpan di registry. File berisi private key **ditolak** dengan peringatan. |
| 10 | Konfigurasi launcher | Registry **satu root dengan app**: `HKCU\<ApplicationName>\Launcher`. |
| 11 | Integrasi Windows | Start menu item, registrasi di **Apps & Features** (`HKCU\...\Uninstall\<AppId>`), dan **Modify** yang membuka jendela maintenance (**Repair / Change source / Uninstall**), dengan `NoRepair = 1`. |
| 12 | Data user saat uninstall | **Selalu dihapus**: seluruh `HKCU\<ApplicationName>`, termasuk pengaturan app, koneksi, dan konfigurasi launcher. |
| 13 | Fitur IT | **Instalasi senyap** (`--install ... --quiet`) dan **kebijakan HKLM** (`HKLM\Software\Policies\<ApplicationName>\Launcher`). |
| 14 | Ikon | Sama dengan app. Path ikon adalah data produk (`product.toml`). Untuk produk contoh, `Logo.ico` direferensikan (bukan disalin) dan ditanam lewat `.rc` + `embed-resource`, bersama application manifest. |
| 15 | Gaya kode | **Sama dengan aplikasi utama** sejauh Rust mengizinkan (lihat keputusan turunan "Gaya kode"). |
| 16 | Cara eksekusi | **Dibagi 6 sesi** (lihat "Pembagian sesi"), supaya tidak kehabisan token di tengah jalan. Setiap sesi berakhir dengan build bersih dan menambah **Catatan eksekusi** di plan ini. Laporan eksekusi memuat bagian **"Konsep Rust"** yang memetakan konsep ke file tempat konsep itu dipakai, yang dicicil per sesi. |
| 17 | Commit | **Commit di akhir setiap sesi**, di branch `launcher-tahap2`, setelah verifikasi sesi itu lolos. Pesan commit dalam Bahasa Indonesia. Tanpa push. |
| 18 | Netral produk | Launcher bisa dipakai aplikasi lain selain produk contoh. Karena itu, **nama project, folder, file, modul, tipe, identifier, dan environment variable tidak boleh membawa nama produk**. Folder project: **`src/frontend/Launcher`**, package `launcher`. Semua yang spesifik produk adalah **data** di `product.toml` (lihat keputusan turunan "Identitas produk"). |

### Keputusan turunan (diambil saat menyusun plan)

**Gaya kode.** Tambahkan `rustfmt.toml` dengan `tab_spaces = 3`, `max_width = 120`, dan
`newline_style = "Windows"`. Ubah `[*.rs]` di `.editorconfig` menjadi `indent_size = 3`. Aturan lain
mengikuti kode C# repo:

- Help (`///`) pada item `pub` ditulis dalam **Bahasa Indonesia**, menjelaskan apa dan kenapa.
- Komentar inline, identifier, pesan error, dan teks log/UI dalam **bahasa Inggris**.
- Komentar inline menjelaskan alasan, bukan mengulang kode.

Dua hal tetap mengikuti Rust:

- **Penamaan**: `snake_case` untuk fungsi, variabel, dan modul (dipaksa lint compiler), `PascalCase`
  untuk tipe (sama dengan C#), dan `SCREAMING_SNAKE_CASE` untuk konstanta.
- **`else` di baris baru** (`csharp_new_line_before_else`) tidak bisa diatur di `rustfmt` stable, jadi
  memakai bawaan `rustfmt`.

**Peletakan tipe** mengikuti pola C# repo (contohnya `Em.Ui.Wpf.Core/Release/`):

- **Satu tipe utama per file**, dan nama file adalah nama tipe itu dalam `snake_case`
  (`ReleaseManifest` → `release_manifest.rs`). Tipe kecil yang hanya melayani tipe utama (enum status,
  struct hasil, tipe error) boleh ikut di file yang sama, seperti record/enum pendamping di file C#.
- **Folder = kelompok fungsi**, seperti folder `Release/` dan `Release/Format/`. Setiap folder punya
  `mod.rs` yang hanya berisi deklarasi `mod` dan `pub use`, tanpa logika.
- **Nama tipe dan file bagian format disamakan dengan pasangannya di C#**, supaya mudah dicocokkan:
  `ReleaseLayout`, `ReleaseManifest`, `ReleaseFile`, `ReleaseSignatureFile`, `ReleaseSignature`,
  `ReleaseHash`, `ReleaseFormatError` (pasangan `ReleaseFormatException`).
- **Urutan di dalam file**: `use`, konstanta modul, definisi `struct`/`enum` (field-nya setara
  properti), lalu `impl` yang dibagi dengan penanda region `// region: <Nama>` ... `// endregion`
  (didukung folding rust-analyzer), memakai nama region yang sama dengan C#: **Statics** (konstruktor
  `new` dan fungsi terkait), **Properties** (getter), **Methods**. Implementasi trait
  (`impl Display for ...`) diletakkan setelah `impl` utama. Test unit ada di `#[cfg(test)] mod tests`
  di paling bawah file.
- Region hanya dipakai kalau file-nya cukup panjang untuk membutuhkannya, sama seperti di C#.

`cargo fmt --check` dan `cargo clippy -D warnings` wajib bersih.

**Identitas produk** (keputusan 18). Semua data produk ada di satu file `product.toml` di root project
launcher. `build.rs` membacanya saat build, menghasilkan konstanta Rust (`include!` dari `OUT_DIR`) dan
file `.rc` (ikon + manifest). Untuk mem-build launcher produk lain, cukup arahkan environment variable
`LAUNCHER_PRODUCT` ke file toml lain, tanpa mengubah kode. Isi untuk produk contoh:

```toml
app_name  = "Em Sample Application"   # harus sama dengan builder.ApplicationName host
publisher = "Acme Corp"
app_exe   = "Em.Ui.Wpf.exe"
app_id    = "Em.SampleApplication"    # AUMID dan nama key Uninstall
icon      = "../../shared/Em.Ui.Wpf.Core/Assets/Icons/Logo.ico"   # relatif terhadap product.toml
```

Di kode, datanya tersedia sebagai modul `product` (`product::APP_NAME`, `product::APP_ID`, dan
seterusnya). `launcher.exe` sendiri adalah nama tetap, bukan data produk. Nilai `app_id` dikirim ke app
lewat environment variable, sehingga app tidak perlu menyimpan salinannya (lihat "Kontrak launcher →
app"). `build.rs` menolak `product.toml` yang field-nya kosong atau ikonnya tidak ada.

**Crate.** Daftar di bawah dipilih supaya sesedikit mungkin. Versi terbaru saat eksekusi dicatat di
laporan.

| Crate | Untuk |
| --- | --- |
| `winsafe` | GUI, dialog, TaskDialog, registry, proses, dan COM shell |
| `windows` | Hanya untuk bagian yang tidak ada di `winsafe`, misalnya `IPropertyStore` + `PKEY_AppUserModel_ID` di shortcut |
| `ureq` (TLS `native-tls`/SChannel) | HTTP. SChannel memakai certificate store Windows, jadi CA internal perusahaan dipercaya |
| `serde`, `serde_json` | JSON |
| `sha2` | SHA-256 |
| `p256` (`ecdsa`, `pkcs8`, `pem`) | Verifikasi tanda tangan dan parse PEM SubjectPublicKeyInfo |
| `base64` | Decode `signature` |
| `lexopt` | Parser argumen CLI kecil |
| `embed-resource` (build) | Ikon dan manifest |
| `tempfile` (dev) | Folder sementara untuk test |

`p256` dengan fitur signing dan `rand_core` hanya dipakai di dev-dependencies, untuk membuat rilis uji.

**Layout instalasi:**

```
<install>\
├── launcher.exe          launcher root: target shortcut, pin, dan entri Uninstall
├── current.json          { "version": "<id>", "publishedAtUtc": "..." }, ditulis atomik
├── launcher.log          log launcher, diputar di 1 MB (satu cadangan .1)
├── app-<id>\             isi binaries/ + release.json + release.json.sig
└── .staging-<id>\        versi yang sedang disiapkan (dipertahankan untuk resume)
```

- `<id>` adalah **12 karakter hex pertama SHA-256 byte `release.json`**. Rilis yang sama selalu
  menghasilkan folder yang sama, dan tidak butuh nomor versi.
- `release.json` dan `.sig` ikut disalin ke `app-<id>\`. App membaca keberadaan `release.json` sebagai
  tanda "terpasang lewat launcher" (*managed*).
- **`DisplayVersion`** di Apps & Features diambil dari `publishedAtUtc`, dengan format
  `yyyy.M.d.HHmm`.

**Sumber rilis.** User mengisi alamat *folder rilis*: URL `http(s)://.../cdn/wpf-release` atau path
folder (lokal/UNC). Dua implementasi trait `ReleaseSource`:

- `HttpSource`: `GET` dengan Range untuk resume. Kalau server menjawab 200 (bukan 206), unduhan
  dimulai ulang dari nol.
- `FolderSource`: baca langsung dari path.

Timeout pengecekan saat startup: sekitar **3 detik** untuk koneksi dan sekitar **5 detik** total per
file kecil. Unduhan file besar memakai timeout baca, bukan batas total. Proxy sistem **tidak**
didukung di tahap ini.

**Urutan update** (sama untuk install, update, dan repair):

1. Ambil `.sig` dan `release.json`, lalu verifikasi dengan urutan wajib `release-format.md` bagian 6.
   Gagal: berhenti. Saat startup, kegagalan ini diam-diam dan hanya tercatat di log.
2. Kalau `<id>` sama dengan versi aktif, tidak ada update (repair tetap berlanjut ke pemeriksaan file).
3. Isi `.staging-<id>\` untuk setiap file di manifest:
   - file dengan path, ukuran, dan SHA-256 yang sama di versi aktif **disalin dari lokal**;
   - selain itu **diunduh** ke `<file>.part` (dengan resume), lalu di-rename.
   Setiap file, baik hasil salinan maupun unduhan, **dicocokkan ukuran lalu SHA-256-nya**. Ada yang
   tidak cocok: batalkan dengan pesan "release changed on the server, try again later" (bagian 7 spec).
4. Tulis `release.json` dan `.sig` ke staging, lalu rename `.staging-<id>` menjadi `app-<id>`.
5. Tulis `current.json` secara atomik (file sementara, lalu rename).
6. Kalau `app-<id>\launcher.exe` berbeda dari launcher root: rename root menjadi `launcher.old.exe`,
   lalu salin yang baru ke root. `launcher.old.exe` dihapus di run berikutnya.
7. Hapus folder `app-*` dan `.staging-*` lain. Folder yang sedang dipakai (app lama masih berjalan)
   dilewati, lalu dicoba lagi di run berikutnya.

Update boleh berjalan walaupun app versi lama masih terbuka, karena versi baru ada di folder lain.
**Repair** memakai urutan yang sama terhadap versi aktif, tapi setiap file lokal selalu di-hash ulang,
dan yang rusak atau hilang diunduh ulang langsung ke `app-<id>\` (lewat `.part` lalu rename). Setelah
itu shortcut dan entri Uninstall juga ditulis ulang.

**Alur saat dijalankan tanpa argumen:**

1. Belum ada konfigurasi (`HKCU\<App>\Launcher\InstallFolder` kosong): buka **form setup**.
2. Ada konfigurasi, tapi exe yang berjalan **bukan** `<install>\launcher.exe` (misalnya paket setup
   dibuka lagi): TaskDialog "Already installed in X" dengan pilihan **Run / Maintenance / Cancel**.
3. Normal: bersihkan sisa run sebelumnya, lalu cek update (keputusan 5). Kalau user memilih
   *Update now*: jendela progres, pasang, lalu jalankan versi baru. Setelah itu jalankan app, dan
   launcher keluar.
4. Folder instalasi atau versi aktif hilang/rusak: tawarkan Repair. Kalau ditolak, keluar dengan pesan.

**CLI** (`--` memisahkan argumen yang diteruskan ke app):

| Perintah | Arti |
| --- | --- |
| *(tanpa argumen)* | Alur di atas |
| `--install [--source S] [--target T] [--import P]... [--no-start-menu] [--no-desktop] [--no-run] [--quiet]` | Tanpa `--quiet`: form setup yang sudah terisi. Dengan `--quiet`: instalasi tanpa GUI |
| `--update [--quiet]` | Cek dan terapkan update tanpa bertanya (juga dipakai untuk verifikasi) |
| `--maintenance` | Jendela maintenance (target `ModifyPath`) |
| `--repair [--quiet]` | Repair |
| `--uninstall [--quiet]` | Uninstall (target `UninstallString` / `QuietUninstallString`) |
| `--import <file.pem> [--quiet]` | Tambah key. Tanpa `--quiet` ada konfirmasi yang menampilkan `keyId` |
| `--list-keys` | Daftar key beserta sumbernya (HKLM / HKCU) |
| `--remove-key <keyId>` | Hapus key dari HKCU (key HKLM tidak bisa dihapus dari sini) |
| `--apply --pid <pid>` | Disiapkan untuk tahap 3: tunggu PID keluar, lalu jalankan alur normal tanpa bertanya |

- Keluaran CLI ditulis ke console induk lewat `AttachConsole(ATTACH_PARENT_PROCESS)`. Exe tetap
  bersubsistem `windows`, jadi tidak ada jendela console yang berkedip.
- Exit code: `0` sukses, `1` argumen salah, `2` sumber tidak bisa dibaca atau rilis tidak sah,
  `3` gagal tulis/IO, `4` dibatalkan user, `5` app masih berjalan (uninstall).

**Registry:**

```
HKCU\<ApplicationName>\Launcher
   Source          REG_SZ     alamat folder rilis
   InstallFolder   REG_SZ
   StartMenu       REG_DWORD  pilihan user, dipakai lagi oleh Repair
   Desktop         REG_DWORD
   TrustedKeys\<keyId> = REG_SZ (PEM)

HKLM\Software\Policies\<ApplicationName>\Launcher          (opsional, ditulis IT)
   Source          REG_SZ     menimpa HKCU; isian sumber di form jadi read-only
   AllowUserKeys   REG_DWORD  0 = key dari HKCU diabaikan (default 1)
   TrustedKeys\<keyId> = REG_SZ (PEM)
```

Key tepercaya = HKLM + HKCU (kalau `AllowUserKeys` tidak 0). Pemilihan key memakai `keyId` dari `.sig`.
Nama value di `TrustedKeys` adalah `keyId` yang **dihitung ulang** dari PEM, bukan diambil dari teks
file.

**Apps & Features** (`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\<app_id>`):
`DisplayName`, `DisplayIcon` (`<install>\launcher.exe,0`), `Publisher`, `DisplayVersion`,
`InstallLocation`, `InstallDate` (`yyyyMMdd`), `EstimatedSize` (KB, diperbarui setiap update),
`UninstallString`, `QuietUninstallString`, `ModifyPath` (`--maintenance`), dan `NoRepair = 1`.

**Shortcut.** Start menu di `%AppData%\Microsoft\Windows\Start Menu\Programs\<ApplicationName>.lnk`,
dan desktop di `Desktop\<ApplicationName>.lnk`. Keduanya menunjuk ke `<install>\launcher.exe`, dengan
ikon dari exe yang sama. Hanya shortcut Start menu yang diberi `System.AppUserModel.ID = product::APP_ID`. Saat
uninstall, yang dihapus hanya shortcut yang target-nya launcher instalasi ini.

**Kontrak launcher → app.** Saat menjalankan app, launcher memasang environment variable berikut:

- `LAUNCHER_PATH` = path lengkap `<install>\launcher.exe`
- `LAUNCHER_APP_ID` = `product::APP_ID`

Working directory app adalah `app-<id>\`. Argumen setelah `--` diteruskan apa adanya.

**Sisi WPF** (hanya `Em.Ui.Wpf.Core` dan host, **tidak dicerminkan ke MAUI** karena update management
khusus WPF, T1 di laporan):

- Paling awal di `EmApp.BuildApp`, sebelum DI dan sebelum jendela apa pun dibuat:
  - **ada `LAUNCHER_PATH`**: panggil `SetCurrentProcessExplicitAppUserModelID(<AUMID dari env>)`
    dan simpan path launcher untuk `RelaunchCommand`;
  - **tidak ada, tapi `release.json` ada** di `AppContext.BaseDirectory` (managed): cari launcher di
    `..\launcher.exe`. Kalau ada, jalankan dengan `--` + argumen app, lalu `Environment.Exit(0)`.
    Kalau tidak ada, jalan biasa tanpa AUMID;
  - **tidak managed** (dijalankan dari Visual Studio, bin, atau local publish folder): tidak melakukan
    apa-apa.
- Setiap `TabbedMainWindow`, saat `SourceInitialized` dan hanya kalau AUMID aktif, mendapat properti
  jendela `PKEY_AppUserModel_ID`, `PKEY_AppUserModel_RelaunchCommand` (`"<launcher>"`),
  `RelaunchDisplayNameResource` (`ApplicationName`), dan `RelaunchIconResource` (`<launcher>,0`)
  lewat `SHGetPropertyStoreForWindow`.
- Tidak ada loop: launcher selalu memasang env, dan app hanya mengalihkan diri kalau env tidak ada.

**Build dan `dist/`.** Script `src/frontend/Launcher/build-dist.ps1` melakukan:

- mencari VS yang punya `VC.Tools.x86.x64` lewat `vswhere`;
- memuat lingkungan `vcvars64.bat`;
- menjalankan `cargo build --release`;
- menyalin `target/release/launcher.exe` ke `dist/launcher/launcher.exe` di root repo (dari folder launcher: `..\..\..\dist\launcher\`), dan membuat foldernya kalau
  belum ada;
- menampilkan ukuran dan SHA-256-nya.

`.cargo/config.toml` memasang `--remap-path-prefix`, supaya path absolut mesin build tidak masuk ke
exe. `dist/` di-commit, sedangkan `target/` tidak. `Em.Ui.Wpf.csproj` mereferensikan
`..\..\..\dist\launcher\launcher.exe` sebagai `Content` dengan `Link="launcher.exe"`,
`CopyToOutputDirectory` dan `CopyToPublishDirectory` = `PreserveNewest`, sehingga di folder publish
file itu tetap berada di root.

Folder `dist/` di root repo khusus untuk **binary jadi yang di-commit dan dipakai project lain**, satu
subfolder per artefak (`dist/launcher/`, dan artefak lain menyusul). Isinya hanya dibangun ulang lewat
script masing-masing, tidak pernah diedit tangan. Folder ini berbeda dari `release/` (hasil publish
Release Manager, diabaikan git) dan dari `target/` (hasil build `cargo`, diabaikan git).

**Teks UI** dalam bahasa Inggris, sama dengan UI WPF. Tampilan UI:

- font Segoe UI;
- manifest mengaktifkan comctl32 v6, DPI **PerMonitorV2**, `asInvoker`, `longPathAware`, dan
  `supportedOS` Windows 10/11;
- tanpa Mica dan tanpa dark mode (ditunda).

## Bentuk akhirnya, singkat

```
Setup:    launcher.exe (+ *.pem di sebelahnya) → form → verifikasi sumber → staging → app-<id>
          → current.json → launcher root → shortcut + Apps & Features → jalankan app
Startup:  shortcut/pin → launcher → cek (sig → keyId → verify → manifest) → tanya → [update] → app → keluar
App langsung: Em.Ui.Wpf.exe (managed, tanpa env) → ..\launcher.exe → keluar
Modify:   Settings → launcher --maintenance → Repair | Change source | Uninstall
```

---

## Pembagian sesi

Keputusan 16 dan 17. Setiap sesi dimulai dengan membaca plan ini, termasuk **Catatan eksekusi** di
bagian paling bawah, lalu mengerjakan hanya bagian sesinya.

| Sesi | Bagian | Isi | Selesai kalau |
| --- | --- | --- | --- |
| 1 | 0 + 1 | Persiapan (`rustfmt.toml`, `.editorconfig`, `.cargo/config.toml`, crate, `product.toml` + `build.rs` + manifest + ikon), lalu `src/format/` dengan test terhadap `doc/release-format-samples/` | `cargo fmt --check`, `clippy -D warnings`, dan `test` bersih; exe berikon |
| 2 | 2 + `config.rs` dari bagian 3 | `source/`, `install_layout`, `updater`, `repair`, `launch`, registry dan key tepercaya, serta test integrasi di folder sementara | Semua skenario test integrasi bagian 2 lolos |
| 3 | 3 (sisanya) + 5 | Shortcut + AUMID, entri Apps & Features, uninstall (salin diri ke `%TEMP%`), `main.rs` + CLI lengkap (`AttachConsole`, exit code, log panic) | Semua perintah CLI bisa dipakai tanpa GUI |
| 4 | 4 | GUI `winsafe`: form setup, progres, maintenance, dan TaskDialog | Semua jendela bisa dibuka dan dicoba manual |
| 5 | 6 + 7 + `build-dist.ps1` | Sisi WPF, `dist/launcher/launcher.exe`, dan semua dokumentasi | Kedua `dotnet build` bersih, `dist/launcher/launcher.exe` ada |
| 6 | 8 | Verifikasi end-to-end (langkah 1–6), laporan eksekusi final, pindah plan ke `plan/executed/`, lalu commit terakhir | Semua skenario a–j tercatat hasilnya |

- Kalau satu sesi terlalu besar, sesi itu boleh dipecah lagi (misalnya 2a/2b). Pecahannya dicatat di
  Catatan eksekusi.
- Di akhir setiap sesi, tambahkan subbagian di **Catatan eksekusi** yang berisi:
  - hasil verifikasi sesi;
  - keputusan yang diambil sendiri;
  - hal yang ditunda ke sesi berikutnya;
  - potongan "Konsep Rust" untuk file yang dibuat di sesi itu.
  Setelah itu commit (keputusan 17). Plan tetap di `plan/unexecuted/` sampai sesi 6.
- Langkah 8 di bagian "Verifikasi eksekusi" hanya untuk sesi 6. Sesi 1–5 cukup menjalankan verifikasi
  sesinya sendiri (kolom "Selesai kalau").

## Yang dikerjakan

### 0. Persiapan

- Bekerja di branch **`launcher-tahap2`** (sudah ada, berisi commit plan + kerangka).
- `rustfmt.toml`, `.editorconfig` `[*.rs]` menjadi 3 spasi, dan `.cargo/config.toml` (keputusan turunan
  "Gaya kode" dan "Build dan `dist/`").
- Tambahkan crate ke `Cargo.toml`. Catat versinya di laporan.
- `product.toml` + `build.rs` (membaca toml, menghasilkan konstanta `product` dan `.rc`) +
  `launcher.manifest`. Pastikan exe hasil build menampilkan ikon produk di Explorer.
- Cek nama: `grep -ri <nama-produk>` di `src/frontend/Launcher` hanya boleh menemukan nilai di `product.toml`
  (keputusan 18). Pemeriksaan yang sama diulang di verifikasi akhir.

### 1. Kontrak format (sisi pembaca) — `src/format/`

Nama file mengikuti pasangannya di `Em.Ui.Wpf.Core/Release/Format/` (keputusan turunan "Peletakan
tipe"):

- `release_layout.rs`: konstanta `BINARIES_FOLDER`, `MANIFEST_FILE_NAME`, `SIGNATURE_FILE_NAME`.
- `release_manifest.rs` / `release_file.rs`: `ReleaseManifest`/`ReleaseFile` (serde, field tak dikenal
  diabaikan), validasi path (bagian 2.3, termasuk unik tanpa memandang huruf besar/kecil), dan hash 64
  hex huruf kecil.
- `release_signature_file.rs`: model `.sig`.
- `release_signature.rs`: `ReleaseSignature::key_id_of(spki_der)`, parse PEM SubjectPublicKeyInfo
  (teks di luar `BEGIN`/`END` diabaikan, `PRIVATE KEY` ditolak dengan error khusus), dan
  `verify(manifest_bytes, sig, trusted_keys) -> Result<(), SignatureError>` dengan varian
  `UnknownKey` / `Invalid` / `Malformed`.
- `release_hash.rs`: SHA-256 streaming dengan callback progres.
- `release_format_error.rs`: `ReleaseFormatError`.
- Test (`cargo test`): keempat varian di `doc/release-format-samples/` memberi hasil sesuai tabel
  bagian 9, `keyId` dan setiap `path size sha256` cocok dengan `expected.txt`, serta aturan path
  (termasuk `..`, `\`, `:`, segmen berawalan titik, duplikat beda kapitalisasi).

### 2. Sumber rilis dan update — `src/source/`, `src/install/`

Nama file di bagian ini dan bagian 3–4 mengikuti tipe utamanya (keputusan turunan "Peletakan tipe").
Nama file yang disebut di bawah hanya gambaran, dan yang final ditentukan tipe yang benar-benar dibuat.

- `source/`: `release_source.rs` (trait `ReleaseSource`), `http_source.rs`, dan `folder_source.rs`
  (keputusan turunan "Sumber rilis").
- `install/install_layout.rs`: path instalasi, `current.json`, dan `<id>`.
- `install/updater.rs`: urutan update 1–7, dengan progres (total byte, file saat ini, byte per detik)
  lewat struktur bersama (`Arc` + atomik) dan pembatalan lewat `AtomicBool`.
- `install/repair.rs`, `install/uninstall.rs`.
- `install/launch.rs`: menjalankan app dengan env kontrak, dan `--apply --pid`.
- Test integrasi di folder sementara: rilis uji dibuat di test dengan key P-256 sementara (helper di
  `tests/common/`). Skenario: install ke folder kosong; update di mana hanya file yang berubah diunduh
  (dihitung dari sumber); resume `.part`; file di sumber diubah setelah manifest (update dibatalkan dan
  versi aktif tetap); tanda tangan salah (tidak ada yang tertulis); repair satu file rusak dan satu file
  hilang; dan pembersihan versi lama.

### 3. Konfigurasi dan integrasi Windows — `src/config.rs`, `src/shell/`

- `config.rs`: baca/tulis `HKCU\<App>\Launcher`, gabungan kebijakan HKLM, dan key tepercaya.
- `shell/shortcut.rs`: `IShellLinkW` + `IPersistFile`, dan AUMID lewat `IPropertyStore` (Start menu).
- `shell/uninstall_entry.rs`: tulis/perbarui/hapus entri Apps & Features.
- `uninstall`: konfirmasi (menyebut bahwa pengaturan user ikut dihapus), cek app berjalan (proses
  dengan image di `<install>`; Retry/Cancel, atau exit code 5 saat `--quiet`), lalu salin diri ke
  `%TEMP%\<acak>\launcher.exe` dan jalankan `--uninstall-finish <install> --parent-pid <pid>`
  (argumen internal). Salinan itu menghapus folder instalasi, shortcut, entri Uninstall, dan seluruh
  `HKCU\<ApplicationName>`, lalu menjadwalkan penghapusan folder `%TEMP%`-nya lewat proses `cmd`
  terlepas.

### 4. GUI — `src/ui/`

- `setup.rs`: form setup (keputusan 7):
  - sumber + Browse folder;
  - folder instalasi + Browse;
  - daftar key (`keyId` + sumber) + **Import...** (file `.pem`);
  - tiga checkbox;
  - tombol **Install / Cancel**.
  `*.pem` di folder exe otomatis masuk daftar. Isian yang dikunci HKLM menjadi read-only. Tombol Install
  aktif kalau sumber, folder, dan minimal satu key terisi. Sebelum membuat apa pun, Install memeriksa
  sumber dulu (unduh + verifikasi), dan kesalahannya ditampilkan di form. Setelah itu form berganti ke
  halaman progres, lalu ke halaman selesai berisi petunjuk pin taskbar.
- `progress.rs`: jendela/halaman progres dengan teks status, nama file, bar berdasarkan byte, kecepatan,
  sisa waktu, dan **Cancel**. Worker berjalan di thread terpisah, dan UI membaca counter lewat
  `WM_TIMER` sekitar 200 ms. Fase tanpa ukuran memakai marquee.
- `maintenance.rs`: info (versi/`publishedAtUtc`, sumber, folder, key) + **Repair**, **Change source**
  (edit sumber + Import/Remove key), **Uninstall**, dan **Close**.
- `prompts.rs`: TaskDialog untuk tawaran update (*Update now / Later*, ukuran unduhan,
  `publishedAtUtc`), konfirmasi import (`keyId`), konfirmasi uninstall, "already installed", dan error.

### 5. `main.rs` dan CLI

Parse argumen dengan `lexopt`, lalu dispatch ke mode (tabel CLI). Hubungkan `AttachConsole` untuk
keluaran CLI, pasang exit code, dan pastikan panic tercatat di `launcher.log` sebelum proses berhenti.

### 6. Sisi WPF — `src/shared/Em.Ui.Wpf.Core` + host

- Kelas baru `Core/LauncherIntegration.cs` (namespace mengikuti folder `Core` yang ada), dipanggil di
  awal `EmApp.BuildApp`, sesuai keputusan turunan "Sisi WPF". P/Invoke `shell32`
  (`SetCurrentProcessExplicitAppUserModelID`, `SHGetPropertyStoreForWindow`) ada di kelas ini. Kelas
  ini juga menyediakan `IsManaged` dan `LauncherPath`.
- `TabbedMainWindow`: pasang properti jendela saat `SourceInitialized` lewat `LauncherIntegration`.
- `Em.Ui.Wpf.csproj`: item `Content` untuk `launcher.exe`.
- XML doc Bahasa Indonesia untuk member `public` baru, tanpa nama objek database.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `dotnet build src/backend/Em.Api.slnx` bersih.

### 7. Dokumentasi

- `src/frontend/Launcher/CLAUDE.md`:
  - gaya kode (keputusan 15, mengganti baris "rustfmt defaults (4-space indent)");
  - layout instalasi;
  - registry dan kontrak env;
  - CLI;
  - `build-dist.ps1`;
  - netral produk (keputusan 18) dan cara mem-build untuk produk lain lewat `LAUNCHER_PRODUCT`;
  - aturan bahwa `dist/launcher/launcher.exe` wajib dibangun ulang dan di-commit setiap source launcher berubah.
- Root `CLAUDE.md`: satu baris di "Repository layout" untuk `dist/`, yaitu binary jadi yang di-commit,
  satu subfolder per artefak, dibangun ulang lewat script-nya, dan tidak diedit tangan
  (`dist/launcher/` dari `src/frontend/Launcher/build-dist.ps1`).
- `src/frontend/CLAUDE.md`: paragraf singkat bahwa host membawa `launcher.exe` sebagai `Content`, dan
  tentang `LauncherIntegration` (managed / AUMID / pengalihan ke launcher).
- Laporan diskusi, bagian 8:
  - tandai langkah 2 sudah punya plan;
  - catat perubahan terhadap laporan: Rust, tanpa key bawaan, key disimpan di registry, dan root
    registry `HKCU\<ApplicationName>` (bukan `HKCU\Software\Em`).

### 8. Verifikasi eksekusi

Semua `cargo` dijalankan dengan lingkungan VS 2022 (lihat temuan).

1. `cargo fmt --check`, `cargo clippy -D warnings`, dan `cargo test` bersih. Kedua `dotnet build` bersih.
2. `build-dist.ps1` menghasilkan `dist/launcher/launcher.exe`. Catat ukurannya, dan pastikan ikonnya tampil di
   Explorer.
3. **Rilis uji nyata**:
   - publish host dengan perintah Prepare tahap 1 ke scratchpad (harus berisi `launcher.exe`);
   - buat folder rilis bertanda tangan lewat program konsol sementara yang mereferensikan
     `Em.Ui.Wpf.Core/Release`, dengan key P-256 sementara; public key-nya diekspor ke `.pem`;
   - jalankan `Em.Api` lokal (debug token), lalu Sync ke CDN lokal lewat `CdnReleaseTarget` seperti
     tahap 1.
4. **Pengaman mesin developer**:
   - sebelum pengujian, `reg export "HKCU\Software\Em Consignment Application"` ke scratchpad, karena
     uninstall menghapus seluruh key itu, termasuk setting Release Manager dan koneksi;
   - instalasi uji memakai folder di scratchpad, bukan default;
   - setelah selesai, pulihkan registry dari cadangan dan pastikan shortcut uji hilang.
5. Skenario, masing-masing dicatat hasilnya:
   - a. **Setup GUI** dari folder "paket setup" berisi `launcher.exe` + `.pem`: form terisi, Install,
     progres, halaman selesai, lalu app berjalan. Screenshot setiap halaman.
   - b. **`Get-StartApps`** menampilkan `<ApplicationName>` dengan AppID sama dengan `app_id` di `product.toml`.
     Entri Apps & Features ada dengan field yang benar (`Get-ItemProperty`).
   - c. **Pengalihan**: jalankan `app-<id>\Em.Ui.Wpf.exe` langsung. Launcher tercatat di log sebagai
     "started by app redirect", lalu app berjalan dengan env kontrak dan tidak ada loop.
   - d. **Update**: ubah satu modul, publish, dan Sync lagi. Launcher menampilkan tawaran (screenshot).
     *Later* menjalankan versi lama. `--update --quiet` hanya mengunduh file yang berubah (cocokkan
     dengan log akses atau hitungan di log launcher). Setelah itu `current.json` pindah dan folder lama
     terhapus.
   - e. **Offline**: sumber dimatikan, startup tetap menjalankan versi terpasang tanpa dialog.
   - f. **Repair**: rusak satu DLL dan hapus satu file, lalu `--repair --quiet` memulihkan keduanya.
     Buka `--maintenance` (screenshot).
   - g. **Key**: `--import` file private key ditolak, `--list-keys` dan `--remove-key` berjalan, dan
     rilis yang ditandatangani key lain ditolak.
   - h. **HKLM**: dilewati kalau tidak ada hak admin, dan dicatat sebagai "tidak diuji". Kalau ada:
     `Source` yang dikunci membuat isian read-only, dan `AllowUserKeys = 0` mengabaikan key HKCU.
   - i. **Instalasi senyap**: `--install --source ... --target ... --import ... --quiet` ke folder kedua,
     dengan exit code 0.
   - j. **Uninstall**: `--uninstall --quiet` saat app berjalan menghasilkan exit code 5. Setelah app
     ditutup, semuanya terhapus (folder, shortcut, entri Uninstall, `HKCU\<App>`). Uninstall dari
     Settings → Installed apps dicoba sekali lewat UI kalau memungkinkan.
6. Folder uji di CDN lokal, key sementara, dan instalasi uji dihapus. Registry dipulihkan.
7. Pindahkan plan ke `plan/executed/` dan tambahkan **Catatan eksekusi**:
   - hasil setiap langkah;
   - keputusan yang diambil sendiri;
   - hal yang ditunda;
   - bagian **"Konsep Rust"**: ownership/borrowing, `Result`/`?` dan tipe error, trait (`ReleaseSource`),
     `Arc`/atomik/thread, `unsafe` dan FFI di `windows`, `build.rs`, modul dan visibilitas, serde derive,
     serta test di Rust. Setiap konsep ditunjukkan dengan file dan baris tempat ia dipakai.
8. **Commit terakhir** di branch `launcher-tahap2`, dalam Bahasa Indonesia (ringkasan + isi),
   mencakup perubahan sesi 6 dan plan yang sudah dipindah. Sesi 1–5 sudah punya commit masing-masing
   (termasuk `Cargo.lock`, `dist/launcher/launcher.exe`, sisi WPF, dan dokumentasi). **Tanpa push.**

---

## Catatan eksekusi

### Sesi 1 — bagian 0 + 1 (2026-09-28)

**Hasil verifikasi** (semua `cargo` di lingkungan `vcvars64.bat` VS 2022 Enterprise):

- `cargo fmt --check`: bersih. `cargo clippy --all-targets -- -D warnings`: bersih.
- `cargo test`: 19 test unit + 5 test integrasi (`tests/release_format_samples.rs`) lolos. Keempat varian
  `doc/release-format-samples/` memberi hasil sesuai tabel bagian 9: `valid` sah dan semua file cocok,
  `tampered-manifest` → `SignatureError::Invalid`, `tampered-file` → tanda tangan sah lalu tepat satu file
  beda SHA-256 (ukurannya sama), `unknown-key` → `SignatureError::UnknownKey`. `keyId` dan setiap
  `path size sha256` sama dengan `expected.txt`.
- `cargo build --release`: `launcher.exe` 256.512 byte. Ikon produk tampil (diekstrak dari exe), resource
  manifest berisi comctl32 v6, `asInvoker`, `PerMonitorV2`, `longPathAware`, `supportedOS`. VERSIONINFO:
  ProductName `Em Sample Application`, CompanyName `Acme Corp`. `dumpbin /dependents` hanya
  `KERNEL32`, `ntdll`, `api-ms-win-core-synch` (tanpa `vcruntime140.dll`).
- `build.rs` menolak `product.toml` dengan field kosong (diuji lewat `LAUNCHER_PRODUCT` ke file sementara).
- `grep -ri <nama-produk>` di `src/frontend/Launcher` (tanpa `target/`): hanya nilai di `product.toml` dan kalimat
  aturan netral-produk di `CLAUDE.md` launcher itu sendiri.
- Tidak ada path `C:\Users\...` di exe release.

**Versi crate** (terbaru saat eksekusi): `winsafe` 0.0.29, `windows` 0.62.2, `ureq` 3.4.2, `serde` 1.0.229,
`serde_json` 1.0.151, `sha2` 0.11.0, `p256` 0.14.0, `base64` 0.23.1, `lexopt` 0.3.2; build: `embed-resource`
3.0.11, `toml` 1.1.6, `serde`; dev: `tempfile` 3.27.0. Toolchain: cargo 1.98.1.

**Keputusan yang diambil sendiri:**

- **Crate = lib + bin.** `src/lib.rs` memuat modul (`format`, `product`), `src/main.rs` memakainya lewat
  `launcher::...`. Alasannya: test integrasi di `tests/` hanya bisa memakai library crate, dan item `pub`
  di library tidak dianggap dead code oleh clippy selama sesi berikutnya belum memakainya.
- **`toml` sebagai build-dependency** (tidak ada di daftar crate plan): `build.rs` butuh parser TOML untuk
  `product.toml`. Tidak ikut ke exe.
- **`p256` tanpa fitur `pem`**: PEM di-parse sendiri di `ReleasePublicKey::from_pem`, karena spec meminta
  teks di luar blok diabaikan dan label `PRIVATE KEY` ditolak dengan error khusus. Fitur `pkcs8` cukup
  untuk DER.
- **Dev-dependency penanda tangan tanpa `rand_core`**: key uji dibuat dari scalar tetap
  (`SigningKey::from_slice(&[seed; 32])`); fitur `ecdsa` `p256` sudah mencakup signing. Sesi 2 bisa memakai
  cara yang sama untuk rilis uji.
- **`ureq` dengan fitur `native-tls-no-default`** (SChannel tanpa bundel root webpki). Sesi 2 harus
  memasang `RootCerts::PlatformVerifier` (atau yang setara) di konfigurasi TLS supaya certificate store
  Windows yang dipakai.
- **`.cargo/config.toml` berisi `+crt-static`, bukan `--remap-path-prefix`.** `rustflags` di file config
  tidak bisa memakai variabel, jadi path mesin (repo, `%USERPROFILE%\.cargo\registry`) tidak bisa ditulis di
  sana tanpa mengikat file itu ke satu mesin; `trim-paths` juga belum stabil di cargo 1.98. Remap dipindah
  ke `build-dist.ps1` (sesi 5) lewat `cargo build --release --config "target.x86_64-pc-windows-msvc.rustflags=[...]"`
  (array `--config` digabung dengan array di file). `crt-static` ditambahkan supaya exe jalan di mesin
  tanpa VC++ redistributable.
- **Tipe tambahan `ReleasePublicKey` + `ReleaseKeyError`** (`release_public_key.rs`): public key yang sudah
  divalidasi P-256 beserta `keyId` hasil hitung ulang, `from_pem`, `from_spki_der`, `to_pem` (untuk
  registry). `ReleaseSignature::verify` menerima `&[ReleasePublicKey]`.
- **Tipe "static class" C# jadi unit struct** dengan associated const/fn (`ReleaseLayout::BINARIES_FOLDER`,
  `ReleaseHash::compute`, `ReleaseSignature::key_id_of`), supaya pemanggilannya sama dengan C#.
- **`ReleaseManifest` hanya bisa dibuat lewat `from_bytes`** (field privat + getter, JSON dibaca lewat struct
  privat `ManifestJson`), jadi manifest yang belum divalidasi tidak bisa ada. `ReleaseFile` tetap field
  publik (record data).
- **`SignatureError::Malformed`** = base64 rusak, panjang ≠ 64 byte, atau `r`/`s` di luar rentang kurva.
- **Perbandingan path tanpa huruf besar/kecil** lewat `ReleaseFile::path_key`: huruf besar per karakter,
  karakter yang huruf besarnya lebih dari satu karakter dibiarkan (meniru `OrdinalIgnoreCase`/NTFS).
- **`publishedAtUtc`** divalidasi ketat tanpa crate waktu (`yyyy-MM-ddTHH:mm:ss[.f…]Z`, tanggal dan jam
  dalam rentang) dan disimpan sebagai teks. Pemecahan untuk `DisplayVersion` menyusul di sesi 3.
- **`ReleaseHash::compute`**: callback progres mengembalikan `io::Result<()>`; `Err` menghentikan hash
  (dipakai untuk pembatalan di sesi 2).
- **VERSIONINFO** ikut ditanam di `.rc` (ProductName, CompanyName, FileDescription `<app_name> Launcher`)
  dari data produk, supaya Task Manager dan Properties tidak menampilkan exe tanpa nama.
- **`CLAUDE.md` launcher** diperbarui sedikit sekarang (baris gaya kode "4-space" sudah salah, dan status),
  supaya sesi berikutnya tidak tersesat. Dokumentasi lengkapnya tetap di sesi 5.

**Ditunda ke sesi berikutnya:**

- Konfigurasi root certificate `ureq` (sesi 2, lihat di atas).
- `--remap-path-prefix` di `build-dist.ps1` (sesi 5).
- Saat build, cargo kadang mencetak `did not finalize incremental compilation session directory ... Access is
  denied`. Hanya peringatan cache incremental (kemungkinan antivirus/indexer), build tetap sukses.

**Konsep Rust (sesi 1):**

| Konsep | Di mana |
| --- | --- |
| Modul dan visibilitas: `mod` privat + `pub use` sebagai "folder = namespace"; `pub(crate)` untuk yang hanya dipakai di dalam crate | `src/format/mod.rs:4-20`, `src/format/release_public_key.rs:123` |
| Library + binary dalam satu package | `src/lib.rs`, `src/main.rs` |
| `build.rs`: kode yang jalan sebelum kompilasi, `cargo:rerun-if-*`, file hasil di `OUT_DIR` lalu `include!` | `build.rs:34-53`, `src/product.rs:5` |
| Tipe `!` (fungsi yang tidak pernah kembali) | `build.rs:160` |
| Ownership vs borrowing: getter mengembalikan pinjaman (`&str`, `&[T]`) bukan salinan; `to_vec()` membuat salinan milik sendiri | `src/format/release_manifest.rs:78-84`, `src/format/release_public_key.rs:86` |
| Lifetime eksplisit: hasil fungsi meminjam dari argumen yang sama | `src/format/release_public_key.rs:144` |
| `Result`/`?`, `map_err`, `ok_or` untuk mengubah jenis error | `src/format/release_signature.rs:55-68`, `src/format/release_manifest.rs:35` |
| Tipe error sendiri: `impl Display` + `impl Error` | `src/format/release_format_error.rs:26-32`, `src/format/release_signature.rs:72-82` |
| Enum sebagai daftar alasan gagal (pengganti enum status C#) | `src/format/release_signature.rs:20`, `src/format/release_public_key.rs:24` |
| Trait dan generic lewat `impl Trait` (`impl Read`, `impl FnMut`), closure sebagai callback | `src/format/release_hash.rs:21-37` |
| `match` pada tuple/`Option`, `let ... else`, let-chain `if a && let Some(x)` (edition 2024) | `src/format/release_file.rs:63`, `src/format/release_manifest.rs:109-112`, `src/format/release_public_key.rs:60` |
| serde derive: `#[derive(Deserialize)]`, `#[serde(rename)]`, field tak dikenal diabaikan secara bawaan | `src/format/release_signature_file.rs:7-11`, `src/format/release_manifest.rs:18-20` |
| Derive trait bawaan (`Debug`, `Clone`, `PartialEq`, `Copy`) | `src/format/release_file.rs:6`, `src/format/release_signature.rs:20` |
| Test unit (`#[cfg(test)] mod tests`, akses ke item privat lewat `use super::*`) dan test integrasi (`tests/`, hanya API publik) | `src/format/release_manifest.rs:159`, `tests/release_format_samples.rs:42-47` |

### Sesi 2 — bagian 2 + konfigurasi dari bagian 3 (2026-09-28)

**Hasil verifikasi** (lingkungan `vcvars64.bat` VS 2022 Enterprise):

- `cargo fmt --check` dan `cargo clippy --all-targets -- -D warnings`: bersih.
- `cargo test`: 22 test unit + 29 test integrasi lolos (`release_format_samples` 5, `updater` 10, `http_source` 5,
  `config` 7, `app_process` 2).
- Skenario test integrasi bagian 2, semuanya di folder sementara dengan rilis uji yang ditandatangani key P-256
  sementara (`tests/common/`):
  - install ke folder kosong (`installs_into_an_empty_folder`): semua file, `release.json` + `.sig` byte demi byte,
    `current.json`, launcher root dari rilis, tanpa sisa staging; install ulang rilis yang sama = `AlreadyActive`
    tanpa membuka file apa pun;
  - update hanya mengunduh file yang berubah, dihitung dari sumber (`CountingSource`): 2 dari 6 file, `download_size`
    = jumlah byte keduanya, versi lama terhapus;
  - resume `.part`: unduhan dilanjutkan dari byte 120.000, hanya sisa 80.000 byte yang diterima;
  - file di sumber diubah setelah manifest: `ServerChanged` ("... try again later"), versi aktif tetap, `app-<id>`
    baru tidak ada;
  - tanda tangan salah (`UnknownKey`, lalu `Invalid` setelah satu byte manifest diubah): folder instalasi tidak
    dibuat sama sekali;
  - repair satu file rusak + satu file hilang: tepat dua file itu yang diunduh ulang; repair tanpa `current.json`
    memakai ulang folder versi tanpa unduhan;
  - pembersihan versi lama: folder versi yang exe-nya masih terbuka (tanpa sharing, meniru app berjalan) dilewati,
    lalu terhapus oleh `remove_leftovers()` di run berikutnya bersama `launcher.old.exe`; staging basi terhapus;
  - tambahan: pembatalan (`Cancelled`, versi aktif tetap) dan salinan lokal yang rusak diunduh ulang, bukan disalin.
- `HttpSource` diuji terhadap server HTTP mini di test: unduhan 2,01 MiB = 3 request Range, resume dari offset,
  offset tepat di akhir file (416), server yang mengabaikan Range (200 → mulai dari nol), path dengan spasi, install
  penuh lewat HTTP, dan server yang tidak bisa dihubungi (`Unavailable`).
- Test registry memakai key `HKCU\Software\LauncherTest-<pid>-<n>` (kebijakan juga di HKCU karena HKLM butuh
  admin) dan menghapusnya sendiri; setelah test tidak ada sisa di `HKCU\Software`.
  `HKCU\Software\Em Consignment Application` tidak disentuh.
- `cargo build --release` bersih; exe masih 256.512 byte karena `main.rs` belum memakai modul baru (dibuang linker).
- `grep -ri <nama-produk>` (tanpa `target/`): tetap hanya `product.toml` dan `CLAUDE.md` launcher.

**Keputusan yang diambil sendiri:**

- **Koreksi sesi 1: `ureq` memakai fitur `native-tls`, bukan `native-tls-no-default`.** Konektor native-tls `ureq`
  hanya dikompilasi di bawah `cfg(feature = "native-tls")`, jadi dengan `native-tls-no-default` saja HTTPS tidak
  jalan sama sekali. Fitur penuh ikut membawa bundel root webpki (`webpki-root-certs`, sekitar 150 KB), tetapi yang
  dipakai tetap certificate store Windows lewat `RootCerts::PlatformVerifier` + `TlsProvider::NativeTls`.
- **Unduhan per potongan 1 MiB lewat Range.** `ureq` 3 tidak punya timeout per baca, hanya total untuk seluruh body.
  Setiap potongan menjadi request sendiri dengan batas 60 detik (koneksi 10 detik), jadi koneksi yang macet
  ketahuan (di bawah sekitar 17 KB/s dianggap macet). Kalau server menjawab 200, file diminta ulang tanpa Range
  dengan agent tanpa batas waktu body. Pengecekan `release.json`/`.sig`: koneksi 3 detik, total 5 detik.
- **Sumber rilis** = trait `ReleaseSource` (`address`, `read_file`, `open_binary(path, offset)`) dengan
  `SourceStream { offset, reader }`: sumber boleh menjawab `offset = 0` (mulai dari nol). Pembuatnya fungsi
  `source::from_address` (awalan `http://`/`https://` → `HttpSource`, selain itu `FolderSource`).
- **Nama file** mengikuti tipe yang benar-benar dibuat: `src/config/` (folder, bukan `config.rs`) berisi
  `launcher_config.rs` (`LauncherConfig` + `ConfigLocation`), `trusted_keys.rs` (`TrustedKeys`, `TrustedKey`,
  `KeyScope`), dan `registry_hive.rs` (`RegistryHive`); `install/launch.rs` menjadi `install/app_process.rs`
  (`AppProcess`). Tipe tambahan: `VerifiedRelease` (satu-satunya jalan mendapat manifest dari sumber, urutan
  bagian 6 langkah 1–4), `UpdateError`, `UpdateProgress` + `UpdatePhase`, `UpdateOutcome` + `UpdateSummary`,
  `VersionFolder`, `CurrentVersion`, `ActiveVersion`, `SourceError` + `SourceErrorKind`, dan `LauncherLog`
  (`src/launcher_log.rs`).
- **`repair.rs` berisi blok `impl Updater` kedua** (padanan *partial class* C#), karena repair memakai semua helper
  updater (`download`, `hash`, `activate`). Field `Updater` dibuat `pub(super)` supaya modul saudara itu bisa
  memakainya.
- **`install/uninstall.rs` belum dibuat.** Tabel "Pembagian sesi" menaruh uninstall di sesi 3, jadi seluruhnya
  dikerjakan di sana.
- **Salinan lokal diverifikasi di tujuan**: file versi aktif disalin (`fs::copy`) ke `.part`, lalu salinannya yang
  di-hash. Kalau tidak cocok (versi aktif rusak), file itu **diunduh**, bukan membatalkan update dengan pesan
  "server berubah" yang keliru. Hanya file hasil unduhan yang tidak cocok yang memicu `ServerChanged`, dan
  `.part`-nya dibuang.
- **Resume di dua tingkat**: file yang sudah utuh di `.staging-<id>` (sudah lolos hash sebelum diberi nama) dipakai
  ulang setelah di-hash sekali lagi, dan `.part` dilanjutkan dari ukurannya. `.part` yang lebih besar dari ukuran
  di manifest dimulai dari nol.
- **Folder `app-<id>` sisa** yang bukan versi aktif dihapus dulu sebelum staging di-rename ke sana.
- **Launcher root** diganti hanya kalau rilis membawa `launcher.exe` dan isinya beda (ukuran + SHA-256). Kegagalan
  langkah 6 hanya dicatat di log, karena versi baru sudah aktif dan tetap bisa dijalankan.
- **Pembersihan saat startup (`remove_leftovers`)** hanya menghapus `launcher.old.exe` dan `app-*` selain versi
  aktif, dan hanya kalau `current.json` terbaca. `.staging-*` dibiarkan untuk resume dan baru dihapus setelah update
  berikutnya sukses (langkah 7).
- **Repair**: kalau folder `app-<id>` rilis di sumber ada (walaupun `current.json` hilang), perbaikan dilakukan di
  tempat lalu versi itu diaktifkan; kalau tidak ada (versi aktif lebih lama, atau instalasi hilang), repair =
  install.
- **String kosong di registry = value dihapus.** `winsafe` 0.0.29 mengirim buffer null untuk `Sz("")`, sehingga
  `RegSetValueExW` gagal dengan `ERROR_NOACCESS` (998). Pembacaannya tetap menghasilkan kosong.
- **`TrustedKeys`**: nama value = `keyId` hasil hitung ulang; saat dibaca `keyId` selalu dihitung ulang dari PEM,
  dan value yang bukan key P-256 sah dilewati (dicatat di log). `remove` mencari lewat `keyId` hasil hitung ulang
  (tanpa memandang huruf besar/kecil), jadi value yang namanya menyesatkan tetap bisa dihapus. `usable` membuang
  duplikat HKLM/HKCU. `LauncherConfig::load` memberi bawaan `StartMenu`/`Desktop` = dicentang dan
  `AllowUserKeys` = 1.
- **`LauncherLog`**: satu log per proses (`static Mutex<Option<...>>`); tulisan diabaikan sampai `open` dipanggil,
  jadi test tidak menulis log. Diputar ke `launcher.log.1` saat mencapai 1 MB. Waktu lokal dari `GetLocalTime`,
  tanpa crate waktu.
- **`AppProcess::wait_for_exit`**: PID yang tidak ada (`ERROR_INVALID_PARAMETER`) dianggap sudah selesai.

**Ditunda ke sesi berikutnya:**

- Sesi 3: uninstall; `main.rs` + CLI yang memakai modul-modul ini; pemetaan `UpdateError` ke exit code (`Source`,
  `Signature`, `Format`, `ServerChanged` → 2, `Io` → 3, `Cancelled` → 4); `--apply --pid` memakai
  `AppProcess::wait_for_exit`; `LauncherLog::open` di awal proses.
- Sesi 3: dua proses launcher yang meng-update folder yang sama bersamaan belum dicegah. Rencananya mutex bernama
  per `app_id` di `main.rs` (satu launcher aktif per produk per user).
- Sesi 6: HTTPS sungguhan (SChannel + certificate store) dan kebijakan di HKLM sungguhan baru teruji di verifikasi
  end-to-end; test sesi ini memakai HTTP polos dan kebijakan di HKCU.

**Konsep Rust (sesi 2):**

| Konsep | Di mana |
| --- | --- |
| Trait sebagai kontrak (setara interface C#), dengan supertrait `Send + Sync` supaya bisa dipakai lintas thread | `src/source/release_source.rs:11` |
| Implementasi trait untuk beberapa tipe, termasuk tipe buatan test | `src/source/folder_source.rs:26`, `src/source/http_source.rs:74`, `tests/common/mod.rs:130` |
| Trait object (`Box<dyn Trait>`, `&dyn Trait`): implementasi dipilih saat runtime | `src/source/release_source.rs:32`, `src/source/release_source.rs:38`, `src/install/updater.rs:28` |
| Implementasi trait bawaan (`impl Read`) supaya tipe sendiri bisa dipakai di mana pun `Read` diterima | `src/source/http_source.rs:172` |
| Lifetime pada struct: `Updater<'a>` hanya meminjam sumber dan layout, jadi tidak boleh hidup lebih lama dari keduanya; `'_` saat nama lifetime tidak dibutuhkan | `src/install/updater.rs:26-30`, `src/install/updater.rs:60`, `src/install/updater.rs:230` |
| Beberapa blok `impl` untuk satu tipe di file berbeda (padanan *partial class*) dan visibilitas `pub(super)` | `src/install/repair.rs:9`, `src/install/updater.rs:232` |
| `Arc` (kepemilikan bersama lintas thread) + atomik (`AtomicU64::fetch_add`) + `Mutex` untuk data yang tidak bisa atomik | `src/install/update_progress.rs:12-19`, `src/install/update_progress.rs:127` |
| `static` global dengan `Mutex` (tanpa `unsafe`, karena `Mutex::new` adalah `const fn`) | `src/launcher_log.rs:10` |
| `impl From<E>` supaya `?` mengubah jenis error secara otomatis | `src/install/update_error.rs:68-84`, `src/install/verified_release.rs:24-28` |
| Fungsi yang mengembalikan closure (`impl FnOnce`) dan `move` untuk memindahkan data ke dalam closure | `src/install/update_error.rs:44-46` |
| Closure sebagai parameter (`impl FnOnce(A) -> A`) dan alias tipe (`type`) | `src/source/http_source.rs:202-204` |
| Generic dengan klausa `where` (`I: IntoIterator<Item = S>, S: AsRef<OsStr>`) | `src/install/app_process.rs:32-35` |
| Pattern matching pada nilai error (`Err(co::ERROR::FILE_NOT_FOUND) => ...`) | `src/config/registry_hive.rs:25`, `src/install/app_process.rs:82` |
| Enum dengan diskriminan eksplisit (`#[repr(u8)]`) untuk disimpan di atomik | `src/install/update_progress.rs:25` |
| serde dua arah (`Serialize` + `Deserialize`) dan `rename` | `src/install/install_layout.rs:32-38` |
| Field privat membatasi pembuatan struct: struct literal dan `..Default::default()` tidak bisa dipakai di luar modul | `src/config/launcher_config.rs:56-57`, `tests/config.rs:64-68` |
| `Option` combinator (`is_none_or`, `bool::then`) | `src/config/launcher_config.rs:111`, `src/config/launcher_config.rs:137` |
| `Drop` sebagai pembersih otomatis (padanan `IDisposable`, tapi tanpa `using`) | `tests/config.rs:35` |
| Thread (`thread::spawn` dengan closure `move`) | `tests/common/mod.rs:164` |
| Meminjam satu nilai sebagai slice (`std::slice::from_ref`) alih-alih meng-clone | `tests/updater.rs:53` |
| Modul bersama untuk test integrasi (`tests/common/mod.rs`) dan atribut level modul `#![allow(...)]` | `tests/common/mod.rs:5` |

### Sesi 3 — bagian 3 (sisanya) + 5 (2026-09-28)

**Hasil verifikasi** (lingkungan `vcvars64.bat` VS 2022 Enterprise):

- `cargo fmt --check` dan `cargo clippy --all-targets -- -D warnings`: bersih.
- `cargo test`: 29 test unit + 41 test integrasi lolos (`commands` 12 baru; `release_format_samples` 5, `updater` 10,
  `http_source` 5, `config` 7, `app_process` 2). Test unit baru: parser CLI (4), `DisplayVersion`,
  `InstallLayout::same_path`/`contains`, dan `LauncherLock`.
- `tests/commands.rs` menjalankan perintah CLI tanpa GUI (`quiet = true`) di folder, key registry
  (`HKCU\Software\LauncherTest-<pid>-<n>`), folder shortcut, dan nama mutex sementara. "App" rilis ujinya salinan
  `PING.EXE`, jadi menjalankan app benar-benar terjadi. Skenario:
  - install senyap: versi aktif, launcher root, konfigurasi, key, shortcut Start menu dengan AUMID `product::APP_ID`,
    shortcut desktop tanpa AUMID, dan semua field entri Apps & Features; `--no-start-menu`/`--no-desktop`;
  - install ditolak tanpa menulis apa pun: tanpa key, file private key, key lain (exit 2), sumber tidak ada (exit 2),
    folder berisi file lain; install ke folder kedua ditolak, install ulang ke folder yang sama boleh;
  - `--update`: tidak ada yang baru, lalu rilis baru dan `DisplayVersion` ikut berubah;
  - `--repair`: file rusak, file hilang, shortcut desktop dan entri hilang; saat ada proses dari folder instalasi
    → exit 5; kunci dipegang launcher lain → exit 3;
  - `--apply`: update tanpa bertanya, lalu memperbaiki instalasi yang rusak;
  - `--import`/`--list-keys`/`--remove-key`, termasuk key kebijakan yang tidak bisa dihapus;
  - uninstall: exit 5 saat app berjalan, lalu semua terhapus; shortcut bernama sama milik program lain dibiarkan;
    tahap kedua menolak folder yang bukan folder instalasi terdaftar.

  Setelah test tidak ada sisa di `HKCU\Software`, Start menu, desktop, maupun `HKCU\Software\Em Consignment Application`.
- **Uji manual exe release** dengan identitas produk sebenarnya. Registry `HKCU\Software\Em Consignment Application`
  diekspor dulu ke scratchpad, lalu dipulihkan dan dicek isinya sama. Sumber `doc/release-format-samples/valid` +
  `public-key.pem`, folder instalasi di scratchpad, semuanya lewat `Start-Process -Wait -PassThru`:
  - `--bogus` → exit 1 + usage; `--list-keys` → "No trusted keys."; `--import` private key → exit 1 dengan
    peringatan;
  - `--install --quiet ... --no-run` → exit 0; `--list-keys` → `a771b420e80ecb06  HKCU (user)`;
    `--update --quiet` → "up to date";
  - `Get-StartApps` → `Em Sample Application` dengan AppID `Em.SampleApplication`; entri Uninstall
    lengkap (`DisplayVersion 2026.9.27.1015`, `NoRepair 1`, `EstimatedSize`, `ModifyPath`, ...);
  - `<install>\launcher.exe --repair --quiet` setelah satu file dirusak dan satu dihapus → keduanya kembali (SHA-256
    sama dengan `expected.txt`);
  - `--uninstall --quiet` saat salinan `PING.EXE` berjalan dari folder instalasi → exit 5; setelah ditutup → exit 0,
    lalu folder, entri, `HKCU\<App>`, shortcut Start menu dan desktop, serta salinan `%TEMP%\launcher-uninstall-*`
    semuanya hilang.
- `cargo build --release`: `launcher.exe` 1.024.512 byte (sesi 2: 256.512, karena `main.rs` belum memakai modul
  apa pun). `grep -ri <nama-produk>` (tanpa `target/`): tetap hanya `product.toml` dan `CLAUDE.md` launcher.

**Keputusan yang diambil sendiri:**

- **Semua perintah ada di library** (`src/commands/`), satu tipe per kelompok: `StartCommand` (alur normal +
  `--apply`), `InstallCommand`, `UpdateCommand` (`--update` + `--repair`), `KeyCommand`, `UninstallCommand` (+ tahap
  kedua), dan `MaintenanceCommand`, dengan `CommandContext` bersama yang field-nya publik. `main.rs` hanya parse,
  dispatch, dan laporan kesalahan. Dengan begitu perintah bisa dites di `tests/commands.rs` terhadap registry,
  folder, dan mutex sementara.
- **`src/cli/`**: `CommandLine`/`Command`/`InstallOptions` (parser `lexopt`; argumen setelah `--` pertama dipisah
  sebelum `lexopt`), `ExitCode` (+ `of_update_error`), `CommandError` (`code` + pesan; pesan kosong = user sengaja
  batal, tidak ditampilkan), dan `Console`. Kesalahan ditulis ke log dan stderr, dan kalau tidak `--quiet` juga ke
  dialog.
- **`--import` punya dua arti**: tanpa perintah lain ia perintah sendiri (satu file); bersama `--install` ia opsi
  yang boleh berulang. Opsi yang tidak cocok dengan perintahnya ditolak (exit 1), begitu juga `--` selain untuk
  alur normal dan `--apply`.
- **Argumen internal tambahan `--from-app`** (sebelum `--`), untuk scenario c: app yang dibuka langsung menjalankan
  `..\launcher.exe --from-app -- <argumen>`, dan launcher mencatat "Launcher started by app redirect". Sesi 5 (sisi
  WPF) memakai argumen ini.
- **Dialog sementara = `MessageBox`** (`src/ui/prompts.rs`, `Prompts`): tawaran update (Yes = *Update now*),
  tawaran Repair, konfirmasi import (menampilkan `keyId`), konfirmasi uninstall, app masih berjalan (Retry/Cancel),
  "already installed" (Yes = Run, No = Maintenance, Cancel), info, dan error. Sesi 4 menggantinya dengan TaskDialog
  tanpa mengubah nama fungsinya.
- **Form setup dan jendela maintenance belum ada**: `--install` tanpa `--quiet`, `--maintenance`, dan alur tanpa
  argumen di mesin yang belum terpasang gagal dengan exit 1 dan pesan "not available in this build yet".
  Update/repair tanpa `--quiet` berjalan tanpa jendela progres (sesi 4).
- **`LauncherLock`** (`src/launcher_lock.rs`, mutex `Local\<APP_ID>.Launcher` lewat crate `windows`, karena
  `winsafe` 0.0.29 tidak punya mutex):
  - install, `--update`, `--repair`, dan `--uninstall` gagal dengan exit 3 kalau kunci dipegang launcher lain;
  - alur normal tidak menunggu, dan langsung menjalankan versi terpasang tanpa cek update;
  - tahap kedua uninstall menunggu kunci sampai 10 detik.
- **Shortcut seluruhnya lewat crate `windows`** (`IShellLinkW`, `IPersistFile`, `IPropertyStore`), bukan `winsafe`.
  `PropVariant` di `winsafe` hanya bisa membuat `BSTR`, sedangkan AppUserModelID wajib `VT_LPWSTR` (dibuat dengan
  `SHStrDupW`, dibebaskan `PropVariantClear`). `PKEY_AppUserModel_ID` ditulis sebagai konstanta sendiri, supaya fitur
  `Win32_Storage_EnhancedStorage` yang besar tidak perlu. Fitur `windows` baru: `Win32_Security`,
  `Win32_Storage_FileSystem`, `Win32_System_Com`, `Win32_System_Com_StructuredStorage`, `Win32_System_Console`,
  `Win32_System_Threading`, `Win32_System_Variant`, `Win32_UI_Shell`, `Win32_UI_Shell_PropertiesSystem`.
- **`ShellIntegration`** (`src/shell/shell_integration.rs`) mengelompokkan shortcut Start menu, shortcut desktop, dan
  `UninstallEntry`; letaknya berupa field supaya bisa dites. Shortcut yang tidak dipilih dicabut kalau target-nya
  launcher instalasi ini. `InstallDate` hanya ditulis sekali, dan `EstimatedSize` = ukuran seluruh folder instalasi
  (KB, dibulatkan ke atas).
- **Pengaman folder instalasi**, karena uninstall menghapus seluruh folder:
  - `--install` hanya menerima folder yang belum ada, kosong, atau sudah berisi instalasi launcher;
  - install ke folder lain saat produk sudah terpasang (`current.json` ada di folder lama) ditolak dengan
    "uninstall it first";
  - tahap kedua uninstall hanya menghapus folder yang sama dengan `InstallFolder` di konfigurasi, dan hanya kalau
    ia berjalan di luar folder itu.

  Akibatnya **scenario i di sesi 6** (instalasi senyap "ke folder kedua") harus dijalankan setelah instalasi pertama
  di-uninstall (atau sebelum scenario a), bukan berdampingan.
- **Urutan uninstall**: folder instalasi dihapus lebih dulu (10 percobaan, jeda 500 ms, untuk antivirus/indexer).
  Kalau gagal, shortcut, entri, dan registry dibiarkan supaya uninstall bisa diulang.
  - Launcher yang berjalan dari folder instalasi menyalin dirinya ke
    `%TEMP%\launcher-uninstall-<pid+waktu>\launcher.exe` (working directory di sana, `CREATE_NO_WINDOW`), lalu keluar
    dengan exit 0. Salinan itu menunggu launcher induk (maks. 60 detik), menghapus semuanya, lalu
    `cmd /d /c "ping -n 3 127.0.0.1 >nul & rmdir /s /q ..."` membuang folder salinannya.
  - Karena itu exit code `--uninstall --quiet` dari launcher root hanya mencerminkan tahap pertama (konfirmasi, app
    berjalan, kunci).
  - Launcher di luar folder instalasi menghapus langsung.
- **Alur normal**:
  - tidak ada konfigurasi → `InstallCommand` (= form setup, sesi 4);
  - launcher di luar folder instalasi saat launcher root ada → "already installed"; *Run* menjalankan launcher root
    dengan `-- <argumen>`;
  - instalasi rusak (tanpa `current.json`, manifest versi aktif hilang, exe app hilang, launcher root hilang) →
    tawaran Repair; ditolak → exit 4 dengan pesan;
  - cek update yang gagal (termasuk tanpa sumber/key) hanya dicatat di log; update yang gagal setelah user memilih
    *Update now* ditampilkan, lalu versi terpasang tetap dijalankan;
  - `--apply` melewati semua pertanyaan dan pemeriksaan "already installed", dan menunggu PID paling lama 5 menit
    (lewat dari itu exit 5).
- **Launcher root dijamin ada**: kalau rilis tidak membawa `launcher.exe` (rilis uji), exe yang sedang berjalan
  disalin ke `<install>\launcher.exe` setelah install/update/repair (`CommandContext::ensure_root_launcher`).
- **`--quiet` pada install tetap menjalankan app** kecuali `--no-run`, sesuai arti opsi di tabel CLI.
- **Key kebijakan**: import saat `AllowUserKeys = 0` tetap disimpan, dengan peringatan bahwa key itu tidak dipakai.
  `--remove-key` untuk key HKLM → exit 1 "set by the IT policy".
- **Console**: setelah `AttachConsole`, kalau handle stdout/stderr masih kosong, `CONOUT$` dibuka lalu dipasang
  dengan `SetStdHandle`; handle yang di-redirect dibiarkan. Karena exe bersubsistem `windows`, cmd/PowerShell tidak
  menunggunya, jadi exit code dibaca lewat `start /wait` atau `Start-Process -Wait -PassThru` (ditulis di help
  `Console`). `--help` tanpa console induk juga ditampilkan dalam dialog.
- **Panic**: hook mencatat ke `launcher.log` lewat `LauncherLog::panic` (memakai `try_lock`, supaya panic saat log
  sedang terkunci tidak membuat proses macet) dan ke stderr. Profil release tetap `panic = "abort"`.

**Ditunda ke sesi berikutnya:**

- Sesi 4: form setup, jendela progres (install/update/repair tanpa `--quiet` sekarang berjalan tanpa jendela),
  jendela maintenance, dan TaskDialog menggantikan `MessageBox` di `Prompts`.
- Sesi 5: `--remap-path-prefix` di `build-dist.ps1` kini benar-benar dibutuhkan, karena exe release sesi ini memuat
  path `C:\Users\...\.cargo\registry\...` (lokasi panic dari crate). Sisi WPF memakai `--from-app`.
- Sesi 6: keluaran CLI ke console sungguhan (tanpa redirect) belum bisa dibuktikan di lingkungan tool, yang hanya
  punya pipe, bukan console. Cek manual di terminal: `start /wait launcher.exe --list-keys`. Scenario i mengikuti
  catatan "Pengaman folder instalasi" di atas.

**Konsep Rust (sesi 3):**

| Konsep | Di mana |
| --- | --- |
| `unsafe` + FFI lewat crate `windows`: memanggil COM (`CoCreateInstance`, `cast()` = `QueryInterface`) dan API Win32 yang tidak dibungkus aman | `src/shell/shortcut.rs:57-84`, `src/launcher_lock.rs:31-33`, `src/cli/console.rs:28` |
| Menulis field `union` (PROPVARIANT) dan konstanta `const` bertipe struct dari crate lain | `src/shell/shortcut.rs:20`, `src/shell/shortcut.rs:74-75` |
| `Drop` untuk RAII: `CoUninitialize`, `ReleaseMutex` + `CloseHandle`, membunuh proses uji, menghapus key registry uji | `src/shell/shortcut.rs:153`, `src/launcher_lock.rs:51`, `tests/commands.rs:118`, `tests/commands.rs:135` |
| Urutan drop variabel lokal (kebalikan urutan deklarasi): `_com` dideklarasikan lebih dulu supaya dilepas paling akhir | `src/shell/shortcut.rs:90-91` |
| `drop(x)` untuk melepas lebih awal, dan binding yang hanya dipakai di satu cabang | `src/commands/start_command.rs:81-97`, `src/commands/uninstall_command.rs:41` |
| Associated const di `impl` bergeneric lifetime butuh `&'static str`, dan const dipakai sebagai pola `match` | `src/install/uninstall.rs:51-54`, `src/cli/command_line.rs:235`, `src/cli/command_line.rs:283` |
| `while let` + pola string (`Arg::Long("install")`) untuk parser, dan `matches!` | `src/cli/command_line.rs:147-149`, `src/cli/command_line.rs:251` |
| `impl From<A> for B` untuk tipe milik std (`std::process::ExitCode`), `#[repr(u8)]` + `as u8` | `src/cli/exit_code.rs:5`, `src/cli/exit_code.rs:44` |
| Mengumpulkan `Iterator<Item = Result<T, E>>` menjadi `Result<Vec<T>, E>` (berhenti di kesalahan pertama) | `src/commands/install_command.rs:68` |
| `let ... else` dengan pola tuple, dan let-chain dengan beberapa `let` | `src/commands/start_command.rs:127`, `src/commands/uninstall_command.rs:65` |
| Closure yang memakai `?` di dalamnya (mengembalikan `Option`) | `src/shell/uninstall_entry.rs:39` |
| Rekursi sederhana dan `Iterator::sum` | `src/shell/uninstall_entry.rs:117-129` |
| `Option::get_or_insert` dan `filter_map(Result::err)` untuk menyimpan kesalahan pertama tanpa berhenti | `src/shell/shell_integration.rs:82-84` |
| `Mutex::try_lock` dan `TryLockError::Poisoned(...).into_inner()` | `src/launcher_log.rs:72-75` |
| Panic hook (`std::panic::set_hook`) dan `main` yang mengembalikan `ExitCode` | `src/main.rs:13-15` |
| `std::thread::scope`: thread yang boleh meminjam variabel lokal, karena dijamin selesai sebelum scope berakhir | `src/launcher_lock.rs:71`, `tests/commands.rs:369` |
| Struct update syntax (`..fixture.options()`) | `tests/commands.rs:216` |
| Ekstensi khusus Windows di std (`CommandExt::raw_arg`, `creation_flags`, `IntoRawHandle`) | `src/install/uninstall.rs:67-72`, `src/cli/console.rs:61` |

### Sesi 4 — bagian 4 (2026-09-28)

**Hasil verifikasi** (lingkungan `vcvars64.bat` VS 2022 Enterprise):

- `cargo fmt --check` dan `cargo clippy --all-targets -- -D warnings`: bersih.
- `cargo test`: 30 test unit + 41 test integrasi lolos (test baru: `formats_sizes_and_dates` untuk `format_size`,
  `format_published`, dan `sentence`). Semua perintah di test tetap `quiet = true`, jadi tidak ada jendela.
- `cargo build --release`: `launcher.exe` 1.136.128 byte (sesi 3: 1.024.512). `grep -ri <nama-produk>` (tanpa `target/`): tetap
  hanya `product.toml` dan `CLAUDE.md` launcher.
- **Uji manual semua jendela** dengan exe release dan identitas produk sebenarnya, dikendalikan lewat UI Automation dan
  `TDM_CLICK_BUTTON`, dengan screenshot tiap jendela (di scratchpad sesi, tidak di-commit). Registry
  `HKCU\Software\Em Consignment Application` diekspor dulu dan dipulihkan dua kali (setelah tiap uninstall uji); hasil ekspor
  akhirnya identik byte demi byte dengan cadangan. Instalasi uji di scratchpad; shortcut dan entri Uninstall uji
  tidak tersisa.
  - **Form setup** dari folder "paket setup" (`launcher.exe` + `public-key.pem`): `.pem` di sebelah exe otomatis masuk
    daftar key, Install nonaktif selama sumber kosong, folder bawaan `%LocalAppData%\<ApplicationName>`. Setelah
    diisi: Install → halaman selesai (folder, petunjuk pin, Finish). Konfigurasi, key, versi aktif, dan log tertulis.
  - **Form dengan argumen** `--install --source ... --target ... --no-run`: form terisi, "Start ... after installing"
    tidak dicentang, key yang sama dengan key tersimpan tidak muncul dua kali. Sumber bertanda tangan key lain →
    pesan "The release is signed by a key that is not trusted." di form, tidak ada yang tertulis; Cancel → exit 4.
  - **Jendela progres** (`--update` tanpa `--quiet`) terhadap server HTTP uji yang dibatasi ~14 MB/s, rilis 180 MB:
    status, bar berdasarkan byte, nama file, "73.7 MB of 180.0 MB, 13.9 MB/s, about 7 s left"; selesai → pesan hasil.
    Cancel (tutup jendela) di tengah unduhan → exit 4 tanpa dialog, `current.json` tetap, `.staging-<id>` disimpan.
  - **Alur startup**: tawaran update (command link *Update now* / *Later*, ikon produk, waktu terbit dan ukuran
    unduhan). *Later* → versi terpasang dijalankan dan log "The user postponed release ...". *Update now* → jendela
    progres → app versi baru dijalankan, exit 0. Exe app dihapus → tawaran Repair → jendela "Repairing ..." → pesan
    hasil → app dijalankan.
  - **"Already installed"** (paket setup dibuka lagi): *Start* / *Maintenance* / Cancel; *Maintenance* membuka jendela
    maintenance.
  - **Jendela maintenance**: versi (`published 2026-09-27 10:15 UTC (release 23f11c7e0528)`), folder, sumber, key.
    Save sumber yang ditandatangani key lain → pertanyaan "The new release source cannot be used" (Enter = Cancel) →
    Cancel → "Not saved: ...", registry tetap. Remove key → konfirmasi. Uninstall → konfirmasi (Enter = Cancel) →
    Uninstall → pesan "was uninstalled"; folder, shortcut, entri Apps & Features, dan `HKCU\<App>` terhapus.
  - **Dialog file** (Browse/Import) terbuka dengan judul dan filter `*.pem` yang benar, tetapi memilih file di dalamnya
    tidak bisa diotomasi dari lingkungan tool; logika sesudahnya (`KeyCommand::read_file`, konfirmasi, simpan) sama
    dengan yang sudah teruji di CLI.

**Keputusan yang diambil sendiri:**

- **File `src/ui/`** mengikuti tipe utamanya: `setup_window.rs` (`SetupWindow` + `SetupOutcome`), `progress_window.rs`
  (`ProgressWindow`), `progress_panel.rs` (`ProgressPanel`, dipakai jendela progres dan halaman progres form setup),
  `maintenance_window.rs` (`MaintenanceWindow` + `MaintenanceAction`), `prompts.rs` (`Prompts`, sekarang TaskDialog),
  ditambah `window_style.rs` (`WindowStyle`: ikon, ukuran DPI, font judul, pembuat kontrol), `key_list_view.rs`
  (`KeyListView`), dan `file_dialogs.rs` (`FileDialogs`, `IFileOpenDialog`).
- **Form setup = satu jendela tiga halaman** (form, progres, selesai). `winsafe` hanya bisa membuat kontrol sebelum
  jendelanya dibuat, jadi semua kontrol dibuat di awal lalu disembunyikan/ditampilkan per halaman. Tombol kanan bawah
  ber-id `IDCANCEL` (Esc) dan berganti teks menjadi *Finish*; tombol Install ber-id `IDOK` (Enter).
- **`InstallCommand` dipecah** supaya GUI dan CLI memakai jalur yang sama: `InstallRequest` (pilihan user, dari argumen
  atau form), `InstallCommand::prepare` (semua pemeriksaan lokal, tanpa menulis), dan `InstallJob::execute` (cek rilis,
  pasang, simpan konfigurasi/key/shortcut; datanya milik sendiri supaya bisa pindah ke thread pekerja).
- **Kunci launcher di GUI** dipegang thread UI selama form setup terbuka: mutex Windows milik thread yang
  mengambilnya, jadi thread pekerja tidak mengambil kunci sendiri.
- **Form setup menampilkan fase "memeriksa"** dengan form tetap terlihat tapi nonaktif; begitu `UpdateProgress`
  melewati tahap `Checking`, jendela pindah ke halaman progres. Kegagalan apa pun (termasuk batal) kembali ke form
  dengan pesannya, dan Install berikutnya melanjutkan staging yang ada.
- **Browse folder instalasi**: folder yang dipilih tidak kosong dan bukan instalasi launcher → `\<ApplicationName>`
  ditambahkan di belakangnya.
- **"Start ... after installing"** dijalankan saat Finish (setelah jendela tertutup), bukan langsung setelah install.
- **`CommandContext::with_progress`**: senyap = langsung di thread ini; tidak senyap = `ProgressWindow`
  (`thread::scope`, jadi pekerjaannya boleh meminjam `Updater` milik pemanggil). Dipakai `--update`, `--repair`, dan
  *Update now* di alur startup. Pemeriksaan rilis `--update`/`--repair` ikut di dalam jendela progres (marquee);
  pemeriksaan di alur startup tetap tanpa jendela.
- **Batal dari jendela progres** = pilihan user: `--update`/`--repair` keluar dengan exit 4 tanpa dialog (log
  "Cancelled by the user"); di alur startup versi terpasang tetap dijalankan tanpa pesan kesalahan.
- **`--update` tanpa `--quiet`** kini menampilkan "is up to date" sebagai pesan (sebelumnya hanya console/log), supaya
  user yang membukanya tanpa console mendapat jawaban.
- **Jendela maintenance**: Save sumber memeriksa rilis di sumber baru di thread pekerja; kalau gagal, user bisa tetap
  menyimpannya ("Save anyway", bawaan Cancel). Import dan Remove key langsung tersimpan; Remove hanya untuk key milik
  user. Repair dan Uninstall menutup jendela, lalu `MaintenanceCommand` menjalankan `UpdateCommand::repair` /
  `UninstallCommand::run` seperti CLI. `--maintenance --quiet` ditolak (exit 1).
- **TaskDialog**: pemiliknya jendela aktif thread ini (modal ke jendela launcher yang terbuka). Tawaran update dan
  "already installed" memakai command link dengan ikon produk; tawaran update tanpa tombol Cancel (Esc/X = *Later*).
  Pertanyaan berisiko (`Prompts::ask`: uninstall, remove key, save anyway) memakai Cancel sebagai tombol Enter;
  tawaran Repair memakai Repair.
- **Teks untuk user**: `format_published` (`2026-09-28 11:00 UTC`), `format_size`, bentuk tunggal "1 file", dan
  `sentence` (huruf besar + titik) untuk pesan kesalahan di form dan dialog. Pesan CLI ikut berubah (tidak ada test
  yang bergantung padanya).
- **Font**: font UI `winsafe` (font sistem, Segoe UI 9 pt di Windows 10/11); judul halaman memakai font pesan sistem
  4/3 lebih besar, SemiBold (`WindowStyle::apply_heading`). Background `COLOR_BTNFACE`, tanpa dark mode (sesuai plan).
- **Exe test butuh comctl32 v6**: `TaskDialogIndirect` hanya ada di versi itu, dan exe test (yang ikut me-link kode GUI)
  tidak punya manifest, sehingga gagal dimuat dengan `STATUS_ORDINAL_NOT_FOUND`. `build.rs` menambahkan
  `/MANIFEST:EMBED` dan `/MANIFESTDEPENDENCY` Common-Controls 6 lewat `cargo:rustc-link-arg-tests`.
- **Koreksi sesi 3: penghapusan folder salinan uninstall.** Jeda tetap `ping -n 3` tidak cukup saat tahap kedua
  berjalan tanpa `--quiet`: pesan "was uninstalled" membuat exe salinan masih berjalan, sehingga `rmdir` gagal dan
  folder `%TEMP%\launcher-uninstall-*` tertinggal. `schedule_removal_of` kini mencoba `rmdir` kira-kira setiap detik
  sampai berhasil (paling lama sekitar 10 menit). Diuji ulang: folder bertahan selama pesan terbuka 8 detik, lalu
  terhapus beberapa detik setelah pesan ditutup.

**Ditunda ke sesi berikutnya:**

- Sesi 5: sisi WPF, `build-dist.ps1`, `dist/launcher/launcher.exe`, dan dokumentasi lengkap (tidak berubah).
- Sesi 6: memilih file/folder lewat dialog Browse dan Import dicoba manual (lihat hasil verifikasi). Scenario a, d,
  dan f meminta screenshot; jendelanya sudah terbukti bisa ditangkap dengan `PrintWindow`.
- Belum ditangani: `winsafe` mengukur kontrol dengan DPI sistem saat start. Jendela yang digeser ke monitor dengan DPI
  lain tidak menyesuaikan ukuran kontrolnya (Windows tidak melakukannya untuk PerMonitorV2 tanpa `WM_DPICHANGED`).
- Catatan uji: UI Automation bisa mengklik tombol jendela yang sedang diblok dialog modal (user sungguhan tidak bisa),
  sehingga dua dialog sempat bertumpuk saat uji. Ini artefak alat uji, bukan perilaku yang perlu dicegah.

**Konsep Rust (sesi 4):**

| Konsep | Di mana |
| --- | --- |
| `std::thread::scope` dengan jendela yang berjalan di thread utama sementara pekerja meminjam data pemanggil | `src/ui/progress_window.rs:34` |
| Guard `Drop` yang tetap jalan saat panic (menandai pekerjaan selesai), dan `panic::resume_unwind` untuk meneruskan panic pekerja | `src/ui/progress_window.rs:39`, `src/ui/progress_window.rs:127-133`, `src/ui/progress_window.rs:48` |
| Generic dengan batas `Send` (`R: Send`, `impl FnOnce() -> R + Send`): tipe dan closure yang boleh berpindah thread | `src/commands/command_context.rs:190`, `src/ui/progress_window.rs:32` |
| `thread::spawn` dengan closure `move` berisi data milik sendiri (`'static`), lalu `JoinHandle::is_finished` untuk polling tanpa memblokir | `src/ui/setup_window.rs:523`, `src/ui/setup_window.rs:546` |
| Struct yang semua datanya milik sendiri supaya bisa dikirim ke thread lain | `src/commands/install_command.rs:44` |
| `Rc` + `Cell`/`RefCell`: state bersama yang bisa diubah di satu thread (interior mutability), tanpa kunci | `src/ui/setup_window.rs:88-99`, `src/ui/progress_panel.rs:28` |
| `Cell::replace` untuk membaca sekaligus mengganti nilai `Copy` | `src/ui/progress_panel.rs:103`, `src/ui/progress_panel.rs:109` |
| Closure `'static` untuk event: setiap handler memegang klon struct jendela (`let this = self.clone(); move \|\| ...`) | `src/ui/setup_window.rs:229-290` |
| `drop(guard)` untuk melepas pinjaman `RefCell` sebelum membuka dialog modal (yang menjalankan handler lain) | `src/ui/maintenance_window.rs:346`, `src/ui/maintenance_window.rs:351` |
| `RefCell::take` untuk mengambil hasil keluar dari state bersama | `src/ui/setup_window.rs:159` |
| `thread_local!` + `OnceCell::get_or_init`: nilai per thread yang dibuat sekali saat pertama dipakai | `src/ui/window_style.rs:5-8`, `src/ui/window_style.rs:122` |
| Builder yang memakan `self` (`fn instruction(mut self, ...) -> Self`) dan struct dengan lifetime yang meminjam teks | `src/ui/prompts.rs:188`, `src/ui/prompts.rs:211`, `src/ui/prompts.rs:241` |
| Parameter `&(impl Trait + 'static)` (generic tanpa nama tipe) | `src/ui/window_style.rs:39` |
| Alias tipe untuk tuple yang panjang | `src/ui/maintenance_window.rs:63` |
| Pola rentang di `match` (`0..60`) | `src/ui/progress_panel.rs:145` |
| Pola array karakter di `ends_with(['.', '!', '?'])` | `src/ui/prompts.rs:173` |
| Turbofish pada `Ok::<_, E>` supaya tipe error closure bisa ditebak compiler | `src/commands/update_command.rs:36` |
| Fungsi biasa sebagai argumen `map_err` (`.map_err(from_update_error)`) | `src/commands/update_command.rs:105` |
| `build.rs` yang memberi argumen linker hanya untuk target test (`cargo:rustc-link-arg-tests`) | `build.rs:59-63` |

### Sesi 5 — bagian 6 + 7 + `build-dist.ps1` (2026-09-28)

Sesi ini dijalankan di **mesin lain** (`ITS2`, VS 2022 Community + VS 18 Community) yang semula tidak punya Rust. User
memasang `rustup` sendiri. Toolchain dari `rust-toolchain.toml` sama dengan sesi 1–4 (cargo 1.98.1).

**Hasil verifikasi:**

- `cargo fmt --check` dan `cargo clippy --all-targets -- -D warnings`: bersih. `cargo test`: 31 test unit (baru:
  `treats_short_names_as_their_long_names`) + 41 test integrasi lolos, tanpa sisa key di `HKCU\Software`.
- `build-dist.ps1` menghasilkan `dist/launcher/launcher.exe` **1.135.616 byte**, SHA-256
  `70a281334bbc43373d158beb16142dda23b1e76d8cac0e5001259217b7f35773`. Dua kali build dari source yang sama memberi
  hash yang sama. Di exe tidak ada `C:\Users\...`, nama user, atau `<repo-privat>`; path crate menjadi
  `cargo\registry\...`. Di mesin ini `vswhere -requires VC.Tools.x86.x64 -latest` memilih VS 18 Community, yang di sini
  punya library x64, jadi build tetap jalan.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `dotnet build src/backend/Em.Api.slnx`: 0 warning, 0 error.
  `launcher.exe` ada di root `bin\Debug\net10.0-windows\`.
- Uji singkat `LauncherIntegration` dengan build Debug di scratchpad (`inst\app-test\` + `release.json`, dengan salinan
  `PING.EXE` sebagai `inst\launcher.exe`): tanpa env kontrak, app menjalankan `..\launcher.exe` lalu keluar dengan
  exit 0; dengan `LAUNCHER_PATH`/`LAUNCHER_APP_ID`, app tetap berjalan dan jendela utama terbuka. Efek AUMID dan pin
  baru bisa dibuktikan di sesi 6 (scenario b dan c).
- `grep -ri <nama-produk>` (tanpa `target/`): tetap hanya `product.toml` dan `CLAUDE.md` launcher.

**Keputusan yang diambil sendiri:**

- **Perbaikan bug: nama pendek 8.3.** Di mesin ini `%TEMP%` berbentuk `C:\Users\USER~1.EXT\...` (nama user bertitik),
  dan dua test `commands` gagal. Shortcut mengembalikan target dalam bentuk panjang, dan daftar proses juga, sehingga
  `same_path`/`contains` yang hanya membandingkan teks menganggap keduanya beda. Akibatnya di produk: uninstall tidak
  menghapus shortcut, dan deteksi app yang berjalan (exit 5) bisa terlewat kalau folder instalasi ditulis dalam bentuk
  8.3. `path_key` kini mengurai bagian path terpanjang yang ada di disk lewat `GetLongPathNameW` (hanya kalau path
  mengandung `~`, jadi path biasa tidak menyentuh disk). Bagian yang belum ada dibiarkan apa adanya. Test yang
  membandingkan target shortcut dengan `assert_eq!` diganti `same_path`.
- **`build-dist.ps1` memulihkan lingkungan pemanggil.** Lingkungan `vcvars64.bat` dimuat ke proses PowerShell pemanggil,
  dan `Platform=x64` yang tertinggal membuat `dotnet build` kedua `.slnx` gagal (`MSB4126 Debug|x64`). Script kini
  menyimpan lingkungan lalu memulihkannya di `finally`, dan menambahkan `~\.cargo\bin` ke PATH kalau `cargo` belum
  dikenali (shell yang dibuka sebelum rustup dipasang).
- **Remap path**: `--remap-path-prefix` untuk folder project (`launcher`) dan `CARGO_HOME` (`cargo`) lewat
  `cargo build --release --config "target.x86_64-pc-windows-msvc.rustflags=[...]"` (string literal TOML `'...'`,
  supaya backslash tidak di-escape). Array ini digabung dengan `+crt-static` dari `.cargo/config.toml`.
- **`LauncherIntegration`** (`Em.Ui.Wpf.Core/Core/LauncherIntegration.cs`) adalah kelas `static`. Isinya:
  - `Initialize(args)` bersifat `internal` dan dipanggil di baris pertama `BuildApp`;
  - `AttachToWindow(window, displayName)` bersifat `public` dan dipanggil constructor `TabbedMainWindow`, jadi jendela
    tear-off juga mendapatkannya;
  - konstanta `PathVariable`, `AppIdVariable`, `ReleaseMarkerFileName`, serta properti `IsManaged`, `LauncherPath`,
    dan `AppUserModelId`.

  AUMID proses hanya dipasang kalau kedua env ada. `IsManaged` = `release.json` ada di `AppContext.BaseDirectory`.
  Pengalihan memakai `ProcessStartInfo.ArgumentList` (quoting argumen sama dengan parser argumen Rust). Kegagalan
  menjalankan launcher membuat app berjalan biasa. `DetachedWindow` tidak diberi properti jendela: ia memakai AUMID
  proses, dan pin dari jendela itu tetap mengikuti grup yang sama.
- **Properti jendela** dipasang saat `SourceInitialized` dengan urutan RelaunchCommand, RelaunchDisplayNameResource,
  RelaunchIconResource, lalu **ID paling akhir** (dokumentasi `System.AppUserModel.ID`: properti lain dipasang sebelum
  ID). Semuanya **dihapus (VT_EMPTY) saat `WM_DESTROY`** lewat hook `HwndSource`, karena dokumentasi
  `SHGetPropertyStoreForWindow` mewajibkan properti dibuang sebelum jendela ditutup. `IPropertyStore` dan `PROPVARIANT`
  didefinisikan sendiri secara minimal, tanpa paket interop tambahan.
- **Release Manager**: `launcher.exe` tidak tercantum di `deps.json`, jadi masuk kelompok *Extra libraries* (hanya
  pengelompokan tampilan). Path-nya sah, dan file itu ikut `release.json` seperti file lain. Tidak ada perubahan di
  `Release/`.
- **Item `Content` tanpa `Condition`**: `dist/launcher/launcher.exe` yang hilang harus menggagalkan build (`MSB3030`),
  bukan diam-diam menghasilkan rilis tanpa launcher.
- **Tidak dicerminkan ke MAUI**, sesuai plan (update management khusus WPF, T1).
- **Dokumentasi**: `CLAUDE.md` launcher ditulis ulang (peran, netral produk + `LAUNCHER_PRODUCT`, build dan
  `build-dist.ps1`, layout instalasi, registry, kontrak env, CLI + exit code, susunan source, gaya kode, aturan
  `dist/`); satu baris `dist/` di root `CLAUDE.md`; subbagian "Launcher integration" di `src/frontend/CLAUDE.md`; dan
  langkah 2 bagian 8 laporan diskusi ditandai beserta perubahannya.

**Ditunda ke sesi berikutnya:**

- Sesi 6: seluruh verifikasi end-to-end. AUMID/pin (scenario b) dan pengalihan dengan launcher sungguhan (scenario c)
  baru terbukti di sana. Sesi 6 juga memindahkan plan ke `plan/executed/`, jadi tautan di laporan diskusi dan di
  `CLAUDE.md` launcher perlu diperbarui ke `plan/executed/launcher-rust-tahap2.md`.
- Belum ditangani (tidak berubah dari sesi 4): ukuran kontrol saat jendela pindah ke monitor dengan DPI lain.

**Konsep Rust (sesi 5):**

| Konsep | Di mana |
| --- | --- |
| `unsafe` untuk FFI dengan buffer milik Rust (`Some(&mut buffer)` sebagai slice keluaran), lalu `Vec::resize` saat buffer kurang | `src/install/install_layout.rs:262-275` |
| `OsString::from_wide` (trait ekstensi khusus Windows `OsStringExt`) untuk mengubah UTF-16 dari API Win32 menjadi path | `src/install/install_layout.rs:272` |
| Meminjam bagian path (`Path::parent`/`file_name` mengembalikan pinjaman dari `path`, bukan salinan) dan menyusunnya kembali dengan `extend` + `rev()` | `src/install/install_layout.rs:240-258` |
| `match` pada tuple dua `Option` | `src/install/install_layout.rs:251` |
| `let ... else` untuk keluar lebih awal dari test, dan `bool::then_some` | `src/install/install_layout.rs:317`, `src/install/install_layout.rs:336` |
| Fungsi pembantu yang hanya ada di `#[cfg(test)] mod tests`, dengan `use` lokal di dalam fungsi | `src/install/install_layout.rs:331-337` |
| Konfigurasi cargo dari command line (`--config`), digabung dengan `.cargo/config.toml` | `build-dist.ps1:48-56` |

### Sesi 6 — bagian 8, verifikasi end-to-end (2026-09-28)

Dijalankan di mesin `ITS2` (sama dengan sesi 5), tanpa hak admin (Medium Mandatory Level). Semua instalasi uji di
scratchpad sesi, bukan folder bawaan. Screenshot jendela launcher ada di
[launcher-rust-tahap2/](launcher-rust-tahap2/). Screenshot yang memuat data pribadi (layar login app, halaman Settings
dengan akun Windows) tidak ikut di-commit.

**Hasil setiap langkah:**

1. **Build**: `cargo fmt --check` dan `cargo clippy --all-targets -- -D warnings` bersih. `cargo test` lolos: 31 test unit
   + 41 test integrasi. `dotnet build` kedua `.slnx`: 0 warning, 0 error.
2. **`build-dist.ps1`**: `dist/launcher/launcher.exe` **1.135.616 byte**, SHA-256
   `70a281334bbc43373d158beb16142dda23b1e76d8cac0e5001259217b7f35773`. Hasilnya **identik dengan commit sesi 5**, jadi
   git tetap bersih. Ikon produk tampil (diekstrak dari exe dengan `ExtractAssociatedIcon`).
3. **Rilis uji nyata**:
   - publish host memakai argumen Prepare (`ReleaseBuilder.BuildPublishArguments`) ke scratchpad: 654 file, dan
     `launcher.exe` ada di root publish (item `Content` sesi 5 bekerja);
   - program konsol sementara (mereferensikan `Em.Ui.Wpf.Core`) membuat dua key P-256 sementara, `test`
     (`3d665aad3d788fff`) dan `other` (`c7d9962c4196922d`), lalu mengekspor public key ke `.pem`;
   - `LocalPublish` → `ReleaseComparer` → `ReleaseSync` lewat **`CdnReleaseTarget`** ke `Em.Api` lokal
     (`http://localhost:5132`, debug token, folder CDN `wpf-release-e2e`). Klien `ICdnServices` di program itu memakai
     `ApiClient` langsung, tanpa `EmApp`.
4. **Pengaman mesin**: `HKCU\Software\Em Consignment Application` diekspor sebelum uji. Sebelum mulai, tidak ada entri Uninstall
   maupun shortcut produk. Setelah uji, registry diimpor kembali dan hasil ekspornya **identik byte demi byte** dengan
   cadangan.
5. Skenario:
   - **a. Setup GUI — lolos.** Paket setup berisi `launcher.exe` + `test.pem`. Key otomatis masuk daftar, dan Install
     nonaktif selama sumber kosong. Setelah sumber CDN dan folder diisi: Install → halaman progres
     ("54.3 MB of 409.7 MB, 25.0 MB/s, about 14 s left") → halaman selesai dengan petunjuk pin → Finish → app berjalan
     dari `app-36f48cc72bb4` (layar Sign in). Screenshot 01–04.
   - **b. Start menu dan Apps & Features — lolos.** `Get-StartApps` menampilkan `Em Sample Application` dengan
     AppID `Em.SampleApplication`. Entri Uninstall lengkap: `DisplayIcon` `...\launcher.exe,0`, `Publisher`,
     `DisplayVersion 2026.9.28.0328`, `InstallLocation`, `InstallDate 20260928`, `EstimatedSize`, `UninstallString`,
     `QuietUninstallString`, `ModifyPath` (`--maintenance`), `NoRepair 1`. **Tambahan yang tertunda dari sesi 5:**
     properti jendela WPF dibaca lewat `SHGetPropertyStoreForWindow`, dan hasilnya benar: `ID` =
     `Em.SampleApplication`, `RelaunchCommand` = `"<install>\launcher.exe"`, `RelaunchDisplayNameResource` =
     `Em Sample Application`, `RelaunchIconResource` = `<install>\launcher.exe,0`.
   - **c. Pengalihan — lolos.** `app-<id>\Em.Ui.Wpf.exe` dijalankan langsung, tanpa env. Proses itu keluar dengan
     exit 0. Log mencatat "Launcher started by app redirect", lalu launcher menjalankan app (pid baru). Setelah stabil,
     hanya ada **satu** proses app, dan jendelanya membawa AUMID. Karena AUMID hanya dipasang kalau env kontrak ada,
     env sampai ke app, dan tidak terjadi loop.
   - **d. Update — lolos.** Satu modul diubah sementara (file penanda di `Em.Sample`, dihapus lagi setelah publish).
     Publish kedua berbeda di **4 file**: `Em.Sample.dll/.pdb` dan `Em.Ui.Wpf.dll/.pdb` (host ikut berubah karena
     mereferensikan modul). Sync: 650 same, 4 changed. Launcher menampilkan tawaran update ("Download: 187.9 KB",
     screenshot 05). *Later* → versi lama dijalankan, dan log mencatat "The user postponed release d98adc7af74b".
     `--update --quiet` (app lama masih terbuka) → exit 0, **4 file diunduh (192.380 byte), 650 dipakai ulang**
     (dihitung dari log launcher), dan `current.json` pindah ke `d98adc7af74b`. Folder lama dilewati ("Access is
     denied", app lama masih berjalan), lalu terhapus di run berikutnya setelah app ditutup ("Removed ...
     app-36f48cc72bb4"), dan versi baru dijalankan.
   - **e. Offline — lolos.** `Em.Api` dihentikan. Launcher keluar setelah sekitar 3,1 detik (timeout koneksi 3 detik)
     tanpa jendela apa pun, log `[WARN] No usable release ... timeout: connect`, dan versi terpasang dijalankan.
   - **f. Repair — lolos.** Satu byte `DevExpress.Data.v25.2.dll` diubah dan `Em.Libs.dll` dihapus. `--repair --quiet`
     → exit 0, "2 files downloaded (5.7 MB), 652 files intact", dan SHA-256 keduanya sama dengan aslinya.
     `--maintenance` terbuka (screenshot 06).
   - **g. Key — lolos.** `--import` file private key → exit 1 dengan peringatan. `--import other.pem --quiet`,
     `--list-keys` (dua key, HKCU), `--remove-key c7d9962c4196922d`, lalu `--list-keys` (satu key). Rilis yang sama
     ditandatangani ulang dengan key `other` (Sync tanpa perubahan): `--update --quiet` → exit 2 "the release is signed
     by a key that is not trusted", jumlah item di folder instalasi tetap sama dan `current.json` tidak berubah;
     startup normal hanya menulis `[WARN]` ke log lalu menjalankan versi terpasang.
   - **h. HKLM — tidak diuji** (tanpa hak admin). Logika kebijakan tetap tercakup test `config` (kebijakan di HKCU)
     dan test `commands` (key kebijakan tidak bisa dihapus).
   - **j. Uninstall — lolos** (dijalankan sebelum i, lihat catatan "Pengaman folder instalasi" sesi 3).
     `--uninstall --quiet` saat app berjalan → exit 5 dengan nama proses, dan tidak ada yang terhapus. Setelah app
     ditutup → exit 0. Folder instalasi, shortcut Start menu dan desktop, entri Uninstall, `HKCU\<App>`, dan salinan
     `%TEMP%\launcher-uninstall-*` semuanya hilang, dan `Get-StartApps` tidak lagi menampilkan produk.
   - **i. Instalasi senyap — lolos.** `--install --source <cdn> --target <folder kedua> --import test.pem --quiet` dari
     paket setup → exit 0, "654 files downloaded (409.7 MB)", dan app dijalankan. Launcher root sama byte demi byte
     dengan `dist/launcher/launcher.exe`.
   - **Uninstall dari Settings → Installed apps — lolos** (instalasi scenario i). Entri tampil dengan versi
     `2026.9.28.0335` dan publisher. Menu entri berisi **Modify** dan **Uninstall** (tanpa Repair). **Modify** membuka
     jendela maintenance launcher. **Uninstall** → konfirmasi Settings → konfirmasi launcher (screenshot 07) → "was
     uninstalled" (screenshot 08), dan semuanya terhapus seperti di scenario j.
6. **Pembersihan**: folder `wpf-release-e2e` dihapus dari CDN lewat `PostMeta_CdnDelete` (di disk juga tidak ada lagi).
   Key sementara dan instalasi uji hanya ada di scratchpad. Tidak ada sisa `HKCU\Software\LauncherTest-*`, shortcut, atau
   entri Uninstall. Registry sudah dipulihkan.
7. Plan dipindah ke `plan/executed/`. Tautan di laporan diskusi dan di `CLAUDE.md` launcher ikut diperbarui.

**Keputusan yang diambil sendiri:**

- **`ActionRateLimit` dinaikkan sementara ke 1000** (atas izin user) selama uji. Nilai 300 menolak Sync penuh ke CDN
  di tengah jalan ("Too many requests"), karena setiap file adalah satu request upload. Nilainya sudah dikembalikan ke
  300, dan `Program.cs` tidak ikut berubah di commit ini.
- **Satu modul diubah lewat file penanda sementara** (bukan mengedit kode yang ada), supaya pengembaliannya cukup dengan
  menghapus file itu. Karena build deterministik, perbedaannya bisa dihitung persis.
- **Uninstall dari Settings** diotomasi lewat UI Automation. Sebelum memilih Uninstall, **Modify** diklik dulu untuk
  memastikan menu itu memang milik entri produk ini (posisi menu di layar tidak menunjukkannya).
- **Exit code install senyap dibaca terpisah**: `Start-Process -Wait` di PowerShell 7 ikut menunggu proses turunan, yaitu
  app yang dijalankan launcher, sehingga perintahnya baru kembali setelah app ditutup. Launcher sendiri sudah keluar.
  Ini perilaku alat uji, bukan launcher. Untuk skrip IT yang butuh exit code segera, pakai `--no-run` atau
  `start /wait`.

**Temuan untuk ditindaklanjuti (di luar cakupan plan ini, tidak diubah):**

- **Release Manager → CDN dan rate limit.** Sync pertama rilis penuh (654 file) ke CDN melebihi `ActionRateLimit = 300`
  dan gagal di tengah jalan. Sync yang diulang melanjutkan dari sisa file, karena `release.json` baru ditulis di akhir,
  jadi tujuan tidak rusak. Tetapi dengan batas bawaan, Release Manager butuh beberapa kali Sync (dengan jeda) untuk
  rilis pertama. Tahap 1 tidak menemukan ini karena Sync ke CDN hanya diuji dengan beberapa file. Pilihannya antara lain
  mengecualikan upload CDN dari batas itu, atau membuat Sync menunggu dan mencoba lagi saat menerima 429. Keputusannya
  diserahkan ke user.

**Ditunda / belum diuji:**

- HKLM sungguhan (scenario h), karena tidak ada hak admin.
- HTTPS sungguhan (SChannel + certificate store Windows). CDN lokal hanya HTTP.
- Memilih file/folder di dialog Browse dan Import (dialog Windows tidak diotomasi; logikanya teruji lewat CLI).
- Keluaran CLI ke console sungguhan (tanpa redirect). Di sesi ini keluaran dibaca lewat redirect. Cek manual:
  `start /wait launcher.exe --list-keys` di cmd.
- Ukuran kontrol saat jendela pindah ke monitor dengan DPI lain (tidak berubah sejak sesi 4).

**Konsep Rust (ringkasan akhir, dari seluruh sesi):**

| Konsep | Di mana |
| --- | --- |
| Ownership dan borrowing: getter mengembalikan pinjaman (`&str`, `&[ReleaseFile]`) alih-alih salinan, jadi pemanggil tidak bisa mengubah atau menyimpan data melebihi umur pemiliknya | `src/format/release_manifest.rs:78-101` |
| Lifetime pada struct: `Updater<'a>` hanya meminjam sumber dan layout, jadi compiler menjamin keduanya hidup lebih lama | `src/install/updater.rs:26-30`, `src/install/updater.rs:64` |
| `Result` dan operator `?`: setiap langkah verifikasi berhenti di kesalahan pertama, dan kesalahannya diteruskan ke pemanggil | `src/install/verified_release.rs:22-28` |
| Tipe error sendiri (`impl Display` + `impl Error`) dan `impl From<...>`, yang membuat `?` bisa mengubah jenis error secara otomatis | `src/install/update_error.rs:66-68`, `src/cli/command_error.rs:58-60`, `src/format/release_signature.rs:82` |
| Trait sebagai kontrak (`ReleaseSource`, supertrait `Send + Sync`) dengan dua implementasi, dipilih saat runtime lewat `&dyn ReleaseSource` | `src/source/release_source.rs:11`, `src/source/http_source.rs:74`, `src/source/folder_source.rs:26`, `src/install/updater.rs:28` |
| `Arc` + atomik: progres dibagi antara thread pekerja dan thread UI tanpa mutex, dan pembatalan lewat `AtomicBool` | `src/install/update_progress.rs:2-19`, `src/install/update_progress.rs:53-69` |
| Thread: `thread::scope` (pekerja boleh meminjam data milik pemanggil) dan `thread::spawn` dengan closure `move` (data dipindah ke thread) | `src/ui/progress_window.rs:34-37`, `src/ui/setup_window.rs:523` |
| `unsafe` dan FFI lewat crate `windows`: pemanggilan Win32 dibungkus blok `unsafe` sekecil mungkin, dan hasilnya segera dijadikan tipe aman | `src/launcher_lock.rs:31-38`, `src/cli/console.rs:28-61`, `src/install/install_layout.rs:267` |
| `build.rs`: membaca `product.toml`, menulis konstanta ke `OUT_DIR`, dan menanam resource; `cargo:rerun-if-*` membatasi kapan skrip ini dijalankan ulang | `build.rs:26-53`, `src/product.rs:5` |
| Modul dan visibilitas: `pub mod` di library, `mod.rs` berisi `pub use`, `pub(crate)` dan `pub(super)` untuk item yang hanya dipakai di dalam crate atau oleh modul saudara | `src/lib.rs:4-14`, `src/format/release_public_key.rs:123`, `src/install/updater.rs:27-30` |
| serde derive: `Deserialize` untuk format rilis (field tak dikenal diabaikan) dan `Serialize` + `Deserialize` untuk `current.json` | `src/format/release_file.rs:6`, `src/format/release_manifest.rs:18`, `src/install/install_layout.rs:36` |
| Test di Rust: test unit di `#[cfg(test)] mod tests` (bisa mengakses item privat) dan test integrasi di `tests/` (hanya API publik, dengan helper bersama di `tests/common/`) | `src/format/release_manifest.rs:159`, `tests/updater.rs:82`, `tests/common/mod.rs` |

## Di luar cakupan

- Cek dan unduhan di dalam app, ikon animasi, notifikasi, dan restart dari app (tahap 3). `--apply --pid`
  hanya disiapkan.
- Pin taskbar otomatis (diblokir Windows) dan Repair bawaan Windows (khusus MSI).
- Proxy sistem, unduhan paralel, dark mode, dan Mica.
- Tanda tangan Authenticode untuk `launcher.exe`.
- Instalasi per mesin (Program Files / HKLM, butuh admin).
- MAUI.
- Perubahan format rilis. `doc/release-format.md` tidak berubah. Kalau eksekusi menemukan hal yang
  menuntut perubahan format, berhenti di bagian itu dan catat di laporan.
