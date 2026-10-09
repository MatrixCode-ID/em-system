# Object storage Em

- **Tanggal:** 2026-10-09
- **Status:** diskusi
- **Plan turunan:** belum ada

Catatan ini merekam rancangan ERD yang dilampirkan pengguna dan penjelasan
awal. Ini bukan perintah implementasi. Nama tabel/kolom di bagian rekonstruksi
mengikuti gambar; DDL, nullability, index, dan isi JSON belum tersedia.

## Keputusan yang sudah jelas

- Pengguna ingin membahas pembangunan object storage pada engine Em.
- Targetnya API/service internal Em. Kesiapan desain untuk S3 diperhatikan
  sejak awal; implementasi protokol S3 ditunda sesuai klarifikasi terbaru
  pengguna pada 2026-10-09 (lihat pembaruan cakupan di akhir catatan).
- Pengguna meminta penjelasan rancangan terlebih dahulu karena sudah lupa
  maksud awalnya. Cakupan `ta_StorePolicy` belum diputuskan; jawaban awal
  "ya" tidak dianggap sebagai persetujuan cakupan tertentu.
- Belum ada permintaan implementasi maupun keputusan membuat plan.

## Rekonstruksi rancangan dari gambar

Rancangan memisahkan namespace logis (bucket dan object), riwayat isi (version),
identitas isi (blob), dan penempatan fisik (blob-volume dan volume).

| Tabel | Kolom utama pada gambar | Peran yang dapat dibaca |
| --- | --- | --- |
| `ta_StoreBucket` | `cStoreBucketId`, `cStorePolicyId`, name/note, stamps, JSON | Wadah logis object; menunjuk satu policy. |
| `ta_StoreObject` | `cStoreObjectId`, `cStoreObjectVersionCurrent`, `cStoreBucketId`, state, name/path/MIME/note, stamps | Identitas object dalam bucket dan penunjuk nomor versi aktif. |
| `ta_StoreObjectVersion` | `cStoreObjectId`, `cStoreObjectVersionId` (`int`), `cStoreObjectBlobHash`, `datestamp` | Pasangan object dan nomor versi menunjuk isi blob tertentu. |
| `ta_StoreObjectBlob` | `cStoreObjectBlobHash` (`binary(32)`), CRC32 (`binary(4)`), size (`bigint`) | Identitas isi beserta checksum tambahan dan ukuran byte. Algoritma hash utama belum dinyatakan. |
| `ta_StoreObjectBlobVol` | `cStoreVolumeId`, `cStoreObjectBlobHash`, state/type | Menghubungkan blob dengan volume tempat salinannya berada. |
| `ta_StoreVolume` | `cStoreVolumeId`, name/state/type/note, stamps, JSON | Tempat penyimpanan fisik/backend; path, endpoint, dan konfigurasi provider belum terlihat eksplisit. |
| `ta_StorePolicy` | `cStorePolicyId`, state/name/note, stamps, JSON | Aturan yang diterapkan pada bucket. Isi aturan belum dapat diketahui dari ERD. |

Alur pembacaan yang tersirat:

```text
Bucket -> Object -> versi aktif -> Blob -> BlobVol -> Volume
Bucket -> Policy
```

Satu bucket memuat banyak object. Satu object memiliki beberapa versi; versi
menunjuk blob. Beberapa versi/object dapat menunjuk hash blob yang sama.
Satu blob dapat tercatat pada beberapa volume. Dengan demikian skema
memungkinkan deduplikasi berbasis isi dan beberapa salinan fisik, tetapi
mekanisme deduplikasi, jumlah replika, dan failover belum ditentukan.

## Contoh untuk mengingat rancangan

Bucket `documents` memiliki object dengan key logis `reports/monthly.pdf`.
Versi 1 menunjuk blob H1. Upload isi baru ke key yang sama dapat membuat versi
2 menunjuk H2, lalu nomor versi aktif object berubah ke 2. Versi 1 tetap
tersedia jika aturan retensi mengizinkan. Semantik overwrite ini masih usulan.

Jika object lain berisi byte identik dengan H1, ia dapat menunjuk H1 juga
tanpa membuat identitas blob baru. Jika H1 ditempatkan pada volume A dan B,
ada dua baris BlobVol untuk hash H1. Bucket merupakan wadah logis; volume
merupakan lokasi fisik. Memindahkan salinan blob antar-volume tidak harus
mengubah nama/key object.

