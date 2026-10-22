# Plan — Folder settings CDN (`.meta` per folder), Rename, Copy (untuk Codex)

Status: **belum dieksekusi**
Laporan: `doc/report/cdn-folder-settings.md` (dibuat saat eksekusi)
Dibuat: 2026-10-22
Pembaca: Codex. Baca juga `CLAUDE.md` di root repo ini dan `CLAUDE.md` subfolder pada jalur berkas yang
disentuh. Semua keputusan di bawah adalah keputusan pengguna kecuali ditandai **(default plan)**; bila selama
eksekusi muncul hal yang tidak tercakup, ambil pilihan yang paling konsisten dengan plan ini dan catat di
laporan, jangan berhenti untuk bertanya.

---

## 1. Tujuan

CDN mendapat **setelan per folder** yang disimpan di berkas `.meta` di dalam folder itu, mirip `.htaccess`.
Berkas `.meta` tidak pernah terlihat dan **tidak pernah bisa diakses lewat URL**. Setelan versi pertama
mengatur listing web publik: mematikan listing folder, serta menyembunyikan berkas/subfolder tertentu dari
listing sementara direct link tetap dilayani.

Pemakai pertama adalah Release Manager: folder rilis tidak boleh memperlihatkan komponen aplikasi di
listing web, supaya tidak ada website yang bisa mengindeks komponen yang dipakai. Berkas hanya bisa diambil
lewat direct link.

Plan ini juga menambahkan action **Rename** dan **Copy** ke CDN, karena aturan `.meta` mencakup keduanya dan
keduanya belum ada.

**Di luar cakupan:** inheritance setelan ke subfolder (masih ide, belum diputuskan). Setelan hanya berlaku
untuk folder tempat `.meta` berada.

## 2. Aturan kerja (untuk Codex)

- **Repo publik dan netral produk.** Jangan menulis nama produk/host turunan, modul bisnis privat, alamat
  server, nama database, atau secret di kode maupun dokumen.
- Komentar kode, XML doc, teks UI, pesan error/exception/log: **bahasa Inggris**. Plan, laporan,
  `doc/ideas/` dan `CLAUDE.md`: Bahasa Indonesia.
