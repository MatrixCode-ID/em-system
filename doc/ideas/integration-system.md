# Integration system di engine

- Tanggal: 2026-11-05
- Status: diskusi
- Latar: pembahasan absensi dan jadwal kerja ([tenant-attendance-schedule.md](tenant-attendance-schedule.md)).
  Pengguna meminta engine punya **integration system**, agar kebutuhan seperti **sync data mesin absensi**,
  **kirim pesan WhatsApp**, dan **integrasi API payment gateway** bisa masuk sistem.
- Nama tabel, class dan method di bawah masih **nama sementara**.

## Keputusan yang sudah jelas

- 2026-11-05 (pengguna): engine **harus punya integration system** untuk menghubungkan sistem dengan pihak luar
  (perangkat, layanan pesan, payment gateway, dan sejenisnya).
- 2026-11-05 (pengguna): **engine menyediakan interface, dan pengguna library yang mengimplementasikannya.**
  Engine tidak menulis connector untuk layanan tertentu. Menjawab pertanyaan no. 1.
- 2026-11-05 (pengguna): **lingkup integrasi bisa server atau UI.** Sebuah integrasi berjalan di server API atau
  di aplikasi client (WPF/MAUI). Lihat "Lingkup server dan UI".
- 2026-11-05 (pengguna): ada **manager** yang bisa **melihat log**, **quick run** (menjalankan integrasi saat
  itu juga), dan **menonaktifkan** integrasi. Pengguna menyebut "disable engine"; dicatat sebagai menonaktifkan
  integrasi tertentu. Perlu dikonfirmasi apakah juga ada saklar mematikan seluruh integration system
  (pertanyaan no. 10).
- 2026-11-05 (pengguna): **Integration Manager hanya bisa dilihat admin** (user dengan switch admin), tanpa claim
  terpisah. Menjawab pertanyaan no. 9.
- 2026-11-05 (pengguna): **integrasi lingkup UI dipakai oleh modul wrapper.** Integrasi UI bukan proses latar
  yang berjalan sendiri, tetapi kemampuan yang dipanggil layar wrapper. Contoh pengguna: **koneksi ke perangkat
  USB dan membaca datanya**.
- 2026-11-05 (pengguna): **background task berbeda dari integrasi.** Engine harus punya engine task scheduler
  sendiri: [task-scheduler.md](task-scheduler.md). Integrasi yang perlu berjalan berkala dipanggil oleh task
  terjadwal; integrasi tidak membawa jadwal sendiri.
- 2026-11-05 (pengguna): **integration system tidak terkait BMM, jadi tidak mengenal istilah tenant.** Profil
  koneksi dan variabel tidak punya tingkat per tenant. Bila sebuah aplikasi butuh pengaturan per tenant atau
  yang lebih kompleks, pengguna library membuat UI khusus di wrapper-nya sendiri. Pertanyaan no. 2 gugur.
- 2026-11-05 (pengguna): **variabel disimpan dalam profil di manager**, dan **kode memanggil profil yang
  dibutuhkan**. Lihat "Profil variabel". Menjawab pertanyaan no. 18.
- 2026-11-05 (pengguna): **manager bisa men-setup variabel yang dibutuhkan integrasi.** Implementasi menyatakan
  variabel apa saja yang ia butuhkan; admin mengisinya di Integration Manager. Lihat "Variabel integrasi".

## Gagasan dasar

Setiap integrasi dengan pihak luar selalu berisi bagian yang sama: konfigurasi koneksi dan rahasianya, antrean
kirim dengan retry, penerimaan event dari luar, jadwal sync, pemetaan id luar ↔ id sistem, serta log untuk
melacak masalah. Tanpa engine, setiap modul menulis ulang bagian itu dan hasilnya tidak seragam.

Usulan: engine menyediakan **kerangka integrasi**; yang spesifik untuk satu layanan ditulis sebagai
**connector**.

| Engine (kerangka) | Connector | Modul pemakai |
| --- | --- | --- |
| Registrasi connector, profil koneksi, penyimpanan rahasia | Cara bicara dengan satu layanan (format request, autentikasi, verifikasi signature) | Kapan dan apa yang dikirim/diterima, arti datanya bagi bisnis |
| Antrean keluar (outbox), retry, idempotensi | Menerjemahkan pesan/event ke/dari bentuk umum | Handler event masuk (mis. pembayaran lunas → dokumen lunas) |
| Penerimaan event masuk (webhook/inbox), dedup | | |
| Jadwal sync dan penanda posisi sync | | |
| Pemetaan id luar ↔ id sistem | | |
| Log panggilan, layar Integration Manager | | |

