# Plan — Navigasi 1/3: hapus layout MultiTab dari WPF

Status: **digantikan** oleh [multitab-tabbed-main-window.md](multitab-tabbed-main-window.md)
(2026-09-26), tidak dieksekusi. MultiTab tidak dihapus, tapi pindah ke `TabbedMainWindow` yang
tersambung ke sistem navigasi. Pembersihan kode DX MultiTab yang direncanakan di sini (`MainWindow`,
`TabHostWindow`, `TabWorkspace*`, `AddMainControl`, operasi `Tab*`, jalur `loginHost`) dikerjakan di
plan pengganti itu.
Dibuat: 2026-09-23, hasil diskusi dengan user.

Rangkaian plan navigasi yang berlaku sekarang:

1. [navigasi-2-entri-per-host.md](../executed/navigasi-2-entri-per-host.md): refactor engine navigasi (entri per host, Title sebagai kunci);
2. [navigasi-3-detach-window.md](../executed/navigasi-3-detach-window.md): detach window (WPF saja, hanya di layout SPA);
3. **plan ini**, menyusul kalau sudah diputuskan: hapus MultiTab.

Tiga bagian plan ini tidak bergantung pada penghapusan MultiTab, jadi sudah **dipindah ke plan 2**
(langkah persiapan) dan tidak perlu dikerjakan lagi di sini:

- `UseHomeNavigation(Navigation)` menggantikan `UseSinglePageLayout(Navigation)` (disetujui user);
- pengikatan claim `admin.users`/`admin.roles` dan penyaringan static tools di home lewat `CanOpen`;
- perbaikan `OnThemeChanged` supaya panel brand login SPA ikut berganti tema.

**Sebelum menjalankan plan ini**, cocokkan dulu dengan kode hasil plan 2 dan 3. Nama member yang
disebut di bawah (mis. `HomeNavigation`, `NavigateHome`, `NavigateToRoot`) berubah atau pindah ke
`NavigationStack` di plan 2, dan plan 3 menambah percabangan layout untuk detach window. Semua
percabangan `ApplicationLayout` yang ditambahkan plan 2 dan 3 ikut dibuang di sini.

Plan ini murni pengurangan kode. Setelah selesai, SPA menjadi satu-satunya layout WPF, sama seperti
MAUI. Aplikasi yang memakai `UseSinglePageLayout()` tidak melihat perubahan perilaku. Aplikasi
yang masih memakai layout bawaan (MultiTab) akan berpindah ke SPA.

---

## Latar belakang

- MultiTab tidak tersambung ke sistem navigasi:
  - `HomeNavigation`/`NavigateHome` melempar `NotSupportedException`;
  - `NavigateTo` kena NRE di `MainWindow._spaHost!`;
  - tab dikelola lewat API string header (`TabCreate`, `TabSelect`, `TabExists`, `TabRename`,
    `TabRemove`) yang tidak kenal `Navigation`, claim, maupun lifecycle body.
- Menggabungkan tab dengan model stack SPA ternyata rumit, sehingga diputuskan MultiTab dihapus
  seluruhnya. Kebutuhan membuka beberapa layar berdampingan dipenuhi lewat detach window (plan 3).

## Keputusan

1. MultiTab dihapus sepenuhnya, termasuk enum `ApplicationLayout` dan semua percabangannya.
2. SPA menjadi satu-satunya layout. Login hanya lewat navigasi `admin.logon`, sehingga jalur
   `loginHost` + `IsSignedIn` milik MultiTab ikut hilang.
3. `AddMainControl` dan semua turunannya dihapus.
4. ~~`admin.users`/`admin.roles` diikat ke claim internalnya~~: dipindah ke plan 2.

---

## Yang dihapus / diubah

### `EmAppBuilder` (`Em.Ui.Wpf.Core/Shared/EmAppBuilder.cs`)

- Hapus `AppLayout` dan `UseSinglePageLayout()`. `UseHomeNavigation(Navigation)` sudah dibuat di
  plan 2 dan tetap; buang kalimat XML doc-nya yang menyebut layout MultiTab.
- Hapus `AddMainControl` dan `MainControls`.
- `src/frontend/Em.Ui.Wpf/Program.cs`: hapus panggilan `UseSinglePageLayout()`.

### `EmApp` (`Core/EmApp*.cs`)

- Hapus property `ApplicationLayout`.
- `InitBuilder`: home selalu dibuat, tanpa cabang layout.
- `HomeNavigation`: getter tanpa cek layout.
- `NavigateHome` dan `NavigateToRoot`: buang cek `ApplicationLayout` beserta komentarnya.
- `Run`: `ShowFirstScreenAsync` selalu dijalankan.
- Hapus region `Tab Operations` (`TabCreate`, `TabSelect`, `TabExists`, `TabRename`, `TabRemove`).
- Hapus `_mainControls`, `MainControlsDefinitions`, dan blok di `InitBuilder` yang mendaftarkan main
  control sebagai transient (termasuk yang menyetel `MvvmModelBase.IsMainControlContext`).
- Perbarui XML doc yang menyebut `AddMainControl` atau main control (`BuildApp`, `InitBuilder`,
  summary class `EmApp`).

### `MainWindow` (`Windows/MainWindow.xaml(.cs)`)

