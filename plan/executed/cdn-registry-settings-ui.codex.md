# Pengaturan CDN dan container registry melalui UI manager

Tanggal: 2026-10-03.
Status: implementasi selesai dan eksekusi ditutup 2026-10-03; hasil aktual dan verifikasi tertunda tercatat di doc/report/cdn-registry-settings-ui-eksekusi.md.

Arahan pengguna saat eksekusi: rapikan Program.cs dengan konfigurasi host tanpa nilai path/limit; simpan pengaturan persisten di `ta_Meta`. Keputusan penyimpanan file pada rancangan awal digantikan dokumen JSON per host di ta_Meta, ditulis atomic dengan transaksi serializable dan revision.

## Tujuan

Pengguna yang berwenang dapat mengaktifkan/menonaktifkan CDN dan container registry (c-reg), serta mengatur directory penyimpanannya dari masing-masing manager WPF. API tetap menjalankan layanan dan mengakses filesystem server. Pengaturan operasional tidak lagi memerlukan edit `Program.cs`.

## Aturan eksekusi

- Baca `claude.md` terbaru sebelum mulai. Pertahankan perubahan pengguna yang sudah ada di working tree.
- Tulis seluruh kode semua tahap sampai selesai terlebih dahulu. Integrasi akhir, build, pengujian HTTP/UI, dan review dilakukan setelah seluruh tahap penulisan kode selesai, mengikuti `claude.md`.
- Jika tindakan ditolak policy, ikuti aturan skrip PowerShell manual di `claude.md`; jangan mencoba melewati penolakan.
- Plan ini tidak mengizinkan pemindahan/penghapusan data storage aktif atau restart server produksi secara otomatis.

## Kondisi sekarang dan titik perubahan

- `src/backend/Em.Api/Program.cs` memanggil `EnableCdn("./data/cdn", 200)` dan `AddContainerRegistry("./data/container-registry")`.
- `Api/Shared/EmAppBuilder.cs` dan `EmAppBuilder.ContainerRegistry.cs` menyimpan konfigurasi startup. Registrasi `CtnContext` dan provider hak robot saat ini ikut panggilan `AddContainerRegistry`.
- `src/backend/Em.Api.Core/Api/Core/EmApp.cs` membentuk singleton `CdnStore`/`CtnBlobStore`, memetakan `/cdn` dan `/v2`, serta menjalankan pemeriksaan startup registry.
- Kontrak API: `src/shared/Em.Libs/Api.Core.Models/ICdnServices.cs` dan `ICtnServices.cs`; implementasi: `Api/Core/CdnServices.cs` dan `Api/Registry/CtnServices.cs`.
- Proxy WPF: `src/shared/Em.Ui.Wpf.Core/Api.Core/CdnService.cs` dan `CtnService.cs`.
- UI: `Navigations/CdnManager.xaml[.cs]`, `ContainerManager.xaml[.cs]`, serta card manager pada `DefaultHomeControl.xaml[.cs]`.
- Store mati dan beberapa action saat ini menjawab 404. Pengaturan baru harus tetap dapat dibaca/disimpan saat store mati.
- Baca ulang titik-titik ini saat eksekusi karena implementasi dapat berubah setelah plan ditulis.

## Keputusan rancangan

### 1. Penerapan setelah restart

- Semua perubahan enable, directory, dan batas upload CDN disimpan sebagai konfigurasi untuk startup berikutnya.
- `Save` tidak mengganti store aktif, middleware publik, atau request/task yang sedang berjalan.
- API mengembalikan konfigurasi **tersimpan** dan **aktif**, serta `RequiresRestart` berdasarkan perbedaan keduanya. Restart tidak diperlukan jika nilainya setara setelah normalisasi.
- UI memberi pesan jelas bahwa perubahan baru berlaku setelah restart API. Restart dilakukan operator; jangan menambahkan tombol restart server pada tahap ini.
- Saat disable sudah diterapkan, `/cdn` atau `/v2` dan operasi isi manager mengikuti perilaku disabled yang ada. Data disk, metadata, grant robot, dan token tetap disimpan.

### 2. Penyimpanan konfigurasi per host

