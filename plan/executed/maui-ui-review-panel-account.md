# Plan — Refactor UI MAUI: panel account, toolbar navigasi, home & login

Status: **sudah dikerjakan (2026-09-19)** — Tahap E, A, B, C, D seluruhnya selesai
Keputusan terbuka: **sudah tertutup semua (2026-09-19)** — lihat bagian 4.
Dibuat: 2026-09-19
Baseline: commit `e9ba4d0` (Add MAUI client foundation and shell), branch `data-services`
Lingkup file:
- `src/shared/Em.Ui.Maui.Core/Navigations/SpaNavigationHost.xaml` + `.xaml.cs`
- `src/shared/Em.Ui.Maui.Core/Navigations/DefaultHomeControl.xaml` + `.xaml.cs`
- `src/shared/Em.Ui.Maui.Core/Navigations/LoginControl.xaml` + `.xaml.cs`
- `src/shared/Em.Ui.Maui.Core/Navigations/AccountPanel.xaml` + `.xaml.cs` (baru)
- `src/shared/Em.Ui.Maui.Core/Styles/Navigation.xaml`
- `src/shared/Em.Ui.Maui.Core/Controls/MaterialIcon.cs` → `FontIcon.cs`, `MaterialIcons.cs` →
  `FontIcons.cs`, `IconButton.cs`
- `src/shared/Em.Ui.Maui.Core/Resources/Fonts/` (baru) + `Em.Ui.Maui.Core.csproj`
- `src/shared/Em.Ui.Maui.Core/Core/EmApp.Statics.cs` (pendaftaran font di `Run<TApp>`)

> **Cara memakai dokumen ini kalau percakapan sudah di-clear.** Bagian 1 adalah hasil
> review yang sudah disepakati — jangan dibuka ulang tanpa alasan baru. Bagian 2 adalah
> temuan kode yang mengubah bentuk pekerjaan; baca sebelum mulai. Bagian 3 tahapan kerja.
> Bagian 4 **sudah tertutup seluruhnya** — tidak ada lagi yang perlu ditanyakan sebelum
> mulai; isinya tinggal catatan alasan. Sebelum lanjut, jalankan `git status` untuk
> memastikan file di atas masih seperti baseline.

## 0. Konteks

Client MAUI (`Em.Ui.Maui.Core`) adalah salinan sengaja dari `Em.Ui.Wpf.Core` —
android-only, SPA-only, modul masih ditunda. Shell pertamanya sudah jalan: satu
`SpaNavigationHost` dengan app bar + drawer kiri, `DefaultHomeControl` berisi kartu-kartu,
dan `LoginControl` dengan picker koneksi.

Review user atas shell itu meminta UI-nya didekatkan ke Google Play Store versi baru:
foto user di pojok kanan atas, panel account yang masuk dari kanan, dan navigasi yang
bentuknya mengikuti WPF. Diskusi penuhnya ada di riwayat percakapan; yang mengikat
tercatat di bagian 1.

## 1. Keputusan yang sudah final

### 1.1 Panel account (fly-in dari kanan)

Dipicu dengan men-tap badge foto user di pojok kanan atas app bar. Animasinya masuk dari
kanan ke kiri. Susunannya dari atas ke bawah:

| Bagian | Ikut scroll | Isi |
| --- | --- | --- |
| Header akun | ya | Satu card rounded. Ikon di kiri, `cContactFullName` font besar, `cCommValue` font kecil. **Tanpa** chevron/dropdown ganti akun — beda dari Play Store. |
| Static navigation | ya | **Satu card untuk semuanya, tanpa pengelompokan.** Tiap baris ikon di kiri, dipisah divider tipis. |
| Server | ya | Status server (mengikuti chip "Connected to …" di layar login WPF) + radio pilih koneksi. Di debug isinya koneksi debug; di release tepat satu baris — server yang sedang dipakai, kembar dengan baris status. Lihat 1.7. |
| Logout | **tidak** | Panel tetap menempel di paling bawah, di luar scroll. Warna danger. |

`cCommValue` dibaca apa adanya sebagai email — untuk user biasa email dijamin ada
(lihat 2.6). Akun khusus (DU/SA) yang terbaca `"N/A"` diterima apa adanya, tidak perlu
fallback.

### 1.2 Isi static navigation

Mengikuti pola `StaticToolMenus` di WPF (`Em.Ui.Wpf.Core/Navigations/DefaultHomeControl.xaml.cs:60`,
`RenderStaticItems` → `AddStaticItems`/`AddCustomStaticItem`). Di MAUI daftarnya pindah
pemilik ke panel account, dan **disalin manual** — konsisten dengan keputusan bahwa
`Em.Ui.Maui.Core` menyalin `Em.Ui.Wpf.Core`, bukan berbagi kode dengannya.

