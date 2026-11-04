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
