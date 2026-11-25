using System.ComponentModel;
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
    public ProcessInventorySnapshot ReadAll(
        IReadOnlyList<Process> processes,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(processes);

        DateTimeOffset timestamp = capturedAt.ToUniversalTime();
        ParentProcessSnapshot parentSnapshot = ReadParentProcessIds();
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
            int processId;
            try
            {
                processId = process.Id;
            }
            catch (Exception exception) when (IsExpectedProcessAccessFailure(exception))
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

            DateTimeOffset? startedAt = TryRead(() =>
                new DateTimeOffset(process.StartTime.ToUniversalTime()));
            string? name = TryRead(() => process.ProcessName);
            ulong? workingSet = TryRead(() => ToUnsigned(process.WorkingSet64));
            ulong? privateBytes = TryRead(() => ToUnsigned(process.PrivateMemorySize64));
            TimeSpan? totalCpuTime = TryRead(() => process.TotalProcessorTime);
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

    private static ParentProcessSnapshot ReadParentProcessIds()
    {
        var parentIds = new Dictionary<int, int?>();
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (snapshot == IntPtr.Zero || snapshot == InvalidHandleValue)
        {
            int error = Marshal.GetLastWin32Error();
            return new ParentProcessSnapshot(
                parentIds,
                ParentMetadataStatus.Unavailable,
                $"יצירת צילום תהליכי Windows נכשלה (Win32 {error}).");
        }

        ParentMetadataStatus status;
        string? failureReason = null;

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32First(snapshot, ref entry))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_NO_MORE_FILES)
                {
                    status = ParentMetadataStatus.Complete;
                }
                else
                {
                    status = ParentMetadataStatus.Unavailable;
                    failureReason = $"קריאת Process32First נכשלה (Win32 {error}).";
                }
            }
            else
            {
                do
                {
                    if (entry.ProcessId > 0)
                    {
                        parentIds[(int)entry.ProcessId] = entry.ParentProcessId > 0
                            ? (int)entry.ParentProcessId
                            : null;
                    }

                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                }
                while (Process32Next(snapshot, ref entry));

                int terminalError = Marshal.GetLastWin32Error();
                if (terminalError == ERROR_NO_MORE_FILES)
                {
                    status = ParentMetadataStatus.Complete;
                }
                else
                {
                    status = ParentMetadataStatus.Partial;
                    failureReason = $"סריקת מזהי ההורים נקטעה (Win32 {terminalError}).";
                }
            }
        }
        finally
        {
            if (!CloseHandle(snapshot))
            {
                int error = Marshal.GetLastWin32Error();
                if (status == ParentMetadataStatus.Complete)
                {
                    status = ParentMetadataStatus.Partial;
                    failureReason = $"סגירת צילום התהליכים נכשלה (Win32 {error}).";
                }
            }
        }

        return new ParentProcessSnapshot(parentIds, status, failureReason);
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

    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpectedProcessAccessFailure(exception))
        {
            return default;
        }
    }

    private static bool IsExpectedProcessAccessFailure(Exception exception) =>
        exception is InvalidOperationException
            or Win32Exception
            or NotSupportedException
            or UnauthorizedAccessException
            or ArgumentException;

    private static ulong? ToUnsigned(long value) =>
        value >= 0 ? (ulong)value : null;

    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_NO_MORE_FILES = 18;

    private sealed record ParentProcessSnapshot(
        IReadOnlyDictionary<int, int?> ParentIds,
        ParentMetadataStatus Status,
        string? FailureReason);

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
