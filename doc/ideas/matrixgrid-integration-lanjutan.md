# Lanjutan integrasi MatrixGrid

Tanggal: 2026-11-13
Status: diskusi

Temuan dan saran dari eksekusi [plan integrasi MatrixGrid](../../plan/executed/matrixgrid-integration.md)
([laporan](../report/matrixgrid-integration.md)). Belum ada keputusan; bukan perintah kerja.

## 1. Namespace `Em.Grid` menutupi `Grid` WPF/MAUI

Kode di dalam namespace `Em.*` yang menyebut `Grid` (WPF atau MAUI) gagal kompilasi karena `Em.Grid` ditemukan lebih
dulu daripada `using` di atas berkas. Empat berkas engine sudah diberi alias. Konsumen yang memakai namespace `Em.*`
(misalnya host turunan) akan terkena hal yang sama saat memperbarui paket.

Pilihan:
- Biarkan, dan tulis di release note sebagai perubahan yang bisa memutus kompilasi, dengan contoh alias.
- Tambah analyzer/aturan konvensi yang melarang namespace akar baru dengan nama tipe umum.
- Tinjau ulang nama `Em.Grid` (mis. `Em.DataGrid`) sebelum paket pertama dirilis. Ini mengubah keputusan pengguna.

Pertanyaan: apakah ada repo turunan yang kodenya berada di namespace `Em.*`?

## 2. Tipe engine di XAML tanpa URI

`GridColumn`, `GridQuery`, `EditorSettings` dan entri menu harus memakai `clr-namespace:...;assembly=Em.Libs` karena
xmlns lintas assembly tidak didukung. Pilihan: tipe pembungkus XAML di `Em.Ui.Wpf.Documents` (mis. `emg:Column`)
yang diubah menjadi `GridColumn`, atau menerima `clr-namespace` sebagai pola resmi.

## 3. Font Noto ganda dan ukuran `EmSys.Libs`

Noto Sans (±570 KB) ada di `Em.Libs` (untuk layout/PDF) dan sekali lagi di `Em.Ui.Wpf.Documents` (pack URI WPF).
Pilihan: `WpfFontProvider` di MatrixGrid membaca font dari resource `Em.Libs`/`TrueTypeFace` alih-alih pack URI; atau
font dipisah ke paket opsional agar konsumen MAUI/server yang tidak membuat PDF tidak membawanya.

## 4. Trimming dan AOT

`Em.Libs` belum ditandai `IsTrimmable` dan belum dianalisis. MatrixGrid sudah punya audit consumer trimmed/AOT;
jalankan padanannya untuk build em-system sebelum `Em.Libs` dipakai di MAUI release yang di-trim.

## 5. `pack-nuget` dan build server

Tanpa `--disable-build-servers`, run RunBay untuk `dotnet build/pack` tertahan sekitar 10 menit setelah selesai.
Usul: tambahkan opsi itu di `scripts/pack-nuget/pack-nuget.ps1` (dan action RunBay lama yang belum memakainya).

## 6. Pelanggaran aturan repo publik yang sudah ada

Tooltip contoh di `src/shared/Em.Ui.Wpf.Core/Navigations/Publish/PublishProfileDialog.xaml.cs` (baris 40–41) memuat
nama image produk konsumen. Usul: ganti dengan contoh netral.

## 7. Analyzer xUnit pada tes hasil sync

Tes hasil sync memunculkan xUnit1051 (CancellationToken) setelah dikonversi ke v3. Perbaikan di MatrixGrid tidak cocok
selama MatrixGrid memakai xUnit v2. Pilihan: migrasi tes MatrixGrid ke v3, atau terima warning.

## 8. Renderer lain dan halaman uji

Renderer Avalonia/MAUI, serta halaman contoh di modul `Em.Test` (pengganti demo yang tidak dipindah) untuk uji GUI
tema dan control di host Em.
