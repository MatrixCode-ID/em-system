# Eksekusi Navigasi 2/3 dan 3/3 — entri per stack dan detach window

Status: **kedua plan selesai dan di-commit; checklist WPF layout SPA (plan 2 dan 3) sudah diuji user
dan lolos (2026-09-25); regresi WPF MultiTab dan uji emulator MAUI belum dijalankan**
Dibuat: 2026-09-24
Baseline: commit `9554679` (Update navigation roadmap), branch `spa-detach`
Plan sumber:
- `plan/executed/navigasi-2-entri-per-host.md` — commit `09c0a04`
- `plan/executed/navigasi-3-detach-window.md` — commit sesudahnya, yang juga memuat laporan ini

Keduanya dijalankan dalam satu kali jalan sesuai bagian "Cara eksekusi" di plan 2.

---

## 1. Hasil build

Baseline diukur dengan rebuild penuh (`--no-incremental`) di worktree terpisah pada commit `9554679`,
supaya warning lama ikut terhitung.

| Solution | Baseline | Sesudah plan 2 | Sesudah plan 3 |
| --- | --- | --- | --- |
| `src/frontend/Em.Ui.Wpf.slnx` | 0 error, 0 warning | 0 error, 0 warning | 0 error, 0 warning |
| `src/frontend/Em.Ui.Maui.slnx` | 0 error, 0 warning | 0 error, 0 warning | 0 error, 0 warning |

Semua angka "sesudah" juga berasal dari rebuild penuh.

Grep `Detach` (tidak peka huruf besar/kecil) di `src/shared/Em.Ui.Maui.Core` dan
`src/shared/Em.Ui.Core`: **tidak ada hasil**, baik sesudah plan 2 maupun sesudah plan 3.

Commit plan 3 tidak menyentuh `src/shared/Em.Ui.Maui.Core`, `src/shared/Em.Ui.Core`, maupun
proyek MAUI di `src/frontend`. Seluruh kodenya ada di `src/shared/Em.Ui.Wpf.Core`, ditambah
dokumentasi.

---

## 2. Yang dikerjakan

### Plan 2 — entri per stack, Title sebagai kunci

- **Kontrak bersama** (`Em.Ui.Core/Ui.Core/shared/`):
  - baru: `NavigationKind`, `INavigationEntry`, `INavigationStack`;
  - `INavigation` tinggal definisi dan mendapat `Kind`;
  - `INavigationHost` menjadi router (`MainStack`, `FindEntry`, dua `NavigateTo`);
  - `NavigationEventArgs.Entry` dan `NavigationPayloadBase.Title` ditambahkan.
- **Kedua core** (`Em.Ui.Wpf.Core` dan `Em.Ui.Maui.Core`) mendapat struktur yang sama:
  - `Navigation` tanpa body/data/stack;
  - `NavigationEntry` dan `NavigationStack` yang baru;
  - router di `EmApp.NavigationHost.cs`;
  - `BodyType.Create(entry)` dan `MvvmModelBase.NavigationEntry`;
  - `SpaNavigationHost` yang terikat ke sebuah stack.
- **Pemakai:**
  - `UserManager` membuka editor lewat `NavigationEntry.NavigateTo`;
  - `UserEditorNavigationPayload.Title` diisi, dan `UserEditor` memanggil `SetTitle` sesudah user
    baru tersimpan;
  - `UserControl1` (Sample) memakai entrinya sendiri;
  - `Kind` diisi di semua navigasi internal dan navigasi Sample.
- **Langkah persiapan:**
  - `UseHomeNavigation` menggantikan `UseSinglePageLayout(Navigation)`;
  - `admin.users` dan `admin.roles` diikat ke claim User/Role Manager Access;
  - item statis home disaring `CanOpen`;
  - `OnThemeChanged` ikut menggambar ulang brand login SPA.
- **`Program.cs`:** `builder.UseSinglePageLayout()` aktif.
- **Dokumentasi:** bagian "Navigation host" di `src/frontend/CLAUDE.md`.

### Plan 3 — detach window (WPF saja)

- `Windows/DetachedWindow.xaml(.cs)`: window detach beserta `DetachedWindowVm`.
- `Core/EmApp.DetachedWindows.cs`: daftar window detach, `CanDetach`, `DetachAsync`, `WindowOf`,
  memunculkan window, konfirmasi penutupan, dan penutupan paksa.
- Router: `FindEntry` mencari di semua stack; aturan Manager → stack utama; window pemilik Title yang
  sudah terbuka dimunculkan.
- `NavigationStack` (WPF): `Extract`, `Adopt`, `AskCurrentToLeave`, `ReleaseAll`, dan `MoveTo` tanpa
  bertanya ke body sumber. `NavigationEntry.Stack` kini bisa berganti.
