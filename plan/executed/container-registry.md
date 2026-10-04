# Plan — Container registry (backend, terintegrasi di Em.Api.Core)

Status: **tahap 1 (backend) selesai dieksekusi 2026-10-02** — lihat bagian "Hasil eksekusi" di akhir berkas; tahap 2 belum.
Dibuat: 2026-10-02 — hasil diskusi desain; stub awal ada di `src/backend/Em.Api/Program.cs`
(commit `425d81b`). Diperbarui 2026-10-02: tree virtual di bawah root namespace, nama pull tepat dua
segmen, root dan nama container dibuat lebih dulu (push tidak membuat nama baru), tabel `ta_Ctn*`,
akun docker `ta_CtnRobot` dengan hak per root di `ta_CtnRobotRoot`, satu claim manager, **cakupan plan
ini hanya backend**, dan registry **terintegrasi di `Em.Api.Core`** (bukan project terpisah).

---

## Kenapa ada berkas ini

Em System akan punya suite hosting artefak untuk development internal: **CDN** (sudah ada),
**container registry** (berkas ini), dan **NuGet server** (task terpisah, sesudah registry).
Registry dibangun terintegrasi di `Em.Api.Core` seperti CDN dan approval (folder `Api/Registry/`),
dinyalakan host lewat satu baris `AddContainerRegistry` di `Program.cs`, dan dipakai client seperti
produk privat untuk push/pull image.

**Cakupan plan ini: backend saja.** Urutan kerjanya: backend dibangun, pengguna mengujinya di desktop
sendiri (`docker login`, `push`, `pull`), baru kemudian pengguna membuat plan terpisah untuk UI WPF
(ContainerManager). Karena itu plan ini juga menyiapkan layanan manajemen (bagian 4) yang nanti
dipanggil UI, supaya root, container, dan robot bisa dibuat tanpa UI untuk pengujian.

Berkas ini hanya rancangan. Belum ada kode yang diubah.

## Keputusan desain yang sudah diambil

