# Dokumentasi publik dalam bahasa Inggris

- Tanggal: 2026-10-06
- Status: diskusi

## Latar

Repo em-system publik. Pada 2026-10-06 seluruh `doc/engine/` sudah ditulis ulang dalam bahasa Inggris, dirapikan,
dicocokkan dengan kode, diberi indeks `doc/engine/README.md`, dan diberi screenshot hasil render harness
(`doc/engine/images/`, tema terang, data contoh).

## Yang sudah jelas

- Nama berkas `doc/engine/*.md` tetap supaya tautan dari `claude.md`, README, dan release note tidak putus.
- Bagian "Maintainer notes" di dokumen engine merujuk harness di `..\.artefacts\em-system\scripts\`, yang ada di luar repo.

## Pertanyaan terbuka

- Dokumen lain yang masih berbahasa Indonesia, mana yang ikut diterjemahkan:
  `src/backend/README.md`, `src/frontend/README.md`, `doc/release-format.md`,
  `doc/panduan-trusted-publishing-nuget.md`, `doc/ReleaseNote/README.md`. Release note yang sudah terbit tidak boleh
  diubah. (`README.md` root, `tests/README.md`, dan konvensi penamaan sudah diterjemahkan 2026-10-06; folder `doc/konvensi/` diganti `doc/convention/`
  dengan berkas `dahlia-convention.md`, `container-naming.md`, `nuget-naming.md`, semua rujukan sudah diperbarui.)
- ~~Prompt konfirmasi `upload-nuget.ps1` (`Push ke ... ? [y/N]`) dan pesan skrip lain masih berbahasa Indonesia.~~ Selesai 2026-10-07: prompt dan pesan skrip kini berbahasa Inggris (plan [plan/executed/xml-comment-bahasa-inggris.md](../../plan/executed/xml-comment-bahasa-inggris.md)).
- `doc/ideas/`, `doc/report/`, dan `plan/` adalah catatan kerja internal; tetap berbahasa Indonesia atau tidak?
- Apakah `claude.md` (instruksi agent) tetap berbahasa Indonesia?
- Fitur engine yang belum punya panduan: konfigurasi `EmApiConfig`/debug token, action dispatcher dan konvensi
  nama action, business task, navigasi/MultiTab WPF, client MAUI.
- Screenshot layar yang belum punya harness render (User Manager tab Robots, Approval Manager, CDN Manager, login,
  hub MY TASKS). Harness `publish-render` menampilkan path workspace lokal di form profile, jadi gambarnya belum dipakai;
  harness `storage-settings-render` menampilkan path fixture (gambar di dokumen sudah di-crop).
- Apakah screenshot perlu versi tema gelap juga.
