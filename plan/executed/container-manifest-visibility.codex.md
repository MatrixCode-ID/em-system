# Container Manager — visibilitas manifest tanpa tag

- Tanggal: 2026-10-19
- Permintaan langsung pengguna: update di EmSys setelah menyetujui menyembunyikan manifest tanpa tag.
- Implementasi: filter tampilan default hanya manifest bertag, checkbox **Show untagged**
  untuk pemeriksaan lanjutan; koleksi lengkap tetap dipakai deploy dan health checks.
- Tidak mengubah atau menghapus data registry maupun memublikasikan aplikasi.
- Panduan: `doc/engine/engine-registry.md`; keputusan berkelanjutan dicatat di `claude.md`.
- Verifikasi: `dotnet build src/shared/Em.Ui.Wpf.Core/Em.Ui.Wpf.Core.csproj --no-restore --verbosity quiet`
  lulus tanpa warning/error. Review diff memastikan deploy dan health checks tetap memakai
  koleksi lengkap. GUI tidak dijalankan sesuai aturan proyek.
- Perubahan pekerjaan lain yang sudah ada dipertahankan; tidak melakukan commit atau push.