| Hal | Keputusan |
| --- | --- |
| Bentuk | **Terintegrasi di `Em.Api.Core`**, bukan project terpisah: fitur opsional `EmAppBuilder` seperti CDN dan approval. Kode di `src/backend/Em.Api.Core/Api/Registry/`, sejajar `Approval`, `Hub`, `Storage`; kontrak layanan manajemen di `src/shared/Em.Libs/Api.Core.Models/`. Tidak ada project baru, dan `Em.Api` tidak butuh referensi baru |
| Method builder | **`AddContainerRegistry(localStorePath)`**, sesuai stub di `Program.cs` dan pola `AddLocalBinaryStorage(rootPath)` di `EmAppBuilder.Approval.cs`. Dibuat sebagai partial `EmAppBuilder.ContainerRegistry.cs` |
| Penamaan tipe | Awalan `Ctn*` untuk entitas/model, selaras dengan tabel `ta_Ctn*`; nama manager di UI kelak `ContainerManager` |
| Jalur publik | `/v2` dipasang seperti `MapCdn` di `EmApp.Run` (sebelum `UseRouting` dan `MapFallback`), di luar jalur action, sehingga tidak terkena `HttpRequestTimeout` dan `ActionRateLimit`. Klien Docker memanggil `/v2/` secara tetap. Autentikasi `/v2` memakai robot, bukan sesi Em |
| Nama pull | **`host/root/nama`, tepat dua segmen**, seperti `microsoft/openjdk-jdk` di Docker Hub. Push dengan tiga segmen atau lebih ditolak |
| Root namespace | Dibuat lewat layanan manajemen; bisa banyak. Root = pemilik = unit hak, kuota, dan retensi (mis. `server`, `acme`) |
| Nama container | Juga **dibuat lewat layanan manajemen**, langsung di folder tujuannya, sebelum bisa di-push. Push ke `root/nama` yang belum terdaftar **ditolak**; push tidak pernah membuat root, folder, atau nama baru |
| Tree | Di dalam root, folder bertingkat yang **murni pengelompokan (virtual)** dan tidak muncul di nama pull. Memindahkan image antar folder hanya mengubah metadata dan tidak merusak pull |
| Jenis node | Tiga: root, folder (hanya di dalam root), image (hanya daun). Nama image unik per root, apa pun foldernya |
| Akun | **Robot** (`ta_CtnRobot`) untuk login lewat `docker login`: **terpisah dari user API** dan **terpisah per fitur** (NuGet kelak punya akun sendiri) |
| Claim manager | **Satu claim saja**: `Container Manager Access` di module `Administrative Tools` (`Defaults.AdministrativeToolsModuleName`), mengikuti `ICdnServices.CdnClaim`. Dipakai semua action layanan manajemen. Pemegangnya boleh semua hal: root, folder, container, robot beserta token dan haknya, memindah, menghapus. Tidak ada claim per aksi. Action menjawab 404 bila registry tidak dinyalakan |
| Penamaan tabel | Awalan `ta_Ctn*` (Ctn = container). Semua tabel `ta_Ctn<Nama>` sejajar (tanpa pewarisan nama: `ta_CtnTag`, `ta_CtnUpload`, dan seterusnya). Entitas container bernama (`root/nama`) adalah `ta_CtnImage`; kolomnya `cCtnImage*` |
| Kredensial | Token acak berentropi tinggi, ditampilkan sekali; yang disimpan hash SHA-256. Verifikasi cepat, tanpa KDF lambat per request |
| Autentikasi awal | Basic auth lewat HTTPS. Alur token Bearer ala Docker bisa menyusul tanpa mengubah model data |
| Penyimpanan | Blob (layer) di disk, beralamat sha256. Manifest, tag, tree, akun, hak di database. Blob di disk adalah fakta; database adalah indeks yang bisa dibangun ulang |
| `IBinaryStorage` | **Tidak dipakai**: satu kunci satu berkas dan tidak bisa menimpa, sedangkan blob butuh unggah bertahap berbasis digest |
| DbContext | `CtnContext` sendiri (turunan `EmDbContext`, di `Api/Registry/`) untuk tabel `ta_Ctn*`, bukan menambah `DbSet` ke `ApiCoreContext`, supaya host tanpa registry tidak membawa model tabel itu |
| Isolasi client | Tidak ada hal spesifik produk di repo ini. Dockerfile dan pipeline client ada di repo client |

### Batas panjang

Mengikuti praktik `distribution/registry` dan Docker Hub (angka 255 dan 128 berasal dari ingatan,
verifikasi ke sumbernya saat dikerjakan). Semuanya konstanta, bukan bagian skema tabel.

| Bagian | Batas |
| --- | --- |
| Nama root | 64 karakter |
| Nama image | 128 karakter |
| `root/nama` total | 255 karakter |
| Tag | 128 karakter |
| Kedalaman folder di bawah root | 8 tingkat |
| Nama folder | 100 karakter; teks bebas karena tidak muncul di nama pull |

Nama root dan image mengikuti aturan nama OCI: huruf kecil, angka, dan pemisah `.`, `_`, `-`.
Panjang nama tidak memengaruhi path di disk karena blob disimpan menurut digest.

## Rancangan

### 1. Tree node

Tiga tabel: `ta_CtnRoot`, `ta_CtnFolder` (self-reference lewat parent), dan `ta_CtnImage` (container bernama,
daun tree). Dipisah, bukan satu tabel node, karena kolom dan aturan keunikannya berbeda dan FK-nya
jadi nyata. Detail kolom di bagian 6. Keunikan: nama root global; nama container unik per root.

Perilaku:

- Root, folder, dan image semuanya dibuat lewat layanan manajemen. Membuat image berarti memilih
  root, folder tujuan, dan nama (lolos aturan nama dan batas panjang). Karena itu setiap image
  selalu punya posisi di tree sejak lahir.
- Push ke root atau nama image yang belum terdaftar ditolak dengan kesalahan OCI `NAME_UNKNOWN`.
  Push tag baru ke image yang sudah terdaftar berjalan seperti biasa, dan tidak mengubah posisinya.
  Konsekuensinya CI tidak bisa memperkenalkan nama baru sendiri; nama dibuat dulu lewat layanan
  manajemen.
