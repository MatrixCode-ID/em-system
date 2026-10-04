# Perjelas cara menjalankan lewat Compose

Tanggal: 2026-10-03

Permintaan: perbarui `doc/setup-container.md` agar cara menjalankan lewat Compose mudah ditemukan.

- Tambahkan langkah langsung dari folder backend, cara run image yang sudah tersedia, serta penjelasan opsi run.
- Pertahankan panduan build dari root repo.
- Validasi Compose tanpa menjalankan service, lalu arsipkan task.

Hasil: selesai. Bagian "Jalankan lewat Docker Compose" sekarang memuat perintah dari folder backend, run tanpa build, mode foreground/background, alamat default, dan navigasi kembali ke root. Validasi konfigurasi Compose dengan `.env.example` dan `git diff --check` lulus. Service tidak dijalankan karena permintaan hanya memperbarui dokumentasi.