- **WPF wajib MVVM** sesuai `CLAUDE.md` (view model turunan `MvvmModelBase`, properti `Get/Set` publik,
  command lewat `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, XAML
  `Command="{Binding Commands[XxxCommand]}"`). Ikuti material design language dan standar kerapian UI WPF
  (acuan UserManager); pakai token di `Styles/MaterialDesign.xaml`, jangan membuat gaya baru per layar.
  Field form yang aturannya tidak jelas dari label diberi help tooltip.
- Kode yang bisa berjalan di luar UI thread harus di-marshal ke UI thread.
- Penamaan action mengikuti konvensi `Meta` yang ada (`GetMeta_`, `PostMeta_`, `PostGetMeta_`). Action GET
  hanya menerima parameter sederhana; objek/array lewat POST.
- **Urutan kerja:** tulis seluruh kode Tahap 1 dan Tahap 2 lebih dulu; build, test terfilter, dan review
  dijalankan sekali di akhir (Tahap 3). Build ringan per tahap boleh bila murah.
- **Commit:** periksa branch aktif harus `work-bench` (bila bukan, jangan commit dan catat di laporan). Satu
  commit per tahap setelah kode tahap itu selesai, plus satu commit untuk perbaikan hasil uji/guide/laporan.
  Pesan commit boleh bahasa Inggris. Stage hanya path yang diubah plan ini; jangan `git add -A`, jangan
  `--no-verify`, **jangan push**.
- **Tidak ada tes penuh dan tidak ada uji visual/GUI** (keputusan pengguna); pengguna memeriksa sendiri.
  Test yang dirawat masuk `tests/` (xUnit v3/MTP).
- Tidak ada perubahan skema database; tidak ada skrip SQL dan tidak ada key `ta_Meta` baru.
- Bila perintah ditolak policy: jangan memutar lewat wrapper lain; ikuti aturan "Tindakan terblokir policy"
  di `CLAUDE.md`, kerjakan bagian lain, dan catat di laporan.

## 3. Kondisi sekarang (titik awal)

- `CdnStore` (`src/backend/Em.Api.Core/Api/Core/CdnStore*.cs`) melayani root publik `files/`, dengan
  `temp/`, `recycle/`, `settings/` (`managed.json`, `cdn.json`) di luar root publik.
- **Berkas berawalan titik, hidden, dan system sudah publik** (komentar `VisibleEntries` di `CdnStore.cs`):
  tampil di listing, dihitung ukuran, ikut archive, dan dilayani `/cdn`. `ValidateName` menerima `.meta`.
  `ValidateName` sudah menolak nama berakhiran titik/spasi, karakter tidak sah, dan nama reserved Windows.
- Listing web publik: `CdnFileProvider` (`GetDirectoryContents`, `GetFileInfo`) dan `CdnDirectoryFormatter`.
  Unduhan memakai stream yang dilacak (`Download`/`OpenRead`), termasuk Range.
- Operasi store: `List`, `CountInside`, `Tree` (dipakai sync Release Manager), `StorageSize`/`SizeOf`
  (kuota managed folder), `Move` (memindahkan ke folder lain dengan nama tetap, `overwrite` untuk berkas),
  `UploadAsync`, `CreateFolder`, `Delete` → `Retire` (memindahkan ke `recycle/<id>/payload/`, atau dibuang
  langsung bila oleh pemilik managed folder), `Restore`, `ArchiveAsync`/`CollectArchiveItems`,
  `ReadExtractPlan`/`ExtractAsync` (business task dengan progres).
- **Belum ada action Rename dan Copy.** Move di UI berjalan lewat drag-drop (`DropTarget.Effect = Move`,
  `PostGetMeta_CdnMove`).
- Managed folder: `EnsureWritable(path)` tanpa `appId` menolak (409) penulisan ke managed folder **dan**
  folder yang berisi managed folder dari CDN Manager; dengan `appId` hanya pemilik yang boleh menulis.
- Release Manager menulis lewat `ICdnReleaseServices` (claim Release Manager) dari
  `src/shared/Em.Ui.Wpf.Core/Release/CdnReleaseTarget.cs`. Folder rilis berisi `release.json`,
  `release.json.sig`, dan `binaries/` (bisa bersarang). `GetMaxFileSizeAsync` memanggil
  `PostGetMeta_CdnReleaseManage` (konversi menjadi managed folder) sebelum Sync; `ListBinariesAsync` memakai
  `GetMeta_CdnReleaseTree`.
- CDN Manager WPF: `src/shared/Em.Ui.Wpf.Core/Navigations/CdnManager.xaml(.cs)`. Pola toggle tampilan yang
  ditiru: **Show untagged** di Container Manager (`ContainerManagerVm.Containers.cs`, `ShowUntagged`).
- Test yang ada: `tests/Em.Api.Core.Tests/CdnStoreTests.cs`, `CdnRuntimeServiceTests.cs`.

## 4. Keputusan final

### 4.1 Penyimpanan, visibilitas, dan akses URL

1. Setelan folder disimpan di berkas bernama persis `.meta` di dalam folder itu. Nama dibandingkan dengan
   `PathComparison` (tidak peka huruf besar di Windows).
2. `.meta` **tidak pernah terlihat**: tidak muncul di listing web publik, `List`/`Tree`/`CountInside` CDN
   Manager dan Release Manager (juga saat **Show hidden files** menyala), dan tidak dihitung di
   `StorageSize`/`SizeOf`/kuota.
3. **`.meta` dipastikan tidak bisa diakses lewat URL** (penegasan pengguna). Semua jalur `/cdn` (GET, HEAD,
   Range, listing direktori) menjawab 404 untuk `.meta` di folder mana pun. Pemeriksaan dilakukan terhadap
   **nama berkas sebenarnya di disk** setelah path di-resolve, bukan hanya teks URL, supaya varian berikut
   juga 404: huruf besar (`.META`), URL ter-encode (`%2emeta`, `%2Emeta`), titik/spasi di akhir
   (`.meta.`, `.meta%20`), alternate data stream (`.meta::$DATA`), dan nama pendek 8.3 Windows (mis.
   `META~1`) bila volume mendukungnya. Action manager mana pun yang menerima path (`Move`, `Delete`,
   unduhan, `Tree`, dll.) juga menolak path yang menunjuk `.meta` dengan 404.
4. **Folder bernama `.meta`** (create, rename, extract, copy target) ditolak 400.
5. Pengunggah tidak bisa membuat `.meta` sebagai berkas biasa: setiap jalur tulis bernama `.meta` diproses
   sebagai setelan (4.3).

### 4.2 Isi setelan versi 1

```json
{
  "version": 1,
  "publicListing": false,
  "hiddenFiles": ["*"],
  "shownFiles": ["release.json", "release.json.sig"]
}
```

- `version`: wajib, harus `1`.
- `publicListing` (bool, opsional, default `true`): `false` membuat listing web folder itu 404; direct link
  ke berkas tetap dilayani.
- `hiddenFiles` / `shownFiles` (array string, opsional, default kosong): pola nama untuk entri **langsung** di
  folder itu (berkas dan subfolder). `*` = urutan karakter apa pun, `?` = satu karakter; dibandingkan dengan
  `PathComparison`. Pola tidak boleh kosong, tidak boleh berisi `/` atau `\`, dan selain `*`/`?` hanya
  karakter yang sah untuk nama berkas. `shownFiles` mengalahkan `hiddenFiles`. Entri tersembunyi tidak tampil
  di listing web, tetapi tetap dilayani `/cdn` lewat URL tepat. Subfolder tersembunyi tetap bisa di-listing
  lewat URL-nya sendiri kecuali subfolder itu punya `.meta` dengan `publicListing: false` (tanpa
  inheritance).
- Validasi **(default plan)**: JSON UTF-8 (BOM boleh), ukuran berkas ≤ 64 KiB, key yang tidak dikenal
  ditolak, paling banyak 256 pola per daftar, panjang pola ≤ batas panjang nama CDN. Pesan error menyebut
  letak kesalahan (key/indeks pola).
- Engine selalu menyimpan bentuk kanonik hasil serialisasi ulang (camelCase, indented), bukan teks mentah
  kiriman. Export mengembalikan bentuk kanonik yang sama, dan bentuk itu selalu lolos validasi.
- Model kontrak di `Em.Libs` (mis. `CdnFolderSettings`) beserta validator dan serializer bersama yang dipakai
  server dan client.

### 4.3 Cara setelan masuk

Semua jalur di bawah **mengganti setelan lama secara utuh** (tidak digabung) dan berlaku seketika. Penulisan
`.meta` atomik (berkas temp di `temp/` lalu move/replace), di bawah `_gate`, dan tunduk pada
`EnsureWritable`: pada managed folder hanya pemiliknya (Release Manager dengan `appId`) yang boleh menulis;
dari CDN Manager setelan managed folder **hanya bisa dibaca**.

1. **Dialog Folder Settings** di CDN Manager (4.6).
2. **Upload berkas bernama `.meta`**: isi dibaca (batas 64 KiB), divalidasi. Valid → menjadi setelan folder
   tujuan, flag `overwrite` diabaikan. Tidak valid → 400, setelan lama tetap. Hasil action mengembalikan
   entri folder tujuan **(default plan)**.
3. **Rename berkas menjadi `.meta`**: isi berkas divalidasi. Valid → menjadi setelan folder itu dan berkas
   asal dihapus (bukan ke recycle bin). Tidak valid → 400, berkas tetap dengan nama lama.
4. **Extract archive**: setiap entri `.meta` divalidasi. Valid → diterapkan sebagai setelan folder tempat
   entri itu berada dan selalu menggantikan setelan yang ada, tanpa memandang flag overwrite extract.
   Tidak valid → hanya entri itu dilewati; berkas
   lain tetap diekstrak. Pesan akhir/progres business task extract menyebut entri `.meta` yang dilewati
   beserta alasannya. `ReadExtractPlan` tidak menghitung `.meta` sebagai konflik.
5. **Release Manager** lewat action release baru (4.7).

### 4.4 Operasi lain terhadap `.meta`

1. **Move folder** membawa `.meta` (sudah otomatis karena berkasnya di dalam folder).
2. **Copy folder** membawa `.meta` beserta setelannya, termasuk `.meta` di subfolder.
3. **Archive** tidak menyertakan `.meta` (`CollectArchiveItems` melewatinya di semua tingkat).
4. **Recycle bin melewati `.meta`**: sebelum folder dipindahkan ke recycle bin (`Retire`), semua `.meta` di
   dalam pohon folder itu dihapus permanen. Folder yang di-restore kembali **tanpa setelan**. Ukuran item
   recycle bin tidak menghitung `.meta`.
5. **Sync Release Manager**: `Tree` tidak memuat `.meta`, sehingga sync tidak pernah mencoba menghapus atau
   membandingkannya.

### 4.5 Listing web publik dan header

1. `CdnFileProvider.GetDirectoryContents` untuk folder dengan `publicListing: false` mengembalikan
   not-found (404 dari middleware listing).
2. Listing folder yang menyala menyaring `.meta` dan entri yang cocok `hiddenFiles` tanpa cocok `shownFiles`.
3. Respons berkas yang berada **langsung** di folder dengan `publicListing: false` otomatis membawa header
   `X-Robots-Tag: noindex` (lewat `OnPrepareResponse` static files atau titik setara). Tidak ada setelan
   terpisah untuk header ini. Folder yang listing-nya menyala tidak mendapat header.
4. Cache setelan per folder di memori, dikunci path folder dan waktu tulis `.meta` (stat murah per request,
   aman bila berkas diubah manual di disk) **(default plan)**. Penulisan oleh engine langsung membatalkan cache
   folder terkait; move/delete/copy folder membatalkan cache subtree terkait.
5. **`.meta` lama yang tidak valid** (sudah ada sebelum fitur ini, karena berkas bertitik sebelumnya publik):
   tetap tersembunyi dan 404; isinya dianggap tidak ada (default semua setelan) dan dicatat sebagai warning
   log sekali per versi berkas. Tidak ada scan saat startup.

### 4.6 CDN Manager (WPF)

1. Tombol **Folder Settings** untuk folder yang sedang dibuka (toolbar/header, mengikuti penempatan tombol
   yang ada). Membuka dialog bergaya material:
   - toggle **Public listing**, daftar pola **Hidden files** dan **Shown files** (tambah/hapus/edit baris),
     help tooltip yang menjelaskan pola `*`/`?`, prioritas Shown, dan bahwa direct link tetap dilayani;
   - **Save** (validasi server; error ditampilkan di dialog), **Export** (simpan `.meta` kanonik lewat Save
     File dialog; nonaktif bila folder belum punya setelan atau setelannya tidak valid), **Cancel**;
   - bila `.meta` yang ada tidak valid: peringatan di dialog, field berisi nilai default, Save menimpanya;
   - managed folder: dialog **hanya-baca** dengan keterangan bahwa setelan dikelola Release Manager.
2. Opsi **Show hidden files** (default mati), meniru **Show untagged**. Mati → baris yang tersembunyi dari
   listing publik tidak ditampilkan. Nyala → ikut tampil dengan penanda (teks redup dan ikon mata dicoret,
   tooltip "Hidden from public listing"). Hanya tampilan; ukuran, copy, move tetap mencakup berkas
   tersembunyi. `CdnEntry` mendapat flag (mis. `IsHidden`) dari server; `.meta` tidak pernah dikirim.
   Nilai toggle disimpan per pengguna lewat `EmApp.BaseRegKey` bila manager lain menyimpan preferensi
   serupa; bila tidak, cukup per sesi **(default plan)**.
3. **Rename** untuk berkas dan folder: perintah per baris (ikuti pola perintah baris yang ada) dan tombol
   `F2`, membuka dialog nama baru. Rename berkas ke `.meta` memakai aturan 4.3.3; konfirmasi singkat
   menjelaskan bahwa berkas akan menjadi setelan folder dan hilang dari daftar.
4. **Copy**: drag-drop dengan **Ctrl** menyalin (konvensi Explorer; tanpa Ctrl tetap Move), plus perintah
   per baris **Copy to…** yang memakai pemilih folder tujuan bila komponen pemilihnya sudah ada; bila belum
   ada, cukup Ctrl+drag dan catat di laporan **(default plan)**. Konflik nama memakai alur konfirmasi
   overwrite yang sama dengan Move. Progres memakai dialog business task yang dipakai Archive/Extract.

### 4.7 Action dan kontrak

Nama final boleh disesuaikan dengan pola file yang ada; catat di laporan bila berbeda.

| Action | Claim | Isi |
| --- | --- | --- |
| `GetMeta_CdnFolderSettings(string? path)` | CDN Manager | Detail: setelan efektif, ada/tidaknya `.meta`, warning bila tidak valid, flag read-only (managed). |
| `PostMeta_CdnFolderSettingsSave(CdnFolderSettingsSave request)` | CDN Manager | Path + setelan; validasi; tulis kanonik. |
| `GetMeta_CdnFolderSettingsExport(string? path)` | CDN Manager | Teks `.meta` kanonik; 404 bila belum ada/tidak valid. |
| `PostGetMeta_CdnRename(string path, string newName)` | CDN Manager | Rename berkas/folder di folder yang sama. |
| `PostGetMeta_CdnCopy(CdnCopyRequest request)` | CDN Manager | Path sumber, folder tujuan, overwrite; business task seperti Archive (beserta action status/cancel/clear mengikuti pola Archive). |
| `PostMeta_CdnReleaseFolderSettings(CdnReleaseFolderSettingsRequest request)` | Release Manager | `AppId`, path, setelan; `EnsureWritable(path, appId)`. |

- Kontrak di `ICdnServices` / `ICdnReleaseServices` (`Em.Libs`), implementasi server di `CdnServices` /
  `CdnReleaseServices`, implementasi client mengikuti pola action CDN yang ada.
- **Rename**: nama baru divalidasi `ValidateName`; nama sama → tanpa perubahan; konflik → 409; guard sama
  dengan Move (`EnsureWritable` sumber, nama reserved root, root CDN tidak bisa di-rename). Rename folder yang
  merupakan atau berisi managed folder ditolak 409 seperti penulisan lain dari CDN Manager.
- **Copy**: berkas atau folder (rekursif, termasuk `.meta`); tujuan di dalam sumber ditolak; tujuan managed
  folder ditolak dari CDN Manager (`EnsureWritable`); salinan berkas dengan nama sama di folder yang sama → 409
  (tidak ada penamaan otomatis "copy of"); overwrite berkas memindahkan berkas lama ke recycle bin seperti
  Move; overwrite folder yang sudah ada ditolak 409 seperti Move. Batas max file size tidak berlaku (bukan
  upload), tetapi ruang disk dicek seperti extract bila ada pemeriksaan serupa. Penulisan lewat folder temp
  lalu move ke tujuan, supaya salinan setengah jadi tidak terlihat publik.

### 4.8 Release Manager

1. `CdnReleaseTarget` menulis `.meta` dengan `publicListing: false` (tanpa daftar pola) ke **setiap folder di
   pohon rilis**: folder rilis, `binaries/`, dan semua subfolder di dalam `binaries/`. Alasan: setelan tidak
   diwariskan, sehingga subfolder `binaries/` yang bersarang tetap bisa di-listing bila tidak diberi `.meta`
   sendiri **(default plan, konsisten dengan tujuan pengguna: komponen tidak terindeks)**.
2. Ditulis saat folder dikonversi menjadi managed folder dan **ditulis ulang setiap Sync**, setelah berkas
   tersinkron (folder baru ikut mendapat `.meta`). Perubahan manual tertimpa Sync berikutnya.
3. Hanya target CDN; `FolderReleaseTarget` (folder lokal) tidak menulis `.meta`.
4. `release.json` dan `release.json.sig` tetap dilayani lewat URL tetap yang dipakai launcher.

## 5. Tahapan

### Tahap 1 — Engine backend dan kontrak (`Em.Libs`, `Em.Api.Core`)

1. Model setelan, validator, serializer kanonik, dan DTO action di `Em.Libs`; flag hidden di `CdnEntry`;
   anggota baru `ICdnServices`/`ICdnReleaseServices` dengan XML comment bahasa Inggris.
2. `CdnStore`: baca/tulis/cache setelan; penyaringan `.meta` di semua enumerasi (`List`, `Tree`,
   `CountInside`, `SizeOf`/`StorageSize`, archive, recycle); penolakan path `.meta` (4.1.3) di
   `ResolveInside` atau titik yang dipakai semua jalur; upload/extract/rename `.meta` sebagai setelan;
   `Retire` membuang `.meta`; Rename; Copy (business task).
3. `CdnFileProvider`/`CdnDirectoryFormatter`: 404 listing `publicListing: false`, penyaringan entri
   tersembunyi, 404 `.meta`; header `X-Robots-Tag: noindex`.
4. Action server baru di `CdnServices`/`CdnReleaseServices`.
5. Commit tahap 1.

### Tahap 2 — Client WPF (`Em.Ui.Core`/`Em.Ui.Wpf.Core`)

1. Implementasi client action baru.
2. CDN Manager: Folder Settings dialog (dengan Export), Show hidden files + penanda, Rename (`F2`), Copy
   (Ctrl+drag, Copy to… bila pemilih folder ada).
3. `CdnReleaseTarget`/alur Sync: tulis `.meta` sesuai 4.8.
4. Commit tahap 2.

### Tahap 3 — Uji, guide, laporan

1. Build: `dotnet build src/backend/Em.Api.slnx`, `dotnet build src/frontend/Em.Ui.Wpf.slnx`,
   `dotnet build src/frontend/Em.Ui.Maui.slnx` (kontrak `Em.Libs` berubah).
2. Test xUnit terfilter (tambah di `tests/Em.Api.Core.Tests`, jalankan hanya test CDN), minimal:
   - validasi setelan: valid, version salah, key asing, pola berisi `/`, pola kosong, melebihi batas, >64 KiB;
     bentuk kanonik round-trip;
   - **akses URL `.meta` selalu 404**: `.meta`, `.META`, `%2emeta`, `.meta.`, `.meta%20`, `.meta::$DATA`,
     HEAD, Range, serta nama pendek 8.3 (skip bila volume test tidak punya nama pendek); test lewat
     `CdnFileProvider` dan, bila ada fixture HTTP CDN yang murah, lewat pipeline HTTP;
   - listing: `publicListing: false` → 404, berkas di folder itu tetap terlayani dengan header
     `X-Robots-Tag: noindex`; pola hidden/shown (prioritas shown, `?`, tidak peka huruf besar di Windows);
     folder tanpa `.meta` tanpa header;
   - `List`/`Tree`/`CountInside`/ukuran tidak memuat `.meta`; `IsHidden` terisi;
   - upload `.meta` valid mengganti setelan, tidak valid 400 dan setelan lama utuh; folder bernama `.meta`
     ditolak;
   - rename berkas → `.meta` valid/tidak valid; rename biasa, konflik 409, managed folder 409;
   - copy berkas/folder membawa `.meta`; tujuan di dalam sumber ditolak;
   - archive tanpa `.meta`; extract menerapkan `.meta` valid dan melewati yang tidak valid sambil
     meneruskan berkas lain;
   - delete folder → recycle tanpa `.meta`; restore tanpa setelan;
   - managed folder: CDN Manager tidak bisa menyimpan setelan (409), pemilik lewat action release bisa;
   - `.meta` lama tidak valid: tersembunyi, 404, diperlakukan default.
3. Tidak ada tes penuh dan tidak ada uji GUI; catat verifikasi visual dialog Folder Settings, toggle Show
   hidden files, Rename, dan Copy sebagai **tertunda (diperiksa pengguna)** di laporan.
4. Guide: perbarui `doc/engine/engine-cdn-storage.md` (format `.meta`, aturan visibilitas dan URL, jalur
   masuk setelan, interaksi archive/extract/recycle/copy/move, Rename, Copy, header noindex, catatan tanpa
   inheritance) dan guide Release Manager yang menjelaskan Sync ke CDN (penulisan `.meta` otomatis). Bila
   guide CDN menyebut berkas bertitik selalu publik, perbarui bagian itu.
5. Laporan `doc/report/cdn-folder-settings.md`: hasil build/test, keputusan **(default plan)** yang dipakai,
   penyimpangan nama action, tindakan terblokir, dan verifikasi tertunda.
6. Pindahkan plan ini ke `plan/executed/` dengan status selesai dan tautan laporan.
7. Commit perbaikan/guide/laporan.

## 6. Kriteria selesai

- Ketiga solution build tanpa error; test CDN terfilter lulus.
- Tidak ada jalur URL `/cdn` yang mengembalikan isi `.meta`, dibuktikan oleh test 4.1.3.
- Folder rilis yang di-Sync Release Manager: listing folder rilis, `binaries/`, dan subfolder-nya 404;
  `release.json` dan berkas `binaries/` tetap terunduh lewat URL tepat dengan header noindex.
- Guide dan laporan diperbarui; plan dipindah ke `plan/executed/`; commit di `work-bench` tanpa push.
