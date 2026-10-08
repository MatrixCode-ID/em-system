# Framework status bar

- Tanggal: 2026-10-08
- Status: diskusi

## Yang sudah ada

UI status bar di bagian bawah `TabbedMainWindow` (hanya window utama, tidak di window hasil tear-off):

- Control lookless `Em.Ui.Wpf.Controls.EmStatusBar` (style default di `Themes/Generic.xaml`):
  `Text` di kiri, `LeftItems` setelahnya, `RightItems` rata kanan.
- Isinya dari `EmApp.MainWindow.Vm.StatusBar` (`MainStatusBarVm`): `IsVisible`, `Text` (bawaan
  "Ready"), `LeftItems`, `RightItems`. Item boleh string, `UIElement`, atau objek ber-data template.

## Pertanyaan terbuka (framework)

- Siapa yang boleh mengisi: host saja, module, atau layar aktif (item per tab yang ikut berganti saat
  tab berpindah)?
- Bentuk item standar: teks, ikon + teks, indikator progres, tombol? Perlu model item (`StatusItem`)
  dengan prioritas/urutan dan id agar bisa diganti/dihapus?
- Pesan sementara (mis. "Saved" hilang setelah beberapa detik) dan tingkat pesan (info/warning/error).
- Item bawaan engine yang layak tampil: koneksi server, user aktif, versi aplikasi, jumlah task berjalan.
- Akses dari thread non-UI.
- MAUI: perlu padanan atau tidak.
