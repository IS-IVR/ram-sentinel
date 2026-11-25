# Shared Process Inventory Integration

Status: implementation in progress; correctness fixes, deterministic tests, and Windows validation pending  
Related: [Issue #7](https://github.com/IS-IVR/ram-sentinel/issues/7), [Issue #9](https://github.com/IS-IVR/ram-sentinel/issues/9), [PR #6](https://github.com/IS-IVR/ram-sentinel/pull/6), [PR #8](https://github.com/IS-IVR/ram-sentinel/pull/8)

## Objective

Use one process enumeration per diagnostic sample for the process table, CPU history, and lifecycle relationship tracking. The sample must have one immutable identity observation per process instance. This work is observational only and must not introduce automatic process actions or user-facing lifecycle recommendations.

## Current implementation checkpoint

Implemented on `feature/shared-process-inventory`:

- `MainForm` obtains the `Process[]` once and passes the same caller-owned instances and UTC timestamp to the lifecycle provider.
- The provider no longer calls `Process.GetProcesses()` and does not dispose caller-owned instances.
- `MainForm` disposes the snapshot instances in a `finally` block.
- CPU history is keyed by `ProcessIdentity` (PID + start time), rather than PID alone.
- Relationship tracking is currently gated by an all-processes-have-observations count check.
- The existing diagnostic cadence and explicit user-confirmed normal-close flow remain unchanged.
- CI workflow trigger was widened in commit `ee7a0223810162e92da6d23babab956be0ed8859` so stacked pull requests targeting feature branches are validated. Run 45 was cancelled when a newer commit superseded it; run 46 for head `7771d68ed53f0eed5fa61e18b6da93bad1f5e37c` completed successfully. This confirms the current compile/Core test workflow, not the unimplemented deterministic Windows-provider failure-path tests or runtime behavior.

This is not yet a correct shared-observation implementation: `MainForm` currently reads `Process.StartTime` and `TotalProcessorTime` again after the provider has already read them. It also reads process name and memory values independently. The duplicated identity read creates a race and violates the single immutable observation rule. The UI still uses the old count-based completeness check. A new Core-level `ProcessInventorySnapshot` contract now defines explicit enumeration status, parent-metadata status, per-entry identity/metadata availability, and the single `CanAdvanceRelationships` gate. It is covered by deterministic Core checks but is not yet wired into the Windows provider or UI.

## Required architecture

### A. One immutable observation per process

Introduce a per-sample result object with:

- `CapturedAtUtc`: the one timestamp assigned to the sample.
- `ProcessIdentity?`: PID + UTC creation time, null when creation time could not be verified.
- `Name`: best-effort process name, explicitly unavailable when unreadable.
- `WorkingSetBytes?` and `PrivateBytes?`: nullable measurements.
- `TotalCpuTime?`: nullable cumulative CPU time.
- `ParentProcessId?`: nullable parent PID.
- Per-field availability/error information sufficient to distinguish access denied, process exit, and unsupported metrics from a measured zero.
- Inventory-level status for enumeration success and parent-metadata snapshot success/completeness.

The provider must capture each process property at most once for the sample. The UI, CPU history, and lifecycle tracker must consume the same immutable observation objects. They must not re-read `StartTime` or `TotalProcessorTime` to reconstruct identity or CPU history.

Do not use zero, a default timestamp, or a fabricated process name to represent unavailable data. A process without verified identity may still be shown in the process list, but it must not enter identity-based CPU history, lifecycle tracking, or the normal-close action path.

### B. Explicit inventory completeness

Replace the current `observations.Count == processes.Length` inference with explicit result state.

- Distinguish successful process enumeration from a failed enumeration.
- Distinguish a successful Toolhelp snapshot with no parent metadata from snapshot creation or enumeration failure.
- Distinguish complete parent metadata from partial metadata.
- Keep per-process metric availability separate from whether the process inventory itself succeeded.
- A process that exits during sampling is an expected per-process failure, not a zero-valued observation.
- A partial or ambiguous inventory must not advance relationship confidence unless a separately documented subset policy proves both endpoints and the relevant parent metadata trustworthy.
- Until such a subset policy is implemented and tested, skip relationship tracker advancement for an incomplete relationship snapshot and expose a diagnostic reason.

Toolhelp enumeration must inspect the terminal `Process32Next` error. `ERROR_NO_MORE_FILES` is normal completion; any other terminal error makes the parent snapshot incomplete. A failed `Process32First` must also be represented explicitly, not returned as an empty successful dictionary.

### C. Snapshot ownership and disposal

- The diagnostic sample owner calls the process enumerator exactly once.
- The owner passes the resulting process instances to the provider.
- The provider never disposes caller-owned process instances.
- The owner disposes each instance exactly once in a `finally` block, even if provider capture, row construction, CPU calculation, or UI rendering fails.
- UI-only refreshes reuse the most recent immutable sample and do not enumerate processes.
- No second timer or background polling loop is introduced.

## Completeness and confidence rules

- Identity is always PID + UTC process creation time. PID-only history is forbidden.
- A PID whose creation time changes is a new process instance; CPU and relationship history must reset.
- Duplicate PIDs in one sample are ambiguous and cannot establish relationships.
- Missing creation time cannot be repaired by guessing.
- Parent PID is evidence only. It is not proof that a child is disposable and never authorizes closing it.
- A relationship may be exposed only after repeated direct observations of both exact process instances, with trustworthy parent metadata.

## Required deterministic tests

1. Same immutable `ProcessIdentity` and `CapturedAtUtc` are consumed by lifecycle tracking, CPU history, and process rows.
2. Each process property used for identity and CPU history is read once per sample.
3. PID reuse between samples resets CPU deltas and cannot inherit a relationship.
4. Process exits or becomes inaccessible during capture without false zero values.
5. Missing creation time does not create a fabricated identity.
6. Toolhelp snapshot creation failure is distinguishable from a valid empty parent map.
7. Toolhelp mid-enumeration failure is distinguishable from normal end-of-list.
8. Partial inventory does not advance relationship confidence.
9. Every process object is disposed exactly once on success and on provider/row-building exceptions.
10. Exactly one process enumeration occurs per diagnostic sample.
11. UI-only refresh performs no new process enumeration.
12. A failed top-level enumeration does not mutate tracker or CPU history.
13. Windows CI build and all Core tests pass.

Tests should use injected interfaces/delegates for process enumeration, process-property capture, Toolhelp parent metadata, and disposal observation. Do not make the core test suite depend on live Windows processes.

## Privacy and safety

- Do not collect command lines, executable paths, window titles, document names, browser contents, or user input.
- Do not close, suspend, terminate, or otherwise control processes automatically.
- Do not change services, registry values, Defender, Windows Update, paging configuration, or caches.
- Do not show lifecycle recommendations as part of this integration.
- Preserve the existing explicit, user-confirmed normal-close behavior; this work must not broaden it.

## Implementation sequence

- [x] Refactor provider to consume caller-owned process instances and a required timestamp.
- [x] Introduce one app-level process snapshot owner for the diagnostic sample.
- [x] Enable CI workflow triggers for stacked pull requests targeting feature branches.
- [x] Add the Core inventory result contract with explicit enumeration/parent-metadata completeness and a single relationship-advancement gate.
- [x] Add deterministic Core checks for failed enumeration, partial parent metadata, missing identity, and a valid empty inventory.
- [ ] Wire the Windows provider to produce the new immutable inventory result and preserve unreadable processes as rows with unavailable metrics.
- [ ] Make the provider's immutable per-process observations the sole source for identity and CPU history.
- [ ] Make process rows consume the same observations without a second identity/CPU read.
- [ ] Return explicit process-enumeration and Toolhelp metadata completeness states, including terminal Win32 error handling.
- [ ] Add injectable enumeration/capture/disposal seams.
- [ ] Add deterministic tests for identity, partial snapshots, Toolhelp failures, and disposal.
- [ ] Verify UI-only refresh does not enumerate processes.
- [x] Confirm the updated stacked-PR workflow runs: run 46 completed successfully for the current PR #8 head at this checkpoint.
- [ ] Run CI after every subsequent implementation commit; a green build does not replace the deterministic provider tests or Windows runtime validation.
- [ ] Perform a low-resource Windows runtime soak test before enabling any lifecycle interpretation.

## Acceptance

Do not merge PR #8 until all required deterministic tests pass, the same immutable observation is used by all consumers, inventory completeness is explicit, Windows CI passes, and the diff is reviewed. No lifecycle recommendations or process-control capabilities may be enabled by this integration.