## Apa yang belum diketahui tentang policy

Relasi yang terlihat hanya Bucket -> Policy. Policy mungkin mengatur
penempatan/replikasi, retensi versi, kuota, hak akses, atau kombinasi aturan
itu. Kolom JSON menyediakan tempat konfigurasi, tetapi tidak membuktikan
aturan mana yang dimaksud pengguna. Belum terlihat relasi Policy -> Volume;
pilihan volume mungkin berada dalam JSON atau belum digambar.

Sebagai contoh hipotesis, policy dapat meminta dua salinan blob pada volume
berbeda. Ini contoh untuk menjelaskan fungsi policy, bukan keputusan
replikasi dua salinan. Jika policy menyimpan ID volume, masih perlu dipilih
apakah memakai JSON atau tabel relasi dengan foreign key.

## Telaah awal dan usulan yang belum disetujui

- Pastikan PK Object cukup ID object. Ikon kunci juga terlihat pada nomor
  versi aktif; jika itu berarti PK gabungan, identitas object akan berubah
  ketika versi aktif berubah. PK Version dapat memakai pasangan ID object
  dan nomor versi. Perlu FK Version -> Object; penunjuk versi aktif harus
  selalu menunjuk versi milik object yang sama.
- Tetapkan identitas key logis yang unik per bucket dan pemisahan name/path.
  Untuk S3, key adalah UTF-8, case-sensitive, maksimum 1.024 byte; batas
  `varchar(255)` pada gambar perlu ditinjau. Jangan memakai key user sebagai
  path file fisik. Rujukan: [S3 object keys](https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-keys.html).
- Pertimbangkan metadata per versi, termasuk MIME dan metadata user, agar
  membaca versi lama mengembalikan metadata yang sesuai isi versi tersebut.
