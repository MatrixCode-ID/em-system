# Plan — Container Manager (WPF)

Status: **sudah dieksekusi 2026-10-02** — hasil, penyimpangan, dan yang belum teruji: [../../doc/report/container-manager-wpf-eksekusi.md](../../doc/report/container-manager-wpf-eksekusi.md)
Dibuat: 2026-10-02
Baseline: commit `4442c3a` (container registry tahap 1), branch `claude/gracious-ramanujan-2h62yg`
Lingkup: **hanya client WPF** (`Em.Ui.Wpf.Core`). Backend dan kontrak `ICtnServices` sudah ada dan
tidak diubah. MAUI tidak mendapat layar ini (sama dengan CDN/User/Role Manager).

---

## Kenapa ada berkas ini

Backend registry sudah jadi ([plan/executed/container-registry.md](container-registry.md),
[doc/engine-registry.md](../../doc/engine-registry.md)), tetapi root, folder, container, robot, dan
hak robot baru bisa dibuat lewat HTTP manual. Plan ini membangun layar **Container Manager** di client
WPF yang memanggil `ICtnServices` (16 action: root, folder, container, manifest, robot, hak).

Layar ini mengikuti pola **CDN Manager** (`Navigations/CdnManager.xaml(.cs)`): layar bawaan
`Em.Ui.Wpf.Core`, ViewModel `MvvmModelBase` dengan `RegisterCommand`, service client tipis di
`Api.Core/`, navigasi diikat ke claim lewat `RequireClaim`, dan keadaan "registry tidak dinyalakan di
server" ditangani dari jawaban 404.

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Lokasi kode | Layar bawaan di **`Em.Ui.Wpf.Core`**: navigasi `admin.container`, `Kind = Manager`, masuk daftar Tools di home (`EmApp.cs` daftar `admin.*`). |
| 2 | Claim | Claim internal **`Administrative Tools:Container Manager Access`** (sama dengan `ICtnServices.CtnClaim` di module `Administrative Tools`), didaftarkan lewat `InitInternalClaims`, dan navigasi diikat dengan `RequireClaim`. Hanya satu claim untuk seluruh layar, seperti server. |
| 3 | Service client | `Api.Core/CtnService.cs`: `[Module(Defaults.AdministrativeToolsModuleName)] class CtnService(EmApp) : ServiceWpfBase, ICtnServices`, satu baris per action seperti `CdnService`, didaftarkan di `InitInternalServices`. Dibuat tangan (skill `scaffold-sdktables` untuk tabel, bukan untuk ini). |
| 4 | Struktur layar | **Dua tab**: **Containers** dan **Robots** (`materialTabControlStyle`). Satu `ContainerManagerVm`, dipecah menjadi `partial` per tab supaya tidak menjadi satu berkas 1.400 baris seperti CDN. |
| 5 | Tab Containers | **Tiga panel**: daftar **root** (kiri) · **tree folder dan container** milik root terpilih (tengah) · **detail** item terpilih (kanan). |
| 6 | Tab Robots | **Dua panel**: daftar robot (kiri) · detail robot dengan tabel **hak per root** (kanan). |
| 7 | Tree | Server mengirim `CtnTree` datar; client menyusunnya menjadi `TreeView` (folder dulu, lalu container, urut nama). Folder bisa dibuka-tutup, kedalaman maks 8 (batas server). |
| 8 | Keadaan nonaktif | 404 pada `GetMeta_CtnRoots` = registry tidak dinyalakan: kedua tab menampilkan keadaan kosong "Container registry is not enabled on this server." Menu tetap terdaftar (sama dengan CDN). |
| 9 | Token robot | Ditampilkan **sekali** di dialog `CtnTokenDialog` (banner peringatan, tombol Copy token, Copy `docker login`). Token tidak disimpan di VM setelah dialog ditutup dan tidak ditulis ke log. |
| 10 | Host registry | Diambil dari `EmApp.ActiveConnection.Host` (seperti `CopyLinkCommand` di CDN): skema dibuang, sisanya `host[:port]` dipakai untuk perintah `docker login/pull/tag`. Server tidak mengirim host. |
| 11 | Validasi | Client memeriksa nama **ringan** (pola `^[a-z0-9]+(?:(?:\.\|_\|__\|-+)[a-z0-9]+)*$`, panjang root 64 / container 128 / robot 100 / folder 100 / deskripsi 500) supaya tombol OK tidak aktif untuk nama jelas salah. **Server tetap otoritas**: 400/409 ditampilkan apa adanya lewat `EmMessageBox`. Konstanta ditulis ulang di client (`CtnNames` server `internal`, tidak bisa direferensikan). |
| 12 | Nama tidak bisa diubah | Root, container, dan robot **tidak punya action rename** (hanya deskripsi/status; folder punya rename). Dialog edit menampilkan nama sebagai teks baca-saja. |
| 13 | Hapus | Selalu lewat konfirmasi. Teks konfirmasi image/root/robot menyebut bahwa **hanya metadata yang dihapus; berkas layer di disk menunggu garbage collection (tahap 2)** — supaya pengguna tidak mengira ruang disk langsung bebas. |
| 14 | Pindah | **Drag and drop** (folder dan container ke folder lain di root yang sama, atau ke baris root-nya) **plus** perintah "Move to…" dengan `CtnFolderPickerDialog`. Pindah tidak mengubah nama pull; UI menyebutnya di dialog. |
| 15 | Copy perintah | Detail container menyediakan **Copy pull name** (`host/root/nama`), **Copy `docker pull`** dan **Copy `docker tag` + `docker push`**, per tag dan per digest. |
| 16 | Refresh | Tidak ada polling. Refresh manual dan `OnReloadRequested`. Setelah 404/409 akibat perubahan orang lain, layar membaca ulang bagian yang terkena. |
| 17 | Commit | Satu commit per fase (pesan Bahasa Indonesia) setelah `dotnet build src/frontend/Em.Ui.Wpf.slnx` bersih. Tidak ada PR kecuali diminta. |

