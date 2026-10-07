<#
Creates and pushes the git tag v<version> from main to trigger the workflow .github/workflows/publish-nuget.yml
(build + test, release environment approval, pack, push to nuget.org through Trusted Publishing).

Prerequisites: the main branch is checked out, the working tree is clean and exactly equal to origin/main, and
the release note doc/ReleaseNote/<PackageId>/<version>.md exists on main for every package in
scripts/pack-nuget/packages.txt (the workflow also rejects a tag without those files). When some versions have
complete release notes but have not been released, the lowest of them becomes the default.
The version is asked for at run time. Its default is the next version after the highest one among the git v*
tags (origin) and on nuget.org (EmSys.Libs): channel.N goes up by one, a release X.Y.Z continues to
X.(Y+1).0-alpha.1, and no version at all becomes 0.1.0-alpha.1.
Impact: creates the annotated tag v<version> and pushes it to origin. A version is never reused in the feed,
so a tag that was published is not deleted; if it was wrong, publish the next version.

Usage: scripts\release-nuget.cmd   (the version is an optional argument, which skips the question)
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
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $output" }
    return $output
}

$branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "The active branch is '$branch'. Switch to main first (git switch main)." }

if (Invoke-Git status --porcelain) { throw 'The working tree is not clean. Commit or stash your changes first.' }

Invoke-Git fetch --quiet --tags origin main | Out-Null
$head = (Invoke-Git rev-parse HEAD).Trim()
$remote = (Invoke-Git rev-parse origin/main).Trim()
if ($head -ne $remote) { throw 'Local main differs from origin/main. Run git pull (or push the local commits) first.' }

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
    $nugetNote = if ($status -and [int]$status -eq 404) { 'no package yet' } else { "unreachable ($($_.Exception.Message))" }
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
Write-Host "Latest git tag version: $(if ($latestTag) { "v$($latestTag.Text)" } else { 'none' })"
Write-Host "Latest nuget version  : $(if ($latestNuget) { $latestNuget.Text } elseif ($nugetNote) { $nugetNote } else { 'no package yet' })"
Write-Host "Release notes ready   : $(if ($pendingNotes) { ($pendingNotes | ForEach-Object Text) -join ', ' } else { 'none (doc/ReleaseNote/<PackageId>/<version>.md for all packages)' })"
Write-Host ''

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host "Version to release [$default]"
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $default }
}
$Version = $Version.Trim() -replace '^v', ''

$parsed = ConvertTo-SemVer $Version
if (-not $parsed) { throw "Version '$Version' is not in the format MAJOR.MINOR.PATCH[-channel.N]." }
if ($Version -match '(?i)pre-?alpha') {
    throw "Version '$Version' is a prealpha. Per doc/convention/nuget-naming.md, prealpha is not published to a public feed."
}
if ($latest -and (Compare-SemVer $parsed $latest) -le 0) {
    throw "Version '$Version' is not higher than the latest version '$($latest.Text)'. A version is never reused or lowered."
}
$missing = Get-MissingNotes $repoRoot $packageIds $Version
if ($missing) {
    throw "Release note is missing or empty:`n  $($missing -join "`n  ")`nCreate it and merge it into main first, then run again."
}
$tag = "v$Version"

& git rev-parse -q --verify "refs/tags/$tag" *> $null
if ($LASTEXITCODE -eq 0) { throw "Tag $tag already exists locally." }

$subject = (Invoke-Git log -1 --format='%h %s').Trim()
Write-Host ''
Write-Host "Tag     : $tag"
Write-Host "Commit  : $subject"
Write-Host "Paket   : $($packageIds -join ', ')"
Write-Host 'Target  : nuget.org (permanent, a version cannot be deleted)'
Write-Host ''
$answer = (Read-Host "Create and push tag $tag ? [y/N]").Trim().ToLowerInvariant()
if ($answer -notin @('y', 'yes')) {
    Write-Host 'Cancelled. No tag was created.'
    exit 0
}

Invoke-Git tag -a $tag -m "Release $Version" | Out-Null
Invoke-Git push origin $tag | Out-Null

Write-Host ''
Write-Host "Tag $tag has been pushed. Watch and approve the publish job at:"
Write-Host 'https://github.com/MatrixCode-ID/em-system/actions/workflows/publish-nuget.yml'
