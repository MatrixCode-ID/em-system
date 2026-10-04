# Perbaikan owner robot dan label Role Manager

Permintaan: akun akun-uji tidak tersedia saat Add Robot di EmPorium; label Role Manager tidak terbaca pada tema gelap.

- Izinkan user aktif, termasuk admin biasa, sebagai owner; kecualikan identitas sistem Admin/Debugger. Owner tetap metadata tanpa pewarisan hak.
- Pastikan tab dan isi Role Manager memakai foreground tema, termasuk disabled.
- Setelah seluruh kode selesai, jalankan build, smoke SQL, pemeriksaan akun secara read-only, dan render terang/gelap.

## Hasil eksekusi (2026-10-03)

- Database engine dan EmPorium pada konfigurasi lokal sama (dibandingkan tanpa mencetak connection string). Pemeriksaan read-only: akun-uji aktif, admin biasa, bukan akun sistem; kini eligible menjadi owner. Tidak ada perubahan status/hak akun tersebut.
- Filter dan validasi server menerima admin biasa; Admin/Debugger serta user tidak aktif tetap ditolak. Bantuan dialog dan panduan diperbarui. Owner tidak mewariskan hak; robot baru tetap tanpa grant.
- Style tab bersama menetapkan foreground tema pada header dan presenter isi, termasuk disabled. Role Manager menetapkan foreground tema pada root dan ScrollViewer agar default WPF tidak menggantinya menjadi hitam/abu sistem.
- `dotnet run --project scripts/robot-smoke`: 43 pemeriksaan SQL Server lulus, termasuk create dengan admin owner, token, grant, dan cleanup fixture.
- `dotnet run --project scripts/robot-smoke -- --inspect-owner akun-uji`: eligible True; mode read-only.
- `dotnet run --project scripts/role-manager-render`: 9 pemeriksaan render lulus (terang → gelap → terang, masing-masing enabled → disabled → enabled). Memakai fixture tanpa server; PNG di bin/Debug/net10.0-windows. Foreground label dan header tab tidak terpilih diperiksa terhadap token tema. Render gelap enabled serta terang disabled ditinjau visual. Perubahan resource tema diberikan ke root render karena visual terlepas dari window live.
- Build Release host EmPorium API dan WPF lulus, 0 warning/error.
- Review akhir: tidak memerlukan migrasi database; hak manager dan autentikasi robot tidak berubah. Style tab bersama juga memperbaiki foreground pada layar lain yang memakai style yang sama.
- Belum diuji: interaksi Add Robot melalui host EmPorium yang sedang berjalan dan pergantian tema di window live. Proses pengguna tidak dihentikan; API/UI perlu dijalankan ulang memakai build baru.
