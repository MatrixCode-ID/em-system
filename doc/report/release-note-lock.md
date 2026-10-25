# Laporan release note lock

Tanggal: 2026-10-26. Status: seksi A–E selesai. Branch: `work-bench`. Tanpa push.
Plan: [release-note-lock.codex.md](../../plan/executed/release-note-lock.codex.md).

## Hasil implementasi

- **A:** kontrak `release.json` memiliki `version` opsional yang ikut ditandatangani; dokumentasi dan dua vektor baru disiapkan sebelum kode. Public key baru tersimpan; private key hanya berada di memori dan dibuang. Manifest/signature vektor akhir memakai LF. Vektor dan public key lama tidak berubah.
- **B:** parser versi lengkap membuang metadata, membandingkan core/channel/N secara numerik, dan memakai BigInteger agar tidak membatasi angka SemVer. Logika folder bersama memvalidasi path relatif, nama berkas, Summary, versi target, status riwayat, dan rentang eksklusif From/inklusif To. Serializer memvalidasi `version` bila ada.
- **C:** profil Publish memiliki lock/folder, pemilih folder pada Advanced, serta pembatasan mode tunggal dan Standard tagging. Build/Push diperiksa di Publisher; Push memeriksa ulang target/berkas dan menolak versi artifact yang berubah. Halaman menyediakan status, pratinjau baca saja, refresh, dan riwayat. Run lock menyimpan `ReleaseNotesFile`, bukan salinan teks.
- **D:** editor Release Manager menampilkan versi dan lock/folder pada Product Identity. Prepare Windows diperiksa sebelum mengosongkan publish folder; Android diperiksa sesudah membaca ulang identity host. Sync memeriksa ulang target/berkas dan kecocokan PreparedVersionName Android, lalu menandatangani `version` hanya saat lock aktif. Verify menampilkan versi. Format profil tetap 2; field baru opsional dan profil lama default lock off.
- **E:** tujuh guide yang disebut plan diperbarui, termasuk template, basis direktori profil, efek Export/Import, batasan mode, riwayat dan troubleshooting.

## Build dan tes RunBay

Seluruh pelaksanaan menggunakan app RunBay `em-system` yang sudah terdaftar. Action lama diperiksa terlebih dahulu; action khusus berikut ditambahkan tanpa menggandakan action lama. Script PowerShell pelaksanaan tersimpan pada action RunBay. Harness/log berada di `../.artefacts/em-system/scripts/release-note-lock-smoke/`; generator di `../.artefacts/em-system/scripts/release-version-vectors/`.

| Action | Run id | Hasil |
| --- | --- | --- |
| `generate-release-version-vectors` | `20261026-050058-generate-release-version-vectors-81fb` | 2 vektor akhir dibuat; exit 0, status success. |
| `build-release-note-lock` | `20261026-045901-build-release-note-lock-bfcb` | WPF: 0 error, 406 warning; MAUI: 0 error, 3 warning; exit 0. |
| `test-release-note-lock-filtered` | `20261026-045526-test-release-note-lock-filtered-13ed` | 130 lulus, 0 gagal, 0 skip. |
| `test-release-note-lock-full` | `20261026-045931-test-release-note-lock-full-ea47` | Suite penuh dijalankan sekali: 349 lulus, 0 gagal, 0 skip; exit 0. |
| `smoke-release-note-lock` | `20261026-045736-smoke-release-note-lock-408f` | 6 pemeriksaan lulus: Windows Sync, signature/hash/Verify/version, status Released, penolakan versi sama, lock off, Android Sync. Exit 0. |
| `test-release-note-lock-rust` | `20261026-045752-test-release-note-lock-rust-d7b9` | `cargo test --locked`: 93 lulus, 0 gagal, 0 diabaikan; harness tambahan `version-valid`: 1 lulus. |
| `test-release-note-lock-vectors` | `20261026-050117-test-release-note-lock-vectors-77a2` | Setelah regenerasi LF: 10 tes C# lulus, 0 gagal/skip; 1 smoke Rust lulus. |

Pada dua run Rust/vektor terakhir, output seluruh pemeriksaan sudah selesai tetapi RunBay masih menandai running. `stop_run` menutup run milik agent tersebut; hasil akhirnya `exit_code: 0`, status `stopped`. Angka lulus di atas berasal dari output pemeriksaan, bukan dari klaim status success RunBay.

Build awal `20261026-045304-build-release-note-lock-b1fb` juga lulus. Percobaan terfilter pertama `20261026-045359-test-release-note-lock-filtered-f64d` menjalankan 129 tes, dengan 3 kegagalan folder tidak sah; diperbaiki dengan menangkap InvalidDataException. Satu kasus urutan angka besar kemudian ditambahkan, sehingga hasil akhir menjadi 130.

