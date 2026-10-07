# Plan: User Manager — tab Roles, Switch User, dan Simulate Login (debug)

Tanggal: 2026-10-07
Status: belum dieksekusi
Area: `src/shared/Em.Ui.Wpf.Core` (WPF saja)

## Latar belakang

1. Tab **Access** di layar editor user (`Navigations/UserEditor.xaml`, `TabItem Header="Access"`) masih
   mockup statis: chip role hardcode (Administrator, Sales Manager, …), grid PERMISSIONS dummy, banner
   "UNDER CONSTRUCTION". Role user tidak bisa diatur dari sini, padahal backend sudah lengkap:
   `GetTa_UserRoles_ByUserId`, `PostTa_UserRole_New/NewBatch/Update/Delete/DeleteBatch` di
   `CredentialServices` (semua `RequireAdmin` untuk tulis; `NewBatch` idempotent).
2. Debug mode selalu memakai user `SYSTEM DEBUGGER`. Untuk menguji hak akses, developer perlu berganti
   menjadi user lain tanpa password. **Server sudah mendukung impersonasi**: request dengan debug token +
   header `X-Em-User` dijalankan sebagai user itu dengan hak aslinya
   (`Em.Api.Core/Api/Core/EmApp.cs`, `ResolveDebugCallerAsync`; user `Suspended`/`Pending`/`Deleted`
   ditolak). Header itu diisi client dari `EmApp.ActiveUser` (`GetActiveApiClient`, `SetActiveUser`).
   Jadi pekerjaannya hampir seluruhnya di client.

## Keputusan final (dari pengguna, 2026-10-07)

### Bagian A — tab Roles

| Topik | Keputusan |
|---|---|
| Nama tab | `Access` diganti `Roles` |
| Isi | Card untuk **semua role** yang ada, masing-masing dengan tombol toggle untuk memilih |
| Kapan disimpan | **Ikut tombol Save editor**. Toggle/periode hanya menandai perubahan; Save menyimpan, Discard membatalkan; status "unsaved changes" dan konfirmasi saat meninggalkan layar ikut menyala |
| Start/expiry | **Bisa diedit di card** (tanggal mulai dan berakhir, keduanya opsional) |
| Grid PERMISSIONS dummy, tombol Copy/Grant/Revoke, banner UNDER CONSTRUCTION | **Dihapus semua**. Hak langsung per user dicatat sebagai ide di `doc/ideas/` |
| User baru (belum disimpan) | **Bisa toggle sebelum save**; role disimpan setelah baris user tercipta, dalam satu klik Save |

### Bagian B — Switch User (debug)

| Topik | Keputusan |
|---|---|
| Letak | **Entri baru di Tools** bernama `Switch User`, hanya muncul saat `IsDebugMode`. Placeholder `Simulate Login` **dibiarkan** apa adanya |
| Bentuk | Dialog seperti Connection Config (`Dialogs/ConnectionConfig.xaml`): banner, card berisi list, tombol aksi |
| Hak akses di client | **Mengikuti user terpilih.** Bypass debug di client hanya berlaku saat user aktif = SYSTEM DEBUGGER; saat memakai user lain, menu, `NavigateTo`, Approval, BusinessTask, dan cek lain mengikuti hak asli user itu, sama seperti server |
| Tab/window terbuka saat ganti user | **Tutup semua, kembali ke awal** (seperti sign-out → sign-in), dengan konfirmasi bila ada perubahan belum disimpan |
| Diingat setelah restart | **Tidak.** Setiap start debug mode kembali ke SYSTEM DEBUGGER |
| Isi daftar | **Semua**: SYSTEM DEBUGGER, Admin bawaan, semua user. User `Suspended`/`Pending`/`Deleted` tampil redup dan tidak bisa dipilih. Ada kotak cari |

### Bagian C — Simulate Login (debug) — ditambahkan 2026-10-07

Tujuan: menguji alur aplikasi tanpa debug (login dengan password, sesi JWT, sign out, sesi kedaluwarsa)
dari build Debug, **tanpa restart IDE** untuk kembali ke debugger. Ide pembanding (launch profile
`--no-debug`, relaunch proses) tidak dipilih.

| Topik | Keputusan |
|---|---|
| Plan | **Digabung ke plan ini** (berbagi perubahan `IsDebugBypass`) |
| Bentuk | Mode yang bisa dibatalkan dalam proses yang sama. Placeholder Tools `Simulate Login` diisi: masuk simulasi → workspace ditutup → layar login; semua perilaku debug dimatikan lewat satu flag |
| Sign out di debug mode (bukan simulasi) | **Disembunyikan** dari menu akun (multi-tab dan SPA). Saat simulasi, Sign out tampil dan berjalan seperti aplikasi normal (kembali ke layar login) |
| Keep me signed in saat simulasi | **Disembunyikan, sesi tidak disimpan.** Simulasi tidak menulis/menghapus apa pun di Registry (`RememberSignIn`, `RememberedUserName`, `RememberedProfileName`, session storage) |
| Exit simulation saat masih login | **Sign out ke server** (`PostMeta_SignOut`, gagal diabaikan) setelah konfirmasi tab yang belum disimpan, kosongkan sesi, kembalikan debug token + SYSTEM DEBUGGER |
| Jalan keluar | Tautan "Exit simulation (back to debugger)" di layar login, dan chip `SIMULATED` + tombol Exit di title bar (multi-tab) / header (SPA) selama login di simulasi |

