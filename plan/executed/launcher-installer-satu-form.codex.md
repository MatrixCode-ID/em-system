# Launcher: satu form installer, progres Finishing dan hapus, pemisahan cab

Tanggal: 2026-10-23. Status: selesai implementasi dan verifikasi console. Eksekutor: Codex.

Lingkup kode: `src/frontend/Launcher` (Rust). Baca `CLAUDE.md` root dan
`src/frontend/Launcher/CLAUDE.md` sebelum mulai. Komentar kode, teks UI, pesan
error, dan log ditulis dalam bahasa Inggris; plan dan `CLAUDE.md` tetap Bahasa
Indonesia. Launcher tetap netral produk.

## Latar belakang

- Tahap **Finishing** (`Updater::prepare`/`install`/`activate`, `repair.rs`)
  dimulai dengan `progress.begin(UpdatePhase::Finishing, 0)`, sehingga
  `ProgressPanel` hanya menampilkan marquee "Finishing...". Padahal tahap ini
  menulis `release.json`/`.sig`, me-rename `.staging-<id>` menjadi `app-<id>`
  (dan sebelumnya menghapus sisa `app-<id>`), menulis `current.json`, mengganti
  launcher root (hash dua exe), lalu menghapus semua versi lama dengan
  `remove_dir_all`. Bagian hapus bisa lama tanpa tanda apa pun.
- **Uninstall** (`Uninstaller::remove`) dan **Clean installation**
  (`SetupWindow::remove_installation`, berjalan di thread UI sehingga form
  membeku) juga menghapus folder tanpa progres.
- Launcher punya tiga jendela: form setup (`setup_window.rs`, halaman Form,
  Checking, Installing, Done), jendela maintenance (`maintenance_window.rs`,
  target Modify), dan jendela progress kecil (`progress_window.rs`). Tombol
  **Maintenance** di form menutup form, melepas lock, lalu membuka jendela
  maintenance.
- Tombol **Download to cab** dan CLI `--download-cab` sekaligus berfungsi
  sebagai update cab (cab lama valid dipakai sebagai donor). Pengguna meminta
  keduanya dipisahkan.

## Keputusan final (pengguna, 2026-10-23)

### Progres

1. **Rincian progres Finishing: per langkah + per file.** Bar terisi per
   langkah: tulis manifest dan signature, rename staging (termasuk hapus sisa
   `app-<id>`), tulis `current.json`, ganti launcher root, hapus versi lama.
   Saat menghapus, bar maju per file dan nama file atau folder yang sedang
   dihapus ditampilkan. Jumlah file diambil dari `release.json` folder yang
   dihapus; file yang tidak tercatat di manifest disapu sesudahnya.
2. **Penghapusan versi lama tetap di Finishing**, urutan langkah tidak berubah,
   hanya ditambah progres. Folder yang masih terkunci tetap hanya dicatat di log
   dan dicoba lagi lewat `remove_leftovers`.
3. **Cakupan progres hapus:** Finishing pada update, install, dan repair;
   **Clean installation**; **Uninstall** (termasuk tahap kedua di salinan
   `%TEMP%`); penghapusan sisa `app-<id>` sebelum rename.

### Satu form, tanpa halaman baru

4. **Jendela maintenance dilebur ke form setup.** Form tetap empat halaman
   (Form, Checking, Installing, Done); tidak ada halaman baru. Saat produk sudah
   terpasang, halaman Form memuat:
   - source + Browse; install folder (terkunci);
   - daftar trusted key dengan **Import...** dan **Remove** (Remove dipindahkan
     dari jendela maintenance, di bawah Import);
   - checkbox Start menu, desktop, dan "Start <app> after ...";
   - **baris radio aksi** menggantikan checkbox Clean installation:
     `Action: (o) Update ( ) Repair ( ) Clean install ( ) Uninstall`;
   - tombol bawah: `[Download to cab|Update cab] [Run installed] ... [<aksi>] [Cancel]`.
     Tombol utama berganti teks mengikuti radio: **Update**, **Repair**,
     **Clean install**, **Uninstall**. Tombol Maintenance dihapus.
   - Ukuran jendela tetap 560×478. Versi terpasang (tanggal publish dan id
     release) ditampilkan di heading atau intro.
   - Pada instalasi baru (belum terpasang), baris aksi tidak tampil dan form
     berperilaku seperti sekarang (tombol Install).
