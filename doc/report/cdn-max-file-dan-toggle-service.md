# Eksekusi CDN max file dan toggle service

Tanggal: 2026-10-22. Repo: em-system, branch `work-bench`.

## Hasil

- Batas file CDN runtime tersimpan atomik di `settings/cdn.json`, dengan cache
  byte atomik dan snapshot batas per operasi upload/extract. Nilai host menjadi
  seed; berkas runtime menang pada startup berikutnya. Default semua jalur 200 MB.
- CDN Manager menghapus kartu Settings, menyediakan pensil pada chip batas,
  NumericBox MB serta Save/Cancel. Managed folder tetap kuota tanpa pensil.
  Action `PostMeta_CdnMaxFileSize` memakai claim CDN Manager Access dan audit
  System.Settings bila log engine terpasang.
- Empat manager memakai `FeatureServiceToggle`, view model MVVM dan style switch
  bersama. Status dapat dibaca pemegang claim manager; perubahan khusus admin,
  langsung berlaku. NuPak lama juga meneruskan aksi enable ke switch yang sama.
- Host feature mati: klaim manager/settings dan action bersangkutan tidak masuk
  katalog aktif. Gate publik menutup /cdn, /nuget, /store dan /v2 dengan 404,
  termasuk prefix fitur yang tidak didaftarkan host (tidak jatuh ke fallback 200).
- Service mati: protokol publik 404; action management tidak diblokir switch.
  Facade `IObjectStore` untuk modul/adapter binary storage melempar
  `ServiceDisabledException` 503, sedangkan engine konkret tetap tersedia untuk
  manager dan maintenance. Operasi yang sudah mulai tidak dibatalkan.
- Deklarasi claim Container Manager ditambahkan secara idempotent; sebelumnya
  registrasi manager hanya memasang action. Filter fitur mati tetap berlaku.

## Pilihan implementasi

- Rentang batas runtime **1–1.048.576 MB** (1 TB), MB = 1.048.576 byte.
  Rentang ini mendukung berkas besar; perkalian batas total extract memakai checked.
  Extract tetap maksimal 10 kali batas per berkas dan 10.000 entri.
- Satu kontrak `FeatureServiceState` dan layanan `IFeatureServiceServices`, dengan
  pasangan action GetMeta/PostMeta per fitur. Claim statis per action mengikuti
  dispatcher existing; pemeriksaan admin dilakukan kembali di badan action tulis.
- Penyimpanan status: CDN dan registry `settings/service.json` (boolean JSON,
  atomic rename); NuPak `ta_Meta.NuPakEnable` existing, Object Storage
  `ta_Meta.ObjectStore.ServiceEnabled`. Memakai lokasi database NuPak mempertahankan
  flag existing dan menghilangkan pemeriksaan server-on lama yang terpisah.
  Key yang belum ada atau kosong berarti enabled. Cache diserialisasi dengan semaphore;
  perubahan memori dilakukan setelah penyimpanan sukses. Satu instance per data.
- CDN/registry tidak mempunyai kontrak store untuk modul publik; action release,
  deploy dan publisher tetap action pengelolaan. Publikasi yang memakai protokol
  NuGet/OCI tunduk pada aturan publik 404. Facade modul yang perlu diblokir khusus
  adalah IObjectStore; manager/maintenance sengaja memakai engine konkret.
- Tidak ada perubahan skema/DDL. Tidak ada deployment database atau host untuk
  eksekusi ini. Tidak diperlukan pembaruan skema pada server pengguna.

## Verifikasi

- `dotnet build src/backend/Em.Api.slnx`: lulus, 0 error.
- `dotnet build src/frontend/Em.Ui.Wpf.slnx`: lulus, 0 error.
- `dotnet build src/frontend/Em.Ui.Maui.slnx`: lulus, 0 error.
- Tes terfilter saja:

```powershell
dotnet test --project tests/Em.Api.Core.Tests --no-build -- --filter-class '*CdnRuntimeServiceTests' --filter-class '*ContainerManagerRegistrationTests'
```

Hasil: **12 lulus, 0 gagal, 0 skip**. Mencakup seed/override/persistensi batas,
perubahan langsung pada store yang sama, snapshot upload, batas entri/total
extract, persistensi switch, 404 pada handler dengan HttpContext tiruan,
503 sebelum akses database pada seluruh metode facade object store,
penolakan non-admin pada keempat action tulis, dan deklarasi claim registry.
Tes tersebut bukan smoke HTTP maupun pengujian database sungguhan.

Review source dan `git diff --check` dilakukan setelah kedua tahap selesai.
Temuan yang diperbaiki: akses Meta memakai nama DbSet core yang benar,
pemeriksaan flag NuPak lama, fallback publik fitur mati, claim registry,
registrasi command editor limit yang sempat terduplikasi, dan notifikasi UI.
Peringatan build yang tersisa berasal dari kode tes/modul existing; tidak ada
peringatan baru pada kode switch/editor.

Log lokal: `../.artefacts/em-system/cdn-service-backend-build.log`,
`cdn-service-wpf-build.log`, `cdn-service-maui-build.log`, `cdn-service-tests.log`.
Tidak ada harness baru, tindakan terblokir policy, atau skrip manual.

## Pemeriksaan pengguna yang tertunda

Sesuai keputusan pengguna, **tidak menjalankan suite penuh, smoke HTTP, atau GUI**.
Pengguna memeriksa layout/interaksi terang-gelap, edit Save/Cancel, status admin
versus non-admin, toggle masing-masing layanan, restart dengan setelan tersimpan,
dan pemakaian nyata terhadap data fitur. Penyimpanan ta_Meta dan alur protokol
live belum diuji ke database/server sungguhan. Tidak ada klaim bahwa pemeriksaan
fungsional/visual tersebut telah lulus.

Guide diperbarui: engine-cdn-storage, engine-storage-settings, engine-nupak,
engine-object-storage (termasuk .id), dan engine-registry. Keputusan dicatat di
CLAUDE.md dengan mempertahankan aturan sebelumnya. Dua commit tahap disimpan
lebih dahulu, disusul commit perbaikan/tes/guide/laporan. Tidak ada push.

## Perbaikan lanjutan 2026-10-22 — menu fitur host mati

Laporan pengguna menemukan NuGet Manager masih bisa diluncurkan ketika API host
tidak memanggil AddNuPak. Penyebab: katalog gabungan client selalu menyertakan
claim internal NuGet, sehingga filter navigasi menganggap fitur tersedia walaupun
claim server sudah dihapus dengan benar. Ini bukan masalah database atau skema.

NavigationAccess.MergeClaimCatalogs sekarang hanya mempertahankan claim internal
storage yang dideklarasikan API setelah katalog server dimuat. WPF/MAUI memakai
penggabungan yang sama; menu/home dan navigasi langsung mengikuti hasilnya.
Claim layar client lainnya dipertahankan. Tidak menambahkan fallback palsu atau
mengaktifkan fitur host yang mati.

Build WPF dan MAUI lulus; 10 tes terfilter NavigationAccessTests lulus. Tidak ada
GUI atau smoke HTTP. Client host perlu rebuild/restart. Perubahan lanjutan ini
belum di-commit pada saat laporan diperbarui.