### Umum

| Topik | Keputusan |
|---|---|
| MAUI | **WPF saja.** Kebutuhan MAUI dicatat di `doc/ideas/` |
| Verifikasi | Build WPF + unit test, render harness PNG, uji SQL/HTTP nyata (lihat Seksi 4) |
| Commit | **Satu commit** di branch `work-bench`, pesan bahasa Indonesia, setelah verifikasi |

## Keputusan turunan (diambil saat menyusun plan, konsisten dengan keputusan di atas)

- Role berstatus non-aktif tetap ditampilkan dan tetap bisa di-toggle, dengan chip `INACTIVE` dan teks
  bantu bahwa role non-aktif tidak memberi hak (sesuai `UserClaimLoader.QueryRoleNames` yang hanya
  menghitung `RoleState.Active`).
- Akun sistem (SYSTEM DEBUGGER `Defaults.DebuggerUserId`, Admin bawaan `Defaults.AdminUserId`) tidak
  bisa diberi role (`Role.EnsureNotSystemAccount`). Bila editor membuka akun sistem, card ditampilkan
  disabled dengan pesan "System accounts already hold every permission and cannot be given roles."
- Pengubahan role butuh admin di server. Card ditampilkan untuk semua yang bisa membuka editor; error
  server ditampilkan lewat `ViewExceptionDetail()` seperti Save yang lain.
- Periode memakai `DatePicker` tanggal saja dengan style `inlineDatePickerStyle` (`Styles/Inputs.xaml`).
  Start disimpan pukul 00:00, expiry disimpan pukul 23:59:59 hari itu (waktu lokal, sama seperti kolom
  `datetime` lain). Kosong = tanpa batas. Validasi: bila keduanya terisi, expiry ≥ start; bila tidak,
  card menampilkan error merah dan Save ditahan.
- Periode lama yang punya jam selain 00:00/23:59:59 tetap dipertahankan selama tanggalnya tidak diubah
  (bandingkan per tanggal, bukan per detik, agar membuka lalu menyimpan tidak mengubah apa-apa).
- Urutan simpan saat Save: (1) `Data.SaveAsync()` hanya bila `Data.IsDirty` atau `Data.IsBlank`
  (`UiModel.SaveAsync` selalu mengirim update, jadi jangan dipanggil bila hanya role yang berubah);
  (2) kirim selisih role: `PostTa_UserRole_DeleteBatch` → `PostTa_UserRole_NewBatch` →
  `PostTa_UserRole_Update` per baris yang periodenya berubah (urutan sama dengan
  `Role.SaveContentAsync`). Bila langkah 2 gagal setelah langkah 1 sukses, baris user sudah tersimpan;
  selisih role tetap ditandai belum disimpan sehingga Save berikutnya mengulang (operasinya idempotent).
- `ustamp`/`datestamp` baris `ta_UserRole` baru diisi dari `App.GetDateStampAsync()`; baris yang
  periodenya diubah mempertahankan `datestamp` lama dan memperbarui `ustamp`.
- Switch User memakai tombol `Switch` (filled) dan `Close` (outlined); double-click baris = Switch.
  User aktif saat ini ditandai chip `CURRENT` dan tombol Switch disabled untuknya.
- Daftar user dimuat lewat **ApiClient terpisah tanpa header user** (identitas = debugger) supaya tetap
  bisa dimuat ketika user aktif adalah non-admin yang tidak berhak membaca daftar user.
- Setelah ganti user, judul/ikon akun di title bar ikut berubah (sudah lewat `ActiveUserChanged`).
  Tambahkan penanda debug di tooltip tombol akun: "Debug: acting as <account>" bila user aktif bukan
  SYSTEM DEBUGGER.

## Seksi 1 — Model dan view model tab Roles

Berkas: `src/shared/Em.Ui.Wpf.Core/Navigations/UserEditor.xaml.cs`.