- Menghapus folder yang berisi image ditolak sebelum isinya dikosongkan atau dipindah.
- Memindahkan folder atau container = mengubah `cCtnFolderParent_cCtnFolderId` atau `cCtnFolderId`.
  Tanpa menyalin blob, tanpa mengubah nama pull.

### 2. Akun dan hak

- Robot (`ta_CtnRobot`): nama, hash token, status aktif, masa berlaku. Satu robot satu token; rotasi =
  buat token baru dan token lama langsung tidak berlaku. Bila nanti perlu masa tumpang tindih tanpa
  downtime, tambahkan `ta_CtnRobotToken` (satu robot banyak token) atau pakai dua robot.
- Hak ada di **`ta_CtnRobotRoot`**: satu baris per pasangan robot dan root, dengan kolom
  `cCtnRobotRootAccess` bernilai `R` (pull) atau `W` (push). `W` sudah pasti mencakup baca, jadi satu
  baris cukup. `W` juga mencakup hapus tag/manifest lewat API registry; hapus lewat layanan manajemen
  memakai user Em, bukan robot. Robot tanpa baris di sebuah root tidak bisa melihat root itu.
- Satu robot boleh punya hak berbeda di beberapa root. Ini perlu karena `docker login` menyimpan satu
  kredensial per host: CI yang `FROM host/server/base` dan push ke `host/acme/api` hanya punya satu
  robot, jadi robot itu butuh `R` di `server` dan `W` di `acme`.
- Tidak ada root yang otomatis terbaca semua robot. Setiap client hanya melihat root yang secara
  eksplisit diberikan kepada robotnya.
- Folder tidak punya hak sendiri; hak mengikuti root, jadi memindahkan image tidak mengubah siapa yang
  berhak mengaksesnya. Butuh pemisahan lebih halus: buat root baru.
- Daftar image, tags, dan tree untuk sebuah robot hanya berisi root yang punya baris hak untuknya.
- Cross-repo blob mount dari root lain butuh baris hak (`R` atau `W`) di root sumber.

Contoh:

| Robot | Root | Access |
| --- | --- | --- |
| `platform-ci` | `server` | `W` |
| `acme-ci` | `acme` | `W` |
| `acme-ci` | `server` | `R` |
| `acme-dev` | `acme` | `R` |
| `acme-dev` | `server` | `R` |

### 3. Endpoint `/v2` (OCI Distribution)

Minimal agar `docker push` dan `docker pull` berjalan:

- `GET /v2/` (ping, jawab 401 bila belum login)
- blob upload bersegmen: `POST`, `PATCH`, `PUT` dengan digest; `HEAD` dan `GET` blob, dengan Range
- manifest: `PUT`, `GET`, `HEAD`
- `GET /v2/<root>/<nama>/tags/list`
- cross-repo blob mount: `POST ...?mount=<digest>&from=<root>/<nama>`, dengan pemeriksaan hak pull
  di root sumber

Routing sederhana karena nama selalu dua segmen: `/v2/{root}/{nama}/manifests/{ref}`,
`/v2/{root}/{nama}/blobs/{digest}`, `/v2/{root}/{nama}/blobs/uploads/`, `/v2/{root}/{nama}/tags/list`.
Permintaan dengan lebih dari dua segmen nama dijawab 404 `NAME_INVALID`. Nama dua segmen yang valid
tetapi belum dibuat lewat layanan manajemen dijawab `NAME_UNKNOWN`, termasuk untuk push dan target
blob mount.

### 4. Layanan manajemen (kontrak untuk ContainerManager)

UI WPF **bukan** bagian plan ini; pengguna membuat plan terpisah setelah backend dites. Yang dibangun
di sini adalah layanan manajemen yang nanti dipanggil UI, karena tanpa itu root, container, dan robot
tidak bisa dibuat (push tidak membuat nama baru) dan pengujian di desktop tidak mungkin.

