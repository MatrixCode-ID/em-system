# Plan — Release Manager (WPF), tahap 1 update management

Status: **sudah dieksekusi** (2026-09-27, lihat [Catatan eksekusi](#catatan-eksekusi))
Dibuat: 2026-09-27, dari laporan [doc/report/diskusi-update-management.md](../../doc/report/diskusi-update-management.md)
(tahap 1 di bagian 8)
Baseline: commit `28b5e23`, branch `spa-detach`

---

## Kenapa ada berkas ini

Rilis client WPF selama ini memakai Velopack, dan setiap publish mengirim aplikasi utuh. Rancangan
pengganti yang disepakati di laporan diskusi sederhana: satu folder rilis berisi `binaries/`
(cermin folder client per path), `release.json` (daftar file + hash), dan `release.json.sig`
(tanda tangan). Launcher (tahap 2) cukup mengunduh file yang hash-nya berbeda.

Plan ini membangun **tahap 1**, yaitu layar **Release Manager** di client WPF. Tugasnya: menyiapkan
hasil publish lokal (**Prepare**), membandingkannya dengan rilis di tujuan, melakukan **Sync** satu
arah ke tujuan (CDN bawaan server atau folder pilihan), lalu **Verify**. Format `release.json` dan
`.sig` dikunci di sini, karena format itulah kontrak dengan launcher tahap 2.

Temuan saat menyusun plan yang mengubah titik awal laporan:

- `PostGetMeta_CdnUpload` **sudah** punya `CdnUploadRequest.Overwrite`, dan `CdnStore.UploadAsync`
  sudah menulis lewat file sementara berawalan titik lalu `File.Move` atomik. Karena itu tidak perlu
  action "timpa file" baru, dan **plan ini tidak mengubah backend sama sekali**.
- Upload mensyaratkan foldernya sudah ada (`RequireFolder`), jadi subfolder di `binaries/` dibuat
  lebih dulu lewat `PostGetMeta_CdnCreateFolder`.
- Nama berawalan titik ditolak API CDN, sehingga file lock tidak bisa dibuat dari client. Sesuai
  keputusan, Sync berjalan **tanpa lock** dan hanya mengandalkan urutan yang aman.
- Repo belum punya `Directory.Build.props`. Sejak .NET 8, SDK menempelkan hash commit git ke
  `AssemblyInformationalVersion`, jadi tanpa penanganan setiap commit mengubah byte semua DLL project.
  Penanganannya lewat argumen publish (keputusan 13).

## Keputusan final

Semua keputusan di bawah sudah diambil bersama user. Saat eksekusi tidak ada lagi pertanyaan ke user.
Hal yang tidak tercakup diputuskan menurut pilihan yang paling konsisten dengan tabel ini, lalu dicatat
di laporan eksekusi.

| # | Topik | Keputusan |
| --- | --- | --- |
| 1 | Lokasi kode | Layar bawaan di **`Em.Ui.Wpf.Core`**, seperti CDN Manager: navigasi `admin.release`, claim internal **`Administrative Tools:Release Manager Access`**, dan masuk daftar Tools. |
| 2 | Tujuan Sync | **User memilih**: **CDN bawaan** server yang sedang tersambung (lewat `ICdnServices` yang ada) atau **folder pilihan** (lokal/jaringan, ditulis langsung). |
| 3 | Lock Sync | **Tanpa lock.** Sync mengandalkan urutan yang aman (langkah 6). Tidak ada perubahan backend. |
| 4 | Folder rilis | **Bisa diatur**, default `wpf-release`: path di dalam CDN, atau subfolder di folder tujuan. Isinya selalu `binaries/`, `release.json`, dan `release.json.sig`. |
| 5 | Hash | **SHA-256** (hex huruf kecil). Menggantikan SHA-1 di R2 laporan. |
| 6 | Tanda tangan | **Dikerjakan sekarang.** Sync menandatangani byte `release.json` apa adanya, dan Verify memeriksanya. |
| 7 | Algoritma tanda tangan | **ECDSA P-256 + SHA-256.** |
| 8 | Signing key | Model **kunci bersama**: yang memegang key boleh publish. Key **dibuat dari Release Manager** (*Create signing key*, hasilnya file `.pfx` berpassword), dibagikan sebagai `.pfx`, lalu dipasang di mesin lain lewat **Import signing key**. Key tersimpan di **certificate store Windows** (`CurrentUser\My`). Dialog Import punya pilihan **Exportable**, **default tidak dicentang**. Hanya key yang diimpor dengan pilihan itu yang bisa **diekspor ulang** ke `.pfx`. **Tanpa key, tidak bisa publish** (Sync nonaktif). Thumbprint disimpan di registry, dan public key bisa disalin/diekspor untuk ditanam di launcher. Izin menulis ke CDN tetap diatur claim CDN secara terpisah. |
| 9 | Kontrak format | **Tidak ada project/library bersama.** Kontraknya adalah dokumen spesifikasi **`doc/release-format.md`** + **contoh uji (test vector)** di repo. Kode format (model, hash, tanda tangan) hanya ada di `Em.Ui.Wpf.Core/Release/` untuk Release Manager. **Bahasa launcher diputuskan nanti** (C#, C++, atau Rust), jadi spesifikasinya harus cukup untuk diimplementasikan ulang di bahasa lain. |
| 10 | Field `release.json` | Per file: **path, ukuran, SHA-256**. Level atas: **waktu publish UTC**. Tanpa versi format, tanpa nama publisher. |
| 11 | Tombol Prepare | **Prepare** menjalankan `dotnet publish` project host ke **local publish folder**. Setting yang disimpan: **file `.slnx`**, **project host** (dipilih dari combobox daftar project di `.slnx`, **diisi manual**, tanpa deteksi otomatis), dan **local publish folder**. |
| 12 | Isi folder lama saat Prepare | **Dikosongkan (dengan konfirmasi), lalu publish langsung ke sana.** |
| 13 | Mode publish | **Self-contained `win-x64`.** Determinisme diatur **lewat argumen publish saja**: `-p:IncludeSourceRevisionInInformationalVersion=false -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:PublishSingleFile=false`. Build harian tidak berubah. |
| 14 | Dasar perbandingan | Setiap kali local publish folder berisi hasil publish (hasil Prepare di mesin ini atau hasil salinan), layar membandingkannya dengan rilis di tujuan. |
| 15 | Prepare di PC tanpa SDK | Tombol Prepare **nonaktif, dengan keterangan**, kalau `dotnet --list-sdks` tidak memuat SDK 10.x atau `.slnx`/project host belum diisi atau tidak ada. Perbandingan, Sync, dan Verify tetap jalan selama local publish folder berisi hasil publish. |
| 16 | Tampilan daftar | **Expander, dikelompokkan dari `*.deps.json`**: **Modul utama** (library tipe `project`), **Library extra** (package dan file lain), dan **Runtime .NET** (terlipat). Setiap baris menampilkan status (baru / ganti / hapus / sama) dan ukuran. |
| 17 | Verify | **Ada.** Mengunduh `release.json` + `.sig` dari tujuan, memeriksa tanda tangan, lalu membaca setiap file dan mencocokkan ukuran + SHA-256, dengan progres dan tombol batal. |
| 18 | Commit | **Tidak ada commit.** Perubahan dibiarkan belum di-commit di akhir eksekusi. |

### Keputusan turunan (diambil saat menyusun plan)

- **Format `release.json`** (UTF-8 tanpa BOM, diindentasi, urutan file menurut path ordinal):

  ```json
  {
    "publishedAtUtc": "2026-09-27T10:15:00Z",
    "files": [
      { "path": "Em.Libs.dll", "size": 123456, "sha256": "9f86d0…" },
      { "path": "runtimes/win-x64/native/x.dll", "size": 2048, "sha256": "…" }
    ]
  }
  ```

  `path` relatif terhadap `binaries/`, dipisah `/`, sekaligus membawa nama file ("nama + lokasi" di R2).
  Perbandingan path **tidak peka huruf besar/kecil** (`OrdinalIgnoreCase`), karena client-nya Windows.
- **Format `release.json.sig`**: JSON kecil `{ "keyId": "<hex>", "signature": "<base64>" }`.
  `signature` adalah tanda tangan ECDSA P-256/SHA-256 atas **byte `release.json` apa adanya**, dalam
  format IEEE P1363 (64 byte). `keyId` adalah 16 karakter hex pertama SHA-256 dari
  SubjectPublicKeyInfo public key, jadi launcher bisa menyimpan dua public key (utama dan cadangan)
  dan memilih yang cocok. File `.sig` sendiri tidak ditandatangani, dan itu tidak perlu.
- **Nama file tetap** di folder rilis: `binaries/`, `release.json`, `release.json.sig` (konstanta di
  `ReleaseLayout`, dan tercantum di spesifikasi).
- **Pembaca bebas memakai parser JSON apa pun.** Tanda tangan memeriksa byte `release.json` apa
  adanya, jadi pembaca tidak perlu menghasilkan ulang format yang identik. Spesifikasi menyebut bahwa
  field yang tidak dikenal diabaikan.
- **Urutan Sync** (keputusan 3): (1) buat folder yang belum ada, (2) unggah file baru/ganti, (3) tulis
  `release.json.sig`, (4) tulis `release.json`, (5) hapus file di `binaries/` yang tidak tercantum,
  lalu subfolder yang menjadi kosong. Selama Sync berjalan, client yang mengunduh bisa mendapat hash
  yang tidak cocok. Launcher tahap 2 menolaknya dan mencoba lagi nanti. Itu risiko yang diterima.
  Sync yang dibatalkan di tengah langkah 2 tidak menulis `release.json` baru. Sync yang sama tinggal
  diulang.
- **Penjaga tanpa lock**: tepat sebelum langkah 1, Sync membaca ulang `release.json` di tujuan. Kalau
  byte-nya berbeda dari yang dipakai saat perbandingan (ada yang Sync lebih dulu), Sync berhenti dan
  meminta perbandingan ulang.
- **Yang dianggap "hapus"** dihitung dari isi `binaries/` yang benar-benar ada di tujuan
  (`GetMeta_CdnTree` untuk CDN, enumerasi direktori untuk folder), bukan hanya dari `release.json`
  lama. Dengan begitu sisa Sync yang terputus ikut bersih. Status "ganti/sama" dihitung dari ukuran +
  SHA-256 di `release.json` tujuan. Kalau `release.json` tidak ada, semua file di local publish folder
  berstatus "baru".
- **Membaca tujuan CDN**: `release.json` dan `.sig` diunduh lewat alamat publik `/cdn/...` (host
  koneksi aktif + `CdnFolderContent.PublicPath`, pola yang sama dengan `CopyLinkCommand` di CDN
  Manager), tanpa login. Menulis lewat `ICdnServices`: `PostGetMeta_CdnCreateFolder`,
  `PostGetMeta_CdnUpload` dengan `Overwrite = true` (stream + progres + batal seperti
  `UploadProgressStream`), dan `PostMeta_CdnDelete`.
- **Hak untuk tujuan CDN**: action CDN mensyaratkan claim `Administrative Tools:CDN Manager Access`.
  User Release Manager tanpa claim itu (dan bukan admin) tidak bisa memilih tujuan CDN. Pilihan itu
  nonaktif dengan keterangan, dan tujuan folder tetap tersedia. Claim tidak digabung.
- **Menulis ke tujuan folder**: setiap file disalin ke file sementara berawalan titik di folder yang
  sama, lalu `File.Move(..., overwrite: true)`. `release.json` dan `.sig` ditulis dengan cara yang sama.
- **Pengelompokan dari `*.deps.json`** (file `<nama assembly host>.deps.json` di local publish folder,
  target `.NETCoreApp,Version=v10.0/win-x64`): aset `runtime`/`native`/`resources` milik library
  `project` masuk **Modul utama**. Milik package berawalan `runtimepack.` masuk **Runtime .NET**.
  Package lain masuk **Library extra**. File yang tidak diklaim siapa pun: kalau namanya (tanpa
  ekstensi) sama dengan assembly project (`.pdb`, `.xml`, `.exe`, `.runtimeconfig.json`,
  `.deps.json` host), masuk Modul utama. Selain itu masuk Library extra. File berstatus "hapus"
  memakai aturan yang sama dan jatuh ke Library extra kalau tidak dikenal. Tanpa `deps.json`, semuanya
  masuk Library extra. Filter **"Sembunyikan yang sama"** aktif secara default.
- **Isi snapshot**: semua isi local publish folder apa adanya, termasuk `.pdb` (R3). Tidak ada filter
  file.
- **Pengaman "kosongkan folder"** saat Prepare: tolak kalau local publish folder adalah akar drive,
  sama dengan atau berada di atas folder `.slnx`, atau berada di dalam folder profil user tanpa
  subfolder (`%UserProfile%` itu sendiri). Konfirmasinya menyebut path lengkap dan jumlah item.
- **Prepare** menjalankan `dotnet publish "<project host>" -c Release -r win-x64 --self-contained true
  -o "<local publish folder>"` ditambah argumen keputusan 13, dengan `WorkingDirectory` di folder
  `.slnx`. Output stdout/stderr tampil di panel log. Batal membunuh pohon proses. Setelah sukses,
  perbandingan berjalan otomatis.
- **Daftar project host** dibaca dari elemen `<Project Path="...">` di `.slnx` (XML). Hanya project
  yang `.csproj`-nya berisi `<OutputType>WinExe</OutputType>` atau `Exe` yang ditampilkan.
- **Registry**: `BaseRegKey\ReleaseManager`, dengan value `SolutionPath`, `HostProject` (path relatif
  terhadap `.slnx`), `PublishFolder`, `TargetKind` (`Cdn`/`Folder`), `TargetFolder`, `ReleaseFolder`
  (default `wpf-release`), dan `SigningThumbprint`.
- **Signing key**. Key berupa sertifikat self-signed `CN=Em Release Signing` (ECDSA P-256, berumur
  10 tahun, dibuat lewat `CertificateRequest`). Operasinya:
  - *Create signing key*: minta password (dua kali) dan lokasi simpan, tulis `.pfx`
    (`Export(X509ContentType.Pfx, password)`), lalu langsung mengimpornya di mesin ini seperti Import,
    **tanpa** Exportable. File `.pfx` itulah salinan induknya, jadi store tidak perlu bisa mengekspornya.
    Sebelum membuat, tampilkan konfirmasi bahwa key baru berarti launcher harus mempercayai public
    key-nya, dan bahwa `.pfx` + password harus disimpan aman.
  - *Import signing key*: pilih `.pfx` + password + checkbox **Exportable** (default tidak dicentang,
    dengan keterangan "Allow this key to be exported again from this machine"). Muat dengan
    `X509KeyStorageFlags.UserKeySet | PersistKeySet`, ditambah `Exportable` hanya kalau dicentang, lalu
    simpan ke `CurrentUser\My`. File yang bukan ECDSA P-256 atau tanpa private key ditolak dengan pesan
    jelas. Kalau sertifikat yang sama sudah ada di store, entri lama diganti, supaya pilihan Exportable
    yang baru berlaku. Setelah sukses, tampilkan saran untuk menghapus file `.pfx`. Checkbox ini hanya
    mengatur key di mesin ini: pemegang file `.pfx` + password tetap bisa mengimpornya lagi dengan
    pilihan lain.
  - *Export signing key*: tulis ulang key terpilih ke `.pfx` dengan password baru (dua kali). **Nonaktif
    untuk key yang tidak exportable**, dengan keterangan "This key was imported as non-exportable".
  - *Select*: memilih dari sertifikat ECDSA P-256 ber-private key yang sudah ada di store (misalnya
    hasil import sebelumnya).
  - *Copy public key* / *Export public key*: menyalin atau menyimpan PEM SubjectPublicKeyInfo beserta
    `keyId`-nya.

  Key terpilih dipakai otomatis dan thumbprint-nya disimpan. Kalau thumbprint tersimpan tidak
  ditemukan lagi di store, pilihan dikosongkan. **Tanpa key yang valid, Sync nonaktif** dengan keterangan
  "No signing key - import one to publish". Password dimasukkan lewat dialog baru
  `Dialogs/PasswordInputDialog` (`PasswordBox`, dengan kolom konfirmasi untuk Create/Export, dan gaya
  material yang sama dengan `TextInputDialog`). Password tidak pernah disimpan.
- **Verify** memeriksa tanda tangan dengan public key dari sertifikat yang terpilih di layar (tahap 1
  belum punya public key yang ditanam). Kalau tidak ada sertifikat, hasilnya "signature not checked"
  dan dihitung sebagai gagal. File CDN diunduh lewat `/cdn` dan di-hash sambil mengalir, tanpa disimpan.
  File di tujuan yang tidak tercantum di `release.json` dilaporkan sebagai "extra". Hasilnya berupa
  ringkasan + daftar yang bermasalah.
- **Hashing lokal** berjalan di latar (`Task.Run`) dengan progres, dan bisa dibatalkan. Tidak ada cache
  hash. Menghitung ulang ±300 MB cukup cepat.
- **MAUI tidak dicerminkan.** Release Manager adalah layar, bukan anggota inti engine (sama seperti CDN
  Manager).
- **Contoh uji** disimpan di `doc/release-format-samples/`: `binaries/` berisi beberapa file kecil
  (termasuk satu di subfolder dan satu berukuran 0 byte), `release.json` dan `release.json.sig` yang
  sah, `public-key.pem`, serta varian yang harus ditolak (`tampered-manifest/`: satu byte `release.json`
  diubah; `tampered-file/`: satu byte file diubah; `unknown-key/`: `keyId` tidak dikenal). Semuanya
  dibuat oleh kode `Release/` yang sama dengan kunci khusus contoh uji. Private key kunci itu dibuang
  setelah dipakai dan **tidak pernah** menjadi kunci rilis.
- **Help text** (XML doc) di `src/shared` ditulis dalam Bahasa Indonesia, tanpa nama objek database.
  Identifier, pesan exception, log, teks UI, dan komentar inline ditulis dalam bahasa Inggris.

## Bentuk akhirnya, singkat

```
Prepare ──dotnet publish──► local publish folder ──hash + deps.json──► daftar lokal
                                                                          │ bandingkan
Tujuan (CDN via /cdn + ICdnServices | folder) ──release.json──────────────┘
Sync:  folder → upload baru/ganti → release.json.sig → release.json → hapus yang tidak tercantum
Verify: release.json + .sig (cek tanda tangan) → setiap file (ukuran + SHA-256) → file "extra"
```

---

## Yang dikerjakan

### 1. Spesifikasi dan kode format — `doc/release-format.md` + `src/shared/Em.Ui.Wpf.Core/Release/Format/`

**Spesifikasi ditulis lebih dulu**, dan kode mengikutinya. `doc/release-format.md` (Bahasa Indonesia,
ditujukan untuk penulis launcher di bahasa apa pun) memuat:

- layout folder rilis (`binaries/`, `release.json`, `release.json.sig`) dan artinya: isi `binaries/`
  = folder client, persis;
- struktur JSON kedua file, tipe setiap field, format waktu (ISO 8601 UTC, akhiran `Z`), encoding
  (UTF-8 tanpa BOM), dan aturan "field tak dikenal diabaikan";
- aturan path: relatif terhadap `binaries/`, pemisah `/`, tanpa `..`, tanpa segmen kosong atau
  berawalan titik, unik tanpa memandang huruf besar/kecil;
- hash: SHA-256 atas isi file, hex huruf kecil 64 karakter;
- tanda tangan: ECDSA P-256 + SHA-256 atas **byte `release.json` apa adanya**, format IEEE P1363
  (`r‖s`, 64 byte), di-encode base64 standar; `keyId` = 16 hex huruf kecil pertama SHA-256 dari DER
  SubjectPublicKeyInfo; public key didistribusikan sebagai PEM SubjectPublicKeyInfo;
- **urutan pemeriksaan wajib bagi pembaca**: unduh `.sig` dan `release.json` → pilih public key lewat
  `keyId` (tidak dikenal = tolak) → verifikasi tanda tangan (gagal = tolak seluruh update) → baru parse
  JSON → setiap file yang diunduh dicocokkan ukuran lalu SHA-256-nya (tidak cocok = batalkan, coba lagi
  nanti);
- urutan penulisan saat Sync (bagian "Keputusan turunan"), supaya pembaca paham kenapa ketidakcocokan
  sementara bisa terjadi;
- petunjuk implementasi singkat: .NET (`ECDsa.VerifyData` dengan
  `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`), Windows CNG (`BCryptVerifySignature`, yang
  memakai P1363 secara bawaan), dan Rust (`p256`, `sha2`);
- rujukan ke contoh uji di `doc/release-format-samples/`, lengkap dengan hasil yang diharapkan untuk
  setiap varian.

**Kode format** di `Em.Ui.Wpf.Core/Release/Format/`, namespace `Em.Ui.Wpf.Core.Release` (JSON
memakai `System.Text.Json` biasa, tanpa source generator):

- `ReleaseLayout.cs`: konstanta `BinariesFolder = "binaries"`, `ManifestFileName = "release.json"`,
  `SignatureFileName = "release.json.sig"`, `DefaultReleaseFolder = "wpf-release"`.
- `ReleaseManifest.cs` / `ReleaseFile.cs`: model format di atas (`PublishedAtUtc` `DateTime` UTC,
  `Files`). Juga pembantu `Find(path)` dengan perbandingan `OrdinalIgnoreCase`.
- `ReleaseSignatureFile.cs`: model `{ KeyId, Signature }`.
- `ReleaseManifestSerializer.cs`: `byte[] Serialize(ReleaseManifest)` (mengurutkan file, UTF-8 tanpa
  BOM) dan `ReleaseManifest Deserialize(ReadOnlySpan<byte>)`, dengan validasi: path relatif, tanpa `..`,
  tanpa segmen berawalan titik, tidak ada duplikat, dan hash 64 hex.
- `ReleaseHash.cs`: `ValueTask<(long Size, string Sha256)> ComputeAsync(Stream, IProgress<long>?,
  CancellationToken)`, SHA-256 inkremental (`IncrementalHash`), hex huruf kecil.
- `ReleaseSignature.cs`:
  - `string KeyIdOf(ReadOnlySpan<byte> subjectPublicKeyInfo)`.
  - `ReleaseSignatureFile Sign(ReadOnlySpan<byte> manifestBytes, ECDsa privateKey)`.
  - `bool Verify(ReadOnlySpan<byte> manifestBytes, ReleaseSignatureFile sig,
    IEnumerable<ReadOnlyMemory<byte>> trustedPublicKeys)`: memilih key lewat `keyId`, lalu
    `VerifyData(..., HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`.
  - `string ToPem(ReadOnlySpan<byte> subjectPublicKeyInfo)`.
- XML doc Bahasa Indonesia di semua member `public`, merujuk ke `doc/release-format.md` sebagai
  sumber kebenaran format. Kalau kode dan spesifikasi berbeda, spesifikasinya yang benar.
- Buat contoh uji `doc/release-format-samples/` (keputusan turunan "Contoh uji") lewat program konsol
  sementara di scratchpad. Programnya dibuang setelahnya, dan hanya hasilnya yang masuk repo.

### 2. Logika Release Manager (non-UI) — `src/shared/Em.Ui.Wpf.Core/Release/`

Folder baru untuk logika yang tidak bergantung XAML, supaya view model tetap tipis.

- `ReleaseSettings.cs`: baca/tulis value registry (keputusan turunan "Registry") lewat
  `EmApp.BaseRegKey`.
- `ReleaseTarget.cs`: abstraksi tujuan, dengan dua implementasi:
  - `CdnReleaseTarget` (memakai `ICdnServices` dari DI + `HttpClient` untuk `/cdn`):
    `ReadFileAsync(relPath)` (null kalau 404), `ListBinariesAsync()`, `EnsureFolderAsync(relFolder)`,
    `WriteFileAsync(relPath, Stream, progress, token)`, `DeleteAsync(relPath)`.
  - `FolderReleaseTarget`: operasi yang sama langsung ke disk, dengan temp + move.
- `LocalPublish.cs`: enumerasi local publish folder, hashing dengan progres, dan membaca `deps.json`
  untuk pengelompokan (keputusan turunan).
- `ReleaseComparer.cs`: menghasilkan daftar `ReleaseDiffItem { Path, Group, Status, LocalSize,
  RemoteSize }` dengan `Status` = `New`/`Changed`/`Removed`/`Same`, dan `Group` =
  `MainModules`/`ExtraLibraries`/`DotNetRuntime`.
- `ReleaseSync.cs`: urutan Sync + penjaga tanpa lock, dengan progres (file ke-n, byte) dan batal.
- `ReleaseVerifier.cs`: Verify (keputusan 17).
- `ReleaseBuilder.cs`: cek SDK (`dotnet --list-sdks`, cari baris `10.`), membaca project `.slnx`,
  pengaman + pengosongan folder, dan menjalankan `dotnet publish` dengan log per baris dan batal (kill
  pohon proses).
- `SigningCertificates.cs`: create (ke `.pfx`), import `.pfx`, export `.pfx`, daftar pilihan dari
  `CurrentUser\My` (ECDSA P-256 + private key), muat berdasarkan thumbprint, dan ekspor public key PEM
  (keputusan turunan "Signing key").

### 3. Layar `ReleaseManager` — `src/shared/Em.Ui.Wpf.Core/Navigations/`

`ReleaseManager.xaml` + `ReleaseManager.xaml.cs` (`ReleaseManager : UserControl, INavigationBody` dan
`ReleaseManagerVm : MvvmModelBase`). Baca dulu bagian MVVM dan material design di
`src/frontend/CLAUDE.md`, lalu salin bentuk template dari `CdnManager.xaml`/`RoleManager.xaml`.
Style diambil dari `Styles/`.

Isi layar, dari atas ke bawah:

1. **Kartu Source**: `.slnx` (textbox read-only + tombol Browse), combobox **Host project**,
   **Local publish folder** (Browse), dan status SDK ("NET SDK 10.x found" / alasan Prepare nonaktif).
2. **Kartu Target**: radio **Server CDN** / **Folder**, **Target folder** (Browse, hanya untuk Folder),
   **Release folder** (textbox, default `wpf-release`), dan keterangan kalau tujuan CDN nonaktif karena
   claim.
3. **Kartu Signing**: subject + thumbprint + `keyId` key terpilih (atau "No signing key"), dengan tombol
   **Import signing key**, **Create signing key**, **Export signing key**, **Select**, **Copy public
   key**, dan **Export public key**, plus penanda "exportable / non-exportable" pada key terpilih. Dialog
   password: `Dialogs/PasswordInputDialog` baru. Checkbox Exportable hanya muncul di mode Import.
4. **Toolbar**: **Prepare**, **Compare**, **Sync**, **Verify**, **Cancel** (aktif selama ada operasi
   berjalan), plus checkbox **Hide unchanged**. Ringkasannya berupa jumlah + ukuran per status, dan
   `publishedAtUtc` rilis di tujuan.
5. **Daftar perbandingan**: tiga `Expander` (Main modules / Extra libraries / .NET runtime, yang
   terakhir terlipat), masing-masing dengan header jumlah per status, dan baris berisi path, status
   (chip berwarna), ukuran lokal → tujuan.
6. **Panel bawah**: progres operasi (bar + teks), log Prepare (read-only, bisa digulir), dan hasil Verify.

Semua command memakai `RegisterCommand(nameof(XxxCommand), XxxCommand, XxxCommandAllowed)`, tanpa
lambda. Setiap setting disimpan saat berubah. Hanya satu operasi yang boleh berjalan sekaligus.
Konfirmasi Sync lewat `EmMessageBox`, dengan ringkasan jumlah dan ukuran yang akan diunggah/dihapus
serta tujuannya. `OnNavigatingAway` menolak meninggalkan layar selama Sync berjalan dan menawarkan
batal. `OnRelease` membatalkan operasi yang berjalan.

### 4. Pendaftaran — `EmApp.Statics.cs` dan `EmApp.cs`

- Const `ReleaseManagerClaim = "Administrative Tools:Release Manager Access"`, lalu
  `AddInternalClaim` di `InitInternalClaims`.
- Navigasi `admin.release` (Title "Release Manager", Subtitle "Publish the desktop client",
  `Kind = Manager`, `IsMenuVisible = false`, ikon FontAwesome `Solid_BoxOpen` atau yang setara di FA6)
  lewat `RequireClaim`.
- Tambahkan `"admin.release"` ke daftar Tools di `EmApp.cs` (baris `foreach (var name in ...)`).
- Registrasi DI tambahan (misalnya `HttpClient` untuk `/cdn`, kalau belum ada yang bisa dipakai ulang)
  hanya di `BuildApp` (lihat memori "DI registration builder-only").

### 5. Dokumentasi

- Root `CLAUDE.md`: di bagian `doc/`, satu baris bahwa `doc/release-format.md` adalah kontrak format
  rilis client (sumber kebenaran untuk Release Manager dan launcher), dan setiap perubahan format
  harus mengubah dokumen itu dan contoh ujinya lebih dulu.
- `src/frontend/CLAUDE.md`: satu paragraf pendek tentang Release Manager: letak logikanya
  (`Em.Ui.Wpf.Core/Release/`), bahwa format rilis mengikuti `doc/release-format.md`, dan argumen
  publish determinisme.
- Laporan diskusi: di bagian 8, tandai langkah 1 sebagai sudah ada plan-nya, lalu catat bahwa hash
  menjadi SHA-256 dan `.sig` sudah dikerjakan di tahap 1.

### 6. Verifikasi eksekusi

1. `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan `dotnet build src/backend/Em.Api.slnx` bersih
   (backend tidak berubah, tapi `src/shared` tersentuh).
2. **Contoh uji**: kode `Release/Format/` memverifikasi varian sah di `doc/release-format-samples/`
   (lolos) dan menolak ketiga varian rusak. Cocokkan juga hash setiap file contoh dengan
   `Get-FileHash -Algorithm SHA256` (implementasi independen), dan `keyId` dengan hitungan manual dari
   PEM, supaya spesifikasi terbukti tidak hanya cocok dengan kode kita sendiri.
3. **Determinisme**: jalankan perintah publish persis milik Prepare dua kali ke dua folder berbeda (dari
   command line, dengan argumen yang sama). Bandingkan SHA-256 semua file. Catat file yang berbeda di
   laporan. Kalau ada yang berbeda (misalnya artefak WPF markup compile), catat penyebabnya dan
   perbaikan yang diterapkan lewat argumen publish, tanpa `Directory.Build.props`.
4. Uji logika non-UI lewat program konsol sementara di scratchpad yang mereferensikan
   `Em.Ui.Wpf.Core`, dengan tujuan **folder**: Sync ke folder kosong, ubah satu file
   lalu Sync lagi (hanya satu yang terunggah), hapus satu file lokal lalu Sync (file di tujuan
   terhapus), lalu Verify sukses. Ubah satu byte di tujuan (Verify melaporkannya), dan ubah
   `release.json` (tanda tangan ditolak). Signing key: Create → `.pfx` terbentuk dan key terpasang
   non-exportable (Export nonaktif); Import `.pfx` itu dengan Exportable → Export ulang → Import hasil
   export menghasilkan `keyId` yang sama; Import tanpa Exportable → `Export()` ditolak oleh CNG; password
   salah dan `.pfx` RSA ditolak; tanpa key Sync ditolak. Key uji dihapus dari store setelahnya. Penjaga
   tanpa lock: ubah `release.json` tujuan di antara
   Compare dan Sync, lalu Sync harus berhenti.
5. Tujuan **CDN**: jalankan `Em.Api` lokal (admin dev DB mati, jadi pakai debug token sesuai
   memori), lalu ulangi skenario 4 lewat `CdnReleaseTarget`, termasuk file di subfolder
   (`runtimes/...`).
6. Jalankan client WPF dan buka Release Manager: layar terbuka, setting tersimpan setelah restart,
   Prepare menulis log dan memicu Compare, expander terkelompok benar, dan Prepare nonaktif dengan
   keterangan saat `.slnx` kosong. Sertakan screenshot di laporan eksekusi kalau memungkinkan.
7. Pindahkan plan ini ke `plan/executed/` dan tambahkan **Catatan eksekusi** di akhir berkas: hasil
   setiap langkah verifikasi, keputusan yang diambil sendiri, dan hal yang ditunda. **Tanpa commit.**

## Di luar cakupan

- Launcher, installer, apply update, registry sisi client, dan public key yang ditanam (tahap 2).
  **Bahasa launcher belum diputuskan** (C#, C++, atau Rust). Tahap 1 tidak mengasumsikan salah satunya.
- Cek update dan unduhan di dalam aplikasi (tahap 3).
- Riwayat rilis (`histories/`), channel, dan rollback (dibatalkan di laporan).
- MAUI.
- Mount volume `./data/cdn` untuk container `Em.Api`.

---

## Catatan eksekusi

Dieksekusi 2026-09-27 di branch `spa-detach`, di atas `59cd691`. **Tanpa commit**: semua perubahan dibiarkan
di working tree (rename plan ini sudah ter-stage oleh `git mv`).

### Yang dibuat

- `doc/release-format.md`: spesifikasi format (layout, `release.json`, `.sig`, tanda tangan, `keyId`,
  urutan pemeriksaan pembaca, urutan Sync, petunjuk .NET/CNG/Rust/OpenSSL, daftar contoh uji).
- `doc/release-format-samples/`: `valid/`, `tampered-manifest/`, `tampered-file/`, `unknown-key/`,
  `public-key.pem` (keyId `a771b420e80ecb06`), `expected.txt`, dan `.gitattributes`.
- `src/shared/Em.Ui.Wpf.Core/Release/Format/`: `ReleaseLayout`, `ReleaseFile`, `ReleaseManifest`,
  `ReleaseSignatureFile`, `ReleaseManifestSerializer`, `ReleaseHash`, `ReleaseSignature`,
  `ReleaseFormatException`.
- `src/shared/Em.Ui.Wpf.Core/Release/`: `ReleaseSettings`, `ReleaseTarget` (+ `ProgressReadStream`),
  `CdnReleaseTarget`, `FolderReleaseTarget`, `LocalPublish` (+ `ReleaseGrouping`, `LocalSnapshot`),
  `ReleaseComparer`, `ReleaseSync`, `ReleaseVerifier`, `ReleaseBuilder`, `SigningCertificates`.
- `Navigations/ReleaseManager.xaml(.cs)` dan `Dialogs/PasswordInputDialog.xaml(.cs)`.
- Pendaftaran: claim `Administrative Tools:Release Manager Access`, navigasi `admin.release`
  (ikon `Solid_BoxOpen`), dan entri Tools di `EmApp.cs`. Tidak ada registrasi DI baru: `HttpClient`
  untuk `/cdn` dibuat statis di `CdnReleaseTarget`, meniru pola `DownloadClients` di CDN Manager.
- Dokumentasi: root `CLAUDE.md` (baris `doc/release-format.md`), `src/frontend/CLAUDE.md` (bagian
  "Release Manager", daftar Tools, daftar layar yang me-merge style bersama), laporan diskusi bagian 8.
- Backend tidak berubah.

### Hasil verifikasi

1. **Build**: `Em.Ui.Wpf.slnx` dan `Em.Api.slnx` bersih, 0 warning, 0 error.
2. **Contoh uji**: kode `Release/Format/` meloloskan `valid/` dan menolak ketiga varian rusak
   (`tampered-manifest` → signature Invalid, `tampered-file` → signature Valid lalu HashMismatch
   `readme.txt`, `unknown-key` → UnknownKey). Pemeriksaan independen: ukuran + SHA-256 setiap file cocok
   dengan `sha256sum` dan `Get-FileHash`; `keyId` cocok dengan SHA-256 DER hasil decode PEM
   (`base64 -d | sha256sum`); tanda tangan `valid/` lolos `openssl dgst -sha256 -verify` (setelah P1363
   dikonversi ke DER), dan `tampered-manifest/` gagal di OpenSSL.
3. **Determinisme**: perintah publish Prepare dijalankan dua kali ke dua folder, dengan `bin/Release` dan
   `obj/Release` semua project dihapus di antaranya (supaya build kedua benar-benar compile ulang, bukan
   incremental). **653 file, SHA-256 semuanya identik.** Tidak ada artefak WPF markup compile yang
   berbeda, jadi tidak perlu argumen tambahan.
4. **Logika, tujuan folder** (program konsol sementara): 20 pemeriksaan lolos, yaitu Sync ke folder kosong,
   satu file berubah (hanya `a.dll` terunggah), file + subfolder dihapus lokal (folder `sub/deep` terhapus
   di tujuan), Verify sukses, "tidak ada perubahan" terdeteksi, Sync dibatalkan di tengah (`release.json`
   tetap yang lama) lalu diulang sampai sukses, penjaga tanpa lock (Sync berhenti tanpa menulis apa pun
   saat `release.json` tujuan berubah sejak Compare), local folder berubah sejak Compare (Sync berhenti),
   satu byte file tujuan diubah (Verify: HashMismatch), `release.json` diubah (signature Invalid), dan
   Verify tanpa key (gagal, "signature not checked").
   **Signing key**: 16 pemeriksaan lolos, yaitu Create menulis `.pfx` dan memasang key non-exportable
   (Export ditolak), key dari store menandatangani dan public key-nya memverifikasi; Import dengan
   Exportable mengganti entri lama (tetap satu di store) dan bisa di-Export; Import hasil export
   menghasilkan `keyId` yang sama; Import tanpa Exportable ditolak CNG saat Export; password salah, `.pfx`
   RSA, dan `.pfx` P-384 ditolak tanpa meninggalkan apa pun di store. Semua key uji dihapus dari store.
5. **Tujuan CDN**: `Em.Api` dijalankan lokal (debug token dari `Resources.resx` frontend, tanpa login
   admin), lalu skenario 4 yang sama (ditambah file `runtimes/win-x64/native/n.dll`) diulang lewat
   `CdnReleaseTarget` di folder CDN sementara: **semua lolos**. Folder uji di CDN dihapus setelahnya.
6. **Client WPF**: Release Manager terbuka dari menu Tools. Screenshot di
   [release-manager-wpf/](release-manager-wpf/):
   - `01-first-open.png`: `.slnx` kosong → Prepare nonaktif dengan keterangan; Compare otomatis berjalan
     (653 new). Screenshot ini masih memperlihatkan bug combobox key (lihat di bawah), yang sudah
     diperbaiki.
   - `02-prepare-log.png`: Prepare setelah restart (setting tersimpan: Folder, release folder
     `wpf-release-ui`, key terpilih). Log `dotnet publish` tampil, lalu Compare berjalan otomatis.
   - `03-groups.png`: kelompok dari `deps.json`: Main modules 19, Extra libraries 166, .NET runtime 468
     (terlipat secara default).
   - `04-sync-confirm.png`: konfirmasi Sync (tujuan, jumlah + ukuran unggah/hapus, `keyId`).
   - `05-verify.png`: Sync 653 file (408 MB) ke folder, Compare ulang "653 same", lalu Verify sukses.

   Tujuan CDN juga dicoba dari UI (Compare ke `http://localhost:5132/cdn/wpf-release/`, 653 new). Setting
   uji di Registry dan key uji di store sudah dihapus.
7. Plan dipindah ke `plan/executed/`.

### Keputusan yang diambil sendiri

- **`.gitignore`**: pola `[Rr]elease/` (build output Visual Studio) ikut mengabaikan folder
  `Em.Ui.Wpf.Core/Release/`, jadi seluruh kodenya tidak akan ter-commit. Nama folder dari plan
  dipertahankan, dan ditambahkan baris negasi `!src/shared/Em.Ui.Wpf.Core/Release/`. Hal ini juga
  dicatat di `src/frontend/CLAUDE.md`.
- **`doc/release-format-samples/.gitattributes`** (`* -text`): repo memakai `core.autocrlf=true`, yang
  akan mengubah akhir baris saat checkout dan merusak hash serta tanda tangan contoh uji.
- **Pengelompokan resource satelit**: `deps.json` tidak mendaftar satellite assembly milik runtime pack
  WindowsDesktop (`cs/PresentationCore.resources.dll`, dst., 221 file). Ditambahkan aturan:
  `X.resources.dll` di folder kultur ikut kelompok `X.dll`. Tanpa aturan ini, file itu jatuh ke Library
  extra. Aset juga membaca `localPath` kalau SDK menulisnya (SDK 10.0.302 belum menulisnya).
- **Pilih key lewat combobox**, bukan tombol *Select* terpisah: memilih di combobox berarti Select,
  ditambah tombol refresh untuk membaca ulang store.
- **Expander memakai gaya lipat bersama** (`groupHeaderToggleStyle` di `Styles/Cards.xaml`), bukan kontrol
  `Expander` bawaan, sesuai aturan material design. Perilakunya sama: tiga kelompok dengan header jumlah
  per status, dan runtime .NET terlipat.
- **Panel bawah**: satu log untuk semua operasi (Prepare, Compare, Sync, Verify). Hasil Verify berupa baris
  ringkasan + daftar masalah di log, dan chip berwarna di toolbar. Chip itu di-reset saat tujuan berubah
  atau setelah Sync.
- **`fieldLabelStyle` dipindah ke `Styles/Typography.xaml`**: sudah tersalin di tiga layar, dan dua UI baru
  juga membutuhkannya. Salinan lokal lama tetap menang, jadi layar lama tidak berubah.
- **Meninggalkan layar selama Sync**: yang ditolak adalah menutup tab/entri, dan di layout SinglePage juga
  setiap perpindahan. Berpindah tab di MultiTab dibiarkan, karena body tetap hidup dan Sync terus jalan.
- **Sync bisa dijalankan tanpa perubahan**: konfirmasinya menyebut bahwa `release.json` hanya ditulis dan
  ditandatangani ulang (berguna setelah ganti key).
- **Penjaga tambahan di Sync**: selain `release.json` tujuan, Sync juga memeriksa bahwa local publish
  folder tidak berubah sejak Compare (ukuran + waktu tulis), dan menolak file yang melebihi batas ukuran
  CDN sebelum mengunggah apa pun. Setelah `release.json.sig` ditulis, `release.json` selalu ikut ditulis
  walaupun ada Cancel, supaya tujuan tidak tertinggal dalam keadaan tanda tangan baru + manifest lama.
- **PKCS#12**: `.pfx` ditulis dengan `Pkcs12ExportPbeParameters.Pbes2Aes256Sha256`. Import memeriksa file
  lewat salinan sementara (`EphemeralKeySet`) dulu, jadi file yang ditolak tidak meninggalkan key di disk.
- **Folder rilis diketik bebas** dan dirapikan saat disimpan (`\` menjadi `/`, tanpa `/` di ujung, kosong
  menjadi `wpf-release`).
- **Prepare mematikan MSBuild node reuse** lewat variabel lingkungan (`MSBUILDDISABLENODEREUSE=1`), bukan
  lewat argumen, supaya proses node yang tertinggal tidak menahan pipe output. Hasil publish tidak
  terpengaruh.

### Ditemukan dan diperbaiki saat uji UI

- Combobox key menampilkan nama tipe, karena `fieldComboStyle` tidak memakai `DisplayMemberPath` untuk
  kotak pilihan. Diperbaiki dengan `ReleaseSigningKey.ToString()`.
- Chip hasil Verify masih menampilkan hasil tujuan lama setelah tujuan diganti. Sekarang di-reset.

### Belum diuji / ditunda

- Penolakan meninggalkan layar selama Sync tidak diuji lewat UI (Sync ke folder lokal selesai dalam
  hitungan detik). Logikanya ada di `ReleaseManagerVm.ConfirmLeave`.
- Sync tanpa key tidak diuji dari UI secara terpisah. Tombolnya dikunci `SyncCommandAllowed`
  (`SelectedKey` wajib ada), dan keterangan "No signing key - import one to publish" tampil di kartu
  Signing.
- Tombol Browse, Create/Import/Export signing key, dan Copy/Export public key tidak diklik dari UI, karena
  dialog file Windows tidak diotomasi. Logika di belakangnya diuji di pemeriksaan signing key (butir 4).
- Sync ke CDN dari UI tidak dijalankan dengan hasil publish penuh (408 MB), supaya CDN lokal tidak terisi.
  Jalur CDN diuji lewat program konsol (butir 5) dan Compare dari UI.

### Perubahan sesudah eksekusi (2026-09-27, atas permintaan user)

- **Source, Target, dan Signing key dipindah ke dialog `Dialogs/ReleaseSettingsDialog`**, dibuka lewat
  tombol **Settings** di kiri Prepare. Dialog itu satu saja, berisi card (Source dan Target berdampingan,
  Signing key selebar dialog), termasuk pengelolaan key (Import, Create, Export, Copy/Export public key,
  refresh). Setting ditulis ke Registry saat **Save**, dan Cancel membuangnya. Operasi key langsung
  terjadi di certificate store, jadi tidak ikut dibatalkan (yang dibatalkan hanya pilihan key-nya).
  Kalau sumber atau tujuan berubah, hasil Compare dan Verify lama dibuang lalu Compare berjalan otomatis.
- **Di layar, ketiga card diganti strip ringkasan** satu baris: local publish folder + kesiapan Prepare,
  tujuan + jenisnya, dan `keyId` + masa berlaku key (atau peringatan kalau belum ada). Daftar
  perbandingan jadi jauh lebih lega. Lihat `06-settings-summary.png` dan `07-settings-dialog.png`
  (screenshot 01-05 memperlihatkan tata letak lama).
- **Tombol jawaban dialog mengikuti aturan *Message-box answers***: Save/OK merah
  (`confirmAnswerButtonStyle`), Cancel netral (`answerButtonStyle`), di `ReleaseSettingsDialog` dan
  `PasswordInputDialog`. Sebelumnya kedua dialog memakai `filledButtonStyle` hasil salinan dari
  `TextInputDialog`. Kedua style itu dipindah dari `Dialogs/EmMessageBox.xaml` ke `Styles/Buttons.xaml`
  supaya bisa dipakai bersama; tampilan `EmMessageBox` tidak berubah (sudah dicek).
- Diuji di client: dialog terbuka dan terisi dari Registry, Registry baru berubah setelah Save,
  strip ringkasan ikut berubah, dan Compare otomatis berjalan ke tujuan baru. Key uji dan setting uji
  sudah dihapus.
