# Absensi dan jadwal kerja employee per tenant

- Tanggal: 2026-11-05
- Status: diskusi
- Latar: kelanjutan employee per tenant (lihat [tenant-position.md](tenant-position.md)). Pengguna membahas
  **engine** untuk absensi dan scheduling employee, dengan contoh pemakaian di **manufacturing** (regu, shift
  bergilir, pabrik yang berjalan terus).
- Nama tabel dan kolom di bawah masih **nama sementara**; penamaan final mengikuti
  [Dahlia Convention](../convention/dahlia-convention.md) saat plan disusun.

## Keputusan yang sudah jelas

- 2026-11-05 (pengguna): absensi dan scheduling dibahas sebagai **fitur engine**, bukan sebagai modul pemakai.
- 2026-11-05 (pengguna): **jadwal harus punya hari mulai.** Hari ke-1 siklus boleh jatuh pada hari apa pun,
  misalnya Rabu; siklus kerja tidak harus mulai Senin. Dengan begitu "hari kerja terakhir yang hanya 5 jam" adalah
  hari terakhir siklus, bukan hari kalender tertentu. Pengguna menyebutnya "di shift"; agent menempatkannya di
  pola/penugasan pola (tanggal acuan), sedangkan shift tetap definisi waktu. Perlu dikonfirmasi (pertanyaan no. 16).
- 2026-11-05 (pengguna): pada aplikasi jadwal sebelumnya, **penugasan dibuat per 2 minggu**, dan **rotasi
  dipelihara manual oleh user** (tidak ada pola yang dibuat otomatis). Akibatnya untuk rancangan: penugasan
  jadwal yang bisa disusun dan diedit manual menjadi inti; tabel pola rotasi dan generator otomatis tidak wajib
  di tahap pertama (pertanyaan no. 18).
- 2026-11-05 (pengguna): **pola rotasi dan generator otomatis ditunda**; penyusunan manual dengan pembantu
  menjadi jalur utama. Menjawab pertanyaan no. 18.
- 2026-11-05 (pengguna): user saat ini menyusun penugasan dari kosong, tetapi **fitur salin pola (dari penugasan
  sebelumnya) diinginkan**. Pembantu pertama yang dibuat: salin penugasan.
- 2026-11-05 (pengguna): **panjang periode penugasan harus bisa diatur** (sistem lama 2 minggu, tetapi engine
  harus fleksibel). Tidak ada panjang tetap di engine. Cara mengaturnya: pertanyaan no. 19.
- 2026-11-05 (pengguna): **panjang periode penugasan: bawaan per tenant (mis. 14 hari) yang bisa ditimpa per
  penugasan**; tanggal selesai terisi otomatis dari tanggal mulai. Menjawab sebagian pertanyaan no. 19.
- 2026-11-05 (pengguna): **tumpang tindih penugasan untuk employee yang sama: tidak boleh bila keduanya sudah
  diproses; draft boleh tumpang tindih** (mengikuti usulan agent). Menjawab sebagian pertanyaan no. 19.
- 2026-11-05 (pengguna): **celah hari tanpa jadwal di antara penugasan diizinkan.** Tanggal itu berstatus
  "tanpa jadwal", berbeda dari "libur"; engine menyediakan daftar employee aktif yang punya celah pada periode
  ke depan. Arti "tanpa jadwal" bagi rekap adalah aturan modul. Menuntaskan pertanyaan no. 19.
- 2026-11-05 (pengguna): **penetapan jadwal bertingkat**: tenant mempunyai setelan **enable** dan **jadwal
  bawaan (default schedule)**; department dan individu employee juga. **Tingkat paling bawah menang**
  (employee mengalahkan department, department mengalahkan tenant). Rincian perilaku: pertanyaan no. 20–23.
- 2026-11-05 (pengguna): **enable per tingkat**: tenant aktif/nonaktif; department dan employee tiga nilai
  (ikut atas, aktif, nonaktif). Menjawab pertanyaan no. 20.
- 2026-11-05 (pengguna): **jadwal bawaan menutup tanggal yang tidak tercakup penugasan**, dari tingkat terbawah
  yang menetapkannya. **Jadwal bawaan bawaan engine mengikuti standar jam kantor** (08:00–17:00, istirahat
  12:00–13:00, Senin–Jumat, Sabtu–Minggu libur). Menjawab pertanyaan no. 21; arti persisnya dikonfirmasi di no. 24.
- 2026-11-05 (pengguna): **jadwal bawaan disalin ke roster tersimpan**, tidak dihitung saat dibaca (usulan agent
  disetujui). Menjawab pertanyaan no. 22; waktu penyalinan: no. 25.
- 2026-11-05 (pengguna): **sub-department mewarisi dari department induk sebelum tenant, dan setelan disimpan
  di tabel milik fitur ini**, bukan kolom baru di tabel BMM (usulan agent disetujui). Menjawab no. 23.
- 2026-11-05 (pengguna): **engine harus melayani dua ujung**: jadwal kompleks (banyak shift, regu, rotasi,
  penugasan per periode) maupun yang sangat sederhana (satu shift untuk perusahaan kecil). Cara mewujudkannya
  masih usulan (pertanyaan no. 17).
- 2026-11-05 (pengguna): **batas rekap memakai pilihan A.** Engine berhenti di roster + log; pencocokan log ke
  roster dan rekap harian dibuat modul. Pilihan B (rekap, periode, handler aturan di engine) tidak dipakai.
  Menjawab pertanyaan no. 1.