### Batas kontrak yang dipatuhi (jangan dibuat tampil seolah ada)

Hal ini dibaca dari `ICtnServices`/`CtnDtos.cs` dan menentukan apa yang **tidak** boleh ada di UI:

- **Tidak ada ukuran image/layer.** `CtnManifestInfo.Size` hanya ukuran manifest, bukan layer. UI
  tidak menampilkan "image size"; kolom itu diberi label **Manifest size** atau dibuang.
- **Tidak ada hapus tag atau manifest.** Hanya hapus container utuh. Tombol hapus tag ditunda ke
  tahap 2 registry (retensi/GC), bukan dibuat dengan menyiasati.
- **Tidak ada daftar "siapa punya hak di root X".** Diturunkan di client dari `GetMeta_CtnRobots`
  (`CtnRobotInfo.Roots`), dipakai untuk panel detail root ("Robots with access").
- **Tidak ada `LastPushed` di `CtnImageInfo`.** Diambil dari manifest terbaru saat container dibuka.
- **Struktur tree datar.** Menghapus folder yang tidak kosong dijawab 409; UI menonaktifkan tombol
  untuk folder yang jelas masih berisi (berdasarkan tree di memori) tetapi tetap menangani 409.
- **Token tidak pernah kembali** dari `GetMeta_CtnRobots` (hanya `TokenPrefix`). Lupa token =
  *Regenerate token*; token lama langsung tidak berlaku (konfirmasi wajib menyebut ini).

## Tata letak