Yang disalin adalah **polanya**, bukan isinya. `admin.users` dan `admin.roles` di daftar
WPF itu sekadar contoh bagaimana sebuah navigasi ditambahkan sebagai entri statis;
keduanya tidak ada di MAUI dan tidak perlu diadakan (lihat 2.3). Isi awal card ini di MAUI
**hanya Simulate Login** — Connection Config tidak diadakan sama sekali (lihat 1.8).

Aturan tampilnya:
- Entri yang hanya bermakna saat debug (Simulate Login) → dibatasi `EmApp.IsDebugMode`.
- Entri lainnya → dibatasi claim lewat `services.Claims()["nama"]`.
- **Bukan dua-duanya.** `ClaimCollection` menjawab `true` untuk setiap claim kalau
  `cUserIsAdmin` (`Em.Ui.Core/Ui.Core/shared/ClaimCollection.cs:61`), dan DU dibuat
  dengan `cUserIsAdmin = true` (`Em.Ui.Maui.Core/Core/EmApp.Statics.cs:184`) — jadi
  di debug entri ber-claim sudah otomatis lolos tanpa cabang `IsDebugMode` sendiri.

### 1.3 Toolbar navigasi (poin 13 & 14 review)

**Tidak ada control host baru. Back/Home/Reload diintegrasikan ke `SpaNavigationHost`.**
Alasannya: host itu sudah pemegang `ISpaShell`, yang memasang body ke `BodyHost`, yang
punya `LeadingCommand`/`ForwardCommand`/`ReloadCommand`, dan yang melayani tombol back
perangkat Android lewat `OnBackButtonPressed`. Memecahnya ke control lain akan membuat dua
jalur berbeda untuk satu perbuatan yang sama.

Susunan app bar sesudah refactor:
- Kiri: **Back**, lalu **Home**.
- Kanan: **Reload**, lalu **badge foto user**.
- **Forward tidak ditampilkan**, tapi `CanGoForward` dan `Navigation.IsForwardVisible`
  tetap dipertahankan — di Android forward tak punya gestur yang wajar, tapi host ini
  bisa dipakai lagi di MAUI desktop.
- Di home: back & home tidak muncul (stack kosong), tersisa judul + reload + badge.
- Badge muncul di **setiap** layar, bukan cuma home — itu yang menggantikan peran drawer.

### 1.4 Drawer kiri dibuang

Panel account sudah menampung apa yang selama ini jadi alasan drawer ada, jadi drawer
dihapus sekalian — bukan disisakan kosong. Isinya habis terbagi: daftar navigasi → home
(Tahap C), akun/server/tema/logout → panel account (1.1).

Karena itu tombol paling kiri berhenti jadi tombol ganda: `LeadingGlyph` yang sekarang
berganti antara hamburger dan panah back menjadi murni back.

### 1.5 Login & mode debug

- `IsDebugMode = true` → langsung masuk `DefaultHome` sebagai DU, tanpa layar login
  (perilaku sekarang, dipertahankan).
- `IsDebugMode = false` → wajib login dulu. Tidak ada pilih koneksi seperti WPF.
- Layar login: input **API Server**, **User Name**, **Password**.
- **Remember Me otomatis** — checkbox-nya dibuang, keluar hanya lewat logout.
- Input API Server: saat release berbentuk **entry teks biasa**, diisikan ulang dengan URL
  terakhir yang berhasil dipakai masuk (lihat 1.7). Saat debug **tidak bisa diketik** —
  nilainya dikunci ke koneksi yang sedang terpilih.
- Tombol **Simulate Login** ada di panel account saat debug; men-tap-nya melompat ke layar
  login untuk mengetik password sungguhan, dengan **URL terkunci** ke koneksi yang dipilih
  di card server panel account. **Tidak ada editable combobox** — keputusan ini
  menggantikan rencana sebelumnya, jadi `Picker` MAUI yang tak bisa diketik (2.5) berhenti
  jadi masalah.

> Poin 6 dan 11 review tertulis `IsDebugMode=false`; **sudah dikonfirmasi user bahwa
> keduanya typo, maksudnya `true`**. Poin 12 **sudah tidak perlu dipastikan lagi**: dengan
> URL yang terkunci saat debug, tidak ada cabang perilaku yang bergantung pada bacaannya.

### 1.6 Home (poin 9 & 10 review)

