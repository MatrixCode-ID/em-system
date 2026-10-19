# Review UI Publish: picker target dan pesan status

- Tanggal: 2026-10-06 (ditambah 2026-10-19: temuan 7-9)
- Status: diskusi

## Temuan: picker target di Publish profile

Ditemukan saat membuat profil Container (mode Dockerfile) di `PublishProfileDialog`
(`src/shared/Em.Ui.Wpf.Core/Navigations/Publish/PublishProfileDialog.xaml.cs`, method `BuiltIn`), tab **Target**,
tombol **Use active built-in server and select destination**.

1. **Combobox root menampilkan nama tipe.** Isinya tampil sebagai `Em.Api.Core.Models.CtnRootInfo`, bukan nama root,
   padahal `DisplayMemberPath="Name"` diset dan `CtnRootInfo.Name` adalah properti publik. Pengguna tidak bisa
   membedakan root bila ada lebih dari satu; satu-satunya konfirmasi adalah pesan `Built-in target: root/container`
   di bawah form setelah container dipilih. Combobox container kemungkinan terkena hal yang sama.
2. **Root tanpa container: combobox kedua kosong tanpa penjelasan.** Bila root yang dipilih belum punya container,
   combobox container kosong dan tidak ada pesan apa pun. Pengguna mengira picker rusak, padahal container harus
   dibuat dulu di tab Containers.
3. **Klik ulang menambah pasangan combobox baru.** Setiap klik tombol menambahkan dua combobox lagi di bawah yang
   lama, tidak me-refresh yang sudah ada. Setelah membuat container lalu kembali ke dialog, pengguna harus memakai
   pasangan yang paling bawah.

## Temuan: pesan status di tab Publish

4. **Pesan hasil operasi mudah terlewat.** Setelah **Check** selesai, pesan `Operation completed. Review item results
   and history.` muncul sebagai teks kecil rata kanan di samping kolom **Release notes**, jauh dari tombol yang
   diklik. Tabel artifact di bawahnya tetap kosong (Check memang tidak menghasilkan artifact), sehingga pengguna
   tidak tahu apakah Check lulus, gagal, atau apa saja yang diperiksa.
5. **Teks pesan tidak membedakan hasil.** "Operation completed" dipakai tanpa menyebut operasinya (Check, Build,
   Push, Verify) dan tanpa tanda berhasil/gagal; pengguna diminta membuka history untuk tahu hasilnya.

6. **Selesainya push tidak dinyatakan jelas.** Setelah **Build & Push**, yang terlihat hanya log mentah Docker
   (`Layer already exists`, lalu `<tag>: digest: sha256:… size: …`). Pengguna harus menebak dari baris digest bahwa
   push sudah selesai dan berhasil. Baris `Layer already exists` pada extra tag (yang memang normal karena tag versi
   didorong lebih dulu) juga bisa disangka masalah.

## Usulan: pesan status

- Letakkan status operasi di satu tempat yang tetap, dekat baris tombol (Check, Build, Push, Verify, Build & Push),
  misalnya bar status di bawah toolbar, terpisah dari area Release notes.
- Sebut operasinya dan hasilnya, dengan warna/ikon sesuai tema: `Check passed`, `Check failed: <alasan singkat>`,
  `Push completed: 1 artifact`.
- Akhiri push dengan pesan eksplisit `Push completed`, menyebut image, semua tag yang terdorong, dan digest-nya
  (mis. `Push completed: root/container 0.1.0-alpha.1, alpha · sha256:…`), lalu tawarkan **Verify** sebagai langkah
  berikutnya. Log Docker tetap tersedia, tetapi bukan satu-satunya tanda selesai.
- Untuk Check, tampilkan daftar pemeriksaan (tools, Dockerfile, target, kredensial) beserta hasil masing-masing,
  karena tabel artifact tidak relevan untuk Check.

## Usulan: picker target

- Tampilkan nama (root: `Name`; container: `Name` atau `FullName`) lewat `ItemTemplate` eksplisit, atau periksa
  apakah style ComboBox Material mengabaikan `DisplayMemberPath`.
- Bila `GetMeta_CtnTree` mengembalikan nol container, tampilkan pesan "Root ini belum punya container. Buat dulu di
  tab Containers." dan nonaktifkan combobox kedua.
- Tombol picker memakai ulang pasangan combobox yang sudah ada (isi ulang `ItemsSource`), bukan menambah baru.

## Pertanyaan terbuka

