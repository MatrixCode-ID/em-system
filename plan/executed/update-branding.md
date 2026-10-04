# Plan — Update Branding: tema bersama, `Em.Ui.Wpf.Core` bebas DevExpress, dan `Em.Ui.Wpf.Core.DevExpress`

Status: **sudah dieksekusi** 2026-09-26 (dua sesi; sesi pertama terhenti sesudah uji jalan, sesi kedua
menyelesaikan Tahap H, verifikasi build, dan commit). Lihat [Catatan eksekusi](#catatan-eksekusi).
Dibuat: 2026-09-26, dari diskusi dengan user. Semua keputusan sudah dijawab user sebelum plan ini
ditulis (lihat [Keputusan final](#keputusan-final)). Menggantikan
`plan/executed/wpf-core-native-editors.md` (digabung ke [Tahap C](#tahap-c--control-dx-editor-grid-wait-image-diganti-wpf-native)).

**Aturan eksekusi.** Plan ini dijalankan tanpa bertanya ke user. Kalau ada hal yang tidak tercakup
di sini, pilih yang paling konsisten dengan keputusan final dan pola kode yang sudah ada, lalu catat
di [Catatan eksekusi](#catatan-eksekusi). Jangan berhenti untuk bertanya. Tahap dikerjakan berurutan;
build `Em.Ui.Wpf.Core` (dan proyek yang disentuh tahap itu) harus bersih di akhir setiap tahap.

Baca dulu sebelum mulai:

- `CLAUDE.md` root: **Keep the UI cores in step**, **XML doc comment updates**, tabel `src/shared`;
- `src/frontend/CLAUDE.md`: **MVVM binding rules (MANDATORY)**, **Material design language
  (MANDATORY)**, **XAML and XML comments**, **UI framework**;
- memory `reference_dx_palette_brush_keys.md` dan `reference_wpf_default_style_trigger.md` (perilaku
  tema DX yang masih relevan untuk proyek DX dan untuk layar core saat tema DX aktif);
- referensi material: `Navigations/UserEditor.xaml`, `Navigations/RoleManager.xaml`,
  `Dialogs/DisplayExceptionData.xaml`, `Windows/TabbedMainWindow.xaml(.cs)` (chrome kustom).

---

## Latar belakang

- `Em.Libs`, `Em.Ui.Core`, dan `Em.Ui.Wpf.Core` akan dipublikasikan ke NuGet dan harus bebas
  dependensi berlisensi. DevExpress boleh dipakai modul, tidak wajib.
- Objek tema di `Em.Ui.Core/Ui.Core/shared/` (`ThemeVariant`, `ThemeColor`, `ThemeColors`,
  `EmTheme`) sudah ter-commit di `3a4d57c` tapi belum dipakai siapa pun. `ThemeColors` memuat 25 slot
  bergaya Material 3 dengan nilai yang sama dengan `Em.Ui.Maui.Core/Styles/Palette.xaml`.
- `BrandingInfo` masih dua kelas terpisah (WPF dan MAUI) dengan warna panel login
  `PanelColor1/2Light/Dark` bertipe warna platform, dan `bool isLightTheme` sebagai pemilih tema.
- Nama tema berbeda: WPF `"Win11Light"`/`"Win11Dark"` (nama tema DX, tersimpan di Registry), MAUI
  `"Light"`/`"Dark"` (Settings).
- Pemakaian DevExpress di `Em.Ui.Wpf.Core` (inventaris 2026-09-26):

| Jenis | Tempat |
| --- | --- |
| `dx:ThemedWindow` | `Dialogs/ConnectionConfig`, `Dialogs/ConnectionConfigEditor`, `Dialogs/DisplayExceptionData`, `Windows/DetachedWindow` |
| Editor/grid/wait/image DX | lihat [Tahap C](#tahap-c--control-dx-editor-grid-wait-image-diganti-wpf-native) |
| `dxi:ThemeResource` (±90) | `Styles/*.xaml`, `Themes/Generic.xaml`, `Navigations/*.xaml`, `Dialogs/DisplayExceptionData.xaml`, `Windows/TabbedMainWindow.xaml`; kunci: `PaletteBrushThemeKey Accent`, `ThemedWindowThemeKey WindowActiveContentBackground`/`WindowContentForeground`, `BrushesThemeKey EditorPopupListBoxBackground` |
| `ThemedMessageBox` | `Extensions.cs`, semua helper `ShowMbox*` |
| `ApplicationThemeHelper` | `Core/EmApp.cs` (setter `CurrentTheme`, `Run`) |
| SVG DX | `Shared/BrandingInfo.cs` (`SvgImageHelper`, `WpfSvgRenderer`) |
| `using` DX tanpa pemakai | `Core/Navigation.cs` (`DevExpress.Mvvm.UI`), `Core/EmApp.Statics.cs` (`DevExpress.Xpf.Core`) |
| Paket | `DevExpress.Wpf`, `DevExpress.Wpf.Themes.Win11Dark`, `DevExpress.Wpf.Themes.Win11Light` 25.2.5 |

Di luar core: host `Em.Ui.Wpf/Program.cs` punya `using DevExpress.Xpf.Core;` (tidak dipakai),
modul `Em.Sample` tidak memakai DX sama sekali.

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Plan editor native | **Digabung** ke plan ini sebagai Tahap C; `wpf-core-native-editors.md` dipindah ke `plan/executed/` dengan status "digantikan". |
| 2 | Tipe tema | **`ThemeBase` abstrak + `LightTheme : ThemeBase` + `DarkTheme : ThemeBase`** di `Em.Ui.Core`, berisi palet standar Em. Aplikasi boleh menurunkan kelasnya sendiri atau mengisi slot lewat object initializer. `EmTheme` dan `ThemeColors` dihapus (slotnya pindah ke `ThemeBase`). |
| 3 | `BrandingInfo` | **Satu kelas data di `Em.Ui.Core`**: `Title`, `Tagline`, `Description`, `Copyright`, `LogoSource` (`string?`), `LightTheme`, `DarkTheme`. `PanelColor1/2Light/Dark` dihapus. `BrandingInfo` per core dihapus (breaking, disengaja); WPF dan MAUI hanya punya helper konversi warna dan logo. |
| 3a | Warna panel login | Slot **`Brand`** (sudah ada) + slot baru **`OnBrand`**. Nilai bawaan sama dengan sekarang: terang `#0F6CBD` / `#FFFFFF`, gelap `#12283D` / `#4FA3FF`. |
| 4 | Tema aktif | **`EmApp.CurrentTheme` bertipe `ThemeVariant`** di WPF dan MAUI. Konstanta string `LightTheme`/`DarkTheme` di `EmApp` dihapus. Nilai lama di Registry/Settings (`Win11Light`, `Win11Dark`, `Light`, `Dark`) tetap terbaca lewat pemetaan. |
| 5 | Penerapan tema WPF | **Token dinamis dari `ThemeBase`**: `EmApp` menulis brush tema aktif ke `Application.Resources` dengan kunci tetap; semua `dxi:ThemeResource` diganti `{DynamicResource ...}`. Palet abu translucent di `Styles/Palette.xaml` tetap. |
| 6 | `ThemedWindow` | **Base window Em bersama** (`EmWindow`), chrome kustom diangkat dari `TabbedMainWindow`. Dipakai tiga dialog, `DetachedWindow`, dan dialog message box baru. |
| 7 | `ThemedMessageBox` | **Dialog material sendiri** (`Dialogs/EmMessageBox`) di atas `EmWindow`; signature `ShowMbox*` tetap. |
| 8 | Logo SVG | **Core hanya bitmap** (PNG/JPG/ICO/BMP). Render SVG pindah ke proyek DX lewat titik perluasan penyedia gambar logo. |
| 9 | Proyek DX | Nama **`Em.Ui.Wpf.Core.DevExpress`**, di `src/shared/`, masuk `Em.Ui.Wpf.slnx`, tidak dipublikasikan. Berisi `builder.UseDevExpress()`. |
| 9a | Cara pasang | **Jalur A (dikecilkan)**: core membuka interface `IThemeApplier` (di `Em.Ui.Core`) dan `ILogoImageLoader` (WPF), plus method builder publik `AddThemeApplier<T>()` / `AddLogoImageLoader<T>()` (idempotent). `UseDevExpress()` hanya memanggil keduanya. |
| 9b | Siapa memanggil | **Host `Em.Ui.Wpf/Program.cs`** mereferensikan proyek DX dan memanggil `builder.UseDevExpress()`. **`Em.Sample` ikut mereferensikan** proyek DX (supaya modul bisa memakai control DX), tapi tidak memanggil `UseDevExpress()`. |
| 9c | Palet DX | **`ThemeBase` → custom `ThemePalette` di atas Win11Light/Win11Dark** (tabel pemetaan di Tahap F). Tema DX dibuat saat pertama dibutuhkan, di-cache per instance tema, `Theme.CachePaletteThemes` dinyalakan. |
| 9d | Style material untuk control DX | **Ditunda** sampai ada modul yang memakai grid/editor DX. |
| 10 | MAUI | **Kunci palet diisi dari tema**: saat startup `EmApp` MAUI menulis nilai kunci `*Light`/`*Dark` di `Styles/Palette.xaml` dari `BrandingInfo.LightTheme`/`DarkTheme`; `AppThemeBinding` tetap. |
| 11 | Plan lama | Perapian `usermanager-filter-ui-design.md` dan `action-naming-cleanup.md` sudah dilakukan 2026-09-26 (sebelum plan ini dieksekusi). Plan lain di `plan/unexecuted/` masih berlaku. |
| 12 | Commit | **Dua commit**, Bahasa Indonesia, branch aktif (`spa-detach`), **tanpa push**: (a) Langkah 0 — perubahan tertunda; (b) akhir plan — seluruh pekerjaan plan ini. |

Kontrak bersama di `Em.Ui.Core` yang disentuh plan ini: `ThemeVariant`, `ThemeColor`, `ThemeBase`,
`LightTheme`, `DarkTheme`, `BrandingInfo`, `IThemeApplier`. Tidak ada kontrak navigasi yang disentuh.

---

## Langkah 0 — Commit perubahan tertunda

`git status` sebelum mulai berisi (dan hanya berisi):

- tiga perubahan kecil sesi 2026-09-26: window tear-off tanpa menu, window utama maximized +
  `CenterScreen`, tombol daftar tab disembunyikan saat kosong — beserta pembaruan `src/frontend/CLAUDE.md`
  dan `plan/executed/multitab-tabbed-main-window.md`;
- perapian plan lama: `plan/unexecuted/usermanager-filter-ui-design.md`, `plan/unexecuted/action-naming-cleanup.md`;
- `doc/TODO-LIST.md`: item "Jalankan plan Update Branding" (ditambahkan 2026-09-26);
- plan-plan baru/pindahan: berkas plan ini, dan `wpf-core-native-editors.md` (sudah dipindah ke
  `plan/executed/` dengan status "digantikan").

Pastikan build `Em.Ui.Wpf.Core` bersih, lalu commit semuanya dalam satu commit (pesan Bahasa
Indonesia, diakhiri `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). Kalau `git status`
berisi hal lain yang tidak dikenali, jangan ikut di-commit; catat. Catat juga jumlah warning build awal
`Em.Ui.Wpf.slnx` dan `Em.Ui.Maui.slnx` (rebuild penuh) untuk verifikasi.

---

## Tahap A — Tema dan branding bersama di `Em.Ui.Core`

Semua berkas di `src/shared/Em.Ui.Core/Ui.Core/shared/`, namespace `Em.Ui.Core.Shared`. Tanpa
dependensi UI (proyek ini `net10.0` biasa).

1. **`ThemeVariant`**, **`ThemeColor`**: tetap. Tambahkan XML doc Bahasa Indonesia kalau belum ada.
2. **`ThemeBase`** (abstract class, publik):
   - `public ThemeVariant Variant { get; }`, di-set constructor `protected ThemeBase(ThemeVariant variant)`;
   - slot warna `ThemeColor` dengan `{ get; init; }` — seluruh slot `ThemeColors` sekarang (Primary,
     OnPrimary, PrimaryContainer, OnPrimaryContainer, Secondary, OnSecondary, SecondaryContainer,
     OnSecondaryContainer, Surface, OnSurface, OnSurfaceVariant, SurfaceContainerLow, SurfaceContainer,
     SurfaceContainerHigh, Outline, OutlineVariant, Error, ErrorContainer, OnErrorContainer, Success,
     Warning, Info, Scrim, Brand) **ditambah `OnBrand`**;
   - XML doc per slot menjelaskan perannya (boleh diambil dari komentar `Em.Ui.Maui.Core/Styles/Palette.xaml`).
3. **`LightTheme : ThemeBase`** dan **`DarkTheme : ThemeBase`**: constructor publik tanpa parameter
   mengisi setiap slot dengan nilai `EmTheme.Default` sekarang (Light/Dark), plus
   `OnBrand` = `#FFFFFF` (terang) / `#4FA3FF` (gelap), dan `Brand` = `#0F6CBD` / `#12283D` (sudah sama).
   Tidak `sealed`: aplikasi boleh menurunkannya.
4. Hapus `ThemeColors.cs` dan `EmTheme.cs`.
5. **`BrandingInfo`** (sealed class, publik):
   - `Title`, `Tagline`, `Description`, `Copyright` (`string?`), `LogoSource` (`string?`, URI atau
     nama resource — ditafsirkan oleh core platform);
   - `LightTheme` (`ThemeBase`, default `new LightTheme()`), `DarkTheme` (`ThemeBase`, default `new DarkTheme()`);
     setter memvalidasi `Variant` (tema terang harus `ThemeVariant.Light`, dst.), `ArgumentException` kalau salah;
   - `DisplayTitle`, `DisplayTagline`, `DisplayDescription`, `DisplayCopyright` dengan teks bawaan
     generik yang sama dengan sekarang (copyright memakai `©`);
   - `ThemeBase GetTheme(ThemeVariant variant)`.
   - Logo bawaan **bukan** urusan kelas ini (berbeda per platform); core platform yang menyediakannya.
6. **`IThemeApplier`** (interface publik): `void Apply(ThemeBase theme);` — dipanggil engine di thread UI
   setiap kali tema aktif diterapkan (startup dan setiap pergantian), sesudah engine menerapkan tokennya
   sendiri. XML doc: untuk pustaka kontrol pihak ketiga yang punya sistem tema sendiri.

## Tahap B — WPF: tema aktif, token dinamis, branding

### B1. `EmApp` (WPF)

- `CurrentTheme` → `ThemeVariant`. Tersimpan di Registry sebagai nama enum. Pembacaan: `"Light"`,
  `"Win11Light"` → `Light`; `"Dark"`, `"Win11Dark"` → `Dark`; lainnya/kosong → default `Dark`. Nilai lama
  ditimpa nama baru saat berikutnya ditulis.
- Hapus konstanta `LightTheme`/`DarkTheme` string dan `DefaultTheme` string.
- Property baru `public ThemeBase ActiveTheme => Branding.GetTheme(CurrentTheme);`.
- Event baru `public event EventHandler? ThemeChanged;` (padanan MAUI yang sudah ada), dipicu setelah
  tema diterapkan.
- Satu method privat `ApplyTheme()`: tulis token (B2) → panggil setiap `IThemeApplier` terdaftar →
  `MainWindow?.OnThemeChanged()` → `ThemeChanged`. Dipanggil di `Run()` (menggantikan
  `ApplicationThemeHelper`) dan di setter `CurrentTheme`.
- `Branding` bertipe `Em.Ui.Core.Shared.BrandingInfo`.
- Semua pemakai string tema disesuaikan: `SpaNavigationHostVm.ColorTheme`,
  `TabbedMainWindowVm.ColorThemeCommand`, `LoginControl(.xaml/.cs)` (`CommandParameter` jadi
  `{x:Static shared:ThemeVariant.Light}`/`Dark`, `LightModeSelected`/`DarkModeSelected`, `RenderBranding`),
  XML doc `CurrentTheme` dan `DisplayExceptionData`/lainnya yang menyebut nama tema DX.

### B2. Token tema di `Application.Resources`

`EmApp.ApplyTheme()` menulis (mengganti nilai) resource berikut di `Application.Current.Resources`,
masing-masing `SolidColorBrush` beku dari slot `ActiveTheme`:

| Kunci | Slot |
| --- | --- |
| `themeAccentBrush` | `Primary` |
| `themeOnAccentBrush` | `OnPrimary` |
| `themeWindowBackgroundBrush` | `Surface` |
| `themeWindowForegroundBrush` | `OnSurface` |
| `themePopupBackgroundBrush` | `SurfaceContainerHigh` |
| `themeBrandBrush` | `Brand` |
| `themeOnBrandBrush` | `OnBrand` |

Konversi `ThemeColor` → `Color`/`SolidColorBrush` lewat extension internal di `Shared/ThemeColorExtensions.cs`.
Kalau sebuah layar butuh `Color` (bukan brush), tambahkan kunci `Color` padanannya (`themeAccentColor`,
…) dan catat. **Kunci-kunci ini tidak boleh dideklarasikan di dictionary mana pun yang di-merge di
level elemen** (mis. `Palette.xaml`): `DynamicResource` mencari pohon elemen lebih dulu, jadi nilai
bawaan di sana akan menutupi nilai tingkat aplikasi. Nama kunci diberi awalan `theme` supaya tidak
bentrok dengan kunci lokal yang sudah ada.

### B3. Ganti semua `dxi:ThemeResource`

- `PaletteBrushThemeKey Accent` → `{DynamicResource themeAccentBrush}`;
  `ThemedWindowThemeKey WindowActiveContentBackground` → `themeWindowBackgroundBrush`;
  `ThemedWindowThemeKey WindowContentForeground` → `themeWindowForegroundBrush`;
  `BrushesThemeKey EditorPopupListBoxBackground` → `themePopupBackgroundBrush`.
- `TabbedMainWindow.xaml`: empat `Border` alias warna (seksi `theme colours`) dihapus; semua
  `{Binding Background, ElementName=…}` / `x:Reference` ke alias itu diganti `DynamicResource`. Catatan
  lama (ContextMenu di luar pohon window) tidak berlaku lagi untuk resource tingkat aplikasi.
- Hapus `xmlns:dxi`/`xmlns:dxt` dari setiap berkas yang tidak lagi memakainya.
- Komentar kepala `Styles/Palette.xaml` ("The accent is the theme's own, read … through the DevExpress
  palette key") diperbarui: aksen dibaca dari token tema Em. Ini komentar penjelas berkas `Styles/`,
  yang memang tempatnya.
- Latar dan teks window: `TabbedMainWindow` dan `EmWindow` (Tahap D) memakai
  `themeWindowBackgroundBrush`/`themeWindowForegroundBrush`.

### B4. Branding WPF

- Hapus `Shared/BrandingInfo.cs` WPF. `EmAppBuilder.ApplyBranding` menerima `Em.Ui.Core.Shared.BrandingInfo`.
- Helper internal `Shared/BrandingImages.cs`: `ImageSource LoadLogo(BrandingInfo)` — `LogoSource` kosong
  → logo bawaan `pack://application:,,,/Em.Ui.Wpf.Core;component/Assets/Icons/Logo.png`; selain itu
  coba setiap `ILogoImageLoader` terdaftar, lalu `BitmapImage`; gagal → logo bawaan. Hasil di-cache per
  `LogoSource`.
- **`ILogoImageLoader`** (interface publik, `Em.Ui.Wpf.Shared`): `ImageSource? TryLoad(Uri source);`
  — `null` berarti "bukan urusan saya".
- `EmAppBuilder`: `public void AddThemeApplier<T>() where T : class, IThemeApplier` dan
  `public void AddLogoImageLoader<T>() where T : class, ILogoImageLoader`, keduanya
  `Services.TryAddEnumerable(ServiceDescriptor.Singleton<…, T>())`.
- `LoginControl.RenderBranding`: panel = `themeBrandBrush`-equivalent dari `ActiveTheme.Brand`, aksen =
  `OnBrand`, logo dari `BrandingImages`. Boleh diubah jadi `DynamicResource` di XAML kalau lebih sederhana;
  catat pilihannya.
- `Program.cs` host: `LogoSource = "pack://application:,,,/Em.Ui.Wpf;component/Logo.png"` (string).
- Hapus `using DevExpress.Mvvm.UI` di `Core/Navigation.cs` dan `using DevExpress.Xpf.Core` di
  `Core/EmApp.Statics.cs` (tidak dipakai).

---

## Tahap C — Control DX (editor, grid, wait, image) diganti WPF native

Isi tahap ini berasal dari `plan/executed/wpf-core-native-editors.md` (keputusannya sudah dijawab user
2026-09-26 dan tetap berlaku):

| Topik | Keputusan |
| --- | --- |
| Cakupan | editor `dxe:*`, grid `dxg:*`, `dx:WaitIndicator` → `Controls/WaitOverlay`, `dx:DXImage` → FontAwesome |
| Dialog koneksi | di-restyle ke material (banner + kartu + action strip, tanpa `SystemColors`) |
| Grid | `ListBox` bergaya daftar material, klik ganda lewat `MouseBinding`, tanpa code-behind |
| SpinEdit | control baru `Controls/NumericBox` (turunan `TextBox`) |
| DateEdit | `DatePicker` + style material |
| Password login | pola UserEditor (`PasswordChanged` satu baris) |
| Letak style field | `Styles/Inputs.xaml`; salinan lokal UserEditor tidak dimigrasi |

Perbedaan terhadap plan asalnya: warna aksen dan latar popup di style baru memakai token Tahap B
(`themeAccentBrush`, `themePopupBackgroundBrush`), bukan `dxi:ThemeResource`; dan `ThemedWindow` kedua
dialog koneksi diganti `EmWindow` di Tahap D (bukan dipertahankan).

### C1. Style field bersama di `Styles/Inputs.xaml`

Tambahkan ke `Styles/Inputs.xaml` (tanpa style implisit, semua ber-`x:Key`; komentar kelompok pendek
mengikuti kebiasaan berkas `Styles/`). Ada dua keluarga:

**Field berbingkai** — field yang menggambar bingkainya sendiri (dialog koneksi). Salin bentuknya dari
`UserEditor.xaml` dengan **nama kunci yang sama**, supaya migrasi UserEditor nanti cukup menghapus
salinan lokalnya (resource lokal menang atas resource hasil merge, jadi tidak bentrok):

- `fieldBoxStyle` (`TextBox`), `fieldPasswordStyle` (`PasswordBox`), `fieldComboStyle` +
  `fieldComboItemStyle` (`ComboBox`): tinggi 42, `CornerRadius` 10, `surfaceStrongBrush` +
  `outlineBrush`, fokus = border aksen, disabled `Opacity` 0.4.
- `fieldNumericBoxStyle` (`controls:NumericBox`, langkah 2): bentuk sama dengan `fieldBoxStyle`, dengan
  dua `RepeatButton` kecil (chevron atas/bawah, `flatIconButtonStyle` diperkecil) di kanan.
- Placeholder memakai konvensi `searchBoxStyle`: teks di `Tag`, tampil selama `Text` kosong.

**Field inline** — editor tanpa bingkai, untuk layar yang sudah menggambar bingkainya sendiri di
sekelilingnya (kartu login, field tanggal di sheet filter UserManager):

- `inlineBoxStyle` (`TextBox`), `inlinePasswordStyle` (`PasswordBox`), `inlineComboStyle` (`ComboBox`,
  dengan popup `menuPopupBorderStyle`-like dan item `fieldComboItemStyle`), `inlineDatePickerStyle`
  (`DatePicker`): latar transparan, tanpa border, `Padding` 0, `FontSize` 13 (DatePicker di UserManager
  memakai 12 lewat atribut lokal), `CaretBrush` mengikuti `Foreground`, `FocusVisualStyle` null.
- Placeholder: `Tag` untuk `TextBox` dan `DatePicker` (tampil selama tanggal `null`), dan untuk
  `ComboBox` tampil selama `SelectedItem` `null`. `PasswordBox` tidak punya placeholder di style (tidak
  ada property untuk dipicu); layar yang butuh menaruhnya sendiri (langkah 4).

**Kalender** — `materialCalendarStyle` untuk popup `DatePicker` (`CalendarStyle`): latar popup,
`CornerRadius` 10, outline, bayangan seperti menu; `CalendarDayButton` / `CalendarButton` /
`CalendarItem` di-template ulang dengan state layer `stateHoverBrush`/`statePressedBrush`, hari terpilih
dan hari ini ditandai aksen, tombol bulan sebelum/berikut memakai glyph FontAwesome. Harus terbaca di
tema terang dan gelap.

Warna latar popup memakai token `themePopupBackgroundBrush`, dan warna aksen (fokus, hari terpilih,
hari ini) memakai `themeAccentBrush` — keduanya `DynamicResource` dari Tahap B.

### C2. Control `NumericBox`

Berkas baru `Controls/NumericBox.cs`, `public class NumericBox : TextBox` (tanpa XAML sendiri, tanpa
default style di `Themes/Generic.xaml`: tanpa style ia tetap `TextBox` yang berfungsi):

- DependencyProperty `Value` (`int`, default 0, `BindsTwoWayByDefault`), `Minimum` (`int.MinValue`),
  `Maximum` (`int.MaxValue`), `Increment` (1).
- `Text` ↔ `Value`: mengetik mengubah `Value` selama teksnya angka valid dalam rentang; saat fokus
  hilang, teks yang kosong/tidak valid dikembalikan ke `Value`, dan nilai di luar rentang di-clamp.
- Hanya digit (dan tanda minus kalau `Minimum < 0`) lewat `PreviewTextInput` dan `DataObject.Pasting`.
- Tombol panah atas/bawah di keyboard menaikkan/menurunkan `Value` sebesar `Increment`, di-clamp.
- Dua `RoutedCommand` statis `IncreaseCommand` / `DecreaseCommand` dengan class command binding
  (`CommandManager.RegisterClassCommandBinding`), dipakai `RepeatButton` di `fieldNumericBoxStyle`.
- XML doc `public` dalam Bahasa Indonesia; komentar inline dan pesan exception dalam bahasa Inggris.

### C3. Dialog koneksi

#### `Dialogs/ConnectionConfig.xaml(.cs)`

- Merge `Styles/MaterialDesign.xaml` di resources window (window-nya menjadi `EmWindow` di Tahap D).
- Bentuk mengikuti `DisplayExceptionData.xaml`: banner (ikon `fa:FontAwesome Icon="Solid_Plug"` —
  ikon yang sama dengan tool Connection Config — judul, dan deskripsi), kartu berisi daftar, lalu action
  strip. Tidak ada lagi `SystemColors`.
- Toolbar: tombol "Add" `tonalButtonStyle` dengan ikon `Solid_Plus`.
- Daftar: `ListBox` dengan `ItemsSource="{Binding ApiConnections}"`,
  `SelectedItem="{Binding SelectedApiConnection, Mode=TwoWay}"`. Baris: header kolom (caption kecil
  upper case, 11px/SemiBold/0.55) di atas daftar, lalu tiap baris `Grid` dengan kolom Profile (180),
  Server URL (`*`), Timeout (100), hover/terpilih lewat state layer. Klik ganda:
  `MouseBinding MouseAction="LeftDoubleClick"` pada template baris ke `Commands[EditConnectionCommand]`
  (lewat `RelativeSource AncestorType=Window`). Menu klik kanan Edit/Delete tetap, dengan `DataContext`
  lewat `PlacementTarget`. Teks kosong ("No connection profiles yet", opacity 0.55) saat daftar kosong.
- Action strip: "Close" `textButtonStyle` atau `outlinedButtonStyle`, `IsCancel="True"`.
- Code-behind: hapus `TableView_OnRowDoubleClick` dan `using DevExpress.Xpf.Grid`.

#### `Dialogs/ConnectionConfigEditor.xaml(.cs)`

- Merge `MaterialDesign.xaml`, banner + kartu + action strip seperti di atas, tanpa `SystemColors`.
- Profile name / Server URL → `TextBox` `fieldBoxStyle`, placeholder lewat `Tag` (teks `NullText` lama),
  `Text="{Binding Connection.ProfileName, UpdateSourceTrigger=PropertyChanged}"` dan seterusnya.
- Timeout → `controls:NumericBox` `fieldNumericBoxStyle`, `Minimum="1"`, `Maximum="600"`, `Width="140"`,
  `Value="{Binding Connection.Timeout}"` (tanpa `FallbackValue`; `ApiConnection.Timeout` sudah `int`).
- Ignore SSL → `CheckBox` `checkBoxStyle` (sudah ada di `Styles/Inputs.xaml`).
- Tombol aksi: aksi utama (simpan) `filledButtonStyle` (satu-satunya elevasi), Test Connection
  `tonalButtonStyle`, Cancel `textButtonStyle` + `IsCancel`. Nama command tetap.
- `dx:WaitIndicator` (resource + `ContentControl`) dan `DataTemplate defaultWaiterTemplate` diganti
  `controls:WaitOverlay IsWaiting="{Binding InWaiting}" Caption="{Binding WaiterText}"` sebagai anak
  terakhir yang menutupi seluruh isi.
- Hapus `xmlns:dxe`; `xmlns:dx` ikut hilang saat window dipindah ke `EmWindow` (Tahap D).

### C4. `Navigations/LoginControl.xaml(.cs)`

- Merge `MaterialDesign.xaml` (salinan palette/style lokal yang sudah ada tetap; lokal menang).
- Hapus `fieldEditStyle` (`dxe:BaseEdit`).
- `connectionEdit` → `ComboBox` `inlineComboStyle`, `Tag="Select a connection profile"`,
  `ItemsSource`/`SelectedItem` sama, tampilan item lewat `ItemTemplate` (`TextBlock Text="{Binding ProfileName}"`),
  bukan `DisplayMemberPath`.
- `usernameEdit` → `TextBox` `inlineBoxStyle`, `Tag="e.g. andi.pratama"`,
  `Text="{Binding UserName, UpdateSourceTrigger=PropertyChanged}"`, `PreviewKeyDown` tetap.
- `passwordEdit` → `PasswordBox` `inlinePasswordStyle` dengan `PasswordChanged` satu baris yang menulis
  `Vm.Password` (pola `UserEditor.NewPasswordBox_PasswordChanged`). Kalau VM mengosongkan `Password`
  (mis. sesudah sign in gagal atau berhasil), `PasswordBox` ikut dikosongkan dengan pola
  `UserEditor` (bandingkan dulu supaya tidak memantul). Placeholder "Enter your password": `TextBlock`
  di atas `PasswordBox` di XAML, `IsHitTestVisible="False"`, opacity placeholder yang sama dengan
  `inlineBoxStyle`, tampil lewat `DataTrigger` saat `Password` di VM kosong.
- `rememberMeEdit` → `CheckBox` `checkBoxStyle`, `FontSize` 12.5, binding sama.
- `x:Name` dan `TabIndex` dipertahankan. Komentar `CredentialField_PreviewKeyDown` yang menyebut editor
  DevExpress diperbarui (alasan "Asked twice" tidak lagi soal DX; logika fallback `MoveFocus` boleh
  tetap).
- Semua tempat di code-behind yang membaca/menulis editor DX (`EditValue`, `Password` DX, dsb.)
  disesuaikan. Hapus `xmlns:dxe`.

### C5. `Navigations/UserManager.xaml(.cs)` dan `UserEditor.xaml`

- UserManager: merge `MaterialDesign.xaml`; `dateFromEdit` / `dateToEdit` → `DatePicker`
  `inlineDatePickerStyle`, `FontSize="12"`, `Tag="From"` / `Tag="To"`. Hapus `dateEditStyle` lokal.
  Code-behind `ClearSheet`: `EditValue = null` → `SelectedDate = null`. Filter tanggal memang belum
  terikat ke VM; plan ini tidak menyambungkannya.
- UserEditor: hapus `dateEditStyle` (`dxe:DateEdit`) yang tidak dipakai, dan `xmlns:dxe` kalau tidak ada
  pemakai lain.

### C6. `Navigations/DefaultHomeControl.xaml`

`dx:WaitIndicator` di kartu koneksi → `controls:WaitOverlay IsWaiting="{Binding InWaiting}"
Caption="{Binding WaiterText}"`, tetap di posisi yang sama (anak terakhir grid kartu, hanya menutupi
kartu itu). Hapus `xmlns:dx` kalau tidak ada pemakai lain.

---

## Tahap D — `EmWindow` dan `EmMessageBox`

### D1. `EmWindow`

- `Windows/EmWindow.cs`: `public class EmWindow : Window` dengan default style di
  `Themes/Generic.xaml`: `WindowChrome` (CaptionHeight 40, ResizeBorderThickness 6, GlassFrameThickness 0),
  title bar berisi ikon + `Title` + tombol caption (min / max-restore / close), lalu `ContentPresenter`.
  Latar/teks dari token tema; koreksi maximized memakai `MaximizedBorderThickness` yang dipindah dari
  `TabbedMainWindow` ke `EmWindow` (`TabbedMainWindow` merujuk ke sana).
- DependencyProperty `ShowMinimizeButton`, `ShowMaximizeButton` (default `true`; dialog mematikannya)
  dan `TitleBarContent` (`object?`, konten tambahan di kanan judul — belum dipakai, disiapkan untuk
  window lain).
- Tombol caption memakai `SystemCommands` (`MinimizeWindowCommand`, …) dengan `CommandBinding` di
  constructor `EmWindow` — ini internal control, bukan view model, jadi tidak melanggar aturan MVVM.
  `ResizeMode="NoResize"` menyembunyikan max/min secara otomatis.
- Style tombol caption (`captionButtonStyle`, `closeCaptionButtonStyle`) dipindah dari
  `TabbedMainWindow.xaml` ke `Styles/Window.xaml` (baru, di-merge `MaterialDesign.xaml`), dipakai
  `TabbedMainWindow` dan `EmWindow`.
- `TabbedMainWindow` **tidak** diturunkan dari `EmWindow` (title bar-nya memuat tab strip); ia hanya
  berbagi style dan `MaximizedBorderThickness`.

### D2. Window yang dipindah ke `EmWindow`

`Dialogs/ConnectionConfig`, `Dialogs/ConnectionConfigEditor` (bersama restyle Tahap C),
`Dialogs/DisplayExceptionData`, `Windows/DetachedWindow`: root XAML `dx:ThemedWindow` →
`windows:EmWindow`, code-behind `: ThemedWindow` → `: EmWindow`, hapus `xmlns:dx` dan
`using DevExpress.*`. `DetachedWindow` tetap berisi `SpaNavigationHost`, judul tetap mengikuti entri.

### D3. `EmMessageBox`

- `Dialogs/EmMessageBox.xaml(.cs)` + `EmMessageBoxVm`: `EmWindow` tanpa min/max,
  `SizeToContent`, lebar maks ±480. Isi: ikon FontAwesome sesuai `MessageBoxImage` (info
  `Solid_CircleInfo`/`infoBrush`, warning `Solid_TriangleExclamation`/`warningBrush`, error
  `Solid_CircleXmark`/`dangerBrush`, question `Solid_CircleQuestion`/`themeAccentBrush`), judul, pesan
  (teks wrap, bisa diseleksi/disalin), action strip dengan tombol sesuai `MessageBoxButton`
  (tombol default = `filledButtonStyle`, lainnya `textButtonStyle`/`tonalButtonStyle`),
  `IsDefault`/`IsCancel` sesuai `defaultButton`. Hasil lewat `RequestClose` (aturan MVVM no. 4).
- Method statis internal `EmMessageBox.Show(Window? owner, string title, string message,
  MessageBoxButton button, MessageBoxImage image, MessageBoxResult defaultButton)`.
- `Extensions.cs`: semua `ShowMbox*` memanggil `EmMessageBox.Show`; **signature publik tetap**.
  Hapus `using DevExpress.Xpf.Core`.

## Tahap E — Lepas paket DevExpress dari `Em.Ui.Wpf.Core`

- Hapus tiga `PackageReference` DevExpress dari `Em.Ui.Wpf.Core.csproj`.
- `Em.Ui.Wpf.Core` harus terbangun. Grep di `src/shared/Em.Ui.Wpf.Core` (di luar `bin/`/`obj/`) tidak
  menemukan `DevExpress`, `xmlns:dx`, `dxi:`, `dxt:`, `dxe:`, `dxg:`, `ThemedWindow`, `ThemedMessageBox`,
  `ApplicationThemeHelper`. XML doc/komentar yang menyebut DevExpress diperbarui atau dihapus (kecuali
  yang memang menjelaskan kenapa core tidak memakainya).

## Tahap F — Proyek `Em.Ui.Wpf.Core.DevExpress`

- `src/shared/Em.Ui.Wpf.Core.DevExpress/Em.Ui.Wpf.Core.DevExpress.csproj`: `net10.0-windows`,
  `UseWPF`, nullable, implicit usings, `IsPackable=false`; `ProjectReference` ke `Em.Ui.Wpf.Core`;
  `PackageReference` DevExpress yang tadinya di core (versi sama 25.2.5). Tambahkan ke
  `src/frontend/Em.Ui.Wpf.slnx`.
- `EmAppBuilderExtensions.UseDevExpress(this EmAppBuilder builder)` (namespace
  `Em.Ui.Wpf.DevExpress`): `builder.AddThemeApplier<DevExpressThemeApplier>()` dan
  `builder.AddLogoImageLoader<DevExpressSvgLogoLoader>()`. Idempotent karena keduanya `TryAddEnumerable`.
- `DevExpressThemeApplier : IThemeApplier`:
  - `Theme.CachePaletteThemes = true` sekali;
  - per instance `ThemeBase` (cache `ConditionalWeakTable`/dictionary): buat `ThemePalette` bernama
    `Em.{Variant}.{hash warna}`, isi warna sesuai tabel di bawah, `Theme.CreateTheme(palette,
    Variant == Light ? Theme.Win11Light : Theme.Win11Dark)`, `Theme.RegisterTheme`;
  - `ApplicationThemeHelper.ApplicationThemeName = theme.Name`.
- Pemetaan `ThemeBase` → nama warna palet Win11 (nama dicek di dokumentasi DX "How to: Use Palette
  Resources", 25.2; warna state hover/pressed dibiarkan milik tema dasar):

| Palet Win11 | Slot `ThemeBase` |
| --- | --- |
| `Accent`, `Backstage.Background`, `Button.CheckedBackground`, `Button.CheckedBorder` | `Primary` |
| `Foreground.Primary` | `OnSurface` |
| `Foreground.Secondary`, `Foreground.Muted` | `OnSurfaceVariant` |
| `WindowBackground` | `Surface` |
| `PanelBackground`, `Editor.Background` | `SurfaceContainerLow` |
| `Control.Background` | `SurfaceContainer` |
| `FlyoutBackground` | `SurfaceContainerHigh` |
| `Border`, `Delimiter`, `Separator`, `PanelBorder`, `Editor.Border`, `FlyoutBorder` | `OutlineVariant` |
| `WindowBorder` | `Outline` |
| `ListItem.SelectionAlt` | `PrimaryContainer` |
| `Custom.Red` / `Custom.Green` / `Custom.Blue` | `Error` / `Success` / `Info` |

  Kalau sebuah nama ternyata tidak dikenal `SetColor` (exception atau tanpa efek), buang dari tabel dan
  catat.
- `DevExpressSvgLogoLoader : ILogoImageLoader`: `.svg` → `WpfSvgRenderer.CreateImageSource(SvgImageHelper.CreateImage(uri))`
  (kode lama `BrandingInfo`), selain itu `null`.
- Host `src/frontend/Em.Ui.Wpf`: `ProjectReference` ke proyek DX; `Program.cs` memanggil
  `builder.UseDevExpress();` (sebelum `AddSampleModule()`); hapus `using DevExpress.Xpf.Core;`.
- Modul `Em.Sample`: `ProjectReference` ke proyek DX, tanpa pemanggilan apa pun.
- XML doc publik Bahasa Indonesia.

## Tahap G — MAUI

Mirror yang diwajibkan **Keep the UI cores in step**:

- `EmApp.CurrentTheme` → `ThemeVariant` (Settings: `"Light"`/`"Dark"` lama tetap terbaca),
  konstanta `LightTheme`/`DarkTheme` dihapus, `IsLightTheme` tetap (dihitung dari enum), `ActiveTheme`
  baru, `ThemeChanged` sudah ada. Pemakai disesuaikan (`AccountPanel`, `LoginControl`, dsb.).
- `Shared/BrandingInfo.cs` MAUI dihapus; `ApplyBranding` menerima `Em.Ui.Core.Shared.BrandingInfo`;
  logo bawaan `em_logo.png` pindah ke helper MAUI; `LogoImage` pemakai (`LoginControl`,
  `AccountPanel`) lewat helper itu; warna panel login dari `ActiveTheme.Brand`/`OnBrand`.
- Saat startup (sebelum halaman pertama), tulis nilai setiap kunci `*Light`/`*Dark` di
  `Application.Resources` dari `BrandingInfo.LightTheme`/`DarkTheme` (peta slot → kunci mengikuti nama
  yang sama: `primaryLight` ← `LightTheme.Primary`, dst.). `Palette.xaml` tetap sebagai nilai bawaan;
  komentarnya diperbarui.
- `EmAppBuilder.AddThemeApplier<T>()` MAUI + pemanggilan `IThemeApplier` di `ApplyTheme` MAUI
  (engine-level, jadi di-mirror walau belum ada pemakai MAUI).
- **Tidak di-mirror**: `ILogoImageLoader`/`AddLogoImageLoader` (MAUI mengubah SVG jadi bitmap saat build),
  token `Application.Resources` gaya WPF (MAUI memakai kunci palet), `EmWindow`, `EmMessageBox`, dan
  proyek DX. Sebutkan ini di reply eksekusi.
- `src/frontend/Em.Ui.Maui/MauiProgram.cs`: `ApplyBranding` disesuaikan.

## Tahap H — Dokumentasi dan memory

- `CLAUDE.md` root: tabel `src/shared` — baris baru `Em.Ui.Wpf.Core.DevExpress` (frontend, host dan
  modul yang memakai DX; `UseDevExpress()`, penerap tema DX, loader logo SVG; tidak dipublikasikan);
  baris `Em.Ui.Core` menyebut tema/branding bersama; baris `Em.Ui.Wpf.Core` menyebut bebas DevExpress.
- `src/frontend/CLAUDE.md`:
  - **Composition root**: contoh `Program.cs` dengan `builder.UseDevExpress()`.
  - **UI framework**: ditulis ulang — core memakai WPF standar + `EmWindow`/`EmMessageBox`; DevExpress
    hanya lewat `Em.Ui.Wpf.Core.DevExpress`, dipasang host; modul boleh mereferensikannya untuk control DX.
  - **Material design language**: aturan warna — satu-satunya warna tema adalah token `theme*Brush`
    (`DynamicResource`), bukan kunci palet DX; ganti contoh `dxi:ThemeResource`.
  - Bagian baru singkat **Theme and branding**: `ThemeBase`/`LightTheme`/`DarkTheme`, `BrandingInfo`,
    `CurrentTheme` (`ThemeVariant`), daftar token, `IThemeApplier`/`ILogoImageLoader`.
  - Isi Tahap C yang menyentuh dokumentasi (MVVM rule 3 contoh `RowDoubleClick`, style field di
    `Inputs.xaml`, daftar layar yang me-merge dictionary bersama).
- Memory: perbarui `reference_dx_palette_brush_keys.md` (kini hanya relevan di proyek DX / modul DX)
  dan `reference_wpf_default_style_trigger.md` (tetap relevan saat tema DX aktif); tambahkan memory
  project "Tema & branding bersama" (keputusan 2–5, 9–9c) dan pointernya di `MEMORY.md`.
- `doc/TODO-LIST.md`: item "Jalankan plan Update Branding" dipindah ke `doc/TODO-LIST.DONE.md`
  (aturan di kepala berkas: hapus, geser nomor, tulis bullet di DONE berisi apa yang dikerjakan dan hasil
  verifikasinya; daftar "Yang menunggu keputusan" disesuaikan). Item "Uji regresi MultiTab dan MAUI
  sesudah refactor navigasi" diperbarui: checklist uji manual plan ini ikut dirujuk.
- Pindahkan plan ini ke `plan/executed/` dan ubah statusnya.

---

## Verifikasi

1. Build rebuild penuh `src/frontend/Em.Ui.Wpf.slnx` dan `src/frontend/Em.Ui.Maui.slnx`: 0 error,
   tanpa warning baru dibanding Langkah 0. Kalau berkas terkunci oleh aplikasi yang sedang
   berjalan/di-debug, **jangan matikan prosesnya**: bangun proyek-proyek yang disentuh satu per satu, dan
   catat.
2. Grep Tahap E lolos; `Em.Ui.Core` tidak lagi punya `ThemeColors`/`EmTheme`; tidak ada lagi
   `PanelColor1`, `GetPanelBrush`, `GetAccentBrush`, `LightTheme = "`, `DarkTheme = "` di `src/`
   (di luar `bin/`/`obj/`, di luar `src/tools`).
3. Uji jalan singkat tanpa interaksi (±15 detik per run, screenshot, matikan hanya proses yang dijalankan
   sendiri):
   - Debug MultiTab dengan `UseDevExpress()` — startup tanpa crash/dialog error, warna title bar/kartu
     mengikuti tema Em.
   - Debug MultiTab **tanpa** `UseDevExpress()` (dikomentari sementara) — core tampil utuh tanpa
     DevExpress; kembalikan sesudahnya.
   - Release (layar login) — panel brand, field login, dan logo tampil.
   - Tema terang: ubah nilai `CurrentTheme` di Registry sementara ke `Light` (catat nilai asal,
     kembalikan sesudahnya), jalankan Debug, screenshot.
   - MAUI tidak diuji jalan oleh executor (butuh emulator); cukup build.
4. Checklist uji manual untuk user, ditulis di catatan eksekusi (tidak dijalankan executor):
   Tahap C —
   - Login: pilih koneksi, ketik username, Enter pindah ke password, Enter sign in; password salah →
     pesan error dan field password kosong; "Keep me signed in" tersimpan; placeholder muncul/hilang;
     Caps Lock hint tetap jalan; tema terang/gelap.
   - Connection Config: daftar tampil, pilih baris, klik ganda membuka editor, menu klik kanan
     Edit/Delete (Delete nonaktif untuk koneksi debug), Add, Close; tema terang/gelap.
   - Connection Config Editor: validasi nama/host, Timeout hanya angka 1–600 (ketik, tombol naik/turun,
     panah keyboard, paste teks), Ignore SSL, Test Connection menampilkan WaitOverlay, Save/Cancel.
   - Home SPA: Test Connection menampilkan WaitOverlay hanya di kartu koneksi.
   - UserManager: buka sheet filter, pilih tanggal From/To lewat kalender (terbaca di kedua tema), Clear
     mengosongkan keduanya.
   Tema, window, dan message box —
   - ganti tema terang/gelap dari tombol tema (MultiTab, SPA, login, window detach, window tear-off):
     semua window dan panel login ikut, termasuk control DX kalau ada;
   - `ShowMbox*` (mis. hapus koneksi, error sign in): dialog baru tampil benar di kedua tema, tombol
     default/cancel dan keyboard (Enter/Esc) bekerja;
   - `DisplayExceptionData` dan `DetachedWindow` dengan `EmWindow`: drag, klik ganda maximize, menu
     sistem, maximized tidak meluber (DPI 100% dan 150%);
   - tema yang tersimpan dari versi lama (`Win11Light`/`Win11Dark`) terbaca benar saat aplikasi dibuka;
   - MAUI: login, panel akun, ganti tema, warna palet sama seperti sebelumnya.

## Commit

Sesudah verifikasi 1–3 lolos: `git add` semua perubahan plan ini (termasuk proyek baru dan plan yang
dipindah), satu `git commit` di branch `spa-detach`, **tanpa push**:

- summary line dan body Bahasa Indonesia; kode, identifier, path apa adanya;
- body merangkum: tema bersama (`ThemeBase`/`LightTheme`/`DarkTheme`, `BrandingInfo` di `Em.Ui.Core`,
  `CurrentTheme` bertipe `ThemeVariant`); token tema WPF; control DX diganti native; `EmWindow` dan
  `EmMessageBox`; `Em.Ui.Wpf.Core` bebas DevExpress; proyek `Em.Ui.Wpf.Core.DevExpress` +
  `UseDevExpress()` di host; mirror MAUI dan yang sengaja tidak di-mirror;
- diakhiri `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Kalau verifikasi gagal dan tidak bisa diperbaiki, **jangan commit**. Catat penyebabnya dan laporkan.

## Catatan eksekusi

**Jalannya eksekusi.** Sesi pertama mengerjakan Langkah 0 (commit `fd8665b`) sampai uji jalan
verifikasi 3, lalu terhenti karena kehabisan token sebelum menulis catatan ini. Sesi kedua memeriksa
ulang hasilnya dari kode dan screenshot uji jalan, menyelesaikan sisa Tahap H (TODO-LIST, pemindahan
plan), menjalankan ulang verifikasi 1–2, dan menulis catatan ini. Catatan di bawah karena itu disusun
dari kode yang ada, bukan dari log sesi pertama.

**Pilihan yang diambil sendiri / penyimpangan dari plan:**

- *Token sistem (B2).* Selain tujuh token `theme*Brush`, `ApplyTheme()` juga menimpa
  `SystemColors.ControlTextBrushKey` dan `WindowTextBrushKey` di `Application.Resources` dengan
  `OnSurface`. Tanpa DevExpress, default style WPF standar membaca kunci itu (hitam) dan teks kontrol
  bawaan tidak terbaca di tema gelap. Nama kunci token dikumpulkan sebagai konstanta di
  `Shared/ThemeColorExtensions.cs`.
- *Panel brand login (B4).* Warna panel dan dekorasinya lewat `{DynamicResource themeBrandBrush}` /
  `themeOnBrandBrush` di XAML (bukan di `RenderBranding`), jadi ikut berganti saat tema diganti tanpa
  kode tambahan. `RenderBranding` tinggal mengisi teks dan logo; `BrandingImages.LoadLogo` menerima
  `IServiceProvider` untuk mencari `ILogoImageLoader`.
- *Palet DX (F).* Nama tema DX `Em{Variant}{fingerprint}`: fingerprint FNV-1a dari warna yang
  dipetakan (bukan `HashCode`, yang diacak per proses), supaya assembly tema yang di-cache
  `CachePaletteThemes` dari warna lama tidak termuat lagi saat warnanya berubah. Seluruh nama palet
  di tabel Tahap F dipertahankan (tidak ada yang dibuang).
- *MAUI (G).* Nilai kunci `*Light`/`*Dark` ditimpa di dalam kamus `Styles/Palette` itu sendiri
  (`Palette.xaml.cs`, kamus kini bertipe kelas `Palette` dan di-merge sebagai `<styles:Palette />` di
  setiap berkas `Styles/`), bukan di `Application.Resources`: style MAUI membaca warna lewat
  `StaticResource` sekali saat dimuat, jadi penulisan sesudahnya tidak sampai ke mana pun.
  `EmApp.BuildApp` mengisi `Palette.Branding` sebelum `App` membangun resource-nya. Logo bawaan MAUI
  lewat `Shared/BrandingImages.cs`.

**Verifikasi.**

1. Rebuild penuh `Em.Ui.Wpf.slnx`: 0 error, 0 warning. `Em.Ui.Maui.slnx`: 0 error, 0 warning.
   Jumlah warning awal Langkah 0 tidak tercatat oleh sesi pertama; hasil akhir 0 warning, jadi tidak
   ada warning baru.
2. Grep Tahap E di `src/shared/Em.Ui.Wpf.Core` (di luar `bin/`/`obj/`) tidak menemukan apa pun;
   `ThemeColors`, `EmTheme`, `PanelColor1`, `GetPanelBrush`, `GetAccentBrush`, `LightTheme = "`,
   `DarkTheme = "` tidak ada lagi di `src/` (di luar `bin/`/`obj/`/`src/tools`).
3. Uji jalan (sesi pertama, screenshot diperiksa ulang sesi kedua):
   - Debug MultiTab dengan `UseDevExpress()`: startup tanpa crash, title bar dan area kerja memakai
     warna tema gelap Em.
   - Debug MultiTab tanpa `UseDevExpress()`: core tampil utuh tanpa DevExpress; `UseDevExpress()`
     sudah dikembalikan di `Program.cs`.
   - Release (layar login): panel brand, logo, field login native, dan checkbox tampil benar.
   - Tema terang (Registry `CurrentTheme` diubah sementara): layar login terang dengan panel brand
     `#0F6CBD`. Nilai Registry sudah kembali ke nilai asalnya (`Win11Dark`), yang sekaligus menguji
     pemetaan nilai lama.
   - MAUI tidak diuji jalan (butuh emulator); cukup build.

**Checklist uji manual untuk user** — daftar di [Verifikasi](#verifikasi) nomor 4 berlaku apa adanya
(login, Connection Config, Connection Config Editor, Home SPA, UserManager, ganti tema di semua window,
`ShowMbox*`, `DisplayExceptionData`/`DetachedWindow` dengan `EmWindow`, tema tersimpan dari versi
lama, MAUI). Dirujuk dari TODO "Uji regresi MultiTab dan MAUI sesudah refactor navigasi".
