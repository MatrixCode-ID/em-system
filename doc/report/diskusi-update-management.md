# Laporan Diskusi — Update Management Aplikasi WPF

Status: **diskusi selesai sampai pembagian tahap, belum ada plan dan belum ada kode yang diubah**
Dibuat: 2026-09-27
Baseline: commit `28b5e23`, branch `spa-detach`
Sifat: laporan **diskusi dan keputusan**, bukan laporan eksekusi
Rujukan:
- `src/backend/Em.Api/Program.cs` (`builder.EnableCdn("./data/cdn", 200)`)
- `src/backend/CLAUDE.md`, bagian "CDN: public files outside the dispatcher"

---

## 1. Ringkasan

Diskusi dimulai dari keluhan terhadap Velopack: setiap publish mengirim aplikasi utuh (bisa sekitar
300 MB), padahal perubahannya sering hanya satu modul. Usulan awal user adalah menyimpan aplikasi di
CDN bersama satu file JSON berisi daftar file dan hash-nya, lalu aplikasi hanya mengunduh file yang
berbeda.

Setelah beberapa putaran, rancangannya mengerucut ke bentuk yang **sengaja minimalis**:

- **CDN hanya menyimpan satu versi**: folder `wpf-release/binaries/` yang mencerminkan folder client
  per path, ditambah satu `wpf-release/release.json` (nama + lokasi file + SHA-1). Tidak ada riwayat
  versi dan tidak ada catatan jumlah upload.
- **Rilis dikelola dari modul sistem Release Manager (WPF)**, dengan pola yang sama seperti Role
  Manager: build, bandingkan, lalu Sync ke CDN.
- **Launcher** menangani instalasi pertama dan penerapan update. Kalau belum ada konfigurasi, launcher
  menampilkan dialog lokasi CDN.

Pekerjaannya dibagi dua tahap: **(1) Release Manager**, lalu **(2) Launcher**.

## 2. Kondisi awal yang ditemukan

| Temuan | Dampak |
| --- | --- |
| CDN adalah bagian dari `Em.Api` sendiri: `EnableCdn("./data/cdn", 200)`, disajikan `StaticFileMiddleware` di `/cdn/...` | Tidak perlu storage atau layanan terpisah. Area rilis cukup satu folder di bawah CDN yang ada |
| `/cdn` publik: tanpa login, tanpa rate limit, dan mendukung Range/ETag/206 | Launcher bisa mengunduh sebelum ada sign in, dan unduhan yang putus bisa dilanjutkan |
| `ServeUnknownFileTypes = true` dengan `application/octet-stream` (`EmApp.cs`) | File `.dll` dan file tanpa ekstensi tetap tersaji |
| Nama berawalan `.` tidak pernah disajikan dan tidak pernah diterima | Cocok untuk file lock atau file sementara di area rilis |
| Upload lewat `ICdnServices` (`PostGetMeta_CdnUpload`) menolak nama yang sudah ada (409) | Model satu versi butuh operasi *replace*. Perlu action baru atau jalur lain |
| Batas upload 200 MB per file | Cukup untuk file per file |
| `Em.Api` akan dirilis sebagai **container** | Folder `./data/cdn` wajib di-mount sebagai volume persisten, kalau tidak isi CDN hilang setiap deploy |
| Modul WPF direferensikan langsung oleh host | `dotnet publish` pada host menghasilkan folder client lengkap |

## 3. Jalannya diskusi

Arah rancangan berubah beberapa kali. Urutannya dicatat supaya alasan keputusan akhir bisa ditelusuri.

1. **Analisa konsep manifest + hash.** Konsepnya layak dan cocok untuk ERP modular. Risiko yang
   diangkat: keamanan (DLL yang diunduh lalu dijalankan), file yang terkunci saat aplikasi berjalan,
   update setengah jadi, dan build yang tidak deterministik (kalau setiap build menstempel versi ke
   semua assembly, semua DLL dianggap berubah).
2. **Alur di dalam aplikasi.** User menetapkan: update diunduh di dalam aplikasi ke folder terpisah,
   ada ikon animasi selama proses, user diberi tahu saat selesai, dan saat restart sebuah exe lain yang
   menerapkannya.
3. **Launcher dan installer.** Install pertama ternyata sama dengan update dari kondisi kosong.
   Diusulkan satu exe untuk peran launcher, installer, dan updater.
4. **Konfigurasi.** User memilih dialog input lokasi CDN daripada menyunting `install.json`, dan
   menyimpannya di **registry**, dalam **satu root dengan aplikasi**.
5. **Publisher.** Awalnya diusulkan rancangan lengkap: blob content-addressed, manifest per versi,
   channel, promote terpisah, dan nomor rilis. Lalu muncul pertanyaan aplikasi terpisah atau modul.
