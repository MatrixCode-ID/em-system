# Profile di Release Manager

- Tanggal: 2026-10-04
- Status: jadi plan

## Gagasan

Release Manager (WPF) sekarang hanya mengingat satu set setting, jadi hanya bisa melayani satu aplikasi. Usulannya: setting dikelompokkan ke dalam **profile** bernama, dan user memilih profile aktif di layar. Satu profile berisi satu aplikasi beserta tujuan rilisnya. Fondasi rilis tidak berubah; yang berubah hanya UI.

## Kondisi sekarang

- `Release/ReleaseSettings.cs` membaca dan menulis langsung ke Registry per user, subkey `ReleaseManager` di bawah `EmApp.BaseRegKey`, dengan value datar: `SolutionPath`, `HostProject`, `PublishFolder`, `TargetKind`, `TargetFolder`, `ReleaseFolder`, `SigningThumbprint`.
- `Navigations/ReleaseManager.xaml.cs` membuat satu `ReleaseSettings` saat `Initialize()`, lalu `ApplySettings()` menyalin isinya ke property layar. `ReleaseSettingsDialog` mengedit setting yang sama.
- Fondasi (`ReleaseBuilder`, `LocalPublish`, `ReleaseComparer`, `ReleaseSync`, `ReleaseVerifier`, `CdnReleaseTarget`/`FolderReleaseTarget`, format rilis) menerima nilai lewat parameter dan tidak membaca setting sendiri. Karena itu klaim "tidak mengubah fondasi" masuk akal.

## Keputusan yang sudah jelas

