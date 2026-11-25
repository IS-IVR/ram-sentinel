namespace RamSentinel.Core;

/// <summary>Describes whether the top-level process enumeration completed.</summary>
public enum ProcessEnumerationStatus
{
    Complete,
    Failed
}

/// <summary>Describes whether parent-process metadata can be trusted for this sample.</summary>
public enum ParentMetadataStatus
{
    Complete,
    Partial,
    Unavailable
}

/// <summary>
/// One process row captured during a single inventory pass. Null values mean that a
/// metric could not be read; they must never be converted to measured zero values.
/// </summary>
public sealed record ProcessInventoryEntry(
    int ProcessId,
    ProcessIdentity? Identity,
    string? Name,
    int? ParentProcessId,
    bool ParentMetadataAvailable,
    ulong? WorkingSetBytes,
    ulong? PrivateBytes,
    TimeSpan? TotalCpuTime,
    DateTimeOffset CapturedAt,
    ulong? ReadOperationCount = null,
    ulong? WriteOperationCount = null,
    ulong? ReadTransferBytes = null,
    ulong? WriteTransferBytes = null);

/// <summary>
/// Immutable result envelope for one process inventory attempt. Relationship tracking
/// is permitted only when enumeration and parent metadata are complete, every process
/// has a verified identity, and no PID is duplicated in the sample.
/// </summary>
public sealed record ProcessInventorySnapshot(
    DateTimeOffset CapturedAt,
    ProcessEnumerationStatus EnumerationStatus,
    ParentMetadataStatus ParentMetadataStatus,
    IReadOnlyList<ProcessInventoryEntry> Entries,
    string? FailureReason = null)
{
    public bool CanAdvanceRelationships
    {
        get
        {
            if (EnumerationStatus != ProcessEnumerationStatus.Complete ||
                ParentMetadataStatus != ParentMetadataStatus.Complete ||
                Entries is null)
            {
                return false;
            }

            var seenProcessIds = new HashSet<int>();
            foreach (ProcessInventoryEntry entry in Entries)
            {
                if (entry is null ||
                    entry.ProcessId <= 0 ||
                    entry.Identity is null ||
                    entry.Identity.ProcessId != entry.ProcessId ||
                    entry.CapturedAt != CapturedAt ||
                    !entry.ParentMetadataAvailable ||
                    !seenProcessIds.Add(entry.ProcessId))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
