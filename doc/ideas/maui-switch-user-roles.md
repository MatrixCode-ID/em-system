# Tab Roles, Switch User, dan Simulate Login untuk MAUI

- Tanggal: 2026-10-07
- Status: diskusi

## Latar belakang

Plan [usermanager-roles-dan-switch-user](../../plan/executed/usermanager-roles-dan-switch-user.md) dikerjakan
untuk WPF saja (keputusan pengguna). Client MAUI (`Em.Ui.Maui.Core`, android, SPA) masih memakai
`IsDebugMode` langsung sebagai bypass hak (lihat komentar `NavigationAccess`), belum punya tab Roles, Switch
User, maupun Simulate Login.

## Keputusan yang sudah jelas

- Server sudah mendukung impersonasi debug (`X-Em-User` + debug token), jadi pekerjaannya di client.
- Aturan tiga saklar WPF (`IsDebugMode`, `IsDebugActive`, `IsDebugBypass`) layak dipakai juga di MAUI agar
  perilaku kedua client sama.

## Pertanyaan terbuka

- Apakah MAUI butuh User Manager sama sekali (saat ini layar admin hanya di WPF)?
- Switch User di MAUI: dialog atau halaman SPA?
- Simulate Login di MAUI: perlu, atau cukup build release untuk menguji login?