Kontrak (`ICtnServices` dan model) ada di `src/shared/Em.Libs/Api.Core.Models/`, seperti `ICdnServices`,
supaya UI kelak bisa mereferensikannya; implementasinya `CtnServices` di `Em.Api.Core/Api/Registry/`.
Didaftarkan di `EmApp` tanpa syarat seperti `ICdnServices` (`AddService<..>(enforceClaims: false)`),
karena setiap action menyebut claim-nya sendiri. Semua action memakai satu claim
`Container Manager Access` dan menjawab 404 bila registry tidak dinyalakan. Cakupan action:

- **Root:** daftar, buat, ubah deskripsi/status, hapus (hanya bila kosong dan tanpa hak).
- **Folder:** tree per root, buat, ganti nama, pindah, hapus (hanya bila kosong).
- **Container (image):** buat (root, folder tujuan, nama), pindah, ubah deskripsi/status, hapus;
  daftar tag dan manifest (digest, ukuran, tanggal push, robot pengirim).
- **Robot:** daftar, buat (token ditampilkan sekali), buat ulang token, nonaktifkan, hapus.
- **Hak:** atur hak robot di sebuah root (`R`/`W`), cabut.

Karena pemegang claim bisa membuat robot dan memberinya hak `W` di root mana pun, claim ini setara
kendali penuh atas registry; berikan hanya ke admin.

### 5. Relasi base dan turunan (nilai tambah, tahap 2)

Layer image turunan diawali layer base. Registry bisa menyimpulkan "dibangun di atas
`server/base:x`" dengan membandingkan digest layer, dan menampilkan "base ini dipakai N image".
Untuk image multi-arch, bandingkan per platform. Hanya berlaku bila turunan benar-benar `FROM` base itu.

### 6. Rancangan database

Mengikuti `doc/Struktur penamaan object database..md`: awalan `ta_`, kolom `c<Tabel><Nama>`, Id ULID
`char(26)`, state `int`, FK memakai nama kunci parent (FK non-standar menyertakan nama kolom asli),
dan setiap tabel punya `ustamp`, `datestamp`, `json_object` kecuali tabel penghubung. Tabel hidup di
`CtnContext`. Skrip DDL: `doc/sqlscript/mssql/sets/Ctn.sql` (lihat bagian akhir).

Relasi:

```
ta_CtnRoot 1─* ta_CtnFolder (parent: self)
ta_CtnRoot 1─* ta_CtnImage ─0..1 ta_CtnFolder
ta_CtnImage 1─* ta_CtnManifest ◄─ ta_CtnTag (tag menunjuk manifest)
ta_CtnManifest *─* ta_CtnBlob   (ta_CtnManifestBlob, berurutan)
ta_CtnImage         *─* ta_CtnBlob   (ta_CtnBlobLink)
ta_CtnRobot *─* ta_CtnRoot      (ta_CtnRobotRoot, Access R/W)
```

Tabel tahap 1:

