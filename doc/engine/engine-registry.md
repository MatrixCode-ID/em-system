# Engine container registry — cara memakai dan menguji

Registry terintegrasi di `Em.Api.Core` (folder `Api/Registry/`), seperti CDN dan approval.
Dokumen ini: cara menyalakan, cara membuat root/container/robot tanpa UI, dan cara mengujinya.
Layar WPF **Container Manager** sudah ada (lihat bagian "Container Manager (WPF)" di bawah).

## Menyalakan

Untuk instalasi lama, jalankan migrasi pada [engine-robots.md](engine-robots.md) dahulu.

1. Jalankan `doc/sqlscript/mssql/tables/030-registry.sql` pada database inti sesudah `tables/010-core.sql` (yang membuat `ta_Robot`); instalasi baru, aman diulang.
2. Di `Program.cs` host (sudah aktif di `Em.Api`):

   ```csharp
   builder.AddContainerRegistry(localStorePath: "./data/container-registry");
   ```

   Path relatif dihitung dari folder konten aplikasi. Isi layer disimpan di sana (`blobs/sha256/..`);
   unggahan sementara di `uploads/`. Panggilan kedua ditolak. Tanpa panggilan ini `/v2` menjawab 404
   dan layanan manajemen menjawab 404.
3. Saat startup, server memeriksa semua tabel `ta_Ctn*`; kalau ada yang belum dibuat, startup gagal
   dengan pesan yang menyebut skripnya. Karena `Em.Api` sekarang menyalakan registry, database yang
   belum dijalankan skripnya tidak bisa dipakai menjalankan `Em.Api` sampai skripnya dijalankan (atau
   baris `AddContainerRegistry` dikomentari).

## Model

- Nama pull: **`host/root/nama`, tepat dua segmen** (`localhost:5132/acme/api:v1`). Tiga segmen atau
  lebih → `NAME_INVALID`. Huruf kecil, angka, pemisah `.`, `_`, `-` (root maks 64, nama maks 128).
- **Root** = pemilik: unit hak. **Folder** hanya pengelompokan di dalam root (maks 8 tingkat, tidak
  muncul di nama pull). **Container** (`root/nama`) harus dibuat dulu lewat layanan manajemen; push ke nama
  yang belum terdaftar → `NAME_UNKNOWN`. Push tidak pernah membuat nama.
- **Robot** = akun `docker login` (nama + token). Hak per root: `R` (pull) atau `W` (push, mencakup
  pull). Robot tanpa baris hak di sebuah root tidak bisa melihat root itu (`NAME_UNKNOWN`). Satu robot
  boleh `W` di `acme` dan `R` di `server` — itu yang membuat `FROM host/server/base` + push ke
  `host/acme/api` cukup dengan satu login, termasuk mount layer base dari `server`.
- Pengelolaan container memakai claim `Container Manager Access`; identitas, token dan hak robot
  memakai `User Manager Access`. Keduanya berada di module `Administrative Tools`.
  Robot dapat dipakai beberapa manager: lihat [engine-robots.md](engine-robots.md).
- Token berentropi 256 bit, ditampilkan **sekali**; yang disimpan hash SHA-256. Satu robot satu
  token; `PostGetMeta_RobotRegenerate` membuat yang baru dan yang lama langsung tidak berlaku.
- Semua waktu UTC.

## Membuat root, container, robot tanpa UI

Action dipanggil lewat `POST /api/Administrative%20Tools/<Action>` dengan `Authorization: Bearer <token>`
(token dari `POST /api/core.credential/PostGetMeta_SignIn`) dan body JSON array parameter
(`[{"ParameterType":"System.String","ParameterOrdinal":0,"ValueData":"acme"}, ...]`; parameter `null`
dihilangkan). Action GET memakai query `?par1=..&par2=..`. Akun `admin` bawaan hanya bisa masuk bila
`AdminUserEnable` di `ta_Meta` bernilai `True` (diubah langsung di database); atau pakai akun biasa
yang memegang claim di atas. Bentuk jawaban: `{"HasData":..,"Data":..,"StatusCode":..,"ErrorMessage":..}`.

