namespace RamSentinel.Core;

/// <summary>
/// Read-only disk latency and Windows-volume capacity sample.
/// Null performance values mean the counter is not available on this system.
/// </summary>
public sealed record DiskMetricsSnapshot(
    DateTimeOffset CapturedAt,
    double? ReadLatencyMilliseconds,
    double? WriteLatencyMilliseconds,
    double? QueueLength,
    double? ActiveTimePercent,
    ulong? SystemDriveFreeBytes,
    ulong? SystemDriveTotalBytes)
{
    public double? SystemDriveFreePercent =>
        SystemDriveFreeBytes.HasValue &&
        SystemDriveTotalBytes.HasValue &&
        SystemDriveTotalBytes.Value > 0
            ? (double)SystemDriveFreeBytes.Value / SystemDriveTotalBytes.Value * 100d
            : null;
}