| Tabel | Kolom penting |
| --- | --- |
| `ta_CtnRoot` | `cCtnRootId`, `cCtnRootName` `varchar(64)` unik, `cCtnRootState`, `cCtnRootDescription` |
| `ta_CtnFolder` | `cCtnFolderId`, `cCtnRootId` FK, `cCtnFolderParent_cCtnFolderId` FK diri sendiri (kosong = langsung di root), `cCtnFolderName` `varchar(100)`, `cCtnFolderOrder` |
| `ta_CtnImage` | `cCtnImageId`, `cCtnRootId` FK, `cCtnFolderId` FK (boleh kosong), `cCtnImageName` `varchar(128)`, `cCtnImageState`, `cCtnImageDescription`. Unik (`cCtnRootId`, `cCtnImageName`) |
| `ta_CtnManifest` | `cCtnManifestId`, `cCtnImageId` FK, `cCtnManifestDigest`, `cCtnManifestMediaType`, `cCtnManifestSize`, `cCtnManifestContent` `varbinary(max)` (byte persis), `cCtnManifestPushedBy_cCtnRobotId`. Unik (`cCtnImageId`, digest). Waktu push = `datestamp` |
| `ta_CtnTag` | PK (`cCtnImageId`, `cCtnTagName` `varchar(128)`), `cCtnManifestId` FK. Memindahkan tag = mengubah manifest |
| `ta_CtnBlob` | `cCtnBlobId`, `cCtnBlobDigest` `varchar(128)` unik, `cCtnBlobSize`. Isi blob ada di disk |
| `ta_CtnBlobLink` | PK (`cCtnImageId`, `cCtnBlobId`): blob mana yang boleh dilihat dari container mana |
| `ta_CtnManifestBlob` | PK (`cCtnManifestId`, `cCtnManifestBlobOrder`), `cCtnBlobId` FK, `cCtnManifestBlobRole` (config/layer), index di `cCtnBlobId` |
| `ta_CtnUpload` | `cCtnUploadId`, `cCtnImageId`, `cCtnRobotId`, `cCtnUploadSize` (offset). Berkas sementara di disk bernama sesuai Id |
| `ta_CtnRobot` | `cCtnRobotId`, `cCtnRobotName` `varchar(255)` unik, `cCtnRobotState`, `cCtnRobotDescription`, `cCtnRobotTokenHash` `varchar(64)` unik (SHA-256), `cCtnRobotTokenPrefix`, `cCtnRobotTokenExpiry`, `cCtnRobotTokenLastUsed` |
| `ta_CtnRobotRoot` | PK (`cCtnRobotId`, `cCtnRootId`), `cCtnRobotRootAccess` `char(1)` (`R` atau `W`; `W` mencakup baca) |

Tahap 2 menambah `ta_CtnAudit` (hanya aksi level manifest: push, pull manifest, hapus, tag; bukan setiap
GET blob supaya tabel tidak membengkak), kolom kuota di `ta_CtnRoot`, dan tabel retensi.
`ta_Log` tidak dipakai untuk audit robot karena FK-nya ke `ta_User`.

Hal yang tidak boleh terlewat:

- **Collation tag dan digest harus case-sensitive.** Skema yang ada memakai
  `SQL_Latin1_General_CP1_CI_AS`; di OCI `Latest` dan `latest` dua tag berbeda dan di collation CI
  bentrok. Nama root dan container aman karena divalidasi huruf kecil.
- **`ta_CtnBlobLink` adalah pagar keamanan.** Tanpa itu, yang tahu digest sebuah layer bisa
  mengambilnya dari container lain, termasuk lintas root. Blob hanya bisa diambil lewat container yang
  terhubung dengannya; blob mount menambah tautan hanya setelah hak pull di sumber diperiksa.
- **Manifest disimpan sebagai byte persis.** Digest dihitung dari byte-nya; diserialisasi ulang
  mengubah digest. Ukurannya kecil, aman di database dan atomik dengan pembaruan tag.
- **Keunikan folder bersaudara dengan parent kosong.** SQL Server menganggap dua NULL sama dalam unique
  index, MySQL dan PostgreSQL tidak. Karena `Em.Api.Core` mendukung ketiganya, keunikan ini dijaga di
  service dalam satu transaksi.
- **Token berentropi tinggi** sehingga SHA-256 cukup. Login Docker mencari lewat hash token, lalu
  mencocokkan username dengan `cCtnRobotName`. `LastUsed` tidak diperbarui di setiap request.
- **Relasi base-turunan** (bagian 5) dihitung dari `ta_CtnManifestBlob`: urutan layer turunan diawali
  urutan layer base.

Perilaku FK: root ke folder dan container `NO ACTION` (root hanya dihapus saat kosong); container ke
manifest, tag, dan tautan blob `CASCADE` (blob yatim dibersihkan garbage collection); root ke hak robot
`NO ACTION` (root yang masih punya hak tidak dihapus); robot ke hak `CASCADE`. Kedalaman folder
(maksimal 8) dan panjang nama dijaga di service, bukan di skema.

## Tahap kerja

**Tahap 1 — registry inti (backend, di `Em.Api.Core`).** Urutan:

