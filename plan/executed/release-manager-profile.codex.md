# Plan — Profile di Release Manager (WPF)

Status: **sudah dieksekusi** (2026-10-04)
Dibuat: 2026-10-04, dari ide [doc/ideas/release-manager-profile.md](../../doc/ideas/release-manager-profile.md)
Pelaksana: Codex (`.codex.md`)
Baseline: branch `main`, commit `166c383`

---

## Kenapa ada berkas ini

Release Manager (layar WPF `admin.release` di `Em.Ui.Wpf.Core`) saat ini hanya mengingat **satu** set setting di
Registry, jadi hanya bisa melayani satu aplikasi. Plan ini menambahkan **profile**: setiap profile menyimpan satu
aplikasi (sumber, tujuan, signing key), dan user memilih profile aktif di layar.

**Fondasi rilis tidak boleh berubah.** Artinya: format rilis (`doc/release-format.md`, `Release/Format/*`),
`ReleaseBuilder`, `LocalPublish`, `ReleaseComparer`, `ReleaseSync`, `ReleaseVerifier`, `ReleaseTarget`,
`CdnReleaseTarget`, dan `FolderReleaseTarget` **tidak disentuh**, kecuali `SigningCertificates` yang boleh mendapat
method/overload **baru** (method yang ada tidak diubah perilakunya dan tidak di-rename). Yang berubah hanya
penyimpanan setting, ViewModel layar, dialog Settings, dan dialog kecil baru.

## Cara membaca plan ini

- Bagian **Keputusan final** adalah hukum. Semua sudah diputuskan pengguna; **jangan bertanya ke pengguna** saat
  eksekusi. Kalau muncul hal yang tidak tercakup, ambil pilihan yang paling konsisten dengan tabel keputusan dan
  pola kode yang sudah ada, lalu catat di laporan eksekusi (bagian "Keputusan saat eksekusi").
- Kerjakan **Tahap 1 sampai 6 (penulisan kode dan dokumen) lebih dulu sampai selesai semua**, baru **Tahap 7**
  (build penuh, smoke, render, review, perbaikan, laporan, commit). Jangan menyelipkan uji atau review di antara
  tahap penulisan kode (aturan `claude.md`: kode dulu, analisa di akhir). Build cepat per tahap boleh bila murah,
  tetapi tidak wajib.
- Bila token menipis: tuntaskan kode semua tahap dulu, hentikan uji/analisa, dan catat di laporan apa yang belum
  sempat diuji.
- Bila ada tindakan yang ditolak dengan `blocked by policy`, ikuti bagian "Tindakan terblokir policy" di
  `claude.md`: jangan mengakali; siapkan skrip `.ps1` di `plan/release-manager-profile-manual/` dan catat di laporan.
- Bahasa: komentar XML-doc dan teks dokumen dalam Bahasa Indonesia (ikuti gaya file yang ada di `Release/` dan
  `Navigations/ReleaseManager.xaml.cs`); **teks UI dalam Bahasa Inggris** (seperti seluruh teks UI Release Manager
  sekarang); komentar `//` di dalam kode mengikuti gaya file (Inggris di file Release Manager).
- Gaya kode: ikuti file sekitarnya (indentasi 3 spasi, brace gaya file `Release/*.cs`, `MvvmModelBase` dengan
  `Get<T>()`/`Set(value)`, `RegisterCommand(nameof(XCommand), XCommand, XCommandAllowed)`). Property
  `Get`/`Set` di `MvvmModelBase` **wajib public getter** (properti private gagal saat runtime). Jangan pakai gaya
  padat satu baris seperti `Publish/*.cs`.

---

## Keputusan final

| # | Topik | Keputusan |
| --- | --- | --- |
| K1 | Penyimpanan isi profile | Isi setting **tidak lagi di Registry**. Satu profile = **satu subfolder** `<ProfilesFolder>\<id>\` berisi `profile.json`, dan bila sumber key-nya berkas: `signing.pfx` + `signing.cer`. `<id>` = GUID format `N` (32 hex huruf kecil). |
| K2 | Lokasi bawaan | `ProfilesFolder` bawaan = `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Em", "ReleaseManager", "Profiles", "Release")`. |
| K3 | Folder bisa diubah | Lewat item menu **Profiles folder...** yang membuka **dialog kecil** (path, Browse, tombol copy, Reset to default, Save/Cancel), seperti `PublisherSettingsDialog`. Mengganti folder **tidak memindahkan** berkas; layar membaca ulang daftar dari folder baru. |
| K4 | Yang tetap di Registry | Hanya preferensi kecil di subkey `ReleaseManager` di bawah `EmApp.BaseRegKey`: value `ProfilesFolder` (kosong = bawaan) dan subkey `LastProfiles` (lihat K5). |
| K5 | Profile terakhir | Diingat **per server**. Subkey `ReleaseManager\LastProfiles`, nama value = kunci server, isi = id profile. Kunci server dari `EmApp.ActiveConnection.Host` (**alamat**, bukan `ProfileName`): bila `Uri.TryCreate` absolut dengan skema http/https → `uri.Authority.ToLowerInvariant()` (host + port non-default); selain itu `Host.Trim().TrimEnd('/').ToLowerInvariant()`. Tanpa koneksi → kunci `(none)`. |
| K6 | Migrasi Registry lama | Saat layar pertama kali memuat daftar: jika folder profile **tidak berisi profile valid** dan subkey `ReleaseManager` memiliki salah satu value lama (`SolutionPath`, `HostProject`, `PublishFolder`, `TargetKind`, `TargetFolder`, `ReleaseFolder`, `SigningThumbprint`), buat profile bernama `Default` dari value itu (sumber key `Store`, thumbprint dari `SigningThumbprint`), simpan, lalu **hapus ketujuh value lama** dari Registry dan tulis satu baris log. Jika sudah ada profile, value lama **dibiarkan** (tidak dihapus, tidak dibaca). |
| K7 | Belum ada profile | **Keadaan kosong** (bukan auto-create): layar menampilkan card kosong "No release profile yet" dengan tombol **New profile** dan **Import profile**. Settings, Prepare, Compare, Sync, Verify nonaktif. Menu profile tetap bisa New, Import, Profiles folder, Open folder. |
| K8 | Letak UI | **Toolbar**: combobox profile di paling kiri, lalu tombol menu kecil (ikon `Solid_EllipsisVertical`), lalu tombol Settings yang sudah ada. Menu berisi: New profile, Duplicate, Rename, Delete, (separator), Import..., Export..., (separator), Open folder, Profiles folder.... |
| K9 | Sumber signing key | **Per profile**, dua pilihan: `Store` = certificate store `CurrentUser\My` lewat thumbprint (bawaan, perilaku sekarang); `ProfileFile` = `signing.pfx` di folder profile, dibuka dengan password saat dibutuhkan. `signing.cer` (sertifikat publik DER, tanpa private key) selalu ditulis bersama `signing.pfx` agar keyId/kedaluwarsa/Verify bisa dibaca tanpa password. |
| K10 | Mode password `.pfx` | Mengikuti pola `SensitiveDataStorage` Publisher. `Separate` (bawaan): password diminta sekali per sesi aplikasi dan disimpan di memori; dengan centang **Remember on this PC**, disimpan terenkripsi DPAPI `CurrentUser` di `LocalApplicationData\Em\ReleaseManager\Secrets\<id>.bin`. `Plaintext`: password disimpan sebagai teks biasa di `profile.json` (`signing.password`). Gagal dekripsi / password salah → dianggap tidak tersedia dan ditanyakan lagi (record DPAPI yang salah dihapus). |
| K11 | Create/Import signing key | User **memilih tujuan saat itu** lewat dialog kecil: **This profile** (berkas di folder profile) atau **Windows certificate store**. Pilihan awal = sumber key profile saat ini. Setelah berhasil, sumber key profile pindah ke tujuan itu dan key langsung terpilih. Import ke profile menyalin `.pfx` **hanya setelah** password terverifikasi dan key lolos pemeriksaan (ber-private key, ECDSA P-256). |
| K12 | Tabrakan tujuan | **Peringatan saja**, tidak diblokir. Tabrakan = profile lain dengan tujuan sama: keduanya `Cdn` dan `ReleaseTarget.NormalizeReleaseFolder(ReleaseFolder)` sama (case-insensitive); atau keduanya `Folder` dan `Path.GetFullPath(Path.Combine(TargetFolder, normalized ReleaseFolder))` sama (case-insensitive). |
| K13 | Publish folder sama | **Peringatan saja** bila `Path.GetFullPath(PublishFolder)` (trailing separator dibuang, case-insensitive) sama dengan profile lain. Tidak ada penanda kepemilikan di folder. |
| K14 | Kapan peringatan muncul | **Keduanya**: saat **Save** di dialog Settings (`ShowMboxDecideWarning`, "Save anyway?") dan **sebelum Prepare dan sebelum Sync** (`ShowMboxDecideWarning`, "Continue?"). Prepare memeriksa K13; Sync memeriksa K12 dan K13. No = batal. |
| K15 | Hapus profile | Konfirmasi, lalu hapus **subfolder profile beserta isinya** saja. Local publish folder, rilis di tujuan, dan key di certificate store **tidak disentuh**. Record DPAPI `Secrets\<id>.bin` milik profile itu ikut dihapus (data milik profile yang tidak berguna lagi; catat di laporan). |
| K16 | Export/Import | **Satu berkas `.zip`**. Export menanyakan "Include sensitive data?" (default **No**). Isi zip: `profile.json` selalu; `signing.cer` selalu bila ada; `signing.pfx` dan `signing.password` plaintext **hanya bila Yes**. Password `Separate`/Remember **tidak pernah** ikut. Bila No: `signing.password` di JSON zip = `null`. Import menerima `.zip` **dan** `profile.json` lepas. |
| K17 | Commit | **Commit di akhir** (satu commit, judul + deskripsi Bahasa Indonesia), hanya berkas plan ini. Berkas yang juga berisi perubahan pengguna yang belum di-commit **tidak di-commit** (lihat Tahap 7.6). |
| K18 | Verifikasi | Build WPF + harness smoke `scripts/release-profile-smoke` + harness render `scripts/release-profile-render` (PNG tema terang/gelap). Interaksi mouse nyata, Sync ke server, dan Sync ke folder dengan key berkas sungguhan lewat UI dicatat sebagai **verifikasi tertunda** bila tidak dikerjakan. |
| K19 | Dokumen | Panduan baru `doc/engine-release-manager.md`, laporan `doc/report/release-manager-profile-eksekusi.md`, entri "Pembaruan" di `claude.md`, status ide diperbarui. |