6. **CDN ternyata bagian dari `Em.Api`.** Setelah `Program.cs` dicek, alasan-alasan untuk aplikasi
   release terpisah gugur, kecuali build dan private key. Arahnya bergeser ke **modul Release
   Manager**.
7. **Modul direferensikan langsung** (user pernah membuat sistem plugin dan sulit di-debug). Prinsip
   **"yang ada di CDN = client"** ditetapkan. Checklist per file dianggap tidak perlu.
8. **Penyederhanaan oleh user.** CDN hanya menyimpan satu versi (`binaries/` + `release.json`). Semua
   rancangan multi-versi dibatalkan dengan alasan tidak perlu overengineering: pemakainya private
   company dan update tidak pernah mundur.
9. **Tanda tangan `.sig` dijelaskan** (bagian 6.1), lalu pekerjaan dibagi menjadi dua tahap.

## 4. Keputusan akhir

### 4.1 Isi CDN dan rilis

| # | Keputusan |
| --- | --- |
| R1 | CDN hanya menyimpan **satu versi**: `wpf-release/binaries/` (cermin folder client per path) dan `wpf-release/release.json`. |
| R2 | `release.json` berisi **nama + lokasi file + SHA-1**. Tidak ada riwayat dan tidak ada catatan jumlah upload. |
| R3 | **Isi CDN = folder client, persis.** Satu rilis adalah snapshot utuh hasil publish host, tanpa file yang dipilih-pilih. |
| R4 | Update **tidak pernah mundur**. Kalau salah rilis, perbaikannya di code, lalu build dan publish ulang. |
| R5 | Kalau riwayat suatu saat diperlukan, cukup folder selevel `binaries/`, misalnya `histories/`. |
| R6 | Modul WPF **direferensikan langsung** oleh host, bukan plugin. |
| R7 | CDN adalah CDN milik `Em.Api` (folder). Backend `Em.Api` dirilis lewat **container**, terpisah dari rilis client. |

### 4.2 Release Manager (tahap 1)

| # | Keputusan |
| --- | --- |
| M1 | Release Manager dibuat sebagai **modul sistem WPF**, seperti Role Manager. |
| M2 | Layar berisi: pengaturan lokasi CDN, baca isi CDN dan tampilkan, tombol **Build for publish**, hasil perbandingan (baru / ganti / hapus), dan tombol **Sync** (salin satu arah ke CDN). |
| M3 | Daftar dibagi dua kelompok: **modul utama** (project dalam solution) dan **library extra**, dengan expander atau tab. |
| M4 | Setelah Sync berhasil, `release.json` di server diperbarui. **Mekanisme persisnya masih perlu dianalisa.** |
| M5 | Tombol Build **hanya aktif di mesin yang punya .NET SDK dan source**. Di PC lain tombol dinonaktifkan dengan keterangan. |

### 4.3 Launcher dan sisi client (tahap 2)

| # | Keputusan |
| --- | --- |
| L1 | Kalau dijalankan **tanpa konfigurasi**, launcher menampilkan **dialog input lokasi CDN**. Setelah OK, launcher menampilkan **progres unduhan**. |
| L2 | Konfigurasi **dibaca dan disimpan di registry**, dalam **satu root dengan aplikasi**. |
| L3 | Update diunduh **di dalam aplikasi** ke folder terpisah, dengan **ikon animasi** selama proses. User diberi tahu saat unduhan selesai. |
| L4 | Saat restart, **exe lain** (launcher/updater) yang menerapkan update. |

### 4.4 Ditunda

| # | Hal | Keterangan |
| --- | --- | --- |
| T1 | MAUI | Belum dipikirkan. Fitur update dianggap khusus WPF untuk sementara |
| T2 | Banyak deployment backend dengan versi berbeda | Bisa jadi central update server atau cluster. Desain jangan dikunci ke salah satunya |

## 5. Yang dibatalkan

| Rancangan | Alasan dibatalkan |
| --- | --- |
| Blob content-addressed (`blobs/{hash}`), manifest per versi, pointer channel | Diganti model satu versi (R1). Dianggap overengineering untuk private company |
| Channel `stable`/`beta`/`internal` dan promote terpisah dari upload | Update tidak pernah mundur (R4); pengujian cukup di CDN server dev/test |
| Nomor rilis bilangan bulat dan reservasi nomor | Tidak ada riwayat versi (R2) |
| Checklist tambah/hapus/replace per file | Rilis adalah snapshot utuh (R3). Memilih file berisiko menghasilkan campuran versi (misalnya modul baru dengan `Em.Libs` lama) |
| Aplikasi release terpisah dengan kredensial storage sendiri | CDN ada di `Em.Api`, sehingga Release Manager menjadi modul (M1) |
| Sistem plugin modul dan instalasi modul terpilih per client | Modul direferensikan langsung (R6); akses diatur lewat claim |
| `install.json` di folder instalasi | Diganti dialog + registry (L1, L2) |
| Menurunkan lokasi update dari alamat API server | Sumber update tetap konfigurasi sendiri supaya central update server tetap mungkin (T2) |