- `SpaNavigationHost`: command Detach diisi; di window detach tombol Home dan akun disembunyikan.
- `MainWindow`: konfirmasi window detach saat window utama ditutup, dan penutupan paksa saat sesi
  berakhir.
- Home dan login memasang `IsDetachVisible = false`.
- **Dokumentasi:**
  - `CLAUDE.md` root, bagian "Keep the UI cores in step": detach dicatat sebagai pengecualian yang
    disengaja;
  - `src/frontend/CLAUDE.md`: bagian baru "Detach windows".

---

## 3. Percabangan `ApplicationLayout` baru (untuk plan 1)

Daftar ini yang perlu dibuang saat MultiTab dihapus nanti.

| # | Tempat | Isi |
| --- | --- | --- |
| 1 | `Core/EmApp.NavigationHost.cs`, getter `MainStack` | melempar `NotSupportedException` di MultiTab. Menggantikan percabangan lama di `HomeNavigation`/`NavigateHome`, dan lewat getter ini `FindEntry` serta `NavigateTo` ikut melempar. |
| 2 | `Core/EmApp.DetachedWindows.cs`, `CanDetach` | `if (ApplicationLayout != ApplicationLayout.SinglePage) return false;` |

Yang **bukan** percabangan baru:
- stack utama dibuat di cabang `SinglePage` yang sudah ada di `InitBuilder`;
- penutupan window detach di handler `SessionEnded` dan `MainWindow.OnClosing` berjalan di kedua
  layout, dan di MultiTab daftarnya selalu kosong.

---

## 4. Penyimpangan dan tambahan terhadap plan

Plan 2:

1. **`NavigateHome` tetap me-reload home.** Plan tidak menyebutnya secara eksplisit. Jalur sesudah
   login di WPF (`GoHomeAfterSignInAsync`) hanya memanggil `NavigateHome` dan selama ini bergantung
   pada reload itu untuk menggambar menu milik user yang baru masuk. Aturan "kunjungan ulang tanpa
   reload" tetap berlaku untuk `NavigateTo` ke Title yang sudah ada. Akibatnya, tempat yang memanggil
   `Home.Reload()` sesudah `NavigateHome` me-reload dua kali. Ini sama dengan sebelumnya dan tidak
   berbahaya.
2. **`ISpaShell` di MAUI dihapus.** Host kini mengikuti event stack (`PropertyChanged(Current)` dan
   `Changed`), sama seperti host WPF, jadi `EmApp` tidak lagi mendorong perubahan ke halaman.
   Kontrak itu tidak punya pemakai lain.
3. **`ChangePasswordControl` (MAUI)** memanggil `app.Backward()`, member yang dihapus. Sekarang
   `NavigationEntry.Stack.Backward()`. Pemakai ini tidak tercantum di plan.
4. **Member tambahan di kelas konkret kedua core, tidak di kontrak bersama:**
   - `NavigationStack.CanGoBack`, `CanGoForward`, dan event `Changed`;
   - `NavigationStack.FindEntry` dan `IndexOf` (internal).
5. **Perbandingan Title tidak peka huruf besar/kecil** (`OrdinalIgnoreCase`). "INV-001" dan "inv-001"
   menunjuk dokumen yang sama.
6. **`args.Entry` pada `OnNavigatingAway` adalah entri yang ditinggalkan**, yaitu entri milik body
   penerima callback. Pada callback lain, `args.Entry` adalah entri tujuan. Aturannya: selalu entri
   milik body yang menerima callback.
7. **`Close()` pada entri yang tidak sedang tampil tetap bertanya** `OnNavigatingAway` ke body-nya.
   Ini mengikuti kalimat plan, yang memperbolehkan body menolak.
8. **`Kind` Change Password (MAUI) = Editor**, sesuai plan.

Plan 3:

9. **`MvvmModelBase.DialogOwner` (baru).** `AlertError`, `DiaplayException`, dua dialog di
   `RoleManager`, dan pertanyaan unsaved changes di `UserEditor` sekarang memakai window yang sedang
   menampilkan entrinya. Tanpa ini, pertanyaan dari body di window detach muncul di window utama,
   sehingga skenario 8 dan 14 tidak bisa dijalankan dengan benar.
10. **Sign out di mode debug ikut menutup semua window detach.** Mode debug tidak punya sesi, jadi
    tidak ada `SessionEnded` yang menutupnya. Build debug aplikasi memang berjalan di mode debug.
11. **Penempatan window.** Posisi bergeser 30 DIP ke kanan-bawah, dan terus bergeser selama tempatnya
    sudah ditempati window detach lain. Kalau window yang bergeser keluar dari work area:
    - ukurannya dikecilkan lebih dulu, paling kecil sampai `MinWidth`/`MinHeight` (600×400);
    - baru sesudah itu posisinya ditarik ke dalam.

    Jadi "ukuran sama dengan window asal" hanya berlaku selama masih muat. Work area dibaca per
    monitor lewat Win32 (`MonitorFromWindow`/`GetMonitorInfo`). Di monitor dengan DPI berbeda,
    posisinya bisa sedikit meleset.
