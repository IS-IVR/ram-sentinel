# Smart Application Lifecycle Manager

## Status and scope

This document specifies the next feature area for RAM Sentinel. It is a design and implementation plan, not a claim that the feature is already implemented.

The feature manages the lifecycle of user applications that retain significant memory or leave helper processes behind. It is not a RAM cleaner and must never equate high memory use, an idle CPU sample, or a background window with proof that a process is unnecessary.

The manager has two distinct workflows:

1. **Orphaned helper review after the main application exits.** Detect child/helper processes that remain after a monitored application closes. Preserve active work and act only when the helper is positively identified and has a safe, profile-specific shutdown mechanism.
2. **Inactive foreground-application suggestions.** Detect a user application that has not been in the foreground for a configurable period, is not showing signs of work, and is retaining a meaningful amount of memory while Windows is under sustained memory pressure. Ask the user before closing it.

The workflows share process identity, application profiles, activity classification, notification preferences, and audit history, but must not share unsafe assumptions.

## Product principles

- Protect user work before attempting to reclaim memory.
- Treat "not foreground" and "low CPU" as observations, not proof of inactivity.
- Use the existing diagnosis engine's sustained memory-pressure finding; do not create a second RAM percentage heuristic.
- Measure private bytes and working set separately. Do not sum working sets and present the sum as reclaimable physical RAM because shared pages can be counted in multiple processes.
- Never promise that the process's reported memory will become available RAM one-for-one.
- Separate detection, recommendation, user approval, shutdown request, and post-action verification.
- Never force-terminate a process. No `Process.Kill`, `TerminateProcess`, job-object termination, or generic timeout escalation.
- Unknown processes, system components, security tools, accessibility tools, backup/sync work, and processes with uncertain identity are observation-only.
- Keep collection local, low-frequency, and bounded. Do not capture window titles, typed input, document names, or command-line arguments for routine tracking.
- Store user preferences locally. Do not upload process inventories or application history.

## Workflow A: helper processes after the main application exits

### Session tracking

While a user application is running, RAM Sentinel maintains an in-memory application session containing:

- Stable identity for the main process: PID plus process creation time, executable path when accessible, and publisher/signature metadata when available.
- Observed process ancestry and descendants, each with PID plus creation time.
- First-seen and last-seen timestamps.
- Per-process CPU-time deltas, working set, private bytes, and process I/O-counter deltas where supported.
- Presence of a visible top-level window.
- An application profile identifier and its configured shutdown capabilities.

A parent PID alone is not sufficient to establish ownership. PIDs are reused, child processes may detach, and a helper may be shared by multiple applications. Relationship confidence must include creation-time validation and repeated observations while the parent is alive. When the relationship is ambiguous, mark it unknown and do not act.

The first implementation should track only relationships it observed directly. It should not infer historic ownership from a single process snapshot.

### Main-process exit

When the tracked main process exits:

1. Mark the application session as `MainExited`.
2. Wait a configurable settling interval (initial default: 15 seconds).
3. Re-enumerate the candidate helpers and revalidate each PID plus creation time.
4. Exclude helpers that have a visible user window, are shared/ambiguous, are protected, or are performing observed work.
5. Check profile-specific completion evidence. Examples may include an application-reported task-complete event or a documented helper status/IPC signal. A quiet CPU and disk sample by themselves are not completion evidence.
6. Evaluate whether a safe shutdown adapter exists for that exact profile and helper role.
7. If evidence or identity is insufficient, keep observing or show an informational record. Never escalate to forced termination.

### Shutdown policy

A helper may be closed automatically only when **all** of the following are true:

- The application profile is explicitly trusted and enabled by the user for automatic helper cleanup.
- The exact helper executable identity and role are allowlisted by that profile; a process name such as `ffmpeg.exe` is not sufficient by itself.
- RAM Sentinel observed its relationship to the main application while the session was active, and identity has been revalidated.
- The parent application has exited and the settling interval has elapsed.
- The helper has profile-specific evidence that its task is complete.
- There is a documented, graceful, profile-specific shutdown mechanism.
- No protection, active-work, or ambiguity rule applies.