```text
┌ Container Manager ──────────────────────────────────────────────────────────┐
│ [Containers] [Robots]                       ⟳ Refresh                       │
├──────────────┬───────────────────────────────────┬──────────────────────────┤
│ ROOTS        │ acme                       + Folder│ acme/api                 │
│ ● acme   12  │ ▾ 📁 services                + Image│ Description …            │
│ ● server  3  │    ▸ 🐳 api         3 tags          │ [Active] 3 tags · 5 mnf  │
│ ○ legacy  0  │    ▸ 🐳 worker      1 tag           │ Pull: host/acme/api      │
│ + New root   │ ▸ 📁 tools                          │ ── Manifests ──          │
│              │ 🐳 base                              │ v1, latest  sha256:ab…   │
│              │                                      │   pushed 02 Okt · ci-bot │
└──────────────┴───────────────────────────────────┴──────────────────────────┘
```

- **Root** ditandai disc bertint (`EntityTintConverter` dari nama, ikon `Solid_Cubes`), badge jumlah
  image, dan chip **Disabled** untuk root nonaktif.
- **Tree**: folder `Solid_Folder`, container `Solid_Box` (nonaktif = abu-abu + chip). Menu konteks:
  New folder, New container, Rename, Move to…, Edit, Delete. Baris diseleksi satu per satu.
- **Detail container**: deskripsi, status, jumlah tag/manifest, nama pull, daftar manifest (tag
  sebagai chip, digest dipotong 12 karakter dengan Copy digest penuh, media type, ukuran manifest,
  waktu push dalam waktu lokal, `PushedBy` atau "deleted robot" bila `null`), tombol Edit, Move,
  Delete.
- **Detail root** (saat root dipilih tanpa item): deskripsi, status, jumlah folder/image, dan
  "Robots with access" (nama + chip R/W, klik melompat ke tab Robots).
- **Tab Robots**: daftar robot (disc bertint, nama, chip status/kedaluwarsa); detail memuat deskripsi,
  `TokenPrefix…`, masa berlaku, last used (waktu lokal, atau "never"), tombol **Edit**, **Regenerate
  token**, **Delete**, lalu tabel **Access** — satu baris per root dengan `ComboBox`
  *No access / Read / Write*. Mengganti pilihan langsung memanggil `PostMeta_CtnRobotRootSet` atau
  `PostMeta_CtnRobotRootRevoke`, dengan indikator per baris dan rollback pilihan jika gagal.
- Chip masa berlaku: lewat = merah (Expired), < 14 hari = kuning, selainnya netral.

## Berkas yang dibuat/diubah

Semua di `src/shared/Em.Ui.Wpf.Core/` kecuali disebut lain.

| Berkas | Isi |
| --- | --- |
| `Api.Core/CtnService.cs` | Client `ICtnServices`, satu baris per action. |
| `Navigations/ContainerManager.xaml` | Tab, tiga panel Containers, dua panel Robots, keadaan kosong/nonaktif. |
| `Navigations/ContainerManager.xaml.cs` | `ContainerManager : UserControl, INavigationBody` + item model (`CtnRootItem`, `CtnTreeNode`, `CtnManifestItem`, `CtnRobotItem`, `CtnRobotRootItem`). |
| `Navigations/ContainerManagerVm.Containers.cs` | Bagian `partial` VM untuk tab Containers: load, tree, command root/folder/image, drag-drop. |
| `Navigations/ContainerManagerVm.Robots.cs` | Bagian `partial` VM untuk tab Robots: load, command robot, hak. |
| `Dialogs/CtnRootDialog.xaml(.cs)` | Buat/edit root (nama hanya saat membuat, deskripsi, Active). |
| `Dialogs/CtnImageDialog.xaml(.cs)` | Buat/edit container (nama saat membuat, deskripsi, Active). |
| `Dialogs/CtnRobotDialog.xaml(.cs)` | Buat/edit robot (nama saat membuat, deskripsi, Active, masa berlaku opsional). |
| `Dialogs/CtnTokenDialog.xaml(.cs)` | Tampil sekali token + Copy; mengikuti aturan tombol jawaban (`confirmAnswerButtonStyle`). |
| `Dialogs/CtnFolderPickerDialog.xaml(.cs)` | Pilih folder tujuan pindah (tree satu root, plus "(root)"). |
| `Core/EmApp.Statics.cs` | `ContainerManagerClaim`, `AddInternalClaim`, `AddNavigation(RequireClaim(…"admin.container"…))`, `AddSingleton<ICtnServices, CtnService>()`. Ikon `Solid_Cubes`. |
| `Core/EmApp.cs` | Tambah `"admin.container"` ke daftar tool home (baris 213). |
| `README`/`claude.md` | Catatan status di akhir eksekusi (bagian Dokumentasi). |