## Tiga arah integrasi

1. **Keluar (outbound)**: sistem memanggil layanan luar, mis. kirim pesan WhatsApp, membuat tagihan di payment
   gateway.
2. **Masuk (inbound/webhook)**: layanan luar memanggil sistem, mis. callback status pembayaran, status pesan
   terkirim/dibaca, balasan pesan.
3. **Tarik terjadwal (polling/sync)**: sistem mengambil data secara berkala, mis. log tap dari mesin absensi.

## Komponen (usulan, belum final)

### 1. Connector (interface diimplementasikan pengguna library)

- Engine hanya menyediakan interface dan kerangkanya; pengguna library menulis implementasinya.
- Didaftarkan di kode lewat builder, mis. `builder.AddConnector<WhatsAppCloudConnector>("whatsapp-cloud")`,
  di host server atau host client sesuai lingkupnya.
- Connector menyatakan kemampuannya (kirim, terima webhook, sync), daftar setting yang dibutuhkan (untuk form
  di UI), dan setting mana yang rahasia.
- Connector tidak menulis data bisnis; ia hanya menerjemahkan dan berkomunikasi.

### Lingkup server dan UI (keputusan 2026-11-05, rincian usulan)

Sketsa interface (nama sementara):

- `IIntegration`: kunci, judul, lingkup (`Server` atau `Ui`), kemampuan (kirim, terima webhook, sync), daftar
  setting.
- `IServerIntegration : IIntegration`: dijalankan worker di server API. Cocok untuk outbound, webhook, dan
  sync ke layanan yang bisa dijangkau server.
- `IUiIntegration : IIntegration`: dijalankan di aplikasi client (WPF/MAUI) yang sedang terbuka. Cocok untuk
  perangkat yang hanya terjangkau dari PC/jaringan user, mis. mesin absensi di LAN pabrik, perangkat USB/serial,
  printer atau scanner lokal. **Dipakai oleh modul wrapper** (keputusan 2026-11-05): layar wrapper memanggil
  integrasi lewat engine, mis. "hubungkan perangkat", "baca data", "putuskan". Engine mencatat log, menegakkan
  status nonaktif, dan menyediakan konteks; wrapper yang menentukan kapan dipanggil dan apa yang dilakukan
  dengan datanya.
- Keduanya menerima konteks dari engine: setting profil, log writer, penanda posisi sync, pemetaan id, dan akses
  ke outbox. Hasil dan log dari integrasi UI dikirim ke server, agar manager tetap melihat semuanya di satu
  tempat.

Contoh lingkup UI: perangkat USB (usulan alur)

1. Modul wrapper mendaftarkan `UsbReaderIntegration : IUiIntegration` di host client.
2. User membuka layar wrapper (mis. layar penimbangan atau download data perangkat) dan menekan **Connect**.
   Layar memanggil engine dengan kunci integrasi; engine menolak bila integrasi nonaktif, lalu memberi konteks
   ke implementasi.
3. Implementasi membuka port USB/serial, membaca data, dan mengembalikan hasil ke layar wrapper.
4. Engine mencatat log (waktu, PC, user, hasil, error) dan mengirimnya ke server.
5. Wrapper menyimpan data hasil baca lewat action servernya sendiri, dengan otorisasi wrapper.

Akibat lingkup UI:

- Integrasi UI hanya berjalan saat aplikasi client terbuka dan user sudah login, dan dipanggil oleh layar wrapper.
  Jadwal latar di client tidak menjadi bagian lingkup UI (usulan; pertanyaan no. 15). Background task ditangani
  [task scheduler](task-scheduler.md), bukan integrasi.
- Akses ke perangkat bergantung pada platform: WPF bisa membuka port serial/USB, sedangkan MAUI Android butuh
  izin USB host dan API Android sendiri. Implementasi per platform menjadi urusan pengguna library.
- Bila beberapa client memasang integrasi yang sama, perlu dijaga agar tidak berjalan bersamaan dan
  menggandakan data (pertanyaan no. 11).
- Rahasia tidak pernah dikirim ke client (lihat profil koneksi), sehingga integrasi UI yang butuh rahasia perlu
  aturan tersendiri (pertanyaan no. 12).
