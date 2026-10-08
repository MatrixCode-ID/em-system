# Build and packaging

Build, test and packaging notes for the engine repository. Comments in `.csproj`, `Directory.Build.props`,
`scripts/pack-nuget/packages.txt` and the workflows point here instead of repeating the details.

## Solutions {#solutions}

| Solution | Contents |
| --- | --- |
| `src/backend/Em.Api.slnx` | `Em.Libs`, `Em.Api.Core`, the `Em.Api` host, the Em.Test API module, backend tests, Compose project |
| `src/frontend/Em.Ui.Wpf.slnx` | `Em.Libs`, `Em.Ui.Core`, `Em.Ui.Wpf.Core`, the `Em.Ui.Wpf` host, the Em.Test WPF module, UI tests |
| `src/frontend/Em.Ui.Maui.slnx` | `Em.Libs`, `Em.Ui.Core`, `Em.Ui.Maui.Core`, the `Em.Ui.Maui` host (Android only) |

```powershell
dotnet build src/backend/Em.Api.slnx
dotnet build src/frontend/Em.Ui.Wpf.slnx
dotnet build src/frontend/Em.Ui.Maui.slnx   # needs the maui-android workload
```

## Tests {#tests}

Test projects live in `tests/` and use xUnit v3 on Microsoft Testing Platform v2. The root `global.json`
selects the `Microsoft.Testing.Platform` runner; without it the .NET 10 SDK rejects MTP v2 projects, so every
new test project must be MTP based.

```powershell
dotnet test src/backend/Em.Api.slnx
dotnet test src/frontend/Em.Ui.Wpf.slnx
```

Integration tests need a local SQL Server; see [tests/README.md](../../tests/README.md).

## Local artefacts folder {#artefacts-path}

Machine-local files that must not enter the public repository (`emapi-config.json`, debug token keys) live in
`$(ArtefactsPath)`, by default the sibling folder `..\.artefacts\em-system\`. Override it with an environment
variable or `dotnet build -p:ArtefactsPath=<path>`. The value always ends with a directory separator.

## Package IDs {#package-id}

Assemblies and namespaces are named `Em.*`; package IDs use the reserved nuget.org prefix `EmSys.`.
`Directory.Build.props` derives `PackageId` by replacing the leading `Em.` of the project name. Host projects
(`Em.Api`, `Em.Ui.Wpf`, `Em.Ui.Maui`) and test projects set `IsPackable=false`.

## Packaged libraries {#packages}

`scripts/pack-nuget/packages.txt` lists the projects released together under one version, one path per line
relative to the repository root (`#` starts a comment). It is read by `pack-nuget.ps1`, `release-nuget.ps1` and
the `validate`/`publish` jobs of `.github/workflows/publish-nuget.yml`. Every listed package needs
`doc/ReleaseNote/<PackageId>/<version>.md` when released.

`EmSys.Ui.Maui.Core` targets `net10.0-android` and can only be packed with the `maui-android` workload
installed; the `publish` job installs it before packing.

## Package metadata {#package-metadata}

- **Icon:** `doc/assets/logo/Logo-128.png`, packed as `icon.png` (a 128x128 copy of `Logo.png`).
- **Readme:** a `README.md` in the project folder is packed automatically when present.
- **Release notes:** `pack-nuget.ps1` writes the `## Summary` section of
  `doc/ReleaseNote/<PackageId>/<version>.md` (plus a link) to a file passed as `EmReleaseNotesFile`, which
  becomes `PackageReleaseNotes`.

## Product version {#product-version}

A host shows the version baked into its entry assembly (`AssemblyInformationalVersion`). The version is
set in MSBuild, by hand, and release automation overrides it with `-p:Version`:

- **em-system** keeps the engine version in `Directory.Build.props` (`<Version>`), equal to the newest
  `doc/ReleaseNote` version, so every engine assembly and both hosts carry it. Raise it in the same change
  that adds a new release note. `ci.yml` passes the newest release note version to every build
  (`Set-CiVersion` in `scripts/release-nuget/release-common.ps1`, which warns when `Directory.Build.props`
  lags behind), and `pack-nuget.ps1` packs with `-p:Version` as well as `-p:PackageVersion`.
- **A product** sets `<Version>` in its own host projects or `Directory.Build.props`, and its release
  pipeline passes the release version. Without `<Version>` the SDK default `1.0.0` is shown.
  `0.0.0-dev` is shown as `dev`.
- **The WPF publisher** passes a container version tag itself: in **Dockerfile** mode it adds the build
  argument `APP_VERSION=<version tag>` (unless the profile already sets `APP_VERSION`), in **Compose**
  mode it passes the same argument per version tag, and in **Template** mode it adds `-p:Version` to
  `dotnet publish`. Floating or free tags that are not `MAJOR.MINOR.PATCH[-prerelease]` (such as
  `latest`) pass nothing. A Dockerfile that builds inside the image takes the argument as an optional
  override, so a build without it keeps the project version:

  ```dockerfile
  ARG APP_VERSION=
  RUN dotnet publish ... ${APP_VERSION:+"/p:Version=$APP_VERSION"}
  ```

  `src/backend/Em.Api/Dockerfile` is the reference.
- At run time `Em.Shared.AppVersion` reads the informational version of the entry assembly.
  `AppVersion.Display` gives `v1.3.0-alpha.1`, or `dev` for `0.0.0-dev`; build metadata (`+commit`) is
  stripped for display. The WPF main window shows it in the status bar.

### Status bar version slot

`TabbedMainWindow` draws the status bar from `EmApp.MainWindow.Vm.StatusBar` (`MainStatusBarVm`). Its
`SystemItems` hold the engine's fixed slots against the right edge; modules cannot change them and add their
own items to `LeftItems`/`RightItems`. The first system slot is the product version (`StatusBar.Version`): the
short text in the bar, the full informational version in the tooltip, and **Copy version** on right-click.

## XML documentation {#xml-docs}

The five packaged libraries set `GenerateDocumentationFile`, so each package ships
`lib/<tfm>/<Assembly>.xml` for IntelliSense. Warning CS1591 is not suppressed: every new public or protected
member needs an English XML comment.

## Resizetizer pin {#resizetizer-pin}

`Directory.Build.props` pins `Microsoft.Maui.Resizetizer` to 10.0.100 for MAUI projects. Versions 10.0.101 and
10.0.110 conflict with Svg.Skia over `System.Memory` when processing SVGs that contain `<filter>` or `<text>`
(error MAUIR0001, `MissingMethodException` on `SKImageFilter.CreateMatrixConvolution` and similar). Resizetizer
is a build-time tool only, so pinning it apart from `$(MauiVersion)` is safe. Remove the pin once a fixed MAUI
release is available.
