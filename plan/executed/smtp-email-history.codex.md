# Riwayat email test SMTP

Permintaan pengguna: From/To menjadi editable ComboBox, riwayat email tersimpan di Registry aplikasi host yang sudah disiapkan (BaseRegKey), tombol Clear history. Aturan Registry ditambahkan ke claude.md sesuai permintaan.

- Tambahkan style editable ComboBox bertema tanpa mengubah perilaku style non-editable.
- Simpan From/To terpisah di subkey SmtpManager melalui BaseRegKey: MRU setelah send diterima, trim/dedup case-insensitive, maksimal 20.
- Baca saat manager dibuat, Clear history hapus kedua daftar tanpa menghapus input/profil atau Registry lain; kegagalan history tidak mengubah hasil send sukses.
- Uji console memakai base Registry key sementara terisolasi, uji WPF/XAML tanpa render dan build; perbarui guide.

## Hasil eksekusi

Selesai: From/To memakai fieldEditableComboStyle dengan PART_EditableTextBox dan popup bertema. Style non-editable tidak diubah. Riwayat REG_MULTI_SZ FromHistory/ToHistory hanya di SmtpManager di bawah EmApp.BaseRegKey host, dimuat ketika manager pertama kali dinavigasi. Setelah test diterima server, alamat trim/dedup case-insensitive dipromosikan ke depan, maksimal 20 per daftar. Kegagalan kirim tidak menambah history; kegagalan Registry setelah kirim tidak menutupi acceptance. Clear history menghapus hanya dua value, membersihkan dropdown, mempertahankan input/SMTP selection.

Rule claude.md: semua Registry fitur internal mengikuti BaseRegKey, tanpa root terpisah/hardcode host; pengecualian hanya integrasi operating system Windows yang mensyaratkan lokasi tertentu. Uji memakai Registry sementara yang terisolasi.

Verifikasi: 11 uji console SMTP WPF lulus, termasuk roundtrip Registry, urutan/dedup/batas, penghapusan tanpa kehilangan setting lain, simpan hanya setelah send sukses, serta load/apply template XAML tanpa render untuk kedua ComboBox editable. Build host WPF lulus dengan warning CS8604 lama module Em.Test. GUI/live SMTP belum diuji. Tidak ada push atau perubahan backend/database. Guide: doc/engine/engine-smtp.md.