12. **Tombol Detach pada akar window detach** tetap terlihat tetapi nonaktif, mengikuti kalimat plan
    "aktif mengikuti aturan ini". Pada home, tombolnya disembunyikan.
13. `EmApp.DetachedWindows`, `CanDetach`, `DetachAsync`, dan `WindowOf` dibuat publik (WPF saja).

---

## 5. Checklist uji untuk user

### WPF, layout SPA (`UseSinglePageLayout()` aktif di `Program.cs`)

Plan 2:
- [x] `UseHomeNavigation(customHome)` + `UseSinglePageLayout()` → home kustom yang tampil; tanpa
      `UseHomeNavigation` → `DefaultHomeControl`.
- [x] Login → home → User Manager → Edit ani → Back → Edit joni: ada dua entri editor, dan
      Back/Forward di antara keduanya bekerja.
- [x] Klik lagi "Edit ani" saat entrinya masih di stack → posisi pindah tanpa reload, isian
      dipertahankan.
- [x] "Create New User" dua kali → hanya satu entri; simpan → Title menjadi "Edit User: <akun>".
- [x] Home membersihkan stack dan melepas semua body (`OnRelease` terpanggil).
- [x] Logout → login dengan stack bersih.
- [x] Ganti tema di layar login → panel brand ikut berganti.
- [x] Akun non-admin tanpa claim User/Role Manager tidak melihat kedua tool di home, dan
      `NavigateTo("admin.users")` mengembalikan `false`.

Plan 3 (akun dengan claim yang sesuai):
- [x] 1. Invoice INV-001 → detach (Window B), INV-002 → detach (Window C). Keduanya tampil
      berdampingan, dan judul window sesuai Title.
- [x] 2. Dari Window A, buka INV-001 lagi → Window B dimunculkan, tidak ada duplikat.
- [x] 3. User Manager → detach (Window D). Di D, edit ani → masuk stack D; Back di D kembali ke User
      Manager.
- [x] 4. Di D, detach "Edit User: ani" → Window E berisi editor saja.
- [x] 5. Dari body di D, `NavigateTo` ke Role Manager → terbuka di Window A, dan Window A dimunculkan.
- [x] 6. Klik User Manager dari menu home Window A → Window D dimunculkan.
- [x] 7. Isian editor yang belum disimpan tetap ada setelah detach.
- [x] 8. Tutup Window D saat editor ani punya perubahan → penolakan `OnNavigatingAway` membatalkan
      penutupan.
- [x] 9. Logout dari Window A → semua window detach tertutup, dan `OnRelease` terpanggil untuk semua
      body.
- [x] 10. Home dan layar login tidak bisa di-detach; akar window detach juga tidak.
- [x] 11. Window detach baru berukuran sama dengan window asal, bergeser sedikit ke kanan-bawah, dan
      `WindowState`-nya Normal, termasuk saat window asal maximized. Detach dua kali berturut-turut
      dari window yang sama tidak menumpuk persis.
- [x] 12. Ganti tema dari toolbar Window E → semua window (A–E) ikut berganti tema.
- [x] 13. Body akar Window E memanggil `entry.Close()` → Window E tertutup sendiri.
- [x] 14. Tutup Window A saat editor di Window D punya perubahan → Window D dimunculkan dan ditanya.
      Kalau menolak, aplikasi tetap jalan dan semua window utuh. Kalau setuju, semua window
      tertutup, `OnRelease` terpanggil, dan aplikasi berhenti.

Catatan: belum ada navigasi Invoice di repo. Skenario 1–2 bisa diganti dengan dua entri "Edit User"
atau dua entri Sample Editor yang Title-nya berbeda.

### WPF, layout MultiTab (`UseSinglePageLayout()` dikomentari sementara)

Perilakunya harus sama dengan sebelum kedua plan:
- [ ] Login lewat `loginHost` → workspace tab.
- [ ] Tombol "Home" di toolbar menambah tab tes; tab bisa ditutup dan ditarik keluar ke
      `TabHostWindow`.
- [ ] Ganti tema dari toolbar, Connection Config, logout → kembali ke login.
- [ ] Mode debug langsung masuk workspace, dengan Simulate Login tersedia.

### MAUI (emulator)

- [ ] Login → home → navigasi → back hardware Android.
- [ ] Forward, Reload.
- [ ] Logout.
- [ ] Change Password dari AccountPanel; sesudah berhasil, layar kembali ke layar sebelumnya.
