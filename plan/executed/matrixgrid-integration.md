# Plan: integrasi MatrixGrid ke engine Em

Tanggal: 2026-11-13. Eksekutor: Claude Code. Status: **dieksekusi 2026-11-13** (langkah 1–8 selesai). Laporan:
[doc/report/matrixgrid-integration.md](../../doc/report/matrixgrid-integration.md); penyimpangan dan keputusan agen
selama eksekusi dicatat di sana.

## Tujuan

Control dan engine dokumen dari repo saudara `..\MatrixGrid` (grid data, MatrixSheet, MatrixWriter,
editor input, print preview, serta writer PDF/XLSX/DOCX/CSV) tersedia di Em:

- **Server** dapat menyusun dan menulis dokumen (XLSX, DOCX, PDF, CSV) hanya dengan `Em.Libs`.
- **UI WPF** merender dan mengedit dokumen dengan control dari project baru `Em.Ui.Wpf.Documents`.
- Struktur siap untuk renderer lain (Avalonia, MAUI) tanpa mengubah kode netral.

MatrixGrid **tetap dikembangkan paralel sebagai master**. Kode di em-system adalah salinan hasil
terjemahan otomatis (skrip sync) dan hanya ditambah berkas adaptasi milik em-system.

## Keputusan final (pengguna, 2026-11-13)

1. **Lapisan**
   - Seluruh kode netral (`net10.0`) masuk `Em.Libs`, termasuk `SheetSession` dan `WriteSession`.
     Kedua session itu satu-satunya API untuk menyusun isi dokumen berformat (style, tabel,
     header/footer, field), sehingga server juga membutuhkannya.
   - Kode WPF masuk project baru `src/shared/Em.Ui.Wpf.Documents`.
   - Renderer lain nanti mendapat project sendiri, misalnya `Em.Ui.Avalonia.Documents`, dengan nama
     kelas control yang sama.
2. **Payung namespace adalah `Em`**, tanpa segmen `Matrix` (Em adalah produk Matrix). Namespace
   mengikuti folder.

   | Asal MatrixGrid | Tujuan (folder → namespace) |
   |---|---|
   | `Core` | `Em.Libs/Grid` → `Em.Grid` |
   | `Data` (tanpa `GeneratedOrderProvider`) | `Em.Libs/Grid/Data` → `Em.Grid.Data` |
   | `Query` | `Em.Libs/Grid/Query` → `Em.Grid.Query` |
   | `Export` | `Em.Libs/Grid/Export` → `Em.Grid.Export` |
   | `Menus` | `Em.Libs/Grid/Menus` → `Em.Grid.Menus` |
   | `Editors` (settings) | `Em.Libs/Editors` → `Em.Editors` |
   | `Editing` | `Em.Libs/Editing` → `Em.Editing` |
   | `Documents/Model`, `Layout`, `Fonts`, `Packaging`, `Formats`, `Assets` | `Em.Libs/Documents/...` → `Em.Documents.*` |
   | `Documents/PdfEngine` | `Em.Documents.Pdf` |
   | `Documents/CellsEngine` | `Em.Documents.Xlsx` (writer XLSX dan CSV) |
   | `Documents/WriterEngine` | `Em.Documents.Docx` |
   | `Sheet/Core` | `Em.Sheet.Cells` |
   | `Sheet/*` lainnya | `Em.Sheet.*` (Calc, Commands, Formulas, Functions, IO, Model, NumberFormats, View) |
   | `Write/*` | `Em.Write.*` (Editing, IO, Layout, Model). Engine **tetap `Write*`** |
   | `Wpf/Grid` | `Em.Ui.Wpf.Documents/Grid` → `Em.Ui.Wpf.Documents.Grid` |
   | `Wpf/Editors` | `Em.Ui.Wpf.Documents.Editors` |
   | `Wpf/Editing` | `Em.Ui.Wpf.Documents.Editing` |
   | `Wpf/Menus` | `Em.Ui.Wpf.Documents.Menus` |
   | `Wpf/Printing` | `Em.Ui.Wpf.Documents.Printing` |
   | `Wpf/Sheet` | `Em.Ui.Wpf.Documents.Sheet` |
   | `Wpf/Write` | `Em.Ui.Wpf.Documents.Writer` |
   | `Wpf/Themes` | `Em.Ui.Wpf.Documents.Themes` |

