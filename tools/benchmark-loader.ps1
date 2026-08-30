[CmdletBinding()]
param(
    [string] $GameRoot = 'G:\Steam\steamapps\common\RimWorld',
    [ValidateRange(1, 10)]
    [int] $Runs = 2,
    [ValidateNotNullOrEmpty()]
    [string] $Variant = 'baseline',
    [switch] $Detailed,
    [switch] $ProfileTextureLoad,
    [switch] $DisableTextureCompression,
    [switch] $DdsCache,
    [string] $DdsCacheRoot,
    [switch] $ProfileTexturePaths,
    [switch] $ProfileFileDiscovery,
    [ValidateNotNullOrEmpty()]
    [string] $MonitorName = 'G276HL',
    [ValidateRange(1, 8)]
    [int] $Monitor = 2,
    [switch] $Minimized,
    [ValidateRange(30, 600)]
    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'rimworld-window.ps1')

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$gameExe = Join-Path $GameRoot 'RimWorldWin64.exe'
$fixtureConfig = Join-Path $workspaceRoot 'benchmarks\saves\spoon-spring-v1-ModsConfig.xml'
$resultsPath = Join-Path $workspaceRoot 'benchmarks\results.csv'
$captureRoot = Join-Path $workspaceRoot 'profiling\captures\loader'
if (-not $DdsCacheRoot) {
    $DdsCacheRoot = Join-Path $workspaceRoot 'profiling\cache\dds-v1'
}
$liveConfigRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture

function Get-LoaderMetrics {
    param([string] $LogPath)

    $totalMilliseconds = $null
    $textures = @()

    foreach ($line in Get-Content -LiteralPath $LogPath) {
        if ($line -match '^\s+(?<ms>[0-9]+(?:\.[0-9]+)?)ms .*ExecuteToExecuteWhenFinished\(\)$') {
            $totalMilliseconds = [double]::Parse($matches.ms, $invariantCulture)
            continue
        }

        if ($line -match '^1x Loading assets of type UnityEngine\.Texture2D for mod (?<mod>.+?) -> (?<ms>[0-9]+(?:\.[0-9]+)?) ms') {
            $textures += [pscustomobject]@{
                Mod = $matches.mod
                Ms = [double]::Parse($matches.ms, $invariantCulture)
            }
        }
    }

    if ($null -eq $totalMilliseconds) {
        throw "Total duration was not found in the log: $LogPath"
    }

    if ($textures.Count -eq 0) {
        throw "Texture timings were not found in the log: $LogPath"
    }

    [pscustomobject]@{
        TotalMilliseconds = $totalMilliseconds
        TextureMilliseconds = ($textures | Measure-Object -Property Ms -Sum).Sum
        Textures = $textures
    }
}

