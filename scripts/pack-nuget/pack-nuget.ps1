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

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    & dotnet pack $projectPath --configuration Release --output $outputDir "-p:PackageVersion=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project (exit code $LASTEXITCODE)."
    }

    $packageId = [System.IO.Path]::GetFileNameWithoutExtension($projectPath) -replace '^Em\.', 'EmSys.'
    $packagePath = Join-Path $outputDir "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Expected package not found: $packagePath"
    }
}

Get-ChildItem -LiteralPath $outputDir -Filter "*.$Version.nupkg" -File |
    Sort-Object Name |
    Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='Package';Expression={[System.IO.Path]::GetFileName($_.Path)}}, Hash