3. **Nama kelas control**: `GridControl` → **`MatrixGrid`** (berkas `MatrixGrid.cs`, `MatrixGrid.*.cs`),
   `MatrixSheet` tetap, `MatrixWrite` → **`MatrixWriter`** (`MatrixWriter.cs`, `MatrixWriter.*.cs`).
   Tipe engine tidak diganti nama.
4. **xmlns per kelompok control**: URI ditulis persis seperti di bawah, termasuk huruf besar-kecil.

   | URI | Prefix | Namespace CLR |
   |---|---|---|
   | `https://MatrixCode/wpf/grid` | `emg` | `Em.Ui.Wpf.Documents.Grid`, `Em.Grid`, `Em.Grid.Query`, `Em.Grid.Menus` |
   | `https://MatrixCode/wpf/editors` | `eme` | `Em.Ui.Wpf.Documents.Editors`, `Em.Editors` |
   | `https://MatrixCode/wpf/sheet` | `ems` | `Em.Ui.Wpf.Documents.Sheet` |
   | `https://MatrixCode/wpf/writer` | `emw` | `Em.Ui.Wpf.Documents.Writer` |
   | `https://MatrixCode/wpf/printing` | `emp` | `Em.Ui.Wpf.Documents.Printing` |

   `Em.Ui.Wpf.Documents.Menus`, `.Editing` dan `.Themes` dipetakan ke kelima URI.
5. **Tema**: palet MatrixGrid dipetakan ke tema aktif Em, sehingga mengganti tema Em juga mengganti
   control. Light/Dark bawaan MatrixGrid tetap menjadi default bila control dipakai tanpa host Em.
6. **Tes** dipindah dan dikonversi ke xUnit v3 / Microsoft Testing Platform, lalu dijalankan lewat
   RunBay.
7. **Demo dan benchmark tidak dipindah**, karena demo MatrixGrid bergantung pada library komersial.
8. **Arah sync MatrixGrid → em-system.** Perbaikan fitur dikerjakan di MatrixGrid lalu disalin. Di
   em-system hanya adaptasi pada berkas milik em-system (bagian 3).
9. **Mekanisme sync**: berkas peta + skrip `scripts/sync-matrixgrid.cmd` + action RunBay.
10. **NuGet**: project baru dipaketkan sebagai `EmSys.Ui.Wpf.Documents` (masuk `packages.txt`).
    **Release note tidak ditulis di plan ini**; disusun saat pengguna siap merilis. Rilis berikutnya
    wajib menyertakan release note untuk paket baru ini.
11. **Lisensi pihak ketiga**: `THIRD-PARTY-NOTICES.md` dan folder `licenses/` di root repo, ikut
    dipaketkan ke `EmSys.Libs` dan `EmSys.Ui.Wpf.Documents`. Header `Ported from ...` di berkas sumber
    dipertahankan.
12. **XML doc**: comment Inggris untuk setiap member public/protected ditulis **di repo MatrixGrid**
    (master), lalu ikut tersalin. CS1591 tidak disuppress di em-system.
13. **Eksekusi** oleh Claude Code, **commit per langkah**. em-system hanya di `work-bench`; MatrixGrid
    mengikuti aturan repo itu. Tanpa push.

### Keputusan agen (mengikuti keputusan di atas, boleh dikoreksi sebelum eksekusi)

- Resource key palet `MatrixGrid.Brush.*` dan key `Material.*` tidak diganti nama. Nama itu mengikuti
  nama control dan tidak perlu diterjemahkan oleh skrip sync.
- Pack URI `/MatrixGrid;component/Wpf/Themes/...` menjadi `/Em.Ui.Wpf.Documents;component/Themes/...`.
  `LogicalName` font Noto menjadi `Em.Documents.Assets.NotoSans-Regular.ttf`.
- `GeneratedOrderProvider` (data contoh) tidak masuk library. Skrip menyalinnya sebagai fixture tes ke
  `tests/Em.Libs.Tests/Grid/Fixtures/`, dan tes WPF memakainya lewat `Compile Include ... Link`.