- 2026-11-05 (pengguna): **libur nasional bersifat global**: diisi sekali dan dipakai semua tenant, tidak per
  tenant. Fiturnya berupa sub module **Public Holiday**.
- 2026-11-05 (pengguna): Public Holiday harus bisa menangani **libur tetap** (mis. Natal, Tahun Baru) dan
  **libur yang bergeser tiap tahun** (mis. Idul Fitri). Mekanisme persisnya: pertanyaan no. 26–27.
- 2026-11-05 (pengguna): Public Holiday juga mencakup libur yang **bersifat ambil cuti** (cuti bersama di
  Indonesia): tanggal yang ditetapkan pemerintah, tetapi employee mengambil cuti, bukan libur penuh. Engine
  harus bisa membedakannya dari libur nasional biasa (jenis entri). Arti bagi jatah cuti dan keikutsertaan
  tenant: pertanyaan no. 28 dan 31.
- 2026-11-05 (pengguna): **libur yang bergeser (Idul Fitri dan sejenisnya) diisi manual per tahun** oleh admin;
  engine tidak menghitung kalender hijriah/lunar, dan memberi peringatan bila tahun berjalan belum diisi
  (usulan agent disetujui). Menjawab pertanyaan no. 27.
- 2026-11-05 (pengguna): **"Tanda" ini khusus untuk cuti bersama saja**: entri holiday punya satu penanda
  "cuti bersama" (ya/tidak) tanpa perilaku tambahan; entri tanpa tanda adalah libur biasa. Meluruskan
  catatan awal yang menafsirkan "Tanda" sebagai sifat seluruh holiday.
- 2026-11-05 (pengguna): **libur tetap (Natal, Tahun Baru) cukup diisi sekali** sebagai aturan bulan + tanggal
  yang berlaku otomatis tiap tahun. Menjawab pertanyaan no. 26.
- 2026-11-05 (pengguna): **masuk atau tidaknya produksi/security saat libur nasional ditentukan roster.**
  Kalender hanya menandai tanggal libur; tidak ada tanda "regu X tetap masuk" di kalender. Jam kerja pada
  tanggal libur itu dihitung sebagai **lembur** (aturan hitung di modul, bukan engine). Menjawab pertanyaan
  no. 30.
- 2026-11-05 (pengguna): **kalender libur menimpa jadwal bawaan** (tanggal libur nasional otomatis menjadi
  libur pada roster yang berasal dari jadwal bawaan), sedangkan **penugasan eksplisit tetap berlaku apa
  adanya** (usulan agent disetujui). Melengkapi pertanyaan no. 30; berkaitan dengan no. 25.
- 2026-11-05 (pengguna): **tidak ada libur khusus tenant**; satu-satunya kalender libur adalah Public Holiday
  global. Menjawab pertanyaan no. 28 (bagian cuti tetap ditunda).
- 2026-11-05 (pengguna): **satu kalender global untuk semua tenant, tanpa pembedaan negara/wilayah.** Bila ada
  libur negara lain (contoh pengguna: 4 Juli), cukup dimasukkan ke kalender global, dan berlaku bagi semua
  tenant. Menjawab pertanyaan no. 29.
- 2026-11-05 (pengguna): **pemetaan PIN/kartu mesin absensi ke employee dikelola dari sisi employee**
  ("di employee ada pemetaan ke absensi"), sehingga log mesin dicocokkan ke employee lewat pemetaan itu.
  Tempat penyimpanan (tabel milik fitur absensi berkunci id employee, atau lewat pemetaan id integration
  system) belum dipastikan; pertanyaan no. 32. Menjawab sebagian pertanyaan no. 4.
- 2026-11-05 (pengguna): **pemetaan PIN/kartu dilewati**, tidak ada tabel pemetaan di fitur absensi. Pencocokan
  data mesin ke employee **dihitung pada integrasi**, memakai **nomor karyawan** atau **ULID id employee/
  contact**. Menggantikan butir di atas, dan menutup pertanyaan no. 4 dan 32.
- 2026-11-05 (pengguna): **tap yang employee-nya tidak ditemukan tetap disimpan** di log kehadiran sebagai
  "tidak terpetakan" (employee kosong, identitas asli dari mesin disimpan), agar data tidak hilang diam-diam
  dan bisa dikoreksi lalu dipakai setelah employee diperbaiki. Konsekuensi untuk rancangan log: kolom employee
  boleh kosong, ditambah kolom identitas asli; cara memetakan ulang diatur di pertanyaan no. 33.
- 2026-11-06 (pengguna): **pemetaan tap mesin berbentuk interface yang diimplementasikan pengguna library**
  (seperti connector pada integration system), sedangkan **engine membangun fondasinya**. Pembagian: engine
  menyediakan kerangka (kontrak interface, penyimpanan log termasuk status "tidak terpetakan", dan penegakan
  prinsip log tidak di-update); implementasi pengguna menentukan cara mencocokkan identitas mesin ke employee
  dan cara menentukan masuk atau pulang. Rincian isi fondasi: pertanyaan no. 34. **Pembahasan cuti ditunda**: jatah cuti, kode status cuti, dan status roster untuk cuti bersama
  tidak dibahas sekarang. Pertanyaan no. 31 ditunda.
