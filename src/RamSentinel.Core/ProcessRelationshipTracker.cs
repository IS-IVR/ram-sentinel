namespace RamSentinel.Core;

/// <summary>
/// Tracks only parent-child relationships directly observed in consecutive process inventories.
/// It never infers ownership from a process name or a stale PID.
/// </summary>
public sealed class ProcessRelationshipTracker
{
    private readonly TimeSpan _maximumObservationGap;
    private Dictionary<RelationshipKey, RelationshipState> _current = [];
    private DateTimeOffset? _lastObservedAt;

    public ProcessRelationshipTracker(TimeSpan? maximumObservationGap = null)
    {
        _maximumObservationGap = maximumObservationGap ?? TimeSpan.FromMinutes(2);
        if (_maximumObservationGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumObservationGap));
        }
    }

    /// <summary>
    /// Adds one complete inventory snapshot. Relationships are exposed only after at least
    /// two consecutive observations of the same parent and child process instances.
    /// Out-of-order or duplicate snapshots are ignored.
    /// </summary>
    public IReadOnlyList<ProcessRelationship> Observe(
        DateTimeOffset capturedAt,
        IReadOnlyList<ProcessLifecycleObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (_lastObservedAt.HasValue && capturedAt <= _lastObservedAt.Value)
        {
            return GetCurrentRelationships();
        }

        ProcessLifecycleObservation[] valid = observations
            .Where(item => item is not null &&
                           item.Identity.ProcessId > 0 &&
                           item.ParentProcessId is null or > 0 &&
                           item.CapturedAt == capturedAt)
            .ToArray();

        var groups = valid.GroupBy(item => item.Identity.ProcessId).ToArray();
        var ambiguousProcessIds = groups
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToHashSet();

        var byProcessId = groups
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        var next = new Dictionary<RelationshipKey, RelationshipState>();

        foreach (ProcessLifecycleObservation child in valid)
        {
            if (child.ParentProcessId is not int parentProcessId ||
                parentProcessId == child.Identity.ProcessId ||
                ambiguousProcessIds.Contains(parentProcessId) ||
                !byProcessId.TryGetValue(parentProcessId, out ProcessLifecycleObservation? parent))
            {
                continue;
            }

            var key = new RelationshipKey(parent.Identity, child.Identity);
            if (_current.TryGetValue(key, out RelationshipState? previous) &&
                capturedAt > previous.LastObservedAt &&
                capturedAt - previous.LastObservedAt <= _maximumObservationGap)
            {
                next[key] = new RelationshipState(
                    previous.FirstObservedAt,
                    capturedAt,
                    previous.ConsecutiveObservations + 1);
            }
            else
            {
                next[key] = new RelationshipState(capturedAt, capturedAt, 1);
            }
        }

        _current = next;
        _lastObservedAt = capturedAt;
        return GetCurrentRelationships();
    }

    public IReadOnlyList<ProcessRelationship> GetCurrentRelationships() =>
        _current
            .Where(pair => pair.Value.ConsecutiveObservations >= 2)
            .Select(pair => new ProcessRelationship(
                pair.Key.Parent,
                pair.Key.Child,
                pair.Value.ConsecutiveObservations,
                pair.Value.FirstObservedAt,
                pair.Value.LastObservedAt,
                pair.Value.ConsecutiveObservations >= 3
                    ? ConfidenceLevel.High
                    : ConfidenceLevel.Medium))
            .OrderBy(item => item.Parent.ProcessId)
            .ThenBy(item => item.Child.ProcessId)
            .ToArray();

    public void Clear()
    {
        _current.Clear();
        _lastObservedAt = null;
    }

    private readonly record struct RelationshipKey(
        ProcessIdentity Parent,
        ProcessIdentity Child);

    private sealed record RelationshipState(
        DateTimeOffset FirstObservedAt,
        DateTimeOffset LastObservedAt,
        int ConsecutiveObservations);
}
