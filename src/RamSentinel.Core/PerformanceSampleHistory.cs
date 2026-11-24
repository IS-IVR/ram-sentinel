namespace RamSentinel.Core;

/// <summary>
/// Keeps a small, chronological, in-memory history for live diagnosis.
/// The history is intentionally bounded by both age and sample count.
/// Nothing is written to disk and old samples are discarded as new ones arrive.
/// </summary>
public sealed class PerformanceSampleHistory
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    public const int DefaultMaximumSamples = 61;

    private readonly Queue<SystemPerformanceSample> _samples = new();
    private DateTimeOffset? _lastCapturedAt;

    public TimeSpan Retention { get; }
    public int MaximumSamples { get; }

    public int Count => _samples.Count;

    public PerformanceSampleHistory(
        TimeSpan? retention = null,
        int maximumSamples = DefaultMaximumSamples)
    {
        Retention = retention ?? DefaultRetention;
        MaximumSamples = maximumSamples;

        if (Retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        if (MaximumSamples < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSamples));
        }
    }

    /// <summary>
    /// Adds a sample only when its timestamp is newer than the current history.
    /// Returns false for duplicate or out-of-order samples.
    /// </summary>
    public bool Add(SystemPerformanceSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (_lastCapturedAt.HasValue && sample.CapturedAt <= _lastCapturedAt.Value)
        {
            return false;
        }

        _samples.Enqueue(sample);
        _lastCapturedAt = sample.CapturedAt;

        DateTimeOffset oldestAllowed = sample.CapturedAt - Retention;
        while (_samples.TryPeek(out SystemPerformanceSample? oldest) &&
               oldest is not null &&
               oldest.CapturedAt < oldestAllowed)
        {
            _samples.Dequeue();
        }

        while (_samples.Count > MaximumSamples)
        {
            _samples.Dequeue();
        }

        return true;
    }

    /// <summary>
    /// Returns a defensive copy so callers cannot mutate the stored history.
    /// </summary>
    public IReadOnlyList<SystemPerformanceSample> GetSnapshot() =>
        _samples.ToArray();

    public void Clear()
    {
        _samples.Clear();
        _lastCapturedAt = null;
    }
}
