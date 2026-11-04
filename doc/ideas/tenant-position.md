# Daftar posisi per tenant di Business Module Manager

- Tanggal: 2026-11-05
- Status: diskusi (desain layout untuk review)
- Desain: `doc/design/BusinessModuleManager.figcraft`, page `10 Business Module Manager` (Positions,
  Copy from tenant) dan `20 Employee Editor` (field Position). Rincian di [README desain](../design/README.md).
- Latar: kelanjutan employee per tenant di Business Module Manager (BMM). Catatan ide employee sebelumnya
  masih disimpan di luar repo ini; ringkasan keputusan yang relevan ada di bawah.

## Kebutuhan

Employee perlu dikategorikan. Kasus uji pertama adalah aplikasi turunan untuk perusahaan konsultan
engineering. Saat ini jabatan employee berupa teks bebas, sehingga tidak bisa dipakai konsisten untuk
filter, laporan, atau aturan.

## Keputusan yang sudah jelas

- 2026-11-05 (pengguna): **chain of command tidak dibuat di engine.** Tidak ada garis atasan eksplisit,
  slot posisi, maupun lookup atasan berjenjang dari pohon department. Kepala department tetap seperti yang
  ada.
- 2026-11-05 (pengguna): engine menyediakan **daftar posisi yang bisa diatur**, isinya ditentukan pemakai,
  mis. CEO, Engineer, dan seterusnya (nama jabatan, bukan grade atau disiplin).
- 2026-11-05 (pengguna): **employee tinggal memilih posisi yang tersedia**; tidak ada pengisian jabatan
  teks bebas pada input employee.
- 2026-11-05 (pengguna): **tidak perlu level/rank** pada posisi.
- 2026-11-05 (pengguna): **kolom teks jabatan employee diganti kolom id posisi** (referensi ke daftar posisi).
  Kolom teks lama tidak dipertahankan.
- 2026-11-05 (pengguna): daftar posisi **bisa disalin dari tenant lain**.
- 2026-11-05 (pengguna): contact employee terkunci setelah disimpan, tetapi **Full Name contact bisa diubah
  dari Employee Editor** (lihat "Contact pada Employee Editor").
- 2026-11-05 (pengguna): **BMM hanya untuk setup dan update info dasar.** Fitur bisnis di atas data ini
  (claim yang proper, layar di menu Apps) dibangun pengguna library sebagai **modul wrapper**. Aturan
  kanonis ada di `CLAUDE.md` root ("Cakupan BMM dan modul wrapper") dan panduan engine BMM.
- Garis proyek (peran employee per dokumen/transaksi) bukan bagian data employee; itu ranah peran employee
  di transaksi bisnis atau modul pemakai.

## Rancangan (usulan, belum final)

### Data

- Tabel posisi per tenant (nama sementara `ta_TenantPosition`): id ULID, tenant, kode (unik per tenant),
  nama, state (0 aktif, -1 nonaktif), catatan, `ustamp`/`datestamp`, `json_object`. Unique
  `(id, tenant)` untuk FK komposit.
- Employee mendapat kolom id posisi nullable (nama sementara `cTenantPositionId`), FK komposit
  `(posisi, tenant)` ke posisi tenant yang sama, seperti pola department. Kolom teks jabatan dibuang.
- Tidak ada hapus fisik; posisi dinonaktifkan. Posisi nonaktif tidak ditawarkan untuk dipilih, tetapi
  employee yang sudah memakainya tetap menampilkannya.

### Layar (desain 2026-11-05)

- Drawer BMM: **Organization (3)** → Tenants, Departments, **Positions**.
- Halaman **Positions**: combo tenant, Refresh, **Copy from tenant**, Save, Deactivate, dan aksi utama
  **New position**. Kiri: daftar posisi (nama, jumlah employee aktif, kode, chip INACTIVE). Kanan: editor
  Code, Name, Note.
- Dialog **Copy positions from tenant**: pilih tenant sumber, centang posisi yang akan disalin. Posisi yang
  kodenya sudah ada di tenant tujuan ditandai EXISTS dan tidak bisa dicentang. Posisi hasil salinan
  disimpan aktif.
- Employee Editor: field **Position** menjadi combo yang hanya berisi posisi aktif tenant employee.

## Pertanyaan terbuka

1. Lingkup daftar posisi sudah per tenant (dengan salin dari tenant lain). Apakah perlu juga pewarisan
   otomatis dari tenant induk, atau cukup salin manual?
2. Satu employee satu posisi, atau boleh lebih dari satu?
3. Apakah posisi terikat department tertentu, atau bebas dipakai di department mana pun? Desain saat ini:
   bebas.
