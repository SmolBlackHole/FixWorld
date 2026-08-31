#requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$Delete,
    [string]$CacheRoot
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$cacheDirectoryName = 'dds-v1'
$hashDirectoryPattern = '^[0-9a-f]{64}$'
$stagingDirectoryPattern = '^\.staging-[0-9]+-[0-9a-f]{32}$'
$comparison = [StringComparison]::OrdinalIgnoreCase

function Test-ReparsePoint {
    param([System.IO.FileSystemInfo]$Item)

    return ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
}

function Resolve-CacheRoot {
    param([string]$RequestedPath)

    $expanded = [Environment]::ExpandEnvironmentVariables($RequestedPath)
    $resolved = [IO.Path]::GetFullPath($expanded).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ([IO.Path]::GetFileName($resolved) -ine $cacheDirectoryName) {
        throw "Refusing cache root that does not end in '$cacheDirectoryName': $resolved"
    }

    $anchor = [IO.Path]::GetPathRoot($resolved).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $userProfilePath = [IO.Path]::GetFullPath(
        [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ($resolved.Equals($anchor, $comparison) -or
        $resolved.Equals($userProfilePath, $comparison) -or
        $userProfilePath.StartsWith($resolved + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "Refusing broad cache root: $resolved"
    }

    $relativePath = $resolved.Substring([IO.Path]::GetPathRoot($resolved).Length)
    $segments = $relativePath.Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -lt 3) {
        throw "Refusing unusually broad cache root: $resolved"
    }

    return $resolved
}

function Assert-WithinCache {
    param(
        [string]$Root,
        [string]$Candidate
    )

    $prefix = $Root.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($Candidate)
    if (-not $resolved.StartsWith($prefix, $comparison)) {
        throw "Path escapes the FixWorld cache: $resolved"
    }
}

function Add-UnknownTree {
    param(
        [string]$Root,
        [System.IO.DirectoryInfo]$Directory,
        [ref]$UnknownFiles
    )

    $pending = New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
    $pending.Push($Directory)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        Assert-WithinCache $Root $current.FullName
        foreach ($entry in Get-ChildItem -LiteralPath $current.FullName -Force) {
            Assert-WithinCache $Root $entry.FullName
            if (Test-ReparsePoint $entry) {
                throw "Refusing reparse point inside cache: $($entry.FullName)"
            }

            if ($entry -is [System.IO.DirectoryInfo]) {
                $pending.Push($entry)
            }
            elseif ($entry -is [System.IO.FileInfo]) {
                $UnknownFiles.Value++
            }
            else {
                throw "Refusing non-regular cache entry: $($entry.FullName)"
            }
        }
    }
}

function Add-StagingTree {
    param(
        [string]$Root,
        [System.IO.DirectoryInfo]$Directory,
        [System.Collections.Generic.List[System.IO.FileInfo]]$Files,
        [System.Collections.Generic.List[System.IO.DirectoryInfo]]$Directories
    )

    $pending = New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
    $pending.Push($Directory)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        Assert-WithinCache $Root $current.FullName
        $Directories.Add($current)
        foreach ($entry in Get-ChildItem -LiteralPath $current.FullName -Force) {
            Assert-WithinCache $Root $entry.FullName
            if (Test-ReparsePoint $entry) {
                throw "Refusing reparse point inside cache: $($entry.FullName)"
            }

            if ($entry -is [System.IO.DirectoryInfo]) {
                $pending.Push($entry)
            }
            elseif ($entry -is [System.IO.FileInfo]) {
                $Files.Add($entry)
            }
            else {
                throw "Refusing non-regular cache entry: $($entry.FullName)"
            }
        }
    }
}

