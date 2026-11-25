using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RamSentinel.Core;

namespace RamSentinel.App.Platform;

/// <summary>
/// Captures one read-only process inventory pass for lifecycle analysis.
/// Parent PIDs come from one Toolhelp snapshot; per-process metrics are best-effort.
/// No command lines, window captions, document names, or user input are collected.
/// </summary>
internal sealed class WindowsProcessInventoryProvider
{
    public IReadOnlyList<ProcessLifecycleObservation> ReadAll(
        IReadOnlyList<Process> processes,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(processes);
        DateTimeOffset timestamp = capturedAt.ToUniversalTime();
        IReadOnlyDictionary<int, int> parentIds = ReadParentProcessIds();
        var observations = new List<ProcessLifecycleObservation>();

        foreach (Process process in processes)
        {
            try
            {
                int processId = process.Id;
                    if (processId <= 0)
                    {
                        continue;
                    }

                    DateTimeOffset? startedAt = TryRead(() =>
                        new DateTimeOffset(process.StartTime.ToUniversalTime()));

                    if (startedAt is null)
                    {
                        // Without a creation time, PID reuse cannot be ruled out.
                        // Such a process is deliberately excluded from lifecycle tracking.
                        continue;
                    }

                    string name = TryRead(() => process.ProcessName) ?? "Unknown";
                    ulong? workingSet = TryRead(() => ToUnsigned(process.WorkingSet64));
                    ulong? privateBytes = TryRead(() => ToUnsigned(process.PrivateMemorySize64));
                    TimeSpan? cpuTime = TryRead(() => process.TotalProcessorTime);
                    IoCounters? io = TryReadIoCounters(processId);

                    observations.Add(new ProcessLifecycleObservation(
                        Identity: new ProcessIdentity(processId, startedAt.Value),
                        Name: name,
                        ParentProcessId: parentIds.TryGetValue(processId, out int parentId) && parentId > 0
                            ? parentId
                            : null,
                        CapturedAt: timestamp,
                        WorkingSetBytes: workingSet,
                        PrivateBytes: privateBytes,
                        TotalCpuTime: cpuTime,
                        ReadOperationCount: io?.ReadOperationCount,
                        WriteOperationCount: io?.WriteOperationCount,
                        ReadTransferBytes: io?.ReadTransferCount,
                        WriteTransferBytes: io?.WriteTransferCount));
                }
            catch (Exception ex) when (IsExpectedProcessAccessFailure(ex))
            {
                // Processes can exit or become inaccessible during an inventory pass.
            }
        }

        return observations
            .OrderBy(item => item.Identity.ProcessId)
            .ToArray();
    }

    private static IReadOnlyDictionary<int, int> ReadParentProcessIds()
    {
        var result = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandleValue)
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                if (entry.ProcessId > 0 && entry.ParentProcessId > 0)
                {
                    result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                }

                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
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
        catch (Exception ex) when (IsExpectedProcessAccessFailure(ex))
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
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessIoCounters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
