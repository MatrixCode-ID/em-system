param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version = '0.1.0-pre-alpha.1'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$outputDir = Join-Path $repoRoot 'dist/nuget-pack'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$projects = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packages.txt') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
if (-not $projects) { throw 'packages.txt tidak berisi project.' }

$notesDir = Join-Path $outputDir '.release-notes'
New-Item -ItemType Directory -Path $notesDir -Force | Out-Null

# PackageReleaseNotes = section "## Ringkasan" of doc/ReleaseNote/<PackageId>/<version>.md plus a link to the full note.
function Get-ReleaseNotesFile([string]$packageId) {
    $notePath = Join-Path $repoRoot 'doc' 'ReleaseNote' $packageId "$Version.md"
    if (-not (Test-Path -LiteralPath $notePath -PathType Leaf)) { return $null }
    $summary = @()
    $inSummary = $false
    foreach ($line in Get-Content -LiteralPath $notePath) {
        if ($line -match '^##\s') { if ($inSummary) { break }; $inSummary = $line -match '^##\s+Ringkasan\s*$'; continue }
        if ($inSummary) { $summary += $line }
    }
    $text = ($summary -join "`n").Trim()
    $url = "https://github.com/MatrixCode-ID/em-system/blob/v$Version/doc/ReleaseNote/$packageId/$Version.md"
    $text = if ($text) { "$text`n`nRelease note lengkap: $url" } else { "Release note: $url" }
    $file = Join-Path $notesDir "$packageId.txt"
    [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding $false))
    return $file
}

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    $packageId = [System.IO.Path]::GetFileNameWithoutExtension($projectPath) -replace '^Em\.', 'EmSys.'
    $packArgs = @($projectPath, '--configuration', 'Release', '--output', $outputDir, "-p:PackageVersion=$Version")
    $notesFile = Get-ReleaseNotesFile $packageId
    if ($notesFile) { $packArgs += "-p:EmReleaseNotesFile=$notesFile" }
    & dotnet pack @packArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project (exit code $LASTEXITCODE)."
    }

    $packagePath = Join-Path $outputDir "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Expected package not found: $packagePath"
    }
}

Get-ChildItem -LiteralPath $outputDir -Filter "*.$Version.nupkg" -File |
    Sort-Object Name |
    Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='Package';Expression={[System.IO.Path]::GetFileName($_.Path)}}, Hash