1. Tambah class `UserRoleCardVm : MvvmModelBase` (di berkas yang sama, di bawah `UserEditorVm`, atau
   berkas baru `Navigations/UserRoleCardVm.cs` bila terlalu panjang). Isi:
   - `Role Role` (model `Em.Api.Core.Models.Role`), `RoleName`, `RoleDescription`, `IsRoleInactive`,
     `ClaimCountCaption` (mis. "12 permissions"; isi dari `Role.ClaimCount` bila tersedia, kalau tidak
     dari `Role.GetRoleClaims()` — lihat langkah 3).
   - Baseline: `bool OriginalIsAssigned`, `DateTime? OriginalStart`, `DateTime? OriginalExpiry`,
     `ta_UserRole? OriginalRow`.
   - Nilai kerja: `bool IsAssigned`, `DateTime? StartDate`, `DateTime? ExpiryDate` (Get/Set
     `MvvmModelBase`, **public** — lihat memory: properti private gagal saat runtime).
   - `bool IsPeriodEnabled => IsAssigned && IsEditable`; `bool IsEditable` (false untuk akun sistem
     atau saat busy).
   - `bool HasPeriodError => StartDate is {} s && ExpiryDate is {} e && e.Date < s.Date`.
   - `bool IsChanged` (assign berubah, atau assign tetap true dan tanggal start/expiry berubah per
     tanggal).
   - `PeriodStateCaption`: `SCHEDULED` / `EXPIRED` / kosong, logika sama dengan
     `RoleManager.xaml.cs` `MemberRowVm.State` (pakai `DateTime.Now` lokal).
   - Event `Changed` yang dipicu setiap nilai kerja berubah.
   - `void Accept()` menjadikan nilai kerja baseline (setelah sukses simpan); `void Revert()` kembali
     ke baseline (Discard).
2. Di `UserEditorVm`:
   - `ObservableCollection<UserRoleCardVm> RoleCards`, `bool IsRolesLoading`, `string? RolesError`,
     `bool HasRoleChanges => RoleCards.Any(c => c.IsChanged)`,
     `bool HasRolePeriodError => RoleCards.Any(c => c.HasPeriodError)`,
     `bool IsSystemAccount => Data?.cUserId is Defaults.DebuggerUserId or Defaults.AdminUserId`.
   - `HasUnsavedChanges` menjadi `Data?.IsDirty == true || HasRoleChanges`.
   - `SaveCommandAllowed`: syarat lama, tetapi `Data.IsDirty` diganti `(Data.IsDirty || HasRoleChanges)`
     dan tambah `!HasRolePeriodError`.
   - `DiscardCommand`: `Data?.RollBack()` lalu `Revert()` semua card.
   - `RefreshRolesCommand` (tombol refresh kecil di pojok kanan atas card daftar role, sesuai aturan
     card dinamis): memuat ulang role + assignment; bila ada perubahan role belum disimpan, tanya dulu
     dengan `ShowMboxDecideWarning`. Dicegah dobel saat loading.
3. `LoadRolesAsync()` dipanggil dari `LoadFrom(payload)` (tanpa await di jalur UI; tangani error ke
   `RolesError`):
   - Ambil semua role lewat `ICredentialServices.GetVi_Roles()` → `Role.Build(app, row)`, urut
     `cRoleName`.
   - Jumlah claim: pakai pola `RoleCollection` (baca `src/shared/Em.Ui.Core/Api.Core.Models/RoleCollection.cs`
     cara ia mengisi `ClaimCount` sekaligus untuk satu halaman). Bila hanya tersedia per role, boleh
     dilewati: caption claim disembunyikan, bukan N request.
   - Bila user tersimpan (`!Data.IsBlank` dan bukan akun sistem): `GetTa_UserRoles_ByUserId(cUserId)`
     untuk baseline. User baru atau akun sistem: baseline kosong.
   - Bila role yang ditugaskan tidak ada di daftar role (tidak mungkin karena FK, tetapi aman), abaikan.
4. `SaveCommand` diperluas sesuai "Urutan simpan" di atas. Gunakan `Data.cUserId` **setelah**
   `SaveAsync` (user baru baru mendapat id di sini). Bangun tiga array `ta_UserRole`:
   - removed: card `OriginalIsAssigned && !IsAssigned` → `{cUserId, cRoleId}`.
   - added: card `!OriginalIsAssigned && IsAssigned` → `{cUserId, cRoleId, cUserRoleStart, cUserRoleExpiry, ustamp, datestamp}`.
   - rescheduled: card assigned di kedua sisi dengan periode berubah → salinan `OriginalRow` dengan
     periode dan `ustamp` baru.
   Panggil service langsung (`ICredentialServices`), lalu `Accept()` semua card. `WaiterText` tetap
   "Saving user...".
5. `RaiseRecordChanged` ikut memberi tahu `HasUnsavedChanges`, `IsSystemAccount`; card `Changed`
   memanggil `NotifyChanged(nameof(HasUnsavedChanges))` + `RaiseCommandsChanged()`.
6. Konversi tanggal: helper statis `ToStart(DateTime? d) => d?.Date`,
   `ToExpiry(DateTime? d) => d?.Date.AddDays(1).AddSeconds(-1)`; pembanding per tanggal
   `SameDate(DateTime? a, DateTime? b)`.

