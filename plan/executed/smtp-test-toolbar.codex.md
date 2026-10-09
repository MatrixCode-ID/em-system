# Toolbar test SMTP

Permintaan pengguna: toolbar kedua di bawah toolbar utama berisi combobox nama SMTP, From, To, Send menggantikan tombol test email dan side sheet.

- Toolbar test selalu terlihat, pilihan SMTP independen dari selection tabel dan default.
- Kirim lewat action diagnostic sender eksplisit yang sudah ada; input wajib, cegah request ganda/ketika editor terbuka.
- Pertahankan pilihan menurut ID saat refresh, kosongkan jika profil dihapus, tampilkan hasil/error di toolbar.
- Perbarui guide, uji view model dan load XAML tanpa render, build WPF. GUI tidak diuji sesuai claude.md.

## Hasil eksekusi

Selesai: toolbar kedua selalu terlihat dengan combo nama SMTP, From, To, Send dan hasil di bawahnya. Tombol Test email dan side sheet lama dihapus; Test connection tetap pada toolbar utama untuk row terpilih. TestProfile independen dari SelectedProfile/default dan dipertahankan menurut ID saat list diperbarui; penghapusan profil pilihan mengosongkannya. Sender/recipient hanya dipakai pada action diagnostic yang sudah tersedia.

Guard Send memerlukan pilihan profil dan kedua input, memblokir request ganda serta pengiriman ketika editor terbuka. Hasil/error tidak menghapus input. Build host WPF lulus, hanya warning lama CS8604 module Em.Test. 9 uji console SMTP WPF lulus (termasuk load XAML tanpa render, pilihan independen, refresh/delete pilihan, busy recovery dan duplicate send). GUI dan SMTP eksternal belum diuji; tidak ada perubahan backend/database atau push. Guide: doc/engine/engine-smtp.md.
