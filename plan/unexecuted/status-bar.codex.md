# Plan — Status bar di jendela utama (untuk Codex)

Status: **belum dieksekusi — keputusan sudah final, siap dikerjakan**
Dibuat: 2026-10-02
Pembaca: Codex. Berkas ini sengaja memuat aturan yang biasanya hanya diingat Claude (bagian 2),
karena Codex tidak bisa membaca memori Claude. Baca juga `claude.md` di root repo ini.

---

## 1. Tujuan

Jendela utama engine (`TabbedMainWindow`) belum punya status bar. Tambahkan satu di bawah kartu
konten, dengan dua jenis isi yang berbeda sumber dan umurnya:

1. **Status layar (kiri)** — teks pendek milik layar yang sedang tampil, mis. "25.995 customers" atau
   "Loading…". Disimpan **per entry navigasi**, jadi pindah tab (MultiTab) atau pindah layar
   (SinglePage) otomatis menampilkan status entry yang baru aktif.
2. **Item status aplikasi (kanan)** — pesan yang tidak bergantung layar: update tersedia, sedang
   mengunduh aplikasi (dengan progress), siap restart, dst. Bentuknya daftar item yang dipasang dan
   dilepas oleh siapa pun lewat satu service. Tahap 3 update in-app (belum dibangun) nanti hanya
   memakai service ini, tanpa mengubah jendela.

Pemicunya: waktu buka layar CML diukur dan hasilnya hanya sempat ditaruh sebagai teks sementara di
dalam layar; engine butuh tempat resmi untuk hal seperti itu.

## 2. Aturan yang tidak tertulis di claude.md

- **Jangan menulis nama produk atau repo privat di kode atau dokumen repo ini.** Repo ini open source dan
  netral produk.
- Bahasa: percakapan dan XML doc (`/// <summary>` dst.) **Bahasa Indonesia**; identifier, pesan
  exception, pesan log, komentar inline, dan string literal tetap **Inggris**. XML doc hanya untuk
  anggota `public`, menjelaskan apa dan kenapa untuk pembaca baru, bukan mengulang nama anggotanya.
- **Tanpa komentar penjelas di XAML dan XML lain** (`.csproj`, `.slnx`). Penanda struktur pendek
  seperti `<!-- ==== status bar ==== -->` boleh. Alasan keputusan ditulis di balasan/laporan, bukan di
  berkas. Komentar yang sudah ada jangan dihapus.
- **WPF: aturan MVVM wajib.** Semua view model turunan `MvvmModelBase`; properti terikat memakai pola
  `get => Get<T>(); set => Set(value);` (publik, bukan auto-property); command didaftarkan di
  konstruktor VM dengan `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`; metode
  can-execute bernama `XxxCommandAllowed` (bukan lambda inline); XAML mengikat
  `Command="{Binding Commands[XxxCommand]}"`; command tidak juga diekspos sebagai properti. Event
  code-behind hanya kalau binding benar-benar mustahil, dan satu baris yang meneruskan ke VM.
- **Material design language yang sudah ada.** Merge `Styles/MaterialDesign.xaml`; pakai kunci
  palette yang ada (`surfaceBrush`, `outlineBrush`, `dividerBrush`, `successBrush`, `warningBrush`,
  `dangerBrush`, token tema `themeAccentBrush`, `themeWindowForegroundBrush`, …). Jangan membuat
  gaya visual baru, jangan memakai `SystemColors.*`, jangan memakai kunci palette DevExpress. Warna
  permukaan = abu translusen (`#xx808080`); aksen hanya untuk aksi utama dan penanda aktif.
  Gaya baru yang bisa dipakai layar lain masuk ke berkas di `Styles/`, bukan ke satu layar.
- **Cermin WPF/MAUI.** Kontrak bersama (`Em.Ui.Core`) harus bisa dipenuhi kedua core. Status bar
  sendiri **hanya WPF** (MAUI tidak punya padanan, sama seperti detach window); sisi MAUI hanya
  memenuhi kontrak tanpa UI. Catat ini di doc supaya tidak dianggap lupa dicerminkan.
