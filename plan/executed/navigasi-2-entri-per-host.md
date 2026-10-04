# Plan — Navigasi 2/3: entri per stack, Title sebagai kunci unik

Status: **sudah dieksekusi** (2026-09-24, commit `09c0a04`), bersama plan 3 dalam satu kali jalan. Laporan:
[doc/report/navigasi-2-3-eksekusi.md](../../doc/report/navigasi-2-3-eksekusi.md). Plan **pertama** yang dijalankan di rangkaian navigasi.
[navigasi-1-hapus-multitab.md](../unexecuted/navigasi-1-hapus-multitab.md) **ditunda** (keputusan user,
2026-09-24): MultiTab tidak dihapus dulu, jadi plan ini dikerjakan dengan MultiTab tetap hidup (lihat
bagian "Hidup berdampingan dengan MultiTab").
Dibuat: 2026-09-23, hasil diskusi dengan user. Semua usulan sudah dikonfirmasi user pada 2026-09-24.

Plan ini mengubah **engine** navigasi di kedua core (`Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core`)
serta kontrak bersamanya di `Em.Ui.Core`. Detach window belum dibuat di sini (itu plan 3), tetapi
semua fondasinya disiapkan di sini.

---

## Cara eksekusi (keputusan user, 2026-09-24)

Berlaku untuk plan 2 dan plan 3, yang dijalankan sebagai satu rangkaian:

1. **Plan 2 lalu plan 3 dalam satu kali jalan**, tanpa berhenti di antaranya untuk menunggu user.
   Berhenti hanya kalau ada build error di luar cakupan plan, atau ada kebutuhan yang melanggar
   batas plan (mis. detach terpaksa masuk ke kontrak bersama). Dalam kasus itu, laporkan dan tanya
   user dulu.
2. **Commit di akhir tiap plan** (satu commit untuk plan 2, satu untuk plan 3), dengan pesan dalam
   Bahasa Indonesia, supaya gampang di-rollback per tahap. Izin commit ini khusus untuk rangkaian
   ini. Sebelum commit, build harus bersih.
3. **Verifikasi oleh Claude hanya build:** `src/frontend/Em.Ui.Wpf.slnx` dan `Em.Ui.Maui.slnx`,
   tanpa error maupun warning baru, plus grep yang disebut di bagian Verifikasi. Skenario klik di
   WPF dan tes di emulator MAUI **dijalankan user sendiri**. Laporan eksekusi mencantumkan daftar
   skenario itu sebagai checklist untuk user.
4. **Penutup:** setelah plan 3 selesai, kedua berkas plan dipindah ke `plan/executed/` dan satu
   laporan eksekusi ditulis di `doc/report/`. Laporan itu mencatat hasil build, percabangan
   `ApplicationLayout` baru yang ditambahkan (untuk plan 1 nanti), dan penyimpangan dari plan kalau
   ada. Pemindahan plan dan laporan ikut di commit plan 3.
5. **`Program.cs`:** di awal eksekusi plan 2, `builder.UseSinglePageLayout()` di
   `src/frontend/Em.Ui.Wpf/Program.cs` **di-uncomment**, sehingga aplikasi berjalan di SPA. Ini
   bagian dari eksekusi dan ikut di commit plan 2.

## Latar belakang

Sekarang satu instance `Navigation` memegang **satu body** (`_body`) dan **satu data**
(`CurrentNavigationData`), dan stack tunggal dipegang `EmApp`. Akibatnya satu navigasi hanya bisa
tampil di satu tempat. "Invoice: INV-001" dan "Invoice: INV-002" tidak bisa hidup bersamaan, padahal
itu tujuan utama detach window (membandingkan dua dokumen berdampingan).

## Keputusan

1. **`Navigation` hanya menjadi definisi:** nama, Title bawaan, `BodyType`, menu, claim, jenis, dan
   flag toolbar. Body, data, dan Title yang sedang tampil pindah ke **entri** stack.
