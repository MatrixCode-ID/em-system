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

Dua cara memicu rilis:
- **Otomatis (utama):** push ke `main` (merge PR atau push) yang mengubah `doc/ReleaseNote/**`. Versi diambil dari release note: versi yang punya berkas `doc/ReleaseNote/<PackageId>/<versi>.md` tapi belum punya tag `v<versi>`. Tidak ada versi seperti itu → workflow selesai tanpa rilis. Lebih dari satu → gagal (satu versi per merge). Bisa juga dijalankan ulang manual lewat **Actions → Publish NuGet → Run workflow** (branch `main`).
- **Manual (cadangan):** push tag `v<versi>` lewat `scripts/release-nuget.cmd`.

Tiga job:
1. `validate`: tentukan versi (dari release note atau dari tag), tolak commit yang tidak ada di `main`, format non-SemVer, prealpha, versi yang tidak lebih tinggi dari tag terakhir, dan paket di `scripts/pack-nuget/packages.txt` yang belum punya release note (atau kosong). Logika versi sama dengan skrip lokal (`scripts/release-nuget/release-common.ps1`).
2. `ci`: menjalankan ulang `ci.yml` (build + test backend dan WPF) sebagai gate.
3. `publish` (Windows, `environment: release`): pack paket di `packages.txt` (saat ini empat; `EmSys.Ui.Maui.Core` ditunda sampai CI MAUI aktif lagi), cocokkan jumlah `.nupkg`, simpan sebagai artifact run, login OIDC lewat `NuGet/login@v1` (secret `NUGET_USER`), `dotnet nuget push` ke nuget.org dengan `--skip-duplicate`, lalu (jalur otomatis) buat dan push tag `v<versi>`, dan terakhir buat GitHub Release `v<versi>` berisi gabungan release note semua paket dengan lampiran `.nupkg` (prerelease bila versi berakhiran `-...`). Tag dari workflow tidak memicu run baru. Setiap paket membawa ikon `doc/assets/logo/Logo-128.png` dan `PackageReleaseNotes` dari bagian Ringkasan release note-nya plus tautan ke berkas lengkap.

**Approval:** job `publish` berhenti dengan status "Waiting" sampai disetujui reviewer environment `release`. Di repo `em-system` buka **Actions → Publish NuGet → run terbaru → Review deployments → centang `release` → Approve and deploy**. GitHub juga mengirim notifikasi/email ke reviewer. Menghapus Required reviewers dari environment `release` membuat rilis berjalan tanpa persetujuan.

## D. Uji coba

Uji pertama memakai `0.1.0-alpha.1`. Release note-nya sudah ada di `doc/ReleaseNote/<PackageId>/0.1.0-alpha.1.md` untuk keempat paket (format: [doc/ReleaseNote/README.md](ReleaseNote/README.md)).

1. Commit di `work-bench`, push ke `private`.
2. Merge `work-bench` ke `main`, push ke `origin`. Karena merge membawa release note baru, workflow **Publish NuGet** langsung jalan.
3. Setujui job `publish` (lihat Approval di atas).
4. Cek tag `v0.1.0-alpha.1` muncul di repo dan paket di <https://www.nuget.org/profiles/MatrixCode-id> (prerelease; indexing bisa beberapa menit).

Rilis berikutnya: tambahkan release note versi baru untuk setiap paket di PR/merge yang sama, lalu merge ke `main`.

Jalur manual `scripts/release-nuget.cmd` (dari `main`) tetap tersedia: skrip menampilkan versi terakhir di tag git dan nuget.org serta versi yang release note-nya lengkap tapi belum dirilis, menanyakan versi (default versi lengkap terendah yang belum dirilis, atau versi berikutnya), menolak prealpha, versi yang tidak naik, release note yang belum lengkap, dan tag yang sudah ada, lalu meminta konfirmasi `y/N` sebelum push tag.