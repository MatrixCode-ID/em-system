# Robot di UserManager

Permintaan 2026-10-03: ganti tabel `ta_CtnRobot` menjadi `ta_Robot` dan
`ta_CtnRobotRoot` menjadi `ta_CtnRootRobot`; pindahkan pengelolaan robot dari
ContainerManager ke UserManager dengan tab Users dan Robots berikon.
Robot harus dapat memakai banyak manager, dengan jenis hak milik setiap manager.

Pengguna sudah melakukan rename kedua tabel pada database lokal `EmDb`.
Ikuti nama kolom terbaru yang dikonfirmasi pengguna; pertahankan seluruh data/token.
Sediakan migrasi idempotent
untuk instalasi lain. Pertahankan perubahan lokal ContainerManager.xaml.

Tahap kode: skema/model; layanan robot umum + provider Container; UI dua tab;
skrip uji dan dokumentasi. Sesudah seluruh kode selesai: build, review, verifikasi.

Pembaruan pengguna: kolom telah di-rename menjadi cRobot* (termasuk FK upload/manifest).
Agent diminta rename cCtnRobotRootAccess → cCtnRootRobotAccess pada database lokal;
sudah dijalankan dengan penggantian CHECK constraint R/W dalam transaksi.

Selesai 2026-10-03: seluruh tahap kode selesai. Build backend/WPF lulus; 30 uji
layanan lulus; migrasi lama + idempotensi lulus; render kedua tema lulus. Uji HTTP
terhenti pada sign-in 401; detail dan langkah ulang tercatat di
`doc/report/robot-user-manager-eksekusi.md`. Tidak ada pengubahan akun/password.