- Satu **search box** di atas, dengan ikon kaca pembesar di kiri (mengikuti WPF).
- **Daftar navigasi collapsible dengan kedalaman dinamis** sesuai `MenuPath`, sama seperti
  WPF.

### 1.7 Dari mana koneksi terisi — **tidak ada riwayat server**

- **Debug** — dari `opt.AddDebugConnection` (`MauiProgram.cs:28`, di dalam `#if DEBUG`),
  lewat `EmApp.DebugConnections`. Sudah jalan sekarang, dan ini **satu-satunya pintu**
  koneksi yang ada di MAUI.
- **Release** — URL diketik di layar login, lalu disimpan sebagai **satu slot profil yang
  selalu ditimpa**: `EmApp.AddApiConnection` (`Core/EmApp.cs:422`) tetap dipanggil,
  tapi yang tersimpan tidak pernah lebih dari satu entri.

**"Tidak ada riwayat" berarti tidak ada daftar yang menumpuk, bukan tidak ada yang
tersimpan.** Nama profilnya diturunkan otomatis dari host URL, jadi server yang sama tidak
pernah menggandakan baris; server yang berbeda **menimpa** yang lama (profil sebelumnya
dihapus lebih dulu). Isinya karena itu selalu tepat satu: server yang sedang dipakai.

Bentuk ini dipilih karena penyimpanan sesi menuntutnya — lihat **2.10**. Tanpa satu profil
pun, "tetap masuk" di release tidak akan pernah jalan.

Akibatnya untuk card server di panel account (1.1): radio pilih koneksi dibangun dari
`RetrieveApiConnections` (`:399`) apa adanya. Di debug isinya koneksi debug; di release
isinya tepat satu baris yang praktis kembar dengan baris status di atasnya. Ganti server
di release berarti logout lalu mengetik URL lain di layar login — dan URL lama hilang,
bukan menumpuk.

### 1.8 Connection Config tidak diadakan

WPF membukanya sebagai dialog (`Em.Ui.Wpf.Core/Dialogs/ConnectionConfig.xaml`). Di MAUI
layar itu **tidak dibuat sama sekali** — bukan ditunda, melainkan tidak punya pekerjaan.
Di debug daftar koneksi ditentukan saat compile lewat `AddDebugConnection`; di release
isinya satu slot yang diurus sendiri oleh layar login (1.7). Tidak ada satu pun profil yang
perlu ditambah, diubah, atau dihapus user lewat UI.

Karena itu folder `Dialogs` di `Em.Ui.Maui.Core` juga tetap tidak dibuat, dan tidak ada
gestur hapus di baris radio panel account.

## 2. Temuan kode yang mengubah bentuk pekerjaan

Ini semua sudah diverifikasi di kode pada baseline, bukan dugaan.

### 2.1 Empat saklar `Navigation` sudah ada tapi mati

`Navigation.IsBackVisible` (`Core/Navigation.cs:77`), `IsHomeVisible` (`:95`),
`IsColorThemeVisible` (`:101`), `IsUserVisible` (`:107`) **tidak dibaca sama sekali** oleh
host MAUI — yang terbaca baru `IsToolbarVisible`, `IsTitleVisible`, `IsReloadVisible`,
`IsForwardVisible`. Jadi sebagian besar Tahap A adalah menyambungkan saklar yang sudah
ada, bukan membuat mekanisme baru.

### 2.2 Katalog claim bisa belum termuat saat panel pertama dibuka

`ClaimCollection` menjawab `false` untuk **semua** claim selama katalognya kosong
(`ClaimCollection.cs:38`). Di MAUI, `RefreshClaimsAsync` baru dipicu dari
`ActiveConnectionChanged` (`Core/EmApp.cs:44`) dan hasilnya disimpan sebagai task di
field `_claimsRefresh` — **tidak ada event yang memberi tahu kalau sudah selesai**.

→ Isi card static navigation **harus dihitung ulang setiap kali panel dibuka**, bukan
sekali saat host dibangun. Kalau dilewat, gejalanya membingungkan: menu hilang di sesi
pertama, muncul setelah aplikasi dibuka ulang.

### 2.3 Tidak ada navigasi terdaftar di MAUI selain logon & home

`MauiProgram.cs` tidak mendaftarkan satu navigasi pun, dan `EmApp.Statics.cs` hanya
mendaftarkan `admin.logon` (`:237`) plus home. `UserManager`/`RoleManager` masih berupa
`UserControl` WPF di `Em.Ui.Wpf.Core`, dan memang tidak diadakan di MAUI untuk sekarang.

