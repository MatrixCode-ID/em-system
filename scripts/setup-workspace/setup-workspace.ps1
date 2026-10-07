<#
Sets up the em-system working folder on a new machine: one local folder named em-system with two remotes,
  private -> MatrixCode-ID/em-system-work (private, branch work-bench)
  origin  -> MatrixCode-ID/em-system      (public, main and ci-sandbox)

Automatic modes:
- Run from inside the repo (scripts\setup-workspace.cmd in an existing clone, e.g. one made by GitHub
  Desktop): only arranges the remotes and tracking branches. Safe to run repeatedly.
- Run outside the repo (setup-workspace.cmd + the setup-workspace folder copied to the new machine): clones
  em-system-work to <parent folder>\em-system, then arranges the remotes.

Prerequisites: git on PATH and a GitHub account with access to both repos (a login appears during clone/fetch).
Impact: a new clone, renaming/adding remotes, creating or setting local tracking branches. It does not delete
branches, does not push, and does not touch uncommitted changes.
The folder ..\.artefacts\em-system (config, keys, PAT) is not in Git; copy it manually from the old machine.
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
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $output" }
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

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'git was not found on PATH. Install Git for Windows and try again.' }

$candidate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
& git -C $candidate rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -eq 0) {
    $repoRoot = (& git -C $candidate rev-parse --show-toplevel).Trim()
    Write-Host "Repo ditemukan: $repoRoot"
} else {
    if ([string]::IsNullOrWhiteSpace($ParentDir)) {
        $defaultParent = (Get-Location).Path
        $ParentDir = Read-Host "Parent folder to clone into [$defaultParent]"
        if ([string]::IsNullOrWhiteSpace($ParentDir)) { $ParentDir = $defaultParent }
    }
    $ParentDir = [IO.Path]::GetFullPath($ParentDir.Trim().Trim('"'))
    if (-not (Test-Path -LiteralPath $ParentDir -PathType Container)) { throw "Parent folder '$ParentDir' does not exist." }
    $repoRoot = Join-Path $ParentDir $folderName
    if (Test-Path -LiteralPath $repoRoot) { throw "'$repoRoot' already exists. Run scripts\setup-workspace.cmd from inside that folder, or choose another parent folder." }
    Write-Host "Clone $privateUrl ke $repoRoot ..."
    & git clone --origin private $privateUrl $repoRoot
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed. Make sure the GitHub account has access to the private repo em-system-work.' }
}

Set-Location -LiteralPath $repoRoot

$originUrl = Get-RemoteUrl 'origin'
$privateRemoteUrl = Get-RemoteUrl 'private'

if (-not $privateRemoteUrl -and $originUrl -and (Test-SameUrl $originUrl $privateUrl)) {
    Invoke-Git remote rename origin private | Out-Null
    Write-Host 'Remote origin (em-system-work) was renamed to private.'
    $privateRemoteUrl = $originUrl
    $originUrl = $null
}
if (-not $privateRemoteUrl) {
    Invoke-Git remote add private $privateUrl | Out-Null
    Write-Host 'Remote private ditambahkan.'
} elseif (-not (Test-SameUrl $privateRemoteUrl $privateUrl)) {
    throw "Remote private points to '$privateRemoteUrl', not $privateUrl. Check manually (git remote -v)."
}
if (-not $originUrl) {
    Invoke-Git remote add origin $publicUrl | Out-Null
    Write-Host 'Remote origin (em-system publik) ditambahkan.'
} elseif (-not (Test-SameUrl $originUrl $publicUrl)) {
    throw "Remote origin points to '$originUrl', not $publicUrl. Check manually (git remote -v)."
}

Write-Host 'Fetching private and origin ...'
Invoke-Git fetch --prune private | Out-Null
Invoke-Git fetch --prune origin | Out-Null

$tracking = [ordered]@{ 'work-bench' = 'private'; 'main' = 'origin'; 'ci-sandbox' = 'origin' }
foreach ($branch in $tracking.Keys) {
    $remote = $tracking[$branch]
    & git rev-parse -q --verify "refs/remotes/$remote/$branch" *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "Skipping $branch`: $remote/$branch does not exist."; continue }
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
        Write-Host "The active branch stays '$current' because there are uncommitted changes."
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
    Write-Host "The artifacts folder exists: $artefacts"
} else {
    Write-Host "The artifacts folder does not exist yet: $artefacts"
    Write-Host 'Copy it from the old machine (config, debug key, PAT, test harness); its content is not in Git.'
}