If any condition is missing, automatic shutdown is forbidden. The UI may offer a user-confirmed action only when the application has a supported graceful shutdown adapter. Otherwise it may explain that a process remains, without offering an unsafe close action.

A generic background process with no main window cannot be assumed to support `CloseMainWindow`. Do not send Ctrl+C, keystrokes, arbitrary window messages, or stdin commands unless a specific application adapter documents and validates that protocol. A timeout after a graceful request means "still running"; it never authorizes a kill.

### FFmpeg and local AI examples

- FFmpeg can still be rendering, muxing, writing output, or waiting for input after the editor closes. A low CPU sample is not proof that the job is complete. FFmpeg must not be auto-closed based only on its filename, parent PID, or short idle duration. An adapter would need evidence from the launching application that its job completed and a supported way to request graceful exit.
- A local AI runtime may intentionally retain a loaded model. That is a cache/latency choice, not automatically a leak or orphan. The default profile must preserve it. The user may choose a policy to suggest shutdown when there is sustained memory pressure, but automatic unloading is allowed only if the runtime exposes a documented graceful unload/stop interface and the user enables it.

## Workflow B: inactive foreground applications

### Foreground and activity tracking

Track foreground changes with a Windows foreground-window event mechanism where available. Resolve the foreground window to a process identity and validate PID plus creation time. Do not poll keyboard or mouse input and do not store window captions.

For each tracked application, derive:

- Time since it was last the foreground application.
- CPU-time delta over successive samples.
- Process I/O read/write deltas where available.
- Visible-window and minimized state.
- Profile-specific activity signals such as an active render, model load, download, sync, or playback session.
- Current system memory-pressure state from the existing diagnosis engine.
- Process working set and private bytes, shown as distinct measurements.

"Idle candidate" is a confidence classification, not a guarantee. A program may be doing GPU work, waiting on a device, streaming audio, holding an important session, or saving data without consuming much CPU or disk.

### Initial suggestion policy

Recommended conservative defaults for the first implementation:

- The application has not been foreground for at least 5 minutes.
- At least three valid activity samples exist across a sustained observation window.
- No profile-specific active-work signal is present.
- CPU and I/O activity remain below configurable, workload-aware thresholds; unavailable metrics remain unknown, never zero.
- The existing diagnosis engine reports sustained memory pressure. High occupancy by itself is insufficient.
- The application is not excluded, protected, or currently subject to a user deferral.
- The application identity is stable and the user-facing application can receive a graceful close request.

The thresholds must be configurable and tuned with real Windows tests. Do not hard-code a single MB amount that is treated as meaningful on every 4 GB, 8 GB, or 32 GB machine.

When any signal is missing or contradictory, do not show a close suggestion. Prefer "not enough information" over a false positive.

### Notification behavior

The notification should be brief and non-modal. It should show:

- Application display name and verified executable identity.
- Time since last foreground use.
- Working set and private bytes as separate values.
- A plain-language explanation that Windows is under sustained memory pressure.
- A warning that closing may lose the application's in-memory session or require reloading a model/project.
- Actions: **Request normal close**, **Remind me later**, and **Ignore this application**.

"Remind me later" should defer the same application for a default 10 minutes. "Ignore" should suppress further suggestions for that application until the user changes the preference. Do not repeatedly prompt after dismissal. If the user returns to the application before acting, cancel the notification immediately.

The default behavior is suggestion-only. No app-level automatic close is allowed in this workflow.

## Application profile catalog

Profiles describe known application behavior and allowed lifecycle actions. They are not a list of executable names alone.

A profile may include:

- Stable application identifier and display name.
- Executable path/publisher constraints.
- Known main-process and helper roles.
- Known active-work signals.
- Whether helper relationships can be trusted.
- Whether the app intentionally retains caches/models.
- A documented graceful shutdown adapter and its validation rules.
- Default inactivity policy and whether automatic helper cleanup is eligible.

