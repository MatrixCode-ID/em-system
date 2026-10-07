# Komentar kode, XML documentation, dan string user dalam bahasa Inggris

- Tanggal: 2026-10-07
- Eksekutor: Claude Code
- Branch: `work-bench` (jangan commit ke `main`)

## Latar

Repo em-system sudah publik/open source, tetapi sebagian besar komentar kode masih berbahasa Indonesia. Survei
2026-10-07: 424 dari 497 berkas `.cs` punya XML comment (~16.000 baris `///`), ±335 berkas di antaranya berbahasa
Indonesia; ±750 baris komentar `//` Indonesia; ±27 komentar XAML; Launcher Rust 57 berkas; beberapa komentar
`.props`/`.csproj`/SQL/PS1/PY/workflow; serta string yang tampil ke user (UI, pesan error, prompt skrip).
`GenerateDocumentationFile` belum aktif, jadi paket NuGet `EmSys.*` belum membawa berkas XML untuk IntelliSense.

Aturan permanen sudah dicatat di `CLAUDE.md` (bullet "Komentar kode dan XML comment wajib bahasa Inggris").

## Keputusan final (dari pengguna, 2026-10-07)

1. **Cakupan terjemahan ke bahasa Inggris:**
   - XML documentation comment `///` di semua `.cs` (library, host, module, tests).
   - Komentar biasa `//` dan `/* */` di `.cs`.
   - Komentar XAML `<!-- -->`.
   - Komentar di `.csproj`, `Directory.Build.props`, `*.props`/`*.targets`, `doc/sqlscript/**/*.sql` (termasuk header
     berkas), `scripts/**/*.ps1`, `scripts/**/*.cmd`, `scripts/_py/*.py`, `scripts/pack-nuget/packages.txt`,
     `.github/workflows/*.yml`, `compose.yml`/`Dockerfile`/`.env.example`.
   - Launcher Rust (`src/frontend/Launcher`): komentar `//`, `///`, `//!`, dan string yang tampil ke user.
   - **String yang tampil ke user**: teks UI (XAML dan kode), pesan exception/error API, pesan validasi, prompt dan
     output skrip, pesan log. **Diganti langsung ke bahasa Inggris**, tanpa `.resx`/infrastruktur lokalisasi.
2. **Pembersihan komentar:** hanya komentar yang **mengulang isi kode** (menyatakan hal yang sudah jelas dari nama
   member/statement, mis. `// set nama`, `/// <summary>Constructor.</summary>` yang tidak menambah informasi) yang
   dihapus. Yang lain **dipertahankan dan diterjemahkan**: kode yang di-comment-out dibiarkan apa adanya (teks
   komentar di sekitarnya diterjemahkan), catatan riwayat/alasan keputusan, dan TODO/HACK.
3. **Komentar informasi build/konfigurasi** (mis. workaround `MAUIR0001` di `Directory.Build.props`, catatan
   `ArtefactsPath`, catatan `PackageId` `EmSys.`, catatan workload MAUI di `packages.txt`) dipindah ke dokumen baru
   **`doc/engine/build.md`** (bahasa Inggris, judul "Build and packaging"), ditaut dari `doc/engine/README.md`. Di
   berkas asal tinggal komentar Inggris satu baris yang merujuk dokumen itu, mis.
   `<!-- See doc/engine/build.md#resizetizer-pin -->`.
4. **`GenerateDocumentationFile` diaktifkan** untuk lima library paket (`packages.txt`): `Em.Libs`, `Em.Api.Core`,
   `Em.Ui.Core`, `Em.Ui.Wpf.Core`, `Em.Ui.Maui.Core`, dan **warning CS1591 dibereskan** (member public/protected yang
   belum punya XML comment dilengkapi dalam bahasa Inggris), bukan di-`NoWarn`.
5. **Eksekutor:** Claude Code, bertahap **per project** (seksi di bawah).
6. **Commit:** satu commit per seksi/project di `work-bench`, pesan commit bahasa Indonesia. Tidak push, tidak ke `main`.
7. **Release note:** tidak ditulis. Poin perubahan yang terlihat (string UI/error kini Inggris, paket membawa XML
   doc) dicatat di laporan eksekusi untuk dipakai saat rilis berikutnya. Jangan menambah berkas ke `doc/ReleaseNote/`.

## Aturan gaya terjemahan

- Bahasa Inggris teknis yang ringkas, kalimat lengkap diakhiri titik di `<summary>`/`<remarks>`. `<summary>` diawali
  kata kerja orang ketiga untuk method ("Gets…", "Creates…", "Returns…") dan frasa benda untuk tipe/properti.
