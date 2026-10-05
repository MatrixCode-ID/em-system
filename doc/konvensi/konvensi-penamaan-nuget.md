# Konvensi penamaan paket NuGet

Draf 2026-10-04, sebagian masih belum diputuskan pengguna (lihat "Pertanyaan terbuka"). Berlaku untuk paket library engine em-system dan repo turunannya (mis. EmPorium House): repo turunan **mengikuti dokumen ini**, tidak membuat aturan sendiri. Aturan versi diturunkan dari [konvensi-penamaan-container.md](konvensi-penamaan-container.md); dokumen ini hanya mencatat bagian yang berbeda untuk NuGet. Cara membuat paket: bagian "Paket library" di [README](../../README.md) dan `scripts/pack-nuget/pack-nuget.ps1`; cara push ke GitHub Packages: `scripts/upload-nuget.cmd`/`upload-nuget.ps1`.

Keputusan pengguna 2026-10-05: **prealpha tidak diterbitkan ke feed publik** (GitHub Packages maupun nuget.org); cukup di feed lokal `dist/nuget-pack` untuk uji sendiri. Feed publik dimulai dari **alpha**. `scripts/upload-nuget/upload-nuget.ps1` menolak push bila `-Version` mengandung `prealpha`/`pre-alpha`.

## Nama paket

- PackageId diturunkan dari nama project, PascalCase, segmen dipisah titik: `<Produk>.<Komponen>[.<Sub>]`. Diatur di `Directory.Build.props`, jangan ditimpa per project.
- Engine memakai awalan paket **`EmSys.`** (keputusan pengguna 2026-10-06): project `Em.Libs` menjadi paket `EmSys.Libs`, dst. Nama project, assembly, dan namespace tetap `Em.*`. Alasannya: nuget.org menolak reservasi prefix di bawah 4 karakter; `EmSys.*` diajukan sebagai ID prefix reservation milik organisasi nuget.org `MatrixCode-ID`. Repo turunan memakai awalannya sendiri (mis. `EmPorium.`), tidak menerbitkan paket berawalan `Em.`/`EmSys.`.
- Hanya library yang dipaket. Project host (`Em.Api`, `Em.Ui.Wpf`, `Em.Ui.Maui`) dan module uji (`Em.Test.*`) diberi `IsPackable=false`.

| Paket | Isi |
|---|---|
| `EmSys.Libs` | kontrak bersama API dan UI |
| `EmSys.Api.Core` | backend engine |
| `EmSys.Ui.Core` | tema, branding, dan dasar UI lintas platform |
| `EmSys.Ui.Wpf.Core` | UI WPF engine |
| `EmSys.Ui.Maui.Core` | UI MAUI engine (belum dirilis ke feed: ditunda sampai job CI MAUI aktif lagi, keputusan pengguna 2026-10-06) |

Daftar project yang benar-benar dipaket dan dirilis ada di `scripts/pack-nuget/packages.txt`. Setiap paket yang dirilis punya release note `doc/ReleaseNote/<PackageId>/<versi>.md` ([format](../ReleaseNote/README.md)).

## Versi

Format `MAJOR.MINOR.PATCH[-channel.N]`, SemVer 2.0, sama dengan konvensi container, dengan perbedaan berikut.

| Channel | Versi paket | Keterangan |
|---|---|---|
| prealpha | `0.1.0-0.prealpha.N` | identifier `0.` di depan membuat prealpha terurut paling rendah; **lokal saja**, tidak pernah di-push ke feed publik |
| alpha | `0.1.0-alpha.N` | |
| beta | `0.1.0-beta.N` | |
| staging | `0.1.0-rc.N` | |
| release | `0.1.0` | **tanpa suffix** |

