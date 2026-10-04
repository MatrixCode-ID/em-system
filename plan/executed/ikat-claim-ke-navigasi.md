# Plan — Ikat claim ke navigasi (menu dan penjagaan NavigateTo)

Status: **sudah dieksekusi** — commit `e2e0780` (Bind navigation access to claims). Tiga overload
`AddNavigation` ada di `EmAppBuilder` dan `NavigationAccess.CanOpen` dipakai oleh menu home
maupun `NavigateTo`, di host WPF dan host MAUI. Matriks uji di bagian akhir belum dijalankan dengan
akun non-admin sungguhan; itu dilacak sebagai item tersendiri di
[doc/TODO-LIST.md](../../doc/TODO-LIST.md) ("Uji gerbang claim dengan akun non-admin sungguhan").
Dibuat: 2026-09-22 — hasil diskusi dengan user; semua keputusan di bawah sudah disetujui kecuali
yang ditandai "usulan".

---

## Tujuan

Di luar mode debug, sesudah user masuk dan sebelum menu utama digambar, tiap navigasi dicek terhadap
claim user:

- navigasi yang tidak boleh dibuka user **tidak tampil** di menu home, dan
- **tidak bisa dibuka** lewat `NavigateTo`, walau dipanggil langsung dengan namanya.

Mode debug melewati seluruh pengecekan ini: semuanya tampil dan semuanya terbuka.

## Keputusan yang sudah diambil

1. **Ikatan dideklarasikan module, bukan ditebak lewat reflection.** `EmAppBuilder` mendapat tiga
   overload `AddNavigation`; module wajib memilih satu, jadi lupa mengikat tidak lagi berakhir sebagai
   "tampil untuk semua" tanpa disadari.

   | Overload | Artinya |
   | --- | --- |
   | `AddNavigation<T>(Navigation)` | terikat module `T`: user boleh kalau punya **claim apa pun** di module itu |
   | `AddNavigation<T>(Navigation, string claimName)` | user harus punya **claim persis itu** (module dari `T`) |
   | `AddNavigation(Navigation)` | tanpa ikatan: terbuka untuk siapa pun yang sudah masuk |

2. **`T` adalah class service** (mis. `SampleServices`), bukan interface-nya, dan **wajib bertanda
   `[Module]`**. Dua overload berikat sama-sama lewat `ClaimAction.Create<T>`, jadi tipe tanpa
   `[Module]` atau nama claim kosong / berisi `:` gagal saat registrasi, bukan saat dipakai.
3. **`builder.Navigations` ditutup** (tidak lagi `public`) supaya tidak ada jalan pintas yang
   melewati pilihan tiga overload tadi.
4. **Claim yang belum dideklarasikan di backend berarti navigasi tidak tampil.** Itu kesalahan dev
   module, bukan kasus yang ditangani secara khusus.
5. **Admin** (`cUserIsAdmin`) selalu lolos, sama seperti `ClaimCollection`. Akun debugger dan admin
   bawaan sudah admin.
6. **Ditolak = `false`**, tanpa dialog, sama dengan kontrak "perpindahan dibatalkan" yang sudah ada.
7. **Di luar cakupan:** tool statis di home (Connection Config, User Manager, Role Manager),
   penegakan di sisi server, dan modul MAUI (ditunda). Lihat bagian terakhir.

## Aturan akses

Satu fungsi murni, dipakai oleh menu dan oleh `NavigateTo`, supaya "yang disembunyikan" dan "yang
ditolak" tidak pernah berbeda.

Urutannya mengikat:

1. Mode debug → boleh.
2. Navigasi tanpa ikatan → boleh.
3. Tidak ada user aktif → tidak.
4. User admin → boleh.
5. Ada claim yang dipersyaratkan → boleh hanya kalau `AvailableClaims` memuat kuncinya
   (`OrdinalIgnoreCase`).
6. Terikat module saja → boleh kalau `AvailableClaims` memuat claim apa pun dengan `ModuleName` itu.

Fungsi ini **tidak** memakai indexer `ClaimCollection`: indexer itu melempar exception di DEBUG
untuk claim yang belum ada di katalog, dan menjawab per module dari `IServices`. Di sini kunci yang
tidak ada cukup berarti "tidak punya".

