# RAM Sentinel - Product Strategy

## Mission

Help an ordinary Windows user understand why a computer feels slow, then apply a small number of safe, reversible changes that measurably improve the tasks the user performs.

The product is not a RAM cleaner. It is a lightweight performance diagnosis and optimization assistant.

## Product promise

"Find what's slowing your PC, understand the evidence, and safely reduce unnecessary background load."

Do not advertise a fixed percentage speed increase. Results depend on the actual bottleneck, hardware, workload, and the changes the user approves.

## Design rules

1. Measure before changing anything.
2. Diagnose the limiting resource; do not assume every slowdown is RAM.
3. Separate observation, recommendation, and action.
4. Explain the likely benefit and the possible downside of every action.
5. Make every supported change reversible and keep a local backup.
6. Never disable security, Windows Update, core OS services, the page file, or recovery mechanisms.
7. Never clear file cache/standby memory or trim another application's working set as a routine optimization.
8. Keep RAM Sentinel's own CPU, memory, disk, and wake-up cost extremely low.
9. Prefer Windows-supported controls over undocumented registry tweaks.
10. Verify changes by comparing measurements before and after.

## Product experience

The detailed UX specification is maintained in [docs/UX_DESIGN.md](UX_DESIGN.md). The framework decision and low-end hardware benchmark gate are documented in [docs/UI_FRAMEWORK_DECISION.md](UI_FRAMEWORK_DECISION.md). The diagnostic evidence model is documented in [docs/DIAGNOSTICS_ENGINE.md](DIAGNOSTICS_ENGINE.md).

### 1. Quick diagnosis (read-only)

A short scan should collect:
- Physical memory available, memory load, commit charge and commit limit.
- System cache and kernel pools.
- System CPU and per-process CPU over a sampling interval.
- Per-process working set and private bytes.
- Disk read/write latency, queue, active time, and Windows-volume free space.
- Startup entries and their source, initially read-only.
- Power mode and whether the device is on AC or battery, where supported.

The result should identify one primary bottleneck and up to two secondary findings. A metric alone does not prove causation; findings should require a sustained interval and corroborating evidence.

### 2. Guided recommendations

Each finding contains:
- What was measured.
- Why it can affect responsiveness.
- Confidence: high / medium / low.
- Estimated scope: startup / idle / current workload.
- Suggested action.
- Side effects and limitations.
- Whether administrator permission is needed.
- Restore plan.

### 3. Reversible optimizations

Initial candidates, ranked by value and safety:

| Priority | Optimization | Expected scope | Safety model |
|---|---|---|---|
| P0 | Identify and manage unnecessary startup apps | Boot and idle responsiveness | Start read-only; then user-approved disable with exact backup and Restore |
| P0 | Identify sustained CPU-heavy apps | Current workload | Suggest closing or reconfiguring only; no priority hacks |
| P0 | Identify real memory pressure and paging | Multitasking/app switching | Suggest closing user apps; no cache clearing |
| P1 | Detect disk latency / queue bottlenecks | File operations and app launches | Read-only diagnostics; open built-in Windows tools |
| P1 | Warn about critically low system-drive free space | Updates, temp storage and general reliability | Show space use and open Storage settings; never delete personal files automatically |
| P1 | Offer Windows visual-effects profile | UI responsiveness on weak GPUs/CPUs | Launch Windows performance-options UI; explain aesthetic trade-off and let user choose |
| P1 | Offer power-mode guidance | CPU responsiveness on AC/battery | Never force Best performance; explain heat, power and battery trade-offs |
| P2 | Startup impact tracking over several logons | Boot optimization | Attribute measured activity only where evidence exists |
| P2 | Detect long-term private-memory growth | Memory leaks / runaway apps | Trend and recommend app update/restart; no automatic kill |
| P3 | Optional WPR trace capture | Complex CPU/disk/memory stalls | Explicit opt-in; trace files remain local and can contain detailed system activity |

### 4. Startup manager requirements

The Startup manager is the first major user-visible optimizer:
- Discover user and machine Run keys, Startup folders, and later logon tasks.
- Initially show entries without changing them.
- Identify origin, command, file existence, and whether the entry is user-scoped or machine-scoped.
- Do not guess an app's importance from its name alone.
- First implementation should support only current-user entries.
- Before disabling an entry, save the exact registry value or shortcut, source location, and metadata to a local restore record.
- Disable only after explicit confirmation.
- Provide Restore and export/import backup.
- Machine-wide entries require a separate elevated workflow and are out of scope for the first version.
- Never disable security software, accessibility tools, input drivers, backup software, or unknown components automatically.

The first implementation now inventories common Run keys and Startup folders in read-only mode. It does not disable or edit entries.

Microsoft documents startup management in Settings and Task Manager, and Task Manager's startup impact uses measured CPU time and disk I/O rather than an arbitrary label:
https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows

