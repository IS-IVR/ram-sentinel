using System.Diagnostics;
using RamSentinel.Core;

internal static class LifecycleEngineChecks
{
    public static IReadOnlyList<(string Name, Action Run)> All =>
    [
        ("A parent-child relationship needs repeated direct observations", TestRepeatedObservation),
        ("A reused parent PID starts a new relationship", TestParentPidReuse),
        ("A missing parent is not inferred from a stale PID", TestMissingParent),
        ("Duplicate process IDs make relationships ambiguous", TestAmbiguousParent),
        ("Out-of-order inventories do not change relationship state", TestOutOfOrderSnapshot),
        ("A long observation gap resets relationship confidence", TestObservationGap),
        ("Incomplete process enumeration blocks relationship tracking", TestIncompleteEnumeration),
        ("Partial parent metadata blocks relationship tracking", TestPartialParentMetadata),
        ("A process without verified identity blocks relationship tracking", TestMissingIdentity),
        ("A valid empty inventory is distinguishable from failure", TestCompleteEmptyInventory),
        ("An identity with a different PID blocks relationship tracking", TestIdentityPidMismatch),
        ("An entry captured at a different time blocks relationship tracking", TestCapturedAtMismatch),
        ("Unavailable parent metadata on an entry blocks relationship tracking", TestEntryParentMetadataUnavailable),
        ("Duplicate PIDs block relationship tracking", TestDuplicateInventoryPid),
        ("Owned snapshot enumerates once and disposes every item", TestOwnedSnapshotOwnership),
        ("Owned snapshot disposes every item after capture failure", TestOwnedSnapshotDisposalAfterFailure),
        ("An inventory failure reason blocks relationship tracking", TestFailureReasonBlocksTracking),
        ("An invalid parent PID blocks relationship tracking", TestInvalidParentPidBlocksTracking),
        ("A parent cannot start after its child", TestParentCreatedAfterChild),
        ("A future process creation time blocks relationship tracking", TestFutureProcessIdentity),
        ("Toolhelp creation failure is unavailable", TestParentSnapshotCreationFailure),
        ("Toolhelp first-entry failure is unavailable", TestParentSnapshotFirstEntryFailure),
        ("An empty Toolhelp snapshot is a successful empty result", TestParentSnapshotEmpty),
        ("A mid-enumeration Toolhelp error is partial", TestParentSnapshotMidEnumerationFailure),
        ("A normal Toolhelp end-of-list is complete", TestParentSnapshotNormalEnd),
        ("A Toolhelp close failure downgrades completeness", TestParentSnapshotCloseFailure),
        ("Duplicate Toolhelp PIDs downgrade completeness", TestParentSnapshotDuplicatePid),
        ("Process properties are each read exactly once", TestProcessPropertiesReadOnce),
        ("Unavailable process properties remain null", TestProcessPropertiesRemainNullWhenUnavailable),
        ("An unavailable PID prevents other process-property reads", TestProcessIdFailureStopsCapture),
        ("Processor derives UI and diagnostic rows from the same immutable inventory", TestProcessorSharedSnapshotConsumers),
        ("Incomplete inventory clears previous relationship confidence", TestProcessorClearsRelationshipsOnIncompleteInventory),
        ("Missing CPU samples reset the cumulative CPU baseline", TestProcessorResetsMissingCpuBaseline),
        ("A reused PID never inherits another process instance CPU history", TestProcessorPidReuseResetsCpuHistory),
        ("Out-of-order inventories cannot replace a newer CPU baseline", TestProcessorOutOfOrderSampleDoesNotChangeCpuBaseline),
    ];

    private static void TestRepeatedObservation()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker();
        ProcessLifecycleObservation[] first = Snapshot(start, Parent(start), Child(start));

        Assert(tracker.Observe(start, first).Count == 0,
            "A single inventory must not establish a stable relationship.");

        DateTimeOffset secondAt = start.AddSeconds(30);
        ProcessRelationship[] relationships = tracker
            .Observe(secondAt, Snapshot(secondAt, Parent(start), Child(start)))
            .ToArray();