## Seksi 2 — XAML tab Roles

Berkas: `src/shared/Em.Ui.Wpf.Core/Navigations/UserEditor.xaml`.

1. Ganti seluruh `TabItem Header="Access"` (komentar `ACCESS` sampai penutup `</TabItem>` sebelum
   komentar `ACTIVITY`) dengan `TabItem Header="Roles"`. Hapus banner UNDER CONSTRUCTION, chip
   statis, grid PERMISSIONS, tombol Copy from user / Grant all / Revoke all. Pertahankan catatan info
   bawah ("Access rules are read when a user signs in. …").
2. Susunan:
   - Header baris: caption `ROLES` (`sectionCaptionStyle`) + teks bantu "Switch a role on to give it to
     this account. Changes are saved with the Save button." + di kanan tombol refresh kecil (ikon
     `Solid_RotateRight`, tooltip "Reload roles", nama aksesibilitas) — samakan dengan tombol refresh
     card lain di repo (cari pola di `Controls/StorageSettingsCard.xaml`).
   - Pesan akun sistem (bila `IsSystemAccount`), pesan error `RolesError` (warna danger), indikator
     loading, dan teks kosong "No roles yet. Create one in Role Manager." bila daftar kosong.
   - `ItemsControl` `RoleCards` dengan `ItemsPanel` `WrapPanel` (card, bukan toolbar — aturan WrapPanel
     hanya berlaku untuk tombol toolbar), lebar card tetap ±300, margin antar card 8/12.
   - Template card: `Border` `cardStyle` (atau surface + outline + CornerRadius 12 seperti card lain),
     baris atas: nama role (SemiBold) + chip `INACTIVE` bila non-aktif + chip periode
     (`SCHEDULED`/`EXPIRED`) + `ToggleButton` di kanan (`IsChecked="{Binding IsAssigned, Mode=TwoWay}"`,
     `IsEnabled="{Binding IsEditable}"`). Cari style toggle/switch yang sudah ada di `Styles/` (mis.
     `switchToggleStyle`/sejenis); bila tidak ada, pakai `filterChipStyle` yang dipakai mockup lama.
     Jangan membuat template tombol baru. Di bawahnya deskripsi (TextWrapping, maks 2 baris, opacity
     0.75) dan caption jumlah permission. Baris periode: label `Start` / `Expiry` dengan dua
     `DatePicker` `inlineDatePickerStyle`, `IsEnabled="{Binding IsPeriodEnabled}"`, label–input
     jarak 4–6, antar input 8. Error periode: teks danger "Expiry is before start." dan border danger.
   - Card yang `IsChanged` diberi penanda kecil (titik aksen atau border aksen) agar tahu apa yang
     belum disimpan.
3. Semua warna dari resource tema (`surfaceBrush`, `outlineBrush`, `dangerBrush`, `themeAccentBrush`,
   …). Pastikan kondisi disabled (akun sistem, busy saat Save) tidak memutih: periksa template
   `ToggleButton` dan `DatePicker` pada `IsEnabled=false`.

## Seksi 3 — Switch User (debug)

### 3a. Hak akses client mengikuti user aktif

Berkas: `src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs` dan pemakai `IsDebugMode` yang membypass hak.

1. Tambah di `EmApp` (bersama flag simulasi dari Seksi 3d):
   ```csharp
   /// <summary>
   /// Whether a debug build is currently running as a normal application, see Simulate Login. While it
   /// is on, every debug-only behaviour is off.
   /// </summary>
   public bool IsSimulatingLogin { get; private set; }

   /// <summary>Whether debug-only features (debug connection pick, Switch User, debug cards) are shown.</summary>
   public bool IsDebugActive => IsDebugMode && !IsSimulatingLogin;

   /// <summary>
   /// Whether the client skips its own permission checks: only while debug is active and the active user is
   /// the debugger account. When a developer switches to another user, the client follows that user's real
   /// permissions, the same way the server does.
   /// </summary>
   public bool IsDebugBypass => IsDebugActive && ActiveUser?.cUserId == Defaults.DebuggerUserId;
   ```
   Telusuri **semua** pemakaian `IsDebugMode` di `Em.Ui.Wpf.Core`, `Em.Test.Wpf`, dan host: yang berarti
   "bypass hak" → `IsDebugBypass`; yang berarti "tampilkan fitur debug" → `IsDebugActive`; hanya yang
   memang berarti "build ini punya konfigurasi debug" (mis. `InitDebugMode`, entri Tools Simulate Login)
   yang tetap `IsDebugMode`. Daftar keputusan per titik dicatat di laporan eksekusi.
