# Plan: Control penyusun gambar di canvas (`ImageComposer`)

- **Tanggal:** 2026-10-29
- **Status:** belum dieksekusi; keputusan sudah final, menunggu perintah eksekusi pengguna.
- **Agent:** Codex (Claude Code boleh memakai file yang sama).
- **Repo:** hanya em-system. Catatan ide asal disimpan sementara di repo turunan privat (keputusan
  pengguna 2026-10-19: ide engine sementara di repo turunan). Konsumen pertama adalah editor dokumen
  di repo turunan; plan konsumennya ada di repo itu dan menautkan ke plan ini. Plan ini **tidak**
  menyebut nama produk, modul bisnis, server, atau database repo turunan (em-system repo publik).

Control baru berupa canvas seukuran area tertentu dalam mm. Pengguna memasukkan beberapa gambar
(paste dan insert), menyusunnya (move, resize, rotate, crop, z-order), dapat menambah panah anotasi,
lalu hasilnya di-flatten menjadi **satu PNG**. Penyimpanan dan validasi hasil milik layar pemakai.

---

## 1. Lokasi dan nama

| Hal | Nilai |
| --- | --- |
| Library | `src/shared/Em.Ui.Wpf.Core` (`Em.Ui.Wpf.Core.csproj`, `RootNamespace` = `Em.Ui.Wpf`) |
| Folder | `Controls/ImageComposer/` (berkas control, VM, item, geometri) |
| Namespace | **`Em.Ui.Wpf.Controls`** untuk semua tipe publik, sama seperti `EmTabStrip`, `NumericBox`, `FeatureServiceToggle`. Subfolder **bukan** segmen namespace: tulis `namespace Em.Ui.Wpf.Controls;` secara eksplisit dan tambahkan folder ke `Em.Ui.Wpf.Core.csproj.DotSettings` sebagai non-namespace-provider bila berkas itu sudah mengatur folder lain dengan cara yang sama (bila tidak ada, cukup namespace eksplisit). |
| Pemakaian XAML | `xmlns:controls="clr-namespace:Em.Ui.Wpf.Controls;assembly=Em.Ui.Wpf.Core"` → `<controls:ImageComposer … />` |
| Tes | `tests/Em.Ui.Wpf.Core.Tests/ImageComposerTests.cs` (xUnit v3, MTP) |
| Guide | `doc/engine/engine-image-composer.md`, ditautkan dari `doc/engine/README.md` |

Tipe publik yang dibuat (nama final; bila bentrok dengan tipe yang ada, tambahkan awalan `Em` dan catat):

- `ImageComposer` — `UserControl` (XAML + code-behind tipis).
- `ImageComposerVm` — turunan `MvvmModelBase`; seluruh state, command, geometri, dan flatten.
- `ComposerItem` (abstrak, turunan `MvvmModelBase`), `ComposerImageItem`, `ComposerArrowItem`.
- `ComposerArrowColor` (enum: `Red`, `Black`, `Blue`, `Yellow`), `ComposerTool` (enum: `Select`, `Arrow`, `Crop`).

---

## 2. Keputusan final

### Keputusan pengguna (2026-10-29)

