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
        foreach ($taskFile in Get-ChildItem -LiteralPath $taskSourceRoot -File -Recurse) {
            $taskName = $taskFile.FullName.Substring($taskSourceRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\','/')
            # Never ship a running host's state, credentials, TLS keys, logs, or proxy cache.
            if ($taskName -match '^(data|logs|local|caddy-download)(/|$)' -or
                $taskName -match '(^|/)(accounts\.json(?:\.tmp)?|word-lists\.json(?:\.tmp)?|hostname\.txt|Caddyfile)$' -or
                $taskName -match '(^|/)caddy([_.-]|$)' -or
                $taskName -match '\.(pfx|cer|pem|key|dpapi|log)(\..*)?$') { continue }
            $taskEntry = $taskZip.CreateEntry($taskName, [IO.Compression.CompressionLevel]::Optimal)
            if ($Linux -and ($taskName.EndsWith('.sh') -or $taskName -eq 'Kupopop.Server')) { $taskEntry.ExternalAttributes = -2115174400 }
            $taskInput = [IO.File]::OpenRead($taskFile.FullName)
            $taskDest = $taskEntry.Open()
            try { $taskInput.CopyTo($taskDest) } finally { $taskInput.Dispose(); $taskDest.Dispose() }
        }
    } finally { $taskZip.Dispose(); $taskZipStream.Dispose() }
}
