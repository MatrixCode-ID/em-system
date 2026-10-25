# Versi rilis terlihat dan metadata build yang stabil

- Tanggal: 2026-10-26.
- Status: dipertimbangkan; diterapkan langsung atas permintaan pengguna 2026-10-26.
- Plan turunan: belum ada.

## Temuan

Release Manager menampilkan tanggal manifest target melalui `TargetReleaseCaption`,
tetapi belum menampilkan `RemoteManifest.Version` pada ringkasan Compare.
Manifest lama atau Sync dengan release note lock mati dapat tidak mempunyai versi
aplikasi. Versi library engine bukan versi rilis aplikasi dan harus dibedakan.

Prepare memakai compilation deterministik dan meniadakan SourceRevisionId dari
InformationalVersion. SourceLink masih membawa commit repository dalam PDB;
perubahan PDB ikut mengubah record checksum PDB di DLL. Karena itu perubahan
commit di luar source modul bisa membuat hash modul berubah.

## Usulan

- Tampilkan versi artefak lokal yang benar-benar sudah di-Prepare, versi aplikasi
  pada manifest server, dan waktu publikasi. Jika versi target tidak tercatat,
  tampilkan status eksplisit; jangan menggantikannya dengan versi launcher/engine.
- Pertimbangkan menulis versi aplikasi ke manifest setiap Sync bila identitas
  artefak tersedia, terpisah dari fungsi release note lock. Pertahankan pembacaan
  manifest lama tanpa versi; jangan menebak versi dari timestamp.
- Nonaktifkan metadata SourceLink berbasis commit khusus build distribusi Prepare.
  Pertahankan simbol debug yang sesuai, normalization path dan deterministic build;
  build development tetap boleh memakai SourceLink.
- Simpan commit untuk penelusuran di catatan build/rilis terpisah, sehingga commit
  global tidak ditanam kembali ke setiap binary modul.
- Compare tetap memakai SHA-256 seluruh binary. Jangan mengabaikan perbedaan
  checksum atau membandingkan hanya IL untuk menentukan file yang perlu diunggah.
- Pembuktian sebelum menyatakan selesai: dua Prepare dari source modul sama dengan
  commit repository berbeda menghasilkan DLL/PDB modul sama, sementara perubahan
  source modul tetap menghasilkan Changed. Periksa juga sisa metadata compiler/WPF.
- Peralihan kebijakan metadata akan membuat binary berubah satu kali; kestabilan
  antar rilis setelah peralihan harus diuji dan tidak menjanjikan semua modul tetap
  sama bila versi, source, compiler atau dependency yang memengaruhi output berubah.

## Keputusan dan pertanyaan terbuka

Pengguna meminta penerapan langsung pada 2026-10-26. Ringkasan Compare memakai
Local prepared dan Target dengan waktu publikasi UTC. Versi dibaca dari artefak,
manifest mencatat versi standar juga saat lock mati. Commit dan hash host dicatat
di `<publish-folder>.build.json` di luar distribusi. Implementasi dan verifikasi
dicatat di `doc/report/release-manager-version-metadata.md`; tanpa plan terpisah
karena permintaan perubahan kode langsung.

Verifikasi selesai: 105 tes terkait rilis dan build host pemakai lulus lewat RunBay.
Uji visual, Prepare produk nyata dan Sync produksi belum dijalankan.

Koreksi pengguna 2026-10-26: ringkasan utama harus menampilkan rilis server,
bukan Local prepared. Nomor rilis Windows yang dibuat saat build terdapat pada
`launcher.version`; versi aplikasi tetap terpisah. Ringkasan kini dibaca langsung
dari manifest target saat memilih/refresh profil, tanpa memerlukan prepared atau
hasil Compare. Status/error eksplisit dan timeout 30 detik mencegah fallback lokal.
Koreksi ini menggantikan usulan ringkasan Local prepared/Target di atas.