function Format-ByteSize {
    param([long]$Bytes)

    $units = @('B', 'KiB', 'MiB', 'GiB', 'TiB')
    $value = [double]$Bytes
    foreach ($unit in $units) {
        if ($value -lt 1024.0 -or $unit -eq $units[-1]) {
            return '{0:N2} {1}' -f $value, $unit
        }
        $value /= 1024.0
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        $CacheRoot = Join-Path $userProfile 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\FixWorld\TextureCache\dds-v1'
    }

    $resolvedRoot = Resolve-CacheRoot $CacheRoot
    $ddsFiles = New-Object 'System.Collections.Generic.List[System.IO.FileInfo]'
    $stagingFiles = New-Object 'System.Collections.Generic.List[System.IO.FileInfo]'
    $directories = New-Object 'System.Collections.Generic.List[System.IO.DirectoryInfo]'
    $unknownFiles = 0

    if (Test-Path -LiteralPath $resolvedRoot) {
        $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
        if (-not ($rootItem -is [System.IO.DirectoryInfo])) {
            throw "Cache root is not a directory: $resolvedRoot"
        }
        if (Test-ReparsePoint $rootItem) {
            throw "Refusing reparse-point cache root: $resolvedRoot"
        }

        foreach ($packageEntry in Get-ChildItem -LiteralPath $resolvedRoot -Force) {
            Assert-WithinCache $resolvedRoot $packageEntry.FullName
            if (Test-ReparsePoint $packageEntry) {
                throw "Refusing reparse point inside cache: $($packageEntry.FullName)"
            }
            if ($packageEntry -is [System.IO.FileInfo]) {
                $unknownFiles++
                continue
            }
            if (-not ($packageEntry -is [System.IO.DirectoryInfo])) {
                throw "Refusing non-regular cache entry: $($packageEntry.FullName)"
            }

            if ($packageEntry.Name -match $stagingDirectoryPattern) {
                Add-StagingTree $resolvedRoot $packageEntry $stagingFiles $directories
                continue
            }

            $directories.Add($packageEntry)
            foreach ($hashEntry in Get-ChildItem -LiteralPath $packageEntry.FullName -Force) {
                Assert-WithinCache $resolvedRoot $hashEntry.FullName
                if (Test-ReparsePoint $hashEntry) {
                    throw "Refusing reparse point inside cache: $($hashEntry.FullName)"
                }
                if ($hashEntry -is [System.IO.FileInfo]) {
                    $unknownFiles++
                    continue
                }
                if (-not ($hashEntry -is [System.IO.DirectoryInfo])) {
                    throw "Refusing non-regular cache entry: $($hashEntry.FullName)"
                }

                if ($hashEntry.Name -notmatch $hashDirectoryPattern) {
                    Add-UnknownTree $resolvedRoot $hashEntry ([ref]$unknownFiles)
                    continue
                }

                $directories.Add($hashEntry)
                foreach ($cacheEntry in Get-ChildItem -LiteralPath $hashEntry.FullName -Force) {
                    Assert-WithinCache $resolvedRoot $cacheEntry.FullName
                    if (Test-ReparsePoint $cacheEntry) {
                        throw "Refusing reparse point inside cache: $($cacheEntry.FullName)"
                    }
                    if ($cacheEntry -is [System.IO.DirectoryInfo]) {
                        throw "Refusing unexpected nested cache directory: $($cacheEntry.FullName)"
                    }
                    if (-not ($cacheEntry -is [System.IO.FileInfo])) {
                        throw "Refusing non-regular cache entry: $($cacheEntry.FullName)"
                    }

                    if ($cacheEntry.Extension -ieq '.dds') {
                        $ddsFiles.Add($cacheEntry)
                    }
                    else {
                        $unknownFiles++
                    }
                }
            }
        }
    }

    $removableFiles = @($ddsFiles) + @($stagingFiles)
    [long]$removableBytes = 0
    foreach ($file in $removableFiles) {
        $removableBytes += $file.Length
    }

    Write-Host "FixWorld DDS cache: $resolvedRoot"
    Write-Host ('Mode: ' + $(if ($Delete) { 'DELETE' } else { 'DRY RUN' }))
    Write-Host "DDS files: $($ddsFiles.Count)"
    Write-Host "Staging files: $($stagingFiles.Count)"
    Write-Host "Removable size: $(Format-ByteSize $removableBytes)"
    Write-Host "Unknown files left untouched: $unknownFiles"

    if ($removableFiles.Count -eq 0) {
        Write-Host 'No FixWorld DDS cache entries found.'
        exit 0
    }
    if (-not $Delete) {
        Write-Host 'Nothing was deleted. Run again with -Delete to remove these entries.'
        exit 0
    }
    if (Get-Process -Name 'RimWorldWin64' -ErrorAction SilentlyContinue) {
        throw 'Close RimWorld before deleting its DDS cache.'
    }

    $errors = New-Object 'System.Collections.Generic.List[string]'
    $deletedFiles = 0
    foreach ($file in $removableFiles) {
        try {
            Assert-WithinCache $resolvedRoot $file.FullName
            $current = Get-Item -LiteralPath $file.FullName -Force
            if (Test-ReparsePoint $current) {
                throw 'Entry became a reparse point.'
            }
            Remove-Item -LiteralPath $current.FullName -Force
            $deletedFiles++
        }
        catch {
            $errors.Add("$($file.FullName): $($_.Exception.Message)")
        }
    }

    $directoryPaths = @($directories | ForEach-Object { $_.FullName } |
        Sort-Object -Unique | Sort-Object { $_.Length } -Descending)
    foreach ($directoryPath in $directoryPaths) {
        try {
            Assert-WithinCache $resolvedRoot $directoryPath
            if (-not (Test-Path -LiteralPath $directoryPath)) {
                continue
            }
            $current = Get-Item -LiteralPath $directoryPath -Force
            if (Test-ReparsePoint $current) {
                throw 'Directory became a reparse point.'
            }
            if (@(Get-ChildItem -LiteralPath $current.FullName -Force).Count -eq 0) {
                Remove-Item -LiteralPath $current.FullName -Force
            }
        }
        catch {
            $errors.Add("${directoryPath}: $($_.Exception.Message)")
        }
    }

    Write-Host "Deleted files: $deletedFiles"
    if ($errors.Count -gt 0) {
        [Console]::Error.WriteLine('Some cache entries could not be deleted:' +
            [Environment]::NewLine + ($errors -join [Environment]::NewLine))
        exit 1
    }

    Write-Host 'FixWorld DDS cache cleanup complete.'
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
