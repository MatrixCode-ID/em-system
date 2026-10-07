<#
Packs the five EmSys.* library packages through scripts/pack-nuget/pack-nuget.ps1, then asks for
confirmation before pushing to the GitHub Packages NuGet feed (doc/convention/nuget-naming.md). The classic
GitHub PAT (scopes write:packages, read:packages) is taken in this order: the environment variable
EM_NUGET_PAT, then the file ..\.artefacts\em-system\github-pat.txt, then a hidden prompt when neither
exists. The PAT is never written to the log.
#>
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version = '0.1.0-pre-alpha.1',

    [string]$Source = 'https://nuget.pkg.github.com/MatrixCode-ID/index.json'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..' '..')).Path
$outputDir = Join-Path $repoRoot 'dist/nuget-pack'

& (Join-Path $repoRoot 'scripts' 'pack-nuget' 'pack-nuget.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "pack-nuget.ps1 failed (exit code $LASTEXITCODE)."
}

$packages = Get-ChildItem -LiteralPath $outputDir -Filter "*.$Version.nupkg" -File | Sort-Object Name
if (-not $packages) {
    throw "No *.$Version.nupkg package in $outputDir."
}

Write-Host ''
Write-Host "$($packages.Count) paket versi $Version siap di: $outputDir"
$answer = (Read-Host "Push to $Source ? [y/N]").Trim().ToLowerInvariant()
if ($answer -ne 'y' -and $answer -ne 'yes') {
    Write-Host 'Push dilewati. Paket tetap di folder di atas.'
    exit 0
}

if ($Version -match '(?i)pre-?alpha') {
    throw "Version '$Version' is a prealpha. Per doc/convention/nuget-naming.md (decision 2026-10-05), prealpha is not published to a public feed; keeping it in $outputDir for local testing is enough. Feed publik dimulai dari alpha."
}

$tokenFile = Join-Path $repoRoot '..' '.artefacts' 'em-system' 'github-pat.txt'

$apiKey = $env:EM_NUGET_PAT
if (-not $apiKey -and (Test-Path -LiteralPath $tokenFile -PathType Leaf)) {
    $fromFile = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
    if ($fromFile) {
        $apiKey = $fromFile
        Write-Host "PAT read from $tokenFile"
    }
}
if (-not $apiKey) {
    Write-Host "No PAT in EM_NUGET_PAT or in $tokenFile."
    Write-Host 'Enter a classic GitHub PAT with the scopes write:packages and read:packages.'
    $secureToken = Read-Host 'GitHub PAT (hidden)' -AsSecureString
    if (-not $secureToken.Length) {
        throw 'The PAT must not be empty.'
    }
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    try {
        $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringUni($pointer)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

Write-Host ''
Write-Host "Push $($packages.Count) paket versi $Version ke $Source ..."
foreach ($package in $packages) {
    Write-Host "  -> $($package.Name)"
    & dotnet nuget push $package.FullName --source $Source --api-key $apiKey --skip-duplicate
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet nuget push failed for $($package.Name) (exit code $LASTEXITCODE)."
    }
}

Write-Host ''
Write-Host "Done. Version $Version must not be reused in the feed; if there was a mistake, raise N or PATCH (doc/convention/nuget-naming.md)."