- Gunakan file khusus, misalnya `data/em.storage-settings.json`, terpisah dari `em.local.json` dan dari directory payload CDN/registry.
- File berisi versi format, revision, serta bagian CDN (`Enabled`, `Directory`, `MaxUploadMb`) dan registry (`Enabled`, `Directory`). Jangan menyimpan secret.
- Host mendaftarkan kemampuan pengaturan melalui builder, misalnya `AddManagedStorageSettings(settingsFilePath, defaults)`. Nama final disesuaikan pola library saat implementasi.
- File yang sudah ada menjadi sumber konfigurasi startup. Defaults hanya dipakai bila file belum ada, bukan menimpa konfigurasi tersimpan pada setiap startup.
- Host contoh mempertahankan nilai awal saat ini melalui defaults: CDN aktif di `./data/cdn`, 200 MB; registry aktif di `./data/container-registry`. Ini mencegah upgrade mematikan layanan yang sudah berjalan.
- Pertahankan builder lama untuk host library lain yang masih memakai konfigurasi statis. Pada mode statis, UI menampilkan kemampuan pengaturan tidak tersedia; jangan menawarkan Save yang tidak akan diterapkan.
- Tolak penggunaan mode managed dan konfigurasi statis untuk fitur yang sama secara bersamaan dengan pesan startup yang jelas.
- Simpan secara atomic menggunakan file sementara di directory konfigurasi yang sama, lalu replace/rename; serialisasi penulisan dan gunakan revision untuk mencegah dua manager menimpa perubahan masing-masing.
- Mutasi satu fitur harus mempertahankan bagian fitur lain. Penyimpanan gagal tidak mengubah konfigurasi tersimpan maupun aktif yang dilaporkan.
- File invalid/versi tidak didukung menimbulkan error startup yang jelas; jangan diam-diam kembali ke defaults aktif. Dokumentasikan pemulihan lewat backup atau koreksi file.
- Jangan commit file runtime atau menimpa konfigurasi deployment saat publish. Dokumentasikan directory konfigurasi yang writable dan persisten bagi akun API, termasuk deployment container.
- Tahap ini untuk satu instance API. Konfigurasi per host tidak menyinkronkan node dalam deployment multi-instance.

### 3. Directory dan perlindungan data

- Directory merujuk filesystem **server API**, bukan mesin UI. Gunakan input path dan validasi server; tidak memakai folder picker lokal WPF.
- Path relatif dihitung dari `ContentRootPath`, konsisten dengan perilaku sekarang. Respons menampilkan path absolut hasil resolusi.
- Validasi path kosong/tidak valid, akses directory, dan kemampuan baca/tulis. Untuk enable, directory wajib valid; directory boleh tetap disimpan ketika disabled.
- Validasi directory yang belum ada memeriksa parent yang tersedia; pembuatan directory dilakukan secara eksplisit saat Save yang membutuhkan directory aktif/startup, dengan hasil yang jelas. Jangan membuat struktur directory hanya karena GET status.
- Jika memakai probe baca/tulis, gunakan file unik, jangan sentuh file pengguna, bersihkan probe sendiri, dan laporkan kegagalan cleanup.
- CDN menyajikan isi directory secara publik. Tolak directory konfigurasi, aplikasi/secret, serta tumpang tindih ancestor/descendant dengan registry, binary approval, atau storage internal lain yang diketahui host; periksa resolusi link/reparse agar aturan tidak mudah dilewati.
- Mengganti directory tidak memindahkan file. UI wajib menjelaskan hal ini sebelum Save perubahan directory dan meminta konfirmasi terhadap dampak tersebut.
- Registry yang sudah berisi metadata tidak boleh diarahkan ke directory kosong seolah aman: blob lama akan hilang dari jangkauan. Pada tahap ini, tolak perubahan root registry yang berisi data/unggahan hingga operator menyiapkan salinan lengkap secara manual dan pemeriksaan kelengkapan blob berhasil. Gunakan daftar blob yang dirujuk metadata, bukan hanya jumlah file target. Laporkan batas pemeriksaan ini.
- Pemeriksaan saat Save tidak menjamin filesystem tidak berubah sebelum restart. Ulangi validasi saat startup dan berikan error yang menjelaskan konfigurasi/storage penyebabnya.

### 4. Hak akses

