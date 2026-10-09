# Sinkronisasi skema SMTP pada database aktif

- Tanggal: 2026-10-09
- Status: selesai
- Permintaan: perbaiki log SQL error 208 `Invalid object name 'ta_Smtp'`, lalu
  sinkronkan database `OSHA_CSM` pada tiga server yang disebut pengguna.
- Nama database dikonfirmasi pengguna setelah typo `osha_cms`.

## Penyebab dan tindakan

API memakai target remote dari `emapi-config.json`; eksekusi sebelumnya hanya
memigrasikan dua target lokal. Kredensial ternyata tersedia pada konfigurasi lokal.
Tidak ada perubahan pada signature action maupun source aplikasi untuk perbaikan
ini. Koneksi dibaca dari artefak privat tanpa mencetak password.

Skrip `doc/sqlscript/mssql/tables/050-smtp.sql` dan
`doc/sqlscript/mssql/views/vi_Smtp.sql` diterapkan pada ketiga target `OSHA_CSM`.
Detail server dan autentikasi hanya berada di
`../.artefacts/em-system/config/db-migration-targets.md`.

Sinkronisasi mencakup skema SMTP. Data aplikasi maupun profil SMTP tidak disalin
antarserver. Migrasi legacy dilakukan service secara atomik sesuai implementasi.

## Verifikasi

Harness `../.artefacts/em-system/scripts/smtp-upgrade-smoke/`:

- Ketiga koneksi berhasil dan database `OSHA_CSM` ditemukan.
- Kolom, index, check/default constraints dan definisi view identik.
- `GetMeta_SmtpProfiles`, adapter `GetMeta_SmtpSettings` dan migrasi ulang
  idempotent lulus pada setiap target.
- Daftar SMTP kosong pada ketiga target; default belum dipilih.
- Tidak menjalankan pengiriman email, render, atau GUI. HTTP live melalui
  aplikasi belum diperiksa; pengguna dapat refresh SMTP Manager.
- Tidak ada push. Hasil tersimpan pada `out/upgrade-report.txt` milik harness.
