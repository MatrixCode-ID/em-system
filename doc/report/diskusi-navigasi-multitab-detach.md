# Laporan Diskusi — Navigasi WPF: dari MultiTab ke SPA + Detach Window

Status: **diskusi selesai, rancangan dituangkan ke 3 plan, belum ada kode yang diubah**
Dibuat: 2026-09-23
Baseline: commit `0e69ef4`, branch `data-services`
Sifat: laporan **diskusi dan keputusan**, bukan laporan eksekusi
Rujukan:
- [plan/unexecuted/navigasi-1-hapus-multitab.md](../../plan/unexecuted/navigasi-1-hapus-multitab.md)
- [plan/executed/navigasi-2-entri-per-host.md](../../plan/executed/navigasi-2-entri-per-host.md)
- [plan/executed/navigasi-3-detach-window.md](../../plan/executed/navigasi-3-detach-window.md)
- [doc/TODO-LIST.md](../TODO-LIST.md), item "Pasang token konkurensi di entity"

---

## 1. Ringkasan

Diskusi dimulai dari permintaan merapikan alur MultiTab WPF dengan menghapus forward/backward, dan
berakhir dengan keputusan yang lebih besar:

- **Layout MultiTab dihapus seluruhnya.** SPA menjadi satu-satunya layout, baik di WPF maupun MAUI.
- **Kebutuhan membuka beberapa layar berdampingan** dipenuhi lewat **detach window**. Fitur ini
  khusus WPF, dan sekarang tombolnya baru stub.
- **Engine navigasi di kedua core di-refactor:**
  - body dan data pindah dari `Navigation` ke **entri stack**;
  - **Title entri menjadi kunci unik** di seluruh aplikasi;
  - setiap navigasi wajib menyatakan **jenisnya** (Manager atau Editor).

Pekerjaannya dibagi tiga plan berurutan: hapus MultiTab → refactor engine → detach.

## 2. Kondisi awal yang ditemukan

| Temuan | Dampak |
| --- | --- |
| MultiTab tidak tersambung ke sistem navigasi. `HomeNavigation`/`NavigateHome` melempar `NotSupportedException`, dan `NavigateTo` kena NRE di `MainWindow._spaHost!` | `NavigateTo` tidak bisa dipakai di layout MultiTab |
| Tab dikelola lewat API string header (`TabCreate`, `TabSelect`, `TabExists`, `TabRename`, `TabRemove`) | tidak kenal `Navigation`, claim (`CanOpen`), maupun lifecycle body |
| Menu MultiTab bersumber dari `AddMainControl`; tombol "Home" di toolbar hanya stub tes | mekanisme lama yang memang akan dihapus |
| Dua jalur login: `loginHost` + `IsSignedIn` (MultiTab) dan navigasi `admin.logon` (SPA) | logika login ganda |
| `EmApp` bercabang di `ApplicationLayout` di banyak tempat | setiap fitur navigasi harus ditulis dua kali |
| `INavigationHost`/`INavigation` di `Em.Ui.Core` memuat member khusus stack | kontrak bersama tidak bermakna untuk layout tanpa stack |
| Satu `Navigation` hanya memegang satu body dan satu data | satu navigasi tidak bisa tampil di dua tempat sekaligus |
| Tombol Detach di `SpaNavigationHost` hanya stub: `DetachWindow()` kosong, `DetachWindowAllowed()` selalu `false` | detach harus dirancang dari nol |
| `admin.users`/`admin.roles` didaftarkan tanpa `RequiredClaim`, padahal claim-nya sudah dideklarasikan di `InitInternalClaims` | User Manager dan Role Manager terbuka untuk semua user yang login |
| `MainWindow.OnThemeChanged` hanya memanggil `RenderBranding` pada login milik MultiTab | panel brand login SPA tidak ikut berganti saat tema berubah |
| Aplikasi yang berjalan (`Program.cs`) sudah memakai `UseSinglePageLayout()` | menghapus MultiTab tidak mengubah perilaku yang dilihat user |

## 3. Jalannya diskusi

Arah rancangan berubah beberapa kali. Urutannya dicatat supaya alasan keputusan akhir bisa ditelusuri.