Unknown applications use a conservative generic profile: monitor only, suggest only when identity and activity evidence are sufficient, and never auto-close helpers.

Profile changes must be versioned and testable. Do not ship an automatic-cleanup allowlist until each profile has documented evidence, graceful shutdown behavior, and Windows integration tests.

## Proposed architecture

Keep lifecycle management separate from `DiagnosticsEngine`. The diagnosis engine remains responsible for detecting sustained system memory pressure; the lifecycle manager consumes that state and process observations.

Suggested components:

| Component | Responsibility |
|---|---|
| `ProcessInventoryProvider` | Low-overhead process identity and resource snapshots |
| `ProcessRelationshipTracker` | Track validated process ancestry while applications are running |
| `ForegroundApplicationTracker` | Track foreground-window changes without collecting user input or captions |
| `ApplicationProfileCatalog` | Resolve application identity, helper roles, activity signals, and shutdown capabilities |
| `ApplicationSessionTracker` | Maintain bounded in-memory lifecycle sessions |
| `ActivityClassifier` | Classify active, background, idle candidate, and unknown states |
| `LifecyclePolicyEngine` | Apply separate policies for orphaned helpers and inactive applications |
| `GracefulShutdownService` | Execute only a supported normal-close adapter and verify its result |
| `LifecycleNotificationService` | Present, defer, cancel, and suppress notifications |
| `LifecycleHistoryStore` | Store local decisions and outcomes with bounded retention |

Keep domain models and policy logic testable without Windows APIs. Windows-specific collectors and shutdown adapters belong in the Windows/platform layer. The WinForms UI should consume notifications and commands; it should not own process-detection logic.

No Windows Service or always-running elevated process is required for the first version.

## Data and privacy boundaries

- Process snapshots and lifecycle sessions are in-memory by default.
- Persist only user preferences, profile choices, notification deferrals/ignores, and a minimal local action history.
- Do not persist command-line arguments, window titles, document names, typed input, or full process inventories.
- Do not transmit process information or telemetry.
- Apply bounded retention and clear session state when RAM Sentinel exits.
- Do not request administrator privileges merely to inspect or manage ordinary user applications.
- Access-denied or unavailable process metrics must remain explicitly unknown.

## Resource budget

- Reuse the existing process sampling cadence where possible; do not introduce a second full process enumeration loop.
- In tray mode, use the existing low-frequency sampling schedule (currently 30 seconds) for resource metrics.
- Use foreground-change events rather than rapid foreground polling.
- Avoid continuous ETW traces, WMI polling loops, recursive disk scans, and per-process high-frequency PDH counters.
- Keep session collections bounded and remove exited process identities promptly.
- Measure RAM Sentinel's own CPU, private bytes, wakeups, and disk activity during idle and active monitoring.

## UX integration

Add a **ניהול תוכנות** page/section with three compact views:

1. **תוכנות פעילות**: application groups, current foreground/background state, process count, and memory measurements.
2. **תוכנות ברקע**: inactivity duration, activity confidence, memory data, and profile policy.
3. **תהליכי המשך**: validated helper processes whose parent application has exited, the relationship confidence, work/completion evidence, and whether a safe shutdown adapter exists.

Settings should include:

- Lifecycle monitoring on/off.
- Inactivity duration (default 5 minutes).
- Require sustained memory pressure (on by default and recommended).
- Per-application ignore / suggest-only / user-approved policy.
- Per-profile automatic helper cleanup only for validated adapters.
- Reminder interval (default 10 minutes).
- Local history retention.

Do not expose low-level process terminology as the primary explanation. Use wording such as "התוכנה לא הייתה בשימוש בחמש הדקות האחרונות" and "התהליך עדיין עובד, ולכן לא נסגור אותו".

## Graceful-close and verification sequence

For a user-facing application:

