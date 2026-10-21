# Laporan eksekusi — Release Manager, managed CDN, launcher dan cab

Tanggal: 2026-10-21 (Asia/Jakarta). Status: implementasi seksi A–H selesai,
build dan pengujian console lulus dengan pengecualian yang dinyatakan di bawah.
Seluruh commit dibuat lokal di `work-bench`; tidak ada push, tag, deployment,
atau perubahan repository produk pemakai.

Plan: [release-manager-cdn-launcher.codex.md](../../plan/executed/release-manager-cdn-launcher.codex.md).

## Hasil per seksi dan commit

| Seksi | Hasil | Commit |
| --- | --- | --- |
| A | Kontrak metadata launcher yang ikut ditandatangani, vektor parser baru, aturan identitas/registry dan panduan Launcher | `b885f94` |
| B | Parent CDN `files/temp/recycle/settings`, file titik/hidden/system publik, temporary garbage, recycle/restore, tracked downloads, ownership dan kuota managed folder, service Release Manager | `ada2115` |
| C | More pada CDN Manager: recycle bin, clear temporary, pemilik managed folder, usage/quota, Release dengan ketik nama; isi managed read-only | `ff07ba9` |
| D | Profile satu JSON versi 2, private PEM, identitas MSBuild, pratinjau logo, alamat publik CDN, source/output launcher per mesin, hash/cache/build launcher dan signed metadata | `02dfd1a` |
| E | Target MSBuild identitas/resource, BaseRegKey/log memakai app_id, redirect melalui nama manifest, slot update berkala dan restart normal, identitas host pada csproj | `8def9f8` |
| F | Product launcher Rust, nama exe/backup, kunci tertanam/fingerprint, settings.ini, versi dan logo, automatic/ask, prepare tanpa aktivasi, binary dist | `521d411` |
| G | FCI/FDI LZX cab, reuse isi cab lama, verifikasi sebelum rename, CLI/tombol download, install dari cab, regresi console, binary dist | `d3de9cd` |
| H | Guide CDN/storage/release/build, guide launcher baru dan indeks; aturan registry root diperbarui | `b829ec5` |
| Perbaikan akhir | Regresi CDN/ReleaseProduct, penghapusan jalur certificate/password lama, exemption dispatcher pada tipe service yang benar, penguncian extract, keamanan cab, cache identity, penanganan restart batal, layout logo dan penguatan validasi | `4fb9319` |

Commit penutupan memindahkan plan dan menyimpan laporan ini. Hash commit
penutupan dapat dilihat pada Git log karena tidak ditulis ke dalam commit-nya sendiri.

Build murah masing-masing seksi berhasil sebelum commit. Pengujian suite dan
review dilakukan setelah seluruh seksi ditulis. Kegagalan awal kompilasi/lint
serta ekspektasi test registry lama sudah diperbaiki; hasil akhir di bawah
menggantikan hasil antara tersebut.

## Verifikasi akhir

| Pemeriksaan | Hasil |
| --- | --- |
| `dotnet build src/backend/Em.Api.slnx --no-restore` | Lulus, 0 error |
| `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore` | Lulus, 0 error; warning lama nullable Em.Test dan analyzer cancellation pada test |
| `dotnet test --solution src/backend/Em.Api.slnx --no-build` | 352 total: 344 lulus, 0 gagal, 8 skip |
| `dotnet test --solution src/frontend/Em.Ui.Wpf.slnx --no-build` | 202 total: 202 lulus, 0 gagal, 0 skip |
| `cargo test` | 76 lulus, 0 gagal; doc-test 0 |
| `cargo fmt --check` | Lulus |
| `cargo clippy --all-targets -- -D warnings` | Lulus |
| `build-dist.ps1` dari source launcher akhir | Lulus; binary dibangun ulang dan di-commit |
| Pack WPF Core versi lokal `0.0.0-smoke`, tanpa publish | Lulus; ZIP paket memuat `buildTransitive/EmSys.Ui.Wpf.Core.targets` |
| Smoke console launcher produk khusus | 15/15 lulus |
| `git diff --check` | Lulus |

Delapan skip backend adalah test log store PostgreSQL/MySQL yang memerlukan
`EM_TEST_PG`/`EM_TEST_MYSQL`. Test SQL Server yang dijalankan oleh suite lulus.
Skip tersebut tidak dihitung sebagai lulus.

