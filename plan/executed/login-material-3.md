# Layar login Material 3 (card + background) dengan pilihan Classic

Plan untuk Codex. Semua keputusan sudah final (diputuskan bersama pengguna 2026-10-02); **jangan bertanya ke pengguna saat eksekusi**. Kalau ada hal yang tidak tercakup, ambil pilihan yang paling konsisten dengan plan dan catat di laporan eksekusi.

Baca dulu `CLAUDE.md` di root repo (aturan proyek, termasuk urutan kode-dulu-analisa-di-akhir dan aturan tindakan terblokir policy).

## Tujuan

Layar login WPF (`src/shared/Em.Ui.Wpf.Core/Navigations/LoginControl.xaml`) terlihat tua dan tidak mengikuti Material Design 3 (panel brand berwarna jenuh + lingkaran watermark + gradien gelap, logo di kotak "frosted", kartu dengan bayangan tebal, field berlabel di atas kotak radius 12, tombol 46 px berbayangan, abu-abu transparan `#808080` di mana-mana). Buat layar login baru bergaya Material 3, sambil **mempertahankan layar lama** sebagai pilihan yang bisa diganti lewat `BrandingInfo`. Tambahkan juga `LightLoginBackground` dan `DarkLoginBackground`.

## Keputusan final

| Hal | Keputusan |
|---|---|
| Layout baru | **Card login di tengah di atas background** (tanpa panel brand di kiri). |
| Pilihan tampilan | `BrandingInfo.LoginStyle` (enum `LoginStyle { Classic, Material }`). **Default `Material`.** Yang memilih adalah pengembang aplikasi lewat `ApplyBranding`; tidak ada tombol pilihan untuk pengguna akhir. |
| Layar lama | Dibekukan: tampilan dan perilakunya tidak boleh berubah, hanya diganti namanya menjadi `LoginControlClassic`. Properti background **diabaikan** di Classic (dicatat di dokumentasi). |
| Platform | **WPF saja** (`Em.Ui.Wpf.Core`). MAUI tidak disentuh; properti baru di `BrandingInfo` (project bersama `Em.Ui.Core`) hanya belum dipakai MAUI dan itu dicatat di XML doc. |
| Gaya Material baru | Style bersama di `Em.Ui.Wpf.Core/Styles/` dengan **kunci baru** (jangan mengubah style yang sudah ada), ditambah token tema baru di `ThemeResources`. |
| Background | Dua properti string di `BrandingInfo`: `LightLoginBackground`, `DarkLoginBackground`. Ditampilkan di **seluruh jendela login**, di belakang card. |
| Tanpa background | **Gradien tonal tiga titik**: `primaryContainer` (kiri-atas) → `surfaceContainerLow` (tengah) → `surface` (kanan-bawah), dari token tema, tanpa warna yang ditulis tetap. |
| Hanya satu background diisi | Gambar yang ada dipakai di **kedua mode**. Lapisan redup (token `Scrim`, opacity 0,35) dipasang **hanya** ketika mode gelap memakai gambar terang. Gambar yang gagal dimuat jatuh ke gambar mode lain, lalu ke gradien. |
| Logo | Di dalam card, di kepala form: tile 56×56 radius 16 berwarna `primaryContainer`, logo di dalamnya, nama dan tagline di sampingnya. |
| `BrandingInfo.Description` | Tetap ada di `BrandingInfo` dan tetap dipakai Classic; **tidak tampil** di layar Material. Tidak dihapus. |
| Commit | **Satu commit di akhir**, pesan judul dan deskripsi Bahasa Indonesia, hanya berkas milik plan ini, tanpa push. |

## Aturan eksekusi

- Urutan: **tulis semua kode semua tahap dulu** (Tahap 1–6), baru pengujian (Tahap 7) dan analisa/review (Tahap 8) sekali di akhir. Jangan menyelipkan tes atau analisa di antara tahap penulisan kode. Kalau token menipis, hentikan tes/analisa dulu, bukan penulisan kode, dan catat apa yang belum sempat diuji.
- Kode ditulis seperti kode di sekitarnya: komentar XML doc Bahasa Indonesia untuk anggota publik (lihat `BrandingInfo.cs`), komentar inline Bahasa Inggris seperti file XAML dan code-behind yang ada, indentasi 3 spasi.
- Jangan menambah proyek test baru ke repo. Harness uji layar (Tahap 7) hidup di scratchpad dan tidak dikomit.
- Bila ada tindakan ditolak dengan `blocked by policy`: ikuti aturan di `CLAUDE.md` (skrip `.ps1` manual di `plan/login-material-3-manual/`, jangan mengulang lewat jalur lain, beri tahu pengguna di akhir).
- Update plan ini dengan mencentang butir yang selesai; saat selesai pindahkan ke `plan/executed/login-material-3.md` (hilangkan `.codex`).