| # | Topik | Keputusan |
| --- | --- | --- |
| K1 | Bentuk | User control **reusable di engine** `Em.Ui.Wpf.Core`, bukan logika satu layar. WPF saja; tidak ada padanan MAUI. |
| K2 | Isi | Satu canvas boleh berisi **banyak gambar**. |
| K3 | Masukan | **Paste** (clipboard) dan **Insert** (pilih file) saja, **tanpa drop**. Layar pemakai juga dapat memberi isi awal dari stream. |
| K4 | Simpan | Saat simpan, canvas **di-flatten** menjadi satu gambar. Susunan per item tidak disimpan; bila salah, pengguna **Clear** lalu menyusun ulang. |
| K5 | Ukuran canvas | **Properti + input bawaan.** `CanvasWidthMm`/`CanvasHeightMm` bisa di-bind (TwoWay) dari layar, dan control menyediakan input lebar × tinggi mm yang bisa diubah pengguna. Layar dapat **mengunci/menyembunyikan** input itu (`IsCanvasSizeEditable`). |
| K6 | Ubah ukuran saat ada isi | **Posisi dan ukuran mm item tetap**, diukur dari kiri-atas canvas. Bagian item di luar canvas terpotong di tampilan dan tidak ikut flatten. Bila canvas berisi **isi awal hasil flatten** tersimpan, ukuran tidak bisa diubah sampai **Clear** (input nonaktif, setter properti dari layar tetap diterima tetapi isi awal digambar memenuhi ukuran baru, lihat C7). |
| K7 | Toolbar | **Toolbar bawaan** di dalam control, **bisa disembunyikan** (`ShowToolbar`, default `true`). Semua command tetap bisa dipicu layar lewat `Commands[...]`. |
| K8 | Cakupan tambahan | **Crop** dan **panah anotasi** masuk cakupan. **Teks tidak.** Bukan image editor penuh (tanpa undo, filter, teks, gambar bebas). |
| K9 | Crop | **Permanen saat diterapkan.** Mode crop pada gambar terpilih; setelah **Apply**, piksel di luar area dibuang dari item itu. Tidak bisa dikembalikan selain menghapus item lalu insert ulang. **Cancel**/Esc membatalkan mode crop tanpa perubahan. |
| K10 | Panah | **Warna dan tebal bisa dipilih**: warna tetap Merah/Hitam/Biru/Kuning, tebal **0,5 / 1 / 2 mm**, satu ujung panah, titik awal dan akhir bisa digeser. |
| K11 | Usulan rincian | Semua usulan di catatan ide **diterima**: satuan mm (tidak bergantung DPI layar), `OutputDpi` default **300**, resize rasio terkunci (Shift = bebas), rotate snap **15°**, z-order, **Fit**, hapus satu item, **Clear**, peringatan resolusi efektif < **150 dpi**, isi awal hasil flatten tampil tetapi tidak bisa disusun ulang sampai Clear, `IsReadOnly` dan `HasChanges` bisa di-bind, latar canvas **putih** di tema terang maupun gelap, material design language dan token tema untuk chrome control. |
| K12 | Format keluaran | **PNG saja**, seukuran canvas pada `OutputDpi`. Tidak ada JPEG. |
| K13 | Mockup dan uji visual | **Tidak ada mockup**: pengguna memutuskan langsung membuat UI asli sebagai pengecualian untuk control engine ini. **Tanpa uji visual/render**: hanya build dan tes console; verifikasi visual dicatat **tertunda** di laporan. Jangan membuat atau menjalankan harness render. |

### Keputusan cadangan (tidak perlu ditanyakan saat eksekusi)

- **C1 — Kepemilikan VM.** `ImageComposer` punya DP `Composer` (tipe `ImageComposerVm`). Bila tidak di-bind,
  control membuat instance sendiri. Layar yang perlu flatten/memuat isi awal membuat `ImageComposerVm` di
  VM-nya lalu bind `Composer="{Binding Picture}"`, sehingga VM layar tidak memegang referensi control.
  DP lain pada control (`CanvasWidthMm`, `CanvasHeightMm`, `OutputDpi`, `IsReadOnly`, `IsCanvasSizeEditable`,
  `ShowToolbar`, `HasChanges` read-only) diteruskan ke/dari `Composer`. Ini merinci usulan "VM internal +
  `ElementName`": command tetap di VM (`RegisterCommand`) dan bisa dipicu lewat
  `{Binding Picture.Commands[ClearCommand]}` atau `{Binding Composer.Commands[ClearCommand], ElementName=…}`.
- **C2 — API publik `ImageComposerVm`** (nama final):
  - Properti: `CanvasWidthMm`, `CanvasHeightMm` (double, rentang **10–1000 mm**, default **100 × 100**),
    `OutputDpi` (int, 72–1200, default 300), `IsReadOnly`, `IsCanvasSizeEditable` (default `true`),
    `Items` (`ObservableCollection<ComposerItem>`, urutan = z-order, indeks terakhir paling atas),
    `SelectedItem`, `Tool` (`ComposerTool`), `ArrowColor` (default `Red`), `ArrowThicknessMm` (default 1),
    `HasInitialImage`, `IsEmpty`, `HasChanges` (setter publik agar layar bisa reset setelah simpan),
    `HasLowResolution` (ada item gambar < 150 dpi).
  - Method: `void LoadInitialImage(Stream png)` / overload `byte[]` (memuat hasil flatten tersimpan,
    `HasChanges = false`), `byte[] FlattenPng()`, `void InsertImage(Stream)` (dipakai Insert/Paste dan layar),
    `void MarkSaved()` (`HasChanges = false`).
  - Command (semua lewat `RegisterCommand(nameof(X), X, XAllowed)`, tidak diekspos sebagai properti):
    `InsertCommand`, `PasteCommand`, `DeleteCommand`, `BringToFrontCommand`, `SendToBackCommand`,
    `FitCommand`, `CropCommand` (masuk mode crop), `ApplyCropCommand`, `CancelCropCommand`, `ArrowToolCommand`
    (toggle tool panah), `ClearCommand`. Semua `...Allowed` mengembalikan `false` saat `IsReadOnly`.
  - Event `Changed` tidak dibuat; layar memakai `HasChanges`.
