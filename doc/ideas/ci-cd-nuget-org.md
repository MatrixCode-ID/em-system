# CI/CD dan publish ke nuget.org

- Tanggal: 2026-10-05
- Status: diskusi

## Gagasan

Siapkan em-system supaya build, test, dan publish paket NuGet lima library engine (`Em.Libs`, `Em.Api.Core`, `Em.Ui.Core`, `Em.Ui.Wpf.Core`, `Em.Ui.Maui.Core`) berjalan otomatis lewat CI/CD (GitHub Actions), dan publish paketnya juga ke nuget.org — sekarang baru manual ke GitHub Packages lewat `scripts/upload-nuget.ps1`.

## Kondisi sekarang

- Belum ada workflow apa pun di `.github/workflows/`.
- `scripts/pack-nuget.ps1` pack lima paket ke `dist/nuget-pack` (lokal, diabaikan Git).
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
3. **Trigger CI**: tag git `v<versi>` (konsisten dengan asal angka `N` di konvensi), `workflow_dispatch` manual dengan input versi, atau tiap commit ke `main` (auto, tanpa tag)? Opsi "tiap commit" tidak otomatis cocok dengan konvensi versi yang ada (versi berasal dari tag, satu siklus channel per target versi) — commit biasa bukan satuan versi, jadi perlu skema auto-increment tersendiri (mis. angka `N` dari jumlah commit atau nomor run CI) kalau opsi ini dipilih.
4. **Test gate di CI**: integration test butuh SQL Server — pakai service container `mssql` di job runner, atau diterima integration test tetap di-skip di CI (artinya CI bukan gate penuh untuk test itu)?
5. **GitHub Packages vs nuget.org**: GitHub Packages tetap dipertahankan paralel untuk semua channel, atau nuget.org menggantikannya untuk channel tertentu (mis. release)?
6. **Reservasi nama**: apakah awalan `Em.` perlu didaftarkan sebagai ID prefix reservation di nuget.org sebelum publish pertama kali, dan dengan akun/organisasi mana?
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

**Tahap 1 sempat dibuat lalu dihapus lagi (2026-10-05):** `.github/workflows/ci.yml` sempat dibuat (trigger push ke `main`/`ci-sandbox` + `workflow_dispatch`, job tunggal `build-backend` di `ubuntu-latest`: `actions/checkout` → `actions/setup-dotnet` 10.0.x → `dotnet restore`/`dotnet build` untuk `src/backend/Em.Api.slnx` saja), tapi dihapus lagi oleh pengguna sebelum sempat di-commit/push — jadi belum pernah benar-benar dijalankan di GitHub Actions. Struktur branch yang disepakati sejauh ini: `main` (tempat tag rilis), `ci-sandbox` (coba-coba isi workflow, dipicu tiap push), `work-bench` (kerja harian, tidak memicu CI). WPF/MAUI solution belum direncanakan masuk CI karena WPF butuh runner `windows-latest` dan MAUI butuh workload tambahan.

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

Pertanyaan terbuka tambahan dari referensi ini:

10. **Policy Ownership** di nuget.org: akun individu atau organisasi GitHub `MatrixCode-ID`?
11. **Nama file workflow** yang akan didaftarkan di policy (harus persis sama dengan nama file di `.github/workflows/`) — belum ditentukan, mis. `publish-nuget.yml`.
12. **Pakai GitHub Actions environment** (mis. `release`) untuk gate approval manual sebelum publish ke nuget.org, atau tidak perlu?

## Plan turunan

Belum ada.