- Lingkup UI bisa menggantikan agent lokal untuk sebagian kasus (lihat bagian 8).

### Variabel integrasi (keputusan 2026-11-05, rincian usulan)

Implementasi integrasi **mendeklarasikan** variabel yang dibutuhkannya; engine menyimpan nilainya, Integration
Manager menampilkan form untuk mengisinya, dan engine memberikan nilainya ke implementasi lewat konteks.

Usulan isi deklarasi per variabel:

| Isi | Contoh |
| --- | --- |
| Kunci | `ApiBaseUrl`, `ComPort`, `BaudRate`, `ApiKey` |
| Label dan help | "Port serial perangkat", "Kosong = deteksi otomatis" |
| Tipe | teks, angka, bool, pilihan (enum), URL, rahasia |
| Wajib atau opsional, nilai bawaan | `BaudRate` bawaan 9600 |
| Validasi sederhana | rentang angka, pola teks |
| Tingkat berlaku | global atau per PC (untuk lingkup UI); tanpa tingkat tenant |

Perilaku:

- Form di manager dibangun dari deklarasi, jadi pengguna library tidak perlu membuat layar setup sendiri.
- Integrasi dengan variabel wajib yang belum diisi tidak bisa dijalankan (termasuk quick run) dan ditandai di
  manager.
- Variabel bertipe **rahasia** disimpan terenkripsi, ditampilkan tersamar, dan hanya bisa diganti, tidak dibaca
  ulang.
- Perubahan variabel tercatat di audit engine.
- Variabel yang ada di data tetapi sudah tidak dideklarasikan implementasi (mis. setelah update) ditampilkan
  sebagai usang, tidak dihapus otomatis.
- Untuk lingkup UI, variabel seperti port COM berbeda per PC, sehingga perlu tingkat **per PC** (pertanyaan
  no. 17).

Variabel ini menjadi isi setting pada profil di bawah.

### 2. Profil variabel (`ta_IntegrationProfile`, keputusan 2026-11-05)

- **Nilai variabel dikelompokkan dalam profil yang dibuat admin di manager.** Satu integrasi bisa punya beberapa
  profil, mis. dua nomor WhatsApp (`wa-sales`, `wa-finance`), atau payment gateway `sandbox` dan `production`.
- **Kode memanggil profil yang dibutuhkan dengan namanya**, mis. `integration.Profile("wa-sales")` (nama method
  sementara). Usulan: ada satu profil default, dan pemanggilan tanpa nama memakai default, seperti profil SMTP.
- Memanggil profil yang tidak ada, nonaktif, atau variabel wajibnya belum lengkap menghasilkan error yang jelas
  (menyebut integrasi dan nama profil), bukan diam-diam memakai profil lain (usulan).
- Setiap profil diisi lewat form yang sama, dibangun dari deklarasi variabel integrasi.
- Kolom: integrasi, kode/nama profil (unik per integrasi), tanda default, nilai variabel (JSON), state, catatan.
- Profil bisa dinonaktifkan sendiri-sendiri, terpisah dari menonaktifkan integrasinya.
- Menjawab pertanyaan no. 18: variabel melekat pada profil.
- **Rahasia** (token, API key, secret signature) disimpan terenkripsi, tidak pernah dikembalikan ke client;
  UI hanya bisa mengganti nilainya.

### 3. Antrean keluar (`ta_IntegrationOutbox`)

- Modul tidak memanggil layanan luar langsung, tetapi **menulis pesan ke outbox di transaksi yang sama** dengan
  data bisnisnya. Worker engine mengirimnya. Bila transaksi bisnis gagal, pesan ikut batal; bila layanan luar
  sedang mati, data bisnis tetap tersimpan.
- Kolom: profil koneksi, jenis pesan, payload, kunci idempotensi, state (antre, dikirim, gagal, dibatalkan),
  jumlah percobaan, jadwal percobaan berikutnya (backoff), respons terakhir, referensi ke data sumber.
- Pola ini sudah dipakai engine untuk audit outbox.

### 4. Kotak masuk (`ta_IntegrationInbox`)

- Webhook diterima lewat endpoint publik engine (`AddPublicEndpoint`), mis. `/hook/{kode-koneksi}`.
- Urutan: verifikasi signature oleh connector → **simpan mentah lebih dulu** → balas cepat ke pengirim →
  proses oleh handler modul di belakang.
