# Link owner account robot — 2026-10-03

## Hasil

- Dialog **New Robot** memiliki pilihan **Owner account**, default **No owner**.
  Daftar berisi user aktif non-admin; akun sistem admin/debugger dikecualikan.
- Endpoint `GetMeta_RobotOwners` memakai claim **User Manager Access**.
  Create menerima `ownerUserId` opsional; backend memvalidasi user yang dipilih.
  Pemanggil dengan tiga parameter tetap dapat membuat robot tanpa owner.
- Owner disimpan di `ta_Robot.cRobotOwner_cUserId` dan dikembalikan beserta akun
  pada create/list/regenerate. Detail robot menampilkan owner. Edit/regenerate
  mempertahankan relasi. Penghapusan user mengosongkan link melalui FK SET NULL.
- Kepemilikan adalah metadata. Tidak mewariskan claim, sesi, atau grant user.
- DDL instalasi `Robot.sql` dan `Ctn.sql` diperbarui; migrasi idempotent tersedia
  di `doc/sqlscript/mssql/updates/20261003-RobotOwner.sql`.
- Migrasi sudah dijalankan pada database lokal tanpa menghapus identitas lama.
  Database lain harus menjalankan migrasi sebelum memakai backend versi ini.
- Perubahan yang sudah ada pada working tree dipertahankan.

## Verifikasi

- `dotnet build src/backend/Em.Api.slnx --no-restore`: lulus, 0 warning/error.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore`: lulus, 0 warning/error.
- `dotnet run --project scripts/robot-smoke -- --migrate-owner`: lulus,
  **42 pemeriksaan**. Migrasi dieksekusi dua kali untuk memastikan idempotence.
  Pemeriksaan mencakup daftar owner, penolakan ID tidak dikenal/admin/inactive,
  penyimpanan dan pembacaan owner, regenerate mempertahankan owner, delete owner
  mengosongkan relasi dan token robot tetap bekerja, serta create tanpa owner.
  Uji sebelumnya untuk grant lintas manager, autentikasi, expiry, rollback cleanup,
  dan penghapusan robot tetap lulus. Fixture user/robot/container dibersihkan di finally.
- `git diff --check`: lulus (peringatan konversi LF/CRLF pada `claude.md`).

## Belum diuji

Interaksi dropdown/dialog WPF dengan server nyata, render visual perubahan dialog,
dan gerbang claim melalui HTTP belum diuji. Uji SQL memakai layanan langsung.

Panduan penggunaan: [engine-robots.md](../engine-robots.md).
