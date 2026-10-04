# Plan — Navigasi 3/3: detach window (WPF saja)

Status: **sudah dieksekusi** (2026-09-24), langsung sesudah plan 2. Laporan:
[doc/report/navigasi-2-3-eksekusi.md](../../doc/report/navigasi-2-3-eksekusi.md). Dikerjakan setelah
[navigasi-2-entri-per-host.md](navigasi-2-entri-per-host.md).
Dibuat: 2026-09-23, hasil diskusi dengan user. Semua usulan dan celah sudah diputuskan user pada
2026-09-24.

Detach **hanya ada di WPF, dan hanya di layout SPA**. MAUI sengaja tidak punya fitur ini; itu
satu-satunya perbedaan struktur navigasi kedua core setelah plan 2, dan dicatat di `CLAUDE.md` root
sebagai pengecualian yang disengaja.

**Batas berkas plan ini:** semua perubahan hanya di `src/shared/Em.Ui.Wpf.Core` (plus dokumentasi).
`src/shared/Em.Ui.Maui.Core`, proyek MAUI di `src/frontend`, dan kontrak bersama
`src/shared/Em.Ui.Core` **tidak disentuh**. Kalau ternyata ada kebutuhan yang terasa harus masuk
ke kontrak bersama (mis. `FindEntry` lintas window, aturan Manager → stack utama, event window
detach), kebutuhan itu dipenuhi di kelas WPF (`EmApp`, `NavigationStack`, `DetachedWindow`), bukan
dengan menambah member ke interface. Kalau benar-benar tidak bisa, eksekusi berhenti dan user
ditanya dulu. Aturan "Keep the UI cores in step" di `CLAUDE.md` root tidak berlaku untuk plan ini,
karena detach adalah pengecualian yang disengaja.

**Cara eksekusi** (commit, verifikasi, penutup, `Program.cs`) diatur di bagian "Cara eksekusi"
[plan 2](navigasi-2-entri-per-host.md). Ringkasnya: plan ini langsung dijalankan setelah plan 2
dalam satu kali jalan, di-commit sendiri dengan pesan Bahasa Indonesia, Claude hanya memverifikasi
lewat build dan grep (skenario klik dijalankan user), lalu kedua plan dipindah ke `plan/executed/`
dan laporan eksekusi ditulis di `doc/report/`.

[navigasi-1-hapus-multitab.md](../unexecuted/navigasi-1-hapus-multitab.md) **ditunda** (keputusan user,
2026-09-24), jadi layout MultiTab masih ada saat plan ini dijalankan. Detach window **tidak
menggantikan dan tidak memakai** mekanisme "tarik tab keluar" milik MultiTab (`TabHostWindow`,
`TabWorkspaceHost`). Keduanya hidup terpisah, masing-masing di layout-nya sendiri (lihat bagian
"MultiTab").

---

## Tujuan

User bisa mengeluarkan layar yang sedang tampil ke window sendiri. Contoh utamanya: membuka Invoice
INV-001 lalu detach, membuka INV-002 lalu detach, kemudian membandingkan kedua window itu
berdampingan.

## Keputusan

1. **Window detach adalah host SPA tanpa Home dan tanpa menu aplikasi.** Entri yang di-detach
   menjadi akar stack window itu. Back paling jauh sampai akar.
2. **Isi window detach hanya bertambah lewat tombol/link di body-nya sendiri**
   (`entry.NavigateTo`). Contoh: Invoice → Sales Order masuk ke stack window detach itu.
3. **Satu Title hanya tampil di satu tempat** di semua window (aturan plan 2). Kalau Title yang
   diminta sudah ada di window lain, window itu yang dimunculkan (restore, lalu dibawa ke depan) dan
   posisi stack-nya dipindah ke entri tersebut. Contoh: Invoice Manager sudah di Window B, lalu
   diklik dari menu Window A → Window B yang muncul.
4. **Manager hanya hidup di window utama, atau sebagai akar window detach.** `NavigateTo` ke
   navigasi `NavigationKind.Manager` dari window detach dibuka di window utama (dan window utama
   dimunculkan), walaupun developer memanggilnya dari body di window detach. Kemungkinan di lapangan:
   window selain window utama hanya membuka editor.
5. **Detach bertingkat boleh.** Editor di window detach bisa di-detach lagi menjadi window baru.
   Karena aturan 4, detach tingkat kedua praktis selalu editor.
6. **Hanya di layout SPA.** Di layout MultiTab tidak ada `SpaNavigationHost`, tidak ada stack
   detach, dan tidak ada yang berubah.
