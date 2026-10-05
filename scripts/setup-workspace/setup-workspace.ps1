<#
Menyiapkan folder kerja em-system di mesin baru: satu folder lokal bernama em-system dengan dua remote,
  private -> MatrixCode-ID/em-system-work (privat, branch work-bench)
  origin  -> MatrixCode-ID/em-system      (publik, main dan ci-sandbox)

Mode otomatis:
- Dijalankan dari dalam repo (scripts\setup-workspace.cmd di clone yang sudah ada, mis. hasil clone
  GitHub Desktop): hanya menyusun remote dan tracking branch. Aman dijalankan berulang.
- Dijalankan di luar repo (setup-workspace.cmd + folder setup-workspace disalin ke mesin baru): clone
  em-system-work ke <folder induk>\em-system, lalu menyusun remote.

Prasyarat: git di PATH dan akun GitHub yang punya akses ke kedua repo (login muncul saat clone/fetch).
Dampak: clone baru, rename/tambah remote, membuat atau mengatur tracking branch lokal. Tidak menghapus
branch, tidak push, dan tidak menyentuh perubahan yang belum di-commit.
Folder ..\.artefacts\em-system (config, key, PAT) tidak ada di Git; salin manual dari mesin lama.
#>
param(
    [string]$ParentDir
)

$ErrorActionPreference = 'Stop'
$privateUrl = 'https://github.com/MatrixCode-ID/em-system-work.git'
$publicUrl = 'https://github.com/MatrixCode-ID/em-system.git'
$folderName = 'em-system'

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') gagal: $output" }
    return $output
}

function Get-RemoteUrl([string]$name) {
    $url = & git remote get-url $name 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $url.Trim()
}

function Test-SameUrl([string]$a, [string]$b) {
    $norm = { param($u) ($u -replace '\.git$', '').TrimEnd('/').ToLowerInvariant() }
    return (& $norm $a) -eq (& $norm $b)
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'git tidak ditemukan di PATH. Pasang Git for Windows lalu coba lagi.' }

$candidate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
& git -C $candidate rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -eq 0) {
    $repoRoot = (& git -C $candidate rev-parse --show-toplevel).Trim()
    Write-Host "Repo ditemukan: $repoRoot"
} else {
    if ([string]::IsNullOrWhiteSpace($ParentDir)) {
        $defaultParent = (Get-Location).Path
        $ParentDir = Read-Host "Folder induk tempat clone [$defaultParent]"
        if ([string]::IsNullOrWhiteSpace($ParentDir)) { $ParentDir = $defaultParent }
    }
    $ParentDir = [IO.Path]::GetFullPath($ParentDir.Trim().Trim('"'))
    if (-not (Test-Path -LiteralPath $ParentDir -PathType Container)) { throw "Folder induk '$ParentDir' tidak ada." }
    $repoRoot = Join-Path $ParentDir $folderName
    if (Test-Path -LiteralPath $repoRoot) { throw "'$repoRoot' sudah ada. Jalankan scripts\setup-workspace.cmd dari dalam folder itu, atau pilih folder induk lain." }
    Write-Host "Clone $privateUrl ke $repoRoot ..."
    & git clone --origin private $privateUrl $repoRoot
    if ($LASTEXITCODE -ne 0) { throw 'git clone gagal. Pastikan akun GitHub punya akses ke repo privat em-system-work.' }
}

Set-Location -LiteralPath $repoRoot

$originUrl = Get-RemoteUrl 'origin'
$privateRemoteUrl = Get-RemoteUrl 'private'

if (-not $privateRemoteUrl -and $originUrl -and (Test-SameUrl $originUrl $privateUrl)) {
    Invoke-Git remote rename origin private | Out-Null
    Write-Host 'Remote origin (em-system-work) diganti nama menjadi private.'
    $privateRemoteUrl = $originUrl
    $originUrl = $null
}
if (-not $privateRemoteUrl) {
    Invoke-Git remote add private $privateUrl | Out-Null
    Write-Host 'Remote private ditambahkan.'
} elseif (-not (Test-SameUrl $privateRemoteUrl $privateUrl)) {
    throw "Remote private menunjuk '$privateRemoteUrl', bukan $privateUrl. Periksa manual (git remote -v)."
}
if (-not $originUrl) {
    Invoke-Git remote add origin $publicUrl | Out-Null
    Write-Host 'Remote origin (em-system publik) ditambahkan.'
} elseif (-not (Test-SameUrl $originUrl $publicUrl)) {
    throw "Remote origin menunjuk '$originUrl', bukan $publicUrl. Periksa manual (git remote -v)."
}

Write-Host 'Fetch private dan origin ...'
Invoke-Git fetch --prune private | Out-Null
Invoke-Git fetch --prune origin | Out-Null

$tracking = [ordered]@{ 'work-bench' = 'private'; 'main' = 'origin'; 'ci-sandbox' = 'origin' }
foreach ($branch in $tracking.Keys) {
    $remote = $tracking[$branch]
    & git rev-parse -q --verify "refs/remotes/$remote/$branch" *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "Lewati $branch`: $remote/$branch tidak ada."; continue }
    & git rev-parse -q --verify "refs/heads/$branch" *> $null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git branch --set-upstream-to "$remote/$branch" $branch | Out-Null
    } else {
        Invoke-Git branch --track $branch "$remote/$branch" | Out-Null
    }
    Write-Host "$branch -> $remote/$branch"
}

$current = (& git branch --show-current).Trim()
if ($current -ne 'work-bench') {
    if (& git status --porcelain) {
        Write-Host "Branch aktif tetap '$current' karena ada perubahan yang belum di-commit."
    } else {
        Invoke-Git switch work-bench | Out-Null
        Write-Host 'Pindah ke branch work-bench.'
    }
}

Write-Host ''
& git remote -v
Write-Host ''
& git branch -vv
Write-Host ''
$artefacts = [IO.Path]::GetFullPath((Join-Path $repoRoot '..' '.artefacts' 'em-system'))
if (Test-Path -LiteralPath $artefacts) {
    Write-Host "Folder artefak ada: $artefacts"
} else {
    Write-Host "Folder artefak belum ada: $artefacts"
    Write-Host 'Salin dari mesin lama (config, debug key, PAT, harness uji); isinya tidak ada di Git.'
}
