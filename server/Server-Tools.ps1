function Initialize-LocalServerState {
    param([Parameter(Mandatory=$true)][string]$RuntimeRoot, [string]$LegacyRoot)
    $taskRoot = [IO.Path]::GetFullPath($RuntimeRoot)
    $taskData = Join-Path $taskRoot 'data'
    New-Item -ItemType Directory -Path $taskData,(Join-Path $taskRoot 'logs') -Force | Out-Null
    $taskExistingData = @(Get-ChildItem -LiteralPath $taskData -File -Force -Recurse | Select-Object -First 1)
    if ($LegacyRoot -and $taskExistingData.Count -eq 0) {
        $taskLegacyData = Join-Path $LegacyRoot 'data'
        if (Test-Path -LiteralPath $taskLegacyData -PathType Container) {
            foreach ($taskItem in Get-ChildItem -LiteralPath $taskLegacyData -Force) {
                Copy-Item -LiteralPath $taskItem.FullName -Destination $taskData -Recurse -Force
            }
            Write-Host 'Copied existing local server data to the permanent folder. The original copy was kept.'
        }
    }
    return $taskRoot
}

function Zip-Directory {
    param([string]$Source, [string]$Destination, [switch]$Linux)
    # Resolve literal input before deriving entry names so relative paths cannot
    # break archive-root exclusions for private runtime folders.
    $taskSourceRoot = (Get-Item -LiteralPath $Source -ErrorAction Stop).FullName
    Add-Type -AssemblyName System.IO.Compression
    $taskZipStream = [IO.File]::Open($Destination, [IO.FileMode]::Create)
    $taskZip = New-Object IO.Compression.ZipArchive($taskZipStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        # Only standalone application files and our source-controlled templates.
        # Unknown backups/state are excluded even when they have no extension.
        $taskAllowed = @('README.md','WORD_LISTS.md','LICENSE','THIRD_PARTY_NOTICES.md',
            'deploy/Dockerfile','deploy/.dockerignore','deploy/compose.yaml','deploy/Caddyfile.example')
        if ($Linux) { $taskAllowed += @('Kupopop.Server','start-server.sh','start-local.sh') }
        else { $taskAllowed += @('Kupopop.Server.exe','Start-Server.cmd','Start-Local.cmd','Start-Server.ps1') }
        foreach ($taskName in $taskAllowed) {
            $taskFile = Get-Item -LiteralPath (Join-Path $taskSourceRoot $taskName) -ErrorAction SilentlyContinue
            if (-not $taskFile -or $taskFile.PSIsContainer) { continue }
            $taskEntry = $taskZip.CreateEntry($taskName, [IO.Compression.CompressionLevel]::Optimal)
            if ($Linux -and ($taskName.EndsWith('.sh') -or $taskName -eq 'Kupopop.Server')) { $taskEntry.ExternalAttributes = -2115174400 }
            $taskInput = [IO.File]::OpenRead($taskFile.FullName)
            $taskDest = $taskEntry.Open()
            try { $taskInput.CopyTo($taskDest) } finally { $taskInput.Dispose(); $taskDest.Dispose() }
        }
    } finally { $taskZip.Dispose(); $taskZipStream.Dispose() }
}

function Remove-StandalonePublishExtras {
    param([Parameter(Mandatory=$true)][string]$Root)
    # The packaged server does not serve static assets or run behind IIS.
    foreach ($taskName in @('web.config','Kupopop.Server.staticwebassets.endpoints.json')) {
        $taskPath = Join-Path ([IO.Path]::GetFullPath($Root)) $taskName
        if (Test-Path -LiteralPath $taskPath -PathType Leaf) { Remove-Item -LiteralPath $taskPath -Force }
    }
}
