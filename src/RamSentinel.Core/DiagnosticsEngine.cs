namespace RamSentinel.Core;

/// <summary>
/// Evaluates a bounded set of local samples and returns explanations only.
/// It does not stop processes, edit settings, delete files, or call Windows APIs.
/// </summary>
public sealed class DiagnosticsEngine
{
    private const ulong GiB = 1024UL * 1024UL * 1024UL;

    private readonly DiagnosticsOptions _options;

    public DiagnosticsEngine(DiagnosticsOptions? options = null)
    {
        _options = options ?? new DiagnosticsOptions();
        _options.Validate();
    }

    public DiagnosticReport Evaluate(
        IEnumerable<SystemPerformanceSample> samples,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Duplicate timestamps add no evidence. Keep the last supplied sample for that instant.
        SystemPerformanceSample[] ordered = samples
            .GroupBy(sample => sample.CapturedAt)
            .Select(group => group.Last())
            .OrderBy(sample => sample.CapturedAt)
            .ToArray();

        var findings = new List<PerformanceFinding>();
        var unavailable = new List<DiagnosticCategory>();

        SystemPerformanceSample[] memorySamples = ordered
            .Where(sample => sample.Memory is { TotalPhysicalBytes: > 0 })
            .ToArray();

        SystemPerformanceSample[] cpuSamples = ordered
            .Where(sample => sample.CpuPercent.HasValue && IsValidPercentage(sample.CpuPercent.Value))
            .ToArray();

        SystemPerformanceSample[] diskSamples = ordered
            .Where(sample => sample.Disk is not null && GetDiskLatency(sample.Disk).HasValue)
            .ToArray();

        SystemPerformanceSample[] storageSamples = ordered
            .Where(HasValidStorageData)
            .ToArray();

        if (memorySamples.Length == 0)
        {
            unavailable.Add(DiagnosticCategory.Memory);
        }

        if (cpuSamples.Length == 0)
        {
            unavailable.Add(DiagnosticCategory.Cpu);
        }

        if (diskSamples.Length == 0)
        {
            unavailable.Add(DiagnosticCategory.Disk);
        }

        if (storageSamples.Length == 0)
        {
            unavailable.Add(DiagnosticCategory.Storage);
        }

        AddIfPresent(findings, EvaluateMemory(memorySamples));
        AddIfPresent(findings, EvaluateCpu(cpuSamples));
        AddIfPresent(findings, EvaluateDisk(diskSamples));
        AddIfPresent(findings, EvaluateStorage(storageSamples));

        PerformanceFinding[] sortedFindings = findings
            .OrderByDescending(finding => finding.Status)
            .ThenByDescending(finding => finding.Confidence)
            .ThenBy(finding => finding.PriorityOrder)
            .ThenByDescending(finding => finding.ObservedDuration)
            .ToArray();

        DateTimeOffset generated = generatedAt ?? DateTimeOffset.UtcNow;
        TimeSpan observedWindow = ordered.Length >= 2
            ? ordered[^1].CapturedAt - ordered[0].CapturedAt
            : TimeSpan.Zero;

        bool isCollecting = !HasGeneralTimeCoverage(ordered);
        string statusMessage = BuildStatusMessage(sortedFindings, unavailable, isCollecting);

        return new DiagnosticReport(
            GeneratedAt: generated,
            SampleCount: ordered.Length,
            ObservedWindow: observedWindow,
            IsCollecting: isCollecting,
            StatusMessage: statusMessage,
            Findings: sortedFindings,
            NotMeasuredCategories: [DiagnosticCategory.Startup],
            UnavailableCategories: unavailable);
    }