- 2026-11-06 (pengguna): **layar-layar engine absensi/jadwal menjadi bagian Business Module Manager (BMM)**:
  setelan penjadwalan, template jadwal, kode status kehadiran, Public Holiday, penugasan jadwal, roster, dan
  log kehadiran (tujuh layar). Layoutnya didesain pada `doc/design/BusinessModuleManager.figcraft`
  (page `30 Attendance`) sebagai grup drawer baru **Attendance (7)**. **Device integration (bagian absensi pada
  Integration Manager) tidak ikut BMM:** pengguna menegaskan 2026-11-06 bahwa integrasi punya tempat sendiri
  (Integration Manager); layarnya tidak didesain di sini. **Benturan dengan aturan yang ada:**
  `CLAUDE.md` root em-system (bagian Cakupan BMM dan modul wrapper) dan panduan engine BMM (bagian Scope and
  wrapper modules) menyatakan BMM hanya untuk setup dan update info dasar dan melarang alur kerja bisnis masuk
  BMM; penugasan jadwal, roster, dan log kehadiran adalah alur kerja. Aturan tersebut belum diubah; pengguna
  yang memutuskan pembaruannya (pertanyaan no. 35). Desain di file `.figcraft` hanya untuk review.
- 2026-11-06 (pengguna): **cakupan BMM diperluas** dengan grup Attendance (setup dan alur kerja absensi/jadwal),
  pilihan (a) pada pertanyaan no. 35. `CLAUDE.md` em-system (Cakupan BMM dan modul wrapper) dan panduan engine
  BMM (Scope and wrapper modules) diperbarui dengan pengecualian ini. Satu claim BMM atau claim terpisah
  tetap terbuka (no. 36).

## Contoh kebutuhan lapangan (pengguna, 2026-11-05)

Pabrik contoh membagi jadwal kerja dalam tiga kategori:

1. **Kantor**: masuk 08:00, 8 jam kerja, istirahat 12:00–13:00, pulang 17:00; Sabtu–Minggu libur. Sebagian
   employee kena lembur, sebagian tidak. Lembur bisa sebelum jam masuk dan sesudah jam pulang, minimal 60 menit.
   Selama **Ramadhan** jadwal berubah: masuk 07:30, istirahat 12:00–12:30, pulang 15:30.
2. **Security**: 12 jam, 07:00–19:00, **digilir per hari**. Masuk di hari liburnya sendiri atau di hari libur
   nasional dihitung lembur.
3. **Line produksi**: shift 1 (07:00–15:00), shift 2 (15:00–23:00), shift 3 (23:00–07:00 hari berikutnya). Ralat
   pengguna 2026-11-05 menggantikan tulisan awal (shift 1 07–16, shift 2 16–23, shift 3 13:00). Masing-masing
   8 jam; standar jam kerja 7 jam. Istirahat belum dikonfirmasi (tulisan awal: shift 1 12:00–13:00).

Catatan agent (belum dikonfirmasi): 8 jam dikurangi istirahat 1 jam = 7 jam kerja, cocok dengan standar bila tiap
shift beristirahat 1 jam. Lihat pertanyaan no. 12–15.

## Referensi: aplikasi jadwal kerja sebelumnya (pengguna, 2026-11-05)

Pengguna meminta aplikasi jadwal kerja yang sudah dipakai sebelumnya dijadikan **bahan contoh** untuk sistem di
engine. Pemahaman agent dari model data dan fungsi hitung jamnya (UI editor belum dibaca):

- **Template jadwal**: nama, tahap, jam masuk, durasi istirahat (menit), hari libur bawaan, toleransi keterlambatan
  (beberapa nilai delta), dan daftar per hari: urutan hari, hari dalam minggu, jam kerja hari itu. Jam kerja
  berbeda per hari, jadi hari pendek (5 jam) hanya satu baris di template. Atribut tersimpan sebagai pasangan
  nama–nilai, bukan kolom tetap.
- **Penugasan jadwal**: satu header berisi nomor, tanggal, deskripsi, **tahap** (draft atau diproses), dan
  **tanggal mulai** (hari ke-1 ditentukan manajemen). Isinya: employee, dan per employee daftar
  (template, selisih hari dari tanggal mulai, penanda). Tanggal kerja = tanggal mulai + selisih hari. Jadi
  jadwal per orang per hari **disimpan eksplisit**, relatif terhadap tanggal mulai; bukan dihitung dari pola.
- Perhitungan jam per periode hanya memakai penugasan dengan tahap minimal tertentu (draft diabaikan atau
  ikut, sesuai parameter), melewati employee yang dikecualikan.
- Kalender libur terpisah: tanggal, nama, libur tetap atau bukan, dan penanda cuti bersama.
- Periode gaji bukan bulan kalender (tanggal 16 sampai 15 bulan berikutnya); itu aturan modul, bukan engine.

Yang dipelajari untuk rancangan engine: (1) hari ke-1 per penugasan sudah terbukti kebutuhan nyata; (2) draft →
diproses sudah dipakai dan menjawab sebagian pertanyaan no. 9; (3) jadwal per hari disimpan eksplisit, selaras
dengan usulan roster tersimpan; (4) jam kerja per hari dalam template menjawab hari pendek tanpa definisi
shift tambahan.

## Gagasan dasar

Ada dua jenis scheduling yang perlu dibedakan:

- **Jadwal kerja orang** (workforce scheduling): siapa masuk shift apa pada tanggal kerja berapa. Ini cakupan
  ide ini dan menjadi acuan absensi.
- **Jadwal produksi**: pekerjaan dikerjakan di line/mesin mana dan kapan. Ini domain modul pemakai, bukan engine.
  Keduanya hanya bertemu pada kebutuhan orang per line (lihat pertanyaan no. 9).

Usulan pembagian (belum diputuskan):

