# Laporan eksekusi — rapikan UI NuGet Manager dan Container Manager (acuan UserManager)

Tanggal: 2026-10-04. Permintaan langsung pengguna ("rapihkan design UI agar konsisten dengan UserManager"), tanpa plan tertulis.
Dikerjakan langsung oleh Claude (Codex sempat dijalankan lalu dihentikan atas permintaan pengguna sebelum mengubah apa pun).
Dasar: working tree sudah berisi perapian bagian Publish dari sesi Codex lain (`doc/report/manager-toolbar-wpf.codex.md`);
pekerjaan ini membangun di atasnya dan menimpanya bila bertentangan.

## Keputusan desain (diambil sendiri karena pertanyaan ke pengguna ditolak)

| Topik | Keputusan |
| --- | --- |
| Header NuGet Manager | Tiga card di atas tab diganti satu **toolbar feed** (combobox feed + refresh, **New Feed** filled, menu More, chip status, switch **Server on**, refresh server) dan satu strip ringkas bagi feed terpilih (alamat + copy, storage, Feed enabled, Anonymous read, refresh). Form Slug/Name/Description pindah ke dialog `NuPakFeedDialog` (New/Edit). |
| Tab bersarang Publish | Tidak ada tab di dalam tab. `PublishView` memakai **segmented control Publish / History** di toolbarnya (bukan dua `UserControl` terpisah, supaya status profil tidak terduplikasi). Menyimpang dari opsi "dua tab luar" yang saya rekomendasikan; alasannya satu instance view dengan satu daftar profil. |
| Daftar | Daftar bernilai banyak (Recycle Bin, Audit, artifact Publish) menjadi **tabel**: header UPPERCASE, kolom lebar tetap + satu kolom `*`, baris ber-divider gaya UserManager. Daftar kecil (Prefixes, Packages, Versions, Robot access, Profiles, Runs) memakai baris dua-baris `listRowCompactStyle`. |
| Container Manager | Aksi pindah ke **toolbar** di atas tiga panel: New Root (filled), New Folder, New Container, Edit, Move to..., Delete, chip storage + refresh storage, Refresh. Edit dan Delete ada dua versi (root vs folder/container) dalam satu sel dengan visibilitas bergantian, jadi posisi tombol tidak bergeser. Tombol aksi di panel detail dan header panel tree dihapus. |
| Card storage settings | `StorageSettingsCard` dipindah dari atas tab Containers ke tab **Settings** tersendiri di Container Manager (NuGet Manager sudah punya tab Settings) dan dirapikan memakai style bersama. |
| Refresh | Semua tombol "↻" teks diganti ikon `Solid_ArrowsRotate` (`rowActionButtonStyle`) dengan tooltip dan nama aksesibilitas; keadaan loading ditunjukkan lewat disabled, bukan lewat penggantian isi tombol. |

## Perubahan

- `Styles/Collections.xaml`: `listRowStyle` dan `listRowCompactStyle` (baris ListBoxItem gaya UserManager, tetap bertema saat disabled); merge `Palette.xaml`.
- `Styles/Buttons.xaml`: `toolbarSeparatorStyle`.
- `Navigations/NuPakManager.xaml` (ditulis ulang), `NuPakManager.xaml.cs` (dialog feed, `FeedMoreClick`, tanpa pengubahan `Content` tombol), `NuPakFeedDialog.cs` (baru).
- `Navigations/Publish/PublishView.xaml` (ditulis ulang), `PublishView.xaml.cs` (`PageChanged`, teks tombol Prepare/Build lewat `TextBlock`).
- `Navigations/ContainerManager.xaml`: toolbar, tab Settings, aksi panel dihapus.
- `Controls/StorageSettingsCard.xaml` (+ `.xaml.cs`): card bertema, input `fieldBoxStyle`, tombol ikon.
- Harness: `scripts/container-manager-render` (baru), `scripts/publish-render` (memakai segmented control), `scripts/storage-settings-render` (toleransi redup untuk input disabled; tetap menolak putih sistem).
- Tidak ada perubahan backend, layanan, skema DB, atau logika view model.

## Hasil verifikasi

- `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `src/frontend/Em.Ui.Maui.slnx`: lulus, 0 warning.
- `scripts/nupak-manager-render` lulus (terang/gelap: ready, busy, off, tiap tab, feed-loading, feed-switched, nol feed, tanpa hak, error/retry); semua PNG tab dilihat langsung.
- `scripts/publish-render` lulus (42 PNG, termasuk sempit 900 px dan History); `scripts/storage-settings-render` lulus; `scripts/container-manager-render` lulus (ready, tiap tab, root/folder/container terpilih, sempit, disabled, registry nonaktif; terang dan gelap). PNG di `..\.artefacts\em-system\{publish-render,container-manager-render}` dan `scripts/nupak-manager-render/bin/Debug/net10.0-windows`.

## Belum diverifikasi / batas yang diketahui

- Interaksi nyata dengan mouse dan keyboard: menu More (feed dan profil), dialog New/Edit feed, drag-drop paket ke Publish, fokus keyboard, double click.
- Layar terhadap server nyata (NuGet, registry); semua render memakai service palsu.
- Transisi busy cepat pada window aplikasi nyata (harness hanya menguji keadaan disabled statis).
- Editor `StorageSettingsCard` saat terbuka (Expander dibuka, mode managed) tidak dirender; hanya keadaan awal "loading" yang diperiksa visual, dan uji warna input disabled lulus.
- `PublishProfileDialog` (form dibangun lewat refleksi) dan `PublisherSettingsDialog` tidak dirombak lagi; hanya perapian Codex sebelumnya.
- Scrollbar horizontal (toolbar Publish dan baris operasi saat lebar < ~1000 px) memakai scrollbar bawaan sistem, belum bertema (tampak terang pada tema gelap di harness). Pada lebar ≥ 1000 px tidak muncul. Lihat `doc/ideas/ui-manager-style-bersama.md`.
- Tidak di-commit (tidak diminta). Perubahan dari sesi Codex lain pada `claude.md` dan `doc/report/manager-toolbar-wpf.codex.md` ikut berada di working tree.
