$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Server-Tools.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskTestRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ('.build\server-tools-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTestRoot -Force | Out-Null
function Assert-ServerTools {
    param([bool]$Condition, [string]$Description)
    if (-not $Condition) { throw "FAILED: $Description" }
    Write-Host "PASS $Description"
}

$taskLegacy = Join-Path $taskTestRoot 'legacy'
New-Item -ItemType Directory -Path (Join-Path $taskLegacy 'data') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $taskLegacy 'data\accounts.json'), 'fixture-old-profile')
$taskState = Initialize-LocalServerState -RuntimeRoot (Join-Path $taskTestRoot 'permanent') -LegacyRoot $taskLegacy
Assert-ServerTools ((Test-Path -LiteralPath (Join-Path $taskState 'data')) -and (Test-Path -LiteralPath (Join-Path $taskState 'logs'))) 'permanent data and log folders are created'
Assert-ServerTools (([IO.File]::ReadAllText((Join-Path $taskState 'data\accounts.json')) -eq 'fixture-old-profile') -and (Test-Path -LiteralPath (Join-Path $taskLegacy 'data\accounts.json'))) 'legacy data is copied without removing the original'
[IO.File]::WriteAllText((Join-Path $taskState 'data\accounts.json'), 'fixture-new-profile')
$null = Initialize-LocalServerState -RuntimeRoot $taskState -LegacyRoot $taskLegacy
Assert-ServerTools ([IO.File]::ReadAllText((Join-Path $taskState 'data\accounts.json')) -eq 'fixture-new-profile') 'subsequent starts preserve existing permanent data'

$taskPayload = Join-Path $taskTestRoot 'payload'
foreach ($taskDirectory in @('data','logs','local','caddy-download','nested')) {
    New-Item -ItemType Directory -Path (Join-Path $taskPayload $taskDirectory) -Force | Out-Null
}
foreach ($taskPublicFile in @('Kupopop.Server.exe','README.md','Start-Local.cmd')) {
    [IO.File]::WriteAllText((Join-Path $taskPayload $taskPublicFile), 'public-fixture')
}
foreach ($taskPrivateFile in @('data\accounts.json','data\localhost.pfx','logs\server.log','local\account.json',
    'caddy-download\caddy.exe','accounts.json','hostname.txt','Caddyfile','caddy.exe','caddy_2.11.7_windows_amd64.zip',
    'nested\device.dpapi','localhost.pfx','private.key')) {
    [IO.File]::WriteAllText((Join-Path $taskPayload $taskPrivateFile), 'private-fixture-never-package')
}
$taskZipPath = Join-Path $taskTestRoot 'server.zip'
Zip-Directory -Source $taskPayload -Destination $taskZipPath
$taskZip = [IO.Compression.ZipFile]::OpenRead($taskZipPath)
try {
    $taskNames = @($taskZip.Entries.FullName | Sort-Object)
    Assert-ServerTools (($taskNames -join ',') -eq 'Kupopop.Server.exe,README.md,Start-Local.cmd') 'release ZIP includes only application files and excludes runtime state, keys, logs, and proxy caches'
} finally { $taskZip.Dispose() }

foreach ($taskScript in @((Join-Path $PSScriptRoot 'Start-Local.ps1'),(Join-Path $PSScriptRoot 'Server-Tools.ps1'),(Join-Path (Split-Path -Parent $PSScriptRoot) 'Build-Release.ps1'))) {
    $taskErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($taskScript, [ref]$null, [ref]$taskErrors) | Out-Null
    Assert-ServerTools ($taskErrors.Count -eq 0) ('script parses: ' + [IO.Path]::GetFileName($taskScript))
}
Write-Host 'Server launcher/state/package checks passed.'