| Engine | Modul pemakai (wrapper) |
| --- | --- |
| Log kehadiran mentah, kode status kehadiran | Toleransi, jendela pencocokan tap ke shift |
| Shift beserta istirahat, kalender libur | Perhitungan terlambat, pulang cepat, lembur, premi |
| Regu, pola rotasi, penugasan pola | Rekap harian dan penguncian periode untuk payroll |
| Generate roster, override manual | Aturan tukar shift, batas jam lembur |
| | Kebutuhan orang per line dari jadwal produksi |

Alasan: shift, regu, rotasi dan hari libur polanya hampir sama di semua perusahaan, sedangkan aturan hitungnya
berbeda per perusahaan dan aturan tenaga kerja setempat.

## Rancangan data (usulan, belum final)

Semua tabel per tenant, memakai id ULID, kolom standar (`ustamp`, `datestamp`, `json_object`), dan konvensi
state engine (0 aktif, negatif nonaktif/dibatalkan; tidak ada hapus fisik). Referensi ke employee memakai FK
komposit `(employee, tenant)` agar data selalu milik tenant yang sama, seperti pola department.

### 1. Log kehadiran mentah (`ta_TenantEmpAttLog`)

| Kolom | Tipe | Keterangan |
| --- | --- | --- |
| `cTenantEmpAttLogId` | char(26) | ULID |
| `cTenantId` | char(26) | Tenant |
| `cTenantEmpId` | char(26) | Employee |
| `cTenantEmpAttLogTime` | datetime | Waktu kejadian sebenarnya; berbeda dari `ustamp` (waktu dicatat) |
| `cTenantEmpAttLogType` | int | 0 tidak diketahui (perangkat tidak membedakan), 1 masuk, 2 keluar, 3 mulai istirahat, 4 selesai istirahat |
| `cTenantEmpAttLogSource` | int | 0 manual, 1 perangkat, 2 aplikasi mobile, 3 import |
| `cTenantEmpAttLogDevice` | varchar(100) NULL | Id terminal/perangkat |
| `cTenantEmpAttLogLat` / `cTenantEmpAttLogLng` | decimal NULL | Lokasi untuk tap dari mobile |
| `cTenantEmpAttLogState` | int | 0 aktif, -1 dibatalkan |
| `cTenantEmpAttLogNote` | varchar(500) NULL | Wajib untuk input manual dan pembatalan |

Prinsip:

- **Log tidak di-update.** Koreksi = membatalkan baris lama lalu menambah baris baru, agar jejak audit utuh.
- **Tanggal kerja tidak disimpan di log.** Tanggal kerja ditentukan roster (shift malam yang melewati tengah
  malam).
- **Cegah duplikat dari perangkat** dengan unique `(employee, waktu, sumber, perangkat)`, agar tarikan ulang dari
  mesin tidak menggandakan data.
- Payload mentah perangkat atau referensi foto (id binary storage) disimpan di `json_object`.

### 2. Kode status kehadiran (`ta_TenantAttCode`)

Daftar yang bisa diatur per tenant, polanya sama dengan daftar posisi: id, tenant, kode (unik per tenant), nama,
kategori enum (hadir, tidak hadir berbayar, tidak hadir tanpa bayar, libur), state, catatan. Bisa disalin dari
tenant lain. Contoh isi: Hadir, Sakit, Izin, Cuti, Dinas. Engine hanya menyimpan daftar; arti tiap kode untuk
penggajian diatur modul.

### 3. Shift (`ta_TenantShift` + `ta_TenantShiftBreak`)

- Shift: kode, nama, jam mulai, jam selesai, tanda lintas hari, state. Hanya definisi waktu; toleransi dan
  aturan hitung tidak disimpan di sini.
- Istirahat (tabel anak): jam mulai dan durasi; satu shift boleh punya lebih dari satu istirahat.

### 4. Kalender libur (`ta_TenantHoliday`)

Per tenant dan tanggal: libur nasional, cuti bersama, shutdown pabrik. Dilengkapi tanda apakah regu produksi
yang berjalan terus tetap masuk.

Revisi 2026-11-05 (pengguna): **libur nasional dipisah menjadi sub module Public Holiday yang global** (tanpa
tenant), mendukung libur tetap dan libur yang bergeser tiap tahun. Libur khusus tenant (shutdown pabrik dan
sejenisnya) belum diputuskan; lihat pertanyaan no. 28. Bentuk tabel final menunggu jawaban no. 26–29.

### 5. Regu (`ta_TenantCrew`)

Contoh Regu A/B/C/D. Di manufacturing, yang dijadwalkan biasanya regu, bukan orang per orang. Usulan: employee
mendapat kolom nullable `cTenantCrewId` (satu employee satu regu, sama dengan pola satu employee satu
department). Riwayat pindah regu, bila dibutuhkan, diatur modul.

### 6. Pola rotasi (`ta_TenantRotation` + `ta_TenantRotationDay`)

Rotasi punya panjang siklus N hari; setiap hari ke-i berisi shift atau libur.

Contoh 4 regu 3 shift untuk pabrik yang berjalan terus, siklus 8 hari:

| Hari ke | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Pola | Pagi | Pagi | Siang | Siang | Malam | Malam | Libur | Libur |

Regu A mulai di hari ke-1, B di hari ke-3, C di hari ke-5, D di hari ke-7, sehingga setiap hari selalu ada satu
regu di tiap shift. Pola kantor juga memakai struktur yang sama, mis. siklus 7 hari: Senin–Jumat Normal,
Sabtu–Minggu Libur.

### 7. Penugasan pola (`ta_TenantCrewRotation`)