- Pertahankan semua tag XML (`<see cref>`, `<c>`, `<paramref>`, `<para>`, `<list>`) dan pastikan `cref` tetap valid.
- Isi harus sesuai perilaku kode saat ini. Bila teks lama sudah tidak cocok dengan kode, tulis yang benar (bukan
  terjemahan harfiah) dan catat di laporan.
- Istilah domain dipertahankan konsisten: approval, request, step, level, claim, role, robot, root, manifest, blob,
  tag, publish, profile, card, navigation. Nama tabel/kolom (`ta_*`, `vi_*`, `c*`) boleh tetap disebut di komentar kode.
- Jangan menulis path absolut mesin, username, IP, atau kredensial (repo publik).
- Jangan mengubah logika. Perubahan string user hanya teks; format placeholder (`{0}`, interpolasi) dan urutan argumen
  dipertahankan.
- Prompt konfirmasi skrip: ubah teks ke Inggris (mis. `Push to ... ? [y/N]`); jawaban yang diterima tetap `y`/`yes`.
- Test yang meng-assert teks pesan (xUnit, harness) disesuaikan dengan string baru.
- Berkas yang **tidak** diubah: release note yang sudah terbit (`doc/ReleaseNote/**`), `plan/`, `doc/ideas/`,
  `doc/report/`, `CLAUDE.md`, `AGENTS.md` (tetap Indonesia sebagai catatan kerja internal). Dokumen Markdown lain yang
  masih Indonesia (`src/backend/README.md`, `src/frontend/README.md`, dll.) di luar cakupan plan ini; tetap ditangani
  catatan [doc/ideas/dokumentasi-bahasa-inggris.md](../../doc/ideas/dokumentasi-bahasa-inggris.md).

## Cara menemukan sisa teks Indonesia

Pakai pencarian kata fungsi Indonesia pada komentar dan string, per project, mis. dari Git Bash:

```bash
re='\b(yang|untuk|dengan|tidak|dari|atau|jika|akan|sudah|belum|harus|bila|saat|dipakai|berisi|ini|itu|dan|gagal|berhasil|silakan|pilih|simpan|hapus)\b'
git grep -niE "(//|<!--|--|#|\").*$re" -- <folder-project>
```

Daftar kata ini tidak lengkap; baca setiap berkas yang disentuh secara utuh, jangan hanya baris hasil grep.

## Seksi

Urutan mengikuti aturan "kode dulu, analisa di akhir": kerjakan semua seksi 1–10, build per seksi bila murah, commit
per seksi; review menyeluruh dan pengujian lengkap di seksi 11.

### Seksi 1 — `doc/engine/build.md` dan komentar build

- Buat `doc/engine/build.md` (Inggris): solution dan perintah build/test/pack (ambil dari `CLAUDE.md` dan README yang
  ada), `ArtefactsPath`, aturan `PackageId` `EmSys.`, ikon/README paket, `PackageReleaseNotes`, pin
  `Microsoft.Maui.Resizetizer` 10.0.100 (alasan MAUIR0001), workload `maui-android` saat pack, `global.json`/MTP,
  dan `GenerateDocumentationFile` (seksi 2). Beri anchor per topik.
- Tautkan dari `doc/engine/README.md`.
- Ganti komentar build di `Directory.Build.props`, semua `.csproj`, `global.json` (bila ada komentar),
  `scripts/pack-nuget/packages.txt`, `.github/workflows/*.yml`, `src/backend/compose.yml`, `Dockerfile`,
  `.env.example` dengan komentar Inggris singkat + rujukan `doc/engine/build.md#...`. Komentar yang menjelaskan baris
  spesifik (bukan informasi build umum) cukup diterjemahkan.
- Commit: `Pindahkan catatan build ke doc/engine/build.md dan terjemahkan komentar build`.

### Seksi 2 — Aktifkan `GenerateDocumentationFile`

- Tambahkan `<GenerateDocumentationFile>true</GenerateDocumentationFile>` untuk lima library paket. Pilihan: di
  `Directory.Build.props` dengan kondisi `'$(IsPackable)' != 'false'` dan bukan project test, atau langsung di lima
  `.csproj`; pilih yang paling sederhana dan konsisten, catat di laporan.
- Jangan `NoWarn` CS1591. Warning CS1591 diselesaikan di seksi 3–7 sambil menerjemahkan project tersebut.
- Periksa juga warning XML lain yang muncul (CS1572/CS1573/CS1574/CS1584/CS1587/CS1734) dan perbaiki di seksi
  project masing-masing.