1. **MultiTab tanpa history.** Rencana awal: satu `Navigation` = satu tab, tanpa home. Menu "Apps"
   memakai pathing `MenuPath` yang sama dengan home SPA, dan static tools masuk menu "Tools".
   Untuk menghilangkan percabangan `ApplicationLayout`, diusulkan objek *navigator* per layout
   (`SpaNavigator`/`TabNavigator`).
2. **Kunci tab berdasarkan Title.** User ingin "Edit User: Ani" dan "Edit User: Joni" bisa terbuka
   bersamaan. Akibatnya body harus per tab, bukan per `Navigation`. Title juga harus bisa di-rename
   ("Create New User" → "Edit User: Ani"), dan Title yang sudah terbuka cukup diaktifkan.
3. **Menggabungkan tab dengan SPA ternyata rumit.** User mengusulkan memakai detach window yang
   tombolnya sudah disiapkan di SPA, dan MultiTab dihapus sepenuhnya. Pendekatan ini jauh lebih
   sederhana:
   - satu jalur login, satu layout;
   - kontrak bersama tidak perlu dipecah;
   - WPF dan MAUI kembali satu struktur.
4. **Pertanyaan performa dan proses terpisah** (bagian 6). Kesimpulannya: in-process lebih dulu,
   dengan kontrak yang tidak menutup kemungkinan pindah ke proses terpisah nanti.
5. **Aturan window detach dipertajam lewat contoh nyata:**
   - window detach tanpa Home;
   - stack sendiri per window;
   - detach bertingkat boleh;
   - Manager selalu di window utama;
   - satu Title hanya tampil di satu tempat.
6. **Plan MultiTab lama dibatalkan** dan diganti tiga plan baru.

## 4. Keputusan akhir

### 4.1 Layout dan kontrak

| # | Keputusan |
| --- | --- |
| K1 | MultiTab dihapus seluruhnya, termasuk `ApplicationLayout`, operasi `Tab*`, `AddMainControl`, `TabHostWindow`, dan jalur login `loginHost`. |
| K2 | SPA menjadi satu-satunya layout WPF, sama seperti MAUI. |
| K3 | `admin.users`/`admin.roles` diikat ke claim internalnya; home SPA menyaring static tools lewat `CanOpen`. Berlaku juga di SPA. |
| K4 | `Navigation` hanya menjadi definisi. Body, data, dan Title yang tampil pindah ke **entri** stack. |
| K5 | Stack dipisah dari `EmApp`: satu stack per window. MAUI hanya punya satu stack. |
| K6 | **Title entri adalah kunci unik** di seluruh aplikasi, dan satu Title hanya boleh tampil di satu tempat. Title awal diambil dari payload, dengan fallback ke `Navigation.Title`. |
| K7 | Title bisa di-rename lewat `SetTitle`, dan kuncinya ikut berubah. Rename ke Title yang sudah dipakai ditolak. |
| K8 | Membuka Title yang sudah ada **hanya mengaktifkan** entri itu: tanpa reload dan tanpa mengganti data. "Create New User" kedua mengaktifkan yang pertama. |
| K9 | Setiap navigasi wajib menyatakan `NavigationKind`: `Manager` atau `Editor`. |
| K10 | MAUI ikut model entri (body per entri, Title sebagai kunci, `SetTitle`, `NavigationKind`), dengan satu stack dan tanpa detach. Kedua core tetap satu struktur. |

### 4.2 Detach window (WPF saja)

| # | Keputusan |
| --- | --- |
| D1 | Window detach adalah host SPA **tanpa Home dan tanpa menu aplikasi**. Entri yang di-detach menjadi akar stack-nya. |
| D2 | Navigasi dari body di window detach masuk ke **stack window itu sendiri**. Contoh: Invoice → Sales Order. |
| D3 | Kalau Title yang diminta sudah ada di window mana pun, **window itu yang dimunculkan** dan posisi stack-nya dipindah ke entri tersebut. |
| D4 | **Manager hanya hidup di window utama**, atau sebagai akar window detach. `NavigateTo` ke Manager dari window lain selalu dibuka di window utama, termasuk kalau developer menyelipkannya dari body. |
| D5 | **Detach bertingkat boleh.** Karena D4, hasil detach tingkat kedua praktis selalu editor. |
| D6 | Detach **in-process** lebih dulu: satu proses, satu UI thread. |
| D7 | MAUI tidak punya detach. Ini satu-satunya perbedaan struktur yang disengaja antara kedua core. |