function Get-TextureLoadProfile {
    param([string] $LogPath)

    $profileLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] Texture loader profile: files=(?<files>[0-9]+); bytes=(?<bytes>[0-9]+); totalMs=(?<total>[0-9]+(?:\.[0-9]+)?); readMs=(?<read>[0-9]+(?:\.[0-9]+)?); processingMs=(?<processing>[0-9]+(?:\.[0-9]+)?)$'
    ) | Select-Object -Last 1

    if ($null -eq $profileLine) {
        throw "Texture load profile was not found in the log: $LogPath"
    }

    $mainThreadLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] Texture main-thread profile: loadImageCalls=(?<loadImageCalls>[0-9]+); loadImageMs=(?<loadImage>[0-9]+(?:\.[0-9]+)?); applyCalls=(?<applyCalls>[0-9]+); applyMs=(?<apply>[0-9]+(?:\.[0-9]+)?); fastCompressCalls=(?<fastCompressCalls>[0-9]+); fastCompressMs=(?<fastCompress>[0-9]+(?:\.[0-9]+)?); otherMs=(?<other>[0-9]+(?:\.[0-9]+)?)$'
    ) | Select-Object -Last 1

    if ($null -eq $mainThreadLine) {
        throw "Main-thread texture profile was not found in the log: $LogPath"
    }

    $ddsLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] DDS loader profile: files=(?<ddsFiles>[0-9]+); bytes=(?<ddsBytes>[0-9]+); totalMs=(?<dds>[0-9]+(?:\.[0-9]+)?)$'
    ) | Select-Object -Last 1

    if ($null -eq $ddsLine) {
        throw "DDS load profile was not found in the log: $LogPath"
    }

    $match = [regex]::Match($profileLine.Line, $profileLine.Pattern)
    $mainThreadMatch = [regex]::Match($mainThreadLine.Line, $mainThreadLine.Pattern)
    $ddsMatch = [regex]::Match($ddsLine.Line, $ddsLine.Pattern)
    [pscustomobject]@{
        Files = [long]::Parse($match.Groups['files'].Value, $invariantCulture)
        Bytes = [long]::Parse($match.Groups['bytes'].Value, $invariantCulture)
        TotalMilliseconds = [double]::Parse($match.Groups['total'].Value, $invariantCulture)
        ReadMilliseconds = [double]::Parse($match.Groups['read'].Value, $invariantCulture)
        ProcessingMilliseconds = [double]::Parse($match.Groups['processing'].Value, $invariantCulture)
        LoadImageCalls = [long]::Parse($mainThreadMatch.Groups['loadImageCalls'].Value, $invariantCulture)
        LoadImageMilliseconds = [double]::Parse($mainThreadMatch.Groups['loadImage'].Value, $invariantCulture)
        ApplyCalls = [long]::Parse($mainThreadMatch.Groups['applyCalls'].Value, $invariantCulture)
        ApplyMilliseconds = [double]::Parse($mainThreadMatch.Groups['apply'].Value, $invariantCulture)
        FastCompressCalls = [long]::Parse($mainThreadMatch.Groups['fastCompressCalls'].Value, $invariantCulture)
        FastCompressMilliseconds = [double]::Parse($mainThreadMatch.Groups['fastCompress'].Value, $invariantCulture)
        OtherMilliseconds = [double]::Parse($mainThreadMatch.Groups['other'].Value, $invariantCulture)
        DdsFiles = [long]::Parse($ddsMatch.Groups['ddsFiles'].Value, $invariantCulture)
        DdsBytes = [long]::Parse($ddsMatch.Groups['ddsBytes'].Value, $invariantCulture)
        DdsMilliseconds = [double]::Parse($ddsMatch.Groups['dds'].Value, $invariantCulture)
    }
}

function Get-DdsCacheProfile {
    param([string] $LogPath)

    $profileLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] DDS cache profile: hits=(?<hits>[0-9]+); misses=(?<misses>[0-9]+)$'
    ) | Select-Object -Last 1
    if ($null -eq $profileLine) {
        throw "DDS cache profile was not found in the log: $LogPath"
    }

    $match = [regex]::Match($profileLine.Line, $profileLine.Pattern)
    $buildLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] DDS cache build: created=(?<created>[0-9]+); invalidated=(?<invalidated>[0-9]+); excluded=(?<excluded>[0-9]+); unsupported=(?<unsupported>[0-9]+); budgetSkipped=(?<budgetSkipped>[0-9]+); failed=(?<failed>[0-9]+); buildMs=(?<build>[0-9]+); cacheBytes=(?<cacheBytes>[0-9]+); maxCacheBytes=(?<maxCacheBytes>[0-9]+)$'
    ) | Select-Object -Last 1
    if ($null -eq $buildLine) {
        throw "DDS cache build profile was not found in the log: $LogPath"
    }

    $buildMatch = [regex]::Match($buildLine.Line, $buildLine.Pattern)
    [pscustomobject]@{
        Hits = [long]::Parse($match.Groups['hits'].Value, $invariantCulture)
        Misses = [long]::Parse($match.Groups['misses'].Value, $invariantCulture)
        Created = [long]::Parse($buildMatch.Groups['created'].Value, $invariantCulture)
        Invalidated = [long]::Parse($buildMatch.Groups['invalidated'].Value, $invariantCulture)
        Excluded = [long]::Parse($buildMatch.Groups['excluded'].Value, $invariantCulture)
        Unsupported = [long]::Parse($buildMatch.Groups['unsupported'].Value, $invariantCulture)
        BudgetSkipped = [long]::Parse($buildMatch.Groups['budgetSkipped'].Value, $invariantCulture)
        Failed = [long]::Parse($buildMatch.Groups['failed'].Value, $invariantCulture)
        BuildMilliseconds = [long]::Parse($buildMatch.Groups['build'].Value, $invariantCulture)
        CacheBytes = [long]::Parse($buildMatch.Groups['cacheBytes'].Value, $invariantCulture)
        MaxCacheBytes = [long]::Parse($buildMatch.Groups['maxCacheBytes'].Value, $invariantCulture)
    }
}

