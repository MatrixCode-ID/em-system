# Project test xUnit v3 di `tests/`

Dibuat 2026-10-04. Lanjutan pekerjaan yang terputus karena context window penuh; sebagian berkas sudah ditulis (lihat Status).

## Keputusan final (dari pengguna)

- Semua project test berada di folder `tests/` di root repo, sejajar dengan `src/`. Nama project `<Project>.Tests`, test integrasi `<Project>.IntegrationTests`.
- Framework: **xUnit v3** (`xunit.v3` 4.0.1, memakai Microsoft Testing Platform v2), ditambah `xunit.runner.visualstudio` 4.0.0 dan `Microsoft.NET.Test.Sdk` 18.10.1 supaya Test Explorer VS, Rider, dan `dotnet test` sama-sama jalan.
- Test integrasi memakai SQL Server **lokal** `(local)` dengan **Windows Authentication** (tanpa password). Server bisa diganti lewat env `EM_TEST_DB_SERVER`. Database sementara `EmSystem_IntegrationTest_<guid>` dibuat per run dan dihapus di akhir. Bila server tidak terjangkau, test di-skip (`Assert.Skip`), bukan gagal.
- Assertion: bawaan xUnit saja untuk sekarang (tanpa FluentAssertions, karena lisensi v8+ komersial). Mock belum dipakai.
- Project test dimasukkan ke solution areanya masing-masing (`.slnx` boleh merujuk path di luar folder).
- Commit di akhir: ya, satu commit berbahasa Indonesia (lihat aturan commit di memory/CLAUDE.md), tanpa push.

## Status

Sudah ditulis (periksa, jangan tulis ulang kecuali perlu perbaikan):

- `tests/Directory.Build.props`: import Directory.Build.props root lewat `GetPathOfFileAbove`, `OutputType=Exe`, `IsPackable=false`, `IsTestProject=true`, ImplicitUsings/Nullable, tiga paket test, `<Using Include="Xunit" />`.
- `tests/Em.Libs.Tests/` (`Em.Libs.Tests.csproj`, `Crc32Tests.cs`): check value CRC-32 `"123456789"` = `0xCBF43926`, teks kosong/null = 0, teks vs byte UTF-8 sama.
- `tests/Em.Api.Core.Tests/` (`Em.Api.Core.Tests.csproj`, `BinaryStorageKeyTests.cs`): Normalize, penolakan kunci berbahaya, Combine, IsValid.
- `tests/Em.Api.Core.IntegrationTests/` (`.csproj`, `SqlServerDatabase.cs`, `ApiCoreContextTests.cs`): `SqlServerDatabase` adalah `[assembly: AssemblyFixture]` dengan `IAsyncLifetime`; `ApiCoreContextTests` memanggil `EnsureCreatedAsync` pada `ApiCoreContext` lalu menghitung `ta_Users` dan `ta_Metas`.
- `tests/Em.Ui.Core.Tests/Em.Ui.Core.Tests.csproj` (net10.0, ref `Em.Ui.Core`).

Belum ditulis:

