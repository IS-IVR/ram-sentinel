# Shared Process Inventory Integration

Status: shared inventory, Toolhelp state machine, process-property reader, and Windows provider smoke test implemented. Windows CI run #64 passed for commit `92216f33451943660df8a37e1b256e1606c99750`. Low-resource runtime soak and full UI-consumer orchestration validation remain open.

Related: [Issue #7](https://github.com/IS-IVR/ram-sentinel/issues/7), [Issue #9](https://github.com/IS-IVR/ram-sentinel/issues/9), [PR #6](https://github.com/IS-IVR/ram-sentinel/pull/6), [PR #8](https://github.com/IS-IVR/ram-sentinel/pull/8).

## Objective

Use exactly one `Process.GetProcesses()` enumeration per diagnostic process sample. The process table, CPU history, and lifecycle tracker must consume one immutable set of observations with one UTC capture timestamp. Collection remains read-only.

## Implemented

- `OwnedSnapshotCapture` owns the array returned by the single enumerator call and disposes every returned item exactly once, including when capture fails.
- `WindowsProcessInventoryProvider` consumes caller-owned `Process` instances and does not enumerate or dispose them.
- `ProcessPropertyReader` reads PID, creation time, name, working set, private bytes, and cumulative CPU through injectable accessors. Each is read at most once. A failed PID read prevents dependent reads.
- Unavailable process metrics remain null. They are never represented as measured zero.
- The UI rows, CPU history, and lifecycle tracker reuse the captured immutable entries. CPU history is keyed by `ProcessIdentity` (PID + UTC creation time); a missing cumulative CPU observation resets its baseline.
- Toolhelp metadata distinguishes complete, partial, and unavailable snapshots through the Core `ProcessParentSnapshotCapture` state machine. The Windows adapter supplies native callbacks.
- `Process32First` unexpected failures, `Process32Next` unexpected terminal errors, duplicate PID entries, and close failures are never reported as complete snapshots.
- A valid empty Toolhelp snapshot is distinct from snapshot creation or enumeration failure.
- A PID missing from an otherwise complete Toolhelp snapshot makes parent metadata partial; no parent is guessed.
- The inventory gate rejects failed enumeration, incomplete parent metadata, missing or mismatched identities, future creation times, mismatched timestamps, unavailable per-entry parent metadata, invalid parent PIDs, failure reasons, and duplicate PIDs.
- Relationship tracking rejects impossible parent/child chronology: a parent cannot have been created later than its child.
- Incomplete inventories clear relationship confidence instead of retaining stale evidence.
- Process enumeration remains inside the existing 30-second diagnostic sample. The UI-only refresh does not perform another process enumeration.
- No new timer, process-control capability, or lifecycle recommendation has been introduced.

## Automated validation

- [x] Ownership helper enumerates exactly once and disposes every item exactly once on success and capture failure.
- [x] Core gate covers failed enumeration, partial parent metadata, missing identity, valid empty inventory, PID mismatch, timestamp mismatch, missing per-entry parent metadata, failure reason, invalid parent PID, future creation time, and duplicate PID.
- [x] Toolhelp callback state machine covers creation failure, unexpected first-entry failure, valid empty snapshot, mid-enumeration failure, normal end-of-list, close failure, and duplicate PID.
- [x] Process-property reader tests verify one read per property, null preservation after access failures, and early stop after PID-read failure.
- [x] Relationship tracker tests reject impossible parent/child chronology.
- [x] Windows provider smoke test runs on a real Windows GitHub Actions runner using the current process. It exercises the concrete `System.Diagnostics.Process` adapter and actual Toolhelp P/Invoke path, and verifies process identity, readable properties, parent metadata, timestamp consistency, and the inventory completeness gate.
- [x] Windows CI run #64 passed for commit `92216f33451943660df8a37e1b256e1606c99750`: Restore, Build, Core logic checks, Windows provider integration check, standalone x64 publish, and artifact upload.

## Remaining validation

- [ ] Add an orchestration test for the complete MainForm/UI, CPU-history, and lifecycle path proving that every consumer reuses the same immutable sample and timestamp.
- [ ] Run a long-duration soak test on a real low-memory Windows machine with a large process count. Record collector CPU, working-set/memory overhead, and sample elapsed time.
- [ ] Check whether strict Toolhelp completeness causes relationship tracking to pause too often on supported Windows versions and under process churn.
- [ ] Review the final stacked PR diff and retarget only after the base provider PR is ready.
- [ ] Keep lifecycle recommendations and automated process actions disabled until the runtime soak and orchestration validation are complete.

## Safety and privacy

- No command lines, executable paths, window captions, document names, browser content, or typed input are collected.
- The provider does not terminate, suspend, close, or modify processes.
- No services, registry values, Defender, Windows Update, paging settings, caches, or user files are changed.
- Parent-child evidence is not proof that a helper is disposable and never authorizes process closure.
- The existing normal-close flow remains a separate, explicit user-confirmed action with identity revalidation immediately before the request.

## Acceptance

Do not merge PR #8 until the full consumer-orchestration test, low-resource Windows runtime soak, and strict-completeness review are complete. The passing CI smoke test validates the collector path; it does not substitute for long-duration validation on a weak target machine.