### 4.3 Contoh perilaku yang disepakati

- **Membandingkan dua invoice.** Invoice INV-001 di-detach (Window B), INV-002 di-detach
  (Window C). Keduanya bisa ditaruh berdampingan, dengan judul window = Title entri.
- **Invoice Manager sudah di Window B,** lalu diklik dari menu Window A → Window B yang dimunculkan.
- **Link "Lihat Customer" dari invoice di Window B membuka Customer Manager** → Customer Manager
  terbuka di Window A (D4).
- **User Manager di-detach (Window D).** "Edit User: Ani" dibuka di stack D, lalu di-detach lagi
  menjadi Window E yang hanya berisi editor.

## 5. Yang dibatalkan

| Rancangan | Alasan dibatalkan |
| --- | --- |
| Plan `multitab-navigasi-tanpa-history.md` (MultiTab tanpa history, navigator per layout, pemecahan `INavigationHost`/`INavigationStackHost`) | MultiTab dihapus seluruhnya |
| Menu "Apps"/"Tools" di toolbar MultiTab dan `InitInternalTools` | toolbar-nya ikut hilang; static tools cukup dibaca home SPA |
| Key terpisah dari Title | user memilih Title sebagai kunci unik |
| Aturan "hanya navigasi aktif window lain yang dicek" | diganti keunikan Title di semua entri semua window (K6) |
| Detach dengan proses terpisah sekarang | biaya dan infrastrukturnya belum sepadan (bagian 6) |

## 6. Jawaban pertanyaan teknis

### 6.1 Apakah window detach masih satu apartment? Apakah berat kalau banyak?

Ya: satu proses, satu UI thread (STA), dan satu heap.

- **CPU:** window yang diam hampir tidak memakai CPU, karena WPF hanya menghitung layout dan
  menggambar saat ada perubahan.
- **Memory:** naik sesuai jumlah **body yang hidup**, bukan jumlah window. N window detach kira-kira
  seberat N entri di stack. Grid DevExpress memakai virtualisasi.
- **Risiko nyata #1:** body yang lupa melepas langganan event berumur panjang di `OnRelease` akan
  tertahan di memory walaupun window-nya sudah ditutup.
- **Risiko nyata #2:** pekerjaan sinkron berat di UI thread membekukan **semua** window. Pekerjaan
  berat harus `async`, atau dijalankan di background.
- **Satu thread per window** secara teknis bisa, tapi tidak disarankan. Objek bersama (`EmApp`,
  model yang dipegang dua layar, resource tema DevExpress) akan tersentuh dari banyak thread, dan
  risiko cross-thread-nya lebih mahal daripada manfaatnya.
- **Opsional:** batas jumlah window lewat `UseMaxDetachedWindows(int)`. Masih usulan.

### 6.2 Bisakah window detach dijalankan sebagai proses terpisah?

Bisa, lewat argumen `--detach <id>`. Model stack per window dari plan 2 sudah cocok untuk itu. Yang
harus dibangun lebih dulu:

1. **Broker token.** Server merotasi refresh token (`TokenServices.RefreshAsync`): token lama mati
   saat dipakai. Dua proses yang me-refresh sendiri-sendiri akan saling mematikan sesi. Solusinya:
   hanya proses utama yang memegang token, dan proses detach meminta access token lewat named pipe.
2. **Payload yang bisa diserialisasi.** Cukup parameter kecil seperti tanggal, user id, atau document
   id, lalu body memuat ulang datanya dari server. Isian yang belum disimpan tidak ikut.
3. **Siaran kejadian global lewat IPC:** logout, tema, koneksi aktif, claim, keunikan Title
   antar-proses, dan `NavigateTo` Manager ke proses utama.

**Transport:** Registry dan mutex kurang cocok. Mutex tidak bisa membawa data, sedangkan Registry
persisten di disk (tertinggal kalau aplikasi crash) dan tidak bisa dipakai untuk komunikasi dua arah.
Pola yang lazim adalah ID lewat argumen command-line, lalu data dan komunikasi lewat **named pipe**.

### 6.3 Kenapa proses terpisah "lebih berat"?

