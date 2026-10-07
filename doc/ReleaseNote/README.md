# Release note

Setiap paket NuGet `EmSys.*` yang dirilis wajib punya release note sendiri, satu folder per paket:

```
doc/ReleaseNote/
├─ EmSys.Libs/0.1.0-alpha.1.md
├─ EmSys.Api.Core/0.1.0-alpha.1.md
├─ EmSys.Ui.Core/0.1.0-alpha.1.md
├─ EmSys.Ui.Wpf.Core/0.1.0-alpha.1.md
└─ EmSys.Ui.Maui.Core/0.1.0-alpha.2.md
```

- Nama folder = PackageId persis. Nama berkas = versi paket di nuget.org tanpa awalan `v` (`0.1.0-alpha.1.md`, `0.1.0-beta.2.md`, `0.1.0.md`).
- Paket yang dirilis ditentukan oleh `scripts/pack-nuget/packages.txt`, **bukan** oleh ada-tidaknya release note. Semua paket di daftar itu dirilis bersama dengan satu versi, jadi setiap paket di daftar wajib punya berkas untuk versi tersebut, termasuk paket yang tidak berubah.
- Paket yang dikeluarkan dari daftar tidak diberi release note sampai kembali dirilis. `EmSys.Ui.Maui.Core` sempat dikeluarkan dan masuk lagi mulai `0.1.0-alpha.2`.
- **Menambahkan release note versi baru ke `main` = merilis.** Push/merge ke `main` yang membawa release note versi baru (lengkap untuk semua paket, belum punya tag) langsung menjalankan `publish-nuget.yml`, menunggu approval, lalu merilis dan membuat tag `v<versi>`. Satu versi per merge. Tulis release note di `work-bench` dan baru merge ke `main` saat siap rilis.
- Setelah paket terbit, job `notify-products` di `publish-nuget.yml` memicu update engine di repo produk (saat ini `MatrixCode-ID/EmPorium-House`, `release.yml` dengan `emsys_version`). Butuh secret repo `PRODUCT_DISPATCH_TOKEN` (token fine-grained, "Actions: Read and write" pada repo produk); tanpa secret atau bila dispatch gagal, rilis NuGet tetap sukses dan produk mengambil versi baru lewat pengecekan nuget.org harian.
- Untuk jalur manual, berkas harus sudah ada di `main` sebelum tag `v<versi>` dibuat. `scripts/release-nuget.cmd` menolak versi yang release note-nya belum lengkap, dan job `validate` di `publish-nuget.yml` gagal bila ada berkas yang tidak ada atau kosong di commit yang di-tag.
- Versi mengikuti [nuget-naming.md](../convention/nuget-naming.md); prealpha tidak dirilis, jadi tidak perlu release note.
- Release note yang sudah terbit tidak diubah isinya. Koreksi ditulis di release note versi berikutnya.

## Format

Ditulis dalam **bahasa Inggris, wajib** (mulai `0.1.0-alpha.3`; versi sebelumnya berbahasa Indonesia dan tidak diubah) untuk pemakai paket (pengembang yang memasang paket itu), bukan untuk pembaca kode internal. Nama kode, API, dan perintah tetap ditulis apa adanya.

```markdown
# EmSys.Api.Core 0.1.0-alpha.2

Date: 2026-10-07
Previous: 0.1.0-alpha.1

## Summary
One to three sentences about what changed in this package in this release.

## Breaking changes
- What changed, who is affected, and how to adapt their code.

## New features
- ...

## Fixes
- ...

## Upgrade notes
- Database migration (`doc/sqlscript/mssql/updates/...`), new configuration, or other manual steps.
```

- Bagian yang tidak punya isi dihapus, kecuali `Summary`. Judul bagian ditulis dalam bahasa Inggris (`Summary`, `Breaking changes`, `New features`, `Fixes`, `Upgrade notes`, `Package contents`, `Usage notes`); pack juga masih mengenali `Ringkasan` pada note lama.
- **Isi `## Summary` menjadi tab Release Notes di nuget.org** (diambil otomatis saat pack, ditambah tautan ke berkas lengkap di tag rilis). nuget.org menampilkannya sebagai teks biasa, jadi tulis Summary sebagai paragraf pendek tanpa daftar, tabel, atau heading.
- Seluruh isi release note semua paket juga digabung otomatis menjadi halaman GitHub Release `v<versi>`. Jangan menulis ulang isi ini di tempat lain.
- Paket yang tidak berubah tetap diberi berkas, cukup `Summary`: "No changes; the version is bumped to follow the shared release of the other EmSys packages."
- Rilis pertama sebuah paket (`Previous: —`) berisi ringkasan isi paket itu, bukan daftar perubahan: bagian `Package contents` menggantikan `New features`/`Fixes`, ditambah `Usage notes` (target framework, dependensi, namespace yang berbeda dari PackageId, perintah instal).
- Bagian opsional `Usage notes` boleh dipakai di rilis mana pun untuk informasi yang tetap berlaku (target, dependensi, cara pasang).
- Perubahan yang menyentuh beberapa paket (mis. kontrak di `EmSys.Libs`, backend di `EmSys.Api.Core`, layar di `EmSys.Ui.Wpf.Core`) ditulis di masing-masing paket sesuai bagiannya, dengan menyebut paket terkait.
- Isi diambil dari commit dan perubahan sejak versi sebelumnya pada folder project paket itu (`git log v<versi-sebelumnya>..HEAD -- <folder project>`), tanpa menyebut nama tabel/view/procedure database.
