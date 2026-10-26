# Perbaikan fondasi UI MAUI dan panel My Tasks

Tanggal: 26 Oktober 2026 (Asia/Jakarta).
Status: **implementasi selesai; validasi perangkat tertunda**.
Repo: engine publik; branch kerja `work-bench`.
Baseline source saat penyusunan: `a4a6788`; working tree engine bersih.
Sumber: [ide fondasi MAUI](../../doc/ideas/maui-m3-expressive.md).

## 1. Tujuan dan batas pekerjaan

Perbaiki keterbacaan, aksesibilitas, adaptive layout, fungsi pencarian, state dan
respons interaksi MAUI. Pisahkan daftar pekerjaan pengguna dari katalog modul:
Home hanya memuat modul; My Tasks berada pada panel tersendiri dari app bar.
Panel dan katalog harus tetap nyaman ketika jumlah jenis tugas/modul bertambah.

Lingkup kode utama: `src/shared/Em.Ui.Maui.Core/`. `Em.Ui.Core` hanya disentuh bila
pasangan warna semantik memang memerlukan kontrak theme bersama; jangan mengubah
aturan approval, API, schema, claim atau navigasi WPF untuk pekerjaan ini.
Tidak menambah paket eksternal, mengganti font/icon, menginstal workload, membuat
push notification, merilis NuGet, memasang APK, atau melakukan deployment.
Plan integrasi konsumen berada di repo pemiliknya; identitas/tautan repo privat
tidak disalin ke dokumen publik ini.

## 2. Keputusan dan Mockup UI

- **26 Oktober 2026:** pengguna meminta panel My Tasks sendiri, tanpa daftar
  My Tasks pada Home, karena kedua daftar sulit dipindai saat sama-sama banyak.
- **26 Oktober 2026:** pengguna memilih: “Langsung perbaiki UI asli sebagai
  pengecualian mockup, memakai spesifikasi audit.” Keputusan mencakup UI MAUI
  dalam topik ini; bukan pengecualian umum untuk fitur lain.
- **Mockup UI: Tidak ada** mockup MAUI yang disetujui untuk shell, login,
  Home, My Tasks atau komponen bersama dalam topik ini. Implementasi mengikuti
  spesifikasi tertulis pada plan ini dan pengecualian pengguna di atas.
- Sebelum eksekusi, baca lagi instruksi terbaru dan periksa ulang status mockup.
  Bila ternyata ada desain yang disetujui dan bertentangan, ikuti aturan konflik
  mockup. Jangan menganggap pengecualian mockup sebagai izin uji GUI.
- Permintaan saat ini adalah penyusunan plan. Kode baru ditulis sesudah pengguna
  menyetujui eksekusi; GUI/perangkat tetap mengikuti otorisasi tersendiri.

## 3. Peta source dan temuan

| Area | Source existing | Pekerjaan |
| --- | --- | --- |
| Warna dan ukuran | `Styles/Palette.xaml(.cs)`, `Cards.xaml`, `Buttons.xaml`, `Navigation.xaml` | F01/F02/F09: pasangan foreground, target, token size/shape |
| Typography/input | `Styles/Typography.xaml`, `Inputs.xaml`, `Navigations/LoginControl.xaml(.cs)` | F06/F08: role teks dan kontrak field |
| Interaksi | `Controls/IconButton.cs`, `Navigations/AccountPanel.xaml(.cs)` | F03/F10: role/action/state, fokus dan motion |
| Shell | `Navigations/SpaNavigationHost.xaml(.cs)` | F02–F04/F10: panel tugas, app bar dan pengelolaan overlay |
| Home | `Navigations/DefaultHomeControl.xaml(.cs)`, `MenuGroupView.xaml(.cs)` | F05: pencarian nyata dan katalog terpisah |
| Sumber tugas | `Core/EmApp.HubTasks.cs`, `ApprovalWrapperCatalog` yang dikonsumsi | Reuse refresh, jumlah dan routing; tidak mengganti scope |
| Review | `Navigations/ApprovalViewer.xaml(.cs)` | F03/F04/F08: teks, state, fokus dan action area |

Source saat ini menempatkan My Tasks di atas modul pada satu ScrollView. Tombol
inbox mengarah ke manager untuk admin/debug dan Home untuk akun biasa; tombol
hilang saat jumlah nol. Ini perilaku awal yang sengaja diganti oleh panel tugas.
`HubTasks` berisi ringkasan per jenis pekerjaan, bukan daftar setiap request.

