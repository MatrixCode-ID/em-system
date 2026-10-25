# Eksekusi Release Manager MAUI Android

Tanggal: 2026-10-25. Implementasi A–G selesai; build dan tes akhir lewat RunBay.
Batas verifikasi APK nyata dijelaskan di bawah, bukan dinyatakan lulus.

## Urutan, branch, dan commit

Kedua working tree bersih sebelum mulai, sehingga tidak ada commit baseline kosong.
Seluruh kode engine A–G ditulis dahulu, lalu kode host pasangan langkah 1–5.
Build/tes/smoke dan review baru dimulai setelah kedua bagian kode selesai.
Semua commit engine memakai pesan Inggris di work-bench; tidak ada push.

| Seksi | Commit | Hasil |
| --- | --- | --- |
| A | 56b49eb | Kontrak Android dan empat vektor bertanda tangan |
| B | 9e111ce | Format pindah ke Em.Ui.Core.Release.Format; serializer Android |
| C | 51447ba | Platform/profile Android, identitas dan penyaringan host |
| D | 0478778 | Environment, Create/Import keystore, targets/publish APK dan versi |
| E | ae6aee3 | Settings, Sync/Verify, menu dan install link |
| F | e15c380 | Checker bersama, unduh/PackageInstaller, startup/manual dan host contoh |
| G | 9e6d09b | Guide Android dan pembaruan guide terkait |
| Aturan | 4eeba4b | Ringkasan CLAUDE.md dan urutan lintas repo |
| Review | 178329c | Perbaikan compile, metadata EmAppId, isolasi lokal dan regresi |
| Review | d9504d3 | Tool preparation, busy guard dan discovery TFM dari import |
| Review | 7f1c5c6 | Install link wajib URL publik dan caption update |

Commit penutupan memuat laporan ini, status dan perpindahan plan ke executed.
Guide utama: [Android updates](../engine/engine-android-updates.md) dan
[Release Manager](../engine/engine-release-manager.md).

## Keputusan implementasi

- Private key sampel lama sudah dibuang menurut kontrak lama. Empat sampel Android memakai
  android-public-key.pem baru; key lama dan byte vektor lama dipertahankan. Semua signature lama
  dan baru diperiksa tes. Tidak ada private key sampel yang disimpan.
- API format sekarang memakai namespace Em.Ui.Core.Release.Format tanpa type forwarder.
  Breaking change ini wajib masuk release note paket berikutnya; plan ini tidak menerbitkan paket.
- API host lama yang mengembalikan path tetap tersedia. Penyaringan platform memakai
  ReadHostPlatforms; TFM dari import dievaluasi dengan MSBuild. Identitas multi-target Android
  dievaluasi lagi dengan TargetFramework Android, bukan memakai properti outer build.
- Prepare membaca ulang versi/TFM/dukungan Java host; identitas pilihan profile tetap berlaku.
  Target kosong memakai ApplicationVersion valid (dibatasi batas Android) atau 1; target yang
  tidak terbaca tidak ditebak. Identity dan signing dibatasi ke path host pada targets.
- Java memakai MinimumSupportedJavaVersion/LatestSupportedJavaVersion yang dievaluasi;
  fallback minimal 17. API Android tanpa versi eksplisit pada TFM net10.0-android memakai API 36.
  Rider options android.xml/jdk.table.xml di konfigurasi JetBrains pengguna ikut dibaca;
  lokasi lain dapat dipilih lewat Browse. Semua SDK/JDK yang ditemukan tetap ditampilkan.
- Certificate keystore diekspor sebagai DER untuk hash yang sama dengan sertifikat X.509;
  Import juga membuat certificate request sementara untuk memeriksa akses private key/password key.
  Tidak ada password pada argumen, targets, atau log; environment child dan redaksi output dipakai.
- Prepare Environment menyusun langkah yang kurang, meminta konfirmasi, memakai winget untuk JDK,
  dan meminta elevasi workload jika dotnet berada di Program Files. Output elevated ditangkap
  melalui script/log sementara. Tidak ada tindakan pemasangan yang dijalankan pada sesi ini.
- EmMaui.Identity.targets menyematkan EmAppId; host ProjectReference mengimpornya secara eksplisit,
  paket membawa import transitive. Settings dan outbox memakai identitas itu; host lama tanpa
  metadata mempertahankan path ApplicationName, tanpa migrasi otomatis.
- Permission UsesPermission dari library berhasil tergabung pada manifest contoh engine.
  Tidak diperlukan fallback permission tambahan di host contoh.
