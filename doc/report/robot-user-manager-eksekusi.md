# Robot User Manager — hasil 2026-10-03

## Perubahan selesai

- Identitas robot dipindahkan ke `ta_Robot` dengan kolom `cRobot*`.
- Grant Container memakai `ta_CtnRootRobot`, `cRobotId`, `cCtnRootId`,
  `cCtnRootRobotAccess`. FK upload dan pengirim manifest memakai `cRobotId`.
- Pengguna sudah rename tabel dan kolom identitas/referensinya. Agent menjalankan
  rename kolom akses pada database lokal, sekaligus mengganti CHECK constraint
  R/W dalam satu transaksi. Constraint aktif dan trusted; tidak ada data dihapus.
- `IRobotServices`/`RobotServices`/`RobotContext` mengelola identitas dan token
  secara terpisah dari registry, dengan claim **User Manager Access**.
- `IRobotAccessManager` menyediakan resource, grant dan kode/label hak masing-masing
  manager. Provider Container mempertahankan Read/Write per root.
- UserManager WPF memiliki tab **Users** dan **Robots**, keduanya berikon.
  Editor robot menampilkan grant beberapa manager/resource dalam satu daftar.
  Buat/edit/hapus, regenerasi token dan grant disimpan lewat layanan robot umum.
- ContainerManager hanya mengelola container. Perubahan tema lokal yang sudah ada
  dipertahankan. Nama action robot lama di ICtnServices dihapus; skrip HTTP disesuaikan.
- Skrip instalasi baru dan migrasi lama tersedia. Cara memakai/menambah provider:
  [engine-robots.md](../engine-robots.md).

## Verifikasi

- `dotnet build src/backend/Em.Api.slnx --no-restore -v:q`: lulus, 0 warning/error.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-restore -v:q`: lulus, 0 warning/error.
- `dotnet run --project scripts/robot-smoke/RobotSmoke.csproj`: **30 pemeriksaan lulus**
  memakai layanan langsung dan SQL Server lokal: CRUD, duplikasi/nama/expiry,
  SHA-256, Basic authentication, nonaktif/expired, regenerate, dua manager pada
  satu identitas, hak non-R/W, pencabutan idempotent, resource/manager/kode tidak
  sah, rollback kegagalan cleanup, grant/upload ikut dihapus, manifest dipertahankan
  tanpa pengirim, serta pendaftaran tanpa provider registry.
- Migrasi dari skema lama diuji dalam schema sementara dengan transaksi yang
  di-rollback: rename tabel/kolom dan CHECK constraint lulus; eksekusi kedua lulus
  sebagai no-op. Tidak ada schema uji atau robot bernama `smoke-*` yang tertinggal.
- Kedua tab dirender pada tema terang dan gelap memakai harness WPF dengan data
  contoh. Dua manager (Container dan Jobs) tampil dengan hak berbeda. Jobs hanya
  contoh pada harness, bukan manager produksi yang ditambahkan. Teks ComboBox
  akses diperbaiki agar terbaca pada tema gelap.
- Server lokal sempat dijalankan dan menerima request; proses pengujian sudah
  dihentikan. `git diff --check` dan syntax skrip HTTP lulus.

Hasil render (bukan screenshot interaksi dengan server):
[Users terang](robot-user-manager/um-users-light.png),
[Users gelap](robot-user-manager/um-users-dark.png),
[Robots terang](robot-user-manager/um-robots-light.png),
[Robots gelap](robot-user-manager/um-robots-dark.png).

## Verifikasi yang belum selesai

Uji `scripts/registry-http-test.py` berhenti saat sign-in admin mendapat **401**,
sebelum fixture HTTP dibuat. Password konfigurasi initial admin tidak menjamin
akun admin aktif atau password saat ini sama. Tidak ada perubahan akun/password
yang dilakukan. Pengujian layanan langsung di atas tidak menguji gerbang claim HTTP.
UI terhadap server dengan mouse, popup pilihan akses, dan Docker sungguhan belum diuji.

Untuk mengulang uji HTTP, nyalakan server kemudian jalankan dari root repo dengan
akun penguji yang dapat login dan memiliki kedua claim manager:

```powershell
$env:EM_BASE_URL = 'http://localhost:5132' # sesuaikan port server
$env:EM_ACCOUNT = '<akun penguji>'
$env:EM_PASSWORD = '<password akun penguji>'
python scripts/registry-http-test.py
Remove-Item Env:EM_PASSWORD
```

Untuk database lokal yang sudah di-rename, tidak perlu menjalankan migrasi lagi.
Database lama lainnya memakai `doc/sqlscript/mssql/updates/20261003-RobotUserManager.sql`.