    private PerformanceFinding? EvaluateMemory(IReadOnlyList<SystemPerformanceSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        bool severe = HasSustained(
            samples,
            sample => IsCriticalMemory(sample.Memory!),
            _options.MinimumMemoryDuration);

        bool warning = severe || HasSustained(
            samples,
            sample => IsWarningMemory(sample.Memory!),
            _options.MinimumMemoryDuration);

        if (!warning)
        {
            return null;
        }

        MemorySnapshot[] snapshots = samples.Select(sample => sample.Memory!).ToArray();
        SystemPerformanceSample lowestAvailableSample = samples
            .MinBy(sample => sample.Memory!.AvailablePercent)!;
        SystemPerformanceSample highestCommitSample = samples
            .Where(sample => sample.Memory!.CommitLimitBytes > 0)
            .OrderByDescending(sample => sample.Memory!.CommitPercent)
            .FirstOrDefault() ?? samples[^1];

        double lowestAvailable = snapshots.Min(snapshot => snapshot.AvailablePercent);
        double highestCommit = snapshots
            .Where(snapshot => snapshot.CommitLimitBytes > 0)
            .Select(snapshot => snapshot.CommitPercent)
            .DefaultIfEmpty(0)
            .Max();
        double pressureRatio = GetMatchRatio(samples, sample => IsWarningMemory(sample.Memory!));
        double severeRatio = GetMatchRatio(samples, sample => IsCriticalMemory(sample.Memory!));
        bool hasCommitData = snapshots.Any(snapshot => snapshot.CommitLimitBytes > 0);
        string commitNote = hasCommitData
            ? $"וה-Commit הגיע עד {highestCommit:0.0}%"
            : "ומדד Commit לא היה זמין";

        ProcessPerformanceSample? largestProcess = (samples[^1].Processes ?? [])
            .Where(process => process.PrivateBytes.HasValue && !string.IsNullOrWhiteSpace(process.Name))
            .OrderByDescending(process => process.PrivateBytes)
            .FirstOrDefault();

        string processNote = largestProcess is null
            ? "לא התקבל מידע מספיק כדי לציין תהליך מוביל."
            : $"התהליך עם הזיכרון הפרטי הגדול ביותר במדגם האחרון הוא {largestProcess.Name} ({FormatGiB(largestProcess.PrivateBytes!.Value)} GB). זה לא מוכיח שהתהליך מיותר.";

        string summary = severe
            ? $"סימני לחץ משמעותיים הופיעו ב-{severeRatio * 100d:0}% מהדגימות בחלון של {FormatDuration(GetDuration(samples))}. הזיכרון הזמין ירד עד {lowestAvailable:0.0}% {commitNote}. {processNote} לא נאספו עדיין נתוני Hard Faults, ולכן אין כאן קביעה שהמחשב מבצע Paging."
            : $"סימני עומס זיכרון הופיעו ב-{pressureRatio * 100d:0}% מהדגימות בחלון של {FormatDuration(GetDuration(samples))}, אך הם אינם מספיקים לקביעה שמדובר בלחץ חמור. {processNote}";

        var evidence = new List<FindingEvidence>
        {
            new(
                "RAM זמין",
                lowestAvailable.ToString("0.0"),
                "%",
                lowestAvailableSample.CapturedAt,
                "הערך הנמוך ביותר בחלון המדידה."),
            new(
                "דגימות עם סימני עומס",
                (pressureRatio * 100d).ToString("0"),
                "%",
                samples[^1].CapturedAt,
                "שיעור הדגימות שבהן לפחות מדד זיכרון אחד חצה את סף האזהרה.")
        };

        if (snapshots.Any(snapshot => snapshot.CommitLimitBytes > 0))
        {
            evidence.Add(new FindingEvidence(
                "Commit",
                highestCommit.ToString("0.0"),
                "%",
                highestCommitSample.CapturedAt,
                "שיא היחס בין Commit בשימוש לבין מגבלת Commit."));
        }

        if (largestProcess?.PrivateBytes is ulong privateBytes)
        {
            evidence.Add(new FindingEvidence(
                $"זיכרון פרטי של {largestProcess.Name}",
                FormatGiB(privateBytes),
                "GB",
                samples[^1].CapturedAt,
                "מדד תצפיתי בלבד; אין להסיק ממנו שהתהליך בטוח לסגירה."));
        }

        return new PerformanceFinding(
            Category: DiagnosticCategory.Memory,
            Status: severe ? FindingStatus.ActionRecommended : FindingStatus.Watch,
            Confidence: severe ? ConfidenceLevel.Medium : ConfidenceLevel.Low,
            Title: severe ? "נמדד לחץ זיכרון מתמשך" : "נמדדו סימני עומס זיכרון",
            Summary: summary,
            ObservedDuration: GetDuration(samples),
            Evidence: evidence,
            Impact: ImpactArea.Multitasking,
            Recommendation: "בדוק אילו תוכנות פתוחות ואינך צריך כרגע. שמור את העבודה לפני סגירת תוכנה, וסגור אותה בדרך הרגילה בלבד. אין צורך לנקות RAM או לרוקן Cache.",
            Risk: RiskLevel.Medium,
            Reversible: false,
            RestorePlan: "RAM Sentinel אינה סוגרת תוכנות במסגרת האבחון. אם סגרת תוכנה בעצמך, פתח אותה מחדש לפי הצורך; עבודה שלא נשמרה עלולה ללכת לאיבוד.",
            PriorityOrder: 10);
    }