- Pertahankan claim `CDN Manager Access` dan `Container Manager Access` untuk melihat manager serta status umum.
- Tambahkan claim terpisah untuk membaca path/config detail, validasi directory, dan mengubah konfigurasi, misalnya `CDN Settings Manage` dan `Container Registry Settings Manage` pada Administrative Tools.
- Terapkan pemeriksaan claim di API, termasuk saat store disabled; menyembunyikan kontrol UI saja tidak cukup.
- Jangan otomatis memberikan claim konfigurasi kepada seluruh pemegang claim manager lama. Ikuti mekanisme administrator dan registrasi claim yang sudah ada.
- User tanpa claim konfigurasi tetap dapat mengelola isi sesuai haknya ketika layanan aktif, tetapi tidak menerima path server melalui DTO status umum.

## Tahap penulisan kode

### A. Kontrak dan penyimpanan backend

- [x] Tambahkan DTO status umum, konfigurasi detail aktif/tersimpan, request Save dengan revision, serta hasil validasi directory.
- [x] Tambahkan action GET status/config, validasi, dan Save pada service CDN/registry dengan claim yang sesuai. Gunakan pola action dan error repository.
- [x] Implementasikan store konfigurasi per host: defaults, versi, revision, pembacaan startup, atomic write, konflik revision, dan normalisasi untuk `RequiresRestart`.
- [x] Implementasikan validasi directory, pemeriksaan overlap/link, serta pemeriksaan kelengkapan registry untuk perubahan root berisi data.

### B. Registrasi dan host API

- [x] Tambahkan mode managed pada builder dengan kompatibilitas mode statis dan validasi konflik.
- [x] Pisahkan registrasi layanan manajemen/config dari enable store. Saat registry disabled, pengaturan tetap tersedia; registrasi `CtnContext`/provider hak robot/pemeriksaan skema mengikuti kebutuhan nyata, bukan sekadar flag enabled lama.
- [x] Nyatakan prasyarat skema registry dengan jelas untuk host managed; jangan membuat UI enable yang baru gagal karena tabel belum disiapkan.
- [x] Muat konfigurasi sebelum membentuk store dan middleware publik; simpan snapshot aktif selama umur proses.
- [x] Ubah host `Em.Api` menjadi managed dengan defaults lama. Pertahankan konfigurasi binary approval dan modul uji.
- [x] Tambahkan pengecualian Git/publish yang diperlukan bagi file runtime tanpa mengubah file lokal pengguna.

### C. UI manager WPF

- [x] Tambahkan proxy dan model UI untuk action baru.
- [x] Tambahkan panel/card Settings pada masing-masing manager: toggle enable, server directory, Validate directory, Save; CDN juga memiliki Max upload MB.
- [x] Tampilkan status aktif dan perubahan tersimpan yang menunggu restart. Toggle menyunting draft; perubahan tidak otomatis tersimpan.
- [x] Settings tetap tersedia saat layanan disabled. Area pengelolaan isi mengikuti status **aktif**, bukan draft toggle atau konfigurasi pending.
- [x] Bedakan layanan disabled, fitur tidak mendukung konfigurasi UI, request gagal, dan path invalid; jangan menyimpulkan disabled hanya dari 404 folder tertentu.
- [x] Tampilkan konflik revision dan beri cara refresh/reload tanpa menimpa draft diam-diam. Cegah Save/Validate ganda dan jelaskan perubahan draft yang belum disimpan saat menutup panel.
- [x] Tambahkan refresh kecil di kanan atas card dinamis; memperbarui seluruh status card secara independen dan tersedia kembali sesudah gagal.
- [x] Terapkan warna tema untuk enabled, disabled, busy, dan transisi; gunakan style collection bersama jika ada list/tree. Perubahan ini untuk WPF; layar manager MAUI tidak ditambahkan.
- [x] Sesuaikan card home agar menunjukkan disabled/pending secara benar tanpa menganggap ukuran gagal sebagai nol; path server hanya tampil di UI berhak.

### D. Dokumentasi dan bahan verifikasi

- [x] Perbarui panduan CDN/registry dan README host: mode managed/statis, lokasi konfigurasi, hak, defaults, restart, backup/pemulihan, dan perubahan directory.
- [x] Siapkan pengujian terisolasi untuk persistence, claim, disabled state, perubahan directory, serta penerapan setelah restart. Jangan memakai storage/database produksi sebagai fixture.
- [x] Catat cara menyiapkan skema registry dan claim baru; perubahan skema tambahan hanya jika implementasi benar-benar membutuhkannya.