## 6. Jawaban pertanyaan teknis

### 6.1 Cara kerja tanda tangan `.sig`

- **Sepasang kunci.** Private key hanya ada di mesin developer yang melakukan Sync. Public key ditanam
  di launcher dan aplikasi. Public key hanya bisa memeriksa tanda tangan, tidak bisa membuatnya, jadi
  aman ikut tersebar.
- **Saat Sync:** byte `release.json` di-hash, hasilnya ditandatangani dengan private key, dan disimpan
  sebagai `release.json.sig` di sebelahnya.
- **Saat client mengecek:** unduh `release.json` dan `.sig`, lalu periksa dengan public key. Kalau
  tidak cocok, tolak seluruh update. Kalau cocok, lanjut unduh file dan cocokkan hash-nya.
- **Satu tanda tangan cukup untuk semua file** karena rantai kepercayaannya: `.sig` menjamin
  `release.json`, dan `release.json` menjamin hash setiap file. Karena itu, kalau tanda tangan
  dipakai, hash-nya perlu kuat (SHA-256). Collision SHA-1 sudah bisa dibuat.
- **Melindungi dari:** file atau `release.json` yang diganti orang yang punya akses ke server, file
  korup, dan server palsu (DNS atau lokasi CDN yang salah diisi).
- **Tidak melindungi dari:** private key yang bocor, bug di rilis sendiri, dan kerahasiaan (tanda
  tangan bukan enkripsi).
- **Praktis:** tandatangani byte file apa adanya, jangan memformat ulang JSON sebelum verifikasi.
  Algoritma ECDSA P-256 atau RSA sudah tersedia di `System.Security.Cryptography`. Private key
  disimpan di certificate store Windows dengan key non-exportable. Untuk jaga-jaga, tanam dua public
  key (utama dan cadangan offline).
- **Kapan:** boleh ditunda, karena strukturnya tidak berubah (cukup satu file tambahan). Tapi
  **launcher versi pertama sebaiknya sudah menyiapkan verifikasinya**, karena launcher yang tidak
  memeriksa tanda tangan tidak akan mulai memeriksanya sampai ia sendiri di-update.

### 6.2 Kenapa bukan Velopack

Velopack sebenarnya punya **delta package**, jadi client yang sudah di versi sebelumnya tidak
mengunduh ulang 300 MB. Masalahnya ada di sisi **publish**: setiap rilis tetap membuat dan mengunggah
full package. Rancangan ini menyelesaikan masalah publish itu, dengan granularitas per file yang cocok
dengan struktur modular.

### 6.3 Syarat build supaya "hanya file yang berubah" benar-benar terjadi

- Build harus **deterministik**. Jangan menstempel versi rilis ke semua assembly (misalnya
  `AssemblyInformationalVersion` dengan git hash), karena setiap DLL akan beda byte meski kodenya
  sama.
- **Jangan pakai `PublishSingleFile`.**
- Kalau perbandingan menunjukkan jauh lebih banyak file berubah dari yang diharapkan, itu tanda build
  tidak deterministik, bukan alasan untuk menyaring file secara manual.

### 6.4 Teknologi launcher

C# **NativeAOT** diusulkan: satu exe native beberapa MB, startup instan, tidak butuh .NET terpasang
(penting untuk install pertama), dan logika `release.json`/hash bisa dibagi dengan aplikasi.
Batasannya: tanpa WPF (WinForms juga tidak didukung AOT), sehingga UI dialog dan progres memakai Win32
minimal (misalnya CsWin32). `System.Text.Json` wajib memakai source generator, dan `Em.Libs` jangan
direferensikan begitu saja karena belum tentu aman untuk trimming/AOT.

## 7. Masih berstatus usulan

Belum dikonfirmasi user. Diputuskan saat menyusun plan tahap terkait.