- **Release tanpa suffix.** NuGet menganggap setiap versi yang memuat `-` sebagai prerelease. Bentuk `0.1.0-release.N` dari publisher container EmPorium **tidak** dipakai untuk NuGet: paket seperti itu tidak terpasang tanpa `--prerelease` dan tidak dianggap stabil oleh Dependabot.
- **Prealpha diberi awalan `0.`.** NuGet mengurutkan label prerelease secara alfabet, sehingga `0.1.0-prealpha.7` dianggap lebih baru daripada `0.1.0-beta.2` oleh `dotnet add package --prerelease`, tombol Update IDE, dan floating version. Identifier angka selalu terurut di bawah identifier huruf, sehingga urutannya benar: `0.1.0-0.prealpha.N` < `0.1.0-alpha.N` < `0.1.0-beta.N` < `0.1.0-rc.N` < `0.1.0`.
- **Satu versi untuk semua paket.** Kelima paket engine selalu dibuat dan diterbitkan bersama dengan versi yang sama, karena `ProjectReference` antar-library menjadi dependency berversi sama. Jangan menerbitkan satu paket saja.
- **Versi tidak pernah dipakai ulang.** Isi paket untuk satu versi tidak pernah diganti, termasuk setelah versi itu dihapus dari feed. NuGet menyimpan paket per versi di cache lokal (`%USERPROFILE%\.nuget\packages`) dan tidak mengunduh ulang versi yang sama. Bila ada yang salah, naikkan `N` atau `PATCH`.
- **Tidak ada versi mengambang di feed.** NuGet tidak punya padanan `:latest`, `:beta`, `:0.1`. Pemakai yang ingin mengikuti channel menulis floating version di project-nya (lihat Pemakaian).
- **Penelusuran ke commit.** Tidak ada padanan tag `:sha-xxxxxxx`. SDK .NET 8+ sudah menyisipkan `RepositoryCommit` ke metadata paket. Build metadata (`+sha.1a2b3c4`) tidak dipakai karena NuGet mengabaikannya saat membandingkan versi.
- Aturan siklus tetap sama dengan container: versi target tetap selama siklus, `N` mulai dari 1 dan reset saat pindah channel, siklus berikutnya menaikkan versi target (`0.2.0-0.prealpha.1`).

Alur satu siklus:

```
0.1.0-0.prealpha.1 … → 0.1.0-alpha.1 … → 0.1.0-beta.1 … → 0.1.0-rc.1 … → 0.1.0
                                                                          ↓
                                                     0.2.0-0.prealpha.1 … (siklus berikutnya)
```

### Asal angka `N`

Dari tag git `v<versi>` per rilis (mis. `v0.1.0-alpha.3`), sama dengan container. Rilis yang menerbitkan paket NuGet dan image container dari commit yang sama memakai versi target dan channel yang sama; hanya bentuk tulisan prealpha dan release yang berbeda seperti tabel di atas.

## Feed

| Feed | Dipakai untuk | Catatan |
|---|---|---|
| lokal `dist/nuget-pack` | uji di mesin sendiri | hasil `scripts/pack-nuget/pack-nuget.ps1`, diabaikan Git |
| GitHub Packages `https://nuget.pkg.github.com/MatrixCode-ID/index.json` | alpha sampai rc, dan release | pemakai wajib login dengan PAT `read:packages`, meski paketnya publik |
| nuget.org | alpha sampai rc, dan release (mulai `0.1.0-alpha.1`, 2026-10-06) | tanpa login; versi tidak bisa dihapus, hanya unlist; dirilis otomatis oleh `publish-nuget.yml` dari release note |

Visibility paket di GitHub Packages diatur terpisah dari repo, per paket.

## Pemakaian

```xml
<!-- kunci satu build -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.0-beta.3" />

<!-- ikuti channel beta pada versi target 0.1.0 -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.0-beta.*" />

<!-- ikuti rilis stabil 0.1.x -->
<PackageReference Include="EmSys.Api.Core" Version="0.1.*" />
```

Floating version diselesaikan ulang saat restore, jadi build bisa berubah tanpa perubahan kode. Untuk host produk, kunci versi persis.

## Pertanyaan terbuka

- Apakah konvensi container juga beralih ke `0.1.0-0.prealpha.N` agar tulisan versi image dan paket identik.
- ~~Apakah awalan `Em.` perlu didaftarkan sebagai ID prefix reservation~~ Ditutup 2026-10-06: awalan paket menjadi `EmSys.` dan reservasinya diajukan lewat email ke nuget.org. Publish ke nuget.org lewat tag `v<versi>` dan workflow `publish-nuget.yml` (`scripts/release-nuget.cmd`); channel mana saja yang masuk nuget.org masih pertanyaan #2 di [doc/ideas/ci-cd-nuget-org.md](../ideas/ci-cd-nuget-org.md).
- Default `scripts/pack-nuget/pack-nuget.ps1` dan `scripts/upload-nuget/upload-nuget.ps1` masih `0.1.0-pre-alpha.1` (penulisan lama, beda dari bentuk tabel `0.1.0-0.prealpha.N`); hanya dipakai untuk pack lokal karena prealpha tidak di-push, tapi penulisannya belum disamakan.