---

## Peta kode yang ada (baca dulu sebelum mulai)

Baca seluruh berkas berikut sebelum menulis kode:

- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseSettings.cs` — setting sekarang (Registry, value datar). **Akan
  dihapus** dan diganti (Tahap 1).
- `src/shared/Em.Ui.Wpf.Core/Release/SigningCertificates.cs` — Create/Import/Export/List/Find/Remove di store,
  `Load` privat memakai `X509CertificateLoader.LoadPkcs12FromFile`, `ExportPfx` memakai
  `Pkcs12ExportPbeParameters.Pbes2Aes256Sha256`.
- `src/shared/Em.Ui.Wpf.Core/Navigations/ReleaseManager.xaml(.cs)` — layar + `ReleaseManagerVm`
  (`Initialize`, `ReloadAsync`, `ApplySettings`, `LoadSigningKey`, `SettingsCommand`, `PrepareCommand`,
  `SyncCommand` yang mengambil private key dari store, `VerifyCommand` yang mengambil public key dari store,
  `ResetComparison`, `ResetVerify`, `RefreshPublishContent`, `CreateTarget`, `TargetBlockedReason`).
- `src/shared/Em.Ui.Wpf.Core/Dialogs/ReleaseSettingsDialog.xaml(.cs)` — dialog setting + class
  `ReleaseSigningKey` (representasi key di store untuk UI) + `ReleaseSettingsDialogVm`.
- `src/shared/Em.Ui.Wpf.Core/Dialogs/PasswordInputDialog.xaml(.cs)` dan `TextInputDialog.xaml(.cs)`.
- `src/shared/Em.Ui.Wpf.Core/Extensions.cs` — helper `ShowMboxDecideWarning`, `ShowMboxInfo`, `ShowMboxWarning`,
  `ShowMboxError`, `ShowMboxDecideCancel`.
- `src/shared/Em.Ui.Wpf.Core/Publish/PublishStorage.cs` — pola acuan: `PublisherSettings` (Registry untuk folder dan
  `LastProfile`), `PublishPaths.Atomic/Inside/RejectLinks`, `PublishSecretStore` (sesi + DPAPI). Boleh **memanggil**
  `PublishPaths` (public static) agar tidak menduplikasi; jangan mengubah file Publisher.
- `src/shared/Em.Ui.Wpf.Core/Navigations/Publish/PublishView.xaml.cs` dan `PublisherSettingsDialog.cs` — pola UI
  profile (Duplicate, Delete, Export dengan pertanyaan sensitive data, Open folder, dialog folder).
- `src/shared/Em.Ui.Wpf.Core/Core/EmApp.cs` — `BaseRegKey`, `ActiveConnection`, `ActiveConnectionChanged`.
- `src/shared/Em.Ui.Core/Ui.Core/shared/ApiConnection.cs` — `Host`, `ProfileName`.
- `src/shared/Em.Ui.Wpf.Core/Styles/*.xaml` (merge lewat `MaterialDesign.xaml`) — style yang dipakai layar:
  `cardStyle`, `outlinedButtonStyle`, `filledButtonStyle`, `textDangerButtonStyle`, `buttonIconStyle`,
  `chipStyle`, `metaLabelStyle`, `settingCardStyle`, dsb. Cari style ComboBox dan ContextMenu/MenuItem bertema yang
  sudah ada (`grep -rn "TargetType=\"ComboBox\"\|TargetType=\"MenuItem\"\|TargetType=\"ContextMenu\"" Styles`) dan
  pakai ulang.
- `scripts/publish-render/` dan `scripts/storage-settings-render/` — pola harness render PNG.
- `doc/release-format.md` (hanya dibaca; tidak diubah).

---

## Tahap 1 — Model, store, preferensi, secret (`Em.Ui.Wpf.Core/Release/`)

Namespace semua class di tahap ini: `Em.Ui.Wpf.Core.Release` (sama dengan `ReleaseSettings` sekarang).

### 1.1 `Release/ReleaseProfile.cs` (baru)

```csharp
public enum ReleaseSigningSource { Store = 0, ProfileFile = 1 }
public enum ReleasePasswordStorage { Separate = 0, Plaintext = 1 }

public sealed class ReleaseProfileSigning {
   public ReleaseSigningSource Source { get; set; }          // bawaan Store
   public string Thumbprint { get; set; } = "";              // Store: sertifikat di store; ProfileFile: thumbprint signing.cer
   public ReleasePasswordStorage PasswordStorage { get; set; } // bawaan Separate
   public string? Password { get; set; }                     // hanya diisi bila Plaintext
}

public sealed class ReleaseProfile {
   public int FormatVersion { get; set; } = 1;
   public string Id { get; set; } = Guid.NewGuid().ToString("N");
   public string Name { get; set; } = "";
   public string SolutionPath { get; set; } = "";
   public string HostProject { get; set; } = "";
   public string PublishFolder { get; set; } = "";
   public ReleaseTargetKind TargetKind { get; set; }         // enum yang ada, bawaan Cdn
   public string TargetFolder { get; set; } = "";
   public string ReleaseFolder { get; set; } = ReleaseLayout.DefaultReleaseFolder;
   public ReleaseProfileSigning Signing { get; set; } = new();
   public ReleaseProfile Clone();   // salinan dalam (lewat serialize/deserialize)
   public void Validate();          // lihat aturan di bawah
}
```

- `ReleaseTargetKind` dipindah dari `ReleaseSettings.cs` ke file ini (isi dan XML-doc dipertahankan).
- Serialisasi `System.Text.Json`: `JsonSerializerOptions` statis dengan `PropertyNamingPolicy = CamelCase`,
  `WriteIndented = true`, `JsonStringEnumConverter` (enum sebagai string), `DefaultIgnoreCondition = Never`.
  Sediakan `static string ToJson(ReleaseProfile)` dan `static ReleaseProfile FromJson(string)` (memanggil
  `Validate`, membungkus `JsonException` menjadi `InvalidDataException` dengan pesan Inggris yang jelas).
- Contoh `profile.json`:

```json
{
  "formatVersion": 1,
  "id": "3f2a9c0e8b7d4a51b2c3d4e5f6a7b8c9",
  "name": "App X (prod)",
  "solutionPath": "D:\\src\\appx\\AppX.slnx",
  "hostProject": "src\\AppX.Wpf\\AppX.Wpf.csproj",
  "publishFolder": "D:\\publish\\appx",
  "targetKind": "Cdn",
  "targetFolder": "",
  "releaseFolder": "wpf-release",
  "signing": {
    "source": "ProfileFile",
    "thumbprint": "AB12CD34...",
    "passwordStorage": "Separate",
    "password": null
  }
}
```

- `Validate()`: `FormatVersion == 1` (selain itu `InvalidDataException("Unsupported profile format version N.")`);
  `Id` GUID valid (`Guid.TryParseExact(Id, "N", ...)`); `Name` tidak kosong setelah `Trim()` dan ≤ 100 karakter;
  enum terdefinisi (`Enum.IsDefined`); `ReleaseFolder` kosong dinormalkan ke bawaan; bila
  `PasswordStorage == Separate` maka `Password` dipaksa `null`.

### 1.2 `Release/ReleaseProfileStore.cs` (baru)

Konstruktor `ReleaseProfileStore(string root)`; `Root` = `Path.GetFullPath(root)`.

- Konstanta nama berkas: `ProfileFileName = "profile.json"`, `KeyFileName = "signing.pfx"`,
  `CertificateFileName = "signing.cer"`.
- `IReadOnlyList<ReleaseProfileEntry> List()`: buat `Root` bila belum ada; enumerasi subfolder langsung yang
  namanya GUID `N`; untuk tiap folder baca `profile.json`. Hasil `ReleaseProfileEntry { string Directory;
  ReleaseProfile? Profile; string? Error; DateTime LastWriteUtc }`. Berkas rusak/tidak ada → entry dengan `Error`
  (tidak menggagalkan daftar). Folder yang `id` di JSON-nya tidak sama dengan nama folder → entry error
  "Profile id does not match its folder.". Urutkan valid dulu berdasarkan `Name` (OrdinalIgnoreCase), error di
  akhir. Lewati folder reparse point (`PublishPaths.RejectLinks` melempar → entry error).
- `string DirectoryOf(string id)`: validasi GUID `N`, lalu `PublishPaths.Inside(Root, id)`.
- `ReleaseProfile Load(string id)`.
- `ReleaseProfileEntry Save(ReleaseProfile profile, DateTime? expectedLastWriteUtc = null)`: `Validate()`, cek nama
  unik (case-insensitive, `Trim`) terhadap profile valid lain → `InvalidOperationException("A profile named 'X'
  already exists.")`; bila `expectedLastWriteUtc` diisi dan berkas di disk lebih baru → lempar
  `ReleaseProfileChangedException` (class baru di file yang sama). Tulis atomik lewat
  `PublishPaths.Atomic(file, json)`.
- `ReleaseProfile Create(string name)`: profile baru, nilai bawaan, simpan, kembalikan.
- `ReleaseProfile Duplicate(string id)`: salin seluruh isi folder (termasuk `signing.pfx`/`.cer`) ke folder id
  baru, ganti `Id`, nama `"<nama> (copy)"`, bila bentrok `"<nama> (copy 2)"`, dst. Record DPAPI tidak disalin.
- `void Rename(string id, string newName)`.
- `void Delete(string id)`: path = `DirectoryOf(id)`; validasi absolut di bawah `Root`, bukan `Root` itu sendiri,
  nama folder GUID, tidak ada reparse point di jalurnya (`PublishPaths.RejectLinks` + periksa entri di dalamnya
  seperti `PublishPaths.ValidateTree`), lalu `Directory.Delete(path, recursive: true)`.
- `void Export(string id, string zipPath, bool includeSensitive)`: tulis zip baru (timpa bila ada, lewat file
  sementara di folder tujuan lalu `File.Move(..., overwrite: true)`). Entri: `profile.json` (salinan profile;
  `signing.password = null` bila `!includeSensitive`), `signing.cer` bila ada, `signing.pfx` hanya bila
  `includeSensitive` dan ada.
- `ReleaseProfile Import(string path, bool newId)`: `.zip` atau `.json`.
  - Zip: hanya entri tepat bernama `profile.json`, `signing.pfx`, `signing.cer` (tanpa folder, tanpa `..`, tanpa
    path absolut); entri lain → `InvalidDataException("Unexpected entry 'X' in the profile archive.")`. Batas ukuran
    per entri 10 MB.
  - Bila `Id` sudah ada dan `newId == false` → `ReleaseProfileConflictException` (class baru); pemanggil UI
    menawarkan "Import as a new profile?" lalu memanggil lagi dengan `newId: true` (pola `PublishView`).
  - Nama bentrok → tambahkan `" (2)"`, `" (3)"`, dst.
  - Tulis ke folder sementara di bawah `Root` (`.import-<guid>`), lalu `Directory.Move` ke `<Root>\<id>`.
  - Bila `Signing.Source == ProfileFile` tetapi `signing.pfx` tidak ikut: profile tetap diimpor; UI menampilkan
    "Key file missing" (lihat 3.6).
- `bool HasKeyFile(string id)`, `string KeyFilePath(string id)`, `string CertificateFilePath(string id)`.

### 1.3 `Release/ReleaseManagerPreferences.cs` (baru)

Membungkus Registry. Dua konstruktor (overload, bukan rename):
`ReleaseManagerPreferences(EmApp app)` → memakai `app.BaseRegKey`, dan
`ReleaseManagerPreferences(Func<RegistryKey> baseKey)` → untuk harness smoke (key uji di HKCU).

- `const string SubKey = "ReleaseManager"`, `LastProfilesSubKey = "LastProfiles"`.
- `static string DefaultProfilesFolder` (K2).
- `string ProfilesFolder { get; set; }`: kosong/tidak ada → bawaan; set menyimpan `Path.GetFullPath(value)`; set ke
  nilai bawaan atau kosong → hapus value.
- `static string ServerKey(ApiConnection? connection)` (K5).
- `string? GetLastProfile(string serverKey)` / `void SetLastProfile(string serverKey, string id)`.
- `bool HasLegacySettings()` dan `ReleaseProfile? ReadLegacyProfile()` (nama `Default`, K6) dan
  `void DeleteLegacySettings()` (hapus ketujuh value lama dengan `throwOnMissingValue: false`).
- Setiap akses Registry membuka dan menutup key (`using`), seperti `ReleaseSettings` sekarang.

### 1.4 `Release/ReleaseSigningSecrets.cs` (baru)

Penyimpan password `.pfx` mode `Separate` (K10). Konstruktor `ReleaseSigningSecrets()` (direktori bawaan
`LocalApplicationData\Em\ReleaseManager\Secrets`) dan overload `ReleaseSigningSecrets(string directory)` untuk
harness. Cache sesi **statis** (`static readonly Dictionary<string, (string Thumbprint, string Password)>`, dikunci
`lock`) agar bertahan selama aplikasi hidup walau layar dibuka ulang.

- `string? Get(string profileId, string thumbprint)`: cek sesi (thumbprint harus cocok), lalu berkas DPAPI
  (`ProtectedData.Unprotect`, `DataProtectionScope.CurrentUser`), isi JSON `{ "thumbprint": "...",
  "password": "..." }`; thumbprint tidak cocok atau gagal dekripsi → `null`.
- `void Put(string profileId, string thumbprint, string password, bool remember)`: simpan ke sesi; `remember` →
  tulis berkas DPAPI (atomik), selain itu hapus berkas bila ada.
- `bool IsRemembered(string profileId)`.
- `void Forget(string profileId)`: hapus dari sesi dan hapus berkas.
- Nama berkas `<profileId>.bin`, profileId divalidasi GUID `N` dan path lewat `PublishPaths.Inside`.

### 1.5 `Release/ReleaseProfileConflicts.cs` (baru)

`static IReadOnlyList<string> Find(ReleaseProfile profile, IEnumerable<ReleaseProfile> others, bool checkTarget,
bool checkPublishFolder)` mengembalikan kalimat Inggris siap tampil, mis.
`"Profile 'App Y' publishes to the same target (server CDN, folder 'wpf-release')."` dan
`"Profile 'App Y' uses the same local publish folder 'D:\\publish\\appx'."`. Aturan K12/K13. Profile dengan `Id`
sama dilewati. Nilai kosong (folder belum diisi) tidak dianggap tabrakan. `Path.GetFullPath` yang melempar
(path tidak valid) → lewati pasangan itu.

### 1.6 `Release/SigningCertificates.cs` (tambahan saja)

Tambahkan method baru; **jangan** mengubah perilaku method yang ada:

- `static X509Certificate2 CreatePfx(string pfxPath, string certificatePath, string password)`: buat key seperti
  `Create` (pakai kode pembuatan yang sama; boleh diekstrak ke helper privat `CreateSelfSigned()` yang juga dipakai
  `Create`), tulis `.pfx` dan `.cer` (`certificate.Export(X509ContentType.Cert)`), **tidak** memasang ke store.
  Kembalikan sertifikat publik (`X509CertificateLoader.LoadCertificate(cerBytes)`).
- `static X509Certificate2 ValidatePfx(string pfxPath, string password)`: pemeriksaan yang sama dengan awal
  `Import` (ephemeral, ber-private key, P-256); kembalikan sertifikat publik (tanpa private key).
- `static X509Certificate2 LoadPfxForSigning(string pfxPath, string password)`: `Load(..., EphemeralKeySet)` +
  pemeriksaan; pemanggil men-`Dispose`.
- `static X509Certificate2? LoadCertificateFile(string certificatePath)`: `null` bila tidak ada/rusak.
- `static void WriteCertificateFile(X509Certificate2 certificate, string certificatePath)`.
- `static void ExportPfxFile(string sourcePfx, string sourcePassword, string targetPfx, string newPassword)`: buka
  dengan `Exportable | EphemeralKeySet`, ekspor ulang dengan password baru.
- Perbarui XML-doc ringkasan class: password **tidak** disimpan oleh class ini; penyimpanan password (opsional)
  diatur `ReleaseSigningSecrets`/profile.

### 1.7 Hapus `Release/ReleaseSettings.cs`

Setelah `ReleaseTargetKind` dipindah, hapus berkas. Semua pemakai (`ReleaseManagerVm`, `ReleaseSettingsDialogVm`)
diganti di Tahap 2–3. Pastikan `grep -rn "ReleaseSettings\b" src` tidak lagi menemukan pemakaian (selain nama
dialog `ReleaseSettingsDialog`, yang **tetap**).

---

## Tahap 2 — Dialog Settings mengedit profile aktif

Berkas: `Dialogs/ReleaseSettingsDialog.xaml(.cs)`.

### 2.1 Konstruktor dan alur data

- Konstruktor baru: `ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile,
  DateTime lastWriteUtc, IReadOnlyList<ReleaseProfile> others, ReleaseSigningSecrets secrets, string? sdkVersion,
  bool isSdkChecked, bool canUseCdn)`. Hapus konstruktor lama (pemakainya hanya `ReleaseManagerVm`).
- `ReleaseSettingsDialogVm.Load(...)` mengisi dari **salinan** profile (`profile.Clone()`), bukan dari Registry.
- Judul banner/dialog: `Release Settings · <nama profile>` (title bar tetap `Release Settings`).
- `SaveCommand`:
  1. Bentuk profile hasil edit (normalisasi `ReleaseFolder` seperti sekarang).
  2. Jalankan `ReleaseProfileConflicts.Find(edited, others, true, true)`; bila ada → `ShowMboxDecideWarning`
     berisi daftar kalimat + "Save anyway?"; No → tetap di dialog.
  3. `store.Save(edited, lastWriteUtc)`; `ReleaseProfileChangedException` →
     `ShowMboxDecideWarning("profile.json was changed outside Release Manager. Overwrite it?")`; Yes →
     `store.Save(edited)` tanpa pemeriksaan; No → tetap di dialog.
  4. Error IO/validasi → `ShowMboxError`, tetap di dialog.
  5. Berhasil → `RequestClose(true)`. Expose `SavedProfile` agar layar bisa membaca hasilnya.
- Cancel membuang perubahan **isi profile**. Operasi berkas key (create/import/remove key file di profile, import ke
  store) terjadi langsung seperti sekarang dan tidak ikut dibatalkan; nyatakan ini di XML-doc class (perluas
  kalimat yang sudah ada).

### 2.2 Card SIGNING KEY

Di dalam card yang ada, tambahkan di bagian atas pilihan sumber berupa dua radio bergaya sama dengan pilihan
CDN/Folder di card TARGET: **Windows certificate store** dan **Key file in this profile**.

- Sumber **Store**: tampilan dan tombol seperti sekarang (combobox key terpasang, KEY ID, EXPIRES, chip
  exportable, Import, Create, Export, Copy public key, Export public key, Refresh).
- Sumber **ProfileFile**:
  - Info key dari `signing.cer`: KEY ID, EXPIRES, thumbprint; bila berkas tidak ada → teks peringatan
    "No key file in this profile - create or import one.".
  - Tombol: **Import**, **Create**, **Export** (menulis `.pfx` baru dengan password baru; meminta password sumber
    lewat 3.5), **Copy public key**, **Export public key**, **Remove key file** (`textDangerButtonStyle`,
    konfirmasi; menghapus `signing.pfx` dan `signing.cer` dan `secrets.Forget(profile.Id)`; key di store tidak
    disentuh).
  - Sub-bagian **Password**: dua radio **Ask once per session** (Separate) dan **Save as plain text in
    profile.json** (Plaintext), dengan keterangan kecil bergaya peringatan untuk Plaintext:
    "Anyone who can read or copy this profile folder can sign releases.". Baris status:
    Separate → "Remembered on this PC (encrypted for this Windows user)" atau "Asked once per session"; Plaintext →
    "Saved in profile.json" atau "Not set". Tombol **Set password...** (Plaintext) membuka `PasswordInputDialog`,
    memverifikasi dengan `SigningCertificates.ValidatePfx`, lalu menyimpan ke field profile yang sedang diedit
    (baru tertulis saat Save). Tombol **Forget remembered password** (Separate, aktif bila `IsRemembered`).
  - Pindah mode Plaintext → Separate: password plaintext (bila ada) dipindah ke sesi lewat `secrets.Put(...,
    remember: false)` saat Save, dan `Password = null`. Separate → Plaintext: isi `Password` dengan
    `secrets.Get(...)` bila tersedia; bila tidak, status "Not set" dan user memakai **Set password...**.
- Tombol Import/Create (kedua sumber) membuka dialog tujuan (3.4) lebih dulu. Hasil:
  - Tujuan **Store**: alur lama (`SigningCertificates.Import` / `Create` + SaveFileDialog master copy), lalu sumber
    profile = Store dan key terpilih.
  - Tujuan **This profile**:
    - Create: konfirmasi seperti sekarang (teks disesuaikan: key disimpan di folder profile dan folder itu adalah
      salinan induknya — anjurkan Export untuk backup), minta password baru (`requireConfirmation: true`), lalu
      `SigningCertificates.CreatePfx(keyPath, cerPath, password)`; bila sudah ada key file → konfirmasi timpa.
    - Import: OpenFileDialog `.pfx` → `PasswordInputDialog` → `ValidatePfx` → salin ke `signing.pfx` (timpa dengan
      konfirmasi) → tulis `signing.cer`.
    - Setelah keduanya: sumber profile = ProfileFile, `Signing.Thumbprint` = thumbprint sertifikat, password
      ditangani sesuai mode: Separate → `secrets.Put(id, thumbprint, password, remember)` dengan nilai `remember`
      dari centang di `PasswordInputDialog` (3.5); Plaintext → simpan ke field `Password` profile yang sedang
      diedit.
  - Log lewat event `Logged` seperti sekarang.
- Karena berkas key ditulis langsung ke folder profile, `ReleaseSettingsDialogVm` butuh `store.KeyFilePath(id)` dan
  `store.CertificateFilePath(id)`. Folder profile pasti ada karena profile sudah tersimpan sebelum dialog dibuka.

### 2.3 Aturan tampilan

- Ikuti style yang sudah dipakai dialog (`settingCardStyle`, `sectionCaptionStyle`, `metaLabelStyle`, radio/chip
  yang sama dengan card TARGET). Semua kondisi enabled/disabled/hover/fokus harus bertema terang/gelap (aturan
  anti-kilatan-putih `claude.md`). Jangan memakai warna statis baru; pakai brush tema yang ada (`warningBrush`,
  `outlineBrush`, `themeAccentBrush`, dst.).
- Lebar dialog boleh dinaikkan bila perlu; tinggi mengikuti konten dengan `ScrollViewer` bila melebihi layar
  (periksa apakah dialog sudah punya; bila belum, bungkus area card dengan `ScrollViewer` ber-style bertema).

---

## Tahap 3 — Layar Release Manager

Berkas: `Navigations/ReleaseManager.xaml(.cs)`.

### 3.1 State ViewModel baru di `ReleaseManagerVm`

- Field: `ReleaseManagerPreferences? _preferences`, `ReleaseProfileStore? _store`,
  `readonly ReleaseSigningSecrets _secrets`, `DateTime _profileLastWriteUtc`, `string? _loadedServerKey`.
- Property (public getter, `Get`/`Set`):
  - `ObservableCollection<ReleaseProfileItem> Profiles` — item untuk combobox (`Id`, `Name`, `Error`,
    `Caption` = nama atau `"<folder> (invalid: <error>)"`, `IsValid`). Item invalid tampil tetapi **tidak bisa
    dipilih** (ItemContainerStyle `IsEnabled=False`); tooltip berisi error.
  - `ReleaseProfileItem? SelectedProfile` — setter memanggil `SelectProfileAsync` (3.3) kecuali sedang reload
    (flag `_reloadingProfiles`, pola `PublishView`).
  - `ReleaseProfile? Profile` (profile aktif yang dimuat), `bool HasProfile`, `bool IsEmptyState`
    (`!HasProfile && daftar valid kosong`).
  - `string ProfilesFolder` (untuk tooltip/menu).
- Semua property setting yang ada (`SolutionPath`, `HostProject`, `PublishFolder`, `TargetKind`, `TargetFolder`,
  `ReleaseFolder`, `SigningKey`) tetap ada dan diisi `ApplySettings()` dari `Profile`.
- Signing di layar: ganti `ReleaseSigningKey? SigningKey` menjadi model tampilan yang bisa mewakili kedua sumber.
  Rekomendasi: perluas `ReleaseSigningKey` (di `ReleaseSettingsDialog.xaml.cs`) dengan property
  `ReleaseSigningSource Source` dan factory `FromCertificateFile(X509Certificate2 publicCertificate)`
  (`IsExportable` = true untuk berkas, karena berkasnya sendiri bisa diekspor ulang dengan password). Bila
  sumber berkas tetapi `signing.cer` tidak ada → `SigningKey = null` dan caption
  "Key file missing - create or import one in Settings.".
- `SigningCaption` dan baris catatan ringkasan SIGNING KEY: Store → seperti sekarang; ProfileFile →
  `"<keyId> · expires <yyyy-MM-dd>"` dan catatan `"Key file in profile · <mode>"` dengan `<mode>` = `asked per
  session` / `remembered on this PC` / `password in profile.json`.
- `LoadSigningKey()`: Store → `ReleaseSigningKey.Find(thumbprint)`; bila tidak terpasang dan thumbprint tidak
  kosong → kosongkan `Signing.Thumbprint` **dan simpan profile** (perilaku sekarang, kini ke berkas). ProfileFile →
  baca `signing.cer`; bila thumbprint berkas ≠ `Signing.Thumbprint` (berkas diganti dari luar) → perbarui
  `Signing.Thumbprint`, simpan profile, dan log satu baris.

### 3.2 Inisialisasi dan reload

- `Initialize()` (dipanggil sekali): `_preferences = new(EmApp)`, `_store = new(_preferences.ProfilesFolder)`,
  `HideUnchanged = true`, `LoadProfiles()`, `_ = CheckSdkAsync()`. Sediakan overload
  `Initialize(ReleaseManagerPreferences preferences, ReleaseProfileStore store, ReleaseSigningSecrets secrets)`
  untuk harness render/smoke (overload, bukan rename); overload tanpa argumen memanggilnya.
- `LoadProfiles(string? selectId = null)`:
  1. `entries = _store.List()`.
  2. Bila tidak ada entry valid dan `_preferences.HasLegacySettings()` → migrasi K6 (simpan profile `Default`,
     `DeleteLegacySettings()`, log `"Settings from the previous version were moved to profile 'Default'."`),
     lalu `List()` lagi.
  3. Isi `Profiles` (flag `_reloadingProfiles`).
  4. Pilih: `selectId` → `_preferences.GetLastProfile(serverKey)` → profile valid pertama → tidak ada (keadaan
     kosong).
  5. Muat profile terpilih (3.3 tanpa compare otomatis bila dipanggil dari `Initialize`; `ReloadAsync` yang
     melakukan compare).
- `ReloadAsync()` (dipanggil host tiap layar dibuka dan tombol Reload): bila `IsRunning` → return. Hitung
  `CanUseCdn` seperti sekarang. Bila `ServerKey(ActiveConnection) != _loadedServerKey` → `LoadProfiles()` (pilih
  profile terakhir server baru), selain itu `LoadProfiles(SelectedProfile?.Id)` (membaca ulang daftar dan isi
  profile aktif dari disk). Lalu compare otomatis bila `CompareCommandAllowed()` (perilaku sekarang).
- Langganan `EmApp.ActiveConnectionChanged` **tidak** diperlukan; perubahan koneksi terdeteksi di `ReloadAsync`.
  Catat di laporan bila ternyata host tidak memanggil `ReloadAsync` saat koneksi berganti.

### 3.3 Ganti profile

`SelectProfileAsync(ReleaseProfileItem item)`:

1. Bila `IsRunning` → abaikan (combobox seharusnya sudah nonaktif).
2. `Profile = _store.Load(id)`, `_profileLastWriteUtc` dari entry, `_preferences.SetLastProfile(serverKey, id)`,
   `_loadedServerKey = serverKey`.
3. `ResetComparison()`, `ResetVerify()`, `ApplySettings()`.
4. `if (CompareCommandAllowed()) await CompareCommand();`
5. Gagal baca → `ShowMboxError` dan muat ulang daftar.

### 3.4 Toolbar dan menu (XAML)

- Di `StackPanel` kolom 0 toolbar, sebelum tombol Settings:
  - `ComboBox` profile: lebar ±240 (MinWidth 200, MaxWidth 320), `ItemsSource="{Binding Profiles}"`,
    `SelectedItem="{Binding SelectedProfile}"`, menampilkan `Caption` lewat `ItemTemplate` (ingat: kotak pilihan
    ComboBox menampilkan item lewat `ToString()`; override `ToString()` di `ReleaseProfileItem` seperti
    `ReleaseSigningKey`), `IsEnabled` terikat ke `!IsRunning` (pakai converter yang ada atau property
    `CanChangeProfile`). Tooltip `"Release profile"`. Pakai style ComboBox bertema yang sudah ada; pastikan kondisi
    disabled tetap bertema (aturan anti-kilatan-putih).
  - Tombol menu: `outlinedButtonStyle` versi ikon (cari style tombol ikon kecil yang ada, mis. yang dipakai tombol
    refresh card), ikon `Solid_EllipsisVertical`, tooltip `"Profile actions"`, membuka `ContextMenu` (set
    `PlacementTarget` dan `IsOpen` di handler klik code-behind; itu urusan view). `MenuItem` terikat ke command VM:
    `NewProfileCommand`, `DuplicateProfileCommand`, `RenameProfileCommand`, `DeleteProfileCommand`,
    `ImportProfileCommand`, `ExportProfileCommand`, `OpenProfilesFolderCommand`, `ProfilesFolderCommand`. Beri ikon
    FontAwesome per item bila style MenuItem yang ada mendukung `Icon`. Pastikan ContextMenu/MenuItem bertema
    terang/gelap; bila belum ada style bertema, tambahkan style bersama di `Styles/` (gabung lewat
    `MaterialDesign.xaml`) dan catat di laporan.
- Command `*Allowed`: semua `!IsRunning`; Duplicate/Rename/Delete/Export butuh `HasProfile`; New/Import/Open
  folder/Profiles folder selalu boleh saat tidak berjalan. `SettingsCommandAllowed` = `!IsRunning && HasProfile`.
  `PrepareCommandAllowed`/`CompareCommandAllowed`/`VerifyCommandAllowed`/`SyncCommandAllowed` juga mensyaratkan
  `HasProfile` (secara alami sudah, karena setting kosong; tetap tambahkan secara eksplisit).
- `RaiseCommandsChanged()` dipanggil saat `Profile`/`HasProfile` berubah.

### 3.5 Command profile

- **New profile**: `TextInputDialog("New Release Profile", "Name of the new profile.", okCaption: "Create",
  icon: Solid_Plus)` → `_store.Create(name)` (nama bentrok → `ShowMboxWarning`, ulangi dialog) →
  `LoadProfiles(newId)` → langsung buka Settings untuk profile baru.
- **Duplicate**: `_store.Duplicate(id)` → `LoadProfiles(copyId)` → log.
- **Rename**: `TextInputDialog` berisi nama sekarang (cek apakah `TextInputDialogVm` punya property teks awal; bila
  tidak, tambahkan parameter opsional `initialText` dengan overload/parameter default yang tidak mengubah pemakai
  lama) → `_store.Rename` → `LoadProfiles(id)`.
- **Delete**: `ShowMboxDecideWarning($"Delete profile '{name}'?\n\nOnly the profile folder is deleted (settings,
  and its key file if any). The local publish folder, the release at the target and keys in the Windows
  certificate store are not touched.")` → `_store.Delete(id)` → `_secrets.Forget(id)` → `LoadProfiles()` → log.
- **Import...**: `OpenFileDialog` filter `Release profile (*.zip;*.json)|*.zip;*.json` → `_store.Import(path,
  false)`; `ReleaseProfileConflictException` → `ShowMboxDecideWarning("A profile with the same id already exists.
  Import it as a new profile?")` → `Import(path, true)` → `LoadProfiles(importedId)` → log. Bila profile hasil
  import bersumber berkas tanpa `signing.pfx` → `ShowMboxInfo` bahwa key file perlu di-import di Settings.
- **Export...**: `ShowMboxDecideCancel` atau `ShowMboxDecideWarning` dengan teks "Include sensitive data?\n\nNo
  (default): only settings and the public certificate. Yes: also the key file and a plain-text password, if this
  profile has them." — **default tombol No** (periksa helper; bila helper tidak bisa mengatur default, buat
  pemanggilan `EmMessageBox` yang bisa, atau catat keterbatasannya) → `SaveFileDialog` `"<nama>.zip"` →
  `_store.Export(id, path, includeSensitive)` → log.
- **Open folder**: buka `_store.Root` dengan `Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
  { UseShellExecute = true })` (buat folder dulu bila belum ada).
- **Profiles folder...**: dialog 3.6; bila disimpan dan nilainya berubah → `_preferences.ProfilesFolder = value`,
  `_store = new(value)`, `LoadProfiles()`, compare otomatis bila boleh.

### 3.6 Dialog baru

Semua dialog baru di `Dialogs/`, turunan `EmWindow`, ber-ViewModel `MvvmModelBase`, gaya mengikuti
`PasswordInputDialog`/`TextInputDialog` (banner ikon + judul + caption, tombol Cancel/OK di kanan bawah).

1. **`ReleaseProfilesFolderDialog`**: TextBox path (bisa diketik), tombol Browse (`OpenFolderDialog`), **tombol
   copy kecil langsung di kanan TextBox** (ikon copy, tooltip "Copy path", `AutomationProperties.Name`, menyalin
   path lengkap; standar kontrol alamat `claude.md`), tombol **Reset to default**, teks kecil
   "Profiles are not moved when the folder changes.". OK = **Save** (nonaktif bila path kosong atau
   `Path.GetFullPath` melempar). Hasil: `SelectedFolder`.
2. **`SigningKeyDestinationDialog`**: judul `"Create Signing Key"`/`"Import Signing Key"` (parameter), dua radio
   besar bergaya kartu: **This profile** ("Saved as signing.pfx in the profile folder. Moves with the profile;
   protected only by its password.") dan **Windows certificate store** ("Installed for this Windows user. Can be
   non-exportable."). Pilihan awal dari parameter. Hasil: `ReleaseSigningSource Destination`.
3. **`PasswordInputDialog` (perluasan)**: tambahkan parameter opsional `showRemember = false` (taruh di **akhir**
   daftar parameter agar pemanggil lama tetap valid) dan property VM `ShowRemember`/`Remember` (default tidak
   dicentang), checkbox "Remember on this PC (encrypted for this Windows user)", tampil hanya bila `ShowRemember`.
   Ikuti pola `ShowExportable`/`Exportable` yang sudah ada.

### 3.7 Mengambil key untuk Sync dan Verify

- Method privat `ECDsa? AcquireSigningKey(out X509Certificate2? holder)` atau setara, dipanggil **di thread UI
  sebelum** `RunAsync("Sync", ...)` (karena bisa menampilkan dialog):
  - Store → perilaku sekarang (`SigningCertificates.Find` + `GetECDsaPrivateKey`), hanya dipindah posisinya bila
    perlu.
  - ProfileFile → password dari: `Plaintext` → `Profile.Signing.Password`; `Separate` → `_secrets.Get(id,
    thumbprint)`; bila tidak ada → `PasswordInputDialog("Signing Key Password", $"Password of the key file in
    profile '{name}'.", okCaption: "Sign", showRemember: true)`. Lalu
    `SigningCertificates.LoadPfxForSigning(KeyFilePath, password)`. Password salah: Separate → `_secrets.Forget(id)`
    dan tanya lagi (maksimal 3 kali, lalu batal dengan pesan); Plaintext → `ShowMboxError("The password saved in
    profile.json is wrong. Fix it in Settings.")` dan batal. Berhasil dari dialog → `_secrets.Put(id, thumbprint,
    password, remember)`.
  - Sertifikat dan `ECDsa` di-`Dispose` setelah Sync selesai (dalam `finally`).
- `SyncCommand`: urutan baru: (a) peringatan tabrakan K14 (target + publish folder; No → batal), (b) konfirmasi
  Sync yang sudah ada (tambahkan baris `Profile: <nama>`), (c) ambil key (di atas), (d) `RunAsync` memanggil
  `ReleaseSync.SyncAsync` **persis seperti sekarang**.
- `PrepareCommand`: peringatan publish folder K13/K14 sebelum konfirmasi kosongkan folder yang sudah ada.
- `VerifyCommand`: public key dari Store (sekarang) atau dari `signing.cer` (ProfileFile). Tidak butuh password.

### 3.8 Keadaan kosong (K7)

- Saat `IsEmptyState`, area daftar perbandingan (row 2) menampilkan card kosong di tengah: ikon
  `Solid_LayerGroup` atau sejenis, judul "No release profile yet", kalimat "A profile keeps the source, target
  and signing key of one application.", tombol **New profile** (`filledButtonStyle`) dan **Import profile**
  (`outlinedButtonStyle`). Card ringkasan atas menampilkan "No profile" pada SOURCE/TARGET/SIGNING KEY tanpa
  peringatan merah (pakai catatan biasa). Daftar invalid (bila ada) tetap tampil di combobox agar user tahu ada
  berkas rusak.

### 3.9 Lain-lain

- `OnNavigatingAway`, `OnRelease`, log, progres: tidak berubah.
- Tooltip tombol Settings berubah menjadi `"Source, target and signing key of this profile"`.
- `ReleaseManagerVm` XML-doc ringkasan diperbarui: setting berasal dari profile aktif.

---

## Tahap 4 — Harness smoke dan render (kode saja; dijalankan di Tahap 7)

Harness **ikut di-commit** di `scripts/` seperti `scripts/publish-smoke` dan `scripts/publish-render`. Proyek konsol
`net10.0-windows`, `UseWPF` (render) / tanpa WPF bila tidak perlu (smoke boleh `UseWPF` juga karena
`Em.Ui.Wpf.Core`), `ImplicitUsings`, `Nullable`, `ProjectReference` ke `../../src/shared/Em.Ui.Wpf.Core/Em.Ui.Wpf.Core.csproj`.
Jangan menambah project ke solution mana pun.

### 4.1 `scripts/release-profile-smoke/`

Semua jalur memakai folder sementara di `Path.GetTempPath()` (`em-release-profile-smoke-<guid>`) dan Registry uji
`HKCU\EmReleaseProfileSmoke-<guid>` lewat `ReleaseManagerPreferences(Func<RegistryKey>)`; keduanya dihapus di
`finally` (validasi path absolut di bawah temp sebelum hapus rekursif; Registry hanya subkey yang dibuat sendiri).
Setiap pemeriksaan mencetak `PASS <nama>` / `FAIL <nama>: <alasan>`; exit code ≠ 0 bila ada FAIL. Minimal:

1. Create, List (urut nama), Load, Save atomik, Rename, nama bentrok ditolak (case-insensitive).
2. `profile.json` rusak → entry error, daftar tetap jalan; id tidak cocok folder → entry error.
3. `ReleaseProfileChangedException` saat berkas diubah dari luar.
4. Duplicate menyalin `signing.pfx`/`.cer`, id dan nama baru.
5. Delete hanya menghapus folder profile; folder lain di `Root`, publish folder palsu di luar `Root`, tidak
   tersentuh; id bukan GUID ditolak.
6. Migrasi K6: tulis value lama ke Registry uji → `ReadLegacyProfile` benar → setelah migrasi value lama hilang;
   bila sudah ada profile, value lama tidak dihapus.
7. `ServerKey`: `https://Server.Example:443/` → `server.example`, `http://host:5132` → `host:5132`, null → `(none)`.
8. `LastProfiles` per server terpisah.
9. Conflicts K12/K13: CDN sama, folder sama (beda penulisan/case/trailing slash), publish folder sama, nilai kosong
   bukan tabrakan.
10. `SigningCertificates.CreatePfx` → `.cer` terbaca tanpa password, `ValidatePfx` password salah melempar
    `CryptographicException`, `LoadPfxForSigning` bisa menandatangani dan tanda tangannya diverifikasi public key
    dari `.cer` (pakai `ReleaseSignature` yang ada bila menyediakan API sign/verify; bila tidak, `ECDsa.SignData`
    / `VerifyData` SHA-256). **Tidak** memasang apa pun ke certificate store.
11. `ReleaseSigningSecrets` (direktori temp): Put tanpa remember → Get dari sesi; Put remember → berkas ada, dan
    instance baru (cache sesi dibersihkan lewat method internal/test hook atau proses terpisah — pilih yang paling
    sederhana dan catat) tetap bisa Get; thumbprint lain → null; berkas rusak → null; Forget.
12. Export No → zip tanpa `signing.pfx`, `password` null; Export Yes (Plaintext) → ada `signing.pfx` dan password;
    Import zip → profile baru; Import id bentrok → `ReleaseProfileConflictException`, `newId: true` berhasil; zip
    berisi entri asing / `..\x` ditolak; Import `profile.json` lepas.

### 4.2 `scripts/release-profile-render/`

Ikuti pola `scripts/publish-render/Program.cs` (Application tanpa window, `ThemeResources.Apply` lewat reflection,
render `RenderTargetBitmap` ke PNG). Output bawaan `../.artefacts/em-system/release-profile-render` (argumen pertama
boleh mengganti). Untuk setiap tema terang dan gelap, render minimal:

1. Layar Release Manager keadaan kosong (K7).
2. Layar dengan 3 profile (satu invalid) dan profile aktif ber-key berkas — idle.
3. Layar yang sama saat busy (`RunningOperation` diisi lewat reflection/setter internal; combobox dan tombol
   nonaktif) — periksa tidak ada putih.
4. Menu profile terbuka (render konten `ContextMenu` atau panel penggantinya).
5. Dialog Settings, sumber Store.
6. Dialog Settings, sumber ProfileFile, mode Separate dan mode Plaintext.
7. `ReleaseProfilesFolderDialog`, `SigningKeyDestinationDialog`, `PasswordInputDialog` dengan Remember.

Layar dibuat tanpa server: gunakan `Initialize(preferences, store, secrets)` dengan folder/Registry uji seperti
smoke, dan `EmApp` tak-terinisialisasi bila diperlukan (`RuntimeHelpers.GetUninitializedObject(typeof(EmApp))`,
lihat catatan harness di repo/plan sebelumnya). Bila konstruktor `ReleaseManager(EmApp)` memaksa `EmApp` nyata,
tambahkan jalur yang aman untuk harness tanpa mengubah perilaku produksi, dan catat di laporan. Dialog:
`Left=-3000; Show(); render; Close()` atau lepas `Content` seperti `publish-render`. Cetak
`PASS rendered N screens: <folder>`.

---

## Tahap 5 — Dokumen

1. **`doc/engine-release-manager.md` (baru)**, Bahasa Indonesia, tanpa nama tabel DB, tanpa path mesin/username
   (pakai placeholder seperti `Documents\Em\...`):
   - Ringkasan Release Manager (Prepare/Compare/Sync/Verify) dengan tautan ke `doc/release-format.md`.
   - Profile: apa isinya, lokasi bawaan, mengganti folder, struktur folder (`profile.json`, `signing.pfx`,
     `signing.cer`), contoh `profile.json`.
   - Memilih profile, profile terakhir per server.
   - Signing key: sumber Store vs berkas di profile, Create/Import dengan pilihan tujuan, mode password
     (Separate/Remember/Plaintext) beserta risikonya.
   - Export/Import zip dan pertanyaan data sensitif.
   - Peringatan tabrakan tujuan dan publish folder.
   - Hapus profile: apa yang dihapus dan tidak.
   - Migrasi dari versi lama (Registry → profile `Default`).
2. Jangan mengubah `doc/release-format.md`.

---

## Tahap 6 — Rapikan sisa kode

- `grep -rn "ReleaseSettings\b\|SigningThumbprint" src` → hanya sisa yang memang benar (mis. migrasi di
  `ReleaseManagerPreferences`).
- Pastikan tidak ada `TODO` tertinggal dari pekerjaan ini.
- Pastikan semua string UI baru berbahasa Inggris dan konsisten dengan istilah yang ada ("signing key", "local
  publish folder", "target", "profile").

---

## Tahap 7 — Build, uji, review, laporan, commit (setelah Tahap 1–6 selesai semua)

### 7.1 Build

```powershell
dotnet build src/frontend/Em.Ui.Wpf.slnx
dotnet build scripts/release-profile-smoke
dotnet build scripts/release-profile-render
```

Perbaiki semua error dan warning **baru** yang berasal dari perubahan ini. `Em.Api.slnx` dan `Em.Ui.Maui.slnx`
tidak terdampak (tidak ada perubahan di `Em.Libs`/backend); jangan build kecuali ada alasan, dan catat bila
dilakukan.

### 7.2 Smoke

```powershell
dotnet run --project scripts/release-profile-smoke
```

Semua PASS. Pastikan Registry uji dan folder temp terhapus setelahnya.

### 7.3 Render

```powershell
dotnet run --project scripts/release-profile-render
```

**Buka dan periksa setiap PNG** (terang/gelap): tidak ada area putih pada tema gelap (combobox disabled, menu,
card kosong, radio, dialog), teks terbaca, tidak terpotong pada lebar layar 1280. Perbaiki lalu render ulang.
Lampirkan daftar PNG (path relatif) di laporan.

### 7.4 Review

Review seluruh diff satu kali: kebenaran (path traversal, penghapusan rekursif hanya di bawah root, zip slip,
dispose sertifikat/ECDsa, password tidak pernah masuk log), fondasi tidak berubah (`git diff --stat` tidak
menyentuh `Release/Format/`, `ReleaseBuilder.cs`, `LocalPublish.cs`, `ReleaseComparer.cs`, `ReleaseSync.cs`,
`ReleaseVerifier.cs`, `ReleaseTarget.cs`, `CdnReleaseTarget.cs`, `FolderReleaseTarget.cs`, `doc/release-format.md`),
dan konsistensi dengan tabel keputusan. Perbaiki temuan, build ulang, ulang smoke/render yang terdampak.

### 7.5 Laporan dan catatan

1. **`doc/report/release-manager-profile-eksekusi.md`**: ringkasan, daftar berkas diubah/baru/dihapus, hasil build,
   hasil smoke (salin ringkas output PASS/FAIL), daftar PNG render, **Keputusan saat eksekusi** (hal yang tidak
   tercakup plan), **Verifikasi tertunda** (minimal: interaksi mouse nyata di aplikasi, Sync ke CDN server nyata
   dengan key berkas, Sync ke folder lewat UI, migrasi Registry di mesin pengguna, perilaku saat ganti koneksi di
   aplikasi nyata), dan tindakan manual bila ada skrip `.ps1`.
2. **`doc/ideas/release-manager-profile.md`**: status sudah `jadi plan`. Perbarui tautan di bagian "Plan turunan" ke
   `plan/executed/release-manager-profile.codex.md` dan tambahkan tautan laporan. Jangan menghapus isi lama.
3. **`claude.md`**: tambahkan satu paragraf "Pembaruan 2026-10-04 (profile Release Manager)" di akhir daftar
   pembaruan: ringkas isi fitur, lokasi profile, yang tetap di Registry, mode password, tautan ke
   `doc/engine-release-manager.md`, laporan, dan plan; sebutkan verifikasi yang tertunda. **Jangan** mengubah atau
   menghapus isi lain.
4. Pindahkan berkas plan ini ke `plan/executed/release-manager-profile.codex.md`, ubah baris Status menjadi
   **sudah dieksekusi** (tanggal), dan tambahkan bagian "Catatan eksekusi" singkat yang menautkan laporan.

### 7.6 Commit (K17)

- Periksa `git status`. Working tree **sudah berisi perubahan pengguna yang belum di-commit** sebelum plan ini
  dimulai: `claude.md`, `doc/setup-container.md`, `src/backend/.env.example`, `src/backend/README.md`,
  `src/backend/compose.yml`, dan berkas untracked `doc/ideas/engine-helper-namespace.md`. **Jangan** stage berkas
  tersebut (termasuk `claude.md`, walaupun plan ini menambah paragraf di sana) — catat di laporan bahwa pengguna
  perlu meng-commit `claude.md` sendiri.
- Stage **hanya** berkas yang dibuat/diubah/dihapus plan ini, secara eksplisit per path (jangan `git add -A` /
  `git add .`): kode `src/shared/Em.Ui.Wpf.Core/...`, `scripts/release-profile-smoke/`, `scripts/release-profile-render/`
  (tanpa `bin/` dan `obj/`; periksa `.gitignore`), `doc/engine-release-manager.md`,
  `doc/report/release-manager-profile-eksekusi.md`, `doc/ideas/release-manager-profile.md`, dan plan di
  `plan/executed/` (serta penghapusan dari `plan/unexecuted/`). Bila ada folder `plan/release-manager-profile-manual/`,
  ikut di-stage.
- Satu commit, Bahasa Indonesia, contoh:

```text
Tambahkan profile di Release Manager

Setting Release Manager kini disimpan per profile sebagai berkas JSON di folder
Documents\Em\ReleaseManager\Profiles\Release, dipilih dari toolbar, dengan profile
terakhir diingat per server. Signing key bisa dari certificate store atau berkas
.pfx di folder profile dengan mode password Separate/Plaintext. Termasuk
export/import zip, peringatan tabrakan tujuan, migrasi setting Registry lama,
harness smoke/render, dan panduan doc/engine-release-manager.md.
```

- Jangan memakai `--no-verify`, jangan amend commit lama, jangan push.
- Setelah commit, jalankan `git status` dan cantumkan hasilnya di pesan penutup (berkas pengguna yang sengaja tidak
  di-commit harus masih berstatus modified/untracked).

### 7.7 Pesan penutup ke pengguna

Ringkas: apa yang selesai, hasil build/smoke/render, hash commit, berkas yang sengaja tidak di-commit
(`claude.md` dan perubahan pengguna sebelumnya), verifikasi tertunda, dan skrip PowerShell manual bila ada
(path, urutan, perintah lengkap). Jangan menyatakan verifikasi tertunda sudah lulus.

## Catatan eksekusi

Kode, dokumen, build WPF, 14 skenario smoke, dan 20 PNG render tema terang/gelap selesai.
Rincian, keputusan tambahan, dan verifikasi nyata yang tertunda ada pada
[laporan eksekusi](../../doc/report/release-manager-profile-eksekusi.md).
`claude.md` sengaja tidak ikut commit sesuai K17/Tahap 7.6.