7. **In-process dulu:** satu proses, satu UI thread. Kontraknya dijaga supaya bisa dipindah ke
   proses terpisah nanti (lihat bagian "Opsi proses terpisah").

## Aturan detach

- **Yang bisa di-detach:** entri yang sedang tampil (`Current`) di stack mana pun, kecuali Home,
  `admin.logon`, dan akar stack window detach (karena window-nya akan kosong). Tombol Detach di
  `SpaNavigationHost` aktif mengikuti aturan ini dan `Navigation.IsDetachVisible`. Home dan login
  memasang `IsDetachVisible = false`.
- **Langkah detach:**
  1. Entri dikeluarkan dari stack asal **tanpa** `OnNavigatingAway` dan tanpa `OnRelease`, karena
     ia hanya dipindah, bukan ditinggalkan. Stack asal pindah ke entri sebelumnya (atau Home) dengan
     `reload: false`, seperti `RemoveFromStack` sekarang.
  2. Window baru dibuat dengan `NavigationStack` baru (tanpa Home) berakar entri itu. Body-nya
     pindah ke window baru apa adanya, **isian yang belum disimpan ikut**.
  3. Judul window = `entry.Title`, dan ikut berubah saat `SetTitle`. Dengan begitu dua invoice
     mudah dibedakan di taskbar.
  4. **Posisi dan ukuran (keputusan user):** ukuran window baru sama dengan ukuran window asal
     (kalau window asal sedang maximized, pakai ukuran `RestoreBounds`-nya). Posisinya digeser
     sedikit dari window asal, ke kanan-bawah (bertingkat), sehingga detach berturut-turut dari
     window yang sama tidak saling menumpuk persis. Window baru selalu dibuka dengan
     `WindowState.Normal` dan dijaga tetap di dalam work area layar window asal.

  **Disetujui user:** body dipindah hidup-hidup (in-process), bukan dibangun ulang dari payload. Alasannya,
  payload yang ada sekarang belum bisa diserialisasi (`UserEditorNavigationPayload` membawa objek
  `User` dan callback `WhenUserCreated`), sehingga membangun ulang akan memblokir detach User Editor
  sampai payload-nya dirombak. Payload yang bisa diserialisasi baru menjadi syarat saat mode proses
  terpisah dibuat.
- **Toolbar window detach:**
  - ada: Title, Back/Forward (di dalam stack-nya), Reload, Detach, tema. Tombol tema mengganti tema
    **seluruh aplikasi** (semua window), sama seperti tombol tema di window utama (keputusan user);
  - tidak ada: Home dan tombol akun/sign out. Sesi dikelola window utama.
- **Menutup window detach** (tombol X):
  1. body yang sedang tampil mendapat `OnNavigatingAway`; kalau menolak, penutupan dibatalkan;
  2. semua entri stack-nya di-`OnRelease`, sama seperti `NavigateHome` melepas stack sekarang.
- **Stack window detach menjadi kosong** (keputusan user): kalau entri akar dikeluarkan dari
  stack-nya, misalnya body akar memanggil `entry.Close()` pada dirinya sendiri setelah simpan, window
  detach itu ikut ditutup. `OnNavigatingAway` sudah dijalankan oleh `Close()`, jadi penutupan window
  ini tidak bertanya lagi. Tidak ada window detach yang dibiarkan hidup tanpa isi.
- **Window berdiri sendiri.** Menutup Window A tidak menutup Window B yang di-detach dari A.
- **Sesi berakhir** (logout atau sesi mati): semua window detach ditutup paksa, tanpa bisa ditolak
  karena sesinya sudah hilang, dan semua body dilepas. Setelah itu layar login tampil di window
  utama.
- **Window utama ditutup** (keputusan user): **ditanya dulu.**
  1. Sebelum aplikasi berhenti, body yang sedang tampil di **setiap** window detach mendapat
     `OnNavigatingAway`, satu per satu. Window yang sedang ditanya dimunculkan dulu supaya user
     melihat isian yang dimaksud.
  2. Kalau satu saja menolak, penutupan window utama dibatalkan. Tidak ada window yang tertutup dan
     tidak ada body yang dilepas.
  3. Kalau semua setuju, semua window detach ditutup, semua body-nya di-`OnRelease`, lalu aplikasi
     berhenti (`ShutdownMode.OnMainWindowClose`, sudah begitu).

  Karena `OnNavigatingAway` asinkron, handler `Closing` window utama membatalkan penutupan pertama,
  menjalankan pemeriksaan, lalu menutup ulang dengan penanda supaya tidak berulang. Perilaku body di
  window utama sendiri tidak diubah plan ini. Di layout MultiTab daftar window detach kosong, jadi
  penutupan berjalan seperti sekarang.

