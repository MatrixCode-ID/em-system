# Plan — Max file size CDN runtime dan toggle service fitur sistem (untuk Codex)

Status: **selesai dieksekusi 2026-10-22 — build API/WPF/MAUI dan 12 tes terfilter lulus; fungsional/GUI diperiksa pengguna**
Laporan: [cdn-max-file-dan-toggle-service](../../doc/report/cdn-max-file-dan-toggle-service.md)
Dibuat: 2026-10-22
Pembaca: Codex. Baca juga `CLAUDE.md` di root repo ini dan `CLAUDE.md` subfolder pada jalur berkas yang
disentuh. Semua keputusan di bawah adalah keputusan pengguna; bila selama eksekusi muncul hal yang tidak
tercakup, ambil pilihan yang paling konsisten dengan plan ini dan catat di laporan, jangan berhenti untuk
bertanya.

---

## 1. Tujuan

Dua perubahan engine, dikerjakan dalam satu eksekusi dengan dua tahap:

1. **Tahap 1 — Max file size CDN sebagai setelan runtime.** Batas ukuran per berkas CDN bisa diubah dari
   CDN Manager kapan saja, tanpa restart dan tanpa managed storage settings. Kartu Settings dibuang dari
   CDN Manager.
2. **Tahap 2 — Standar toggle service fitur sistem.** CDN, NuGet feed (NuPak), Object Storage, dan
   Container Registry mendapat toggle enable/disable **service** di kanan atas layar manager-nya, berlaku
   langsung, khusus admin.

Latar belakang: host yang memakai `EnableCdn` statis menampilkan kartu Settings yang hanya bisa dibaca
("static host configuration", Save ditolak 409), sehingga batas file hanya bisa diubah lewat kode/env dan
restart. Managed storage settings tidak bisa dijadikan jalan keluar karena ikut menyalakan dependensi
container registry.

## 2. Aturan kerja (untuk Codex)

- **Repo publik dan netral produk.** Jangan menulis nama produk/host turunan, modul bisnis privat, alamat
  server, nama database, atau secret di kode maupun dokumen.
- Komentar kode, XML doc, teks UI, pesan error/exception/log: **bahasa Inggris**. Plan, laporan,
  `doc/ideas/` dan `CLAUDE.md`: Bahasa Indonesia.