Dua akibat praktis:

- Baris seperti `AddStaticItems(_app.Navigations.Single(r => r.Name == "admin.users"))`
  **akan melempar** kalau disalin apa adanya dari WPF. Yang diambil dari sana adalah bentuk
  `AddStaticItems`/`AddCustomStaticItem`-nya, bukan argumennya.
- Mesin cek claim di 1.2 tetap dibangun sekarang supaya entri ber-claim tinggal dipasang
  nanti, tapi **belum ada yang memakainya** sampai ada navigasi MAUI yang butuh dibatasi.
  Jangan bingung kalau di tahap ini tidak ada entri yang pernah tersembunyi karena claim.

### 2.4 Search box WPF belum tersambung ke apa pun

`DefaultHomeControl.xaml:582` — `<TextBox x:Name="searchBox" …/>` tanpa binding `Text`,
tanpa handler `TextChanged`. Satu-satunya yang membacanya adalah `DataTrigger` watermark
di baris 593. Jadi search box WPF hari ini **dekoratif**.

→ **Sudah diputuskan: di MAUI ikut dibuat dekoratif.** Bentuknya sama dengan WPF, belum
tersambung ke apa pun. Filternya dikerjakan nanti, saat sudah ada banyak module yang
membuatnya berguna — dan sebaiknya di kedua client sekaligus, bukan MAUI sendirian.

### 2.5 MAUI tidak punya Expander, editable ComboBox, maupun Dialogs

- Tidak ada `Expander` di MAUI → grup collapsible untuk pohon `MenuPath` harus dibangun
  sendiri (header yang bisa di-tap + `IsVisible` pada isinya + rotasi ikon chevron).
  Logika pohonnya sendiri bisa di-port apa adanya: `MenuGroup`, `SplitMenuPath`,
  `ResolveGroup`, `RebuildMenuTree` di `Em.Ui.Wpf.Core/Navigations/DefaultHomeControl.xaml.cs:295-338`.
- `Picker` MAUI tidak bisa diketik → **tidak lagi jadi masalah**: 1.5 sudah tidak memakai
  editable combobox, input API Server-nya entry teks biasa (release) atau terkunci (debug).
- `Em.Ui.Maui.Core` **tidak punya folder `Dialogs`** — padanan `Dialogs/ConnectionConfig`
  WPF belum ada sama sekali, dan memang tidak akan diadakan (lihat 1.8).

### 2.6 Aturan "email wajib" belum berlaku di kode

Di luar lingkup MAUI, tapi menyentuh header panel account: `UserEditor` WPF hanya memeriksa
*bentuk* email, bukan *keberadaannya*. `HasEmailError`
(`Em.Ui.Wpf.Core/Navigations/UserEditor.xaml.cs:229`) sengaja lolos untuk nilai kosong,
dan `SaveCommandAllowed` (`:456`) hanya mewajibkan `cUserAccount` dan `cContactFullName`.

→ User tanpa email masih bisa disimpan hari ini, dan panel account-nya akan menampilkan
baris kedua kosong. Perbaikannya **tidak** termasuk plan ini.

### 2.7 FontAwesome di MAUI: fontnya bisa, paketnya tidak

Keputusan: ikon MAUI memakai **FontAwesome**, supaya sama dengan WPF — bukan path Material
seperti sekarang.

**Versinya sengaja berbeda per client — ini keputusan, bukan hal yang menunggu diperbaiki:**

| Client | Versi | Sebabnya |
| --- | --- | --- |
| MAUI | **Font Awesome 7** (OTF: `Free-Solid-900`, `Free-Regular-400`, `Brands-Regular-400`) | Merender glyph langsung dari file font, jadi tidak butuh library sama sekali. |
| WPF | **Font Awesome 6.2.0** (paket `FontAwesome6.Fonts` 2.5.1) | Control `fa:FontAwesome` di XAML datang dari paket itu, dan **belum ada paket serupa untuk FA7**. |

WPF tetap di 6 sampai ada library FA7 untuknya. Akibat yang perlu diterima sementara ini:
bentuk sebagian ikon bisa sedikit berbeda antara dua client, dan ikon yang baru ada di 7
tidak punya padanan di WPF. Codepoint ikon lama stabil antar versi mayor, jadi tabel di
bawah tetap berlaku untuk keduanya.

Hasil penelusuran paket yang dipakai WPF:

