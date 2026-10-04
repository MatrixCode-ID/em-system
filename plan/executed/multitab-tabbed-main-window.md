# Plan — `TabbedMainWindow` menjadi satu-satunya window utama (MultiTab dan SPA)

Status: **sudah dieksekusi** (2026-09-26). Lihat [Catatan eksekusi](#catatan-eksekusi) untuk
penyimpangan dan checklist uji manual.
Dibuat: 2026-09-26, dari diskusi dengan user. Semua keputusan sudah dijawab user sebelum plan ini
ditulis (lihat [Keputusan final](#keputusan-final)).

**Aturan eksekusi.** Plan ini dijalankan tanpa bertanya ke user. Kalau ada hal yang tidak tercakup
di sini, pilih yang paling konsisten dengan keputusan final dan pola kode yang sudah ada, lalu catat
di [Catatan eksekusi](#catatan-eksekusi). Jangan berhenti untuk bertanya.

Baca dulu sebelum mulai:

- `src/frontend/CLAUDE.md`, section **Navigation host**, **Detach windows**, **MVVM binding rules
  (MANDATORY)**, **Material design language (MANDATORY)**, **XAML and XML comments**;
- `CLAUDE.md` root, section **Keep the UI cores in step** dan **XML doc comment updates**;
- `plan/executed/tabbed-main-window.md`: bentuk `TabbedMainWindow` saat ini, termasuk catatan
  eksekusinya.

---

## Latar belakang

- `TabbedMainWindow` (commit `3a4d57c`) sudah jadi sebagai window bertab tanpa DevExpress, tapi
  masih berdiri sendiri. Menu, tab, koneksi, dan pengguna di dalamnya masih dummy, dan window ini
  baru dibuka dari tombol di `MainWindow`.
- Layout MultiTab yang sekarang (`MainWindow` dengan `DXTabControl`, `loginHost`, `TabWorkspace*`,
  `TabHostWindow`, `AddMainControl`, API `TabCreate`/…) tidak terhubung ke sistem navigasi.
  `MainStack`, `FindEntry`, dan `NavigateTo` melempar `NotSupportedException` di sana.
- Plan ini menjadikan `TabbedMainWindow` window utama layout MultiTab dan menghubungkannya ke
  sistem navigasi:
  - menu Apps diisi navigasi `IsMenuVisible = true`;
  - klik menu berarti `NavigateTo`;
  - menu Tools diisi manual, sama dengan static tools di home SPA;
  - alur login berjalan penuh.
- **`MainWindow` dihapus seluruhnya.** `TabbedMainWindow` juga menjadi window utama layout SPA,
  dengan kartu konten berisi `SpaNavigationHost(MainStack)` dan title bar yang dirampingkan. Hasilnya
  satu kelas window utama tanpa interface perantara, dan window utama di kedua layout tidak lagi
  memakai `ThemedWindow` DevExpress.
- Premis `plan/unexecuted/navigasi-1-hapus-multitab.md` (menghapus MultiTab) gugur: MultiTab tetap
  ada, hanya pindah implementasi. Pembersihan kode DX MultiTab yang direncanakan di sana dikerjakan
  di plan ini.

## Keputusan final

### Model navigasi MultiTab

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Stack | **Satu `NavigationStack` per window**, tanpa home. Window utama memakai `MainStack`, dan setiap window hasil tear-off punya stack sendiri. |
| 1a | Tab | **Satu tab = satu `NavigationEntry`** di stack window-nya. Urutan tab = urutan `Entries`, dan tab aktif = `Current`. Judul tab = `entry.Title`, ikut `SetTitle`. |
| 1b | Membuka | Setiap `NavigateTo` yang judulnya belum terbuka **melahirkan tab baru**, termasuk editor yang dibuka dari body manager. **Posisinya paling kanan.** Tidak ada entri yang dibuang (`ClearForwardStacks` tidak berlaku di stack bertab). |
| 1c | Window tujuan | Menu Apps, Tools, dan `EmApp.NavigateTo` dari luar → window utama. Editor yang diminta body → window milik body itu. Manager yang diminta dari window tear-off → window utama, lalu window utama dimunculkan (sama dengan aturan detach sekarang). Judul yang sudah terbuka di mana pun → tab itu dipilih dan window-nya dimunculkan. |
| 1d | Pindah tab | **Body yang ditinggalkan tetap ditanya** (`OnNavigatingAway`, boleh menolak), sama dengan `MoveTo` SPA. Kalau menolak, pilihan tab kembali ke tab semula. `OnNavigatingIn` dipanggil di tab tujuan. |
| 1e | Tampilan isi | **Tidak ada `SpaNavigationHost` di MultiTab.** Kartu konten langsung menampilkan `Current.Body`. Tidak ada Back/Forward/Home/Detach. Flag toolbar per navigasi (`IsToolbarVisible`, `IsBackVisible`, …) tidak berpengaruh di MultiTab. |
| 1f | Reload | **Tombol ikon kecil di header tab, tepat di kiri tombol close** (seperti ikon suara di tab Chrome). Aturan visibilitasnya sama dengan tombol close: tampil saat tab aktif atau di-hover, `Hidden` (bukan `Collapsed`) di luar itu supaya lebar tab tidak melompat. Juga ada item **"Reload"** di menu klik kanan tab. Keduanya memanggil `entry.Reload()`. |
| 1g | Tutup tab aktif | Yang dipilih berikutnya adalah **tab yang terakhir aktif** sebelum tab itu (riwayat aktivasi per stack). Kalau riwayatnya habis: tetangga kanan, lalu kiri. |

### Lain-lain

| # | Topik | Keputusan |
| --- | --- | --- |
| 2 | Login | **Memenuhi satu window.** Selama belum ada yang masuk, kartu konten menampilkan body `admin.logon` yang menjadi satu-satunya entri `MainStack`. Yang disembunyikan: tab strip, tombol daftar tab, Apps, Tools, combobox koneksi, tombol akun, dan tombol tema title bar (login punya tombol tema sendiri). Yang tetap tampil: logo + judul dan tombol caption. Sesudah login, `MainStack` dikosongkan dan window beralih ke mode tab. Sign out atau sesi habis: window tear-off ditutup, semua tab dilepas, lalu window kembali ke login. |
| 3 | Combobox koneksi | **Hanya tampil di mode debug** (dan hanya di mode tab), meniru kartu koneksi di home SPA. Terikat ke `EmApp.UIConnections`. Memilihnya menulis `EmApp.ActiveConnection`, dengan `DefaultDebugConnection` terpilih di awal. |
| 4 | Kode MultiTab lama | **Dihapus**, termasuk **`MainWindow` seluruhnya** (XAML, code-behind, `MainWindowVm`). |
| 5 | `EmApp.MainWindow` | Nama tetap, **tipenya menjadi `TabbedMainWindow`** (breaking change yang disengaja). Tanpa interface perantara. |
| 5a | Window utama SPA | `TabbedMainWindow` juga dipakai di layout SPA. Kartu konten selalu berisi `SpaNavigationHost(MainStack)` (dengan home). Title bar hanya berisi logo + judul dan tombol caption. Home, detach, dan toolbar SPA tetap seperti sekarang. Nama kelas tetap `TabbedMainWindow`. |
| 5b | Tombol tema di title bar | **Hanya tampil di mode tab MultiTab.** |
| 6 | Menu Tools | **Sama dengan static tools SPA**: Connection Config, User Manager, Role Manager, dan Simulate Login (tetap no-op), disaring `CanOpen`. Daftarnya dipindah ke satu sumber bersama yang dipakai home SPA dan `TabbedMainWindow`. |
| 7 | Detach di MultiTab | **Tidak ada** (tidak ada host SPA yang memunculkan tombolnya). `CanDetach` tetap `false` di MultiTab, dan tear-off tab menggantikan perannya. |
| 8 | Home di MultiTab | **Tidak ada home.** Apps dan Tools menggantikannya. Sesudah login, window kosong dengan teks "No tab open". `UseHomeNavigation` tidak berpengaruh di MultiTab, dan XML doc-nya menyebut hal itu. |
| 9 | Commit | **Commit otomatis** setelah build bersih: satu commit, pesan berbahasa Indonesia, di branch aktif (`spa-detach`), **tanpa push**. |
| 10 | Plan navigasi-1 | Dipindah ke `plan/executed/` dengan status "digantikan oleh plan ini". TODO-LIST nomor 15 dipindah ke DONE. |

Default layout tidak berubah: `Program.cs` tetap memakai MultiTab (`UseSinglePageLayout()` tetap
dikomentari).

---

## Langkah 1 — Hapus kode MultiTab lama

Grep pemakaian setiap member sebelum dihapus.

- `Shared/EmAppBuilder.cs`: hapus `AddMainControl` dan `MainControls`.
- `Shared/MainControlDefinition.cs`: hapus berkasnya.
- `Shared/IEmAppUi.cs`: hapus `MainControlsDefinitions`. Interface-nya tetap ada dan jadi kosong,
  karena masih didaftarkan di DI. Perbarui summary-nya.
- `Core/EmApp.cs`: hapus `_mainControls`, `MainControlsDefinitions`, dan seluruh region
  `Tab Operations` (`TabCreate`, `TabSelect`, `TabExists`, `TabRename`, `TabRemove`).
- `Core/EmApp.Statics.cs`: hapus blok `pars.MainControls.EachOf(...)` di `InitBuilder`, lalu
  perbarui XML doc `BuildApp`, `InitBuilder`, dan summary class yang menyebut main control.
- `Shared/MvvmModelBase.cs`: hapus `IsMainControlContext` kalau sudah tidak ada yang mengisi atau
  membacanya. Perbarui XML doc yang menyebut main control atau multi-tab.
- Hapus `Shared/TabWorkspaceHost.cs`, `Shared/TabWorkspaceVm.cs`, dan
  `Windows/TabHostWindow.xaml(.cs)`.
- `src/frontend/modules/8XX-NSM/Em.Sample/Extensions.cs`: hapus panggilan `AddMainControl`, dan
  `using System.Windows.Controls` kalau jadi tidak terpakai.
- **`Styles/Tabs.xaml` tidak dihapus.** Berkas ini di-merge `MaterialDesign.xaml` dan dipakai
  layar lain.
- `ApplicationLayout` **tetap ada** (MultiTab masih hidup).

## Langkah 2 — Hapus `MainWindow`

- Hapus `Windows/MainWindow.xaml` dan `Windows/MainWindow.xaml.cs` beserta `MainWindowVm`. Isinya
  yang masih berguna dipindah dulu:
  - alur login, sesi, dan pemulihan sesi → `EmApp` (langkah 3);
  - konfirmasi tutup window beserta detach → `TabbedMainWindow` (langkah 6);
  - `OnThemeChanged` → `TabbedMainWindow` (langkah 3).
- Sisanya dibuang: `loginHost`, `DXTabControl` dan toolbar-nya, `SyncLoginHost`, `_loginControl`,
  `UsesNavigationLoginFlow`, `ActiveLoginControl`, `InitTabs`, `BarItem_OnItemClick`, region
  `Tab Operations`, dan member VM yang hanya melayani toolbar MultiTab lama.
- Semua rujukan ke kelas `MainWindow` di `src/` diganti `TabbedMainWindow`, termasuk XML doc di
  `LoginControl.xaml.cs`, `MvvmModelBase.cs`, `DetachedWindow.xaml.cs`, dan `EmApp*.cs`.
  `MvvmModelBase.MainWindow` (property owner dialog bertipe `Window`) **bukan** rujukan ke kelas itu
  dan tidak diubah.
- `LoginControlVm.RefreshThemeState`: tetap dipanggil saat tema berganti, lewat
  `TabbedMainWindow.OnThemeChanged`.

## Langkah 3 — `EmApp.MainWindow` bertipe `TabbedMainWindow`, alur sesi di `EmApp`

### `EmApp`

- `public TabbedMainWindow MainWindow { get; private set; } = null!;`, dengan XML doc diperbarui.
- `Run()`: selalu `MainWindow = new TabbedMainWindow(this); MainWindow.InitLayout();`.
  `ShutdownMode.OnMainWindowClose` tetap, tapi komentarnya diperbarui (tidak lagi menyebut
  `TabHostWindow`, melainkan window hasil tear-off yang juga bukan owned window).
  `ShowFirstScreenAsync` berjalan **di kedua layout**:
  - mode debug → `MainWindow.ShowSignedInAsync()`;
  - selain itu → `ShowLoginScreenAsync(null)`.
- `CurrentTheme` setter tetap memanggil `MainWindow?.OnThemeChanged()`.
- Berkas partial baru `Core/EmApp.MainWindowFlow.cs`, isinya dipindah dari `MainWindow.xaml.cs`
  lama. Semuanya `internal`/`private`:
  - `ShowLoginScreenAsync(string? notice)`: `NavigateToRoot(logon)`, lalu pasang
    `SignInSucceeded` ke `LoginControl` yang sedang tampil dan serahkan `notice` sekali
    (logika `ShowLoginScreen` sekarang), lalu `MainWindow.ShowLoginMode()`;
  - handler `SignInSucceeded` → `MainWindow.ShowSignedInAsync()` (async void dengan try/catch dan
    `MainWindow.ShowMboxError`, seperti sekarang);
  - handler `SessionEnded` (didaftarkan di `Run`, lewat `Dispatcher.InvokeAsync`):
    `MainWindow.ReleaseWorkspaceAsync()`, lalu, kalau bukan mode debug,
    `ShowLoginScreenAsync(e.Reason)`;
  - `OnMainWindowLoadedAsync()`, dipanggil window utama sekali pada `Loaded` pertama:
    `RetrieveApiConnections()`, pilih `DefaultDebugConnection` kalau ada (pengganti
    `LoadConnections`), lalu pemulihan sesi (logika `RestoreSessionAsync` sekarang). Kalau berhasil
    → `MainWindow.ShowSignedInAsync()` (di luar mode debug), dengan `SetRestoringSession` dan
    `SyncSelectedConnection` pada `LoginControl` yang sedang tampil, seperti sekarang;
  - `SessionEndedNotice` pindah ke sini sebagai field privat;
  - `SignOutAsync()`, dipakai tombol akun `TabbedMainWindow` **dan**
    `SpaNavigationHostVm.SignOutCommand`, supaya satu jalur:
    - mode debug: `MainWindow.ReleaseWorkspaceAsync()` lalu `ShowLoginScreenAsync(null)` (di SPA
      sama dengan perilaku sekarang: detach ditutup, lalu ke login);
    - selain itu: `EndSessionAsync(notifyServer: true)`.

### Member layout-dependent di `TabbedMainWindow` (internal)

| Member | Layout SPA | Layout MultiTab |
| --- | --- | --- |
| `InitLayout()` | pasang `SpaNavigationHost(MainStack)` di kartu konten, mode SPA | ikat VM ke `MainStack`, mode login |
| `ShowLoginMode()` | tidak ada apa-apa (host `MainStack` sudah menampilkan login) | `IsSignedIn = false` |
| `ShowSignedInAsync()` | `MainStack.NavigateHome()`, dan di mode debug juga `Home.Reload()` (sama dengan sekarang) | `MainStack.ReleaseAll()`, `IsSignedIn = true`, bangun ulang menu |
| `ReleaseWorkspaceAsync()` | `CloseDetachedWindowsAsync()` | tutup window tear-off tanpa bertanya dan lepas stack-nya, lalu `MainStack.ReleaseAll()` |
| `OnThemeChanged()` | render ulang branding `LoginControl` yang sedang tampil dan `RefreshThemeState`-nya | sama |

Percabangan cukup memakai `EmApp.ApplicationLayout`. Jangan membuat subclass atau interface.

## Langkah 4 — Stack bertab (`Core/NavigationStack.cs`)

Stack yang dipakai window bertab berperilaku berbeda dari jalur linear SPA. Tambahkan mode
**internal** lewat parameter konstruktor `internal NavigationStack(EmApp app, Navigation? home,
bool tabbed = false)` dan property `internal bool IsTabbed`. Semua perbedaan di bawah hanya berlaku
kalau `IsTabbed`, dan perilaku SPA tidak boleh berubah sedikit pun.

- **`Open`**: `ClearForwardStacks()` **tidak** dipanggil. Entri baru ditambahkan di ujung
  `_entries` (keputusan 1b). Urutan callback lain (`OnNavigatingAway` sumber, `OnNavigatingIn`,
  pemeriksaan judul kembar, `Reload`) tetap sama.
- **`MoveTo`**: tetap menanyai sumber (keputusan 1d).
- **`CanGoBack` / `CanGoForward`** → `false`; `Backward` / `Forward` → `false` tanpa berbuat apa-apa.
  `ClearForwardStacks` → tidak berbuat apa-apa.
- **Riwayat aktivasi**: `List<NavigationEntry> _activationHistory` diperbarui di `SetCurrent`
  (entri dipindah ke ujung, `null` diabaikan), dan dibersihkan dari entri yang keluar (`Close`,
  `Extract`, `ReleaseAll`, `NavigateToRoot`).
- **`Close`**: fallback untuk entri aktif = entri terakhir di riwayat aktivasi yang masih ada di
  stack; kalau tidak ada, tetangga kanan, lalu kiri (keputusan 1g). Sisanya tetap: `MoveTo(fallback)`
  (boleh ditolak body yang ditutup), atau `AskToLeave` kalau tidak ada fallback, lalu `Release`.
- **`Extract(entry)`** untuk memindah tab ke window lain: di mode bertab boleh untuk **entri mana
  pun**, bukan hanya `Current`. Tanpa `OnNavigatingAway`/`OnRelease`. Kalau entri itu `Current`,
  pindah ke fallback yang sama dengan `Close` lewat `MoveTo(fallback, askSource: false)`. Kalau
  tidak ada fallback, stack boleh jadi kosong (window tear-off lalu menutup sendiri; tab tunggal
  window utama memang tidak boleh keluar, dan itu dijaga `CanMoveOut` yang sudah ada).
- **`Adopt(entry, int index)`** (overload baru untuk mode bertab): sisipkan di `index`, set
  `entry.Stack = this`, `RaiseChanged()`. Tidak menjadikannya `Current`; pemanggil lalu memanggil
  `MoveTo(entry)`, jadi body aktif di window tujuan tetap ditanya. Kalau menolak, tab masuk tapi
  tidak aktif. `Adopt(entry)` yang lama tetap untuk detach.
- **`Move(entry, int index)`** baru untuk menggeser urutan tab (drag di strip). Tanpa callback
  body, lalu `RaiseChanged()`.
- `NavigateHome` tidak relevan (stack bertab tidak punya home). `NavigateToRoot` tetap dipakai untuk
  login di `MainStack`.
- Semua anggota baru `internal`. XML doc `NavigationStack` (summary) menyebut singkat bahwa ada mode
  bertab untuk layout multi-tab, tanpa mengubah kontrak `INavigationStack`.

## Langkah 5 — Navigasi dan registri window bertab

### `Core/EmApp.Statics.cs`, `Core/EmApp.NavigationHost.cs`

- `InitBuilder`: `_mainStack` **selalu dibuat**. SPA dengan home (seperti sekarang). MultiTab tanpa
  home dan bertab (`new NavigationStack(app, home: null, tabbed: true)`); navigasi home tidak
  dibuat atau didaftarkan di MultiTab.
- `MainStack` tidak lagi melempar di MultiTab. Perbarui XML doc: di MultiTab, stack ini berisi tab
  window utama, atau layar login sebelum ada yang masuk. Hapus `<exception>`
  `NotSupportedException` dari `MainStack`, `FindEntry`, `NavigateTo`, dan `NavigateToRoot`.
- `EmAppBuilder.UseHomeNavigation`: XML doc menyebut bahwa di layout multi-tab tidak ada home.
  `UseSinglePageLayout` diberi XML doc kalau belum ada.
- **Router**: aturan yang sekarang sudah memenuhi keputusan 1c, jadi **tidak perlu cabang
  MultiTab**:
  - judul sudah terbuka → `BringToFront` + `MoveTo`;
  - Manager dari stack selain `MainStack` → `MainStack` + `BringToFront`;
  - selain itu → `origin.Open`, yang di stack bertab berarti tab baru paling kanan.

  Pastikan hanya `AllStacks` dan `WindowOf` yang mengenali stack window tear-off.

### Berkas partial baru `Core/EmApp.TabbedWindows.cs` (semua `internal`)

- `_tearOffWindows`: window `TabbedMainWindow` hasil tear-off (window utama tidak termasuk).
  Didaftarkan saat dibuat, dan keluar saat `OnClosed`.
- `AllStacks` (`Core/EmApp.DetachedWindows.cs`): `MainStack`, lalu stack setiap window tear-off,
  lalu stack detach.
- `WindowOf(stack)`: `MainStack` → `MainWindow`, stack tear-off → window-nya, stack detach →
  window-nya. Tipe kembaliannya tetap `Window?`.
- `ConfirmCloseTearOffWindowsAsync()`: untuk setiap window tear-off, setiap tab: munculkan
  window-nya, `MoveTo` ke entri itu (sekaligus menanyai body yang sedang aktif), lalu `AskToLeave`
  entri itu. Satu penolakan menghentikan semuanya. Cara yang sama dipakai untuk tab window utama
  (langkah 6).
- `CloseTearOffWindowsAsync()`: tutup semua window tear-off tanpa bertanya dan lepas stack-nya
  (pola `DetachedWindow.CloseWithoutAskingAsync`).
- `CanDetach`: tetap `false` di luar SPA.

## Langkah 6 — `TabbedMainWindow` tersambung

`Windows/TabbedMainWindow.xaml(.cs)`:

- **Hapus seluruh region `Dummy`** berikut pemanggilannya, `TabbedMainWindowMenuItem.CreateContent`,
  dan `OpenTab(string, Func<object>)`. Konstruktor tanpa parameter di window diganti
  `TabbedMainWindow(EmApp app)` untuk window utama dan konstruktor internal
  `TabbedMainWindow(EmApp app, NavigationStack stack)` untuk tear-off.
- **VM terikat ke stack.** `TabbedMainWindowVm.Stack` (`NavigationStack`).
  - `Tabs` disinkronkan dari `Stack.Entries` setiap kali `Stack.Changed`: satu
    `TabbedMainWindowTab` per entri, urutan sama, dan objek tab dipakai ulang untuk entri yang sama
    supaya template tidak dibangun ulang.
  - `TabbedMainWindowTab` mendapat `NavigationEntry Entry`. `Title` mengikuti `Entry.Title` lewat
    `PropertyChanged` (pola `DetachedWindowVm.Track`), dan langganannya dilepas saat tab keluar.
    `Content` = `Entry.Body`.
  - `SelectedTab` mengikuti `Stack.Current` (`PropertyChanged(Current)`). **Setter dari UI**
    (klik tab, `SelectTabCommand`, dropdown daftar tab) memanggil `Stack.MoveTo(tab.Entry)` secara
    async. Kalau `false`, `SelectedTab` dikembalikan ke tab milik `Stack.Current` lewat
    `Dispatcher.InvokeAsync` (supaya `ListBox` ikut balik). Ambil pola yang sudah terbukti di
    kode; kalau tidak ada, pakai pendekatan ini dan catat.
  - `IsActive` pada tab tetap dipakai dropdown daftar tab.
  - **Kartu konten** menampilkan `SelectedTab.Content` (body entri, bukan host).
- **Mode SPA.** `IsSpaLayout` (`Get`/`Set`, diisi di `InitLayout`). Kalau `true`, kartu konten
  berisi `SpaNavigationHost(MainStack)`, title bar hanya logo + judul + tombol caption (keputusan
  5a), dan VM tidak terikat ke stack. Judul window = `ApplicationName`. Kalau toolbar
  `SpaNavigationHost` terlihat dobel bingkai dengan kartu, pilih yang paling rapi dan catat.
- **Mode login / mode tab (MultiTab).** `IsSignedIn` (`Get`/`Set`) ditulis oleh
  `ShowLoginMode`/`ShowSignedInAsync`, bukan dihitung dari `ActiveUser`, karena mode debug punya
  pengguna tanpa login.
  - `false`: kartu konten menampilkan body `MainStack.Current` (layar login), dan elemen keputusan
    2 disembunyikan.
  - `true`: tampilan tab.
  - Visibilitas di XAML diikat ke property terhitung di VM yang menggabungkan `IsSpaLayout`,
    `IsSignedIn`, dan `ClosesWhenEmpty`. Jangan `MultiBinding` baru. Pakai converter yang sudah ada
    (`InverseBoolToVisibilityConverter`, `BooleanToVisibilityConverter`) atau property
    `Visibility`.
- **Menu Apps.** Dibangun ulang dari `EmApp.Navigations` dengan filter
  `IsMenuVisible && CanOpen(nav)`. Urutan dan pengelompokan `MenuPath` mengikuti
  `DefaultHomeControlVm.RebuildMenuTree`: setiap segmen menjadi `TabbedMainWindowMenuItem` cabang,
  cocok tanpa peduli huruf besar/kecil, dan navigasi tanpa path berada di level teratas.
  - Pemecahan path dipindah ke satu helper bersama yang dipakai home dan window ini, kalau bisa
    tanpa mengubah perilaku home. Kalau tidak, salin dan catat.
  - Dibangun ulang saat `ShowSignedInAsync`, `ActiveUserChanged`, dan sesudah
    `EnsureClaimsLoadedAsync()` selesai.
  - `TabbedMainWindowMenuItem` mendapat `Navigation? Navigation` dan `Func<Task>? Invoke`. Item cabang
    keduanya `null`.
  - `OpenMenuItemCommand` (async): tutup popup, lalu `item.Navigation != null` →
    `EmApp.NavigateTo(item.Navigation)` (selalu relatif ke `MainStack`), atau jalankan
    `item.Invoke`. Exception ditangkap dan ditampilkan lewat `AlertError`.
- **Menu Tools.** Dari sumber bersama static tools (langkah 7), dibangun ulang pada saat yang sama
  dengan menu Apps. Isi dummy lama (Color Mode, Users Account, Application Log) dibuang.
- **Combobox koneksi.** `Connections` menjadi `ObservableCollection<ApiConnection>?`, meneruskan
  `EmApp.UIConnections` (null-safe). `SelectedConnection` bertipe `ApiConnection?`, dan setter-nya
  menulis `EmApp.ActiveConnection`.
  - Tampil hanya kalau `IsDebugMode && !IsSpaLayout && IsSignedIn && !ClosesWhenEmpty`.
  - Pilihan disinkronkan saat koleksi berubah (pola `OnApiConnectionsChanged` /
    `SyncSelectedConnection` di home).
  - Item ditampilkan dengan `ProfileName`.
- **Tombol akun.** `UserDisplayName`, `UserAccount`, dan `UserInitials` diisi dari
  `EmApp.ActiveUser` dengan aturan yang sama persis seperti `SpaNavigationHostVm`. Diperbarui pada
  `ActiveUserChanged`, dan langganannya dilepas saat window ditutup.
  - Warna avatar memakai palet yang sama dengan SPA. Pakai `Converters/AvatarPaletteConverter.cs` /
    `InitialsConverter.cs` kalau hasilnya identik dengan `SpaNavigationHostVm`. Kalau tidak identik,
    angkat logika SPA ke satu helper bersama. Jangan membuat palet ketiga.
  - `ChangePasswordCommandAllowed()` tetap `false`.
- **Sign out.** `SignOutCommand` (async) memanggil `EmApp.SignOutAsync()` dalam try/catch +
  `AlertError`. `SpaNavigationHostVm.SignOutCommand` dialihkan ke method yang sama. Event
  `SignOutRequested` dihapus kalau tidak ada pemakai lagi.
- **Tombol tema title bar**: tampil hanya kalau `!IsSpaLayout && IsSignedIn && !ClosesWhenEmpty`.
- **Menu klik kanan tab**: urutannya Reload, separator, Close, Close Others, Close to the Right,
  separator, Move to New Window.
  - `ReloadTabCommand(tab)` → `tab.Entry.Reload()`, dengan try/catch + `AlertError`.
- **Tombol reload di header tab** (keputusan 1f), di template tab, tepat di kiri tombol close:
  - gaya dan ukurannya sama dengan tombol close (`flatIconButtonStyle` diperkecil), ikon
    `fa:FontAwesome Icon="Solid_RotateRight"`, ToolTip "Reload";
  - `Command="{Binding ... Commands[ReloadTabCommand]}"`, `CommandParameter="{Binding}"`, lewat
    jalur yang sama dengan tombol close untuk mencapai VM window;
  - trigger visibilitasnya menumpang trigger tombol close yang sudah ada (aktif atau hover →
    `Visible`, selain itu `Hidden`);
  - `TabStripPanel.MinItemWidth` (110) dicek ulang supaya judul masih terbaca dengan dua tombol.
    Kalau perlu dinaikkan, catat nilainya.
  - `CloseTabCommand` (termasuk tombol close dan klik tengah) → `tab.Entry.Close()`.
  - `CloseOtherTabsCommand` / `CloseTabsToRightCommand` → `Close()` satu per satu. Penolakan
    pertama menghentikan sisanya. Semuanya async dengan try/catch + `AlertError`.
- **Tear-off dan drag antarwindow** (`TearOff`, `TabbedMainWindowTabDrag`):
  - pindah = `source.Extract(entry)`, lalu `UpdateLayout()` pada window asal (alasannya sama dengan
    `DetachAsync`: satu control tidak boleh punya dua parent visual), lalu
    `target.Adopt(entry, index)` + `target.MoveTo(entry)`;
  - tear-off membuat `new NavigationStack(app, null, tabbed: true)` dan window baru dengan stack itu,
    didaftarkan di `_tearOffWindows`. `CopyShellFrom` menyalin `ToolsMenu`, `IsSignedIn`, dan
    `EmApp`, dan tidak lagi mengosongkan tab sendiri (tab ikut stack);
  - geser urutan di strip → `Stack.Move`;
  - `MoveTab`, `InsertTab`, `RemoveTab` di VM diubah menjadi pembungkus operasi stack itu, atau
    dihapus kalau jadi tidak terpakai;
  - `CanTearOff` / `CanMoveOut` / `CanAcceptTab` dan penutupan otomatis window tear-off yang kosong
    tetap, dengan dasar `Stack.Entries`.
- **Menutup window.**
  - SPA: logika `OnClosing` / `ConfirmCloseAsync` / `_closeAgreed` dari `MainWindow` lama dipindah
    apa adanya.
  - MultiTab, window utama: kalau ada tab di window mana pun, batalkan dulu. Lewat dispatcher, tanya
    semua tab: window tear-off lebih dulu (`ConfirmCloseTearOffWindowsAsync`), lalu tab window
    utama dengan cara yang sama. Satu penolakan membatalkan semuanya. Kalau disetujui:
    `CloseTearOffWindowsAsync()`, `MainStack.ReleaseAll()`, lalu tutup. Di mode login tidak ada yang
    ditanya.
  - MultiTab, window tear-off: tanya tab-tabnya dengan cara yang sama. Kalau setuju, tutup lalu lepas
    stack-nya.
- **`OnLoaded` pertama** → `EmApp.OnMainWindowLoadedAsync()` (hanya window utama).
- Tidak ada shortcut Back/Forward di MultiTab.
- Code-behind tetap minimal, sesuai aturan MVVM dan catatan eksekusi plan `tabbed-main-window`.
  Semua XML doc baru ditulis dalam Bahasa Indonesia, tanpa menyebut nama objek database.

## Langkah 7 — Sumber bersama static tools

- Buat `Shared/StaticTool.cs` (internal): `Name`, `Title`, `Subtitle`, `Description`,
  `ImageSource Icon`, `Navigation? Navigation`, `Func<Window, Task>? Invoke` (dengan owner dialog).
- `EmApp` (internal) `IReadOnlyList<StaticTool> GetStaticTools()`: Connection Config (dialog
  `ConnectionConfig`, owner dari parameter), `admin.users`, `admin.roles`, dan Simulate Login
  (no-op). Urutannya sama dengan `DefaultHomeControl.RenderStaticItems` sekarang, dan tool
  navigasi disaring `CanOpen`.
- `DefaultHomeControl.RenderStaticItems` memakai sumber ini: memetakan ke `MenuNavigation` dengan
  owner `Window.GetWindow(this) ?? _app.MainWindow`. Tampilan dan perilaku home tidak boleh
  berubah.
- `TabbedMainWindow` memetakan ke `TabbedMainWindowMenuItem`, dengan owner window itu sendiri.

## Langkah 8 — Mirror ke MAUI

Tidak ada yang di-mirror. Alasannya:

- MultiTab, stack bertab, `TabbedMainWindow`, dan tear-off khusus WPF (CLAUDE.md root menyebut
  `MultiTab` sebagai pengecualian);
- semua anggota baru (`IsTabbed`, riwayat aktivasi, `Adopt(entry, index)`, `Move`,
  `EmApp.MainWindowFlow.cs`, `EmApp.TabbedWindows.cs`, `StaticTool`) internal;
- perilaku stack SPA tidak berubah;
- `EmApp.MainWindow` adalah window WPF tanpa padanan di MAUI.

**Jangan** menambah member publik baru di `EmApp` atau `NavigationStack` WPF. Kalau terpaksa,
cek padanannya di `src/shared/Em.Ui.Maui.Core` dan mirror, lalu catat. Kontrak di `Em.Ui.Core`
(`INavigation*`) tidak disentuh.

## Langkah 9 — Dokumentasi, TODO, plan lama, memory

- `src/frontend/CLAUDE.md`:
  - **Composition root**: registrasi `MainWindow` dan contoh `Program.cs` disesuaikan.
  - **Module shape**: hapus kalimat tentang `AddMainControl` dan layout MultiTab.
  - **Navigation host**: tulis ulang paragraf dua layout. Hapus kalimat `NotSupportedException`.
    Sebut bahwa `TabbedMainWindow` adalah satu-satunya window utama di kedua layout.
  - Tambah subsection **MultiTab layout (`TabbedMainWindow`)**, sejajar **Detach windows**, yang
    merangkum keputusan 1–1g, 2, 3, 5b, 6–8 dan aturan tutup tab/window/sesi. Tegaskan untuk
    penulis body:
    - pindah tab tetap memanggil `OnNavigatingAway`;
    - `entry.NavigateTo` ke editor membuka tab baru;
    - flag toolbar navigasi tidak berlaku di MultiTab.
  - **Stack rules**: tambahkan catatan bahwa aturan jalur linear (branching, `ClearForwardStacks`,
    Back/Forward) hanya untuk stack SPA; stack bertab mengikuti subsection MultiTab.
  - **Detach windows**: window utama = `TabbedMainWindow`, dan di MultiTab perannya diganti
    tear-off.
  - **MVVM base**: hapus `IsMainControlContext`.
  - Referensi berkas `MainWindow.xaml` → `TabbedMainWindow.xaml` di seluruh berkas.
- `CLAUDE.md` root, **Keep the UI cores in step** dan tabel `src/shared`: `MainWindow`/`TabHostWindow`
  diganti `TabbedMainWindow`. Pengecualian MultiTab tetap ada.
- `doc/TODO-LIST.md`:
  - nomor 15 dihapus dan nomor sesudahnya digeser (aturan di kepala berkas);
  - nomor 4 diubah: uji regresi MultiTab kini berarti menguji `TabbedMainWindow` (checklist di
    catatan eksekusi plan ini);
  - daftar "Yang menunggu keputusan" disesuaikan dengan nomor baru.
- `doc/TODO-LIST.DONE.md`: bullet baru di paling atas tentang pekerjaan ini (apa yang dikerjakan +
  hasil verifikasi), termasuk bahwa item "Hapus layout MultiTab" digantikan.
- `plan/unexecuted/navigasi-1-hapus-multitab.md` → `plan/executed/`. Status di kepalanya diganti:
  **digantikan** oleh `plan/executed/multitab-tabbed-main-window.md` (2026-09-26), MultiTab tidak
  dihapus tapi pindah ke `TabbedMainWindow`, dan pembersihan kode DX MultiTab dikerjakan di sana.
- Memory (`<folder memory lokal>`): hapus
  `project_addmaincontrol_to_be_removed.md` berikut barisnya di `MEMORY.md`. Buat memory project
  baru "MultiTab: satu stack per window, tab = entri" berisi ringkasan keputusan 1–1g, 5, dan 5a,
  lalu tambahkan pointer-nya di `MEMORY.md`.
- Pindahkan plan ini ke `plan/executed/` dan ubah status di kepalanya.

## Verifikasi

1. `dotnet build src/frontend/Em.Ui.Wpf.slnx`: 0 error, tanpa warning baru dibanding sebelum
   eksekusi (catat jumlah warning awal sebelum mulai). `src/shared/Em.Ui.Core` tidak disentuh,
   jadi solution MAUI dan backend tidak perlu dibangun. Kalau ternyata tersentuh, bangun juga
   solution yang mereferensikannya.
2. Grep di `src/` (di luar `bin/`/`obj/`) tidak menemukan: `AddMainControl`, `MainControlDefinition`,
   `MainControlsDefinitions`, `TabCreate`, `TabWorkspace`, `TabHostWindow`, `MainTabVm`, `loginHost`,
   `DXTabControl`, `IsMainControlContext`, `class MainWindow\b`, `MainWindowVm\b`, `new MainWindow(`,
   `AddDummyContent`, dan `(dummy)`.
3. Uji jalan singkat (tanpa interaksi):
   - jalankan `src/frontend/Em.Ui.Wpf` (Debug, MultiTab) di background ±15 detik, pastikan proses
     tidak crash saat startup, lalu matikan prosesnya;
   - ulangi dengan `UseSinglePageLayout()` diaktifkan sementara;
   - kembalikan `Program.cs` sebelum commit.
4. Checklist uji manual untuk user, ditulis di catatan eksekusi dan dirujuk dari TODO nomor 4
   (tidak dijalankan executor):
   - MultiTab non-debug: login memenuhi window → sesudah masuk, mode tab kosong ("No tab open"); menu
     Apps berisi navigasi yang boleh dibuka; menu Tools berisi static tools.
   - User Manager → edit user: **User Editor terbuka sebagai tab baru** paling kanan; judul tab
     ikut `SetTitle`.
   - Membuka judul yang sudah ada (di window mana pun) → tab itu yang dipilih, dan window-nya muncul.
   - Pindah tab dari User Editor yang punya perubahan belum disimpan → ditanya; kalau batal, tab
     tetap di User Editor.
   - Tutup tab aktif → kembali ke tab yang terakhir aktif. Tombol close, klik tengah, Close Others,
     dan Close to the Right berjalan, dan body yang menolak menahan tab-nya.
   - Tombol reload di header tab (kiri tombol close) dan item Reload di menu klik kanan memuat ulang
     isi tab. Tombolnya muncul/hilang bersama tombol close, dan lebar tab tidak melompat saat hover.
   - Tear-off dan drag antarwindow dengan isian belum disimpan: isinya tetap ada. Editor yang dibuka
     dari body di window tear-off menjadi tab di window itu. Manager dari window tear-off terbuka di
     window utama.
   - Sign out dan sesi habis: window tear-off tertutup, semua tab hilang, kembali ke login.
     "Remember sign in" memulihkan sesi saat aplikasi dibuka ulang.
   - Menutup window utama dengan body yang menolak → batal, tidak ada yang tertutup.
   - Mode debug: langsung ke mode tab; combobox koneksi tampil dan mengganti koneksi aktif.
   - Tema terang/gelap mewarnai semua window, termasuk panel brand login.
   - SPA (`UseSinglePageLayout()`): login, home, Back/Forward, detach, dan sign out tetap seperti
     sebelum plan ini; title bar hanya berisi logo, judul, dan tombol caption; drag, klik ganda
     maximize, menu sistem, maximized tidak meluber (DPI 100% dan 150%); menutup window dengan
     window detach yang menolak → batal.

## Commit

Sesudah verifikasi 1–3 lolos: `git add` semua perubahan plan ini (termasuk berkas yang dipindah
atau dihapus). Perubahan memory di luar repo tidak ikut. Lalu satu `git commit` di branch
`spa-detach`, **tanpa push**:

- summary line dan body dalam Bahasa Indonesia, sedangkan kode, identifier, dan path tetap apa
  adanya;
- body merangkum:
  - MultiTab memakai `TabbedMainWindow` yang tersambung ke navigasi;
  - satu stack per window, dan tab = entri; editor membuka tab baru;
  - kode DX MultiTab lama dan `MainWindow` dihapus;
  - `TabbedMainWindow` menjadi window utama kedua layout, dan `EmApp.MainWindow` bertipe
    `TabbedMainWindow`;
  - alur sesi pindah ke `EmApp`;
  - static tools dipakai bersama;
  - tidak ada mirror MAUI, beserta alasannya;
- diakhiri baris `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Kalau verifikasi gagal dan tidak bisa diperbaiki, **jangan commit**. Catat penyebabnya di catatan
eksekusi dan laporkan.

## Catatan eksekusi

### Hasil verifikasi

- **Build.** `dotnet build src/frontend/Em.Ui.Wpf.slnx --no-incremental`: sebelum eksekusi
  0 warning / 0 error, sesudah eksekusi 0 warning / 0 error. `src/shared/Em.Ui.Core` tidak
  disentuh, jadi solution backend dan MAUI tidak dibangun.
- **Grep** (di luar `bin/`/`obj/`): tidak ada lagi di `src/shared` maupun `src/frontend` (kode).
  Sisa kecocokan yang dibiarkan:
  - `MainWindowVm\b` cocok dengan `TabbedMainWindowVm` (positif palsu pola grep-nya);
  - `class MainWindow\b` / `MainWindowVm` di `src/tools/ModelGenerator` dan dokumen
    `src/tools/DbTransmutter`, yaitu tool terpisah di luar kedua solution dan di luar cakupan plan.
- **Uji jalan singkat** (tanpa interaksi, proses dimatikan sesudah 15 detik, dengan screenshot):
  - MultiTab (Debug): proses hidup, window langsung di mode tab dengan "No tab open", combobox
    koneksi berisi "Localhost", menu Apps dan Tools, tombol tema, dan avatar "SD" tampil. Tidak ada
    dialog error.
  - SPA (`UseSinglePageLayout()` diaktifkan sementara): proses hidup, title bar hanya logo, judul, dan
    tombol caption; kartu konten berisi host navigasi dengan home, kartu koneksi, dan daftar Tools
    yang urutannya sama seperti sebelumnya. Tidak ada dialog error.
  - `Program.cs` sudah dikembalikan (`UseSinglePageLayout()` dikomentari lagi) sebelum commit.

### Penyimpangan dan pilihan yang diambil sendiri

- **Kartu konten mengikuti `Stack.Current`, bukan `SelectedTab`.** VM punya `ActiveTab` (entri
  `Current`) dan `CardContent`. `SelectedTab` hanya sorotan di strip: saat user memilih tab, sorotan
  pindah dulu, tapi isi kartu baru berganti setelah `MoveTo` berhasil. Kalau body yang ditinggalkan
  menolak, sorotan dikembalikan lewat `Dispatcher.InvokeAsync(Sync)`. Tanpa ini, body baru sudah
  tampil selagi body lama masih bertanya. Pola kembalikan-sorotan belum ada di kode, jadi pendekatan
  dari plan ini yang dipakai.
- **Konfirmasi tutup menanyai setiap body sekali.** `ConfirmCloseTabsAsync` menanyai tab yang sedang
  tampil lebih dulu (`AskCurrentToLeave`), lalu untuk tab lain: `MoveTo(entry, askSource: false)`
  supaya tabnya terlihat, lalu `AskToLeave(entry)`. Cara di plan (`MoveTo` biasa lalu `AskToLeave`)
  akan menanyai body yang sedang tampil dua kali. `NavigationStack.AskToLeave` dijadikan `internal`.
- **`SessionEndedNotice` tidak disimpan sebagai field.** Keterangannya diteruskan langsung sebagai
  parameter `ShowLoginScreenAsync(notice)` ke layar login, jadi field-nya tidak punya pemakai lagi.
- **Koneksi debug di `OnMainWindowLoadedAsync`.** Selain `RetrieveApiConnections()`,
  `DefaultDebugConnection` langsung dijadikan `ActiveConnection` kalau belum. `LoadConnections` lama
  hanya memilihnya di combobox tanpa menulis `ActiveConnection`, sedangkan keputusan 3 meminta
  combobox menulisnya.
- **Sinkronisasi combobox koneksi hanya di mode debug.** Membangun ulang daftar koneksi membuat objek
  `ApiConnection` baru untuk profil yang sama, dan sesi terikat ke objek (kunci `_apiClients`). Kalau
  window utama ikut menulis ulang `ActiveConnection` di luar mode debug, sesi yang sedang hidup
  kehilangan client-nya. Combobox memang hanya tampil di mode debug.
- **Avatar.** `AvatarPaletteConverter`/`InitialsConverter` tidak identik dengan `SpaNavigationHostVm`
  (palet, alpha, dan aturan inisial berbeda), jadi logika SPA diangkat ke `Shared/UserAvatar.cs`
  (internal) dan dipakai `SpaNavigationHostVm` serta `TabbedMainWindowVm`. `TabbedMainWindowVm` mendapat
  `UserAvatarBrush`.
- **Helper `MenuPath` bersama.** `Shared/MenuPaths.cs` (internal): `Split` dan `SegmentComparison`,
  dipakai `DefaultHomeControlVm.RebuildMenuTree` dan menu Apps. Perilaku home tidak berubah. Di menu
  Apps, navigasi tanpa path diletakkan di atas grup, sama seperti home.
- **`StaticTool`** ada di `Shared/StaticTool.cs`, dan `GetStaticTools()` di `Core/EmApp.cs`. Home
  memetakan tool navigasi lewat `AddStaticItems` yang lama, dan tool lain lewat `AddCustomStaticItem`
  dengan owner `Window.GetWindow(this) ?? _app.MainWindow`.
- **`TabStripPanel.MinItemWidth` dinaikkan 110 → 130.** Dengan tombol reload (20px + margin),
  110 hanya menyisakan sekitar 40px untuk judul.
- **SPA di dalam kartu.** Toolbar `SpaNavigationHost` tidak punya latar sendiri, jadi tidak tampak
  berbingkai dobel di dalam kartu. Kartu dibiarkan apa adanya.
- **Login di MultiTab** ditampilkan lewat mekanisme tab yang sama: entri `admin.logon` menjadi satu
  tab (tersembunyi karena strip tab disembunyikan), dan kartu menampilkan body-nya.
- **Sign out mode debug di SPA** kini lewat `ShowLoginScreenAsync` (login menjadi satu-satunya entri
  `MainStack`), bukan `NavigateTo(logon)` biasa. Ini akibat satu jalur `EmApp.SignOutAsync` dari
  langkah 3.
- **Tab yang pindah window** mendapat objek `TabbedMainWindowTab` baru di window tujuan (tab mengikuti
  stack), jadi template tab-nya dibangun ulang. Body-nya tetap objek yang sama.
- **Window tear-off tanpa menu** (koreksi user 2026-09-26, sesudah commit `c420d9a`): menu hanya ada
  di window utama; window tear-off hanya berisi deretan tab dan dropdown daftar tab. `CopyShellFrom`
  tidak lagi menyalin `AppMenu`, `ToolsMenu`, maupun identitas pengguna (langkah 6 plan menyebut
  `ToolsMenu` ikut disalin). Tampilannya sudah begitu sejak awal lewat `WorkspaceToolsVisibility`.
- **Konstruktor.** `TabbedMainWindow(EmApp app)` tetap `public`; konstruktor tear-off
  `TabbedMainWindow(EmApp, NavigationStack?)` `internal`. Tidak ada lagi konstruktor tanpa parameter.
- `IEmAppUi` dibiarkan sebagai interface kosong (masih didaftarkan di DI), sesuai langkah 1.
- `EmAppBuilder.UseSinglePageLayout` diberi XML doc. XML doc `UseHomeNavigation` sudah menyebut
  bahwa multi-tab tidak punya home, jadi tidak diubah.
- Komentar XAML lama di `LoginControl.xaml` yang menyebut `MainWindow.OnThemeChanged` hanya diganti
  namanya menjadi `TabbedMainWindow.OnThemeChanged`.

### Checklist uji manual (untuk user)

Belum dijalankan executor. Dirujuk dari TODO "Uji regresi MultiTab dan MAUI sesudah refactor
navigasi".

- [ ] MultiTab non-debug: login memenuhi window → sesudah masuk, mode tab kosong ("No tab open"); menu
  Apps berisi navigasi yang boleh dibuka; menu Tools berisi static tools.
- [ ] User Manager → edit user: **User Editor terbuka sebagai tab baru** paling kanan; judul tab ikut
  `SetTitle`.
- [ ] Membuka judul yang sudah ada (di window mana pun) → tab itu yang dipilih, dan window-nya muncul.
- [ ] Pindah tab dari User Editor yang punya perubahan belum disimpan → ditanya; kalau batal, tab tetap
  di User Editor (sorotan kembali).
- [ ] Tutup tab aktif → kembali ke tab yang terakhir aktif. Tombol close, klik tengah, Close Others,
  dan Close to the Right berjalan, dan body yang menolak menahan tab-nya.
- [ ] Tombol reload di header tab (kiri tombol close) dan item Reload di menu klik kanan memuat ulang
  isi tab. Tombolnya muncul/hilang bersama tombol close, dan lebar tab tidak melompat saat hover.
- [ ] Tear-off dan drag antarwindow dengan isian belum disimpan: isinya tetap ada. Editor yang dibuka
  dari body di window tear-off menjadi tab di window itu. Manager dari window tear-off terbuka di
  window utama.
- [ ] Sign out dan sesi habis: window tear-off tertutup, semua tab hilang, kembali ke login. "Remember
  sign in" memulihkan sesi saat aplikasi dibuka ulang.
- [ ] Menutup window utama dengan body yang menolak → batal, tidak ada yang tertutup.
- [ ] Mode debug: langsung ke mode tab; combobox koneksi tampil dan mengganti koneksi aktif.
- [ ] Tema terang/gelap mewarnai semua window, termasuk panel brand login.
- [ ] SPA (`UseSinglePageLayout()`): login, home, Back/Forward, detach, dan sign out tetap seperti
  sebelum plan ini; title bar hanya berisi logo, judul, dan tombol caption; drag, klik ganda maximize,
  menu sistem, maximized tidak meluber (DPI 100% dan 150%); menutup window dengan window detach yang
  menolak → batal.