    private PerformanceFinding? EvaluateCpu(IReadOnlyList<SystemPerformanceSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        bool severe = HasSustained(
            samples,
            sample => sample.CpuPercent!.Value >= _options.CpuActionThresholdPercent,
            _options.MinimumCpuDuration);

        bool warning = severe || HasSustained(
            samples,
            sample => sample.CpuPercent!.Value >= _options.CpuWatchThresholdPercent,
            _options.MinimumCpuDuration);

        if (!warning)
        {
            return null;
        }

        double averageCpu = samples.Average(sample => sample.CpuPercent!.Value);
        double peakCpu = samples.Max(sample => sample.CpuPercent!.Value);
        double loadRatio = GetMatchRatio(
            samples,
            sample => sample.CpuPercent!.Value >= (severe
                ? _options.CpuActionThresholdPercent
                : _options.CpuWatchThresholdPercent));
        ProcessCpuAggregate? leadingProcess = GetLeadingCpuProcess(samples);

        bool strongAttribution = leadingProcess is not null &&
                                 leadingProcess.AverageCpuPercent >= 15 &&
                                 leadingProcess.SampleCount >= Math.Ceiling(samples.Count * 0.65);

        string processNote = leadingProcess is null
            ? "לא זוהה תהליך יחיד עם מדידות מספיקות כדי לייחס לו את העומס."
            : $"התהליך המוביל במדידות הוא {leadingProcess.Name}, עם ממוצע של {leadingProcess.AverageCpuPercent:0.0}% מקיבולת המעבד הכוללת. זהו קשר למדידה, לא הוכחה לתקלה בתוכנה.";

        var evidence = new List<FindingEvidence>
        {
            new(
                "CPU כולל - ממוצע",
                averageCpu.ToString("0.0"),
                "%",
                samples[^1].CapturedAt,
                "ממוצע המדידות בחלון הזמן; לא קפיצה רגעית."),
            new(
                "CPU כולל - שיא",
                peakCpu.ToString("0.0"),
                "%",
                samples.MaxBy(sample => sample.CpuPercent)!.CapturedAt,
                "הערך הגבוה ביותר בחלון המדידה.")
        };

        if (leadingProcess is not null)
        {
            evidence.Add(new FindingEvidence(
                $"CPU של {leadingProcess.Name}",
                leadingProcess.AverageCpuPercent.ToString("0.0"),
                "%",
                samples[^1].CapturedAt,
                "האחוז מנורמל לקיבולת הכוללת של כל המעבדים הלוגיים."));
        }

        return new PerformanceFinding(
            Category: DiagnosticCategory.Cpu,
            Status: severe ? FindingStatus.ActionRecommended : FindingStatus.Watch,
            Confidence: strongAttribution ? ConfidenceLevel.High : ConfidenceLevel.Medium,
            Title: severe ? "המעבד עמוס לאורך זמן" : "נמדד עומס CPU מתמשך",
            Summary: $"עומס ה-CPU הממוצע היה {averageCpu:0.0}%, וסף העומס נחצה ב-{loadRatio * 100d:0}% מהדגימות בחלון של {FormatDuration(GetDuration(samples))}. {processNote}",
            ObservedDuration: GetDuration(samples),
            Evidence: evidence,
            Impact: ImpactArea.CurrentWorkload,
            Recommendation: leadingProcess is null
                ? "בדוק במנהל המשימות איזו תוכנה פעילה בזמן ההאטה. ייתכן שמדובר בעדכון, סריקה או משימה זמנית."
                : $"בדוק אם {leadingProcess.Name} מבצע כרגע פעולה שאתה מצפה לה. לפני סגירה שמור את העבודה, וסגור את התוכנה בדרך הרגילה בלבד.",
            Risk: severe ? RiskLevel.Medium : RiskLevel.Low,
            Reversible: false,
            RestorePlan: "האבחון אינו משנה עדיפות או שיוך ליבות ואינו סוגר תהליכים. אם בחרת לסגור תוכנה, פתח אותה מחדש לפי הצורך.",
            PriorityOrder: 20);
    }

