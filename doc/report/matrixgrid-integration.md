# Laporan eksekusi: integrasi MatrixGrid ke engine Em

Tanggal: 2026-11-13. Eksekutor: Claude Code (eksekusi terjadwal otomatis, tanpa pengguna hadir). Plan:
[plan/executed/matrixgrid-integration.md](../../plan/executed/matrixgrid-integration.md). Panduan:
[doc/engine/engine-documents.md](../engine/engine-documents.md).

## Ringkasan

Langkah 1–8 selesai. Kode netral MatrixGrid ada di `Em.Libs`, control WPF di project baru `Em.Ui.Wpf.Documents`,
tes dikonversi ke xUnit v3, sync berjalan lewat `scripts/sync-matrixgrid.cmd` dan action RunBay **Sync MatrixGrid**.
Build ketiga solution lulus, tes engine kedua solution lulus, paket lokal memuat lisensi. Tidak ada push.
Uji GUI/visual tidak dijalankan (di luar cakupan plan).

## Commit

### MatrixGrid (`..\MatrixGrid`, branch `main`)

| Commit | Isi |
| --- | --- |
| `c00ec16` | Langkah 1: `GenerateDocumentationFile`, XML doc Inggris untuk 1.390 member public/protected, bagian sync di `CLAUDE.md`, catatan `docs/Progress.md` |
| `20d4a0b` | Hook netral `GridTheme.UseHostPalette` (langkah 3, adaptasi tema) |
| `66d8676` | Kualifikasi penuh `global::MatrixGrid.Data.*` di popup filter kolom (ditemukan saat build langkah 8) |
| `ec4583c` | Mengembalikan nama aksesibilitas "Select choice" pada tombol Select `LookupEditor` (regresi dari `b24ef28`; ditemukan oleh tes langkah 8) |

### em-system (branch `work-bench`)

| Commit | Langkah | Isi |
| --- | --- | --- |
| `7462e4b` | 2 | Peta dan skrip sync |
| `b30913e` | 3 | Project `Em.Ui.Wpf.Documents`, xmlns, `DocumentsThemeApplier` + `UseDocuments()`, `Em.Libs` (font, IVT, paket lisensi), `THIRD-PARTY-NOTICES.md`, project tes, slnx, `packages.txt` |
| `605c8b7` | 4 | Sync pertama dari MatrixGrid `20d4a0b` (187 berkas) |
| `e2f0383` | 5 | Host `Em.Ui.Wpf` memanggil `UseDocuments()` |
| `065b139` | 6 | `engine-documents.md`, `build.md`, `README.md` engine, bagian baru `claude.md` |
| — | 7 | Action RunBay tersimpan di data folder RunBay (di luar repo em-system), jadi tidak ada commit |
| `fa818e5` | 8 | Perbaikan build: alias `Grid` WPF, sync `66d8676`, skrip sync (hash, line ending, BOM) |
| `984011c` | 8 | Verifikasi xmlns (pemetaan `AssemblyName` dihapus), alias `Grid` MAUI, sync `ec4583c`, panduan |
| (commit penutup) | 8 | Laporan ini, catatan ide, plan dipindah ke `plan/executed/` |

`last-sync.json` mencatat sumber terakhir: MatrixGrid `ec4583c`.

Perubahan pengguna yang sudah ada di working tree em-system sebelum eksekusi (`claude.md` bagian atas,
`doc/engine/engine-debug-mode.md`, beberapa berkas `Em.Ui.Wpf.Core`, `DebugConnectionClaimsTests.cs`) tidak ikut
di-commit. Untuk `claude.md`, hanya bagian baru dari plan yang di-stage (lewat index), isi milik pengguna tetap
belum di-commit.

## Run RunBay

