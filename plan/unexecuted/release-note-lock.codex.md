# Plan — Release note lock di Container Manager (Publish) dan Release Manager (untuk Codex)

Status: **belum dieksekusi**

Dibuat: 2026-10-23
Pembaca: Codex. Berkas ini memuat semua keputusan yang dibutuhkan; jangan bertanya ke pengguna selama
eksekusi. Hal yang tidak tercakup diputuskan dengan pilihan yang paling konsisten dengan plan ini, lalu
dicatat di laporan eksekusi (bagian 9).

Sebelum mulai, baca versi terbaru di disk:

- `CLAUDE.md` di root repo (aturan bersama Claude Code dan Codex).
- `doc/release-format.md` (kontrak format rilis; diubah **lebih dulu** sebelum kodenya).
- `doc/convention/container-naming.md` (format versi standar).
- Guide yang akan diubah: `doc/engine/engine-registry-guide.md` dan `.id.md`, `doc/engine/engine-publish.md`
  dan `.id.md`, `doc/engine/engine-release-manager.md`, `doc/engine/build.md` (bagian versi produk).

---

## 1. Tujuan

Rilis container dan aplikasi desktop bisa dilacak perbedaannya lewat release note yang ditulis sebelum rilis
(biasanya dibuat agent) dan di-commit di repo produk:

1. Opsi **Release note lock** di profil Container Manager (Publish) dan profil Release Manager.
2. Saat lock on, halaman rilis menampilkan berkas release note yang **formatnya benar** dan **versinya lebih
   baru dari rilis di server/target**. Pengguna memilih satu berkas; **tanpa pilihan, Push/Sync tidak bisa
   dijalankan**.
3. **Nama berkas menjadi sumber utama versi rilis** (tag container; versi aplikasi di `dotnet publish`).
4. **Riwayat release note** ditampilkan dari folder, tanpa database.
5. Versi aplikasi ikut tertulis di `release.json` agar Release Manager bisa membandingkan.

---

## 2. Aturan eksekusi

- **Kode semua seksi dulu, tes dan review di akhir** (aturan root `CLAUDE.md`). Per seksi cukup pastikan
  `dotnet build` solution terkait lolos.
- **Commit di akhir** di branch `work-bench`, setelah build dan tes bagian 8 selesai (boleh beberapa commit:
  kode, perbaikan hasil tes, lalu pemindahan plan + laporan). Periksa branch aktif sebelum commit; bila bukan
  `work-bench`, jangan commit dan catat di laporan. **Tanpa push.** Pesan commit bahasa Inggris boleh (repo
  publik).
