namespace RamSentinel.Core;

public enum MemoryPressureLevel
{
    Normal,
    Warning,
    High
}

/// <summary>
/// Reports pressure only after the configured condition remains true
/// continuously for the required duration.
/// </summary>
public sealed class MemoryPressureEvaluator
{
    private readonly TimeSpan _sustainedDuration;
    private DateTimeOffset? _pressureStartedAt;

    public MemoryPressureEvaluator(TimeSpan? sustainedDuration = null)
    {
        _sustainedDuration = sustainedDuration ?? TimeSpan.FromSeconds(20);
        if (_sustainedDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sustainedDuration));
        }
    }

    public MemoryPressureLevel Evaluate(MemorySnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool pressureDetected = snapshot.MemoryLoadPercent >= 90 || snapshot.AvailablePercent <= 10;
        if (!pressureDetected)
        {
            _pressureStartedAt = null;
            return MemoryPressureLevel.Normal;
        }

        _pressureStartedAt ??= now;
        return now - _pressureStartedAt.Value >= _sustainedDuration
            ? MemoryPressureLevel.High
            : MemoryPressureLevel.Warning;
    }
}
