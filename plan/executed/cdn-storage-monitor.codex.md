# Pemantauan storage CDN

- Tambahkan action CDN storage dengan total byte dan jumlah file publik di seluruh root/subfolder.
- Hindari file sementara/internal, hidden/system, serta symlink/reparse point; dukung pembatalan scan.
- Card CDN Manager mendapat angka storage dan tombol refresh seperti Container Manager; masing-masing refresh independen.
- CDN Manager menampilkan label total global dan cakupan, dibaca ulang saat refresh/mutasi.
- Setelah kode selesai: build backend/WPF, uji scan dengan fixture terisolasi, review dan catat batas verifikasi.

## Hasil eksekusi 2026-10-03

- Selesai: DTO `CdnStorageInfo`, action GET berclaim CDN Manager Access, scan CdnStore, proxy WPF,
  total dan refresh pada card CDN; label global dan cakupan pada CDN Manager.
- Template card dibagikan untuk CDN/container lewat `HasStorageRefresh`, dengan tooltip berbeda
  sesuai layanan. Request storage home berjalan independen melalui Task.WhenAll; tombol guard per card.
- Total berdasarkan panjang file publik di seluruh root; internal/temp/hidden/system/reparse tidak dihitung.
  Scan iteratif dan cancellation token; akses gagal tidak diam-diam menghasilkan total parsial.
- Build backend dan WPF lulus, 0 warning/error; git diff --check lulus (hanya warning konversi LF pada claude.md yang sudah berubah sebelum task).
- `dotnet run --project scripts/cdn-storage-smoke`: 7 pemeriksaan lulus (empty, nested dan pengecualian,
  symlink keluar root/cycle, refresh sesudah delete, claim GET, cancellation, disabled 404).
  Fixture dibersihkan dan CDN aktif tidak disentuh.
- Review selesai: tidak ada migrasi database, perubahan pengguna dipertahankan, semua mutasi yang
  membaca ulang folder memperbarui angka. Tombol refresh sibling tombol navigasi, kondisi disabled
  tetap memakai template bertema dari pekerjaan sebelumnya.
- Verifikasi tertunda: HTTP end-to-end serta render/interaksi WPF tema terang/gelap dan busy/disabled.
  Build bukan bukti render. Tidak ada tindakan terblokir policy.
- Batas: scan penuh mengikuti jumlah file, dan bukan snapshot transaksi. Hardlink dihitung per path
  sesuai ukuran payload file, bukan alokasi disk fisik. Panduan: doc/engine-cdn-storage.md.