Setiap proses memuat ulang seluruh fondasi yang di in-process cukup dimuat sekali:
- runtime .NET dan GC;
- assembly WPF dan DevExpress beserta hasil JIT-nya;
- resource tema;
- `EmApp` dan DI container;
- koneksi HTTP dan cache data.

Hanya body layarnya yang memang baru. Perkiraan kasar, **belum diukur**:

- **Memory:** sekitar **100–200 MB** per proses. Lima window detach berarti enam fondasi
  multi-proses, dibanding satu fondasi in-process.
- **Waktu buka:** proses baru butuh sekitar **1–3 detik** sampai siap. Detach in-process terasa
  instan karena body tinggal dipindah.
- **Imbalannya hanya isolasi:** satu window yang macet atau crash tidak menjatuhkan yang lain.
- **Cara mengukur:** jalankan aplikasi sampai home, lalu baca kolom *Memory* proses-nya di Task
  Manager. Angka itu kira-kira harga satu fondasi per proses.

### 6.4 Optimistic concurrency

Optimistic concurrency mencegah *lost update*: penyimpanan kedua diam-diam menimpa perubahan yang
disimpan lebih dulu.

- **Cara kerjanya:** setiap baris punya penanda versi, dan `UPDATE` hanya berhasil kalau versinya
  masih sama dengan yang dulu dibaca client. Kalau tidak, penyimpanan ditolak dan user diminta
  memuat ulang.
- **Keunikan Title (K6)** sudah mencegah dokumen yang sama terbuka dua kali dalam satu aplikasi.
  Bentrokan **antar-user** tetap mungkin.
- **Kolom `Revision`** menurut konvensi kemungkinan bermakna revisi bisnis (misalnya "SO revisi 2"),
  jadi kurang tepat dipakai sebagai token. Kandidat lainnya `ustamp` atau kolom `rowversion`.
- **Tindak lanjut:** sudah digabung ke item TODO yang ada, "Pasang token konkurensi di entity".
  Tidak masuk plan navigasi.

## 7. Masih berstatus usulan

Poin-poin ini dikonfirmasi saat eksekusi plan terkait:

| Usulan | Plan |
| --- | --- |
| `UseHomeNavigation(customHome)` meniru nama builder MAUI, menggantikan `UseSinglePageLayout(customHome)` | **disetujui**, dipindah ke plan 2 |
| Kunjungan ulang ke Title yang sudah ada di stack SPA tidak me-reload body. Ini perubahan perilaku SPA. | 2, **disetujui** |
| Detach **memindahkan body apa adanya** (isian yang belum disimpan ikut), bukan membangun ulang dari payload. `UserEditorNavigationPayload` membawa objek `User` dan callback, jadi belum bisa diserialisasi. | 3, **disetujui** |
| `UseMaxDetachedWindows(int)` sebagai batas opsional | 3, **tidak dikerjakan** |

Semua usulan di atas diputuskan user pada 2026-09-24. Pada hari yang sama user juga memutuskan
celah-celah plan 3 (penutupan window utama, stack detach kosong, posisi/ukuran window, tema global).
Rinciannya ada di plan 3.

## 8. Langkah berikutnya

> **Pembaruan 2026-09-24:** user memutuskan MultiTab **tidak dihapus dulu**. Plan 1 ditunda, dan
> plan 2 serta 3 dikerjakan dengan MultiTab tetap hidup berdampingan dengan SPA. Detach window hanya
> ada di layout SPA. Pengikatan claim User/Role Manager dan perbaikan `OnThemeChanged` dipindah dari
> plan 1 ke langkah persiapan plan 2. Urutan di bawah menggantikan urutan semula (plan 1 → 2 → 3).

1. Eksekusi **plan 2** (langkah persiapan, lalu refactor engine navigasi di WPF dan MAUI).
2. Eksekusi **plan 3** (detach window, WPF saja, layout SPA).
3. **Plan 1** (hapus MultiTab) menunggu keputusan user.

Cara eksekusi plan 2 dan 3 (satu kali jalan, commit per plan, verifikasi klik oleh user,
`UseSinglePageLayout()` di-uncomment) ada di bagian "Cara eksekusi" plan 2.

4. Opsional: ukur memory satu proses aplikasi untuk mengganti perkiraan di bagian 6.3 dengan angka
   nyata.
