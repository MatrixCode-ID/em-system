<#
.SYNOPSIS
Copies the MatrixGrid master into Em and translates names on the way.

.DESCRIPTION
MatrixGrid stays the master of the grid, sheet, writer and document code. This script copies the files that
map.json selects, translates namespaces, the renamed control types and a few strings, converts the xUnit v2
tests to xUnit v3, deletes copies whose source is gone, and records the source commit in last-sync.json.
Files listed as owned in map.json belong to Em and are never written or deleted. Do not edit the synced copies;
fix the map, this script, or the source in MatrixGrid and sync again. See doc/engine/engine-documents.md#sync.

.PARAMETER Source
The MatrixGrid checkout. Defaults to ..\MatrixGrid next to this repository.

.PARAMETER WhatIf
Reports what would change without writing anything.
#>
[CmdletBinding()]
param(
    [string]$Source,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
$map = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'map.json') | ConvertFrom-Json
if (-not $Source) { $Source = Join-Path $repo $map.defaultSource }
$Source = [System.IO.Path]::GetFullPath($Source)
if (-not (Test-Path -LiteralPath (Join-Path $Source 'src'))) {
    throw "MatrixGrid source not found at '$Source'. Pass -Source with the MatrixGrid checkout."
}

Write-Output "=== Sync MatrixGrid"
Write-Output "Source: $Source"
Write-Output "Target: $repo"
if ($WhatIf) { Write-Output "Mode:   report only (-WhatIf), nothing is written" }

