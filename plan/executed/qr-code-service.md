# Plan: service QR code di engine

Tanggal: 2026-11-01. Eksekutor: Claude Code. Ide asal disimpan di repo host (lokasi sementara
ide engine, keputusan pengguna 2026-10-19).

## Tujuan

Engine menyediakan pembuat QR code (teks → PNG) yang tersedia di backend, WPF dan MAUI tanpa
panggilan apa pun di kode host, plus helper `ImageSource` dan control `QrCodeView` di kedua UI core.

## Keputusan final (pengguna, 2026-11-01)

1. **Paket**: QRCoder **1.8.1** (MIT), renderer `PngByteQRCode`. Tidak menulis encoder sendiri.
2. **Penempatan**: kontrak dan implementasi di **`Em.Libs`** (fondasi, bisa dipakai backend);
   QRCoder hanya direferensikan `Em.Libs`. `Em.Ui.Core` tidak membuat kelas QR sendiri: UI memakai
   kontrak dari `Em.Libs` yang sudah direferensikan lewat `Em.Ui.Core`.
3. **Registrasi**: `IQrCodeService` → `QrCodeService` (singleton) didaftarkan engine di
   `Em.Api.Core` (DI server, **tanpa** action/endpoint HTTP), `Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core`
   (`EmApp.InitInternalServices`). Host tidak perlu memanggil apa pun.
4. **Control API** `QrCodeView` (WPF & MAUI): `Text` (string), `Size` (double, sisi persegi,
   default 160), `ErrorCorrection` (default M). Quiet zone selalu on. Teks kosong/null = tidak
   menggambar QR (tidak melempar).
5. **Tema**: selalu modul gelap di atas tile terang di kedua tema. Warna dari token tema, tanpa
   hard-code: dari pasangan latar/teks tema, yang lebih terang menjadi tile dan yang lebih gelap
   menjadi modul (tema terang: latar = tile; tema gelap: teks terang = tile). Tile bersudut membulat,
   quiet zone tetap ada; gambar dibuat ulang saat tema berganti.
6. **Default service**: ECC **M**, **10 px** per modul, quiet zone **on** (4 modul), warna hitam
   di atas putih. Control me-render dengan pixels per module yang cukup untuk `Size` dan skala layar.
7. **Validasi**: `text` null → `ArgumentNullException`; kosong → `ArgumentException`; melebihi
   kapasitas QR untuk ECC yang dipilih → `ArgumentException` dengan pesan kapasitas (membungkus
   exception QRCoder). `PixelsPerModule` < 1 atau > 100 → `ArgumentOutOfRangeException`.
8. **Tes**: `tests/Em.Libs.Tests` + **ZXing.Net** (core, hanya di project test) dan decoder PNG kecil
   di test. Helper WPF dites di `tests/Em.Ui.Wpf.Core.Tests`. MAUI tanpa project test (build saja).
9. **Release note**: versi baru **0.1.0-alpha.20** untuk kelima paket (alpha.19 sudah ditag); hanya di
   `work-bench`, tidak memicu rilis. `EmSys.Ui.Core` mencatat bahwa kontrak tersedia lewat Em.Libs.
10. Ide tetap di repo host; plan ini di em-system. Commit di `work-bench`, tanpa push.

## Nama dan lokasi

`src/shared/Em.Libs/Shared/` (namespace `Em.Shared`):
- `QrErrorCorrection.cs` — enum `L, M, Q, H` (milik engine; tipe QRCoder tidak bocor ke kontrak).
- `QrColor.cs` — `readonly record struct QrColor(byte R, byte G, byte B)` + `Black`, `White`.
- `QrCodeOptions.cs` — record `ErrorCorrection`, `PixelsPerModule`, `QuietZone`, `Dark`, `Light`;
  `Default`.
- `IQrCodeService.cs` — `byte[] CreatePng(string text, QrCodeOptions? options = null)`.
- `QrCodeService.cs` — implementasi QRCoder (UTF-8 hanya bila teks di luar ISO-8859-1).

WPF `src/shared/Em.Ui.Wpf.Core/`:
- `Shared/QrCodeImage.cs` (namespace `Em.Ui.Wpf.Shared`) — `BitmapSource Create(IQrCodeService, text, options)`
  (frozen) + overload memakai service dari `EmApp`.
- `Controls/QrCodeView.cs` (namespace `Em.Ui.Wpf.Controls`) — lookless, style default di
  `Themes/Generic.xaml` (Border tile + Image), `Background`/`Foreground` dari token
  `themeWindowBackgroundBrush`/`themeWindowForegroundBrush`.

MAUI `src/shared/Em.Ui.Maui.Core/`:
- `Shared/QrCodeImage.cs` — `ImageSource Create(...)` (`ImageSource.FromStream`).
- `Controls/QrCodeView.cs` — `ContentView` (Border + Image), warna `surface*`/`onSurface*` dari
  `Styles.Palette`, mengikuti `RequestedThemeChanged`.

## Langkah

1. Kode Em.Libs (paket QRCoder, lima berkas) + registrasi Em.Api.Core.
2. Helper + control WPF, registrasi WPF.
3. Helper + control MAUI, registrasi MAUI.
4. Tes Em.Libs.Tests (decode numeric/alphanumeric/UTF-8, opsi, validasi) dan Em.Ui.Wpf.Core.Tests.
5. Release note alpha.20, guide `doc/engine/engine-qr-code.md` + tautan README.
6. Build/tes sekali di akhir lewat RunBay (app "EmCode ERP Engine"), perbaiki, commit `work-bench`.

## Tertunda

- Uji visual WPF dan emulator/perangkat Android (menunggu konfirmasi pengguna), termasuk scan nyata
  di tema gelap.

## Laporan eksekusi (2026-11-01)

- Semua langkah 1-6 selesai sesuai keputusan final. Penyimpangan kecil: control juga punya properti
  baca-saja `Error` (alasan teks gagal digambar) di WPF dan MAUI, menggantikan rencana tooltip agar
  kedua core tetap cermin.
- RunBay app "EmCode ERP Engine", action baru `test-qr-code` (belum ada action yang membangun
  Em.Libs.Tests + MAUI core + tes QR). Run `20261101-080148-test-qr-code-88a9` gagal (CS0103
  `DpiChanged` di control WPF, diganti override `OnDpiChanged`); run `20261101-080232-test-qr-code-bfe8`
  lulus: build backend, MAUI core (Android) dan WPF tanpa error; Em.Libs.Tests 44/44,
  QrCodeViewTests 6/6; release note alpha.20 lima paket ada dengan Summary.
- `Directory.Build.props` tetap `0.1.0-alpha.19`; versi dinaikkan saat rilis.
- Tertunda: uji visual WPF, emulator/perangkat Android dan scan nyata di tema gelap (menunggu
  konfirmasi pengguna).
