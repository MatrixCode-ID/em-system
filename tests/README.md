# Test

Project test engine `Em.*`, memakai **xUnit v3** (`xunit.v3`, Microsoft Testing Platform v2). Pengaturan bersama ada di [Directory.Build.props](Directory.Build.props): paket test, `OutputType=Exe`, `IsPackable=false`, dan `using Xunit`.

| Project | Menguji | Solution |
|---|---|---|
| `Em.Libs.Tests` | `Em.Libs` (mis. `Crc32`) | `src/backend/Em.Api.slnx` |
| `Em.Api.Core.Tests` | `Em.Api.Core` tanpa database (mis. `BinaryStorageKey`) | `src/backend/Em.Api.slnx` |
| `Em.Api.Core.IntegrationTests` | `Em.Api.Core` terhadap SQL Server | `src/backend/Em.Api.slnx` |
| `Em.Ui.Core.Tests` | `Em.Ui.Core` (tema) | `src/frontend/Em.Ui.Wpf.slnx` |
| `Em.Ui.Wpf.Core.Tests` | `Em.Ui.Wpf.Core` (resource style bersama, `[WpfFact]`/`[WpfTheory]` dari `Xunit.StaFact`) | `src/frontend/Em.Ui.Wpf.slnx` |

Penamaan: `<Project>.Tests` untuk unit test, `<Project>.IntegrationTests` untuk test yang butuh layanan luar.

## Menjalankan

```powershell
dotnet test src/backend/Em.Api.slnx
dotnet test src/frontend/Em.Ui.Wpf.slnx
```

Atau lewat Test Explorer di Visual Studio / Rider setelah membuka salah satu solution di atas. Satu project bisa juga dijalankan langsung sebagai executable (`dotnet run --project tests/Em.Libs.Tests`).

`global.json` di root repo memilih runner `Microsoft.Testing.Platform` untuk `dotnet test`. Ini wajib: di .NET 10 SDK, project MTP v2 ditolak oleh mode VSTest (`Testing with VSTest target is no longer supported...`). Akibatnya semua project test di repo ini harus memakai MTP; jangan menambah project test VSTest-only (mis. xUnit v2).

## Test integrasi

`Em.Api.Core.IntegrationTests` membuat database sementara `EmSystem_IntegrationTest_<guid>` sekali per run lalu menghapusnya di akhir.

- Server default `(local)` dengan **Windows Authentication** (tanpa password). Akun Windows yang menjalankan test butuh hak membuat database (`dbcreator`).
- Ganti server lewat environment variable `EM_TEST_DB_SERVER`, mis. `.\SQLEXPRESS` atau `(localdb)\MSSQLLocalDB`:

  ```powershell
  $env:EM_TEST_DB_SERVER = '(localdb)\MSSQLLocalDB'
  dotnet test src/backend/Em.Api.slnx
  ```

- Bila server tidak terjangkau, test integrasi di-skip dengan alasannya, bukan gagal.
- Run yang terputus paksa bisa meninggalkan database. Periksa dengan `SELECT name FROM sys.databases WHERE name LIKE 'EmSystem_IntegrationTest_%'` lalu hapus manual.

## Yang tidak ditaruh di sini

Folder ini hanya untuk test yang dirawat dan dijalankan berulang. Harness verifikasi sekali pakai (smoke terhadap database, render layar WPF ke PNG, skrip uji HTTP) disimpan di luar repo pada `..\.artefacts\em-system\scripts\`; lihat aturan "Alat uji sementara" di [claude.md](../claude.md).