- **Jangan menyentuh set perubahan lain yang belum di-commit.** Working tree repo ini berisi
  pekerjaan lain yang belum selesai: `Controls/WaitDots.cs`, `Controls/ButtonWait.cs`,
  `Controls/WaitOverlay.cs`, `Styles/Buttons.xaml`, `Themes/Generic.xaml`. Boleh **memakai**
  `WaitDots`, tapi jangan mengubah, mengembalikan, atau meng-commit berkas-berkas itu. Jangan
  `git add -A`; stage hanya path yang diubah tahap yang sedang dikerjakan.
- **Commit:** satu commit per tahap, pesan **Bahasa Indonesia** (judul dan isi; kode, identifier, dan
  path tetap apa adanya). Jangan `--no-verify`, jangan `push`.
- Kalau sebuah perintah ditolak policy ("blocked by policy" atau sejenisnya): jangan memutar lewat
  shell atau wrapper lain; catat tindakannya dan alasannya di laporan, kerjakan bagian lain, dan
  beri tahu pengguna apa yang perlu dijalankan sendiri.
- Tidak ada project test di repo. Verifikasi = build bersih + daftar uji manual di bagian 6.
  Jangan menyatakan uji manual "lulus" kalau tidak benar-benar dijalankan; tulis sebagai tertunda.

## 3. Keputusan final

| Hal | Keputusan |
| --- | --- |
| Letak bar | Baris ketiga `TabbedMainWindow`, di bawah kartu konten; berlaku di **MultiTab dan SinglePage** (keduanya memakai kelas jendela yang sama). |
| Jendela lain | **Hanya jendela utama.** Jendela tear-off (`ClosesWhenEmpty == true`) dan `DetachedWindow` **tidak** punya bar. |
| Sumber status layar | `INavigationEntry.SetStatus(...)` — disimpan per entry, konsisten dengan `SetTitle`. Bukan property di view model. |
| Item aplikasi, versi pertama | Teks + tingkat (info/peringatan/error) + progress (pasti atau tak tentu) + satu aksi klik opsional + penanda sibuk. |
| MAUI | Kontrak `INavigationEntry` dipenuhi (simpan nilai, angkat `PropertyChanged`), tanpa tampilan. |
| Pengukur waktu CML | **Di luar plan ini.** Teks sementara di modul tidak disentuh (repo lain). |
| Update in-app (tahap 3) | Di luar plan ini; plan ini hanya menyiapkan tempat dan service-nya. |

## 4. Rancangan

### 4.1 Kontrak dan service — `Em.Ui.Core` (`src/shared/Em.Ui.Core/Ui.Core/shared/`)

- `StatusSeverity` — enum `Info`, `Warning`, `Error`.
- `StatusAction` — record `(string Caption, Func<Task> Execute)`.
- `StatusItem` — kelas sealed, `INotifyPropertyChanged` (pakai `NotifyPropertyBase` bila
  `Em.Ui.Core` sudah mereferensikan `Em.Libs`, kalau tidak implementasi sendiri). Anggota: `Id`
  (string, wajib, unik), `Text`, `Severity` (bawaan `Info`), `Progress` (`double?`, 0..1; `null` =
  tanpa progress), `IsIndeterminate` (bool; progress tanpa angka), `IsBusy` (bool; titik tunggu),
  `Action` (`StatusAction?`), `Order` (int, bawaan 0; urutan tampil kecil → besar, lalu urutan
  dipasang).
- `AppStatus` — kelas sealed, singleton DI, seperti `BusinessTaskTracker`: tidak bergantung UI
  framework. Anggota: `IReadOnlyList<StatusItem> Items`, `event EventHandler? Changed`,
  `void Set(StatusItem item)` (menambah, atau mengganti item ber-`Id` sama), `bool Remove(string id)`,
  `void Clear()`. Semua anggota dipanggil dari satu thread, thread UI — tulis itu di XML doc seperti
  pada `BusinessTaskTracker`. `Changed` dimunculkan setelah setiap perubahan susunan; perubahan
  properti sebuah `StatusItem` yang sudah terpasang cukup lewat `INotifyPropertyChanged`-nya.