- **Paket WPF-nya tidak bisa dipakai MAUI.** `lib/` hanya berisi net462, net472,
  netcoreapp3.1, net5.0/6.0(-windows7.0), netstandard1.4/2.1, dan uap10.0 — **tidak ada
  target Android**. Control `FontAwesome6.Fonts.FontAwesome` yang dipakai XAML WPF memang
  WPF-only.
- **File TTF FA6 ikut terbawa di paket itu**, di
  `lib/uap10.0/FontAwesome6.UWP/Fonts/fa-solid-900.ttf` (plus `fa-regular-400`,
  `fa-brands-400`). Tidak dipakai — MAUI sudah di FA7 — tapi dicatat supaya tidak ada yang
  mencarinya lagi.
- **`FontAwesome6.Core.dll` netral framework** (netstandard2.1/net6.0) dan memuat enum
  `EFontAwesomeIcon` (2017 nama) — secara teknis MAUI boleh mereferensinya.
- **Tapi Core tidak memuat codepoint-nya.** Nilai enumnya indeks berurutan, bukan Unicode
  (`Solid_MagnifyingGlass` = 1148, padahal glyph-nya U+F002). Peta codepoint ada di
  assembly WPF-only `FontAwesome6.Fonts.Net.dll` → `FontAwesome6.FontAwesomeUnicodes.Data`,
  berbentuk `Dictionary<string, Tuple<string,string>>` dengan kunci nama PascalCase tanpa
  awalan style (`MagnifyingGlass`), `Item1` = karakter glyph, `Item2` = slug.

→ Artinya MAUI tetap butuh tabel codepoint sendiri, mau mereferensi `FontAwesome6.Core`
atau tidak. Codepoint yang diperlukan plan ini sudah diambil dari peta di atas:

| Nama | Codepoint | Dipakai untuk |
| --- | --- | --- |
| `ArrowLeft` | `U+F060` | Back |
| `House` | `U+F015` | Home |
| `RotateRight` | `U+F2F9` | Reload |
| `ArrowRight` | `U+F061` | Forward (disembunyikan, tetap ada) |
| `User` | `U+F007` | Badge & header akun |
| `MagnifyingGlass` | `U+F002` | Search box home |
| `ChevronDown` / `ChevronRight` | `U+F078` / `U+F054` | Grup collapsible |
| `Gear` / `Plug` | `U+F013` / `U+F1E6` | Cadangan — dipakai kalau ada entri statis baru |
| `RightToBracket` | `U+F2F6` | Simulate Login |
| `RightFromBracket` | `U+F2F5` | Logout |
| `Envelope` | `U+F0E0` | Baris email header |
| `Xmark` | `U+F00D` | Tutup panel |
| `Server` | `U+F233` | Card server |
| `Moon` / `Sun` | `U+F186` / `U+F185` | Ganti tema |
| `TableCellsLarge` | `U+F009` | Pengganti `Apps` |
| `WindowMaximize` | `U+F2D0` | Pengganti `Window` |

### 2.8 Seam `UiIconType` sudah di tempat yang benar

`UiIconType` tinggal di `Em.Libs/Shared/UiIconType.cs` — dipakai bersama frontend dan
backend — sementara pemetaannya ke gambar konkret ada di lapisan tampilan
(`Em.Ui.Wpf.Core/Converters/UiIconConverter.cs`, 25 token). Jadi MAUI tidak perlu
menyentuh enum-nya sama sekali; ia cukup punya converter sendiri yang memetakan token yang
sama ke glyph FontAwesome. Ini kebetulan yang menguntungkan: seam-nya sudah dirancang
persis untuk keadaan ini.

Converter MAUI itu **belum ada** dan bukan bagian Tahap A–D; baru diperlukan saat ada layar
MAUI yang menggambar ikon dari data. Cukup dicatat supaya tidak ada yang menyalin
`UiIconConverter` WPF apa adanya (ia `IValueConverter` WPF dan memakai `EFontAwesomeIcon`).

### 2.9 Token warna yang dibutuhkan sudah lengkap

`Styles/Palette.xaml` sudah punya `errorLight`/`errorDark` untuk logout (drawer sekarang
memang sudah memakainya), plus set `surfaceContainer*`, `outlineVariant*`, `scrimColor`
yang diperlukan card dan divider panel. Tidak ada token baru yang perlu ditambah.

### 2.10 `RememberedProfileName` itu load-bearing — sesi dititipkan per nama profil

Ditemukan saat memeriksa akibat 1.7, dan inilah yang memaksa bentuk "satu slot profil" di
sana. Jalur tetap-masuk menuntut nama profil di **tiga** tempat berurutan:

1. `TryRestoreRememberedSessionAsync` (`Core/EmApp.cs:371`) menyerah kalau
   `RememberedProfileName` kosong.