2. Ganti pemakaian `IsDebugMode` **yang berarti bypass hak** dengan `IsDebugBypass`:
   - `Core/EmApp.cs` `CanOpen`: `(IsDebugBypass || NavigationAccess.CanOpen(navigation, ActiveUser))`.
   - `Core/ApprovalAccessCatalog.cs` `CanOpen`.
   - `Navigations/ApprovalManager.xaml.cs` `HoldsClaim`.
   - `Navigations/BusinessTaskManager.xaml.cs` `IsAdmin`.
   - `src/modules/Em.Test/Em.Test.Wpf/TestVmBase.cs` dan `TestHome.xaml.cs` baris `isAdmin`.
   - Perbarui komentar `NavigationAccess.cs` dan XML doc `CanOpen` yang menyebut debug mode.
   Pemakaian bersifat tampilan (connection combobox `TabbedMainWindow`, `SyncSelectedConnection`,
   `DefaultHomeControl` card debug, refresh claims di handler `ActiveConnectionChanged` konstruktor
   `EmApp`) memakai `IsDebugActive`. Alur login/sign-out diatur di Seksi 3d. `TestHome` info
   "debug mode" menampilkan ketiganya (`IsDebugMode`, `IsSimulatingLogin`, `IsDebugBypass`).

### 3b. Mengganti user aktif

Berkas: `src/shared/Em.Ui.Wpf.Core/Core/EmApp.MainWindowFlow.cs` (atau partial baru
`Core/EmApp.DebugUser.cs`).

1. `internal async Task<bool> SwitchDebugUserAsync(User user)`:
   - Tolak bila `!IsDebugMode` (`InvalidOperationException`).
   - Bila user sama dengan `ActiveUser` → `return true`.
   - Konfirmasi seperti menutup window: layout multi-tab: `ConfirmCloseTearOffWindowsAsync()` lalu
     `ConfirmCloseTabsAsync(MainStack)`; layout SPA: `ConfirmCloseDetachedWindowsAsync()` lalu
     `MainStack.AskCurrentToLeave()`. Satu penolakan → `return false`, tidak ada yang berubah.
   - `await MainWindow.ReleaseWorkspaceAsync()`.
   - `SetActiveUser(user)`; lalu `_claimsRefresh = RefreshClaimsAsync(); await _claimsRefresh;`.
     Bila refresh gagal: kembalikan ke debugger (`SetActiveUser(CreateDebuggerUser(this))` + refresh)
     dan lempar ulang exception agar dialog menampilkannya.
   - `await MainWindow.ShowSignedInAsync()` (SPA: kembali ke home + reload; multi-tab: menu dibangun
     ulang sesuai hak user baru).
   - `return true`.
2. Untuk kembali ke debugger, dialog memanggil method yang sama dengan objek dari
   `CreateDebuggerUser(this)` (jadikan `internal` bila perlu). Admin bawaan memakai `CreateAdminUser`.

### 3c. Dialog Switch User

Berkas baru: `src/shared/Em.Ui.Wpf.Core/Dialogs/SwitchUserDialog.xaml` + `.xaml.cs`.

1. Root `windows:EmWindow` (aturan EmWindow), `Title="Switch User"`, ukuran seperti
   `ConnectionConfig` (760×520, min 640×420), `ShowMinimizeButton=False`, `ShowInTaskbar=False`,
   `CenterOwner`, merge `Styles/MaterialDesign.xaml`.
2. Layout mengikuti `ConnectionConfig.xaml`:
   - Banner: ikon `Solid_UserSecret` (atau `Solid_UserGear`), judul "Switch User", teks "Debug only.
     Act as another account without its password. The server applies that account's real
     permissions."; di kanan chip akun aktif saat ini.
   - Kotak cari (filter akun, nama lengkap, e-mail; client-side) di atas card.
   - Card list kolom: `ACCOUNT`, `NAME`, `STATE`, `ROLE` (Admin/User). Baris pertama dua akun sistem
     (SYSTEM DEBUGGER, Admin bawaan) dengan chip `SYSTEM`. Akun aktif chip `CURRENT`. Baris tidak
     bisa dipilih (`Suspended`, `Pending`, `Deleted`) opacity 0.5, `IsEnabled=false`, tooltip
     "The server refuses to act as a <state> account." Admin bawaan yang dinonaktifkan server akan
     ditolak saat switch; tampilkan error dari refresh claims.
   - Pakai `surfaceListBoxStyle` dari `Styles/Collections.xaml` (atau template eksplisit seperti
     `connectionRowStyle`) agar ListBox tidak memutih saat disabled/busy.
   - Tombol kanan bawah: `Switch` (`filledButtonStyle`, disabled bila tidak ada pilihan / pilihan =
     current / busy) dan `Close` (`outlinedButtonStyle`, `IsCancel`). Tombol refresh kecil di pojok
     kanan atas card list (aturan card dinamis).
   - Loading/error state: teks "Loading users..." dan pesan error danger + tombol refresh untuk coba
     ulang.
