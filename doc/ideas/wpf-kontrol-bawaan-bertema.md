# WPF: kontrol bawaan harus mengikuti tema engine

- **Tanggal:** 2026-10-08
- **Status:** diskusi
- **Plan turunan:** belum ada

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Latar belakang

Di host yang hanya membungkus engine (EmPorium House, paket EmSys 0.1.0-alpha.6), User Editor tema gelap menunjukkan dua celah: header tab yang tidak terpilih tampil hitam (style bawaan `TabItem` memberi warna teks hitam tetap), dan scrollbar tampil terang (style bawaan `ScrollBar` memakai warna sistem). Engine tidak boleh mengandalkan tema dari luar untuk kontrol WPF bawaan.

Keduanya sudah diperbaiki langsung atas permintaan pengguna (2026-10-08): style `ScrollBar` implicit bertema di `Styles/ScrollBars.xaml` (dipasang di level aplikasi oleh `EmApp.Run` dan lewat `MaterialDesign.xaml`), dan `Foreground` bertema pada `editorTabItemStyle` serta `detailTabItemStyle`.

## Gagasan lanjutan

- **Audit kontrol bawaan lain** yang warna default-nya diambil dari tema Aero2/sistem dan belum dibungkus style engine, mis. sudut pertemuan dua scrollbar di `ScrollViewer` (kotak terang), `GridSplitter`, `ToolTip`, `ContextMenu`/`MenuItem` bawaan, `Expander`, `GroupBox`, `DataGrid` (bila dipakai), `CheckBox`/`RadioButton` tanpa style. Cari layar engine yang memakai kontrol itu tanpa style eksplisit.
- **Acuan uji:** render layar engine utama di host yang hanya membungkus engine (tema terang dan gelap) sebagai bagian review UI.

## Pertanyaan terbuka

- Seberapa luas audit dilakukan: semua layar engine, atau hanya yang tampil di host tanpa modul?
