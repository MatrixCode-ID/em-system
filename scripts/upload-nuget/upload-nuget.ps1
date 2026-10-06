<#
Pack lima paket library EmSys.* lewat scripts/pack-nuget/pack-nuget.ps1, lalu tanya konfirmasi untuk push
ke feed NuGet GitHub Packages (doc/convention/nuget-naming.md). PAT classic GitHub
(scope write:packages, read:packages) diambil berurutan dari: environment variable
EM_NUGET_PAT, lalu berkas ..\.artefacts\em-system\github-pat.txt, lalu prompt tersembunyi
bila keduanya tidak ada. PAT tidak pernah ditulis ke log.
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
    throw "pack-nuget.ps1 gagal (exit code $LASTEXITCODE)."
}

$packages = Get-ChildItem -LiteralPath $outputDir -Filter "*.$Version.nupkg" -File | Sort-Object Name
if (-not $packages) {
    throw "Tidak ada paket *.$Version.nupkg di $outputDir."
}

Write-Host ''
Write-Host "$($packages.Count) paket versi $Version siap di: $outputDir"
$answer = (Read-Host "Push ke $Source ? [y/N]").Trim().ToLowerInvariant()
if ($answer -ne 'y' -and $answer -ne 'yes') {
    Write-Host 'Push dilewati. Paket tetap di folder di atas.'
    exit 0
}

if ($Version -match '(?i)pre-?alpha') {
    throw "Versi '$Version' adalah prealpha. Sesuai doc/convention/nuget-naming.md (keputusan 2026-10-05), prealpha tidak diterbitkan ke feed publik; cukup di $outputDir untuk uji lokal. Feed publik dimulai dari alpha."
}

$tokenFile = Join-Path $repoRoot '..' '.artefacts' 'em-system' 'github-pat.txt'

$apiKey = $env:EM_NUGET_PAT
if (-not $apiKey -and (Test-Path -LiteralPath $tokenFile -PathType Leaf)) {
    $fromFile = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
    if ($fromFile) {
        $apiKey = $fromFile
        Write-Host "PAT dibaca dari $tokenFile"
    }
}
if (-not $apiKey) {
    Write-Host "PAT tidak ada di EM_NUGET_PAT maupun $tokenFile."
    Write-Host 'Masukkan PAT classic GitHub dengan scope write:packages dan read:packages.'
    $secureToken = Read-Host 'GitHub PAT (hidden)' -AsSecureString
    if (-not $secureToken.Length) {
        throw 'PAT tidak boleh kosong.'
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
        throw "dotnet nuget push gagal untuk $($package.Name) (exit code $LASTEXITCODE)."
    }
}

Write-Host ''
Write-Host "Selesai. Versi $Version tidak boleh dipakai ulang di feed; bila ada kesalahan, naikkan N atau PATCH (doc/convention/nuget-naming.md)."