| Run id | Action | Hasil |
| --- | --- | --- |
| `20261113-195641-build-43af` | MatrixGrid Build | Gagal sesuai rencana: 1.390 member tanpa doc setelah CS1591 aktif |
| `20261113-200753-build-8f58` | MatrixGrid Build | Lulus, 0 error (langkah 1) |
| `20261113-201443-build-a59d` | MatrixGrid Build | Lulus (hook palet) |
| `20261113-201755-sync-matrixgrid-2c86` | Sync MatrixGrid | 186 berkas baru |
| `20261113-202202-build-documents-892a` | Build Documents | Gagal: `Grid` tertutup namespace `Em.Grid` |
| `20261113-203238-build-documents-25bd` | Build Documents | Gagal: `Data.RowRange` parsial, `Grid` di tes engine |
| `20261113-204327-build-1ec7` | MatrixGrid Build | Lulus (`66d8676`) |
| `20261113-204343-sync-matrixgrid-d930` | Sync MatrixGrid | Gagal: bug `SequenceEqual` di skrip |
| `20261113-204359-sync-matrixgrid-51ca` | Sync MatrixGrid | Lulus, 1 berkas berubah |
| `20261113-204404-build-documents-4855` | Build Documents | **Lulus**, API + WPF, 0 error |
| `20261113-205433-test-documents-c784` | Test Documents | Em.Libs.Tests 120/120; WPF 20/22 (xmlns lintas assembly, tombol Select) |
| `20261113-210549-build-d27c` | MatrixGrid Build | Lulus (`ec4583c`) |
| `20261113-210639-sync-matrixgrid-bd33` | Sync MatrixGrid | Lulus |
| `20261113-210643-test-documents-a2a9` | Test Documents | Gagal kompilasi: MC3074 untuk `emg:GridColumn` |
| `20261113-210708-test-documents-8bec` | Test Documents | **Lulus**: Em.Libs.Tests 120/120, Em.Ui.Wpf.Documents.Tests 23/23 |
| `20261113-210738-test-engine-solutions-140a` | Test engine solutions (API + WPF) | **Lulus**: API 564 (525 lulus, 39 dilewati), WPF 516/516 |
| `20261113-210844-build-maui-solution-a18f` | Build MAUI solution | Gagal: `Grid` MAUI tertutup `Em.Grid` |
| `20261113-210900-build-maui-solution-249d` | Build MAUI solution | **Lulus**, 0 error (workload `maui-android` terpasang) |
| `20261113-210932-pack-documents-local-c981` | Pack NuGet locally | **Lulus**, paket di `dist/nuget-pack` (diabaikan Git), tanpa upload |

Action baru di app RunBay `em-system`: `sync-matrixgrid`, `build-documents`, `test-documents`,
`test-engine-solutions`, `build-maui-solution`, `pack-documents-local`. Dua action terakhir dan
`test-engine-solutions` ditambahkan di luar daftar plan untuk langkah 8.

## Hasil build dan tes

- Build `Em.Api.slnx`, `Em.Ui.Wpf.slnx`, `Em.Ui.Maui.slnx`: 0 error. Warning yang ada berasal dari kode lama
  (CS1591 di berkas Release `Em.Ui.Wpf.Core`, xUnit1051 di tes lama, warning MAUI).
- Tes netral di `Em.Libs.Tests` (120, termasuk tes lama Em.Libs) dan tes WPF baru (23) lulus. Tes engine lain di kedua
  solution tetap lulus; 39 tes API dilewati karena syarat database lokal masing-masing tes (perilaku lama).
- Isi paket: `EmSys.Libs` 912 KB (Em.Libs.dll 1,9 MB, XML doc 0,96 MB) dan `EmSys.Ui.Wpf.Documents` 1.002 KB
  (dll 1,9 MB). Keduanya memuat `THIRD-PARTY-NOTICES.md` dan 11 berkas `licenses/`.

## Keputusan agen selama eksekusi

1. **Konflik nama `Grid`.** Namespace `Em.Grid` (keputusan 2) membuat kode di dalam namespace `Em.*` yang memakai
   `Grid` WPF/MAUI gagal kompilasi (CS0118). Diperbaiki dengan alias `using Grid = ...;` di dalam namespace pada
   empat berkas engine: `PublishProfileDialog.xaml.cs`, `ApprovalManagerTests.cs`, `WorkspaceTab.cs`,
   `MaterialField.cs`. Ini perubahan engine di luar daftar berkas plan, tetapi akibat langsung keputusan namespace.
   Konsumen yang kodenya juga di bawah namespace `Em.*` akan terkena hal yang sama (lihat ide lanjutan).
2. **xmlns lintas assembly gagal.** Compiler XAML (MC3074) dan `XamlReader` runtime sama-sama mengabaikan
   `XmlnsDefinition(..., AssemblyName = "Em.Libs")`, dan atribut itu tidak tersedia tanpa WPF sehingga `Em.Libs` tidak
   bisa mendeklarasikannya sendiri. Sesuai cadangan di plan, pemetaan `Em.Grid`, `Em.Grid.Query`, `Em.Grid.Menus` dan
   `Em.Editors` dihapus dari URI; tipe engine memakai `clr-namespace:Em.Grid;assembly=Em.Libs`. Dicatat di panduan dan
   diuji dengan `XamlNamespaceSample.xaml` terkompilasi.
3. **Palet host.** Popup, menu dan dialog MatrixGrid memanggil `GridTheme.SetMode`, yang menggabungkan palet
   Light/Dark di tingkat elemen dan menutupi palet aplikasi. Hook netral di MatrixGrid berupa properti statis
   `GridTheme.UseHostPalette` (bukan nilai enum "Host" seperti contoh di plan) karena berlaku untuk semua elemen tanpa
   perlu mengubah pemanggil. `DocumentsThemeApplier` menyalakannya.