- Commit digabung dengan seksi 3 bila build seksi 2 sendirian penuh warning; kalau dipisah, commit:
  `Aktifkan GenerateDocumentationFile untuk library paket`.

### Seksi 3 — `Em.Libs` (`src/shared/Em.Libs`, ±60 berkas komentar Indonesia)

- Terjemahkan `///`, `//`, string user (±31 baris), hapus komentar yang mengulang kode.
- Lengkapi XML comment member public/protected sampai CS1591 = 0 untuk project ini.
- `dotnet build src/backend/Em.Api.slnx` (Em.Libs ikut) tanpa warning XML untuk Em.Libs.
- Commit: `Terjemahkan komentar dan XML doc Em.Libs ke bahasa Inggris`.

### Seksi 4 — `Em.Api.Core` (`src/backend/Em.Api.Core`, ±71 berkas)

- Sama seperti seksi 3, termasuk pesan exception/error API dan pesan log.
- Sesuaikan test di `tests/Em.Api.Core.Tests` dan `tests/Em.Api.Core.IntegrationTests` yang meng-assert pesan.
- Build `src/backend/Em.Api.slnx`, CS1591 = 0 untuk Em.Api.Core.
- Commit: `Terjemahkan komentar, XML doc, dan pesan Em.Api.Core ke bahasa Inggris`.

### Seksi 5 — `Em.Ui.Core` (`src/shared/Em.Ui.Core`, ±37 berkas)

- Sama seperti seksi 3, termasuk teks UI di view model.
- Build `src/frontend/Em.Ui.Wpf.slnx`, CS1591 = 0 untuk Em.Ui.Core.
- Commit: `Terjemahkan komentar, XML doc, dan teks UI Em.Ui.Core ke bahasa Inggris`.

### Seksi 6 — `Em.Ui.Wpf.Core` (`src/shared/Em.Ui.Wpf.Core`, ±116 berkas, terbesar)

- `.cs` dan `.xaml`: komentar, XML doc, teks UI (`Content`, `Text`, `ToolTip`, `AutomationProperties.Name`,
  `Header`, pesan dialog/MessageBox), pesan error.
- Teks UI yang lebih panjang dalam bahasa Inggris bisa mengubah layout; catat layar yang perlu dicek render.
- Sesuaikan `tests/Em.Ui.Wpf.Core.Tests` dan `tests/Em.Ui.Core.Tests`.
- Build `src/frontend/Em.Ui.Wpf.slnx`, CS1591 = 0 untuk Em.Ui.Wpf.Core.
- Bila token menipis, boleh dipecah menjadi dua commit (Navigations/ lalu sisanya).
- Commit: `Terjemahkan komentar, XML doc, dan teks UI Em.Ui.Wpf.Core ke bahasa Inggris`.

### Seksi 7 — `Em.Ui.Maui.Core` (`src/shared/Em.Ui.Maui.Core`, ±45 berkas)

