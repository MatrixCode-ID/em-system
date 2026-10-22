# Laporan eksekusi — Folder settings CDN (`.meta`), Rename, Copy

Tanggal: 2026-10-22
Plan: [plan/executed/cdn-folder-settings.codex.md](../../plan/executed/cdn-folder-settings.codex.md)
Pelaksana: Claude Code (plan ditulis untuk Codex, dijalankan dengan aturan yang sama)
Branch: `work-bench`; tidak ada push.

## Commit

| Tahap | Commit | Isi |
| --- | --- | --- |
| 1 | `01a5566` | Kontrak `Em.Libs` (`CdnFolderSettings`, `CdnFolderSettingsFormat`, DTO, `CdnEntry.IsHidden`, action baru), `CdnStore.FolderSettings.cs`, penyaringan `.meta`, upload/rename/extract sebagai setelan, recycle tanpa `.meta`, Rename, Copy, `CdnFileProvider` (404, filter, `NoIndex`), header `X-Robots-Tag` di `EmApp`, action server. |
| 2 | `e57a5da` | Client action WPF, dialog Folder Settings, Show hidden files, Rename (`F2`), Ctrl+drag Copy dengan status task, hook `ReleaseTarget.CompleteSyncAsync` dan penulisan `.meta` oleh `CdnReleaseTarget`. |
| 3 | (commit ini) | Test, perbaikan comparer cache, guide, laporan, pemindahan plan. |

## Build dan test

- `dotnet build src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, `src/frontend/Em.Ui.Maui.slnx`: ketiganya
  **sukses, 0 error**. Warning yang muncul hanya warning lama `TestService.cs` (CS8604) di module Em.Test.
- `dotnet test tests/Em.Api.Core.Tests --filter "FullyQualifiedName~Cdn"`: **35 lulus, 0 gagal, 0 skip**
  (13 test baru di `tests/Em.Api.Core.Tests/CdnFolderSettingsTests.cs` + test CDN lama). Test nama pendek 8.3
  berjalan (volume test punya nama pendek), tidak di-skip.
- Log nonrahasia: `../.artefacts/em-system/cdn-folder-settings-build-{api,wpf,maui}.log` dan
  `cdn-folder-settings-tests.log`.
- Setelah test, comparer cache setelan diubah menjadi peka huruf besar di non-Windows (perubahan satu baris,
  tidak di-build/test ulang; ditinjau manual).
- Sesuai keputusan pengguna: tanpa tes penuh, tanpa harness smoke HTTP, tanpa uji GUI/render.

## Keputusan **(default plan)** yang dipakai

- Validasi: UTF-8 (BOM boleh), ≤ 64 KiB, key asing ditolak, ≤ 256 pola per daftar, panjang pola ≤ 255; pesan
  menyebut key/indeks (mis. `'hiddenFiles[1]' must not be empty`).
- Upload `.meta` mengembalikan entri folder tujuan.
- Cache setelan per folder di memori, kunci path folder + waktu tulis + panjang berkas; tulis/move/delete/copy
  membatalkan cache terkait.
- Release Manager menulis `.meta` (`publicListing: false`) ke folder rilis, `binaries/`, dan semua subfolder di
  dalam `binaries/`; juga ke folder rilis saat diadopsi/dipilih ulang (`GetMaxFileSizeAsync`).
- Show hidden files: **per sesi** (preferensi serupa seperti Show untagged di Container Manager tidak disimpan ke
  registry).
- Copy to…: **tidak dibuat** karena belum ada komponen pemilih folder CDN; Copy hanya lewat Ctrl+drag.

## Keputusan pelaksana untuk hal yang tidak tercakup

- Pemeriksaan nama sebenarnya di disk: setiap segmen bernama `.meta` ditolak 404 di `ResolveInside`; di Windows,
  bila ada segmen bertanda `~` dan path ada, path di-expand dengan `GetLongPathNameW` lalu diperiksa lagi.
  Varian `.meta.`, `.meta%20`, `.meta::$DATA` sudah ditolak `ValidateName` (400 di action manager, 404 di `/cdn`).
- Folder lama bernama `.meta` (bila ada) ikut disembunyikan dari listing/enumerasi dan tidak bisa dicapai.
- Archive: nama sumber `.meta` → 404. Extract: segmen folder `.meta` di zip menolak seluruh archive (400), sama
  seperti nama segmen tidak sah lainnya.
- Copy: business task global satu per server (`cdn:copy`) dengan action status/cancel/clear seperti Archive;
  validasi diulang sebelum pemindahan akhir. Tidak ada pemeriksaan ruang disk karena extract juga tidak punya.
- Cursor drag tetap ikon Move saat Ctrl ditekan (DropTarget bersama tidak diubah agar layar lain tidak
  terpengaruh); aksinya Copy.
- Logger warning setelan tidak valid dipasang di `MapCdn` (`CdnStore.Logger`).
- Teks remark `ICdnServices` dan guide yang menyatakan berkas bertitik selalu publik diperbarui.

## Penyimpangan nama action

Tidak ada untuk enam action di plan. Tambahan yang mengikuti pola Archive: `GetMeta_CdnCopyTask`,
`PostMeta_CdnCopyCancel`, `PostMeta_CdnCopyClear`.

## Header noindex

Header `X-Robots-Tag: noindex` dipasang di `OnPrepareResponse` static files untuk `CdnFileProvider.Download`
dengan `NoIndex = true`. Test memeriksa flag `NoIndex` lewat provider; pipeline HTTP tidak dijalankan karena
tidak ada fixture HTTP CDN yang murah.

## Tindakan terblokir

Tidak ada.

## Verifikasi tertunda (diperiksa pengguna)

- Visual/interaksi dialog Folder Settings (tema terang/gelap, read-only managed, peringatan file tidak valid,
  Export), toggle Show hidden files dan penandanya, Rename (`F2` dan tombol baris), Copy dengan Ctrl+drag serta
  tombol status copy.
- Respons HTTP nyata `/cdn`: 404 listing, header `X-Robots-Tag`, HEAD/Range terhadap `.meta` dan varian URL
  ter-encode lewat Kestrel.
- Sync Release Manager ke server nyata (penulisan `.meta` di seluruh pohon rilis, launcher tetap membaca
  `release.json`).

## Guide

- `doc/engine/engine-cdn-storage.md`: bagian baru "Folder settings (.meta)", CDN Manager (Folder Settings, Show
  hidden files, Rename, Copy), daftar action, pengecualian berkas bertitik.
- `doc/engine/engine-release-manager.md`: penulisan `.meta` otomatis saat Sync/adopsi.
