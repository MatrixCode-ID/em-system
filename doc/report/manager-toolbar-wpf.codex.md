# Kerapian toolbar manager WPF

Tanggal: 2026-10-04. Permintaan langsung pengguna, acuan UserManager.

- Toolbar profil Publish dipindahkan dari sidebar ke satu baris horizontal di atas area kerja; aksi utama filled, aksi sekunder outlined, ikon dan separator mengikuti UserManager. Aksi tambahan tersedia melalui More. Sidebar hanya daftar profil dengan refresh. Perubahan dipakai bersama NuGet Manager dan Container Manager.
- Toolbar operasi dan history tetap satu baris dengan scroll horizontal saat ruang sempit. Margin card, label, input NuGet, dialog, dan settings dirapikan; field multiline tidak lagi memakai tinggi tetap 42. Perubahan NuPakManager yang sudah ada sebelum pekerjaan dipertahankan.
- Aturan tetap ditambahkan ke claude.md tanpa menghapus isi sebelumnya.
- Verifikasi: build solution WPF lulus tanpa warning/error; harness Publish menghasilkan 42 render terang/gelap, idle/busy/error/history/form/settings dan lebar 900; hasil layout utama, ukuran sempit dan settings diperiksa secara visual. Harness NuPak lulus pada kedua tema termasuk busy, retry, permission dan pergantian feed.
- Interaksi mouse/keyboard menu More pada window aplikasi nyata dan publish ke server belum diuji. Render bukan pengujian proses publish end-to-end.
- Artefak Publish berada di ../.artefacts/em-system/publish-toolbar-render (di luar repo).
