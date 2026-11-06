# Remove only the retired 3307 instance data. Run manually after review.
# Impact: permanently deletes all databases in that retired data directory.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
$parent = [IO.Path]::GetFullPath((Join-Path $repo '..'))
$expected = [IO.Path]::GetFullPath((Join-Path $parent '.artefacts/mysql-em-case-data'))
if (-not (Test-Path -LiteralPath $expected -PathType Container)) { throw "Directory is missing: $expected" }
$resolved = (Resolve-Path -LiteralPath $expected).ProviderPath
if (-not [string]::Equals($resolved.TrimEnd('\'), $expected.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected resolved target: $resolved" }
if (Get-NetTCPConnection -State Listen -LocalPort 3307 -ErrorAction SilentlyContinue) { throw 'Port 3307 is listening; stop it before cleanup.' }
$running = Get-CimInstance Win32_Process -Filter "Name='mysqld.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($resolved, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
if ($running) { throw "A MySQL process still uses the directory: $($running.ProcessId -join ', ')" }
Write-Host "Deleting retired MySQL data directory: $resolved"
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Host 'Retired 3307 data directory removed.'