        Assert(relationships.Length == 1, "The repeated relationship should be reported.");
        Assert(relationships[0].ConsecutiveObservations == 2, "The relationship should record two observations.");
        Assert(relationships[0].Confidence == ConfidenceLevel.Medium, "Two observations should produce Medium confidence.");
    }

    private static void TestParentPidReuse()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker();
        _ = tracker.Observe(start, Snapshot(start, Parent(start), Child(start)));

        DateTimeOffset reusedAt = start.AddSeconds(30);
        ProcessLifecycleObservation replacementParent = Parent(reusedAt);
        ProcessRelationship[] relationships = tracker
            .Observe(reusedAt, Snapshot(reusedAt, replacementParent, Child(start)))
            .ToArray();

        Assert(relationships.Length == 0,
            "A new process instance reusing the parent PID must not inherit the previous relationship.");
    }

    private static void TestMissingParent()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker();
        ProcessLifecycleObservation child = Child(at);

        Assert(tracker.Observe(at, [child]).Count == 0,
            "A child whose parent is absent from the current inventory must remain unlinked.");
    }

    private static void TestAmbiguousParent()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker();
        ProcessLifecycleObservation parent = Parent(at);
        ProcessLifecycleObservation duplicate = parent with
        {
            Identity = new ProcessIdentity(parent.Identity.ProcessId, at.AddSeconds(-1))
        };

        Assert(tracker.Observe(at, Snapshot(at, parent, duplicate, Child(at))).Count == 0,
            "Duplicate PID identities in one inventory must make the relationship ambiguous.");
    }

    private static void TestOutOfOrderSnapshot()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker();
        _ = tracker.Observe(at, Snapshot(at, Parent(at), Child(at)));

        DateTimeOffset earlier = at.AddSeconds(-5);
        Assert(tracker.Observe(earlier, Snapshot(earlier, Parent(at), Child(at))).Count == 0,
            "An older snapshot must not advance relationship evidence.");

        DateTimeOffset later = at.AddSeconds(30);
        ProcessRelationship[] relationships = tracker
            .Observe(later, Snapshot(later, Parent(at), Child(at)))
            .ToArray();
        Assert(relationships.Length == 1 && relationships[0].ConsecutiveObservations == 2,
            "Only the valid later snapshot should advance the count.");
    }

    private static void TestObservationGap()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var tracker = new ProcessRelationshipTracker(TimeSpan.FromMinutes(1));
        _ = tracker.Observe(at, Snapshot(at, Parent(at), Child(at)));

        DateTimeOffset afterGap = at.AddMinutes(2);
        Assert(tracker.Observe(afterGap, Snapshot(afterGap, Parent(at), Child(at))).Count == 0,
            "A long gap must reset the relationship instead of implying continuous observation.");
    }

    private static void TestIncompleteEnumeration()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var snapshot = new ProcessInventorySnapshot(
            at,
            ProcessEnumerationStatus.Failed,
            ParentMetadataStatus.Complete,
            [InventoryEntry(at)],
            "Enumeration failed");

        Assert(!snapshot.CanAdvanceRelationships,
            "A failed top-level enumeration must never advance relationship evidence.");
    }

    private static void TestPartialParentMetadata()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var snapshot = new ProcessInventorySnapshot(
            at,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Partial,
            [InventoryEntry(at)]);

        Assert(!snapshot.CanAdvanceRelationships,
            "Partial parent metadata must never advance relationship evidence.");
    }

    private static void TestMissingIdentity()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var entry = InventoryEntry(at) with { Identity = null };
        var snapshot = new ProcessInventorySnapshot(
            at,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Complete,
            [entry]);

        Assert(!snapshot.CanAdvanceRelationships,
            "A process without a verified creation time must not enter identity-based tracking.");
    }

    private static void TestCompleteEmptyInventory()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var snapshot = new ProcessInventorySnapshot(
            at,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Complete,
            Array.Empty<ProcessInventoryEntry>());

        Assert(snapshot.CanAdvanceRelationships,
            "A valid empty inventory is complete and must be distinguishable from enumeration failure.");
    }

    private static void TestIdentityPidMismatch()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var entry = InventoryEntry(at) with
        {
            Identity = new ProcessIdentity(101, at.AddMinutes(-1))
        };
        var snapshot = CompleteSnapshot(at, entry);

        Assert(!snapshot.CanAdvanceRelationships,
            "An identity belonging to another PID must never be accepted for this process row.");
    }

    private static void TestCapturedAtMismatch()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var entry = InventoryEntry(at) with { CapturedAt = at.AddMilliseconds(1) };
        var snapshot = CompleteSnapshot(at, entry);

        Assert(!snapshot.CanAdvanceRelationships,
            "Every entry must use the exact timestamp assigned to its inventory snapshot.");
    }

    private static void TestDuplicateInventoryPid()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        ProcessInventoryEntry first = InventoryEntry(at);
        ProcessInventoryEntry duplicate = first with
        {
            Identity = new ProcessIdentity(first.ProcessId, at.AddSeconds(-1))
        };
        ProcessInventorySnapshot snapshot = new(
            at,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Complete,
            [first, duplicate]);

        Assert(!snapshot.CanAdvanceRelationships,
            "Duplicate PIDs must block relationship tracking even when both entries have valid identities.");
    }

    private static void TestOwnedSnapshotOwnership()
    {
        int enumerationCalls = 0;
        int captureCalls = 0;
        var disposed = new List<int>();

        int result = OwnedSnapshotCapture.Execute(
            enumerate: () =>
            {
                enumerationCalls++;
                return new[] { 1, 2, 3 };
            },
            capture: items =>
            {
                captureCalls++;
                return items.Sum();
            },
            dispose: item => disposed.Add(item));

        Assert(result == 6, "The capture result should be returned.");
        Assert(enumerationCalls == 1, "The enumerator must be called exactly once.");
        Assert(captureCalls == 1, "The capture callback must be called exactly once.");
        Assert(disposed.SequenceEqual([1, 2, 3]),
            "Every owned item must be disposed exactly once in enumeration order.");
    }

    private static void TestOwnedSnapshotDisposalAfterFailure()
    {
        int enumerationCalls = 0;
        var disposed = new List<int>();
        bool threw = false;

        try
        {
            _ = OwnedSnapshotCapture.Execute<int, int>(
                enumerate: () =>
                {
                    enumerationCalls++;
                    return new[] { 4, 5, 6 };
                },
                capture: _ => throw new InvalidOperationException("Expected capture failure."),
                dispose: item => disposed.Add(item));
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "Expected capture failure.")
        {
            threw = true;
        }

        Assert(threw, "The original capture failure must be rethrown.");
        Assert(enumerationCalls == 1, "The enumerator must still be called exactly once.");
        Assert(disposed.SequenceEqual([4, 5, 6]),
            "Every owned item must be disposed exactly once after capture fails.");
    }

    private static void TestFailureReasonBlocksTracking()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        ProcessInventorySnapshot snapshot = CompleteSnapshot(at, InventoryEntry(at)) with
        {
            FailureReason = "The provider reported an incomplete capture."
        };

        Assert(!snapshot.CanAdvanceRelationships,
            "A snapshot carrying a failure reason must not advance relationship evidence.");
    }

    private static void TestInvalidParentPidBlocksTracking()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        ProcessInventoryEntry entry = InventoryEntry(at) with { ParentProcessId = 0 };
        ProcessInventorySnapshot snapshot = CompleteSnapshot(at, entry);

        Assert(!snapshot.CanAdvanceRelationships,
            "A non-null parent PID must be a positive process identifier.");
    }

    private static void TestParentCreatedAfterChild()
    {
        DateTimeOffset childStartedAt = DateTimeOffset.UtcNow;
        DateTimeOffset parentStartedAt = childStartedAt.AddSeconds(1);
        DateTimeOffset firstCapturedAt = childStartedAt.AddSeconds(2);
        var tracker = new ProcessRelationshipTracker();

        ProcessLifecycleObservation parent = Parent(parentStartedAt);
        ProcessLifecycleObservation child = Child(childStartedAt);
        Assert(tracker.Observe(firstCapturedAt, Snapshot(firstCapturedAt, parent, child)).Count == 0,
            "A process that started after the child cannot be its parent.");

        DateTimeOffset secondCapturedAt = firstCapturedAt.AddSeconds(30);
        Assert(tracker.Observe(
            secondCapturedAt,
            Snapshot(secondCapturedAt, parent, child)).Count == 0,
            "Repeated PID observations must not turn an impossible chronology into a relationship.");
    }

    private static void TestFutureProcessIdentity()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        ProcessInventoryEntry entry = InventoryEntry(capturedAt) with
        {
            Identity = new ProcessIdentity(100, capturedAt.AddSeconds(1))
        };
        ProcessInventorySnapshot snapshot = CompleteSnapshot(capturedAt, entry);

        Assert(!snapshot.CanAdvanceRelationships,
            "A process creation timestamp after the sample timestamp is invalid.");
    }

    private static void TestParentSnapshotCreationFailure()
    {
        int firstCalls = 0;
        int closeCalls = 0;
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(false, 0, 5),
            first: _ =>
            {
                firstCalls++;
                return new ProcessParentSnapshotEntryResult(false, ErrorCode: 18);
            },
            next: _ => throw new InvalidOperationException("Next must not be called."),
            close: _ =>
            {
                closeCalls++;
                return new ProcessParentSnapshotCloseResult(true);
            },
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Unavailable,
            "A failed snapshot creation must be unavailable.");
        Assert(result.ParentIds.Count == 0 && result.FailureReason is not null,
            "Creation failure must not look like a successful empty result.");
        Assert(firstCalls == 0 && closeCalls == 0,
            "No enumeration or close callback is valid when creation did not return a handle.");
    }

    private static void TestParentSnapshotFirstEntryFailure()
    {
        int closeCalls = 0;
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 10),
            first: _ => new ProcessParentSnapshotEntryResult(false, ErrorCode: 5),
            next: _ => throw new InvalidOperationException("Next must not be called."),
            close: _ =>
            {
                closeCalls++;
                return new ProcessParentSnapshotCloseResult(true);
            },
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Unavailable,
            "An unexpected Process32First failure must not be treated as an empty snapshot.");
        Assert(closeCalls == 1, "A successfully created snapshot handle must be closed exactly once.");
    }

    private static void TestParentSnapshotEmpty()
    {
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 11),
            first: _ => new ProcessParentSnapshotEntryResult(false, ErrorCode: 18),
            next: _ => throw new InvalidOperationException("Next must not be called."),
            close: _ => new ProcessParentSnapshotCloseResult(true),
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Complete,
            "ERROR_NO_MORE_FILES from the first call represents a valid empty snapshot.");
        Assert(result.ParentIds.Count == 0 && result.FailureReason is null,
            "A valid empty snapshot must remain distinct from failure.");
    }

    private static void TestParentSnapshotMidEnumerationFailure()
    {
        int nextCalls = 0;
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 12),
            first: _ => new ProcessParentSnapshotEntryResult(true, 100, null),
            next: _ =>
            {
                nextCalls++;
                return nextCalls == 1
                    ? new ProcessParentSnapshotEntryResult(true, 200, 100)
                    : new ProcessParentSnapshotEntryResult(false, ErrorCode: 5);
            },
            close: _ => new ProcessParentSnapshotCloseResult(true),
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Partial,
            "A non-end-of-list terminal error must mark the snapshot partial.");
        Assert(result.ParentIds.Count == 2 && result.ParentIds[200] == 100,
            "Entries read before the failure may be retained as partial evidence, not as complete evidence.");
        Assert(result.FailureReason is not null,
            "A partial enumeration must retain a diagnostic reason.");
    }

    private static void TestParentSnapshotNormalEnd()
    {
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 13),
            first: _ => new ProcessParentSnapshotEntryResult(true, 300, null),
            next: _ => new ProcessParentSnapshotEntryResult(false, ErrorCode: 18),
            close: _ => new ProcessParentSnapshotCloseResult(true),
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Complete,
            "ERROR_NO_MORE_FILES after the final entry represents a complete snapshot.");
        Assert(result.ParentIds.TryGetValue(300, out int? parent) && parent is null,
            "A known root process must preserve its known-null parent entry.");
    }

    private static void TestParentSnapshotCloseFailure()
    {
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 14),
            first: _ => new ProcessParentSnapshotEntryResult(false, ErrorCode: 18),
            next: _ => throw new InvalidOperationException("Next must not be called."),
            close: _ => new ProcessParentSnapshotCloseResult(false, 6),
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Partial,
            "A snapshot handle close failure must prevent a complete status.");
        Assert(result.FailureReason is not null,
            "A close failure must remain diagnosable.");
    }

    private static void TestParentSnapshotDuplicatePid()
    {
        int nextCalls = 0;
        ProcessParentSnapshotResult result = ProcessParentSnapshotCapture.Capture<int>(
            create: () => new ProcessParentSnapshotCreation<int>(true, 15),
            first: _ => new ProcessParentSnapshotEntryResult(true, 400, null),
            next: _ =>
            {
                nextCalls++;
                return nextCalls == 1
                    ? new ProcessParentSnapshotEntryResult(true, 400, 200)
                    : new ProcessParentSnapshotEntryResult(false, ErrorCode: 18);
            },
            close: _ => new ProcessParentSnapshotCloseResult(true),
            endOfEnumerationErrorCode: 18);

        Assert(result.Status == ParentMetadataStatus.Partial,
            "A duplicate PID in a native snapshot must never be reported as complete.");
    }

    private static void TestProcessPropertiesReadOnce()
    {
        using var process = new Process();
        var access = new CountingProcessPropertyAccess();
        var reader = new ProcessPropertyReader(access);

        ProcessPropertyObservation observation = reader.Capture(process);

        Assert(observation.ProcessId == access.ProcessId, "The captured PID should match the accessor.");
        Assert(observation.StartedAtUtc == access.StartedAtUtc, "The creation timestamp should be reused.");
        Assert(observation.Name == access.Name, "The process name should be reused.");
        Assert(observation.WorkingSetBytes == (ulong)access.WorkingSetBytes,
            "Working set should be converted without changing its value.");
        Assert(observation.PrivateBytes == (ulong)access.PrivateBytes,
            "Private bytes should be converted without changing its value.");
        Assert(observation.TotalCpuTime == access.TotalCpuTime,
            "Cumulative CPU time should be reused.");

        Assert(access.ProcessIdReads == 1, "PID must be read exactly once.");
        Assert(access.StartedAtReads == 1, "Start time must be read exactly once.");
        Assert(access.NameReads == 1, "Process name must be read exactly once.");
        Assert(access.WorkingSetReads == 1, "Working set must be read exactly once.");
        Assert(access.PrivateBytesReads == 1, "Private bytes must be read exactly once.");
        Assert(access.CpuTimeReads == 1, "Cumulative CPU time must be read exactly once.");
    }

    private static void TestProcessPropertiesRemainNullWhenUnavailable()
    {
        using var process = new Process();
        var access = new CountingProcessPropertyAccess
        {
            ThrowOnStartTime = true,
            ThrowOnName = true,
            ThrowOnWorkingSet = true,
            ThrowOnPrivateBytes = true,
            ThrowOnCpuTime = true
        };
        var reader = new ProcessPropertyReader(access);

        ProcessPropertyObservation observation = reader.Capture(process);

        Assert(observation.ProcessId == access.ProcessId,
            "The readable PID should remain available.");
        Assert(observation.StartedAtUtc is null, "An inaccessible creation time must stay null.");
        Assert(observation.Name is null, "An inaccessible process name must stay null.");
        Assert(observation.WorkingSetBytes is null, "An inaccessible working set must stay null.");
        Assert(observation.PrivateBytes is null, "Inaccessible private bytes must stay null.");
        Assert(observation.TotalCpuTime is null, "Inaccessible cumulative CPU time must stay null.");

        Assert(access.StartedAtReads == 1 && access.NameReads == 1 &&
               access.WorkingSetReads == 1 && access.PrivateBytesReads == 1 &&
               access.CpuTimeReads == 1,
            "A failed property read must not be retried in the same capture.");
    }

    private static void TestProcessIdFailureStopsCapture()
    {
        using var process = new Process();
        var access = new CountingProcessPropertyAccess { ThrowOnProcessId = true };
        var reader = new ProcessPropertyReader(access);

        ProcessPropertyObservation observation = reader.Capture(process);

        Assert(observation.ProcessId is null, "A failed PID read must not create an identity.");
        Assert(observation.StartedAtUtc is null && observation.Name is null &&
               observation.WorkingSetBytes is null && observation.PrivateBytes is null &&
               observation.TotalCpuTime is null,
            "No dependent property should be read without a PID.");
        Assert(access.ProcessIdReads == 1, "The PID accessor must be called once.");
        Assert(access.StartedAtReads == 0 && access.NameReads == 0 &&
               access.WorkingSetReads == 0 && access.PrivateBytesReads == 0 &&
               access.CpuTimeReads == 0,
            "Other accessors must not run after the PID read fails.");
    }

    private static void TestProcessorSharedSnapshotConsumers()
    {
        DateTimeOffset firstAt = DateTimeOffset.UtcNow;
        DateTimeOffset secondAt = firstAt.AddSeconds(30);
        DateTimeOffset parentStarted = firstAt.AddMinutes(-10);
        DateTimeOffset childStarted = firstAt.AddMinutes(-5);
        var processor = new ProcessInventorySampleProcessor(logicalProcessorCount: 1);

        ProcessInventorySnapshot firstInventory = ProcessorSnapshot(
            firstAt,
            ProcessorEntry(100, parentStarted, firstAt, null, TimeSpan.FromSeconds(1), 4096, 8192, "Parent"),
            ProcessorEntry(200, childStarted, firstAt, 100, TimeSpan.FromSeconds(2), 2048, 4096, "Child"));
        ProcessInventoryProcessingResult first = processor.Process(firstInventory);

        Assert(ReferenceEquals(first.Inventory, firstInventory),
            "The processing result must retain the exact source inventory instance.");
        Assert(first.Rows.Count == 2 && first.PerformanceSamples.Count == 2,
            "Both process rows and diagnostic samples must derive from the same two entries.");
        Assert(first.Relationships.Count == 0,
            "A single sample must not establish a repeated parent-child relationship.");

        ProcessInventorySnapshot secondInventory = ProcessorSnapshot(
            secondAt,
            ProcessorEntry(100, parentStarted, secondAt, null, TimeSpan.FromSeconds(2), 4096, 8192, "Parent"),
            ProcessorEntry(200, childStarted, secondAt, 100, TimeSpan.FromSeconds(3), 2048, 4096, "Child"));
        ProcessInventoryProcessingResult second = processor.Process(secondInventory);

        Assert(ReferenceEquals(second.Inventory, secondInventory),
            "All consumers must share the exact immutable inventory passed to the processor.");
        Assert(second.RelationshipsAdvanced && second.Relationships.Count == 1,
            "The lifecycle tracker must consume the same complete inventory.");
        Assert(second.Relationships[0].LastObservedAt == secondInventory.CapturedAt,
            "Relationship timestamps must match the source inventory timestamp.");

        ProcessInventoryRow childRow = second.Rows.Single(row => row.ProcessId == 200);
        ProcessPerformanceSample childSample =
            second.PerformanceSamples.Single(sample => sample.ProcessId == 200);

        Assert(childRow.Name == "Child" && childSample.Name == childRow.Name,
            "The process list and diagnostics must use the same captured process name.");
        Assert(childRow.StartTimeUtc == childStarted.UtcDateTime &&
               childSample.StartedAtUtc == childRow.StartTimeUtc,
            "Both projections must use the same captured process identity.");
        Assert(childRow.WorkingSetBytes == 2048 &&
               childSample.WorkingSetBytes == childRow.WorkingSetBytes,
            "Both projections must use the same captured memory value.");
        Assert(childRow.CpuPercent.HasValue &&
               Math.Abs(childRow.CpuPercent.Value - 3.333333333d) < 0.001 &&
               childSample.CpuPercent == childRow.CpuPercent,
            "CPU history and diagnostic samples must use one shared calculated CPU result.");
        Assert(second.Inventory.CapturedAt == secondInventory.CapturedAt,
            "All derived views must retain the exact source sample timestamp.");
    }

    private static void TestProcessorClearsRelationshipsOnIncompleteInventory()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var processor = new ProcessInventorySampleProcessor(logicalProcessorCount: 1);
        DateTimeOffset parentStarted = start.AddMinutes(-10);
        DateTimeOffset childStarted = start.AddMinutes(-5);

        _ = processor.Process(ProcessorSnapshot(
            start,
            ProcessorEntry(100, parentStarted, start, null, TimeSpan.FromSeconds(1), 4096, 8192, "Parent"),
            ProcessorEntry(200, childStarted, start, 100, TimeSpan.FromSeconds(2), 2048, 4096, "Child")));

        DateTimeOffset establishedAt = start.AddSeconds(30);
        ProcessInventoryProcessingResult established = processor.Process(ProcessorSnapshot(
            establishedAt,
            ProcessorEntry(100, parentStarted, establishedAt, null, TimeSpan.FromSeconds(2), 4096, 8192, "Parent"),
            ProcessorEntry(200, childStarted, establishedAt, 100, TimeSpan.FromSeconds(3), 2048, 4096, "Child")));
        Assert(established.Relationships.Count == 1,
            "The repeated complete inventory should establish one relationship.");

        DateTimeOffset incompleteAt = start.AddSeconds(60);
        ProcessInventorySnapshot incomplete = new(
            incompleteAt,
            ProcessEnumerationStatus.Failed,
            ParentMetadataStatus.Partial,
            [ProcessorEntry(100, parentStarted, incompleteAt, null, TimeSpan.FromSeconds(3), 4096, 8192, "Parent")],
            "Injected partial inventory");
        ProcessInventoryProcessingResult paused = processor.Process(incomplete);

        Assert(!paused.RelationshipsAdvanced && paused.Relationships.Count == 0,
            "An incomplete inventory must clear existing relationship confidence.");

        DateTimeOffset recoveredAt = start.AddSeconds(90);
        ProcessInventoryProcessingResult recovered = processor.Process(ProcessorSnapshot(
            recoveredAt,
            ProcessorEntry(100, parentStarted, recoveredAt, null, TimeSpan.FromSeconds(4), 4096, 8192, "Parent"),
            ProcessorEntry(200, childStarted, recoveredAt, 100, TimeSpan.FromSeconds(5), 2048, 4096, "Child")));

        Assert(recovered.Relationships.Count == 0,
            "The first complete sample after a gap must not restore stale relationship confidence.");
    }

    private static void TestProcessorResetsMissingCpuBaseline()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        DateTimeOffset processStarted = start.AddMinutes(-2);
        var processor = new ProcessInventorySampleProcessor(logicalProcessorCount: 1);

        _ = processor.Process(ProcessorSnapshot(
            start,
            ProcessorEntry(300, processStarted, start, null, TimeSpan.FromSeconds(1), 1000, 2000, "Worker")));

        DateTimeOffset missingAt = start.AddSeconds(30);
        ProcessInventoryProcessingResult missing = processor.Process(ProcessorSnapshot(
            missingAt,
            ProcessorEntry(300, processStarted, missingAt, null, null, 1000, 2000, "Worker")));
        Assert(missing.PerformanceSamples.Single().CpuPercent is null,
            "A missing cumulative CPU value must remain unavailable.");

        DateTimeOffset restoredAt = start.AddSeconds(60);
        ProcessInventoryProcessingResult restored = processor.Process(ProcessorSnapshot(
            restoredAt,
            ProcessorEntry(300, processStarted, restoredAt, null, TimeSpan.FromSeconds(10), 1000, 2000, "Worker")));
        Assert(restored.PerformanceSamples.Single().CpuPercent is null,
            "The first CPU value after a gap must establish a new baseline rather than bridge the gap.");

        DateTimeOffset nextAt = start.AddSeconds(90);
        ProcessInventoryProcessingResult next = processor.Process(ProcessorSnapshot(
            nextAt,
            ProcessorEntry(300, processStarted, nextAt, null, TimeSpan.FromSeconds(11), 1000, 2000, "Worker")));
        Assert(next.PerformanceSamples.Single().CpuPercent.HasValue,
            "A second consecutive CPU value after recovery should produce a valid delta.");
    }

    private static void TestProcessorPidReuseResetsCpuHistory()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var processor = new ProcessInventorySampleProcessor(logicalProcessorCount: 1);
        DateTimeOffset originalStarted = start.AddMinutes(-2);

        _ = processor.Process(ProcessorSnapshot(
            start,
            ProcessorEntry(400, originalStarted, start, null, TimeSpan.FromSeconds(20), 1000, 2000, "Worker")));

        DateTimeOffset reusedAt = start.AddSeconds(30);
        DateTimeOffset replacementStarted = reusedAt.AddSeconds(-1);
        ProcessInventoryProcessingResult replacement = processor.Process(ProcessorSnapshot(
            reusedAt,
            ProcessorEntry(400, replacementStarted, reusedAt, null, TimeSpan.FromSeconds(1), 1000, 2000, "Worker")));

        Assert(replacement.PerformanceSamples.Single().CpuPercent is null,
            "A new process identity reusing a PID must not inherit the old process CPU baseline.");

        DateTimeOffset nextAt = start.AddSeconds(60);
        ProcessInventoryProcessingResult next = processor.Process(ProcessorSnapshot(
            nextAt,
            ProcessorEntry(400, replacementStarted, nextAt, null, TimeSpan.FromSeconds(2), 1000, 2000, "Worker")));

        Assert(next.PerformanceSamples.Single().CpuPercent.HasValue,
            "The replacement process should establish its own CPU history after a second sample.");
    }

    private static ProcessInventorySnapshot ProcessorSnapshot(
        DateTimeOffset capturedAt,
        params ProcessInventoryEntry[] entries) =>
        new(
            capturedAt,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Complete,
            entries);

    private static ProcessInventoryEntry ProcessorEntry(
        int processId,
        DateTimeOffset startedAt,
        DateTimeOffset capturedAt,
        int? parentProcessId,
        TimeSpan? totalCpuTime,
        ulong workingSet,
        ulong privateBytes,
        string name) =>
        new(
            ProcessId: processId,
            Identity: new ProcessIdentity(processId, startedAt),
            Name: name,
            ParentProcessId: parentProcessId,
            ParentMetadataAvailable: true,
            WorkingSetBytes: workingSet,
            PrivateBytes: privateBytes,
            TotalCpuTime: totalCpuTime,
            CapturedAt: capturedAt);

    private static void TestProcessorOutOfOrderSampleDoesNotChangeCpuBaseline()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        DateTimeOffset processStarted = start.AddMinutes(-2);
        var processor = new ProcessInventorySampleProcessor(logicalProcessorCount: 1);

        _ = processor.Process(ProcessorSnapshot(
            start,
            ProcessorEntry(500, processStarted, start, null, TimeSpan.FromSeconds(1), 1000, 2000, "Worker")));

        DateTimeOffset newestAt = start.AddSeconds(30);
        ProcessInventoryProcessingResult newest = processor.Process(ProcessorSnapshot(
            newestAt,
            ProcessorEntry(500, processStarted, newestAt, null, TimeSpan.FromSeconds(2), 1000, 2000, "Worker")));
        Assert(newest.PerformanceSamples.Single().CpuPercent.HasValue,
            "The second chronological sample should create a CPU percentage.");

        DateTimeOffset staleAt = start.AddSeconds(15);
        ProcessInventoryProcessingResult stale = processor.Process(ProcessorSnapshot(
            staleAt,
            ProcessorEntry(500, processStarted, staleAt, null, TimeSpan.FromSeconds(100), 1000, 2000, "Worker")));
        Assert(!stale.RelationshipsAdvanced && stale.Relationships.Count == 0,
            "An out-of-order inventory must not expose relationship state as current for the stale sample.");
        Assert(stale.Rows.Count == 0 && stale.PerformanceSamples.Count == 0,
            "An out-of-order inventory must not replace the process-list rows or diagnostic samples.");
        Assert(stale.Relationships.Count == 0,
            "An out-of-order inventory must not expose current relationship evidence as belonging to stale data.");

        DateTimeOffset nextAt = start.AddSeconds(60);
        ProcessInventoryProcessingResult next = processor.Process(ProcessorSnapshot(
            nextAt,
            ProcessorEntry(500, processStarted, nextAt, null, TimeSpan.FromSeconds(3), 1000, 2000, "Worker")));
        Assert(next.PerformanceSamples.Single().CpuPercent.HasValue &&
               Math.Abs(next.PerformanceSamples.Single().CpuPercent!.Value - 3.333333333d) < 0.001,
            "The next chronological sample must use the newest valid CPU baseline, not the stale sample.");
    }

    private static void TestEntryParentMetadataUnavailable()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var entry = InventoryEntry(at) with { ParentMetadataAvailable = false };
        var snapshot = CompleteSnapshot(at, entry);

        Assert(!snapshot.CanAdvanceRelationships,
            "A snapshot cannot advance relationships when any entry lacks trustworthy parent metadata.");
    }

    private static ProcessInventorySnapshot CompleteSnapshot(
        DateTimeOffset at,
        ProcessInventoryEntry entry) =>
        new(
            at,
            ProcessEnumerationStatus.Complete,
            ParentMetadataStatus.Complete,
            [entry]);

    private static ProcessInventoryEntry InventoryEntry(DateTimeOffset at) =>
        new(
            ProcessId: 100,
            Identity: new ProcessIdentity(100, at.AddMinutes(-1)),
            Name: "Example",
            ParentProcessId: null,
            ParentMetadataAvailable: true,
            WorkingSetBytes: 1024,
            PrivateBytes: 2048,
            TotalCpuTime: TimeSpan.FromSeconds(1),
            CapturedAt: at);

    private static ProcessLifecycleObservation Parent(DateTimeOffset startedAt) =>
        new(new ProcessIdentity(100, startedAt), "ParentApp", null, startedAt);

    private static ProcessLifecycleObservation Child(DateTimeOffset startedAt) =>
        new(new ProcessIdentity(200, startedAt), "HelperApp", 100, startedAt);

    private static ProcessLifecycleObservation[] Snapshot(
        DateTimeOffset capturedAt,
        params ProcessLifecycleObservation[] items) =>
        items.Select(item => item with { CapturedAt = capturedAt }).ToArray();

    private sealed class CountingProcessPropertyAccess : IProcessPropertyAccess
    {
        public int ProcessId { get; init; } = 321;
        public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow.AddMinutes(-5);
        public string Name { get; init; } = "Example";
        public long WorkingSetBytes { get; init; } = 4096;
        public long PrivateBytes { get; init; } = 8192;
        public TimeSpan TotalCpuTime { get; init; } = TimeSpan.FromSeconds(12);

        public bool ThrowOnProcessId { get; init; }
        public bool ThrowOnStartTime { get; init; }
        public bool ThrowOnName { get; init; }
        public bool ThrowOnWorkingSet { get; init; }
        public bool ThrowOnPrivateBytes { get; init; }
        public bool ThrowOnCpuTime { get; init; }

        public int ProcessIdReads { get; private set; }
        public int StartedAtReads { get; private set; }
        public int NameReads { get; private set; }
        public int WorkingSetReads { get; private set; }
        public int PrivateBytesReads { get; private set; }
        public int CpuTimeReads { get; private set; }

        public int ReadProcessId(Process process)
        {
            ProcessIdReads++;
            if (ThrowOnProcessId) throw new InvalidOperationException("PID is unavailable.");
            return ProcessId;
        }

        public DateTimeOffset ReadStartedAtUtc(Process process)
        {
            StartedAtReads++;
            if (ThrowOnStartTime) throw new InvalidOperationException("Start time is unavailable.");
            return StartedAtUtc;
        }

        public string ReadProcessName(Process process)
        {
            NameReads++;
            if (ThrowOnName) throw new InvalidOperationException("Name is unavailable.");
            return Name;
        }

        public long ReadWorkingSetBytes(Process process)
        {
            WorkingSetReads++;
            if (ThrowOnWorkingSet) throw new InvalidOperationException("Working set is unavailable.");
            return WorkingSetBytes;
        }

        public long ReadPrivateBytes(Process process)
        {
            PrivateBytesReads++;
            if (ThrowOnPrivateBytes) throw new InvalidOperationException("Private bytes are unavailable.");
            return PrivateBytes;
        }

        public TimeSpan ReadTotalCpuTime(Process process)
        {
            CpuTimeReads++;
            if (ThrowOnCpuTime) throw new InvalidOperationException("CPU time is unavailable.");
            return TotalCpuTime;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
