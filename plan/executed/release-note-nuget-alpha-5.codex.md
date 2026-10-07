# Release note NuGet 0.1.0-alpha.5

Tanggal: 2026-10-07

## Permintaan

Buat release note NuGet berdasarkan perubahan sejak `v0.1.0-alpha.4`, mengikuti `claude.md` dan `doc/ReleaseNote/README.md`.

## Pekerjaan

- Tulis release note bahasa Inggris untuk kelima paket di `scripts/pack-nuget/packages.txt`.
- Jelaskan fitur, perubahan kompatibilitas, dan langkah upgrade sesuai perubahan tiap paket.
- Periksa kelengkapan berkas, metadata versi/tanggal, Summary, dan kecocokan dengan kode.
- Simpan di branch kerja; permintaan ini hanya membuat catatan, tanpa publikasi.

## Hasil

- Selesai: lima release note `doc/ReleaseNote/<PackageId>/0.1.0-alpha.5.md`, tanggal 2026-10-07, versi sebelumnya 0.1.0-alpha.4.
- Isi dicocokkan dengan log dan diff tiap project sejak tag sebelumnya, kontrak deployment, konfigurasi modul, navigasi, publisher, dan dokumentasi engine.
- Perubahan kompatibilitas mencakup penambahan method `ICtnServices`, penghapusan `ContainerTarget.ExtraTags`, penyaringan navigasi, dan migrasi database registry yang wajib sebelum startup.
- Validasi memakai `Get-PackageIds` dan `Get-MissingNotes` dari skrip rilis: semua lima berkas tersedia dan tidak kosong. Judul, tanggal, versi sebelumnya, Summary berupa paragraf teks biasa, dan target tautan lokal valid.
- `git diff --check` lulus. Build/test tidak dijalankan karena perubahan hanya dokumentasi; hasil pengujian fitur dari pekerjaan sebelumnya tidak diuji ulang.
- Tidak ada commit, merge, tag, push, atau publikasi. Penambahan catatan versi baru ke `main` akan memicu workflow rilis sesuai aturan repo.

## Tindak lanjut

- Pengguna meminta commit di workspace pada 2026-10-07. Branch kerja aktif adalah `work-bench`, sesuai aturan repo; kelima release note dan laporan ini disertakan dalam commit lokal.
