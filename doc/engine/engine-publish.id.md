# Publish paket NuGet dan container dari WPF

[English](engine-publish.md)

**NuGet Manager** dan **Container Manager** memakai tab **Publish** dan tampilan **Publish history** yang sama.
Publish adalah operasi lokal pengembang: sesi GUI yang sedang masuk hanya menampilkan daftar tujuan, sedangkan
push dilakukan oleh robot atau API key yang dikonfigurasi terpisah. Kepemilikan robot tidak mewariskan hak
pemiliknya.

- Server NuGet bawaan membutuhkan hak **W** pada prefix feed yang sesuai.
- Container registry bawaan membutuhkan hak **W** pada root.
- Claim manager dan settings terpisah dari hak publish.

![Tab Publish di NuGet Manager](images/publish-nuget.png)

## Alur kerja

1. **Buat profil**, atau pakai **Create profile from .slnx**. Pilih workspace dan sumber, lalu periksa ID paket
   dan framework hasil evaluasi. Project yang tidak packable tidak bisa dipilih sebagai paket; project desktop
   dikecualikan sebagai host container Linux.
2. **Atur opsi build, tujuan, dan kredensial per host.** Tujuan bawaan diambil dari koneksi aktif; setelah
   berganti koneksi, pilih dan periksa lagi. Feed, prefix, root, atau container yang belum ada harus dibuat
   dulu di manager masing-masing.
3. **Check** memvalidasi tool, sumber, dan tujuan. **Prepare** (NuGet) atau **Build** (container) menghasilkan
   artefak tanpa push. NuGet juga menerima berkas `.nupkg` yang sudah ada lewat pemilih berkas atau drag and
   drop.
4. Pilih artefak, isi release note bila diwajibkan, periksa konfirmasi, lalu **Push**.
   **Prepare & Push** / **Build & Push** menggabungkan kedua langkah. Cancel hanya menghentikan pohon proses
   yang dimulai runner. Periksa hasil sebagian sebelum mengulang; item yang sudah berhasil dilewati saat
   diulang.
5. **Verify** memeriksa identitas paket NuGet dan SHA-512, atau digest manifest OCI di server tujuan. Hasil
   verifikasi disimpan terpisah dari hasil push. Penemuan tag di server tujuan memakai OCI, bukan API vendor.

![Tab Publish di Container Manager](images/publish-container.png)

## Profil dan berkas lokal

| Item | Lokasi bawaan |
| --- | --- |
| Profil | `Documents\Em\Publish\Profiles\NuGet` dan `...\Profiles\Container` |
| Log eksekusi | `Documents\Em\Publish\Logs` |
| Folder kerja | `LocalApplicationData\Em\Publish\Work` |
| Secret yang diingat | `LocalApplicationData\Em\Publish\Secrets` |

Publisher settings dapat mengubah folder Profiles, Logs, dan Work; preferensinya disimpan di bawah registry key
aplikasi (jadi tiap aplikasi, misalnya Em System dan EmPorium House, punya pilihan sendiri, tetapi folder
bawaannya sama). Berkas profil ditulis secara atomik; perubahan dari luar aplikasi harus dimuat ulang atau
ditimpa secara eksplisit. Import dan duplicate membuat referensi kredensial yang independen. Menghapus profil
tidak menghapus riwayatnya, dan riwayat mencakup profil yang sudah dihapus. Log eksekusi disimpan sampai
dihapus manual.

```json
{
  "formatVersion": 1,
  "id": "8b89b6538a83469d894951e13efad379",
  "name": "Engine packages",
  "kind": "NuGet",
  "description": "",
  "workspace": ".",
  "sensitiveDataStorage": "Separate",
  "nuGet": {
    "sources": [{ "path": "src/backend/Em.Api.slnx", "projects": [] }],
    "configuration": "Release",
    "versionOverride": "1.0.0",
    "msbuildProperties": [],
    "target": { "type": "Custom", "serviceIndex": "https://packages.example/v3/index.json" },
    "duplicateHandling": "Skip"
  },
  "credentials": [],
  "keepWorkspace": false,
  "requireReleaseNotes": true
}
```

Satu berkas per profil, format versi 1, dengan ID GUID yang tetap. Path sumber relatif diselesaikan dari
workspace. Profil NuGet mendukung beberapa sumber project atau solution.

### Mode container

Profil container memakai salah satu dari lima mode: **Dockerfile**, **LocalImage**, **Template**, **Compose**,
dan **Set**.

- Form mengubah build argument, named context, referensi BuildKit secret, configuration, runtime, framework,
  publish profile, environment, port, dan entrypoint. Dockerfile hasil generate dan set berkas dapat
  dipratinjau sebelum build.
