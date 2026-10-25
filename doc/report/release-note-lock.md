# Laporan release note lock

Tanggal: 2026-10-26. Status: seksi A–E selesai. Branch: `work-bench`. Tanpa push.
Plan: [release-note-lock.codex.md](../../plan/executed/release-note-lock.codex.md).

## Hasil implementasi

- **A:** kontrak `release.json` memiliki `version` opsional yang ikut ditandatangani; dokumentasi dan dua vektor baru disiapkan sebelum kode. Public key baru tersimpan; private key hanya berada di memori dan dibuang. Manifest/signature vektor akhir memakai LF. Vektor dan public key lama tidak berubah.
- **B:** parser versi lengkap membuang metadata, membandingkan core/channel/N secara numerik, dan memakai BigInteger agar tidak membatasi angka SemVer. Logika folder bersama memvalidasi path relatif, nama berkas, Summary, versi target, status riwayat, dan rentang eksklusif From/inklusif To. Serializer memvalidasi `version` bila ada.
- **C:** profil Publish memiliki lock/folder, pemilih folder pada Advanced, serta pembatasan mode tunggal dan Standard tagging. Build/Push diperiksa di Publisher; Push memeriksa ulang target/berkas dan menolak versi artifact yang berubah. Halaman menyediakan status, pratinjau baca saja, refresh, dan riwayat. Run lock menyimpan `ReleaseNotesFile`, bukan salinan teks.
- **D:** editor Release Manager menampilkan versi dan lock/folder pada Product Identity. Prepare Windows diperiksa sebelum mengosongkan publish folder; Android diperiksa sesudah membaca ulang identity host. Sync memeriksa ulang target/berkas dan kecocokan PreparedVersionName Android, lalu menandatangani `version` hanya saat lock aktif. Verify menampilkan versi. Format profil tetap 2; field baru opsional dan profil lama default lock off.
- **E:** tujuh guide yang disebut plan diperbarui, termasuk template, basis direktori profil, efek Export/Import, batasan mode, riwayat dan troubleshooting.

## Build dan tes RunBay

Seluruh pelaksanaan menggunakan app RunBay `em-system` yang sudah terdaftar. Action lama diperiksa terlebih dahulu; action khusus berikut ditambahkan tanpa menggandakan action lama. Script PowerShell pelaksanaan tersimpan pada action RunBay. Harness/log berada di `../.artefacts/em-system/scripts/release-note-lock-smoke/`; generator di `../.artefacts/em-system/scripts/release-version-vectors/`.

| Action | Run id | Hasil |
| --- | --- | --- |
| `generate-release-version-vectors` | `20261026-050058-generate-release-version-vectors-81fb` | 2 vektor akhir dibuat; exit 0, status success. |
| `build-release-note-lock` | `20261026-045901-build-release-note-lock-bfcb` | WPF: 0 error, 406 warning; MAUI: 0 error, 3 warning; exit 0. |
| `test-release-note-lock-filtered` | `20261026-045526-test-release-note-lock-filtered-13ed` | 130 lulus, 0 gagal, 0 skip. |
| `test-release-note-lock-full` | `20261026-045931-test-release-note-lock-full-ea47` | Suite penuh dijalankan sekali: 349 lulus, 0 gagal, 0 skip; exit 0. |
| `smoke-release-note-lock` | `20261026-045736-smoke-release-note-lock-408f` | 6 pemeriksaan lulus: Windows Sync, signature/hash/Verify/version, status Released, penolakan versi sama, lock off, Android Sync. Exit 0. |
| `test-release-note-lock-rust` | `20261026-045752-test-release-note-lock-rust-d7b9` | `cargo test --locked`: 93 lulus, 0 gagal, 0 diabaikan; harness tambahan `version-valid`: 1 lulus. |
| `test-release-note-lock-vectors` | `20261026-050117-test-release-note-lock-vectors-77a2` | Setelah regenerasi LF: 10 tes C# lulus, 0 gagal/skip; 1 smoke Rust lulus. |