1. `tests/Em.Ui.Core.Tests/ThemeTests.cs`: `TheoryData<ThemeBase, ThemeVariant>` berisi `LightTheme`/`ThemeVariant.Light` dan `DarkTheme`/`ThemeVariant.Dark` (namespace `Em.Ui.Core.Shared`). Test 1: `theme.Variant` sesuai. Test 2: `Primary != OnPrimary`, `PrimaryContainer != OnPrimaryContainer`, `Secondary != OnSecondary`, `SecondaryContainer != OnSecondaryContainer` (`ThemeColor` adalah `readonly record struct`).
2. `tests/Em.Ui.Wpf.Core.Tests/Em.Ui.Wpf.Core.Tests.csproj`: `net10.0-windows`, `UseWPF=true`, paket `Xunit.StaFact` 4.0.24 (mendukung xunit.v3 4.x), ref `..\..\src\shared\Em.Ui.Wpf.Core\Em.Ui.Wpf.Core.csproj`.
3. `tests/Em.Ui.Wpf.Core.Tests/SharedStylesTests.cs`: `[WpfFact]` yang memuat `ResourceDictionary` dari `pack://application:,,,/<AssemblyName Em.Ui.Wpf.Core>;component/Styles/MaterialDesign.xaml` dan memastikan key `surfaceListBoxStyle` dan `surfaceTreeViewStyle` ada (keduanya didefinisikan di `Styles/Collections.xaml`). Sebelum membuat `Uri` pack, sentuh `System.IO.Packaging.PackUriHelper.UriSchemePack` agar skema `pack` terdaftar. Periksa `AssemblyName` di csproj Em.Ui.Wpf.Core. Bila dictionary butuh resource tema (StaticResource) yang hanya ada setelah `ThemeResources.Apply` (internal, dipanggil via reflection di harness lama), terapkan tema dulu dengan cara yang sama atau ubah test menjadi memuat `Styles/Collections.xaml` saja; catat pilihannya di laporan.
4. Tambah ke solution:
   - `src/backend/Em.Api.slnx`: folder `/tests/` berisi `../../tests/Em.Libs.Tests/Em.Libs.Tests.csproj`, `../../tests/Em.Api.Core.Tests/Em.Api.Core.Tests.csproj`, `../../tests/Em.Api.Core.IntegrationTests/Em.Api.Core.IntegrationTests.csproj`.
   - `src/frontend/Em.Ui.Wpf.slnx`: folder `/tests/` berisi `../../tests/Em.Ui.Core.Tests/Em.Ui.Core.Tests.csproj`, `../../tests/Em.Ui.Wpf.Core.Tests/Em.Ui.Wpf.Core.Tests.csproj`.
   - Tambahkan juga `tests/Directory.Build.props` sebagai `<File>` di folder `/tests/` kedua solution.
5. `tests/README.md` singkat (bahasa Indonesia): struktur project, cara menjalankan (`dotnet test src/backend/Em.Api.slnx`, `dotnet test src/frontend/Em.Ui.Wpf.slnx`, Test Explorer), syarat test integrasi (SQL Server lokal, Windows Authentication, hak `dbcreator`, env `EM_TEST_DB_SERVER`, contoh `(localdb)\MSSQLLocalDB`), dan bahwa harness sekali pakai tidak ditaruh di sini (lihat aturan "Alat uji sementara" di `claude.md`).
6. `claude.md`: tambahkan paragraf "Pembaruan 2026-10-04 (project test)" di bagian status repo: lokasi `tests/`, xUnit v3, cara menjalankan, syarat DB integrasi. Pertahankan isi lain.

## Verifikasi

