# Publish NuGet dan container dari GUI

- **Tanggal:** 2026-10-04
- **Status:** jadi plan
- **Plan turunan:** [plan/executed/publish-nuget-container-gui.codex.md](../../plan/executed/publish-nuget-container-gui.codex.md)
- **Keputusan yang sudah jelas:** konfigurasi dan operasi publish tersedia di GUI seperti Release Manager; pengguna tidak perlu membuat `.cmd`, `.bat`, atau `.ps1` untuk setiap project; bisa dipakai lintas project. Project legacy hanya referensi.
- **Pembaruan diskusi 2026-10-04:** pengguna menetapkan profil disimpan lokal dan mengusulkan folder profil dengan profil berbentuk file. Rancangan utama tidak memerlukan perubahan skema DB; usulan format file dan alur folder dirinci di bawah.
- **Keputusan lanjutan 2026-10-04:** tersedia opsi menyimpan sensitive data langsung di JSON profil. Larangan secret di JSON pada gagasan awal diganti menjadi pilihan penyimpanan per profil; penyimpanan terpisah tetap usulan default. Profil berisi secret tetap lokal di luar repo publik.
- **Usulan lokasi 2026-10-04:** pengguna menyarankan Windows Documents untuk folder profil. Default yang diusulkan menjadi `Documents\Em\Publish\Profiles`, tetap bisa diganti melalui GUI. Usulan sebelumnya memakai folder artefak untuk profil digantikan oleh arah ini; log dan workspace build tetap terpisah.
- **Usulan awal pembuatan 2026-10-04 (digantikan keputusan berikut):** pengguna menyarankan Create profile dari `.slnx` atau `.csproj`; agent sempat mengusulkan wizard untuk mengisi form awal dari informasi project.
- **Koreksi alur pembuatan 2026-10-04:** pengguna memilih dua tombol langsung, **Create profile** dan **Create profile from .slnx**. Keduanya membuka form profil yang sama; tanpa wizard bertahap. Tombol pertama membuka form kosong, tombol kedua meminta file `.slnx` lalu mengisi form dari hasil pembacaan solution.
- **Usulan publish log 2026-10-04:** menanggapi pertanyaan pengguna tentang penyimpanan log, usulkan `Documents\Em\Publish\Logs`, dikelompokkan per ID profil dan per run. Log tetap file lokal tanpa perubahan DB; rincian ada di bagian Publish log.
- **Koreksi NuPak 2026-10-04:** NuGet Manager/NuPak **bukan** modul produk yang dibiarkan di EmPorium House. Pengguna memutuskan NuPak dipindah ke engine (`Em.Api.Core`/`Em.Libs`/`Em.Ui.Wpf.Core`) seperti manager lain, sebagai fase pertama plan; repo turunan hanya berisi host, home screen, dan card. Uraian di bawah yang menyebut NuPak sebagai modul produk/adapter dari engine digantikan keputusan ini.
- **Keputusan final saat promosi ke plan 2026-10-04:** nama NuPak dipertahankan; storage NuGet ikut managed storage settings; host `Em.Api` menyalakan NuGet server; container v1 mencakup semua mode (Dockerfile, local image, publish-template, file set base/module, publish set Base → App, Compose); target built-in + custom untuk container dan NuGet; secret Separate (DPAPI) + JSON plaintext; tambahan Prepare & Push, Compose build, publish set NuGet; log disimpan dan dihapus manual; `DOCKER_CONFIG` sementara per run; commit per fase per repo. Ditunda: JSON-encrypted, `.snupkg`, retensi log otomatis, build jarak jauh/MAUI.
- **Cek kasus base/module legacy 2026-10-04:** pola skrip privat (satu publish dengan `.pubxml`, Base = semua kecuali daftar module, Module = hanya daftar, Dockerfile tulisan tangan dengan `RUN`, tag versi + latest, catatan rilis wajib, tampilkan tag terakhir) memerlukan tambahan: sumber publish dari `.pubxml`, satu publish untuk beberapa image, daftar file bersama sebagai include/komplemen, Dockerfile existing atas staging context, base untuk App yang dijalankan sendiri, tag remote terakhir, opsi catatan rilis wajib. Semua dimasukkan ke plan bagian 5.1.
- **Keputusan target container 2026-10-04:** tersedia pilihan **Use built-in registry**, mengikuti pola Built-in CDN di Release Manager. Tujuan memakai registry server yang sedang terhubung; pengguna memilih root/container melalui GUI. Custom registry tetap merupakan opsi tujuan lain yang diusulkan.

Catatan ini berisi gagasan, bukan perintah implementasi. Belum ada perubahan kode atau pengujian fitur. Setelah keputusan terbuka disepakati, rincian eksekusi masuk ke `plan/unexecuted/`; catatan ini dipertahankan dan diberi tautan ke plan tersebut.

