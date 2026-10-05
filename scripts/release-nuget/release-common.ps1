# Fungsi bersama aturan versi dan release note rilis NuGet. Di-dot-source oleh release-nuget.ps1 dan
# job validate di .github/workflows/publish-nuget.yml, supaya skrip lokal dan workflow memakai aturan sama.

$script:SemVerPattern = '^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z][0-9A-Za-z.-]*))?$'

function ConvertTo-SemVer([string]$text) {
    if ($text -notmatch $script:SemVerPattern) { return $null }
    [pscustomobject]@{
        Text = $text
        Core = [int[]]@($Matches[1], $Matches[2], $Matches[3])
        Pre  = if ($Matches[4]) { $Matches[4].Split('.') } else { @() }
    }
}

function Compare-SemVer($a, $b) {
    for ($i = 0; $i -lt 3; $i++) {
        if ($a.Core[$i] -ne $b.Core[$i]) { return [Math]::Sign($a.Core[$i] - $b.Core[$i]) }
    }
    if ($a.Pre.Count -eq 0 -or $b.Pre.Count -eq 0) { return [Math]::Sign($b.Pre.Count - $a.Pre.Count) }
    for ($i = 0; $i -lt [Math]::Min($a.Pre.Count, $b.Pre.Count); $i++) {
        $x = $a.Pre[$i]; $y = $b.Pre[$i]
        $xNum = $x -match '^\d+$'; $yNum = $y -match '^\d+$'
        if ($xNum -and $yNum) { $c = ([bigint]$x).CompareTo([bigint]$y) }
        elseif ($xNum) { $c = -1 }
        elseif ($yNum) { $c = 1 }
        else { $c = [Math]::Sign([string]::CompareOrdinal($x, $y)) }
        if ($c -ne 0) { return $c }
    }
    return [Math]::Sign($a.Pre.Count - $b.Pre.Count)
}

function Get-Highest($versions) {
    $best = $null
    foreach ($v in $versions) { if ($v -and (-not $best -or (Compare-SemVer $v $best) -gt 0)) { $best = $v } }
    return $best
}

function Get-Lowest($versions) {
    $best = $null
    foreach ($v in $versions) { if ($v -and (-not $best -or (Compare-SemVer $v $best) -lt 0)) { $best = $v } }
    return $best
}

function Get-NextVersion($latest) {
    if (-not $latest) { return '0.1.0-alpha.1' }
    $core = $latest.Core
    if ($latest.Pre.Count -eq 0) { return "$($core[0]).$($core[1] + 1).0-alpha.1" }
    $pre = [string[]]$latest.Pre.Clone()
    $last = $pre.Count - 1
    if ($pre[$last] -match '^\d+$') { $pre[$last] = [string]([bigint]$pre[$last] + 1) } else { $pre += '1' }
    return "$($core -join '.')-$($pre -join '.')"
}

function Test-PreAlpha([string]$version) { return $version -match '(?i)pre-?alpha' }

function Get-PackageIds([string]$repoRoot) {
    $ids = @(Get-Content -LiteralPath (Join-Path $repoRoot 'scripts' 'pack-nuget' 'packages.txt') |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) -replace '^Em\.', 'EmSys.' })
    if (-not $ids) { throw 'scripts/pack-nuget/packages.txt tidak berisi project.' }
    return $ids
}

# Release note yang belum ada atau kosong untuk satu versi, sebagai path relatif repo.
function Get-MissingNotes([string]$repoRoot, [string[]]$packageIds, [string]$version) {
    @($packageIds | Where-Object {
        $path = Join-Path $repoRoot 'doc' 'ReleaseNote' $_ "$version.md"
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or -not (Get-Content -LiteralPath $path -Raw).Trim()
    } | ForEach-Object { "doc/ReleaseNote/$_/$version.md" })
}

# Semua versi yang punya release note untuk minimal satu paket di daftar.
function Get-NoteVersions([string]$repoRoot, [string[]]$packageIds) {
    $noteDir = Join-Path $repoRoot 'doc' 'ReleaseNote'
    @(Get-ChildItem -LiteralPath $noteDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $packageIds } |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.md' -File } |
        ForEach-Object BaseName | Sort-Object -Unique)
}
