# Release note

Setiap rilis paket NuGet `EmSys.*` wajib punya release note di folder ini, bernama persis sama dengan versi paket di nuget.org, tanpa awalan `v`: `0.1.0-alpha.1.md`, `0.1.0-beta.2.md`, `0.1.0.md`.

- Berkas harus sudah ada di `main` sebelum tag `v<versi>` dibuat. `scripts/release-nuget.cmd` menolak versi tanpa berkas ini, dan workflow `publish-nuget.yml` gagal di job `validate` bila berkas tidak ada atau kosong di commit yang di-tag.
- Versi mengikuti [konvensi-penamaan-nuget.md](../konvensi/konvensi-penamaan-nuget.md); prealpha tidak dirilis, jadi tidak perlu release note.
- Release note yang sudah terbit tidak diubah isinya. Koreksi ditulis di release note versi berikutnya.

## Format

Ditulis dalam Bahasa Indonesia untuk pemakai paket (pengembang yang memasang `EmSys.*`), bukan untuk pembaca kode internal. Nama kode, API, dan perintah tetap ditulis apa adanya.

```markdown
# EmSys 0.1.0-alpha.2

Tanggal: 2026-10-07
Sebelumnya: 0.1.0-alpha.1

## Ringkasan
Satu sampai tiga kalimat tentang isi rilis ini.

## Perubahan yang memutus (breaking)
- Apa yang berubah, siapa yang terdampak, dan cara menyesuaikan kode.

## Fitur baru
- ...

## Perbaikan
- ...

## Catatan upgrade
- Migrasi database (`doc/sqlscript/mssql/updates/...`), konfigurasi baru, atau langkah manual lain.
```

Bagian yang tidak punya isi dihapus, kecuali `Ringkasan`. Isi diambil dari commit dan perubahan sejak versi sebelumnya (`git log v<versi-sebelumnya>..HEAD`), dikelompokkan per paket atau fitur bila perlu, tanpa menyebut nama tabel/view/procedure database.