## Tahap 1 — `BrandingInfo` dan enum (project `Em.Ui.Core`)

- [x] Buat `src/shared/Em.Ui.Core/Ui.Core/shared/LoginStyle.cs`: `public enum LoginStyle { Material, Classic }` dengan XML doc (Material = card di atas background, Classic = panel brand di kiri seperti sebelumnya).
- [x] Di `BrandingInfo.cs` tambahkan:
  - `public LoginStyle LoginStyle { get; set; } = LoginStyle.Material;`
  - `public string? LightLoginBackground { get; set; }` dan `public string? DarkLoginBackground { get; set; }` — lokasi gambar dengan tafsiran yang sama seperti `LogoSource` (WPF: pack URI atau URI absolut; hanya bitmap kecuali ada `ILogoImageLoader`). XML doc harus menyebut: hanya dipakai WPF dengan `LoginStyle.Material`; diabaikan oleh `Classic` dan oleh MAUI; aturan jika hanya satu yang diisi; ukuran gambar disarankan sekitar 1920 px lebar.
  - Method murni data `public (string? Source, bool IsBorrowed) ResolveLoginBackgrounds(ThemeVariant variant)` **tidak perlu**; pemuatan dan aturan jatuh-ke-mode-lain diurus di Tahap 5 karena bergantung pada berhasil-tidaknya gambar dimuat. Cukup sediakan properti.

## Tahap 2 — Token tema baru (`Em.Ui.Wpf.Core/Shared/ThemeColorExtensions.cs`)

`ThemeResources.Apply` baru menulis tujuh token. Tambahkan token berikut, ditulis dari `ThemeBase` yang aktif pada setiap `Apply` (aturan yang ada di komentar file tetap berlaku: kunci ini **tidak boleh** dideklarasikan di dictionary yang di-merge di level elemen).

- [x] Brush: `themePrimaryContainerBrush`, `themeOnPrimaryContainerBrush`, `themeSurfaceContainerLowBrush`, `themeSurfaceContainerBrush`, `themeOnSurfaceVariantBrush`, `themeOutlineBrush`, `themeOutlineVariantBrush`, `themeErrorBrush`, `themeErrorContainerBrush`, `themeOnErrorContainerBrush`, `themeScrimBrush`.
- [x] Color (untuk `GradientStop.Color`, yang tidak menerima Brush): `themePrimaryContainerColor`, `themeSurfaceContainerLowColor`, `themeSurfaceColor`.
- [x] Tambahkan konstanta nama di `ThemeResources` seperti yang sudah ada; `themePopupBackgroundBrush` (= `SurfaceContainerHigh`) tidak diubah.
- [x] Tidak perlu nilai bawaan untuk designer; layar yang ada juga membaca `themeAccentBrush` tanpa nilai bawaan.

## Tahap 3 — Style bersama baru (`Em.Ui.Wpf.Core/Styles/`)

Semua kunci **baru**; style lama (`filledButtonStyle`, `outlinedButtonStyle`, `fieldBoxStyle`, dst.) tidak diubah supaya layar lain tidak ikut berubah. Semua dimasukkan lewat file yang sudah di-merge `MaterialDesign.xaml` (jangan menambah file merge baru kecuali perlu). Warna hanya dari token tema (Tahap 2) dan brush netral yang sudah ada; **jangan memakai abu-abu `#808080` transparan** untuk permukaan, garis, dan teks baru. Baca `Buttons.xaml` sebelum menulis tombol: ia memakai kunci resource `buttonHoverBrush`/`buttonPressedBrush` di `Style.Resources` dan attached `controls:ButtonWait.OnAccent`; ikuti pola itu.

