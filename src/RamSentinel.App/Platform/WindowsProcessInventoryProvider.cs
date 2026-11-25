using System.Diagnostics;
using System.Runtime.InteropServices;
using RamSentinel.Core;

namespace RamSentinel.App.Platform;

/// <summary>
/// Captures one immutable, read-only inventory from caller-owned Process instances.
/// The caller owns enumeration and disposal. Every process property is read at most once.
/// </summary>
internal sealed class WindowsProcessInventoryProvider
{
    private readonly ProcessPropertyReader _processPropertyReader = new();

    public ProcessInventorySnapshot ReadAll(
        IReadOnlyList<Process> processes,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(processes);

        DateTimeOffset timestamp = capturedAt.ToUniversalTime();
        ProcessParentSnapshotResult parentSnapshot = ReadParentProcessIds();
        var entries = new List<ProcessInventoryEntry>(processes.Count);
        var failureReasons = new List<string>();
        ProcessEnumerationStatus enumerationStatus = ProcessEnumerationStatus.Complete;
        ParentMetadataStatus parentStatus = parentSnapshot.Status;

        if (parentSnapshot.FailureReason is not null)
        {
            failureReasons.Add(parentSnapshot.FailureReason);
        }

        foreach (Process process in processes)
        {
            ProcessPropertyObservation properties = _processPropertyReader.Capture(process);
            if (properties.ProcessId is not int processId)
            {
                enumerationStatus = ProcessEnumerationStatus.Failed;
                failureReasons.Add("לא ניתן לקרוא PID של אחד התהליכים במהלך הדגימה.");
                continue;
            }

            if (processId == 0)
            {
                // PID 0 is the Windows System Idle pseudo-process, not an ordinary process row.
                continue;
            }

            if (processId < 0)
            {
                enumerationStatus = ProcessEnumerationStatus.Failed;
                failureReasons.Add("נמצא מזהה תהליך לא תקין במהלך הדגימה.");
                continue;
            }

            DateTimeOffset? startedAt = properties.StartedAtUtc;
            string? name = properties.Name;
            ulong? workingSet = properties.WorkingSetBytes;
            ulong? privateBytes = properties.PrivateBytes;
            TimeSpan? totalCpuTime = properties.TotalCpuTime;
            IoCounters? io = TryReadIoCounters(processId);

            bool parentAvailable = parentSnapshot.ParentIds.TryGetValue(
                processId,
                out int? parentProcessId);

            if (!parentAvailable && parentStatus == ParentMetadataStatus.Complete)
            {
                parentStatus = ParentMetadataStatus.Partial;
                failureReasons.Add("צילום ה-Toolhelp לא כלל את כל מזהי התהליכים שנאספו.");
            }

            entries.Add(new ProcessInventoryEntry(
                ProcessId: processId,
                Identity: startedAt.HasValue
                    ? new ProcessIdentity(processId, startedAt.Value)
                    : null,
                Name: name,
                ParentProcessId: parentAvailable ? parentProcessId : null,
                ParentMetadataAvailable: parentAvailable,
                WorkingSetBytes: workingSet,
                PrivateBytes: privateBytes,
                TotalCpuTime: totalCpuTime,
                CapturedAt: timestamp,
                ReadOperationCount: io?.ReadOperationCount,
                WriteOperationCount: io?.WriteOperationCount,
                ReadTransferBytes: io?.ReadTransferCount,
                WriteTransferBytes: io?.WriteTransferCount));

            if (!startedAt.HasValue)
            {
                failureReasons.Add($"זמן יצירת התהליך PID {processId} אינו זמין; זהותו לא אומתה.");
            }
        }

        if (entries.GroupBy(entry => entry.ProcessId).Any(group => group.Count() > 1))
        {
            parentStatus = ParentMetadataStatus.Partial;
            failureReasons.Add("נמצאו מזהי PID כפולים בדגימה; שיוך קשרי הורה-ילד אינו אמין.");
        }

        return new ProcessInventorySnapshot(
            CapturedAt: timestamp,
            EnumerationStatus: enumerationStatus,
            ParentMetadataStatus: parentStatus,
            Entries: entries.ToArray(),
            FailureReason: failureReasons.Count == 0
                ? null
                : string.Join(" ", failureReasons.Distinct()));
    }

    private static ProcessParentSnapshotResult ReadParentProcessIds() =>
        ProcessParentSnapshotCapture.Capture(
            create: () =>
            {
                IntPtr handle = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                int error = handle == IntPtr.Zero || handle == InvalidHandleValue
                    ? Marshal.GetLastWin32Error()
                    : 0;
                return new ProcessParentSnapshotCreation<IntPtr>(
                    handle != IntPtr.Zero && handle != InvalidHandleValue,
                    handle,
                    error);
            },
            first: ReadFirstParentEntry,
            next: ReadNextParentEntry,
            close: handle =>
            {
                bool succeeded = CloseHandle(handle);
                int error = succeeded ? 0 : Marshal.GetLastWin32Error();
                return new ProcessParentSnapshotCloseResult(succeeded, error);
            },
            endOfEnumerationErrorCode: ERROR_NO_MORE_FILES);

    private static ProcessParentSnapshotEntryResult ReadFirstParentEntry(IntPtr snapshot)
    {
        var entry = new ProcessEntry32
        {
            Size = (uint)Marshal.SizeOf<ProcessEntry32>()
        };

        bool succeeded = Process32First(snapshot, ref entry);
        if (!succeeded)
        {
            return new ProcessParentSnapshotEntryResult(
                Succeeded: false,
                ErrorCode: Marshal.GetLastWin32Error());
        }

        return new ProcessParentSnapshotEntryResult(
            Succeeded: true,
            ProcessId: (int)entry.ProcessId,
            ParentProcessId: entry.ParentProcessId > 0
                ? (int)entry.ParentProcessId
                : null);
    }

    private static ProcessParentSnapshotEntryResult ReadNextParentEntry(IntPtr snapshot)
    {
        var entry = new ProcessEntry32
        {
            Size = (uint)Marshal.SizeOf<ProcessEntry32>()
        };

        bool succeeded = Process32Next(snapshot, ref entry);
        if (!succeeded)
        {
            return new ProcessParentSnapshotEntryResult(
                Succeeded: false,
                ErrorCode: Marshal.GetLastWin32Error());
        }

        return new ProcessParentSnapshotEntryResult(
            Succeeded: true,
            ProcessId: (int)entry.ProcessId,
            ParentProcessId: entry.ParentProcessId > 0
                ? (int)entry.ParentProcessId
                : null);
    }

    private static IoCounters? TryReadIoCounters(int processId)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessIoCounters(handle, out IoCounters counters)
                ? counters
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_NO_MORE_FILES = 18;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessIoCounters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
