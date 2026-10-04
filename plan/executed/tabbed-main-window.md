# Plan — `TabbedMainWindow`: window utama bertab tanpa DevExpress

Status: **sudah dieksekusi** (2026-09-25). Lihat [Catatan eksekusi](#catatan-eksekusi) untuk penyimpangan.
Dibuat: 2026-09-25 — hasil diskusi melepas DevExpress dari paket publik. Eksekusinya ditunda atas
permintaan user supaya bisa mulai dengan context bersih.

---

## Latar belakang (hasil diskusi 2026-09-25)

- `Em.Libs`, `Em.Ui.Core`, dan `Em.Ui.Wpf.Core` akan dipublikasikan ke NuGet dan harus
  **bebas dependensi berlisensi**. Saat ini hanya `Em.Ui.Wpf.Core` yang masih memakai DevExpress
  (`ThemedWindow`, `DXTabControl`, palette `dxi:ThemeResource`, editor, grid, bars, `ThemedMessageBox`,
  `DXImage`/SVG, `WaitIndicator`).
- DevExpress tetap **boleh dipakai modul, tapi tidak wajib**. Sistem ini harus bisa dipakai aplikasi
  tanpa DevExpress.
- Setup tema DX nantinya ada di proyek internal `Em.Ui.Wpf.DevExpress` (tidak dipublikasikan),
  direferensikan hanya oleh modul yang memakai DX, dan dipanggil dari extension modul
  (mis. `builder.UseDevExpressTheme()` di `AddSampleModule`), idempotent lewat
  `TryAddEnumerable<IThemeApplier, ...>`. **Bukan bagian dari plan ini.**
- Layout MultiTab lama bergantung pada `DXTabControl`, jadi butuh sistem tab sendiri. Itulah
  `TabbedMainWindow`.

### Status objek tema (terkait, belum final)

Sudah dibuat, belum di-commit, belum dipakai siapa pun, di
`src/shared/Em.Ui.Core/Ui.Core/shared/`: `ThemeColor.cs`, `ThemeVariant.cs`, `ThemeColors.cs`,
`EmTheme.cs`. Arah yang disepakati tapi **belum dikerjakan**: rombak menjadi `ThemeBase` +
`LightTheme : ThemeBase` + `DarkTheme : ThemeBase`, dan pasangan terang/gelap dipegang oleh
`BrandingInfo` (versi data di `Em.Ui.Core`), bukan oleh `EmTheme`. Tanyakan user dulu sebelum
menyentuhnya. `TabbedMainWindow` **tidak** bergantung pada objek ini.

---

## Tujuan

Membuat window baru `TabbedMainWindow` di `src/shared/Em.Ui.Wpf.Core/Windows/`
(`TabbedMainWindow.xaml` + `TabbedMainWindow.xaml.cs`), turunan `System.Windows.Window` biasa (bukan
`ThemedWindow`), dengan title bar kustom lewat `WindowChrome`. Satu baris di atas berisi:

```
[ikon + judul] [Apps ▾] [tab][tab][tab] ........ (area drag) ........ [◐] [avatar ▾] [─][□][✕]
```

- **Apps**: dropdown berisi menu.
- **Tab** sejajar dengan tombol Apps, di baris yang sama.
- Di kiri tombol min/max/close: **tombol tema (halfmoon)** dan **tombol user (avatar + menu akun)**,
  meniru persis `SpaNavigationHost.xaml`.
- Di bawahnya: area konten berupa kartu yang menampilkan isi tab aktif.

Window ini **berdiri sendiri**. Tidak dihubungkan ke `EmApp.Run`, `MainWindow`, `NavigationStack`,
atau `ApplicationLayout`. Integrasi menyusul di plan lain.

## Aturan yang wajib diikuti

Baca dulu `src/frontend/CLAUDE.md`, section **MVVM binding rules (MANDATORY)**,
**Material design language (MANDATORY)**, dan **XAML and XML comments**. Intinya:

- **Maksimalkan XAML, minimalkan code-behind** (permintaan eksplisit user). Code-behind hanya untuk
  jembatan yang memang terikat ke view, mis. `Vm.RequestClose += Close;`.
- VM bernama `TabbedMainWindowVm : MvvmModelBase`, **di file `.cs` yang sama** dengan code-behind.
  `DataContext` di-set di XAML (`<Window.DataContext><local:TabbedMainWindowVm /></Window.DataContext>`),
  code-behind membacanya lewat `public TabbedMainWindowVm Vm => (TabbedMainWindowVm)DataContext;`.
- Property ter-bind memakai `Get<T>()` / `Set(value)`. Command hanya lewat
  `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)` di konstruktor, di-bind sebagai
  `Command="{Binding Commands[XxxCommand]}"`. Can-execute berupa method bernama `...Allowed`, bukan
  lambda. Command tidak diekspos sebagai property.
- Bahasa desain material: merge
  `pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml`, pakai token
  `surfaceBrush`, `surfaceStrongBrush`, `outlineBrush`, `dividerBrush`, `stateHoverBrush`,
  `statePressedBrush`, `dangerBrush`, dan `flatIconButtonStyle`. Kartu `CornerRadius="14"`, hover/pressed
  lewat state layer, disabled `Opacity 0.4`, `FocusVisualStyle="{x:Null}"` kalau template menggambar
  fokus sendiri, `Cursor="Hand"` pada elemen yang benar-benar menjawab klik.
- **Jangan tulis komentar penjelasan di XAML.** Penanda seksi pendek (`<!-- ==== title bar ==== -->`)
  boleh.
- XML doc untuk member `public` dalam Bahasa Indonesia; komentar inline, pesan exception, dan
  identifier tetap bahasa Inggris.

## Warna: satu-satunya titik sentuh DevExpress

Window ini dimaksudkan bebas DX, tapi selama objek tema belum tersambung, warna latar, teks, aksen,
dan latar popup masih hanya tersedia dari tema DX. Kumpulkan **keempatnya sebagai alias di
`Window.Resources`**, dan seluruh markup lain hanya merujuk ke alias itu:

| Alias | Sumber sementara |
| --- | --- |
| `windowBackgroundBrush` | `{dxi:ThemeResource {dxt:ThemedWindowThemeKey ResourceKey=WindowActiveContentBackground}}` |
| `windowForegroundBrush` | `{dxi:ThemeResource {dxt:ThemedWindowThemeKey ResourceKey=WindowContentForeground}}` |
| `accentBrush` | `{dxi:ThemeResource {dxt:PaletteBrushThemeKey ResourceKey=Accent}}` |
| `popupBackgroundBrush` | `{dxi:ThemeResource {dxt:BrushesThemeKey ResourceKey=EditorPopupListBoxBackground}}` |

Dengan begitu, begitu penerap tema Em ada, cukup empat baris itu yang diganti. Untuk permukaan
opaque jangan pakai `PaletteBrushThemeKey` selain `Accent` (ResourceKey-nya string, nama salah
menjadi transparan tanpa error).

## Struktur window

### `WindowChrome`

- `CaptionHeight` = tinggi title bar (sekitar 40), `ResizeBorderThickness="6"`,
  `GlassFrameThickness="0"`, `UseAeroCaptionButtons="False"`, `CornerRadius="0"`.
- Semua elemen interaktif di title bar (tombol Apps, tab strip, tombol tema, tombol user, tombol
  caption) diberi `WindowChrome.IsHitTestVisibleInChrome="True"`. Property ini diwariskan, jadi cukup
  di kontainernya. Area kosong tetap bisa di-drag, klik ganda untuk maximize, dan klik kanan
  memunculkan menu sistem.
- **Koreksi maximized**: window dengan `WindowChrome` meluber keluar layar saat maximized. Pakai
  `Window.Style` dengan trigger `WindowState=Maximized` yang menyetel `BorderThickness` ke
  `{x:Static SystemParameters.WindowResizeBorderThickness}`. `Background`/`Foreground` window juga
  di-set lewat setter di style itu, bukan atribut lokal, supaya tidak bentrok dengan trigger. Uji di
  DPI 100% dan 150%. Kalau hasilnya masih meluber, sesuaikan dan catat alasannya di reply.

### Title bar (Grid satu baris)

Kolom: ikon + judul (Auto) | Apps (Auto) | tab strip (`*`) | tema (Auto) | user (Auto) | caption (Auto).

1. **Ikon + judul**: ikon `pack://application:,,,/Em.Ui.Wpf.Core;component/Assets/Icons/Logo.ico`
   dan `Title` (binding ke `Vm.Title`).
2. **Apps**: `ToggleButton` + `Popup`, dengan `IsChecked` dan `IsOpen` di-bind ke property yang sama
   (`IsAppsMenuOpen`, TwoWay), `StaysOpen="False"`. Ini pola yang sama dengan menu akun di
   `SpaNavigationHost.xaml`. Isi popup: `ItemsControl` dari `AppMenu` (grup), tiap grup punya caption
   (11px, SemiBold, upper case, opacity 0.55) dan daftar item bergaya baris menu (salin
   `accountMenuItemStyle` dari `SpaNavigationHost.xaml`). Klik item memanggil
   `Commands[OpenMenuItemCommand]` dengan `CommandParameter="{Binding}"`, lewat `RelativeSource`
   ke `Window` untuk mencapai VM. Command menutup popup dengan menulis `IsAppsMenuOpen = false`.
   Popup memakai `popupBackgroundBrush`, `CornerRadius 10`, outline, dan `DropShadowEffect` seperti
   menu akun SPA.
3. **Tab strip**: `ListBox` dengan `ItemsSource="{Binding Tabs}"`,
   `SelectedItem="{Binding SelectedTab, Mode=TwoWay}"`, `ItemsPanel` berupa `StackPanel` horizontal,
   `HorizontalAlignment="Left"` supaya sisa kolom tetap menjadi area drag, dan `ScrollViewer`
   horizontal tanpa scrollbar yang terlihat. Style item mengikuti `materialTabItemStyle` di
   `Styles/Tabs.xaml`: state layer pill saat hover, underline aksen 3px di bawah tab aktif, caption
   SemiBold dan berwarna aksen pada **strip caption**, bukan pada item. Tidak ada kotak/header tab.
   Isi tiap tab: judul (`TextTrimming`, `MaxWidth` sekitar 200) dan tombol close kecil
   (`flatIconButtonStyle` diperkecil, ikon `Solid_Xmark`) yang terlihat saat hover atau saat tab
   aktif. Tombol close memanggil `Commands[CloseTabCommand]` dengan `CommandParameter="{Binding}"`.
4. **Tombol tema**: sama dengan SPA, `Button` `flatIconButtonStyle` dengan
   `<fa:FontAwesome FontSize="18" Icon="Solid_CircleHalfStroke" />`, ToolTip "Color Theme",
   `Commands[ColorThemeCommand]`.
5. **Tombol user**: salin dari SPA, yaitu `accountButtonStyle` (termasuk setter `Foreground` yang
   menetralkan trigger `IsChecked` bawaan tema DX), avatar inisial 24px, dan chevron `Solid_ChevronDown`.
   Popup akun berisi header (avatar 38px, nama, akun), divider, "Change Password" (`Solid_Key`), divider,
   dan "Sign Out" (`Solid_RightFromBracket`), terbuka lewat `IsUserMenuOpen`.
6. **Tombol caption** min / max-restore / close: `Button` 46×40 kotak tanpa radius, glyph dari font
   `Segoe Fluent Icons, Segoe MDL2 Assets` (`&#xE921;` minimize, `&#xE922;` maximize, `&#xE923;`
   restore, `&#xE8BB;` close). Hover memakai `stateHoverBrush`, sedangkan hover tombol close memakai
   `dangerBrush` dengan teks putih. Glyph max/restore berganti lewat `DataTrigger` pada `WindowState`.
   Tidak memakai `SystemCommands` karena butuh `CommandBinding` di code-behind. Sebagai gantinya:
   - `Window.WindowState="{Binding WindowState, Mode=TwoWay}"`;
   - `MinimizeCommand` / `MaximizeRestoreCommand` cukup menulis `WindowState` di VM;
   - `CloseWindowCommand` memicu `event Action? RequestClose`, dan code-behind menjembataninya dengan
     `Vm.RequestClose += Close;`.

### Area konten

`Border` kartu (`Margin="8,0,8,8"`, `CornerRadius="14"`, `surfaceBrush`, 1px `outlineBrush`) berisi
`ContentControl Content="{Binding SelectedTab.Content}"`. Saat `SelectedTab` null, tampilkan teks
kosong yang redup ("No tab open", opacity 0.55) lewat `DataTrigger`.

## `TabbedMainWindowVm` (di `TabbedMainWindow.xaml.cs`)

Property (semuanya `Get`/`Set`):

| Property | Tipe | Keterangan |
| --- | --- | --- |
| `Title` | `string` | judul window |
| `WindowState` | `WindowState` | two-way dengan window |
| `Tabs` | `ObservableCollection<TabbedMainWindowTab>` | read-only, dibuat di konstruktor |
| `SelectedTab` | `TabbedMainWindowTab?` | memicu `RaiseCanExecuteChanged` bila perlu |
| `AppMenu` | `ObservableCollection<TabbedMainWindowMenuGroup>` | isi dropdown Apps |
| `IsAppsMenuOpen` | `bool` | |
| `IsUserMenuOpen` | `bool` | |
| `UserDisplayName`, `UserAccount`, `UserInitials` | `string` | diisi dari luar; inisial `?` bila kosong |

Command (nama handler dengan akhiran `Command`, key `nameof`):

| Command | Perilaku |
| --- | --- |
| `OpenMenuItemCommand` (`TabbedMainWindowMenuItem`) | tutup popup Apps, lalu `OpenTab(item.Title, item.CreateContent)` |
| `CloseTabCommand` (`TabbedMainWindowTab`) | hapus tab. Kalau yang ditutup tab aktif, pilih tetangganya (kanan dulu, lalu kiri) |
| `ColorThemeCommand` | kalau `EmApp` ada: tukar `EmApp.CurrentTheme` Light ⇄ Dark, seperti `SpaNavigationHostVm.ColorTheme`. `ColorThemeCommandAllowed()` → `EmApp != null` |
| `ChangePasswordCommand` | tutup menu akun. `ChangePasswordCommandAllowed()` → `false` (sama dengan SPA) |
| `SignOutCommand` | tutup menu akun, lalu picu `event Action? SignOutRequested` (window ini tidak mengurus sesi) |
| `MinimizeCommand`, `MaximizeRestoreCommand`, `CloseWindowCommand` | lihat bagian tombol caption |

Method publik:

- `TabbedMainWindowTab OpenTab(string title, Func<object> createContent)`: **judul adalah kunci unik**,
  sama dengan aturan navigasi. Kalau judul sudah ada, cukup pilih tab itu tanpa membuat ulang isinya.
  Kalau belum, buat tab baru, tambahkan, lalu pilih.

Model pendukung (file yang sama, turunan `NotifyPropertyBase` dari `Em.Shared`):

- `TabbedMainWindowTab`: `Title` (`Get`/`Set`), `Content` (`object`).
- `TabbedMainWindowMenuGroup`: `Caption`, `Items` (`ObservableCollection<TabbedMainWindowMenuItem>`).
- `TabbedMainWindowMenuItem`: `Title`, `Subtitle?`, `CreateContent` (`Func<object>`).

Avatar: SPA memakai palet warna privat di `SpaNavigationHostVm` (`AvatarBrushes`, FNV-1a). Untuk
sekarang cukup latar netral (`outlineStrongBrush`). Kalau ingin warna yang sama dengan SPA, **tanya
user dulu** apakah palet itu boleh diangkat ke tempat bersama.

## Di luar cakupan

- Integrasi ke `EmApp` (`Run`, `ApplicationLayout`, `NavigationStack`, menu dari `Navigations`).
- Snap Layouts Windows 11 pada hover tombol maximize (butuh hook `WM_NCHITTEST` → `HTMAXBUTTON`).
- Drag tab untuk mengubah urutan, drag keluar menjadi window baru, `Ctrl+Tab`/`Ctrl+W`, dan overflow
  tab ke dropdown.
- Menghapus `TabHostWindow`/`TabWorkspaceHost`/`MainWindow` lama.
- Mirror ke `Em.Ui.Maui.Core`: tidak perlu, karena ini window/view khusus WPF, bukan anggota engine.

## Verifikasi

1. `dotnet build src/frontend/Em.Ui.Wpf.slnx` tanpa error dan tanpa warning baru.
2. Uji visual sementara (jangan di-commit): buka `TabbedMainWindow` dari `Program.cs` atau dari
   tombol debug dengan beberapa grup menu dan tab contoh, lalu cek:
   - tab sejajar dengan Apps;
   - area kosong bisa di-drag, klik ganda maximize/restore, klik kanan memunculkan menu sistem;
   - maximized tidak meluber; min/max/restore/close bekerja; tombol close berwarna merah saat hover;
   - Apps dan menu akun tertutup saat klik di luar, dan tidak terbuka lagi karena klik yang sama;
   - membuka judul yang sudah ada hanya memindah pilihan; menutup tab aktif memilih tetangganya;
   - tombol halfmoon mengganti tema dan semua warna mengikuti (terang dan gelap).
3. Kembalikan perubahan uji di `Program.cs` sebelum selesai. Jangan commit kecuali diminta.

## Catatan eksekusi

- **Alias warna bukan entri `Window.Resources`.** `dxi:ThemeResource` adalah `MultiBinding` yang
  membutuhkan elemen target di visual tree (dicek lewat reflection:
  `DevExpress.Xpf.Core.Native.ThemeResourceExtension : DXMarkupExtensionBase`, `CreateBinding`), jadi
  tidak bisa disimpan sebagai entri dictionary. Keempat alias dibuat sebagai `Border` tersembunyi
  (`Visibility="Collapsed"`) bernama `windowBackgroundBrush`, `windowForegroundBrush`, `accentBrush`,
  `popupBackgroundBrush` di seksi `theme colours`, dan markup lain merujuknya lewat
  `{Binding Background, ElementName=...}`. Titik gantinya tetap empat baris itu saja.
- Tombol close tab memakai `Visibility="Hidden"` (bukan `Collapsed`) saat tidak aktif/hover, supaya
  lebar tab tidak melompat saat kursor lewat.
- Konstruktor kedua `TabbedMainWindow(EmApp app)` mengisi `Vm.EmApp` (setter-nya `internal`).
- Verifikasi: build `Em.Ui.Wpf.Core` bersih (0 warning); uji visual lewat `Program.cs` sementara
  (sudah dikembalikan): tema gelap, terang, ganti tema saat window tampil, popup Apps, menu akun,
  maximized (tidak meluber di DPI 100%), kosong ("No tab open"). DPI 150% dan interaksi mouse
  (drag, klik ganda, menu sistem, klik di luar popup) belum diuji.
- **Menu Apps dibuat bertingkat** (permintaan lanjutan 2026-09-25). `Popup` + `ItemsControl` bergrup
  diganti `ContextMenu` milik tombol Apps dengan `MenuItem` rekursif, supaya buka-submenu-saat-hover,
  delay, dan navigasi keyboard memakai perilaku bawaan WPF. `TabbedMainWindowMenuGroup` dihapus;
  `TabbedMainWindowMenuItem` kini punya `Items` (anak), dan `CreateContent` bernilai `null` untuk
  item cabang. Tiga hal teknis yang perlu diingat:
  - `PlacementTarget` di-set di code-behind (`appsMenu.PlacementTarget = appsButton;`), karena
    `ContextMenu` yang dibuka lewat `IsOpen` tidak mendapatkannya sendiri, padahal `DataContext`-nya
    dibaca lewat target itu.
  - Latar dan warna teks `ContextMenu` diambil lewat `x:Reference` ke alias warna, karena
    `ContextMenu` berada di luar pohon window sehingga `ElementName` dan pewarisan tidak sampai.
  - Tombol Apps memakai `ClickMode="Press"` dan tidak bisa diklik sampai 200 ms setelah menu tertutup
    (storyboard pada `IsHitTestVisible`). Tanpa itu, menu langsung tertutup lagi saat dibuka, atau
    klik yang menutupnya diteruskan ke tombol dan membukanya kembali.
- **Perilaku tab** (permintaan lanjutan 2026-09-25), meniru `MainWindow` lama yang memakai DevExpress:
  - Klik tengah menutup tab: `MouseBinding MouseAction="MiddleClick"` di template tab, murni XAML.
  - Kursor panah biasa di tab dan tombol close-nya (`Cursor="Hand"` dihapus).
  - Drag dan drag-out ada di `Windows/TabbedMainWindowTabDrag.cs`, sebuah attached behavior
    (`local:TabbedMainWindowTabDrag.IsEnabled="True"` pada strip tab). Perilakunya:
    - Tab bisa digeser ke kiri/kanan secara langsung.
    - Tab bisa ditarik lebih dari 24px keluar dari strip. Selama itu, sebuah "ghost" (popup berisi
      judul tab) mengikuti kursor.
    - Kalau dilepas di strip window lain, tab pindah ke window itu. Ditolak kalau di sana sudah ada
      tab dengan judul yang sama.
    - Kalau dilepas di tempat kosong, lahir window baru lewat `TabbedMainWindow.TearOff`. Window baru
      menyalin judul, menu, pengguna, dan aplikasi (`CopyShellFrom`), lalu menutup sendiri saat kosong
      (`ClosesWhenEmpty`).
    - Tab terakhir window utama tidak bisa ditarik keluar. Window hasil tear-off yang tinggal satu
      tab cukup dipindah posisinya.
  - Operasi daftar tab ada di VM: `MoveTab`, `InsertTab`, `RemoveTab`, `CanTearOff`,
    `CanAcceptTab`.
  - Capture mouse dipegang item tab, bukan `ListBox`. `ListBox` yang memegang capture memilih item
    yang dilewati kursor, sehingga tab aktif ikut berganti di tengah drag.
- **Window hasil drag-out** (permintaan lanjutan 2026-09-25):
  - Tidak menampilkan menu Apps, tombol tema, dan tombol akun. Ketiganya disembunyikan lewat
    `ClosesWhenEmpty` + `InverseBoolToVisibilityConverter`.
  - Tab tunggal tidak bisa di-drag-out lagi, di window mana pun (`CanTearOff` = masih ada tab lain).
    Dengan begitu perilaku lama yang memindah window satu-tab dihapus.
  - Tab tunggal di window luar tetap boleh di-drag-in ke window lain (`CanMoveOut`). Setelah itu
    window luarnya menutup sendiri.
  - Tab tunggal di window utama tidak bisa keluar sama sekali.
- **Combobox koneksi dan dropdown Tools** (permintaan lanjutan 2026-09-25), baru sebatas layout
  dengan isi dummy. Urutannya di kiri tombol tema: `[koneksi ▾] [Tools ▾] [◐]`.
  - Combobox memakai `titleBarComboStyle`, versi ringkas `fieldComboStyle` milik UserEditor: pill
    32px, ikon server, dan popup yang sama dengan menu lain. Datanya `Connections`/`SelectedConnection`,
    sementara berupa string dummy dan belum tersambung ke `EmApp.UIConnections`/`ActiveConnection`.
  - Tools memakai pola yang sama dengan Apps: `ToggleButton` + `ContextMenu` bertingkat, dengan
    `ToolsMenu` dan `IsToolsMenuOpen`. Isinya dummy, meniru menu Tools di `MainWindow`.
  - Keduanya ikut disembunyikan di window hasil drag-out.
- **Tab banyak** (permintaan lanjutan 2026-09-25): paket 1 + 2 + 3.
  1. **Tab menyusut.** `Controls/TabStripPanel.cs` menggantikan `StackPanel`. Tab selebar isinya
     selama muat. Kalau tidak muat, semua tab dibatasi satu lebar bersama, jadi yang paling lebar
     menyusut lebih dulu dan judulnya terpotong "…". Batas bawahnya `MinItemWidth` (110).
  2. **Strip bisa digeser.** Panel yang sama mengimplementasikan `IScrollInfo` di dalam `ScrollViewer`
     bertemplate sendiri: tanpa scrollbar, dengan tombol ‹ › (`ScrollBar.LineLeft/RightCommand`) yang
     hanya tampil saat tidak muat, dan roda mouse menggeser mendatar. Tab yang dipilih dari kode
     digeser ke dalam pandangan lewat `ScrollIntoView` di code-behind.
  3. **Dropdown daftar tab.** Tombol ⌄ di ujung kanan strip membuka daftar semua tab: tab aktif
     ditandai, tiap baris punya tombol close, dan kotak pencarian muncul mulai 8 tab
     (`TabListItems`, `TabListFilter`, `IsTabSearchVisible`, `SelectTabCommand`,
     `TabbedMainWindowTab.IsActive`).
  - Belum ada: menandai tab yang sedang tersembunyi di dropdown, dan menggeser strip otomatis saat
    tab di-drag ke tepi.
- **Menu klik kanan pada tab** (permintaan lanjutan 2026-09-25, bagian dari nomor 4): Close, Close
  Others, Close to the Right, lalu separator, lalu Move to New Window.
  - `ContextMenu` dipasang lewat style item tab. `ListBoxItem.Tag` memegang VM window, supaya
    menu, yang berada di luar pohon window, tetap bisa mencapai command-nya. Latar menu diambil dari
    `ListBoxItem.Background`, yang diisi alias `popupBackgroundBrush`.
  - Command di VM: `CloseOtherTabsCommand`, `CloseTabsToRightCommand`, dan `MoveTabToNewWindowCommand`,
    masing-masing dengan `...Allowed`. Ketiganya di-refresh setiap kali daftar tab berubah, karena
    `UiCommand` tidak ikut `CommandManager.RequerySuggested`.
  - Move to New Window memicu `TearOffRequested`. Window menjawabnya dengan `TearOff`; window baru
    muncul bergeser +40/+40 dari window asal.
  - Separator menu harus memakai kunci `MenuItem.SeparatorStyleKey`, karena style implisit
    `Separator` tidak dipakai di dalam menu.
  - Shortcut keyboard (`Ctrl+W`, `Ctrl+Tab`, `Ctrl+1..9`) dari nomor 4 belum dibuat.
- **Tombol daftar tab dipindah dan diberi jumlah** (permintaan lanjutan 2026-09-25):
  - Tombol ⌄ tidak lagi menempel di ujung tab terakhir. Sekarang ia punya kolom sendiri di title bar
    (kolom 3), tepat di kiri kelompok koneksi/Tools/tema, dan selalu tampil dengan jumlah tab
    (`{Binding Tabs.Count}`).
  - Kolom sendiri dipilih supaya tombol itu tetap ada di window hasil drag-out, yang menyembunyikan
    kelompok koneksi/Tools/tema.
  - Area drop tab dari window lain kini dibatasi lebar kolom strip
    (`LayoutInformation.GetLayoutSlot`), jadi tidak lagi mencakup tombol ⌄ dan toolbar.
- **Koreksi maximized diperbaiki** (2026-09-25). Catatan eksekusi awal keliru menyatakan window
  maximized tidak meluber.
  - `SystemParameters.WindowResizeBorderThickness` hanya 4 (bingkai resize), padahal window meluber
    8 (bingkai + padded border). Akibatnya bagian atas title bar terpotong 4px dan isinya tampak
    mepet ke tepi atas.
  - Sekarang trigger maximized memakai `TabbedMainWindow.MaximizedBorderThickness`, yaitu bingkai
    resize + `SM_CXPADDEDBORDER` yang dikonversi ke DIP lewat `GetDpiForSystem`.
  - Terukur di DPI 100%: konten pas dengan work area (0,0 sampai 1920×1032). DPI 150% dan monitor
    dengan DPI berbeda belum diuji.
