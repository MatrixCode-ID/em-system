# Release note

Setiap paket NuGet `EmSys.*` yang dirilis wajib punya release note sendiri, satu folder per paket:

```
doc/ReleaseNote/
├─ EmSys.Libs/0.1.0-alpha.1.md
├─ EmSys.Api.Core/0.1.0-alpha.1.md
├─ EmSys.Ui.Core/0.1.0-alpha.1.md
└─ EmSys.Ui.Wpf.Core/0.1.0-alpha.1.md
```

- Nama folder = PackageId persis. Nama berkas = versi paket di nuget.org tanpa awalan `v` (`0.1.0-alpha.1.md`, `0.1.0-beta.2.md`, `0.1.0.md`).
- Paket yang dirilis ditentukan oleh `scripts/pack-nuget/packages.txt`, **bukan** oleh ada-tidaknya release note. Semua paket di daftar itu dirilis bersama dengan satu versi, jadi setiap paket di daftar wajib punya berkas untuk versi tersebut, termasuk paket yang tidak berubah.
- Paket yang sedang dikeluarkan dari daftar (saat ini `EmSys.Ui.Maui.Core`, menunggu CI MAUI) tidak diberi release note sampai kembali dirilis.
- **Menambahkan release note versi baru ke `main` = merilis.** Push/merge ke `main` yang membawa release note versi baru (lengkap untuk semua paket, belum punya tag) langsung menjalankan `publish-nuget.yml`, menunggu approval, lalu merilis dan membuat tag `v<versi>`. Satu versi per merge. Tulis release note di `work-bench` dan baru merge ke `main` saat siap rilis.
- Untuk jalur manual, berkas harus sudah ada di `main` sebelum tag `v<versi>` dibuat. `scripts/release-nuget.cmd` menolak versi yang release note-nya belum lengkap, dan job `validate` di `publish-nuget.yml` gagal bila ada berkas yang tidak ada atau kosong di commit yang di-tag.
- Versi mengikuti [konvensi-penamaan-nuget.md](../konvensi/konvensi-penamaan-nuget.md); prealpha tidak dirilis, jadi tidak perlu release note.
- Release note yang sudah terbit tidak diubah isinya. Koreksi ditulis di release note versi berikutnya.

## Format

Ditulis dalam Bahasa Indonesia untuk pemakai paket (pengembang yang memasang paket itu), bukan untuk pembaca kode internal. Nama kode, API, dan perintah tetap ditulis apa adanya.

```markdown
# EmSys.Api.Core 0.1.0-alpha.2

Tanggal: 2026-10-07
Sebelumnya: 0.1.0-alpha.1

## Ringkasan
Satu sampai tiga kalimat tentang perubahan paket ini di rilis ini.

## Perubahan yang memutus (breaking)
- Apa yang berubah, siapa yang terdampak, dan cara menyesuaikan kode.

## Fitur baru
- ...

## Perbaikan
- ...

## Catatan upgrade
- Migrasi database (`doc/sqlscript/mssql/updates/...`), konfigurasi baru, atau langkah manual lain.
```

- Bagian yang tidak punya isi dihapus, kecuali `Ringkasan`.
- **Isi `## Ringkasan` menjadi tab Release Notes di nuget.org** (diambil otomatis saat pack, ditambah tautan ke berkas lengkap di tag rilis). nuget.org menampilkannya sebagai teks biasa, jadi tulis Ringkasan sebagai paragraf pendek tanpa daftar, tabel, atau heading.
- Seluruh isi release note semua paket juga digabung otomatis menjadi halaman GitHub Release `v<versi>`. Jangan menulis ulang isi ini di tempat lain.
- Paket yang tidak berubah tetap diberi berkas, cukup `Ringkasan`: "Tidak ada perubahan; versi naik mengikuti rilis bersama paket EmSys lainnya."
- Rilis pertama sebuah paket (`Sebelumnya: —`) berisi ringkasan isi paket itu, bukan daftar perubahan: bagian `Isi paket` menggantikan `Fitur baru`/`Perbaikan`, ditambah `Catatan pemakaian` (target framework, dependensi, namespace yang berbeda dari PackageId, perintah instal).
- Bagian opsional `Catatan pemakaian` boleh dipakai di rilis mana pun untuk informasi yang tetap berlaku (target, dependensi, cara pasang).
- Perubahan yang menyentuh beberapa paket (mis. kontrak di `EmSys.Libs`, backend di `EmSys.Api.Core`, layar di `EmSys.Ui.Wpf.Core`) ditulis di masing-masing paket sesuai bagiannya, dengan menyebut paket terkait.
- Isi diambil dari commit dan perubahan sejak versi sebelumnya pada folder project paket itu (`git log v<versi-sebelumnya>..HEAD -- <folder project>`), tanpa menyebut nama tabel/view/procedure database.
