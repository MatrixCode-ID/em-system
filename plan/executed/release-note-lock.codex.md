# Plan — Release note lock di Container Manager (Publish) dan Release Manager (untuk Codex)

Status: **selesai dieksekusi 2026-10-26**

Laporan: [doc/report/release-note-lock.md](../../doc/report/release-note-lock.md).
Seksi A–E selesai; build WPF/MAUI, suite penuh 349 tes, tes launcher dan smoke console dijalankan lewat RunBay.
Verifikasi visual serta Push/Sync ke server nyata tetap tertunda; rincian hasil dan keputusan implementasi ada pada laporan.

Dibuat: 2026-10-23
Diperbarui: 2026-10-26. Plan ditulis ulang setelah Platform Android Release Manager (2026-10-25) dan
keputusan pengguna 2026-10-26: lock hanya penjaga di UI, versi tetap dari aplikasi, `version` masuk kontrak.

Pembaca: Codex. Berkas ini memuat semua keputusan yang dibutuhkan; jangan bertanya ke pengguna selama
eksekusi. Hal yang tidak tercakup diputuskan dengan pilihan yang paling konsisten dengan plan ini, lalu
dicatat di laporan eksekusi (bagian 9).

Sebelum mulai, baca versi terbaru di disk:

- `CLAUDE.md` di root repo (aturan bersama Claude Code dan Codex), terutama bagian editor profile Release
  Manager, tes lewat RunBay, dan Rilis MAUI Android.
- `doc/release-format.md` (kontrak format rilis; diubah **lebih dulu** sebelum kodenya), termasuk 2.5
  Metadata Android dan 9 Contoh uji.
- `doc/convention/container-naming.md` (format versi standar).
- Guide yang akan diubah: `doc/engine/engine-registry-guide.md` dan `.id.md`, `doc/engine/engine-publish.md`
  dan `.id.md`, `doc/engine/engine-release-manager.md`, `doc/engine/engine-android-updates.md`,
  `doc/engine/build.md` (bagian versi produk).

---

## 1. Tujuan

Release note lock adalah **penjaga di UI**: rilis container dan aplikasi tidak bisa di-push/deploy tanpa
berkas release note untuk versi yang dirilis. Release note ditulis sebelum rilis (biasanya oleh agent) dan
di-commit di repo produk.

1. Opsi **Release note lock** di profil Container Manager (Publish) dan profil Release Manager.
2. **Versi rilis tetap dari sumber yang sekarang**: strip versi Publish (Tagging Standard) dan `Version`
   csproj host di Release Manager. Lock tidak mengubah cara versi ditentukan.
3. Saat lock on, rilis hanya bisa jalan bila folder release note berisi berkas `<versi>.md` untuk versi itu,
   formatnya benar, dan versinya lebih baru dari versi tertinggi di server/target.
4. **Riwayat release note** ditampilkan dari folder, tanpa database, dengan status Released/Pending.
5. Rilis Windows mencatat versi aplikasi di `release.json` (field `version`) agar status dan pemeriksaan
   "lebih baru" bisa dihitung.

**Lock off = perilaku sekarang**, tanpa perubahan apa pun. Profil lama terbaca dengan lock off.

---

## 2. Aturan eksekusi

- **Kode semua seksi dulu, tes dan review di akhir** (aturan root `CLAUDE.md`). Per seksi cukup pastikan
  build solution terkait lolos.
- **Build dan tes lewat RunBay** (aturan root `CLAUDE.md`, "Tes lewat RunBay"): pakai app RunBay em-system
  yang sudah terdaftar; periksa action yang ada dengan `get_app` sebelum menambah action baru. Script
  pelaksanaan `.ps1` disimpan di action RunBay, bukan di `.artefacts`. Laporkan nama action dan run id.
- **Commit di akhir** di branch `work-bench`, setelah build dan tes bagian 8 selesai (boleh beberapa commit:
  kode, perbaikan hasil tes, lalu pemindahan plan + laporan). Periksa branch aktif sebelum commit; bila bukan
  `work-bench`, jangan commit dan catat di laporan. **Tanpa push.** Pesan commit bahasa Inggris boleh (repo
  publik).
