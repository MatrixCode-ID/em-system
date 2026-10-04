#Requires -Version 7
<#
.SYNOPSIS
   Builds launcher.exe in release mode and copies it to dist/launcher/ at the repository root.

.DESCRIPTION
   Loads the environment of a Visual Studio that has the x64 MSVC build tools (found through vswhere),
   runs `cargo build --release` with the absolute paths of this machine remapped out of the exe, copies
   target/release/launcher.exe to dist/launcher/launcher.exe, and prints its size and SHA-256.

   dist/launcher/launcher.exe is committed and shipped with every release of the desktop client, so run
   this script and commit its output whenever the launcher source changes.
#>

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $projectDir '..\..\..')).Path
$distDir = Join-Path $repoRoot 'dist\launcher'

# Rust picks the newest Visual Studio on its own, which may have link.exe without the x64 libraries
# (LNK1104: msvcrt.lib). Only an installation with the x64 tools component will do.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found at $vswhere." }
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
   Select-Object -First 1
if (-not $vsPath) { throw 'No Visual Studio with the x64 MSVC build tools (VC.Tools.x86.x64) was found.' }
$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
Write-Host "Using $vcvars"

# vcvars64.bat only changes the environment of its own cmd; take that environment over into this process. The
# process is the caller's shell, so the original environment is put back at the end: a leftover Platform=x64 alone
# breaks every later `dotnet build` of the solutions.
$environment = & cmd.exe /d /c "`"$vcvars`" >nul 2>&1 && set"
if ($LASTEXITCODE -ne 0) { throw "vcvars64.bat failed with exit code $LASTEXITCODE." }
$savedEnvironment = [Environment]::GetEnvironmentVariables()
foreach ($line in $environment) {
   if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2]) }
}

# Keep the machine's paths out of the exe (panic locations of the crates carry them). Cargo config files
# cannot hold variables, so the remapping is passed here; `--config` arrays are merged with the one in
# .cargo/config.toml. TOML literal strings ('...') keep the backslashes as they are.
$cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
# A shell opened before rustup was installed does not have cargo on its PATH yet.
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { $env:PATH = "$(Join-Path $cargoHome 'bin');$env:PATH" }
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { throw 'cargo was not found. Install Rust through rustup first.' }
$remaps = @(
   "--remap-path-prefix=$cargoHome=cargo",
   "--remap-path-prefix=$projectDir=launcher"
) | ForEach-Object { "'$_'" }
$rustflags = "target.x86_64-pc-windows-msvc.rustflags=[$($remaps -join ', ')]"

Push-Location $projectDir
try {
   & cargo build --release --config $rustflags
   if ($LASTEXITCODE -ne 0) { throw "cargo build failed with exit code $LASTEXITCODE." }
}
finally {
   Pop-Location
   foreach ($name in [Environment]::GetEnvironmentVariables().Keys) {
      if (-not $savedEnvironment.Contains($name)) { [Environment]::SetEnvironmentVariable($name, $null) }
   }
   foreach ($entry in $savedEnvironment.GetEnumerator()) {
      [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
   }
}

$built = Join-Path $projectDir 'target\release\launcher.exe'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$target = Join-Path $distDir 'launcher.exe'
Copy-Item -Path $built -Destination $target -Force

$file = Get-Item $target
$hash = (Get-FileHash -Algorithm SHA256 -Path $target).Hash.ToLowerInvariant()
Write-Host ''
Write-Host "Copied to $target"
Write-Host ("Size:    {0:N0} bytes" -f $file.Length)
Write-Host "SHA-256: $hash"