    private PerformanceFinding? EvaluateDisk(IReadOnlyList<SystemPerformanceSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        bool severe = HasSustained(samples, IsCriticalDisk, _options.MinimumDiskDuration);
        bool warning = severe || HasSustained(samples, IsWarningDisk, _options.MinimumDiskDuration);

        if (!warning)
        {
            return null;
        }

        double averageLatency = samples
            .Select(sample => GetDiskLatency(sample.Disk!)!.Value)
            .Average();
        double loadRatio = GetMatchRatio(
            samples,
            severe ? IsCriticalDisk : IsWarningDisk);
        SystemPerformanceSample peakLatencySample = samples
            .MaxBy(sample => GetDiskLatency(sample.Disk!)!.Value)!;
        double peakLatency = GetDiskLatency(peakLatencySample.Disk!)!.Value;
        double averageActive = samples
            .Where(sample => IsValidPercentage(sample.Disk!.ActiveTimePercent))
            .Select(sample => sample.Disk!.ActiveTimePercent!.Value)
            .DefaultIfEmpty(0)
            .Average();
        double peakQueue = samples
            .Where(sample => IsValidCounter(sample.Disk!.QueueLength))
            .Select(sample => sample.Disk!.QueueLength!.Value)
            .DefaultIfEmpty(0)
            .Max();

        bool corroborated = HasSustained(
            samples,
            sample =>
                GetDiskLatency(sample.Disk!) is double latency &&
                latency >= _options.DiskActionLatencyMilliseconds &&
                sample.Disk!.ActiveTimePercent is double active &&
                active >= _options.DiskActionActivePercent &&
                sample.Disk!.QueueLength is double queue &&
                queue >= _options.DiskActionQueueLength,
            _options.MinimumDiskDuration);

        var evidence = new List<FindingEvidence>
        {
            new(
                "זמן תגובת דיסק ממוצע",
                averageLatency.ToString("0.0"),
                "ms",
                samples[^1].CapturedAt,
                "בכל דגימה נלקח הגבוה מבין זמן הקריאה והכתיבה הזמינים."),
            new(
                "שיא זמן תגובה",
                peakLatency.ToString("0.0"),
                "ms",
                peakLatencySample.CapturedAt,
                "הזמן שבו נמדד השיא; אינו מוצג לבדו כהוכחה לצוואר בקבוק."),
            new(
                "פעילות דיסק ממוצעת",
                averageActive.ToString("0.0"),
                "%",
                samples[^1].CapturedAt,
                "מדד עומס נוסף שנועד להבחין בין זמן תגובה גבוה בזמן פעילות לבין דגימה לא מייצגת."),
            new(
                "תור דיסק מרבי",
                peakQueue.ToString("0.00"),
                "פעולות",
                samples[^1].CapturedAt,
                "נבדק יחד עם זמן תגובה ופעילות, ולא כמדד יחיד.")
        };

        return new PerformanceFinding(
            Category: DiagnosticCategory.Disk,
            Status: severe ? FindingStatus.ActionRecommended : FindingStatus.Watch,
            Confidence: corroborated ? ConfidenceLevel.High : ConfidenceLevel.Medium,
            Title: severe ? "הדיסק מגיב באיטיות תחת עומס" : "נמדדו זמני תגובה גבוהים בדיסק",
            Summary: $"זמן התגובה הממוצע שנמדד הוא {averageLatency:0.0}ms, ותנאי העומס נחצו ב-{loadRatio * 100d:0}% מהדגימות בחלון של {FormatDuration(GetDuration(samples))}. המדידה אינה מזהה לבדה את הסיבה או את תקינות הכונן.",
            ObservedDuration: GetDuration(samples),
            Evidence: evidence,
            Impact: ImpactArea.AppLaunch,
            Recommendation: "בדוק אם מתבצעת העתקה, סנכרון, עדכון או סריקה ברקע. אם ההאטה חוזרת גם כשהמחשב אינו עסוק, בדוק את מצב הכונן בכלי Windows או בכלי היצרן. RAM Sentinel לא תריץ סריקה או איחוי אוטומטיים.",
            Risk: RiskLevel.Low,
            Reversible: true,
            RestorePlan: "לא בוצע שינוי בכונן; אין צורך בשחזור.",
            PriorityOrder: 30);
    }

