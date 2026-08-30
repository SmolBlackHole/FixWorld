[CmdletBinding()]
param(
    [string] $DotNetPath
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Test-DotNetSdk([string] $Candidate) {
    if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
        return $false
    }

    $sdks = & $Candidate --list-sdks
    return $LASTEXITCODE -eq 0 -and @($sdks).Count -gt 0
}

if ($DotNetPath) {
    if (-not (Test-DotNetSdk $DotNetPath)) {
        throw "No usable .NET SDK found at: $DotNetPath"
    }
}
else {
    $candidates = [System.Collections.Generic.List[string]]::new()
    $pathCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($pathCommand) {
        $candidates.Add($pathCommand.Source)
    }

    $scoopCommand = Get-Command scoop -ErrorAction SilentlyContinue
    if ($scoopCommand) {
        $scoopPrefix = scoop prefix dotnet-sdk
        if ($LASTEXITCODE -eq 0 -and $scoopPrefix) {
            $candidates.Add((Join-Path $scoopPrefix 'dotnet.exe'))
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-DotNetSdk $candidate) {
            $DotNetPath = $candidate
            break
        }
    }

    if (-not $DotNetPath) {
        throw 'No .NET SDK found. Pass DotNetPath explicitly or install dotnet-sdk.'
    }
}

$project = Join-Path $PSScriptRoot 'RimWorldOptim.Poc\Source\RimWorldOptim.Poc.csproj'
& $DotNetPath build $project --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$assembly = Join-Path $PSScriptRoot 'RimWorldOptim.Poc\Assemblies\RimWorldOptim.Poc.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "The build succeeded, but the mod DLL is missing: $assembly"
}

$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
Write-Output "SDK: $DotNetPath"
Write-Output "Mod-DLL: $assembly"
Write-Output "SHA-256: $hash"
