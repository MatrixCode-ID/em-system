# Eksekusi CDN/registry settings UI

Tanggal: 2026-10-03. Implementasi selesai; verifikasi tambahan di bawah masih tertunda.

## Hasil

- `Program.cs` kini cukup memanggil `builder.AddManagedStorageSettings()`: tidak ada
  path CDN/registry atau limit CDN pada konfigurasi host. Defaults lama dipertahankan
  dalam overload builder. Binary approval dan Em.Test tetap terdaftar.
- Sesuai arahan pengguna saat eksekusi, persistence memakai dokumen JSON berversi
  dalam `ta_Meta`, menggantikan rancangan awal file runtime. Key per host memakai
  hash machine name, content root, dan host ID. Transaksi serializable + revision
  mencegah kehilangan perubahan antar-fitur/proses; konflik DB juga dijawab 409.
  Tidak ada perubahan DDL atau konfigurasi persisten database lokal aplikasi.
- Kontrak umum tanpa path, detail aktif/tersimpan, Validate, Save, dan claim baru
  tersedia pada layanan CDN/registry. Claim lama tidak mendapat akses settings.
  Metadata/config tetap tersedia ketika store disabled. Managed mensyaratkan skema
  registry meskipun disabled; static host lama tetap didukung dan menolak Save.
- Save mempertahankan snapshot/store/middleware aktif sampai restart. Pending
  dibatalkan bila nilai aktif disimpan kembali, dengan perbandingan path ternormalisasi.
- Validasi meliputi parent read/write, probe unik + cleanup, overlap root aktif/pending,
  binary/task/application storage, reserved/device/short-name/alternate-stream path
  Windows, reparse/junction ancestor dan descendant CDN, serta file secret yang dikenal.
  Directory enabled yang belum ada dibuat saat Save/startup; GET tidak membuat directory.
  Save tidak memindahkan/menghapus payload. Kegagalan persistence tidak mengganti snapshot.
- Registry Validate/Save perubahan root atau enable kembali memeriksa blob metadata,
  ukuran/hash target dan menolak upload metadata yang masih ada. Startup managed aktif
  memeriksa blob lagi. Missing/corrupt blob tidak diterima sebagai migrasi aman.
- WPF mendapat card Settings di kedua manager, termasuk saat disabled: draft toggle,
  server directory, limit CDN, Validate, Save, refresh independen, konfirmasi directory,
  konfirmasi draft saat reload/menutup panel/navigasi, retry, dan pesan konflik.
  Detail/path dibatasi claim, disembunyikan ketika sesi berganti/otorisasi ditolak.
  Card informasi ukuran juga mendapat refresh independen; home menampilkan status
  disabled/pending dan ukuran unavailable bila request gagal. MAUI tidak mendapat layar.
- Panduan: [engine-storage-settings.md](../engine-storage-settings.md), ditautkan dari
  panduan CDN/registry dan README backend.

## Verifikasi aktual

| Pemeriksaan | Hasil |
| --- | --- |
| Backend, WPF, MAUI solution build | Lulus, masing-masing 0 warning/error |
| `git diff --check` | Lulus |
| `dotnet run --project scripts/storage-settings-smoke -- --sql` | 98 pemeriksaan lulus |
| `dotnet run --project scripts/storage-settings-render` | 8 pemeriksaan render/transisi lulus |
| `dotnet run --project scripts/cdn-storage-smoke` | 7 pemeriksaan regresi ukuran CDN lulus |

Smoke memakai directory temp unik dan database SQL Server `EmSettingsTest_<GUID>`
yang dibuat/dihapus oleh pengujian. Model EF membuat skema fixture; bukan skema atau
payload aplikasi lokal. Host fixture menggunakan dispatcher `EmApp.ProcessRequest`
dan pemetaan CDN/OCI engine lewat Kestrel loopback dengan port sementara. Admin seeding
dan inisialisasi durable business task host tidak dijalankan. Token bearer user
diterbitkan layanan token engine; claim diperiksa dari tabel fixture, bukan mock.

98 pemeriksaan mencakup defaults, reload/restart, dokumen invalid/version, persistence
gagal, konflik revision dalam/silang proses, dua writer SQL bersamaan (satu commit,
satu 409), preservasi bagian fitur lain, pembatalan pending, path relatif/invalid/
overlap/alias Windows/secret/junction, cleanup probe, blob target hilang/korup,
mode statis, API 401/403 dan status umum tanpa path, serta Settings saat disabled.

HTTP juga menguji enable setelah restart, download CDN + Range 206, batas upload
baru 413 tanpa file tersisa, Save disable ketika streaming upload masih berjalan
(upload tetap berhasil), disable setelah restart tanpa menghapus file, login robot OCI,
push/pull blob, grant root, target registry tidak lengkap ditolak, serta token/grant/blob
tetap bekerja setelah restart. Ini uji HTTP OCI, bukan Docker CLI atau manifest image penuh.

Render tanpa window tampil memeriksa card CDN/registry pada LightTheme/DarkTheme,
input enabled/disabled dan transisi busy cepat ke enabled. Pixel background input
gelap tetap BGRA `24,20,17,255`; terang berbeda maksimal satu tingkat akibat alpha
rounding (`254,249,250,255` ke `253,249,250,255`). PNG terang/gelap kondisi busy
diperiksa secara visual. Template input/toggle dan tombol mempertahankan tema.

## Batas dan verifikasi tertunda

- Docker CLI sungguhan, manifest image push/pull lengkap, multi-layer image, dan
  migrasi root registry dengan salinan lengkap pada ukuran produksi belum diuji.
- WPF dengan mouse/keyboard terhadap server nyata: konfirmasi, conflict/retry,
  user terbatas, panel disabled/pending dan penutupan tab/window masih perlu diuji;
  render card bukan bukti interaksi manager end-to-end.
- Directory dengan ACL read-only nyata, cleanup probe yang sengaja digagalkan,
  koneksi database putus saat commit, serta provider MySQL/PostgreSQL belum diuji.
  Persistence gagal disimulasikan pada fixture; SQL concurrent writer diuji nyata.
- Startup Program.cs lengkap (seed admin, cache task, approval, Em.Test) dan task archive
  yang sedang berjalan ketika Save belum diuji ulang. Streaming upload diuji nyata.
- Pemeriksaan hash seluruh blob/tree CDN bisa mahal. Filesystem dan metadata masih
  bisa berubah setelah Save; startup memvalidasi lagi. Tidak menjanjikan resume upload.
  Tolak semua row upload ketika mengganti root/enable kembali; operator perlu
  menyelesaikan/cancel/cleanup melalui prosedur registry, tidak dibersihkan otomatis.
- Key konfigurasi harus stabil lintas deployment. Hostname/content root berubah
  berarti key baru; salin konfigurasi secara manual dengan pemeriksaan path.
  Bukan sinkronisasi multi-instance. Data payload tetap perlu volume/backup terpisah.

Tidak ada tindakan ditolak policy dan tidak ada skrip PowerShell manual wajib.
Tidak ada restart server produksi, copy/move/penghapusan storage aktif, atau perubahan
claim user aplikasi lokal yang dilakukan. Operator menerapkan pengaturan melalui UI
berhak lalu melakukan restart API normal saat siap.
