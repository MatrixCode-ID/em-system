# SMTP multi-profile dan UI tabular

- Tanggal: 2026-10-09
- Status: kode dan verifikasi console selesai; target remote diselesaikan pada tindak lanjut sinkronisasi database
- Sumber: [diskusi](../../doc/ideas/smtp-manager-multi-profile.md)

## Cakupan

1. Integrasikan `ta_Smtp`, migrasi konfigurasi lama secara atomik/idempotent.
2. Pertahankan semua action/DTO `ISmtpService` dan overload cancellation.
3. Tambahkan interface/action profil terpisah untuk CRUD, default, test dan send
   berdasarkan nama. Default baru dipilih manual; delete default diperbolehkan;
   disabled default tetap default tetapi send error. Tanpa nama/default error.
4. UI WPF tabular mengikuti UserManager dan MaterialDesign; New selalu tersedia
   ketika tidak busy, editor dan test memakai profil terpilih.
5. Perbarui guide, build backend/WPF/MAUI dan uji console regresi/integrasi.

Adapter save lama tanpa default membuat default baru agar alur client lama tetap
bekerja. Revision kompatibilitas global bertipe long, revision per profil int.
Key/password lama tetap dipertahankan; arsip lama bukan fallback runtime.
Uji visual tidak dijalankan sesuai claude.md. Tidak ada push.

## Hasil

- ORM `ta_Smtp` terpasang pada `ApiCoreContext`; penyimpanan profil memakai tabel.
- `ISmtpService`, DTO/proxy lama dan kelima action tetap tersedia. Interface
  `ISmtpProfileService` terpisah membawa tujuh action baru tanpa overload HTTP.
- Migrasi runtime serializable mempertahankan key/ciphertext dan global revision
  long, serta menyimpan marker agar delete tidak memunculkan konfigurasi lama.
- Default dipilih manual melalui UI. Menghapus/clear default diperbolehkan;
  disabled tidak mencabut default. Send tanpa default 409; nama tidak ada 404.
- Save client lama tanpa default membuat satu default baru, tanpa mengubah profil
  non-default lain. Revision global menolak stale save setelah pergantian default.
- Manager WPF tabular memiliki New/Edit, default/clear, Delete dengan konfirmasi,
  editor draft terpisah, test profil terpilih, refresh dan wait overlay bertema.
- Kedua proxy didaftarkan otomatis pada WPF/MAUI. Guide selesai di
  [engine-smtp.md](../../doc/engine/engine-smtp.md), indeks engine diperbarui.

## Verifikasi

- Build backend/WPF/MAUI lulus. Backend 0 warning; WPF memiliki warning CS8604
  yang sudah ada pada `Em.Test.Wpf/TestService.cs`; MAUI 0 warning.
- `dotnet test --solution src/backend/Em.Api.slnx --no-build --no-restore`:
  148 passed, 0 failed, 0 skipped.
- `dotnet test --solution src/frontend/Em.Ui.Wpf.slnx --no-build --no-restore`:
  82 passed, 0 failed, 0 skipped. Termasuk load XAML tanpa render.
- Harness `../.artefacts/em-system/scripts/smtp-schema-smoke/` lulus pada DB
  sementara: banyak profil, tambah saat default ada, unique nama/default, nilai
  invalid, view tanpa password, ganti/hapus default dan rerun idempotent.
- Harness `../.artefacts/em-system/scripts/smtp-upgrade-smoke/` menerapkan skema
  dan memeriksa adapter/migrasi ulang pada dua DB lokal dalam catatan target
  privat. Keduanya belum memiliki konfigurasi SMTP, sehingga daftar awal kosong.
- Build dan test console dilakukan setelah seluruh kode/UI/test ditulis.
- Tidak ada perubahan pada ide object storage milik pengguna; tidak ada push.

## Tertunda

- Render/interaksi GUI tidak dijalankan sesuai claude.md. Pengiriman melalui
  SMTP eksternal serta autentikasi HTTP live dari aplikasi belum diverifikasi.

## Tindak lanjut error schema, 2026-10-09

Log pengguna menunjukkan SQL error 208 pada `ta_Smtp`. Koneksi API ternyata
memakai target remote, sementara eksekusi awal baru mencakup dua DB lokal.
Kredensial target remote tersedia dalam konfigurasi lokal; asumsi sebelumnya
bahwa kredensial tidak tersedia telah dikoreksi. Skema diterapkan pada database
API tersebut, lalu pengguna meminta sinkronisasi pada tiga target `OSHA_CSM`.

Tabel/view sudah diterapkan pada ketiga target yang dicatat dalam artefak privat.
Kolom, index, check/default constraints dan definisi view identik. Action list,
adapter read lama, serta migrasi ulang idempotent lulus pada setiap target.
Ketiganya memiliki daftar SMTP kosong; tidak ada data aplikasi yang disalin
antarserver. Laporan: [smtp-sync-database.codex.md](smtp-sync-database.codex.md).