2. **Stack dipisah dari `EmApp`** menjadi objek sendiri. Di plan ini masing-masing core baru
   punya satu stack (stack utama); plan 3 menambah stack per window detach di WPF.
3. **Title entri adalah kunci unik** di seluruh aplikasi. Satu Title hanya boleh tampil di satu
   tempat. `NavigateTo` dengan Title yang sudah ada tidak membuka duplikat, tapi memindahkan posisi
   ke entri itu.
4. **Title awal** diambil dari payload (`NavigationPayloadBase.Title`), dengan fallback ke
   `Navigation.Title`. Title bisa di-rename lewat `SetTitle`, misalnya "Create New User" menjadi
   "Edit User: ani" setelah disimpan.
5. **Setiap navigasi wajib menyatakan jenisnya**, `NavigationKind.Manager` atau
   `NavigationKind.Editor`. Aturan jenis baru berpengaruh di plan 3; di sini hanya dideklarasikan.
6. **MAUI ikut model yang sama:** entri, Title sebagai kunci, `SetTitle`, dan `NavigationKind`,
   dengan satu stack dan tanpa detach. Kedua core tetap satu struktur.
   **Tidak ada satu pun member detach yang masuk ke MAUI maupun ke kontrak bersama `Em.Ui.Core`:**
   - `IsDetachVisible`, command/tombol Detach, daftar stack detach, dan batas jumlah window
     tetap milik `Em.Ui.Wpf.Core` saja; `INavigation`, `INavigationStack`, `INavigationEntry`,
     dan `INavigationHost` tidak memuatnya;
   - `Navigation` MAUI tidak mendapat `IsDetachVisible`, dan `SpaNavigationHost` MAUI tidak
     mendapat tombol Detach;
   - XML doc kontrak bersama tidak menyebut detach. Contohnya, `INavigationStack.Home` cukup
     dijelaskan "nullable, untuk stack tanpa home", bukan "untuk stack window detach";
   - `NavigationKind` tetap ada di MAUI karena itu klasifikasi navigasi, bukan fitur detach. Di MAUI
     nilainya tidak memengaruhi perilaku apa pun.
7. **Layout MultiTab WPF dipertahankan apa adanya.** Engine navigasi baru hanya berjalan di layout
   SPA. Lihat bagian berikut.

---

## Hidup berdampingan dengan MultiTab (WPF)

MultiTab sekarang tidak tersambung ke sistem navigasi: tab-nya diisi lewat `AddMainControl` dan API
string header (`TabCreate`, dsb.), dan login-nya lewat `loginHost` + `IsSignedIn`. Plan ini **tidak
menyentuh** jalur itu. Aturannya:

- **Tidak ada yang dihapus atau diperbarui dari MultiTab** (keputusan user, 2026-09-24), termasuk
  item toolbar MultiTab yang tidak punya padanan di SPA: `ApplicationLayout`,
  `UseSinglePageLayout()`, `AddMainControl`/`MainControlDefinition`, region
  `Tab Operations`, `TabWorkspaceHost`/`TabWorkspaceVm`/`TabHostWindow`, `DXTabControl` di
  `MainWindow`, jalur login `loginHost`, dan `MainWindowVm` beserta command toolbar MultiTab tetap
  seperti sekarang.
- **Navigasi tetap didaftarkan di kedua layout** (`InitInternalNavigation` + navigasi module), jadi
  `Kind` wajib diisi tanpa melihat layout.
- **`MainStack` hanya dibuat dan dipakai di layout SPA.** Di MultiTab tidak ada home, sama seperti
  sekarang (`InitBuilder` hanya membuat home di cabang `SinglePage`).
- **Router di MultiTab:** `EmApp.NavigateTo`, `FindEntry`, dan member `MainStack` melempar
  `NotSupportedException("Multi-tab navigation is not supported.")`, sama dengan yang dilakukan
  `HomeNavigation`/`NavigateHome` sekarang. Ini menggantikan NRE di `MainWindow._spaHost!`, tanpa
  mengubah fakta bahwa navigasi memang tidak didukung di MultiTab.