- **C3 — Satuan dan geometri.** Semua posisi/ukuran item disimpan dalam mm (double): `XMm`, `YMm`
  (kiri-atas kotak sebelum rotasi), `WidthMm`, `HeightMm`, `AngleDeg` (rotasi terhadap pusat kotak).
  Panah: `StartXMm`, `StartYMm`, `EndXMm`, `EndYMm`, `Color`, `ThicknessMm`. Konversi mm → piksel tampilan
  dan → piksel flatten ada di satu kelas internal `ComposerGeometry` agar bisa diuji tanpa UI.
- **C4 — Tampilan.** Area canvas selalu diskalakan agar muat di ruang yang tersedia (uniform, di tengah),
  tanpa zoom manual. Canvas putih dengan bayangan/outline token tema; area di luar canvas memakai
  latar permukaan tema; isi di-clip ke batas canvas. Handle seleksi, handle rotasi, kotak crop, ikon
  peringatan resolusi hanya di layar (tidak ikut flatten).
- **C5 — Insert dan Paste.**
  - Insert: `OpenFileDialog` multi-select, filter PNG/JPEG/BMP/GIF/TIFF; satu item per berkas.
  - Paste (`Ctrl+V` saat control fokus, atau tombol): bila clipboard berisi gambar → satu item; bila berisi
    daftar berkas (copy dari Explorer) → item untuk tiap berkas gambar yang dikenali; selain itu tidak
    terjadi apa-apa dan tombol Paste nonaktif (`PasteCommandAllowed` memeriksa clipboard).
  - Gambar didekode penuh lalu `Freeze()`. Gambar gagal didekode memunculkan `EmMessageBox` berisi pesan
    yang jelas; item lain tetap dimasukkan.
  - Penempatan awal: ukuran alami dari DPI gambar (96 bila metadata kosong); bila lebih besar dari 80%
    canvas, diperkecil agar muat 80% (rasio tetap); diletakkan di tengah; menjadi item teratas dan terpilih.
- **C6 — Interaksi.** Seleksi tunggal. Drag badan item = move. Delapan handle resize; sudut menjaga rasio
  kecuali Shift; sisi selalu bebas pada sumbunya. Handle rotasi di atas kotak: snap 15°, Shift = bebas.
  Panah: drag badan = geser seluruh panah, drag ujung = geser titik itu. Keyboard: Delete = hapus item,
  panah keyboard = geser 1 mm (Ctrl = 0,1 mm), Esc = batalkan crop/batalkan seleksi. Ukuran item minimal 1 mm.
- **C7 — Isi awal.** `LoadInitialImage` menampilkan PNG sebagai lapisan dasar terkunci yang memenuhi
  seluruh canvas (stretch ke ukuran canvas). Saat `HasInitialImage`: tidak bisa dipilih atau diubah,
  Insert/Paste/Arrow nonaktif, input ukuran nonaktif; hanya **Clear** yang tersedia. Ini mengikuti K4/K11:
  pengguna Clear lalu menyusun ulang.
- **C8 — Clear.** Meminta konfirmasi `EmMessageBox` (Yes/No). Setelah Clear: `Items` kosong,
  `HasInitialImage = false`, `IsEmpty = true`, `HasChanges = true` (layar memutuskan apakah canvas kosong
  berarti menghapus gambar tersimpan).
- **C9 — Crop.** Hanya untuk item gambar. Kotak crop berada di kerangka lokal item (ikut rotasi item),
  persegi panjang, dengan 8 handle, minimal 1 mm. Apply: hitung area piksel sumber (dibulatkan ke piksel
  utuh), buat `CroppedBitmap` dari piksel item, beku, lalu sesuaikan `XMm/YMm/WidthMm/HeightMm` sehingga
  bagian yang tersisa tetap di posisi yang sama di canvas. Selama mode crop, command lain kecuali
  Apply/Cancel nonaktif.
