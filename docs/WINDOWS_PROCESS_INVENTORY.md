# Windows Process Inventory Provider

## Purpose

`WindowsProcessInventoryProvider` captures a best-effort, read-only snapshot for the Smart Application Lifecycle Manager. It is an input collector only: it does not classify applications, notify the user, or perform process actions.

## Collection

Each call performs one process inventory pass:

1. Takes one Toolhelp process snapshot to read PID and Parent PID pairs.
2. Enumerates processes through `Process.GetProcesses()`.
3. Reads process creation time, name, working set, private bytes, and cumulative CPU time where Windows permissions allow.
4. Reads cumulative process I/O operation and byte counters through `GetProcessIoCounters` when a limited query handle can be opened.
5. Returns observations ordered by PID.

The provider does not read executable paths, command-line arguments, window captions, document names, browser content, or typed input. It does not enumerate files or query WMI.

## Data quality rules

- PID plus UTC process creation time forms the process-instance identity. A process whose creation time cannot be read is retained as a row when its PID is known, but its identity remains null and it cannot enter identity-based CPU history or lifecycle tracking.
- Process name, working set, private bytes, cumulative CPU time, and I/O counters are captured once per process. Each unavailable metric remains null.
- Parent PID is nullable. A successful Toolhelp entry with Parent PID 0 is recorded as a known null parent; a process absent from the Toolhelp snapshot is marked as having unavailable parent metadata.
- Toolhelp snapshot creation failure, an unexpected `Process32First` error, or a non-`ERROR_NO_MORE_FILES` terminal `Process32Next` error is not reported as a complete snapshot.
- A valid empty Toolhelp snapshot is represented as complete and empty, not as an error.
- Any missing per-process identity or parent metadata, duplicate PID, or timestamp mismatch blocks relationship tracking for the entire sample. The tracker is cleared rather than retaining stale relationships.
- I/O values are cumulative counters. A later layer must calculate deltas between observations of the same process identity before describing current I/O activity.
- Toolhelp parent relationships are a point-in-time observation and can race with process creation/exit. The Core relationship tracker therefore requires repeated observations of the same process identities.
- A parent-child relationship is not proof that the child is a disposable helper.

## Resource and safety boundaries

- One Toolhelp snapshot and one process enumeration per call; callers must reuse the existing diagnostic sampling cadence rather than create a second polling loop.
- Handles opened for I/O counters request only `PROCESS_QUERY_LIMITED_INFORMATION` and are closed immediately.
- No process is suspended, terminated, closed, or modified.
- No Windows settings, registry entries, services, or files are changed.
- The collector is intended to run at the existing 30-second diagnostic interval, not on every UI refresh.

## Windows integration validation

The Windows-only test project exercised the concrete provider against the current process on a Windows runner in CI run #68. It verifies a complete Toolhelp snapshot, the current PID and process identity, readable process name/memory/CPU properties, parent metadata presence, and exact sample timestamp reuse. This is a provider smoke test, not a long-duration performance benchmark or a test of the full MainForm orchestration.

## Remaining integration work

The provider is connected to the existing process-list refresh through one owned snapshot. `ProcessInventorySampleProcessor` derives process-list rows, diagnostic CPU samples, and lifecycle relationship evidence from the same inventory instance and timestamp. `MainForm` passes the timestamp used by the enclosing RAM/CPU/disk diagnostic sample. Duplicate or out-of-order snapshots do not update CPU baselines or relationship evidence. An incomplete inventory is displayed best-effort but cannot advance relationship confidence. Windows CI run #68 executes the concrete provider, `System.Diagnostics.Process` property adapter, and Toolhelp P/Invoke path against the current process, and the Core suite tests the sample orchestration. A desktop UI smoke test and a low-resource-machine soak test are still required before enabling lifecycle recommendations.