Regu (atau employee individual untuk staf non-regu), rotasi, tanggal acuan (anchor), offset hari, dan berlaku
mulai. Penggantian pola tidak mengubah jadwal yang sudah lewat.

### 8. Roster (`ta_TenantRoster`)

Satu baris per employee per **tanggal kerja**: shift atau libur, sumber (0 dari pola, 1 override manual,
2 tukar shift, 3 lembur), dan department/line opsional untuk orang yang dipinjam ke line lain.

Usulan: roster **disimpan per periode** (di-generate dan dipublikasikan), tidak dihitung setiap kali dibuka:

- absensi butuh acuan stabil "seharusnya masuk shift apa";
- override dan tukar shift perlu tempat disimpan;
- generate ulang hanya menimpa baris bersumber pola pada tanggal yang belum dikunci; override manual tidak
  tersentuh.

## Kaitan absensi dan roster

- **Tanggal kerja** ditentukan roster: tap pukul 06:50 milik shift malam yang mulai kemarin pukul 23:00 masuk
  tanggal kerja kemarin.
- Log mentah dicocokkan ke **jendela shift** di roster (mis. 2 jam sebelum mulai sampai 4 jam setelah selesai).
  Lebar jendela adalah aturan.
- Rekap harian (terlambat, lembur, tidak hadir) = roster dibandingkan dengan log.
- Tukar shift dan lembur hari libur bisa menjadi dokumen dengan engine approval; setelah disetujui, roster
  ditulis dengan sumber tukar shift/lembur. Dynamic signer bisa diambil dari kepala department.

### Batas engine untuk rekap (belum diputuskan)

- **A.** Engine berhenti di roster + log. Pencocokan log ke roster dan rekap harian sepenuhnya dibuat modul.
- **B.** Engine juga menyimpan rekap harian dan mencocokkan log ke roster; aturannya diberikan modul lewat
  handler. Ini pengecualian dari keputusan sebelumnya bahwa engine tidak menyediakan handler aturan employee.

Usulan agent: **A**, agar engine tetap netral. B layak dipertimbangkan bila pemakai engine ternyata menulis
pencocokan yang sama berulang-ulang.

### Rincian pilihan B (usulan untuk dipelajari, 2026-11-05)

Pada B, engine tidak hanya menyimpan roster (seharusnya) dan log (kenyataan), tetapi juga **menjalankan proses
yang menghasilkan rekap harian**. Engine memegang alurnya: kapan dihitung, data mana yang dipakai, hasil
disimpan di mana, dan apa yang dikunci. Modul hanya menyumbang **aturan** lewat handler. Engine tidak punya
angka aturan bawaan.

#### Pembagian kerja pada B

| Langkah | Pemilik |
| --- | --- |
| Menentukan kapan rekap dihitung ulang (log baru/dibatalkan, roster berubah, perintah hitung ulang periode) | Engine |
| Mengambil roster, shift, istirahat, libur dan log yang relevan | Engine |
| Menentukan jendela pencocokan log ke shift | Handler modul (engine bertanya) |
| Mengelompokkan log ke tanggal kerja dan mencatat log mana dipakai | Engine, dengan jendela dari handler |
| Menilai hari itu: kode status, terlambat, pulang cepat, lembur, nilai tambahan | Handler modul |
| Menyimpan rekap, melewati hari yang dikunci atau di-override manual | Engine |
| Mengunci periode | Engine (perintah dari user/modul) |
| Memakai rekap untuk payroll, laporan, sanksi | Modul |

#### Tambahan data pada B

- **Rekap harian (`ta_TenantEmpAttDay`)**: id, tenant, employee, tanggal kerja (unik per employee), roster
  acuan, shift, masuk pertama, keluar terakhir, menit kerja, kode status (FK ke kode status kehadiran), menit
  terlambat, menit pulang cepat, menit lembur, sumber (0 hasil hitung, 1 override manual), kunci handler yang
  menghitung, waktu hitung, dan `json_object` untuk nilai tambahan milik aturan modul (mis. premi shift malam).
- **Log yang dipakai rekap (`ta_TenantEmpAttDayLog`)**: tautan rekap ↔ log, agar setiap angka bisa dijelaskan
  ("terlambat 12 menit karena tap 23:12 ini"). Log tetap tidak diubah, karena tautannya berada di tabel
  terpisah.
- **Periode absensi (`ta_TenantAttPeriod`)**: tenant, tanggal dari–sampai, state (terbuka/dikunci). Hari dalam
  periode terkunci tidak dihitung ulang. Log yang datang terlambat tetap tersimpan, tetapi tidak mengubah rekap.
  Penyesuaiannya menjadi urusan modul (mis. adjustment di periode berikutnya).

#### Bentuk handler (sketsa)

Modul mendaftarkan aturan dengan kunci, mis. `builder.AddAttendanceRule<FactoryRule>("factory-2026")`.
Setiap tenant memilih kunci aturan yang dipakai, sehingga dua company di satu instalasi bisa memakai aturan
berbeda. Isi handler, dengan nama sementara:

- `GetMatchWindow(shift)`: berapa lama sebelum mulai dan sesudah selesai sebuah tap masih dianggap milik
  shift itu.
- `Evaluate(context)`: menerima roster hari itu, shift, istirahat, status libur dan log yang sudah
  dikelompokkan, lalu mengembalikan kode status, menit terlambat/pulang cepat/lembur, dan nilai tambahan.