Dialog teks sederhana (nama folder) memakai `TextInputDialog` yang ada; konfirmasi memakai `EmMessageBox`.

## Fase eksekusi

Tiap fase berakhir dengan build bersih dan commit sendiri.

### Fase 1 — Fondasi: service, navigasi, claim, kerangka layar

1. `CtnService` + registrasi DI.
2. Claim internal + navigasi `admin.container` + masuk daftar tool.
3. `ContainerManager` kosong dengan dua tab, `OnReloadRequested`, deteksi 404 → keadaan nonaktif.
4. Build `src/frontend/Em.Ui.Wpf.slnx`.

### Fase 2 — Tab Containers (baca)

1. Muat root (`GetMeta_CtnRoots`), pilih root pertama, muat tree (`GetMeta_CtnTree`) dan susun `TreeView`.
2. Panel detail root dan detail container (`GetMeta_CtnImageManifests` dimuat saat container dipilih,
   dengan pembatalan kalau pilihan berganti cepat).
3. Copy name / pull / push, tampilan waktu lokal, keadaan kosong (root tanpa isi).

### Fase 3 — Tab Containers (ubah)

1. Root: New/Edit/Delete (409 → pesan "root masih berisi folder, image, atau hak robot").
2. Folder: New/Rename/Delete, Move (picker dan drag-drop).
3. Container: New/Edit/Delete, Move (picker dan drag-drop).
4. Penanganan 400/404/409 seragam: pesan server ditampilkan, lalu data dibaca ulang.

### Fase 4 — Tab Robots

1. Muat robot (`GetMeta_CtnRobots`) dan gabungkan dengan daftar root untuk tabel hak.
2. New robot → `CtnTokenDialog`; Regenerate → konfirmasi → `CtnTokenDialog`.
3. Edit/Delete robot; ubah hak langsung per baris (Set/Revoke) dengan rollback.
4. Panel "Robots with access" di detail root, tersambung ke tab ini.

### Fase 5 — Poles dan dokumentasi

1. Tema terang/gelap, ukuran jendela kecil, tab detach (jendela terpisah) — cek lewat host.
2. Tab/keyboard: Enter/Esc di dialog, F5 = Refresh, Delete = hapus baris terpilih (dengan konfirmasi).
3. Perbarui `doc/engine-registry.md` (hapus "UI WPF belum ada", jelaskan layar) dan catatan di
   `claude.md` (satu paragraf "Pembaruan", tanpa menghapus isi lama).
4. Laporan eksekusi `doc/report/container-manager-wpf-eksekusi.md`, plan dipindah ke `plan/executed/`.

## Pengujian

Layar ini hanya bisa dijalankan di Windows. Lingkungan cloud (Linux) tidak menjalankan WPF.

1. **Build**: `dotnet build src/frontend/Em.Ui.Wpf.slnx` (dan `Em.Api.slnx` bila ada penyentuhan
   backend, seharusnya tidak). Di Linux dicoba dengan `-p:EnableWindowsTargeting=true` hanya untuk
   memastikan XAML/C# terkompilasi; hasil itu **bukan** bukti layar berjalan.