4. Salin dari tenant lain: hanya posisi aktif di tenant sumber yang ditampilkan, atau termasuk yang
   nonaktif? Desain saat ini: hanya yang aktif.
5. Salinan bersifat sekali salin (tidak tersinkron dengan tenant sumber). Apakah ini cukup?
6. Pencocokan "sudah ada" memakai kode posisi saja, atau kode dan nama?
7. Apakah nanti diperlukan jenis kategori lain (grade, jenis kepegawaian) sebagai mekanisme klasifikasi
   generik, atau cukup daftar posisi?
8. Data yang sudah terpasang: kolom teks jabatan employee masih kosong di instalasi yang ada, jadi
   penggantian kolom tidak butuh migrasi data. Perlu dipastikan lagi saat menyusun plan.

## Contact pada Employee Editor (2026-11-05)

- Keputusan (pengguna, menyetujui usulan): **contact employee tidak bisa diganti** setelah employee disimpan.
  Search contact hanya ada saat membuat employee baru. Salah pilih orang diatasi dengan menonaktifkan
  employee lalu membuat yang baru; contact ganda untuk orang yang sama menjadi bahan ide alat gabung contact.
- Keputusan (pengguna): **Full Name contact boleh diubah dari Employee Editor.** Perubahan berlaku pada
  contact itu sendiri, jadi ikut terlihat di tenant dan akun lain yang memakai contact yang sama.
- Keputusan (pengguna): **user name (akun user yang tertaut ke contact) tidak bisa diubah dari Employee
  Editor.** Editor hanya menampilkannya sebagai informasi baca saja; perubahan akun tetap lewat User Manager.
  Yang bisa diubah dari sisi contact hanya Full Name.
- Kondisi engine saat ini: server sudah menolak perubahan tenant/contact pada employee tersimpan, tetapi
  penyimpanan employee belum memperbarui nama lengkap contact yang sudah ada (hanya contact baru). Ini perlu
  masuk plan implementasi.
- Kebutuhan implementasi update Full Name (usulan untuk plan):
  - Editor employee menampilkan nama lengkap contact sebagai input biasa, baik saat employee baru maupun
    tersimpan; kolom ini ikut tanda "unsaved changes" dan Revert.
  - Save employee mengirim nama lengkap, dan server memperbarui nama contact pada transaksi yang sama dengan
    data employee, komunikasi dan alamat, memakai pemeriksaan versi contact yang sudah ada.
  - Nama tidak boleh kosong (di-trim); panjang mengikuti batas kolom nama contact.
  - Perubahan masuk audit employee/contact yang sudah ada.
  - Hak yang dibutuhkan sama dengan menyimpan employee (claim BMM); tidak ada claim terpisah.
- Desain: `doc/design/BusinessModuleManager.figcraft`, page `20 Employee Editor` (field Contact full name dan
  frame New employee).

## Head of department dari Employee Editor (2026-11-05)

- Keputusan (pengguna): **kepala department juga bisa ditetapkan dari Employee Editor**, selain dari field
  Head di halaman Departments.
- Desain: checkbox **Head of department** di bawah Department, dengan keterangan kepala saat ini (mis.
  "current head: Rina Halim (EMP-0042)"). Dicentang lalu Save = employee ini menjadi kepala department yang
  dipilih, menggantikan kepala sebelumnya. Centang dilepas lalu Save = kepala department dikosongkan.
- Usulan aturan untuk plan:
  - Checkbox hanya aktif bila department terisi dan employee aktif (sama dengan syarat kepala yang ada).
  - Mengganti kepala lain menampilkan konfirmasi sebelum Save, seperti konfirmasi pindah department kepala.
  - Pindah department sambil mencentang: kepala department lama dikosongkan dan employee menjadi kepala
    department baru, dalam satu transaksi.
  - Daftar Employees tetap menampilkan chip HEAD dari data department; tidak ada sumber kedua.
- Pertanyaan: chip HEAD di kolom STATUS daftar Employees dipindah ke kolom Department (usulan), atau diganti
  label DEPT HEAD?

## Note pada Employee Editor (2026-11-05)

- Pengguna menanyakan Note di Employee Editor. Aplikasi sudah punya field **Note** (catatan employee, maks.
  500 karakter) setelah Position; yang belum ada adalah di mockup.
- Mockup diperbaiki: field Note multi-baris ditambahkan setelah Department, sebelum Active employee, di semua
  frame Employee Editor (Communications, Addresses, New employee; Light/Dark). Note ini milik employee per
  tenant, bukan catatan contact.