function ConvertTo-Slash([string]$path) { $path.Replace('\', '/') }

function Test-Glob([string]$path, [string]$pattern) {
    $regex = '^' + [regex]::Escape($pattern).Replace('\*\*/', '(.*/)?').Replace('\*\*', '.*').Replace('\*', '[^/]*') + '$'
    return $path -match $regex
}

$excludes = @($map.exclude)
$owned = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $map.owned) { [void]$owned.Add((ConvertTo-Slash $item)) }

# Longest mapped namespace wins, matched on whole dotted segments so resource keys such as
# "MatrixGrid.Brush.Primary" and the class name MatrixGrid stay untouched.
$namespaces = @($map.namespaces.PSObject.Properties | Sort-Object { $_.Name.Length } -Descending)
$namespaceLookup = @{}
foreach ($entry in $namespaces) { $namespaceLookup[$entry.Name] = $entry.Value }
$dotted = [regex]'(?<![\w.])[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+'
$types = @($map.types.PSObject.Properties)

function Convert-Namespaces([string]$text) {
    return $dotted.Replace($text, {
        param($match)
        $value = $match.Value
        $segments = $value.Split('.')
        for ($count = $segments.Length; $count -ge 2; $count--) {
            $prefix = [string]::Join('.', $segments[0..($count - 1)])
            if ($namespaceLookup.ContainsKey($prefix)) {
                return $namespaceLookup[$prefix] + $value.Substring($prefix.Length)
            }
        }
        return $value
    })
}

function Convert-Text([string]$text, [bool]$isTest) {
    $text = Convert-Namespaces $text
    foreach ($type in $types) {
        $text = [regex]::Replace($text, '\b' + [regex]::Escape($type.Name) + '\b', $type.Value)
    }
    foreach ($rule in $map.strings) { $text = $text.Replace($rule.from, $rule.to) }
    foreach ($drop in $map.dropLines) {
        $text = [regex]::Replace($text, '(?m)^[^\r\n]*' + [regex]::Escape($drop) + '[^\r\n]*(\r?\n)?', '')
    }
    # Several source namespaces can map to one Em namespace; keep only the first of identical using directives.
    $seenUsings = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $text = [regex]::Replace($text, '(?m)^[ \t]*using[ \t]+[\w.]+[ \t]*;[ \t]*(\r?\n)', {
        param($match)
        if ($seenUsings.Add($match.Value.Trim())) { return $match.Value }
        return ''
    })
    if ($isTest) {
        # xUnit v3: Xunit.Abstractions is gone (ITestOutputHelper lives in Xunit) and IAsyncLifetime uses ValueTask.
        $text = [regex]::Replace($text, '(?m)^\s*using\s+Xunit\.Abstractions\s*;[^\r\n]*(\r?\n)?', '')
        if ($text -match '\bIAsyncLifetime\b') {
            $text = [regex]::Replace($text, '\b(public\s+(?:async\s+)?)Task(\s+(?:InitializeAsync|DisposeAsync)\s*\()', '$1ValueTask$2')
        }
    }
    return $text
}

function Get-TargetName([string]$name) {
    foreach ($type in $types) {
        if ($name -match ('^' + [regex]::Escape($type.Name) + '(\..+)$')) { return $type.Value + $Matches[1] }
    }
    return $name
}

$textExtensions = @('.cs', '.xaml', '.json', '.md', '.txt', '.xml', '.props', '.targets')
$planned = [ordered]@{}
$skipped = [System.Collections.Generic.List[string]]::new()

function Add-Plan([string]$sourceFile, [string]$relativeSource, [string]$target) {
    if ($owned.Contains($target)) { $skipped.Add("$relativeSource (target $target is owned by Em)"); return }
    $planned[$target] = [pscustomobject]@{ Source = $sourceFile; Relative = $relativeSource; Target = $target }
}

Write-Output "=== Reading source files"
$folderMaps = @($map.folders | Sort-Object { $_.from.Length } -Descending)
$fileMaps = @{}
foreach ($entry in $map.files) { $fileMaps[(ConvertTo-Slash $entry.from)] = ConvertTo-Slash $entry.to }

# Only files tracked by Git are copied, so local build output and scratch files never reach Em.
$roots = @($map.folders | ForEach-Object { ($_.from -split '/')[0..1] -join '/' } | Sort-Object -Unique)
$tracked = @(& git -C $Source ls-files -- @roots)
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed in '$Source'." }
foreach ($relative in $tracked) {
    $relative = ConvertTo-Slash $relative
    $file = Get-Item -LiteralPath (Join-Path $Source $relative) -ErrorAction SilentlyContinue
    if (-not $file) { $skipped.Add("$relative (tracked but missing on disk)"); continue }
    if ($relative -match '(^|/)(bin|obj)/') { continue }
    if ($fileMaps.ContainsKey($relative)) { Add-Plan $file.FullName $relative $fileMaps[$relative]; continue }
    if ($excludes | Where-Object { Test-Glob $relative $_ }) { $skipped.Add("$relative (excluded)"); continue }
    $folder = $folderMaps | Where-Object { $relative.StartsWith($_.from + '/', [StringComparison]::Ordinal) } | Select-Object -First 1
    if (-not $folder) { $skipped.Add("$relative (no mapping)"); continue }
    $rest = $relative.Substring($folder.from.Length + 1)
    $directory = [System.IO.Path]::GetDirectoryName($rest)
    $name = Get-TargetName ([System.IO.Path]::GetFileName($rest))
    $target = if ($directory) { "$($folder.to)/$(ConvertTo-Slash $directory)/$name" } else { "$($folder.to)/$name" }
    Add-Plan $file.FullName $relative $target
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
$added = [System.Collections.Generic.List[string]]::new()
$changed = [System.Collections.Generic.List[string]]::new()
$unchanged = 0

Write-Output "=== Translating and writing $($planned.Count) files"
foreach ($item in $planned.Values) {
    $targetPath = Join-Path $repo $item.Target
    $extension = [System.IO.Path]::GetExtension($item.Source).ToLowerInvariant()
    $bytes = [System.IO.File]::ReadAllBytes($item.Source)
    if ($textExtensions -contains $extension) {
        $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $text = $utf8.GetString($bytes, $(if ($bom) { 3 } else { 0 }), $bytes.Length - $(if ($bom) { 3 } else { 0 }))
        $text = Convert-Text $text $item.Target.StartsWith('tests/', [StringComparison]::Ordinal)
        $body = $utf8.GetBytes($text)
        $bytes = if ($bom) { [byte[]]@(@(0xEF, 0xBB, 0xBF) + $body) } else { $body }
    }
    if (Test-Path -LiteralPath $targetPath) {
        $existing = [System.IO.File]::ReadAllBytes($targetPath)
        if ($textExtensions -contains $extension) {
            # Git may check text files out with other line endings; only a content change counts.
            $same = $utf8.GetString($existing).Replace("`r`n", "`n") -ceq $utf8.GetString($bytes).Replace("`r`n", "`n")
        }
        else {
            $same = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($existing)) -eq
                [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
        }
        if ($same) { $unchanged++; continue }
        $changed.Add($item.Target)
    }
    else { $added.Add($item.Target) }
    if (-not $WhatIf) {
        [void](New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($targetPath)))
        [System.IO.File]::WriteAllBytes($targetPath, $bytes)
    }
}

Write-Output "=== Looking for copies whose source is gone"
$removed = [System.Collections.Generic.List[string]]::new()
$targetRoots = @($map.folders | ForEach-Object { $_.to }) + @($map.files | ForEach-Object { [System.IO.Path]::GetDirectoryName($_.to) | ForEach-Object { ConvertTo-Slash $_ } })
foreach ($root in ($targetRoots | Sort-Object -Unique)) {
    $rootPath = Join-Path $repo $root
    if (-not (Test-Path -LiteralPath $rootPath)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $rootPath -Recurse -File) {
        $relative = ConvertTo-Slash ([System.IO.Path]::GetRelativePath($repo, $file.FullName))
        if ($relative -match '/(bin|obj)/') { continue }
        if ($planned.Contains($relative) -or $owned.Contains($relative)) { continue }
        if ($removed.Contains($relative)) { continue }
        $removed.Add($relative)
        if (-not $WhatIf) { Remove-Item -LiteralPath $file.FullName }
    }
}

$commit = (& git -C $Source rev-parse HEAD 2>$null)
$dirty = [bool](& git -C $Source status --porcelain 2>$null)
if (-not $WhatIf) {
    $record = [ordered]@{
        sourceCommit = $commit
        sourceDirty = $dirty
        syncedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        files = $planned.Count
    }
    $json = ($record | ConvertTo-Json) + "`n"
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'last-sync.json'), $json, $utf8)
}

Write-Output ""
Write-Output "=== Summary"
Write-Output "Source commit: $commit$(if ($dirty) { ' (working tree has uncommitted changes)' })"
Write-Output "New:       $($added.Count)"
foreach ($item in $added) { Write-Output "  + $item" }
Write-Output "Changed:   $($changed.Count)"
foreach ($item in $changed) { Write-Output "  ~ $item" }
Write-Output "Deleted:   $($removed.Count)"
foreach ($item in $removed) { Write-Output "  - $item" }
Write-Output "Unchanged: $unchanged"
Write-Output "Skipped:   $($skipped.Count)"
foreach ($item in $skipped) { Write-Output "  . $item" }
if ($WhatIf) { Write-Output "Nothing was written (-WhatIf)." }