## 4. Kontrak UI yang akan diterapkan

### 4.1 Home dan My Tasks

1. Home menampilkan `Search modules`, clear dan daftar destinasi yang lolos
   `CanOpen`/katalog. Tidak ada kartu, daftar, loading atau error My Tasks di Home.
   Hapus subscription tugas dari Home; tugas tetap dimuat oleh layanan shell.
2. Search lokal atas title, subtitle yang tersedia dan jalur grup yang tampil;
   trim dan pencocokan case-insensitive. Pertahankan urutan/menu hierarchy
   existing. Parent grup hasil harus terlihat; tidak mengekspos destinasi terlarang.
   Query dan posisi yang masih valid dipertahankan saat kembali; sesi berganti
   mengosongkan state pengguna sebelumnya. Bedakan no-result dari katalog kosong.
3. Daftar modul memakai virtualisasi bila panjang; jangan menaruh CollectionView
   pada ScrollView yang membuat pengukuran tak terbatas. Reuse data grup; bila
   perlu, proyeksikan header/baris yang terlihat ke satu daftar virtual.
4. Tombol **My Tasks** tersedia setelah sign-in pada shell yang menampilkan
   aksi akun, termasuk saat nol/error. Badge hanya saat jumlah positif; `99+`
   adalah format visual, nama aksesibel menyebut jumlah sebenarnya. Nol saat
   load belum selesai tidak diumumkan sebagai tidak ada tugas.
5. Admin/debug dan akun biasa membuka panel My Tasks yang sama. Akses alat
   administrasi tetap di jalur existing dan tetap diperiksa haknya.
6. Panel membuka dari kanan pada medium/expanded; lebar target 400 logical units
   dibatasi available window/insets. Compact <600 dp memakai seluruh area
   konten tersedia. Ini satu panel adaptif, bukan katalog tugas kedua pada Home.
7. Header tetap: `My Tasks`, jumlah dan Refresh/Close. Body virtual berisi
   ringkasan jenis pekerjaan existing (judul, konteks, jumlah), dengan loading,
   empty, error/retry dan stale beserta waktu refresh berhasil terakhir.
   Jangan mengarang timestamp/count bila belum diketahui.
8. Aktifkan baris melalui `OpenHubTaskAsync` dan wrapper catalog existing;
   jangan menambahkan filter atau route khusus produk. Setelah navigasi sukses
   tutup panel; bila ditolak, pertahankan konteks dan jelaskan penolakannya.
9. Pakai cache/refresh coalescing `EmApp.HubTasks` yang sama dengan badge;
   pertahankan polling foreground/resume existing. Refresh gagal mempertahankan
   data lama dengan penanda stale. User/connection/session berubah membuang data
   lama dan respons terlambat; tidak ada kebocoran antar akun.

### 4.2 Shell, aksesibilitas, warna dan motion

- Back dan konteks title diprioritaskan. Pada compact, Home/Reload masuk overflow
  berlabel; My Tasks dan akun tetap terjangkau. Login tidak menampilkan chrome
  untuk pengguna yang belum sign-in. Overflow juga tunduk pada hak dan semantics.
- Satu overlay aktif: My Tasks, akun atau overflow. Membuka yang lain menutup
  yang lama; Back/Escape/scrim menutup overlay terlebih dahulu. Fokus masuk ke
  panel, tidak lolos ke belakang, dan kembali ke pembuka saat ditutup.
- Area interaktif minimum 48×48 dp; avatar/glyph boleh lebih kecil. Gunakan
  minimum height untuk teks, bukan tinggi tetap yang memotong font besar.
- Badge memakai `error/onError`; chip primary/secondary container memakai pasangan
  on yang sesuai pada dua tema. Hitung kontras termasuk opacity: teks biasa
  minimal 4,5:1, indikator esensial 3:1. Uji hasil branding/theme aktual.
- Kontrol kustom mempunyai name/role/action/state dan keyboard activation sesuai
  platform; glyph dekoratif tidak dibaca. Pertahankan binding Command dan API
  pemakai `IconButton` atau sediakan migrasi seluruh pemakai yang terdampak.
- Command dijalankan tanpa menunggu animasi kosmetik; busy/can-execute/guard
  mencegah dispatch ganda selama aksi/transisi yang sama, lalu pulih setelah
  selesai. Jangan memakai debounce waktu sebagai pengganti guard operasi.