- `INavigationEntry` (file `INavigationEntry.cs`), anggota baru:
  - `string? Status { get; }` dan `bool StatusBusy { get; }`
  - `void SetStatus(string? status, bool busy = false)` — `null`/kosong menghapus status. Entry yang
    sudah dilepas (`_released`) mengabaikan panggilan, tidak melempar exception.

### 4.2 Pemenuhan kontrak

- `Em.Ui.Wpf.Core/Core/NavigationEntry.cs`: simpan `Status`/`StatusBusy`, angkat `PropertyChanged`
  untuk keduanya (pola yang dipakai `SetTitle`). Hanya angkat kalau nilainya benar-benar berubah.
- `Em.Ui.Maui.Core/Core/NavigationEntry.cs`: sama, tanpa UI.
- `Em.Ui.Wpf.Core/Core/EmApp.Statics.cs`: daftarkan `AppStatus` sebagai singleton di dekat
  `BusinessTaskTracker`. MAUI tidak mendaftarkannya (tidak ada UI untuk menampilkan).

### 4.3 Tampilan — `Em.Ui.Wpf.Core`

- `Windows/TabbedMainWindow.xaml`: tambah `RowDefinition Height="Auto"` ketiga dan blok
  `<!-- ==== status bar ==== -->` di `Grid.Row="2"`. Margin sejalan dengan kartu konten
  (`8,0,8,6`), tinggi sekitar 28. Tanpa kartu/outline sendiri; garis pemisah tipis `dividerBrush`
  di atasnya cukup. Terlihat hanya bila **bukan** tear-off (`!ClosesWhenEmpty`).
- Kiri: teks status entry aktif (12 px, `Opacity` 0.65) dan, bila `StatusBusy`, `WaitDots`
  (`Width=24 Height=7`) di depannya. Kosong → tidak ada apa-apa, tinggi bar tetap.
- Kanan: `ItemsControl` horizontal berisi item `AppStatus`. Tiap item: `WaitDots` bila `IsBusy`;
  teks (warna: `Info` = warna teks biasa, `Warning` = `warningBrush`, `Error` = `dangerBrush`);
  progress mini 80 px bila `Progress` ada (determinate) atau `IsIndeterminate` (pakai
  `hairlineProgressStyle` dari `Styles/Progress.xaml`, jangan membuat gaya progress baru); tombol
  teks (`textButtonStyle`) bila `Action` ada.
- `Styles/StatusBar.xaml` (baru, di-merge dari `Styles/MaterialDesign.xaml`): gaya bar, gaya item,
  template item. Kunci resource diawali `statusBar`. Header berkas mengikuti berkas `Styles/` lain
  (alasan dua tema, di berkas ini boleh berkomentar karena itu referensi gaya).
- `TabbedMainWindowVm` (di `TabbedMainWindow.xaml.cs`):
  - `StatusText`, `StatusBusy` (bindable) mengikuti entry `Stack.Current` — berlangganan
    `PropertyChanged` entry itu, **berhenti berlangganan** saat entry berganti atau dilepas.
    Hubungkan di `Sync()` (dipanggil di kedua event stack), jangan di tempat lain.
  - `StatusItems`: koleksi pembungkus yang dibangun dari `AppStatus.Items` dan disegarkan pada
    `AppStatus.Changed`.
  - `RegisterCommand<StatusItem>(nameof(StatusItemActionCommand), StatusItemActionCommand, StatusItemActionCommandAllowed)`:
    menjalankan `item.Action.Execute()`; kegagalan lewat `AlertError`. Dapat dieksekusi hanya bila
    item punya `Action`.
  - Lepas langganan `AppStatus.Changed` dan entry saat jendela ditutup.