- [x] `Typography.xaml` — skala tipe Material 3, kunci baru: `typeHeadlineSmallStyle` (24/32 Regular), `typeTitleLargeStyle` (22/28 Medium), `typeTitleMediumStyle` (16/24 Medium), `typeBodyMediumStyle` (14/20 Regular), `typeBodySmallStyle` (12/16 Regular), `typeLabelLargeStyle` (14/20 Medium), `typeLabelSmallStyle` (11/16 Medium). Warna teks sekunder memakai `themeOnSurfaceVariantBrush` lewat setter/DynamicResource, bukan `Opacity`.
- [x] `Buttons.xaml` — `materialFilledButtonStyle` (tinggi 40, pil radius 20, `themeAccentBrush`/`themeOnAccentBrush`, **tanpa bayangan**, state layer hover/pressed/fokus, label `typeLabelLargeStyle`), `materialTonalButtonStyle` (`themePrimaryContainerBrush`/`themeOnPrimaryContainerBrush`, tinggi 40 pil), `materialIconButtonStyle` (lingkaran 40, tanpa outline, state layer, ikon `themeOnSurfaceVariantBrush`). Text button memakai `textButtonStyle` yang sudah ada kalau cukup.
- [x] `Inputs.xaml` — field outlined Material 3 dengan **label mengambang**, untuk `TextBox`, `PasswordBox`, dan `ComboBox`:
  - Tinggi 56, `CornerRadius` 4, border 1 px `themeOutlineBrush`; hover lebih tegas; fokus 2 px `themeAccentBrush`; error (`shared:FieldValidation.HasError` atau properti serupa) memakai `themeErrorBrush`.
  - Label berada di tengah field (ukuran 14–16) saat kosong dan tidak fokus; saat fokus atau berisi, label naik ke garis tepi atas (ukuran 12), dengan latar "takik" yang memotong garis. Warna latar takik bawaan `themeSurfaceContainerBrush` (warna card login) dan bisa diganti lewat attached property. Animasi pendek (sekitar 150 ms) mengikuti pola `EnableAnimation` yang sudah dipakai di repo (cari pemakaiannya); tanpa animasi label langsung pindah.
  - Teks label dibaca dari attached property baru (mis. `Em.Ui.Wpf.Shared.FieldLabel.Text`) supaya `Tag` tetap bebas. `PasswordBox` tidak punya `Text`: sediakan attached behavior yang menjaga penanda "ada isi" lewat event `PasswordChanged`, tanpa code-behind di layar pemakai.
  - **Tanpa ikon di depan field** (supaya label tidak perlu bergeser). `ComboBox` memakai chevron di kanan dan popup yang sama dengan `fieldPopupBorderStyle`/`fieldComboItemStyle` yang sudah ada.
  - Kunci: `materialTextFieldStyle`, `materialPasswordFieldStyle`, `materialComboFieldStyle`.
- [x] `Inputs.xaml` — `materialCheckBoxStyle` (kotak 18, `CornerRadius` 2, border 2 px `themeOutlineBrush`, centang di atas `themeAccentBrush`).
- [x] `Chips.xaml` — `materialAssistChipStyle` (`Border`, tinggi 32, `CornerRadius` 8, border 1 px `themeOutlineVariantBrush`, latar transparan, padding 12,0).

## Tahap 4 — Pisahkan layar login (Classic dan antarmuka bersama)

- [x] Pindahkan `LoginControlVm` dan `ServerProbeStatus` dari `LoginControl.xaml.cs` ke berkas sendiri `Navigations/LoginControlVm.cs` (namespace tetap `Em.Ui.Wpf.Navigations`, isi tidak diubah).
- [x] Ubah nama layar lama: `LoginControl.xaml(.cs)` → `LoginControlClassic.xaml(.cs)`, kelas `LoginControlClassic`, `x:Class` disesuaikan. **Isi XAML dan perilakunya tidak berubah** (hanya nama kelas); pakai `git mv` supaya riwayat terjaga.
- [x] Buat antarmuka `ILoginScreen` (di `Navigations/`, turunan `INavigationBody`, anggota `LoginControlVm Vm { get; }`). `LoginControlClassic` mengimplementasikannya.
- [x] Logika code-behind yang sama untuk kedua layar (sinkron `PasswordBox` ↔ `Vm.Password`, Enter berpindah dari username ke password, langganan `EmApp.UIConnections.CollectionChanged` → `Vm.SyncSelectedConnection`, `OnRelease`) **tidak boleh digandakan**: pindahkan ke helper internal bersama (atau kelas dasar) yang dipakai kedua layar. Perilaku Classic tidak boleh berubah.
- [x] `EmApp.MainWindowFlow.cs`: `_loginControl`, `ActiveLoginControl`, dan `Body is not LoginControl` memakai `ILoginScreen`. `TabbedMainWindow.OnThemeChanged` tetap memakai `.Vm`.
- [x] `EmApp.Statics.cs` (`InitInternalNavigation`): `BodyType` navigasi `admin.logon` dipilih menurut `Branding.LoginStyle` (`LoginControlMaterial` atau `LoginControlClassic`). Periksa apakah `Branding` sudah terisi saat `InitInternalNavigation` berjalan; bila belum, pilih tipe secara lazy lewat cara yang didukung `BodyType`/`BodyActivator.cs`. Catat temuannya di laporan.