- `DemoSmokeTests` (FlaUI terhadap demo) tidak dipindah. FlaUI tidak menjadi dependency em-system.
- `InternalsVisibleTo("MatrixGrid.Tests")` diterjemahkan menjadi `Em.Ui.Wpf.Documents.Tests`.
  `InternalsVisibleTo("MatrixGrid.Wpf.Benchmarks")` dibuang.
- Commit em-system berbahasa Inggris (repo publik); commit MatrixGrid berbahasa Indonesia sesuai
  aturan repo itu.

## Aturan yang wajib diikuti saat eksekusi

- Baca `claude.md` em-system versi terbaru dan `CLAUDE.md` MatrixGrid sebelum mulai.
- em-system repo publik: kode, komentar, dokumen dan teks UI berbahasa Inggris. Jangan menyebut produk
  konsumen, alamat server, atau path absolut mesin. Path ke MatrixGrid ditulis relatif
  (`..\MatrixGrid`).
- Urutan em-system: **tulis seluruh kode semua langkah dulu**, baru build penuh, tes dan review di
  akhir (langkah 8). Commit lokal per langkah tetap dilakukan. Pengecualian: langkah 1 berada di repo
  MatrixGrid, yang aturannya mewajibkan build setelah perubahan.
- Build dan tes lewat RunBay dengan output verbose (`-v:n`, `--verbosity normal`), sesuai aturan
  RunBay. GUI/visual hanya atas konfirmasi pengguna; plan ini tidak mencakup uji visual.
- Working tree em-system saat plan ditulis berisi perubahan pengguna yang belum di-commit, di luar plan
  ini. Jangan ikut meng-commit-nya: stage per path milik langkah ini saja.

## Langkah

### Langkah 1 — MatrixGrid (master): XML doc dan kesiapan sync

Repo `..\MatrixGrid`, branch kerja sesuai aturan repo itu.

1. Aktifkan `GenerateDocumentationFile` di `src/MatrixGrid/MatrixGrid.csproj`. Dengan
   `TreatWarningsAsErrors`, CS1591 menjadi error dan memaksa kelengkapan doc.
2. Tulis XML comment Inggris untuk semua member public/protected (±1.150 deklarasi), mengikuti perilaku
   kode sebenarnya dan tanpa sekadar mengulang nama member.
3. Tambahkan bagian di `CLAUDE.md` MatrixGrid: kode ini disalin ke em-system lewat peta dan skrip sync.
   Mengubah nama atau folder publik berarti memperbarui peta di em-system
   (`scripts/sync-matrixgrid/map.json`). Doc public tetap wajib.
4. Build lewat action RunBay `Build` milik app MatrixGrid, lalu commit.

### Langkah 2 — em-system: peta dan skrip sync

1. `scripts/sync-matrixgrid/map.json` berisi:
   - Pemetaan folder sumber → folder target (bagian Keputusan 2, juga untuk folder tes).
   - Pemetaan namespace **nama lengkap** → namespace baru. Jangan mengganti per prefix, agar
     resource key `MatrixGrid.Brush.*` dan nama kelas `MatrixGrid` tidak tersentuh.
   - Rename tipe dan berkas: `GridControl` → `MatrixGrid`, `MatrixWrite` → `MatrixWriter`.
   - Rename string: pack URI, `LogicalName`, `InternalsVisibleTo`, URI xmlns lama.
   - Daftar pengecualian: demo, benchmark, `DemoSmokeTests`, `NamespaceMappings.cs`, csproj.
   - Daftar **berkas milik em-system** yang tidak boleh ditimpa atau dihapus skrip.
2. `scripts/sync-matrixgrid.cmd` (CRLF, cek `pwsh`, `pause` di akhir) dan
   `scripts/sync-matrixgrid/sync-matrixgrid.ps1`:
   - Parameter `-Source` (bawaan `..\MatrixGrid` dari root repo) dan `-WhatIf` (laporan tanpa
     menulis).
   - Menyalin berkas sesuai peta. Penggantian berbasis token identifier: namespace dulu, lalu tipe,
     lalu string. Berkas target diganti nama sesuai peta.
   - Konversi tes xUnit v2 → v3: `Xunit.Abstractions` dihapus; `IAsyncLifetime` mengembalikan
     `ValueTask`; atribut dan assertion lain tetap.
   - Menghapus berkas target yang sumbernya sudah tidak ada, **hanya** di folder hasil peta dan bukan
     berkas milik em-system.
   - Mencatat commit sumber dan waktu sync di `scripts/sync-matrixgrid/last-sync.json`, lalu mencetak
     ringkasan (berkas baru, berubah, dihapus, dilewati) ke console.
