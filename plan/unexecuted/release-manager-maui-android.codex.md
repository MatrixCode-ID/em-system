# Plan — Release Manager merilis aplikasi MAUI Android dan update dari dalam aplikasi (untuk Codex)

Status: **belum dieksekusi**

Dibuat: 2026-10-25
Pembaca: Codex atau Claude Code. Berkas ini memuat semua keputusan yang dibutuhkan; jangan bertanya ke
pengguna selama eksekusi. Hal yang tidak tercakup diputuskan dengan pilihan yang paling konsisten dengan plan
ini, lalu dicatat di laporan eksekusi (bagian 9).

Plan pasangan di repo produk turunan menyesuaikan host Android produk itu dan dieksekusi **setelah kode plan
ini selesai** (bagian 2). Plan ini tidak menyebut produk tersebut.

Sebelum mulai, baca versi terbaru di disk:

- `CLAUDE.md` di root repo (aturan bersama Claude Code dan Codex), terutama: tes lewat RunBay, Registry
  mengikuti host, `EmWindow` dan dialog di tengah owner, help field/input angka, kilatan putih saat
  busy/disabled, managed CDN dan Release Manager (2026-10-21), bahasa komentar, guide wajib.
- `doc/release-format.md` dan `doc/release-format-samples/` (kontrak format rilis; diubah **lebih dulu**
  sebelum kodenya).
- Guide yang akan diubah: `doc/engine/engine-release-manager.md`, `doc/engine/engine-launcher.md`,
  `doc/engine/build.md`, `doc/engine/README.md`.
- `src/frontend/Launcher/CLAUDE.md` hanya untuk memastikan launcher **tidak** perlu diubah (bagian 3.4).

---

## 1. Tujuan

1. Release Manager (WPF) bisa membuat profile **Android** untuk host MAUI, menyiapkan environment build
   Android (workload, Android SDK, JDK), mengelola keystore, dan menjalankan Prepare yang menghasilkan **satu
   APK `arm64-v8a` bertanda tangan** dengan `versionCode` yang naik otomatis.
2. Rilis Android memakai tujuan dan alur yang sama dengan Windows (managed folder CDN atau Folder, quota,
   Compare/Sync/Verify, `.meta` `publicListing: false`) dan `release.json` bertanda tangan dengan objek
   `android` baru.
3. Aplikasi MAUI memeriksa update ke CDN tempat rilisnya diterbitkan saat aplikasi dibuka dan secara manual,
   mengunduh APK, memeriksa hash, lalu menyerahkannya ke package installer Android. Update biasa boleh ditunda;
   update wajib bila versi terpasang di bawah `minVersionCode`.

Di luar cakupan: iOS/MacCatalyst/Windows MAUI, Play Store/AAB, ABI selain `arm64-v8a`, penimpaan ikon/judul
aplikasi dari profile, perubahan launcher Rust.

---

## 2. Aturan eksekusi

- **Urutan lintas repo (keputusan pengguna 2026-10-25):** tulis seluruh kode plan ini lebih dulu, lalu kode
  plan pasangan di repo produk. Build, tes, Prepare nyata, review, dan perbaikan baru dijalankan **setelah kode
  kedua plan selesai**; jangan menjalankan tes di antara kedua plan. Per seksi tidak ada build/tes.
- **Commit per seksi** di branch `work-bench` (keputusan pengguna 2026-10-25). Periksa branch aktif sebelum
  commit; bila bukan `work-bench`, jangan commit dan catat di laporan. Pesan commit bahasa Inggris. **Tanpa
  push.** Perbaikan hasil tes akhir di-commit terpisah. Commit terakhir memindahkan plan ini ke
  `plan/executed/` beserta laporannya.