- **Uji visual (render GUI) tidak dijalankan.** Semua perubahan ada di layar yang sudah ada (halaman Publish,
  form profil, Release Manager, Release settings); catat verifikasi visual sebagai tertunda. Uji console boleh
  langsung (`dotnet build`, `dotnet test`, smoke console di `..\.artefacts\em-system\scripts\<nama>-smoke\`).
- **Tindakan yang ditolak policy:** jangan diakali; siapkan skrip `.ps1` manual di
  `plan/release-note-lock-manual/` dan catat di laporan.
- **Bahasa:** kode, komentar, XML comment, teks UI, log, dan pesan error berbahasa Inggris. Plan, laporan, dan
  `CLAUDE.md` berbahasa Indonesia.
- **Product neutral:** contoh di kode, test, dan dokumen memakai `contoso/inventory-api`, `Contoso.Inventory`,
  `doc/ReleaseNote/inventory-api/`. Tidak ada nama produk turunan.
- **Tanpa database, tanpa action API baru.** Fitur ini sepenuhnya di client WPF (`Em.Ui.Wpf.Core`) ditambah
  satu field opsional di kontrak `release.json`.
- **Gaya kode** mengikuti berkas yang disentuh (mis. `PublishView.xaml.cs` dan `Publisher.cs` memakai gaya
  ringkas; `Release/*` memakai gaya beridentasi). Aturan WPF root (`MvvmModelBase`, material design, display
  text eksplisit pada list/ComboBox) berlaku untuk UI baru.
- **Lock off = perilaku sekarang**, tanpa perubahan apa pun. Profil lama terbaca dengan lock off.

---

## 3. Keputusan final: berkas dan folder release note

### 3.1 Folder

- Profil menyimpan **satu folder release notes**, diatur **sekali** lewat tombol folder di form profil, lalu
  halaman rilis hanya membacanya (teks baca saja, tanpa pemilih folder di halaman rilis). Mengganti folder hanya
  lewat edit profil.
- Folder itu **langsung** berisi berkas versi untuk container/aplikasi profil tersebut (tanpa subfolder
  otomatis per container atau `app_id`). Contoh: `doc/ReleaseNote/inventory-api/1.2.0-beta.3.md`.
- Path disimpan **relatif**: terhadap workspace profil Publish (seperti path lain di profil Publish), dan
  terhadap folder solution (`SolutionPath`) untuk profil Release Manager. Path absolut yang dipilih lewat tombol
  folder dikonversi ke relatif bila berada di bawah folder dasarnya; bila tidak, simpan apa adanya (absolut).
- Folder ini dimaksudkan di-commit ke repo produk. Tool **tidak** memanggil git dan tidak memberi peringatan
  untuk berkas yang belum di-commit.
- Lock on dengan folder kosong (belum diatur) atau tidak ada: halaman rilis menampilkan pesan dan Build/Push/
  Prepare/Sync tidak bisa dijalankan.

### 3.2 Nama berkas = versi

- Format versi mengikuti standar container (`ContainerVersion`, `container-naming.md`):
  `MAJOR.MINOR.PATCH-<channel>.N` untuk prerelease (`prealpha`, `alpha`, `beta`, `rc`; `N` ≥ 1), dan
  `MAJOR.MINOR.PATCH` untuk release. Nama berkas = versi + `.md`, mis. `1.2.0-beta.3.md`, `1.2.0.md`.
- **Versi rilis diambil dari nama berkas yang dipilih.** Saat lock on, strip versi (A.B.C + channel dengan `N`
  otomatis) di halaman Publish tidak dipakai/disembunyikan; channel dan floating tag diturunkan dari versi
  berkas lewat `ContainerVersion` yang sama.

### 3.3 Berkas "format benar"

Berkas tampil di daftar pilihan bila:

1. Nama berkas berformat versi pada 3.2 (berkas `.md` lain diabaikan diam-diam, mis. `README.md`).
2. Isi punya heading `## Summary` dengan teks tidak kosong sebelum heading berikutnya.
3. Versinya **lebih tinggi** (urutan SemVer: prerelease lebih rendah dari release `A.B.C` yang sama; antar
   prerelease urut channel `prealpha < alpha < beta < rc` lalu `N` numerik) daripada **versi tertinggi di
   server/target** (3.5).

Baris lain (`Date`, `Previous`, `Source`, `Target`, dst.) dianjurkan, diisi agent, tetapi **tidak dicek**.
Template yang dianjurkan (ditulis di guide):

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

### 3.4 Daftar pilihan dan keterangan berkas yang dilewati

- Daftar pilihan (ComboBox dengan display text eksplisit = versi) berisi berkas yang lolos 3.3, urut versi
  menurun. Pilihan **otomatis hanya bila tepat satu berkas** lolos; selain itu kosong sampai pengguna memilih.
- Di bawahnya keterangan singkat jumlah berkas yang dilewati beserta alasannya, mis.
  `2 files skipped: 1.1.0.md (not newer than 1.1.0 on the server), 1.3.0.md (missing ## Summary)`.
  Berkas yang namanya bukan versi tidak dihitung.
- Pratinjau isi berkas terpilih ditampilkan baca saja (teks markdown apa adanya, tanpa render) di tempat kotak
  **Release notes** pada Publish; di Release Manager di panel riwayat (3.6).
- Tombol refresh membaca ulang folder dan versi server.

### 3.5 Versi di server/target

- **Container Manager:** versi tertinggi di antara tag registry yang berformat versi standar (tag mengambang
  dan tag non-standar diabaikan), memakai pembacaan tag yang sudah ada (`TakenTags`/`OciClient.Tags`), untuk
  target BuiltIn maupun Custom. Registry tidak terbaca → daftar kosong dengan pesan (lock tidak menebak).
  Repository yang belum ada (404) → belum ada versi, semua berkas valid tampil.
- **Release Manager:** field `version` di `release.json` target (bagian 4). Target belum punya rilis atau
  `release.json` belum punya field `version` → belum ada versi, semua berkas valid tampil. Target tidak
  terbaca → daftar kosong dengan pesan.
- **Dicek ulang tepat sebelum Push/Sync.** Bila sejak daftar dimuat server sudah punya versi yang sama atau
  lebih baru, Push/Sync dibatalkan dengan pesan dan daftar dimuat ulang.

### 3.6 Riwayat

- **Panel di halaman rilis**: Container Manager menambah bagian **Release notes** di halaman Publish; Release
  Manager menambah bagian sejenis di halamannya. Panel tampil bila profil lock on (folder diatur).
- Isi: semua berkas versi di folder (termasuk yang tidak lolos 3.3 karena sudah dirilis), urut versi menurun,
  dengan tanda **Released** bila versinya ≤ versi tertinggi di server (Container Manager: tag persis ada di
  registry → Released; Release Manager: ≤ `version` target) dan **Pending** bila belum.
- Memilih satu berkas menampilkan isinya. Memilih rentang dua versi (From/To) menampilkan gabungan semua
  catatan di antaranya (eksklusif From, inklusif To), urut versi menaik, dipisah judul berkas.
- Tanpa database; sumbernya hanya folder (developer perlu `git pull` untuk catatan dari developer lain).

---

## 4. Keputusan final: kontrak `release.json`

- Tambah field opsional tingkat atas **`version`** (string) = versi aplikasi rilis, format
  `MAJOR.MINOR.PATCH[-(prealpha|alpha|beta|rc).N]`, ikut ditandatangani seperti field lain.
- Ditulis Release Manager **hanya bila lock on** (dari nama berkas). Lock off: field tidak ditulis (perilaku
  sekarang).
- Pembaca lama boleh mengabaikan field ini; tidak ada nomor versi format baru (seperti field `launcher`).
  Parser C# (`ReleaseManifestSerializer`) membaca dan memvalidasi formatnya bila ada (format salah →
  `ReleaseFormatException`). Parser Rust launcher sudah mengabaikan field tak dikenal (serde tanpa
  `deny_unknown_fields`); **launcher tidak diubah**.
- Urutan kerja: perbarui `doc/release-format.md` (bagian 2.2) dan tambah vektor uji di
  `doc/release-format-samples/` (mis. `app-version.json` + expected) **lebih dulu**, baru kode.

---

## 5. Keputusan final: Container Manager (Publish)

- Profil baru: `ReleaseNoteLock` (bool) dan `ReleaseNotesFolder` (string, relatif workspace) di
  `PublishProfile`, tampil di tab **Advanced** form profil di dekat **Require Release Notes**, dengan help text.
- **Hanya mode tunggal:** lock hanya tersedia untuk mode **Dockerfile**, **LocalImage**, dan **Template**.
  Untuk **Set** dan **Compose** opsi dinonaktifkan dengan keterangan; profil Set/Compose yang tersimpan dengan
  lock on (mis. mode diganti) diperlakukan lock off dan form memberi keterangan. Step profil di dalam Set
  menjalankan Set seperti sekarang; lock step tunggal itu **tidak** berlaku saat dijalankan sebagai step Set
  (catat di guide).
- **Hanya Tagging Standard.** Profil Tagging Manual tidak bisa mengaktifkan lock (opsi nonaktif dengan
  keterangan).
- Lock on berarti release notes wajib (sama seperti **Require Release Notes** on, apa pun nilainya); kotak teks
  **Release notes** menjadi pratinjau baca saja isi berkas terpilih.
- Berkas dipilih **sebelum Build**. Build, Push, dan Build & Push tidak bisa dijalankan tanpa pilihan. Tag
  version = nama berkas; floating tag mengikuti `ContainerVersion.FloatingTags`. Tag disimpan ke profil seperti
  sekarang (`SaveVersionTag`) agar `APP_VERSION`/`-p:Version` build memakai versi berkas.
- Push ditolak bila pilihan berkas berubah sejak Build sesi itu (versi berkas ≠ version tag artifact yang
  dibangun), dengan pesan untuk build ulang.
- Cek release tag yang sudah ada di registry (aturan sekarang) tetap berlaku; berkas berversi sama dengan tag
  di registry memang sudah tersaring oleh 3.3.
- `PublishRun` menyimpan **path berkas** (relatif terhadap workspace) di field baru, mis.
  `ReleaseNotesFile`; `ReleaseNotes` dikosongkan untuk run yang memakai lock. Run detail di tab Riwayat
  menampilkan path itu. Run lama tetap terbaca.
- Prepare/Check tidak terpengaruh lock (tetap bisa jalan tanpa pilihan berkas), kecuali Build.

---

## 6. Keputusan final: Release Manager

- Profile JSON baru: `ReleaseNoteLock` (bool) dan `ReleaseNotesFolder` (string, relatif terhadap folder
  solution) di `ReleaseProfile`, diatur di dialog Release settings (tombol folder). `FormatVersion` profil
  tidak perlu dinaikkan bila field baru opsional dengan default lock off; bila serializer profil menolak field
  tak dikenal, naikkan dan catat di laporan.
- Berkas dipilih **sebelum Prepare**. Prepare tidak bisa dijalankan tanpa pilihan saat lock on. Prepare
  mengirim versi berkas sebagai versi host **lewat berkas identity targets yang sudah ada**
  (`WriteIdentityTargets`, `CustomBeforeMicrosoftCommonTargets`), **bukan** `-p:Version` global, agar library
  yang direferensikan tidak ikut berganti versi. Versi berkas menimpa `<Version>` csproj host.
- Sync tidak bisa dijalankan tanpa pilihan saat lock on. Sebelum Sync, Release Manager memastikan publish
  folder memang hasil versi itu: `ProductVersion`/informational version exe host di publish folder (tanpa
  suffix `+metadata`) harus sama dengan versi berkas terpilih; bila beda (pilihan berubah sejak Prepare atau
  publish folder lama), Sync ditolak dengan pesan untuk Prepare ulang.
- Sync menulis `version` (bagian 4) ke `release.json` yang ditandatangani.
- Compare dan Verify tidak memerlukan pilihan berkas. Verify menampilkan `version` target bila ada.
- Channel/versi launcher (setting launcher di profile) **tidak berubah** dan terpisah dari versi aplikasi.
- Target Folder dan CDN berlaku sama.

---

## 7. Seksi kerja

### Seksi A — Kontrak dan dokumen lebih dulu

1. `doc/release-format.md`: field `version` (bagian 4) di 2.2, catatan pembaca lama.
2. `doc/release-format-samples/`: vektor dengan `version` (valid) dan bentuk salah bila pola sampel yang ada
   mendukung kasus negatif.

### Seksi B — Model dan logika bersama (`Em.Ui.Wpf.Core`)

1. Perbandingan versi SemVer standar di `ContainerVersion` (parse dari string lengkap termasuk `N`, compare),
   dipakai Publish dan Release Manager. Unit test.
2. Kelas baru (mis. `Publish/ReleaseNoteFolder.cs` atau folder `ReleaseNotes/`) untuk: membaca folder,
   memvalidasi nama + `## Summary`, menyaring terhadap versi server, menyusun alasan berkas dilewati, daftar
   riwayat dengan status Released/Pending, dan gabungan rentang. Tanpa ketergantungan WPF agar bisa diuji
   console.
3. `ReleaseManifest`/`ReleaseManifestSerializer`: field `Version` opsional (tulis bila ada, baca + validasi).

### Seksi C — Container Manager (Publish)

1. `PublishProfile`: `ReleaseNoteLock`, `ReleaseNotesFolder`; `PublishRun`: `ReleaseNotesFile`.
2. Form profil tab Advanced: switch + folder (tombol folder, konversi relatif), aturan nonaktif untuk Set/
   Compose/Tagging Manual, help text.
3. `PublishView`: pilihan berkas pengganti strip versi saat lock on, keterangan berkas dilewati, pratinjau di
   tempat kotak Release notes, refresh, guard Build/Push/Build & Push, cek ulang server sebelum Push, cek
   pilihan vs artifact yang dibangun, panel riwayat **Release notes**.
4. `Publisher`: validasi lock (`ValidateReleaseNotes` diperluas atau metode baru), penyimpanan
   `ReleaseNotesFile` ke run, run detail.

### Seksi D — Release Manager

1. `ReleaseProfile`: `ReleaseNoteLock`, `ReleaseNotesFolder`; dialog Release settings: switch + folder.
2. `ReleaseManager` view model/XAML: pilihan berkas, keterangan, refresh, panel riwayat, guard Prepare/Sync,
   cek ulang target sebelum Sync, cek versi exe host di publish folder.
3. `ReleaseBuilder`: versi host lewat identity targets saat lock on.
4. `ReleaseSync`: menulis `version` ke manifest.
5. `ReleaseVerifier`/tampilan Verify: menampilkan `version` bila ada.

### Seksi E — Guide

1. `doc/engine/engine-registry-guide.md` dan `.id.md`: opsi lock di Advanced, pilihan berkas di Publish,
   panel riwayat, batasan mode tunggal dan Tagging Standard, pesan error baru di tabel troubleshooting.
2. `doc/engine/engine-publish.md` dan `.id.md`: ringkasan lock dan `ReleaseNotesFile` di riwayat.
3. `doc/engine/engine-release-manager.md`: lock, versi dari berkas, `version` di `release.json`, Sync guard.
4. `doc/engine/build.md` bagian versi produk: versi dari berkas release note saat lock on.
5. Bagian bersama (di salah satu guide, ditautkan dari yang lain): format nama berkas, syarat `## Summary`,
   template 3.3, dan alur kerja dengan agent (agent membuat berkas di folder repo, pengguna menjalankan
   Push/Sync).

---

## 8. Pengujian (setelah semua seksi selesai)

1. `dotnet build src/frontend/Em.Ui.Wpf.slnx` (dan `src/backend/Em.Api.slnx` bila `Em.Libs` tersentuh).
2. `dotnet test src/frontend/Em.Ui.Wpf.slnx`. Tambah test untuk: parse/compare versi (prerelease vs release,
   urutan channel, `N` numerik dua digit), validasi berkas (nama salah, `## Summary` kosong/hilang, berkas non-
   versi diabaikan), penyaringan terhadap versi server (tanpa versi, sama, lebih baru), alasan berkas dilewati,
   status Released/Pending, gabungan rentang, path relatif folder, serializer `release.json` dengan/tanpa
   `version` dan format salah, vektor `doc/release-format-samples/`, profil lama tanpa field baru (lock off),
   serta guard: Set/Compose/Manual tidak bisa lock.
3. Smoke console opsional di `..\.artefacts\em-system\scripts\release-note-lock-smoke\` (folder sementara berisi
   berkas contoh → daftar, alasan, riwayat; Release Manager target Folder sementara → Sync menulis `version`).
   Yang butuh registry/API nyata dicatat tertunda.
4. **Uji visual tidak dijalankan**; daftar layar yang tertunda di laporan (form profil Advanced, halaman
   Publish, Release settings, halaman Release Manager, panel riwayat).

---

## 9. Laporan eksekusi dan selesai bila

Selesai bila seksi A–E selesai dan di-commit di `work-bench`, build dan test bagian 8 lolos atau kegagalannya
dijelaskan, guide cocok dengan kode, dan plan dipindah ke `plan/executed/` bersama laporannya.

Tulis `doc/report/release-note-lock.md` (bahasa Indonesia): ringkasan per seksi, commit yang dibuat, hasil
build/test (dengan angka), keputusan implementasi yang diambil sendiri, verifikasi tertunda (visual, push/Sync
nyata ke registry/CDN), dan tindakan manual yang terblokir policy beserta skripnya. Jangan menyatakan
verifikasi tertunda sebagai lulus.