## Masalah dan kondisi sekarang

Release Manager sudah menyediakan Settings serta operasi Prepare, Compare, Sync, Verify, log, dan pembatalan. Setting lokalnya diingat per user Windows melalui `EmApp.BaseRegKey`. Pola pengalaman ini cocok untuk publisher NuGet dan container, meskipun operasinya berbeda.

Container Manager di engine WPF mengelola root, folder, container, dan manifest. Pengguna masih menyalin perintah Docker lalu menjalankan build/push sendiri. Registry engine mensyaratkan container terdaftar lebih dahulu, nama repository tepat `root/container`, serta robot dengan hak W di root tujuan. Folder pengelompokan di GUI tidak masuk ke nama repository.

NuGet Manager yang diperiksa berada di modul produk NuPak pada repo EmPorium, bukan modul bawaan engine. Layar tersebut mengelola feed, package, prefix, recycle bin, dan audit; pack/push masih dilakukan di CLI. Feed dan prefix harus tersedia serta robot memiliki hak W pada prefix tujuan. Hak membuka manager tidak otomatis menjadi hak push.

Referensi skrip legacy `docker-publish` menunjukkan alur: publish aplikasi, pilih berkas untuk base/module, build dari Dockerfile, beri tag versi dan `latest`, lalu login/push dan tulis catatan publish. Yang diambil adalah kebutuhan alurnya. Isi skrip, nama produk, alamat registry, kredensial, dan daftar file privat tidak disalin ke repo publik.

## Gagasan utama: profil publish

Tambahkan tab **Publish** pada masing-masing manager. Tab pengelolaan server tetap tersedia. Publisher mempunyai daftar profil, tombol New/Edit/Duplicate/Delete, dan Settings berbentuk form. Satu project boleh mempunyai beberapa profil: staging/production, beberapa feed, API/worker, base/application, atau konfigurasi build berbeda.

Sebuah profil menyatakan **sumber → persiapan → artefak → tujuan**. Pengguna memilih file/folder dan mengisi field; engine membentuk argumen serta menjalankan alat yang diperlukan. Tidak perlu menulis command sendiri.

| Bagian | Contoh isi form |
|---|---|
| Identitas | Nama profil, jenis NuGet/Container, deskripsi. |
| Sumber | Workspace, project atau solution; tidak dipatok pada nama host atau SDK engine. |
| Persiapan | Configuration, version, runtime/framework bila relevan, properti build terstruktur. |
| Artefak | Daftar `.nupkg` atau image beserta tag dan hasil build. |
| Tujuan | Feed atau registry/repository, credential yang dipilih. |
| Operasi | Check, Prepare/Build, Push, Verify, Cancel, log. |

**Dinamis** berarti path, framework, nama package/image, registry, tag, dan daftar file berasal dari profil. Bukan nama project tertentu yang ditanam di kode. Project `.csproj`, `.sln`, dan `.slnx` dapat dipilih untuk NuGet; container juga bisa berasal dari workspace non-.NET dengan Dockerfile yang sudah ada.

## Dua tombol pembuatan profil

Toolbar daftar profil menyediakan dua tombol yang terlihat langsung:

- **Create profile:** membuka form kosong untuk diisi manual. Field Source menyediakan Browse project/solution (`.csproj`, `.slnx`, `.sln`); mode container juga menyediakan Browse Dockerfile. Pengguna dapat membaca informasi sumber dari dalam form bila diperlukan.
- **Create profile from .slnx:** membuka file picker `.slnx`, membaca solution yang dipilih, lalu membuka form yang sama dengan field awal terisi dan daftar project tersedia.

Jenis **NuGet** atau **Container** mengikuti manager tempat tombol ditekan. Form dapat mengelompokkan Source, Build, Target, dan Credentials dalam area/tab Settings, tetapi tidak memakai tahapan Next/Back. Pengguna langsung mengedit seluruh setting dan menekan **Save** atau **Cancel**.

Hasil pembacaan `.slnx` mengisi nama profil, workspace/path sumber, Configuration, target framework, identitas/version package bila tersedia, serta kandidat host untuk container. Project dipilih dalam form, bukan lewat langkah wizard tambahan. Untuk `.csproj` yang dipilih dari form, satu project langsung menjadi sumber. Nilai yang belum diketahui ditandai agar pengguna dapat melengkapinya.

Pengguna mengatur feed/registry, repository/tag, credential, mode sensitive data, dan opsi build yang relevan pada form tersebut. Tujuan publish tetap ditinjau, bukan ditebak dari nama project. **Save** menyimpan JSON ke folder profil aktif, default Windows Documents; **Cancel** tidak membuat file profil. Pembuatan profil tidak otomatis menjalankan pack, publish, build image, atau push.