3. VM `SwitchUserDialogVm : MvvmModelBase`:
   - Memuat user lewat **ApiClient terpisah**: `var client = app.ActiveConnection!.CreateApiClient();`
     (debug token sudah melekat di `ApiConnection.DebugToken`; `ActiveUserId/Account` dibiarkan null
     sehingga server memakai identitas debugger), lalu
     `client.GetAsync<vi_User[]>(Defaults.CredentialModuleName, nameof(ICredentialServices.GetVi_Users))`.
     Periksa tanda tangan `ApiClient.GetAsync<T>(module, action, params object[] args)` sebelum menulis.
     Bungkus `User.Build(app, row)`. Client dibuang (`Dispose` bila `IDisposable`) saat dialog tutup.
   - Bila `ActiveConnection` null: tampilkan error "Pick a debug connection first."
   - `SwitchCommand`: `await app.SwitchDebugUserAsync(selected)`; `true` → tutup dialog
     (`DialogResult = true`), `false` → tetap terbuka tanpa pesan (user membatalkan konfirmasi), error →
     `ShowMboxError`.
4. Daftarkan di `EmApp.GetStaticTools()` (`Core/EmApp.cs`): entri baru **sebelum** placeholder
   `tools.simulatelogin`, hanya bila `IsDebugMode`:
   ```csharp
   Name = "tools.switchuser", Title = "Switch User", Subtitle = "Act as another user",
   Description = "Debug only: act as another account without its password to test permissions.",
   Icon = EFontAwesomeIcon.Solid_UserSecret.CreateImageSource(Brushes.Gray),
   Invoke = owner => { new Dialogs.SwitchUserDialog(this) { Owner = owner }.ShowDialog(); return Task.CompletedTask; }
   ```
   Entri ini memakai syarat `IsDebugActive` (tidak tampil saat simulasi). Placeholder
   `Simulate Login` diisi di Seksi 3d.
5. Tooltip tombol akun di title bar (`TabbedMainWindow`) dan header akun SPA (`SpaNavigationHost`):
   bila `IsDebugMode` dan user aktif bukan debugger, tambahkan "Debug: acting as <account>". Cukup
   lewat properti VM yang sudah di-refresh oleh `ActiveUserChanged`.

### 3d. Simulate Login

Berkas: `Core/EmApp.cs`, `Core/EmApp.MainWindowFlow.cs` (atau partial baru `Core/EmApp.Simulation.cs`),
`Navigations/LoginControlVm.cs`, `LoginControlMaterial.xaml`, `LoginControlClassic.xaml`,
`Windows/TabbedMainWindow.xaml(.cs)`, `Navigations/SpaNavigationHost.xaml(.cs)`.

1. **Sign out disembunyikan di debug aktif.** Tombol Sign out di `TabbedMainWindow.xaml` (sekitar baris
   1060) dan `SpaNavigationHost.xaml` (sekitar baris 213) diberi `Visibility` dari properti VM
   `IsSignOutVisible => EmApp is { } app && (!app.IsDebugMode || app.IsSimulatingLogin)`, di-refresh pada
   `ActiveUserChanged` dan saat simulasi mulai/berakhir. `SignOutAsync` juga menolak (no-op) bila
   `IsDebugActive`, sebagai pengaman lapis kedua.
2. **Masuk simulasi** — `internal async Task<bool> BeginLoginSimulationAsync()`:
   - Tolak bila `!IsDebugMode` atau sudah simulasi.
   - Konfirmasi tab/window seperti `SwitchDebugUserAsync` (Seksi 3b); batal → `false`.
   - `ReleaseWorkspaceAsync()`.
   - Simpan dan kosongkan debug token semua `DebugConnections` (`_simulationDebugToken`), sehingga sejak
     layar login tidak ada request yang membawa debug token. Catatan: `BeginSessionAsync` kemudian
     menyimpan `_suspendedDebugToken = null`, dan `EndSessionAsync` mengembalikan `null` — konsisten.
   - `IsSimulatingLogin = true`; `SetActiveUser(null)`; raise event baru `DebugStateChanged` (dipakai
     window/VM untuk me-refresh visibilitas fitur debug, Sign out, chip SIMULATED, menu Tools).
   - `await ShowLoginScreenAsync(null)`.
3. **Entri Tools** `tools.simulatelogin` (placeholder yang ada, hanya bila `IsDebugActive`):
   `Invoke = _ => BeginLoginSimulationAsync()` dengan deskripsi "Debug only: sign in with a real account
   and password, as the application runs without debug. Exit from the login screen or the SIMULATED chip."
   Error ditampilkan `ShowMboxError`.
