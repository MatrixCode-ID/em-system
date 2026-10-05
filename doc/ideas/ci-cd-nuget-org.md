# CI/CD dan publish ke nuget.org

- Tanggal: 2026-10-05
- Status: diskusi

## Gagasan

Siapkan em-system supaya build, test, dan publish paket NuGet lima library engine (`Em.Libs`, `Em.Api.Core`, `Em.Ui.Core`, `Em.Ui.Wpf.Core`, `Em.Ui.Maui.Core`) berjalan otomatis lewat CI/CD (GitHub Actions), dan publish paketnya juga ke nuget.org — sekarang baru manual ke GitHub Packages lewat `scripts/upload-nuget/upload-nuget.ps1`.

## Kondisi sekarang

- Belum ada workflow apa pun di `.github/workflows/`.
- `scripts/pack-nuget/pack-nuget.ps1` pack lima paket ke `dist/nuget-pack` (lokal, diabaikan Git).
- `scripts/upload-nuget.cmd`/`upload-nuget.ps1` push manual+interaktif ke GitHub Packages (`https://nuget.pkg.github.com/MatrixCode-ID/index.json`): minta konfirmasi `y/N`, ambil PAT dari `EM_NUGET_PAT` → berkas artefak → prompt tersembunyi, dan menolak push kalau versi mengandung prealpha.
- `Directory.Build.props` sudah isi `Authors`, `Company`, `RepositoryUrl`, `PackageLicenseExpression` (MIT), `PackageReadmeFile` per project kalau ada `README.md`. Belum ada `Description` per project, `PackageIcon`, SourceLink, atau symbol package (`.snupkg`).
- Konvensi versi (`doc/konvensi/konvensi-penamaan-nuget.md`) masih draf dengan tiga pertanyaan terbuka sendiri: kapan mulai nuget.org, apakah `Em.` perlu ID prefix reservation, dan penyamaan default versi prealpha di script.
- Project test (`tests/`) pakai xUnit v3 (Microsoft Testing Platform v2). Integration test butuh SQL Server lokal `(local)` dengan Windows Authentication dan di-skip otomatis bila server tak terjangkau — GitHub-hosted runner default tidak punya SQL Server.

## Keputusan yang sudah jelas (diwarisi dari konvensi yang sudah ada)

- Prealpha tidak pernah dipublish ke feed publik (GitHub Packages maupun nuget.org); hanya lokal di `dist/nuget-pack`.
- Lima paket selalu dipack dan dipublish bersama dengan versi yang sama, karena `ProjectReference` antar-library menjadi dependency berversi sama.
- Versi tidak pernah dipakai ulang di feed; kalau ada kesalahan, naikkan `N` atau `PATCH`, bukan overwrite.
- Format versi SemVer 2.0 `MAJOR.MINOR.PATCH[-channel.N]`, dengan release tanpa suffix (lihat tabel channel di konvensi).
- Angka `N` berasal dari tag git `v<versi>` per rilis.

## Pertanyaan terbuka