2. `TryRestoreSessionAsync` (`:747`) menyerah kalau tidak ada koneksi ber-`ProfileName` itu
   di `UIConnections` — dan `UIConnections` hanya terisi dari `RetrieveApiConnections`,
   yaitu koneksi debug plus profil tersimpan.
3. `SessionStorage.Load(profileName)` (`:752`) mencari sesi tersimpan **berdasarkan nama
   profil**, bukan URL.

→ Kalau di release tidak ada satu profil pun, ketiganya gagal berturut-turut dan
"Remember Me otomatis, keluar hanya lewat logout" (1.5) tidak pernah jalan: tiap buka
aplikasi user mengetik password lagi. Karena itu 1.7 tetap menyimpan satu profil.

→ Konsekuensi yang menguntungkan: `SessionStorage`, `TryRestoreSessionAsync`, dan
`TryRestoreRememberedSessionAsync` **tidak perlu disentuh sama sekali**. Tidak ada
perubahan Core di luar lingkup file plan ini.

## 3. Tahapan kerja

Urutannya mengikat: Tahap A membongkar drawer, dan Tahap B mengisi tempat yang ditinggalkannya.

### Tahap A — Toolbar & pembuangan drawer

`SpaNavigationHost.xaml` + `.xaml.cs`, `Styles/Navigation.xaml`

1. App bar dirombak jadi `Back, Home | Judul | Reload, Badge` sesuai 1.3.
2. `LeadingCommand` jadi murni back; sambungkan `IsBackVisible`/`IsHomeVisible`/
   `IsUserVisible` (lihat 2.1). Tambah `HomeCommand` + `HomeCommandAllowed` dan
   `AccountPanelCommand` + `AccountPanelCommandAllowed` — pakai
   `RegisterCommand(nameof(…), …, …Allowed)` seperti command yang sudah ada.
3. Tombol forward disembunyikan, `CanGoForward` & `IsForwardVisible` dipertahankan.
4. Hapus drawer: elemen `Drawer`, `drawerItemTemplate`, kelas `DrawerItemVm`,
   `RebuildMenu()`, `OpenDrawerItemCommand`, `CloseDrawerCommand`, properti `IsDrawerOpen`,
   dan `PrimaryItems`/`ModuleItems`/`IsModuleListEmpty`.
5. `OnBackButtonPressed`: cabang `IsDrawerOpen` diganti cabang panel account (tutup panel
   dulu kalau terbuka, baru mundur satu layar).
6. `Styles/Navigation.xaml`: `navigationDrawerStyle`/`drawerHeaderStyle`/`drawerItemStyle`/
   `drawerActionItemStyle`/`drawerItemLabelStyle`/`drawerItemIconStyle` dialihkan jadi
   style panel account. `scrimStyle` dipakai ulang apa adanya.

Selesai Tahap A aplikasi masih bisa jalan: badge sudah ada tapi belum membuka apa-apa.

### Tahap B — Panel account

`Navigations/AccountPanel.xaml` + `.xaml.cs` (baru), dipasang sebagai overlay oleh host.

1. Struktur sesuai tabel 1.1: `Grid RowDefinitions="*,Auto"` — baris 0 `ScrollView`
   (header, static navigation, server), baris 1 logout yang tidak ikut scroll.
2. Animasi masuk dari kanan: salin `AnimateDrawerAsync` di `SpaNavigationHost.xaml.cs:247`
   dengan arah `TranslationX` dibalik (`+lebar` → `0`), durasi & scrim dipertahankan.
3. Header akun sesuai 1.1, dibaca dari `EmApp.ActiveUser`.
4. Card static navigation sesuai 1.2 — **dibangun ulang tiap panel dibuka** (lihat 2.2).
   Untuk sekarang isinya **Simulate Login saja**, dan itu pun hanya saat `IsDebugMode`
   (lihat 2.3 dan 1.8). Di release card ini tidak punya satu baris pun sampai ada navigasi
   MAUI yang perlu dibatasi claim — jangan dirender sebagai card kosong.
5. Card server sesuai 1.7: baris status koneksi, lalu radio koneksi. Radio-nya mengikuti
   pola WPF (`DefaultHomeControl.xaml:471` `connectionItemStyle`) — radio jadi template
   item di dalam list supaya seluruh baris jadi target tap, bukan cuma titiknya. Sumbernya
   `RetrieveApiConnections` apa adanya: koneksi debug saat debug, tepat satu baris saat
   release. **Tidak ada gestur tambah/hapus profil.**
