param(
    [Parameter(Mandatory)][string]$MySqlBaseDir,
    [Parameter(Mandatory)][string]$DataDir,
    [ValidateSet(2)][int]$CaseMode = 2
)
$ErrorActionPreference = 'Stop'
$base = [IO.Path]::GetFullPath($MySqlBaseDir)
$data = [IO.Path]::GetFullPath($DataDir)
$serverName = if ($IsWindows) { 'mysqld.exe' } else { 'mysqld' }
$server = Join-Path (Join-Path $base 'bin') $serverName
if (-not (Test-Path -LiteralPath $server -PathType Leaf)) { throw "MySQL server executable not found: $server" }
if (Test-Path -LiteralPath $data) { throw "Data directory already exists; refusing to alter it: $data" }
if (-not $IsWindows) { throw 'The Em System standard requires lower_case_table_names=2; this initializer supports Windows only.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($data)) | Out-Null
Write-Host "Initializing a new MySQL data directory with lower_case_table_names=$CaseMode."
Write-Host 'The initial root account has no password. Configure credentials before exposing this server.'
& $server --no-defaults --initialize-insecure --console "--basedir=$base" "--datadir=$data" "--lower-case-table-names=$CaseMode"
if ($LASTEXITCODE -ne 0) { throw "Initialization failed. The data directory has been retained for inspection: $data" }
Write-Host 'Initialization complete. Start this instance using the same basedir, datadir and lower_case_table_names value.'
Write-Host "Then run mysql-schema-install.ps1 against the new instance to create the Em System database."