- Dedup dengan id event dari luar, karena layanan luar biasa mengirim ulang event yang sama.
- Event yang gagal diproses tetap tersimpan dan bisa diproses ulang dari UI.

### 5. Jadwal sync

*Diganti keputusan 2026-11-05 (pengguna): background task terpisah dari integrasi dan dijalankan
[task scheduler](task-scheduler.md) engine. Rancangan `ta_IntegrationJob` di bawah tidak dipakai.*

- ~~Jadwal per profil koneksi (interval atau cron), state, waktu jalan terakhir, hasil terakhir.~~ Sync
  berkala = task terjadwal yang memanggil integrasi.
- **Penanda posisi** (cursor/watermark) tetap dibutuhkan agar sync berikutnya hanya mengambil data baru. Apakah
  disimpan di integrasi (per profil koneksi) atau di task scheduler masih perlu diputuskan (pertanyaan no. 20).

### 6. Pemetaan id (`ta_IntegrationMap`)

- Menautkan id di sistem luar dengan id sistem: PIN/kartu di mesin absensi ↔ employee, nomor tagihan payment
  gateway ↔ dokumen, nomor WhatsApp ↔ contact.
- Kolom: profil koneksi, jenis objek, id luar, id sistem, state.
- Menjawab sebagian pertanyaan identitas mesin absensi di ide absensi.

### 7. Log panggilan

- Setiap request/response (keluar maupun masuk) dicatat ringkas: waktu, durasi, status, potongan isi dengan
  rahasia disamarkan. Retensi mengikuti engine log.

### 8. Agent lokal untuk perangkat di jaringan internal

- Mesin absensi biasanya ada di LAN pabrik, sedangkan server bisa berada di luar dan tidak bisa menjangkaunya.
- Usulan: **agent kecil** dipasang di jaringan lokal, menarik data dari perangkat, lalu mengirimnya ke API
  sistem memakai identitas **robot** engine (token robot). Dari sisi engine, data agent masuk seperti inbound.

### 9. Integration Manager (UI)

Fungsi yang diputuskan pengguna 2026-11-05 (manager **hanya untuk admin**):

- **Lihat log** setiap integrasi, baik lingkup server maupun UI.
- **Quick run**: menjalankan integrasi saat itu juga di luar jadwal. Integrasi server dijalankan di server;
  integrasi UI dijalankan di client tempat manager dibuka (usulan; pertanyaan no. 13).
