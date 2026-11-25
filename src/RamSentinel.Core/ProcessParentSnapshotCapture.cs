namespace RamSentinel.Core;

/// <summary>Result returned by the process-snapshot creation boundary.</summary>
public sealed record ProcessParentSnapshotCreation<THandle>(
    bool Succeeded,
    THandle Handle,
    int ErrorCode = 0);

/// <summary>One native parent-metadata enumeration result.</summary>
public sealed record ProcessParentSnapshotEntryResult(
    bool Succeeded,
    int ProcessId = 0,
    int? ParentProcessId = null,
    int ErrorCode = 0);

/// <summary>Result returned after releasing the native snapshot handle.</summary>
public sealed record ProcessParentSnapshotCloseResult(
    bool Succeeded,
    int ErrorCode = 0);

/// <summary>
/// Read-only parent metadata result. An empty dictionary can be complete or unavailable;
/// callers must always inspect Status rather than inferring success from the entry count.
/// </summary>
public sealed record ProcessParentSnapshotResult(
    IReadOnlyDictionary<int, int?> ParentIds,
    ParentMetadataStatus Status,
    string? FailureReason);

/// <summary>
/// Applies the Toolhelp-style snapshot state machine through injectable callbacks.
/// The Core checks error semantics, duplicate IDs, and handle release without depending
/// on Win32 or live processes. The Windows adapter supplies the actual native callbacks.
/// </summary>
public static class ProcessParentSnapshotCapture
{
    public static ProcessParentSnapshotResult Capture<THandle>(
        Func<ProcessParentSnapshotCreation<THandle>> create,
        Func<THandle, ProcessParentSnapshotEntryResult> first,
        Func<THandle, ProcessParentSnapshotEntryResult> next,
        Func<THandle, ProcessParentSnapshotCloseResult> close,
        int endOfEnumerationErrorCode)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(close);

        if (endOfEnumerationErrorCode <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endOfEnumerationErrorCode));
        }

        var parentIds = new Dictionary<int, int?>();
        ProcessParentSnapshotCreation<THandle> created;

        try
        {
            created = create();
        }
        catch (Exception exception)
        {
            return Failure(
                parentIds,
                ParentMetadataStatus.Unavailable,
                $"Process snapshot creation threw: {exception.Message}");
        }

        if (!created.Succeeded)
        {
            return Failure(
                parentIds,
                ParentMetadataStatus.Unavailable,
                $"Process snapshot creation failed (error {created.ErrorCode}).");
        }

        ParentMetadataStatus status = ParentMetadataStatus.Unavailable;
        string? failureReason = null;
        bool invalidEntry = false;

        try
        {
            ProcessParentSnapshotEntryResult current = first(created.Handle);
            if (!current.Succeeded)
            {
                if (current.ErrorCode == endOfEnumerationErrorCode)
                {
                    status = ParentMetadataStatus.Complete;
                }
                else
                {
                    status = ParentMetadataStatus.Unavailable;
                    failureReason = $"First process snapshot entry failed (error {current.ErrorCode}).";
                }
            }
            else
            {
                invalidEntry |= !TryAddEntry(parentIds, current, out string? firstError);
                if (firstError is not null)
                {
                    failureReason = firstError;
                }

                while (true)
                {
                    current = next(created.Handle);
                    if (!current.Succeeded)
                    {
                        if (current.ErrorCode == endOfEnumerationErrorCode)
                        {
                            status = invalidEntry
                                ? ParentMetadataStatus.Partial
                                : ParentMetadataStatus.Complete;
                        }
                        else
                        {
                            status = ParentMetadataStatus.Partial;
                            failureReason = AppendReason(
                                failureReason,
                                $"Process snapshot enumeration stopped unexpectedly (error {current.ErrorCode}).");
                        }

                        break;
                    }

                    bool added = TryAddEntry(parentIds, current, out string? entryError);
                    if (!added)
                    {
                        invalidEntry = true;
                    }

                    if (entryError is not null)
                    {
                        failureReason = AppendReason(failureReason, entryError);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            status = parentIds.Count == 0
                ? ParentMetadataStatus.Unavailable
                : ParentMetadataStatus.Partial;
            failureReason = AppendReason(
                failureReason,
                $"Process snapshot enumeration threw: {exception.Message}");
        }
        finally
        {
            try
            {
                ProcessParentSnapshotCloseResult closed = close(created.Handle);
                if (!closed.Succeeded)
                {
                    if (status == ParentMetadataStatus.Complete)
                    {
                        status = ParentMetadataStatus.Partial;
                    }

                    failureReason = AppendReason(
                        failureReason,
                        $"Process snapshot handle could not be closed (error {closed.ErrorCode}).");
                }
            }
            catch (Exception exception)
            {
                if (status == ParentMetadataStatus.Complete)
                {
                    status = ParentMetadataStatus.Partial;
                }

                failureReason = AppendReason(
                    failureReason,
                    $"Closing the process snapshot threw: {exception.Message}");
            }
        }

        return new ProcessParentSnapshotResult(parentIds, status, failureReason);
    }

    private static bool TryAddEntry(
        IDictionary<int, int?> parentIds,
        ProcessParentSnapshotEntryResult entry,
        out string? error)
    {
        error = null;

        if (entry.ProcessId == 0)
        {
            // Toolhelp may return the Windows System Idle pseudo-process.
            return true;
        }

        if (entry.ProcessId < 0 ||
            entry.ParentProcessId is < 0 ||
            entry.ParentProcessId == entry.ProcessId)
        {
            error = "The process snapshot contained an invalid PID/parent PID entry.";
            return false;
        }

        if (!parentIds.TryAdd(
                entry.ProcessId,
                entry.ParentProcessId is > 0 ? entry.ParentProcessId : null))
        {
            error = $"The process snapshot contained duplicate PID {entry.ProcessId}.";
            return false;
        }

        return true;
    }

    private static ProcessParentSnapshotResult Failure(
        IReadOnlyDictionary<int, int?> parentIds,
        ParentMetadataStatus status,
        string reason) =>
        new(parentIds, status, reason);

    private static string AppendReason(string? current, string next) =>
        string.IsNullOrWhiteSpace(current)
            ? next
            : $"{current} {next}";
}