- Apakah penyebab butir 1 ada di style ComboBox bersama (berarti combobox lain yang memakai `DisplayMemberPath`
  juga terdampak), atau hanya di combobox yang dibuat dari code-behind dialog ini?
- Picker dialog ini dibuat dari code-behind, berbeda dari aturan MVVM engine. Perbaikan cukup lokal, atau sekalian
  dijadikan bagian view model?

## Temuan 2026-10-19: profil Template, export, dan pembacaan tag

Ditemukan saat menyiapkan profil Container mode **Template** (Dockerfile yang sudah ada, `StagingSubfolder`)
yang berkasnya disimpan di dalam repo produk agar bisa diimport ulang, lalu dipush ke registry bawaan lewat HTTPS.

7. **Export tidak bisa diimport dari lokasi lain.** `ProfileStore.Export` → `MakePortable`
   (`src/shared/Em.Ui.Wpf.Core/Publish/PublishStorage.cs`) selalu menulis `"workspace": "."` dan path sumber relatif
   terhadap workspace. `ProfileStore.Read` menyelesaikan workspace relatif terhadap **folder berkas yang diimport**.
   Akibatnya hasil export hanya benar bila berkasnya disimpan tepat di folder workspace; disimpan di tempat lain
   (mis. `doc/publish-profiles/Container/`), workspace hasil import menjadi folder berkas itu dan semua path sumber
   meleset. Pengguna harus mengoreksi `workspace` dengan tangan setiap kali export ulang.
8. **Check lolos, Build gagal dengan pesan menyesatkan.** `Publisher.Check` hanya memastikan folder workspace ada.
   Dengan workspace yang salah (butir 7), Check lulus; Build baru gagal di `PublishOutput` dengan
   `Select an executable/Web project for Template publish. An error occurred trying to start process 'dotnet' with
   working directory '…'. The directory name is invalid.` Pesan itu menyuruh memilih project lain, padahal penyebabnya
   project tidak ditemukan di bawah workspace.
9. **Allow HTTP pada host HTTPS membuat pembacaan tag gagal tanpa alasan.** Bila kredensial `push` dicentang
   **Allow HTTP**, `OciClient.Request` memakai `http://<host>`. Registry di balik reverse proxy HTTPS menjawab
   **301** ke `https://`, sedangkan `HttpClientHandler` diset `AllowAutoRedirect=false`, sehingga daftar tag gagal.
   `PublishView.TakenTags` menelan exception (`catch(Exception) → RemoteKnown=false`) dan Publish hanya menampilkan
   `The tags on the registry could not be read, so the next build number cannot be chosen safely. Check the target
   and the credential, then try again.` Pengguna tidak diberi tahu bahwa masalahnya HTTP vs HTTPS.

## Usulan: profil Template, export, dan pembacaan tag

- **Export (7):** tulis `workspace` relatif terhadap folder berkas tujuan export (mis. `../../../src`), dan path
  sumber tetap relatif terhadap workspace. Bila tidak bisa dibuat relatif (beda drive), kosongkan workspace dan
  minta pengguna memilihnya saat import. Tambahkan uji round-trip export → import dari folder lain.
- **Check (8):** pada mode Template periksa bahwa `Template.Project` dan `Template.ExistingDockerfile` (bila
  `ExistingFile`) ada di bawah workspace; pada mode Dockerfile periksa `Dockerfile.File` dan `Dockerfile.Context`.
  Pesan menyebut path efektif yang dicari, mis. `Project not found: <path>. Check the workspace of the profile.`
- **Pembacaan tag (9):** bawa alasan kegagalan ke pesan Publish (status HTTP / jenis exception), jangan ditelan.
  Untuk 3xx dari `http://` ke `https://` host yang sama, beri pesan khusus: `The registry redirects to HTTPS. Turn off
  Allow HTTP on the credential for <host>.` Check juga bisa memberi peringatan yang sama lewat probe `GET /v2/`.

## Pertanyaan terbuka (temuan 7-9)

- Export yang memakai workspace relatif terhadap berkas: apakah perlu pilihan eksplisit di dialog Export ("simpan
  workspace relatif terhadap berkas ini"), atau cukup selalu dihitung otomatis?
- Pada butir 9, apakah publisher boleh otomatis mengikuti redirect HTTP → HTTPS ke host yang sama (kredensial tetap
  dalam batas host), atau tetap menolak dan hanya memberi pesan yang jelas?