### 5. Bottleneck diagnosis

#### Memory
High RAM occupancy alone is not enough. Combine Available Physical Memory, Commit percentage, sustained pressure, process private bytes, Page Reads/sec, and disk latency/activity. Hard Page Reads can also come from executable images and memory-mapped files, so they are corroborating evidence, not proof of page-file traffic. System cache/standby pages are not disposable waste.

#### CPU
Measure per-process CPU from deltas in cumulative process CPU time. Normalize by logical processor count and sample duration. Show both system-wide CPU and top processes. Do not change process priority or affinity automatically.

#### Disk
Collect disk latency and queue information using low-frequency Windows performance counters / PDH. Do not treat a short spike as a bottleneck. Require sustained latency and workload correlation. A slow HDD may dominate app launches even when RAM is available.

#### Storage capacity
Check free space only; do not recursively scan the entire disk. If space is low, point users to Storage Sense / Cleanup Recommendations for review. Never delete Downloads, browser profiles, caches, restore points, or personal data without a clear per-category preview and explicit approval.

#### Power and visual effects
Offer a guided route to Windows' own power and visual-effects settings. Best Performance can increase heat and power use and reduce battery life. Visual-effects reduction changes appearance. Both are user-controlled trade-offs, not one-click defaults.

#### Advanced diagnosis
Use Windows Performance Recorder / Analyzer for deeper CPU, disk, and memory traces. The trace workflow should be user-triggered, time-limited, clearly explained, and local-only.

## Performance score

Do not create one opaque "speed score" from RAM percentage. Use category findings with evidence:
- Startup impact.
- CPU saturation.
- Memory pressure / commit pressure.
- Disk latency / queue.
- Storage capacity.
- Power mode / thermal limitations (only when reliable telemetry exists).

A recommendation is high-confidence only when the relevant signal persists and is correlated with the user's slow task.

## Before/after verification

Record a local baseline and a post-change measurement:
- Idle CPU and memory after a fixed settling period.
- Time from sign-in to stable idle.
- Launch time of a user-selected app, measured across at least three runs.
- Disk read/write latency during the same task.
- Memory commit and available physical memory during multitasking.
- Repeat under the same power mode, AC/battery state, network state, and workload.

Report the median, not the best run. If the result is within measurement noise or no improvement appears, say so. Do not upload telemetry.

## App's own resource budget

- Tray-hidden mode: system snapshot every 30-60 seconds.
- Process list refresh: no more than once per 30 seconds unless the user manually refreshes.
- No background disk scans, WMI polling loops, busy-wait, or high-frequency counters.
- Avoid timer wakeups while fully minimized where possible.
- No service process in the first release.
- Set a measurable target after profiling: near-zero average CPU while idle and a small, stable private working set. The target must be based on actual measurements, not a marketing promise.

## Explicit non-goals

- "RAM booster" or "one-click 200% faster" claims.
- Emptying standby list, file cache, or working sets.
- Disabling SysMain, Windows Search, Defender, Windows Update, firewall, or arbitrary services.
- Disabling the page file.
- Deleting registry keys or temp files in bulk.
- Forcing process termination.
- Applying undocumented registry tweaks or disabling mitigations.
- Changing CPU affinity, priority, or power policy without informed user consent.

## Milestones

### M1 - Trusted diagnosis
- Per-process CPU measurement.
- Low-frequency disk metrics and system-volume free-space reading.
- Adaptive low-overhead sampling.
- Read-only startup inventory.
- Evidence-based detectors with confidence levels.
- Clear bottleneck explanation, based on sustained correlated measurements.

### M2 - Startup and background management
- User-scope disable with exact backup.
- Restore and audit log.
- Startup impact measured over repeated boots/logons.

### M3 - Reversible optimization profiles
- Guided visual-effects choice.
- Guided power-mode choice.
- Optional temporary Focus Session with explicit user-selected apps only.
- Automatic restoration of temporary settings on exit / reboot where possible.

### M4 - Validation and release
- Before/after benchmark flow.
- 24-hour soak test.
- Tests on low-end HDD and SSD systems with 4/8/16 GB RAM.
- CPU/RAM overhead budget.
- Code signing, installer, uninstaller and update channel.
- Publish claims only from reproducible benchmarks.

## Official technical references

- Windows performance recommendations: https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows
- Startup application management: https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows
- Windows power mode: https://support.microsoft.com/en-us/windows/experience/change-the-power-mode-for-your-windows-pc
- Drive optimization: https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/defragment-optimize-your-data-drives-in-windows
- Storage settings and Storage Sense: https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/storage-settings-in-windows
- Windows Performance Recorder: https://learn.microsoft.com/en-us/windows-hardware/test/wpt/introduction-to-wpr
- Performance counters and collection frequency: https://learn.microsoft.com/en-us/windows/win32/perfctrs/about-performance-counters
