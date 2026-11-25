# Shared Process Inventory Integration

Status: shared-snapshot integration implemented; Windows CI passed for commit `9537c15bb24af34461a35ddca2d30b1bb14d9001`; provider-specific deterministic tests and runtime soak test pending  
Related: [Issue #7](https://github.com/IS-IVR/ram-sentinel/issues/7), [Issue #9](https://github.com/IS-IVR/ram-sentinel/issues/9), [PR #6](https://github.com/IS-IVR/ram-sentinel/pull/6), [PR #8](https://github.com/IS-IVR/ram-sentinel/pull/8)

## Objective

Use exactly one `Process.GetProcesses()` enumeration per diagnostic process sample. The process table, CPU history, and lifecycle tracker must consume one immutable set of observations with one UTC capture timestamp. Collection remains read-only.

## Implemented in this revision

- `OwnedSnapshotCapture` owns the array returned by the single enumerator call and disposes each returned item exactly once, including when capture fails.
- `WindowsProcessInventoryProvider` consumes caller-owned `Process` instances and never enumerates or disposes them.
- Each process property used by the sample (PID, creation time, name, working set, private bytes, cumulative CPU time) is captured once. UI rows and CPU history use those captured values rather than reading the live process again.
- Missing creation time remains a null identity. Missing process metrics remain null and are displayed as unavailable, never as zero.
- CPU history is keyed by `ProcessIdentity` (PID + creation time). A missing cumulative CPU observation resets that identity's delta baseline.
- Toolhelp metadata distinguishes complete, partial, and unavailable snapshots. `Process32First` failures are checked, and `Process32Next` must end with `ERROR_NO_MORE_FILES` to be considered complete.
- A successful empty Toolhelp snapshot is distinct from snapshot creation or enumeration failure.
- A PID absent from an otherwise complete Toolhelp snapshot makes parent metadata partial; it is not assigned a guessed parent.
- The Core inventory gate rejects failed enumeration, incomplete parent metadata, missing or mismatched identities, mismatched timestamps, unavailable per-entry parent metadata, and duplicate PIDs.
- Incomplete inventories clear lifecycle relationship confidence. No stale relationship is retained as current evidence.
- The UI-only refresh path does not enumerate processes. Process enumeration stays inside the existing 30-second sample refresh.
- No new timer, process-control capability, or lifecycle recommendation is introduced.

## Deterministic validation added

- Duplicate PIDs block relationship tracking.
- The owned-snapshot helper calls the enumerator exactly once.
- Every item is disposed exactly once after a successful capture.
- Every item is disposed exactly once when the capture callback throws, and the original capture exception is rethrown.
- Existing Core checks cover failed enumeration, partial parent metadata, missing identity, empty successful inventory, PID mismatch, timestamp mismatch, unavailable parent metadata, and duplicate PIDs.
- Snapshot gating also rejects non-empty failure reasons and invalid non-null parent PIDs.
- Windows CI run #56 passed for commit `9537c15bb24af34461a35ddca2d30b1bb14d9001`: restore, build, all Core checks, standalone x64 publish, and artifact upload.
- The current Core tests do not yet directly execute the Windows P/Invoke provider; provider-specific injection seams and deterministic Toolhelp failure tests remain outstanding.

## Remaining validation

- [x] Windows CI passes for commit `9537c15bb24af34461a35ddca2d30b1bb14d9001` (run #56).
- [ ] Add injectable Toolhelp and process-property capture seams; test snapshot creation failure, Process32First failure, mid-enumeration failure, and property-read failures deterministically.
- [ ] Add a Windows integration test proving all UI/CPU/lifecycle consumers reuse the same immutable entry and timestamp.
- [ ] Review the final PR diff after provider-specific tests are added.
- [ ] Run on a real Windows machine with low available RAM and a large process count; record collector CPU, memory, and elapsed-time overhead.
- [ ] Confirm strict completeness does not starve relationship tracking on the target Windows versions.
- [ ] Keep all lifecycle recommendations and automated process actions disabled until provider tests and runtime soak validation are complete.

## Safety and privacy

- No command lines, executable paths, window captions, document names, browser content, or typed input are collected.
- The provider does not terminate, suspend, close, or modify processes.
- No services, registry values, Defender, Windows Update, paging settings, caches, or user files are changed.
- Parent-child evidence is not proof that a helper is disposable and never authorizes process closure.
- The existing normal-close flow remains a separate, explicit user-confirmed action with identity revalidation immediately before it is requested.

## Acceptance

Do not merge PR #8 until deterministic checks pass, the same immutable observation is used by all consumers, inventory completeness is explicit, Windows CI passes, and low-resource Windows runtime validation is recorded.
