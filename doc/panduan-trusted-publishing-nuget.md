# Panduan setup Trusted Publishing (OIDC) untuk publish ke nuget.org

- Status: langkah manual, belum dijalankan (per 2026-10-05).
- Konteks dan alasan tiap keputusan: [doc/ideas/ci-cd-nuget-org.md](ideas/ci-cd-nuget-org.md).

Panduan ini isinya langkah manual yang harus dijalankan sendiri di web UI GitHub dan nuget.org (bukan tindakan di repo, jadi tidak bisa dikerjakan oleh Claude/Codex). Setelah semua langkah di bawah selesai, lanjutkan ke bagian **C** untuk minta workflow `publish-nuget.yml` ditulis.

## A. Sisi GitHub

1. Pastikan kamu admin di organisasi GitHub `MatrixCode-ID` (repo `em-system` sudah menunjuk ke situ lewat `RepositoryUrl`).
2. Buka repo → **Settings → Environments → New environment**, beri nama `release`.
3. Di environment `release`, set **Required reviewers** ke akun kamu. Ini jadi gate approval manual sebelum step push ke nuget.org jalan.
   - Untuk mematikan gate ini nanti (publish jadi full otomatis): cukup hapus Required reviewers dari environment `release`. Tidak perlu ubah workflow atau policy nuget.org.
   - **Deployment branches and tags**: jangan diisi branch `main`. Workflow dipicu tag `v<versi>`, jadi ref yang dicek adalah tag, bukan branch; aturan branch `main` justru memblokir run dari tag. Bila ingin dibatasi, pilih **Selected branches and tags** → **Add deployment tag rule** → `v*`. Tag hanya dibuat dari commit `main` yang CI-nya hijau adalah disiplin proses rilis, bukan dijamin oleh pengaturan ini.
   - **Secret `NUGET_USER`** (setelah langkah B.7): di repo → **Settings → Secrets and variables → Actions → New repository secret**, buat secret `NUGET_USER` berisi **username nuget.org pribadi** kamu (nama profil yang membuat policy di langkah B.7, bukan email dan bukan nama organisasi). Dipakai step `NuGet/login@v1`.

## B. Sisi nuget.org

4. Login ke nuget.org dengan akun kamu (buat akun dulu kalau belum ada).
5. Buat **Organization** di nuget.org bernama `MatrixCode-ID` (menu profil → **Organizations → Add Organization**). Ini entitas terpisah dari organisasi GitHub, cuma disamakan namanya. Akun kamu jadi Owner di organisasi ini.
   - Sudah dibuat (2026-10-06), profil: <https://www.nuget.org/profiles/MatrixCode-id>
6. *(Opsional, boleh dilewati)* Reservasi **ID Prefix**. Tidak ada menu self-service di nuget.org: permohonan dikirim lewat email ke `account@nuget.org` berisi display name owner (`MatrixCode-ID`) dan prefix yang diminta, lalu ditinjau manual ([dokumentasi](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation)). Kriteria mereka menghindari prefix di bawah 4 karakter dan kata generik, jadi yang diajukan adalah `EmSys.*` (keputusan 2026-10-06), bukan `Em.`. Trusted Publishing tidak membutuhkan reservasi ini: paket tetap terbit sebagai `EmSys.*` meski permohonan ditolak, hanya tanpa tanda verified.
7. Di organisasi `MatrixCode-ID`, buka menu **Trusted Publishing → Add policy**, isi:
   - **Repository Owner**: `MatrixCode-ID`
   - **Repository**: `em-system`
   - **Workflow File**: `publish-nuget.yml` (nama file saja, bukan path lengkap)
   - **Environment**: **kosongkan** (jangan isi `release` — gate approval cukup diatur di sisi GitHub pada langkah A.3)
   - **Policy Scope**: `EmSys.*` (glob, cakup kelima paket engine sekaligus termasuk publish pertama kali; PackageId memakai prefix `EmSys.`, keputusan 2026-10-06)

## C. Workflow `publish-nuget.yml` (sudah ditulis 2026-10-06)

`.github/workflows/publish-nuget.yml` dipicu push tag `v*` dan berjalan dalam tiga job:
1. `validate`: ambil versi dari tag (`v0.1.0-alpha.1` → `0.1.0-alpha.1`), tolak format non-SemVer dan prealpha, serta tolak tag yang commit-nya tidak ada di `main`.
2. `ci`: menjalankan ulang `ci.yml` (build + test backend dan WPF) sebagai gate.
3. `publish` (Windows, `environment: release`): pasang workload `maui-android`, pack lima paket lewat `scripts/pack-nuget/pack-nuget.ps1`, simpan `.nupkg` sebagai artifact run, login OIDC lewat `NuGet/login@v1` (secret `NUGET_USER`), lalu `dotnet nuget push` ke nuget.org dengan `--skip-duplicate` agar run ulang bisa menuntaskan push yang sempat terputus.

Required reviewers di environment `release` membuat job `publish` menunggu persetujuan setelah test lulus.

## D. Uji coba

Sebelum dipakai untuk channel release sungguhan, uji dulu dengan tag versi channel rendah, mis. `v0.1.0-alpha.1`, dan pastikan paket benar-benar muncul di nuget.org dengan versi/metadata yang sesuai.

Tag dibuat lewat `scripts/release-nuget.cmd` (double-click, atau dari terminal `scripts\release-nuget.cmd 0.1.0-alpha.1`). Skrip memastikan branch `main` aktif, working tree bersih dan sama dengan `origin/main`, versi bukan prealpha, dan tag belum ada, lalu meminta konfirmasi `y/N` sebelum membuat dan push tag. Setelah itu pantau tab **Actions** → **Publish NuGet**, setujui job `publish` saat diminta, lalu cek <https://www.nuget.org/profiles/MatrixCode-id>.