Handler hanya menghitung dan tidak menulis ke database; engine yang menyimpan hasilnya dalam satu transaksi.
Bila tenant belum memilih aturan, engine hanya mengisi masuk pertama/keluar terakhir dan membiarkan kolom
penilaian kosong (atau menolak hitung; ini masih perlu diputuskan).

#### Contoh alur

1. Roster: Budi, tanggal kerja 10 Nov, shift Malam 23:00–07:00.
2. Log masuk: 23:12 dan 07:05 (11 Nov). Engine menandai rekap 10 Nov untuk dihitung ulang.
3. Engine bertanya jendela ke handler. Jawaban: 2 jam sebelum mulai, 4 jam sesudah selesai. Kedua tap masuk
   jendela shift malam 10 Nov.
4. Engine memanggil `Evaluate`. Handler, dengan toleransi 10 menit miliknya, menjawab: status Hadir, terlambat
   12 menit, lembur 0.
5. Engine menyimpan rekap dan tautan ke dua log tersebut.
6. Akhir bulan, periode dikunci. Modul payroll membaca rekap dari service engine.

#### Kelebihan B

- Bagian tersulit (shift malam, tap ganda, lupa tap, hitung ulang setelah koreksi) ditulis sekali di engine
  dan dipakai semua pemakai.
- Hanya ada satu sumber rekap, sehingga modul HR dan payroll tidak menghitung dengan hasil berbeda.
- Engine bisa menyediakan layar/servis rekap standar dan penjelasan angka (log mana yang dipakai).
- Penguncian periode konsisten untuk semua pemakai.

#### Kekurangan dan risiko B

- Engine perlu kontrak handler, yaitu pengecualian dari keputusan "engine tidak menyediakan handler aturan
  employee". Kontrak itu harus stabil, karena perubahannya memecah semua modul pemakai.
- Kolom rekap tetap (terlambat, pulang cepat, lembur) mungkin tidak cukup untuk semua aturan, sehingga sebagian
  nilai lari ke `json_object` dan sulit di-query.
- Proses hitung ulang menambah beban: kapan dijalankan (langsung saat log masuk atau terjadwal), dan bagaimana
  menangani ribuan employee pada satu periode.
- Hasil rekap bergantung pada versi aturan. Bila aturan modul berubah, rekap lama yang belum dikunci akan
  berubah saat dihitung ulang.

#### Perbandingan singkat

| Hal | A | B |
| --- | --- | --- |
| Engine menyimpan | Roster + log | Roster + log + rekap + periode |
| Pencocokan log ke shift | Modul | Engine (jendela dari handler) |
| Aturan hitung | Modul | Modul, lewat handler |
| Handler di engine | Tidak ada | Ada (kontrak baru) |
| Sumber rekap | Bisa lebih dari satu modul | Satu, di engine |
| Beban implementasi pemakai | Tinggi | Rendah (cukup aturan) |
| Beban dan risiko engine | Rendah | Lebih tinggi |

#### Pertanyaan khusus B

- Hitung ulang langsung saat log/roster berubah, terjadwal (mis. tiap malam), atau keduanya?
- Bila tenant belum memilih aturan: isi sebagian (masuk/keluar saja) atau tolak hitung?
- Apakah override manual rekap boleh dilakukan di engine, dan siapa yang berhak (claim)?
- Perlukah menyimpan versi aturan yang dipakai, agar perubahan aturan tidak mengubah rekap lama tanpa jejak?

## Pertanyaan terbuka

1. ~~Batas engine untuk rekap: A atau B (lihat di atas)?~~ Dijawab 2026-11-05: A.
2. Setuju shift, regu, rotasi dan roster di engine, dengan aturan hitung di modul?
3. Zona waktu: cukup `datetime` waktu lokal seperti konvensi sekarang, atau tenant perlu zona waktu sendiri
   (`datetimeoffset`) karena lokasi berbeda?
4. (Dijawab 2026-11-05, lihat Keputusan: dilewati, dicocokkan di integrasi) Identitas di mesin absensi: cukup kode employee, atau perlu pemetaan terpisah id/PIN/kartu perangkat ↔
   employee (satu employee bisa punya beberapa kartu)? Usulan: memakai pemetaan id pada
   [integration system](integration-system.md); sync mesin absensi juga berjalan lewat kerangka itu.
5. Absensi dari mobile: perlu foto/selfie dan geofence lokasi kantor? Bila ya, geofence di engine atau modul?
6. Letak layar: setup (kode status, shift, libur, regu, rotasi) masuk Business Module Manager sebagai setup,
   atau layar sendiri? Input/koreksi log dan roster adalah alur kerja, sehingga menurut aturan cakupan BMM
   bukan di BMM; perlu dipastikan apakah engine menyediakan layarnya atau hanya service.
7. Apakah regu selalu di dalam satu department, atau boleh lintas department?
8. Staf non-produksi dijadwalkan lewat roster dengan pola 5 hari kerja, atau cukup jadwal default tenant tanpa
   roster?
9. Generate roster: berapa jauh ke depan (per bulan?), dan perlu status Draft → Published sebelum terlihat oleh
   employee/absensi?
10. Kebutuhan orang per line/mesin (target jumlah orang per shift): perlu sekarang, atau ditunda sampai modul
    produksi membutuhkannya?
11. ~~Shift 3 produksi: jam masuk sebenarnya 13:00 atau 23:00?~~ Dijawab 2026-11-05 (pengguna): **23:00**
    (shift 3 = 23:00–07:00 hari berikutnya). Ralat pengguna 2026-11-05: shift 1 07:00–15:00, shift 2 15:00–23:00,
    shift 3 23:00–07:00; ketiganya 8 jam.