Urutan untuk uji pertama (parameter berurutan sesuai `ICtnServices` dan `IRobotServices`):

| Langkah | Action | Parameter |
| --- | --- | --- |
| Root | `PostGetMeta_CtnRootCreate` | `name`, `description?` |
| Folder (opsional) | `PostGetMeta_CtnFolderCreate` | `rootId`, `parentFolderId?`, `name` |
| Container | `PostGetMeta_CtnImageCreate` | `rootId`, `folderId?`, `name`, `description?` |
| Robot | `PostGetMeta_RobotCreate` | `name`, `description?`, `tokenExpiry?` — **simpan `Token`** |
| Hak | `PostMeta_RobotAccessSet` | `robotId`, `managerId` (`Container`), `resourceId` (`rootId`), `access` (`R`/`W`) |

Lainnya: `GetMeta_CtnRoots`, `GetMeta_CtnTree(rootId)`, `GetMeta_CtnImageManifests(imageId)`,
`GetMeta_Robots`, `PostGetMeta_CtnImageMove`, `PostGetMeta_CtnFolderMove/Rename`,
`PostMeta_CtnImageUpdate/Delete`, `PostMeta_RobotUpdate/Delete`, `PostMeta_RobotAccessSet (access kosong)`,
`PostMeta_CtnRootUpdate/Delete`, `PostMeta_CtnFolderDelete`. Menghapus image/robot/root/folder hanya
menghapus metadata; berkas layer di disk menunggu garbage collection (tahap 2).

## Container Manager (WPF)

Layar bawaan `Em.Ui.Wpf.Core`: menu **Container Manager** di daftar Tools (navigasi `admin.container`), muncul hanya
untuk akun yang memegang claim `Administrative Tools:Container Manager Access` (atau administrator).
Yang belum teruji: layar terhadap server, docker sungguhan, dan drag-drop dengan mouse.

- **Containers**: daftar root (kiri), tree folder dan container root terpilih (tengah), detail item
  terpilih (kanan). Buat/edit/hapus root, folder, container; pindah lewat menu "Move to..." atau drag-drop
  (hanya di root yang sama, nama pull tidak berubah). Detail container menampilkan manifest (tag, digest,
  ukuran manifest, waktu push, pengirim) dan menyalin nama pull, `docker pull` per tag atau digest, serta
  `docker tag` + `docker push` (klik kanan tag). Nama root, container, dan robot tidak bisa diganti setelah
  dibuat; folder bisa diganti namanya.
- **User Manager → Robots**: daftar robot, buat/edit/hapus, **Regenerate token**, dan tabel hak per root
  (*No access / Read / Write*) yang langsung dikirim saat pilihan diganti. Token muncul **sekali** di dialog
  setelah robot dibuat atau token dibuat ulang; lupa token berarti Regenerate (token lama langsung mati).
- Host untuk perintah `docker` diambil dari koneksi aktif (`host[:port]`, tanpa skema). Alamat HTTP polos ke
  selain `localhost` ditandai: Docker menolaknya sampai server memakai HTTPS.
- Server tanpa `AddContainerRegistry` menjawab 404; Container Manager lalu hanya menampilkan "Container registry is
  not enabled on this server."
- Pintasan: F5 muat ulang; Delete menghapus baris terpilih (dengan konfirmasi); F2 mengganti nama folder atau
  mengedit container; klik kanan membuka menu.
- Tidak ada (batas kontrak): ukuran image/layer, hapus tag atau manifest, polling otomatis. Hapus container
  hanya membuang metadata; ruang disk baru kembali sesudah garbage collection (tahap 2).

## Pengujian di desktop (docker sungguhan)

Docker Desktop mengizinkan HTTP biasa ke `localhost`; selain itu klien Docker mewajibkan HTTPS.

```powershell
docker login localhost:5132 -u acme-ci -p <token>
docker tag myimage localhost:5132/acme/api:v1
docker push localhost:5132/acme/api:v1
docker pull localhost:5132/acme/api:v1
```

