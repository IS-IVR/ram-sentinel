using RamSentinel.Core;

internal static class DiagnosticsEngineChecks
{
    private const ulong GiB = 1024UL * 1024UL * 1024UL;

    public static IReadOnlyList<(string Name, Action Run)> All =>
    [
        ("Sustained RAM pressure creates a cautious finding", TestSustainedMemoryPressure),
        ("A brief RAM spike is ignored", TestBriefMemorySpike),
        ("Low available RAM alone is not called critical", TestMemoryPressureNeedsCorroboration),
        ("Sustained hard-fault reads plus disk load strengthen RAM diagnosis", TestSustainedPagingCorrelation),
        ("Page reads without disk load do not strengthen RAM diagnosis", TestPagingNeedsDiskCorrelation),
        ("Write latency alone does not corroborate page reads", TestPagingNeedsReadLatency),
        ("Page reads without RAM pressure do not create a RAM finding", TestPagingWithoutMemoryPressure),
        ("Invalid paging counters do not increase confidence", TestInvalidPagingMetrics),
        ("Sustained CPU load identifies a leading process", TestSustainedCpuLoad),
        ("Reused process IDs are not merged into one attribution", TestProcessIdReuse),
        ("A brief CPU spike is ignored", TestBriefCpuSpike),
        ("Invalid CPU samples are unavailable, not zero", TestInvalidCpuSample),
        ("Sustained disk latency requires concurrent activity", TestSustainedDiskLatency),
        ("A brief disk latency spike is ignored", TestBriefDiskSpike),
        ("Disk latency without activity is not called a bottleneck", TestDiskLatencyWithoutActivity),
        ("Critically low storage is reported separately from disk speed", TestCriticalStorage),
        ("Low percentage alone does not warn on a large drive", TestLargeDriveLowPercentage),
        ("Startup impact stays explicitly unmeasured", TestStartupIsUnmeasured),
        ("Long sampling gaps prevent a sustained diagnosis", TestSamplingGap),
        ("No samples produce a collecting report", TestNoSamples),
        ("Invalid storage capacity is not interpreted as zero space", TestInvalidStorage),
        ("In-memory history removes expired samples", TestHistoryRetention),
        ("In-memory history enforces a maximum sample count", TestHistoryMaximumCount),
        ("In-memory history rejects duplicate and out-of-order samples", TestHistoryOrdering)
    ];

