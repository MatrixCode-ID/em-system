# Plan — Desain UI UserManager (toolbar, pager, filter) sebelum diekstrak ke CustomControl

Status: **desain dalam progres** (dicek ulang 2026-09-26: desain dasar sudah ter-commit dan
layarnya sudah punya view model serta pager; butir terbuka di bagian 5 — state filter bersama,
ekstraksi ke control, query object — masih berlaku. Harness di bagian 6 memakai `ThemedWindow` dan
`ApplicationThemeHelper` DevExpress, yang akan dikeluarkan dari `Em.Ui.Wpf.Core` oleh plan
Update Branding; sesuaikan harness-nya saat dipakai lagi.)
Dibuat: 2026-09-09
Baseline: commit `36624c7` (Update Em.Ui.Wpf.Core.csproj)
File kerja: `src/shared/Em.Ui.Wpf.Core/Navigations/UserManager.xaml` + `.xaml.cs`

> **Cara memakai dokumen ini kalau percakapan sudah di-clear.** Bagian 1–4 adalah keputusan
> yang sudah final — jangan dibuka ulang tanpa alasan baru. Bagian 5 adalah yang **masih
> terbuka**. Sebelum lanjut, jalankan `git status` dan `git diff --stat` untuk memastikan
> file kerja di atas masih dalam kondisi yang dijelaskan di sini (kalau sudah di-commit,
> lihat riwayat commit setelah baseline).

## 0. Konteks

`UserManager.xaml` sedang dipakai sebagai kanvas desain UI murni XAML (tanpa view model)
untuk layar User Manager, dengan rencana: setelah layout disetujui, bagian-bagian yang
generik (toolbar tools, pager, filter bar) diekstrak jadi `CustomControl` baru di
`Em.Ui.Wpf.Core`, supaya modul lain bisa pakai ulang. Diskusi lengkap soal alasan filter
harus turun ke server-side query (bukan filter grid di memori) ada di riwayat percakapan
sesi ini — intinya: paging server-side membuat filter grid ala DevExpress tidak relevan lagi,
sehingga filter dirancang jadi query object (`UserListQuery` + `PagedResult<T>`, belum
diimplementasikan) bukan `CollectionViewSource`.

## 1. Layout final (sudah dibangun di XAML)

Struktur baris (root `Grid` dengan tiga baris + overlay filter sheet di luar row grid):

1. **Toolbar** — tools saja, tanpa judul layar (judul sudah digambar host di
   `SpaNavigationHost` tepat di atas control ini — judul kedua di sini akan dobel).
   Urutan: `New User` (filled, satu-satunya tombol filled) → separator vertikal →
   `Refresh` (tonal) + `Export` (outlined) → search box → tombol `Filter` (ToggleButton
   berbadge) → chip jumlah user/active.
2. **Filter strip** — quick filter (`RadioButton` segmented: All/Active/Pending/
   Suspended/Inactive) di kiri, baris chip filter aktif (tiap kondisi bisa dihapus
   sendiri via ✕) + tombol `Clear all` di kanan.
3. **Kartu list** — header kolom (USER/ROLE/DEPARTMENT/STATUS/LAST ACTIVE), `ListBox`
   dengan baris avatar+nama+email+chip status+aksi, footer berisi `Rows per page`
   (ComboBox chrome custom) + pager lengkap (First/Prev/nomor halaman RadioButton/
   Next/Last, dengan gap `…`).
4. **Filter sheet** (side panel, di luar row grid, `ClipToBounds` di Grid terluar) —
   slide-in dari kanan (`TranslateTransform` + `DoubleAnimation` dipicu `DataTrigger`
   ke `IsChecked` tombol Filter), scrim redup di belakangnya, isi: section STATUS
   (chip toggle), ROLE & DEPARTMENT (checkbox list), LAST ACTIVE (preset chip + 2
   `dxe:DateEdit`), OPTIONS (switch), footer Reset/Apply.

Semua styling pakai palet abu translusen `#xx808080` (theme-agnostic) + satu warna accent
dari tema DevExpress, konsisten dengan pola yang sudah ada di `DefaultHomeControl.xaml`.