- Motion policy bersama menghormati animation scale/reduced motion Android,
  mendukung interruption/cancellation dan memastikan scrim pulih pada close,
  navigation, release dan recreation. Default tidak menambah physics library;
  gunakan easing yang didokumentasikan sebagai easing, tanpa klaim physics.

### 4.3 Input, typography, shape dan feedback bersama

- Field bersama memuat label permanen, value/help/error serta focused, error,
  disabled dan read-only. Pilihan awal outlined; kesalahan tidak dibedakan hanya
  oleh warna. Hubungkan label/error secara native, bukan hanya posisi visual.
- Login: keyboard account tanpa autocorrect, password show/hide aksesibel,
  paste/password manager tetap bekerja, error field untuk input kosong dan error
  autentikasi umum tetap umum. Branding/form/CTA dapat scroll/reflow saat IME,
  landscape atau font besar. Pertahankan simulasi login dan Return to SD.
- Petakan role typography dan alias style existing agar pemakai tidak pecah;
  sertakan weight/tracking/line height yang dipetakan ke MAUI dengan benar.
  Pertahankan Open Sans. Identitas/jumlah/status keputusan tidak diredupkan
  seperti metadata sekunder. Token spacing 4–48 dan shape 8/12/16/28/full
  adalah profil proyek, bukan klaim semua komponen mengikuti angka Google itu.
- Tombol primary, secondary, selection dan status memiliki hierarki konsisten;
  satu fokus utama per konteks, selected juga memiliki indikator non-warna.
- Sediakan komponen feedback/progress serta panel hasil batch bersama bila
  diperlukan integrasi konsumen: info, success, partial, error dan stale terpisah;
  ringkasan tetap dan daftar hasil virtual dapat dibuka kembali selama sesi layar.
  Kontrak presentasi generik; pemakai memasok label bisnis dan hasil per item.
- Viewer menjaga identitas/current step, signer, komentar, status lampiran/PDF
  dan action area yang reflow. Jangan mengubah hak atau payload keputusan.

## 5. Urutan implementasi sesudah persetujuan

Seluruh kode engine E1–E4, lalu seluruh kode plan konsumen dituntaskan sebelum
build, tes, render atau review integrasi. Jangan menjalankan tes di sela tahap.
Penelusuran source untuk menulis kode tetap boleh; persetujuan plan lintas repo
mencakup urutan ini. Setelah seluruh kode selesai, lakukan verifikasi dan perbaikan.

- [x] **E1:** palette/on roles, typography, spacing/shape/button aliases, field
  bersama serta status/result presentation yang diperlukan; dokumentasikan API baru.
- [x] **E2:** kontrol aksesibel, target hit, motion policy dan guard transisi.
- [x] **E3:** panel My Tasks, overlay/fokus/back, app bar/overflow, Home tanpa
  tugas serta search/katalog virtual; lifecycle refresh dan pergantian akun.
- [x] **E4:** login, account dan viewer mengonsumsi komponen baru; tulis tes
  fungsi bermakna dan guide. Jangan mengubah navigasi WPF secara tidak sengaja.
- [x] **E5:** setelah kode konsumen selesai, build/regresi, review hasil dan
  koreksi; catat bagian GUI/perangkat yang belum diotorisasi.

## 6. Verifikasi dan acceptance

Build/test melalui RunBay pada runner engine existing; sebelum membuat action,
periksa `list_apps/get_app`. Script pelaksana `.ps1` disimpan pada action,
harness sementara/output engine di `../.artefacts/em-system/`; test yang dirawat
di `tests/`. Jangan mencetak credential atau menulis output uji ke repo.

| Bukti console/VM yang wajib | Lulus bila |
| --- | --- |
| Search dan akses | Search/clear/grouping/return konsisten; destinasi terlarang tidak muncul |
| My Tasks 0/1/100 jenis | Tersedia di shell; Home bebas tugas; baris memakai wrapper/izin existing |
| Refresh/race | Coalesced, failure stale, switch account/connection/logout/release menolak respons lama |
| Overlay dan command | Satu panel aktif, Back menutup panel, repeated input tidak menggandakan transisi/aksi |
| Field/status/kontras | State/error association dan tipe hasil benar; pasangan palette aktual lolos ambang |
| Thread/lifecycle | Callback worker dimarshal ke UI thread; unsubscribe/release tidak meninggalkan listener |
| Regresi engine | Build MAUI dan WPF yang terdampak; suite relevan, simulasi login dan routing tetap lulus |

