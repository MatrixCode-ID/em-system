# Cegah kilatan putih Container Manager

- Perbaiki ListBox roots dan TreeView container yang disabled saat busy.
- Sediakan template bersama bertema untuk daftar/tree dan gunakan juga pada Robots serta folder picker Container Manager.
- Setelah kode lengkap, build WPF dan verifikasi render enabled/disabled pada tema terang/gelap, scrolling serta selection/tree expansion.
- Catat hasil dan batas verifikasi, lalu pindahkan plan ke executed.

## Hasil 2026-10-03

- Roots ListBox dan TreeView memakai template native yang mengecat latar disabled
  dengan warna sistem: BGRA roots `255,255,255,255`, tree `240,240,240,255`.
  Keduanya terpicu oleh binding `IsNotBusy` pada operasi async.
- Tambahkan keyed style `surfaceListBoxStyle`/`surfaceTreeViewStyle` pada
  `Styles/Collections.xaml` dan gabungkan lewat `MaterialDesign.xaml`.
  Template mengikat background/border/padding serta pengaturan ScrollViewer;
  tidak memiliki trigger disabled yang mengganti warna dengan warna sistem.
- Terapkan pada Container Manager roots/tree, CtnFolderPickerDialog, dan Robots
  (menggantikan template lokal sebelumnya). Binding dan template item tidak diubah.
- Perbarui aturan claude.md agar agen berikutnya memakai style bersama tersebut.
- Build WPF `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore` lulus,
  0 warning/error; `git diff --check` lulus.
- Harness sementara `%TEMP%/em-container-flash-qa` merender ContainerManager
  pada LightTheme dan DarkTheme dalam kondisi `IsBusy=false/true`; warna roots
  dan tree identik sebelum/sesudah busy (terang BGRA `247,243,244,255`, gelap
  `30,26,23,255`). Template native direproduksi dengan menghapus style lokal,
  menghasilkan putih/abu terang di kedua tema seperti nilai di atas.
- Uji style dengan 200 item: selection, scrolling lewat ScrollViewer, dan
  virtualisasi ListBox lulus. TreeView dengan 30 anak: expand, selection,
  scrolling, collapse lulus pada kedua tema. Uji dilakukan tanpa window tampil.
- Harness Robots sebelumnya diuji ulang: idle/busy identik di kedua tema.
- Render Container Manager gelap saat busy diperiksa secara visual.
- Interaksi mouse/keyboard, drag-drop, folder picker dalam window nyata,
  dan transisi request terhadap server belum diuji. Uji visual tidak mengubah DB.