- **Build dan tes lewat RunBay** (aturan root `CLAUDE.md`). Pakai app RunBay `em-system` (EmCode ERP Engine).
  Cek dulu dengan `get_app`; tambahkan custom action baru bila belum ada, jangan membuat duplikat dan jangan
  menghapus action pengguna:
  - `build-release-android`: `dotnet build src/frontend/Em.Ui.Wpf.slnx` dan
    `dotnet build src/frontend/Em.Ui.Maui.slnx`.
  - `test-release-android`: `dotnet test src/frontend/Em.Ui.Wpf.slnx` (mencakup `Em.Ui.Core.Tests` dan
    `Em.Ui.Wpf.Core.Tests`).
  - `smoke-release-android`: menjalankan harness di `..\.artefacts\em-system\scripts\release-android-smoke\`.
  Script action menyiapkan environment-nya sendiri. Laporkan nama action dan run id.
- **Uji visual tidak dijalankan** (keputusan pengguna 2026-10-25). Layar/dialog baru dicatat sebagai
  verifikasi visual tertunda. Uji console boleh langsung.
- **Pengujian (keputusan pengguna 2026-10-25):** console + **Prepare APK nyata** memakai SDK/JDK yang sudah
  terpasang di mesin, Sync dan Verify ke target **Folder** sementara. Install dan update di emulator/perangkat
  dicatat tertunda. Prepare Environment yang benar-benar mengunduh/memasang (winget, workload, SDK) **tidak**
  dijalankan saat tes; cukup uji pembentukan perintahnya.
- **Tindakan yang ditolak policy:** jangan diakali; siapkan skrip `.ps1` manual di
  `plan/release-manager-maui-android-manual/` dan catat di laporan.
- **Bahasa:** kode, komentar, XML comment, teks UI, log, dan pesan error berbahasa Inggris. Plan, laporan,
  dan `CLAUDE.md` berbahasa Indonesia; `doc/release-format.md` mengikuti bahasa berkas yang ada.
- **Product neutral:** tidak ada nama produk turunan, alamat server, path mesin, atau secret di kode,
  dokumen, contoh, maupun test. Contoh identitas memakai `Contoso.Inventory.Mobile`,
  `com.contoso.inventory`, `inventory.apk`.
- **Guide:** perbarui guide di `doc/engine/` (bahasa Inggris) dan tautan di `doc/engine/README.md`.
- **Kompatibilitas:** profile lama tanpa `platform` dibaca sebagai Windows tanpa migrasi; `formatVersion`
  profile tetap 2. Rilis Windows dan launcher tidak berubah perilaku.

### Aturan yang tidak tertulis di CLAUDE.md (dari memori Claude)

- Property bindable `MvvmModelBase` (Get/Set) **wajib `public`**; property private gagal saat runtime. Getter
  yang memanggil `Trim()` membuang spasi saat binding TwoWay, jadi trim saat validasi/simpan, bukan di getter.
- `em-system` repo publik: kode dan dokumen tidak boleh menyebut produk turunan.
- Di mesin pengembang bisa ada dua Android SDK (`C:\Program Files (x86)\Android\android-sdk` milik Visual
  Studio dan `%LOCALAPPDATA%\Android\Sdk`); deteksi harus menampilkan keduanya sebagai kandidat, bukan
  mengambil yang pertama secara diam-diam.
- MAUI core memakai Font Awesome 7 (`FontIcons` di `Em.Ui.Maui.Core/Controls`); jangan memakai glyph FA6.

---

## 3. Keputusan final

Sumber: catatan ide "Rilis MAUI Android lewat Release Manager" (2026-10-25, repo produk) ditambah keputusan
pengguna 2026-10-25 saat plan ini ditulis.

### 3.1 Kode format rilis pindah ke `Em.Ui.Core`

- Seluruh isi `src/shared/Em.Ui.Wpf.Core/Release/Format/` (`ReleaseFile`, `ReleaseFormatException`,
  `ReleaseHash`, `ReleaseLauncherMetadata`, `ReleaseLayout`, `ReleaseManifest`, `ReleaseManifestSerializer`,
  `ReleaseSignature`, `ReleaseSignatureFile`) dipindah ke `src/shared/Em.Ui.Core/Release/Format/` dengan
  namespace `Em.Ui.Core.Release.Format` (keputusan pengguna 2026-10-25). Kodenya bebas WPF; MAUI
  mereferensi `Em.Ui.Core`.
- Pemakai di WPF core diperbarui `using`-nya. Tes format yang ada pindah ke `tests/Em.Ui.Core.Tests`.
- Tidak ada type forwarder; ini perubahan breaking untuk paket NuGet berikutnya, dicatat di bagian
  `Breaking changes` release note berikutnya bila release note ditulis (plan ini tidak menulis release note).

### 3.2 Profile

- `ReleaseProfile.Platform`: enum `ReleasePlatform { Windows = 0, Android = 1 }`, diserialisasi sebagai
  string. JSON tanpa field ini → `Windows`.
- Objek baru `ReleaseProfile.Android` (`ReleaseAndroid`), selalu ada (default kosong) agar JSON stabil:

  | Field | Arti |
  | --- | --- |
  | `applicationId` | nama paket Android; huruf kecil, reverse-domain, divalidasi pola Android (`^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`) |
  | `apkFileName` | nama APK tetap di `binaries/`, pola `^[a-z0-9][a-z0-9._-]*\.apk$`; default dari `AppId` (huruf kecil, `.` → `-`) + `.apk` |
  | `minVersionCode` | 0 = tidak ada update wajib; selain itu 1..versionCode rilis yang disiapkan |
  | `keystoreBase64` | berkas keystore (PKCS12 atau JKS) |
  | `keystoreAlias` | alias key |
  | `keystorePassword`, `keyPassword` | password store dan password key |
  | `certificateSha256` | SHA-256 sertifikat (hex huruf kecil), diisi saat Create/Import, hanya tampilan dan pengecekan |
  | `preparedVersionCode`, `preparedVersionName`, `preparedApkSha256` | hasil Prepare terakhir; dipakai Sync untuk menulis objek `android` |

- Validasi profile Android: `applicationId`, `apkFileName`, keystore lengkap, dan `PrivateKeyPem` wajib saat
  Prepare/Sync (bukan saat menyimpan draft). Blok `Launcher` diabaikan untuk Android.
- **Key manifest:** setiap profile Android memegang key P-256 sendiri (`PrivateKeyPem` yang sudah ada). Saat
  Platform sebuah profile diubah di Settings, Release Manager meminta konfirmasi lalu **membuat key baru**,
  sehingga profile hasil Duplicate dari profile Windows tidak memakai key yang sama.
- Settings menampilkan bagian sesuai platform: Windows → Launcher seperti sekarang; Android → blok Launcher
  **dinonaktifkan** (tampil, tidak bisa diedit), bagian Android (ApplicationId, APK file name, Minimum version
  code, Keystore, Android environment) aktif. Menu profile **Build launcher** dan **Open launcher folder**
  tetap tampil tetapi dinonaktifkan untuk profile Android.
- Settings menampilkan peringatan permanen di bagian Keystore dan key: keystore atau key yang hilang berarti
  update tidak bisa dipasang/diterima; lakukan **Export** profile sebagai backup.

### 3.3 Identitas dan deteksi host

- `ReleaseBuilder.ReadHostProjects` mengembalikan platform tiap host: TFM berakhiran `-android` → Android,
  `-windows` atau tanpa platform pada WinExe → Windows. Project multi-target memilih TFM `*-android` untuk
  profile Android. Daftar host di Settings hanya menampilkan host yang cocok dengan platform profile.
- `ReleaseProduct.ReadAsync` untuk Android membaca `ApplicationTitle` (AppName; fallback `Product`),
  `Company` (Publisher), `EmAppId`, `ApplicationId` (nilai awal `applicationId` profile bila kosong),
  `ApplicationVersion`, `Version`, `TargetFramework(s)`, item `MauiIcon` (ditampilkan bila SVG/PNG bisa
  dibaca; tidak wajib), dan `IsEmHost` = mereferensi `Em.Ui.Maui.Core` (ProjectReference atau
  PackageReference `EmSys.Ui.Maui.Core`). Ikon dan judul tidak ditimpa Release Manager.
- Tiga identitas terpisah: `EmAppId` Android sendiri (kepemilikan managed folder CDN, penyimpanan lokal),
  `applicationId` (di profile, diterapkan saat Prepare, **dikunci setelah rilis pertama**: Prepare dan Sync
  menolak bila `android.applicationId` di tujuan berbeda), sertifikat keystore (permanen).

### 3.4 Format `release.json` untuk Android

- `doc/release-format.md` diubah **lebih dulu**: layout folder sama; `binaries/` berisi **tepat satu** APK,
  `<apkFileName>`. Objek opsional bertanda tangan sejajar `launcher`:

  ```json
  "android": {
    "fileName": "inventory.apk",
    "applicationId": "com.contoso.inventory",
    "versionCode": 12,
    "versionName": "1.2.0",
    "minVersionCode": 10
  }
  ```

  Aturan: `fileName` menunjuk file di root `binaries/` yang tercantum di `files` dan berakhiran `.apk`;
  `applicationId` pola paket Android; `versionCode` 1..2100000000; `versionName` SemVer sesuai pola versi yang
  dipakai `Version`; `minVersionCode` opsional, 1..`versionCode`. Manifest boleh memuat `launcher` **atau**
  `android`, tidak keduanya. Tanpa `signingCertSha256` (Android memeriksa sertifikat sendiri).
- Pembaca: client MAUI **menolak** manifest tanpa objek `android`. Launcher Rust **tidak diubah**: key Android
  berbeda sehingga launcher Windows menolak manifest Android lewat `keyId`. Pembaca lama mengabaikan field
  tak dikenal; tidak ada nomor versi format baru.
- Contoh uji baru di `doc/release-format-samples/`: `android-valid/`, `android-missing-object/` (manifest
  valid tanpa `android`, harus ditolak client Android), `android-bad-file/` (`fileName` tidak ada di `files`),
  dan `android-and-launcher/` (keduanya ada, ditolak). Ditandatangani key sampel yang sudah ada; perbarui
  `expected.txt`.
- `ReleaseAndroidMetadata` ditambahkan di `Em.Ui.Core.Release.Format`, dibaca/ditulis
  `ReleaseManifestSerializer` dengan validasi di atas.

### 3.5 Versi

- `versionName` = property `Version` host (SemVer), satu sumber versi seperti Windows.
- `versionCode` = `android.versionCode` rilis di tujuan + 1. Tujuan tanpa rilis → `ApplicationVersion` di
  csproj bila bilangan bulat ≥ 1, selain itu 1. Tujuan berisi manifest tanpa objek `android` (rilis Windows)
  → Prepare berhenti dengan pesan jelas. Tujuan tidak terbaca (CDN tidak terjangkau, butuh login) → Prepare
  berhenti; versionCode tidak ditebak.
- Nilai diterapkan lewat targets yang dihasilkan (`ApplicationVersion`, `ApplicationDisplayVersion` =
  `versionName` tanpa metadata build), bukan diketik di csproj. Akibatnya setiap Prepare Android menghasilkan
  APK berbeda; guide menyebut Prepare hanya saat akan merilis.

### 3.6 Keystore

- **Create:** `keytool` dari JDK terpilih:
  `-genkeypair -storetype PKCS12 -keyalg RSA -keysize 4096 -validity 10000 -alias <alias>
  -dname "CN=<Publisher>"`, password lewat `-storepass:env` dan `-keypass:env` (environment variable proses
  anak), berkas di folder sementara lalu dibaca ke `keystoreBase64` dan dihapus. Password dibuat acak kuat
  (32 karakter) dan bisa dilihat/disalin di Settings. Masa berlaku 10000 hari (> 25 tahun).
- **Import:** menerima `.jks`, `.keystore`, `.p12`/`.pfx`; alias dan dua password dimasukkan pengguna lalu
  diperiksa saat itu juga dengan `keytool -list -alias` (password lewat `:env`).
- **SHA-256 sertifikat:** `keytool -exportcert -rfc -alias` lalu dihitung di .NET; disimpan di
  `certificateSha256` dan ditampilkan.
- Saat Prepare keystore ditulis ke folder sementara per profile, password diteruskan sebagai environment
  variable proses `dotnet publish` (`EM_ANDROID_STORE_PASS`, `EM_ANDROID_KEY_PASS`) dan dirujuk dengan
  `env:` di properti signing, lalu berkasnya dihapus di `finally`. Password tidak pernah muncul di command line,
  berkas targets, atau log.

### 3.7 Environment build Android

- Preferensi mesin di `BaseRegKey\ReleaseManager` (seperti folder launcher, bukan profile):
  `AndroidSdkDirectory`, `JavaSdkDirectory`.
- **Deteksi kandidat**, urutan awal: pilihan pengguna tersimpan > `ANDROID_HOME`/`JAVA_HOME` > Visual Studio
  (`C:\Program Files (x86)\Android\android-sdk`, `C:\Program Files\Microsoft\jdk-*`) > Rider (lokasi SDK/JDK
  yang dikonfigurasi Rider, mis. `%LOCALAPPDATA%\Android\Sdk` dan JDK di folder JetBrains bila ada) >
  `%LOCALAPPDATA%\Android\Sdk`. Semua kandidat yang ada ditampilkan dalam combo untuk dipilih; pengguna juga
  bisa Browse folder sendiri.
- **Pemeriksaan** per kandidat: SDK punya `platform-tools`, `build-tools` (berisi `apksigner`), dan platform API
  target project; JDK punya `bin\keytool.exe` dan `bin\java.exe` dengan versi yang didukung workload
  (properti MSBuild `MinimumSupportedJavaVersion`/`LatestSupportedJavaVersion` dari host project bila
  tersedia, selain itu minimal 17); workload `maui-android` terpasang (`dotnet workload list`). Hasil tampil
  per baris (OK/Missing + alasan). SDK di `Program Files (x86)` yang kurang komponen diberi saran memakai SDK
  di `%LOCALAPPDATA%\Android\Sdk` karena butuh hak admin.
- **Prepare Environment** (tombol), hanya memasang yang kurang, dengan konfirmasi daftar langkah:
  1. workload: `dotnet workload install maui-android` (elevated bila dotnet di Program Files);
  2. JDK: `winget install --id Microsoft.OpenJDK.<versi> --exact --accept-source-agreements` (keputusan
     pengguna 2026-10-25), versi = `LatestSupportedJavaVersion` atau 21; lokasi bawaan
     `C:\Program Files\Microsoft\jdk-…`. Bila winget tidak ada → pesan untuk memasang App Installer atau
     menunjuk JDK manual;
  3. SDK: `dotnet build <host> -t:InstallAndroidDependencies -f <tfm-android>
     -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk> -p:AcceptAndroidSDKLicenses=True`, dengan SDK
     terpilih atau `%LOCALAPPDATA%\Android\Sdk` bila belum ada. Tolak path ber-spasi/non-ASCII untuk SDK baru.
  Output dialirkan ke log Release Manager; lalu pemeriksaan diulang.
- Checkbox **Set ANDROID_HOME and JAVA_HOME for this user** (default mati) di Prepare Environment. Tanpa itu
  environment variable tidak diubah; build selalu menerima path eksplisit `-p:AndroidSdkDirectory` dan
  `-p:JavaSdkDirectory`.

### 3.8 Prepare Android

1. Validasi profile Android dan environment (3.7); gagal → pesan apa yang kurang.
2. Baca rilis di tujuan (Compare-read yang sama dengan Windows) untuk menentukan `versionCode` (3.5) dan
   memeriksa `applicationId` terkunci (3.3).
3. Tentukan alamat rilis yang ditanam: `ReleaseTarget.ReleaseAddress(CdnAddress, ReleaseFolder)`; untuk CDN
   dengan `CdnAddress` kosong diturunkan dari koneksi aktif; target Folder **wajib** punya `CdnAddress`
   (alamat publik tempat folder itu dilayani), selain itu Prepare berhenti.
4. `WriteIdentityTargets` diperluas untuk Android (tetap bersyarat path host): `EmAppId`, `ApplicationId`,
   `ApplicationVersion`, `ApplicationDisplayVersion`, `AndroidPackageFormat=apk`,
   `RuntimeIdentifiers=android-arm64`, `AndroidKeyStore=true`, `AndroidSigningKeyStore`,
   `AndroidSigningKeyAlias`, `AndroidSigningStorePass=env:EM_ANDROID_STORE_PASS`,
   `AndroidSigningKeyPass=env:EM_ANDROID_KEY_PASS`, dan item `AssemblyAttribute`
   `System.Reflection.AssemblyMetadataAttribute` untuk `EmReleaseAddress` (alamat rilis lengkap, berakhiran
   `/`) dan `EmReleasePublicKey` (public key SPKI DER Base64). Tidak menulis `AssemblyName`, `Product`,
   ikon, atau logo untuk Android.
5. `dotnet publish <host> -c Release -f <tfm-android> -r android-arm64 -p:AndroidSdkDirectory=<sdk>
   -p:JavaSdkDirectory=<jdk> -p:CustomBeforeMicrosoftCommonTargets=<targets> -o <folder sementara>` dengan
   environment variable password. `DeterminismArguments` Windows tidak dipakai bila tidak berlaku untuk Android.
6. Ambil `<applicationId>-Signed.apk` dari output (gagal bila tidak ada tepat satu), kosongkan Publish folder
   dengan pemeriksaan aman yang sudah ada, salin sebagai `<apkFileName>`. Tidak ada launcher.
7. Simpan `preparedVersionCode`, `preparedVersionName`, `preparedApkSha256` ke profile; hapus berkas sementara
   (keystore, targets, output) di `finally`.

### 3.9 Compare, Sync, Verify, dan link instalasi

- Compare/Sync/Verify, quota, managed folder, dan `.meta` dipakai apa adanya.
- Sync profile Android menulis objek `android` dari `prepared*` + `applicationId` + `apkFileName` +
  `minVersionCode`; menolak bila hash APK di Publish folder ≠ `preparedApkSha256` (Prepare ulang), bila
  `minVersionCode` > `preparedVersionCode`, atau bila Publish folder berisi selain satu APK tersebut.
- Verify profile Android juga memeriksa objek `android` ada, `fileName` cocok, dan `applicationId` sama dengan
  profile.
- Menu profile **Copy install link** (aktif untuk Android setelah alamat rilis bisa ditentukan): menyalin
  `<alamat rilis>/binaries/<apkFileName>`.

### 3.10 Update dari aplikasi MAUI

- **Logika platform-neutral di `Em.Ui.Core`** (`Release/Update/`), agar bisa dites tanpa Android:
  `ReleaseUpdateSource` (alamat + public key dari `AssemblyMetadata` assembly host) dan `ReleaseUpdateChecker`
  yang mengunduh `release.json` dan `release.json.sig` (timeout 5 detik, tanpa cache), memverifikasi tanda
  tangan dengan key tertanam, mewajibkan objek `android`, memeriksa `applicationId` = paket terpasang, lalu
  menghasilkan `UpToDate`, `Optional`, `Mandatory` (`installed < minVersionCode`), `NotConfigured` (build tanpa
  metadata, mis. debug), atau `Failed` (alasan). Versi lebih rendah/sama dari terpasang → `UpToDate`.
- **Android di `Em.Ui.Maui.Core`** (`Platforms`/folder `Update/` dengan `#if ANDROID` bila perlu):
  - Sumber metadata: assembly `Application.Current.GetType().Assembly` (bukan `GetEntryAssembly`, yang bisa
    null di Android).
  - Versi terpasang dari `PackageManager.GetPackageInfo` (`LongVersionCode` API 28+, `VersionCode` di bawahnya)
    dan `PackageName`.
  - Unduh APK ke `CacheDir/updates/`, periksa ukuran dan SHA-256 terhadap manifest, hapus bila tidak cocok.
  - Pasang lewat **`PackageInstaller` session API** (tanpa FileProvider): tulis APK ke session, commit dengan
    `PendingIntent` ke `BroadcastReceiver` yang didaftarkan saat runtime (intent eksplisit ke paket sendiri,
    `PendingIntentFlags.Mutable` di API 31+). Status `STATUS_PENDING_USER_ACTION` → jalankan intent konfirmasi;
    status gagal → pesan.
  - Izin: `[assembly: UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]` di `Em.Ui.Maui.Core`.
    Setelah build, periksa manifest gabungan host (`obj/.../AndroidManifest.xml`); bila izin tidak ikut
    tergabung, tambahkan lewat `buildTransitive/EmSys.Ui.Maui.Core.targets` atau, terakhir, tulis entri yang
    harus ditambahkan host di guide dan di host `Em.Ui.Maui`. Catat cara yang berhasil di laporan.
  - API 26+: bila `CanRequestPackageInstalls()` false, tampilkan penjelasan lalu buka
    `Settings.ActionManageUnknownAppSources` untuk paket ini; kembali ke aplikasi → coba lagi.
