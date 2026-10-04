# Link owner account saat membuat robot

Permintaan: saat create robot, sediakan pilihan menghubungkan owner ke user biasa.

- Tambahkan owner opsional (default tanpa owner), daftar akun user aktif non-admin,
  validasi backend, penyimpanan relasi, dan tampilan owner pada detail robot.
- Link owner tidak mewariskan hak user; grant robot tetap melalui manager.
- Perbarui DDL instalasi dan sediakan migrasi idempotent untuk database lama.
- Setelah seluruh kode selesai, build backend/WPF dan uji layanan SQL Server;
  catat hasil serta verifikasi yang belum dilakukan.
- Pertahankan perubahan sebelumnya dalam working tree.