3. Daftarkan action RunBay di app `em-system`: **Sync MatrixGrid** (menjalankan skrip, output penuh
   di console). Commit.

### Langkah 3 — Project dan berkas milik em-system

1. `src/shared/Em.Ui.Wpf.Documents/Em.Ui.Wpf.Documents.csproj`:
   - `net10.0-windows`, `UseWPF`, `RootNamespace` `Em.Ui.Wpf.Documents`,
     `GenerateDocumentationFile`, `Nullable`, `ImplicitUsings`.
   - Reference ke `Em.Libs`, `Em.Ui.Core` dan `Em.Ui.Wpf.Core`.
   - `Resource` untuk font Roboto dan `Page` untuk `Themes/Generic.xaml`.
   - Pack `licenses/` dan `THIRD-PARTY-NOTICES.md`.
   - `README.md` project berisi ringkasan pemakaian.
2. `Em.Libs.csproj`:
   - `EmbeddedResource` Noto Sans dengan `LogicalName` baru.
   - Pack `licenses/` dan `THIRD-PARTY-NOTICES.md`.
   - Tag paket `documents;spreadsheet;pdf;xlsx;docx`.
   - Tidak menambah dependency NuGet; kode MatrixGrid hanya memakai BCL.
3. `Em.Ui.Wpf.Documents/Properties/XmlnsDefinitions.cs`: kelima URI dan prefix dari Keputusan 4.
   Namespace dari `Em.Libs` dipetakan dengan `XmlnsDefinition(..., AssemblyName = "Em.Libs")`. Bila
   compiler XAML tidak menerima pemetaan lintas assembly, kolom/filter memakai prefix
   `clr-namespace:Em.Grid;assembly=Em.Libs`. Catat hasilnya di panduan.
4. Adaptasi tema:
   - Kelas `DocumentsThemeApplier : IThemeApplier` menulis key `MatrixGrid.Brush.*` ke
     `Application.Resources` dari token `ThemeBase`. Token yang tidak dimiliki Em (misalnya tertiary,
     error container, scrim, inverse) diambil dari palet Light/Dark MatrixGrid sesuai
     `ThemeBase.Variant`.
   - Registrasi lewat extension `EmAppBuilder.UseDocuments()` (memanggil `AddThemeApplier`).
   - Periksa bahwa tidak ada dictionary level elemen yang mendeklarasikan key palet dan menutupi nilai
     level aplikasi. Kandidat yang dicek: penggabungan `Light.xaml` di `GridControl.Surface.cs`
     (MatrixGrid) dan mode `GridTheme`. Bila ada, perbaikan dilakukan **di MatrixGrid** dengan hook
     netral (misalnya mode "Host" yang tidak memuat palet sendiri), lalu di-sync.
   - Tabel pemetaan token dicatat di panduan.
5. Root repo: `THIRD-PARTY-NOTICES.md` (isi dari `ATTRIBUTION.md` dan `NOTICE` MatrixGrid) dan
   `licenses/` (salinan seluruh berkas lisensi MatrixGrid).
6. Tes:
   - `tests/Em.Ui.Wpf.Documents.Tests` (baru, `net10.0-windows`, `UseWPF`) dimasukkan ke
     `src/frontend/Em.Ui.Wpf.slnx`.
   - Tes netral masuk `tests/Em.Libs.Tests` (sudah ada di `src/backend/Em.Api.slnx`).
   - Tes arsitektur ditulis ulang sebagai berkas milik em-system: `Em.Libs` tidak mereferensikan
     WPF/`System.Windows`, dan `Em.Ui.Wpf.Documents` tidak mereferensikan library komersial.
7. Tambahkan `Em.Ui.Wpf.Documents` ke `src/frontend/Em.Ui.Wpf.slnx` dan
   `scripts/pack-nuget/packages.txt`.
