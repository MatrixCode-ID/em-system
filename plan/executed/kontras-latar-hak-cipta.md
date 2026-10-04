# Perjelas latar hak cipta login Material

Permintaan pengguna 2026-10-02: latar pill hak cipta pada mode terang kurang terlihat.

- [x] Pada mode terang, gunakan pasangan token PrimaryContainer/OnPrimaryContainer, opacity penuh, dan outline variant tipis untuk pill hak cipta.
- [x] Pertahankan tampilan mode gelap dan Classic.
- [x] Build WPF dan render lewat harness yang sudah tersedia; periksa hasil mode terang dan gelap.
- [x] Catat hasil lalu pindahkan plan ke `plan/executed/`.

## Hasil

Pill hak cipta terang kini memakai latar tonal biru dan teks pasangan temanya, outline variant 1 px, serta opacity 1. Mode gelap mempertahankan surface container dengan opacity 0,85. Tidak ada perubahan palet global atau Classic.

Saat uji render ditemukan state tema VM belum diberi notifikasi setelah AttachApp. Tambahkan `Vm.RefreshThemeState()` pada constructor Material agar trigger mode terang serta tombol tema benar sejak layar pertama dibuka.

`dotnet build src/frontend/Em.Ui.Wpf.slnx --nologo -v:q` lulus dengan 0 warning dan 0 error. Harness scratchpad yang sudah tersedia dijalankan ulang; semua assertion lulus, termasuk kesetaraan piksel Classic pada kedua tema. Render `normal-Light.png` dan `normal-Dark.png` diperiksa; render terang akhir memperlihatkan latar pill biru yang jelas. `git diff --check` lulus.

Belum diuji pada window aplikasi sungguhan atau background aplikasi asli. Perubahan disertakan dalam commit perbaikan tampilan login sesuai permintaan pengguna.

## Perluasan: mode gelap

Pengguna meminta latar hak cipta mode gelap diperjelas juga. Hasil akhir kedua mode kini memakai pasangan `PrimaryContainer`/`OnPrimaryContainer` dengan opacity penuh dan outline variant 1 px. Trigger khusus terang disederhanakan menjadi DynamicResource langsung; pergantian tema mengikuti token aplikasi. Keputusan mempertahankan latar gelap pada tahap sebelumnya digantikan permintaan ini. Classic tetap tidak berubah.

Build WPF ulang lulus dengan 0 warning dan 0 error. Harness dijalankan ulang dan seluruh assertion lulus. Render gelap dengan gradien dan gambar terang pinjaman diperiksa secara visual: latar pill tonal biru dan teks terang terlihat jelas. Belum diuji pada window aplikasi sungguhan atau gambar asli aplikasi; perubahan disertakan dalam commit perbaikan tampilan login sesuai permintaan pengguna.
