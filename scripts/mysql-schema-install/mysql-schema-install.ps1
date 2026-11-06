param(
    [string]$MySql = 'mysql',
    [string]$Server = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 3306,
    [string]$User = 'root',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,63}$')][string]$Database = 'em_system_mysql'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
$sqlRoot = Join-Path $repo 'doc/sqlscript/mysql'
$clientArgs = @('--host', $Server, '--port', "$Port", '--user', $User, '--batch', '--skip-column-names')
$caseValue = & $MySql @clientArgs --execute 'SELECT @@lower_case_table_names'
if ($LASTEXITCODE -ne 0) { throw 'Cannot read MySQL server configuration.' }
if ("$caseValue".Trim() -ne '2') {
    throw 'Em System requires lower_case_table_names=2. Initialize a compatible MySQL instance with this setting, then retry.'
}
$existing = & $MySql @clientArgs --execute "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = '$Database'"
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing databases.' }
if ($existing) { throw "Database '$Database' already exists; refusing to overwrite it." }
& $MySql @clientArgs --execute "CREATE DATABASE ``$Database`` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci"
if ($LASTEXITCODE -ne 0) { throw 'Database creation failed.' }
try {
    $files = @('sets/000-case-policy.sql', 'sets/000-ulid.sql') +
        @(Get-ChildItem -LiteralPath (Join-Path $sqlRoot 'tables') -Filter '*.sql' -File | Sort-Object Name | ForEach-Object { 'tables/' + $_.Name })
    foreach ($file in $files) {
        Write-Host "Installing $file"
        Get-Content -Raw -LiteralPath (Join-Path $sqlRoot $file) | & $MySql @clientArgs $Database
        if ($LASTEXITCODE -ne 0) { throw "Installation failed at $file. Database retained for inspection." }
    }
    $actual = & $MySql @clientArgs $Database --execute "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND BINARY TABLE_NAME=BINARY 'ta_Meta' AND TABLE_TYPE='BASE TABLE'"
    if ($LASTEXITCODE -ne 0 -or $actual -cne 'ta_Meta') { throw "The server did not retain the exact table name ta_Meta. Inspect database '$Database'." }
    Write-Host "Installed tables in $Database; table name verified: $actual."
} catch { throw }
