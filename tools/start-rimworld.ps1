[CmdletBinding()]
param(
    [string] $GameRoot = 'G:\Steam\steamapps\common\RimWorld',
    [ValidateNotNullOrEmpty()]
    [string] $MonitorName = 'G276HL',
    [ValidateRange(1, 16)]
    [int] $FallbackMonitor = 2,
    [switch] $Minimized
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'rimworld-window.ps1')

$gameExe = Join-Path $GameRoot 'RimWorldWin64.exe'
if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw "RimWorld was not found: $gameExe"
}

if (Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw 'RimWorld is already running.'
}

$launch = Start-RimWorldOnDisplay `
    -FilePath $gameExe `
    -WorkingDirectory $GameRoot `
    -ArgumentList '-popupwindow' `
    -MonitorName $MonitorName `
    -FallbackMonitor $FallbackMonitor `
    -Minimized:$Minimized

Write-Host "RimWorld is running on $($launch.Display.FriendlyName) ($($launch.ActualDeviceName)), $($launch.WindowStyle.ToLowerInvariant())."
$launch.Process