## 2. Tiga lapis filter (keputusan desain)

Alasan lengkap ada di riwayat chat; ringkasnya:

1. **Search** (toolbar) — teks bebas, menutup mayoritas kebutuhan sehari-hari.
2. **Quick filter** (segmented control di atas list) — satu facet paling sering dipakai
   (Status untuk User), satu klik tanpa membuka apa pun.
3. **Filter sheet** — sisanya (Role, Department, rentang tanggal, opsi lain), dibuka dari
   tombol Filter berbadge.

Kondisi dari lapis 2 & 3 direpresentasikan sebagai chip yang bisa dihapus satu-satu di
filter strip — supaya list yang terfilter selalu bisa menjelaskan dirinya sendiri.

Rencana jangka panjang (belum dikerjakan): generalisasi jadi `ListFilterBar` +
`FilterDefinition { Field, Caption, EditorKind, Operators, ValueSource }` di
`Em.Ui.Core`, supaya modul lain (dan ModelGenerator/DbTransmutter) bisa
mendeklarasikan filter tanpa menulis UI baru. **Jangan bangun abstraksi ini duluan** —
tunggu sampai 2–3 layar nyata memakai pola yang sama.

## 3. Handler sementara (code-behind)

`UserManager.xaml.cs` punya `#region Temporary for designing UI` berisi handler yang
**hanya memindahkan visual state**, tidak membangun query apa pun:

- `FilterSheetClose_Click`, `FilterScrim_MouseDown`, `FilterApply_Click` → set
  `filterToggle.IsChecked = false` (tutup sheet).
- `FilterReset_Click` → `ClearSheet` → `ClearToggles` (matikan semua ToggleButton/CheckBox
  di sheet, RadioButton dibiarkan karena exclusive set selalu butuh satu terpilih) lalu
  kosongkan `dateFromEdit`/`dateToEdit` — keduanya menyimpan nilai, bukan state toggle,
  jadi tidak ikut terjaring `ClearToggles`.
- `ActiveFilterRemove_Click` → sembunyikan satu chip (lewat `Tag` yang menunjuk ke
  container chip-nya sendiri).
- `ActiveFilterClearAll_Click` → sembunyikan semua chip + reset sheet.
- `UpdateActiveFilterState` → sinkronkan badge angka, visibility strip filter aktif,
  dan tombol Clear all berdasarkan berapa chip yang masih visible.

**Region ini seluruhnya hilang** saat dipindah ke CustomControl — digantikan binding ke
view model / `FilterState` bersama.

## 4. Isu teknis yang sudah dipecahkan

- **Komentar XML tidak boleh mengandung `--`** (MC3000) — semua pemisah komentar visual
  diganti dari `----` jadi `====`.
- **`PaletteBrushThemeKey.ResourceKey` bertipe `string`, bukan enum** — nama yang salah
  (`Window`, `Window.Background`) lolos compile tapi resolve ke `null` → permukaan
  transparan tanpa error. Untuk permukaan yang wajib opaque (popup, side sheet), dipakai
  theme key yang `ResourceKey`-nya enum sehingga nama salah gagal build:
  - Filter sheet: `{dxt:ThemedWindowThemeKey ResourceKey=WindowActiveContentBackground}`
  - Popup `Rows per page`: `{dxt:BrushesThemeKey ResourceKey=EditorPopupListBoxBackground}`
  - Detail & cara cari enum lain lewat refleksi ada di memory
    `reference_dx_palette_brush_keys.md`.
