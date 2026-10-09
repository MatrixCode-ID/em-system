# SMTP Manager: banyak konfigurasi dan UI tabular

- **Tanggal:** 2026-10-09
- **Status:** jadi plan
- **Plan turunan:** [smtp-multi-profile.codex.md](../../plan/executed/smtp-multi-profile.codex.md)

Catatan ini merekam permintaan pengguna dan usulan desain untuk dibahas.
Pada diskusi awal belum ada permintaan eksekusi. Permintaan berikutnya membuat
skema `ta_Smtp` dicatat pada pembaruan di akhir; integrasi service/UI belum diminta.

## Keputusan yang sudah jelas

- SMTP Manager harus bisa menambah konfigurasi SMTP meskipun sudah ada satu
  SMTP default. Default menentukan pilihan pengiriman, bukan batas jumlah SMTP.
- Pengiriman tanpa menyebut nama SMTP memakai default. Bila tidak ada default,
  pengiriman tersebut menghasilkan error.
- Pengguna meminta UI tabular. Bila menggunakan card, kemampuan menambah SMTP
  baru tetap wajib tersedia.
- Pengguna meminta diskusi ulang sebelum implementasi.
- Pembaruan 2026-10-09: pengguna meminta tabel `ta_Smtp` untuk penyimpanan
  konfigurasi. Usulan koleksi konfigurasi di `ta_Meta` di bawah tidak dipakai.
- Pembaruan 2026-10-09: pengguna meminta agar action service yang sekarang
  dipakai tidak rusak saat integrasi multi-SMTP. Kompatibilitas menjadi syarat
  perubahan kontrak dan migrasi.

## Kondisi implementasi saat ini

- `SmtpSettingsStore` menyimpan satu dokumen konfigurasi di `ta_Meta`, key
  `Em.Smtp.Settings`, dengan password terenkripsi dan revision.
- `SmtpSettings` belum memiliki nama atau penanda default; `SmtpMessage` belum
  menyediakan pilihan SMTP. `SendAsync` selalu memakai konfigurasi tunggal.
- `SmtpManager.xaml` menampilkan satu form dalam card, toolbar Save dan test,
  tanpa daftar, aksi tambah, atau pemilihan konfigurasi.
- Perubahan memerlukan kontrak di `Em.Libs`, backend, proxy UI bersama, layar
  WPF, pengujian yang terkait, dan pembaruan `doc/engine/engine-smtp.md`.

## Usulan perilaku pengiriman

- Setiap konfigurasi mempunyai ID tetap, nama unik, settings, password sendiri,
  serta status enabled. Detail aturan nama masih perlu ditetapkan saat desain
  dimatangkan.
- Maksimal satu default per database; keadaan tanpa default diperbolehkan.
- Nama tidak diberikan atau kosong: pilih default. Bila default tidak ada,
  error `No default SMTP is configured.` meskipun hanya ada satu konfigurasi.
- Nama diberikan: pilih SMTP tersebut, termasuk bila tidak ada default.
  Nama tidak ditemukan menghasilkan error, tanpa pindah ke default.
- SMTP terpilih disabled menghasilkan error. Tidak otomatis mencoba SMTP lain.
- Aksi Set default mengganti pilihan lama secara atomik. Keberadaan default
  tidak memblokir aksi New SMTP.
- Test connection dan Test email memakai konfigurasi yang dipilih di tabel,
  sehingga konfigurasi non-default juga bisa diuji. Seperti perilaku sekarang,
  connection test dapat berjalan saat disabled, sedangkan email test memerlukan
  enabled. Pengujian memakai settings yang sudah disimpan.
- Pemilihan nama bersifat opsional pada API pengiriman; pemanggilan lama tanpa
  nama tetap bisa dipakai setelah konfigurasi lama dikonversi menjadi default.
  Bentuk kontrak final belum diputuskan.

## Usulan UI

- Toolbar horizontal mengikuti UserManager dan resource MaterialDesign bersama:
  New SMTP sebagai aksi utama, Edit, Set default, lalu Test connection dan
  Test email. Delete dapat masuk More bila toolbar terlalu panjang.
- Tabel berisi kolom Name, Host, Port, Security, Sender, Enabled, dan Default.
  Password tidak ditampilkan. Default ditunjukkan sebagai badge pada baris.
- New SMTP selalu tersedia bagi pemilik hak manager ketika tidak busy,
  termasuk saat tabel kosong atau default sudah ada. Aksi terhadap baris
  memerlukan selection.
- Form tambah/edit berada dalam side sheet dengan Save/Cancel; card boleh
  dipakai sebagai wadah tabel atau editor, tanpa membatasi jumlah konfigurasi.
- Refresh daftar tersedia. Bila tanpa default, tampilkan informasi
  `No default SMTP. Sending without an SMTP name will fail.`
- Enabled dan Default adalah dua status terpisah.

## Usulan kompatibilitas dan penyimpanan

- Konfigurasi lama yang sudah disimpan dikonversi menjadi satu profil bernama
  `default`, ditandai default, dengan settings/password dan status enabled tetap.
  Database tanpa konfigurasi tersimpan memulai dengan daftar kosong.