    private static void TestSustainedMemoryPressure()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (index, at) =>
            CreateSample(at, availablePercent: 4, memoryLoad: 96, commitPercent: 95));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);

        Assert(finding.Status == FindingStatus.ActionRecommended, "Sustained critical memory pressure should recommend review.");
        Assert(finding.Confidence == ConfidenceLevel.Medium, "Memory confidence must stay Medium without paging evidence.");
        Assert(finding.Summary.Contains("אין כאן קביעה", StringComparison.Ordinal), "The finding must not claim paging without hard-fault data.");
        Assert(finding.Recommendation.Contains("בדרך הרגילה", StringComparison.Ordinal), "The recommendation must require normal user-controlled app closing.");
    }

    private static void TestBriefMemorySpike()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (index, at) =>
            index == 1
                ? CreateSample(at, availablePercent: 3, memoryLoad: 97, commitPercent: 96)
                : CreateSample(at, availablePercent: 40, memoryLoad: 60, commitPercent: 60));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Memory),
            "One critical memory sample must not create a sustained finding.");
    }

    private static void TestMemoryPressureNeedsCorroboration()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, availablePercent: 5, memoryLoad: 80, commitPercent: 60));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);
        Assert(finding.Status == FindingStatus.Watch, "Low available RAM alone should be a Watch finding, not a critical action.");
        Assert(finding.Confidence == ConfidenceLevel.Low, "Single-signal memory pressure must have Low confidence.");
    }

    private static void TestSustainedPagingCorrelation()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                availablePercent: 4,
                memoryLoad: 96,
                commitPercent: 95,
                readLatency: 80,
                writeLatency: 70,
                activePercent: 90,
                queueLength: 2,
                pageReadsPerSecond: 20,
                pagesInputPerSecond: 120));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);

        Assert(finding.Confidence == ConfidenceLevel.High,
            "Confidence should increase only when critical RAM pressure, page reads, and active high-latency disk samples persist together.");
        Assert(finding.Summary.Contains("אינו מוכיח", StringComparison.Ordinal),
            "The explanation must clarify that page-read counters do not identify page-file-only traffic.");
        Assert(finding.Evidence.Any(item => item.Metric.Contains("Page Reads/sec", StringComparison.Ordinal)),
            "The measured page-read rate should appear in the evidence.");
    }

    private static void TestPagingNeedsDiskCorrelation()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                availablePercent: 4,
                memoryLoad: 96,
                commitPercent: 95,
                readLatency: 3,
                writeLatency: 4,
                activePercent: 10,
                queueLength: 0.1,
                pageReadsPerSecond: 20,
                pagesInputPerSecond: 120));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);

        Assert(finding.Confidence == ConfidenceLevel.Medium,
            "Page reads without concurrent disk latency and activity must not increase confidence.");
    }

    private static void TestPagingNeedsReadLatency()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                availablePercent: 4,
                memoryLoad: 96,
                commitPercent: 95,
                readLatency: 3,
                writeLatency: 80,
                activePercent: 90,
                queueLength: 2,
                pageReadsPerSecond: 20,
                pagesInputPerSecond: 120));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);

        Assert(finding.Confidence == ConfidenceLevel.Medium,
            "High write latency without high read latency must not be attributed to hard-page-fault reads.");
    }

    private static void TestPagingWithoutMemoryPressure()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                availablePercent: 40,
                memoryLoad: 60,
                commitPercent: 60,
                readLatency: 80,
                writeLatency: 70,
                activePercent: 90,
                queueLength: 2,
                pageReadsPerSecond: 20,
                pagesInputPerSecond: 120));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);

        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Memory),
            "Page reads and disk activity alone must not be described as a RAM bottleneck.");
    }

    private static void TestInvalidPagingMetrics()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                availablePercent: 4,
                memoryLoad: 96,
                commitPercent: 95,
                readLatency: 80,
                writeLatency: 70,
                activePercent: 90,
                queueLength: 2,
                pageReadsPerSecond: double.NaN,
                pagesInputPerSecond: double.PositiveInfinity));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Memory);

        Assert(finding.Confidence == ConfidenceLevel.Medium,
            "NaN and infinite paging values must be rejected and cannot increase confidence.");
        Assert(!finding.Evidence.Any(item => item.Metric.Contains("Page Reads/sec", StringComparison.Ordinal)),
            "Invalid paging values must not appear as measured evidence.");
    }

    private static void TestSustainedCpuLoad()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(
                at,
                cpuPercent: 92,
                processes:
                [
                    new ProcessPerformanceSample(1234, "WorkApp", 28, 500 * 1024UL * 1024UL, 400 * 1024UL * 1024UL)
                ]));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Cpu);

        Assert(finding.Status == FindingStatus.ActionRecommended, "Sustained high CPU should be actionable.");
        Assert(finding.Confidence == ConfidenceLevel.High, "A repeatedly observed leading process should increase attribution confidence.");
        Assert(finding.Summary.Contains("WorkApp", StringComparison.Ordinal), "The leading process should be named in the explanation.");
        Assert(finding.RestorePlan.Contains("אינו סוגר", StringComparison.Ordinal), "The engine must not close the process.");
    }

    private static void TestProcessIdReuse()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (index, at) =>
            CreateSample(
                at,
                cpuPercent: 92,
                processes:
                [
                    new ProcessPerformanceSample(
                        1234,
                        "WorkApp",
                        28,
                        500 * 1024UL * 1024UL,
                        400 * 1024UL * 1024UL,
                        StartedAtUtc: (index < 2 ? start : start.AddMinutes(1)).UtcDateTime)
                ]));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Cpu);

        Assert(!finding.Summary.Contains("WorkApp", StringComparison.Ordinal),
            "Samples from two process instances that reused the same PID must not be combined.");
        Assert(finding.Confidence == ConfidenceLevel.Medium,
            "The system CPU finding should remain, without strong process attribution.");
    }

    private static void TestBriefCpuSpike()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (index, at) =>
            CreateSample(at, cpuPercent: index == 2 ? 98 : 18));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Cpu),
            "One high CPU sample must not create a sustained finding.");
    }

    private static void TestInvalidCpuSample()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, cpuPercent: double.NaN));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Cpu),
            "NaN CPU values must not be interpreted as a valid reading.");
        Assert(report.UnavailableCategories.Contains(DiagnosticCategory.Cpu),
            "A category with no valid CPU readings must be listed as unavailable.");
    }

    private static void TestSustainedDiskLatency()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, readLatency: 80, writeLatency: 70, activePercent: 90, queueLength: 2));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Disk);

        Assert(finding.Status == FindingStatus.ActionRecommended, "Sustained latency under disk activity should be flagged.");
        Assert(finding.Confidence == ConfidenceLevel.High, "Latency, activity and queue together should provide strong evidence.");
        Assert(finding.Summary.Contains("אינה מזהה לבדה", StringComparison.Ordinal), "The finding must not claim the drive is faulty.");
    }

    private static void TestBriefDiskSpike()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (index, at) =>
            index == 1
                ? CreateSample(at, readLatency: 100, writeLatency: 100, activePercent: 95, queueLength: 3)
                : CreateSample(at, readLatency: 3, writeLatency: 4, activePercent: 10, queueLength: 0.1));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Disk),
            "A single high-latency sample must be ignored.");
    }

    private static void TestDiskLatencyWithoutActivity()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, readLatency: 90, writeLatency: 80, activePercent: 5, queueLength: 0.1));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Disk),
            "High latency without concurrent activity or queue should not be diagnosed as a bottleneck.");
    }

    private static void TestCriticalStorage()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, freeGiB: 4, totalGiB: 100));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        PerformanceFinding finding = Find(report, DiagnosticCategory.Storage);

        Assert(finding.Status == FindingStatus.ActionRecommended, "Very low free capacity should be reported.");
        Assert(finding.Impact == ImpactArea.GeneralReliability, "Low storage must not be described as proof of slow disk performance.");
        Assert(finding.Recommendation.Contains("לא מוחקת קבצים", StringComparison.Ordinal), "The recommendation must not delete files.");
    }

    private static void TestLargeDriveLowPercentage()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, freeGiB: 50, totalGiB: 1000));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Storage),
            "A low percentage with ample absolute free space must not trigger a storage warning.");
    }

    private static void TestStartupIsUnmeasured()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) => CreateSample(at));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(report.NotMeasuredCategories.Contains(DiagnosticCategory.Startup),
            "Startup inventory must not be presented as measured startup impact.");
    }

    private static void TestSamplingGap()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        DateTimeOffset[] times =
        [
            start,
            start.AddSeconds(30),
            start.AddMinutes(3),
            start.AddMinutes(3).AddSeconds(30)
        ];

        var samples = times.Select(at =>
            CreateSample(at, availablePercent: 3, memoryLoad: 97, commitPercent: 96)).ToArray();

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Memory),
            "A large gap must prevent a sustained diagnosis.");
        Assert(report.IsCollecting, "The report should indicate that more data is needed after a large gap.");
    }

    private static void TestNoSamples()
    {
        DiagnosticReport report = new DiagnosticsEngine().Evaluate(Array.Empty<SystemPerformanceSample>());
        Assert(report.IsCollecting, "An empty history must be reported as collecting.");
        Assert(report.Findings.Count == 0, "An empty history cannot produce findings.");
        Assert(report.StatusMessage.Contains("אוספים נתונים", StringComparison.Ordinal), "The status should explain that data is being collected.");
    }

    private static void TestInvalidStorage()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var samples = CreateSeries(start, (_, at) =>
            CreateSample(at, freeGiB: 150, totalGiB: 100));

        DiagnosticReport report = new DiagnosticsEngine().Evaluate(samples);
        Assert(!report.Findings.Any(finding => finding.Category == DiagnosticCategory.Storage),
            "Free space greater than total capacity is invalid data, not a storage finding.");
        Assert(report.UnavailableCategories.Contains(DiagnosticCategory.Storage),
            "Invalid storage data should be marked unavailable.");
    }



    private static void TestHistoryRetention()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var history = new PerformanceSampleHistory(TimeSpan.FromMinutes(1), maximumSamples: 10);

        Assert(history.Add(CreateSample(start)), "The first history sample should be accepted.");
        Assert(history.Add(CreateSample(start.AddSeconds(30))), "The second history sample should be accepted.");
        Assert(history.Add(CreateSample(start.AddSeconds(60))), "The third history sample should be accepted.");
        Assert(history.Add(CreateSample(start.AddSeconds(90))), "The fourth history sample should be accepted.");

        IReadOnlyList<SystemPerformanceSample> snapshot = history.GetSnapshot();
        Assert(snapshot.Count == 3, $"Expected 3 retained samples, got {snapshot.Count}.");
        Assert(snapshot[0].CapturedAt == start.AddSeconds(30), "Samples older than the retention window should be removed.");
        Assert(snapshot[^1].CapturedAt == start.AddSeconds(90), "The newest sample should remain.");
    }

    private static void TestHistoryMaximumCount()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var history = new PerformanceSampleHistory(TimeSpan.FromHours(1), maximumSamples: 3);

        for (int index = 0; index < 5; index++)
        {
            Assert(history.Add(CreateSample(start.AddSeconds(index))), "Increasing timestamps should be accepted.");
        }

        IReadOnlyList<SystemPerformanceSample> snapshot = history.GetSnapshot();
        Assert(snapshot.Count == 3, $"Expected a maximum of 3 samples, got {snapshot.Count}.");
        Assert(snapshot[0].CapturedAt == start.AddSeconds(2), "The oldest samples should be discarded when the cap is reached.");
    }

    private static void TestHistoryOrdering()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var history = new PerformanceSampleHistory();

        Assert(history.Add(CreateSample(start)), "The initial sample should be accepted.");
        Assert(!history.Add(CreateSample(start)), "A duplicate timestamp should be rejected.");
        Assert(!history.Add(CreateSample(start.AddSeconds(-1))), "An older timestamp should be rejected.");
        Assert(history.Count == 1, "Rejected samples must not modify the history.");

        history.Clear();
        Assert(history.Count == 0, "Clear should empty the history.");
        Assert(history.Add(CreateSample(start.AddSeconds(-10))), "A new baseline should be accepted after Clear.");
    }

    private static IReadOnlyList<SystemPerformanceSample> CreateSeries(
        DateTimeOffset start,
        Func<int, DateTimeOffset, SystemPerformanceSample> factory,
        int count = 4)
    {
        return Enumerable.Range(0, count)
            .Select(index => factory(index, start.AddSeconds(index * 30)))
            .ToArray();
    }

    private static SystemPerformanceSample CreateSample(
        DateTimeOffset capturedAt,
        double availablePercent = 40,
        uint memoryLoad = 60,
        double commitPercent = 60,
        double? cpuPercent = 20,
        double? readLatency = 3,
        double? writeLatency = 4,
        double? activePercent = 10,
        double? queueLength = 0.1,
        ulong? freeGiB = 100,
        ulong? totalGiB = 500,
        IReadOnlyList<ProcessPerformanceSample>? processes = null,
        double? pageReadsPerSecond = null,
        double? pagesInputPerSecond = null)
    {
        const ulong totalRam = 16 * GiB;
        const ulong commitLimit = 24 * GiB;

        var memory = new MemorySnapshot(
            TotalPhysicalBytes: totalRam,
            AvailablePhysicalBytes: (ulong)(totalRam * availablePercent / 100d),
            MemoryLoadPercent: memoryLoad,
            CapturedAt: capturedAt,
            CommitUsedBytes: (ulong)(commitLimit * commitPercent / 100d),
            CommitLimitBytes: commitLimit);

        ulong? freeBytes = freeGiB.HasValue ? freeGiB.Value * GiB : null;
        ulong? totalBytes = totalGiB.HasValue ? totalGiB.Value * GiB : null;

        var disk = new DiskMetricsSnapshot(
            CapturedAt: capturedAt,
            ReadLatencyMilliseconds: readLatency,
            WriteLatencyMilliseconds: writeLatency,
            QueueLength: queueLength,
            ActiveTimePercent: activePercent,
            SystemDriveFreeBytes: freeBytes,
            SystemDriveTotalBytes: totalBytes);

        return new SystemPerformanceSample(
            CapturedAt: capturedAt,
            Memory: memory,
            CpuPercent: cpuPercent,
            Disk: disk,
            Processes: processes ?? [],
            Paging: pageReadsPerSecond.HasValue || pagesInputPerSecond.HasValue
                ? new PagingMetricsSnapshot(pageReadsPerSecond, pagesInputPerSecond)
                : null);
    }

    private static PerformanceFinding Find(DiagnosticReport report, DiagnosticCategory category) =>
        report.Findings.FirstOrDefault(finding => finding.Category == category)
        ?? throw new InvalidOperationException($"Expected a {category} finding.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