- **Style eksplisit tidak menggantikan default style tema** (2026-09-09) — properti yang
  *tidak* disebut style kita tetap mengambil setter/trigger dari default style tema DX.
  Default style `ToggleButton` di Win11Dark/Win11Light punya trigger `IsChecked` yang
  men-set `Foreground` jadi hitam, jadi chip STATUS dan tombol `Filter` teksnya gelap saat
  aktif. `RadioButton`/`CheckBox` tidak punya trigger itu — itu sebabnya seksi LAST ACTIVE
  (RadioButton) terlihat benar padahal style-nya sama persis (`filterChipStyle`).
  Perbaikan: `filterChipStyle`, `filterToggleStyle`, `pageSizeToggleStyle` mem-pin
  `Foreground` ke nilai warisan lewat
  `{Binding Foreground, RelativeSource={RelativeSource AncestorType=UserControl}}` —
  setter style (presedensi 5) mengalahkan default style trigger (presedensi 6), dan tetap
  theme-agnostic. Verifikasi: `DependencyPropertyHelper.GetValueSource` berubah dari
  `DefaultStyleTrigger`/`#FF000000` jadi `Style`/`#FFFFFFFF` (dark) & `#FF1A1A1A` (light).
- **Field tanggal pakai `dxe:DateEdit`, bukan `TextBox` atau `DatePicker`** (2026-09-09) —
  editor rumah di repo ini memang DevExpress (`LoginControl`, `ConnectionConfigEditor`,
  `MainWindow`); tidak ada `DatePicker` di mana pun. Chrome editor dimatikan
  (`ShowBorder=False`, `Background=Transparent`) sehingga `Border` ber-`dateFieldStyle`
  tetap satu-satunya bingkai — pola yang sama dengan search box. `NullText` menggantikan
  watermark `TextBlock`+`DataTrigger` yang sebelumnya dipakai, dan nilainya jadi `DateTime?`
  bukan teks bebas. Popup kalendernya sudah opaque bawaan tema (diverifikasi lewat screen
  capture, karena popup hidup di HWND terpisah dan tidak ikut `RenderTargetBitmap`).
- Overlay filter sheet + scrim sengaja diletakkan **di luar** row grid utama (dibungkus
  satu `Grid` luar ber-`ClipToBounds="True"`), supaya panel yang `Grid.RowSpan` semua
  baris tidak mendorong tinggi baris `Auto` toolbar.

## 5. Yang masih terbuka / next steps

- [x] **Verifikasi visual setelah fix theme key** — selesai 2026-09-09. Sheet dan popup
  sudah opaque di Win11Dark maupun Win11Light. Diverifikasi lewat harness render
  (lihat "Harness verifikasi visual" di bawah), bukan lewat aplikasi penuh.
- [ ] Quick filter (lapis 2) dan filter sheet (lapis 3) masih berdiri sendiri — belum
  ditulis ke satu state bersama. Chip aktif di mock ini masih data statis, bukan hasil
  generate dari state quick filter/sheet.
- [ ] Setelah layout disetujui user: ekstrak toolbar tools + pager + filter bar jadi
  `CustomControl` baru di `Em.Ui.Wpf.Core` (lokasi belum ditentukan — kemungkinan
  `Controls/` sejajar `UserManager.xaml` atau folder baru khusus list/filter control).
- [ ] `UserListQuery` + `PagedResult<T>` belum dibuat — ini fondasi yang dibutuhkan begitu
  filter/paging beneran terhubung ke data. **Terhambat keputusan skema:** facet ROLE,
  DEPARTMENT dan LAST ACTIVE di mock tidak punya padanan di `vi_User` (yang ada hanya
  `cUserState`, `cContactType`, `cAddressLocation`, dan `datestamp` yang merupakan stempel
  ubah, bukan waktu login terakhir). Perlu diputuskan dulu: facet itu dibuang, dipetakan ke
  kolom lain, atau skema ditambah. Catatan lokasi: `ICredentialServices` ada di
  `Em.Libs/Api.Core.Models`, bukan `Em.Models` seperti dugaan awal plan ini — jadi
  `UserListQuery` sebaiknya ikut ke `Em.Libs`, dan `PagedResult<T>` ke `Em.Libs/Shared`
  bersama primitive generik lain.
