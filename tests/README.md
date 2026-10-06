# Tests

Automated tests for the `Em.*` engine, using **xUnit v3** (`xunit.v3` on Microsoft Testing Platform v2).
Shared settings live in [Directory.Build.props](Directory.Build.props): test packages, `OutputType=Exe`,
`IsPackable=false` and a global `using Xunit`.

| Project | Tests | Solution |
| --- | --- | --- |
| `Em.Libs.Tests` | `Em.Libs` (for example `Crc32`) | `src/backend/Em.Api.slnx` |
| `Em.Api.Core.Tests` | `Em.Api.Core` without a database (for example `BinaryStorageKey`) | `src/backend/Em.Api.slnx` |
| `Em.Api.Core.IntegrationTests` | `Em.Api.Core` against SQL Server | `src/backend/Em.Api.slnx` |
| `Em.Ui.Core.Tests` | `Em.Ui.Core` (themes) | `src/frontend/Em.Ui.Wpf.slnx` |
| `Em.Ui.Wpf.Core.Tests` | `Em.Ui.Wpf.Core` shared style resources (`[WpfFact]`/`[WpfTheory]` from `Xunit.StaFact`) | `src/frontend/Em.Ui.Wpf.slnx` |

Naming: `<Project>.Tests` for unit tests, `<Project>.IntegrationTests` for tests that need an external
service.

## Running

```powershell
dotnet test src/backend/Em.Api.slnx
dotnet test src/frontend/Em.Ui.Wpf.slnx
```

You can also use Test Explorer in Visual Studio or Rider after opening one of these solutions, or run a
single project directly as an executable (`dotnet run --project tests/Em.Libs.Tests`).

The root `global.json` selects the `Microsoft.Testing.Platform` runner for `dotnet test`. This is required:
the .NET 10 SDK rejects MTP v2 projects in VSTest mode (`Testing with VSTest target is no longer
supported...`). Every test project in this repository must therefore use MTP; do not add VSTest-only
projects (such as xUnit v2).

## Integration tests

`Em.Api.Core.IntegrationTests` creates a temporary database `EmSystem_IntegrationTest_<guid>` once per run
and drops it at the end.

- The default server is `(local)` with **Windows Authentication** (no password). The Windows account
  running the tests needs permission to create databases (`dbcreator`).
- Use another server through the `EM_TEST_DB_SERVER` environment variable, for example `.\SQLEXPRESS` or
  `(localdb)\MSSQLLocalDB`:

  ```powershell
  $env:EM_TEST_DB_SERVER = '(localdb)\MSSQLLocalDB'
  dotnet test src/backend/Em.Api.slnx
  ```

- If the server cannot be reached, the integration tests are skipped with a reason instead of failing.
- A run that is killed can leave its database behind. Find leftovers with
  `SELECT name FROM sys.databases WHERE name LIKE 'EmSystem_IntegrationTest_%'` and drop them manually.

## What does not belong here

This folder is only for tests that are maintained and run repeatedly. One-off verification harnesses
(database smoke tests, WPF screen renders to PNG, single-use HTTP scripts) are kept outside the repository
in `..\.artefacts\em-system\scripts\`. HTTP test scripts that are maintained live in `scripts/_py/`.