Smoke awal `20261026-045643-smoke-release-note-lock-cb2b` gagal compile karena import System.IO pada harness; percobaan `20261026-045650-smoke-release-note-lock-2915` melewati 5 pemeriksaan lalu gagal karena fixture belum mengisi AndroidTargetFramework. Kedua masalah fixture diperbaiki, percobaan ketiga lulus 6/6. Run Rust awal `20261026-045642-test-release-note-lock-rust-9600` memiliki 2 kegagalan parser expected.txt; koreksinya dijelaskan di bawah. Tidak ada kegagalan yang membutuhkan lebih dari tiga putaran perbaikan.

Generator awal `20261026-044701-generate-release-version-vectors-3b47` menghasilkan dua vektor dan exit 0, lalu ditutup saat RunBay tetap running. Vektor itu diganti dengan hasil generator LF akhir dan diuji ulang secara terfilter. Build backend tidak diperlukan karena Em.Libs tidak disentuh. Warning build tetap tercatat; build tidak dinyatakan bebas warning.

## Keputusan implementasi di luar rincian plan

1. Parser strip lama dipertahankan. Parser lengkap memakai nilai terpisah dengan angka BigInteger, sehingga perilaku input strip lama tidak berubah.
2. Riwayat dan gabungan rentang memakai satu kontrol bersama untuk kedua halaman, sedangkan validasi folder/versi tetap bebas WPF.
3. `expected.txt` ternyata dibaca tes Rust dengan grammar ketat: hanya baris keyId dan tiga kolom file. Teks penjelasan Android yang sudah ada serta tambahan penjelasan version tidak dapat dibaca parser itu. Teks tersebut dikeluarkan dari expected.txt; hasil/keyId vektor baru disimpan di `version-expected.txt`, dan penjelasan Android tetap di kontrak 2.5. Runtime maupun tes Rust tidak diubah. Signature, manifest, binary, dan public key vektor lama tetap identik.
4. Pembacaan target Folder pada guard menggunakan pembacaan file langsung, agar kesalahan izin tidak tersamar menjadi file yang belum ada. Target CDN tetap memakai pembaca yang sudah tersedia dan pemeriksaan manifest server.

## Commit

- `0bccfc0` — Add versioned release note locks to Publish and Release Manager.
- `eba2a77` — Normalize signed release version vectors to LF.
- Laporan dan pemindahan plan dibuat dalam commit penutupan terpisah setelah kedua commit implementasi tersebut.

Branch diperiksa sebelum setiap commit; seluruh commit hanya pada `work-bench`. Tidak ada push.

## Verifikasi tertunda

- Uji visual tidak dijalankan: form profil Publish Advanced, halaman Publish, editor profil Release Manager, halaman Release Manager, dan panel riwayat kedua layar.
- Push nyata ke registry BuiltIn/Custom, Sync nyata ke CDN, serta kondisi balapan antar developer di server nyata belum diuji. Smoke hanya memakai target Folder sementara dan fixture APK, bukan build/instalasi APK nyata.
- Tidak ada aplikasi GUI, emulator, perangkat, screenshot atau render PNG yang dibuka/dijalankan. Hal-hal tertunda ini tidak dinyatakan lulus.

## Tindakan manual terblokir policy

Tidak ada tindakan ditolak policy. Tidak ada skrip manual di `plan/release-note-lock-manual/` yang perlu disiapkan atau dijalankan pengguna.


## Refactor folder absolut (keputusan pengguna 2026-10-26)

Pengguna mengganti aturan path relatif profil menjadi folder tetap pada mesin.
Pembaca bersama, editor Container Publish Advanced dan editor Release Manager sekarang
menyimpan/memakai path absolut tanpa basis profil atau pembatasan drive yang sama.
Pemindahan/impor profil mempertahankan lokasinya; path relatif lama tidak ditebak atau
migrasi otomatis. Folder kosong, relatif, hilang, tidak valid, dan kegagalan baca
memblokir lock serta menampilkan lokasi/error dan instruksi memperbarui Release notes
folder di pengaturan profil. Run baru mencatat path absolut catatan. File profil dan
folder pengguna tidak dipindahkan atau diubah dalam pekerjaan ini.

Aturan baru dicatat pada `CLAUDE.md`; guide Publish, Registry dan Release Manager
beserta pasangan bahasa Indonesianya diselaraskan. Harness Folder Sync yang sudah
ada di artefak disesuaikan dengan API tanpa parameter basis profil dan memakai
path absolut untuk fixture.

