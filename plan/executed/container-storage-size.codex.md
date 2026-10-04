# Ukuran storage container

Permintaan: action cek storage pada service container, total di card Container Manager pada DefaultHomeControl, dan label keterangan di Container Manager.

- Tambahkan DTO dan action GET berclaim Container Manager Access; jumlah blob unik tersimpan + manifest, tanpa upload sementara atau overhead database.
- Tampilkan total pada card home dan label beserta rincian pada manager; refresh membaca ulang angka.
- Pertahankan perubahan pengguna yang sudah ada; tanpa migrasi database.
- Setelah semua kode selesai, build backend/WPF, uji agregasi layanan, lalu catat hasil dan batas verifikasi.

## Hasil eksekusi 2026-10-03

- Selesai: DTO `CtnStorageInfo`, action `GetMeta_CtnStorageSize`, proxy WPF, angka total pada card DefaultHomeControl, label total/rincian/cakupan pada Container Manager. Refresh dan mutasi yang membaca ulang root juga memperbarui ukuran.
- Tidak ada migrasi database; ukuran berdasarkan metadata blob unik + seluruh payload manifest. Blob yatim tetap dihitung karena belum ada GC. Upload sementara dan overhead tidak dihitung.
- Build backend dan WPF lulus (0 warning/error). WPF dibangun ulang setelah perbaikan refresh ukuran pascamutasi.
- `dotnet run --project scripts/container-storage-smoke --no-restore`: 7 pemeriksaan SQL Server lulus; fixture transaksi di-rollback. Memeriksa hasil terhadap SQL, deduplikasi blob bersama, retained blob, bigint >4 GB, tag tidak menggandakan manifest, total JSON, claim GET, dan 404 registry nonaktif (beberapa kondisi digabung dalam satu pemeriksaan).
- Review: tidak ada join tag/link dalam agregasi yang dapat menggandakan ukuran; SUM nullable menangani tabel kosong; cancellation token diteruskan; hak mengikuti claim yang sudah ada. Perubahan pengguna yang sudah ada dipertahankan.
- Verifikasi tertunda: gerbang HTTP end-to-end dan render/interaksi WPF tema terang/gelap termasuk busy/disabled. Label baru berupa TextBlock yang mewarisi warna bertema; kontrol list/tree dan template busy yang sudah ada tetap dipakai. Build tidak dianggap membuktikan render.
- Batas: dua SUM dibaca berurutan sehingga angka dapat berubah saat push/delete berjalan bersamaan; ini ringkasan pemantauan, bukan snapshot transaksi atau pengukuran ruang volume.