- **Compose** hanya membangun service yang dipilih dan tidak pernah menjalankan `up`.
- **Template** dapat memakai `.pubxml` project; `PublishDir` diarahkan ke workspace eksekusi.
- **Set** membangun Base sebelum App dan berbagi input publish yang identik. Daftar berkas bernama mendukung
  pelengkap IncludeOnly dan Exclude, dengan Error (bawaan) atau Warning untuk entri yang tidak ada. Dockerfile
  yang sudah ada dibangun terhadap output yang di-stage, dapat di bawah subfolder bernama (`.file-base`,
  `.file-module`). App dapat memakai digest Base dari eksekusi yang sama, log Base terakhir yang berhasil, atau
  referensi eksplisit. Referensi Base yang immutable dan release note dicatat di riwayat.
- Duplikat profil untuk varian .NET atau repositori.

## Kredensial

**Separate** adalah penyimpanan bawaan. **Remember** menyimpan kredensial terenkripsi dengan DPAPI CurrentUser di
LocalApplicationData; tanpa Remember, kredensial hanya hidup selama sesi aplikasi. Dekripsi yang gagal membuat
kredensial tidak tersedia.

- Kredensial dibatasi pada host efektif (termasuk port) dan tidak pernah dikirim ke host lain.
- **Plaintext** menyimpan secret sebagai teks JSON biasa: siapa pun yang dapat membaca atau menyalin berkas itu
  dapat memakainya. Berkas profil di disk tidak pernah dienkripsi; hanya bundle export (di bawah) yang
  terenkripsi.
- Kolom secret ditutup (masked).

### Export dan import beserta secret

**Export** menanyakan apakah data sensitif disertakan. Bawaannya tidak: berkas berisi path yang dibuat portabel
dan tanpa secret, sehingga password harus diisi ulang setelah import. Dengan **Include sensitive data** Anda
memilih:

- **Encrypted with a passphrase** (disarankan): berkas bundle, `.ctnconfig` untuk profil Container Manager dan
  `.nugetconfig` untuk profil NuGet Manager. Isinya AES-256-GCM dengan kunci yang diturunkan dari passphrase
  (PBKDF2-SHA256, 600.000 iterasi); passphrase minimal 8 karakter, tidak pernah disimpan, dan tidak dapat
  dipulihkan. Bundle tidak bergantung pada akun Windows, sehingga dapat dibuka di komputer mana pun.
- **Plain text**: berkas `.json` biasa dengan secret terbaca. Siapa pun yang dapat membaca berkas itu dapat
  memakainya.

Secret profil **Separate** dibaca dari sesi atau dari penyimpanan Remember; kredensial yang tidak punya secret
tersimpan diekspor kosong dan layar menyebutkan jumlahnya. **Import** menerima berkas `.json` dan bundle (meminta
passphrase) lalu memulihkan secret-nya: inline untuk profil Plaintext, dan ke penyimpanan Remember (DPAPI untuk
akun yang mengimpor) untuk profil Separate, sehingga profil langsung bisa melakukan push. Kredensial selalu
mendapat ID baru, jadi import tidak pernah menyentuh secret profil lain yang tersimpan.

## Tool dan perilaku saat berjalan

- Membutuhkan .NET SDK 8 atau lebih baru untuk evaluasi metadata (SDK dan `global.json` milik project tetap
  berlaku), Docker daemon untuk image, Compose v2 untuk Compose, dan Buildx untuk build lintas platform.
- Push NuGet berjalan di dalam proses lewat NuGet.Protocol, sehingga API key tidak pernah muncul di argumen
  proses. Versi yang berada di recycle bin tujuan tetap gagal dan perlu dipulihkan atau dihapus permanen secara
  manual. Hanya `.nupkg` yang didukung.
- Docker berjalan dengan `DOCKER_CONFIG` sementara, login lewat stdin password, dan meneruskan BuildKit secret
  sebagai environment variable. **Use my Docker login** adalah opsi profil yang eksplisit; verifikasi OCI dan
  penemuan tag dapat membaca konfigurasi Docker atau credential helper milik host itu tanpa mengubah atau
  menyimpan secret-nya.
- Tag versi di-push sebelum tag tambahan. `latest` bersifat opsional dan dapat menimpa rilis sebelumnya.
- Pembersihan folder kerja memvalidasi kepemilikan dan batas folder; folder sumber dan output milik pengguna tidak
  pernah dihapus. Paket yang sudah disiapkan dan output publish bersama tetap bersama eksekusinya agar Push
  berikutnya dapat memakai snapshot itu.
- Log berisi output yang di-mask, exit code tiap tahap, tujuan efektif, artefak, hasil verifikasi terpisah,
  pengaturan non-secret, dan referensi retry. Eksekusi yang belum selesai tampil sebagai **Interrupted**.

## Keterbatasan

Registry eksternal yang mengirim bearer credential ke host autentikasi terpisah membutuhkan integrasi dengan
cakupan tersendiri; publisher tidak pernah mengirim kredensial registry melewati batas host. Symbol, berkas profil
terenkripsi di disk, retensi log otomatis, serta build jarak jauh atau MAUI belum diimplementasikan.