1. Revalidate process identity (PID, creation time, executable identity).
2. Re-check that the application has not returned to the foreground.
3. Re-check protected status and profile-specific activity signals.
4. Show the user the consequence and request confirmation.
5. Send a normal close request using the supported application mechanism.
6. Allow the application to save, cancel, or prompt the user.
7. Wait for a bounded verification interval. A timeout records that the application remains open; it never triggers force termination.
8. Re-measure system available memory and process memory after the process exits.
9. Report observed values and explicitly avoid claiming one-to-one RAM reclamation.

For a helper process, use the validated profile adapter and all automatic-cleanup gates above. If the adapter is absent or returns an ambiguous result, do not escalate.

## Failure handling

- Process exits or PID is reused between detection and action: cancel the action.
- Foreground application changes before user confirmation: cancel the suggestion.
- Process metrics are inaccessible: classify as unknown; do not infer zero activity.
- Relationship to parent is uncertain: do not treat the process as an orphaned helper.
- Helper continues working: keep it running and record why no action was taken.
- Graceful close is unsupported: do not offer a generic close workaround.
- Application refuses or ignores a normal close request: report that it remains open; never force it.
- RAM pressure has cleared: cancel an inactivity suggestion that was based on memory pressure.

## Implementation phases

### Phase 1 - read-only process and foreground inventory

- Reuse and extend existing process snapshots.
- Add stable process identity and observed parent/child relationship tracking.
- Add foreground-window event tracking.
- Track process CPU and I/O deltas without storing sensitive window or command-line data.
- Add deterministic unit tests for PID reuse, inaccessible metrics, parent exit, and process relationship ambiguity.
- No notifications and no process actions.

### Phase 2 - lifecycle classification and suggestions

- Add application sessions and generic unknown-app profiles.
- Add sustained idle-candidate classification using multiple valid samples.
- Consume the existing memory-pressure diagnostic state.
- Add local ignore/defer preferences.
- Add notification UI with automatic cancellation when the user returns to the app.
- Suggestion-only; no automatic action.

### Phase 3 - user-approved graceful close

- Extract the existing normal-close logic into a shared `GracefulShutdownService`.
- Revalidate identity and foreground state immediately before action.
- Handle save prompts and close timeouts without force termination.
- Verify process exit and post-action memory measurements.
- Add audit history and tests.

### Phase 4 - validated helper profiles

- Add profile-specific helper-role definitions and completion evidence.
- Add only documented graceful shutdown adapters.
- Keep automatic cleanup disabled by default.
- Enable it only per explicitly trusted profile and explicit user opt-in.
- Add Windows integration tests for every allowed adapter before shipping it.

### Phase 5 - real-world validation

- Test video editors and FFmpeg workflows, including active rendering and completed jobs.
- Test local AI runtimes while loading, generating, idle with a cached model, and shutting down.
- Test browsers, sync clients, audio/video apps, backup tools, and unknown processes.
- Validate on low-memory Windows hardware as well as systems with ample RAM.
- Run a 24-hour soak test and measure RAM Sentinel's own overhead.
- Do not claim performance improvement until repeatable before/after measurements show it.

## Acceptance criteria

The feature is not ready for general release until:

- A still-rendering FFmpeg process is never classified as safe to close from low CPU alone.
- A local AI model intentionally cached in memory is not automatically unloaded.
- An inactive suggestion appears only when the configured inactivity and sustained memory-pressure rules are met.
- Returning to the application cancels the suggestion.
- Stale PID or uncertain identity always cancels an action.
- No process can be force-terminated by the lifecycle manager.
- Unknown and protected processes remain observation-only.
- A graceful-close refusal or timeout never escalates to a kill.
- Post-action measurements distinguish working set, private bytes, and system available memory.
- Idle lifecycle monitoring stays within the measured RAM Sentinel resource budget.

## Non-goals

- A generic RAM cleaner.
- Automatically closing every background application.
- Treating all child processes as disposable.
- Killing or suspending processes, changing their priority/affinity, or trimming working sets.
- Clearing standby memory or file cache.
- Automatically unloading AI models without a documented user-configurable runtime interface.
- Guessing process ownership from a process name or a single parent PID snapshot.
