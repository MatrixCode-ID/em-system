# Framework status bar

- Tanggal: 2026-10-08
- Status: diskusi

## Yang sudah ada

UI status bar di bagian bawah `TabbedMainWindow` (hanya window utama, tidak di window hasil tear-off):

- Control lookless `Em.Ui.Wpf.Controls.EmStatusBar` (style default di `Themes/Generic.xaml`):
  `Text` di kiri, `LeftItems` setelahnya, `RightItems` rata kanan.
- Isinya dari `EmApp.MainWindow.Vm.StatusBar` (`MainStatusBarVm`): `IsVisible`, `Text` (bawaan
  "Ready"), `LeftItems`, `RightItems`. Item boleh string, `UIElement`, atau objek ber-data template.

## Diskusi 2026-10-08

Usulan dua sumbu: **pemilik** (system / module) dan **jenis** (indikator menetap, pesan sementara,
aktivitas berprogres, pemberitahuan yang butuh tindakan). Status seperti update adalah satu item ber-id
tetap yang berganti keadaan (checking → tersedia → downloading → siap/gagal), bukan beberapa item terpisah.

Keputusan sementara pengguna:

- **Slot system tetap**: engine menyediakan slot system yang selalu punya tempat (mis. update). Update
  ditampilkan sebagai satu ikon + label, dengan context menu (Check, Cancel), dan ikon beranimasi
  selama proses berjalan.
- **Status bersifat global**, tidak ada status per tab/layar. Module hanya mendorong (push) status ke GUI.
- **Mekanisme update ditunda**; slot-nya saja yang dipikirkan sekarang (pembagian dengan Launcher, jadwal
  cek, dsb. belum dibahas).
- **MAUI tidak mendapat status bar**; ini fitur desktop.
- **Isi slot system**: server (koneksi) dan update. User aktif **tidak** masuk status bar karena
  sudah tampil di bagian atas window.
- **Slot update ringkas**: hanya ikon; saat updating ikon beranimasi + persen (mis. `⟳ 45%`).
  Tindakan lewat context menu (Check, Cancel). Usulan agent (belum diputuskan): detail keadaan
  (versi baru, "Up to date", pesan gagal) ditampilkan di tooltip.
- **Posisi slot system tetap** (urutan dan tempat tidak berubah).

### Versi produk di slot update

- Slot update juga menampilkan **versi produk** (bukan versi engine/server) saat idle; teksnya berganti
  persen saat updating.
- Sumber versi: **tag rilis** (usulan pengguna). Build rilis (GitHub Action atau alat rilis lain) mengirim
  versi dari tag ke build (`-p:Version=...`), yang masuk ke `AssemblyInformationalVersion` dan dibaca engine
  dari entry assembly saat runtime. Build lokal tanpa tag memakai versi penanda dev dan ditampilkan sebagai
  `dev` (keputusan pengguna). Build yang dirilis selalu lewat pipeline rilis bertag, jadi `dev` hanya
  muncul di build lokal.
- Catatan diskusi: build dev tidak boleh ikut alur update otomatis; format tag perlu divalidasi semver;
  bila host produk dirilis terpisah, tag perlu prefix per host (belum diputuskan).
- Host API produk dirilis sebagai container lewat publisher Container Manager (registry built-in). Di sana
  "tag" = version tag image. Publisher sekarang `docker build` dengan `--build-arg` dari profile, tetapi
  version tag belum diteruskan ke build, jadi versi di assembly belum mengikuti tag image. Diputuskan dan
  sudah dikerjakan (lihat di bawah; Compose ternyata cukup lewat `--build-arg`): Dockerfile menerima `ARG APP_VERSION=0.0.0-dev` dan meneruskan `-p:Version` ke
  `dotnet publish`; publisher otomatis mengisi `APP_VERSION` dari version tag (mode Compose lewat
  `args:` di compose + variabel lingkungan). Versi server bisa dikirim ke client (mis. lewat handshake)
  untuk tooltip/About.

### Sudah dikerjakan (2026-10-08, permintaan langsung pengguna)

- `Em.Shared.AppVersion` (Em.Libs) membaca versi produk; `0.0.0-dev` tampil `dev`.
- `EmStatusBar.SystemItems` + `MainStatusBarVm.SystemItems` (read-only untuk module) dengan slot pertama
  `StatusVersionItem` (teks, tooltip, context menu Copy version).
- Host `Em.Api`/`Em.Ui.Wpf` mendeklarasikan `<Version>0.0.0-dev</Version>`; produk mengatur sendiri
  (keputusan pengguna).
- Publisher: `APP_VERSION` otomatis untuk Dockerfile/Compose, `-p:Version` untuk Template; Dockerfile Em.Api
  memakai `ARG APP_VERSION`. Panduan: `doc/engine/build.md#product-version`.
- Keputusan lanjutan pengguna: versi bawaan tetap diset manual oleh host produk (`<Version>0.0.0-dev</Version>`
  di project host); tanpa itu SDK mengisi `1.0.0` dan tampil `v1.0.0`. Tidak ada file versi khusus atau
  props bawaan dari paket engine; pengisian versi rilis diotomatiskan oleh alat rilis nanti.
- Belum: slot server dan update, versi server ke client (handshake), push status dari module.

## Pertanyaan terbuka (framework)

- Siapa yang boleh mengisi: host saja, module, atau layar aktif (item per tab yang ikut berganti saat
  tab berpindah)?
- Bentuk item standar: teks, ikon + teks, indikator progres, tombol? Perlu model item (`StatusItem`)
  dengan prioritas/urutan dan id agar bisa diganti/dihapus?
- Pesan sementara (mis. "Saved" hilang setelah beberapa detik) dan tingkat pesan (info/warning/error).
- Item bawaan engine yang layak tampil: koneksi server, user aktif, versi aplikasi, jumlah task berjalan.
- Akses dari thread non-UI.
- MAUI: perlu padanan atau tidak.