## Tahap 5 — Layar baru `LoginControlMaterial`

Berkas: `Navigations/LoginControlMaterial.xaml(.cs)`, `UserControl` dengan constructor `(EmApp app)` seperti Classic, mengimplementasikan `ILoginScreen`, memakai `LoginControlVm` yang sama sebagai `DataContext`. Merge `MaterialDesign.xaml` seperti Classic. `MinWidth` 480, `MinHeight` 520.

**Perilaku harus setara Classic** (jangan dikurangi): semua binding dan command yang sama (`Commands[SignInCommand]`, `Commands[ChangeThemeCommand]`, `Commands[ConnectionConfigCommand]`, `ApiConnections`/`SelectedConnection`, `UserName`, `Password`, `RememberMe`, `SignInError`/`SignInErrorDetail`, `SessionEndedNotice`, `InWaiting`/`WaiterText`, `ProbeStatus`/`ServerStatusText`/`ServerStatusDetail`, `LightModeSelected`), urutan tab lokal (`TabIndex` 0–4, `KeyboardNavigation.TabNavigation="Local"`), `IsDefault` pada tombol Sign in, tombol tema yang `CommandParameter`-nya berganti menurut `LightModeSelected`, teks versi `v1.0.0` apa adanya. Caps Lock hint dan link "Forgot password?" **tidak** dibawa (di Classic keduanya tidak aktif; catat di laporan).

Struktur visual (urut dari belakang ke depan):

- [x] **Latar**: `Grid` dengan tiga lapis — (1) gradien `LinearGradientBrush` (Start 0,0 End 1,1; stop `themePrimaryContainerColor` 0, `themeSurfaceContainerLowColor` 0,5, `themeSurfaceColor` 1) lewat `DynamicResource`; (2) `Image` `Stretch="UniformToFill"` untuk background, disembunyikan bila tidak ada gambar; (3) `Rectangle` redup berisi `themeScrimBrush` dengan `Opacity` 0,35, tampil hanya bila gambar yang dipakai adalah gambar terang yang dipinjam mode gelap.
- [x] **Pill utilitas** kanan atas: `Border` `themeSurfaceContainerBrush`, radius 20, berisi tombol tema (`materialIconButtonStyle`, ikon `Solid_CircleHalfStroke`) dan tombol Connections (`materialTonalButtonStyle` atau text button, ikon `Solid_NetworkWired`).
- [x] **Card** di tengah (di dalam `ScrollViewer`): `Border` `MaxWidth` 440, `Padding` 32, `CornerRadius` 28, `Background` `themeSurfaceContainerBrush`, border 1 px `themeOutlineVariantBrush`, bayangan lembut (blur 24, depth 4, opacity 0,18). Isi, atas ke bawah:
  1. Kepala: tile 56×56 radius 16 `themePrimaryContainerBrush` berisi logo (`Image`, `Stretch="Uniform"`, padding 10–12, `HighQuality`); di sampingnya nama (`typeTitleLargeStyle`) dan tagline (`typeBodySmallStyle`, `themeOnSurfaceVariantBrush`), satu baris masing-masing dengan `CharacterEllipsis`.
  2. "Sign in" (`typeHeadlineSmallStyle`) dan kalimat pendukung (`typeBodyMediumStyle`, `themeOnSurfaceVariantBrush`).
  3. Banner error (`themeErrorContainerBrush`, teks `themeOnErrorContainerBrush`, radius 12, tanpa border, ikon `Solid_TriangleExclamation`, tooltip `SignInErrorDetail`) dan banner notice sesi berakhir (warna `warningChipBrush`, tanpa border), keduanya Collapsed menurut state VM seperti di Classic.
  4. Tiga field `materialComboFieldStyle` (Server connection, daftar `ApiConnections` dengan `ProfileName`), `materialTextFieldStyle` (Username), `materialPasswordFieldStyle` (Password), jarak antar field 16. Teks bantu "Pick the server you are signing in to — the button below stays off until you do." sebagai supporting text di bawah field server, memakai `Visibility` **Hidden** (bukan Collapsed) supaya tinggi card tidak melompat, tampil saat `SelectedConnection` null.
  5. `CheckBox` "Keep me signed in on this computer" (`materialCheckBoxStyle`).
  6. Tombol Sign in `materialFilledButtonStyle` selebar card, `IsDefault`.
  7. Progress linear 4 px (`themeAccentBrush` di atas track `themeOutlineVariantBrush`) dengan teks `WaiterText`, tampil saat `InWaiting`; ruangnya tetap dipesan (Hidden) agar card tidak melompat.
  8. Footer card: garis `themeOutlineVariantBrush`, lalu baris berisi chip status (`materialAssistChipStyle`; titik status 8 px dan warnanya mengikuti `ProbeStatus` seperti `serverStatusDotStyle` di Classic, titik berdenyut saat `Probing`; tooltip `ServerStatusDetail`) di kiri dan teks versi di kanan.