Verifikasi melalui RunBay:

- `em-system / test-release-note-lock-filtered`, run
  `20261026-051259-test-release-note-lock-filtered-123b`: output akhir **138 lulus,
  0 gagal, 0 skip**. Mencakup lokasi absolut lintas drive/UNC, penolakan path relatif
  lama, folder hilang, file menggantikan folder, berkas terkunci, pemilihan ulang,
  round-trip profil, serta regresi versi/lock. Proses dotnet test sudah selesai;
  RunBay masih menandai running sehingga run milik agent ditutup lewat stop_run.
  Hasil metadata akhir exit 0/status stopped, bukan success.
- Build host pemakai melalui action Build yang sudah tersedia: run
  `20261026-051534-build-6f08`, **success, exit 0**, 0 error dan 384 warning.
  Build akhir mengonfirmasi source setelah normalisasi encoding; run awal
  `20261026-051358-build-caa9` juga lulus dengan hasil yang sama.
- `em-system / smoke-release-note-lock`, run
  `20261026-051433-smoke-release-note-lock-e849`: **success, 6 lulus, 0 gagal**;
  Windows/Android Folder Sync, tanda tangan/Verify, penolakan versi sama dan lock off.
- `git diff --check` lulus. Tidak ada commit/push, GUI, render, registry Push atau
  CDN Sync nyata. Verifikasi visual/interaksi tetap tertunda.

## Perbaikan tema tombol refresh (2026-10-26)

Pengguna melaporkan tombol Refresh release notes berlatar putih dengan teks pudar
pada tema gelap. Penyebab terverifikasi di source: tombol dibuat dari kode tanpa
style eksplisit sehingga memakai template Windows bawaan. Tombol sekarang memakai
`outlinedButtonStyle` bersama, foreground dinamis `themeWindowForegroundBrush`,
serta jarak atas 8 agar mengikuti kontrol Material di halaman Publish. Template
bersama menangani hover, pressed, focus dan disabled tanpa latar putih sistem.

Build host melalui RunBay `emporium-localtest / Build`, run
`20261026-051905-build-cc85`, success/exit 0, 0 error dan 384 warning.
`git diff --check` lulus. Tidak menambah tes untuk perubahan style sederhana ini;
render dan interaksi GUI belum dijalankan. Tidak ada commit/push.


## Pemilih berkas release Container Manager (2026-10-26)

Pengguna meminta strip angka versi/channel diganti ComboBox release file saat lock
aktif. ComboBox sekarang menampilkan nama `<versi>.md` beserta status
Pending/Released/Invalid dari folder absolut profil. Pemilihan menetapkan versi
persis dari nama berkas, menyimpannya ke profil dan memperbarui target, floating
tags serta pratinjau. Refresh mempertahankan versi tersimpan bila berkas masih
ada; tidak memilih berkas pengganti bila hilang. Build/Push membaca ulang berkas
dan registry serta tidak menghitung N otomatis saat lock aktif. Pemilihan berkas
lain setelah Build tetap membutuhkan Build ulang sebelum Push. Lock mati dan
mode Manual/Compose/Set mempertahankan perilaku sebelumnya. Release Manager tetap
mengikuti versi host. Aturan baru dicatat pada CLAUDE.md dan guide diselaraskan.

Verifikasi RunBay:

- `em-system / test-release-note-lock-full`, run
  `20261026-052352-test-release-note-lock-full-243a`: output suite penuh **361 lulus,
  0 gagal, 0 skip**. Regresi tambahan memeriksa versi pilihan tetap saat catatan
  lebih baru muncul, pilihan Released yang ditolak, pemilihan catatan invalid lalu
  valid, serta berkas yang dihapus tanpa fallback. RunBay masih menandai running
  setelah dotnet test keluar sehingga run milik agent ditutup; exit 0/status stopped.
- `emporium-localtest / Build`, run `20261026-052458-build-cad4`: success, exit 0,
  0 error dan 384 warning.
- `git diff --check` lulus. Tidak ada GUI/render atau Push nyata; tampilan dan
  interaksi ComboBox di aplikasi belum diverifikasi. Tidak ada commit/push.


## Sembunyikan indikator floating tag saat lock aktif (2026-10-26)

Pengguna meminta teks tambahan seperti `+ beta` di samping pemilih berkas dihapus.
`ShowVersionMode` sekarang menyembunyikan `floatingText` saat lock aktif, termasuk
setelah pemilihan berkas atau refresh. Saat lock mati tampilannya tetap seperti
sebelumnya. Aturan tampilan dicatat pada CLAUDE.md.