- Sama seperti seksi 6 untuk MAUI (XAML + C#).
- Build `src/frontend/Em.Ui.Maui.slnx` (butuh workload maui-android), CS1591 = 0 untuk Em.Ui.Maui.Core.
- Commit: `Terjemahkan komentar, XML doc, dan teks UI Em.Ui.Maui.Core ke bahasa Inggris`.

### Seksi 8 — Host, module uji, dan tests

- `src/backend/Em.Api`, `src/frontend/Em.Ui.Wpf`, `src/frontend/Em.Ui.Maui`, `src/modules/Em.Test` (±28 berkas),
  semua project di `tests/`: komentar, XML doc, teks UI/pesan. Tidak diwajibkan CS1591 (bukan paket).
- Build ketiga solution.
- Commit: `Terjemahkan komentar host, module Em.Test, dan tests ke bahasa Inggris`.

### Seksi 9 — Launcher Rust (`src/frontend/Launcher`, ±57 berkas)

- Komentar `//`, `///`, `//!`, komentar `Cargo.toml`, dan string yang tampil ke user (±4 baris) serta skrip `.ps1`
  di folder itu.
- `cargo build` (dan `cargo test` bila ada) di folder Launcher; kalau toolchain Rust tidak tersedia, catat sebagai
  verifikasi tertunda.
- Commit: `Terjemahkan komentar dan pesan Launcher ke bahasa Inggris`.

### Seksi 10 — Skrip, SQL, dan konfigurasi lain

- `scripts/**` (`.ps1`, `.cmd`, `_py/*.py`): komentar, `Write-Host`/prompt/pesan error (mis. `Push ke ... ? [y/N]` →
  `Push to ... ? [y/N]`). Logika parsing jawaban tidak diubah.
- `doc/sqlscript/**`: header dan komentar SQL, serta pesan `RAISERROR`/`THROW` bila ada. Jangan mengubah DDL.
- Sisa berkas konfigurasi yang belum tersentuh seksi 1 (mis. `.gitattributes`, `emapi-config.example.json` bila
  memuat teks Indonesia).
- Jalankan `pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile(...)"` untuk setiap
  `.ps1` yang diubah, dan `python -m py_compile` untuk `.py`.
- Commit: `Terjemahkan komentar dan pesan skrip serta SQL ke bahasa Inggris`.

### Seksi 11 — Verifikasi dan review akhir (sekali, setelah semua kode selesai)

1. Build penuh: `dotnet build` ketiga solution, nol warning CS1591/CS157x untuk lima library paket.
2. `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx`.
3. `dotnet pack` lima library (atau `scripts/pack-nuget`), periksa tiap `.nupkg` berisi `lib/<tfm>/<Assembly>.xml`.
4. Grep sisa kata Indonesia di seluruh repo (kecuali berkas yang dikecualikan) dan selesaikan sisanya.
5. Review diff: tidak ada perubahan logika, placeholder format string utuh, `cref` valid, tidak ada path/kredensial.
6. Render layar WPF penting (Login, User Manager, Container Manager, NuGet Manager tab Publish, Approval Manager)
   tema terang/gelap dengan harness di `..\.artefacts\em-system\scripts\` untuk cek teks Inggris yang lebih panjang
   tidak terpotong. Yang tidak sempat dirender dicatat sebagai verifikasi tertunda.
7. Perbarui `doc/ideas/dokumentasi-bahasa-inggris.md`: tandai pertanyaan "prompt skrip" selesai, tautkan plan ini.
8. Tulis laporan eksekusi di akhir berkas plan ini, lalu pindahkan ke `plan/executed/`.
   Commit terakhir: `Tutup plan komentar dan XML doc bahasa Inggris`.

## Laporan eksekusi

Dieksekusi 2026-10-07 oleh Claude Code di branch `work-bench`; satu commit per seksi (pesan commit berbahasa
Indonesia), tidak ada push dan tidak ada release note baru.

### Ringkasan per seksi

| Seksi | Commit | Isi |
|---|---|---|
| 1-2 | `fdece49` dan commit terkait sebelum `2279ace` | `GenerateDocumentationFile` aktif untuk lima library, catatan build dipindah ke `doc/engine/build.md` |
| 3 | `2279ace` | Em.Libs |
| 4 | `56b1d42` | Em.Api.Core |
| 5 | `83ec169` | Em.Ui.Core |
| 6 | `85da082` | Em.Ui.Wpf.Core (kode, XAML, doc enum dan record Publish/Release) |
| 7 | `b3652e1` | Em.Ui.Maui.Core (kode dan XAML) |
| 8 | `5085ea7` | host (`Em.Api`, `Em.Ui.Wpf`, `Em.Ui.Maui`), module `Em.Test`, dan `tests/` |
| 9 | `62023e3` | Launcher Rust (58 berkas: `//`, `///`, `//!`, string `build.rs`) |
| 10 | `8ecb92f` | `scripts/**` (ps1, cmd, py), komentar `doc/sqlscript/**`, `.gitignore`, `emapi-config.example.json` |
| 11 | commit pemindaian akhir | sisa teks Indonesia yang lolos dari detektor per-seksi (28 baris), dokumentasi, penutupan plan |

### Hasil verifikasi

- `dotnet build` ketiga solution (`Em.Api.slnx`, `Em.Ui.Wpf.slnx`, `Em.Ui.Maui.slnx`) dengan `--no-incremental`:
  0 error; nol CS1591, CS157x, CS0419, CS1570 untuk lima library paket. Satu-satunya warning solution WPF adalah
  CS8604 (nullable) di `Em.Test.Wpf/TestService.cs`, yang sudah ada sebelum plan ini dan tidak diubah.
- Test lulus semua bila dijalankan langsung dari executable-nya: `Em.Libs.Tests` 4, `Em.Api.Core.Tests` 71,
  `Em.Api.Core.IntegrationTests` 28 (SQL Server lokal), `Em.Ui.Core.Tests` 10, `Em.Ui.Wpf.Core.Tests` 44.
- `cargo test` Launcher (dengan environment MSVC dari `vcvars64.bat`): 72 test lulus; `cargo fmt --check` bersih.
- `dotnet pack` kelima library: setiap `.nupkg` memuat `lib/<tfm>/<Assembly>.xml` (net10.0, net10.0-windows7.0,
  net10.0-android36.0).
- Parser PowerShell (`Parser::ParseFile`) 0 error untuk semua `.ps1`; `python -m py_compile` lulus untuk kedua
  skrip `_py`.
- Render tema terang/gelap dengan harness di `..\.artefacts\em-system\scripts\`: `container-manager-render`
  (tab, selection, narrow, disabled), `publish-render` (42 layar tab Publish NuGet dan Container, termasuk narrow),
  `deploy-render` (kartu DEPLOY dan dialog), `role-manager-render`. Teks Inggris yang lebih panjang tidak terpotong
  pada gambar yang diperiksa (Container Manager terang, Publish Container narrow gelap, form Target terang).

### Temuan dan catatan

- `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx` pada sesi ini melaporkan
  "Zero tests ran" (exit code 5) untuk setiap project, padahal executable test yang sama menjalankan semua test dan
  lulus. Ini masalah runner `dotnet test`/MTP di mesin ini, bukan hasil perubahan komentar (perubahan tidak menyentuh
  logika); perlu diperiksa terpisah. Verifikasi dilakukan lewat executable langsung.
- Detektor blok komentar (`//`, `///`, XAML) per seksi melewatkan sebagian teks: ringkasan satu baris di class
  extension module, `#region`, komentar blok `/* */` di `Program.cs`, pesan di skrip PowerShell yang tidak memuat
  kata-kata umum, `<summary>` pendek ("Judul dialog."), dan string exception ("Prepare ulang"). Semuanya ditemukan
  oleh pemindaian akhir berbasis daftar kata Indonesia pada komentar dan string, lalu diterjemahkan.
- Pesan exception `Publisher.Push` kini "Source/build settings changed; Prepare again." (sebelumnya "...Prepare
  ulang."); kutipannya di `doc/engine/engine-registry-guide.md` dan `.id.md` ikut diperbarui.
- Komentar di `.gitignore`, header SQL, dan komentar `emapi-config.example.json` ikut diterjemahkan. DDL SQL dan nilai konfigurasi tidak diubah. Prompt `Push ke ... ? [y/N]` menjadi
  `Push to ... ? [y/N]`; logika parsing jawaban `y`/`yes` tidak berubah.
- Komentar lama `registry-http-test.py` yang menyebut "garbage collection (tahap 2)" diperbarui menjadi "until
  garbage collection runs", karena GC manual sudah ada.
- Skrip harness `deploy-render` menulis PNG ke folder `out/` di root repo (path relatif). Folder itu dihapus
  setelah render; harness di luar repo itu perlu diubah supaya keluarannya masuk folder artefak.
- Tidak diubah (sengaja, di luar cakupan plan): `doc/ReleaseNote/**`, `plan/`, `doc/ideas/`, `doc/report/`,
  `CLAUDE.md`, `AGENTS.md`, README Indonesia, `doc/release-format.md`, `doc/panduan-trusted-publishing-nuget.md`,
  `doc/wiki/` (basis pengetahuan berbahasa Indonesia), dan `doc/engine/*.id.md`.

### Komentar yang dihapus karena mengulang kode

Komentar yang hanya menyatakan ulang nama member atau statement (mis. ringkasan "Konstruktor" dan "Mengambil/menyetel
nilai X" tanpa informasi tambahan) tidak diterjemahkan, melainkan dihapus atau diganti `<inheritdoc />` pada
implementasi service client yang interface-nya sudah berdoc. Tidak ada daftar per baris; lihat diff per commit.

### Poin untuk release note berikutnya (bahasa Inggris)

- Package users now get IntelliSense documentation: every `EmSys.*` package ships its XML documentation file.
- User-visible strings (UI captions, error messages, validation messages) are English in all packages; callers that
  match on old Indonesian exception text must update.
- The `Publisher.Push` stale-profile message is now "Source/build settings changed; Prepare again."

### Verifikasi tertunda

- Render layar yang tidak punya harness: Login, User Manager tab Users, Approval Manager, dan hub MY TASKS; teks
  Inggris di layar itu belum diperiksa terhadap pemotongan.
- Interaksi mouse, dialog dengan server nyata, MAUI di emulator/perangkat (hanya build dan pack yang diperiksa).
- `dotnet test` melalui runner solution (lihat temuan di atas).
- Tidak ada tindakan terblokir policy, jadi tidak ada skrip manual di `plan/xml-comment-bahasa-inggris-manual/`.