Native role, fokus TalkBack, touch bounds, IME/insets dan animation scale 0 harus
dibuktikan pada Android; shim/tes console tidak menggantikannya. Matriks target:
320/360/600/840 dp, portrait/landscape/split window, font 100/150/200%, light/dark,
TalkBack/keyboard/switch yang didukung, daftar panjang dan back dari viewer.
**Default plan: GUI/perangkat tidak dijalankan tanpa izin eksplisit.** Bila belum
diotorisasi, laporkan kriteria tersebut belum diverifikasi; jangan tutup temuan
runtime atau mengklaim kepatuhan penuh M3 Expressive.

## 7. Dokumentasi, fallback dan penutupan

- Perbarui guide MAUI/navigasi/aksesibilitas di `doc/engine/` (bahasa Inggris),
  komentar public API dan catatan migrasi alias/komponen. Jika paket akan dirilis,
  ikuti `doc/ReleaseNote/README.md`; plan ini tidak menaikkan versi atau menerbitkan paket.
- Tidak menambahkan dependency bila handler platform cukup. Keterbatasan
  framework tidak boleh ditutupi oleh Description atau klaim semantics lulus;
  catat gap yang spesifik dan tandai acceptance perangkat belum lulus.
- Jika API/tokens bersama kurang, selesaikan generik di engine; jangan memindah
  data/aturan privat ke repo publik. Perubahan kontrak bisnis/API server di luar scope.
- Tindakan ditolak policy tidak diulang lewat jalur lain; ikuti skrip manual
  proyek. Masalah RunBay dicatat di repo alat tersebut, bukan sebagai fitur UI ini.
- Baseline/commit mengikuti izin per repo; push, tag, pack dan publish menunggu
  permintaan terpisah. Laporan memuat file, hasil tes, action/run id dan risiko.
- Setelah implementasi yang disetujui selesai, pindah plan ke `plan/executed/`
  dan perbarui ide. Tulis **implementasi selesai; validasi perangkat tertunda**
  bila itu keadaan sebenarnya; daftar temuan hanya ditutup dengan bukti yang tepat.

## 8. Default untuk persetujuan

Menyetujui eksekusi berarti menyetujui kontrak panel adaptif, search lokal,
hasil batch berupa panel dalam sesi layar, kompatibilitas alias existing dan
tanpa dependency baru di atas. Mockup dikecualikan oleh pengguna. Tidak ada
keputusan desain wajib lain yang masih kosong; device/usability baseline dan
target performa rilis tetap pekerjaan validasi terpisah setelah diotorisasi.

## 9. Status penyusunan

Plan dan pencatatan keputusan selesai; belum ada source UI yang diubah.
Pemeriksaan dokumentasi melalui RunBay pada 26 Oktober 2026 lulus (exit 0):
tautan lokal, code fence dan whitespace; `git diff --check` engine juga lulus.
Tidak ada build aplikasi, render, tes native atau klaim runtime yang dijalankan
untuk penyusunan plan. Eksekusi menunggu persetujuan pengguna.

## Execution — 27 October 2026

Execution was approved on 26 October and scheduled for 01:30 Asia/Jakarta.
E1–E4 were committed before consumer implementation; verification ran afterward.
E5 completed console review and corrections. Native build was attempted but blocked
by NETSDK1147 (missing wasm-tools); no workload was installed. The consuming host
also lacks its pinned SDK. Android compilation and device criteria remain pending.

RunBay `em-system / verify-maui-ui-ux`, run
`20261027-020153-verify-maui-ui-ux-df1c`: 80 core tests, 319 WPF tests,
20 source-linked mobile logic checks and 34 source/contrast checks passed.
Controls also compiled independently against the installed MAUI references;
this is not an Android build. Earlier runner/fixture corrections are retained
in RunBay history. No GUI, package release, installation or deployment ran.

Guide: [MAUI experience](../../doc/engine/engine-maui-experience.md).
Temporary harnesses/logs: `../.artefacts/em-system/scripts/maui-ui-ux-smoke/`
and `maui-ui-ux-controls/`. Device focus, keyboard, bounds, large text, insets,
IME, animation scale zero and performance remain unverified.