| Jenis | Deteksi untuk mengisi form | Pilihan pengguna |
|---|---|---|
| NuGet | Project kandidat pack, `IsPackable`, Package ID/version, target framework. | Pack solution atau project terpilih; versi mengikuti project atau override; feed dan credential. |
| Container | Kandidat aplikasi/host, framework, Dockerfile yang ditemukan di lokasi terkait. | Host jika lebih dari satu, Existing Dockerfile atau publish-template, build context, runtime/platform, base image, entrypoint, dan registry/tag. |

Project yang tidak packable tetap dapat terlihat dengan alasan dan tidak dipilih sebagai package. Untuk container, project library tidak otomatis dianggap host. Project desktop tidak otomatis diubah menjadi container Linux. Dockerfile/context yang ditemukan adalah saran yang ditinjau user; kebutuhan seperti root workspace dan dependensi di luar folder project tidak boleh ditebak hanya dari letak Dockerfile.

Usulan pembacaan metadata memakai hasil evaluasi project untuk Configuration/framework yang dipilih, memperhitungkan properti bersama dan kondisi; parsing XML `.csproj` saja tidak dijadikan sumber final. Bila SDK atau evaluasi belum tersedia, pengguna tetap bisa membuat profil manual dengan hasil deteksi terbatas. Reference project di luar daftar solution ditampilkan terpisah bila ditemukan; tidak otomatis ditambahkan ke daftar package yang akan dipush.

Settings menyediakan **Re-read project information** untuk mengambil ulang kandidat metadata setelah project berubah. Perubahan ditampilkan untuk ditinjau dan tidak menimpa override, target, atau credential profil secara diam-diam. Kedua tombol menghasilkan profil lokal yang dapat diedit kemudian, tanpa perubahan project atau DB.

## Bentuk layar

Alur yang disarankan:

1. Pilih atau buat profil.
2. Buka Settings untuk mengatur Source, Build, Target, dan Credentials.
3. Tekan **Check** untuk memeriksa kesiapan sumber, alat, dan tujuan.
4. Tekan **Prepare** pada NuGet atau **Build** pada Container.
5. Lihat artefak dan tujuan yang akan dikirim, pilih item/tag, lalu tekan **Push**.
6. Lihat hasil per item dan jalankan **Verify**.

Layar utama menampilkan ringkasan Source, Target, kesiapan alat, daftar hasil, log langsung, dan Cancel. Setiap card dinamis memiliki refresh kecil di pojok kanan atas. Refresh mengulang pemeriksaan card tersebut, bukan memulai build/push. Alamat lengkap dapat disalin memakai tombol ikon tepat di sebelah kanan alamat. Enabled, disabled, busy, hover, dan fokus mengikuti tema terang/gelap.

Build dan Push dipisah agar hasil bisa diperiksa. Tombol **Prepare & Push** dapat dipertimbangkan kemudian dengan ringkasan tujuan sebelum mulai. Perubahan setting build/sumber membuat hasil sebelumnya berstatus perlu dibangun ulang; perubahan target/tag harus memperbarui ringkasan push. Push memakai snapshot artefak dan setting yang ditinjau, bukan file yang kebetulan ditemukan saat tombol ditekan.

## NuGet: pack, pilih, push

Form Settings yang disarankan:

- Project/solution, Configuration, output workspace yang dikelola publisher.
- Versi package opsional: kosong mengikuti project; diisi menjadi override pack. Project tidak diedit otomatis.
- Properti MSBuild berupa tabel key/value, misalnya properti yang diperlukan project tertentu; tidak harus menulis baris command.
- Feed tujuan: pilih feed server aktif atau masukkan URL service index untuk provider lain.
- Credential/API key, serta pilihan penanganan duplikat yang jelas.

**Prepare** menjalankan pack dan membaca identitas/version dari metadata package, bukan menebaknya dari nama file. Hanya hasil run itu yang masuk daftar: Package ID, Version, ukuran, sumber, dan status. Project `IsPackable=false` tidak dianggap package yang gagal. Solution boleh menghasilkan banyak package; user memilih mana yang di-push.

Sediakan **Add existing packages** lewat picker/drag-drop untuk `.nupkg` yang sudah ada. Dengan demikian pengguna yang menerima artefak dari CI dapat push tanpa source atau SDK lokal. Symbols `.snupkg` perlu keputusan tersendiri karena server NuPak saat ini belum mendukungnya.

Untuk tujuan NuPak, Check menunjukkan feed aktif/nonaktif, prefix yang cocok, dan kesiapan autentikasi sejauh API mengizinkan. Prefix/feed yang belum tersedia ditampilkan dengan pintasan ke layar pengelolaan; jangan dibuat diam-diam saat push. Untuk feed eksternal, kemampuan pemeriksaan mengikuti provider.