- **WPF wajib MVVM** sesuai `CLAUDE.md` (view model turunan `MvvmModelBase`, properti `Get/Set` publik,
  command lewat `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, XAML
  `Command="{Binding Commands[XxxCommand]}"`). Ikuti material design language dan standar kerapian UI WPF
  (acuan UserManager); pakai token yang ada di `Styles/MaterialDesign.xaml`, jangan membuat gaya baru per
  layar. Input angka memakai `controls:NumericBox` dan field diberi help tooltip sesuai aturan form WPF.
- Kode yang bisa berjalan di luar UI thread harus di-marshal ke UI thread.
- **Urutan kerja:** tulis seluruh kode Tahap 1 dan Tahap 2 lebih dulu; build, test terfilter, dan review
  dijalankan sekali di akhir (bagian 6). Build ringan per tahap boleh bila murah.
- **Commit:** periksa branch aktif harus `work-bench` (bila bukan, jangan commit dan catat di laporan). Satu
  commit per tahap setelah kode tahap itu selesai, plus satu commit untuk perbaikan hasil uji/guide/laporan.
  Pesan commit boleh bahasa Inggris. Stage hanya path yang diubah plan ini; jangan `git add -A`, jangan
  `--no-verify`, **jangan push**.
- Tidak ada harness smoke untuk plan ini (lihat bagian 6). Test yang dirawat masuk `tests/` (xUnit v3/MTP).
- **Tidak ada tes penuh dan tidak ada uji visual/GUI** (keputusan pengguna); pengguna memeriksa sendiri.
- Bila perintah ditolak policy: jangan memutar lewat wrapper lain; ikuti aturan "Tindakan terblokir policy"
  di `CLAUDE.md`, kerjakan bagian lain, dan catat di laporan.

## 3. Kondisi sekarang (titik awal)

- `CdnStore` (`src/backend/Em.Api.Core/Api/Core/CdnStore*.cs`) menerima `maxFileSize` di konstruktor dan
  memegangnya sebagai `MaxFileSize` tetap sejak startup (`EmApp.ApplyCdnRegistration`). Dipakai upload
  (413 bila lewat), extract (`MaxExtractTotalFactor = 10`, `MaxExtractEntries = 10_000`), dan
  `CdnFolderContent.MaxFileSize` (managed folder: `long.MaxValue`).
- Folder CDN berisi `files/`, `temp/`, `recycle/`, `settings/`; `settings/managed.json` ditulis atomik
  lewat berkas `.tmp` di `settings/` (`CdnStore.Management.cs`).
- Nilai bawaan: `EmAppBuilder.DefaultCdnMaxFileSizeMb = 20` (`EnableCdn(path)`), managed settings
  `MaxUploadMb = 200`, `EmApiConfig.Storage.CdnMaxFileSizeMb` default dari konstanta builder.
- `CdnManager.xaml` memuat `<controls:StorageSettingsCard x:Name="settingsCard" .../>`. Kartu yang sama
  juga dipakai `ContainerManager.xaml` dan `NuPakManager.xaml` (tab Settings) — **keduanya tidak diubah**.
- Header CDN Manager menampilkan chip "N folders · N files" dan chip `LimitCaption`
  ("Max … per file" / kuota managed folder), lalu tombol refresh.
- Fitur mati di host: CDN memakai `CdnStore.Disabled` dan `ICdnServices` tetap terdaftar (menjawab 404);
  registry `CtnBlobStore.Disabled`; NuPak hanya terdaftar bila `NuPakRegistered`; Object Storage lewat
  `AddObjectStorage` dengan endpoint publik `/store`.
- UI menyembunyikan navigasi yang claim/modulnya tidak dideklarasikan server
  (`NavigationAccess.IsDeclared`, juga untuk admin dan mode debug).
- Claim yang ada: `CDN Manager Access`, `CDN Settings Manage`, `NuGet Manager Access`,
  `NuGet Settings Manage`, `Object Storage Manager Access`, `Object Storage Settings Manage`,
  `Container Manager Access`, `Container Registry Settings Manage`.

## 4. Tahap 1 — Max file size CDN runtime

### Keputusan final

- Max file size menjadi **setelan runtime CDN** yang disimpan di folder `settings/` CDN (mis.
  `settings/cdn.json`, ditulis atomik seperti `managed.json`), diubah dari CDN Manager, berlaku **tanpa
  restart** dan **tanpa managed storage settings**.
- `CdnStore` membaca batas dari setelan pada setiap upload/extract (cache di memori, diperbarui saat Save),
  sehingga perubahan berlaku untuk upload berikutnya. Upload/extract yang sedang berjalan memakai batas saat
  operasi itu dimulai.
- **Nilai dari host** (`EnableCdn(path, mb)`, `EmApiConfig.Storage.CdnMaxFileSizeMb`, atau `MaxUploadMb`
  CDN pada managed storage settings) hanya menjadi **nilai awal** sampai admin menyimpan setelan dari UI;
  setelah berkas setelan ada, nilai berkas yang berlaku.
- **Batas extract ikut setelan:** tiap entri ≤ max file, total ≤ 10× max file; batas 10.000 entri tetap.
- **Nilai bawaan engine diseragamkan 200 MB** untuk semua jalur (`DefaultCdnMaxFileSizeMb = 200`, default
  config dan managed settings). Perbarui XML doc/guide yang menyebut 20 MB.
- **Claim pengubah: `CDN Manager Access`.**
- Managed folder tetap tidak terkena batas per berkas; batasnya kuota folder (perilaku sekarang).
- **Kartu Settings dibuang dari CDN Manager di semua host**, termasuk host dengan managed storage
  settings. Path dan enable fitur CDN cukup dari konfigurasi host. Action/claim managed storage settings
  yang ada **tidak dihapus** (masih dipakai Container Manager dan NuPak Manager).

### UI

- Di chip "Max … per file" pada header CDN Manager ditambahkan **ikon pensil** (hanya saat folder aktif
  bukan managed folder dan user punya hak). Klik pensil mengubah chip menjadi `NumericBox` (MB, bilangan
  bulat positif) dengan tombol **Save** dan **Cancel** selama mode edit. Save memanggil action baru dan
  memperbarui chip; Cancel mengembalikan nilai lama tanpa memanggil server. Help tooltip menjelaskan arti
  dan rentang nilai.
- Saat folder aktif adalah managed folder, chip menampilkan kuota seperti sekarang tanpa pensil max file.

### Backend

- Action baru di `ICdnServices` (claim `CDN Manager Access`), mengikuti konvensi nama: mis.
  `PostMeta_CdnMaxFileSize(int maxFileSizeMb)`. Validasi rentang (1 sampai batas atas wajar yang ditetapkan
  di kode dan dicatat di guide); tulis audit seperti perubahan setelan CDN lain bila pola audit ada.
- Nilai yang berlaku tetap dibawa ke client lewat `CdnFolderContent.MaxFileSize` (sudah ada).

## 5. Tahap 2 — Standar toggle service fitur sistem

### Keputusan final

Dua prinsip yang terpisah:

1. **Fitur (di API/host):** konfigurasi host menyalakan/mematikan fitur (`EnableCdn`, `AddNuPak`,
   `AddObjectStorage`, `AddContainerRegistry` atau managed storage settings). Bila fitur **mati**, layar
   manager-nya **tidak ada di UI** dan semua aksesnya 404. Caranya: server tidak mendeklarasikan claim
   manager fitur itu, sehingga `NavigationAccess.IsDeclared` menyembunyikan navigasi dan kartu home-nya.
   Periksa perilaku sekarang per fitur; tambahkan hanya yang belum.
2. **Service (di UI):** bila fitur hidup, layar manager menyediakan **toggle enable/disable service di
   kanan atas** layar (pada kartu header, sebaris dengan tombol refresh). Toggle berlaku **langsung tanpa
   restart**.

- **Cakupan:** CDN, NuGet feed (NuPak), Object Storage, Container Registry. Bangun sebagai mekanisme
  engine yang seragam (satu kontrak status service + satu kontrol/style toggle yang dipakai keempat layar),
  bukan empat implementasi lepas.
- **Yang boleh menekan toggle: admin saja** (`CallerIsAdmin`); pemegang claim manager bukan admin hanya
  melihat status.
- **Nilai awal: enabled** saat fitur pertama kali hidup.
- **Penyimpanan status per fitur di data milik fitur itu:** CDN di `settings/` folder CDN; fitur lain di
  tempat setelan miliknya (folder data atau setelan database fitur tersebut), agar ikut terbawa bersama
  datanya. Pilih lokasi yang paling konsisten dengan penyimpanan setelan fitur itu sekarang dan catat di
  guide.
- **Saat service disabled:**
  - Akses layanan dari luar menjawab **404**: `/cdn/...`, feed `/nuget`, `/v2` registry, `/store` object
    storage.
  - **Action pengelolaan di layar manager tetap jalan** untuk pemegang claim (browse, upload, dsb.), agar
    admin bisa memeriksa dan menyalakan kembali.
  - **Pemakaian internal oleh modul di dalam API juga mati** (mis. `IObjectStore` untuk lampiran). Pemanggil
    menerima exception khusus engine yang menjadi **503** dengan pesan jelas "`<service>` is disabled".
    Tujuannya off switch sementara saat ada masalah pada aplikasi pemakainya.
- Layar manager menampilkan status disabled dengan jelas (mis. chip/teks status di header) selain posisi
  toggle.

### Backend

- Satu kontrak status service untuk keempat fitur (mis. `GetMeta_<Feature>ServiceState` /
  `PostMeta_<Feature>ServiceState(bool enabled)` per service fitur, atau satu service bersama —
  pilih yang paling konsisten dengan konvensi action di repo dan catat alasannya di laporan). Action
  tulis menolak non-admin (403).
- Endpoint publik dan jalur internal membaca status dari satu sumber di memori yang diperbarui saat toggle
  disimpan; tidak ada restart.
- Satu instance API per folder/database fitur (batasan yang sudah tercatat); tidak perlu sinkronisasi lintas
  proses.

## 6. Verifikasi (sekali di akhir)

**Keputusan pengguna 2026-10-22: tidak ada tes penuh karena boros token, dan tidak ada tes GUI. Pengguna
akan memeriksa sendiri.** Aturan ini mengesampingkan aturan verifikasi umum di `CLAUDE.md` untuk plan ini.

- Build saja: `dotnet build src/backend/Em.Api.slnx`, `dotnet build src/frontend/Em.Ui.Wpf.slnx`, dan
  `dotnet build src/frontend/Em.Ui.Maui.slnx` (MAUI tidak punya layar manager ini; pastikan kontrak bersama
  tetap terpenuhi). Perbaiki error kompilasi sampai lolos.
- Tambahkan unit test kecil untuk logika inti (setelan max file: nilai awal dari host, berkas setelan
  menang, berlaku tanpa membuat ulang store; status service: 404 publik, 503 internal, hanya admin), lalu
  jalankan **hanya test class yang ditambah/diubah plan ini** dengan filter, bukan seluruh solution.
- **Tidak menjalankan** `dotnet test` seluruh solution, smoke HTTP, maupun uji visual/GUI. Catat di laporan
  bahwa pemeriksaan fungsional dan visual dilakukan pengguna.

## 7. Dokumentasi dan penutupan

- Perbarui guide: `doc/engine/engine-cdn-storage.md`, `engine-storage-settings.md`, `engine-nupak.md`,
  `engine-object-storage.md` (+ `.id.md` bila isinya terkait), `engine-registry.md`; jelaskan max file
  runtime, kartu Settings yang tidak ada lagi di CDN Manager, dan standar toggle service (siapa, berlaku
  kapan, 404 vs 503, lokasi penyimpanan status).
- Tambahkan catatan singkat keputusan di `CLAUDE.md` root (pertahankan isi lama).
- Tulis laporan eksekusi di `doc/report/cdn-max-file-dan-toggle-service.md`: hasil build dan test terfilter, pilihan
  implementasi yang diambil sendiri, verifikasi yang tertunda.
- Pindahkan plan ini ke `plan/executed/` setelah selesai.