5. **Source disimpan saat aksi dijalankan**, mengikuti perilaku setup sekarang:
   dicek, lalu disimpan pada Update, Repair, atau Clean install. Tombol Save
   jendela maintenance tidak dipindahkan. Uninstall tidak menyimpan source.
   Remove key berlaku seperti di jendela maintenance (langsung disimpan setelah
   konfirmasi); Import tetap mengikuti alur setup (disimpan setelah aksi
   berhasil).
6. **Perilaku aksi di form:**
   - **Update**: alur Update yang ada.
   - **Repair**: menjalankan repair (`Updater::repair`) pada worker thread
     dengan `ProgressPanel` di halaman Installing (fase Verifying, Preparing,
     Finishing), lalu halaman Done dengan ringkasan.
   - **Clean install**: konfirmasi (`Prompts::confirm_clean_install`), tunggu
     app tertutup, lalu **penghapusan dipindahkan ke worker thread** dengan
     progres di halaman Installing, lalu install baru dalam worker yang sama.
   - **Uninstall**: konfirmasi uninstall, tunggu app tertutup, hapus pada worker
     thread dengan progres di halaman Installing, lalu halaman Done
     ("<app> was uninstalled", tombol Finish; Run installed dan cab tidak tampil).
   - Heading/intro halaman Installing dan Done mengikuti aksi (mis.
     "Repairing ...", "Removing ...").