- **C10 — Fit.** Untuk item gambar terpilih: rotasi direset 0°, diskalakan *contain* ke canvas penuh,
  di tengah. Untuk panah: Fit nonaktif.
- **C11 — Z-order.** `BringToFront` dan `SendToBack` saja (tanpa satu langkah maju/mundur). Isi awal selalu
  paling bawah dan tidak ikut z-order.
- **C12 — Panah.** Warna mutlak (bukan token tema) karena mewakili tinta cetak, seperti latar putih:
  Merah `#D32F2F`, Hitam `#000000`, Biru `#1565C0`, Kuning `#F9A825`. Kepala panah segitiga terisi,
  panjang `max(3 mm, 4 × tebal)`, lebar `max(2 mm, 3 × tebal)`. Tool panah: drag di canvas dari titik awal
  ke akhir; panjang < 2 mm dibatalkan. Mengubah warna/tebal di toolbar menerapkan ke panah terpilih dan
  menjadi default panah berikutnya.
- **C13 — Peringatan resolusi.** Resolusi efektif item gambar =
  `min(PixelWidth / (WidthMm / 25.4), PixelHeight / (HeightMm / 25.4))`. Di bawah 150 dpi: ikon peringatan
  di sudut item dengan tooltip (mis. "Effective resolution 112 dpi; the printed image may look blurry."),
  dan `HasLowResolution = true`. Tidak memblokir apa pun.
- **C14 — Flatten.** `DrawingVisual` dengan latar putih penuh, isi awal (bila ada), lalu item sesuai z-order
  dengan transform mm → piksel pada `OutputDpi`; `RenderTargetBitmap(widthPx, heightPx, OutputDpi, OutputDpi,
  Pbgra32)` dengan `widthPx = round(CanvasWidthMm / 25.4 × OutputDpi)`; encode `PngBitmapEncoder`
  (metadata DPI ikut dari bitmap). Gambar memakai `BitmapScalingMode.HighQuality`. Flatten harus berjalan di
  thread UI/STA; `FlattenPng()` dipanggil dari thread UI (dokumentasikan di XML doc dan guide).
- **C15 — Toolbar.** Satu baris di atas canvas, tombol ikon Font Awesome 6 + tooltip, mengikuti style yang
  ada di `Styles/` (`FlatIconButton.xaml`, `Buttons.xaml`, `Inputs.xaml`, `Menus.xaml`). Urutan:
  Insert, Paste │ Delete, Bring to front, Send to back, Fit, Crop (saat crop: Apply, Cancel) │ Arrow (toggle),
  warna (4 swatch), tebal (ComboBox 0,5/1/2 mm dengan `ItemTemplate` teks eksplisit) │ ukuran canvas
  W × H mm (`NumericBox` + `fieldNumericBoxStyle`, bilangan bulat 10–1000; tampil hanya bila
  `IsCanvasSizeEditable`) │ Clear. Bila toolbar sempit, area ukuran turun ke baris berikutnya (WrapPanel).
  Saat `IsReadOnly`, toolbar tetap tampil tetapi tombol nonaktif (bukan disembunyikan).
- **C16 — Teks UI.** Semua teks UI, tooltip, pesan berbahasa Inggris dan netral produk.
- **C17 — Branch dan commit.** Kerja dan commit di `work-bench`, pesan commit Bahasa Indonesia, **commit
  setiap fase selesai tanpa menunggu diminta**, tanpa push. Bila checkout tidak di `work-bench` atau ada
  perubahan lain yang belum di-commit sebelum mulai: buat commit baseline perubahan itu bila jelas milik
  pekerjaan sebelumnya; bila tidak jelas, kerjakan tanpa commit dan catat di laporan. Jangan
  `stash`/`reset`/`checkout --`.
- **C18 — Release note.** Tidak menulis release note NuGet (tidak ada rilis paket pada plan ini; release note
  versi baru di `main` memicu rilis otomatis). Release note aplikasi host turunan ditulis oleh plan konsumen
  saat control mulai dipakai host.
