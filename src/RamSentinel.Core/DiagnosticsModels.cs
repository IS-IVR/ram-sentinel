namespace RamSentinel.Core;

public enum DiagnosticCategory
{
    Memory,
    Cpu,
    Disk,
    Startup,
    Storage
}

public enum FindingStatus
{
    Watch,
    ActionRecommended
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}

public enum ImpactArea
{
    CurrentWorkload,
    Multitasking,
    AppLaunch,
    Startup,
    GeneralReliability
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// A read-only observation of one process at the time of a system sample.
/// CPU percentages are normalized to the total logical-processor capacity.
/// </summary>
public sealed record ProcessPerformanceSample(
    int ProcessId,
    string Name,
    double? CpuPercent,
    ulong? WorkingSetBytes,
    ulong? PrivateBytes,
    DateTime? StartedAtUtc = null);

/// <summary>
/// One immutable point in the local performance history.
/// Missing collectors are represented by null, never by fabricated zero values.
/// </summary>
public sealed record SystemPerformanceSample(
    DateTimeOffset CapturedAt,
    MemorySnapshot? Memory = null,
    double? CpuPercent = null,
    DiskMetricsSnapshot? Disk = null,
    IReadOnlyList<ProcessPerformanceSample>? Processes = null);

public sealed record FindingEvidence(
    string Metric,
    string Value,
    string Unit,
    DateTimeOffset CapturedAt,
    string Explanation);

/// <summary>
/// A diagnosis, not an action. Creating or displaying a finding never changes Windows.
/// </summary>
public sealed record PerformanceFinding(
    DiagnosticCategory Category,
    FindingStatus Status,
    ConfidenceLevel Confidence,
    string Title,
    string Summary,
    TimeSpan ObservedDuration,
    IReadOnlyList<FindingEvidence> Evidence,
    ImpactArea Impact,
    string Recommendation,
    RiskLevel Risk,
    bool Reversible,
    string RestorePlan,
    int PriorityOrder);

public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAt,
    int SampleCount,
    TimeSpan ObservedWindow,
    bool IsCollecting,
    string StatusMessage,
    IReadOnlyList<PerformanceFinding> Findings,
    IReadOnlyList<DiagnosticCategory> NotMeasuredCategories,
    IReadOnlyList<DiagnosticCategory> UnavailableCategories)
{
    public PerformanceFinding? PrimaryFinding => Findings.FirstOrDefault();

    public IReadOnlyList<PerformanceFinding> SecondaryFindings =>
        Findings.Skip(1).Take(2).ToArray();
}
