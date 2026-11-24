namespace RamSentinel.Core;

/// <summary>
/// Immutable snapshot of physical memory reported by Windows.
/// </summary>
public sealed record MemorySnapshot(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    uint MemoryLoadPercent,
    DateTimeOffset CapturedAt)
{
    public ulong UsedPhysicalBytes =>
        TotalPhysicalBytes > AvailablePhysicalBytes
            ? TotalPhysicalBytes - AvailablePhysicalBytes
            : 0;

    public double AvailablePercent => TotalPhysicalBytes == 0
        ? 0
        : (double)AvailablePhysicalBytes / TotalPhysicalBytes * 100d;
}
