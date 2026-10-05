<#
Membuat dan push tag git v<versi> dari main untuk memicu workflow .github/workflows/publish-nuget.yml
(build + test, approval environment release, pack, push ke nuget.org lewat Trusted Publishing).

Prasyarat: branch main aktif, working tree bersih, dan sama persis dengan origin/main.
Dampak: membuat tag annotated v<versi> lalu push ke origin. Versi tidak pernah dipakai ulang di feed,
jadi tag yang sudah dipush tidak dihapus; bila salah, terbitkan versi berikutnya.

Pemakaian: scripts\release-nuget.cmd 0.1.0-alpha.1   (tanpa argumen: versi ditanyakan)
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Set-Location -LiteralPath $repoRoot

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') gagal: $output" }
    return $output
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host 'Versi yang dirilis (mis. 0.1.0-alpha.1)'
}
$Version = $Version.Trim() -replace '^v', ''

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Versi '$Version' bukan format MAJOR.MINOR.PATCH[-channel.N]."
}
if ($Version -match 'pre-?alpha') {
    throw "Versi '$Version' adalah prealpha. Sesuai doc/konvensi/konvensi-penamaan-nuget.md, prealpha tidak diterbitkan ke feed publik."
}
$tag = "v$Version"

$branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Branch aktif '$branch'. Pindah dulu ke main (git switch main)." }

if (Invoke-Git status --porcelain) { throw 'Working tree belum bersih. Commit atau stash perubahan dulu.' }

Invoke-Git fetch --quiet origin main | Out-Null
$head = (Invoke-Git rev-parse HEAD).Trim()
$remote = (Invoke-Git rev-parse origin/main).Trim()
if ($head -ne $remote) { throw 'main lokal berbeda dengan origin/main. Jalankan git pull (atau push commit lokal) dulu.' }

& git rev-parse -q --verify "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) { throw "Tag $tag sudah ada di lokal." }
if (Invoke-Git ls-remote --tags origin "refs/tags/$tag") { throw "Tag $tag sudah ada di origin." }

$subject = (Invoke-Git log -1 --format='%h %s').Trim()
Write-Host ''
Write-Host "Tag     : $tag"
Write-Host "Commit  : $subject"
Write-Host 'Paket   : EmSys.Libs, EmSys.Api.Core, EmSys.Ui.Core, EmSys.Ui.Wpf.Core, EmSys.Ui.Maui.Core'
Write-Host 'Tujuan  : nuget.org (permanen, versi tidak bisa dihapus)'
Write-Host ''
$answer = Read-Host "Buat dan push tag $tag ? [y/N]"
if ($answer -notin @('y', 'yes')) {
    Write-Host 'Dibatalkan. Tidak ada tag yang dibuat.'
    exit 0
}

Invoke-Git tag -a $tag -m "Release $Version" | Out-Null
Invoke-Git push origin $tag | Out-Null

Write-Host ''
Write-Host "Tag $tag sudah dipush. Pantau dan setujui job publish di:"
Write-Host 'https://github.com/MatrixCode-ID/em-system/actions/workflows/publish-nuget.yml'