2. **Uji manual di Windows terhadap `Em.Api` + SQL Server uji** (skrip `Ctn.sql` sudah dijalankan;
   lihat catatan di `doc/engine-registry.md` bahwa DDL belum pernah diuji di SQL Server — layar ini
   sekaligus ujinya). Skenario wajib:
   - Akun tanpa claim: menu tidak muncul; dengan claim: muncul; admin: muncul.
   - Server tanpa `AddContainerRegistry`: keadaan nonaktif di kedua tab.
   - Buat root `acme` dan `server`, folder bersarang, container; nama tidak sah (huruf besar, tiga
     segmen) ditolak (klien dan server); nama ganda → 409 ditampilkan.
   - Hapus root/folder tidak kosong → 409 ditampilkan; hapus container dengan konfirmasi.
   - Pindah container dan folder (picker dan drag-drop), cek nama pull tidak berubah.
   - Robot: buat → token tampil sekali → `docker login localhost:5132 -u <robot> -p <token>` berhasil;
     beri `W` di `acme` dan `R` di `server`; push ke `acme/api` berhasil, push ke `server/*` ditolak
     (`DENIED`); Regenerate → token lama gagal login; Revoke → `NAME_UNKNOWN`.
   - Manifest: setelah push, tag dan digest muncul di detail; `PushedBy` terisi nama robot; hapus
     robot → `PushedBy` jadi "deleted robot".
   - Dua client sekaligus: ubah dari client A, refresh di client B, aksi kedaluwarsa di B menghasilkan
     pesan yang jelas, bukan crash.
   - Tema gelap/terang dan jendela diperkecil.
3. **Tidak ada unit test baru** kecuali logika murni yang diekstrak (penyusun tree dari daftar datar,
   validasi nama, pemformat perintah docker) — bila repo sudah punya project test untuk Wpf.Core,
   tambahkan di sana; bila belum, jangan membuat project test baru dalam plan ini dan catat di laporan.

## Risiko dan hal yang perlu diperhatikan

- **Rename tidak ada** (butir 12): pengguna yang salah mengetik nama root/container harus
  membuat ulang dan memindahkan. Ini keterbatasan kontrak; bila dirasa perlu, itu perubahan backend
  di plan terpisah.
- **Skema DDL SQL Server belum teruji** — masalah bisa muncul pertama kali di layar ini. Catat, jangan
  disembunyikan.
- **`Host` koneksi bisa `http://` non-localhost**: perintah `docker login` yang disalin tidak akan
  bekerja (Docker mewajibkan HTTPS selain `localhost`). Detail container menampilkan catatan kecil
  saat host bukan HTTPS dan bukan `localhost`.
- **Claim ini setara kendali penuh atas registry** (lihat `ICtnServices`): layar tidak perlu
  menyembunyikan apa pun, tetapi teks bantuan di Role Manager sebaiknya tidak menyarankan memberikan
  claim ini kecuali ke administrator (cek apakah ada deskripsi claim yang bisa diisi).
- **Tahap 2 registry** (GC, retensi, kuota, audit) akan menambah kontrak baru; layar dirancang agar
  panel detail container mudah ditambah bagian (tab di panel kanan), tanpa perubahan struktur.

## Butir terbuka dengan nilai bawaan

Mudah diubah; dipakai bila tidak ada keputusan lain saat eksekusi.

| Butir | Nilai bawaan |
| --- | --- |
| Ikon untuk menu | `Solid_Cubes` (alternatif `Solid_Box`). |
| Tab awal | **Containers**. |
| Root nonaktif | Tetap tampil di daftar, abu-abu dengan chip **Disabled**. |
| Masa berlaku token di dialog | Kosong = tidak kedaluwarsa; pilihan cepat 30/90/365 hari ditambah tanggal bebas. |
| Drag-drop ke root lain | **Tidak diizinkan** (server hanya memindah di root yang sama); kursor "tidak boleh" dan pesan di status. |
| Jumlah baris manifest | Muat semua (tidak ada paging di kontrak); jika terasa lambat, jadikan catatan untuk tahap 2. |