Pada dua run Rust/vektor terakhir, output seluruh pemeriksaan sudah selesai tetapi RunBay masih menandai running. `stop_run` menutup run milik agent tersebut; hasil akhirnya `exit_code: 0`, status `stopped`. Angka lulus di atas berasal dari output pemeriksaan, bukan dari klaim status success RunBay.

Build awal `20261026-045304-build-release-note-lock-b1fb` juga lulus. Percobaan terfilter pertama `20261026-045359-test-release-note-lock-filtered-f64d` menjalankan 129 tes, dengan 3 kegagalan folder tidak sah; diperbaiki dengan menangkap InvalidDataException. Satu kasus urutan angka besar kemudian ditambahkan, sehingga hasil akhir menjadi 130.

Smoke awal `20261026-045643-smoke-release-note-lock-cb2b` gagal compile karena import System.IO pada harness; percobaan `20261026-045650-smoke-release-note-lock-2915` melewati 5 pemeriksaan lalu gagal karena fixture belum mengisi AndroidTargetFramework. Kedua masalah fixture diperbaiki, percobaan ketiga lulus 6/6. Run Rust awal `20261026-045642-test-release-note-lock-rust-9600` memiliki 2 kegagalan parser expected.txt; koreksinya dijelaskan di bawah. Tidak ada kegagalan yang membutuhkan lebih dari tiga putaran perbaikan.

Generator awal `20261026-044701-generate-release-version-vectors-3b47` menghasilkan dua vektor dan exit 0, lalu ditutup saat RunBay tetap running. Vektor itu diganti dengan hasil generator LF akhir dan diuji ulang secara terfilter. Build backend tidak diperlukan karena Em.Libs tidak disentuh. Warning build tetap tercatat; build tidak dinyatakan bebas warning.

## Keputusan implementasi di luar rincian plan

1. Parser strip lama dipertahankan. Parser lengkap memakai nilai terpisah dengan angka BigInteger, sehingga perilaku input strip lama tidak berubah.
2. Riwayat dan gabungan rentang memakai satu kontrol bersama untuk kedua halaman, sedangkan validasi folder/versi tetap bebas WPF.
3. `expected.txt` ternyata dibaca tes Rust dengan grammar ketat: hanya baris keyId dan tiga kolom file. Teks penjelasan Android yang sudah ada serta tambahan penjelasan version tidak dapat dibaca parser itu. Teks tersebut dikeluarkan dari expected.txt; hasil/keyId vektor baru disimpan di `version-expected.txt`, dan penjelasan Android tetap di kontrak 2.5. Runtime maupun tes Rust tidak diubah. Signature, manifest, binary, dan public key vektor lama tetap identik.
4. Pembacaan target Folder pada guard menggunakan pembacaan file langsung, agar kesalahan izin tidak tersamar menjadi file yang belum ada. Target CDN tetap memakai pembaca yang sudah tersedia dan pemeriksaan manifest server.

## Commit

- `0bccfc0` — Add versioned release note locks to Publish and Release Manager.
- `eba2a77` — Normalize signed release version vectors to LF.
- Laporan dan pemindahan plan dibuat dalam commit penutupan terpisah setelah kedua commit implementasi tersebut.

Branch diperiksa sebelum setiap commit; seluruh commit hanya pada `work-bench`. Tidak ada push.

## Verifikasi tertunda

- Uji visual tidak dijalankan: form profil Publish Advanced, halaman Publish, editor profil Release Manager, halaman Release Manager, dan panel riwayat kedua layar.
- Push nyata ke registry BuiltIn/Custom, Sync nyata ke CDN, serta kondisi balapan antar developer di server nyata belum diuji. Smoke hanya memakai target Folder sementara dan fixture APK, bukan build/instalasi APK nyata.
- Tidak ada aplikasi GUI, emulator, perangkat, screenshot atau render PNG yang dibuka/dijalankan. Hal-hal tertunda ini tidak dinyatakan lulus.

## Tindakan manual terblokir policy

Tidak ada tindakan ditolak policy. Tidak ada skrip manual di `plan/release-note-lock-manual/` yang perlu disiapkan atau dijalankan pengguna.