- [x] **Hak cipta** di bawah jendela: teks 11 px di atas pill `themeSurfaceContainerBrush` (opacity 0,85, radius 12, padding 10,4) supaya terbaca di atas gambar apa pun.

Code-behind dan pemuatan gambar:

- [x] `RenderBranding()` mengisi logo, nama, tagline, dan hak cipta dari `_app.Branding` (pakai `BrandingImages.LoadLogo`); `Description` tidak dipakai.
- [x] `RenderBackground()` dipanggil dari constructor **dan** setiap `EmApp.ThemeChanged` (langganan di constructor, lepas di `OnRelease`), karena tombol tema di layar ini mengganti tema saat layar tampil. Gradien mengikuti tema sendiri lewat `DynamicResource`; hanya gambar dan lapisan redup yang perlu dihitung ulang.
- [x] Tambahkan di `Shared/BrandingImages.cs` method `LoadLoginBackground(BrandingInfo branding, ThemeVariant variant, IServiceProvider services, out bool dimmed)`: urutan — gambar milik mode aktif; kalau kosong atau gagal dimuat, gambar mode lain; kalau keduanya tidak ada, `null`. `dimmed` bernilai true **hanya** bila `variant == Dark` dan yang dipakai adalah `LightLoginBackground`. Gunakan rantai `ILogoImageLoader` lalu bitmap seperti `TryLoad`, dengan cache terpisah dari cache logo (kunci diawali `bg:`). Batasi lebar decode paling besar 1920 piksel **tanpa memperbesar** gambar yang lebih kecil (baca ukuran asli lebih dulu, mis. lewat `BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, ...)`), lalu `Freeze()`. Kegagalan tidak boleh melempar (jatuh ke berikutnya).

## Tahap 6 — Dokumentasi

- [x] Buat `doc/engine-login-branding.md`: cara memilih `LoginStyle`, cara memasang `LightLoginBackground`/`DarkLoginBackground` (format, ukuran yang disarankan, pack URI contoh), aturan jatuh-ke-mode-lain dan peredupan, gradien bawaan, bahwa Classic mengabaikan background dan MAUI belum memakai properti ini, dan bahwa `Description` tidak tampil di Material. Jangan menyebut nama objek database.
- [x] `CLAUDE.md`: tambahkan satu paragraf "Pembaruan 2026-10-02 (login Material)" setelah paragraf Container Manager yang ada (jangan menghapus atau menimpa isi lain), berisi ringkasan, tautan ke `doc/engine-login-branding.md`, `doc/report/login-material-eksekusi.md`, dan `plan/executed/login-material-3.md`, serta hal yang belum teruji.

## Tahap 7 — Pengujian (setelah semua kode selesai)

