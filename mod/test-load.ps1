[CmdletBinding()]
param(
    [string] $GameRoot = 'G:\Steam\steamapps\common\RimWorld',
    [ValidateRange(10, 600)]
    [int] $TimeoutSeconds = 180,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $PSScriptRoot 'RimWorldOptim.Poc'
$modLink = Join-Path $GameRoot 'Mods\RimWorldOptim.Poc'
$gameExe = Join-Path $GameRoot 'RimWorldWin64.exe'
$configTemplate = Join-Path $PSScriptRoot 'test-data\Config\ModsConfig.xml'
$userDataRoot = Join-Path $workspaceRoot 'profiling\poc-userdata'
$configRoot = Join-Path $userDataRoot 'Config'
$configPath = Join-Path $configRoot 'ModsConfig.xml'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logPath = Join-Path $userDataRoot "Player-$timestamp.log"

if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw "RimWorld wurde nicht gefunden: $gameExe"
}

if (-not (Test-Path -LiteralPath $configTemplate -PathType Leaf)) {
    throw "Testkonfiguration wurde nicht gefunden: $configTemplate"
}

$link = Get-Item -LiteralPath $modLink -ErrorAction Stop
if ($link.LinkType -ne 'Junction' -or $link.Target -notcontains $modRoot) {
    throw "Der Mod-Junction zeigt nicht auf $modRoot. Aktuelles Ziel: $($link.Target -join ', ')"
}

if (Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw 'RimWorld laeuft bereits. Der isolierte Test startet keine zweite Instanz.'
}

New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
Copy-Item -LiteralPath $configTemplate -Destination $configPath -Force

$arguments = @(
    "-savedatafolder=$userDataRoot"
    '-logFile'
    $logPath
    '-quicktest'
    '-popupwindow'
    '-screen-width'
    '1280'
    '-screen-height'
    '720'
)

$process = $null
$loaded = $false
$finalized = $false
$relevantErrors = @()
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $process = Start-Process -FilePath $gameExe -WorkingDirectory $GameRoot -ArgumentList $arguments -PassThru

    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $loaded = [bool](Select-String -LiteralPath $logPath -SimpleMatch '[RimWorldOptim.Poc] Loaded.' -Quiet)
            $finalized = [bool](Select-String -LiteralPath $logPath -SimpleMatch '[RimWorldOptim.Poc] Harmony PoC observed Game.FinalizeInit.' -Quiet)
            if ($finalized) {
                break
            }
        }

        if ($process.HasExited) {
            throw "RimWorld wurde vor dem FinalizeInit-Nachweis beendet. Log: $logPath"
        }

        Start-Sleep -Seconds 1
        $process.Refresh()
    }

    if (-not $loaded -or -not $finalized) {
        throw "Laufzeittest nach $TimeoutSeconds Sekunden nicht vollstaendig. Loaded=$loaded, Finalized=$finalized, Log=$logPath"
    }

    $relevantErrors = @(
        Select-String -LiteralPath $logPath -Pattern @(
            'Exception while patching'
            'Could not load file or assembly'
            'MissingMethodException'
            'TypeLoadException'
            '\[RimWorldOptim\.Poc\].*(error|exception|fail)'
        )
    )

    if ($relevantErrors.Count -gt 0) {
        throw "Der Laufzeittest enthaelt relevante Fehler. Log: $logPath"
    }
}
finally {
    if ($null -ne $process -and -not $KeepRunning) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
        }
    }
}

[pscustomobject]@{
    Loaded = $loaded
    Finalized = $finalized
    RelevantErrors = $relevantErrors.Count
    DurationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)
    Log = $logPath
}
