using System.Diagnostics;
using System.Runtime.InteropServices;
using RamSentinel.Core;

namespace RamSentinel.App.Platform;

/// <summary>
/// Reads system-wide hard-page-fault read counters at the same low frequency
/// as diagnostic samples. This provider is read-only and does not inspect or
/// modify the page file.
/// </summary>
internal sealed class WindowsMemoryPagingProvider : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhStatusValidData = 0x00000000;
    private const uint PdhStatusNewData = 0x00000001;

    private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromSeconds(1);

    private IntPtr _query;
    private IntPtr _pageReadsCounter;
    private IntPtr _pagesInputCounter;
    private long _lastCollectionTimestamp;
    private bool _disposed;

    public WindowsMemoryPagingProvider()
    {
        if (PdhOpenQueryW(null, UIntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
            return;
        }

        _pageReadsCounter = TryAddCounter(@"\Memory\Page Reads/sec");
        _pagesInputCounter = TryAddCounter(@"\Memory\Page Inputs/sec");

        // Rate counters require a baseline before their first formatted value.
        if (PdhCollectQueryData(_query) == 0)
        {
            _lastCollectionTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public PagingMetricsSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_query == IntPtr.Zero)
        {
            return new PagingMetricsSnapshot(null, null);
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastCollectionTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastCollectionTimestamp, now) < MinimumSampleInterval)
        {
            return new PagingMetricsSnapshot(null, null);
        }

        uint status = PdhCollectQueryData(_query);
        _lastCollectionTimestamp = Stopwatch.GetTimestamp();

        if (status != 0)
        {
            return new PagingMetricsSnapshot(null, null);
        }

        return new PagingMetricsSnapshot(
            PageReadsPerSecond: ReadCounter(_pageReadsCounter),
            PagesInputPerSecond: ReadCounter(_pagesInputCounter));
    }

    private IntPtr TryAddCounter(string counterPath)
    {
        uint status = PdhAddEnglishCounterW(_query, counterPath, UIntPtr.Zero, out IntPtr counter);
        return status == 0 ? counter : IntPtr.Zero;
    }

    private static double? ReadCounter(IntPtr counter)
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
            !double.IsFinite(value.DoubleValue) ||
            value.DoubleValue < 0 ||
            value.DoubleValue > 1_000_000)
        {
            return null;
        }

        return value.DoubleValue;
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
