# Plan — Ganti control DevExpress (editor, grid, wait, image) di `Em.Ui.Wpf.Core` dengan WPF native

Status: **digantikan** oleh [update-branding.md](update-branding.md) (2026-09-26),
tidak dieksekusi sendiri. Isinya digabung ke plan itu sebagai Tahap C (keputusan tetap berlaku; warna
memakai token tema, dan `ThemedWindow` dialog koneksi diganti `EmWindow`). Setelah plan pengganti
dieksekusi, tautan di atas berpindah ke `plan/executed/update-branding.md`.
Dibuat: 2026-09-26, dari diskusi dengan user. Semua keputusan sudah dijawab user sebelum plan ini
ditulis (lihat [Keputusan final](#keputusan-final)).

**Aturan eksekusi.** Plan ini dijalankan tanpa bertanya ke user. Kalau ada hal yang tidak tercakup
di sini, pilih yang paling konsisten dengan keputusan final dan pola kode yang sudah ada, lalu catat
di [Catatan eksekusi](#catatan-eksekusi). Jangan berhenti untuk bertanya.

Baca dulu sebelum mulai:

- `src/frontend/CLAUDE.md`, section **MVVM binding rules (MANDATORY)**, **Material design language
  (MANDATORY)**, **XAML and XML comments**;
- `CLAUDE.md` root, section **Keep the UI cores in step** dan **XML doc comment updates**;
- referensi material: `Navigations/UserEditor.xaml` (field, combo, password), `Navigations/RoleManager.xaml`
  (merge dictionary bersama, daftar), `Dialogs/DisplayExceptionData.xaml` (banner + kartu + action strip).

---

## Latar belakang

`Em.Ui.Wpf.Core` akan dipublikasikan sebagai paket NuGet yang bebas dependensi berlisensi (lihat
`plan/executed/tabbed-main-window.md`). Inventaris 2026-09-26 menemukan control DevExpress berikut:

| Berkas | Control DX |
| --- | --- |
| `Dialogs/ConnectionConfig.xaml(.cs)` | `dxg:GridControl` + `TableView` (3 kolom, context menu, `RowDoubleClick` di code-behind), `dx:DXImage` |
| `Dialogs/ConnectionConfigEditor.xaml` | `dxe:TextEdit` ×2, `dxe:SpinEdit`, `dxe:CheckEdit`, `dx:WaitIndicator`, `dx:DXImage` |
| `Navigations/LoginControl.xaml` | `dxe:TextEdit`, `dxe:PasswordBoxEdit`, `dxe:ComboBoxEdit`, `dxe:CheckEdit`, style `fieldEditStyle` (`TargetType="dxe:BaseEdit"`) |
| `Navigations/UserManager.xaml(.cs)` | `dxe:DateEdit` ×2 (filter tanggal di sheet filter, belum terikat ke VM; code-behind mengosongkan `EditValue`), style `dateEditStyle` |
| `Navigations/UserEditor.xaml` | style `dateEditStyle` (`TargetType="dxe:DateEdit"`) yang **tidak dipakai** |
| `Navigations/DefaultHomeControl.xaml` | `dx:WaitIndicator` di kartu koneksi |

Plan ini hanya mengganti **control** di atas. Yang tetap DevExpress dan di luar cakupan:
`dx:ThemedWindow` (tiga dialog dan `DetachedWindow`), `ThemedMessageBox` (`Extensions.cs`), warna
tema `dxi:ThemeResource`, `ApplicationThemeHelper`, dan render SVG `BrandingInfo`. Paket
`DevExpress.Wpf` karena itu tetap direferensikan. Modul (`src/frontend/modules`) tidak disentuh.

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Cakupan | Editor `dxe:*` dan grid `dxg:*`, **ditambah** `dx:WaitIndicator` → `Controls/WaitOverlay` yang sudah ada, dan `dx:DXImage` → ikon FontAwesome. `ThemedWindow`, `ThemedMessageBox`, dan warna tema tidak disentuh. |
| 2 | Gaya dialog koneksi | `ConnectionConfig` dan `ConnectionConfigEditor` **di-restyle ke bahasa desain material** (merge `MaterialDesign.xaml`, banner + kartu + action strip, tombol pill, tanpa `SystemColors`). `ThemedWindow`-nya tetap. |
| 3 | Grid | `dxg:GridControl` → **`ListBox` bergaya daftar material** (baris seperti daftar UserManager/RoleManager). Kolom Profile / Server URL / Timeout lewat `Grid` di template baris. Klik ganda dan menu klik kanan lewat `InputBindings`/`ContextMenu`, **tanpa code-behind**. |
| 4 | SpinEdit | **Control baru `NumericBox`** di `Controls/`: turunan `TextBox`, hanya angka bulat, `Value`/`Minimum`/`Maximum`/`Increment`, tombol naik/turun. Style material-nya di `Styles/Inputs.xaml`. |
| 5 | DateEdit | **`DatePicker` bawaan WPF** + style material (field, tombol kalender, popup `Calendar`) di `Styles/Inputs.xaml`. Tanpa control buatan sendiri. |
| 6 | Password login | **Pola UserEditor**: `PasswordBox` dengan handler `PasswordChanged` satu baris di code-behind yang menyerahkan teks ke VM. |
| 7 | Letak style field | **`Styles/Inputs.xaml`**, dipakai login, dialog koneksi, dan UserManager. Salinan lokal di `UserEditor.xaml` (`fieldBoxStyle`, `fieldComboStyle`, `fieldPasswordStyle`) **tidak dimigrasi** di plan ini. |
| 8 | Commit | **Dua commit**, Bahasa Indonesia, di branch aktif (`spa-detach`), **tanpa push**: (a) sebelum eksekusi, commit tiga perubahan kecil yang masih tertunda; (b) sesudah verifikasi lolos, satu commit untuk plan ini. |

---

## Langkah 0 — Commit perubahan tertunda

Sebelum menyentuh kode, `git status` harus berisi hanya tiga perubahan kecil dari sesi 2026-09-26 (plus
berkas plan ini yang untracked):

- window tear-off tanpa menu (`CopyShellFrom` tidak menyalin menu dan identitas);
- window utama dibuka maximized dan `CenterScreen`;
- tombol dropdown daftar tab disembunyikan saat tidak ada tab (`TabListButtonVisibility`);

beserta pembaruan `src/frontend/CLAUDE.md` dan `plan/executed/multitab-tabbed-main-window.md`.

Pastikan build `src/shared/Em.Ui.Wpf.Core` bersih, lalu `git add` berkas-berkas itu (**bukan**
berkas plan ini) dan commit. Pesan Bahasa Indonesia, diakhiri
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Kalau `git status` berisi hal lain yang tidak
dikenali, jangan ikut di-commit; catat di catatan eksekusi.

## Langkah 1 — Style field bersama di `Styles/Inputs.xaml`

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

Warna latar popup memakai sumber yang sama dengan popup lain di repo
(`{dxi:ThemeResource {dxt:BrushesThemeKey ResourceKey=EditorPopupListBoxBackground}}`) — warna tema di luar
cakupan plan ini. Kalau sebuah resource dictionary tidak bisa memakai `dxi:ThemeResource` (lihat
catatan eksekusi `plan/executed/tabbed-main-window.md`: ia `MultiBinding` yang butuh elemen di visual
tree), pakai pola yang sudah dipakai `Styles/*.xaml` lain untuk warna aksen, atau ambil dari
`Background` elemen target lewat binding, dan catat pilihannya.

## Langkah 2 — Control `NumericBox`

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

## Langkah 3 — Dialog koneksi

### `Dialogs/ConnectionConfig.xaml(.cs)`

- Merge `Styles/MaterialDesign.xaml` di `ThemedWindow.Resources`.
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

### `Dialogs/ConnectionConfigEditor.xaml(.cs)`

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
- Hapus `xmlns:dxe`; `xmlns:dx` tetap untuk `ThemedWindow`.

## Langkah 4 — `Navigations/LoginControl.xaml(.cs)`

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

## Langkah 5 — `Navigations/UserManager.xaml(.cs)` dan `UserEditor.xaml`

- UserManager: merge `MaterialDesign.xaml`; `dateFromEdit` / `dateToEdit` → `DatePicker`
  `inlineDatePickerStyle`, `FontSize="12"`, `Tag="From"` / `Tag="To"`. Hapus `dateEditStyle` lokal.
  Code-behind `ClearSheet`: `EditValue = null` → `SelectedDate = null`. Filter tanggal memang belum
  terikat ke VM; plan ini tidak menyambungkannya.
- UserEditor: hapus `dateEditStyle` (`dxe:DateEdit`) yang tidak dipakai, dan `xmlns:dxe` kalau tidak ada
  pemakai lain.

## Langkah 6 — `Navigations/DefaultHomeControl.xaml`

`dx:WaitIndicator` di kartu koneksi → `controls:WaitOverlay IsWaiting="{Binding InWaiting}"
Caption="{Binding WaiterText}"`, tetap di posisi yang sama (anak terakhir grid kartu, hanya menutupi
kartu itu). Hapus `xmlns:dx` kalau tidak ada pemakai lain.

## Langkah 7 — Mirror MAUI

Tidak ada. Semua yang diubah adalah view/XAML dan control primitif WPF (`NumericBox`), yang termasuk
pengecualian "XAML/view code" di `CLAUDE.md` root. `Em.Ui.Core` tidak disentuh.

## Langkah 8 — Dokumentasi

- `src/frontend/CLAUDE.md`:
  - MVVM rule 3 memakai "DevExpress `RowDoubleClick`" sebagai contoh event tanpa binding bersih. Contoh
    itu tidak ada lagi di kode: ganti dengan contoh umum (mis. `PasswordBox.PasswordChanged`, yang
    memang tetap di code-behind), dan sebut bahwa klik ganda baris dibinding lewat `MouseBinding`.
  - Material design language: sebut bahwa `Styles/Inputs.xaml` kini juga memuat field berbingkai
    (`field*Style`), field inline (`inline*Style`), `fieldNumericBoxStyle`, dan `materialCalendarStyle`;
    tambahkan `Controls/NumericBox` ke daftar control primitif kalau ada daftar semacam itu.
  - Paragraf "`UserManager`, `UserEditor`, `LoginControl` and the dialogs still carry their own copy…":
    sesuaikan — LoginControl, UserManager, dan kedua dialog koneksi kini me-merge dictionary bersama
    (salinan lokalnya belum dibuang).
  - UI framework: sebut bahwa `Em.Ui.Wpf.Core` tidak lagi memakai editor/grid DevExpress; yang tersisa
    `ThemedWindow`, `ThemedMessageBox`, warna tema, dan render SVG logo.
- Plan ini dipindah ke `plan/executed/` dengan status diperbarui.

## Verifikasi

1. `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-incremental`: 0 error, tanpa warning baru dibanding
   sebelum eksekusi (catat jumlah awal). Kalau build solution gagal karena berkas terkunci oleh aplikasi
   yang sedang berjalan/di-debug, **jangan matikan prosesnya**: bangun
   `src/shared/Em.Ui.Wpf.Core/Em.Ui.Wpf.Core.csproj` saja, dan catat.
2. Grep di `src/shared/Em.Ui.Wpf.Core` (di luar `bin/`/`obj/`) tidak menemukan: `dxe:`, `dxg:`,
   `dx:WaitIndicator`, `dx:DXImage`, `DevExpress.Xpf.Grid`, `DevExpress.Xpf.Editors`, `RowDoubleClick`,
   `EditValue`, `SystemColors` (di dua dialog koneksi).
3. Uji jalan singkat tanpa interaksi (±15 detik per run, screenshot layar, lalu matikan proses yang
   dijalankan sendiri):
   - **Release** (tanpa koneksi debug, jadi layar login tampil): layar login bergaya sama seperti
     sebelumnya — combobox koneksi, username, password (dengan placeholder), checkbox "Keep me signed
     in", tombol Sign in — di tema aktif. Kalau build Release tidak bisa dijalankan, catat.
   - **Debug** MultiTab: startup tanpa crash dan tanpa dialog error.
4. Checklist uji manual untuk user, ditulis di catatan eksekusi (tidak dijalankan executor):
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

## Commit

Sesudah verifikasi 1–3 lolos: `git add` semua perubahan plan ini (termasuk plan yang dipindah), lalu
satu `git commit` di branch `spa-detach`, **tanpa push**:

- summary line dan body dalam Bahasa Indonesia; kode, identifier, dan path apa adanya;
- body merangkum: control DX yang diganti dan penggantinya (ListBox, TextBox/PasswordBox/ComboBox/
  CheckBox, `NumericBox` baru, `DatePicker`, `WaitOverlay`, FontAwesome); style field bersama di
  `Styles/Inputs.xaml`; restyle material dua dialog koneksi; yang masih DevExpress
  (`ThemedWindow`, `ThemedMessageBox`, warna tema, SVG logo); tidak ada mirror MAUI;
- diakhiri `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Kalau verifikasi gagal dan tidak bisa diperbaiki, **jangan commit**. Catat penyebabnya dan laporkan.

## Catatan eksekusi

_(diisi saat eksekusi: penyimpangan dari plan, pilihan yang diambil sendiri beserta alasannya,
jumlah warning sebelum/sesudah, hasil uji jalan, dan checklist uji manual di atas)_