- **Uji visual (render GUI) tidak dijalankan.** Semua perubahan ada di layar yang sudah ada (halaman Publish,
  form profil Publish, editor profile Release Manager, halaman Release Manager); catat verifikasi visual
  sebagai tertunda. Uji console boleh langsung (`dotnet build`, `dotnet test`, smoke console di
  `..\.artefacts\em-system\scripts\<nama>-smoke\`).
- **Tindakan yang ditolak policy:** jangan diakali; siapkan skrip `.ps1` manual di
  `plan/release-note-lock-manual/` dan catat di laporan.
- **Bahasa:** kode, komentar, XML comment, teks UI, log, dan pesan error berbahasa Inggris. Plan, laporan, dan
  `CLAUDE.md` berbahasa Indonesia.
- **Product neutral:** contoh di kode, test, dan dokumen memakai `contoso/inventory-api`, `Contoso.Inventory`,
  `doc/ReleaseNote/inventory-api/`. Tidak ada nama produk turunan.
- **Tanpa database, tanpa action API baru.** Fitur ini di client WPF (`Em.Ui.Wpf.Core`) ditambah satu field
  opsional di kontrak `release.json` (kode format di `Em.Ui.Core/Release/Format/`).
- **Launcher Rust dan client Android MAUI tidak diubah.** Keduanya sudah mengabaikan field tak dikenal.
- **Gaya kode** mengikuti berkas yang disentuh (mis. `PublishView.xaml.cs` dan `Publisher.cs` memakai gaya
  ringkas; `Release/*` memakai gaya beridentasi). Aturan WPF root (`MvvmModelBase`, material design, display
  text eksplisit pada list/ComboBox, urutan editor profile Release Manager) berlaku untuk UI baru.

---

## 3. Keputusan final: berkas dan folder release note

### 3.1 Folder

- Profil menyimpan **satu folder release notes** (`ReleaseNotesFolder`), diatur **sekali** lewat tombol folder
  di form profil, **tanpa nilai default**. Halaman rilis hanya membacanya (teks baca saja, tanpa pemilih
  folder di halaman rilis). Mengganti folder hanya lewat edit profil.
- Folder itu **langsung** berisi berkas versi untuk container/aplikasi profil tersebut (tanpa subfolder
  otomatis per container atau `app_id`). Contoh: `doc/ReleaseNote/inventory-api/1.2.0-beta.3.md` di repo
  produk.
- **Path disimpan relatif terhadap folder berkas profile** (folder tempat `<id>.json` profile berada di
  profile store aktif), karena berkas profile bisa di-commit ke git dan dipakai di PC lain. Path ini tidak
  bergantung pada `SolutionPath`/`Workspace` profile, yang saat ini tersimpan absolut.
  - Selalu relatif, dengan pemisah `/` (mis. `../../inventory/doc/ReleaseNote/inventory-api`). Saat dibaca,
    `\` juga diterima.
  - Folder yang tidak bisa dinyatakan relatif (drive lain) **ditolak** di form profil dengan pesan; nilai
    absolut tidak disimpan. Nilai absolut yang ditemukan di profile (diedit manual) diperlakukan tidak sah:
    halaman rilis menampilkan pesan dan lock memblokir rilis.
  - Basis untuk profil Publish: folder berkas profile di `PublisherSettings.Profiles` (beserta subfolder
    jenisnya bila store memakainya). Basis untuk profil Release Manager: `ReleaseProfileStore.Root`.
  - Export/Import profile menyalin nilai apa adanya; setelah import, path diselesaikan terhadap folder
    profile hasil import. Catat di guide bahwa memindah profile ke folder lain bisa membuat path tidak
    menunjuk folder yang sama.
- Folder dimaksudkan di-commit ke repo produk. Tool **tidak** memanggil git dan tidak memberi peringatan untuk
  berkas yang belum di-commit.
- Lock on dengan folder kosong (belum diatur), tidak sah, atau tidak ada: halaman rilis menampilkan pesan dan
  aksi yang dijaga (5 dan 6) tidak bisa dijalankan.

### 3.2 Nama berkas = versi rilis

- Format versi mengikuti standar container (`ContainerVersion`, `container-naming.md`):
  `MAJOR.MINOR.PATCH-<channel>.N` untuk prerelease (`prealpha`, `alpha`, `beta`, `rc`; `N` ≥ 1), dan
  `MAJOR.MINOR.PATCH` untuk release. Nama berkas = versi + `.md`, mis. `1.2.0-beta.3.md`, `1.2.0.md`.
- **Berkas tidak menentukan versi.** Versi rilis tetap dari aplikasi (5 dan 6); lock mencari berkas yang
  namanya persis versi itu. Tidak ada daftar pilihan berkas.
- Versi aplikasi yang tidak berformat standar (mis. `1.2`, prerelease bebas) saat lock on: rilis diblokir
  dengan pesan bahwa lock memerlukan versi standar. Build metadata `+...` dibuang sebelum dicocokkan.

### 3.3 Berkas "format benar"

1. Nama berkas berformat versi pada 3.2 (berkas `.md` lain diabaikan diam-diam, mis. `README.md`).
2. Isi punya heading `## Summary` dengan teks tidak kosong sebelum heading berikutnya.

Baris lain (`Date`, `Previous`, `Source`, dst.) dianjurkan, diisi agent, tetapi **tidak dicek**. Template yang
dianjurkan (ditulis di guide):

```markdown
# inventory-api 1.2.0-beta.3

Date: 2026-10-23
Previous: 1.2.0-beta.2
Source: 0b18037

## Summary
One short paragraph.

## New features
- ...

## Fixes
- ...

## Upgrade notes
- ...
```

### 3.4 Syarat lock dan tampilan di halaman rilis

Saat lock on, aksi rilis yang dijaga hanya bisa jalan bila:

1. Folder sah dan ada (3.1).
2. Berkas `<versi-app>.md` ada dan format benar (3.3).
3. Versi app **lebih tinggi** daripada versi tertinggi di server/target (3.5). Urutan SemVer: prerelease lebih
   rendah dari release `A.B.C` yang sama; antar prerelease urut channel `prealpha < alpha < beta < rc` lalu
   `N` numerik.

Halaman rilis menampilkan:

- Status lock satu baris: versi app, berkas yang dipakai, dan alasan bila diblokir, mis.
  `Release note 1.2.0-beta.3.md found` atau
  `Blocked: 1.2.0-beta.3.md not found in doc/ReleaseNote/inventory-api` /
  `Blocked: 1.2.0-beta.3.md has no ## Summary` /
  `Blocked: 1.2.0-beta.3 is not newer than 1.2.0-beta.3 on the server`.
- Pratinjau isi berkas yang cocok, baca saja (teks markdown apa adanya, tanpa render): di tempat kotak
  **Release notes** pada Publish; di panel riwayat pada Release Manager (3.6).
- Tombol refresh membaca ulang folder dan versi server.

### 3.5 Versi di server/target

- **Container Manager:** versi tertinggi di antara tag registry yang berformat versi standar (tag mengambang
  dan tag non-standar diabaikan), memakai pembacaan tag yang sudah ada (`TakenTags`/`OciClient.Tags`), untuk
  target BuiltIn maupun Custom. Registry tidak terbaca → lock memblokir dengan pesan (tidak menebak).
  Repository yang belum ada (404) → belum ada versi.
- **Release Manager Windows:** field `version` di `release.json` target (bagian 4).
- **Release Manager Android:** field `version` bila ada; bila tidak ada, `android.versionName` **hanya bila**
  berformat versi standar (3.2, tanpa build metadata); selain itu dianggap belum ada versi.
- Target belum punya rilis atau tidak punya versi yang bisa dipakai → belum ada versi; semua versi app yang
  berkasnya valid lolos syarat 3.4.3. Target tidak terbaca → lock memblokir dengan pesan.
- **Dicek ulang tepat sebelum Push/Sync.** Bila sejak halaman dimuat server sudah punya versi yang sama atau
  lebih baru, atau berkas sudah hilang/tidak valid, Push/Sync dibatalkan dengan pesan dan status dimuat ulang.

### 3.6 Riwayat

- **Panel di halaman rilis**: Container Manager menambah bagian **Release notes** di halaman Publish; Release
  Manager menambah bagian sejenis di halamannya. Panel tampil bila profil lock on dan folder sah.
- Isi: semua berkas versi berformat nama benar di folder, urut versi menurun (ListBox dengan display text
  eksplisit = versi), dengan tanda:
  - **Released**: Container Manager bila tag persis ada di registry; Release Manager bila versi ≤ versi target
    (3.5).
  - **Pending**: selain itu.
  - **Invalid**: nama benar tetapi `## Summary` hilang/kosong (tetap tampil agar penulis tahu).
- Memilih satu berkas menampilkan isinya. Memilih rentang dua versi (From/To) menampilkan gabungan semua
  catatan di antaranya (eksklusif From, inklusif To), urut versi menaik, dipisah judul berkas.
- Tanpa database; sumbernya hanya folder (developer perlu `git pull` untuk catatan dari developer lain).

---

## 4. Keputusan final: kontrak `release.json`

- Tambah field opsional tingkat atas **`version`** (string) = versi aplikasi rilis, format
  `MAJOR.MINOR.PATCH[-(prealpha|alpha|beta|rc).N]` (3.2, tanpa build metadata), ikut ditandatangani seperti
  field lain. Ditulis di `doc/release-format.md` bagian 2.2 (struktur), dengan catatan di 2.5 untuk Android.
- Ditulis Release Manager **hanya bila lock on**, untuk Windows maupun Android. Lock off: field tidak ditulis
  (perilaku sekarang).
- Android: Release Manager menulis `version` sama dengan `android.versionName` tanpa build metadata. Parser
  **tidak** memeriksa kesamaan keduanya.
- Pembaca lama boleh mengabaikan field ini; tidak ada nomor versi format baru (seperti field `launcher`).
  Parser C# (`Em.Ui.Core/Release/Format/ReleaseManifestSerializer`) membaca dan memvalidasi formatnya bila
  ada (format salah → `ReleaseFormatException`). Launcher Rust dan client Android tidak diubah.
- **Vektor uji (keputusan pengguna 2026-10-26):** private key sampel lama sudah dibuang, jadi dibuat **kunci
  sampel baru khusus vektor `version`**, mengikuti pola vektor Android:
  - Buat kunci baru, tandatangani vektor, **buang private key-nya**, simpan public key sebagai
    `doc/release-format-samples/version-public-key.pem` (dengan baris `keyId: ...` seperti `public-key.pem`).
  - Vektor `version-valid/` (diterima) dan `version-bad-format/` (format `version` salah, ditolak parser),
    masing-masing `release.json`, `release.json.sig`, `binaries/` kecil.
  - Vektor lama dan kuncinya tetap byte-identik; tes launcher yang memakai `public-key.pem` tidak berubah.
  - Perbarui `expected.txt` dan tabel bagian 9 `doc/release-format.md`.
  - Generator dijalankan dari harness di `..\.artefacts\em-system\scripts\release-version-vectors\` (tidak
    masuk repo); private key tidak disimpan di mana pun.
- Urutan kerja: dokumen dan vektor **lebih dulu**, baru kode.

---

## 5. Keputusan final: Container Manager (Publish)

- Profil baru: `ReleaseNoteLock` (bool) dan `ReleaseNotesFolder` (string, relatif folder berkas profile, 3.1)
  di `PublishProfile`, tampil di tab **Advanced** form profil di dekat **Require Release Notes**, dengan help
  text (hover) yang menjelaskan basis path relatif.
- **Hanya mode tunggal:** lock hanya tersedia untuk mode **Dockerfile**, **LocalImage**, dan **Template**.
  Untuk **Set** dan **Compose** opsi dinonaktifkan dengan keterangan; profil Set/Compose yang tersimpan dengan
  lock on diperlakukan lock off dan form memberi keterangan. Step profil di dalam Set menjalankan Set seperti
  sekarang; lock step tunggal itu **tidak** berlaku saat dijalankan sebagai step Set (catat di guide).
- **Hanya Tagging Standard.** Profil Tagging Manual tidak bisa mengaktifkan lock (opsi nonaktif dengan
  keterangan).
- **Strip versi tetap dipakai** seperti sekarang. Versi yang dicocokkan = version tag hasil strip
  (`A.B.C` + channel + `N` otomatis dari registry, lewat `ContainerVersion`). Status lock diperbarui setiap
  kali strip versi atau target berubah.
- Lock on berarti release notes wajib (sama seperti **Require Release Notes** on, apa pun nilainya); kotak
  teks **Release notes** menjadi pratinjau baca saja isi berkas yang cocok.
- **Build, Push, dan Build & Push** tidak bisa dijalankan saat lock memblokir (3.4). Push dicek ulang sesuai
  3.5. Bila version tag artifact yang dibangun di sesi itu berbeda dari versi strip saat Push, Push ditolak
  dengan pesan untuk build ulang (strip diubah setelah Build).
- Cek release tag yang sudah ada di registry (aturan sekarang) tetap berlaku.
- `PublishRun` menyimpan **path berkas** release note (relatif folder berkas profile, sama seperti
  `ReleaseNotesFolder`) di field baru `ReleaseNotesFile`; `ReleaseNotes` dikosongkan untuk run yang memakai
  lock. Run detail di tab Riwayat menampilkan path itu. Run lama tetap terbaca.
- Prepare/Check tidak terpengaruh lock.

---

## 6. Keputusan final: Release Manager

- `ReleaseProfile` baru: `ReleaseNoteLock` (bool) dan `ReleaseNotesFolder` (string, relatif
  `ReleaseProfileStore.Root`, 3.1). Diatur di editor profile (`ReleaseSettingsDialog`) pada bagian
  **Product Identity**, tepat setelah field versi, dengan tombol folder dan help text yang terlihat (aturan
  editor profile root `CLAUDE.md`). Berlaku untuk Platform Windows dan Android. `FormatVersion` profil tidak
  dinaikkan bila field baru opsional dengan default lock off; bila serializer profil menolak field tak
  dikenal, naikkan dan catat di laporan.
- **Versi app** = `Product.Version` profile tanpa build metadata, yang sudah dibaca dari csproj host
  (Windows lewat editor profile/identity targets, Android lewat `AndroidReleaseBuilder` yang juga mengisi
  `PreparedVersionName`). Cara versi ditentukan dan dikirim ke build **tidak diubah**.
- **Prepare** tidak bisa dijalankan saat lock memblokir (3.4) untuk versi app saat itu. Untuk Android,
  periksa versi setelah identity host dibaca ulang dan sebelum build.
- **Sync** tidak bisa dijalankan saat lock memblokir, dicek ulang sesuai 3.5, dengan versi yang dicocokkan:
  - Windows: `Product.Version` profile.
  - Android: `Android.PreparedVersionName` (versi APK yang sudah di-Prepare). Bila berbeda dari
    `Product.Version` saat ini, Sync ditolak dengan pesan untuk Prepare ulang.
- Sync menulis `version` (bagian 4) ke `release.json` yang ditandatangani, hanya bila lock on.
- **Tidak ada** pemeriksaan versi exe host di publish folder dan tidak ada field versi Prepare baru untuk
  Windows (keputusan 2026-10-26: lock tidak mengubah alur yang ada).
- Compare dan Verify tidak dijaga lock. Verify menampilkan `version` target bila ada.
- Channel/versi launcher (setting launcher di profile) **tidak berubah** dan terpisah dari versi aplikasi.
- Target Folder dan CDN berlaku sama.

---

## 7. Seksi kerja

### Seksi A — Kontrak dan dokumen lebih dulu

1. `doc/release-format.md`: field `version` di 2.2, catatan Android di 2.5, catatan pembaca lama, tabel
   vektor di 9.
2. `doc/release-format-samples/`: `version-public-key.pem`, `version-valid/`, `version-bad-format/`,
   `expected.txt` (4).

### Seksi B — Model dan logika bersama

1. `Em.Ui.Wpf.Core/Publish/ContainerVersion`: parse versi standar dari string lengkap (termasuk `N`, buang
   `+metadata`) dan compare SemVer, dipakai Publish dan Release Manager.
2. Kelas baru tanpa ketergantungan WPF (mis. `Em.Ui.Wpf.Core/ReleaseNotes/ReleaseNoteFolder.cs`) untuk:
   resolusi dan konversi path relatif terhadap folder profile (3.1, tolak drive lain/absolut), membaca
   folder, validasi nama + `## Summary`, evaluasi syarat lock untuk satu versi app terhadap versi server
   beserta pesan blokir, daftar riwayat dengan Released/Pending/Invalid, dan gabungan rentang.
3. `Em.Ui.Core/Release/Format`: `ReleaseManifest.Version` opsional; serializer menulis bila ada, membaca dan
   memvalidasi formatnya.

### Seksi C — Container Manager (Publish)

1. `PublishProfile`: `ReleaseNoteLock`, `ReleaseNotesFolder`; `PublishRun`: `ReleaseNotesFile`.
2. Form profil tab Advanced: switch + folder (tombol folder, konversi relatif, tolak drive lain), aturan
   nonaktif untuk Set/Compose/Tagging Manual, help text.
3. `PublishView`: status lock dari versi strip, pratinjau di tempat kotak Release notes, refresh, guard
   Build/Push/Build & Push, cek ulang server sebelum Push, cek version tag artifact vs strip, panel riwayat
   **Release notes**.
4. `Publisher`: validasi lock (`ValidateReleaseNotes` diperluas atau metode baru), penyimpanan
   `ReleaseNotesFile` ke run, run detail.

### Seksi D — Release Manager

1. `ReleaseProfile`: `ReleaseNoteLock`, `ReleaseNotesFolder`; `ReleaseSettingsDialog`: switch + folder di
   Product Identity.
2. `ReleaseManager` view model/XAML: status lock, refresh, panel riwayat, guard Prepare/Sync, cek ulang target
   sebelum Sync, aturan versi Android (`PreparedVersionName`).
3. `ReleaseSync`: menulis `version` ke manifest saat lock on (Windows dan Android).
4. `ReleaseVerifier`/tampilan Verify: menampilkan `version` bila ada.

### Seksi E — Guide

1. `doc/engine/engine-registry-guide.md` dan `.id.md`: opsi lock di Advanced, status lock dan panel riwayat di
   Publish, batasan mode tunggal dan Tagging Standard, pesan blokir di tabel troubleshooting.
2. `doc/engine/engine-publish.md` dan `.id.md`: ringkasan lock dan `ReleaseNotesFile` di riwayat.
3. `doc/engine/engine-release-manager.md`: lock di editor profile, syarat Prepare/Sync, `version` di
   `release.json`.
4. `doc/engine/engine-android-updates.md`: `version` opsional di samping `android.versionName`; client tidak
   memakainya.
5. `doc/engine/build.md` bagian versi produk: lock memerlukan versi standar dan berkas `<versi>.md`.
6. Bagian bersama (di salah satu guide, ditautkan dari yang lain): format nama berkas, syarat `## Summary`,
   template 3.3, basis path relatif folder profile (termasuk efek Export/Import dan profile di git), dan alur
   kerja dengan agent (agent membuat berkas di folder repo, pengguna menjalankan Push/Sync).

---

## 8. Pengujian (setelah semua seksi selesai)

Semua build/tes lewat RunBay (bagian 2).

1. Build `src/frontend/Em.Ui.Wpf.slnx` dan `src/frontend/Em.Ui.Maui.slnx` (karena `Em.Ui.Core` tersentuh),
   serta `src/backend/Em.Api.slnx` bila `Em.Libs` tersentuh.
2. `dotnet test src/frontend/Em.Ui.Wpf.slnx`. Tambah test untuk: parse/compare versi (prerelease vs release,
   urutan channel, `N` numerik dua digit, build metadata dibuang, versi non-standar), path relatif folder
   profile (relatif, `..`, `\` vs `/`, drive lain ditolak, nilai absolut tidak sah), validasi berkas (nama
   salah, `## Summary` kosong/hilang, berkas non-versi diabaikan), syarat lock (berkas tidak ada, invalid,
   versi server tanpa/sama/lebih baru/lebih lama), versi target Android (`version`, fallback
   `versionName` standar dan non-standar), status Released/Pending/Invalid, gabungan rentang, serializer
   `release.json` dengan/tanpa `version` dan format salah, vektor `version-*` dan vektor lama tetap lulus,
   profil lama tanpa field baru (lock off), serta guard: Set/Compose/Manual tidak bisa lock.
3. Tes launcher Rust (`cargo test` di `src/frontend/Launcher`) dijalankan untuk memastikan vektor lama tetap
   lulus; vektor `version-valid` dicoba bila tes launcher mudah diarahkan ke kunci tambahan, bila tidak catat
   di laporan.
4. Smoke console opsional di `..\.artefacts\em-system\scripts\release-note-lock-smoke\` (folder sementara
   berisi berkas contoh → status lock, riwayat; Release Manager target Folder sementara → Sync menulis
   `version`). Yang butuh registry/API nyata dicatat tertunda.
5. **Uji visual tidak dijalankan**; daftar layar yang tertunda di laporan (form profil Advanced, halaman
   Publish, editor profile Release Manager, halaman Release Manager, panel riwayat).

---

## 9. Laporan eksekusi dan selesai bila

Selesai bila seksi A–E selesai dan di-commit di `work-bench`, build dan test bagian 8 lolos atau kegagalannya
dijelaskan, guide cocok dengan kode, dan plan dipindah ke `plan/executed/` bersama laporannya.

Tulis `doc/report/release-note-lock.md` (bahasa Indonesia): ringkasan per seksi, commit yang dibuat, hasil
build/test (dengan angka dan action/run id RunBay), keputusan implementasi yang diambil sendiri, verifikasi
tertunda (visual, Push/Sync nyata ke registry/CDN), dan tindakan manual yang terblokir policy beserta
skripnya. Jangan menyatakan verifikasi tertunda sebagai lulus.
