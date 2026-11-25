using System.Diagnostics;
using RamSentinel.App.Platform;
using RamSentinel.Core;

try
{
    DateTimeOffset capturedAt = DateTimeOffset.UtcNow;

    ProcessInventorySnapshot snapshot = OwnedSnapshotCapture.Execute(
        enumerate: () => new[] { Process.GetCurrentProcess() },
        capture: processes => new WindowsProcessInventoryProvider().ReadAll(processes, capturedAt),
        dispose: process => process.Dispose());

    Assert(snapshot.EnumerationStatus == ProcessEnumerationStatus.Complete,
        $"Expected complete process enumeration, received {snapshot.EnumerationStatus}.");
    Assert(snapshot.ParentMetadataStatus == ParentMetadataStatus.Complete,
        $"Expected complete Toolhelp metadata, received {snapshot.ParentMetadataStatus}: {snapshot.FailureReason}");
    Assert(snapshot.Entries.Count == 1,
        $"Expected one captured process row, received {snapshot.Entries.Count}.");

    ProcessInventoryEntry entry = snapshot.Entries[0];
    Assert(entry.ProcessId == Environment.ProcessId,
        "The inventory PID must match the current Windows process.");
    Assert(entry.Identity is not null,
        "The current process must have a verified creation-time identity.");
    Assert(entry.Identity!.StartedAtUtc <= snapshot.CapturedAt,
        "The process creation time must not be later than the inventory sample.");
    Assert(!string.IsNullOrWhiteSpace(entry.Name),
        "The current process name must be readable.");
    Assert(entry.WorkingSetBytes is > 0,
        "The current process working set should be readable and positive.");
    Assert(entry.PrivateBytes is > 0,
        "The current process private bytes should be readable and positive.");
    Assert(entry.TotalCpuTime.HasValue,
        "The current process cumulative CPU time should be readable.");
    Assert(entry.ParentMetadataAvailable,
        "Toolhelp must provide a parent-metadata row for the current process.");
    Assert(entry.CapturedAt == capturedAt.ToUniversalTime(),
        "The process entry must reuse the exact sample timestamp.");
    Assert(snapshot.CanAdvanceRelationships,
        "A complete, single-process Windows sample should satisfy the Core relationship gate.");

    Console.WriteLine("PASS: Windows process property reader and Toolhelp provider integration");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: Windows process inventory integration - {exception}");
    Environment.ExitCode = 1;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
