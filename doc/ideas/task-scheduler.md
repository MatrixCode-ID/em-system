# Task scheduler di engine

- Tanggal: 2026-11-05
- Status: diskusi, **ide awal**. Pengguna 2026-11-05: scheduler tidak dibahas sekarang; catatan ini hanya
  menyimpan gagasan awal. Rancangan dan pertanyaan di bawah belum dibahas dan belum diputuskan.
- Latar: pembahasan [integration system](integration-system.md) dan
  [absensi/jadwal kerja](tenant-attendance-schedule.md). Pengguna menegaskan bahwa **background task berbeda dari
  integrasi**, dan engine harus punya engine untuk menjalankan **task scheduler**.
- Nama tabel, class dan method di bawah masih **nama sementara**.

## Keputusan yang sudah jelas

- 2026-11-05 (pengguna): **background task terpisah dari integration system.** Integrasi tidak membawa
  penjadwalan sendiri; yang perlu berjalan terjadwal dijalankan oleh task scheduler.
- 2026-11-05 (pengguna): engine **harus menyediakan engine task scheduler** untuk menjalankan task terjadwal.
- 2026-11-05 (pengguna): **task scheduler tidak terkait BMM, jadi tidak mengenal istilah tenant.** Bila sebuah
  task butuh pembagian per tenant atau pengaturan yang lebih kompleks, pengguna library mengaturnya di task dan UI
  wrapper-nya sendiri (mis. lewat parameter). Pertanyaan no. 9 gugur.

## Kondisi engine saat ini

Proses latar engine ditulis masing-masing sebagai hosted service dengan jadwal di kodenya sendiri, mis.
pengiriman audit, retensi log, dan pekerjaan latar object storage. Modul hanya punya `AddHostedService<T>()`.
Akibatnya tidak ada tempat bersama untuk melihat task apa yang berjalan, kapan terakhir berjalan, hasilnya, atau
untuk menjalankan/menghentikannya dari UI.

## Rancangan (usulan, belum final)

Polanya mengikuti integration system: **engine menyediakan interface dan kerangka, pengguna library
mengimplementasikan task-nya.**

### 1. Task (interface)

- `IScheduledTask` (nama sementara): kunci, judul, deskripsi, deklarasi variabel/parameter (memakai mekanisme
  deklarasi yang sama dengan variabel integrasi), dan `RunAsync(context, cancellationToken)`.
- Didaftarkan di host server, mis. `builder.AddScheduledTask<RosterGenerateTask>("attendance.roster-generate")`,
  beserta jadwal bawaan.
- Konteks dari engine: nilai parameter, log writer (masuk riwayat run), laporan progres, penanda posisi
  (cursor) yang tersimpan antar run, dan token pembatalan.

### 2. Jadwal (`ta_TaskSchedule`)

- Task, jadwal (interval atau cron), zona waktu, state (aktif/nonaktif), nilai parameter.
- Kebijakan:
  - **tumpang tindih**: bila run sebelumnya belum selesai, run berikutnya dilewati (usulan bawaan);
  - **terlewat** (server mati saat jadwal tiba): jalankan sekali saat server hidup, atau lewati;
  - **batas waktu** run, dan **retry** dengan jeda bila gagal.
- Satu task boleh punya beberapa jadwal dengan parameter berbeda.

### 3. Riwayat run (`ta_TaskRun`)

- Jadwal, waktu mulai/selesai, durasi, state (berjalan, berhasil, gagal, dibatalkan, dilewati), pemicu (jadwal
  atau manual oleh user siapa), pesan hasil, progres, server yang menjalankan.
- Log baris per run disimpan ringkas; retensi riwayat diatur (pertanyaan no. 6).

### 4. Worker dan banyak instance server

- Satu hosted service engine membaca jadwal yang jatuh tempo dan menjalankan task.
- Bila API berjalan lebih dari satu instance (mis. beberapa container), hanya satu instance yang boleh
  menjalankan satu jadwal: memakai **kunci di database** (lease dengan batas waktu), agar instance yang mati tidak
  mengunci selamanya.
- Server yang dimatikan dengan rapi mengirim pembatalan ke task yang sedang berjalan.

### 5. Task Scheduler Manager (UI)

Usulan, mengikuti Integration Manager (hanya admin):

- Daftar task terpasang beserta jadwal, state, run terakhir, dan run berikutnya.
- Ubah jadwal dan parameter, aktifkan/nonaktifkan.
- **Run now** (quick run) dan **batalkan** run yang sedang berjalan.
- Riwayat run dan log per run.

## Contoh pemakaian

| Task | Pemilik | Jadwal contoh |
| --- | --- | --- |
| Sync mesin absensi lewat integrasi server | Wrapper/integrasi | Setiap 5 menit |
| Kirim antrean keluar integrasi (outbox) | Engine | Setiap beberapa detik, atau worker tersendiri (pertanyaan no. 7) |
| Generate roster periode berikutnya | Engine absensi | Setiap tanggal 25 |
| Hitung ulang rekap absensi (bila pilihan B) | Engine absensi | Setiap malam |
| Retensi log, pembersihan data sementara | Engine | Setiap hari |
| Laporan berkala lewat email/WhatsApp | Wrapper | Setiap Senin pagi |

Integrasi yang perlu berjalan berkala tidak membuat jadwal sendiri: sebuah task memanggil integrasi tersebut.
Dengan begitu manager integrasi tetap fokus pada koneksi, variabel dan log integrasi, sedangkan kapan berjalan
diatur di scheduler.

## Pertanyaan terbuka

1. Memakai library penjadwal yang sudah ada (mis. Quartz.NET, Hangfire) di dalam engine, atau ditulis sendiri di
   atas hosted service? Perlu dicek lisensi dan dukungan SQL Server/PostgreSQL/MySQL yang dipakai engine.
2. Scheduler hanya di server, atau ada juga task terjadwal di client (WPF/MAUI) saat aplikasi terbuka?
   Usulan: hanya server; kebutuhan di client dilayani integrasi UI yang dipanggil wrapper.
3. Format jadwal: cukup interval dan cron, atau perlu UI pembuat jadwal yang ramah (harian, mingguan, bulanan)?
4. Zona waktu jadwal: zona waktu server, atau zona waktu per jadwal?
5. Proses latar engine yang sudah ada (audit, retensi log, object storage) dipindah menjadi task scheduler, atau
   tetap hosted service sendiri?
6. Retensi riwayat run: berapa lama/berapa banyak, dan apakah log baris ikut retensi engine log?
7. Worker outbox integrasi: menjadi task terjadwal, atau worker tersendiri yang terus berjalan (lebih cepat)?
8. Manager hanya untuk admin seperti Integration Manager, atau perlu claim agar wrapper bisa memberi user tertentu
   hak menjalankan task miliknya?
9. ~~Task per tenant?~~ Gugur 2026-11-05: scheduler tidak mengenal tenant.
10. Notifikasi bila task gagal berulang kali: lewat engine log saja, atau mengirim pesan (email/integrasi)?