6. Logout: panel tetap di bawah, `errorLight`/`errorDark`.
7. Ganti tema dipindah ke sini dari drawer; sambungkan `IsColorThemeVisible` (2.1).

### Tahap C — Home: search box & pohon navigasi

`DefaultHomeControl.xaml` + `.xaml.cs`

1. Buang kartu server dan kartu "SIGNED IN AS" — keduanya pindah ke panel account.
2. Search box di atas, ikon kaca pembesar di kiri — **bentuknya saja, dekoratif**, belum
   tersambung ke apa pun (lihat 2.4).
3. Port pohon `MenuPath` dari WPF (2.5): `MenuGroup`, `SplitMenuPath`, `ResolveGroup`,
   `RebuildMenuTree`, plus `RootAppMenus` untuk navigasi tanpa `MenuPath`.
4. Grup collapsible dirakit tanpa `Expander`; template grup memanggil dirinya sendiri
   untuk sub-grup supaya kedalamannya dinamis, sama seperti `navGroupBoundStyle` WPF.

### Tahap D — Layar login

`LoginControl.xaml` + `.xaml.cs`

1. `Picker` koneksi diganti input **API Server** sesuai 1.5 — entry teks saat release,
   terkunci (read-only) saat debug/Simulate Login ke koneksi yang sedang terpilih.
2. Checkbox Remember Me dibuang; `RememberMe` selalu `true`.
   `EmApp.RememberSignIn` **tetap ada**, hanya selalu diisi `true`.
3. `SignInCommandAllowed` disesuaikan: syaratnya bukan lagi `SelectedConnection is not null`
   melainkan URL server yang terisi.
4. Sesudah sign in berhasil, saat **bukan** debug, URL-nya disimpan sebagai **satu slot
   profil** lewat `AddApiConnection`, dengan nama profil diturunkan otomatis dari host URL
   — dan profil lama yang namanya berbeda **dihapus lebih dulu**, supaya yang tersimpan
   tidak pernah lebih dari satu (1.7). Ditulis di blok yang sama dengan
   `RememberedUserName`/`RememberedProfileName` (`LoginControl.xaml.cs`, di dalam
   `SignInCommand` sesudah `BeginSessionAsync`). `RememberedProfileName` wajib ikut diisi
   nama itu — tanpa dia sesi tidak bisa dipulihkan sama sekali (2.10).
5. Jalur Simulate Login dari panel account mendarat di sini dengan URL sudah terisi dan
   terkunci; yang diketik user tinggal nama akun dan password.

### Tahap E — Pindah ke FontAwesome, sekaligus rename `MaterialIcon` → `FontIcon`

Ini menggantikan seluruh ikon Material yang ada sekarang, bukan menambahinya. Sebaiknya
dikerjakan **lebih dulu** dari Tahap A–D kalau ingin menghindari menulis XAML dua kali:
setiap `controls:MaterialIcon` di Tahap A–D akan ikut berubah bentuk.

1. **Sudah dikerjakan.** Tiga file OTF Font Awesome 7 ada di
   `Em.Ui.Maui.Core/Resources/Fonts/` dan ter-glob sebagai `MauiFont` di csproj library,
   jadi ikut terbawa ke host yang mereferensinya. Build MAUI sudah lewat.
   Sisanya: `fonts.AddFont("Font Awesome 7 Free-Solid-900.otf", "FontAwesomeSolid")`
   didaftarkan **di `EmApp.Run<TApp>`** (`Core/EmApp.Statics.cs:81-93`), sesudah
   `UseMauiApp<TApp>()` dan **sebelum** `configure?.Invoke(mauiBuilder)` — bukan di
   `MauiProgram.cs`. Font-nya ikut di library, jadi library pula yang mendaftarkannya:
   host baru tidak bisa lupa, dan tidak ada ikon yang muncul sebagai kotak kosong gara-gara
   satu baris yang terlewat. `MauiProgram.cs` karena itu **tidak berubah sama sekali** —
   `OpenSans-Regular`/`OpenSans-Semibold` di sana tetap milik host.
   `Free-Regular-400` dan `Brands-Regular-400` baru didaftarkan kalau benar-benar terpakai.
2. `Controls/MaterialIcons.cs` → **`Controls/FontIcons.cs`**, kelas `FontIcons`: 14 path
   data diganti tabel konstanta glyph FontAwesome sesuai tabel codepoint di 2.7.