- **Alur UI:**
  - Saat aplikasi dibuka, di `EmApp` sebelum `ShowLoginScreen`: jalankan cek. `Failed`/`NotConfigured`/timeout →
    lanjut ke login tanpa gangguan (dicatat di log). `Optional` → dialog **Update available** (versionName baru,
    versi terpasang) dengan **Update now** / **Later**; ditanyakan lagi pada pembukaan berikutnya. `Mandatory` →
    layar blokir **Update required** dengan **Update** dan **Exit**, tanpa akses ke aplikasi.
  - Selama unduh tampil progres dan tombol batal; kegagalan bisa dicoba ulang.
  - **AccountPanel**: baris versi terpasang (`versionName (versionCode)`) dan **Check for updates**.
  - **LoginControl**: teks versi dan tautan **Check for updates** (update bisa dibutuhkan sebelum login).
  - Cek manual memakai alur yang sama; `UpToDate` → pesan singkat; `NotConfigured` → "Updates are not
    configured for this build".
- Gaya UI mengikuti layar MAUI core yang ada (AccountPanel, LoginControl) dan MVVM `MvvmModelBase` dengan
  `RegisterCommand`.

### 3.11 Host contoh `Em.Ui.Maui`

- Tambahkan `<Version>` SemVer dan `<EmAppId>` netral (ikuti pola penamaan `EmAppId` host WPF em-system; bila
  tidak ada, `Em.Ui.Maui.Android`). `ApplicationId` `com.matrixcode.emsystem` tetap. Host ini dipakai smoke.

