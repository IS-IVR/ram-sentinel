# Shared Process Inventory Integration

Status: implementation in progress; Windows CI and review pending
Related: [Issue #7](https://github.com/IS-IVR/ram-sentinel/issues/7), [PR #6](https://github.com/IS-IVR/ram-sentinel/pull/6)

## Objective

Use one process enumeration per 30-second diagnostic sample for both the existing process table / CPU history and lifecycle relationship tracking. This change is observational only. It must not introduce automatic process actions or user-facing lifecycle recommendations.

## Current architecture and problem

- `MainForm.RefreshSnapshot()` decides when a diagnostic sample is due.
- `MainForm.RefreshProcessList()` currently calls `Process.GetProcesses()`, builds the process table, and updates CPU history keyed by PID.
- `WindowsProcessInventoryProvider.ReadAll()` independently calls `Process.GetProcesses()` and captures lifecycle observations.
- Calling both paths in one sample would enumerate the process list twice and increase overhead on low-resource machines.
- `ProcessRelationshipTracker` already keys relationships by `ProcessIdentity` (PID + UTC start time), rejects duplicate PIDs and out-of-order snapshots, and resets evidence after long gaps.

## Target design

1. The diagnostic sample owns a single UTC timestamp captured once at the beginning of the process inventory pass.
2. The app calls `Process.GetProcesses()` exactly once for that pass.
3. The resulting `Process[]` is passed to the lifecycle provider. The provider must not enumerate or dispose those instances.
4. The UI process-table builder consumes the same process instances and timestamp. It must not call `Process.GetProcesses()` itself.
5. The provider and UI share the same observed process identity where available. Identity is always PID + UTC process start time; PID-only history is forbidden.
6. Process objects are disposed exactly once by the owner after both consumers have finished.
7. A process whose start time cannot be read is excluded from identity-based CPU/lifecycle history. It must not be assigned a fabricated timestamp or zero-valued metrics.
8. A PID whose start time changes is a new process instance; its CPU delta and relationship history start fresh.
9. UI-only refreshes reuse the most recent sample and do not enumerate processes.
10. Sampling remains on the existing 30-second diagnostic cadence. No second timer or background polling loop is introduced.

## Completeness and confidence rules

- A failed top-level enumeration is a failed inventory; it must not be passed to the relationship tracker.
- Per-process access failures remain best-effort and must be distinguishable from measured zero values.
- If the implementation cannot establish a trustworthy, timestamp-consistent inventory, it must skip `Observe` for that sample rather than increase relationship confidence.
- Parent PID is evidence only. It is not proof that a child is disposable and never authorizes closing it.
- A relationship may be exposed only after repeated direct observations of both process instances.

## Privacy and safety

- Do not collect command lines, executable paths, window titles, document names, browser contents, or user input.
- Do not close, suspend, terminate, or otherwise control processes automatically.
- Do not change services, registry values, Defender, Windows Update, paging configuration, or caches.
- Do not show lifecycle recommendations as part of this integration.
- Preserve the existing explicit, user-confirmed normal-close behavior; this work must not broaden it.

## Required tests

1. One call to the process enumeration source per diagnostic sample.
2. UI refresh between samples does not enumerate processes again.
3. Provider and UI consume the same process snapshot and timestamp.
4. Process instances are disposed once, including exceptional paths.
5. PID reuse resets CPU deltas and cannot inherit a relationship.
6. Missing start time and access denied remain unavailable, not zero.
7. Duplicate PIDs and incomplete snapshots do not strengthen relationship confidence.
8. Repeated stable parent-child observations establish confidence; long gaps reset it.
9. Failed inventory does not advance tracker state.
10. Windows CI build and all Core checks pass.

## Implementation sequence

- [x] Refactor provider to consume caller-owned process instances and a required timestamp.
- [x] Introduce one app-level process snapshot owner for the diagnostic sample.
- [x] Build UI rows, CPU deltas, and lifecycle observations from that shared snapshot.
- [x] Change CPU history key from PID to `ProcessIdentity` and prune exited identities safely.
- [x] Gate tracker updates on inventory validity/completeness.
- [ ] Add focused tests for shared-snapshot semantics and failure paths.
- [ ] Run Windows CI and inspect the resulting PR diff before merge.

## Acceptance

The process table and lifecycle tracker receive data from one process enumeration per diagnostic sample, the existing 30-second cadence remains unchanged, and no new automated process action or recommendation is enabled.


## Implementation checkpoint

Implemented on `feature/shared-process-inventory`:
- `MainForm` now obtains the `Process[]` once and passes the same caller-owned instances and UTC timestamp to the lifecycle provider.
- The provider no longer calls `Process.GetProcesses()` and does not dispose caller-owned instances. It still takes a Toolhelp32 snapshot for parent-PID metadata.
- `MainForm` disposes the snapshot instances in a `finally` block.
- The process table and lifecycle tracker now use observations from the same sample.
- CPU history is keyed by `ProcessIdentity` (PID + start time), not PID alone.
- Tracker confidence is not advanced when the provider cannot produce an observation for every enumerated process.
- The existing diagnostic cadence and user-confirmed normal-close flow are unchanged.

Not yet verified: Windows build, automated checks, and runtime behavior on a low-resource PC. This branch is not ready to merge until CI and code review pass.