- [ ] **Generalisasi ke entity/module lain** (dibahas 2026-09-09). Yang sudah generik:
  toolbar, strip chip filter aktif, rangka kartu + footer pager, dan cangkang filter sheet.
  Yang masih khusus User: header kolom + `DataTemplate` baris, dan nilai-nilai facet.
  Keduanya sekarang menyatu di satu file — itu yang membuat layout terlihat User-only.
  Langkah murah yang disepakati arahnya: pisahkan dulu secara fisik di mock ini (facet jadi
  `ItemsControl` data-driven, row template + definisi kolom ke resource terpisah) **tanpa**
  membangun engine `FilterDefinition`, sesuai peringatan di bagian 2.
- [ ] Desain dasar sudah di-commit di `133a0d0`; perbaikan `Foreground` ToggleButton
  (bagian 4) **belum di-commit**. Jalankan `git status` sebelum lanjut.

- [ ] **Pengguna kedua sudah muncul: rail Role Manager** (dibahas 2026-09-18). `RoleManager.xaml`
  memakai pola yang sama persis di rail kirinya — kotak search (`:282`) plus segment
  All / In use / Disabled (`:310`) di atas daftar, dengan footer "6 of 6 roles" dan tombol
  prev/next di bawahnya. Pada plan binding Role Manager (`rolemanager-binding-view-model.md`)
  filter itu **sengaja dilewati**: kontrolnya dibiarkan tergambar tapi tidak diikat ke apa pun,
  karena menyembunyikannya berarti menggambar ulang layout rail dan harus dibongkar lagi nanti.
  Pagernya tetap diikat, karena itu paging dan bukan filter.

  Dua akibatnya untuk plan ini:

  1. Generalisasi di poin sebelumnya berhenti jadi spekulasi — sudah ada dua layar yang
     memintanya, dan facet keduanya berbeda jauh (User punya ROLE/DEPARTMENT/LAST ACTIVE,
     Role cuma punya satu sumbu: terpakai atau dimatikan). Itu ujian yang bagus untuk memastikan
     control-nya benar-benar data-driven, bukan User-only yang disamarkan.
  2. Nama `UserListQuery` jadi terlalu sempit. Begitu Role Manager ikut, yang dibutuhkan adalah
     bentuk query per entitas dengan rangka yang sama. Diputuskan saat membuatnya nanti, tapi
     jangan sampai `PagedResult<T>` di `Em.Libs/Shared` terlanjur dipasangi asumsi User.

- [ ] **Segment state Role terikat ke `RoleState`** (`Em.Libs/Shared/RoleState.cs`):
  `Disabled = 0`, `Active = 1`. Nilainya sudah benar dan tidak perlu diapa-apakan — slot 0 di
  konvensi `Stage` adalah *draft*, dan role tidak mengenal tahap draft, jadi slot itu dipakai
  untuk `Disabled`. Yang perlu diperhatikan hanya saat mengikat segment: "Disabled" di sini
  bukan nilai negatif, jadi jangan menulis penyaringnya sebagai `state < 0`.

## 6. Harness verifikasi visual

`UserManager.xaml` bisa dirender tanpa login/backend, karena ia `UserControl` biasa tanpa
view model. Harness sekali pakai (di scratchpad, tidak masuk repo) cukup berisi project
`net10.0-windows`/`UseWPF` yang mereferensikan `Em.Ui.Wpf.Core`, lalu:

1. `ApplicationThemeHelper.ApplicationThemeName = "Win11Dark"` (atau `"Win11Light"`)
   **sebelum** kontrol dibuat;
2. `new ThemedWindow { Content = new UserManager() }` lalu `Show()`;
3. di `ContentRendered`, `body.FindName("filterToggle")` di-`IsChecked = true` untuk membuka
   sheet, tunggu ±900 ms supaya animasi slide selesai;
4. `RenderTargetBitmap` (dpi 192 supaya detail chip terbaca) → simpan PNG.

Untuk melacak asal sebuah nilai properti — bukan sekadar melihat hasilnya — pakai
`DependencyPropertyHelper.GetValueSource(element, Control.ForegroundProperty)`. Itulah yang
membuktikan bug di bagian 4 berasal dari `DefaultStyleTrigger`, bukan dari markup ini.
