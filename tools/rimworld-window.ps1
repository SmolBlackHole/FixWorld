Set-StrictMode -Version Latest

if ($null -eq ('FixWorld.DisplayNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FixWorld
{
    public sealed class DisplayDeviceInfo
    {
        public string AdapterName { get; set; }
        public string Description { get; set; }
        public string HardwareId { get; set; }
    }

    public static class DisplayNative
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public int Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public int StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(
            string device,
            uint deviceNumber,
            ref DisplayDevice displayDevice,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        public static DisplayDeviceInfo[] GetMonitors()
        {
            var result = new List<DisplayDeviceInfo>();
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var adapter = CreateDisplayDevice();
                if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
                    break;

                for (uint monitorIndex = 0; ; monitorIndex++)
                {
                    var monitor = CreateDisplayDevice();
                    if (!EnumDisplayDevices(adapter.DeviceName, monitorIndex, ref monitor, 0))
                        break;

                    result.Add(new DisplayDeviceInfo
                    {
                        AdapterName = adapter.DeviceName,
                        Description = monitor.DeviceString,
                        HardwareId = monitor.DeviceId
                    });
                }
            }

            return result.ToArray();
        }

        public static string GetWindowMonitorDeviceName(IntPtr window)
        {
            const uint NearestMonitor = 2;
            IntPtr monitor = MonitorFromWindow(window, NearestMonitor);
            if (monitor == IntPtr.Zero)
                return null;

            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            return GetMonitorInfo(monitor, ref info) ? info.DeviceName : null;
        }

        private static DisplayDevice CreateDisplayDevice()
        {
            return new DisplayDevice
            {
                Size = Marshal.SizeOf(typeof(DisplayDevice))
            };
        }
    }
}
'@
}

Add-Type -AssemblyName System.Windows.Forms

function ConvertFrom-MonitorCharacterArray {
    param([AllowNull()][object] $Characters)

    if ($null -eq $Characters) {
        return ''
    }

    return -join @($Characters | Where-Object { $_ -ne 0 } | ForEach-Object { [char] $_ })
}