- Penyimpanan dapat tetap memakai `ta_Meta` dengan format koleksi baru dan
  revision, serta key enkripsi terpisah. Ini usulan, belum keputusan bentuk
  penyimpanan. Konversi data harus melindungi kredensial dan concurrency.
- Tidak menambah fallback atau retry otomatis saat pengiriman gagal.

## Pertanyaan yang masih terbuka

1. Untuk konfigurasi baru, apakah default dipilih manual melalui Set default
   (usulan), atau SMTP pertama otomatis menjadi default?
2. Saat default dihapus atau dinonaktifkan, apakah default boleh dikosongkan
   dengan konsekuensi error untuk pengiriman tanpa nama (usulan: kosongkan
   saat dihapus; saat disabled tetap terpilih dan pengiriman error), atau wajib
   menunjuk pengganti terlebih dahulu?

## Tindak lanjut setelah keputusan

Setelah desain disepakati dan pengguna meminta plan/eksekusi, turunkan ke plan
di `plan/unexecuted/` sesuai aturan proyek. Catatan ini tetap dipertahankan dan
ditautkan ke plan tersebut. Belum ada perubahan kode maupun uji GUI dari diskusi
ini.

## Pembaruan 2026-10-09: tabel ta_Smtp

Permintaan berikutnya adalah membuat tabel `ta_Smtp`. Skrip skema disiapkan di
`doc/sqlscript/mssql/tables/050-smtp.sql`, bersama view tanpa ciphertext password
di `doc/sqlscript/mssql/views/vi_Smtp.sql`. Permintaan ini belum mengubah service
atau UI dan tidak dianggap sebagai keputusan atas dua pertanyaan default di atas.

- Satu baris per konfigurasi dengan `cSmtpId` ULID, nama unik case-insensitive,
  `cSmtpState` (0 disabled, 1 enabled), revision, note, settings SMTP dan stamps.
- `cSmtpDefault` memakai unique filtered index untuk membatasi maksimal satu
  default. Banyak baris non-default serta nol default tetap valid.
- `cSmtpPassword` berisi ciphertext AES-GCM; key tetap terpisah di
  `ta_Meta` (`Em.Smtp.Key`). View hanya mengungkap `cvSmtpHasPassword`.
- Tabel baru tidak mengisi default otomatis. Pemilihan default di UI/service
  dan perilaku penghapusan masih mengikuti pertanyaan diskusi di atas.
- Konfigurasi lama belum dipindah dari `Em.Smtp.Settings`; migrasi data harus
  dilakukan bersama perubahan backend supaya service lama tidak kehilangan data.
- Skrip idempotent dapat digunakan pada instalasi baru maupun database lama.
  Ini penambahan skema; belum diterapkan ke database aplikasi.

Verifikasi console pada database SQL Server sementara lulus: tabel/view dibuat,
beberapa SMTP tanpa default, penambahan saat default ada, penolakan default kedua
dan nama duplikat case-insensitive, constraint nilai, view tanpa ciphertext,
pergantian default dalam transaksi, penghapusan default, serta eksekusi ulang
yang mempertahankan data. Database pengujian sudah dihapus. Harness berada di
`../.artefacts/em-system/scripts/smtp-schema-smoke/`.

## Pembaruan 2026-10-09: kompatibilitas service lama

Permintaan pengguna: "usahakan action service yang sekarang tidak rusak".
Kontrak saat ini telah diperiksa di `ISmtpService`, `SmtpService`,
`SmtpClientService`, dan pemakaiannya di view model SMTP Manager.

### Kontrak yang wajib dipertahankan

Nama, HTTP verb, module `Administrative Tools`, jumlah/urutan/tipe parameter,
tipe hasil, struktur DTO lama, serta claim tetap dipertahankan:

| Member lama | Parameter | Hasil | Claim HTTP |
| --- | --- | --- | --- |
| `GetMeta_SmtpSettings` | tidak ada | `SmtpSettingsDetail` | `SMTP Manager Access` |
| `PostGetMeta_SmtpSettingsSave` | `SmtpSettingsSave request` | `SmtpSettingsDetail` | `SMTP Manager Access` |
| `PostGetMeta_SmtpTestConnection` | tidak ada | `SmtpConnectionResult` | `SMTP Manager Access` |
| `PostGetMeta_SmtpTestEmail` | `string recipient` | `SmtpSendResult` | `SMTP Manager Access` |
| `PostGetMeta_SmtpSend` | `SmtpMessage message` | `SmtpSendResult` | `SMTP Send` |
| `SendAsync` | `SmtpMessage message` | `SmtpSendResult` | bukan action HTTP |

Overload server `SendAsync(SmtpMessage, CancellationToken)` juga tetap tersedia
dan tidak diekspos sebagai action. Jangan menambah parameter wajib atau mengganti
signature lama dengan signature berparameter opsional: pemanggilan source mungkin
tetap berhasil, tetapi client/library yang sudah dikompilasi dapat rusak.

### Rancangan integrasi yang kompatibel

