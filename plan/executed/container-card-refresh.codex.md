# Refresh card Container Manager

- Tambahkan tombol ikon refresh kecil di pojok kanan atas card Container Manager pada DefaultHomeControl.
- Klik hanya memperbarui storage card, dengan guard request ganda dan kondisi disabled bertema.
- Selesaikan kode, lalu build WPF dan review; catat verifikasi visual yang belum dilakukan.

## Hasil

- Tombol ikon refresh 26×26 di pojok kanan atas, hanya pada card Container Manager. Tombol navigasi dan refresh adalah sibling sehingga klik refresh tidak menavigasi.
- Command memanggil pembacaan storage untuk card itu saja; request ganda dicegah, tombol disabled selama request, aktif kembali melalui finally. Status gagal/registry nonaktif tetap tampil pada card.
- Memakai template toolTileStyle yang eksplisit, foreground mengikuti UserControl bertema, disabled memakai opacity; teks diberi ruang agar tidak bertumpuk dengan tombol.
- Build WPF lulus tanpa warning/error; review binding, guard async, dan pemisahan tombol selesai. Verifikasi render/interaksi tema terang/gelap serta transisi busy belum dilakukan.