**Push** menampilkan hasil tiap package: uploaded, duplicate/skipped, failed, atau cancelled. Package yang sudah berhasil tidak diulang saat retry. Pada NuPak, versi dalam recycle bin masih direservasi; tampilkan penyebab konflik dan tindakan yang sesuai, jangan menyebutnya berhasil diupload atau menghapusnya otomatis.

**Verify** memeriksa ID/version di tujuan dengan credential yang sesuai. Kecocokan ID/version saja tidak membuktikan byte identik; pemeriksaan hash/download dapat disediakan bila provider mendukung. Daftar artefak tetap berguna untuk memeriksa package mana yang belum terkirim.

## Container: build dan push

Sediakan dua cara membentuk image yang dipilih di Settings:

| Mode | Kegunaan | Field utama |
|---|---|---|
| Existing Dockerfile | Semua project dengan Dockerfile, termasuk non-.NET. | Build context, Dockerfile, target stage opsional, platform, build arguments key/value, named contexts bila diperlukan. |
| .NET publish + template image | Project .NET yang ingin publish folder lalu dibungkus image melalui GUI. | Project, Configuration, runtime, self-contained, base image, entrypoint berupa daftar argumen, working directory, port, environment nonsecret, pilihan file. |

Mode template menghasilkan Dockerfile dan staging area di workspace publisher, tidak mengubah source project. Tampilkan preview Dockerfile agar hasil dapat diperiksa. Base image bebas dipilih; tidak dipatok pada distro atau versi .NET tertentu. Template awal sebaiknya sederhana; project dengan kebutuhan khusus dapat memilih Existing Dockerfile. Mode ini tetap memerlukan konfigurasi build yang dimengerti pengguna, tetapi tidak memerlukan skrip orchestration.

Target mencakup registry host, repository, tag versi, dan tag tambahan. `latest` opsional dan terlihat jelas karena dapat mengganti penunjuk rilis sebelumnya. Tag versi di-push lebih dahulu; tag tambahan hanya dilanjutkan jika tahap sebelumnya berhasil. Jika sebagian tag berhasil dan sebagian gagal, UI melaporkan hasil parsial.

### Target: Built-in registry atau Custom registry

Settings pada profil container menyediakan pilihan target yang terlihat langsung, mengikuti pola pilihan tujuan Release Manager:

| Pilihan | Isi form tujuan |
|---|---|
| **Use built-in registry** | Alamat registry berasal dari koneksi aplikasi yang aktif dan tampil read-only dengan tombol copy; pilih Root, Container, dan tag. Tidak perlu mengetik ulang alamat registry. |
| **Custom registry** (usulan) | Isi host registry, repository, tag, serta credential untuk provider yang dipilih. |

Pada mode built-in, daftar root/container dibaca dari Container Manager server aktif dan dapat di-refresh. Item yang dipilih pada tab Containers boleh mengisi pilihan awal form Publish, tetapi tidak mengganti target profil tersimpan secara diam-diam. Nama image tujuan dibentuk sebagai `host/root/container:tag`; folder pengelompokan bukan bagian nama repository. Bila root/container belum tersedia, berikan pintasan ke pengelolaannya lalu refresh daftar pilihan.

Profil JSON mencatat jenis target **BuiltIn** atau **Custom** dan pilihan repository/root/container. Untuk BuiltIn, alamat tujuan efektif berasal dari koneksi aktif; identitas root/container dan credential harus dicakup ke server terkait, bukan dianggap berlaku di semua server. Ringkasan sebelum Push dan publish log menampilkan alamat tujuan yang benar-benar dipakai.

Jika koneksi aktif berubah, hasil Check tujuan sebelumnya menjadi usang dan pilihan root/container diperiksa ulang. Operasi yang sudah berjalan tetap memakai snapshot tujuan saat mulai. Jangan mengirim credential server lama ke server baru otomatis atau berpindah ke Custom ketika built-in tidak tersedia.

Check pada built-in menunjukkan registry aktif/nonaktif, root/container terdaftar, kesiapan credential push, dan alamat yang bisa digunakan Docker. Hak membaca manager memakai layanan manajemen yang sudah ada; push tetap mengikuti autentikasi registry yang sudah ada (robot dengan hak W pada root). Pilihan built-in adalah pilihan tujuan, tidak mengasumsikan sesi login GUI otomatis menjadi credential Docker. Pengaturan credential tetap memakai opsi penyimpanan profil yang disepakati. Tidak ada perubahan skema DB untuk pilihan target ini.

Untuk registry engine, user bisa memilih root/container yang sudah terdaftar dan nama tujuan mengikuti kontraknya. Untuk registry lain, form mengikuti kemampuan provider dan tidak memaksakan aturan dua segmen engine. Tag yang ditawarkan dari pilihan container tidak boleh memasukkan folder organisasi UI.