### 3.12 Batasan versi pertama

- Satu ABI (`arm64-v8a`); emulator x64 memakai build debug, bukan rilis.
- Update hanya diperiksa saat aplikasi dibuka dan manual; tidak ada cek berkala/latar belakang atau push.
- Tidak ada rollback/downgrade (Android menolaknya); perbaikan dilakukan dengan rilis versionCode baru.
- Pindah antara build debug (debug keystore) dan rilis pada satu perangkat perlu uninstall (ApplicationId sama).

---

## 4. Mockup / UI

em-system tidak mewajibkan mockup. UI WPF baru (bagian Android di Release settings, Prepare Environment, dialog
keystore) mengikuti acuan Material yang ada (`Dialogs/ReleaseSettingsDialog`, `Navigations/UserManager.xaml`,
`EmWindow`, help ToolTip, `NumericBox` untuk Minimum version code). UI MAUI baru mengikuti AccountPanel dan
LoginControl. Semua tercatat sebagai verifikasi visual tertunda.

---

## 5. Seksi kerja

### Seksi A — Kontrak format

- Ubah `doc/release-format.md` (3.4) dan buat contoh uji baru di `doc/release-format-samples/`.
- Commit.

### Seksi B — Pindah format ke `Em.Ui.Core` dan objek `android`

