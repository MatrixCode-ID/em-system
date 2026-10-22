# Laporan eksekusi skin CDN dan home

Tanggal: 2026-10-22. Status: selesai implementasi, build/test terfilter dan review;
pemeriksaan visual dilakukan pengguna. Plan: [cdn-home-skin.codex.md](../../plan/executed/cdn-home-skin.codex.md).

## Hasil

- Kontrak/model skin, EmbeddedFile, EmSkinAssets dan base class Razor ada di
  `src/backend/Em.Api.Core/Api/Skin/`; registrasi singleton satu kali ada di
  `EmAppBuilder.Skin.cs`. ApplicationName berasal dari host, versi memakai AppVersion
  entry assembly (metadata dibuang, awalan v; development menjadi dev).
- Formatter CDN menjadi adaptor model; DefaultCdnSkin mempertahankan HTML/CSS listing
  lama, urutan folder dahulu, escaping, ukuran dan waktu UTC. DefaultHomeSkin memakai
  logo/ikon existing lewat EmbeddedResource Link, tanpa salinan aset engine.
- Home dan favicon dipetakan eksplisit. Fallback seluruh path, termasuk berekstensi,
  kini 404; GET penerima HTML memakai skin. Ketiga kondisi CDN not found memakai skin
  yang sama, tanpa mengungkap penyebab. Unduhan tetap static middleware existing.
- `.skin` ditulis langsung sebelum server menerima request, divalidasi terhadap path
  absolut tepat di root publik, tanpa staging/lock/marker. Nama root dicadangkan pada
  resolver manajemen; provider publik hanya mengizinkan berkasnya, tanpa listing folder.
  Listing/tree/count/storage/archive dan jalur extract/move/upload/managed menegakkan
  pengecualian/pencadangan tersebut. Folder `.skin` di bawah folder biasa tetap sah.
- Guide [engine-skin.md](../engine/engine-skin.md), indeks, guide CDN/public endpoints
  dan keputusan CLAUDE.md diperbarui. Tidak ada release note NuGet atau perubahan DDL.

## Urutan dan verifikasi

Seluruh kode engine (termasuk test) ditulis dahulu, dilanjutkan kode host pasangan.
Tidak ada build, test atau review di antara tahap kode tersebut. Sesudah kedua kode
selesai, build/test dan review dijalankan. Perbaikan review memaksa pembacaan logo pada
constructor/startup, memvalidasi prefix aset yang tak cocok, dan merapikan komentar.
Verifikasi yang terdampak perbaikan tersebut kemudian diulang.

| Perintah | Hasil akhir |
| --- | --- |
| `dotnet build src/backend/Em.Api.slnx --nologo -v minimal` | Lulus, 0 error, 2 warning xUnit1051 existing di ApprovalAnalyzerTests baris 34/44 |
| Build solution API host pasangan | Lulus, 0 warning/error; rinciannya pada laporan privat pemilik host |
| `dotnet test --project tests/Em.Api.Core.Tests --no-build --filter-class Em.Api.Core.Tests.SkinTests` | 14 lulus, 0 gagal, 0 skip |
| Review source dan `git diff --check` | Selesai, tidak ada masalah whitespace atau temuan tersisa |

Hanya **SkinTests** yang ditambah/diubah dan dijalankan. Test mencakup negosiasi GET/HEAD/
Accept (termasuk quality nol), escaping/sorting/parent listing bawaan, kedua base Razor
melalui komponen C#, home HTML/teks/HEAD, favicon ada/tidak ada, 404 situs/CDN dengan/tanpa
aset, registrasi ganda, reserved endpoint/resource hilang, penulisan/penggantian/penghapusan
aset, kegagalan startup, provider berkas vs listing, reservasi operasi manajemen, archive
root, extract folder-only dan count/storage tanpa aset.

Log nonrahasia di luar repo: `../.artefacts/em-system/cdn-home-skin-build.log` dan
`../.artefacts/em-system/cdn-home-skin-tests.log`. Tidak ada smoke HTTP, suite seluruh
solution, uji visual, publish/deploy atau push. Tidak ada tindakan ditolak policy.

## Penyesuaian dan pilihan implementasi

1. Model directory memakai record dengan required init properties dan read-only collections;
   model lainnya positional record. Label tetap teks asli untuk di-encode renderer; href
   sudah di-escape per segmen. Ini menghindari double-encoding Razor.
2. Versi memakai `AppVersion.Display` agar sama persis dengan aturan product version.
3. URL aset memakai hash SHA-256 penuh. Pemisah logical resource dinormalisasi, prefix harus
   berakhiran `/`, tidak boleh kosong dan harus cocok setidaknya satu resource. Path invalid
   dan nama duplikat menggagalkan startup sebelum penghapusan aset lama.
4. Accept HTML dengan quality nol dianggap tidak menerima HTML. Tidak ada font eksternal.
5. Saat service off sejak startup, disk dan peta URL aset tidak dimuat ulang; toggle tidak
   menulis disk. Restart dengan service aktif diperlukan untuk menerapkan aset skin baru
   atau menghapus aset setelah registrasi dibuang. Ini menjaga aturan disk tidak disentuh
   saat service off. Home/favicon tetap berfungsi independen.
6. DefaultHomeSkin adalah implementasi interface self-contained; guide juga memberi contoh
   Razor. Tidak ada penambahan MVC/paket atau perubahan SDK engine.

## Bahan release note dan batas verifikasi

Perubahan perilaku: welcome catch-all 200 menjadi not-found 404; root eksplisit;
halaman HTML hanya GET penerima text/html; root `.skin` selalu dicadangkan.
Range/ETag/download tidak diimplementasikan ulang. Perilaku HTTP live tidak diuji sesuai
batas pengguna. Tampilan terang/gelap, ponsel, home/favicon, CDN listing/asset/not-found
serta path tak dikenal diperiksa pengguna. Multi-instance storage tetap di luar cakupan.

Commit hanya path plan di work-bench, tanpa push. Perubahan UI dan dokumentasi toggle
service yang sudah ada sebelumnya tetap di luar staging/commit pekerjaan ini.