    private PerformanceFinding? EvaluateStorage(IReadOnlyList<SystemPerformanceSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        SystemPerformanceSample latest = samples[^1];
        DiskMetricsSnapshot disk = latest.Disk!;
        ulong freeBytes = disk.SystemDriveFreeBytes!.Value;
        ulong totalBytes = disk.SystemDriveTotalBytes!.Value;
        double freePercent = disk.SystemDriveFreePercent!.Value;

        bool critical = freeBytes <= 5 * GiB ||
                        (freePercent <= 5 && freeBytes <= 20 * GiB);
        bool warning = critical ||
                       freeBytes <= 15 * GiB ||
                       (freePercent <= 10 && freeBytes <= 40 * GiB);

        if (!warning)
        {
            return null;
        }

        string freeText = FormatGiB(freeBytes);
        string totalText = FormatGiB(totalBytes);

        return new PerformanceFinding(
            Category: DiagnosticCategory.Storage,
            Status: critical ? FindingStatus.ActionRecommended : FindingStatus.Watch,
            Confidence: ConfidenceLevel.High,
            Title: critical ? "נותר מעט מאוד מקום בכונן Windows" : "המקום הפנוי בכונן Windows נמוך",
            Summary: $"נותרו {freeText} GB פנויים מתוך {totalText} GB ({freePercent:0.0}%). מחסור במקום עלול להפריע לעדכונים ולקבצים זמניים, אך אינו מוכיח שהדיסק עצמו איטי.",
            ObservedDuration: TimeSpan.Zero,
            Evidence:
            [
                new FindingEvidence(
                    "מקום פנוי בכונן Windows",
                    freeText,
                    "GB",
                    latest.CapturedAt,
                    "נפח פנוי שנקרא מ-Windows; לא בוצעה סריקה של קבצים.")
                ,
                new FindingEvidence(
                    "אחוז פנוי",
                    freePercent.ToString("0.0"),
                    "%",
                    latest.CapturedAt,
                    "מוצג לצד הנפח המוחלט כדי למנוע מסקנה לפי אחוז בלבד.")
            ],
            Impact: ImpactArea.GeneralReliability,
            Recommendation: "פתח את הגדרות האחסון של Windows ועבור על הקטגוריות וההמלצות. בדוק כל פריט בעצמך לפני מחיקה. RAM Sentinel לא מוחקת קבצים.",
            Risk: RiskLevel.Low,
            Reversible: true,
            RestorePlan: "לא בוצע שינוי; פתיחת הגדרות האחסון אינה משנה קבצים.",
            PriorityOrder: 100);
    }