Sediakan **Select local image** supaya image yang sudah dibangun IDE/CI dapat ditag dan dikirim dari GUI tanpa rebuild. Tampilkan identitas image lokal dan tujuan akhirnya sebelum push. **Verify** membaca manifest/digest remote dan mencocokkannya dengan hasil publish bila digest pembanding tersedia; detail kekuatan verifikasi ditampilkan.

## Kebutuhan base/module dari referensi legacy

Pola pemisahan base dan module dapat ditangani dengan **file set** pada mode publish-template:

- Include/exclude berupa daftar file/folder atau pola, diedit lewat GUI dengan browser dan preview hasil.
- Opsi exclude debug symbols, tanpa menghapus `.pdb` dari sumber.
- Setiap image memiliki staging folder sendiri; sumber publish tetap utuh.
- Missing file ditampilkan; file yang ditandai wajib menggagalkan persiapan, bukan dilewati tanpa penjelasan.

Contoh generik: profil Base mengambil runtime/dependency, profil App mengambil file aplikasi dan menggunakan tag Base tertentu melalui `FROM`. Pengguna juga bisa memakai satu Dockerfile multi-stage tanpa pemisahan file set.

Ketergantungan antarprofil perlu dibatasi: usulan lanjutan berupa publish set berurutan **Base → App**, dengan pilihan build/push tiap langkah. Base gagal berarti App tidak berjalan; App gagal tidak dianggap seluruh publish berhasil. Tag/digest base dicatat agar App tidak tanpa sengaja memakai `latest` yang berubah. Untuk versi pertama, Existing Dockerfile sudah menampung kebutuhan umum; konfigurasi base/module sebaiknya diputuskan terpisah agar fitur awal tidak terlalu besar.

## Penyimpanan dan credential

**Keputusan: profil disimpan lokal, tanpa update skema DB.** Penyimpanan profil berupa file dalam folder lokal diusulkan untuk menjawab diskusi lanjutan pengguna. Pengalaman Settings mengikuti Release Manager, tetapi profil publish tidak harus mengikuti penyimpanan setting Release Manager di Registry Windows. Path source/build berasal dari PC pengguna; jangan simpan path tersebut sebagai setting server global. Riwayat/log/workspace lokal berada di luar repo dalam folder artefak aplikasi yang dapat diatur.

### Folder dan file profil lokal

Usulan lokasi default profil adalah folder **Windows Documents**, supaya file mudah ditemukan, dibuat, disalin, atau dibackup oleh pengguna. Struktur yang diusulkan:

```text
Documents\Em\Publish\Profiles\
    NuGet\
      libraries-release.json
    Container\
      api-staging.json
      api-production.json
```

`Documents` di sini berarti lokasi Documents yang ditentukan Windows untuk user aktif, bukan path absolut mesin tertentu. Implementasi mengambil lokasi itu dari Windows, termasuk bila folder Documents dialihkan; jangan merangkai path `C:\Users\<user>\Documents` sendiri. Ini juga berarti profil mengikuti sinkronisasi folder Documents bila pengguna mengaktifkannya; pilihan sensitive data dalam JSON tetap berlaku pada file tersebut.