Kasus negatif yang perlu dicoba: push ke nama yang belum dibuat (`NAME_UNKNOWN`), push dengan robot
`R` (`DENIED`), nama tiga segmen (`NAME_INVALID`), huruf besar (`NAME_INVALID`).

## Pengujian otomatis (skrip HTTP)

`scripts/registry-http-test.py` meniru klien Docker lewat HTTP (82 pemeriksaan: kasus uji wajib plan —
routing dua segmen, NAME_INVALID, mount lintas root, hak per root, pindah image, nama tidak sah/terlalu
panjang, NAME_UNKNOWN, robot R/W — ditambah Range, case-sensitive tag, regenerasi token, folder).

```powershell
$env:EM_BASE_URL = "http://localhost:5132"; $env:EM_PASSWORD = "<password akun penguji>"
python scripts/registry-http-test.py
```

Membuat dan menghapus data berakhiran acak; jalankan hanya ke server dan database uji.

## Cek ukuran storage

`ICtnServices.GetMeta_CtnStorageSize()` adalah action GET dengan claim **Container Manager Access**.
DTO `CtnStorageInfo` mengembalikan `BlobBytes`, `ManifestBytes`, dan `TotalBytes` dalam byte (64-bit).
Total merupakan jumlah seluruh blob tersimpan (satu kali per blob, meskipun dipakai banyak image)
ditambah payload manifest di database. Blob yang masih tersimpan setelah image dihapus ikut dihitung.
Angka berasal dari metadata registry; tidak mencakup upload sementara, overhead database/filesystem,
atau kapasitas/free space volume disk. Tidak memerlukan migrasi database.

WPF DefaultHomeControl menampilkan total pada card **Container Manager** saat home dimuat ulang.
Tombol refresh kecil di pojok kanan atas card memperbarui storage card tersebut; tombol dinonaktifkan
selama request berlangsung dan tidak membuka layar manager.
Container Manager menampilkan total global, rincian blob/manifest, dan keterangan cakupan di atas panel.
Refresh serta pembacaan ulang root setelah perubahan membaca ulang angka. Registry nonaktif atau
request gagal menampilkan status, bukan angka nol yang menyesatkan.

Uji agregasi SQL Server: `dotnet run --project ..\.artefacts\em-system\scripts\container-storage-smoke` dari root repo.
Data uji menggunakan transaksi yang selalu di-rollback.

## Batas yang diketahui


- Skrip DDL hanya MSSQL. Sudah dijalankan di SQL Server (konfirmasi pengguna 2026-10-02), tetapi pemakaian tabelnya
  oleh registry dan Container Manager belum diuji end-to-end (lihat laporan eksekusi plan).
  Kodenya provider-agnostik (EF Core) dan lulus uji penuh di PostgreSQL dengan skema hasil EF.
- Tahap 2 belum ada: garbage collection blob yatim dan unggahan basi, retensi tag, tag immutable,
  kuota, audit, relasi base-turunan, token Bearer ala Docker, `_catalog`.
- Hanya sha256, manifest image dan index (Docker v2 dan OCI); schema 1 ditolak. Layer asing (`urls`)
  tidak dicek. `DELETE blob` tidak didukung.
- Batas ukuran body Kestrel dimatikan per request untuk unggahan blob; reverse proxy di depannya harus
  mengizinkan body besar (mis. nginx `client_max_body_size 0`) dan HTTPS bagi klien non-localhost.


## Pengaturan storage melalui UI

Host contoh menggunakan `builder.AddManagedStorageSettings()` tanpa path/limit di `Program.cs`. Persistence di `ta_Meta`, hak Settings terpisah, dan perubahan diterapkan setelah restart API. Panduan lengkap: [pengaturan storage](engine-storage-settings.md).

## Publish from WPF

The manager includes a shared Publish tab and publish history. Configure host-scoped robot credentials independently of the GUI session. See [engine-publish.md](engine-publish.md) for profiles, Prepare/Push/Verify, file sets, Compose and ordered Base/App Sets.