- **`MvvmModelBase.NavigationEntry` nullable.** Nilainya `null` untuk VM yang tidak dibangun lewat
  navigasi, termasuk main control di tab MultiTab (`IsMainControlContext = true`). Body yang
  memanggil `NavigationEntry.NavigateTo` hanya dibangun lewat navigasi, jadi tidak pernah bertemu
  `null`.
- **Percabangan `ApplicationLayout` yang baru** hanya ditambahkan kalau memang perlu, dengan pola
  yang sama seperti yang sudah ada (cek di `EmApp`, bukan di body). Setiap percabangan itu dicatat
  di laporan eksekusi supaya mudah dibuang saat plan 1 dijalankan.
- **`Program.cs`:** `builder.UseSinglePageLayout()` di-uncomment sebagai bagian eksekusi (lihat
  "Cara eksekusi" butir 5), jadi aplikasi berjalan di SPA. MultiTab tetap bisa dicoba dengan
  mengomentari baris itu lagi.

## Langkah persiapan (dipindah dari plan 1)

Tiga bagian plan 1 tidak bergantung pada penghapusan MultiTab, jadi dikerjakan di sini sebelum
refactor engine:

- **`UseHomeNavigation(Navigation)` (disetujui user, 2026-09-24):**
  - di `EmAppBuilder` WPF, overload `UseSinglePageLayout(Navigation customHomeNavigation)` diganti
    `UseHomeNavigation(Navigation customHomeNavigation)`, dengan nama, isi, dan XML doc yang sama
    dengan builder MAUI (`Em.Ui.Maui.Core/Shared/EmAppBuilder.cs`): hanya mengisi
    `CustomHomeNavigation`, **tidak** memilih layout;
  - `UseSinglePageLayout()` tanpa parameter **tetap ada**, karena itulah saklar layout selama
    MultiTab belum dihapus. Aplikasi SPA dengan home sendiri memanggil keduanya;
  - XML doc `UseHomeNavigation` di WPF menambahkan satu kalimat: di layout MultiTab tidak ada home,
    jadi pemanggilan ini tidak berpengaruh di sana;
  - overload lama tidak punya pemanggil di repo (sudah dicek dengan grep), jadi tidak ada yang perlu
    dimigrasi.
- **Claim User Manager / Role Manager (disetujui user):**
  - `InitInternalNavigation` (`EmApp.Statics.cs`): `admin.users` diikat ke claim
    `Administrative Tools:User Manager Access`, dan `admin.roles` ke
    `Administrative Tools:Role Manager Access`, lewat `Navigation.ModuleName`/`RequiredClaim`
    (setter internal sudah ada). Deklarasi claim di `InitInternalClaims` tidak berubah.
  - `DefaultHomeControl.RenderStaticItems`: `AddStaticItems` dilewati untuk navigasi yang gagal
    `CanOpen`, jadi aturan tampilnya sama dengan menu aplikasi.
  - Efeknya (SPA): user non-admin tanpa claim itu tidak lagi melihat kedua tool di home, dan
    `NavigateTo` menolaknya. MultiTab tidak terpengaruh karena tidak membuka kedua tool itu lewat
    navigasi.
- **`MainWindow.OnThemeChanged`:** sekarang hanya memanggil `_loginControl?.RenderBranding()`, yang
  hanya ada di MultiTab. Tambahkan `_spaLoginControl?.RenderBranding()` di sampingnya (bukan
  mengganti), supaya panel brand login di kedua layout ikut berganti warna saat tema berubah.

---

## Kontrak bersama (`Em.Ui.Core/Ui.Core/shared/`)

### `NavigationKind` (baru)

```csharp
public enum NavigationKind { Manager, Editor }
```

