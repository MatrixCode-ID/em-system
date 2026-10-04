param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version = '0.1.0-pre-alpha.1'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputDir = Join-Path $repoRoot 'dist/nuget-pack'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$projects = @(
    'src/shared/Em.Libs/Em.Libs.csproj'
    'src/backend/Em.Api.Core/Em.Api.Core.csproj'
    'src/shared/Em.Ui.Core/Em.Ui.Core.csproj'
    'src/shared/Em.Ui.Wpf.Core/Em.Ui.Wpf.Core.csproj'
    'src/shared/Em.Ui.Maui.Core/Em.Ui.Maui.Core.csproj'
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    & dotnet pack $projectPath --configuration Release --output $outputDir "-p:PackageVersion=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project (exit code $LASTEXITCODE)."
    }

    $packageId = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $packagePath = Join-Path $outputDir "$packageId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Expected package not found: $packagePath"
    }
}

Get-ChildItem -LiteralPath $outputDir -Filter "*.$Version.nupkg" -File |
    Sort-Object Name |
    Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='Package';Expression={[System.IO.Path]::GetFileName($_.Path)}}, Hash
