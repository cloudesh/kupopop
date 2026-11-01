param([switch]$SkipPlugin, [string]$Output = 'artifacts')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Output))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null


function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed: $($Arguments[0])" }
}
if (-not $SkipPlugin) {
    Invoke-Dotnet @('build','plugin/Kupopop/Kupopop.csproj','-c','Release','-p:UseSharedCompilation=false','-m:1')
    $taskPackaged = Join-Path $PSScriptRoot 'plugin/Kupopop/bin/Release/Kupopop/latest.zip'
    if (-not (Test-Path -LiteralPath $taskPackaged)) { throw 'DalamudPackager output was not found.' }
    Copy-Item -LiteralPath $taskPackaged -Destination (Join-Path $taskOutput 'testing.zip') -Force
    Copy-Item -LiteralPath $taskPackaged -Destination (Join-Path $PSScriptRoot 'plugin/releases/testing.zip') -Force
    $taskManifest = Get-Content -LiteralPath 'plugin/Kupopop/bin/Release/Kupopop.json' -Raw | ConvertFrom-Json
    $taskTag = 'v' + (($taskManifest.AssemblyVersion -split '\.')[0..2] -join '.')
    $taskLink = "https://github.com/cloudesh/kupopop/releases/download/$taskTag/testing.zip"
    $taskManifest | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $taskLink -Force
    $taskManifest | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue $taskLink -Force
    $taskManifest | Add-Member -NotePropertyName DownloadLinkTesting -NotePropertyValue $taskLink -Force
    $taskManifest | Add-Member -NotePropertyName IsHide -NotePropertyValue $false -Force
    $taskManifest | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $false -Force
    $taskManifest | Add-Member -NotePropertyName LastUpdate -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
    $taskJson = (ConvertTo-Json -InputObject @($taskManifest) -Depth 8).Replace("`r`n", "`n")
    # The live feed is promoted only after GitHub has uploaded and verified the
    # matching ZIP. Advertising a still-building release caused installer 404s.
    [IO.File]::WriteAllText((Join-Path $taskOutput 'repo.json'), $taskJson, (New-Object Text.UTF8Encoding($false)))
}
Write-Host "Release files: $taskOutput"
