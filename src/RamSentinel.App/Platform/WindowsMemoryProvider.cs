using System.ComponentModel;
using System.Runtime.InteropServices;
using RamSentinel.Core;

namespace RamSentinel.App.Platform;

internal sealed class WindowsMemoryProvider
{
    public MemorySnapshot Read()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        ulong commitUsed = 0;
        ulong commitLimit = 0;
        ulong systemCache = 0;
        ulong kernelNonpaged = 0;
        uint processCount = 0;
        uint threadCount = 0;

        var performance = new PerformanceInformation
        {
            Size = (uint)Marshal.SizeOf<PerformanceInformation>()
        };

        if (GetPerformanceInfo(ref performance, performance.Size))
        {
            ulong pageSize = performance.PageSize.ToUInt64();
            if (pageSize > 0)
            {
                commitUsed = ToBytes(performance.CommitTotal, pageSize);
                commitLimit = ToBytes(performance.CommitLimit, pageSize);
                systemCache = ToBytes(performance.SystemCache, pageSize);
                kernelNonpaged = ToBytes(performance.KernelNonpaged, pageSize);
                processCount = performance.ProcessCount;
                threadCount = performance.ThreadCount;
            }
        }

        return new MemorySnapshot(
            TotalPhysicalBytes: status.TotalPhysical,
            AvailablePhysicalBytes: status.AvailablePhysical,
            MemoryLoadPercent: status.MemoryLoad,
            CapturedAt: DateTimeOffset.Now,
            CommitUsedBytes: commitUsed,
            CommitLimitBytes: commitLimit,
            SystemCacheBytes: systemCache,
            KernelNonpagedBytes: kernelNonpaged,
            ProcessCount: processCount,
            ThreadCount: threadCount);
    }

    private static ulong ToBytes(UIntPtr pages, ulong pageSize)
    {
        ulong pageCount = pages.ToUInt64();
        return pageCount > ulong.MaxValue / pageSize
            ? ulong.MaxValue
            : pageCount * pageSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(
        ref PerformanceInformation performanceInformation,
        uint size);
}
