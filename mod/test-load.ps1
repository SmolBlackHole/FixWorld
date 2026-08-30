[CmdletBinding()]
param(
    [string] $GameRoot = 'G:\Steam\steamapps\common\RimWorld',
    [ValidateRange(10, 600)]
    [int] $TimeoutSeconds = 180,
    [ValidateNotNullOrEmpty()]
    [string] $MonitorName = 'G276HL',
    [switch] $DisableDdsCache,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workspaceRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $workspaceRoot 'tools\rimworld-window.ps1')

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
    throw "RimWorld was not found: $gameExe"
}

if (-not (Test-Path -LiteralPath $configTemplate -PathType Leaf)) {
    throw "The test configuration was not found: $configTemplate"
}

$link = Get-Item -LiteralPath $modLink -ErrorAction Stop
if ($link.LinkType -ne 'Junction' -or $link.Target -notcontains $modRoot) {
    throw "The mod junction does not point to $modRoot. Current target: $($link.Target -join ', ')"
}

if (Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw 'RimWorld is already running. The isolated test will not start another instance.'
}

New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
Copy-Item -LiteralPath $configTemplate -Destination $configPath -Force

$arguments = @(
    "-savedatafolder=$userDataRoot"
    '-logFile'
    $logPath
    '-quicktest'
    '-popupwindow'
)

$process = $null
$loaded = $false
$finalized = $false
$relevantErrors = @()
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $previousCacheSetting = [Environment]::GetEnvironmentVariable(
        'RIMWORLDOPTIM_DDS_CACHE',
        [EnvironmentVariableTarget]::Process)
    try {
        if ($DisableDdsCache) {
            [Environment]::SetEnvironmentVariable(
                'RIMWORLDOPTIM_DDS_CACHE',
                '0',
                [EnvironmentVariableTarget]::Process)
        }

        $launch = Start-RimWorldOnDisplay `
            -FilePath $gameExe `
            -WorkingDirectory $GameRoot `
            -ArgumentList $arguments `
            -MonitorName $MonitorName `
            -FallbackMonitor 2
        $process = $launch.Process
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            'RIMWORLDOPTIM_DDS_CACHE',
            $previousCacheSetting,
            [EnvironmentVariableTarget]::Process)
    }

    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $loaded = [bool](Select-String -LiteralPath $logPath -SimpleMatch '[RimWorldOptim.Poc] Loaded.' -Quiet)
            $finalized = [bool](Select-String -LiteralPath $logPath -SimpleMatch '[RimWorldOptim.Poc] Harmony PoC observed Game.FinalizeInit.' -Quiet)
            if ($finalized) {
                break
            }
        }

        if ($process.HasExited) {
            throw "RimWorld exited before FinalizeInit was observed. Log: $logPath"
        }

        Start-Sleep -Seconds 1
        $process.Refresh()
    }

    if (-not $loaded -or -not $finalized) {
        throw "Runtime test incomplete after $TimeoutSeconds seconds. Loaded=$loaded, Finalized=$finalized, Log=$logPath"
    }

    $relevantErrors = @(
        Select-String -LiteralPath $logPath -Pattern @(
            'Exception while patching'
            'Could not load file or assembly'
            'MissingMethodException'
            'TypeLoadException'
            '\[RimWorldOptim\.Poc\].*(error|exception)'
        )
    )

    if ($relevantErrors.Count -gt 0) {
        throw "The runtime test contains relevant errors. Log: $logPath"
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
