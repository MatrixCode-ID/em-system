# Eksekusi profile Release Manager

Tanggal: 2026-10-04. Status: implementasi, build, smoke, render, dan review selesai.
Plan: [release-manager-profile.codex.md](../../plan/executed/release-manager-profile.codex.md).
Panduan: [engine-release-manager.md](../engine-release-manager.md).

## Hasil

Setting rilis kini tersimpan per profile sebagai JSON atomik pada folder GUID. Toolbar menyediakan
pemilihan profile dan seluruh aksi pengelolaan; keadaan kosong tidak membuat profile otomatis.
Pilihan terakhir diingat per alamat server. Migrasi tujuh value Registry lama hanya dilakukan jika
belum ada profile valid, dan value lama baru dihapus setelah profile berhasil tersimpan.

Key dapat memakai certificate store atau berkas `.pfx`/`.cer` dalam profile. Password Separate
tersedia dalam sesi atau DPAPI CurrentUser dengan Remember; mode Plaintext tersimpan dalam JSON.
Sync mengambil password pada thread UI sebelum operasi async dan melepas sertifikat/private key
sesudahnya. Verify tetap memakai sertifikat publik tanpa memerlukan password atau berkas private key.
ZIP dapat menyertakan data sensitif hanya setelah pilihan eksplisit, default No. Delete terbatas pada
folder profile dan record password sesi/DPAPI miliknya. Peringatan tujuan serta publish folder
tersedia saat Save, Prepare, dan Sync sesuai K12–K14.

## Berkas

Baru:

- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseProfile.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseProfileStore.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseManagerPreferences.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseSigningSecrets.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseProfileConflicts.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/ReleaseProfileKeyAccess.cs`
- `src/shared/Em.Ui.Wpf.Core/Dialogs/ReleaseProfilesFolderDialog.xaml` dan `.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Dialogs/SigningKeyDestinationDialog.xaml` dan `.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Styles/Menus.xaml`
- `scripts/release-profile-smoke/Program.cs` dan `ReleaseProfileSmoke.csproj`
- `scripts/release-profile-render/Program.cs` dan `ReleaseProfileRender.csproj`
- `doc/engine-release-manager.md` dan laporan ini.

Diubah:

- `src/shared/Em.Ui.Wpf.Core/Navigations/ReleaseManager.xaml` dan `.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Dialogs/ReleaseSettingsDialog.xaml` dan `.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Dialogs/PasswordInputDialog.xaml` dan `.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Dialogs/TextInputDialog.xaml.cs`
- `src/shared/Em.Ui.Wpf.Core/Release/SigningCertificates.cs` (method tambahan; perilaku method lama tetap)
- `src/shared/Em.Ui.Wpf.Core/Styles/MaterialDesign.xaml`
- `doc/ideas/release-manager-profile.md`
- `claude.md` (paragraf tambahan, sengaja tidak di-commit).

Dihapus: `src/shared/Em.Ui.Wpf.Core/Release/ReleaseSettings.cs`; enum tujuan dipindah ke model profile.
Plan dipindah dari `plan/unexecuted/` ke `plan/executed/` dan ditandai sudah dieksekusi.
Project harness tidak ditambahkan ke solution. `bin/` dan `obj/` tidak ikut commit.

## Build dan smoke

Ketiga build terakhir lulus dengan **0 warning, 0 error**:

```powershell
dotnet build src/frontend/Em.Ui.Wpf.slnx
dotnet build scripts/release-profile-smoke
dotnet build scripts/release-profile-render
dotnet run --project scripts/release-profile-smoke
dotnet run --project scripts/release-profile-render
```

Backend dan MAUI tidak di-build karena tidak terdampak. Percobaan build awal menemukan kesalahan callback
dan using harness; keduanya diperbaiki. Build harness bersamaan sempat memperebutkan output WPF bersama;
build berikutnya berurutan. Warning lock Em.Libs sementara pada build awal tidak muncul pada build akhir.
Tidak ada proses pengguna yang dihentikan.

Output smoke terakhir:

```text
PASS Create List Load Save Rename unique names
PASS Broken JSON and mismatched id stay in list
PASS External changes rejected
PASS PFX public certificate validation signing and re-export
PASS Duplicate copies files and gets new identity and name
PASS Delete confined to profile folder and GUID ids
PASS Registry migration only when no valid profiles
PASS ServerKey normalization and last profiles per server
PASS Target and publish conflicts normalized; empty values ignored
PASS Session DPAPI thumbprint corruption and Forget
PASS Export sensitive selection Import conflict new id and loose JSON
PASS Settings draft cancel and password mode roundtrip
PASS Public certificate retained without private key; empty-state commands
PASS ZIP duplicate entries and oversized entry rejected
PASS cleanup temporary folder and test Registry; failures=0
```

Harness memakai folder sementara dan subkey HKCU ber-GUID miliknya sendiri, lalu menghapus keduanya di
finally. Penghapusan folder memvalidasi batas path dan reparse point. Tidak memasang/menghapus key di
certificate store. Uji pemulihan DPAPI mengosongkan cache melalui test hook internal lewat reflection,
lalu membaca dengan instance baru pada user Windows yang sama.

## Render

Hasil: `PASS rendered 20 screens`. Semua PNG dibuka dan diperiksa: teks terbaca, toolbar muat pada lebar
1280, dialog tidak terpotong, combobox/menu/card/radio/dialog bertema gelap tidak memiliki area putih.
Keadaan busy menonaktifkan pemilihan profile dan aksi yang terkait. Potongan alamat ringkasan tetap
memiliki tooltip penuh. Berkas tersimpan di luar repo pada `../.artefacts/em-system/release-profile-render/`:

| Keadaan | Terang | Gelap |
| --- | --- | --- |
| Kosong | `light-empty.png` | `dark-empty.png` |
| Profile aktif | `light-idle.png` | `dark-idle.png` |
| Busy | `light-busy.png` | `dark-busy.png` |
| Menu | `light-menu.png` | `dark-menu.png` |
| Settings Store | `light-settings-Store-Separate.png` | `dark-settings-Store-Separate.png` |
| Settings ProfileFile Separate | `light-settings-ProfileFile-Separate.png` | `dark-settings-ProfileFile-Separate.png` |
| Settings ProfileFile Plaintext | `light-settings-ProfileFile-Plaintext.png` | `dark-settings-ProfileFile-Plaintext.png` |
| Folder | `light-folder.png` | `dark-folder.png` |
| Tujuan key | `light-destination.png` | `dark-destination.png` |
| Password Remember | `light-password.png` | `dark-password.png` |

PNG menu memakai latar transparan di luar bounds popup; area di luar menu bukan permukaan UI.
Harness melepas konten dialog dengan mempertahankan DataContext/resources/foreground. Setiap keadaan
layar memakai visual tree baru untuk menghindari drawing posisi lama setelah reparent pada renderer.
Ini merupakan pemeriksaan render statis; transisi cepat pada window nyata tetap tertunda.

## Review

Review seluruh perubahan dilakukan setelah Tahap 1–6. Temuan yang diperbaiki: InvalidDataException
harus ditangkap tersendiri agar daftar tetap berjalan; clone draft tidak boleh memutasi password asli;
sertifikat publik tetap dimuat walaupun `.pfx` tidak tersedia; duplicate juga mempertahankan folder
kosong; sertifikat dibuang pada jalur kegagalan pembacaan. Uji regresi Settings dan arsip ditambahkan.

Path GUID, batas root, penolakan reparse point, validasi seluruh tree sebelum delete/duplicate, whitelist
nama ZIP, batas 10 MB saat ekstraksi, dan penolakan entri ganda diperiksa. Tidak ada password pada log
produksi. DPAPI tidak diekspor. `git diff --check` lulus. Pencarian ReleaseSettings tidak menemukan sisa
pemakai; SigningThumbprint hanya tersisa pada migrasi Registry. Tidak ada TODO baru.

Fondasi rilis yang dilarang diubah tetap utuh: `Release/Format/`, ReleaseBuilder, LocalPublish,
ReleaseComparer, ReleaseSync, ReleaseVerifier, ReleaseTarget, CdnReleaseTarget, FolderReleaseTarget,
serta `doc/release-format.md`. Tidak ada perubahan Em.Libs/backend maupun format rilis.

## Keputusan saat eksekusi

- Baseline aktual adalah `0f4c6bb`, dan working tree bersih saat mulai, berbeda dari snapshot plan
  `166c383` yang mencatat enam perubahan pengguna. Tidak ada berkas pengguna tersebut yang di-stage.
  `claude.md` tetap dikecualikan sesuai K17/Tahap 7.6; pengguna perlu meng-commit catatan itu sendiri.
- Dialog tujuan/folder mengikuti pola EmWindow dan MVVM. Constructor view tanpa argumen hanya membangun
  visual tree; inisialisasi produksi tetap melalui constructor EmApp. Render Settings memakai EmApp
  tak terinisialisasi tanpa akses Registry produksi, jaringan, atau service provider.
- `ReleaseProfileKeyAccess` internal menyatukan permintaan/validasi password Sync dan Export key.
  Selain P-256, pasangan `.pfx`/`.cer` diperiksa thumbprint-nya sebelum signing.
- Cache secret statis memakai path file terenkripsi serta id sebagai scope agar harness/lokasi berbeda
  tidak saling memakai cache. Data DPAPI rusak dihapus bila izin filesystem memungkinkan; kegagalan
  cleanup tetap menghasilkan password tidak tersedia.
- Menu bersama baru di `Styles/Menus.xaml` memakai template eksplisit untuk menu datar, serta separator
  bertema. Combobox menggunakan style fieldCombo yang sudah ada. Chip/filter di toolbar ditempatkan pada
  baris kedua supaya seluruh aksi tetap muat pada lebar 1280. Card ringkasan mendapat tombol refresh.
- Import juga menolak entri ZIP ganda dan membatasi JSON lepas 10 MB. Duplicate memotong nama jika perlu
  agar akhiran copy/nomor tetap memenuhi batas 100 karakter.
- Sertifikat publik profile tanpa private-key file tetap ditampilkan dan tersedia untuk Verify;
  Sync dan Export private key dinonaktifkan sampai `.pfx` diimpor.
- Tidak ada tindakan ditolak policy, tidak ada skrip PowerShell manual yang diperlukan.

## Verifikasi tertunda

- Interaksi mouse nyata: menu, pemilihan invalid profile, keyboard, folder picker, konfirmasi, serta dialog key.
- Sync ke CDN server nyata memakai key berkas dan password Separate/Remember/Plaintext.
- Prepare/Sync/Verify tujuan folder lewat UI, memakai private key berkas sungguhan.
- Migrasi setting Registry pada mesin pengguna dengan setting lama sebenarnya.
- Perilaku pemilihan profile ketika koneksi server aplikasi berganti. NavigationStack memanggil Reload
  saat membuka layar baru, tetapi handler ActiveConnectionChanged pada TabbedMainWindow hanya memperbarui
  koneksi/menu; tidak terlihat pemanggilan Reload body Release Manager saat mengganti koneksi pada tab
  yang tetap terbuka. Sesuai plan tidak ditambahkan subscription baru; gunakan Reload untuk membaca
  pilihan terakhir server baru. Alur aplikasi nyata ini belum diuji.
- Transisi busy cepat dan pemulihan fokus pada window nyata dalam kedua tema.

## Commit

Satu commit Bahasa Indonesia berjudul `Tambahkan profile di Release Manager`, tanpa push/amend/no-verify.
Hanya berkas plan ini yang di-stage secara eksplisit. Status akhir yang diharapkan dan diverifikasi
sesudah commit: hanya `M claude.md`; berkas setup/container dan ide namespace yang disebut snapshot
plan tidak memiliki perubahan lokal sejak awal eksekusi ini.
