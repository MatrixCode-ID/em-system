# Pengaturan storage CDN dan container registry

Host contoh cukup memanggil `builder.AddManagedStorageSettings()` di `Program.cs`.
Default pertama: CDN aktif di `./data/cdn`, batas upload 200 MB, registry aktif di
`./data/container-registry`. Nilai tersimpan dibaca sebelum store/middleware dibentuk.
Save tidak mengganti snapshot aktif, membatalkan task, atau memindahkan data; perubahan
berlaku setelah operator melakukan shutdown/restart API secara normal.

## Persistence di ta_Meta

Sesuai arahan pengguna saat eksekusi, konfigurasi memakai satu dokumen JSON di
`ta_Meta.cMetaValue`, bukan file runtime. `cMetaKey` berbentuk
`Em.StorageSettings:<SHA256(machine name + content root + hostId)>`.
Default `hostId` adalah `default`. Overload builder menerima ID dan defaults eksplisit
untuk host lain. Key membedakan host yang berbagi DB; ini tidak menyinkronkan node.
Machine name/content root/ID harus stabil saat deployment. Jika berubah, operator harus
menyalin row konfigurasi ke key baru setelah memastikan path sesuai host baru.
Container memerlukan hostname stabil dan volume payload persisten yang writable bagi
akun API. Permission filesystem tetap divalidasi oleh akun proses API.

Dokumen berisi `Version=1`, `Revision`, `Cdn` dan `Registry`. Bagian fitur berisi
`Enabled`, `Directory`, dan `MaxUploadMb` (hanya berlaku pada CDN). Tidak ada secret.
Defaults dipakai ketika row belum ada; Save pertama membuat row. Save memakai transaksi
serializable dan expected revision. CDN dan registry berbagi revision, sehingga Save
bersamaan dapat menghasilkan 409; refresh lalu terapkan ulang draft. Update satu fitur
mempertahankan fitur lain. Perubahan aktif tidak ikut berubah ketika row direfresh.
Row invalid/versi unsupported menggagalkan startup; pulihkan row dari backup DB atau
koreksi JSON, kemudian restart. Jangan hapus row untuk reset tanpa mencatat path lama.
Tidak diperlukan DDL baru karena `ta_Meta` sudah ada. Siapkan skema inti dan registry
(`doc/sqlscript/mssql/sets/Ctn.sql`, termasuk migrasi robot bila skema lama) sebelum startup
managed, termasuk ketika registry dinonaktifkan. Startup tetap memeriksa tabel registry.

## Hak dan layar WPF

Manager/status umum memakai claim lama: `CDN Manager Access` atau
`Container Manager Access`. Action detail, Validate, dan Save memakai claim baru
`CDN Settings Manage` atau `Container Registry Settings Manage` dalam module
`Administrative Tools`. Claim terdaftar melalui atribut action engine; berikan lewat
User/Role Manager. Hak lama tidak otomatis mendapat hak konfigurasi. Administrator
mengikuti mekanisme claim administrator yang ada. Status umum tidak memuat path server.

Card Settings ada di kedua manager WPF dan tetap tampil ketika layanan nonaktif.
Refresh kecil memperbarui status/config card; reload draft meminta konfirmasi.
Toggle menyunting draft saja. Save/Validate mencegah request ganda, konflik ditampilkan
dan draft dipertahankan sampai pengguna refresh secara eksplisit. Menutup panel atau
meninggalkan navigasi memberi konfirmasi untuk draft belum tersimpan.
Area isi menggunakan status aktif API, bukan toggle draft. Home menampilkan disabled
dan pending restart; kegagalan membaca ukuran ditampilkan unavailable.
MAUI tidak mendapat manager baru.

## Directory dan integritas

Path adalah filesystem API, relatif terhadap `ContentRootPath`; bukan folder client.
GET status/detail tidak membuat folder. Validate memeriksa parent yang sudah ada dengan
probe unik baca/tulis, lalu membersihkannya; kegagalan cleanup ditampilkan. Save enabled
membuat directory yang diperlukan secara eksplisit. Directory disabled boleh kosong;
path nonkosong tetap diperiksa. Save tidak menyalin/memindahkan/menghapus payload.

Path tidak boleh melalui symlink/reparse point; tree CDN juga diperiksa agar tidak
berisi link ke storage/config lain atau file secret yang dikenal (`emapi-config.json`, `em.local.json`,
`secrets.local.json`, `.env*`, `.git`). Path Windows ambigu (trailing dot/space,
device/reserved names, alternate stream, short-name alias) ditolak. CDN dan registry tidak boleh overlap
(ancestor/descendant), termasuk dengan root fitur lain yang masih aktif sampai restart,
binary approval dan cache task. Directory aplikasi hanya mengizinkan payload di bawah
`data`; binaries/configuration/application root ditolak. Host lain harus memakai root
dedikasi dan tidak menaruh secret dalam directory payload publik CDN.

Validate/Save perubahan root atau enable kembali registry memeriksa seluruh blob metadata (termasuk blob retained/orphan),
ukuran dan SHA256 file target. Target tidak lengkap ditolak. Row upload yang masih ada
menolak perubahan root; selesaikan/batalkan dan bersihkan upload lewat prosedur registry
operator dahulu. Startup mengulangi pemeriksaan blob ketika registry managed aktif,
termasuk startup setelah perpindahan. File uploads tidak dijanjikan dapat di-resume.
Pemeriksaan Save bukan snapshot filesystem/DB sepanjang masa: concurrent push dan
perubahan manual masih mungkin setelah pemeriksaan; startup memeriksa ulang. Pemeriksaan
hash seluruh metadata bisa mahal pada registry besar. Metadata manifest berada di DB.

Mode statis `EnableCdn`/`AddContainerRegistry` tetap tersedia bagi host library lama.
Status menyatakan Managed=false; UI tidak menawarkan Save dan API menolaknya dengan 409.
Menggabungkan managed dan statis dalam host yang sama ditolak saat registrasi.

## Verifikasi terisolasi

`dotnet run --project ..\.artefacts\em-system\scripts\storage-settings-smoke\StorageSettingsSmoke.csproj`
memakai directory temp unik dan persistence fixture tanpa menyentuh payload/database host.
Tambahkan `-- --sql` untuk membuat database SQL Server fixture unik melalui koneksi lokal,
menguji `ta_Meta`, lalu menghapus hanya database fixture yang dibuat pengujian.
SQL fixture membuat tabel dari model EF dalam database baru, lalu menguji persistence,
HTTP dengan token user sungguhan, claim manager/settings, restart, CDN Range/upload,
streaming upload selama Save pending, serta login/push/pull blob OCI dan grant robot.
Fixture memakai dispatcher dan pemetaan publik engine, bukan Program.cs host produksi;
inisialisasi admin/business task host tidak dijalankan. Ini bukan uji Docker CLI.
`dotnet run --project ..\.artefacts\em-system\scripts\storage-settings-render\StorageSettingsRender.csproj`
merender card enabled/disabled dan transisi busy pada kedua tema tanpa window tampil;
PNG disimpan pada output build script. Interaksi mouse terhadap server perlu uji operator.