    private bool HasSustained(
        IReadOnlyList<SystemPerformanceSample> samples,
        Func<SystemPerformanceSample, bool> condition,
        TimeSpan requiredDuration)
    {
        if (samples.Count < _options.MinimumSamples)
        {
            return false;
        }

        if (GetDuration(samples) < requiredDuration)
        {
            return false;
        }

        if (!HasAcceptableSampleGaps(samples))
        {
            return false;
        }

        int requiredMatches = (int)Math.Ceiling(samples.Count * _options.RequiredSustainedRatio);
        return samples.Count(condition) >= requiredMatches;
    }

    private bool HasGeneralTimeCoverage(IReadOnlyList<SystemPerformanceSample> samples) =>
        samples.Count >= _options.MinimumSamples &&
        GetDuration(samples) >= _options.MinimumDiskDuration &&
        HasAcceptableSampleGaps(samples);

    private bool HasAcceptableSampleGaps(IReadOnlyList<SystemPerformanceSample> samples)
    {
        for (int index = 1; index < samples.Count; index++)
        {
            if (samples[index].CapturedAt - samples[index - 1].CapturedAt > _options.MaximumSampleGap)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWarningMemory(MemorySnapshot memory) =>
        memory.AvailablePercent <= 12 ||
        memory.MemoryLoadPercent >= 88 ||
        (memory.CommitLimitBytes > 0 && memory.CommitPercent >= 90);

    private static bool IsCriticalMemory(MemorySnapshot memory)
    {
        bool lowAvailable = memory.AvailablePercent <= 8;
        bool highLoad = memory.MemoryLoadPercent >= 93;
        bool highCommit = memory.CommitLimitBytes > 0 && memory.CommitPercent >= 90;

        return (lowAvailable && (highLoad || highCommit)) ||
               (highCommit && memory.AvailablePercent <= 15);
    }

    private bool IsWarningDisk(SystemPerformanceSample sample)
    {
        DiskMetricsSnapshot disk = sample.Disk!;
        double? latency = GetDiskLatency(disk);
        if (!latency.HasValue || latency.Value < _options.DiskWatchLatencyMilliseconds)
        {
            return false;
        }

        bool active = IsValidPercentage(disk.ActiveTimePercent) &&
                      disk.ActiveTimePercent!.Value >= _options.DiskWatchActivePercent;
        bool queued = IsValidCounter(disk.QueueLength) &&
                      disk.QueueLength!.Value >= _options.DiskWatchQueueLength;

        return active || queued;
    }

    private bool IsCriticalDisk(SystemPerformanceSample sample)
    {
        DiskMetricsSnapshot disk = sample.Disk!;
        double? latency = GetDiskLatency(disk);
        if (!latency.HasValue || latency.Value < _options.DiskActionLatencyMilliseconds)
        {
            return false;
        }

        bool active = IsValidPercentage(disk.ActiveTimePercent) &&
                      disk.ActiveTimePercent!.Value >= _options.DiskActionActivePercent;
        bool queued = IsValidCounter(disk.QueueLength) &&
                      disk.QueueLength!.Value >= _options.DiskActionQueueLength;

        return active || queued;
    }

    private static bool HasValidStorageData(SystemPerformanceSample sample) =>
        sample.Disk is not null &&
        sample.Disk.SystemDriveFreeBytes is ulong freeBytes &&
        sample.Disk.SystemDriveTotalBytes is ulong totalBytes &&
        totalBytes > 0 &&
        freeBytes <= totalBytes;

    private static bool IsValidPercentage(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 && value.Value <= 100;

    private static bool IsValidCounter(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value >= 0;

    private static double? GetDiskLatency(DiskMetricsSnapshot disk)
    {
        double?[] values =
        [
            disk.ReadLatencyMilliseconds,
            disk.WriteLatencyMilliseconds
        ];

        double[] valid = values
            .Where(value => value.HasValue &&
                            double.IsFinite(value.Value) &&
                            value.Value >= 0 &&
                            value.Value <= 60_000)
            .Select(value => value!.Value)
            .ToArray();

        return valid.Length == 0 ? null : valid.Max();
    }

    private static ProcessCpuAggregate? GetLeadingCpuProcess(
        IReadOnlyList<SystemPerformanceSample> samples)
    {
        int minimumPresence = Math.Max(2, (int)Math.Ceiling(samples.Count * 0.65));

        return samples
            .SelectMany(sample => sample.Processes ?? [])
            .Where(process =>
                !string.IsNullOrWhiteSpace(process.Name) &&
                process.CpuPercent.HasValue &&
                IsValidPercentage(process.CpuPercent.Value))
            .GroupBy(process => (process.ProcessId, process.StartedAtUtc))
            .Select(group =>
            {
                ProcessPerformanceSample[] processSamples = group.ToArray();
                return new ProcessCpuAggregate(
                    ProcessId: group.Key.ProcessId,
                    Name: processSamples[^1].Name,
                    AverageCpuPercent: processSamples.Average(process => process.CpuPercent!.Value),
                    SampleCount: processSamples.Length);
            })
            .Where(process => process.SampleCount >= minimumPresence)
            .OrderByDescending(process => process.AverageCpuPercent)
            .FirstOrDefault();
    }

    private static double GetMatchRatio(
        IReadOnlyList<SystemPerformanceSample> samples,
        Func<SystemPerformanceSample, bool> condition) =>
        samples.Count == 0 ? 0 : (double)samples.Count(condition) / samples.Count;

    private static TimeSpan GetDuration(IReadOnlyList<SystemPerformanceSample> samples) =>
        samples.Count < 2
            ? TimeSpan.Zero
            : samples[^1].CapturedAt - samples[0].CapturedAt;

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:0} דקות"
            : $"{Math.Max(1, duration.TotalSeconds):0} שניות";

    private static string FormatGiB(ulong bytes) =>
        ((double)bytes / GiB).ToString("0.0");

    private static string BuildStatusMessage(
        IReadOnlyList<PerformanceFinding> findings,
        IReadOnlyList<DiagnosticCategory> unavailable,
        bool isCollecting)
    {
        if (findings.Count > 0 && isCollecting)
        {
            return "נמצאו סימנים ראשוניים. ממשיכים לאסוף נתונים כדי לוודא שמדובר בעומס מתמשך.";
        }

        if (findings.Count > 0)
        {
            return $"הבדיקה הסתיימה. נמצאו {findings.Count} ממצאים שכדאי לבדוק.";
        }

        if (isCollecting)
        {
            return "אוספים נתונים כדי להבדיל בין עומס רגעי לבעיה מתמשכת. אין צורך לבצע פעולה כרגע.";
        }

        if (unavailable.Count >= 4)
        {
            return "לא התקבלו מדדי ביצועים תקינים. בדוק שההרשאות ומוני Windows זמינים, ונסה שוב.";
        }

        if (unavailable.Count > 0)
        {
            return "לא זוהה צוואר בקבוק מתמשך בנתונים הזמינים. חלק מהמדדים לא היו זמינים במחשב זה.";
        }

        return "לא זוהה כרגע צוואר בקבוק מתמשך במדדים שנאספו.";
    }

    private static void AddIfPresent(
        ICollection<PerformanceFinding> findings,
        PerformanceFinding? finding)
    {
        if (finding is not null)
        {
            findings.Add(finding);
        }
    }

    private sealed record ProcessCpuAggregate(
        int ProcessId,
        string Name,
        double AverageCpuPercent,
        int SampleCount);
}