- **C19 — Build dan tes lewat RunBay.** Gunakan app/action RunBay yang sudah terdaftar untuk em-system
  (cek `list_apps`/`get_app`). Bila belum ada action untuk build WPF core atau tes `Em.Ui.Wpf.Core.Tests`,
  buat custom action PowerShell (`dotnet build src/frontend/Em.Ui.Wpf.slnx`,
  `dotnet test src/frontend/Em.Ui.Wpf.slnx`, filter `ImageComposer` boleh dipakai) lalu laporkan nama
  action dan run id. Bila MCP RunBay tidak tersedia, beri tahu pengguna, fallback ke shell, dan catat
  bahwa run tidak terlihat di RunBay.
- **C20 — Tindakan terblokir policy.** Jangan diulang lewat jalan lain; siapkan `.ps1` di
  `plan/image-composer-manual/` dan laporkan sesuai aturan `CLAUDE.md`.

---

## 3. Mockup UI

**Tidak ada.** Keputusan pengguna 2026-10-29 (K13): control engine ini langsung dibuat sebagai UI asli,
sebagai pengecualian alur mockup, tanpa uji visual/render. Layar konsumen di repo turunan tetap mengikuti
alur mockup repo itu. Agent pelaksana tetap memeriksa ulang folder mockup repo turunan bila diakses
(tidak wajib); temuan mockup control ini, bila ada, dilaporkan.

---

## 4. Aturan yang wajib diikuti (ringkasan dari `CLAUDE.md` em-system dan aturan yang belum tertulis)

- Baca `CLAUDE.md` root em-system versi terbaru sebelum mulai, termasuk "Standar kerapian UI WPF — acuan
  UserManager", "Aturan UI WPF — cegah kilatan putih saat busy/disabled", "List: pisahkan item/nilai
  pilihan dari teks tampilan", "Guide wajib setelah membuat modul", dan "Uji visual (GUI) hanya atas
  konfirmasi".
- Komentar kode, XML doc, komentar XAML, teks UI, pesan exception/log: **bahasa Inggris**. Member
  public/protected baru wajib punya XML doc (CS1591 tidak di-`NoWarn`). Plan dan laporan berbahasa Indonesia.
- Generik: tanpa nama modul bisnis/produk turunan, server, database, atau path mesin.
- MVVM: VM turunan `MvvmModelBase`, properti bindable memakai `Get`/`Set` milik base class dan **wajib
  `public`** (properti private gagal saat runtime; jangan `Trim` di getter properti TwoWay). Command
  dibuat dengan `RegisterCommand(nameof(XCommand), XCommand, XCommandAllowed)`, di-bind
  `Command="{Binding Commands[XCommand]}"`, can-execute berupa method `...Allowed` (bukan lambda), dan
  command tidak diekspos sebagai properti. Code-behind hanya untuk interaksi pointer/keyboard yang
  memanggil method VM (hit-test, drag, capture), bukan untuk logika state.
- Warna chrome hanya dari token tema (`Styles/MaterialDesign.xaml`, `Palette.xaml`); pengecualian yang
  disengaja: latar canvas putih dan warna panah (C12). Ikon WPF memakai Font Awesome 6 (bukan FA7).
- ComboBox dengan item objek memakai `ItemTemplate` eksplisit; jangan mengandalkan `DisplayMemberPath`
  pada `fieldComboStyle` (diketahui menampilkan nama class pada kotak terpilih).