Regresi baru CDN menguji susunan folder, file titik/hidden/system, isi lama yang
tidak disajikan, recycle/restore dan konflik, handle download tetap terbuka,
nama `item.json`, folder managed kosong/bertingkat/pemilik, perubahan kuota,
preflight dan per-file guard, pengecualian Max file size, garbage yang tidak
mengganggu upload/reader aktif, archive/extract dan perlindungan managed target,
serta persistensi managed.json. Pengujian dispatcher membuktikan bahwa empat
request CDN tanpa identitas tetap 401, sedangkan request non-CDN berikutnya
masih terkena 429; katalog claim Release Manager juga diperiksa.

Regresi WPF/Release menguji JSON/PEM, penolakan format lama dan key non-P256,
hash isi versus timestamp/build output, versi semi otomatis, metadata host dan
BaseRegKey melalui factory Registry fixture, properti hasil evaluasi MSBuild,
metadata manifest, pengecualian settings.ini, dan bentrok nama app/launcher.
Tidak ada rendering window pada tes ini.

Rust menguji vektor signature lama dan metadata launcher baru, prepare yang
resumable tanpa mengganti current.json, INI fixture, serta FCI/FDI round-trip,
pembaruan cab, cab lama tetap utuh ketika sumber gagal, staging offline dan
aktivasi melalui updater yang sama.

Smoke produk khusus memakai app_id fixture unik, kunci P-256 sementara, folder
artefak dan Registry fixture. Pemeriksaannya meliputi versi tampilan dan PE
`A.B.C.N`, cache tidak dibangun ulang, reuse tanpa source, penolakan cache setelah
identitas berubah, embedded public key, download cab dan kondisi terbaru,
quiet install dari cab ketika source offline, tidak menyalin settings.ini,
JSON prepare gagal/siap/tidak ada, current.json tetap saat prepare, aktivasi
staging, serta penolakan app_id sama dengan fingerprint berbeda. Registry dan
entry Apps & Features fixture dibersihkan dalam `finally`; tidak ada aplikasi
GUI produk yang dijalankan.

## Keputusan implementasi

- Pengecualian ActionRateLimit memeriksa route yang benar-benar terdaftar dan
  tipe interface `ICdnServices`/`ICdnReleaseServices` sebelum limiter. Tidak
  memakai awalan nama action yang dapat ditiru service lain. Gate autentikasi,
  claim dan validasi parameter tetap berjalan.
- API mendaftarkan claim CDN Manager, CDN Settings dan Release Manager bila
  belum ada, sehingga tidak menduplikasi deklarasi host.
- Public file provider mengembalikan PhysicalPath null agar middleware memakai
  tracked stream dengan FileShare.Read | FileShare.Delete, termasuk Range.
  Rename memperbarui lokasi reader; purge melewati item yang sedang dibaca.
- Recycle memakai `recycle/<id>/item.json` dan
  `recycle/<id>/payload/<nama-asli>`. Wrapper payload mencegah bentrok bila
  file/folder asli sendiri bernama `item.json`; nama asli tetap dipertahankan.
- Operasi temp/metadata/mutasi diserialkan pada gate proses tunggal; upload,
  archive dan extract tetap melakukan pekerjaan stream di luar critical section
  lalu memeriksa ulang ownership sebelum publikasi. Tidak ada cleanup startup.
- Perhitungan awal Sync memakai inventory hasil akhir, termasuk ukuran JSON
  manifest dan signature. Per-upload juga memeriksa pemakaian live: pada Sync
  yang sementara membutuhkan lebih banyak ruang sebelum obsolete files dihapus,
  guard ini dapat menghentikan upload meskipun ukuran akhir rencana muat.
  Kuota tidak dilonggarkan dan manifest baru belum diterbitkan pada kegagalan
  sebelum penulisan pasangan metadata; operator perlu menyediakan headroom.
- Build hash memakai isi source dan nama relatif berkas, manifest Windows,
  config Cargo/toolchain serta script build, identitas/aset/public key dan
  setting produk. Timestamp, target output dan commit git tidak menggantikan
  content hash. Marker juga menyimpan hash exe dan hash input produk tersendiri
  agar perubahan identitas tidak memakai cache lama ketika source tidak ada.
- Target MSBuild dikemas buildTransitive dan di-import eksplisit pada host
  ProjectReference. Resource identitas memiliki logical path tetap di assembly;
  override kode tetap diterapkan. Setting launcher source/output memakai
  BaseRegKey host, bukan root Registry fitur tersendiri.
- WIC dipilih untuk PNG karena decoder disediakan Windows dan tidak menautkan
  decoder PNG/deflate tambahan ke exe. Hasil premultiplied BGRA dirender dengan
  GDI AlphaBlend. Tidak dilakukan benchmark dua alternatif decoder; ukuran
  binary aktual dicatat di bawah. Area logo tetap 240 × 48, fit uniform,
  left/center, tanpa crop transparansi. Hanya child control tingkat pertama
  digeser untuk menyediakan ruang logo.
