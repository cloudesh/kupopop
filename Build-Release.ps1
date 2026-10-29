param([switch]$SkipPlugin, [switch]$SkipServers, [string]$Output = 'artifacts')
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
function Zip-Directory {
    param([string]$Source, [string]$Destination, [switch]$Linux)
    Add-Type -AssemblyName System.IO.Compression
    $taskZipStream = [IO.File]::Open($Destination, [IO.FileMode]::Create)
    $taskZip = New-Object IO.Compression.ZipArchive($taskZipStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskFile in Get-ChildItem -LiteralPath $Source -File -Recurse) {
            $taskName = $taskFile.FullName.Substring($Source.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\','/')
            $taskEntry = $taskZip.CreateEntry($taskName, [IO.Compression.CompressionLevel]::Optimal)
            if ($Linux -and ($taskName.EndsWith('.sh') -or $taskName -eq 'Kupopop.Server')) { $taskEntry.ExternalAttributes = -2115174400 }
            $taskInput = [IO.File]::OpenRead($taskFile.FullName)
            $taskDest = $taskEntry.Open()
            try { $taskInput.CopyTo($taskDest) } finally { $taskInput.Dispose(); $taskDest.Dispose() }
        }
    } finally { $taskZip.Dispose(); $taskZipStream.Dispose() }
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
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'plugin/releases/repo.json'), $taskJson, (New-Object Text.UTF8Encoding($false)))
    Copy-Item -LiteralPath 'plugin/releases/repo.json' -Destination (Join-Path $taskOutput 'repo.json') -Force
}
if (-not $SkipServers) {
    foreach ($taskRuntime in @('win-x64','linux-x64')) {
        $taskFolder = Join-Path $taskOutput "server-$taskRuntime"
        Invoke-Dotnet @('publish','server/Kupopop.Server/Kupopop.Server.csproj','-c','Release','-r',$taskRuntime,
            '--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:DebugType=None','-p:UseSharedCompilation=false','-m:1','-o',$taskFolder)
        if ($taskRuntime -eq 'win-x64') {
            Copy-Item -Path 'server/hosting/*.cmd','server/hosting/*.ps1' -Destination $taskFolder -Force
        } else { Copy-Item -Path 'server/hosting/*.sh' -Destination $taskFolder -Force }
        Copy-Item -LiteralPath 'server/HOSTING.md' -Destination (Join-Path $taskFolder 'README.md') -Force
        Copy-Item -LiteralPath 'LICENSE','THIRD_PARTY_NOTICES.md' -Destination $taskFolder -Force
        Zip-Directory -Source $taskFolder -Destination (Join-Path $taskOutput "Kupopop.Server-$taskRuntime.zip") -Linux:($taskRuntime -eq 'linux-x64')
    }
}
Write-Host "Release files: $taskOutput"
