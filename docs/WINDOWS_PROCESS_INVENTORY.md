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

- PID plus UTC process creation time forms the process-instance identity. A process whose creation time cannot be read is excluded rather than assigned a potentially unsafe PID-only identity.
- Parent PID is nullable. Missing or inaccessible parent information is not replaced with a guess.
- Memory, CPU, and I/O values are independently nullable. Access denial or a process exiting during collection does not become a zero measurement.
- I/O values are cumulative counters. A later layer must calculate deltas between observations of the same process identity before describing current I/O activity.
- Toolhelp parent relationships are a point-in-time observation and can race with process creation/exit. The Core relationship tracker therefore requires repeated observations of the same process identities.
- A parent-child relationship is not proof that the child is a disposable helper.

## Resource and safety boundaries

- One Toolhelp snapshot and one process enumeration per call; callers must reuse the existing diagnostic sampling cadence rather than create a second polling loop.
- Handles opened for I/O counters request only `PROCESS_QUERY_LIMITED_INFORMATION` and are closed immediately.
- No process is suspended, terminated, closed, or modified.
- No Windows settings, registry entries, services, or files are changed.
- The collector is intended to run at the existing 30-second diagnostic interval, not on every UI refresh.

## Remaining integration work

The provider is added but is not yet connected to `MainForm` or the lifecycle tracker. Integration must avoid a second full process enumeration: the existing process-list refresh should become the shared inventory pass, and the same captured timestamp must be used for every observation in that pass. Windows CI and a low-resource-machine soak test are required before enabling lifecycle recommendations.