## Langkah

### 1. `Em.Ui.Core` — kontrak dan aturan (UI-agnostik)

- `INavigation` (`Ui.Core/shared/INavigation.cs`) mendapat dua anggota baca-saja:
  `string? ModuleName { get; }` dan `ClaimAction? RequiredClaim { get; }`.
- Berkas baru `Ui.Core/shared/NavigationAccess.cs`: `static bool CanOpen(INavigation navigation, User? user)`
  berisi langkah 2-6 di atas. Langkah 1 (debug) milik `EmApp`, karena `IsDebugMode` ada di sana.

Aturan ditaruh di sini, bukan disalin ke dua `EmApp`, supaya WPF dan MAUI memakai satu sumber.

### 2. `Em.Ui.Wpf.Core`

- `Core/Navigation.cs`: `ModuleName` dan `RequiredClaim` dengan setter `internal`, jadi hanya builder
  yang bisa mengisinya.
- `Shared/EmAppBuilder.cs`:
  - tiga overload `AddNavigation` (lihat tabel di atas). Overload berikat memanggil
    `ClaimAction.Create<T>` dulu; overload module-saja mengisi `ModuleName` dari
    `ModuleAttribute.ResolveName(typeof(T))`; overload claim mengisi `RequiredClaim` sekaligus
    `ModuleName`.
  - `Navigations` menjadi `internal`. Isinya tetap dipakai `InitBuilder` (`pars.Navigations.EachOf(app.AddNavigation)`).
- `Core/EmApp.cs`: `public bool CanOpen(Navigation navigation) => IsDebugMode || NavigationAccess.CanOpen(navigation, ActiveUser);`
- `Core/EmApp.NavigationHost.cs`: pengaman di **overload publik**
  `NavigateTo(Navigation targetNav, object? data)` — `if (!CanOpen(targetNav)) return Task.FromResult(false);`.
  `NavigateTo(string, ...)` dan `NavigateToRoot(...)` sudah lewat overload ini, jadi tidak perlu
  dijaga lagi.
  - **Yang sengaja tidak dijaga:** back/forward, `RemoveFromStack`, dan `NavigateHome` (memakai
    overload privat `reload: false`). Semuanya hanya masuk kembali ke navigasi yang sudah lolos
    pengecekan saat dibuka; claim dimuat sekali saat login dan stack dikosongkan setiap sesi
    berakhir, jadi tidak ada jalan bagi navigasi terlarang untuk lewat di sini.
- `Navigations/DefaultHomeControl.xaml.cs` (`RenderMainItems`): filter menjadi
  `.Where(r => r.IsMenuVisible && _app.CanOpen(r))`. Group di pohon menu yang tidak punya isi ikut
  hilang sendiri karena pohonnya dibangun dari item yang tersisa.

### 3. `Em.Ui.Maui.Core` — samakan strukturnya

Sesuai aturan "Keep the UI cores in step" di `CLAUDE.md` root, perubahan yang sama dipasang di MAUI:

- `Core/Navigation.cs`: dua properti yang sama.
- `Shared/EmAppBuilder.cs`: tiga overload `AddNavigation`, `Navigations` menjadi `internal`
  (`UseHomeNavigation` tidak berubah).
- `Core/EmApp.cs`: `CanOpen`.
- `Core/EmApp.NavigationHost.cs`: pengaman di `NavigateTo(Navigation, object?)`.
- `Navigations/DefaultHomeControl.xaml.cs` (`ReloadAsync`): filter `.Where(r => r.IsMenuVisible && app.CanOpen(r))`.
- `Core/EmApp.Statics.cs:54`: komentar parameter yang menyebut `Navigations.Add` diganti ke `AddNavigation`.

Modul MAUI sendiri masih ditunda, jadi tidak ada `Extensions.cs` MAUI yang perlu dimigrasi.

Cakupan sudah dicek (2026-09-22): core UI yang punya `EmApp`/`Navigation`/`EmAppBuilder` hanya dua,
`Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core`; `Em.Ui.Core` adalah lapisan bersama yang sudah
dikerjakan di langkah 1. Host `Em.Ui.Maui` tidak memanggil `Navigations` di mana pun, jadi
menutupnya tidak merusak apa-apa. Kalau nanti ada core UI ketiga, ia ikut aturan yang sama.