function Get-TexturePathProfile {
    param([string] $LogPath)

    $profileLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] Texture path profile: unique=(?<unique>[0-9]+); duplicatePaths=(?<duplicates>[0-9]+); potentiallyShadowedFiles=(?<shadowed>[0-9]+); potentiallyShadowedBytes=(?<bytes>[0-9]+); topShadowedMods=(?<mods>.*)$'
    ) | Select-Object -Last 1
    if ($null -eq $profileLine) {
        throw "Texture path profile was not found in the log: $LogPath"
    }

    $match = [regex]::Match($profileLine.Line, $profileLine.Pattern)
    [pscustomobject]@{
        Unique = [long]::Parse($match.Groups['unique'].Value, $invariantCulture)
        Duplicates = [long]::Parse($match.Groups['duplicates'].Value, $invariantCulture)
        Shadowed = [long]::Parse($match.Groups['shadowed'].Value, $invariantCulture)
        Bytes = [long]::Parse($match.Groups['bytes'].Value, $invariantCulture)
        TopMods = $match.Groups['mods'].Value
    }
}

function Get-FileDiscoveryProfile {
    param([string] $LogPath)

    $profileLine = Select-String -LiteralPath $LogPath -Pattern (
        '^\[RimWorldOptim\.Poc\] File discovery profile: calls=(?<calls>[0-9]+); files=(?<files>[0-9]+); totalMs=(?<total>[0-9]+(?:\.[0-9]+)?); textureCalls=(?<textureCalls>[0-9]+); textureFiles=(?<textureFiles>[0-9]+); textureMs=(?<texture>[0-9]+(?:\.[0-9]+)?)$'
    ) | Select-Object -Last 1
    if ($null -eq $profileLine) {
        throw "File discovery profile was not found in the log: $LogPath"
    }

    $match = [regex]::Match($profileLine.Line, $profileLine.Pattern)
    [pscustomobject]@{
        Calls = [long]::Parse($match.Groups['calls'].Value, $invariantCulture)
        Files = [long]::Parse($match.Groups['files'].Value, $invariantCulture)
        TotalMilliseconds = [double]::Parse($match.Groups['total'].Value, $invariantCulture)
        TextureCalls = [long]::Parse($match.Groups['textureCalls'].Value, $invariantCulture)
        TextureFiles = [long]::Parse($match.Groups['textureFiles'].Value, $invariantCulture)
        TextureMilliseconds = [double]::Parse($match.Groups['texture'].Value, $invariantCulture)
    }
}

if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
    throw "RimWorld was not found: $gameExe"
}

if (-not (Test-Path -LiteralPath $fixtureConfig -PathType Leaf)) {
    throw "Baseline mod configuration was not found: $fixtureConfig"
}

if (-not (Test-Path -LiteralPath $resultsPath -PathType Leaf)) {
    throw "Results file was not found: $resultsPath"
}

if (-not (Test-Path -LiteralPath $liveConfigRoot -PathType Container)) {
    throw "Current RimWorld configuration was not found: $liveConfigRoot"
}

if ($DdsCache) {
    New-Item -ItemType Directory -Path $ddsCacheRoot -Force | Out-Null
}

if (Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue) {
    throw 'RimWorld is already running. The benchmark will not stop an unrelated instance.'
}

[xml] $modsConfig = Get-Content -LiteralPath $fixtureConfig
$activeModCount = @($modsConfig.ModsConfigData.activeMods.li).Count
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null

