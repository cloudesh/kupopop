param([switch]$Local)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:KUPOPOP_ROOT = $PSScriptRoot
$taskServer = Join-Path $PSScriptRoot 'Kupopop.Server.exe'
if (-not (Test-Path -LiteralPath $taskServer)) { throw 'Extract the complete Windows server ZIP first.' }

if ($Local) { & $taskServer --local --trust-local; exit $LASTEXITCODE }
$taskHostFile = Join-Path $PSScriptRoot 'hostname.txt'
$taskHostname = ''
if (Test-Path -LiteralPath $taskHostFile) { $taskHostname = ([IO.File]::ReadAllText($taskHostFile)).Trim() }
if (-not $taskHostname) {
    Write-Host 'Kupo Skribbl server - Windows x64'
    Write-Host 'For public hosting, point a DNS hostname here and allow inbound TCP ports 80 and 443.'
    $taskHostname = Read-Host 'Public DNS hostname (press Enter for local testing only)'
    if (-not $taskHostname) { & $taskServer --local --trust-local; exit $LASTEXITCODE }
}
if ($taskHostname -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]*[a-zA-Z0-9]$' -or
    [Uri]::CheckHostName($taskHostname) -ne [UriHostNameType]::Dns -or $taskHostname -eq 'localhost') {
    throw 'Enter a public DNS hostname, without https://, a port, path, or IP address.'
}
[IO.File]::WriteAllText($taskHostFile, $taskHostname, (New-Object Text.UTF8Encoding($false)))
$taskVersion = '2.11.7'
$taskBaseUrl = "https://github.com/caddyserver/caddy/releases/download/v$taskVersion"
$taskCaddy = Join-Path $PSScriptRoot 'caddy.exe'
if (-not (Test-Path -LiteralPath $taskCaddy)) {
    Write-Host "Downloading Caddy $taskVersion from its official GitHub release..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $taskChecks = Join-Path $PSScriptRoot 'caddy-checksums.txt'
    $taskArchive = Join-Path $PSScriptRoot "caddy_${taskVersion}_windows_amd64.zip"
    Invoke-WebRequest "$taskBaseUrl/caddy_${taskVersion}_checksums.txt" -OutFile $taskChecks -UseBasicParsing
    if ((Get-FileHash -LiteralPath $taskChecks -Algorithm SHA256).Hash.ToLowerInvariant() -ne '5d27b76b95f496638d79208c55250317b8755fd9e90005f7466aa32956944a90') {
        throw 'Official Caddy checksum manifest verification failed.'
    }
    Invoke-WebRequest "$taskBaseUrl/caddy_${taskVersion}_windows_amd64.zip" -OutFile $taskArchive -UseBasicParsing
    $taskExpected = ((Get-Content -LiteralPath $taskChecks | Where-Object { $_ -match "caddy_${taskVersion}_windows_amd64.zip$" }) -split '\s+')[0]
    if (-not $taskExpected -or (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) {
        throw 'Caddy archive verification failed.'
    }
    Expand-Archive -LiteralPath $taskArchive -DestinationPath (Join-Path $PSScriptRoot 'caddy-download') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'caddy-download\caddy.exe') -Destination $taskCaddy
}
$taskConfig = "$taskHostname {`n    reverse_proxy 127.0.0.1:5080`n}`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'Caddyfile'), $taskConfig, (New-Object Text.UTF8Encoding($false)))
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'logs') -Force | Out-Null
Write-Host "Plugin server URL: https://$taskHostname"
Write-Host 'Caddy manages trusted certificates. Keep this window open; Ctrl+C stops the game server.'
$taskProxy = Start-Process -FilePath $taskCaddy -ArgumentList @('run','--config','Caddyfile','--adapter','caddyfile') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'logs\caddy-out.log') -RedirectStandardError (Join-Path $PSScriptRoot 'logs\caddy-error.log')
try {
    Start-Sleep -Seconds 1
    if ($taskProxy.HasExited) { throw 'Caddy stopped. Check logs/caddy-error.log.' }
    & $taskServer --proxy
    if ($LASTEXITCODE -ne 0) { throw "Game server exited with code $LASTEXITCODE." }
}
finally { if (-not $taskProxy.HasExited) { Stop-Process -Id $taskProxy.Id } }