Log dan workspace build bukan bagian dari folder Profiles. Usulan awal log di `..\.artefacts\<nama-repo>\publish\logs\` digantikan usulan log di `Documents\Em\Publish\Logs` pada diskusi berikutnya. Workspace build tetap terpisah dan dapat memakai `..\.artefacts\<nama-repo>\publish\work\` untuk artefak developer lokal, dengan lokasi runtime yang dapat diatur. Menaruh profil/log di Documents tidak memindahkan hasil build ke Documents atau mengubah folder artefak/key/config repo yang sudah ada.

**Satu file JSON per profil.** File berisi versi format, ID profil, nama tampilan, jenis NuGet/Container, konfigurasi sumber/build, tujuan, dan mode penyimpanan sensitive data. Secara default credential direferensikan dengan ID lokal; bila opsi simpan di JSON dipilih, file membawa data sensitif sesuai mode yang dipilih. Nama file boleh berbeda dari nama tampilan profil. ID stabil dipakai untuk menghubungkan riwayat; Duplicate membuat ID baru.

Folder profil dapat dipilih lewat GUI; contoh struktur di atas adalah usulan default, bukan path absolut yang ditanam di engine. Sediakan **Browse**, **Open folder**, dan **Use default** di Settings folder profil. Preferensi kecil seperti lokasi folder profil dan profil terakhir dapat memakai setting lokal aplikasi yang sudah ada. Pemilihan folder tidak perlu disimpan di DB. Mengganti folder hanya mengganti daftar profil yang dibaca; profil lama tidak dipindahkan atau dihapus otomatis.

Alur GUI yang disarankan:

- **Create profile / Create profile from .slnx:** dua tombol langsung untuk membuka form kosong atau form yang terisi dari solution, tanpa wizard. Field Source pada form tetap mendukung pemilihan `.csproj`.
- **Save / Save As / Duplicate:** menyimpan atau menyalin file profil dari form, sehingga pengguna tidak wajib menulis JSON sendiri.
- **Open / Import:** membuka profil yang sudah dibuat sebagai file; import menyalin ke folder profil yang aktif. File dapat juga dibuat atau diedit manual dengan format yang terdokumentasi.
- **Open profiles folder / Refresh:** membuka folder lokal dan membaca ulang daftar setelah ada file baru atau perubahan dari luar aplikasi. File rusak ditandai dengan error yang dapat dibaca tanpa membuat seluruh daftar gagal.
- **Export:** menghasilkan salinan portabel untuk backup atau berbagi; workspace/path mesin dan referensi credential lokal dibersihkan. Opsi **Include sensitive data** default off; bila diaktifkan, data sensitif yang disimpan dalam profil ikut sesuai mode penyimpanannya. Secret dari penyimpanan terpisah tidak dibaca lalu disisipkan otomatis.

Path project, Dockerfile, dan build context dapat relatif terhadap workspace profil. Lokasi workspace pada PC ini dipilih lewat GUI; profil lokal boleh mencatat path absolut lokal, tetapi salinan untuk berbagi tidak membawanya. Menyalin file secara langsung membawa seluruh isi file, termasuk secret bila tersimpan di JSON; gunakan Export tanpa sensitive data untuk membuat salinan yang bersih. Import atau membuka file tidak menjalankan build/push otomatis. Perubahan file dari luar saat form sedang diedit perlu terdeteksi agar Save tidak menimpa perubahan tanpa pemberitahuan; penulisan file sebaiknya atomik.

Export/import profil memakai JSON berversi dan path relatif terhadap workspace bila memungkinkan. Export default menghapus data sensitif; menyertakannya merupakan pilihan pengguna. Setelah import pengguna memilih workspace dan melengkapi credential yang tidak tersedia atau tidak dapat dibuka. Secret build memakai fasilitas secret provider/tool, bukan field build argument biasa; sumber nilainya juga dapat mengikuti mode penyimpanan sensitive data profil.

### Opsi sensitive data di JSON

Settings menyediakan pilihan **Sensitive data storage** per profil. Kebutuhan menyimpan langsung di JSON sudah diminta pengguna; rincian mode terenkripsi berikut masih usulan:

| Mode | Isi JSON | Pemakaian |
|---|---|---|
| Separate storage (usulan default) | Referensi credential lokal, tanpa nilai secret. | Secret di penyimpanan lokal terlindungi; boleh hanya untuk sesi bila Remember tidak dipilih. |
| JSON — encrypted (usulan tambahan) | Nilai secret terenkripsi dan penanda format proteksi. | File memuat secret dengan proteksi; metode proteksi dan portabilitas perlu ditetapkan sebelum plan. |
| JSON — plain text | Token, password, API key, atau sensitive data lain langsung di field JSON. | Bisa dibuat/diedit manual dan dipindahkan bersama profil. Siapa pun yang dapat membaca file dapat membaca secret. |

Pilihan plaintext tersedia melalui GUI tanpa perlu edit file manual. Field secret tetap dimasking dalam form, preview profil/log tetap menyamarkannya, dan label mode menjelaskan bahwa nilainya tersimpan sebagai teks biasa di file. Memilih plaintext tidak mengubah cara pengiriman credential ke tool/server dan tidak membolehkan secret muncul di log atau command line.

Mode yang dipilih tercatat eksplisit di JSON sehingga reader tidak menebak format dari isi nilai. Jangan menyebut Base64 sebagai enkripsi. Bila encrypted dipilih, kunci dekripsi tidak disimpan sebagai teks biasa di file yang sama. Kegagalan dekripsi ditampilkan sebagai credential belum tersedia, tanpa fallback diam-diam ke plaintext. Pilihan enkripsi terikat user/PC atau password untuk portabilitas masih perlu dimatangkan; opsi plaintext tetap tersedia sesuai permintaan pengguna.

Pergantian mode memperbarui penyimpanan sensitive data profil secara eksplisit. Beralih ke Separate storage menghapus nilai inline dari JSON yang disimpan; backup/salinan lama tidak berubah otomatis. Export tanpa sensitive data menghapus seluruh field secret inline, termasuk nilai terenkripsi, bukan hanya menyamarkan teks di layar. Perilaku Duplicate/Save As harus mengikuti mode yang dipilih dan menjelaskan bahwa salinan lokal dapat membawa secret.

Credential dicakup ke host/feed terkait dan tidak dikirim ke alamat baru ketika profil berganti target. UI tidak mengasumsikan token robot dapat dibaca kembali: server saat ini menampilkan token sekali, sehingga token perlu dimasukkan, dibaca dari profil bila dipilih, atau dipilih dari penyimpanan lokal. Semua mode tetap lokal dan tidak memerlukan update DB.

Hak manager untuk mengelola metadata dan hak robot untuk push tetap terpisah. GUI menjelaskan hak yang dibutuhkan dan memberi pintasan ke User Manager; ownership robot tidak mewariskan hak. Untuk provider luar, dukung credential provider-nya sesuai kebutuhan, bukan selalu menganggap identitas robot engine.

## Publish log

Usulan default log lokal ada di **`Documents\Em\Publish\Logs`**, berdampingan dengan folder Profiles. Lokasi log dapat diganti melalui Settings; mengikuti Documents Windows sebagaimana folder profil. Tidak ada tabel atau perubahan skema DB untuk riwayat publisher ini.

```text
Documents\Em\Publish\
  Profiles\
    NuGet\
    Container\
  Logs\
    <profile-id>\
      <tanggal>-<run-id>\
        result.json
        output.log