- Pindahkan `Release/Format` (3.1), perbarui pemakai WPF dan tes.
- Tambah `ReleaseAndroidMetadata` dan validasi serializer (3.4).
- Commit.

### Seksi C — Profile, identitas, dan host

- `ReleasePlatform`, `ReleaseAndroid`, validasi, pembuatan key baru saat Platform berubah (3.2).
- `ReadHostProjects` dengan platform dan `ReleaseProduct` Android (3.3).
- Commit.

### Seksi D — Environment, keystore, dan Prepare Android

- Preferensi mesin, deteksi kandidat, pemeriksaan, Prepare Environment (3.7).
- Create/Import keystore dan SHA-256 (3.6).
- `versionCode`, targets Android, publish, salin APK, `prepared*` (3.5, 3.8).
- Commit.

### Seksi E — Sync/Verify, link, dan UI Release Manager

- Sync/Verify Android, Copy install link (3.9).
- Release settings sesuai platform, blok Launcher nonaktif, menu launcher nonaktif untuk Android, bagian
  Android, Prepare Environment, peringatan backup (3.2).
- Commit.

### Seksi F — Update di MAUI

- `Em.Ui.Core/Release/Update` (checker platform-neutral) dan bagian Android di `Em.Ui.Maui.Core`, izin,
  dialog/layar update, AccountPanel, LoginControl (3.10).