4. **Alur selama simulasi** (mengikuti jalur non-debug):
   - `OnSessionEndedAsync`: ganti `if (IsDebugMode) return;` menjadi `if (IsDebugActive) return;` sehingga
     sign out atau sesi kedaluwarsa saat simulasi kembali ke layar login (dengan notice), seperti aplikasi
     normal.
   - `SignOutAsync`: cabang debug hanya untuk `IsDebugActive`; saat simulasi → `EndSessionAsync(true)`.
   - `RestoreSessionAsync`, `ShowSignedInAfterLoginAsync` (`if (!IsDebugMode)`) → `IsDebugActive`.
   - `ShowSignedInAsync` (`TabbedMainWindow`, `if (_app.IsDebugMode) await stack.Home!.Reload()`) →
     `IsDebugActive` bila maksudnya fitur debug; periksa dan putuskan saat eksekusi.
5. **Remember disembunyikan.** Di `LoginControlVm`:
   - Properti `IsRememberVisible => EmApp?.IsSimulatingLogin != true`; kedua layar login
     (Material, Classic) mengikat `Visibility` checkbox "Keep me signed in" ke properti ini.
   - Saat simulasi, `AttachApp` **tidak** membaca/menulis `RememberSignIn`/`RememberedUserName`, dan
     `OnRememberMeChanged` tidak menulis Registry; sign in mengirim `remember: false` dan tidak mengisi
     `RememberedUserName`/`RememberedProfileName`.
   - `BeginSessionAsync(remember: false)` saat ini memanggil `SessionStorage.Clear(connection.ProfileName)`;
     saat simulasi lewati `Clear` itu (dan di `EndSessionAsync`) agar sesi tersimpan milik run non-debug
     tidak ikut terhapus.
6. **Keluar simulasi** — `internal async Task<bool> EndLoginSimulationAsync()`:
   - Bila masih login di simulasi: konfirmasi tab/window (batal → `false`), lalu jalankan bagian sign out
     dari `EndSessionAsync(notifyServer: true)` **tanpa** memicu `SessionEnded`→layar login (tambah
     overload/flag internal, mis. `EndSessionCoreAsync(notifyServer, raiseEnded: false)`), lalu
     `ReleaseWorkspaceAsync()`.
   - Kembalikan debug token dari `_simulationDebugToken`; `IsSimulatingLogin = false`;
     `SetActiveUser(CreateDebuggerUser(this))`; raise `DebugStateChanged`.
   - Pastikan `ActiveConnection` kembali ke `DefaultDebugConnection` bila selama simulasi login memilih
     profil lain; lalu `_claimsRefresh = RefreshClaimsAsync()`.
   - Lepas layar login seperti `ShowSignedInAfterLoginAsync` (lepas handler `SignInSucceeded`), lalu
     `MainWindow.ShowSignedInAsync()`.