12. Jam kerja standar 7 jam: apakah tiap shift 8 jam dikurangi istirahat 1 jam? Bila ya, jam kerja bisa
    diturunkan dari jam shift dan istirahat, sehingga shift tidak perlu menyimpan "jam kerja standar".
13. Security "digilir per hari": digilir antar orang (satu orang masuk, yang lain libur) atau antar shift? Apakah
    ada shift malam 19:00–07:00, dan berapa hari libur tiap orang?
14. Ramadhan: berlaku untuk kantor saja, atau produksi juga? Tanggal mulai/selesai ditentukan manual tiap tahun?
15. ~~Produksi libur Sabtu–Minggu, atau berjalan terus?~~ Dijawab sebagian 2026-11-05 (pengguna): produksi
    **libur hanya hari Minggu**; target jam kerja **40 jam per minggu**. Sisa: Senin–Sabtu 7 jam × 6 hari = 42
    jam, bukan 40. Dijawab 2026-11-05 (pengguna): **hari kerja terakhir dalam seminggu hanya 5 jam** (5×7 +
    5 = 40 jam); untuk shift 1 itu Sabtu mulai pukul 07:00. Penegasan pengguna: intinya "hari terakhir hanya 5
    jam", jadi berlaku untuk tiap shift, bukan khusus Sabtu. Perlu dipastikan: jam selesai hari pendek tiap
    shift (shift 1 mungkin 07:00–12:00; shift 2 dan 3?), dan apa yang dimaksud "hari terakhir" untuk shift 3 yang
    melewati tengah malam. Sisa: regu atau individu yang digilir, dan seberapa sering (mingguan)?
19. ~~Panjang periode penugasan dan aturan antarpenugasan~~ Dijawab 2026-11-05: panjang bawaan per tenant yang
    bisa ditimpa; tumpang tindih ditolak bila keduanya diproses (draft boleh); celah diizinkan dengan status
    "tanpa jadwal" dan daftar peringatan.
18. ~~Karena rotasi dipelihara manual:~~ Dijawab 2026-11-05: ditunda; manual + salin sebagai jalur utama. Pembantu penyusunan yang
    mungkin berguna selanjutnya: geser seluruh regu satu langkah, isi satu template ke banyak orang sekaligus.
20. (Dijawab 2026-11-05, lihat Keputusan) Arti **enable** per tingkat: apakah penjadwalan/absensi berlaku untuk tenant/department/orang itu (mis.
    direksi atau pekerja lepas tidak dijadwalkan)? Usulan: tingkat bawah punya tiga nilai (ikut atas, aktif,
    nonaktif); tenant hanya aktif atau nonaktif.
21. (Dijawab 2026-11-05, lihat Keputusan) **Jadwal bawaan dan penugasan**: bila tanggal tidak tercakup penugasan, dipakai jadwal bawaan dari tingkat
    terbawah yang menetapkannya; bila tidak ada, status "tanpa jadwal" (keputusan celah). Setujukah? Bila ya,
    perusahaan kecil cukup menetapkan jadwal bawaan tenant tanpa penugasan.
22. (Dijawab 2026-11-05, lihat Keputusan) Jadwal bawaan dipakai **saat dibaca** (dihitung, tidak masuk roster) atau **disalin ke roster tersimpan**
    pada saat tertentu? Terkait keputusan roster disimpan (menjaga acuan stabil untuk rekap).
23. (Dijawab 2026-11-05, lihat Keputusan) Department punya pohon induk–anak: bila sub-department tidak menetapkan, apakah ia mewarisi dari department
    induk lebih dulu sebelum tenant? Dan di mana setelan disimpan: tabel setelan milik fitur ini (tingkat, id
    acuan, enable, template bawaan), bukan kolom baru di tabel BMM (usulan agent, karena BMM hanya setup dasar).
24. Jadwal bawaan standar kantor: apakah ini **nilai bawaan di kode engine** yang dipakai bila tidak ada tingkat
    yang menetapkan jadwal bawaan (selaras aturan engine mulai dari data kosong), dan tenant boleh menggantinya?
    Bila ya, status "tanpa jadwal" hanya muncul untuk yang nonaktif; apakah itu dimaksud?
25. Kapan jadwal bawaan disalin ke roster: saat tenant/department/employee diaktifkan lalu untuk periode ke
    depan (task terjadwal), saat penugasan dibuat, atau saat roster pertama kali dibutuhkan? Dan nilai awal
    **enable** tenant: nonaktif sampai admin mengaktifkan (usulan agent, agar tidak membuat roster tanpa sengaja)?
17. Kesederhanaan: satu mekanisme dengan nilai bawaan (penugasan jadwal ke seluruh tenant dengan satu template,
    roster dibuat otomatis), atau jalur sederhana terpisah tanpa roster? Usulan agent: satu mekanisme.
16. Hari mulai (keputusan 2026-11-05): disimpan pada penugasan pola sebagai tanggal acuan (hari ke-1 jatuh pada
    tanggal itu), atau pada pola sebagai hari dalam minggu (mis. selalu mulai Rabu)? Apakah hari mulai bisa
    berbeda per regu/orang yang memakai pola yang sama?
26. (Dijawab 2026-11-05, lihat Keputusan) Libur **tetap** (Natal, Tahun Baru): disimpan sebagai aturan bulan + tanggal yang berlaku tiap tahun
    (engine otomatis menganggapnya libur di tahun mana pun), atau tetap diisi per tahun seperti libur lain
    (misalnya lewat tombol "salin dari tahun lalu")? Usulan agent: aturan berulang, karena tidak pernah lupa
    diisi.
