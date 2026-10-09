# Double-click baris SMTP Manager

Permintaan pengguna: double-click row membuka editor.

- Tambahkan event pada container baris SMTP, gunakan draft editor yang sama dengan tombol Edit.
- Hanya klik kiri pada baris, saat list dapat digunakan; area kosong tidak membuka editor.
- Perbarui guide, build dan jalankan uji console SMTP WPF yang sudah ada. GUI tidak dijalankan sesuai claude.md.

## Hasil

Selesai: double-click kiri pada ListBoxItem SMTP memilih profil baris itu lalu menjalankan EditCommand yang sama dengan toolbar. Guard CanBrowse mencegah membuka editor saat busy atau sheet/editor sudah terbuka. Style turunan mempertahankan tema/selection style bersama, dan klik area kosong tidak memiliki handler baris.

Build host WPF dan 7 uji SMTP WPF console (termasuk load XAML tanpa render) lulus. Interaksi mouse/GUI belum diuji. Guide: doc/engine/engine-smtp.md. Tidak ada perubahan backend/database atau push.