8. Commit.

### Langkah 4 — Sync pertama

1. Jalankan action **Sync MatrixGrid**. Kode dan tes tersalin ke lokasi Keputusan 2 dan bagian tes.
2. Perbaiki **peta atau skrip** bila hasil terjemahan salah. Jangan menyunting hasil salinan secara
   manual, karena akan tertimpa di sync berikutnya. Kekurangan yang memang harus diperbaiki di sumber
   dikerjakan di MatrixGrid (commit di sana), lalu sync diulang.
3. Commit hasil sync dengan menyebut commit sumber MatrixGrid.

### Langkah 5 — Integrasi host Em

1. Host `Em.Ui.Wpf` memanggil `builder.UseDocuments()`, sehingga tema terpasang tanpa memakai control.
   Tidak ada navigasi atau menu baru, karena demo tidak dipindah.
2. Commit.

### Langkah 6 — Dokumentasi dan aturan engine

1. `doc/engine/engine-documents.md`:
   - Lapisan dan peta namespace.
   - Contoh server: menyusun XLSX dengan `SheetSession` + `SheetFiles`, serta DOCX/PDF dengan
     `WriteSession` + `WriteFiles`/`WritePdf`.
   - Contoh WPF: xmlns, `MatrixGrid` dengan `ItemsSource`/`IGridDataProvider`, `MatrixSheet`,
     `MatrixWriter`, print preview.
   - `UseDocuments()` dan pemetaan tema.
   - Batas renderer (Avalonia/MAUI nanti).
   - Alur sync dan berkas milik em-system.
   - Keterbatasan yang diwarisi (tautan ke dokumen MatrixGrid, tidak disalin).
2. `doc/engine/build.md`: paket baru di bagian packages, serta catatan XML doc untuk kode hasil sync.
3. `claude.md` em-system: bagian baru **"Kontrol dokumen dari MatrixGrid (keputusan pengguna
   2026-11-13)"**, berisi ringkasan Keputusan 1–12 dan larangan menyunting berkas hasil sync; perubahan
   lewat MatrixGrid + sync. Pertahankan isi lain.
4. Commit.

### Langkah 7 — RunBay

Tambahkan atau perbarui action di app `em-system` (cek dulu dengan `get_app` agar tidak duplikat):

- **Build Documents**: `dotnet build` `Em.Api.slnx` dan `Em.Ui.Wpf.slnx`, `-v:n`.
- **Test Documents**: `Em.Libs.Tests` dan `Em.Ui.Wpf.Documents.Tests`, `--verbosity normal`.

Setiap skrip mencetak baris penanda sebelum langkah yang lama diam. Commit konfigurasi bila tercatat
di repo.

### Langkah 8 — Build, tes, review, laporan

1. Jalankan **Build Documents**. Bangun juga `src/frontend/Em.Ui.Maui.slnx`, karena `Em.Libs` berubah.
   Bila workload `maui-android` tidak ada, catat sebagai tidak terverifikasi.
2. Jalankan **Test Documents** dan tes engine yang sudah ada di kedua solution, untuk memastikan
   `Em.Libs` tidak merusak tes lama.
3. Jalankan `scripts/pack-nuget.cmd` secara lokal (tanpa upload) untuk memastikan paket baru dan
   lisensi ikut. Isi paket diperiksa.
4. Review: ukuran `Em.Libs` (font Noto ±570 KB ikut ke semua konsumen, termasuk MAUI), trimming, dan
   kesesuaian aturan publik (tanpa referensi produk konsumen).
5. Laporan `doc/report/matrixgrid-integration.md`: run id RunBay, hasil, hal yang belum diuji (GUI,
   MAUI, pemetaan xmlns lintas assembly bila gagal), dan saran lanjutan ke `doc/ideas/`.
6. Pindahkan plan ke `plan/executed/`, lalu commit.

## Di luar cakupan

- Renderer Avalonia/MAUI, demo atau halaman uji di modul `Em.Test`, benchmark.
- Uji GUI/visual (butuh konfirmasi terpisah).
- Release note NuGet dan rilis.
- Pemakaian control oleh aplikasi konsumen.
- Push ke remote mana pun.
