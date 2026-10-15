# TODO List — yang sudah selesai

Pindahan dari [TODO-LIST.md](TODO-LIST.md). Tanpa nomor: nomor di daftar aktif digeser setiap ada
yang selesai, jadi nomor lama di sini akan menyesatkan. Urutannya dari yang paling baru selesai.

Riwayat sebelum daftar ini dipindah ke repo engine (2026-10-15) tidak disalin ke sini; yang tercatat
di bawah adalah item yang ditemukan sudah selesai saat pemindahan itu.

---

- **Sambungkan `ta_Log` sebagai jejak audit** — selesai 2026-10-12 lewat log engine
  (`plan/executed/penyimpanan-log.md`). `ta_Log` lama digantikan log engine di `Em.Api.Core/Api/Log/`:
  telemetry (`EmLogProvider`, `LogQueue`, `LogWriterBackground`) dan audit (`AuditCatalog`,
  `IAuditWriter`/`AuditWriter`, `AuditSaveInterceptor`), dengan retensi (`LogRetention`), store
  terpisah, layar WPF Log Manager, dan tab Activity User Editor. Dua kebutuhan yang dulu menunggu:
  penyamaran lewat token debug kini tercatat sebagai audit `System.Auth` `debug.impersonate`, dan baris
  data yang ditulis saat impersonasi membawa akun yang disamarkan. Penolakan action karena hak kurang
  tidak punya entri audit sendiri. Verifikasi: 269 test backend (SQL Server, PostgreSQL, MySQL) dan
  smoke HTTP lulus; layar WPF dan hook MAUI belum diuji. Panduan:
  [doc/engine/engine-log.md](engine/engine-log.md).

- **Layanan email engine** — selesai 2026-10-09 sebagai SMTP engine, bukan `IEmailSender`:
  `ISmtpService` di `Em.Libs`, `SmtpService` (MailKit) di `Em.Api.Core`, proxy client WPF/MAUI,
  dinyalakan host lewat `builder.AddSmtp()`. Banyak profil SMTP di `ta_Smtp` dengan satu default,
  password terenkripsi AES-GCM, SMTP Manager WPF, dan claim `SMTP Send` untuk pengiriman lewat HTTP;
  business task module bisa memanggil `SendAsync` langsung dari scope API. Pengirim (`FromAddress`)
  diisi module pengonsumsi. Verifikasi: build backend/WPF/MAUI dan test console SMTP lulus; display
  nama profil diuji pengguna di GUI; pengiriman lewat server SMTP eksternal belum diuji. Panduan:
  [doc/engine/engine-smtp.md](engine/engine-smtp.md).

- **Keluarkan connection string dari kode (bagian engine)** — selesai 2026-10-04. Host `Em.Api` tidak
  lagi memuat connection string di kode: konfigurasi dibaca dari `emapi-config.json` di luar repo
  (`EmApiConfig`, `Helper.ApplyConfig`), environment variable `EM_DB_CONNECTION_STRING` dan kawan-kawan
  mengesampingkannya, dan container memakai Compose secret. Connection string milik host produk
  adalah pekerjaan produk dan tetap tercatat di daftar repo produknya.

- **Module contoh teladan yang utuh (bagian engine)** — selesai 2026-10-02 lewat module uji
  `Em.Test` (`src/modules/Em.Test`, panduan [doc/engine/engine-test-module.md](engine/engine-test-module.md)):
  view model, service client, daftar ber-paging, `DbContext` milik module, dan action ber-`claim:`
  eksplisit (`ITestServices.EditItemsClaim`). Membenahi module produk agar mengikuti pola ini adalah
  pekerjaan produk dan tetap tercatat di daftar repo produknya.