1. Skrip DDL 11 tabel `ta_Ctn*` di `doc/sqlscript/mssql/sets/Ctn.sql`, entitas di
   `Api/Registry/Models/`, dan `CtnContext`.
2. `EmAppBuilder.ContainerRegistry.cs` (partial, seperti `EmAppBuilder.Approval.cs`):
   `AddContainerRegistry(localStorePath)` dengan validasi, path absolut di `BuildApp`, dan penolakan
   pemanggilan kedua, mengikuti `EnableCdn`.
3. Store disk: blob beralamat digest, unggah bertahap, Range.
4. `/v2` dan autentikasi robot (bagian 3), dipasang seperti `MapCdn` di `EmApp.Run`.
5. Layanan manajemen: kontrak di `Em.Libs`, implementasi `CtnServices`, claim (bagian 4).
6. Pengecekan startup tabel `ta_Ctn*` bila registry menyala, mengikuti `ApprovalStartupChecks`.
7. `Em.Api`: aktifkan stub di `Program.cs`
   (`builder.AddContainerRegistry(localStorePath: "./data/container-registry")`). Tidak perlu
   `ProjectReference` baru karena `Em.Api` sudah mereferensikan `Em.Api.Core`.

Selesai bila `dotnet build src/backend/Em.Api.slnx` hijau, kasus uji di bawah lulus, dan pengguna
berhasil `docker login`, `push`, dan `pull` di desktopnya (bagian "Pengujian di desktop pengguna").

Kasus uji wajib:

1. Routing nama dua segmen (`server/base`, `acme/api`), termasuk nama dengan `.`, `_`, `-`.
2. Nama tiga segmen atau lebih (`host/a/b/c`) ditolak dengan `NAME_INVALID`.
3. Cross-repo mount: image turunan tidak mengunggah ulang layer base, termasuk lintas root
   (butuh hak pull di root sumber).
4. Hak per root: `acme-ci` tidak bisa push ke `server` dan tidak melihat root client lain.
5. Memindahkan image antar folder tidak mengubah hasil `pull`.
6. Nama tidak valid (huruf besar) dan nama melewati batas panjang ditolak dengan pesan jelas.
7. Push ke `root/nama` yang belum dibuat lewat layanan manajemen ditolak (`NAME_UNKNOWN`); setelah
   dibuat, push yang sama berhasil.
8. Robot `R` tidak bisa push; robot `W` bisa pull. Satu robot dengan `W` di `acme` dan `R` di `server`
   bisa membangun `FROM server/base` dan push ke `acme/api` dengan satu login, termasuk blob mount
   dari `server` (butuh baris hak di root sumber), tetapi tidak bisa push ke `server`.

**Tahap 2 — operasional.** Dikerjakan setelah tahap 1 dites pengguna. Retensi (mis. simpan N tag
terakhir, hapus yang tanpa tag), garbage collection dengan hitungan referensi lintas repo dan lintas
root, tag immutable untuk tag rilis, kuota dan laporan ukuran per root, audit push/pull/hapus,
relasi base-turunan (bagian 5).

## Panduan untuk sesi eksekusi

Plan ini dibuat agar bisa dikerjakan tanpa riwayat diskusi. Hal yang perlu diketahui sesi baru:

- Baca `claude.md` (diarahkan `AGENTS.md`) dan `doc/Struktur penamaan object database..md` lebih dulu.
  Bahasa percakapan Indonesia. Kerjakan di branch yang ditunjuk sesi; jangan membuat PR kecuali diminta.
- Cek `dotnet --version` sebelum mulai. Bila SDK tidak ada di lingkungan, nyatakan itu dan jangan
  mengklaim build hijau.
- Pola yang ditiru: CDN. Baca `EmAppBuilder.EnableCdn` (`src/backend/Em.Api.Core/Api/Shared/EmAppBuilder.cs`),
  `EmApp.MapCdn` dan `EmApp.Run` (`.../Api/Core/EmApp.cs`), `CdnStore.cs`, `CdnServices.cs`, dan
  `src/shared/Em.Libs/Api.Core.Models/ICdnServices.cs`. Pola builder berfitur: `EmAppBuilder.Approval.cs`
  (`AddLocalBinaryStorage(rootPath)`); registrasi service inti di `EmApp.cs`; pengecekan startup:
  `ApprovalStartupChecks.cs`.
