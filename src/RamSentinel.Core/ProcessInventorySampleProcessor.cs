namespace RamSentinel.Core;

/// <summary>
/// A UI-neutral process row produced from one immutable inventory snapshot.
/// </summary>
public sealed record ProcessInventoryRow(
    int ProcessId,
    string? Name,
    ulong? WorkingSetBytes,
    ulong? PrivateBytes,
    double? CpuPercent,
    DateTime? StartTimeUtc);

/// <summary>
/// All derived process views produced from the exact same inventory instance.
/// </summary>
public sealed record ProcessInventoryProcessingResult(
    ProcessInventorySnapshot Inventory,
    IReadOnlyList<ProcessInventoryRow> Rows,
    IReadOnlyList<ProcessPerformanceSample> PerformanceSamples,
    IReadOnlyList<ProcessRelationship> Relationships,
    bool RelationshipsAdvanced,
    string? RelationshipPauseReason);

/// <summary>
/// Applies one immutable process sample to CPU history and lifecycle tracking.
/// This class has no Windows or UI dependency, so the full consumer orchestration
/// can be tested deterministically in the Core test project.
/// </summary>
public sealed class ProcessInventorySampleProcessor
{
    private readonly int _logicalProcessorCount;
    private readonly ProcessRelationshipTracker _relationshipTracker;
    private readonly Dictionary<ProcessIdentity, CpuSample> _cpuSamples = new();
    private DateTimeOffset? _lastProcessedAt;

    public ProcessInventorySampleProcessor(
        int logicalProcessorCount,
        ProcessRelationshipTracker? relationshipTracker = null)
    {
        if (logicalProcessorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
        }

        _logicalProcessorCount = logicalProcessorCount;
        _relationshipTracker = relationshipTracker ?? new ProcessRelationshipTracker();
    }

    /// <summary>
    /// Produces display rows, diagnostic samples, and relationship evidence from one
    /// snapshot. All derived timestamps and values come from that snapshot's entries.
    /// </summary>
    public ProcessInventoryProcessingResult Process(ProcessInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        bool isNewerSample = !_lastProcessedAt.HasValue ||
                             inventory.CapturedAt > _lastProcessedAt.Value;
        if (!isNewerSample)
        {
            // A stale sample must not replace the currently displayed process table either.
            return new ProcessInventoryProcessingResult(
                Inventory: inventory,
                Rows: Array.Empty<ProcessInventoryRow>(),
                PerformanceSamples: Array.Empty<ProcessPerformanceSample>(),
                Relationships: Array.Empty<ProcessRelationship>(),
                RelationshipsAdvanced: false,
                RelationshipPauseReason: "הדגימה כפולה או ישנה יותר מהדגימה האחרונה; לא עודכנו נתוני תהליכים.");
        }

        bool relationshipsAdvanced = false;
        IReadOnlyList<ProcessInventoryEntry> entries =
            inventory.Entries ?? Array.Empty<ProcessInventoryEntry>();

        if (isNewerSample)
        {
            relationshipsAdvanced = inventory.CanAdvanceRelationships;
            if (relationshipsAdvanced)
            {
                ProcessLifecycleObservation[] lifecycleObservations = entries
                    .Select(entry => new ProcessLifecycleObservation(
                        Identity: entry.Identity!,
                        Name: entry.Name,
                        ParentProcessId: entry.ParentProcessId,
                        CapturedAt: inventory.CapturedAt,
                        WorkingSetBytes: entry.WorkingSetBytes,
                        PrivateBytes: entry.PrivateBytes,
                        TotalCpuTime: entry.TotalCpuTime,
                        ReadOperationCount: entry.ReadOperationCount,
                        WriteOperationCount: entry.WriteOperationCount,
                        ReadTransferBytes: entry.ReadTransferBytes,
                        WriteTransferBytes: entry.WriteTransferBytes))
                    .ToArray();

                _relationshipTracker.Observe(inventory.CapturedAt, lifecycleObservations);
            }
            else
            {
                // Never retain stale relationship confidence after an incomplete inventory.
                _relationshipTracker.Clear();
            }

            _lastProcessedAt = inventory.CapturedAt;
        }

        var rows = new List<ProcessInventoryRow>();
        var activeIdentities = new HashSet<ProcessIdentity>();

        foreach (ProcessInventoryEntry entry in entries
        {
            if (entry is null || entry.ProcessId <= 0)
            {
                continue;
            }

            ProcessIdentity? identity = entry.Identity;
            DateTime? startedAtUtc = identity?.StartedAtUtc.UtcDateTime;
            double? cpuPercent = null;

            if (isNewerSample && identity is not null)
            {
                activeIdentities.Add(identity);

                if (entry.TotalCpuTime is TimeSpan totalCpuTime)
                {
                    if (_cpuSamples.TryGetValue(identity, out CpuSample? previous) &&
                        previous is not null)
                    {
                        cpuPercent = CpuUsageCalculator.Calculate(
                            previous.TotalCpuTime,
                            totalCpuTime,
                            inventory.CapturedAt - previous.SampledAt,
                            _logicalProcessorCount);
                    }

                    _cpuSamples[identity] = new CpuSample(totalCpuTime, inventory.CapturedAt);
                }
                else
                {
                    // Do not bridge a gap in cumulative CPU data with a later delta.
                    _cpuSamples.Remove(identity);
                }
            }

            rows.Add(new ProcessInventoryRow(
                ProcessId: entry.ProcessId,
                Name: entry.Name,
                WorkingSetBytes: entry.WorkingSetBytes,
                PrivateBytes: entry.PrivateBytes,
                CpuPercent: cpuPercent,
                StartTimeUtc: startedAtUtc));
        }

        if (isNewerSample)
        {
            // Remove exited and PID-reused identities to keep the history bounded.
            foreach (ProcessIdentity identity in _cpuSamples.Keys
                         .Where(identity => !activeIdentities.Contains(identity))
                         .ToArray())
            {
                _cpuSamples.Remove(identity);
            }
        }

        ProcessPerformanceSample[] performanceSamples = rows
            .Select(row => new ProcessPerformanceSample(
                ProcessId: row.ProcessId,
                Name: row.Name ?? "לא זמין",
                CpuPercent: row.CpuPercent,
                WorkingSetBytes: row.WorkingSetBytes,
                PrivateBytes: row.PrivateBytes,
                StartedAtUtc: row.StartTimeUtc))
            .ToArray();

        return new ProcessInventoryProcessingResult(
            Inventory: inventory,
            Rows: rows.ToArray(),
            PerformanceSamples: performanceSamples,
            Relationships: isNewerSample
                ? _relationshipTracker.GetCurrentRelationships()
                : Array.Empty<ProcessRelationship>(),
            RelationshipsAdvanced: relationshipsAdvanced,
            RelationshipPauseReason: relationshipsAdvanced
                ? null
                : inventory.FailureReason ?? "נתוני האיסוף אינם מלאים.");
    }

    /// <summary>Clears both cumulative CPU baselines and lifecycle relationship evidence.</summary>
    public void Clear()
    {
        _cpuSamples.Clear();
        _relationshipTracker.Clear();
        _lastProcessedAt = null;
    }

    private sealed record CpuSample(TimeSpan TotalCpuTime, DateTimeOffset SampledAt);
}
