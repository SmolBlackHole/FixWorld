[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RimWorldRoot,

    [string] $ExpectedSha256 = '5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $RimWorldRoot 'RimWorldWin64_Data\Managed'
$sourceAssembly = Join-Path $managedPath 'Assembly-CSharp.dll'
$outputPath = Join-Path $projectRoot 'decompiled\Assembly-CSharp'
$ilspy = Join-Path $PSScriptRoot 'ilspycmd\11.0.0.9375\package\tools\net10.0\any\ilspycmd.dll'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

foreach ($requiredPath in @($sourceAssembly, $ilspy, $dotnet)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file is missing: $requiredPath"
    }
}

$actualSha256 = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
if ($actualSha256 -ne $ExpectedSha256) {
    throw "Assembly hash mismatch. Expected: $ExpectedSha256, actual: $actualSha256"
}

if (Test-Path -LiteralPath $outputPath) {
    throw "Output already exists and will not be overwritten: $outputPath"
}

New-Item -ItemType Directory -Path $outputPath | Out-Null

& $dotnet $ilspy `
    --disable-updatecheck `
    --nested-directories `
    --project `
    --outputdir $outputPath `
    --referencepath $managedPath `
    $sourceAssembly

if ($LASTEXITCODE -ne 0) {
    throw "ilspycmd exited with code $LASTEXITCODE."
}

$fileCount = (Get-ChildItem -LiteralPath $outputPath -Recurse -File).Count
Write-Output "Decompilation complete: $fileCount files"
Write-Output "Quelle: $sourceAssembly"
Write-Output "SHA-256: $actualSha256"
Write-Output "Output: $outputPath"