- Jangan menambah `DbSet` ke `ApiCoreContext` dan jangan mengubah tabel inti; registry memakai `CtnContext` sendiri.
- Bangun dengan `dotnet build src/backend/Em.Api.slnx`. Aktifkan stub di `Program.cs` hanya setelah
  `AddContainerRegistry` ada dan bisa dikompilasi, supaya build tidak pecah di tengah jalan.
- Setelah selesai: pindahkan berkas ini ke `plan/executed/`, tambahkan catatan "Pembaruan" singkat di
  `claude.md` tanpa menghapus isi yang ada, dan laporkan jujur apa yang belum teruji (terutama
  `docker` sungguhan, yang dites pengguna di desktop).

**Nilai bawaan untuk butir "belum diputuskan"** (pakai ini bila pengguna belum menjawab lain; semuanya
mudah diubah, dan sebutkan di laporan akhir bahwa itu nilai bawaan):

| Butir | Bawaan |
| --- | --- |
| Bootstrap tanpa UI | Panggil action lewat HTTP dengan token debug atau login admin; tulis resep langkahnya di `doc/engine-registry.md` (mengikuti `doc/engine-approval.md`) |
| Skrip DDL | `doc/sqlscript/mssql/sets/Ctn.sql` (folder `sets` untuk objek yang saling bergantung), hanya MSSQL; ditambah pengecekan startup seperti `ApprovalStartupChecks` |
| Token per robot | Satu |
| Pengujian otomatis | Skrip HTTP; jangan membuat project test baru tanpa persetujuan |

## Pengujian di desktop pengguna

Setelah tahap 1 selesai, pengguna menguji di desktop sendiri. Rencana langkahnya:

1. Jalankan `Em.Api` dengan registry menyala. Docker Desktop mengizinkan HTTP biasa ke `localhost`,
   jadi TLS belum dibutuhkan untuk uji lokal.
2. Buat root, container, robot, dan hak lewat layanan manajemen. Karena UI belum ada, ini lewat
   pemanggilan action langsung (pola alamat `/api/{module}/{action}`); caranya diputuskan saat
   dikerjakan (lihat bagian akhir).
3. `docker login localhost:<port>` dengan nama robot dan token.
4. `docker tag` lalu `docker push localhost:<port>/<root>/<nama>:<tag>`, lalu `docker pull` dari sana.
5. Cek kasus negatif: push ke nama yang belum dibuat, push dengan robot `R`, nama tiga segmen.

## Hubungan dengan NuGet server

Registry dan NuGet punya pola yang sama (akun, token, hak, audit, retensi, kuota), tetapi **akun
terpisah per fitur**: registry memakai `ta_CtnRobot`, NuGet kelak punya tabel sendiri. Tidak ada
abstraksi atau penamaan netral yang disiapkan sekarang. Bagian yang terbukti sama diekstrak saat NuGet
dikerjakan (logika token, audit, dan retensi), bukan ditebak di muka.

## Yang belum diputuskan

- **Cara memanggil layanan manajemen tanpa UI** untuk pengujian: HTTP langsung dengan token debug
  (`EM_DEBUG_TOKEN`) atau login admin, atau seed dari konfigurasi. Perlu dicek bagaimana action
  diautentikasi.
- **Satu token per robot atau banyak** (`ta_CtnRobotToken`)? Rancangan awal: satu token.
- **TLS.** Klien Docker menolak HTTP biasa kecuali ke `localhost`. Untuk deployment, perlu dipastikan
  reverse proxy dan batas ukuran body-nya cukup untuk layer besar.
- **Batas body Kestrel.** Belum diperiksa: timeout dan rate limit action terbukti hanya di jalur
  action, tetapi batas ukuran body server belum.
