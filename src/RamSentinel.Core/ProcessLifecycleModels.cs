namespace RamSentinel.Core;

/// <summary>
/// Stable identity for one process instance. PID alone is not an identity because Windows reuses PIDs.
/// </summary>
public sealed record ProcessIdentity(int ProcessId, DateTimeOffset StartedAtUtc);

/// <summary>
/// Read-only observation captured from one process inventory pass.
/// Unavailable values stay null; they are never represented as zero.
/// </summary>
public sealed record ProcessLifecycleObservation(
    ProcessIdentity Identity,
    string Name,
    int? ParentProcessId,
    DateTimeOffset CapturedAt,
    string? ExecutablePath = null,
    ulong? WorkingSetBytes = null,
    ulong? PrivateBytes = null,
    TimeSpan? TotalCpuTime = null,
    ulong? ReadOperationCount = null,
    ulong? WriteOperationCount = null,
    ulong? ReadTransferBytes = null,
    ulong? WriteTransferBytes = null,
    bool? HasVisibleWindow = null);

/// <summary>
/// A parent-child relationship repeatedly observed while both process instances were alive.
/// This is evidence of a relationship, not proof that the child is disposable.
/// </summary>
public sealed record ProcessRelationship(
    ProcessIdentity Parent,
    ProcessIdentity Child,
    int ConsecutiveObservations,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    ConfidenceLevel Confidence);