| Usulan | Tahap |
| --- | --- |
| **Urutan Sync:** unggah file baru/diganti → tulis `release.json` paling akhir secara atomik (temp lalu rename) → baru hapus file yang tidak tercantum. Ditambah satu **lock** (file berawalan `.`) supaya tidak ada dua Sync bersamaan. | 1 |
| Action baru untuk **menimpa file** di CDN (lewat temp file lalu move atomik), karena upload yang ada menolak 409. Alternatifnya menulis langsung ke folder volume. | 1 |
| Model dan logika hash `release.json` di **library kecil bersama** yang aman untuk AOT, karena format ini kontrak antara tahap 1 dan 2. | 1 |
| Pembagian modul utama / library dibaca otomatis dari `*.deps.json` (tipe `project` vs `package`), ditambah kelompok runtime .NET yang terlipat. Expander lebih disarankan daripada tab. | 1 |
| Selain Build, sediakan **"Pilih folder publish"** supaya Sync bisa dilakukan dari PC tanpa SDK. | 1 |
| Pengecekan tombol Build: `dotnet --list-sdks` (harus ada SDK 10.x) dan path project host yang tersimpan di registry. | 1 |
| Fungsi **Verify** di Release Manager untuk menguji isi CDN sebelum launcher ada. | 1 |
| Field **waktu publish (UTC)** di `release.json` sebagai penanda versi. Dibutuhkan kalau versi client minimum di handshake atau tampilan versi di About dikerjakan. | 1 |
| Client **mencocokkan hash setiap file yang diunduh**. Kalau tidak cocok (misalnya Sync sedang berjalan), batalkan dan coba lagi nanti. | 2 |
| Update lokal di **folder versi baru**, lalu pindah pointer saat restart. Listrik mati di tengah update tidak merusak instalasi, dan updater tidak menimpa file yang terkunci. | 2 |
| **Satu exe** untuk peran launcher, installer (`--install`), apply (`--apply`, menunggu PID aplikasi keluar), repair, dan uninstall. | 2 |
| Instalasi **per user** di `%LocalAppData%` supaya update tidak butuh admin. Entri uninstall di `HKCU`. | 2 |
| Struktur registry: `HKCU\Software\Em\...` dengan subkey per pemilik (`Update\` milik launcher). Kebijakan IT opsional di `HKLM\Software\Policies\Em\...` yang juga mengunci isian dialog. Status instalasi (versi aktif, pointer) tetap berupa file, bukan di registry. | 2 |
| Launcher tanpa lokasi CDN yang valid tetap menjalankan versi terpasang. Hanya berhenti kalau belum ada instalasi sama sekali. | 2 |
| `release.json` ikut disalin ke folder client. Aplikasi membaca versinya dari sini, dan ketiadaannya berarti mode developer (`NotManaged`, update mati). | 2 |
| **Data milik user** (config, cache, log) tidak boleh berada di folder aplikasi, karena folder itu harus identik dengan `release.json`. | 2 |
| Bagian **in-app** (cek di latar belakang, unduhan, ikon animasi, notifikasi, restart yang menghormati data belum disimpan) dijadikan **tahap 3**. Tahap 2 cukup launcher yang mengecek update saat startup. | 2/3 |
| Tanda tangan `.sig` dan SHA-256 ditunda, tapi launcher pertama sudah menyiapkan verifikasinya (bagian 6.1). | 2 |
| Folder `./data/cdn` di-mount sebagai volume saat `Em.Api` dijalankan sebagai container. | Deploy backend |

## 8. Langkah berikutnya

1. ~~Susun **plan tahap 1 (Release Manager)**.~~ Sudah dieksekusi (plan diarsipkan). Dua hal di laporan ini berubah di sana: hash file menjadi **SHA-256** (bukan
   SHA-1 seperti R2), dan tanda tangan `release.json.sig` (ECDSA P-256) **sudah dikerjakan di tahap 1**.
   Format rilis kini dikunci di [doc/release-format.md](../release-format.md).
2. ~~Susun **plan tahap 2 (Launcher)**.~~ Sudah dieksekusi (plan diarsipkan) (6 sesi di branch `launcher-tahap2`, termasuk verifikasi end-to-end). Perubahan
   terhadap laporan ini:
   - launcher ditulis dalam **Rust** (`src/frontend/Launcher`, GUI Win32 lewat `winsafe`), bukan C# NativeAOT
     seperti usulan di bagian 6.4;
   - **tanpa public key bawaan** di launcher maupun aplikasi (bagian 6.1 mengusulkan menanamnya): key diimpor dari
     file `*.pem` saat setup, lewat `--import`, jendela maintenance, atau kebijakan HKLM;
   - key tepercaya **disimpan di registry** (`...\Launcher\TrustedKeys`);
   - root registry adalah **`HKCU\<ApplicationName>`**, satu root dengan pengaturan aplikasi (`HKCU\<ApplicationName>\Launcher`),
     bukan `HKCU\Software\Em\...`; kebijakan IT di `HKLM\Software\Policies\<ApplicationName>\Launcher`;
   - launcher selalu dijalankan lebih dulu dan menawarkan update saat startup (*Update now / Later*), sedangkan
     cek dan unduhan di dalam aplikasi (L3) tetap tahap 3.
3. Tentukan apakah bagian in-app menjadi tahap 3 tersendiri.
