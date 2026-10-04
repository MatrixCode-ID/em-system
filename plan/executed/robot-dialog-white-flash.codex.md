# Perbaikan kilatan putih sebelum dialog New Robot

- Keluhan: panel Robots memutih sebentar saat tombol New robot ditekan pada tema gelap.
- Telusuri kondisi busy saat mengambil owner accounts dan template ListBox.
- Pertahankan latar transparan daftar pada kondisi enabled/disabled, scrolling, dan pemblokiran command saat busy.
- Verifikasi build WPF dan render daftar enabled/disabled dalam tema terang/gelap.
- Catat hasil serta batas verifikasi, lalu pindahkan plan ke `plan/executed/`.

## Hasil eksekusi 2026-10-03

- Penyebab terkonfirmasi: `NewRobotCommand` mengambil owner accounts lewat
  `RunBusyAsync`, sehingga ListBox menjadi disabled. Template ListBox bawaan
  WPF mengecat background disabled dengan warna sistem putih, walaupun kontrol
  memiliki `Background="Transparent"`.
- `RobotManager.xaml` kini menggunakan template ListBox dengan Border yang
  mengikat background/padding/border kontrol dan ScrollViewer/ItemsPresenter.
  Tidak ada trigger warna sistem saat disabled. `IsEnabled`, binding selection,
  item template, scrolling, dan virtualisasi tetap dipertahankan.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore`: lulus, 0 warning/error.
- Harness WPF sementara di `%TEMP%/em-robot-flash-qa` merender RobotManager pada
  tema LightTheme/DarkTheme, dengan `IsBusy=false/true`. Pixel panel daftar
  enabled/disabled identik setelah perbaikan: BGRA terang `247,243,244,255`,
  gelap `30,26,23,255`. Mengembalikan template native melalui `ClearValue`
  menghasilkan BGRA `255,255,255,255` ketika disabled di kedua tema, sehingga
  masalah asli berhasil direproduksi. Daftar tetap disabled ketika busy.
- Render tema gelap kondisi busy diperiksa secara visual; tidak ada panel putih.
- `git diff --check`: lulus (hanya pemberitahuan konversi line ending claude.md
  yang sudah berubah sebelum task ini).
- Klik New Robot terhadap server nyata belum diuji; verifikasi di atas menguji
  kondisi visual WPF yang dipicu command tersebut tanpa akses database/server.
