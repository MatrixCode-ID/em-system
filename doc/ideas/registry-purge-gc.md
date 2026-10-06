# Container registry: Purge dan Garbage Collection

- **Tanggal:** 2026-10-06
- **Status:** jadi plan (2026-10-06) untuk GC + review UI + hapus tag/manifest; purge berfilter, retensi, dan GC terjadwal masih diskusi
- **Plan turunan:** [plan/unexecuted/registry-gc-review.md](../../plan/unexecuted/registry-gc-review.md)
- **Catatan terkait:** [doc/engine/engine-registry.md](../engine/engine-registry.md) ("Known limitations": GC, retensi tag, kuota, audit = tahap 2)

Catatan ini berisi gagasan, bukan perintah kerja. Agent tidak boleh mengeksekusinya sebelum catatan ini dijadikan plan di `plan/unexecuted/`.

## Latar belakang

Pengguna bertanya cara menghapus artefak lama di Container Manager. Keadaan saat ini (dibaca dari kode 2026-10-06, belum dijalankan):

- UI WPF hanya bisa menghapus **container utuh** (`PostMeta_CtnImageDelete`). Menghapus tag atau manifest tertentu memang tidak disediakan ("Not available (by design)").
- API OCI `DELETE /v2/<name>/manifests/<tag|digest>` sudah ada (`CtnRegistryEndpoint.DeleteManifestAsync`) dan membutuhkan robot dengan hak W:
  - lewat tag: hanya baris `ta_CtnTag` yang dihapus, manifest tetap ada (menjadi *untagged*);
  - lewat digest: tag, `ta_CtnManifestBlob`, dan manifest dihapus.
- Semua penghapusan hanya mengenai **metadata**. Baris `ta_CtnBlob` dan file blob di storage (`CtnBlobStore.BlobPath`) tetap ada, begitu juga `ta_CtnBlobLink` setelah manifest dihapus lewat API. Upload yang tidak pernah diselesaikan (`ta_CtnUpload` dan file upload) juga tidak pernah dibersihkan.
- `GetMeta_CtnStorageSize` menghitung semua blob, jadi angkanya tidak turun setelah penghapusan.

## Gagasan: dua tahap seperti registry umum

Mengikuti pola Harbor, ACR (`acr purge`), dan Docker Distribution (`registry garbage-collect`):

### Tahap 1: Purge (metadata)

Menghapus tag dan manifest yang tidak dibutuhkan lagi.

- **Manual dari UI:** hapus satu tag atau satu manifest di daftar manifest detail container (klik kanan atau tombol Delete, dengan konfirmasi). Ini mengubah keputusan "by design" yang lama.
- **Purge berfilter:** per container, folder, atau root, dengan kriteria seperti:
  - manifest *untagged*;
  - umur (`datestamp` manifest lebih tua dari N hari);
  - pola nama tag (mis. `dev-*`, `pr-*`);
  - simpan N tag terbaru (retensi);
  - pengecualian tag yang dilindungi (mis. `latest`, pola semver rilis).
- **Dry run** wajib ada: tampilkan dulu daftar yang akan dihapus beserta ukurannya, baru eksekusi.
- Manifest yang direferensikan oleh index/manifest list lain tidak boleh dihapus selama index-nya masih ada.

### Tahap 2: Garbage Collection (disk)

Menghapus blob yang tidak lagi direferensikan.

- Pilih blob kandidat: `ta_CtnBlob` yang tidak dirujuk oleh `ta_CtnManifestBlob` mana pun. Tentukan juga perlakuan `ta_CtnBlobLink` (lihat pertanyaan terbuka).
- Hapus file blob lalu baris `ta_CtnBlob`; bersihkan `ta_CtnBlobLink` yatim.
- Bersihkan upload basi: `ta_CtnUpload` beserta file upload yang lebih tua dari N jam.
- Laporkan hasilnya: jumlah blob, byte yang dibebaskan, dan upload yang dibersihkan.

## Risiko yang perlu dijawab desainnya

- **Balapan dengan push:** client Docker meng-upload blob dulu, lalu PUT manifest. Blob yang baru selesai di-upload belum dirujuk manifest, sehingga GC bisa menghapusnya di tengah push. Opsi: masa tenggang berdasarkan umur blob, mode read-only selama GC, atau lock.
- **Blob dipakai bersama** banyak image (cross-mount): kandidat harus dihitung secara global, bukan per image.
- **Urutan file dan DB:** jika file dihapus tetapi transaksi DB gagal, metadata menunjuk ke file yang hilang. Urutan yang lebih aman: hapus baris DB dulu, lalu file; file yatim yang tertinggal bisa disapu pada run berikutnya.
- `RegistryStorageIntegrity` dan `CtnStartupChecks` mungkin perlu tahu bahwa blob boleh hilang setelah GC.

## Pertanyaan terbuka

1. Purge berfilter cukup manual (tombol + dry run) dulu, atau langsung ada **aturan retensi tersimpan** per root/container yang berjalan terjadwal?
2. GC dijalankan manual dari layar Settings atau Container Manager, terjadwal (`AddHostedService`), atau keduanya?
3. Saat GC berjalan, registry masuk read-only atau cukup memakai masa tenggang umur blob? Berapa lamanya?
4. Apakah `ta_CtnBlobLink` (penanda blob milik image, dipakai untuk cross-mount dan izin baca blob) tetap mempertahankan blob tanpa manifest? Jika ya, kapan link itu dihapus?
5. Claim: cukup `Container Manager Access`, atau perlu claim terpisah seperti `Container Registry Purge` (mengikuti pola `Container Registry Settings Manage`)?
6. Perlu jejak audit (siapa menghapus apa) sekarang, atau ditunda bersama fitur audit tahap 2?
7. Apakah tag immutable/terlindung masuk cakupan yang sama, karena bersinggungan dengan pengecualian purge?
8. Apakah perlu kolom waktu baru, misalnya waktu terakhir di-pull untuk kriteria "tidak dipakai", atau cukup `datestamp` push?
