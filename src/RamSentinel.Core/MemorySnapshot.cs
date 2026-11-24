namespace RamSentinel.Core;

/// <summary>
/// Immutable system memory snapshot. Cache memory is informational:
/// Windows can reclaim standby pages when applications need memory.
/// </summary>
public sealed record MemorySnapshot(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    uint MemoryLoadPercent,
    DateTimeOffset CapturedAt,
    ulong CommitUsedBytes = 0,
    ulong CommitLimitBytes = 0,
    ulong SystemCacheBytes = 0,
    ulong KernelNonpagedBytes = 0,
    uint ProcessCount = 0,
    uint ThreadCount = 0)
{
    public ulong UsedPhysicalBytes =>
        TotalPhysicalBytes > AvailablePhysicalBytes
            ? TotalPhysicalBytes - AvailablePhysicalBytes
            : 0;

    public double AvailablePercent => TotalPhysicalBytes == 0
        ? 0
        : (double)AvailablePhysicalBytes / TotalPhysicalBytes * 100d;

    public double CommitPercent => CommitLimitBytes == 0
        ? 0
        : (double)CommitUsedBytes / CommitLimitBytes * 100d;
}