## MultiTab

- Detach window hanya dibuat dari `SpaNavigationHost`, jadi di layout MultiTab fitur ini memang
  tidak terjangkau. Tidak perlu ada tombol, menu, atau percabangan baru di jalur MultiTab.
- `DetachedWindow` adalah kelas baru. `TabHostWindow` dan `TabWorkspaceHost` **tidak** dipakai
  ulang dan **tidak** diubah.
- Pembersihan detach saat sesi berakhir (tutup paksa semua window detach) dipasang di jalur
  `SessionEnded` yang dipakai kedua layout. Di MultiTab daftar stack detach selalu kosong, jadi
  langkah itu tidak melakukan apa-apa di sana.
- Kalau ada percabangan `ApplicationLayout` baru yang dibutuhkan, catat di laporan eksekusi (sama
  seperti di plan 2) supaya ikut dibuang saat plan 1 dijalankan.

## Implementasi (WPF)

- `EmApp`:
  - memegang daftar stack detach di samping `MainStack`;
  - `FindEntry` mencari di semua stack;
  - router `NavigateTo` menambah aturan Manager → stack utama.
- **`DetachedWindow` (baru):**
  - letak `Windows/DetachedWindow.xaml(.cs)`, sebuah `ThemedWindow` berisi `SpaNavigationHost`
    dalam mode detach (tanpa Home, tanpa menu, tanpa akun);
  - memegang `NavigationStack`-nya sendiri;
  - mendaftar ke `EmApp` saat dibuka dan melepas diri saat ditutup;
  - desain mengikuti material design di `src/frontend/CLAUDE.md`;
  - menutup diri sendiri saat stack-nya menjadi kosong.
- **`SpaNavigationHost`:**
  - `DetachWindow`/`DetachWindowAllowed` diisi;
  - mode detach menyembunyikan tombol Home dan akun.

  Shortcut back/forward sudah dipasang per window, jadi otomatis berlaku di window detach.
- "Memunculkan window": `WindowState` Minimized menjadi Normal, lalu `Activate()`.
- **`MainWindow`:** handler `Closing` menjalankan pemeriksaan window detach (lihat "Window utama
  ditutup").

## Performa

- **Satu proses, satu UI thread (STA), satu heap.** Window detach yang diam hampir tidak memakai
  CPU, karena WPF hanya menghitung layout dan menggambar saat ada perubahan.
- **Beban memory mengikuti jumlah body yang hidup**, bukan jumlah window. N window detach kira-kira
  seberat N entri di stack. Grid DevExpress memakai virtualisasi, jadi jumlah baris tidak membuat
  visual tree membengkak.
- **Risiko nyata #1: kebocoran.** Body yang berlangganan event berumur panjang (`EmApp.*`,
  `UIConnections`) wajib melepasnya di `OnRelease`. Kalau tidak, body tertahan walau window-nya
  sudah ditutup.
- **Risiko nyata #2: pekerjaan sinkron berat di UI thread** membekukan **semua** window. Panggilan
  API dan pemrosesan data berat harus `async`, atau dijalankan di background.
- **Tidak dikerjakan (keputusan user):** `EmAppBuilder.UseMaxDetachedWindows(int)` untuk membatasi
  jumlah window detach. Jumlah window detach tidak dibatasi; batas ini bisa ditambahkan nanti kalau
  memang dibutuhkan.

## Opsi proses terpisah (tidak dikerjakan di plan ini)

Secara teknis bisa: setiap window detach dijalankan sebagai proses baru (`--detach <id>`) yang
membangun `EmApp` sendiri dengan satu stack tanpa Home. Model "stack per window" dari plan 2 sudah
cocok untuk itu. Yang harus dibangun lebih dulu:

1. **Broker token.** Server merotasi refresh token (`TokenServices.RefreshAsync`): token lama mati
   saat dipakai. Dua proses yang me-refresh sendiri-sendiri akan saling mematikan sesi. Solusinya:
   proses utama menjadi satu-satunya pemegang token, dan proses detach meminta access token lewat
   **named pipe**.
2. **Payload yang bisa diserialisasi.** Yang dikirim ke proses baru hanya nama navigasi dan
   parameter kecil (tanggal, user id, document id); body membangun ulang datanya dari server.
   Isian yang belum disimpan tidak ikut.
3. **Siaran kejadian global lewat IPC:** logout/sesi berakhir, tema, koneksi aktif, claim, keunikan
   Title antar-proses, dan `NavigateTo` Manager ke proses utama.
