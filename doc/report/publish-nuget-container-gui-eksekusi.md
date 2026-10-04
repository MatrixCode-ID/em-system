# Eksekusi migrasi NuPak dan publisher WPF

Tanggal: 2026-10-04. Implementasi selesai; verifikasi Docker dan interaksi pengguna yang disebut di bawah masih tertunda.

Plan: [publish-nuget-container-gui.codex.md](../../plan/executed/publish-nuget-container-gui.codex.md). Panduan: [NuPak](../engine-nupak.md), [publisher](../engine-publish.md), [registry](../engine-registry.md).

## Hasil per fase

| Fase | Hasil |
| --- | --- |
| 1 | NuPak dipindah dari modul EmPorium House ke Em.Api.Core/Em.Libs/Em.Ui.Wpf.Core. Host engine memakai AddNuPak() managed; host produk memakai AddNuPak(path, maxPackageMb) statis. Navigasi admin.nupak dan statistik home tersedia dari engine. |
| 2 | Profil JSON, ProfileStore atomik dengan pemeriksaan perubahan eksternal, folder settings, DPAPI CurrentUser/session, ekspor tanpa secret, workspace milik run, process runner, masking dan riwayat run. |
| 3 | Pembacaan .slnx/.sln/.csproj dan evaluasi MSBuild, form Source/Build/Target/Credentials/Advanced, browse, pemilihan kandidat package/host dan pembacaan ulang metadata. |
| 4 | Pack beberapa sumber, tambah .nupkg yang sudah ada, push NuGet.Protocol in-process, hasil per item, duplicate/retry/cancel, catatan rilis wajib, verifikasi identity/SHA-512. |
| 5 | Dockerfile, LocalImage, Template, Compose build dan Set; file list bersama/komplemen; generated/existing Dockerfile; satu publish untuk Base/Module; App menggunakan digest Base atau Last published/Explicit reference; auth sementara dan verifikasi manifest. Implementasi dibuild, operasi Docker belum diuji. |
| 6 | Tab Publish bersama pada kedua manager, card refresh independen, checkbox artefak, konfirmasi tujuan, live log, settings dan history termasuk profil terhapus. Render terang/gelap dilakukan. |
| 7 | Panduan publisher, tautan dari NuPak/registry, pembaruan catatan ide serta claude.md kedua repo dan laporan ini. |

## Peta pemindahan dan penghapusan

| Sumber di EmPorium House | Tujuan di em-system |
| --- | --- |
| EmPoriumHouse.NuPak.Api: DbContext, endpoint, operations, robot access, services, settings, store, startup | src/backend/Em.Api.Core/Api/NuPak/ dengan namespace Em.Api.Core.NuPak |
| NuPakModuleExtensions API | src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.NuPak.cs; startup dipisahkan menjadi NuPakStartup.cs |
| EmPoriumHouse.NuPak.Models/INuPakServices.cs dan Entities.cs | src/shared/Em.Libs/Api.Core.Models/INuPakServices.cs dan NuPakEntities.cs |
| EmPoriumHouse.NuPak.Models.Ui/NuPakDisplay.cs | src/shared/Em.Ui.Wpf.Core/Shared/NuPakDisplay.cs |
| EmPoriumHouse.NuPak.Wpf/NuPakService.cs dan Navigations/NuPakManager* | src/shared/Em.Ui.Wpf.Core/Api.Core/NuPakService.cs dan Navigations/NuPakManager* |
| doc/nuget-server.md dan doc/nuget-multifeed-upgrade.md | doc/engine-nupak.md dan doc/engine-nupak-multifeed-upgrade.md |
| SQL sets/NuPak.sql dan updates/20261003-NuPakMultiFeed.sql | doc/sqlscript/mssql/ dengan lokasi relatif yang sama |
| scripts/nuget-smoke termasuk legacy-schema.sql | scripts/nupak-smoke/ |
| Bagian manager pada scripts/module-card-qa | scripts/nupak-manager-render/; harness home produk tetap di EmPorium House |

Empat project NuPak produk dan seluruh source modul lamanya dihapus. ProjectReference dan pemanggilan AddNuPakModule produk dicabut. Home/card, branding dan host produk tetap berada di produk. Engine tidak memiliki referensi balik ke modul produk; realm HTTP menjadi `Em NuGet`.

## Keputusan implementasi dan hasil review

