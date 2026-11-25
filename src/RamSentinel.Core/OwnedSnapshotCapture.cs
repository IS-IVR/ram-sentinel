namespace RamSentinel.Core;

/// <summary>
/// Executes one owned snapshot operation. Enumeration is called exactly once, and every
/// returned item is disposed exactly once even when capture or another disposal fails.
/// </summary>
public static class OwnedSnapshotCapture
{
    public static TResult Execute<TItem, TResult>(
        Func<TItem[]> enumerate,
        Func<IReadOnlyList<TItem>, TResult> capture,
        Action<TItem> dispose)
    {
        ArgumentNullException.ThrowIfNull(enumerate);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(dispose);

        TItem[] items = enumerate()
            ?? throw new InvalidOperationException("The snapshot enumerator returned null.");

        TResult? result = default;
        Exception? captureFailure = null;

        try
        {
            result = capture(items);
        }
        catch (Exception exception)
        {
            captureFailure = exception;
        }

        var disposalFailures = new List<Exception>();
        foreach (TItem item in items)
        {
            try
            {
                dispose(item);
            }
            catch (Exception exception)
            {
                disposalFailures.Add(exception);
            }
        }

        if (captureFailure is not null && disposalFailures.Count > 0)
        {
            throw new AggregateException(
                "Snapshot capture failed and one or more items could not be disposed.",
                new[] { captureFailure }.Concat(disposalFailures));
        }

        if (captureFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(captureFailure)
                .Throw();
        }

        if (disposalFailures.Count > 0)
        {
            throw new AggregateException(
                "One or more snapshot items could not be disposed.",
                disposalFailures);
        }

        return result!;
    }
}