- Action lama menjadi adapter terhadap SMTP default dalam `ta_Smtp`. Pengiriman
  lama tanpa nama tetap melalui default. Ketiadaan default menghasilkan error
  sesuai permintaan awal, tanpa memilih SMTP pertama atau fallback ke data lama.
- Read/save lama tetap memakai DTO tunggal, bukan diganti daftar. Usulan: read
  tanpa default tetap mengembalikan settings disabled/kosong seperti awal saat
  belum dikonfigurasi; save tanpa default membuat profil default untuk menjaga
  alur konfigurasi client lama. Ini perilaku adapter khusus client lama, bukan
  aturan default otomatis untuk New SMTP pada manager baru. Detail ini belum
  diputuskan dan perlu dimatangkan saat integrasi.
- Daftar, CRUD profil, Set default, pengujian profil terpilih, serta pengiriman
  berdasarkan nama memakai action baru dengan nama berbeda. Hindari overload
  action HTTP bernama sama yang dapat mengacaukan discovery/routing.
- Usulan: kontrak tambahan dipisah ke interface baru agar implementasi pihak
  ketiga yang sudah mengimplementasikan `ISmtpService` tidak diwajibkan menambah
  member. `ISmtpService`, DTO lama dan proxy lama tetap tersedia.
- Claim pengiriman baru tetap `SMTP Send`; aksi manajemen/diagnostik tetap
  `SMTP Manager Access`. Parameter nama SMTP tidak boleh memerlukan hak manager
  untuk pengiriman aplikasi.
- Migrasi konfigurasi tunggal lama membuat satu profil default dengan settings,
  status enabled, dan ciphertext password tetap, menggunakan key lama. Migrasi
  bersifat atomik, idempotent dan selesai sebelum adapter membaca tabel baru.
  Jangan menghapus konfigurasi lama sebelum konversi berhasil atau menimpa profil
  yang sudah ada. Keberadaan data lama setelah migrasi bukan fallback pengiriman.
- Token `Revision`/`ExpectedRevision` lama tetap `long` dan tetap mendeteksi
  stale save. Revision per profil saja tidak cukup: client lama dapat membaca
  default A, lalu default diganti B dengan angka revision yang sama. Usulan
  adapter memakai token revision global yang meningkat saat default atau data
  profil berubah, dan memeriksanya dalam transaksi. Save stale harus 409,
  bukan menimpa default baru. Angka lama tidak boleh di-cast ke `int` tanpa
  pemeriksaan; `cSmtpRevision` pada tabel adalah revision per profil yang berbeda
  dari token kompatibilitas ini.
- Semantik password null = pertahankan, `ClearPassword` = hapus, kerahasiaan
  respons, validasi, cancellation, serta send dari scope tanpa HTTP tetap dijaga.

### Verifikasi wajib saat integrasi

- Jalankan test SMTP lama dan pastikan client tanpa perubahan masih dapat
  read/save, test connection, test email, dan send melalui URL/body lama.
- Periksa metadata kelima action lama beserta claim dan parameter; verifikasi
  signature serta field DTO tetap, termasuk `SendAsync` non-action.
- Uji migrasi dengan konfigurasi lama enabled/disabled, password tersimpan,
  revision lama, eksekusi ulang, serta rollback bila konversi gagal.
- Uji client lama yang menyimpan setelah perubahan default maupun edit melalui
  manager baru: wajib konflik 409. Uji tanpa default: send wajib error.
- Uji tambahan action baru tanpa menghilangkan pengujian action lama; test
  registrasi yang kini mengharapkan tepat lima action perlu memastikan kelima
  action lama tetap ada sekaligus memeriksa action tambahan.

Pembaruan ini mencatat persyaratan dan rancangan kompatibilitas. Tidak ada
perubahan source service, interface, proxy, DTO, atau UI pada tahap ini;
kompatibilitas setelah integrasi belum diklaim sudah teruji.

## Pembaruan 2026-10-09: eksekusi

Pengguna meminta "jalankan". Implementasi backend, migrasi, proxy dan UI tabular
sudah dibuat. Asumsi eksekusi yang disampaikan: default dipilih manual, delete
default diperbolehkan, disabled tetap default. Adapter legacy save tanpa default
membuat default baru agar client lama tetap dapat mengonfigurasi SMTP. Token
global long dipakai bersama, termasuk sesudah default berubah.

Build backend/WPF/MAUI dan 230 test console lulus; skema diterapkan ke dua target
database lokal yang tercatat di artefak privat. Target remote belum dimigrasikan
karena kredensial tidak tersedia. Uji GUI dan SMTP eksternal belum dilakukan.
Detail hasil dan keterbatasan ada dalam plan executed yang ditautkan di atas.

Tindak lanjut pada tanggal yang sama: target database aktif yang belum menerima
skema menyebabkan error SQL 208. Konfigurasi lokal menyediakan koneksi untuk
memperbaikinya. Sesuai permintaan pengguna, skema SMTP kini disinkronkan pada
tiga target `OSHA_CSM`; struktur identik dan action list/adapter read lulus.
Laporan: [smtp-sync-database.codex.md](../../plan/executed/smtp-sync-database.codex.md).