function Get-RimWorldDisplay {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $FriendlyName,

        [ValidateRange(1, 16)]
        [int] $FallbackMonitor = 1
    )

    $screens = @([Windows.Forms.Screen]::AllScreens)
    if ($screens.Count -eq 0) {
        throw 'Windows did not report an active monitor.'
    }

    $monitorIds = @()
    try {
        $monitorIds = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID |
            Where-Object { $_.Active } |
            ForEach-Object {
                $segments = $_.InstanceName -split '\\'
                [pscustomobject]@{
                    FriendlyName = ConvertFrom-MonitorCharacterArray $_.UserFriendlyName
                    HardwareId = if ($segments.Count -gt 1) { $segments[1] } else { '' }
                }
            })
    }
    catch {
        Write-Warning "The monitor name could not be read through WMI: $($_.Exception.Message)"
    }

    $requestedId = $monitorIds |
        Where-Object { $_.FriendlyName -eq $FriendlyName } |
        Select-Object -ExpandProperty HardwareId -First 1
    $displayDevice = $null

    if ($requestedId) {
        $hardwarePrefix = 'MONITOR\' + $requestedId + '\'
        $displayDevice = [FixWorld.DisplayNative]::GetMonitors() |
            Where-Object { $_.HardwareId.StartsWith($hardwarePrefix, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
    }

    if ($null -eq $displayDevice) {
        $displayDevice = [FixWorld.DisplayNative]::GetMonitors() |
            Where-Object { $_.Description -eq $FriendlyName } |
            Select-Object -First 1
    }

    $screen = if ($null -ne $displayDevice) {
        $screens |
            Where-Object { $_.DeviceName -eq $displayDevice.AdapterName } |
            Select-Object -First 1
    }
    else {
        $null
    }

    $usedFallback = $null -eq $screen
    if ($usedFallback) {
        $screen = $screens |
            Where-Object { $_.DeviceName -eq "\\.\DISPLAY$FallbackMonitor" } |
            Select-Object -First 1

        if ($null -eq $screen) {
            $screen = $screens | Where-Object { $_.Primary } | Select-Object -First 1
        }

        Write-Warning "Monitor '$FriendlyName' is not active. Falling back to $($screen.DeviceName)."
    }

    $displayNumberMatch = [regex]::Match($screen.DeviceName, 'DISPLAY(?<number>[0-9]+)$')
    if (-not $displayNumberMatch.Success) {
        throw "The Unity monitor index could not be derived from '$($screen.DeviceName)'."
    }

    return [pscustomobject]@{
        FriendlyName = if ($usedFallback) { $screen.DeviceName } else { $FriendlyName }
        DeviceName = $screen.DeviceName
        UnityMonitor = [int] $displayNumberMatch.Groups['number'].Value
        Bounds = $screen.Bounds
        WorkingArea = $screen.WorkingArea
        IsPrimary = $screen.Primary
        UsedFallback = $usedFallback
    }
}

function Set-RimWorldWindowPlacement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [Diagnostics.Process] $Process,

        [Parameter(Mandatory)]
        [object] $Display,

        [ValidateRange(1, 60)]
        [int] $TimeoutSeconds = 30,

        [switch] $Maximize
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $window = [IntPtr]::Zero
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw 'RimWorld exited before its window could be positioned.'
        }

        $window = $Process.MainWindowHandle
        if ($window -ne [IntPtr]::Zero) {
            break
        }

        Start-Sleep -Milliseconds 100
    }

    if ($window -eq [IntPtr]::Zero) {
        throw "The RimWorld window was not found within $TimeoutSeconds seconds."
    }

    $bounds = $Display.Bounds
    $noActivateOrZOrder = 0x0014
    $restoreWindow = 9
    $maximizeWindow = 3
    Start-Sleep -Milliseconds 250
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $null = [FixWorld.DisplayNative]::ShowWindow($window, $restoreWindow)
        if (-not [FixWorld.DisplayNative]::SetWindowPos(
            $window,
            [IntPtr]::Zero,
            $bounds.X,
            $bounds.Y,
            $bounds.Width,
            $bounds.Height,
            $noActivateOrZOrder)) {
            throw "The RimWorld window could not be moved to $($Display.DeviceName)."
        }

        if ($Maximize) {
            $null = [FixWorld.DisplayNative]::ShowWindow($window, $maximizeWindow)
        }

        Start-Sleep -Milliseconds 100
        $actualDeviceName = [FixWorld.DisplayNative]::GetWindowMonitorDeviceName($window)
        if ($actualDeviceName -eq $Display.DeviceName) {
            return [pscustomobject]@{
                WindowHandle = $window
                DeviceName = $actualDeviceName
            }
        }
    }

    throw "The RimWorld window remained on '$actualDeviceName' instead of '$($Display.DeviceName)'."
}

function Start-RimWorldOnDisplay {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
        [string] $WorkingDirectory,

        [string[]] $ArgumentList = @(),

        [ValidateNotNullOrEmpty()]
        [string] $MonitorName = 'G276HL',

        [ValidateRange(1, 16)]
        [int] $FallbackMonitor = 2,

        [switch] $Minimized
    )

    $display = Get-RimWorldDisplay -FriendlyName $MonitorName -FallbackMonitor $FallbackMonitor
    $arguments = @($ArgumentList) + @(
        '-monitor'
        $display.UnityMonitor.ToString([Globalization.CultureInfo]::InvariantCulture)
        '-screen-width'
        $display.Bounds.Width.ToString([Globalization.CultureInfo]::InvariantCulture)
        '-screen-height'
        $display.Bounds.Height.ToString([Globalization.CultureInfo]::InvariantCulture)
    )
    $startWindowStyle = if ($Minimized) { 'Minimized' } else { 'Normal' }
    $windowStyle = if ($Minimized) { 'Minimized' } else { 'Maximized' }
    $process = Start-Process -FilePath $FilePath -WorkingDirectory $WorkingDirectory `
        -ArgumentList $arguments -WindowStyle $startWindowStyle -PassThru

    $actualDeviceName = $null
    if (-not $Minimized) {
        $placement = Set-RimWorldWindowPlacement -Process $process -Display $display -Maximize
        $actualDeviceName = $placement.DeviceName
    }

    return [pscustomobject]@{
        Process = $process
        Display = $display
        ActualDeviceName = $actualDeviceName
        WindowStyle = $windowStyle
        Arguments = $arguments
    }
}