- 2026-10-04: **isi setting profile tidak lagi disimpan di Registry.** Setiap profile disimpan sebagai satu berkas JSON, mengikuti pola profile Publish di NuGet Manager/Container Manager (`Publish/PublishStorage.cs`: satu `<id>.json` per profile di folder Profiles, ditulis atomik lewat `PublishPaths.Atomic`). Akibatnya `ReleaseSettings` ditulis ulang menjadi model profile dan penyimpanannya, sedangkan fondasi rilis tetap tidak berubah.
- 2026-10-04: **lokasi berkas profile adalah `Documents\Em\ReleaseManager\Profiles\Release\`** milik user Windows (`Environment.SpecialFolder.MyDocuments`), terpisah dari folder Publisher. Karena foldernya sendiri, Release Manager memakai store kecil sendiri, bukan `PublishStorage` yang terikat ke `PublishKind`. Helper umum seperti `PublishPaths.Atomic` masih bisa dipakai ulang.
- 2026-10-04: **folder profile bisa diubah dari Settings**, seperti folder Profiles di Publisher. `Documents\Em\ReleaseManager\Profiles\Release\` menjadi nilai bawaan.
- 2026-10-04: **path folder profile dan profile terakhir yang dipilih tetap disimpan di Registry**, seperti `Profiles`/`LastProfile` di Publisher: dua value kecil di subkey `ReleaseManager` di bawah `EmApp.BaseRegKey`. Isi profile tidak disimpan di Registry.
- 2026-10-04: **sumber signing key dipilih per profile (opsi C):** (1) certificate store `CurrentUser\My` lewat `SigningThumbprint`, seperti sekarang dan tetap jadi bawaan; atau (2) berkas `.pfx` di folder profile, dibuka dengan password saat dibutuhkan. (Ketentuan "password tidak pernah disimpan" diubah oleh keputusan password `.pfx` di bawah.) Untuk sumber store, folder profile boleh menyimpan public key `.pem` sebagai referensi. Fondasi tidak berubah karena kedua sumber menghasilkan `X509Certificate2` yang sama untuk `ReleaseSync`. Alasan dan pertimbangan opsi A/B dicatat di bagian "Pertimbangan signing key".
- 2026-10-04: **satu profile = satu subfolder** `<folder profile>\<id>\` berisi `profile.json` dan, kalau sumber key-nya berkas, `.pfx` miliknya. (Ini mengikuti keputusan hapus profile di bawah. Penyebutan "satu berkas `<id>.json` per profile" di keputusan pertama berarti satu berkas setting per profile, kini diletakkan di subfolder itu.)
- 2026-10-04: **profile terakhir diingat per koneksi server**, bukan per user saja. Value-nya tetap di Registry (subkey `ReleaseManager`), dengan kunci berdasarkan koneksi aktif (`EmApp.ActiveConnection`). Ganti koneksi berarti layar memilih profile terakhir milik server itu.
- 2026-10-04: **tabrakan tujuan hanya diberi peringatan, tidak diblokir.** Peringatan muncul kalau profile lain menunjuk tujuan yang sama (CDN + `ReleaseFolder` yang sama, atau folder tujuan + `ReleaseFolder` yang sama).
- 2026-10-04: **local publish folder yang sama di dua profile hanya diberi peringatan.** Tidak ada penanda kepemilikan di folder itu.
- 2026-10-04: **hapus profile hanya menghapus subfolder profile beserta isinya** (`profile.json` dan `.pfx` bila ada), setelah konfirmasi. Local publish folder, rilis di tujuan, dan key di certificate store tidak disentuh.
- 2026-10-04: **password `.pfx` boleh disimpan sebagai teks biasa di `profile.json`**, sebagai pilihan per profile. Berkas profile yang memuat password berarti siapa pun yang bisa membaca atau menyalin folder itu bisa menandatangani rilis. Pilihan ini harus diambil secara sadar di Settings, dengan keterangan yang jelas, seperti mode `Plaintext` di Publisher.
- 2026-10-04: **mode password `.pfx` mengikuti pola `SensitiveDataStorage` Publisher:** `Separate` (bawaan) meminta password sekali per sesi aplikasi; dengan pilihan Remember, password disimpan terenkripsi DPAPI CurrentUser di LocalApplicationData (hanya user dan mesin ini). `Plaintext` menyimpan password sebagai teks biasa di `profile.json`. Gagal dekripsi berarti password dianggap tidak tersedia dan ditanyakan lagi.
- 2026-10-04: **Export profile mengikuti Publisher:** sebelum export, user ditanya apakah data sensitif ikut disertakan (default No). Jika No, `.pfx` dan password plaintext tidak ikut. Password `Separate`/Remember tidak pernah ikut.
- 2026-10-04: **Create/Import signing key memilih tujuan saat itu** (folder profile atau certificate store); pilihan awal mengikuti sumber key profile.
- 2026-10-04: **server untuk profile terakhir dikenali dari alamat (`Host`)**, bukan nama koneksi.
- 2026-10-04: **peringatan tabrakan muncul saat Save di Settings dan dicek lagi sebelum Prepare/Sync.**
- 2026-10-04: **letak UI:** combobox profile + tombol menu di toolbar, sebelum tombol Settings. Menu berisi New, Duplicate, Rename, Delete, Import, Export, Open folder, Profiles folder.
- 2026-10-04: **folder profile diubah lewat dialog kecil dari menu** ("Profiles folder..."), bukan di dialog Settings profile.
- 2026-10-04: **Export/Import memakai satu berkas `.zip`**; Import juga menerima `profile.json` lepas.
- 2026-10-04: **belum ada profile → keadaan kosong** dengan tombol New/Import, bukan membuat `Default` otomatis (kecuali migrasi setting Registry lama).

## Bentuk yang terpikir

- **Penyimpanan:** `<id>\profile.json` per profile berisi `Id`, `Name`, dan nilai yang sekarang ada di Registry (`SolutionPath`, `HostProject`, `PublishFolder`, `TargetKind`, `TargetFolder`, `ReleaseFolder`, sumber key, `SigningThumbprint`). Thumbprint bukan rahasia; password signing key tetap tidak disimpan.
- **Bonus dari pola berkas:** Duplicate, Import, dan Export profile, serta Open folder, bisa meniru tab Publish.
- **Migrasi:** saat pertama dibuka, jika value Registry lama ada dan belum ada profile, value itu dijadikan profile `Default` lalu dihapus dari Registry.
- **UI:** combobox profile di header layar, di dekat tombol Settings, ditambah aksi New, Duplicate, Rename, dan Delete (lewat menu kecil atau di dalam dialog Settings). Ganti profile memanggil `ApplySettings()` lalu `ReloadAsync()` (Compare otomatis). Combobox dinonaktifkan saat `IsRunning`.
- **Dialog Settings** mengedit profile aktif dan menampilkan namanya di judul.

## Pertanyaan terbuka

Tidak ada. Semua keputusan sudah final dan dipindah ke plan.

## Pertimbangan signing key (opsi A/B/C, sebelum diputuskan)

- Kondisi sekarang (`Release/SigningCertificates.cs`): Create menulis `.pfx` ke lokasi pilihan user lalu memasangnya ke store `CurrentUser\My`, bawaannya non-exportable. Import memasang `.pfx` ke store yang sama. Password tidak pernah disimpan. Profile hanya menyimpan `SigningThumbprint`.
- **Opsi A, `.pfx` di folder profile:** key ikut profile, mudah dipindah ke mesin lain, dan tiap profile jelas memakai key-nya sendiri. Akibatnya: (a) password perlu ditanyakan saat Sync (sekali per Sync atau sekali per sesi) karena tidak disimpan; (b) perlindungan non-exportable dari store hilang, dan key hanya dilindungi kekuatan password; (c) folder Documents sering ikut tersinkron (OneDrive) atau ter-backup, sehingga key ikut tersebar; (d) Export profile harus jelas menyertakan key atau tidak. Fondasi tetap aman karena `.pfx` yang dibuka dengan password menghasilkan `X509Certificate2` yang sama seperti dari store.
- **Opsi B, key tetap di store:** profile menyimpan thumbprint, dan folder profile boleh berisi public key (`.pem`) sebagai referensi. Perubahannya paling kecil, tetapi profile tidak membawa key saat dipindah ke mesin lain.
- **Opsi C, keduanya:** profile memilih sumber key, yaitu store (thumbprint) atau berkas `.pfx` di folder profile.

## Plan turunan

- [plan/executed/release-manager-profile.codex.md](../../plan/executed/release-manager-profile.codex.md) (2026-10-04, untuk Codex).

- Laporan eksekusi 2026-10-04: [release-manager-profile-eksekusi.md](../report/release-manager-profile-eksekusi.md).
