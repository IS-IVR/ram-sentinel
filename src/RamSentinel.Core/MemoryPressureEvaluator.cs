namespace RamSentinel.Core;

public enum MemoryPressureLevel
{
    Normal,
    Warning,
    High
}

/// <summary>
/// Identifies sustained memory pressure. A high reading by itself is not
/// enough: the condition must remain critical for the configured duration.
/// </summary>
public sealed class MemoryPressureEvaluator
{
    private readonly TimeSpan _sustainedDuration;
    private DateTimeOffset? _criticalStartedAt;

    public MemoryPressureEvaluator(TimeSpan? sustainedDuration = null)
    {
        _sustainedDuration = sustainedDuration ?? TimeSpan.FromSeconds(30);
        if (_sustainedDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sustainedDuration));
        }
    }

    public MemoryPressureLevel Evaluate(MemorySnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool critical =
            snapshot.AvailablePercent <= 5 ||
            snapshot.MemoryLoadPercent >= 95 ||
            snapshot.CommitPercent >= 95;

        bool warning =
            snapshot.AvailablePercent <= 12 ||
            snapshot.MemoryLoadPercent >= 88 ||
            snapshot.CommitPercent >= 90;

        if (!warning)
        {
            _criticalStartedAt = null;
            return MemoryPressureLevel.Normal;
        }

        if (!critical)
        {
            _criticalStartedAt = null;
            return MemoryPressureLevel.Warning;
        }

        _criticalStartedAt ??= now;
        return now - _criticalStartedAt.Value >= _sustainedDuration
            ? MemoryPressureLevel.High
            : MemoryPressureLevel.Warning;
    }
}