- Dokumen managed storage tetap kompatibel dengan dokumen lama tanpa bagian NuGet: default enabled, `./data/nuget`, 250 MB. AddManagedStorageSettings tidak otomatis menyalakan NuPak. Startup AddNuPak tetap memeriksa skema, termasuk ketika endpoint dinonaktifkan.
- Managed enabled adalah keputusan startup; NuPakEnable tetap toggle runtime terpisah. Status/feed effective enabled kini mempertimbangkan kedua tingkat tersebut.
- NuGet.Protocol dan NuGet.Packaging memakai 7.0.3; pemilihan versi menghindari advisory [GHSA-g4vj-cjjj-v7hg](https://github.com/NuGet/NuGet.Client/security/advisories/GHSA-g4vj-cjjj-v7hg). Build akhir tidak menghasilkan warning audit.
- UI profil memakai editor field/list bersama. Kandidat dari MSBuild ditinjau lewat Re-read; pilihan package eksplisit dapat kosong tanpa diam-diam berubah menjadi seluruh solution. Template menolak library/desktop sebagai host.
- Prepare menyimpan nupkg dan shared publish output di direktori log agar Push berikutnya dapat memakai hasil yang sama setelah workspace run dibersihkan. Delete run menghapus juga artefak tersebut; ini dijelaskan dalam konfirmasi. Source/build yang berubah memerlukan Prepare ulang, termasuk perubahan child Set; target dapat diperbarui. Reload/edit target pada profil yang sama mempertahankan Prepared di UI.
- Set menjalankan langkah berurutan. App From this run dibangun saat Push sesudah digest Base tersedia; shared output hasil Prepare digunakan kembali. Nested Set ditolak; langkah berulang/operasi bersamaan pada child diblokir. Langkah build-disabled menggunakan LocalImage atau image dari log sebelumnya.
- Default Docker auth terisolasi selama Build dan Push. Pilihan Use my Docker login dihormati per langkah Set; direktori auth dibersihkan sekalipun Keep workspace aktif. Verify/latest tags dapat membaca login Docker secara read-only dari config atau credential helper untuk host yang sama, tanpa menyimpan credential itu ke profil. Tag versi didorong sebelum tag tambahan; kegagalan tag tambahan dicatat sebagai hasil parsial.
- Compose config tidak dialirkan mentah ke log karena dapat mengandung nilai environment. Mapping built-in memeriksa root/container aktif; repository dan tag divalidasi sebelum proses build/push.
- Secret tidak dikirim dalam argumen proses. NuGet memakai handler per operasi untuk pembatasan host dan pembatalan; redirect otomatis dimatikan. OCI bearer realm harus satu host termasuk port. Registry/feed dengan authentication/resource host berbeda belum didukung; publisher menolak meneruskan credential lintas host.
- Process runner membunuh pohon proses miliknya ketika Cancel atau penulisan stdin gagal. Penghapusan workspace/log memeriksa batas direktori, ownership marker dan reparse points sebelum recursive delete. Log run aktif tidak dapat dihapus.
- `.gitignore` mengecualikan output publish; ditambah negasi yang khusus mengizinkan dua folder source publisher agar kode tidak tersembunyi dari Git pada Windows.
- Fase 4 dan 5 memakai satu orchestrator Publisher sehingga di-commit bersama. Fondasi, form, UI/harness, dokumentasi dan perbaikan Fase 1 tetap dipisahkan. DTO/entity ditempatkan dalam NuPakEntities.cs sesuai pola gabungan yang sudah ada.

## Hasil build dan pengujian

| Pemeriksaan | Hasil |
| --- | --- |
| Em.Api.slnx | 0 warning, 0 error |
| Em.Ui.Wpf.slnx | 0 warning, 0 error |
| Em.Ui.Maui.slnx | 0 warning, 0 error |
| EmPoriumHouse.Api.slnx | 0 warning, 0 error |
| EmPoriumHouse.Ui.Wpf.slnx | 0 warning, 0 error |
| Semua 12 project harness engine | 0 warning, 0 error |
| Harness module-card-qa produk | 0 warning, 0 error; assertion terang/gelap lulus |
| nupak-smoke --publisher | **170 pemeriksaan lulus**, SQL terisolasi + HTTP loopback; multi-feed, push/restore/recycle/purge, klaim manajemen/settings, publisher built-in dua sumber, duplicate dan SHA-512 |
| storage-settings-smoke --sql | **105 pemeriksaan lulus**, termasuk default NuGet dokumen lama, hak settings, Save → RequiresRestart, status disabled dan /nuget 404 setelah restart instance uji |
| publish-smoke | **35 pemeriksaan lulus**: profil/corrupt/external conflict, ekspor, DPAPI/session/host, masking/interrupted/active log, workspace/file set, .slnx/MSBuild, fingerprint, release notes, pembatalan proses serta pembacaan login Docker tanpa mengubah file/lintas host |
| Startup host engine dan produk nyata | **6 pemeriksaan lulus**: masing-masing unknown NuGet 404, /v2/ 401, /cdn/ 200 |
| publish-render | **38 PNG**, terang/gelap: Publish idle/busy/error, history, lima tab form, settings |
| nupak-manager-render | **28 PNG**, assertion loading/duplicate guard/selection stale/permissions/empty/disabled/error/retry lulus |
| module-card-qa produk | **12 PNG**, assertion card refresh independen dan toggle NuGet lulus |
| publish-container-smoke | Build lulus; run menghasilkan **PENDING Docker daemon unavailable**, sehingga tidak ada klaim sukses build/push Docker |
| git diff --check | Lulus untuk perubahan tugas |

Build memakai output terpisah di `../.artefacts/<repo>/` karena aplikasi pengguna yang terbuka mengunci sebagian output default. Proses aplikasi pengguna tidak dihentikan. Error sementara pada fixture settings disebabkan perbedaan tipe key EF dan SQL produksi; harness saja disesuaikan lalu seluruh 105 pemeriksaan diulang sampai lulus. SQL produksi tidak diubah untuk menyesuaikan fixture.

DDL dan perubahan feed dilakukan hanya dalam database uji dengan nama GUID yang dibuat/dihapus oleh harness. Startup host nyata adalah pemeriksaan baca melalui proses sementara. Tidak ada push ke feed/registry pengguna atau eksternal. Token fixture dan konfigurasi mesin tetap di luar repo.

## Verifikasi tertunda

- Dockerfile, LocalImage, Template, Compose dua service, Base → Module digest dari satu publish, App Last published, dan hash config Docker user terhadap daemon sungguhan. Harness generik telah tersedia tetapi berhenti sebelum menyiapkan fixture ketika daemon tidak tersedia; SQL fixture container dan seluruh alur Docker-nya belum terverifikasi runtime.
- Interaksi mouse/keyboard di aplikasi utama: drag-drop .nupkg, clipboard, dialog/picker, import/export, penggantian koneksi, transisi busy cepat sebelum dialog, dan alur UI terhadap server nyata. PNG offline membuktikan render beberapa keadaan, bukan semua transisi interaksi.
- Push/Verify ke feed atau registry eksternal, credential helper Docker sungguhan, serta challenge auth vendor. Pembatasan host auth/resource di atas merupakan batas implementasi v1.
- Tidak ada publisher MAUI, build remote, symbols .snupkg, JSON-encrypted atau retensi otomatis; ide lanjutan tetap tercatat di [catatan ide](../ideas/publish-nuget-container-gui.md).

Menjalankan pemeriksaan utama dari root engine:

```powershell
dotnet run --project scripts/publish-smoke/PublishSmoke.csproj --artifacts-path ../.artefacts/em-system/recheck-publish -- .
dotnet run --project scripts/nupak-smoke/NuGetSmoke.csproj --artifacts-path ../.artefacts/em-system/recheck-nupak -- . --publisher
dotnet run --project scripts/storage-settings-smoke/StorageSettingsSmoke.csproj --artifacts-path ../.artefacts/em-system/recheck-settings -- --sql
dotnet run --project scripts/publish-container-smoke/PublishContainerSmoke.csproj --artifacts-path ../.artefacts/em-system/recheck-container -- .
dotnet run --project scripts/publish-render/PublishRender.csproj --artifacts-path ../.artefacts/em-system/recheck-render -- ../.artefacts/em-system/publish-render
```

Harness SQL membaca konfigurasi lokal di folder artefak; container harness juga menerima EM_DB_CONNECTION_STRING. Akun uji membutuhkan izin membuat database uji. Setelah Docker tersedia, jalankan container harness untuk melanjutkan verifikasi yang tertunda; kode itu tidak menjalankan Compose up/deploy.

## Commit dan perubahan pengguna

Engine:

- `4aaded7` — migrasi NuPak.
- `7ca315d` — perbaikan status managed disabled, terpisah dari publisher.
- `d8d3179` — fondasi profil/log.
- `9d67ce8` — pembaca project/form dan helper target/template.
- `f5f92fa` — orchestrator NuGet/container.
- `bede346` — UI Publish/history/settings dan harness.
- `016c630` — login Docker read-only untuk Verify/latest tags OCI.
- Commit dokumentasi meliputi laporan, panduan, catatan ide dan plan yang dipindah ke executed.

EmPorium House:

- `13087d9` — menggunakan NuPak engine dan menghapus modul lama.
- `1a30247` — merapikan harness home produk.
- `fef2706` — contoh konfigurasi NuPak host produk.
- `4232e5c` — tautan panduan produk ke engine.

Berkas pengguna berikut sengaja tidak di-stage atau di-commit: engine `claude.md`, `doc/setup-container.md`, `src/backend/.env.example`, `src/backend/README.md`, `src/backend/compose.yml`, `doc/ideas/engine-helper-namespace.md`; produk `CLAUDE.md`, `src/backend/.env.example`, `src/backend/README.md`, `src/backend/compose.yml`, serta catatan ide produk yang sudah ada. Pembaruan tugas pada claude.md kedua repo dan tautan runbook pada README backend produk tetap berada di working tree bersama perubahan pengguna. Isi lama dipertahankan. Plan dan ide yang secara eksplisit menjadi sumber tugas ini disertakan pada commit dokumentasi.

Tidak ada tindakan yang ditolak oleh policy selama eksekusi ini, sehingga tidak ada skrip manual pemulihan policy. Commit lokal dibuat; tidak ada git push.