1. `dotnet build src/backend/Em.Api.slnx` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx` lulus tanpa warning baru.
2. `dotnet test src/backend/Em.Api.slnx` dan `dotnet test src/frontend/Em.Ui.Wpf.slnx`.
   - Risiko: xunit.v3 4.x memakai **MTP v2**. Di .NET 10 SDK, `dotnet test` mode VSTest mungkin menolak project MTP v2. Bila itu terjadi, tambahkan `global.json` di root berisi `{ "test": { "runner": "Microsoft.Testing.Platform" } }` (cek dokumentasi xUnit "Microsoft Testing Platform support in xUnit.net v3" untuk bentuk yang benar), lalu catat di README tests dan laporan. Jangan turunkan versi xUnit tanpa mencatat alasannya.
   - Test integrasi: harus lulus bila SQL Server lokal tersedia, atau ter-skip dengan alasan yang jelas bila tidak. Pastikan tidak ada database `EmSystem_IntegrationTest_*` yang tertinggal setelah run (`SELECT name FROM sys.databases WHERE name LIKE 'EmSystem_IntegrationTest_%'`).
3. Test Explorer Visual Studio tidak bisa diuji agent; catat sebagai verifikasi manual tertunda.

## Penutup

Pindahkan berkas ini ke `plan/executed/` dan tulis ringkasan hasil (apa yang lulus, apa yang di-skip, verifikasi tertunda) di akhir berkas sebelum commit.

---

## Catatan sesi sebelumnya (bukan bagian plan, jangan dieksekusi)

Hal yang masih menunggu keputusan pengguna saat context dibersihkan:

- `main` 4 commit di depan `origin/main` (belum di-push): `7ef81b3`, `03092b8`, `1fc2b59`, `82a2f13`.
- 15 folder `scripts/*-smoke` dan `scripts/*-render` sudah dihapus pengguna di working tree tetapi **belum di-commit**, dan tidak ada salinannya di `..\.artefacts\em-system\scripts\`. Pilihan: (A) pulihkan dari git ke `..\.artefacts\em-system\scripts\`, sesuaikan `ProjectReference`, commit penghapusan, perbarui kalimat terakhir aturan "Alat uji sementara" di `claude.md`; (B) commit penghapusan saja dan perbarui kalimat itu.
- `src/frontend/Launcher/CLAUDE.md` baris 75 dan 210 serta komentar `src/frontend/Em.Ui.Wpf/Em.Ui.Wpf.csproj` baris 25 masih menyuruh meng-commit `dist/launcher/launcher.exe`, padahal kini diabaikan Git.
- `doc/konvensi/konvensi-penamaan-nuget.md` masih draf dengan 4 pertanyaan terbuka; default `scripts/pack-nuget.ps1` (`0.1.0-pre-alpha.1`) menunggu keputusan itu.
- Script `.cmd` publish NuGet ke GitHub Packages (default hanya pack, push hanya dengan argumen eksplisit dan PAT dari env) belum dibuat; pengguna minta jangan push paket selama masih tes.
- Lisensi `Oracle.EntityFrameworkCore` yang terbawa `EFCore.BulkExtensions` 10.0.1 belum dicek.

---

## Laporan eksekusi (2026-10-04)

Dikerjakan:

- `ThemeTests.cs` ternyata sudah ada dari sesi sebelumnya dan sesuai plan; tidak ditulis ulang.
- `tests/Em.Ui.Wpf.Core.Tests/` (csproj + `SharedStylesTests.cs`): `[WpfTheory]` memuat `Styles/MaterialDesign.xaml` lewat pack URI dan memastikan `surfaceListBoxStyle` dan `surfaceTreeViewStyle` adalah `Style`. Dictionary dimuat **tanpa menerapkan tema**: token tema dibaca lewat `DynamicResource`, sedangkan `StaticResource` hanya merujuk key `Palette.xaml`, jadi `ThemeResources.Apply` tidak diperlukan.
- Penyimpangan dari plan: menyentuh `PackUriHelper.UriSchemePack` tidak cukup (`NotSupportedException: The URI prefix is not recognized`). Handler `pack://` untuk `ResourceDictionary.Source` didaftarkan oleh static constructor `System.Windows.Application`; test memanggil `RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle)` tanpa membuat instance `Application`.
- Kedua solution mendapat folder `/tests/` beserta `tests/Directory.Build.props` sebagai `<File>`.
- Risiko MTP v2 terjadi: `dotnet test` menolak dengan `Testing with VSTest target is no longer supported...`. Ditambahkan `global.json` di root berisi `{ "test": { "runner": "Microsoft.Testing.Platform" } }` (bentuk sesuai dokumentasi Microsoft Learn "Migrate from MTP v1 to v2"). Versi xUnit tidak diturunkan. Dicatat di `tests/README.md` dan `claude.md`.
- `tests/README.md` dan paragraf "Pembaruan 2026-10-04 (project test)" di `claude.md`.

Hasil verifikasi:

- `dotnet build src/backend/Em.Api.slnx` dan `dotnet build src/frontend/Em.Ui.Wpf.slnx`: lulus, 0 warning, 0 error.
- `dotnet test src/backend/Em.Api.slnx`: 19 lulus, 0 gagal, 0 skip. Test integrasi benar-benar berjalan terhadap SQL Server lokal `(local)` (tidak di-skip).
- `dotnet test src/frontend/Em.Ui.Wpf.slnx`: 6 lulus, 0 gagal, 0 skip.
- Setelah run, `SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'EmSystem_IntegrationTest_%'` = 0.

Verifikasi manual tertunda:

- Test Explorer Visual Studio dan Rider belum diuji agent.
- Jalur skip test integrasi (server tidak terjangkau) belum diuji karena SQL Server lokal tersedia.