4. **Token Em yang dipakai.** Em punya `ErrorContainer` dan `Scrim`, jadi keduanya diambil dari tema Em, bukan dari
   palet cadangan seperti contoh di plan. Layer state (hover, selected, disabled) diturunkan dari warna Em dengan
   opasitas MatrixGrid. Tabel lengkap ada di panduan.
5. **Font Noto di WPF.** `WpfFontProvider` memuat Noto lewat pack URI ke assembly WPF, jadi `Em.Ui.Wpf.Documents`
   memuat salinan kedua sebagai `Resource` (link ke berkas di `Em.Libs`), dengan rewrite string
   `/MatrixGrid;component/Documents/`.
6. **InternalsVisibleTo.** Kode MatrixGrid satu assembly memakai member internal lintas lapisan, jadi `Em.Libs` memberi
   IVT ke `Em.Ui.Wpf.Documents`, `Em.Ui.Wpf.Documents.Tests` dan `Em.Libs.Tests`
   (`src/shared/Em.Libs/Properties/DocumentsAccess.cs`, berkas milik em-system).
7. **Peta tes.** Tes WPF disalin ke subfolder `tests/Em.Ui.Wpf.Documents.Tests/Wpf` (namespace
   `Em.Ui.Wpf.Documents.Tests.Wpf`) agar berkas milik em-system di root project tidak berada di folder hasil peta.
   `EditorContractTests` masuk `tests/Em.Libs.Tests/Editing`. `licenses/` juga dipetakan sehingga ikut tersinkron.
8. **Skrip sync.** Hanya berkas yang dilacak Git di MatrixGrid yang disalin; perbandingan berbasis hash dan teks tanpa
   beda CRLF/LF supaya normalisasi Git tidak menimbulkan perubahan palsu.
9. **Action RunBay memakai `--disable-build-servers`.** Tanpa opsi itu run tertahan sekitar 10 menit setelah build
   selesai (server kompilator menahan output). `scripts/pack-nuget` sendiri belum memakainya, sehingga action pack
   masih sekitar 10 menit.
10. **Dua bug master ditemukan dan diperbaiki di MatrixGrid**: referensi parsial `Data.*` dan nama aksesibilitas tombol
    Select. Tes MatrixGrid sendiri tidak dijalankan di repo itu (aturan repo: tes hanya atas permintaan); verifikasinya
    lewat salinan tes di em-system.

## Review

- **Ukuran `Em.Libs`.** Dll naik menjadi 1,9 MB, termasuk Noto Sans sekitar 570 KB yang ikut ke semua konsumen
  `EmSys.Libs`, termasuk aplikasi MAUI dan server. Paket WPF memuat salinan kedua font.
- **Trimming.** `Em.Libs` belum ditandai `IsTrimmable` dan belum dianalisis dengan trimming/AOT di em-system. Bagian
  netral memakai `System.Text.Json` source generation untuk layout grid; MatrixGrid punya action audit trimming
  sendiri, tetapi hasilnya belum diverifikasi untuk build em-system.
- **Aturan repo publik.** Berkas baru dan hasil sync bersih dari nama produk konsumen, path absolut mesin dan alamat
  server. Ditemukan pelanggaran lama di luar cakupan: contoh nama image produk konsumen di tooltip
  `PublishProfileDialog.xaml.cs` (baris 40–41); dicatat sebagai ide, tidak diubah.
- **Release note.** Sesuai plan tidak ditulis. Rilis NuGet berikutnya wajib menambah release note
  `EmSys.Ui.Wpf.Documents` dan menyebut perubahan namespace `Em.Grid` dkk. sebagai perubahan yang bisa memutus
  kompilasi kode konsumen di bawah namespace `Em.*`.

## Belum terverifikasi

- **GUI/visual**: control di dalam host Em, pergantian tema Em terhadap grid, popup, menu, dialog dan print preview
  (light/dark) belum dilihat. Tes otomatis hanya memeriksa pemetaan palet (`DocumentsThemeApplierTests`).
- **MAUI**: hanya build; tidak ada uji di perangkat/emulator. MAUI belum punya renderer dokumen.
- **xmlns**: compiler dan runtime sudah diuji dalam project tes; belum dicoba dari repo konsumen yang memakai paket
  NuGet (bukan ProjectReference).
- **Tema**: `UseDocuments()` di host `Em.Ui.Wpf` belum dijalankan secara interaktif.
- **Upload/rilis paket**: tidak dilakukan (di luar cakupan).
- **Tes integrasi API** yang dilewati (39) tidak dijalankan ulang dengan database.

## Ide lanjutan

Dicatat di [doc/ideas/matrixgrid-integration-lanjutan.md](../ideas/matrixgrid-integration-lanjutan.md).