27. (Dijawab 2026-11-05, lihat Keputusan) Libur **bergeser** (Idul Fitri, Idul Adha, Nyepi, Waisak, dan lain-lain): diisi **manual per tahun** oleh
    admin, tanpa engine menghitung kalender hijriah/lunar, karena tanggalnya ditetapkan pemerintah dan bisa
    berubah di tengah tahun? Usulan agent: manual per tahun, dengan peringatan bila tahun berjalan belum
    diisi.
28. (Dijawab 2026-11-05: tidak ada libur khusus tenant; bagian cuti bersama ditunda) Dengan libur nasional global, apakah **libur khusus tenant** (shutdown pabrik, libur perusahaan) tetap
    perlu, sebagai daftar terpisah milik tenant? Dan apakah **cuti bersama** masuk Public Holiday (global)
    dengan penanda jenis, sehingga tenant boleh mengikuti atau tidak?
29. (Dijawab 2026-11-05, lihat Keputusan) Satu kalender global untuk semua, atau perlu dibedakan per **negara/wilayah** (tenant di negara lain, atau
    libur daerah)? Bila perlu, tenant memilih kalender mana yang dipakai.
30. (Dijawab 2026-11-05, lihat Keputusan) Cara tenant **tetap masuk saat libur nasional** (security, produksi): usulan agent, kalender hanya
    menandai "tanggal ini libur nasional"; masuk atau tidaknya ditentukan roster/penugasan jadwal. Engine
    tidak menaruh tanda "regu X tetap masuk" di kalender. Jadwal bawaan yang menutup tanggal (enable tenant
    aktif) mengikuti libur nasional kecuali ditimpa. Setuju?
36. Claim: satu claim BMM untuk seluruh grup Attendance (asumsi pada desain, mengikuti "satu claim BMM"
    yang berlaku), atau claim terpisah untuk absensi (mis. setup, penugasan, log) karena alur kerjanya
    dipakai orang HR yang bukan admin setup? Belum diputuskan.
35. (Dijawab 2026-11-06: pilihan (a), cakupan BMM diperluas; lihat Keputusan) Aturan scope BMM: karena layar absensi menjadi bagian BMM (keputusan 2026-11-06), bagaimana aturan
    "BMM hanya setup dan update info dasar" diperbarui? Pilihan: (a) cakupan BMM diperluas dengan grup
    Attendance (setup dan alur kerja absensi); (b) hanya setup absensi (setelan, template, kode status,
    Public Holiday) di BMM, sedangkan penugasan, roster, dan log tetap layar wrapper; (c) lainnya. Perubahan
    `CLAUDE.md` dan panduan engine BMM menunggu keputusan pengguna.
34. Isi fondasi dari engine (usulan agent, sketsa): (a) kontrak interface pemetaan: masukan tap mentah
    (identitas mesin, waktu kejadian, perangkat, jenis dari mesin bila ada), keluaran id employee (kosong bila
    tidak ditemukan) dan jenis tap (boleh 0 = tidak diketahui); (b) aksi bawaan engine "petakan ulang"
    (batalkan baris lama + tambah baris baru) yang dipanggil implementasi, per identitas; (c) daftar tap
    "tidak terpetakan" yang bisa dibaca; (d) pemanggilan implementasi dijalankan lewat integration system.
    Cukupkah isi fondasi ini, atau ada yang perlu ditambah atau dikurangi?
33. (Sebagian dijawab 2026-11-06: cara memetakan diserahkan ke implementasi; sisa: penegakan batalkan + tambah
    oleh engine, lihat no. 34) Karena log tidak di-update (koreksi = batalkan lalu tambah baris baru), memetakan ulang tap yang
    "tidak terpetakan" mengikuti prinsip itu: baris lama dibatalkan dan baris baru berisi employee dibuat?
    Atau pengecualian: kolom employee pada baris tidak terpetakan boleh diisi sekali? Usulan agent: mengikuti
    prinsip batalkan + tambah, agar jejak audit utuh. Siapa yang boleh memetakan ulang?
32. (Gugur 2026-11-05: pemetaan dilewati) Pemetaan PIN/kartu ↔ employee: usulan agent, **tabel milik fitur absensi** berkunci id employee (satu
    employee boleh banyak kartu/PIN, tiap baris menyebut perangkat bila PIN berlaku per mesin), sesuai aturan
    "data tambahan wrapper bukan kolom baru di tabel BMM". Diisi dari layar employee atau layar absensi. Mesin
    yang menarik data lewat integration system memakai tabel ini untuk mengubah PIN menjadi employee. Apakah
    PIN unik antar seluruh mesin tenant, atau hanya per mesin?
31. (DITUNDA 2026-11-05 atas arahan pengguna: cuti tidak dibahas dulu) **Cuti bersama** (keputusan 2026-11-05: bagian dari Public Holiday, jenis tersendiri). Pertanyaan: (a) apakah
    engine hanya menandai jenisnya, sedangkan pemotongan jatah cuti dan status kehadiran (kode "Cuti") adalah
    aturan modul? Usulan agent: ya. (b) Apakah tenant boleh memilih mengikuti atau tidak mengikuti cuti bersama
    (ada perusahaan yang tetap masuk)? Bila ya, pilihannya per tenant, per jenis, atau per tanggal? (c) Pada
    roster, tanggal cuti bersama bertanda "libur" atau "cuti"? Usulan agent: status roster tersendiri
    "cuti bersama", agar rekap modul bisa membedakannya dari libur nasional biasa.
