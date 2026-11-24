using RamSentinel.Core;

var checks = new List<(string Name, Action Run)>
{
    ("Normal memory is reported as Normal", TestNormalMemory),
    ("High but non-critical memory is reported as Warning", TestWarningMemory),
    ("Critical pressure must persist before High is reported", TestSustainedCriticalPressure),
    ("A recovery resets the sustained-pressure timer", TestRecoveryResetsTimer),
    ("High commit pressure is detected", TestCommitPressure),
    ("One busy core is normalized across logical processors", TestCpuNormalization),
    ("Invalid process CPU deltas are rejected", TestInvalidCpuDelta)
};

int failures = 0;
foreach ((string name, Action run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name} - {ex.Message}");
    }
}

Console.WriteLine($"{checks.Count - failures}/{checks.Count} checks passed.");
return failures == 0 ? 0 : 1;

static void TestNormalMemory()
{
    var evaluator = new MemoryPressureEvaluator(TimeSpan.FromSeconds(30));
    MemoryPressureLevel result = evaluator.Evaluate(CreateSnapshot(availablePercent: 30, memoryLoad: 70, commitPercent: 65), DateTimeOffset.UtcNow);
    Assert(result == MemoryPressureLevel.Normal, $"Expected Normal, got {result}");
}

static void TestWarningMemory()
{
    var evaluator = new MemoryPressureEvaluator(TimeSpan.FromSeconds(30));
    MemoryPressureLevel result = evaluator.Evaluate(CreateSnapshot(availablePercent: 10, memoryLoad: 90, commitPercent: 80), DateTimeOffset.UtcNow);
    Assert(result == MemoryPressureLevel.Warning, $"Expected Warning, got {result}");
}

static void TestSustainedCriticalPressure()
{
    var evaluator = new MemoryPressureEvaluator(TimeSpan.FromSeconds(30));
    DateTimeOffset start = DateTimeOffset.UtcNow;
    MemorySnapshot critical = CreateSnapshot(availablePercent: 3, memoryLoad: 97, commitPercent: 96);

    Assert(evaluator.Evaluate(critical, start) == MemoryPressureLevel.Warning, "First critical sample must only warn.");
    Assert(evaluator.Evaluate(critical, start.AddSeconds(29)) == MemoryPressureLevel.Warning, "Pressure before the duration must remain Warning.");
    Assert(evaluator.Evaluate(critical, start.AddSeconds(30)) == MemoryPressureLevel.High, "Pressure at the duration must become High.");
}

static void TestRecoveryResetsTimer()
{
    var evaluator = new MemoryPressureEvaluator(TimeSpan.FromSeconds(30));
    DateTimeOffset start = DateTimeOffset.UtcNow;
    MemorySnapshot critical = CreateSnapshot(availablePercent: 3, memoryLoad: 97, commitPercent: 96);
    MemorySnapshot normal = CreateSnapshot(availablePercent: 30, memoryLoad: 70, commitPercent: 65);

    Assert(evaluator.Evaluate(critical, start) == MemoryPressureLevel.Warning, "Expected initial Warning.");
    Assert(evaluator.Evaluate(normal, start.AddSeconds(10)) == MemoryPressureLevel.Normal, "Expected recovery to Normal.");
    Assert(evaluator.Evaluate(critical, start.AddSeconds(11)) == MemoryPressureLevel.Warning, "Critical timer must restart after recovery.");
    Assert(evaluator.Evaluate(critical, start.AddSeconds(40)) == MemoryPressureLevel.Warning, "Only 29 seconds have elapsed since the reset.");
    Assert(evaluator.Evaluate(critical, start.AddSeconds(41)) == MemoryPressureLevel.High, "30 seconds after reset must become High.");
}

static void TestCommitPressure()
{
    var evaluator = new MemoryPressureEvaluator(TimeSpan.FromSeconds(30));
    DateTimeOffset start = DateTimeOffset.UtcNow;
    MemorySnapshot criticalCommit = CreateSnapshot(availablePercent: 20, memoryLoad: 80, commitPercent: 96);

    Assert(evaluator.Evaluate(criticalCommit, start) == MemoryPressureLevel.Warning, "Commit pressure should start as Warning.");
    Assert(evaluator.Evaluate(criticalCommit, start.AddSeconds(30)) == MemoryPressureLevel.High, "Sustained critical commit should become High.");
}

static void TestCpuNormalization()
{
    double? percent = CpuUsageCalculator.Calculate(
        previousCpuTime: TimeSpan.Zero,
        currentCpuTime: TimeSpan.FromSeconds(2),
        elapsedWallTime: TimeSpan.FromSeconds(2),
        logicalProcessorCount: 4);

    Assert(percent.HasValue && Math.Abs(percent.Value - 25d) < 0.001, $"Expected 25%, got {percent}");
}

static void TestInvalidCpuDelta()
{
    double? negative = CpuUsageCalculator.Calculate(
        previousCpuTime: TimeSpan.FromSeconds(3),
        currentCpuTime: TimeSpan.FromSeconds(2),
        elapsedWallTime: TimeSpan.FromSeconds(1),
        logicalProcessorCount: 4);
    Assert(negative is null, "A negative CPU delta should be rejected.");

    double? zeroElapsed = CpuUsageCalculator.Calculate(
        previousCpuTime: TimeSpan.Zero,
        currentCpuTime: TimeSpan.FromSeconds(1),
        elapsedWallTime: TimeSpan.Zero,
        logicalProcessorCount: 4);
    Assert(zeroElapsed is null, "A zero elapsed time should be rejected.");
}

static MemorySnapshot CreateSnapshot(double availablePercent, uint memoryLoad, double commitPercent)
{
    const ulong total = 100_000;
    const ulong commitLimit = 100_000;
    return new MemorySnapshot(
        TotalPhysicalBytes: total,
        AvailablePhysicalBytes: (ulong)(total * availablePercent / 100d),
        MemoryLoadPercent: memoryLoad,
        CapturedAt: DateTimeOffset.UtcNow,
        CommitUsedBytes: (ulong)(commitLimit * commitPercent / 100d),
        CommitLimitBytes: commitLimit);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