- Bila butuh varian method, tambah **overload** dengan nama sama, jangan rename.
- Harness/smoke di luar repo (`..\.artefacts\em-system\scripts\<nama>-smoke\`) dan tidak boleh membuat
  berkas di repo; plan ini tidak memerlukan harness render (K13). Tes yang dirawat berada di `tests/`.
- Urutan kerja: tulis seluruh kode fase 1–2 dulu, baru build/tes/review di fase 3.

---

## 5. Fase pekerjaan

### Fase 1 — Model, VM, dan geometri (kode)

1. `Controls/ImageComposer/ComposerGeometry.cs` (internal): konversi mm ↔ piksel, transform item
   (translate + rotate pusat), hit-test titik terhadap item berotasi, snap sudut, resize dengan/ tanpa rasio
   per handle, pemetaan kotak crop ke piksel sumber, resolusi efektif, kepala panah.
2. `ComposerItem`, `ComposerImageItem` (`Source` beku, `PixelWidth/Height`, `EffectiveDpi`,
   `IsLowResolution`), `ComposerArrowItem`; enum `ComposerArrowColor`, `ComposerTool`.
3. `ImageComposerVm`: properti, command, method sesuai C2; validasi rentang ukuran/DPI (clamp);
   aturan nonaktif saat `IsReadOnly`, `HasInitialImage`, mode crop; `HasChanges` diset pada setiap
   perubahan isi/susunan dan ukuran canvas; flatten (C14); muat isi awal (C7); insert/paste (C5) dengan
   abstraksi kecil untuk clipboard dan dialog berkas (internal interface, implementasi default WPF) agar
   tes tidak membuka dialog.
4. Commit fase 1.

### Fase 2 — Control, toolbar, interaksi, guide (kode dan dokumen)

1. `ImageComposer.xaml` + `.xaml.cs`: DP sesuai C1; toolbar (C15) dengan `Visibility` dari `ShowToolbar`;
   area canvas berskala (C4) berisi lapisan isi awal, `ItemsControl` item (DataTemplate per tipe item,
   posisi/rotasi lewat converter atau binding ke properti piksel tampilan di VM), lapisan adorner untuk
   seleksi/rotasi/crop/peringatan; penanganan pointer dan keyboard (C6, C9, C12) yang memanggil method VM.
2. Style lokal control di resource control itu sendiri atau di `Styles/` bila dipakai ulang; jangan menyalin
   palette.
3. `doc/engine/engine-image-composer.md` (bahasa Inggris): tujuan, cara pasang di XAML, bind `Composer`,
   ukuran canvas dan penguncian, `OutputDpi`, memuat isi awal, flatten dari thread UI, `HasChanges`/
   `MarkSaved`, `IsReadOnly`, `ShowToolbar`, crop permanen, panah, peringatan resolusi, batasan (WPF saja,
   tanpa drop/teks/undo, PNG saja). Tautkan dari `doc/engine/README.md`.
4. Commit fase 2.

### Fase 3 — Tes, build, review (setelah seluruh kode selesai)

1. `tests/Em.Ui.Wpf.Core.Tests/ImageComposerTests.cs` (jalankan di thread STA seperti tes WPF yang ada):
   - konversi mm ↔ piksel dan ukuran PNG flatten (mis. 100 × 50 mm @300 dpi → 1181 × 591 px) beserta
     DPI metadata;
   - flatten canvas kosong = putih penuh; panah merah menghasilkan piksel merah di posisinya; item di luar
     canvas terpotong;
   - resize sudut menjaga rasio, Shift bebas; snap rotasi 15°; hit-test item berotasi;
   - crop: area piksel benar, ukuran piksel item mengecil, posisi bagian tersisa tetap; Cancel tanpa
     perubahan;
   - z-order BringToFront/SendToBack; Delete; Fit (contain, rotasi 0);
   - ubah ukuran canvas mempertahankan mm item (K6); isi awal mengunci input ukuran dan command selain
     Clear (C7); Clear mengosongkan dan `HasChanges = true` (konfirmasi lewat abstraksi yang bisa diganti
     di tes);
   - `IsReadOnly` menonaktifkan semua command; `HasChanges` berubah pada perubahan dan reset lewat
     `MarkSaved`;
   - peringatan resolusi pada ambang 150 dpi;
   - clamp `CanvasWidthMm/HeightMm` 10–1000 dan `OutputDpi` 72–1200.
2. Build `src/frontend/Em.Ui.Wpf.slnx` dan jalankan tes lewat RunBay (C19). Perbaiki error/warning baru
   (warning lama tidak wajib).
3. Review kode terhadap aturan bagian 4 (MVVM, token tema, XML doc Inggris, netral produk), perbaiki
   temuan, build/tes ulang.
4. Tulis laporan eksekusi di bagian 6 plan ini; pindahkan plan ke `plan/executed/`; commit fase 3.
   Tidak push.

---

## 6. Laporan eksekusi

(Diisi saat eksekusi: commit per fase, nama action/run id RunBay, hasil build dan tes, temuan review dan
perbaikannya, keputusan cadangan yang dipakai di luar daftar, tindakan manual bila ada.
**Verifikasi visual: tertunda** sesuai K13; sebutkan bagian yang belum dilihat secara visual, yaitu tampilan
toolbar terang/gelap, handle seleksi/rotasi/crop, panah, dan skala canvas.)
