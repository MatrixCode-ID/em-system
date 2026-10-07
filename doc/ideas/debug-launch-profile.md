# Profil launch `--no-debug` untuk build Debug

- Tanggal: 2026-10-07
- Status: diskusi

## Latar belakang

Saat menyusun plan [usermanager-roles-dan-switch-user](../../plan/executed/usermanager-roles-dan-switch-user.md)
ada dua cara menguji alur aplikasi tanpa debug dari build Debug: (1) Simulate Login di dalam proses yang sama,
(2) profil launch / argumen `--no-debug` yang membuat host tidak memanggil `AddDebug`, atau relaunch proses.
Pengguna memilih (1); (2) dicatat sebagai pembanding.

## Keputusan yang sudah jelas

- Simulate Login sudah dibangun dan menutup kebutuhan menguji login, sesi, sign out, dan sesi kedaluwarsa.

## Pertanyaan terbuka

- Apakah alur startup non-debug (pemulihan sesi tersimpan `RestoreSessionAsync`, layar login pertama kali)
  perlu diuji dari build Debug? Simulate Login tidak melewati startup.
- Bila perlu: argumen baris perintah di host (`Em.Ui.Wpf`), profil di `launchSettings.json`, atau keduanya?
