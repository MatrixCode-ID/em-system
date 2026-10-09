# Perbaikan sender SMTP

Permintaan pengguna: save tanpa sender, editor hanya konfigurasi koneksi; form test meminta sender. Pengirim diisi saat service dikonsumsi. Pertahankan action lama.

- Lepas validasi sender wajib dari konfigurasi dan kolom/input editor.
- Tambahkan FromAddress/FromName opsional pada pesan; fallback setting lama menjaga client lama.
- Tambahkan action diagnostic terpisah dengan sender eksplisit, proxy dan input test.
- Uji console validasi, save/read SQL, routing test/proxy, view model, build WPF/MAUI; perbarui guide.
- Uji GUI tidak dijalankan sesuai claude.md.

## Hasil eksekusi

Selesai: editor dan tabel tidak lagi menampilkan sender; sender wajib hanya pada pesan saat send, dengan fallback konfigurasi lama. Form test meminta kedua alamat dan memakai action baru ber-claim Manager, sehingga tidak membutuhkan SMTP Send. Kelima action ISmtpService dan action test profil terdahulu tetap tersedia tanpa perubahan signature. Penyebab baris tidak muncul ialah Save ditolak validasi sender; validasi tersebut sudah dihapus dari save/test koneksi.

Verifikasi console: 34 test SMTP lulus (17 unit backend, 8 integrasi SQL Server, 2 proxy, 7 WPF termasuk load XAML tanpa render). Mencakup save profil relay enabled tanpa autentikasi/sender, hasil list SQL, tabel awal kosong setelah save, validasi sender pada send, legacy fallback, routing test dan ordinal parameter proxy. Build API dan MAUI Core tanpa warning; build host WPF lulus dengan warning CS8604 lama di module Em.Test. Tidak melakukan migrasi database, push atau membuka aplikasi GUI. Verifikasi GUI/SMTP eksternal tertunda.

Guide: [engine-smtp.md](../../doc/engine/engine-smtp.md).
