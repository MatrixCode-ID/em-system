# CLAUDE.md

## Library desain Em (keputusan pengguna 2026-11-05)

- Seluruh desain layout/mockup engine Em disimpan di `doc/design/` repo ini, dengan FigCraft
  (`.figcraft`). Desain modul engine di level atas folder: `<Subject>.figcraft`.
- Baseline Em ada di `_BASELINE/em-wpf.figcraft` (**Em WPF Baseline**). Baseline ini **bukan** salinan
  aturan/desain produk lain: tanpa nama produk, logo, server, atau data dari repo konsumen. Logo berasal
  dari `doc/assets/logo/`.
- Desain baru disalin dari baseline, nama dokumen diisi sesuai subjek, page baseline yang tidak dipakai
  dihapus, page `00 Assets` dipertahankan. Aset baru dibuat lebih dulu sebagai component di `00 Assets`,
  layar memakai instance. Warna terikat ke collection `Em Palette` (Light/Dark).
- Saat mendesain, pakai control channel FigCraft port 7979 (RunBay action em-system
  **Open FigCraft design (control 7979)**) agar pekerjaan terlihat di aplikasi.
- Repo publik: sebelum commit, pastikan berkas `.figcraft` (zip) tidak membawa image tak terpakai
  atau riwayat versi (`versions/`) dari desain produk lain.
- Rincian dan keputusan per desain dicatat di [README](README.md).

## Business Module Manager (keputusan pengguna 2026-11-05)

Navigasi halaman BMM memakai left drawer berisi TreeView yang diisi manual oleh BMM. Node **Folder**
(kelompok) dan **App** (halaman) dibedakan dengan ikon. Gaya node mengikuti kartu bertingkat katalog
(tile ikon, judul, subtitle); App tanpa arrow, pin, maupun tombol tiga titik. Ikon `secondary` di atas tile
`secondaryContainer`; App terpilih memakai tile `primary` dengan ikon `onPrimary`.

Keputusan pengguna 2026-11-07: drawer tree diterapkan ke BMM. Pada hari yang sama pengguna meminta menerapkan
Positions, Contact dan Head of department di Employee Editor, serta Attachments; page `30 Attendance` tetap bukan
perintah implementasi.