## Integrasi, pengujian, dan review akhir

Lakukan bagian ini setelah kode A–D lengkap.

- [x] Build `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, dan `src/frontend/Em.Ui.Maui.slnx` bila kontrak bersama memengaruhinya; jalankan `git diff --check`.
- [ ] Uji defaults tanpa file, reload setelah restart, file invalid, atomic write gagal, revision konflik, dan Save CDN/registry bersamaan tanpa kehilangan salah satu bagian.
- [x] Uji API: unauthenticated/tanpa claim tidak dapat membaca path/menyimpan; claim konfigurasi bisa bekerja saat layanan disabled; mode statis menolak Save secara jelas.
- [x] Uji Save enable/disable/path/limit: runtime lama tetap aktif sampai restart; sesudah restart konfigurasi baru diterapkan dan `RequiresRestart` menjadi false. Menyimpan kembali nilai aktif membatalkan status pending.
- [ ] Uji directory invalid/read-only/relative, overlap storage, link/reparse, serta registry target tidak lengkap. Pastikan data lama tidak dipindah/dihapus.
- [ ] Uji `/cdn` download/Range, upload limit, `/v2` login/push/pull, serta robot grants sebelum/sesudah restart pada fixture. Jika Docker nyata tidak tersedia, bedakan uji HTTP dari uji Docker.
- [ ] Uji perubahan pending ketika upload/task masih berjalan: Save tidak memutus operasinya. Restart mengikuti prosedur shutdown normal; jangan menjanjikan resume upload tanpa bukti.
- [ ] Verifikasi WPF tema terang/gelap, disabled/loading, response cepat, retry gagal, hak terbatas, layanan disabled, dan perubahan pending. Build bukan bukti render/interaksi.
- [x] Review keseluruhan: kebocoran path/secret, race saat Save, kompatibilitas host lama, registrasi saat disabled, startup failure, dan integritas referensi blob.

## Kriteria selesai dan penutupan

- Pengaturan enable/directory dapat dikelola dari masing-masing manager oleh user berhak dan bertahan setelah restart API.
- Konfigurasi aktif/pending jelas, manager tetap dapat mengaktifkan kembali layanan disabled, dan operasi storage yang ada tidak mengalami regresi.
- File/data lama tetap utuh; perubahan root registry tidak diterima dengan referensi blob yang diketahui tidak lengkap.
- Dokumentasi dan laporan `doc/report/cdn-registry-settings-ui-eksekusi.md` menyebut hasil aktual, batas yang diketahui, serta verifikasi/tindakan manual yang belum dilakukan.
- Setelah implementasi dan penutupan eksekusi, pindahkan plan ke `plan/executed/`. Tambahkan catatan status di `claude.md` tanpa menghapus isinya. Jangan memindahkan plan hanya karena dokumen perencanaan ini selesai ditulis.

## Di luar cakupan tahap ini

- Penerapan konfigurasi tanpa restart, tombol restart API, dan sinkronisasi konfigurasi banyak node.
- Migrasi otomatis/copy/move isi storage, browser filesystem server umum, atau pemilihan directory lokal client.
- GC/retensi/kuota registry, perubahan identitas robot, dan manager MAUI baru.


## Penutupan eksekusi

Kode A-D selesai. Checklist yang berkaitan file runtime diselesaikan melalui persistence `ta_Meta` sesuai koreksi pengguna (atomic transaction, bukan replace file; tidak ada file runtime untuk Git/publish). Defaults builder mempertahankan layanan lama; Program.cs cukup satu pemanggilan managed tanpa path/limit.

Build tiga solution, 98 pemeriksaan smoke SQL/HTTP, dan 8 pemeriksaan render card lulus. Item integrasi yang belum dicentang mencakup verifikasi parsial dan lanjutan; rinciannya tidak dianggap lulus seluruhnya. Laporan hasil dan batas: [cdn-registry-settings-ui-eksekusi.md](../../doc/report/cdn-registry-settings-ui-eksekusi.md). Tidak ada tindakan terblokir policy atau skrip manual wajib.