- Host `Em.Ui.Maui` (3.11).
- Commit.

### Seksi G — Guide

- `engine-release-manager.md`: profile Platform, Android settings, environment, keystore, versi, Prepare,
  Copy install link, backup.
- Guide baru `doc/engine/engine-android-updates.md`: metadata tertanam, alur cek/unduh/pasang, izin, update
  wajib, batasan; tautkan dari `doc/engine/README.md`.
- `engine-launcher.md`: catatan bahwa rilis Android tidak memakai launcher dan ditolak lewat `keyId`.
- `build.md`: versi Android (`versionName`/`versionCode`).
- Commit.

Setelah seksi G: **berhenti menulis kode engine**, kerjakan kode plan pasangan di repo produk, lalu lanjut ke
bagian 6.

---

## 6. Pengujian (setelah kode kedua plan selesai)

1. RunBay `build-release-android`: kedua solution frontend lolos tanpa error.
2. RunBay `test-release-android`. Tambah test untuk:
   - serializer: round-trip objek `android`, semua aturan validasi, `launcher`+`android` ditolak, vektor
     `release-format-samples` baru dan lama;
   - `ReleaseUpdateChecker`: UpToDate/Optional/Mandatory/NotConfigured/Failed, tanda tangan salah, key lain,
     manifest tanpa `android`, `applicationId` beda, timeout (HTTP handler palsu);
   - profile: JSON lama → Windows, validasi Android, key baru saat Platform berubah;
   - `ReadHostProjects`/identitas Android memakai project fixture kecil;
   - perhitungan `versionCode` (tujuan kosong, `ApplicationVersion`, rilis Windows di tujuan, tujuan tak
     terbaca);
   - isi targets Android: properti signing memakai `env:`, tidak ada password, `AssemblyMetadata` benar,
     bersyarat path host;
   - argumen publish, perintah keytool/winget/workload/InstallAndroidDependencies (tanpa menjalankannya);
   - urutan kandidat SDK/JDK dengan folder palsu dan base key Registry terisolasi.