### `INavigation`: tinggal definisi

- **Tetap:** `Name`, `Title`, `Subtitle`, `Description`, `MenuPath`, `BodyType`, `RequireParameter`,
  `OrderIndex`, `ModuleName`, `RequiredClaim`, `NavigationHost`.
- **Tambah:** `NavigationKind Kind`.
- **Keluar:** `NavigationBody`, `Forward()`, `Backward()`, `Reload()`, `StackIndex`. Semuanya adalah
  urusan entri atau stack.

### `INavigationEntry` (baru): satu tempat di stack

| Member | Arti |
| --- | --- |
| `INavigation Navigation` | definisinya |
| `string Title` | kunci unik sekaligus judul yang tampil |
| `object? Data` | parameter yang dipakai membuka entri ini |
| `INavigationBody Body` | body milik entri ini, dibangun saat entri dibuat |
| `INavigationStack Stack` | stack yang memegang entri ini |
| `Task Reload()` | memanggil `OnReloadRequested` pada body-nya |
| `bool SetTitle(string title)` | mengganti Title; `false` kalau Title itu sudah dipakai entri lain (Title lama dipertahankan) |
| `Task<bool> Close()` | mengeluarkan entri dari stack-nya (`OnNavigatingAway` boleh menolak, lalu `OnRelease`) |
| `Task<bool> NavigateTo(string name, object? data = null)` + overload `INavigation` | membuka navigasi **relatif ke stack entri ini**; lihat aturan di bawah |

### `INavigationStack` (baru): pindahan member stack dari `INavigationHost`

`Entries`, `Current`, `Home` (nullable; stack detach di plan 3 tidak punya home), `Forward()`,
`Backward()`, `NavigateHome()`, `ClearForwardStacks()`, `IsInStack(title)`.

### `INavigationHost`: router tingkat aplikasi (`EmApp`)

- **Tetap:** `Navigated`, `Navigations`, `NavigateTo(name, data)`, `NavigateTo(INavigation, data)`.
  Keduanya membuka relatif ke **stack utama**, dipakai dari luar body (menu home, login, dsb.).
- **Tambah:** `INavigationStack MainStack`, `INavigationEntry? FindEntry(string title)` (mencari di
  semua stack).
- **Keluar** (pindah ke `INavigationStack`): `Stacks`, `CurrentNavigationStack`, `HomeNavigation`,
  `Forward`, `Backward`, `NavigateHome`, `ClearForwardStacks`, `IsInStack`, `RemoveFromStack`.

Member lama dihapus, bukan dibiarkan sebagai alias. Ini perubahan struktur, bukan varian method.

### `NavigationEventArgs`

Tambah `INavigationEntry Entry`. Body mendapat entrinya sendiri lewat callback, jadi bisa memanggil
`args.Entry.SetTitle(...)`, `Close()`, dan `NavigateTo(...)`. `NavigationItem` tetap ada.

### `NavigationPayloadBase`

Tambah `public virtual string? Title => null;`. XML doc-nya wajib menyatakan bahwa Title dokumen
berparameter **harus mengandung penanda unik dokumen** (mis. RefNumber atau akun), bukan teks yang
bisa kembar seperti nama customer. Kalau dua dokumen berbeda menghasilkan Title yang sama, dokumen
kedua tidak akan pernah bisa dibuka.

## Aturan `NavigateTo`

Berlaku untuk `EmApp.NavigateTo` (asal: stack utama) dan `entry.NavigateTo` (asal: stack entri):

1. `CanOpen(nav)` gagal → `false`.
2. Hitung Title: `(data as NavigationPayloadBase)?.Title ?? nav.Title`.
3. **Title sudah ada** di stack mana pun (`FindEntry`) → pindahkan posisi stack pemiliknya ke entri
   itu:
   - aturan pindah posisi sama seperti kunjungan ulang sekarang: entri lain tidak dibuang;
   - body yang sedang tampil di stack itu mendapat `OnNavigatingAway` dan boleh menolak;
   - **tanpa reload dan tanpa mengganti data** entri tersebut.