- **Setup variabel** yang dideklarasikan integrasi, lewat form yang dibangun dari deklarasinya (lihat "Variabel
  integrasi").
- **Disable**: menonaktifkan integrasi tanpa mencabut kodenya. Integrasi nonaktif tidak dijalankan jadwal,
  tidak memproses outbox, dan menolak webhook masuk (usulan perilaku; webhook ditolak atau disimpan saja
  masih perlu diputuskan, pertanyaan no. 14).

Fungsi tambahan yang diusulkan agent: alat admin di Tools, daftar connector terpasang
beserta lingkupnya, profil koneksi (termasuk tes koneksi), jadwal sync, antrean keluar (lihat, kirim ulang,
batalkan), dan kotak masuk (lihat, proses ulang).

## Contoh alur

**Sync mesin absensi**

1. Agent lokal membaca log dari mesin setiap 5 menit, mulai dari penanda posisi terakhir.
2. Agent mengirim log ke API dengan token robot.
3. Engine memetakan PIN mesin ke employee lewat pemetaan id, lalu menulis log kehadiran (sumber: perangkat).
   PIN yang belum dipetakan disimpan di kotak masuk sebagai gagal, untuk dipetakan admin lalu diproses ulang.

**Kirim pesan WhatsApp**

1. Modul menyimpan dokumen dan, dalam transaksi yang sama, menulis pesan "dokumen menunggu persetujuan" ke
   outbox untuk profil WhatsApp default.
2. Worker mengirim lewat connector. Bila gagal, dicoba ulang dengan jeda bertambah.
3. Webhook status (terkirim/dibaca) masuk ke kotak masuk dan memperbarui state pesan.

**Payment gateway**

1. Modul membuat tagihan lewat outbox, dengan kunci idempotensi dari id dokumen agar tagihan tidak terbuat dua
   kali.
2. Pelanggan membayar; payment gateway mengirim callback.
3. Connector memverifikasi signature; engine menyimpan event dan mencegah duplikat.
4. Handler modul menandai dokumen lunas. Event yang sama datang dua kali tidak menghasilkan dua pelunasan.

## Hubungan dengan fitur engine yang ada

- **SMTP Manager** sudah punya profil berganda + default. Perlu diputuskan apakah email nantinya menjadi connector
  di kerangka ini atau tetap terpisah.
- **Endpoint publik** (`AddPublicEndpoint`) menjadi jalur webhook.
- **Robot** menjadi identitas agent lokal dan sistem luar yang memanggil API.
- **Audit outbox** menjadi contoh pola outbox.
- **Engine approval** bisa memakai outbox untuk notifikasi (mis. WhatsApp) tanpa bergantung pada connector
  tertentu.

## Pertanyaan terbuka

1. ~~Connector spesifik layanan ditulis di engine, sebagai paket terpisah, atau oleh pemakai engine?~~
   Dijawab 2026-11-05: engine menyediakan interface, pengguna library mengimplementasikan. Sisa pertanyaan:
   apakah engine tetap menyediakan satu contoh/connector HTTP-webhook generik?
2. ~~Profil koneksi per tenant, global, atau keduanya?~~ Gugur 2026-11-05: engine tidak mengenal tenant.
3. Email (SMTP Manager) dipindah menjadi connector, atau tetap fitur terpisah?
4. Agent lokal: dibuat sebagai aplikasi engine (mis. service Windows/Linux kecil), atau cukup kontrak API dan
   contoh sehingga pemakai membuat agent sendiri?
5. Kebijakan retry: jumlah percobaan dan jeda default, dan apakah bisa diatur per profil?
6. Retensi data: berapa lama outbox, kotak masuk dan log panggilan disimpan, dan apakah payload mentah ikut
   dihapus?
7. Pesan masuk dua arah (mis. balasan WhatsApp dari user): cukup disimpan dan diteruskan ke handler modul, atau
   engine perlu konsep percakapan?
8. Kunci enkripsi rahasia disimpan di mana (konfigurasi host di luar database)?
9. ~~Claim Integration Manager?~~ Dijawab 2026-11-05: manager hanya untuk admin, tanpa claim.
10. "Disable": cukup per integrasi, atau juga ada saklar mematikan seluruh integration system (mis. lewat
    konfigurasi host seperti saklar modul)?
11. Integrasi UI yang terpasang di beberapa client: siapa yang menjalankan? Pilihan: PC yang ditunjuk di manager,
    kunci (lock) di server agar hanya satu yang berjalan, atau semua boleh berjalan dengan dedup.
12. Integrasi UI yang butuh rahasia (mis. password perangkat): rahasia boleh dikirim ke client tertentu, disimpan
    lokal di PC itu, atau integrasi semacam itu wajib lingkup server/agent?
13. Quick run integrasi UI dari manager: dijalankan di client tempat manager dibuka, atau dikirim ke client yang
    ditunjuk?
14. Webhook ke integrasi nonaktif: ditolak, atau tetap disimpan di kotak masuk tanpa diproses?
15. Integrasi UI hanya dipanggil layar wrapper, atau boleh juga punya jadwal latar di client (mis. sync mesin
    absensi dari PC tertentu setiap 5 menit selama aplikasi terbuka)? Bila hanya dipanggil wrapper, pertanyaan
    no. 11 menjadi lebih sederhana.
16. Disable integrasi UI: cukup per integrasi, atau juga per PC (mis. perangkat USB hanya aktif di PC tertentu)?
17. Variabel per PC untuk lingkup UI: PC dikenali dari apa (nama mesin, id instalasi client), dan apakah admin
    mengisinya dari manager di PC mana pun atau hanya dari PC itu sendiri?
18. ~~Apakah variabel melekat pada profil?~~ Dijawab 2026-11-05: ya, variabel disimpan dalam profil dan kode
    memanggil profil yang dibutuhkan.
21. Perlukah profil default, atau kode selalu wajib menyebut nama profil?
22. Variabel per PC untuk lingkup UI: menjadi profil per PC, atau nilai per PC di dalam satu profil (terkait
    no. 17)?
23. Parameter task scheduler: memakai mekanisme profil yang sama, atau tetap nilai parameter per jadwal?
    *Ditunda: scheduler belum dibahas (ide awal).*
19. Tipe variabel: apakah daftar di atas cukup, atau perlu tipe lain (berkas sertifikat, daftar nilai, JSON)?
20. Penanda posisi sync (cursor): disimpan per profil koneksi integrasi, atau per jadwal di task scheduler?
