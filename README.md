# Em System

Em System adalah fondasi ERP yang dikembangkan oleh **Matrix Code**. Repo ini memisahkan engine bersama, host API, serta host desktop WPF dan mobile .NET MAUI.

Proyek masih dalam pengembangan. Kode Em System dirilis dengan lisensi [MIT](LICENSE); paket NuGet belum diterbitkan ke nuget.org.

## Struktur proyek

| Lokasi | Isi |
| --- | --- |
| `src/backend/Em.Api` | Host server HTTP |
| `src/backend/Em.Api.Core` | Engine dan layanan inti API |
| `src/shared/Em.Libs` | Tipe dan utilitas bersama |
| `src/shared/Em.Ui.Core` | Engine UI bersama |
| `src/shared/Em.Ui.Wpf.Core` | Komponen UI WPF |
| `src/shared/Em.Ui.Maui.Core` | Komponen UI .NET MAUI |
| `src/frontend/Em.Ui.Wpf` | Host aplikasi desktop |
| `src/frontend/Em.Ui.Maui` | Host aplikasi Android |
| `src/frontend/Launcher` | Installer dan updater desktop berbasis Rust |

Setiap host memiliki solution sendiri: `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx`, dan `src/frontend/Em.Ui.Maui.slnx`. Solution di root tidak digunakan.

## Persyaratan

- .NET SDK 10 untuk semua proyek .NET.
- Windows untuk membangun dan menjalankan host WPF.
- Workload Android/.NET MAUI beserta Android SDK untuk host MAUI.
- Untuk membangun Launcher: Rust, PowerShell 7, dan Visual Studio Build Tools dengan toolchain MSVC x64.

## Build

Jalankan dari root repo:

```powershell
dotnet build src/backend/Em.Api.slnx
dotnet build src/frontend/Em.Ui.Wpf.slnx
dotnet build src/frontend/Em.Ui.Maui.slnx
```

## Konfigurasi dan menjalankan API

Salin [`emapi-config.example.json`](src/backend/Em.Api/emapi-config.example.json) ke `..\.artefacts\em-system\config\emapi-config.json` (folder di sebelah repo, dapat diganti lewat properti MSBuild `ArtefactsPath`), lalu isi `database.connectionString` dan `admin.initialPassword`. `database.provider` dapat bernilai `MicrosoftSqlServer`, `MySql`, atau `PostgreSql`. `debugTokens` opsional dan berisi public key RSA untuk akses debug, bukan private key.

Berkas itu berada di luar repo, disalin ke output build, dan tidak ikut `dotnet publish`. Environment variable `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`, `EM_ADMIN_INITIAL_PASSWORD`, dan `EM_DEBUG_TOKEN` mengesampingkan nilai berkas; `EM_API_CONFIG` dapat menunjuk berkas di lokasi lain. Untuk deployment, berikan rahasia melalui secret manager lingkungan server.

```powershell
dotnet run --project src/backend/Em.Api/Em.Api.csproj
```

Panduan lebih lanjut tersedia di [`src/backend/README.md`](src/backend/README.md).

## Frontend dan Launcher

Host WPF dan MAUI memakai engine UI bersama tanpa modul bisnis dari proyek asal. Lihat [`src/frontend/README.md`](src/frontend/README.md) untuk rincian frontend. Untuk menghasilkan Launcher yang disertakan dalam output WPF:

```powershell
pwsh -File src/frontend/Launcher/build-dist.ps1
```

Identitas produk Launcher berada di `src/frontend/Launcher/product.toml`.

## Paket library

Ketiga solution dapat di-`dotnet pack` secara lokal; project host diberi `IsPackable=false`, sehingga hanya library `Em.*` yang menghasilkan paket. Lima library memiliki deskripsi, README paket, dan metadata lisensi MIT. Periksa isi paket dan dependensi sebelum publikasi NuGet.

Untuk membuat paket library yang terdaftar di `scripts/pack-nuget/packages.txt` (PackageId `EmSys.*`; saat ini tanpa `EmSys.Ui.Maui.Core` sampai CI MAUI aktif lagi) dengan satu versi di `dist/nuget-pack`, jalankan `scripts\pack-nuget.cmd` atau:

```powershell
pwsh -File scripts/pack-nuget/pack-nuget.ps1 -Version 0.1.0-pre-alpha.1
```

Folder tersebut dapat didaftarkan sebagai sumber NuGet lokal pada mesin yang sudah meng-clone repo:

```powershell
dotnet nuget add source "E:\em-system\dist\nuget-pack" --name EmLocal
```

Gunakan path folder clone pada mesin masing-masing. Konsumen MAUI tetap perlu memasang workload MAUI dan mereferensikan `Microsoft.Maui.Controls` di project aplikasinya.

Paket lokal ini belum merupakan rilis publik. Uji pemasangan paket dan periksa hasil build sebelum mengunggahnya.

Untuk pack lalu push ke feed GitHub Packages (`https://nuget.pkg.github.com/MatrixCode-ID/index.json`, lihat [konvensi-penamaan-nuget.md](doc/konvensi/konvensi-penamaan-nuget.md)), jalankan:

```cmd
scripts\upload-nuget.cmd -Version 0.1.0-pre-alpha.1
```

Skrip selalu pack dulu, lalu tanya konfirmasi `Push ke ... ? [y/N]` sebelum benar-benar push; jawab apa pun selain `y`/`yes` untuk berhenti di pack saja. Kalau dikonfirmasi dan environment variable `EM_NUGET_PAT` belum diset, skrip minta PAT classic GitHub (scope `write:packages`, `read:packages`) lewat prompt tersembunyi — tidak pernah ditulis ke log atau file. Set `EM_NUGET_PAT` lebih dulu untuk melewati prompt itu.

Skrip memerlukan `pwsh` (PowerShell 7+) di PATH; `pack-nuget.ps1` lama memakai `Get-FileHash` yang tidak selalu termuat di Windows PowerShell 5.1 bila dijalankan lewat `cmd.exe`.