4. **Title belum ada** → buat entri baru di stack asal:
   1. `OnNavigatingAway` pada body yang sedang tampil (boleh menolak);
   2. bangun body baru, lalu `OnNavigatingIn` (boleh menolak; kalau menolak, body baru langsung
      di-`OnRelease` dan dibuang);
   3. `ClearForwardStacks`, tambahkan entri;
   4. `Reload()`, lalu picu `Navigated`.
5. Aturan `NavigationKind` di plan ini belum mengubah apa pun, karena baru ada satu stack. Plan 3
   menambahkan: Manager yang diminta dari stack detach dibuka di stack utama.

**Perubahan perilaku SPA** (dikonfirmasi user, 2026-09-24):
- Satu navigasi bisa punya beberapa entri di stack yang sama kalau Title-nya berbeda, misalnya
  "Edit User: ani" dan "Edit User: joni". Sekarang kunjungan kedua akan memakai ulang entri yang
  sama.
- Kunjungan ulang ke Title yang sudah ada **tidak lagi me-reload**. Sekarang `NavigateTo` publik
  selalu me-reload dengan data baru. Ini mengikuti kesepakatan "yang sudah terbuka hanya
  diaktifkan". **Disetujui.**

## Struktur per core

Keduanya dibangun dengan bentuk yang sama; hanya bagian view yang berbeda.

| Bagian | WPF (`Em.Ui.Wpf.Core`) | MAUI (`Em.Ui.Maui.Core`) |
| --- | --- | --- |
| Definisi | `Core/Navigation.cs`: buang `_body`, `NavigationBody`, `HasBody`, `CurrentNavigationData`, `Forward`, `Backward`, `Reload`, `ReleaseBody`, `StackIndex`; tambah `required NavigationKind Kind` | sama |
| Entri | `Core/NavigationEntry.cs` (baru): pindahan body/data/reload/release dari `Navigation` | sama |
| Stack | `Core/NavigationStack.cs` (baru): pindahan isi `EmApp.NavigationHost.cs` (stack, home, forward/backward, release) dengan elemen `NavigationEntry` | sama |
| Router | `Core/EmApp.NavigationHost.cs`: `MainStack`, `FindEntry`, aturan `NavigateTo` di atas, `CanOpen` | sama |
| Pembuatan body | `BodyType.Create` menerima entri; `MvvmModelBase` mendapat `NavigationEntry` (setter internal) di samping `EmApp` | `BodyType.Create` menerima entri (VM MAUI mengikuti pola yang ada) |
| Host view | `SpaNavigationHost` terikat ke `INavigationStack`; toolbar membaca `Current.Title` dan flag dari `Current.Navigation` | `SpaNavigationHost` MAUI: `BodyHost.Content = Vm.Current?.Body`, dsb. |

Home tetap entri khusus di posisi -1 milik stack utama, dan body-nya tidak pernah dilepas (aturan
yang sama dengan sekarang).

## Pemakai yang harus disesuaikan

- **WPF:**
  - `SpaNavigationHost` (Back/Forward/Home/Reload, `NavigationBody`, `BackAllowed`,
    `ForwardAllowed`);
  - `MainWindow.ShowLoginScreen`, yang membaca body login dari `MainStack.Current.Body`;
  - `EmApp.ShowFirstScreenAsync` (`HomeNavigation.Reload()` menjadi `MainStack.Home.Reload()`);
  - `DefaultHomeControl`, dengan menu tetap memanggil `EmApp.NavigateTo(nav)`.
- **`UserManager`:** `AddNewUserCommand`/`EditUserCommand` memanggil `NavigationEntry.NavigateTo(...)`
  milik body-nya, bukan `EmApp.NavigateTo`, supaya di plan 3 editor terbuka di window yang sama
  dengan User Manager-nya.
