# Em System

Em System is an ERP foundation developed by **Matrix Code**. This repository contains the shared engine,
the API host, a WPF desktop host and a .NET MAUI (Android) host.

The project is under active development (alpha). The code is released under the [MIT](LICENSE) license,
and the engine libraries are published to nuget.org as `EmSys.*` prerelease packages.

## Project layout

| Location | Contents |
| --- | --- |
| `src/backend/Em.Api` | HTTP server host |
| `src/backend/Em.Api.Core` | API engine and core services |
| `src/shared/Em.Libs` | Shared contracts, types and utilities |
| `src/shared/Em.Ui.Core` | Shared UI engine (themes, branding, UI models) |
| `src/shared/Em.Ui.Wpf.Core` | WPF UI engine |
| `src/shared/Em.Ui.Maui.Core` | .NET MAUI UI engine |
| `src/frontend/Em.Ui.Wpf` | Desktop application host |
| `src/frontend/Em.Ui.Maui` | Android application host |
| `src/frontend/Launcher` | Rust-based desktop installer and updater |
| `src/modules/Em.Test` | Sample/test module that exercises most engine features |
| `tests/` | Automated tests ([tests/README.md](tests/README.md)) |
| `doc/` | Engine guides, conventions, SQL scripts and release notes |

Each host has its own solution: `src/backend/Em.Api.slnx`, `src/frontend/Em.Ui.Wpf.slnx` and
`src/frontend/Em.Ui.Maui.slnx`. There is no solution at the repository root.

## Requirements

- .NET SDK 10 for every .NET project.
- Windows to build and run the WPF host.
- The Android/.NET MAUI workload and the Android SDK for the MAUI host.
- To build the Launcher: Rust, PowerShell 7, and Visual Studio Build Tools with the MSVC x64 toolchain.

## Build and test

From the repository root:

```powershell
dotnet build src/backend/Em.Api.slnx
dotnet build src/frontend/Em.Ui.Wpf.slnx
dotnet build src/frontend/Em.Ui.Maui.slnx

dotnet test src/backend/Em.Api.slnx
dotnet test src/frontend/Em.Ui.Wpf.slnx
```

## Configure and run the API

1. Create the database by running the SQL Server scripts in `doc/sqlscript/mssql/` in order: `sets/`, then
   `tables/` by number, then `views/`. The `updates/` folder only migrates older databases. Scripts are
   provided for SQL Server only; `900-emtest.sql` is needed only for the sample test module.
2. Copy [`emapi-config.example.json`](src/backend/Em.Api/emapi-config.example.json) to
   `..\.artefacts\em-system\config\emapi-config.json` (a folder next to the repository; change it with the
   MSBuild property `ArtefactsPath`), then fill in `database.connectionString` and
   `admin.initialPassword`. `database.provider` can be `MicrosoftSqlServer`, `MySql` or `PostgreSql`.
   `debugTokens` is optional and holds RSA **public** keys for debug access, never private keys.
3. Run the host:

   ```powershell
   dotnet run --project src/backend/Em.Api/Em.Api.csproj
   ```

The configuration file lives outside the repository, is copied to the build output, and is not included
in `dotnet publish`. The environment variables `EM_DB_CONNECTION_STRING`, `EM_DB_PROVIDER`,
`EM_ADMIN_INITIAL_PASSWORD` and `EM_DEBUG_TOKEN` override the file, and `EM_API_CONFIG` can point to a file
elsewhere. The sample test module is off unless `"modules": { "test": true }` or `EM_MODULE_TEST=true` is set. In deployments, supply secrets through the server's secret manager.

More details, including the container image and Docker Compose: [src/backend/README.md](src/backend/README.md).

## Frontend and Launcher

The WPF and MAUI hosts use the shared UI engine without business modules. See
[src/frontend/README.md](src/frontend/README.md). To build the Launcher that ships with the WPF output:

```powershell
pwsh -File src/frontend/Launcher/build-dist.ps1
```

The Launcher's product identity is in `src/frontend/Launcher/product.toml`.

## NuGet packages

The five engine libraries are published together with one version. Project, assembly and namespace names
stay `Em.*`; package IDs use the `EmSys.` prefix.

| Package | Contents |
| --- | --- |
| `EmSys.Libs` | Shared API and UI contracts |
| `EmSys.Api.Core` | Backend engine |
| `EmSys.Ui.Core` | Cross-platform UI foundation: themes, branding, UI models |
| `EmSys.Ui.Wpf.Core` | WPF UI engine |
| `EmSys.Ui.Maui.Core` | .NET MAUI UI engine |

```xml
<PackageReference Include="EmSys.Api.Core" Version="0.1.0-alpha.2" />
```

MAUI consumers also need the MAUI workload and a reference to `Microsoft.Maui.Controls` in their app
project. Versioning and feeds: [NuGet package naming](doc/convention/nuget-naming.md).

### Releasing

Releases are automated. Merging a commit to `main` that adds release notes for a new version
(`doc/ReleaseNote/<PackageId>/<version>.md` for every package in `scripts/pack-nuget/packages.txt`) runs the
`publish-nuget.yml` workflow. The workflow publishes that version to nuget.org and creates the `v<version>`
tag and GitHub Release. Release note format: [doc/ReleaseNote/README.md](doc/ReleaseNote/README.md).
`scripts\release-nuget.cmd` is a manual fallback.

### Local packages

`scripts\pack-nuget.cmd` (or `pwsh -File scripts/pack-nuget/pack-nuget.ps1 -Version <version>`) packs every
library listed in `scripts/pack-nuget/packages.txt` into `dist/nuget-pack`. Register that folder as a local
source with the path of your own clone:

```powershell
dotnet nuget add source "<repo>\dist\nuget-pack" --name EmLocal
```

`scripts\upload-nuget.cmd -Version <version>` packs, then asks `Push ke ... ? [y/N]` before pushing to
GitHub Packages (`https://nuget.pkg.github.com/MatrixCode-ID/index.json`). Any answer other than `y`/`yes`
stops after packing. The GitHub classic PAT (`write:packages`, `read:packages`) is read from `EM_NUGET_PAT`,
or asked for through a hidden prompt; it is never written to a log or file. Prealpha versions are never
pushed to a public feed.

The scripts need `pwsh` (PowerShell 7+) on the `PATH`.

## Documentation

- [Engine guides](doc/engine/README.md): approval, CDN, container registry, NuGet server, storage settings,
  robots, publishing, Release Manager, login branding, and the test module.
- Conventions: [Dahlia Convention: database and C# model naming](doc/convention/dahlia-convention.md),
  [container image naming](doc/convention/container-naming.md),
  [NuGet package naming](doc/convention/nuget-naming.md).
- [Desktop release format](doc/release-format.md).
