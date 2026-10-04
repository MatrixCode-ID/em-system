# Release Manager WPF

Release Manager (`admin.release`) menyiapkan client desktop dengan **Prepare**, membandingkan hasilnya
dengan **Compare**, menerbitkan dan menandatangani dengan **Sync**, lalu memeriksa tanda tangan dan semua
berkas dengan **Verify**. Format rilis tetap mengikuti [kontrak format rilis](../release-format.md).
Prepare memerlukan .NET SDK 10; CDN memerlukan akses CDN Manager selain akses Release Manager.

## Profile

Satu profile menyimpan sumber solution/project, local publish folder, tujuan, dan pilihan signing key
untuk satu aplikasi. Pilih lewat combobox paling kiri di toolbar. Profile terakhir diingat **per alamat
server**, termasuk port non-default. Tanpa koneksi, pilihan disimpan terpisah.

Lokasi bawaan: `Documents\Em\ReleaseManager\Profiles\Release`. Menu **Profiles folder...** menyediakan
path, Browse, Copy path, dan Reset to default. Mengganti folder hanya membaca lokasi baru;
berkas lama tidak dipindahkan. **Open folder** membuka folder seluruh profile.

```text
<ProfilesFolder>/<GUID-format-N>/
  profile.json
  signing.pfx  (opsional, private key berpassword)
  signing.cer  (sertifikat publik DER, tanpa private key)
```

Contoh `profile.json` (gunakan path sesuai lingkungan Anda):

```json
{
  "formatVersion": 1,
  "id": "3f2a9c0e8b7d4a51b2c3d4e5f6a7b8c9",
  "name": "Example desktop",
  "solutionPath": "<workspace>\\Example.slnx",
  "hostProject": "src\\Example.Wpf\\Example.Wpf.csproj",
  "publishFolder": "<publish>\\Example",
  "targetKind": "Cdn",
  "targetFolder": "",
  "releaseFolder": "wpf-release",
  "signing": {
    "source": "ProfileFile",
    "thumbprint": "<certificate-thumbprint>",
    "passwordStorage": "Separate",
    "password": null
  }
}
```

Menu profile menyediakan **New profile**, **Duplicate**, **Rename**, **Delete**, **Import...**, dan
**Export...**. Nama harus unik tanpa membedakan kapitalisasi. Duplicate memberi id baru, menyalin berkas
key bila ada, dan tidak menyalin password DPAPI. Folder rusak tetap tampil sebagai invalid dan tidak
dapat dipilih. Tanpa profile, buat atau impor profile lewat card kosong; operasi rilis belum tersedia.

Settings mengedit salinan profile; Save menulis atomik. Bila `profile.json` diubah dari luar saat dialog
terbuka, pengguna memilih menimpa atau tetap mengedit. Cancel membuang perubahan setting. Operasi
Create/Import/Remove key terjadi langsung, sehingga tetap berlaku setelah Cancel.

## Signing key dan password

**Windows certificate store** memakai thumbprint key ECDSA P-256 di `CurrentUser\My`, dengan perilaku
exportable/non-exportable seperti sebelumnya. **Key file in this profile** memakai `signing.pfx` dan
sertifikat publik `signing.cer`. Verify dan informasi key tidak memerlukan password; Sync memerlukan
private key. Create/Import selalu meminta tujuan saat itu: **This profile** atau **Windows certificate
store**. Berkas impor diverifikasi password, private key, dan kurva P-256 sebelum disalin.

Password berkas memiliki dua mode:

- **Ask once per session** (Separate): password ada di memori aplikasi. Centang **Remember on this PC**
  untuk DPAPI CurrentUser pada `LocalApplicationData\Em\ReleaseManager\Secrets\<id>.bin`.
  Data ini hanya dapat dibuka user Windows yang sama. **Forget remembered password** menghapus sesi dan
  berkas terenkripsi. Password salah atau record rusak dihapus dan diminta ulang, maksimal tiga percobaan.
- **Save as plain text in profile.json** (Plaintext): password tertulis pada JSON. Siapa pun yang
  membaca atau menyalin folder profile dapat menandatangani rilis. **Set password...** memvalidasi
  password sebelum menyimpan draft. Password yang tersimpan salah harus diperbaiki di Settings.

Pindah Plaintext ke Separate saat Save memindahkan password ke sesi dan menghapus teks dari JSON.
Pindah Separate ke Plaintext memakai password sesi/DPAPI bila tersedia; jika belum, gunakan Set password.
Export key menghasilkan `.pfx` berpassword baru. Copy/Export public key menghasilkan PEM untuk launcher.
Remove key file menghapus `.pfx`, `.cer`, dan password milik profile itu; key di store tidak disentuh.

## Export, import, dan tabrakan

Export menghasilkan satu ZIP. Pertanyaan **Include sensitive data?** memiliki jawaban default **No**:
hanya JSON tanpa password dan sertifikat publik. **Yes** juga menyertakan `.pfx` serta password Plaintext
bila ada. Password Separate dan Remember tidak pernah disertakan. Import menerima ZIP atau JSON lepas;
id bentrok dapat diimpor sebagai profile baru dan nama bentrok mendapat akhiran nomor. Arsip dengan
entri selain tiga nama berkas di atas ditolak; batas setiap entri 10 MB. Profile tanpa `.pfx` tetap dapat
diimpor; impor key melalui Settings sebelum Sync.

Tujuan CDN yang sama, folder rilis lokal yang sama, atau local publish folder yang sama memunculkan
peringatan saat Save. Prepare memperingatkan publish folder yang sama, dan Sync memperingatkan tujuan
serta publish folder yang sama. **No** membatalkan; **Yes** melanjutkan. Peringatan tidak memberi kepemilikan
eksklusif terhadap folder tersebut.

Delete menghapus hanya subfolder profile beserta isinya dan record password DPAPI/sesi miliknya.
Local publish folder, rilis yang sudah terbit, dan certificate store tetap tersedia.

## Migrasi versi lama

Saat daftar pertama dimuat dan belum ada profile valid, tujuh setting Registry lama dipindah ke profile
**Default** dengan sumber key Store. Setelah JSON berhasil disimpan, value lama dihapus. Jika sudah ada
profile valid, value lama dibiarkan. Registry baru menyimpan hanya preferensi folder dan profile terakhir
per server. Migrasi dicatat di activity log.
