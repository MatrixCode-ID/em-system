# Audit persiapan open source — em-system dan EmPorium House

Tanggal: 2026-10-04. Cakupan: `em-system` dan `emporium` (EmPorium House). Metode: pencarian berbasis pola teks pada HEAD dan seluruh riwayat git (secret, kunci privat, IP, path lokal, nama produk privat, lisensi, binary terlacak). Screenshot PNG belum diperiksa secara visual.

Catatan: laporan ini sengaja tidak menulis nilai sensitif yang ditemukan (IP server, nama database atau produk privat, nama orang). Gunakan perintah di bagian "Cara memeriksa ulang" untuk menemukan lokasinya.

## Ringkasan

| Repo | Status | Sisa pekerjaan |
| --- | --- | --- |
| em-system | **Belum siap** | Riwayat git masih memuat detail server dev dan nama produk privat; binary hasil build terlacak; dokumen pendukung belum ada |
| EmPorium House | **Hampir siap** | Bergantung pada em-system (`ProjectReference`); perubahan belum di-commit; email pribadi di author commit |

Kedua repo GitHub saat diperiksa mengembalikan HTTP 404 dari luar, artinya masih privat atau belum ada. Riwayat masih aman ditulis ulang sebelum dipublikasikan.

## Yang sudah aman

- Tidak ada private key, token, atau password asli di HEAD maupun riwayat; temuan hanya `CHANGE_ME` pada berkas contoh dan fixture uji Launcher.
- `emapi-config.json`, `.env`, `em.local.json`, dan `secrets.local.json` sudah di-ignore; yang terlacak hanya `*.example`.
- LICENSE MIT di kedua repo; metadata paket NuGet konsisten.
- Direktori arsip legacy produk privat tidak pernah masuk riwayat git.
- Tidak ada referensi paket DevExpress di `.csproj`.

## Status pembersihan HEAD em-system (2026-10-04)

Selesai:

- Kode `src/` bebas nama produk privat dan modul bisnisnya (teks bantuan, placeholder, komentar, handler navigasi yang tak terpakai). Build `Em.Api.slnx` dan `Em.Ui.Wpf.slnx` lulus.
- Skill, snapshot struktur database, dan pola service yang terikat produk privat dipindah ke repo privat dan dihapus dari repo ini.
- Laporan, daftar TODO, dan plan yang murni membahas produk privat dipindah ke repo privat. Dokumen lain di `doc/` dan `plan/` disunting ke nama netral (`Em.*`, `acme`), IP dan nama database dibuang.
- Sampel format rilis (`doc/release-format-samples/`) dibuat ulang dengan kunci uji baru; `keyId` berubah. Diverifikasi terpisah lewat PowerShell; tes Rust Launcher belum dijalankan (lingkungan MSVC).
- Skrip uji registry dinetralkan, bukan dipindah, karena didokumentasikan sebagai alat uji engine.
- Data pribadi pada data contoh UI, skrip uji, dan komentar diganti nilai generik.

Sisa di HEAD:

- Rujukan historis ke berkas yang sudah dipindah (daftar TODO, laporan kesiapan) di `plan/executed/` dan `doc/report/` kini menunjuk berkas yang tidak ada. Dibiarkan sebagai catatan sejarah.
- Pencarian ulang pola nama produk, IP server dev, dan nama pribadi di HEAD: nol hasil (di luar laporan ini).

## Yang masih harus dikerjakan

1. **Tulis ulang riwayat git em-system.** Detail server dev, nama database dan produk privat, serta berkas yang kini sudah dipindah masih ada di commit lama. Disarankan repo baru dengan satu commit awal (baru 75 commit dan repo masih privat), atau `git filter-repo`. Sekaligus ganti email author ke alamat `noreply` GitHub bila tidak ingin terlihat publik.
2. **Keluarkan binary hasil build dari git.** `dist/launcher/launcher.exe` dan 5 berkas `dist/nuget-pack/*.nupkg` terlacak. Sebarkan lewat GitHub Releases atau NuGet; sesuaikan README yang menyebut `dist/nuget-pack`.
3. **Dokumen pendukung.** `SECURITY.md`, `CONTRIBUTING.md`, `.github/` (CI build, Dependabot, template issue), dan berkas atribusi pihak ketiga. Cek lisensi: FontAwesome (font WPF/MAUI), Bootstrap dan FontAwesome JS di `doc/wiki`, PDFium/PDFsharp, dan **`MySql.EntityFrameworkCore` (Oracle, GPLv2 + FOSS exception)** yang dapat menyulitkan pengguna; Pomelo (MIT) adalah alternatif.
4. **Putuskan nasib `plan/` dan `doc/report/`** (±100 berkas log kerja internal): dipertahankan atau dipangkas.
5. **Pengaturan GitHub.** Secret scanning dan push protection, branch protection `main`, 2FA wajib di organisasi.
6. **Pastikan server dev yang pernah tercatat tidak terjangkau dari internet.** Tidak ada password yang ditemukan, tetapi IP dan user admin database pernah tercatat.
7. **Jalankan tes Rust Launcher** (`release_format_samples`) dari shell dengan toolchain MSVC lengkap.

## Temuan EmPorium House

1. Tidak dapat dibangun sendirian: `ProjectReference` ke `..\em-system` (lewat `EmSystemPath`). Buka em-system lebih dulu atau terbitkan paket NuGet-nya.
2. Rujukan nama produk privat di `CLAUDE.md` sudah dihapus.
3. Perubahan belum di-commit: `src/backend/EmPoriumHouse.Api/Dockerfile`, `src/backend/README.md`, `scripts/upload-api-ghcr.cmd`, `scripts/upload-api-ghcr/`. Skrip GHCR menerima PAT lewat stdin dan tidak menyimpannya; aman. Commit atau buang sebelum rilis.
4. Email pribadi pada author commit (sama seperti em-system).
5. Tambahkan `SECURITY.md`, `CONTRIBUTING.md`, dan CI seperti em-system.

## Urutan yang disarankan

1. Selesaikan butir 2–3 di atas pada em-system dan commit.
2. Tulis ulang riwayat (butir 1).
3. Jadikan em-system publik, lalu aktifkan pengaman GitHub (butir 5).
4. Rapikan EmPorium, lalu jadikan publik setelah em-system.

## Cara memeriksa ulang

```powershell
# dari root repo; pola disusun dari nilai yang dicatat secara lokal, bukan ditulis di berkas ini
git grep -nIiE "<nama-produk-privat>|<ip-server-dev>|<nama-pribadi>"
git log --all --oneline -S"<ip-server-dev>"
git ls-files dist
git log --all --format='%ae' | sort -u
```

## Belum diperiksa

- Isi visual screenshot PNG di `plan/executed/`, `doc/report/`, `doc/wiki/data/`.
- Isi rinci lisensi tiap dependensi transitif (butuh `dotnet list package --include-transitive`).
- Hasil penggantian massal di dokumen lama tidak dibaca satu per satu; kalimat tertentu bisa terdengar janggal.