3. RunBay `smoke-release-android` (harness console di `..\.artefacts\em-system\scripts\release-android-smoke\`,
   output di `out/`): dengan SDK/JDK yang terpasang, buat keystore sementara lewat jalur Create, Prepare host
   `Em.Ui.Maui` ke target Folder sementara (dengan `CdnAddress` placeholder), Sync, Verify; periksa APK dengan
   `apksigner verify --print-certs` (SHA-256 = `certificateSha256`) dan `aapt2 dump badging` (package,
   versionCode, versionName, ABI `arm64-v8a`); Prepare kedua → versionCode +1; manifest gabungan memuat
   `REQUEST_INSTALL_PACKAGES`. Bila SDK/JDK/workload tidak ada di mesin, catat sebagai tertunda.
4. **Tertunda** (dicatat, tidak dijalankan): uji visual semua layar baru, Prepare Environment yang benar-benar
   memasang, install dan update di emulator/perangkat, Sync ke CDN nyata.

---

## 7. Selesai bila

- Seksi A–G selesai dan di-commit di `work-bench`; build/test/smoke bagian 6 lolos atau kegagalannya
  dijelaskan.
- Guide cocok dengan kode.
- Ringkasan singkat ditambahkan ke `CLAUDE.md` root (pola bagian "Managed CDN, Release Manager dan
  launcher").
- Plan dipindah ke `plan/executed/` dan laporan ditulis.

## 8. Tindakan manual

Tidak ada yang direncanakan. Tindakan yang terblokir policy masuk ke `plan/release-manager-maui-android-manual/`.

## 9. Laporan eksekusi

Tulis `doc/report/release-manager-maui-android.md` (bahasa Indonesia): ringkasan per seksi, commit, hasil
build/test/smoke dengan angka dan run id RunBay, keputusan implementasi yang diambil sendiri (mis. cara izin
`REQUEST_INSTALL_PACKAGES` tergabung, sumber versi Java yang didukung, lokasi Rider), verifikasi tertunda, dan
tindakan manual. Jangan menyatakan verifikasi tertunda sebagai lulus.
