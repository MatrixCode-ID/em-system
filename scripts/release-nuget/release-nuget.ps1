<#
Membuat dan push tag git v<versi> dari main untuk memicu workflow .github/workflows/publish-nuget.yml
(build + test, approval environment release, pack, push ke nuget.org lewat Trusted Publishing).

Prasyarat: branch main aktif, working tree bersih, dan sama persis dengan origin/main, serta release note
doc/ReleaseNote/<PackageId>/<versi>.md sudah ada di main untuk setiap paket di scripts/pack-nuget/packages.txt
(workflow juga menolak tag tanpa berkas itu). Bila ada versi yang release note-nya lengkap tapi belum dirilis,
versi terendah di antaranya menjadi default.
Versi ditanyakan saat jalan. Default-nya versi berikutnya dari versi tertinggi di tag git v* (origin)
dan di nuget.org (EmSys.Libs): channel.N naik satu, rilis X.Y.Z lanjut ke X.(Y+1).0-alpha.1,
belum ada versi sama sekali menjadi 0.1.0-alpha.1.
Dampak: membuat tag annotated v<versi> lalu push ke origin. Versi tidak pernah dipakai ulang di feed,
jadi tag yang sudah terbit tidak dihapus; bila salah, terbitkan versi berikutnya.

Pemakaian: scripts\release-nuget.cmd   (versi opsional sebagai argumen, melewati pertanyaan)
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Set-Location -LiteralPath $repoRoot
. (Join-Path $PSScriptRoot 'release-common.ps1')

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') gagal: $output" }
    return $output
}

$branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Branch aktif '$branch'. Pindah dulu ke main (git switch main)." }

if (Invoke-Git status --porcelain) { throw 'Working tree belum bersih. Commit atau stash perubahan dulu.' }

Invoke-Git fetch --quiet --tags origin main | Out-Null
$head = (Invoke-Git rev-parse HEAD).Trim()
$remote = (Invoke-Git rev-parse origin/main).Trim()
if ($head -ne $remote) { throw 'main lokal berbeda dengan origin/main. Jalankan git pull (atau push commit lokal) dulu.' }

$tagVersions = @(Invoke-Git ls-remote --tags --refs origin 'refs/tags/v*' |
    ForEach-Object { ($_ -split "`t")[1] -replace '^refs/tags/v', '' } |
    ForEach-Object { ConvertTo-SemVer $_ } | Where-Object { $_ })

$nugetVersions = @()
$nugetNote = ''
try {
    $index = Invoke-RestMethod -Uri 'https://api.nuget.org/v3-flatcontainer/emsys.libs/index.json' -TimeoutSec 15
    $nugetVersions = @($index.versions | ForEach-Object { ConvertTo-SemVer $_ } | Where-Object { $_ })
} catch {
    $status = $_.Exception.Response.StatusCode
    $nugetNote = if ($status -and [int]$status -eq 404) { 'belum ada paket' } else { "tidak terjangkau ($($_.Exception.Message))" }
}

$latestTag = Get-Highest $tagVersions
$latestNuget = Get-Highest $nugetVersions
$latest = Get-Highest @($latestTag, $latestNuget | Where-Object { $_ })

$packageIds = Get-PackageIds $repoRoot
$pendingNotes = @(Get-NoteVersions $repoRoot $packageIds | ForEach-Object { ConvertTo-SemVer $_ } |
    Where-Object { $_ -and (-not $latest -or (Compare-SemVer $_ $latest) -gt 0) -and -not (Get-MissingNotes $repoRoot $packageIds $_.Text) })
$lowestPending = Get-Lowest $pendingNotes
$default = if ($lowestPending) { $lowestPending.Text } else { Get-NextVersion $latest }

Write-Host ''
Write-Host "Versi terakhir tag git : $(if ($latestTag) { "v$($latestTag.Text)" } else { 'belum ada' })"
Write-Host "Versi terakhir nuget   : $(if ($latestNuget) { $latestNuget.Text } elseif ($nugetNote) { $nugetNote } else { 'belum ada paket' })"
Write-Host "Release note siap      : $(if ($pendingNotes) { ($pendingNotes | ForEach-Object Text) -join ', ' } else { 'belum ada (doc/ReleaseNote/<PackageId>/<versi>.md untuk semua paket)' })"
Write-Host ''

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host "Versi yang dirilis [$default]"
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $default }
}
$Version = $Version.Trim() -replace '^v', ''

$parsed = ConvertTo-SemVer $Version
if (-not $parsed) { throw "Versi '$Version' bukan format MAJOR.MINOR.PATCH[-channel.N]." }
if ($Version -match '(?i)pre-?alpha') {
    throw "Versi '$Version' adalah prealpha. Sesuai doc/konvensi/konvensi-penamaan-nuget.md, prealpha tidak diterbitkan ke feed publik."
}
if ($latest -and (Compare-SemVer $parsed $latest) -le 0) {
    throw "Versi '$Version' tidak lebih tinggi dari versi terakhir '$($latest.Text)'. Versi tidak pernah dipakai ulang atau mundur."
}
$missing = Get-MissingNotes $repoRoot $packageIds $Version
if ($missing) {
    throw "Release note belum ada atau kosong:`n  $($missing -join "`n  ")`nBuat dan merge ke main dulu, lalu jalankan ulang."
}
$tag = "v$Version"

& git rev-parse -q --verify "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) { throw "Tag $tag sudah ada di lokal." }

$subject = (Invoke-Git log -1 --format='%h %s').Trim()
Write-Host ''
Write-Host "Tag     : $tag"
Write-Host "Commit  : $subject"
Write-Host "Paket   : $($packageIds -join ', ')"
Write-Host 'Tujuan  : nuget.org (permanen, versi tidak bisa dihapus)'
Write-Host ''
$answer = (Read-Host "Buat dan push tag $tag ? [y/N]").Trim().ToLowerInvariant()
if ($answer -notin @('y', 'yes')) {
    Write-Host 'Dibatalkan. Tidak ada tag yang dibuat.'
    exit 0
}

Invoke-Git tag -a $tag -m "Release $Version" | Out-Null
Invoke-Git push origin $tag | Out-Null

Write-Host ''
Write-Host "Tag $tag sudah dipush. Pantau dan setujui job publish di:"
Write-Host 'https://github.com/MatrixCode-ID/em-system/actions/workflows/publish-nuget.yml'
