# Perbaikan dark mode Container Manager

- Ikat foreground teks dan ikon Container Manager ke resource tema dinamis, termasuk tab, daftar, dan pohon container.
- Pertahankan warna aksen tab aktif serta warna ikon entitas dan status.
- Verifikasi build WPF dan tinjau diff; catat batas verifikasi visual.

## Hasil (2026-10-03)

- Selesai: `ContainerManager.xaml` memakai `themeWindowForegroundBrush` melalui `DynamicResource` pada layar, tab control/item, list, tree, item row, dan expander. Teks caption, detail, dan empty state serta ikon tanpa warna khusus mengikuti foreground tema. Aksen tab aktif tetap ditetapkan di header oleh style Material; tint entitas dan warna status tetap berlaku.
- Lulus: `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore -t:Rebuild` (0 warning, 0 error) dan `git diff --check`.
- Build incremental awal gagal BG1002 akibat BAML lama yang hilang; rebuild berhasil.
- Belum diverifikasi: tampilan aplikasi langsung, pemilihan row dengan data server, dan pergantian light/dark pada window berjalan.