- **`UserEditorNavigationPayload`:**
  - `Title` mengembalikan `"Create New User"` atau `$"Edit User: {Data.cUserAccount}"`;
  - `UserEditor` memanggil `SetTitle` setelah user baru tersimpan (`SetNewUser`).
- **MAUI:**
  - `SpaNavigationHost`, termasuk `OnBackButtonPressed`, `CanGoForward`, `LeadingCommand`;
  - `LoginControl` (`HomeNavigation.Reload()`);
  - `AccountPanel` dan `DefaultHomeControl`, yang cukup memanggil `NavigateTo` router.
- **Module Sample** (`Em.Sample`):
  - `Extensions.cs`: isi `Kind` (`sample.manager` = Manager, Sample Editor = Editor);
  - `UserControl1` memakai `NavigationEntry.NavigateTo`.
- **Registrasi internal:** isi `Kind` di `InitInternalNavigation` WPF dan MAUI:
  - Home dan `admin.logon` = Manager (tidak bisa di-detach; diatur di plan 3);
  - `admin.users` dan `admin.roles` = Manager;
  - `admin.users.editor` = Editor;
  - navigasi internal MAUI (mis. Change Password) = Editor.

## Dokumentasi

- `src/frontend/CLAUDE.md`, bagian "Navigation host": model entri, Title sebagai kunci, aturan
  `NavigateTo`, `NavigationKind` wajib, dan `entry.NavigateTo` untuk navigasi dari dalam body.
  Catat bahwa semua itu hanya berlaku di layout SPA, dan bahwa MultiTab tetap memakai
  `AddMainControl` + API tab sampai plan 1 dijalankan.
- XML doc (Bahasa Indonesia) untuk semua member publik baru di `src/shared`. Tanpa menyebut nama
  objek database.

## Verifikasi

- Build `Em.Ui.Wpf.slnx` dan `Em.Ui.Maui.slnx` tanpa error maupun warning baru.
- **WPF, layout SPA** (`builder.UseSinglePageLayout()` aktif di `Program.cs`, sudah di-uncomment
  di awal eksekusi):
  - `UseHomeNavigation(customHome)` + `UseSinglePageLayout()` → home kustom yang tampil; tanpa
    `UseHomeNavigation` → `DefaultHomeControl`;
  - login → home → User Manager → Edit ani → Back → Edit joni: dua entri editor, Back/Forward di
    antara keduanya bekerja;
  - klik lagi "Edit ani" saat entrinya masih di stack → posisi pindah tanpa reload, isian
    dipertahankan;
  - "Create New User" dua kali → hanya satu entri; simpan → Title menjadi "Edit User: <akun>";
  - Home membersihkan stack dan melepas semua body (cek `OnRelease` terpanggil);
  - logout → login dengan stack bersih;
  - ganti tema di layar login → panel brand ikut berganti;
  - akun non-admin tanpa claim User/Role Manager tidak melihat kedua tool di home, dan
    `NavigateTo("admin.users")` mengembalikan `false`.
- **WPF, layout MultiTab** (`UseSinglePageLayout()` dikomentari sementara oleh user untuk tes).
  Perilakunya harus sama dengan sebelum plan ini:
  - login lewat `loginHost` → workspace tab;
  - tombol "Home" di toolbar menambah tab tes; tab bisa ditutup dan ditarik keluar ke
    `TabHostWindow`;
  - ganti tema dari toolbar, Connection Config, logout → kembali ke login;
  - mode debug langsung ke workspace, dengan Simulate Login tersedia.
- Grep `Detach` (tidak peka huruf besar/kecil) di `src/shared/Em.Ui.Maui.Core` dan
  `src/shared/Em.Ui.Core` tidak menemukan apa-apa.
- **MAUI (emulator):**
  - login → home → navigasi → back hardware Android;
  - Forward, Reload;
  - logout;
  - Change Password dari AccountPanel.
