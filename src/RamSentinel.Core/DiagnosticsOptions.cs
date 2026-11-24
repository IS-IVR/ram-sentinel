namespace RamSentinel.Core;

/// <summary>
/// Tunable thresholds for diagnosis. Defaults are deliberately conservative:
/// short spikes are ignored and disk findings require a sustained active workload.
/// </summary>
public sealed record DiagnosticsOptions
{
    public int MinimumSamples { get; init; } = 3;
    public double RequiredSustainedRatio { get; init; } = 0.75;
    public TimeSpan MaximumSampleGap { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan MinimumMemoryDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MinimumCpuDuration { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MinimumDiskDuration { get; init; } = TimeSpan.FromSeconds(60);

    public double CpuWatchThresholdPercent { get; init; } = 70;
    public double CpuActionThresholdPercent { get; init; } = 85;

    public double DiskWatchLatencyMilliseconds { get; init; } = 25;
    public double DiskActionLatencyMilliseconds { get; init; } = 50;
    public double DiskWatchActivePercent { get; init; } = 50;
    public double DiskActionActivePercent { get; init; } = 60;
    public double DiskWatchQueueLength { get; init; } = 0.5;
    public double DiskActionQueueLength { get; init; } = 1;

    internal void Validate()
    {
        if (MinimumSamples < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSamples));
        }

        if (!double.IsFinite(RequiredSustainedRatio) ||
            RequiredSustainedRatio <= 0 ||
            RequiredSustainedRatio > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredSustainedRatio));
        }

        if (MaximumSampleGap <= TimeSpan.Zero ||
            MinimumMemoryDuration < TimeSpan.Zero ||
            MinimumCpuDuration < TimeSpan.Zero ||
            MinimumDiskDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSampleGap));
        }

        if (!IsValidThreshold(CpuWatchThresholdPercent) ||
            !IsValidThreshold(CpuActionThresholdPercent) ||
            CpuWatchThresholdPercent > CpuActionThresholdPercent ||
            !IsValidThreshold(DiskWatchLatencyMilliseconds) ||
            !IsValidThreshold(DiskActionLatencyMilliseconds) ||
            DiskWatchLatencyMilliseconds > DiskActionLatencyMilliseconds ||
            !IsValidThreshold(DiskWatchActivePercent) ||
            !IsValidThreshold(DiskActionActivePercent) ||
            DiskWatchActivePercent > DiskActionActivePercent ||
            !IsValidThreshold(DiskWatchQueueLength) ||
            !IsValidThreshold(DiskActionQueueLength) ||
            DiskWatchQueueLength > DiskActionQueueLength)
        {
            throw new ArgumentOutOfRangeException(nameof(DiagnosticsOptions), "Diagnostic thresholds are invalid.");
        }
    }

    private static bool IsValidThreshold(double value) =>
        double.IsFinite(value) && value >= 0;
}