for ($run = 1; $run -le $Runs; $run++) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $phase = if ($DdsCache) { 'L3' } elseif ($ProfileTextureLoad) { 'L1' } else { 'L0' }
    $id = "$phase-$Variant-$timestamp-r$run"
    $runRoot = Join-Path $captureRoot $id
    $userDataRoot = Join-Path $runRoot 'userdata'
    $configRoot = Join-Path $userDataRoot 'Config'
    $logPath = Join-Path $runRoot 'Player.log'

    New-Item -ItemType Directory -Path $userDataRoot -Force | Out-Null
    Copy-Item -LiteralPath $liveConfigRoot -Destination $userDataRoot -Recurse
    Copy-Item -LiteralPath $fixtureConfig -Destination (Join-Path $configRoot 'ModsConfig.xml') -Force

    $prefsPath = Join-Path $configRoot 'Prefs.xml'
    [xml] $prefs = Get-Content -LiteralPath $prefsPath
    $prefs.PrefsData.logVerbose = if ($Detailed) { 'True' } else { 'False' }
    $prefs.PrefsData.fullscreen = 'False'
    $prefs.PrefsData.textureCompression = if ($DisableTextureCompression) { 'False' } else { 'True' }
    $prefs.Save($prefsPath)

    $arguments = @(
        "-savedatafolder=$userDataRoot"
        '-logFile'
        $logPath
        '-popupwindow'
    )

    $process = $null
    $display = $null
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $completed = $false
    $completionWallMilliseconds = $null

    try {
        $windowStyle = if ($Minimized) { 'Minimized' } else { 'Maximized' }
        Write-Host "Loader benchmark $run/$Runs starts $($windowStyle.ToLowerInvariant()) on ${MonitorName}: $id"
        $environmentOverrides = [ordered]@{
            RIMWORLDOPTIM_DDS_CACHE = if ($DdsCache) { '1' } else { '0' }
            RIMWORLDOPTIM_PROFILE_TEXTURE_LOAD = if ($ProfileTextureLoad) { '1' } else { $null }
            RIMWORLDOPTIM_DDS_CACHE_ROOT = if ($DdsCache) { $ddsCacheRoot } else { $null }
            RIMWORLDOPTIM_PROFILE_TEXTURE_PATHS = if ($ProfileTexturePaths) { '1' } else { $null }
            RIMWORLDOPTIM_PROFILE_FILE_DISCOVERY = if ($ProfileFileDiscovery) { '1' } else { $null }
        }
        $previousEnvironment = @{}

        try {
            foreach ($environmentName in $environmentOverrides.Keys) {
                $previousEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable(
                    $environmentName,
                    [EnvironmentVariableTarget]::Process)
                [Environment]::SetEnvironmentVariable(
                    $environmentName,
                    $environmentOverrides[$environmentName],
                    [EnvironmentVariableTarget]::Process)
            }
            $launch = Start-RimWorldOnDisplay `
                -FilePath $gameExe `
                -WorkingDirectory $GameRoot `
                -ArgumentList $arguments `
                -MonitorName $MonitorName `
                -FallbackMonitor $Monitor `
                -Minimized:$Minimized
            $process = $launch.Process
            $display = $launch.Display
        }
        finally {
            foreach ($environmentName in $environmentOverrides.Keys) {
                [Environment]::SetEnvironmentVariable(
                    $environmentName,
                    $previousEnvironment[$environmentName],
                    [EnvironmentVariableTarget]::Process)
            }
        }

        while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                $completionMarker = if ($Detailed) {
                    '1x Static constructor calls ->'
                }
                else {
                    '[RimWorldOptim.Poc] Main menu ready.'
                }
                $completed = [bool](Select-String -LiteralPath $logPath `
                    -SimpleMatch $completionMarker -Quiet)
                if ($completed) {
                    $completionWallMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds)
                    break
                }
            }

            $process.Refresh()
            if ($process.HasExited) {
                throw "RimWorld exited before the loading phase completed. Log: $logPath"
            }

            Start-Sleep -Milliseconds 500
        }

        if (-not $completed) {
            throw "Loading phase did not complete within $TimeoutSeconds seconds. Log: $logPath"
        }

        Start-Sleep -Seconds 1
        $process.Refresh()
        $actualMonitorAtCompletion = if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            [RimWorldOptim.DisplayNative]::GetWindowMonitorDeviceName($process.MainWindowHandle)
        }
        else {
            $null
        }
        if (-not $Minimized -and $actualMonitorAtCompletion -ne $display.DeviceName) {
            $placement = Set-RimWorldWindowPlacement -Process $process -Display $display -Maximize
            $actualMonitorAtCompletion = $placement.DeviceName
        }

        $metrics = $null
        $totalMilliseconds = ''
        $textureMilliseconds = ''
        $topTextures = @()
        $profileNotes = @()

        if ($Detailed) {
            $metrics = Get-LoaderMetrics -LogPath $logPath
            $metrics.Textures |
                Sort-Object -Property Ms -Descending |
                ForEach-Object {
                    [pscustomobject]@{
                        Mod = $_.Mod
                        Ms = $_.Ms.ToString('0.###', $invariantCulture)
                    }
                } |
                Export-Csv -LiteralPath (Join-Path $runRoot 'textures.csv') `
                    -NoTypeInformation -Encoding UTF8

            $totalMilliseconds = $metrics.TotalMilliseconds.ToString('0.###', $invariantCulture)
            $textureMilliseconds = $metrics.TextureMilliseconds.ToString('0.###', $invariantCulture)
            $topTextures = $metrics.Textures |
                Sort-Object -Property Ms -Descending |
                Select-Object -First 3 |
                ForEach-Object {
                    $_.Mod + '=' + $_.Ms.ToString('0.###', $invariantCulture) + 'ms'
                }
        }

        if ($ProfileTextureLoad) {
            $profile = Get-TextureLoadProfile -LogPath $logPath
            $profileNotes = @(
                'textureFiles=' + $profile.Files.ToString($invariantCulture)
                'textureMB=' + ($profile.Bytes / 1MB).ToString('0.###', $invariantCulture)
                'textureTotalMs=' + $profile.TotalMilliseconds.ToString('0.###', $invariantCulture)
                'textureReadMs=' + $profile.ReadMilliseconds.ToString('0.###', $invariantCulture)
                'textureProcessingMs=' + $profile.ProcessingMilliseconds.ToString('0.###', $invariantCulture)
                'loadImageCalls=' + $profile.LoadImageCalls.ToString($invariantCulture)
                'loadImageMs=' + $profile.LoadImageMilliseconds.ToString('0.###', $invariantCulture)
                'applyCalls=' + $profile.ApplyCalls.ToString($invariantCulture)
                'applyMs=' + $profile.ApplyMilliseconds.ToString('0.###', $invariantCulture)
                'fastCompressCalls=' + $profile.FastCompressCalls.ToString($invariantCulture)
                'fastCompressMs=' + $profile.FastCompressMilliseconds.ToString('0.###', $invariantCulture)
                'textureOtherMs=' + $profile.OtherMilliseconds.ToString('0.###', $invariantCulture)
                'ddsFiles=' + $profile.DdsFiles.ToString($invariantCulture)
                'ddsMB=' + ($profile.DdsBytes / 1MB).ToString('0.###', $invariantCulture)
                'ddsLoadMs=' + $profile.DdsMilliseconds.ToString('0.###', $invariantCulture)
            )
        }

        if ($DdsCache) {
            $ddsProfile = Get-DdsCacheProfile -LogPath $logPath
            $profileNotes += @(
                'ddsCacheHits=' + $ddsProfile.Hits.ToString($invariantCulture)
                'ddsCacheMisses=' + $ddsProfile.Misses.ToString($invariantCulture)
                'ddsCacheCreated=' + $ddsProfile.Created.ToString($invariantCulture)
                'ddsCacheInvalidated=' + $ddsProfile.Invalidated.ToString($invariantCulture)
                'ddsCacheExcluded=' + $ddsProfile.Excluded.ToString($invariantCulture)
                'ddsCacheUnsupported=' + $ddsProfile.Unsupported.ToString($invariantCulture)
                'ddsCacheBudgetSkipped=' + $ddsProfile.BudgetSkipped.ToString($invariantCulture)
                'ddsCacheFailed=' + $ddsProfile.Failed.ToString($invariantCulture)
                'ddsCacheBuildMs=' + $ddsProfile.BuildMilliseconds.ToString($invariantCulture)
                'ddsCacheMB=' + ($ddsProfile.CacheBytes / 1MB).ToString('0.###', $invariantCulture)
                'ddsCacheMaxMB=' + ($ddsProfile.MaxCacheBytes / 1MB).ToString('0.###', $invariantCulture)
            )
        }

        if ($ProfileTexturePaths) {
            $pathProfile = Get-TexturePathProfile -LogPath $logPath
            $profileNotes += @(
                'uniqueTexturePaths=' + $pathProfile.Unique.ToString($invariantCulture)
                'duplicateTexturePaths=' + $pathProfile.Duplicates.ToString($invariantCulture)
                'potentiallyShadowedTextures=' + $pathProfile.Shadowed.ToString($invariantCulture)
                'potentiallyShadowedMB=' + ($pathProfile.Bytes / 1MB).ToString('0.###', $invariantCulture)
                'topShadowedMods=' + $pathProfile.TopMods
            )
        }

        if ($ProfileFileDiscovery) {
            $discoveryProfile = Get-FileDiscoveryProfile -LogPath $logPath
            $profileNotes += @(
                'discoveryCalls=' + $discoveryProfile.Calls.ToString($invariantCulture)
                'discoveredFiles=' + $discoveryProfile.Files.ToString($invariantCulture)
                'discoveryMs=' + $discoveryProfile.TotalMilliseconds.ToString('0.###', $invariantCulture)
                'textureDiscoveryCalls=' + $discoveryProfile.TextureCalls.ToString($invariantCulture)
                'textureFilesDiscovered=' + $discoveryProfile.TextureFiles.ToString($invariantCulture)
                'textureDiscoveryMs=' + $discoveryProfile.TextureMilliseconds.ToString('0.###', $invariantCulture)
            )
        }

        $relevantErrors = @(
            Select-String -LiteralPath $logPath -Pattern @(
                'Exception while patching'
                'Could not load file or assembly'
                'MissingMethodException'
                'TypeLoadException'
                'Root level exception'
            )
        )

        $result = if ($relevantErrors.Count -eq 0) { 'valid' } else { 'invalid' }
        $record = [pscustomobject][ordered]@{
            id = $id
            track = 'loader'
            variant = $Variant
            run = $run.ToString($invariantCulture)
            build = '1.6.4871 rev591'
            fixture = ''
            wall_ms = $completionWallMilliseconds.ToString($invariantCulture)
            total_ms = $totalMilliseconds
            texture_ms = $textureMilliseconds
            tps = ''
            fps = ''
            result = $result
            notes = (@(
                "activeMods=$activeModCount"
                "detailed=$($Detailed.IsPresent)"
                "profileTextureLoad=$($ProfileTextureLoad.IsPresent)"
                "textureCompression=$(-not $DisableTextureCompression.IsPresent)"
                "ddsCache=$($DdsCache.IsPresent)"
                "profileTexturePaths=$($ProfileTexturePaths.IsPresent)"
                "profileFileDiscovery=$($ProfileFileDiscovery.IsPresent)"
                "monitorName=$($display.FriendlyName)"
                "monitor=$($display.UnityMonitor)"
                "monitorDevice=$($display.DeviceName)"
                "monitorActual=$actualMonitorAtCompletion"
                "monitorFallback=$($display.UsedFallback)"
                "windowStyle=$windowStyle"
                "relevantErrors=$($relevantErrors.Count)"
                "topTextures=$($topTextures -join '|')"
            ) + $profileNotes) -join '; '
        }

        $record | Export-Csv -LiteralPath $resultsPath -Append -NoTypeInformation -Encoding UTF8
        $record
    }
    finally {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                $null = $process.CloseMainWindow()
                if (-not $process.WaitForExit(10000)) {
                    Stop-Process -Id $process.Id
                }
            }
        }
    }
}
