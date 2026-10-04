# Report — eksekusi Container Manager (WPF)

Penutup eksekusi [plan Container Manager (WPF)](../../plan/executed/container-manager-wpf.md), 2026-10-02.
Cara memakai layarnya: bagian "Container Manager (WPF)" di [../engine-registry.md](../engine-registry.md).
Backend dan kontrak `ICtnServices` tidak diubah; MAUI tidak mendapat layar ini.

## Yang dibangun

Semua di `src/shared/Em.Ui.Wpf.Core/`. Satu commit per fase (build `Em.Ui.Wpf.slnx` bersih di tiap commit).

| Fase | Hasil |
| --- | --- |
| 1 | `Api.Core/CtnService.cs` (satu baris per action), claim internal `Administrative Tools:Container Manager Access`, navigasi `admin.container` (ikon `Solid_Cubes`) di daftar Tools home, kerangka `ContainerManager` dua tab dengan keadaan "registry tidak dinyalakan" dari 404, `Shared/CtnInput.cs` (aturan nama ringan, bentuk perintah docker, host dari koneksi aktif) |
| 2 | Tab Containers (baca): daftar root, tree yang disusun dari daftar datar server (folder dulu, urut nama), detail root/folder/container, manifest dimuat saat container dipilih (jawaban usang dibuang dengan nomor versi), salin nama pull / `docker pull` per tag dan digest / `docker tag` + `push` |
| 3 | Tab Containers (ubah): root, folder, container (buat/edit/hapus), pindah lewat `CtnFolderPickerDialog` dan drag-drop, dialog `CtnRootDialog`/`CtnImageDialog`, jawaban 400/404/409 ditampilkan apa adanya lalu bagian layar yang terkena dibaca ulang |
| 4 | Tab Robots: daftar robot, `CtnRobotDialog` (Create/Edit/Regenerate), `CtnTokenDialog` (token tampil sekali), tabel hak per root yang langsung mengirim perubahan dengan indikator per baris dan rollback, panel "Robots with access" di detail root yang melompat ke tab Robots |
| 5 | F5 muat ulang, Delete hapus baris terpilih (dengan konfirmasi), F2 ganti nama/edit, klik kanan memilih baris dulu; `doc/engine-registry.md` diperbarui; laporan ini |

Berkas: `Navigations/ContainerManager.xaml(.cs)`, `ContainerManagerVm.Containers.cs`, `ContainerManagerVm.Containers.Edit.cs`,
`ContainerManagerVm.Robots.cs`, `ContainerManagerItems.Robots.cs`; `Dialogs/Ctn*Dialog.xaml(.cs)` dan `CtnFormVmBase.cs`.

## Yang berubah dari plan

- **Model item robot** ada di berkas sendiri (`ContainerManagerItems.Robots.cs`), bukan di `ContainerManager.xaml.cs`;
  VM tab Containers dipecah satu lagi (`...Containers.Edit.cs`) supaya tidak menjadi berkas 700 baris.
- **Edit folder dan container satu command** (`EditNodeCommand`; tulisan tombolnya `Rename` untuk folder, `Edit` untuk
  container). Menu konteks dan tombol bekerja pada baris yang terpilih, tanpa parameter; klik kanan memilih baris dulu.
- **Konfirmasi Regenerate digabung ke dialog masa berlaku** (satu dialog, peringatan token lama langsung mati tampil
  di dalamnya), bukan message box lalu dialog.
- **Folder** dibuat/diganti namanya lewat `TextInputDialog` yang ada: tidak ada pemeriksaan nama di client selain
  tidak kosong; panjang dan sisanya diserahkan ke server. Pemeriksaan ringan (pola OCI, panjang) hanya untuk root,
  container, dan robot.
- **"Pesan di status" untuk drag-drop yang tidak diizinkan** tidak dibuat: kursor "tidak boleh" cukup. Jatuhan di
  ruang kosong tree sama dengan jatuhan di baris root (pindah ke tingkat teratas).
- **Panel detail** memakai kolom bintang (bukan 400 px tetap) supaya tidak terpotong di jendela 900 px.
- **Teks bantuan claim di Role Manager** tidak bisa diisi: `AddInternalClaim` tidak punya parameter deskripsi.
  Peringatan "claim ini setara kendali penuh" hanya ada di `doc/engine-registry.md` dan komentar `ICtnServices`.

## Verifikasi

Lulus:

- `dotnet build src/frontend/Em.Ui.Wpf.slnx` bersih (0 peringatan, 0 galat) di setiap fase.
- **Harness sementara** (proyek konsol `net10.0-windows` di luar repo, tidak dikomit — plan melarang proyek test baru)
  yang memuat layar dan semua dialog di runtime dengan data dummy, merender ke PNG, dan menjalankan logika:
  - semua XAML terparse di runtime (resource statis, template, trigger) — pemeriksaan ini menangkap galat yang
    tidak terlihat saat build; tampilan terang dan gelap, jendela 1300 dan 900 px dilihat lewat PNG;
  - logika murni: aturan nama OCI dan robot, host dari alamat server, deteksi HTTP polos non-localhost, digest
    dipotong, waktu tanpa `Kind` dianggap UTC, penyusun tree (urutan, kedalaman, induk hilang, `Contains`);
  - alur VM terhadap service palsu di memori: muat awal, pilihan root/simpul, manifest, refresh mempertahankan
    folder terbuka dan pilihan, ganti root, aturan drag-drop (masuk folder, ke atas, ditolak untuk diri sendiri,
    sudah di tempatnya, dan root lain), ubah hak robot (terkirim, dicabut, rollback saat ditolak 404), dan
    keadaan registry nonaktif lalu aktif lagi.

**Belum teruji — butuh manual di Windows terhadap `Em.Api` + SQL Server uji** (skenario lengkap di plan,
bagian Pengujian):

- Semua pemanggilan ke server sungguhan, termasuk bahwa `ParameterOrdinal`/`DateTime?` sampai benar ke action.
  Skrip `Ctn.sql` sudah dijalankan pengguna di SQL Server (konfirmasi 2026-10-02); yang belum teruji adalah
  tabelnya dipakai layar ini dan registry.
- Docker sungguhan: `docker login` dengan token dari dialog, push ditolak (`DENIED`) di root `R`, `NAME_UNKNOWN`
  setelah hak dicabut, token lama gagal setelah Regenerate, `PushedBy` terisi dan jadi "deleted robot".
- Drag-drop dengan mouse nyata, menu konteks, fokus papan ketik, pintasan; tab yang ditarik jadi jendela terpisah;
  layar di host `TabbedMainWindow` dan SinglePage; dua client sekaligus (aksi kedaluwarsa di B menghasilkan pesan,
  bukan crash).
- Akun tanpa claim (menu tidak muncul) vs dengan claim vs administrator.

Tidak ada tindakan yang diblokir policy, jadi tidak ada skrip `.ps1` manual untuk dijalankan.

## Analisa di akhir (satu kali, sesudah semua kode)

Ditemukan dan sudah diperbaiki:

- Waktu dari server tanpa `DateTimeKind` diubah dengan `ToUniversalTime()` (dianggap lokal, jamnya bergeser);
  diganti `CtnInput.AsUtc`. Berlaku untuk keadaan masa berlaku robot dan pilihan "Keep current".
- `ReadTreeAsync` yang 404 memanggil `ReadRootsAsync` yang memanggil `ReadTreeAsync` lagi — bisa berputar kalau
  server tidak konsisten; pemanggilan dari `ReadRootsAsync` sekarang tidak mengulang.
- `ComboBox` bergaya `fieldComboStyle`/`inlineComboStyle` mengabaikan `DisplayMemberPath` pada kotak pilihan
  (tampil nama tipe); diganti `ItemTemplate`.
- Tombol salin di `CtnTokenDialog` dan tombol OK dialog edit dinilai sebelum nilai awal diisi (tampak mati);
  dinilai ulang setelah inisialisasi.
- Spasi ganda di antara `Run` pada judul dialog token dan baris keterangan manifest; baris "pushed by" terpotong.
- Baris di panel "Robots with access" tampil di tengah (Button); diganti baris penuh yang bisa diklik.
- Chip jumlah di header tree selalu tampil, `DetailPullName` ikut diperbarui saat host koneksi berganti.

Dibiarkan (catatan, bukan cacat yang diketahui):

- Kegagalan selain 400/404/409 saat perubahan (mis. jaringan putus) tetap diikuti pembacaan ulang di `finally`;
  kalau pembacaan ulang itu ikut gagal, hanya galat terakhir yang tampil.
- Layar tidak polling; perubahan dari client lain baru terlihat setelah Refresh atau aksi berikutnya.
- Jumlah manifest dimuat semua (kontrak tanpa paging); container dengan ribuan manifest akan terasa lambat —
  jadi catatan untuk tahap 2 registry.
- Rename root/container/robot tidak ada (batas kontrak); salah ketik berarti membuat ulang dan memindahkan.

## Sisa pekerjaan

- Uji manual di atas (terutama layar terhadap server dan docker sungguhan).
- Tahap 2 registry (garbage collection, retensi, kuota, audit): panel detail container disiapkan supaya bagian
  baru mudah ditambah; ruang disk belum kembali sesudah hapus container (konfirmasi hapus mengatakannya).
- `claude.md` diberi satu paragraf "Pembaruan"; berkas itu masih memuat perubahan Anda yang belum dikomit
  (aturan urutan eksekusi plan), jadi tidak ikut saya komit.