- **Pengujian otomatis.** Belum ada project test di repo; kasus uji wajib dijalankan sebagai skrip HTTP
  atau project test baru. Docker CLI belum tentu tersedia di lingkungan pengembangan Claude; uji
  `docker` sungguhan dilakukan pengguna di desktop.
- **Tumpang tindih `ReleaseManager`.** Belum dibaca; periksa sebelum NuGet dikerjakan.

## Di luar cakupan

UI WPF ContainerManager dan service klien WPF-nya (plan terpisah, dibuat pengguna setelah backend
dites). Mirror/proxy registry upstream, pemindaian kerentanan, penandatanganan image, dan webhook;
bisa ditambahkan kemudian bila dibutuhkan.

---

## Hasil eksekusi (2026-10-02)

Tahap 1 dikerjakan terintegrasi di `Em.Api.Core`. Cara pakai: [../../doc/engine-registry.md](../../doc/engine-registry.md).

**Dibuat:** `Api/Registry/` (entitas `ta_Ctn*`, `CtnContext`, `CtnBlobStore`, `CtnRegistryEndpoint` + `CtnRoute`,
`CtnRobotAuth`, `CtnServices`, `CtnNames`, `CtnStartupChecks`), `EmAppBuilder.ContainerRegistry.cs`
(`AddContainerRegistry`), kontrak `ICtnServices` + DTO di `Em.Libs/Api.Core.Models/`, pemasangan di
`EmApp` (`ApplyContainerRegistryRegistration`, `MapContainerRegistry` sebelum `UseRouting`, pengecekan
startup, `AddService<ICtnServices, CtnServices>`), stub di `Em.Api/Program.cs` diaktifkan,
`doc/sqlscript/mssql/sets/Ctn.sql`, `scripts/registry-http-test.py`.

**Teruji:** `dotnet build src/backend/Em.Api.slnx` hijau (0 warning). Skrip HTTP (82 pemeriksaan, semua
kasus uji wajib 1-8 di atas) lulus terhadap `Em.Api` asli yang dijalankan di PostgreSQL 16 lokal, dengan
skema dibuat EF dari model (bukan dari `Ctn.sql`). Juga teruji: unggahan blob 120 MB sekali PATCH
(batas body Kestrel tidak menghalangi), startup gagal dengan pesan jelas bila tabel hilang.

**Belum teruji / perlu pengguna:**
- `docker login/push/pull` sungguhan (Docker CLI tidak ada di lingkungan pengembangan Claude).
- `Ctn.sql` terhadap SQL Server (tidak ada SQL Server di lingkungan ini); yang diperiksa hanya
  ketergantungan urutan dan aturan FK secara manual. Jalankan, lalu `Em.Api` di MSSQL, lalu skrip HTTP.
- Solution WPF/MAUI tidak dibangun (Em.Libs hanya bertambah file; tidak ada perubahan yang ada).

**Nilai bawaan yang dipakai** (butir "belum diputuskan"; mudah diubah): bootstrap tanpa UI lewat HTTP
dengan login (resep di `doc/engine-registry.md`); skrip DDL hanya MSSQL di `sets/Ctn.sql` plus pengecekan
startup; satu token per robot; pengujian otomatis berupa skrip HTTP Python (bukan project test baru).

**Keputusan kecil selama eksekusi:** semua waktu UTC (`DateTime.UtcNow`; Npgsql menolak waktu Local);
akun robot dicari lewat hash token lalu username dicocokkan; `DENIED` untuk robot `R` yang menulis dan
`NAME_UNKNOWN` untuk robot tanpa baris hak di root itu; mount yang syaratnya tidak terpenuhi jatuh ke
unggahan biasa tanpa membocorkan root sumber; `DELETE manifest/tag` lewat API registry ditambahkan
(hak `W`); nama robot dibatasi `[a-z0-9._-]` (username Basic auth tidak boleh memuat `:`).

**Belum diputuskan (dari plan) yang tetap terbuka:** TLS/reverse proxy produksi dan batas body-nya;
tumpang tindih `ReleaseManager` sebelum NuGet.