### 4. Module Sample frontend (`Em.Sample/Extensions.cs`)

- Hapus region `Stub` dan dua panggilan `builder.Navigations.Add`.
- `Constants.HomeNavigation` dan `Constants.SampleEditor` didaftarkan dengan `AddNavigation<SampleServices>(...)`
  (terikat module). `SampleEditor` tetap `IsMenuVisible = false`, jadi tidak muncul di menu tapi
  ikut dijaga di `NavigateTo`.
- `builder.AddMainControl(...)` tidak disentuh.

### 5. Claim untuk Sample di backend (supaya Sample tidak hilang untuk non-admin)

- Berkas baru `src/shared/Em.Models/Sample/SampleClaims.cs` berisi konstanta nama claim, dipakai
  backend (dan frontend kalau nanti ada overload berklaim), supaya nama tidak diketik dua kali.
- `src/backend/modules/8XX-NSM/Em.Sample/Extensions.cs`: `builder.AddClaims(ClaimAction.Create<SampleServices>(SampleClaims.Access));`
  (contohnya sudah ada sebagai komentar di berkas itu).
- **Usulan:** nama claim `"Access"`. Perlu konfirmasi sebelum dieksekusi; nama ini tampil di layar
  Role Manager.

### 6. Dokumentasi

- XML doc komentar untuk anggota `public` baru di `src/shared/*`, dalam Bahasa Indonesia dan tanpa
  menyebut nama objek database (aturan `CLAUDE.md` root).
- `src/frontend/CLAUDE.md`: bagian "Registering a navigation" dan bullet "Module shape" yang masih
  memakai `builder.Navigations.Add` diganti ke tiga overload `AddNavigation`, lengkap dengan aturan
  aksesnya dan penjagaan di `NavigateTo`.

## Verifikasi

Tidak ada proyek test, jadi lewat build dan uji manual.

Build: `src/frontend/Em.Ui.Wpf.slnx`, solution backend `Em.Api.slnx`, dan proyek yang memuat
`Em.Ui.Maui.Core`.

Uji manual (WPF, layout satu halaman):

1. **Mode debug:** semua tile tampil, `NavigateTo("sample.editor")` terbuka.
2. **Non-debug, user tanpa claim Sample:** tile Sample tidak ada; `NavigateTo("sample.manager")` dan
   `NavigateTo("sample.editor")` mengembalikan `false` dan layar tidak berpindah.
3. **Non-debug, user dengan claim Sample (lewat role):** tile tampil dan bisa dibuka.
4. **Non-debug, admin:** tampil dan bisa dibuka.
5. **Sign out lalu masuk sebagai user lain tanpa restart:** menu mengikuti user yang baru.
6. **Sesi dipulihkan otomatis** (opsi tetap masuk): menu benar sejak layar pertama.
7. **Navigasi internal** (login, home, User Manager, Role Manager) tidak terblokir.
8. **Overload berklaim:** dicoba sementara pada satu navigasi; hanya user yang punya klaim persis
   itu yang lolos, pemegang claim lain di module yang sama tidak.

## Di luar cakupan (dicatat supaya tidak hilang)

- **Tool statis di home.** `User Manager` dan `Role Manager` punya claim internal sendiri
  (`Administrative Tools:...`) dan bisa memakai overload berklaim; ikatannya butuh cara mendaftarkan
  navigasi internal dengan claim, karena mereka dibuat di `InitInternalNavigation`, bukan lewat
  builder. Dikerjakan terpisah.
- **Penegakan di server.** Menyembunyikan menu dan menolak `NavigateTo` hanya mengatur UI. Yang
  menjaga data tetap server.
- **Modul MAUI.** Struktur intinya disamakan di langkah 3, tapi migrasi modul ke `AddNavigation`
  menunggu modul MAUI dikerjakan.
- **Peringatan claim salah ketik di overload berklaim.** Nama yang tidak ada di katalog server
  hanya membuat navigasi tidak tampil, tanpa pesan. Konstanta bersama di `Em.Models` menutup
  sebagian besar risikonya; pesan diagnostik di DEBUG bisa ditambah nanti.