- Layout SinglePage: tidak ada perlakuan khusus; `Stack.Current` di `MainStack` sudah entry yang
  tampil. Entry `Home` tidak punya status → bagian kiri kosong.

### 4.4 Dokumen

- `doc/status-bar.md` (baru, Bahasa Indonesia, untuk penulis modul): cara memakai
  `entry.SetStatus(...)` dari body (lewat `NavigationEntry` di `MvvmModelBase` atau `args.Entry`),
  cara memasang/melepas item lewat `AppStatus` (contoh progress unduhan dengan aksi "Restart"),
  aturan thread, bahwa hanya jendela utama yang menampilkannya, dan bahwa MAUI hanya menyimpan nilai.
  **Jangan menyebut nama objek database.**
- Tambahkan satu baris ke `claude.md` bagian pembaruan (pertahankan isi lama): bar status ada dan
  rujukan ke `doc/status-bar.md`.

## 5. Tahap kerja

Tiap tahap diakhiri build bersih dan satu commit (stage hanya path tahap itu).

1. **Kontrak dan service**: butir 4.1 dan 4.2. Build `src/frontend/Em.Ui.Wpf.slnx` dan
   `src/frontend/Em.Ui.Maui.slnx`.
2. **Tampilan**: butir 4.3. Build kedua solution, lalu jalankan uji manual 6.1–6.4.
3. **Dokumen**: butir 4.4.

Build: `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `dotnet build src/frontend/Em.Ui.Maui.slnx`.
Kalau MAUI gagal dibangun karena workload mesin (bukan karena perubahan ini), catat apa adanya dan
jangan memperbaiki hal di luar cakupan. Jika aplikasi sedang berjalan di debugger, DLL terkunci
(`MSB3027`) — itu bukan galat kompilasi; minta pengguna menutup aplikasi.

## 6. Uji manual (dijalankan pengguna atau di sesi dengan tampilan)

Tidak ada UI uji bawaan. Untuk menguji item aplikasi dan status layar, pakai kode sementara yang
**tidak di-commit** (mis. di `Em.Ui.Wpf` host, setelah login) lalu hapus:

1. MultiTab: buka dua tab; dari body tab A panggil `entry.SetStatus("A", busy: true)`, dari tab B
   `SetStatus("B")`. Pindah tab → teks kiri bergantian benar, titik tunggu hanya di A.
2. SinglePage (`builder.UseSinglePageLayout()`): buka layar, set status, Back/Forward → status
   mengikuti entry; Home → kiri kosong.
3. `AppStatus.Set(new StatusItem { Id = "update", Text = "Downloading 40%", Progress = 0.4, IsBusy = true })`
   → item muncul di kanan; ubah `Progress` → bar bergerak; `Action` terpasang → tombol memanggilnya;
   `Remove("update")` → hilang. Cek `Warning`/`Error` terbaca di tema terang dan gelap.
4. Tear-off sebuah tab (MultiTab) dan Detach (SinglePage) → jendela hasilnya **tanpa** bar; menutup
   jendela utama tidak meninggalkan langganan yang bocor (tidak ada exception saat menutup).
5. Pastikan tidak ada regresi: layar login masih utuh, jendela bisa dimaksimalkan, tinggi kartu
   konten hanya berkurang setinggi bar.

## 7. Laporan eksekusi

Tulis `doc/report/status-bar-eksekusi.md` (Bahasa Indonesia): apa yang selesai per tahap, build yang
dijalankan beserta hasilnya, uji manual yang **belum** dijalankan (jangan dinyatakan lulus),
keputusan kecil yang diambil di luar plan ini, dan temuan. Setelah selesai, pindahkan plan ini ke
`plan/executed/`. Beri tahu pengguna bahwa dokumen di repo privat yang menjelaskan engine bagi
penulis modul (bagian navigasi di panduan frontend repo itu) perlu satu paragraf tentang bar status;
**jangan** mengeditnya dari sini.