- [x] `dotnet build src/frontend/Em.Ui.Wpf.slnx` harus lolos tanpa error baru. Bangun juga `src/frontend/Em.Ui.Maui.slnx` dan `src/backend/Em.Api.slnx` untuk memastikan perubahan `BrandingInfo` tidak merusak; jika MAUI gagal karena workload/SDK yang tidak terkait, catat dan jangan mengejarnya.
- [x] Harness layar tanpa server di scratchpad, mengikuti catatan di memori proyek "Uji layar WPF tanpa server" (proyek konsol STA `net10.0-windows`, `UseWPF`, `ProjectReference` ke `Em.Ui.Wpf.Core.csproj`, token tema ditulis lewat `ThemeResources.Apply` atau setara, `EmApp` tak-terinisialisasi dengan service palsu, render `RenderTargetBitmap` ke PNG; heredoc panjang di Bash kadang gagal, tulis skrip ke berkas). Build saja **tidak** memvalidasi XAML runtime, jadi harness wajib. Render dan periksa dengan mata:
  - `LoginControlMaterial` terang dan gelap, tanpa gambar (gradien) dan dengan gambar uji buatan sendiri (PNG di scratchpad): hanya Light diisi, hanya Dark diisi, keduanya diisi; pastikan lapisan redup hanya muncul pada kasus Dark meminjam Light.
  - Keadaan: normal, error, notice sesi berakhir, sibuk (progress), tanpa koneksi terpilih (supporting text tampil), chip status Probing/Connected/Unreachable, label field mengambang saat kosong/fokus/berisi, field error.
  - Lebar jendela 480 dan 1000, tinggi 520.
  - `LoginControlClassic` dimuat dan dirender tanpa exception dan **identik** secara visual dengan sebelum perubahan (bandingkan dengan render dari commit sebelum plan ini bila perlu).
  - Alur VM tanpa server (service palsu `ICredentialServices`): sign in gagal 401 memunculkan banner error, password dikosongkan, label field password kembali ke posisi kosong.
- [x] Verifikasi dengan `grep` bahwa XAML baru tidak memuat `#808080`/`#xx808080` untuk permukaan, garis, atau teks, dan tidak ada warna tema yang ditulis tetap selain nilai status yang memang sudah ada.

## Tahap 8 — Analisa dan review (sekali, di akhir)

- [x] Tinjau seluruh diff sebagai satu kesatuan: duplikasi logika antara dua layar, kunci resource yang bentrok dengan yang sudah ada (cari duplikat `x:Key` di `Styles/` dan di merge `MaterialDesign.xaml`), kebocoran langganan event (`ThemeChanged`, `CollectionChanged`) di `OnRelease`, perilaku `ActiveLoginControl` saat layar dibangun ulang tiap sesi berakhir, kontras card terhadap latar pada mode gelap (tambahkan border `themeOutlineVariantBrush` bila perlu), penggunaan memori gambar besar.
- [x] Perbaiki temuan yang jelas; yang tidak sempat dicatat di laporan.

## Laporan eksekusi dan penutup

- [x] Tulis `doc/report/login-material-eksekusi.md`: apa yang dikerjakan per tahap, penyimpangan dari plan beserta alasannya, temuan Tahap 4 soal `Branding` pada `InitInternalNavigation`, hasil build, apa yang sudah diuji lewat harness (sebut render apa saja), dan **yang belum teruji**: layar terhadap server nyata, ganti tema langsung saat layar tampil di window sungguhan (`TabbedMainWindow` dan layout satu halaman), gambar background asli dari aplikasi, MAUI.
- [x] Pindahkan plan ke `plan/executed/login-material-3.md`.
- [x] Satu commit di branch yang sedang aktif, tanpa push: judul dan deskripsi Bahasa Indonesia, hanya berkas milik plan ini (termasuk perpindahan plan dan laporan), tanpa berkas scratchpad atau harness.
- [x] Di pesan penutup ke pengguna, bedakan pekerjaan yang selesai dari verifikasi manual yang masih tertunda; jangan menyatakan verifikasi yang belum dijalankan sudah lulus.

## Di luar cakupan

MAUI; memakai token tema baru di layar lain (`UserEditor`, `UserManager`, dll.); Caps Lock hint dan pemulihan password; pemuat SVG; mengubah `Description`/`Copyright` di `BrandingInfo`; proyek test baru; mengubah host (`Em.Ui.Wpf`) agar memasang gambar background.

## Hasil eksekusi 2026-10-02

Implementasi, build tiga solution, harness runtime 46 render layar, dan review akhir selesai. Detail hasil serta batas verifikasi: [laporan eksekusi](../../doc/report/login-material-eksekusi.md). Verifikasi terhadap server nyata, kedua host window sungguhan, background asli aplikasi, UI MAUI, dan interaksi pointer lengkap tetap belum dilakukan sebagaimana dicatat pada laporan. Tidak ada tindakan terblokir policy.