1. **Kredensial nuget.org**: Trusted Publishing (OIDC dari GitHub Actions, tanpa API key tersimpan) atau API key classic yang di-scope ke pattern `Em.*` dan disimpan sebagai GitHub Actions secret? **Pertimbangan tambahan (per 2026-10-05):** sejak 17 Agustus 2026, API key baru nuget.org dibatasi maksimal 30 hari (durasi 365 hari dihapus), dan semua API key yang dibuat sebelum tanggal itu kadaluarsa total 1 November 2026. API key classic tetap didukung tapi butuh rotasi manual tiap ≤30 hari untuk CI — Trusted Publishing tidak punya masalah ini karena kredensialnya otomatis & sementara. Microsoft secara eksplisit mendorong migrasi ke Trusted Publishing untuk alasan ini. (Sumber: [devblogs.microsoft.com — Strengthening NuGet supply chain security](https://devblogs.microsoft.com/dotnet/strengthening-nuget-supply-chain-security-reducing-api-key-lifetime/))
2. **Channel yang mulai dipublish ke nuget.org**: hanya `release` (tanpa suffix), atau alpha/beta/rc juga seperti yang sekarang berlaku di GitHub Packages?
3. **Diskusi 2026-10-06:** pengguna ingin rilis ke nuget.org otomatis saat PR di-merge ke `main` (alur: `work-bench` lokal → `em-system-work` privat → `work-bench` publik → PR → `main`). Usulan agent, belum diputuskan: versi target + channel disimpan di berkas repo (mis. `version.txt` berisi `0.1.0-alpha`), `N` dihitung otomatis dari tag tertinggi, workflow push ke `main` membuat tag `v<versi>` otomatis lalu pack dan push; pindah channel/versi lewat PR yang mengubah berkas itu; filter path (`src/**`, `Directory.Build.props`) supaya merge yang hanya dokumen tidak menerbitkan versi; approval environment `release` tetap bisa jadi rem. Menggantikan trigger tag manual `publish-nuget.yml` dan peran `scripts/release-nuget.cmd` (sudah ditulis 2026-10-06). Perlu juga trigger `pull_request` di `ci.yml` dan ruleset `main`. **Release note wajib (keputusan pengguna 2026-10-06, sudah diterapkan pada alur tag):** satu folder per paket, `doc/ReleaseNote/<PackageId>/<versi>.md`, untuk setiap paket di `scripts/pack-nuget/packages.txt`; `EmSys.Ui.Maui.Core` dikeluarkan sementara dari rilis sampai job `build-maui` aktif lagi. Awalnya satu berkas `doc/ReleaseNote/<versi>.md` per versi; harus ada sebelum rilis; dicek `release-nuget.ps1` dan job `validate`. **Disetujui pengguna 2026-10-06, sudah diimplementasi di `publish-nuget.yml` (trigger push `main` dengan path `doc/ReleaseNote/**`, tag dibuat workflow; trigger tag tetap sebagai cadangan; belum dijalankan di GitHub):** untuk rilis otomatis saat merge, versi diambil dari release note baru yang ditambahkan PR itu (berkas `doc/ReleaseNote/<versi>.md` tertinggi yang belum punya tag), sehingga `version.txt` tidak diperlukan dan merge tanpa release note baru tidak merilis apa pun (menggantikan filter path). Usulan lain: isi release note dipakai juga untuk `PackageReleaseNotes` di paket dan isi GitHub Release. Riwayat pertanyaan: **Trigger CI**: tag git `v<versi>` (konsisten dengan asal angka `N` di konvensi), `workflow_dispatch` manual dengan input versi, atau tiap commit ke `main` (auto, tanpa tag)? Opsi "tiap commit" tidak otomatis cocok dengan konvensi versi yang ada (versi berasal dari tag, satu siklus channel per target versi) — commit biasa bukan satuan versi, jadi perlu skema auto-increment tersendiri (mis. angka `N` dari jumlah commit atau nomor run CI) kalau opsi ini dipilih.
4. **Test gate di CI**: integration test butuh SQL Server — pakai service container `mssql` di job runner, atau diterima integration test tetap di-skip di CI (artinya CI bukan gate penuh untuk test itu)?
5. **GitHub Packages vs nuget.org**: GitHub Packages tetap dipertahankan paralel untuk semua channel, atau nuget.org menggantikannya untuk channel tertentu (mis. release)?
6. **Ditutup 2026-10-06 (keputusan pengguna):** PackageId memakai prefix `EmSys.` (`EmSys.Libs`, `EmSys.Api.Core`, `EmSys.Ui.Core`, `EmSys.Ui.Wpf.Core`, `EmSys.Ui.Maui.Core`) apa pun hasil reservasi; namespace/assembly/project tetap `Em.*`. Permohonan reservasi `EmSys.*` (private) dikirim lewat email ke `account@nuget.org`; policy scope Trusted Publishing `EmSys.*`. Belum dikerjakan: ganti `PackageId` di project dan perbarui `konvensi-penamaan-nuget.md`. Riwayat diskusi: **Reservasi nama**: apakah awalan `Em.` perlu didaftarkan sebagai ID prefix reservation di nuget.org sebelum publish pertama kali, dan dengan akun/organisasi mana? **Temuan 2026-10-06:** reservasi tidak self-service (permohonan email ke `account@nuget.org`, ditinjau manual), dan kriterianya menghindari prefix < 4 karakter serta kata generik, jadi `Em.` kemungkinan ditolak. Tidak memblokir Trusted Publishing; ditunda. **Alternatif (diskusi 2026-10-06):** minta `MatrixCode.*` (cocok dengan nama organisasi, besar kemungkinan disetujui) sebagai pilihan pertama bila `Em.*` ditolak, dengan konsekuensi PackageId menjadi `MatrixCode.Em.*` (assembly/namespace tetap `Em.*`). Harus diputuskan sebelum publish pertama ke nuget.org karena ID paket permanen; bila dipilih, `konvensi-penamaan-nuget.md`, policy scope Trusted Publishing (`Em.*` → `MatrixCode.Em.*`), dan `PackageId` di project ikut berubah. Menunggu balasan email permohonan. Varian lain: `MatrixCode.Libs`, `MatrixCode.Api.Core`, dst. (tanpa `Em.`), juga tercakup reservasi `MatrixCode.*`, jadi email tidak perlu diubah. Kelemahannya: identitas engine hilang (ambigu bila organisasi menerbitkan paket produk lain) dan nama paket makin jauh dari namespace `Em.*`. Agent menyarankan `MatrixCode.Em.*`; belum diputuskan. **Arah terbaru (2026-10-06):** prefix `EmSys.*` (singkatan em-system, 5 karakter, belum dipakai siapa pun di nuget.org per pencarian 2026-10-06) diajukan sebagai pilihan utama di email, `MatrixCode.*` sebagai cadangan. PackageId menjadi `EmSys.Libs`, `EmSys.Api.Core`, `EmSys.Ui.Core`, `EmSys.Ui.Wpf.Core`, `EmSys.Ui.Maui.Core`; namespace/assembly tetap `Em.*`. Policy scope Trusted Publishing dan konvensi NuGet ikut disesuaikan setelah balasan diterima. Saran agent (2026-10-06): pakai `EmSys.*` sebagai PackageId terlepas dari hasil reservasi (reservasi hanya memberi tanda verified, tidak memblokir publish). Rename penuh namespace/assembly/project ke `EmSys` **tidak** disarankan sekarang (biaya besar: namespace, `.slnx`, `xmlns` XAML, dokumen, repo turunan, nama tipe tersimpan sebagai string); bila tetap diinginkan, waktu termurah adalah sebelum ada konsumen paket di luar organisasi. Belum diputuskan pengguna.
7. **Script non-interaktif**: `upload-nuget.ps1` sekarang minta konfirmasi `y/N` dan prompt PAT tersembunyi — untuk CI perlu varian non-interaktif. Dibuat sebagai mode baru (`-NonInteractive` atau deteksi otomatis dari environment CI) di script yang sama, atau script terpisah khusus CI?
8. **Kelengkapan metadata**: `Description` per project, `PackageIcon`, SourceLink + `.snupkg` — dikerjakan sebagai bagian plan CI/CD ini, atau idea/plan terpisah yang jalan lebih dulu?
9. **Tiga pertanyaan terbuka di `konvensi-penamaan-nuget.md`** (kapan mulai nuget.org, ID prefix reservation — tumpang tindih dengan #6 di atas, penyamaan default versi prealpha di script) — ditutup dulu sebelum plan CI/CD ditulis, atau boleh dibahas paralel di sini?

## Konsep dasar CI/CD (catatan belajar, 2026-10-05)

Dasar pemikirannya: CI/CD menjalankan script yang sama seperti yang sekarang dipakai manual (`pack-nuget.ps1`, `upload-nuget.ps1`), tapi otomatis di komputer pinjaman milik GitHub.

- **CI (Continuous Integration)**: tiap kali ada perubahan kode (push/PR), otomatis build + test. Tujuannya ketahuan cepat kalau ada yang rusak, tanpa build manual tiap kali.
- **CD (Continuous Delivery/Deployment)**: lanjutan CI — kalau build+test lolos, otomatis lanjut publish/deploy (pack, push ke nuget.org). Ini yang nanti menggantikan konfirmasi `y/N` manual di `upload-nuget.ps1`.
- **GitHub Actions**: layanan CI/CD bawaan GitHub. GitHub menyalakan komputer virtual kosong (runner) sesaat, clone repo, jalankan daftar perintah dari satu file resep `.github/workflows/nama.yml`, lalu komputer itu dibuang. Tiap run dapat komputer baru yang bersih.
- **Workflow `.yml`** isinya konsepnya sama dengan urutan perintah yang sekarang dijalankan manual (`dotnet build`, `dotnet test`, `pack-nuget.ps1`, `dotnet nuget push`), cuma ditulis format YAML dan dijalankan GitHub, bukan di PowerShell laptop sendiri.
- **Secrets**: tempat GitHub menyimpan kredensial (API key, password) dengan aman, dipakai workflow tanpa tertulis di kode.
- **Trusted Publishing** cuma satu langkah tambahan di dalam resep itu: sebelum step `dotnet nuget push`, ada step yang menukar identitas GitHub Actions jadi API key sementara, jadi tidak perlu simpan API key di secrets sama sekali.

### Urutan belajar+implementasi yang disarankan (dipecah kecil, bukan langsung lengkap)

1. **CI paling sederhana, tanpa publish**: workflow yang cuma `dotnet build src/backend/Em.Api.slnx` tiap push ke `main`. Tujuannya melihat bentuk file `.yml`, cara GitHub menjalankannya, dan di mana lihat log/hasilnya (tab **Actions** di repo GitHub).
2. Tambah `dotnet test` ke workflow yang sama — masih belum publish apa-apa.
3. Tambah step pack (`pack-nuget.ps1`), push-nya masih manual dulu; hasil pack diunduh dari halaman Actions lewat `actions/upload-artifact`, untuk dicek dulu sebelum dipercayakan auto-push.
4. Baru tahap terakhir: tambah step push ke GitHub Packages (kredensial sudah ada), lalu terakhir ke nuget.org pakai Trusted Publishing.

Alasan dipecah begini: supaya paham dan terbiasa baca workflow & log-nya dulu sebelum mempercayakan langkah yang auto-publish ke feed publik.

**Tahap 1 dibuat ulang (2026-10-05), sekarang termasuk test dan WPF:** `.github/workflows/ci.yml` — trigger push ke `main`/`ci-sandbox` + `workflow_dispatch`, dua job paralel:
- `build-and-test-backend` di `ubuntu-latest`: `actions/checkout` → `actions/setup-dotnet` 10.0.x → `dotnet restore`/`build`/`test` untuk `src/backend/Em.Api.slnx` (mencakup `Em.Libs.Tests`, `Em.Api.Core.Tests`, `Em.Api.Core.IntegrationTests`).
- `build-and-test-wpf` di `windows-latest` (WPF target `net10.0-windows` + `UseWPF=true`, hanya bisa di-build di Windows; **tidak perlu workload tambahan** seperti MAUI karena Windows Desktop targeting pack otomatis lewat NuGet restore saat di OS Windows): langkah sama, untuk `src/frontend/Em.Ui.Wpf.slnx` (mencakup `Em.Ui.Core.Tests`, `Em.Ui.Wpf.Core.Tests`).

Database test memakai **Opsi A** dari bagian "Database test di CI" di bawah: `ubuntu-latest` tidak ada SQL Server, jadi `Em.Api.Core.IntegrationTests` otomatis skip (sudah didesain begitu), sementara unit test tetap jadi gate penuh. Branch kerja (`work-bench`, `ci-sandbox`) sudah dibuat pengguna di repo. Belum pernah di-push ke GitHub — percobaan jalan sungguhan menyusul.

**Job ketiga, `build-maui`, ditambahkan (2026-10-05):** `src/frontend/Em.Ui.Maui.slnx` target `net10.0-android` saja (keputusan lama: android-only). Dicek: `Em.Ui.Maui.slnx` tidak berisi project test, jadi hanya restore+build, tanpa `dotnet test`. Beda dari WPF: workload MAUI **tidak otomatis ada** di runner manapun (beda dari Windows Desktop targeting pack WPF yang otomatis lewat NuGet), jadi perlu step tambahan `dotnet workload install maui-android` sebelum restore. Runner dipilih `ubuntu-latest` (bukan Windows/macOS) karena target android-only tidak butuh OS tertentu. **Dicoba push sungguhan ke `ci-sandbox` (2026-10-05):** backend dan WPF langsung lulus. `build-maui` gagal di step Build — `MAUIR0001 MissingMethodException: SKImageFilter.CreateMatrixConvolution` saat memroses `em_logo.svg`. Ini bug yang sudah dikenal: `Microsoft.Maui.Resizetizer` 10.0.101/10.0.110 bentrok versi `System.Memory` antara SkiaSharp dan Svg.Skia, muncul khusus untuk SVG berisi elemen `<filter>` atau `<text>` (`em_logo.svg` punya `<filter>`). Perbaikan: pin `Microsoft.Maui.Resizetizer` ke `10.0.100` lewat `Directory.Build.props` (`Condition="'$(UseMaui)' == 'true'"`, `PrivateAssets="all"`, `NoWarn="NU1605"` untuk warning downgrade) — Resizetizer tool build-time saja, aman dipin terpisah dari `$(MauiVersion)`, dan tetap dipertahankan di `Directory.Build.props` terlepas dari status job CI-nya (relevan juga untuk build MAUI manual di luar CI). Setelah fix, push ulang ke `ci-sandbox`: **ketiga job lulus** (`build-and-test-backend` 42s, `build-maui` 3m17s, `build-and-test-wpf` 1m15s). Fix ini sementara sampai MAUI merilis versi yang sudah diperbaiki (MAUI 11.0.0-rc.1 sudah tidak kena karena pakai SkiaSharp versi lain).

**Job `build-maui` dicabut dari `ci.yml` (2026-10-05, keputusan pengguna):** MAUI belum perlu dites otomatis di CI untuk saat ini. Workflow sekarang cuma dua job (backend, WPF). Catatan di atas (penyebab bug, cara pasang workload, fix Resizetizer) dibiarkan sebagai referensi kalau job ini mau diaktifkan lagi nanti — tinggal tambahkan kembali job `build-maui` seperti sebelumnya (`dotnet workload install maui-android` → restore → build `src/frontend/Em.Ui.Maui.slnx`), fix Resizetizer di `Directory.Build.props` sudah otomatis berlaku.

### Database test di CI

Tiga opsi untuk `Em.Api.Core.IntegrationTests` (butuh SQL Server, GitHub-hosted runner tidak otomatis punya):

- **Opsi A (dipakai sekarang):** `ubuntu-latest`, tidak ada SQL Server, integration test skip otomatis (sudah didesain begitu), unit test tetap jadi gate. Nol setup, tapi integration test belum benar-benar teruji di CI.
- **Opsi B (peningkatan nanti):** `windows-latest` — runner ini **sudah ada SQL Server Express LocalDB terpasang**. LocalDB jalan di bawah identitas user Windows yang menjalankannya, otomatis cocok dengan Windows Authentication yang dipakai test project sekarang (**tidak perlu ubah kode test**). Caranya: `sqllocaldb create MSSQLLocalDB -s` di step CI, lalu set env `EM_TEST_DB_SERVER=(localdb)\MSSQLLocalDB` (variabel ini sudah didukung test project). Karena `em-system` repo publik, menit GitHub Actions gratis tak terbatas, jadi runner Windows tidak menambah biaya.
- **Opsi C (tidak disarankan):** container `mssql/server` (Linux) sebagai `services:` di `ubuntu-latest` — SQL Server Linux/container **tidak mendukung Windows Authentication**, cuma SQL Authentication, jadi perlu ubah logika koneksi test project. Lebih invasif, keluar dari desain yang sudah ada.

## Urutan tahap (rancangan kasar, belum plan)

Tahap sebelum CI bisa auto-publish, dari nol:

1. **Tutup pertanyaan terbuka yang blocking** di atas — minimal #1 (kredensial), #2 (channel), #3 (trigger), #6 (reservasi nama). Tanpa ini workflow tidak bisa dirancang final.
2. **Siapkan akun & kredensial nuget.org**: daftar akun publisher, lalu pilih salah satu — Trusted Publishing (OIDC, tanpa secret tersimpan, didaftarkan di nuget.org menunjuk ke repo + workflow file + environment GitHub tertentu) atau API key classic di-scope ke `Em.*` disimpan sebagai GitHub Actions secret.
3. **(Opsional) Reservasi ID prefix `Em.`** di nuget.org sebelum publish pertama kali, supaya nama tidak diklaim pihak lain.
4. **Lengkapi metadata paket** yang belum ada: `Description` per project, `PackageIcon`, SourceLink (`Microsoft.SourceLink.GitHub`) + symbol package `.snupkg`.
5. **Pastikan versi bisa diturunkan otomatis di CI** sesuai trigger yang dipilih di #3 — kalau trigger tag, parsing tag `v<versi>` jadi `-Version`; kalau per-commit, perlu skema auto-increment baru (lihat catatan di pertanyaan #3).
6. **Tulis workflow GitHub Actions**: job build (tiga solution) → `dotnet test` sebagai gate (perlu keputusan #4 soal SQL Server di runner) → pack lima paket lewat `pack-nuget.ps1` → push.
7. **Buat varian non-interaktif** dari logika `upload-nuget.ps1` (tanpa prompt `y/N`, tanpa `Read-Host` PAT) untuk dipanggil dari step CI, dengan guard yang sama (tolak prealpha).
8. **Uji coba di channel rendah dulu** (alpha) dengan tag/commit percobaan, pastikan paket benar-benar muncul di nuget.org dan versi/metadata sesuai, sebelum dipakai untuk channel release.
9. Baru setelah tahap 8 terbukti jalan, pipeline dipakai untuk publish sungguhan sesuai channel yang diputuskan di #2.

## Referensi: cara setup Trusted Publishing (GitHub Actions)

Sumber: [Trusted Publishing — Microsoft Learn](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

- Di nuget.org: username → **Trusted Publishing** → add policy, isi Repository Owner (`MatrixCode-ID`), Repository (`em-system`), Workflow File (**nama file saja**, mis. `publish-nuget.yml`, bukan path `.github/workflows/...`), Environment (opsional).
- **Policy Scope** pakai glob `Em.*` supaya satu policy mencakup kelima paket, termasuk publish pertama kali untuk paket yang belum pernah ada.
- **Policy Ownership**: individu atau organisasi `MatrixCode-ID` — masih pertanyaan terbuka (lihat di bawah).
- Workflow GitHub Actions butuh `permissions: id-token: write`, step `NuGet/login@v1` (input `user` = username profil nuget.org, bukan email) menghasilkan API key sementara (berlaku 1 jam), lalu `dotnet nuget push` dengan key itu ke `https://api.nuget.org/v3/index.json`.
- Publish pertama yang sukses mengunci policy ke ID repo & owner GitHub (mencegah resurrection attack kalau repo dihapus & dibuat ulang nama sama).

Pertanyaan terbuka tambahan dari referensi ini (keputusan pengguna 2026-10-05):

10. ~~**Policy Ownership** di nuget.org: akun individu atau organisasi GitHub `MatrixCode-ID`?~~ **Ditutup:** organisasi `MatrixCode-ID`. Perlu dibuat/dikonfirmasi sebagai Organization terpisah di nuget.org (bukan otomatis sama dengan organisasi GitHub), akun pengguna jadi Owner di sana.
11. **Nama file workflow** yang akan didaftarkan di policy (harus persis sama dengan nama file di `.github/workflows/`) — rencana: `publish-nuget.yml`.
12. ~~**Pakai GitHub Actions environment** (mis. `release`) untuk gate approval manual sebelum publish ke nuget.org, atau tidak perlu?~~ **Ditutup:** ya, pakai environment `release` dengan Required reviewers di GitHub (Settings → Environments), tapi field Environment pada policy Trusted Publishing di nuget.org **dikosongkan** (tidak diisi `release`). Dengan begitu gate approval murni diatur di sisi GitHub dan bisa dimatikan kapan saja (hapus Required reviewers dari environment itu) tanpa mengubah workflow atau policy nuget.org.

### Langkah setup Trusted Publishing (OIDC) — belum dijalankan

A. GitHub: (1) pastikan admin organisasi `MatrixCode-ID`; (2) Settings → Environments → New environment `release`; (3) set Required reviewers di environment itu.

B. nuget.org: (4) login/buat akun; (5) buat Organization `MatrixCode-ID` di nuget.org, jadi Owner; (6) opsional sekalian reservasi ID Prefix `Em.` di organisasi itu (pertanyaan #6); (7) di organisasi, Trusted Publishing → Add policy — Repository Owner `MatrixCode-ID`, Repository `em-system`, Workflow File `publish-nuget.yml`, Environment dikosongkan, Policy Scope `Em.*`.

C. Setelah A & B selesai: tulis `.github/workflows/publish-nuget.yml` (`permissions: id-token: write`, job pakai `environment: release`, step `NuGet/login@v1` lalu `dotnet nuget push`, trigger tag `v<versi>`).

D. Uji coba dulu dengan tag channel rendah (mis. `v0.1.0-alpha.1`) sebelum dipakai untuk channel release sungguhan.

Langkah A dan B bersifat manual oleh pengguna (akun/organisasi GitHub & nuget.org, bukan tindakan di repo) — belum dijalankan per 2026-10-05.

Pembaruan 2026-10-06: Organization nuget.org sudah dibuat, profil <https://www.nuget.org/profiles/MatrixCode-id>. Environment `release` di GitHub: "Deployment branches and tags" tidak diisi branch `main` (trigger dari tag), bila dibatasi pakai deployment tag rule `v*`.

## Plan turunan

Belum ada.