- Jika menawarkan S3 Versioning, putuskan status versioning bucket, version
  ID eksternal, dan delete marker. Nomor versi integer internal dapat tetap
  digunakan jika pemetaan eksternalnya jelas. S3 membuat delete marker untuk
  delete biasa pada bucket versioning-enabled. Rujukan: [S3 Versioning](https://docs.aws.amazon.com/AmazonS3/latest/userguide/Versioning.html).
- Multipart memerlukan pencatatan upload ID, parts, metadata pembuka, serta
  complete/abort. Ini state upload sementara, berbeda dari versi object yang
  sudah tersedia. ETag multipart tidak selalu merupakan MD5 isi lengkap;
  jangan menyamakan ETag protokol dengan hash deduplikasi internal tanpa
  menentukan semantiknya. Rujukan: [S3 multipart upload](https://docs.aws.amazon.com/AmazonS3/latest/userguide/mpuoverview.html).
- Tetapkan algoritma untuk hash 32 byte. SHA-256 merupakan usulan awal;
  CRC32 pada gambar merupakan checksum tambahan, bukan identitas blob.
- Tetapkan kapan upload boleh dinyatakan sukses: staging -> verifikasi isi
  -> salinan fisik yang memenuhi syarat policy -> publikasi metadata versi.
  File/backend storage dan transaksi SQL tidak otomatis atomik bersama;
  perlu pemulihan setelah proses berhenti serta perlindungan upload bersamaan.
- Blob baru boleh dihapus fisik setelah tidak dirujuk versi yang dipertahankan
  maupun upload aktif. Garbage collection perlu koordinasi dengan upload
  dan replikasi. Deduplikasi global pada gambar juga perlu keputusan batas
  antar-bucket/tenant dan pembukuan kuota logis versus byte fisik.
- Usulan arsitektur: satu layanan inti dengan adapter API Em dan adapter
  protokol S3 agar logika versi, policy, penempatan, dan izin tetap konsisten.

## Konteks kode yang sudah ada

`src/backend/Em.Api.Core/Api/Storage/IBinaryStorage.cs` memiliki Put, OpenRead,
Delete, dan Exists dengan key relatif. Put menolak overwrite, sehingga adapter
object storage untuk kontrak ini harus mempertahankan perilaku tersebut.
`LocalBinaryStorage` merupakan implementasi lokalnya. CDN saat ini menyajikan
folder publik; rancangan object storage menambahkan metadata dan versi.
Penggantian CDN/registry/NuPak atau migrasi datanya belum diputuskan.

## Pertanyaan yang masih terbuka

1. Apa cakupan policy: placement/replikasi, retensi, kuota, atau hak akses?
2. Volume berupa folder lokal, network mount, remote provider, atau node storage?
3. Operasi S3 dan client mana yang kelak ditargetkan? Implementasi protokol
   S3 ditunda; penentuan cakupan lengkapnya bukan penghambat tahap internal Em.
4. Bagaimana key, versioning, overwrite, delete, dan retensi didefinisikan?
5. Apakah deduplikasi lintas bucket/tenant diperbolehkan?
6. Berapa replika minimum untuk sukses upload dan bagaimana saat volume gagal?
7. Satu instance API atau beberapa instance yang berbagi metadata/storage?

Catatan ini dipromosikan ke plan setelah keputusan matang dan pengguna meminta
implementasi; tautan plan ditambahkan tanpa menghapus riwayat diskusi.

## Pembaruan 2026-10-09 — chat rancangan sebelumnya

Pengguna memberikan ekspor Markdown chat sebelumnya, berjudul pada tautan
share "Implementasi Object Store S3". Berkas ekspor dibaca langsung; bagian
gambar berisi tautan, bukan gambar tertanam. Gambar revisi di chat lama belum
diperiksa, sehingga perubahan FK/PK di setiap revisi tidak dapat diverifikasi
dari ekspor saja. Label `You` dan `ChatGPT` tampak tertukar pada sebagian
percakapan; atribusi mengikuti isi/konteks dan tidak menjadi persetujuan
implementasi saat ini. Tautan gambar bertanda tangan tidak disalin ke repo.

### Arah yang dapat dipulihkan dari teks

- Tujuan awal sudah eksplisit: membangun object store sendiri yang kompatibel
  dengan protokol Amazon S3. API internal Em ditambahkan sebagai target yang
  dikonfirmasi dalam diskusi sekarang.
- Arsitektur yang dijelaskan adalah server ASP.NET Core dengan metadata di
  SQL Server dan isi file terpisah di disk/NAS/backend storage. Streaming
  upload/download dan abstraksi provider merupakan saran dalam chat lama.
- Fitur yang dibahas meliputi PUT/GET/HEAD/DELETE object, listing, create/delete
  bucket, Signature V4, presigned URL, multipart, Range, dan akses private/public
  read. Fitur lanjutan seperti lifecycle, object lock, encryption, tagging,
  notification, dan bucket policy disebut sebagai pengembangan bertahap;
  penyebutannya bukan keputusan memasukkan semua fitur ke versi pertama.
- Setelah beberapa revisi diagram dan kendala urutan insert, teks kembali ke
  alur Bucket -> Object -> Version -> Blob -> BlobVolume -> Volume.
- Saran terakhir di teks: `cStoreObjectVersionCurrent` adalah integer biasa
  tanpa FK balik ke Version; pertahankan FK Version -> Object. Object dibuat
  lebih dahulu, lalu versi pertama dibuat dalam transaksi SQL yang sama.
  Ini merupakan arah diskusi historis, belum keputusan DDL final.
- Nomor versi disarankan lokal per object, dikelola aplikasi, bukan IDENTITY
  global. Pasangan object ID dan nomor versi harus unik. Jika pasangan itu
  sudah menjadi PK, tidak perlu unique index kedua yang identik.
- Rollback internal dijelaskan sebagai perubahan pointer aktif ke nomor
  versi lama, tanpa mengubah isi blob/riwayat versi.
- Chat lama juga membahas PostgreSQL dan konvensi nama. Pembahasan tersebut
  tidak menetapkan database object storage atau perubahan provider engine.

### Klarifikasi terhadap telaah awal

1. **Policy tetap belum terdefinisi.** Teks menyebut bucket policy dalam
   pembahasan ACL/S3 dan menyebut blob-volume, tetapi tidak menjelaskan
   `ta_StorePolicy` maupun pemetaan policy ke volume. Tidak dapat disimpulkan
   bahwa tabel policy khusus placement, khusus izin, atau keduanya.
2. **Tanpa FK balik adalah pilihan integritas.** Penunjuk versi aktif tetap
   merupakan referensi ke baris Version secara logis. Menghilangkan FK balik
   memudahkan urutan insert, tetapi aplikasi harus memastikan pointer valid
   dalam transaksi, termasuk saat menghapus versi atau melakukan rollback.
   CHECK angka positif saja tidak memastikan versi tersebut ada. FK balik
   yang nullable juga merupakan alternatif teknis, bukan usulan untuk langsung
   mengubah pilihan historis. Rujukan aturan FK: [SQL Server foreign keys](https://learn.microsoft.com/en-us/sql/relational-databases/tables/create-foreign-key-relationships?view=sql-server-ver17).
3. **Nomor berikutnya terpisah dari pointer aktif.** Jika pernah ada versi
   1, 2, dan 3 lalu aktif kembali ke 2, `CurrentVersion + 1` akan bentrok dengan
   versi 3. Perlu alokasi nomor monotonik tersendiri atau perhitungan atas
   riwayat dengan penguncian/transaksi yang mencegah nomor ganda. Pointer aktif
   bukan counter alokasi versi.
4. **Restore S3 perlu semantik sendiri.** Copy versi lama ke key yang sama
   pada bucket versioning-enabled membuat versi baru yang aktif; versi lain
   tetap ada. Menghapus versi aktif secara permanen dapat membuat versi
   sebelumnya aktif. Usulan untuk restore yang mempertahankan riwayat: buat
   versi baru yang menunjuk blob lama, tanpa menggandakan byte blob. Jangan
   menganggap perubahan pointer internal saja identik dengan operasi restore
   S3. Rujukan: [Restoring previous versions](https://docs.aws.amazon.com/AmazonS3/latest/userguide/RestoringPreviousVersions.html).
5. **Kinerja belum diukur.** Klaim chat lama bahwa pointer pasti lebih cepat
   tidak dijadikan hasil benchmark. Pembacaan isi tetap perlu mengakses data
   Version/Blob. Pointer berguna untuk menyatakan pilihan aktif yang berbeda
   dari nomor versi tertinggi; performa tergantung index dan query.

Status tetap **diskusi**. Belum ada kode, DDL, migrasi, atau plan implementasi.

## Pembaruan 2026-10-09 — S3 disiapkan, implementasi ditunda

Pengguna mengklarifikasi: "untuk s3, yang penting disiapkan dulu saja.
implementasi bisa nanti". Ini memperjelas jawaban sebelumnya "keduanya sejak
awal": yang disiapkan sejak awal adalah fondasi desain untuk API internal Em
dan adapter S3 kelak, bukan kewajiban mengimplementasikan kedua API sekaligus.

- **Cakupan awal:** desain engine object storage dan API/service internal Em.
  Diskusi ini belum merupakan perintah mulai implementasi.
- **Persiapan S3 dalam desain:** layanan inti terpisah dari transport API,
  identitas bucket/key terpisah dari lokasi blob fisik, riwayat dan metadata
  versi dapat dikembangkan, serta hash blob internal tidak otomatis menjadi
  identitas/ETag protokol eksternal. Rincian persiapan merupakan usulan desain
  yang dibahas lebih lanjut, bukan daftar fitur kode yang sudah disetujui.
- **Ditunda:** endpoint/protokol S3, Signature V4, kredensial S3, XML response,
  presigned URL, multipart khusus S3, dan uji kompatibilitas client S3.
  Kebutuhan upload bertahap internal, jika ada, diputuskan terpisah.
- **Tidak perlu sekarang:** scaffolding endpoint kosong atau menambah seluruh
  tabel/fitur S3 hanya untuk menyatakan kesiapan. Titik integrasi dan konsekuensi
  pada skema inti cukup dicatat dalam rancangan.
- **Policy:** cakupannya tetap perlu dibahas untuk kebutuhan storage internal;
  `ta_StorePolicy` belum dianggap sebagai implementasi AWS bucket policy.

Catatan dan telaah S3 sebelumnya tetap disimpan sebagai pertimbangan untuk
tahap mendatang. Status tetap **diskusi**, tanpa plan atau implementasi baru.