7. **Tombol keluar**:
   - Layar login (Material dan Classic): tautan/text button "Exit simulation (back to debugger)", hanya
     tampil bila `IsSimulatingLogin`; command `ExitSimulationCommand` di `LoginControlVm` memanggil
     `EndLoginSimulationAsync`. Letakkan di area bawah form, gaya `textButtonStyle`, warna tema.
   - Selama login di simulasi: chip `SIMULATED` (warna warning) di title bar `TabbedMainWindow` dan header
     `SpaNavigationHost`, dengan tombol kecil Exit (ikon + tooltip "Exit simulation and go back to the
     debugger", nama aksesibilitas). Tetap tampil walaupun user tidak punya hak apa pun.
8. Selama simulasi entri Tools Switch User dan Simulate Login tersembunyi (`IsDebugActive` false); combobox
   koneksi debug tersembunyi; layar login tetap menampilkan pilihan koneksi seperti biasa (termasuk profil
   debug, kini tanpa debug token).

## Seksi 4 — Pengujian dan analisa (dikerjakan sekali setelah Seksi 1–3 selesai)

Ikuti aturan "kode dulu, analisa di akhir": per seksi cukup tulis kode; semua uji di bawah setelah
seluruh kode selesai. Bila token menipis, hentikan uji/analisa dulu dan catat yang tertunda.

1. **Build + unit test**: `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan
   `dotnet test src/frontend/Em.Ui.Wpf.slnx`. Bila menyentuh kontrak bersama, juga
   `dotnet build src/backend/Em.Api.slnx`. Tambahkan unit test di `tests/Em.Ui.Wpf.Core.Tests` untuk
   logika murni: `UserRoleCardVm` (IsChanged per tanggal, HasPeriodError, Accept/Revert, konversi
   start/expiry) dan pembentukan tiga array selisih role (pisahkan ke method statis/internal yang bisa
   dites; `InternalsVisibleTo` bila belum ada — periksa dulu).
2. **Render harness PNG** di `..\.artefacts\em-system\scripts\user-roles-render\` (contoh harness:
   `..\.artefacts\em-system\scripts\role-manager-render`, memory "Uji layar WPF tanpa server"): render
   tab Roles (user tersimpan dengan role terpilih + periode, user baru, akun sistem disabled, error
   periode, loading/busy) dan dialog Switch User (daftar dengan CURRENT/SYSTEM/redup, loading, error)
   pada tema **terang dan gelap**, plus satu lebar sempit. Periksa tidak ada kilatan putih pada kondisi
   disabled/busy.
3. **Uji SQL/HTTP nyata** di `..\.artefacts\em-system\scripts\user-roles-smoke\` terhadap SQL Server
   lokal dan `Em.Api` lokal dengan debug token (lihat `tests/README.md` dan konfigurasi
   `emapi-config.json` di `..\.artefacts\em-system\config\`):
   - Assign role ke user lama lewat jalur yang sama dengan Save (batch new/delete/update), baca kembali
     `GetTa_UserRoles_ByUserId`, cek periode tersimpan 00:00 / 23:59:59.
   - User baru + role dalam satu Save.
   - Impersonasi: request dengan debug token + `X-Em-User` user non-admin → `GetMeta_UserRoleClaims`
     berisi claim dari role; action ber-claim yang tidak dimiliki ditolak; user `Suspended` ditolak.
   - Simulate Login: request sign-in tanpa debug token sukses dengan password asli; request selama
     simulasi tidak membawa header debug token; `PostMeta_SignOut` mematikan sesi di server.
   - Bersihkan data uji setelah selesai.
3b. **Unit test simulasi**: dengan `EmApp` tak-terinisialisasi seperti harness headless, uji transisi
   flag (`IsDebugActive`, `IsDebugBypass`, `IsSimulatingLogin`) dan pemulihan debug token setelah masuk →
   keluar simulasi, bila bisa dipisahkan tanpa window. Bila tidak praktis, catat sebagai tertunda.
3c. **Render**: layar login Material dan Classic saat simulasi (tanpa checkbox remember, dengan tautan
   Exit simulation) dan chip `SIMULATED` di title bar/header, tema terang dan gelap.
4. Bila ada langkah yang terblokir policy, ikuti aturan "Tindakan terblokir policy" di `CLAUDE.md`
   (skrip `.ps1` di `plan/usermanager-roles-dan-switch-user-manual/`).
5. **Belum bisa diverifikasi agent** (catat di laporan sebagai tertunda): interaksi mouse nyata di
   window sungguhan (toggle, DatePicker, double-click), alur Switch User end-to-end di aplikasi WPF yang
   berjalan terhadap server, dan alur Simulate Login end-to-end (masuk → login → sign out → login lagi →
   Exit simulation → kembali sebagai debugger tanpa restart).

## Seksi 5 — Dokumentasi, ide, dan penutupan

1. `doc/engine/` (bahasa Inggris): perbarui dokumen yang membahas User Manager / roles (cari
   `engine-*.md` yang menyebut User Manager atau Role Manager) dengan bagian singkat tab Roles; tambah
   bagian "Switch User (debug mode)" di dokumen yang membahas debug token/debug mode (cari
   `DebugToken`/`debug mode` di `doc/engine/`), termasuk aturan `IsDebugBypass`, serta bagian
   "Simulate Login" (cara masuk/keluar, apa yang dimatikan, sesi tidak disimpan, Sign out disembunyikan
   di debug aktif).
2. `doc/ideas/` (bahasa Indonesia, format catatan ide: tanggal, status `diskusi`, keputusan jelas,
   pertanyaan terbuka):
   - `user-hak-langsung.md`: editor hak langsung per user (`ta_UserClaim`) pengganti grid PERMISSIONS
     yang dihapus.
   - `maui-switch-user-roles.md`: tab Roles, Switch User, dan Simulate Login untuk MAUI.
   - `debug-launch-profile.md`: profil launch `--no-debug` untuk menguji alur startup non-debug dari
     build Debug (status `diskusi`; ide pembanding yang belum dipilih, 2026-10-07).
   - Saran lain yang muncul saat eksekusi.
3. `CLAUDE.md`: tambah satu paragraf "Pembaruan 2026-10-07 (User Manager Roles & Switch User)" berisi
   ringkasan, aturan `IsDebugActive`/`IsDebugBypass`/`IsSimulatingLogin`, status uji, dan yang
   tertunda. Jangan menghapus isi lama.
4. Komentar kode, XML doc, dan teks UI dalam **bahasa Inggris**; member public/protected baru wajib
   XML comment (CS1591 aktif).
5. Pindahkan berkas plan ini ke `plan/executed/` dengan laporan eksekusi di bagian bawah (yang selesai,
   hasil uji, keputusan yang diambil saat eksekusi, verifikasi tertunda).
6. **Commit** satu kali di `work-bench` (jangan ke `main`), pesan bahasa Indonesia, mis.
   `User Manager: tab Roles, Switch User, dan Simulate Login untuk debug mode`, diakhiri baris
   `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Laporan eksekusi

(diisi saat eksekusi)