3. `Controls/MaterialIcon.cs` → **`Controls/FontIcon.cs`**, kelas `FontIcon`: berubah dari
   penggambar `Path` jadi penggambar glyph font. API-nya hampir utuh: `Glyph` tetap
   `string` (isinya glyph, bukan path data), `IconSize` tetap, `TintColor` dipetakan ke
   warna teks. Yang hilang: `MaterialIcons.CanvasSize` 24 sebagai nilai bawaan `IconSize`
   — ganti konstanta sendiri.
4. Sisir pemakainya: `Styles/Navigation.xaml` (`drawerItemIconStyle` yang beralih jadi
   style ikon panel account; `TargetType`-nya ikut berubah), `SpaNavigationHost`,
   `DefaultHomeControl`, `LoginControl`, dan `Controls/IconButton.cs` (field `_icon`,
   nilai bawaan `IconSize`, serta `<see cref>` di XML doc-nya).

Nama `Material*` sengaja tidak dipertahankan: isinya glyph FontAwesome, jadi namanya akan
menyesatkan pembaca berikutnya. Diganti **sekarang** selagi pemakainya masih sedikit dan
Tahap A–D toh akan menulis ulang XAML yang sama.

## 4. Keputusan yang tadinya terbuka — **semua sudah ditutup (2026-09-19)**

Tidak ada lagi yang memblokir; seluruh isinya sudah dipindah ke bagian 1–3. Dicatat di sini
supaya alasannya tidak hilang, dan supaya yang pernah membaca versi lama plan ini tahu
bagian mana yang berubah.

1. **Connection Config di MAUI** → **tidak diadakan** (ditulis di 1.8). Satu-satunya pintu
   koneksi adalah `AddDebugConnection` di `MauiProgram.cs:28`, di dalam `#if DEBUG` —
   daftar koneksi ditentukan saat compile dan tidak pernah bertambah dari perbuatan user,
   jadi tidak ada profil yang bisa ditambah atau dihapus lewat UI. Tidak ada gestur hapus
   di baris radio, dan folder `Dialogs` tetap tidak dibuat.

2. **Riwayat server** → **tidak ada daftar yang menumpuk**, tapi tetap ada **satu slot
   profil yang selalu ditimpa** (1.7). Bentuk ini bukan pilihan gaya: penyimpanan sesi
   dikunci ke nama profil, jadi tanpa satu profil pun "tetap masuk" di release tidak
   pernah jalan — lihat **2.10**.

3. **Card server saat release** → baris status + tepat satu baris radio (server yang
   sedang dipakai). Lihat 1.1 dan Tahap B langkah 5.

4. **Search box home** → **dekoratif dulu** (ditulis di 2.4). Bentuknya ikut WPF, belum
   tersambung ke apa pun; filternya dikerjakan nanti saat sudah ada banyak module.

5. **Typo `IsDebugMode` poin 12** → **tidak perlu dipastikan lagi.** Dengan URL yang
   terkunci saat debug (1.5), tidak ada cabang perilaku yang bergantung pada bacaan poin
   itu.

6. **Lokasi `AccountPanel`** → tetap `Navigations/`, sesuai asumsi plan.

7. **Nama profil hasil 1.7** → **gugur.** Tidak ada profil yang dibuat sama sekali, jadi
   tidak ada nama yang perlu diketik maupun diturunkan.

8. **Nama `MaterialIcon`/`MaterialIcons`** → **diganti jadi `FontIcon`/`FontIcons`
   sekarang**, sebagai bagian Tahap E. Alasannya ditulis di akhir tahap itu.

9. **Apakah MAUI mereferensi `FontAwesome6.Core`?** → **tidak.** Cukup konstanta bernama
   sendiri di `FontIcons`: Core hanya memberi nama enum, sementara codepoint-nya tetap
   harus ditabelkan sendiri (2.7). Gampang dibalik kalau ternyata 2017 nama itu berguna.

10. **Pendaftaran font Font Awesome** → **di library, bukan di host.** Didaftarkan di
    `EmApp.Run<TApp>` sebelum callback host dijalankan, supaya host baru tidak bisa lupa
    dan ikon tidak pernah muncul sebagai kotak kosong. `MauiProgram.cs` tidak berubah.
    Ditulis di Tahap E langkah 1.

11. **Cara eksekusi** → **per tahap, berhenti tiap selesai.** Urutannya E → A → B → C → D.
    Tiap tahap di-build lalu berhenti, supaya hasilnya sempat dijalankan di perangkat dan
    bentuk UI-nya dikoreksi sebelum tahap berikutnya menumpuk di atasnya. Alasannya:
    layar Android tidak bisa diperiksa dari sisi yang mengerjakan.