```

Satu folder untuk satu run, dengan ID unik agar proses di hari yang sama tidak saling menimpa. `result.json` memuat ringkasan terstruktur untuk dibaca tab **Publish history**, sedangkan `output.log` berisi output proses yang sudah disamarkan dan mudah dibuka sebagai teks. Riwayat ditautkan lewat ID profil, sehingga perubahan nama profil tidak memutus riwayatnya.

Isi ringkasan yang disarankan:

- Run ID, Profile ID/nama pada saat run, jenis operasi, waktu mulai/selesai, dan durasi.
- Catatan rilis opsional yang diisi di GUI sebelum push.
- Tujuan feed/registry dan artefak: Package ID/version atau repository/tag/image ID/digest yang tersedia.
- Hasil tiap item/tahap: success, skipped/duplicate, failed, cancelled, atau belum dijalankan; exit code dan pesan error bila ada.
- Hasil Verify terpisah dari hasil Push, termasuk tingkat pemeriksaan yang dilakukan. Push berhasil tetapi Verify gagal tetap menunjukkan kedua fakta tersebut.
- Snapshot setting nonsecret yang diperlukan untuk memahami hasil; retry menjadi run baru dan dapat mencatat ID run sebelumnya.

Log mulai ditulis ketika operasi dimulai dan diperbarui saat tiap tahap selesai, termasuk saat gagal atau dibatalkan. Run yang tidak mempunyai catatan selesai akibat aplikasi berhenti ditampilkan sebagai **interrupted/unknown**, bukan dianggap berhasil. Hasil parsial tetap menyebut package/tag yang sudah diterima server; catatan lokal tidak menjamin rollback.

GUI menyediakan **Publish history**, detail hasil, **Open log**, **Open logs folder**, dan export log terpilih. Save profil tidak membuat publish log; Prepare/Build, Push, dan Verify dicatat sebagai operasi yang dapat ditelusuri. Penghapusan profil tidak otomatis menghapus log; riwayat masih menampilkan nama profil dari saat operasi berlangsung.

Token/password/API key dan nilai build secret tidak ikut snapshot, preview, atau output log, termasuk bila profil memilih JSON plaintext. Jangan menyimpan salinan JSON profil mentah sebagai log. Riwayat ini adalah catatan di PC pengguna; audit server yang sudah ada tetap mempunyai cakupannya sendiri.

## Alat berjalan di belakang GUI

Pengguna tidak harus membuka terminal atau membuat skrip. Docker tetap diperlukan untuk build/push image; SDK diperlukan bila menjalankan pack/publish .NET. GUI memeriksa alat di PC, daemon/context Docker, serta error koneksi dan menunjukkan tindakan perbaikan. Engine tidak mengunci SDK 10 hanya karena versi engine sekarang 10; workspace/project menentukan SDK, termasuk `global.json`.

CLI dapat dijalankan internal memakai argumen terpisah tanpa shell dan output dialirkan ke log. Tidak perlu membuat `.cmd` per project. Untuk upload NuGet, adapter HTTP/protokol dapat dipertimbangkan agar API key tidak harus diletakkan pada command line; detail klien ditentukan saat plan. Credential Docker dikirim lewat mekanisme input/credential yang sesuai, tidak menjadi teks di argumen/log. Konfigurasi auth publisher harus mempunyai lifecycle jelas dan tidak diam-diam mengganti login Docker user.

Satu profil menjalankan satu operasi pada satu waktu. Cancel menghentikan proses yang dimulai publisher, tidak menutup daemon Docker atau container pengguna. Publish yang sudah diterima server tidak otomatis dibatalkan balik; UI membaca ulang keadaan tujuan setelah cancel/gagal. Log menyamarkan secret dan menyimpan exit code, artefak, tag/digest, waktu, serta catatan rilis opsional.

Folder kerja dibuat khusus per run. Jangan membersihkan folder source atau folder output milik user secara rekursif. Pemeriksaan perubahan source/setting, hasil parsial, koneksi terputus, dan kegagalan tool perlu menjadi bagian perilaku normal layar.

## Batas arsitektur yang disarankan

Fondasi generik bisa berada di `Em.Ui.Wpf.Core`: profil lokal, form build, process runner, progress/cancel/log, file staging, dan provider tujuan. Container publisher dipasang pada Container Manager engine. NuGet publisher generik dapat dipakai modul NuPak produk lewat adapter feed/prefix/credential.

Engine tidak membawa referensi balik ke modul NuPak atau nama produk. Kontrak provider memungkinkan tujuan registry/feed lain tanpa menyalin layar. UI WPF menjadi sasaran awal karena build menggunakan PC pengguna; dukungan MAUI atau build jarak jauh bukan konsekuensi otomatis dari gagasan ini.

## Pertanyaan yang masih terbuka

1. Versi pertama mencakup Existing Dockerfile dan pack NuGet terlebih dahulu, atau sekaligus form .NET publish-template/file set base-module?
2. **Terjawab untuk container built-in:** pilihan Use built-in registry wajib tersedia dengan server aktif sebagai sumber alamat dan daftar root/container, seperti pilihan Built-in CDN di Release Manager. Cakupan Custom registry/feed eksternal pada versi pertama masih perlu ditetapkan; bukan pengganti mode built-in.
3. **Terjawab untuk rancangan utama:** profil lokal, dengan usulan satu file JSON per profil, default di `Documents\Em\Publish\Profiles`, dan folder yang dapat diganti lewat GUI; tersedia opsi sensitive data di JSON. Recipe bersama dapat dibagikan tanpa secret lewat Export, atau dengan sensitive data bila opsi tersebut dipilih. Penyimpanan profil terpusat tidak masuk cakupan utama. Detail format JSON serta metode enkripsi bila mode encrypted dipilih masih perlu dimatangkan.
4. Untuk NuGet, apakah pack/push seluruh solution sudah cukup, atau perlu beberapa project/solution dalam satu publish set sejak awal?
5. Apakah Compose build diperlukan sejak awal? Dukungan Compose/named contexts harus dirancang jelas jika dipilih; build/push tidak otomatis berarti deploy atau menjalankan service.
6. **Usulan setelah pertanyaan publish log:** riwayat lokal berupa `result.json` + `output.log` di `Documents\Em\Publish\Logs`, dapat dibaca/diekspor melalui GUI, dengan catatan rilis opsional. Format final dan kebijakan retensi log masih perlu dimatangkan. Penambahan audit terpusat baru yang memerlukan perubahan DB berada di luar cakupan; riwayat lokal tidak disebut audit server.

## Hasil yang diharapkan bila dipromosikan menjadi plan

Pengguna dapat membuat profil lewat form, memilih project apa pun yang sesuai mode, mempersiapkan artefak, melihat tujuan serta versi/tag, melakukan push, dan membaca hasilnya dari manager. Tidak perlu membuat skrip publish khusus project. Jalur artefak yang sudah ada juga tersedia, dengan hasil per item, retry, cancel, dan pilihan menyimpan sensitive data terpisah atau dalam JSON. Secret tersimpan sesuai pilihan pengguna dan tetap disamarkan pada log/preview.

Referensi engine yang dibaca: [ReleaseManager.xaml.cs](../../src/shared/Em.Ui.Wpf.Core/Navigations/ReleaseManager.xaml.cs), [ReleaseSettings.cs](../../src/shared/Em.Ui.Wpf.Core/Release/ReleaseSettings.cs), [ContainerManager.xaml.cs](../../src/shared/Em.Ui.Wpf.Core/Navigations/ContainerManager.xaml.cs), [engine-registry.md](../engine-registry.md), dan [engine-robots.md](../engine-robots.md). NuPak diperiksa dari layar dan panduan pada repo produk; tidak disalin ke engine.

## Implementasi 2026-10-04

Migrasi NuPak ke engine dan publisher WPF selesai diimplementasikan. Build lima solution, 170 pemeriksaan NuPak, 105 pemeriksaan settings, 35 pemeriksaan publisher serta render terang/gelap lulus. Docker daemon tidak tersedia sehingga build/push container sungguhan masih tertunda; interaksi aplikasi utama dan tujuan eksternal juga belum diuji. Rincian hasil dicatat di [laporan](../report/publish-nuget-container-gui-eksekusi.md). Ide lanjutan ditunda: JSON-encrypted, symbols .snupkg, retensi log otomatis, build jarak jauh dan MAUI.
