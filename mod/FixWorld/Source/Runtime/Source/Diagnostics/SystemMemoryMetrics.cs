using System;
using System.Runtime.InteropServices;

namespace FixWorld.Diagnostics
{
    internal readonly struct SystemMemorySnapshot
    {
        internal SystemMemorySnapshot(
            bool available,
            long processBytes,
            long freePhysicalBytes)
        {
            Available = available;
            ProcessBytes = processBytes;
            FreePhysicalBytes = freePhysicalBytes;
        }

        internal bool Available { get; }

        internal long ProcessBytes { get; }

        internal long FreePhysicalBytes { get; }
    }

    internal static class SystemMemoryMetrics
    {
        internal static SystemMemorySnapshot Read()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return default;
            }

            MemoryStatus memory = new MemoryStatus
            {
                Length = (uint)Marshal.SizeOf(typeof(MemoryStatus))
            };
            ProcessMemoryCounters process = new ProcessMemoryCounters
            {
                Size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters))
            };
            if (!GlobalMemoryStatusEx(ref memory) ||
                !GetProcessMemoryInfo(GetCurrentProcess(), out process, process.Size))
            {
                return default;
            }

            return new SystemMemorySnapshot(
                true,
                checked((long)process.WorkingSetSize.ToUInt64()),
                checked((long)memory.AvailablePhysical));
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr process,
            out ProcessMemoryCounters counters,
            uint size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length;
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            internal uint Size;
            internal uint PageFaultCount;
            internal UIntPtr PeakWorkingSetSize;
            internal UIntPtr WorkingSetSize;
            internal UIntPtr QuotaPeakPagedPoolUsage;
            internal UIntPtr QuotaPagedPoolUsage;
            internal UIntPtr QuotaPeakNonPagedPoolUsage;
            internal UIntPtr QuotaNonPagedPoolUsage;
            internal UIntPtr PagefileUsage;
            internal UIntPtr PeakPagefileUsage;
        }
    }
}