Aplikasi host milik pengguna masih berjalan; verifikasi kompilasi memakai action
RunBay engine yang sudah tersedia dengan output terisolasi, tanpa menghentikan
atau menimpa aplikasi pengguna. Action `em-system / build-frontend-approval`, run
`20261026-052743-build-frontend-approval-7da1`, success/exit 0, 0 error dan 439 warning.
Tidak ada GUI/render atau tes baru
untuk perubahan visibility ini. `git diff --check` lulus; tidak ada commit/push.


## Tombol buka folder di samping release file (2026-10-26)

Permintaan pengguna: tombol untuk membuka folder di sebelah dropdown release note
file. Tombol ikon FolderOpen sekarang tampil tepat di kanan dropdown saat lock aktif,
memakai style outlined bersama, tooltip dan nama aksesibilitas. Tombol membuka path
absolut pada profil melalui shell Windows meski belum ada berkas terpilih. Folder
tidak dibuat otomatis; lokasi hilang/invalid atau gagal dibuka menampilkan pesan
untuk memperbarui Release notes folder pada profil. Aturan dan guide diperbarui.

RunBay menghentikan `emporium-localtest` run `20261026-052859-build-run-e5af`
(status stopped/exit 0), kemudian menjalankan Build + Run terbaru:
`20261026-053123-build-run-2912`. Build selesai dengan 0 error dan 384 warning;
aplikasi masuk tahap Run dan tetap berjalan. Tidak menambah tes
untuk aksi UI sederhana ini. `git diff --check` lulus; interaksi Explorer/GUI belum
diverifikasi. Tidak ada commit/push.


## Release notes di side sheet Material dan maximize (2026-10-26)

Pengguna meminta area pratinjau/riwayat yang memenuhi halaman Publish dipindah ke
panel kanan lewat tombol Release notes, dengan bahasa desain Material 3. Permintaan
lanjutan menambahkan maksimalkan/pulihkan panel. Tombol memakai sheetToggleStyle;
scrim, tombol tutup, permukaan tema, animasi slide dan spacing mengikuti pola side
sheet bersama. Escape/klik luar menutup panel; tombol maximize/restore mengubah lebar
antara 560 dan lebar halaman, dengan MaxWidth mengikuti halaman saat diperkecil.
Pratinjau lock hanya satu, di panel; catatan manual tetap dapat diedit saat lock mati.
Area artefak dan log tidak lagi berisi blok release notes. Membuka panel memfokuskan
tombol tutup sebelum refresh async; navigasi keyboard di dalam panel memakai Cycle.

Kontrol history bersama diperbaiki agar From/To memiliki ItemTemplate versi eksplisit,
termasuk pilihan aktif pada template Material; representasi record tidak ditampilkan.
From/To disembunyikan bila hanya ada satu catatan. Panel memakai preview tanpa batas
tinggi internal agar seluruh isi dapat dibaca lewat scroll panel, tanpa pratinjau
kedua. Release Manager tetap memakai batas tinggi bawaannya. Aturan dan guide diperbarui.

Verifikasi melalui RunBay:

- `test-release-note-lock-filtered`, run
  `20261026-055111-test-release-note-lock-filtered-f2ea`: 146 tes, 1 gagal pada
  pemuatan DataTemplate programatik yang belum sealed. Diperbaiki dengan Seal
  sebelum pemakaian. Dotnet sudah keluar; run ditutup lewat RunBay, exit 2.
- Run ulang `20261026-055243-test-release-note-lock-filtered-0c83`: **146 lulus,
  0 gagal, 0 skip**. Empat tes WPF baru tanpa render memeriksa teks dropdown/pilihan
  aktif, satu catatan tanpa range, hasil gabungan From/To dan pembersihan preview
  saat folder tidak tersedia. RunBay tetap running sesudah dotnet keluar; ditutup
  lewat stop_run, metadata akhir exit 0/status stopped. Hasil lulus berasal dari output.
- Run host lama `20261026-054246-build-run-c27a` dihentikan melalui RunBay setelah
  memeriksa identitas app/run; status stopped/exit 0.
- Build + Run host terbaru `20261026-055407-build-run-f758`: build selesai dengan
  **0 error, 384 warning**; aplikasi masuk tahap Run dan tetap berjalan.
- `git diff --check` lulus. Tidak ada screenshot/render, verifikasi visual atau
  interaksi panel/maximize langsung; hal tersebut tetap belum diverifikasi.
  Tidak ada commit/push atau publish/deployment.
