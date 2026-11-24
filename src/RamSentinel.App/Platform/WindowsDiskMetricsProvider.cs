using System.ComponentModel;
using System.Runtime.InteropServices;
using RamSentinel.Core;

namespace RamSentinel.App.Platform;

/// <summary>
/// Low-frequency, read-only physical-disk sampling through Windows PDH counters.
/// The query is created once and reused. No disk scan or file enumeration is performed.
/// </summary>
internal sealed class WindowsDiskMetricsProvider : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhStatusValidData = 0x00000000;
    private const uint PdhStatusNewData = 0x00000001;

    private IntPtr _query;
    private IntPtr _readLatencyCounter;
    private IntPtr _writeLatencyCounter;
    private IntPtr _queueLengthCounter;
    private IntPtr _activeTimeCounter;
    private bool _disposed;

    public WindowsDiskMetricsProvider()
    {
        uint status = PdhOpenQueryW(null, UIntPtr.Zero, out _query);
        if (status != 0)
        {
            _query = IntPtr.Zero;
            return;
        }

        _readLatencyCounter = TryAddCounter(@"\PhysicalDisk(_Total)\Avg. Disk sec/Read");
        _writeLatencyCounter = TryAddCounter(@"\PhysicalDisk(_Total)\Avg. Disk sec/Write");
        _queueLengthCounter = TryAddCounter(@"\PhysicalDisk(_Total)\Current Disk Queue Length");
        _activeTimeCounter = TryAddCounter(@"\PhysicalDisk(_Total)\% Disk Time");

        // Establish the first sample. Rate/average counters need a baseline.
        _ = PdhCollectQueryData(_query);
    }

    public DiskMetricsSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _ = PdhCollectQueryData(_query);

        (ulong? freeBytes, ulong? totalBytes) = ReadSystemDriveSpace();

        return new DiskMetricsSnapshot(
            CapturedAt: DateTimeOffset.Now,
            ReadLatencyMilliseconds: ReadCounter(_readLatencyCounter, secondsToMilliseconds: true),
            WriteLatencyMilliseconds: ReadCounter(_writeLatencyCounter, secondsToMilliseconds: true),
            QueueLength: ReadCounter(_queueLengthCounter),
            ActiveTimePercent: ReadCounter(_activeTimeCounter),
            SystemDriveFreeBytes: freeBytes,
            SystemDriveTotalBytes: totalBytes);
    }

    private IntPtr TryAddCounter(string counterPath)
    {
        if (_query == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        uint status = PdhAddEnglishCounterW(_query, counterPath, UIntPtr.Zero, out IntPtr counter);
        return status == 0 ? counter : IntPtr.Zero;
    }

    private static double? ReadCounter(IntPtr counter, bool secondsToMilliseconds = false)
    {
        if (counter == IntPtr.Zero)
        {
            return null;
        }

        uint status = PdhGetFormattedCounterValue(
            counter,
            PdhFmtDouble,
            out _,
            out PdhFormattedCounterValue value);

        if (status != 0 ||
            (value.CStatus != PdhStatusValidData && value.CStatus != PdhStatusNewData) ||
            double.IsNaN(value.DoubleValue) ||
            double.IsInfinity(value.DoubleValue) ||
            value.DoubleValue < 0)
        {
            return null;
        }

        double result = secondsToMilliseconds ? value.DoubleValue * 1000d : value.DoubleValue;
        return Math.Clamp(result, 0d, secondsToMilliseconds ? 60_000d : 1_000_000d);
    }

    private static (ulong? FreeBytes, ulong? TotalBytes) ReadSystemDriveSpace()
    {
        try
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return (null, null);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return (null, null);
            }

            return ((ulong)drive.AvailableFreeSpace, (ulong)drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_query != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(
        string? dataSource,
        UIntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query,
        string fullCounterPath,
        UIntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue")]
    private static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint type,
        out PdhFormattedCounterValue value);

    [DllImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
