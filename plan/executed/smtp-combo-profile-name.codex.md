# Nama profil pada ComboBox SMTP

Laporan screenshot pengguna: pilihan SMTP menampilkan nama type CLR. Ganti DisplayMemberPath dengan ItemTemplate Name eksplisit agar template ComboBox bersama menampilkan nama profil baik di selection maupun dropdown.

Verifikasi: build WPF dan uji console SMTP/XAML tanpa render; GUI tidak dijalankan sesuai claude.md.

Permintaan lanjutan pengguna: tambahkan aturan item/value versus display text ke claude.md agar kasus nama class pada list tidak berulang. Tambahkan verifikasi template/binding dengan objek nyata tanpa render.

## Hasil

Selesai: ItemTemplate eksplisit Name dipakai dropdown dan SelectionBoxItemTemplate, SelectedItem tetap SmtpProfileDetail. Rule list/item/value/display ditambahkan ke claude.md sesuai permintaan pengguna.

Build host WPF dan 11 uji SMTP WPF console lulus; uji template menggunakan objek bernama Primary relay memastikan teks Name tampil, SelectedItem tetap model dan selection template sama dengan item template. Binding diuji setelah antrean DataBind dijalankan, tanpa render/GUI. Tidak ada push atau perubahan backend/database.

Verifikasi pengguna: pengguna menguji perbaikan nama profil SMTP pada aplikasi dan menyatakan berhasil (2026-10-09).