4. **Biaya:** setiap proses memuat sendiri .NET, WPF, dan DevExpress. Perkiraan kasar, belum diukur:
   100–200 MB dan 1–3 detik start per window. Jadi total lebih berat daripada in-process;
   keuntungannya hanya isolasi (satu window macet atau crash tidak menjatuhkan yang lain).

Transport lewat Registry atau mutex tidak dipakai: mutex tidak membawa data, dan Registry persisten
di disk (tertinggal kalau crash) dan tidak dua arah. Named pipe hidup di memory dan bisa dibatasi ke
user Windows yang sama.

## Di luar cakupan

- **Optimistic concurrency** (penolakan simpan kalau data sudah diubah pihak lain). Keunikan Title
  sudah mencegah duplikasi dalam satu aplikasi, tapi bentrokan **antar-user** tetap mungkin. Ini
  pekerjaan backend dan konvensi tabel (`Revision` bermakna revisi bisnis; alternatifnya `ustamp`
  atau kolom `rowversion`). Sudah tercatat di `doc/TODO-LIST.md`, item "Pasang token konkurensi
  di entity".
- MAUI.

## Dokumentasi

- `CLAUDE.md` root, bagian "Keep the UI cores in step": **tambahkan** detach window sebagai fitur
  WPF-only yang disengaja, di samping contoh "the `MultiTab` layout" yang sudah ada (contoh MultiTab
  baru dibuang saat plan 1 dijalankan).
- `src/frontend/CLAUDE.md`, bagian navigasi: aturan detach, aturan Manager, dan kewajiban
  `OnRelease`/`async` untuk body.

## Verifikasi

- Build `Em.Ui.Wpf.slnx` tanpa error maupun warning baru.
- MAUI tetap tanpa detach:
  - `git diff` plan ini tidak menyentuh `src/shared/Em.Ui.Maui.Core`, `src/shared/Em.Ui.Core`,
    maupun proyek MAUI di `src/frontend`;
  - grep `Detach` (tidak peka huruf besar/kecil) di `src/shared/Em.Ui.Maui.Core` dan
    `src/shared/Em.Ui.Core` tidak menemukan apa-apa;
  - build `Em.Ui.Maui.slnx` tetap bersih, sebagai bukti kontrak bersama tidak berubah.
- Skenario di layout SPA (`builder.UseSinglePageLayout()` sudah aktif sejak plan 2), dijalankan
  **user sendiri**, dengan akun yang punya claim yang sesuai:
  1. Invoice INV-001 → detach (Window B); INV-002 → detach (Window C). Keduanya tampil berdampingan,
     judul window sesuai Title.
  2. Dari Window A buka INV-001 lagi → Window B dimunculkan, tidak ada duplikat.
  3. User Manager → detach (Window D); di D, edit ani → masuk stack D; Back di D kembali ke User
     Manager.
  4. Di D, detach "Edit User: ani" → Window E berisi editor saja.
  5. Dari body di D, `NavigateTo` ke Role Manager → terbuka di Window A, dan Window A dimunculkan.
  6. Klik User Manager dari menu home Window A → Window D dimunculkan.
  7. Isian editor yang belum disimpan tetap ada setelah detach.
  8. Tutup Window D saat editor ani punya perubahan → penolakan `OnNavigatingAway` membatalkan
     penutupan.
  9. Logout dari Window A → semua window detach tertutup, `OnRelease` terpanggil untuk semua body.
  10. Home dan layar login tidak bisa di-detach; akar window detach tidak bisa di-detach.
  11. Window detach baru berukuran sama dengan window asal, bergeser sedikit ke kanan-bawah, dan
      `WindowState`-nya Normal, termasuk saat window asal maximized. Detach dua kali berturut-turut
      dari window yang sama tidak menumpuk persis.
  12. Ganti tema dari toolbar Window E → semua window (A, B, C, D, E) ikut berganti tema.
  13. Body akar Window E memanggil `entry.Close()` → Window E tertutup sendiri.
  14. Tutup Window A saat editor di Window D punya perubahan → Window D dimunculkan dan ditanya;
      menolak → aplikasi tetap jalan dan semua window utuh; setuju → semua window tertutup,
      `OnRelease` terpanggil, aplikasi berhenti.
- Layout MultiTab (`UseSinglePageLayout()` dikomentari): login → workspace tab, tab tes bisa
  dibuat, ditutup, dan ditarik keluar ke `TabHostWindow`; logout kembali ke login. Perilakunya sama
  dengan sebelum plan ini.
