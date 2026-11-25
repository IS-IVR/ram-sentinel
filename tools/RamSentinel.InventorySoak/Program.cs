using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RamSentinel.App.Platform;
using RamSentinel.Core;

namespace RamSentinel.InventorySoak;

internal static class Program
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    private static async Task<int> Main(string[] args)
    {
        SoakOptions options;
        try
        {
            options = SoakOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"שגיאה: {exception.Message}");
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("הכלי מיועד ל-Windows בלבד.");
            return 2;
        }

        string outputPath = Path.GetFullPath(
            options.OutputPath ??
            $"ram-sentinel-soak-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        try
        {
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine("לא ניתן ליצור את תיקיית קובץ התוצאות.");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var samples = new List<SoakSample>();
        var runClock = Stopwatch.StartNew();
        using Process currentProcess = Process.GetCurrentProcess();

        try
        {
            using var outputStream = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            await using var writer = new StreamWriter(outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            await writer.WriteLineAsync(SoakSample.CsvHeader);
            await writer.FlushAsync();

            Console.WriteLine("RAM Sentinel - בדיקת עומס לאיסוף תהליכים");
            Console.WriteLine($"תדירות דגימה: {(options.RunOnce ? "דגימה אחת" : $"כל {options.IntervalSeconds} שניות")}");
            Console.WriteLine($"משך: {(options.RunOnce ? "דגימה אחת" : $"{options.DurationMinutes} דקות")}");
            Console.WriteLine("הכלי אוסף מדדים מצטברים בלבד. הוא אינו שומר PID, שמות תהליכים או נתונים אישיים.");
            Console.WriteLine($"קובץ תוצאות: {outputPath}");
            Console.WriteLine();

            TimeSpan interval = TimeSpan.FromSeconds(options.IntervalSeconds);
            TimeSpan nextSampleAt = interval;
            DateTimeOffset? previousSampleAt = null;
            int sampleNumber = 0;

            while (!cancellation.IsCancellationRequested &&
                   (options.RunOnce || runClock.Elapsed < TimeSpan.FromMinutes(options.DurationMinutes)))
            {
                sampleNumber++;
                DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
                double? actualIntervalSeconds = previousSampleAt.HasValue
                    ? (capturedAt - previousSampleAt.Value).TotalSeconds
                    : null;
                previousSampleAt = capturedAt;

                SoakSample sample = CaptureSample(
                    sampleNumber,
                    capturedAt,
                    actualIntervalSeconds,
                    currentProcess);

                samples.Add(sample);
                await writer.WriteLineAsync(sample.ToCsv());
                await writer.FlushAsync();

                Console.WriteLine(
                    $"דגימה {sample.Number}: {sample.ElapsedMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms | " +
                    $"{sample.EnumeratedProcesses} תהליכים | " +
                    $"Toolhelp: {sample.ParentStatus} | " +
                    $"CPU הכלי: {Format(sample.CollectorCpuPercent, "0.0")}%");

                if (options.RunOnce)
                {
                    break;
                }

                while (nextSampleAt <= runClock.Elapsed)
                {
                    nextSampleAt += interval;
                }

                TimeSpan delay = nextSampleAt - runClock.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                nextSampleAt += interval;
            }
        }
        catch (IOException) when (File.Exists(outputPath))
        {
            Console.Error.WriteLine("קובץ התוצאות כבר קיים. בחר נתיב אחר כדי לא לדרוס קובץ קודם.");
            Console.CancelKeyPress -= cancelHandler;
            return 1;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Console.Error.WriteLine("שמירת בדיקת העומס נכשלה. לא נשמרו נתוני תהליכים אישיים.");
            Console.CancelKeyPress -= cancelHandler;
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        PrintSummary(samples, outputPath, cancellation.IsCancellationRequested);
        return samples.Count > 0 ? 0 : 1;
    }

    private static SoakSample CaptureSample(
        int sampleNumber,
        DateTimeOffset capturedAt,
        double? actualIntervalSeconds,
        Process currentProcess)
    {
        var timer = Stopwatch.StartNew();
        TimeSpan? cpuBefore = TryReadTimeSpan(() =>
        {
            currentProcess.Refresh();
            return currentProcess.TotalProcessorTime;
        });

        int enumeratedProcessCount = 0;
        bool captureFailed = false;
        ProcessInventorySnapshot inventory;

        try
        {
            inventory = OwnedSnapshotCapture.Execute(
                enumerate: () => Process.GetProcesses(),
                capture: processes =>
                {
                    enumeratedProcessCount = processes.Count;
                    return new WindowsProcessInventoryProvider().ReadAll(processes, capturedAt);
                },
                dispose: process => process.Dispose());
        }
        catch (Exception exception) when (IsExpectedCollectionFailure(exception))
        {
            captureFailed = true;
            inventory = new ProcessInventorySnapshot(
                CapturedAt: capturedAt,
                EnumerationStatus: ProcessEnumerationStatus.Failed,
                ParentMetadataStatus: ParentMetadataStatus.Unavailable,
                Entries: Array.Empty<ProcessInventoryEntry>(),
                FailureReason: "Process inventory capture failed.");
        }

        timer.Stop();
        TimeSpan? cpuAfter = TryReadTimeSpan(() =>
        {
            currentProcess.Refresh();
            return currentProcess.TotalProcessorTime;
        });

        double? collectorCpuPercent = null;
        if (cpuBefore.HasValue && cpuAfter.HasValue && timer.Elapsed.TotalMilliseconds > 0)
        {
            double cpuMilliseconds = (cpuAfter.Value - cpuBefore.Value).TotalMilliseconds;
            collectorCpuPercent = Math.Clamp(
                cpuMilliseconds / (timer.Elapsed.TotalMilliseconds * Math.Max(1, Environment.ProcessorCount)) * 100d,
                0d,
                100d);
        }

        long? collectorWorkingSet = TryReadInt64(() =>
        {
            currentProcess.Refresh();
            return currentProcess.WorkingSet64;
        });
        long? collectorPrivateBytes = TryReadInt64(() =>
        {
            currentProcess.Refresh();
            return currentProcess.PrivateMemorySize64;
        });

        IReadOnlyList<ProcessInventoryEntry> entries = inventory.Entries ?? Array.Empty<ProcessInventoryEntry>();
        ProcessInventoryEntry[] withWorkingSet = entries
            .Where(entry => entry.WorkingSetBytes.HasValue)
            .ToArray();

        double observedWorkingSetBytes = withWorkingSet.Sum(entry => (double)entry.WorkingSetBytes!.Value);
        int parentMetadataRows = entries.Count(entry => entry.ParentMetadataAvailable);

        return new SoakSample(
            Number: sampleNumber,
            CapturedAt: capturedAt,
            ActualIntervalSeconds: actualIntervalSeconds,
            ElapsedMilliseconds: timer.Elapsed.TotalMilliseconds,
            EnumeratedProcesses: enumeratedProcessCount,
            CapturedEntries: entries.Count,
            WorkingSetReadableEntries: withWorkingSet.Length,
            ObservedWorkingSetMegabytes: observedWorkingSetBytes / BytesPerMegabyte,
            CollectorCpuPercent: collectorCpuPercent,
            CollectorWorkingSetMegabytes: collectorWorkingSet.HasValue
                ? collectorWorkingSet.Value / BytesPerMegabyte
                : null,
            CollectorPrivateMegabytes: collectorPrivateBytes.HasValue
                ? collectorPrivateBytes.Value / BytesPerMegabyte
                : null,
            ManagedHeapMegabytes: GC.GetTotalMemory(forceFullCollection: false) / BytesPerMegabyte,
            EnumerationStatus: inventory.EnumerationStatus,
            ParentStatus: inventory.ParentMetadataStatus,
            ParentMetadataRows: parentMetadataRows,
            RelationshipsReady: inventory.CanAdvanceRelationships,
            FailureReasonPresent: captureFailed || !string.IsNullOrWhiteSpace(inventory.FailureReason));
    }

    private static bool IsExpectedCollectionFailure(Exception exception) =>
        exception is Win32Exception
            or InvalidOperationException
            or NotSupportedException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or AggregateException;

    private static TimeSpan? TryReadTimeSpan(Func<TimeSpan> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? TryReadInt64(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void PrintSummary(
        IReadOnlyList<SoakSample> samples,
        string outputPath,
        bool cancelled)
    {
        Console.WriteLine();
        Console.WriteLine(cancelled ? "הבדיקה נעצרה לפי בקשה." : "הבדיקה הסתיימה.");
        Console.WriteLine($"מספר דגימות: {samples.Count}");
        Console.WriteLine($"קובץ תוצאות: {outputPath}");

        if (samples.Count == 0)
        {
            return;
        }

        double[] elapsed = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
        double[] cpu = samples
            .Where(sample => sample.CollectorCpuPercent.HasValue)
            .Select(sample => sample.CollectorCpuPercent!.Value)
            .Order()
            .ToArray();

        int completeParentSnapshots = samples.Count(sample => sample.ParentStatus == ParentMetadataStatus.Complete);
        int relationshipsReady = samples.Count(sample => sample.RelationshipsReady);
        int maxProcessCount = samples.Max(sample => sample.EnumeratedProcesses);
        double maxCollectorWorkingSet = samples
            .Where(sample => sample.CollectorWorkingSetMegabytes.HasValue)
            .Select(sample => sample.CollectorWorkingSetMegabytes!.Value)
            .DefaultIfEmpty()
            .Max();

        Console.WriteLine($"זמן איסוף ממוצע: {elapsed.Average().ToString("0.0", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"זמן איסוף P95: {Percentile95(elapsed).ToString("0.0", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"זמן איסוף מרבי: {elapsed.Max().ToString("0.0", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"CPU ממוצע של כלי המדידה: {(cpu.Length == 0 ? "לא זמין" : cpu.Average().ToString("0.00", CultureInfo.InvariantCulture) + "%")}");
        Console.WriteLine($"שיא זיכרון Working Set של כלי המדידה: {maxCollectorWorkingSet.ToString("0.0", CultureInfo.InvariantCulture)} MB");
        Console.WriteLine($"שיא מספר תהליכים שנצפו: {maxProcessCount}");
        Console.WriteLine($"צילום Toolhelp מלא: {Percent(completeParentSnapshots, samples.Count)}");
        Console.WriteLine($"דגימות שאפשרו עדכון קשרים: {Percent(relationshipsReady, samples.Count)}");
    }

    private static double Percentile95(IReadOnlyList<double> sortedValues)
    {
        int index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * 0.95d) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static string Percent(int count, int total) =>
        $"{(total == 0 ? 0d : count * 100d / total).ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string Format(double? value, string format) =>
        value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "לא זמין";

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            RAM Sentinel - בדיקת עומס לאיסוף תהליכים

            שימוש:
              RamSentinel.InventorySoak.exe
              RamSentinel.InventorySoak.exe --duration-minutes 30 --interval-seconds 30
              RamSentinel.InventorySoak.exe --once
              RamSentinel.InventorySoak.exe --duration-minutes 30 --interval-seconds 30 --output "C:\Temp\ram-sentinel-soak.csv"

            אפשרויות:
              --duration-minutes <1..1440>   משך הבדיקה. ברירת מחדל: 30 דקות.
              --interval-seconds <1..3600>   מרווח מתוכנן בין דגימות. ברירת מחדל: 30 שניות.
              --output <path>                נתיב CSV. ברירת מחדל: קובץ חדש בתיקייה הנוכחית.
              --once                         מבצע דגימה אחת בלבד, עבור בדיקת תקינות מהירה.
              --help                         מציג עזרה.

            עצירה בטוחה: Ctrl+C. הדגימות שכבר נשמרו יישארו בקובץ.

            פרטיות:
              נשמרים רק מספרי תהליכים, זמני איסוף, מדדי זיכרון מצטברים ומצבי שלמות.
              לא נשמרים PID, שמות תהליכים, נתיבי קבצים, כותרות חלונות או פקודות הרצה.
            """);
    }

    private sealed record SoakOptions(
        int DurationMinutes,
        int IntervalSeconds,
        string? OutputPath,
        bool RunOnce,
        bool ShowHelp)
    {
        public static SoakOptions Parse(string[] args)
        {
            int durationMinutes = 30;
            int intervalSeconds = 30;
            string? outputPath = null;
            bool runOnce = false;
            bool showHelp = false;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        showHelp = true;
                        break;
                    case "--once":
                        runOnce = true;
                        break;
                    case "--duration-minutes":
                        durationMinutes = ParseValue(args, ref index, argument, 1, 1440);
                        break;
                    case "--interval-seconds":
                        intervalSeconds = ParseValue(args, ref index, argument, 1, 3600);
                        break;
                    case "--output":
                        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++index]))
                        {
                            throw new ArgumentException("חסר נתיב אחרי --output.");
                        }

                        outputPath = args[index];
                        break;
                    default:
                        throw new ArgumentException($"אפשרות לא מוכרת: {argument}");
                }
            }

            return new SoakOptions(durationMinutes, intervalSeconds, outputPath, runOnce, showHelp);
        }

        private static int ParseValue(
            string[] args,
            ref int index,
            string option,
            int minimum,
            int maximum)
        {
            if (index + 1 >= args.Length ||
                !int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < minimum ||
                value > maximum)
            {
                throw new ArgumentException(
                    $"הערך של {option} חייב להיות מספר בין {minimum} ל-{maximum}.");
            }

            return value;
        }
    }

    private sealed record SoakSample(
        int Number,
        DateTimeOffset CapturedAt,
        double? ActualIntervalSeconds,
        double ElapsedMilliseconds,
        int EnumeratedProcesses,
        int CapturedEntries,
        int WorkingSetReadableEntries,
        double ObservedWorkingSetMegabytes,
        double? CollectorCpuPercent,
        double? CollectorWorkingSetMegabytes,
        double? CollectorPrivateMegabytes,
        double ManagedHeapMegabytes,
        ProcessEnumerationStatus EnumerationStatus,
        ParentMetadataStatus ParentStatus,
        int ParentMetadataRows,
        bool RelationshipsReady,
        bool FailureReasonPresent)
    {
        public const string CsvHeader =
            "sample,utc_timestamp,actual_interval_seconds,elapsed_ms,enumerated_processes,captured_entries," +
            "working_set_readable_entries,observed_working_set_mb,collector_cpu_percent,collector_working_set_mb," +
            "collector_private_mb,managed_heap_mb,enumeration_status,parent_metadata_status,parent_metadata_rows," +
            "relationships_ready,failure_reason_present";

        public string ToCsv() => string.Join(",",
            Number.ToString(CultureInfo.InvariantCulture),
            CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            Csv(ActualIntervalSeconds),
            ElapsedMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
            EnumeratedProcesses.ToString(CultureInfo.InvariantCulture),
            CapturedEntries.ToString(CultureInfo.InvariantCulture),
            WorkingSetReadableEntries.ToString(CultureInfo.InvariantCulture),
            ObservedWorkingSetMegabytes.ToString("0.###", CultureInfo.InvariantCulture),
            Csv(CollectorCpuPercent),
            Csv(CollectorWorkingSetMegabytes),
            Csv(CollectorPrivateMegabytes),
            ManagedHeapMegabytes.ToString("0.###", CultureInfo.InvariantCulture),
            EnumerationStatus.ToString(),
            ParentStatus.ToString(),
            ParentMetadataRows.ToString(CultureInfo.InvariantCulture),
            RelationshipsReady.ToString(),
            FailureReasonPresent.ToString());

        private static string Csv(double? value) =>
            value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
    }
}