- **XAML:**
  - hapus seluruh `DXTabControl` (termasuk toolbar kiri "Apps"/"Home" dan toolbar kanan yang ada di
    `ControlBoxLeft/RightTemplate`), `loginHost`, dan `WindowKind="Tabbed"`;
  - isi window dipasang dari code-behind (`SpaNavigationHost`), seperti sekarang.

  Toolbar kanan MultiTab (koneksi, akun, Tools, tema) sudah punya padanan di toolbar
  `SpaNavigationHost` dan di home; cek per item sebelum dihapus, dan catat di laporan kalau ada item
  yang tidak punya padanan. Selama plan ini ditunda, toolbar MultiTab dibiarkan apa adanya
  (keputusan user, 2026-09-24); nasib item tanpa padanan diputuskan ulang saat plan ini dijalankan.
- **Code-behind:**
  - `InitLayout` langsung memasang `SpaNavigationHost`; hapus `InitMultiTabLayout`,
    `InitSinglePageLayout`, `SyncLoginHost`, `_loginControl`, `UsesNavigationLoginFlow`,
    `ActiveLoginControl`, `InitTabs`, `BarItem_OnItemClick`, dan region `Tab Operations`;
  - `ShowLoginScreen` dan `RestoreSessionAsync` memakai `_spaLoginControl` secara langsung;
  - di handler `SessionEnded`, kondisi `UsesNavigationLoginFlow` diganti `!_app.IsDebugMode`.
- **`OnThemeChanged`:** plan 2 menambahkan `_spaLoginControl?.RenderBranding()` di samping
  `_loginControl?.RenderBranding()`. Di sini tinggal membuang panggilan milik `_loginControl`.
- **`MainWindowVm`:**
  - tidak lagi turunan `TabWorkspaceVm`, melainkan `MvvmModelBase`;
  - buang member yang hanya melayani toolbar/workspace MultiTab: `LoginVisible`, `WorkspaceVisible`,
    `CurrentUserToolsVisible`, `UserAccountVisible`, `LoggerViewerVisible`, `SimulateLogin`,
    `SimulateLoginVisible`, `LightModeSelected`/`DarkModeSelected`, `ChangeTheme`,
    `ConnectionConfig`, `PerformLogoff`, `ApiConnections`, dan `SelectedConnection`;
  - pertahankan yang masih dipakai jalur SPA: `IsSignedIn` (kalau masih dibaca),
    `SessionEndedNotice`, `LoadConnections`, dan `RefreshThemeState` (kalau masih dibutuhkan).

  Setiap member diperiksa pemakaiannya dengan grep sebelum dihapus.

### Berkas yang dihapus

- `Shared/TabWorkspaceHost.cs` (`TabWorkspaceHost`, `TabLocation`)
- `Shared/TabWorkspaceVm.cs` (`TabWorkspaceVm`, `MainTabVm`)
- `Windows/TabHostWindow.xaml` dan `.xaml.cs`
- `Shared/MainControlDefinition.cs`
- `Core/ApplicationLayout.cs`
- `Styles/Tabs.xaml`, **hanya kalau** grep menunjukkan tidak ada pemakai selain tab MultiTab. File ini
  di-merge di `MaterialDesign.xaml`, jadi cek key-nya satu per satu (mis. dipakai `RoleManager`).

### Lain-lain

- `IEmAppUi.MainControlsDefinitions`: hapus.
- `MvvmModelBase.IsMainControlContext`: hapus kalau sudah tidak ada yang mengisi atau membacanya.
- `src/frontend/modules/8XX-NSM/Em.Sample/Extensions.cs`: hapus panggilan `AddMainControl`.
- `Navigation.IsDetachVisible` dan tombol Detach di `SpaNavigationHost` **dibiarkan**. Keduanya
  dipakai plan 3.

### Claim User Manager / Role Manager

Sudah dipindah ke plan 2 (langkah persiapan), jadi tidak ada lagi yang dikerjakan di sini.

## Dokumentasi & memory

- `src/frontend/CLAUDE.md`:
  - bagian "Navigation host" (baris tentang `ApplicationLayout`, `UseSinglePageLayout`, dan
    `NotSupportedException`);
  - catatan `AddMainControl` di bagian module;
  - entri `IsMainControlContext`.
- `CLAUDE.md` root, bagian "Keep the UI cores in step": hapus contoh "the `MultiTab` layout" dan
  sisakan detach window (sudah ditambahkan plan 3) sebagai fitur WPF-only.
- Memory: hapus `project_addmaincontrol_to_be_removed.md` beserta barisnya di `MEMORY.md`.

## Verifikasi

- Build `src/frontend/Em.Ui.Wpf.slnx` tanpa error maupun warning baru. Grep `ApplicationLayout`,
  `TabCreate`, `AddMainControl`, `MainTabVm`, dan `TabHostWindow` di `src/` tidak menemukan apa-apa.
- Aplikasi (mode normal):
  - login → home;
  - User Manager → User Editor;
  - Back, Forward, Home, Reload;
  - logout → login dengan stack bersih;
  - "remember sign in" memulihkan sesi saat aplikasi dibuka ulang;
  - skenario detach window dari plan 3 tetap berjalan.
- Mode debug: langsung ke home, tanpa layar login.
