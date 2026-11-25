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
        ("Owned snapshot disposes every item after capture failure", TestOwnedSnapshotDisposalAfterFailure)
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