- Unduh APK memeriksa ukuran/hash, membuang unduhan rusak atau batal. Cancel hanya tersedia
  selama download, sebelum PackageInstaller menangani konfirmasi. Mandatory tidak punya Later
  atau akses kembali ke aplikasi; flow perangkat aktual tetap belum diverifikasi.

## Verifikasi RunBay

Action terdaftar pada app em-system (EmCode ERP Engine). Script PowerShell berada di data
RunBay milik workspace host; source harness/log/output di
../.artefacts/em-system/scripts/release-android-smoke/ (output out/).
Harness host pasangan memakai implementasi smoke yang sama melalui source link.

| Action | Run id akhir | Hasil |
| --- | --- | --- |
| build-release-android | 20261025-205631-build-release-android-d706 | WPF dan MAUI Debug lulus, 0 error; permission gabungan lulus |
| test-release-android | 20261025-205716-test-release-android-eb26 | 294 lulus, 0 gagal, 0 skip |
| smoke-release-android | 20261025-205553-smoke-release-android-5167 | Create/Import keystore nyata lulus; exit 2 (pending prasyarat); APK nyata tertunda |

Suite mencakup round-trip/validasi/vektor lama-baru, SemVer, status update,
signature salah/key lain, package berbeda, Android object hilang, timeout/no cache,
download korup, JSON lama, perubahan platform/key, host multi-target/import,
versionCode, targets tanpa password, command environment/keytool, kandidat tools,
Registry terisolasi serta Folder Sync/Verify dan penolakan APK/identitas berubah.
Warning CS1591 pada API publik baru dan xUnit1051 pada beberapa pemanggilan tes
masih ada; tidak ada error build atau kegagalan tes.

Smoke menemukan SDK API 36/build-tools/platform-tools dan JDK 21. Workload list
berisi android, ios, maccatalyst, maui-windows, tetapi tidak maui-android/maui.
Preflight sesuai plan menghentikan Prepare sebelum publish. Tidak ada upaya memasang
workload, mengganti prasyarat atau melakukan Sync CDN produksi.

Diagnostik sebelumnya:
- Build 20261025-204243-build-release-android-6c71 gagal pada dua referensi namespace
  format lama dan pemanggilan extension tema; diperbaiki.
- Build 20261025-204327-build-release-android-d3c5 kemudian lulus WPF/MAUI.
- Test 20261025-204436-test-release-android-4e4d memakai opsi --disable-build-servers
  yang tidak cocok dengan runner MTP; exit 5, dihentikan. Action diperbaiki.
- Test 20261025-204528-test-release-android-1c6b lulus 287.
- Test 20261025-204934-test-release-android-1e0c lulus 293/exit 0; status RunBay
  dihentikan karena child MSBuild bertahan. Node reuse kemudian dimatikan pada action.
- Smoke awal gagal compile harness (using System.IO), lalu deteksi Java diperbaiki
  dengan parser output java -version yang dites. Discovery Rider memerlukan using
  System.Xml.Linq; diperbaiki. Smoke 20261025-204848-smoke-release-android-a36a
  memastikan workload maui-android memang tidak ada.
- Build 20261025-204934-build-release-android-743c sempat gagal parsing script action;
  quoting pemeriksaan manifest diperbaiki. Run 20261025-205046-build-release-android-7136
  dan 20261025-205219-build-release-android-03c1 lulus.

## Tertunda dan tindakan pengguna

1. Pasang maui-android lewat Prepare Environment yang sudah tersedia, atau dari terminal
   yang sesuai SDK aktif: dotnet workload install maui-android. Pemasangan ini tidak dijalankan agent.
2. Jalankan ulang smoke-release-android di RunBay setelah workload tersedia.
   Yang belum dibuktikan: publish APK arm64 bertanda tangan, apksigner/certificate APK,
   aapt2 package/version/ABI, metadata update dalam APK, Prepare kedua code +1.
   Folder Sync/Verify sudah diuji dengan fixture console; alur APK nyata tetap tertunda.
3. Verifikasi visual Settings/Prepare Environment/keystore dan semua UI MAUI baru;
   pemasangan/update opsional/wajib/izin unknown sources di perangkat/emulator.
4. Sync ke CDN nyata, profile/keystore/key produksi dan backup permanen dilakukan pengguna.
   Jangan menyalin keystore uji; harness memakai key sementara dan menghapus fixture di finally.

Tidak ada tindakan yang ditolak policy, sehingga tidak ada skrip manual policy.
Kuota diperiksa sebelum mulai dan berkala; terakhir sisa 5 jam 65%, mingguan 81%, tidak mencapai ambang berhenti 1%.
Tidak ada reset kuota, pembelian kredit, push, uji visual atau pemasangan environment.
