namespace RamSentinel.Core;

/// <summary>
/// Converts process CPU-time deltas into a percentage of total machine capacity.
/// A process using one full logical processor on a 4-thread machine reports about 25%.
/// </summary>
public static class CpuUsageCalculator
{
    public static double? Calculate(
        TimeSpan previousCpuTime,
        TimeSpan currentCpuTime,
        TimeSpan elapsedWallTime,
        int logicalProcessorCount)
    {
        if (elapsedWallTime <= TimeSpan.Zero || logicalProcessorCount <= 0)
        {
            return null;
        }

        TimeSpan cpuDelta = currentCpuTime - previousCpuTime;
        if (cpuDelta < TimeSpan.Zero)
        {
            return null;
        }

        double capacityMilliseconds = elapsedWallTime.TotalMilliseconds * logicalProcessorCount;
        if (capacityMilliseconds <= 0)
        {
            return null;
        }

        double percent = cpuDelta.TotalMilliseconds / capacityMilliseconds * 100d;
        return Math.Clamp(percent, 0d, 100d);
    }
}