- Cab menggunakan callback FCI/FDI dengan pemetaan nama token ke PathBuf agar
  folder mesin yang Unicode tidak bergantung pada ANSI filesystem. Nama entry
  memakai flag UTF-8. Cab terpecah, traversal, nama perangkat Windows,
  duplikat dan file di luar manifest ditolak. Metadata dibaca untuk pemilihan
  sumber, lalu payload diekstrak langsung ke staging instalasi. Cab baru
  diekstrak dan diverifikasi sebelum MoveFileEx mengganti cab lama.
- UI maintenance memakai EmWindow, style bersama, input angka khusus kuota dan
  help tooltip. Pemilihan host dan import aset/key mengedit draft; cancel tidak
  memasang key ke certificate store. Jalur certificate/PFX/password lama dihapus.

## Binary akhir

`dist/launcher/launcher.exe`: **1.196.544 byte**.

SHA-256:
`9d5fce5bd7162cb075fe5dc64dda7e0bfd1b18a55ffb4bd9fdb6cc5402dfeb6d`.

Source produk bawaan tidak membawa key produksi. Produk Release Manager
menanam public key dari profile. Versi engine yang ditanam saat build mencatat
dirty state bila perubahan source belum di-commit saat binary dibangun.

## Verifikasi tertunda dan batasan

Sesuai plan, **tidak ada uji visual/GUI**. Yang belum dibuktikan lewat render
atau interaksi nyata:

- CDN Manager/maintenance: light/dark, enabled/disabled/busy, recycle, quota,
  dialog ketik nama, drag-drop managed yang ditolak, lebar window sempit.
- Release Settings: dialog perbedaan identitas, pemilihan folder CDN, pratinjau
  logo 100%/200%, hover help dan navigasi field pada window nyata.
- Slot update: tooltip/menu, polling dalam sesi aplikasi nyata, konfirmasi
  restart dan pembatalan layar yang belum disimpan.
- Win32 setup/maintenance/progress: logo transparan pada variasi DPI/aspek,
  tombol Download to cab, dialog perbarui cab dan interaksi cancel nyata.

Smoke penuh Prepare → Sync melalui HTTP pada API lokal yang sedang berjalan,
reverse proxy/UNC nyata, serta aplikasi Em/non-Em sesungguhnya belum dijalankan.
Kontrak service/storage diuji langsung dan melalui dispatcher, sedangkan
installer/updater produk fixture diuji sebagai proses console. Batas cab sekitar
2 GB diperiksa kode; file uji berukuran mendekati batas tersebut tidak dibuat.
Ini tidak dicatat sebagai verifikasi yang lulus.

Tidak ada tindakan yang ditolak policy dan tidak ada skrip manual policy yang
harus dijalankan pengguna. Push tetap dilakukan pengguna bila diinginkan.

## Artefak lokal

Harness dan hasil hanya berada di
`../.artefacts/em-system/scripts/release-cdn-smoke/`, di luar repo:

- `Program.cs`, `ReleaseSmoke.csproj`: smoke produk fixture console.
- `validate-launcher.ps1`: memilih lingkungan MSVC x64 yang sesuai dan
  menjalankan cargo test/fmt/clippy; bukan bypass policy.
- `out/backend-build.log`, `backend-test.log`, `backend-unit.log`,
  `frontend-build.log`, `frontend-test.log`, `cargo-test.log`, `fmt.log`,
  `clippy.log`, `product-smoke.log`, `product/` dan `packages/`.

## Konsep Rust yang digunakan

- **Ownership/borrowing:** Updater meminjam source/layout; worker selesai sebelum
  pinjaman berakhir. Buffer cab dan reader dimiliki scope operasi.
- **Result dan `?`:** error IO/signature dibawa ke CommandError tanpa mengaktifkan
  versi yang belum terverifikasi. Cancel mempunyai kode keluar tersendiri.
- **Traits:** ReleaseSource menyatukan HTTP/folder dan memungkinkan updater yang
  sama memakai berkas staging hasil cab tanpa logika verifikasi kedua.
- **RAII/Drop:** guard FCI/FDI, state thread-local dan folder kerja membersihkan
  resource saat berhasil maupun gagal. Pointer callback hanya hidup selama
  pemanggilan synchronous Windows; file handle dikembalikan/dilepas oleh callback.
- **Rc/RefCell dan Arc:** data paint berada di thread UI; progress memakai Arc
  untuk worker. State callback cabinet bersifat thread-local agar operasi
  paralel di thread lain tidak saling mencampur handle/path.