7. **Modify dari Apps & Features (`--maintenance`) membuka form dari salinan
   `%TEMP%`.** Bila `--maintenance` berjalan dari folder instalasi, launcher
   menyalin dirinya ke `%TEMP%\launcher-uninstall-<random>\` (pakai mekanisme
   `Uninstaller::start_finisher` yang sudah ada, digeneralisasi), menjalankan
   salinan itu untuk membuka form terpasang, lalu keluar. Salinan menunggu
   proses induk keluar sebelum mengambil lock, dan menjadwalkan penghapusan
   foldernya sendiri (`Uninstaller::schedule_removal_of`) saat keluar. Bila
   `--maintenance` dijalankan dari luar folder instalasi, form langsung dibuka.
   Konsekuensinya, Uninstall dari form selalu berjalan dari luar folder
   instalasi dan dapat menghapus langsung dengan progres.
8. **`maintenance_window.rs` dihapus.** `--maintenance` tetap ada sebagai
   perintah CLI dan target Modify, tetapi membuka form gabungan. `--maintenance
   --quiet` tetap error seperti sekarang. `--uninstall` dan `--repair` CLI tetap
   ada; tanpa `--quiet` keduanya memakai `ProgressWindow`.
9. **`ProgressWindow` tetap**, hanya untuk update otomatis dari runner dan
   perintah CLI tanpa form (`--update`, `--repair`, `--uninstall`, cab CLI).
   Jendela ini ikut mendapat progres Finishing dan hapus.

### Cab

10. **Tombol dan CLI cab dipisahkan.**
    - Tombol di form: teks **Download to cab** bila cab
      (`CabCommand::path()`) belum ada, **Update cab** bila ada. Teks diperbarui
      setelah operasi cab selesai dan saat form kembali ke halaman Form.
    - CLI: `--download-cab [--source S] [--quiet]` hanya membuat cab baru dan
      gagal dengan pesan jelas bila cab sudah ada (arahkan ke `--update-cab`).
      `--update-cab [--source S] [--quiet]` baru, hanya memperbarui cab yang ada
      dan gagal bila cab belum ada (arahkan ke `--download-cab`).
    - Kode dipisah menjadi dua fungsi publik (`CabCommand::download_new` dan
      `CabCommand::update_existing`, atau nama setara) yang memakai bagian
      bersama untuk unduh, kompresi, verifikasi, dan penggantian atomik.
11. **Update cab dengan cab rusak atau key tidak cocok: langsung dibuat ulang**
    dengan unduhan penuh, seperti perilaku sekarang (dicatat di log, tanpa
    tanya). Cab yang sudah sama dengan rilis tetap menghasilkan "Cab is up to
    date".
12. Prompt "Update cab before installing?" di `CabCommand::choose` tidak
    berubah.

### Eksekusi

13. Eksekutor **Codex**. Setelah semua kode selesai: `cargo build`,
    `cargo build --release`, `cargo test`, `cargo fmt --check`, dan
    `cargo clippy --all-targets -- -D warnings` harus lulus, lalu **commit di
    `work-bench`** tanpa push. Commit hanya memuat berkas plan ini; perubahan
    lain di working tree yang bukan bagian plan ini tidak ikut di-stage.
14. Uji visual (GUI) tidak dijalankan tanpa konfirmasi pengguna; cukup build,
    unit test, dan test console.
15. `dist/launcher/launcher.exe` di repo produk tidak di-rebuild oleh plan ini.

## Rancangan teknis

### Model progres

- Tambah `UpdatePhase::Removing` (nilai baru di akhir enum agar nilai lama
  tidak bergeser) untuk penghapusan instalasi (Clean install, Uninstall).
- Untuk `Finishing` dan `Removing`, `total_bytes`/`done_bytes` dipakai sebagai
  **jumlah unit** (langkah dan file), bukan byte. Perbarui doc comment kedua
  properti dan `UpdatePhase` agar menyebut unit per fase. Jangan menambah
  counter baru bila tidak perlu.
- Finishing: total = jumlah langkah tetap + jumlah file folder yang akan
  dihapus (dari manifest masing-masing; folder tanpa manifest dihitung 1 unit
  dan disapu dengan `remove_dir_all`). `set_file` menampilkan nama langkah atau
  path relatif file yang sedang dihapus.
- Helper hapus bersama, mis. `install::folder_removal` (nama bebas), yang
  menghapus file per file berdasarkan daftar, lalu menyapu sisa dan folder
  kosong, sambil memanggil `add_done`/`set_file`. Helper ini dipakai oleh
  `remove_versions_except`, penghapusan sisa `app-<id>`, dan
  `Uninstaller::remove`. Retry `Uninstaller::remove` (10× 500 ms) tetap berlaku
  untuk folder instalasi. Cek pembatalan tidak ditambahkan pada Finishing dan
  Removing (keduanya tidak boleh berhenti di tengah); tombol Cancel dinonaktifkan
  selama fase ini.
- `Uninstaller::remove` menerima `&UpdateProgress` (overload atau parameter
  baru; pemanggil tanpa UI memberi progress baru).

### ProgressPanel

- `sized` mencakup `Finishing` dan `Removing` bila total > 0.
- Teks status: "Finishing..." dengan detail langkah atau file, dan
  "Removing the installation..." untuk `Removing`. Baris detail untuk kedua
  fase menampilkan "n of m" (unit), bukan ukuran byte atau kecepatan.

### Form setup

- Ganti `clean: gui::CheckBox` dengan empat radio button aksi (WindowStyle
  perlu helper radio bila belum ada; pertahankan gaya yang ada). Posisi baris
  aksi di y=370, tempat checkbox lama.
- Tambah tombol **Remove** di bawah Import (y≈241); logika diambil dari
  jendela maintenance (konfirmasi, simpan, isi ulang daftar). Remove tidak
  boleh menghapus key tertanam atau key policy (ikuti aturan maintenance).
- Hapus `SetupOutcome::Maintenance` dan tombol Maintenance; tambah outcome
  bila perlu untuk Uninstall (mis. `Uninstalled`) agar pemanggil tidak
  menjalankan app.
- Pindahkan pengerjaan Repair, Clean install, dan Uninstall ke worker thread
  yang sama dengan Install (`job.execute` dalam `thread::spawn`) agar timer
  200 ms dapat menyegarkan `ProgressPanel`.
- Tombol cab membaca keberadaan cab saat `show_page(Page::Form)` dan setelah
  operasi cab.

### Modify dari `%TEMP%`

- Generalisasi `Uninstaller::start_finisher` menjadi fungsi yang menyalin
  launcher ke `%TEMP%\launcher-uninstall-<random>\` dan menjalankan salinan
  dengan argumen tertentu. Tambah argumen internal untuk form maintenance
  (mis. `--maintenance --parent-pid <pid>`), yang menunggu induk keluar sebelum
  mengambil lock dan menjadwalkan penghapusan foldernya saat keluar, seperti
  `UninstallCommand::finish`.
- Salinan harus membaca konfigurasi dan folder instalasi yang sama; pastikan
  `CommandContext`/`InstallCommand::run` mengenali form ini sebagai mode
  terpasang walau exe berada di `%TEMP%` (bukan `settings.ini` di samping exe
  salinan; source diambil dari konfigurasi terpasang).
- `--uninstall-finish` tetap untuk `--uninstall` CLI dari folder instalasi.

## Langkah eksekusi

Tulis seluruh kode semua langkah lebih dulu; build, test, dan review sekali di
akhir.

1. Model progres: `UpdatePhase::Removing`, dokumentasi unit, helper hapus per
   file.
2. Finishing bertahap di `Updater::install`, `prepare`, `activate`,
   `remove_versions_except`, `replace_root_launcher`, dan `repair.rs`.
3. `Uninstaller::remove` dengan progres; `UninstallCommand` CLI tanpa quiet
   memakai `ProgressWindow`.
4. `ProgressPanel`: tampilan unit untuk Finishing/Removing.
5. Form setup: baris radio aksi, Remove key, aksi Repair/Clean install/
   Uninstall di worker, teks heading dan tombol per aksi, halaman Done per aksi.
6. `--maintenance` membuka form (langsung, atau lewat salinan `%TEMP%`); hapus
   `maintenance_window.rs`, `MaintenanceAction`, dan rujukannya di `ui/mod.rs`
   dan `commands/maintenance_command.rs`.
7. Cab: pisahkan `download_new`/`update_existing`, argumen `--update-cab` di
   `cli/command_line.rs` (beserta help), tombol form dengan teks dinamis,
   `--download-cab` gagal bila cab sudah ada.
8. Test: tambah atau perbarui test di `tests/` dan unit test modul untuk:
   helper hapus (jumlah unit, sapuan file di luar manifest, nama file pada
   progres), Finishing dengan versi lama berisi file (total dan done akhir
   sama), parse `--update-cab` dan argumen internal maintenance, perilaku
   `--download-cab` saat cab ada dan `--update-cab` saat cab tidak ada.
   Perbarui test yang memakai `SetupOutcome::Maintenance` atau
   `MaintenanceWindow`.
9. Dokumen: perbarui `src/frontend/Launcher/CLAUDE.md` (bagian "Installer di
   PC yang sudah terpasang", CLI `--update-cab`, nasib jendela maintenance,
   dengan tanggal keputusan 2026-10-23) dan `doc/engine/engine-launcher.md`
   (form terpasang, aksi, progres, cab). Rujukan lain yang menyebut jendela
   maintenance atau "Download to cab" disesuaikan bila menjelaskan perilaku
   sekarang; laporan dan plan lama tidak diubah.
10. Build, test, fmt, clippy (keputusan 13); perbaiki temuan; commit di
    `work-bench`.
11. Pindahkan plan ini ke `plan/executed/` dan tambahkan laporan eksekusi di
    bawah.

## Hal yang tidak tercakup

Bila muncul hal yang tidak tercakup, ambil pilihan yang paling konsisten
dengan keputusan di atas dan catat di laporan eksekusi; jangan berhenti untuk
bertanya.

## Laporan eksekusi

Tanggal pelaksanaan: 2026-10-23. Branch: `work-bench`. Tidak ada push.

### Hasil implementasi

- Form setup tetap empat halaman dengan radio Update/Repair/Clean install/Uninstall,
  tombol utama/heading/Done sesuai aksi, versi terpasang, Remove key user setelah
  konfirmasi, serta tombol cab dinamis. `maintenance_window.rs` dihapus.
- Pekerjaan Repair, Clean install dan Uninstall berjalan pada worker. Uninstall
  tidak membutuhkan source/key dan menghasilkan outcome tersendiri tanpa menjalankan app.
  Source/import hanya disimpan setelah aksi pemasangan/perbaikan berhasil; Remove
  langsung tersimpan, dan key embedded/policy tidak dapat dihapus dari form.
- `--maintenance` membuka form gabungan dari konfigurasi terpasang tanpa settings.ini.
  Dari dalam instalasi, launcher memakai salinan TEMP dan `--parent-pid`; parent
  ditunggu sebelum mengambil lock. Salinan menjadwalkan pembersihan saat keluar.
- Finishing memakai langkah tetap dan unit hapus dari manifest. Helper bersama
  menyapu file tambahan/folder kosong dan tidak mengikuti directory reparse point
  saat menghapus file manifest. Versi lama terkunci dicatat dan dicoba lagi saat
  remove_leftovers; penghitung menyelesaikan unit percobaan yang ditunda tersebut.
- Removing mencakup clean/uninstall beserta salinan tahap kedua CLI. Retry 10�500 ms
  dipertahankan tanpa menghitung ulang unit yang sudah selesai. Cancel dinonaktifkan
  pada Finishing/Removing, termasuk permintaan lewat tombol tutup jendela.
- Cab dipisah menjadi `download_new` dan `update_existing`, argumen/help/dispatch
  `--update-cab` ditambah. Cab rusak atau key tidak cocok dibuat ulang dengan log;
  cab sama tetap up to date. Pembuatan cab baru juga tidak menimpa path yang muncul
  ketika operasi berlangsung (replacement atomik hanya untuk update).
- Guide `doc/engine/engine-launcher.md` dan `src/frontend/Launcher/CLAUDE.md`
  diperbarui sesuai keputusan 2026-10-23. Tidak ada perubahan format rilis,
  dependency, database, atau binary `dist/launcher/launcher.exe` produk.

### Pilihan implementasi dan review

- Satu unit sapuan ditambahkan per folder selain jumlah file manifest. Folder
  tanpa manifest valid hanya memakai unit sapuan. Finishing memakai lima langkah
  tetap pada install/update, empat pada repair, dan satu pada prepare tanpa aktivasi.
- Clean memeriksa source/signature sebelum penghapusan. Jika memakai cab offline,
  staging sementara berada di samping instalasi agar selamat dari penghapusan
  dan dapat dipindah pada volume yang sama. Key lama yang hanya ada di registry
  tetap hilang kecuali diimpor kembali sebagai bagian request baru.
- Review akhir memperbaiki Cancel sebelum clean menghapus instalasi, cleanup staging
  sisa dengan id yang sama saat Repair, serta retry hapus yang tidak menggandakan counter.
  Form tetap mengenali instalasi dari konfigurasi meskipun current.json perlu diperbaiki.
- Tidak ada tindakan yang ditolak policy atau skrip manual yang perlu dijalankan pengguna.

### Verifikasi

Semua perintah final dijalankan dari `src/frontend/Launcher` dengan environment
MSVC x64 dari `vcvars64.bat` yang ditemukan lewat vswhere. Percobaan awal tanpa
environment tersebut gagal menemukan header `excpt.h`; environment build yang
lengkap menyelesaikannya. Clippy awal menemukan clone untuk slice satu key pada
regresi baru; diganti borrowing melalui `std::slice::from_ref`.

| Pemeriksaan | Hasil final |
| --- | --- |
| `cargo build` | Lulus |
| `cargo build --release` | Lulus |
| `cargo test` | 90 lulus, 0 gagal, 0 diabaikan |
| `cargo fmt --check` | Lulus |
| `cargo clippy --all-targets -- -D warnings` | Lulus tanpa warning |
| `git diff --check` | Lulus |

Test mencakup helper hapus (unit, nama file terkunci, sapuan file tambahan),
Finishing update dengan versi lama dan target sisa, Repair dengan staging sisa,
retry Uninstall, Uninstall tanpa source/key, pemeriksaan signature/Cancel sebelum
clean, penyimpanan source Repair, penghapusan setting/key lama pada clean,
parse argumen cab/maintenance internal, prasyarat cab, roundtrip cab, serta rebuild
cab rusak/key berbeda dengan unduhan penuh. Vektor kontrak rilis dan suite HTTP
lokal yang sudah ada ikut lulus.

Log final di luar repo: `../.artefacts/em-system/scripts/launcher-installer-smoke/out/`
(`debug-build.log`, `release-build.log`, `test.log`, `fmt.log`, `clippy.log`).

### Batas verifikasi

Uji visual/GUI tidak dijalankan sesuai keputusan plan. Tata letak nyata, interaksi
radio/key/Done, respons form saat menghapus, serta peluncuran Modify lewat salinan
TEMP perlu dicoba pengguna pada GUI. Tes console memeriksa parser internal dan
jalur engine penghapusan/repair, bukan membuka jendela tersebut. Binary dist produk
sengaja tidak di-rebuild; hasil build ada pada target/debug dan target/release
project Launcher. Commit hanya memuat perubahan plan ini di `work-bench`, tanpa push.
