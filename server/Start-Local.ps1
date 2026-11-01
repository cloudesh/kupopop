param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskProjectRoot
. (Join-Path $PSScriptRoot 'Server-Tools.ps1')
$taskBundle = Join-Path $taskProjectRoot 'artifacts\server-win-x64'
$taskExecutable = Join-Path $taskBundle 'Kupopop.Server.exe'
$taskRunning = @(Get-Process -Name 'Kupopop.Server' -ErrorAction SilentlyContinue)
if ($taskRunning.Count -gt 0) {
    throw 'A Kupopop server is already running. Stop its server window with Ctrl+C, then start this launcher again to update/restart it.'
}

$taskState = Initialize-LocalServerState -RuntimeRoot (Join-Path $PSScriptRoot 'local') -LegacyRoot $taskBundle
$env:KUPOPOP_ROOT = $taskState
$env:KUPOPOP_PORT = '7443'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
Write-Host 'Kupo Skribbl - My Local Server'
Write-Host 'Plugin server address: https://localhost:7443'
Write-Host "Profiles and certificates: $(Join-Path $taskState 'data')"
Write-Host "Logs: $(Join-Path $taskState 'logs')"
Write-Host ''

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    Write-Host 'Preparing the current server source (unchanged builds are incremental)...'
    & dotnet publish (Join-Path $PSScriptRoot 'Kupopop.Server\Kupopop.Server.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
        -p:UseSharedCompilation=false -m:1 -v minimal -o $taskBundle
    if ($LASTEXITCODE -ne 0) { throw 'Server build failed. The old server was not started. Review the build output above.' }
} elseif (-not (Test-Path -LiteralPath $taskExecutable)) {
    throw 'The server bundle is missing. Install the .NET 10 SDK to build this checkout, or build/download the Windows server package into artifacts/server-win-x64.'
} else {
    Write-Host 'The .NET SDK is unavailable; using the existing prebuilt server bundle.'
}
Remove-StandalonePublishExtras -Root $taskBundle
if ($BuildOnly) { Write-Host 'Local server ready. Build-only check complete; the server was not started.'; exit 0 }

Write-Host ''
Write-Host 'Keep this window open while playing. Press Ctrl+C to stop the server.'
Write-Host 'First run asks you to type TRUST for this Windows user''s localhost certificate.'
& $taskExecutable --local --trust-local
exit $LASTEXITCODE
