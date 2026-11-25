using System.ComponentModel;
using System.Diagnostics;

namespace RamSentinel.Core;

/// <summary>
/// Read-only set of process properties captured once from a single Process instance.
/// Missing values remain null and are never replaced with measured zero values.
/// </summary>
public sealed record ProcessPropertyObservation(
    int? ProcessId,
    DateTimeOffset? StartedAtUtc,
    string? Name,
    ulong? WorkingSetBytes,
    ulong? PrivateBytes,
    TimeSpan? TotalCpuTime);

/// <summary>
/// Injectable access boundary for System.Diagnostics.Process properties.
/// Each method represents exactly one property read.
/// </summary>
public interface IProcessPropertyAccess
{
    int ReadProcessId(Process process);
    DateTimeOffset ReadStartedAtUtc(Process process);
    string ReadProcessName(Process process);
    long ReadWorkingSetBytes(Process process);
    long ReadPrivateBytes(Process process);
    TimeSpan ReadTotalCpuTime(Process process);
}

/// <summary>
/// Captures each process property at most once. The PID is read first; if it is unavailable,
/// other properties are not queried because the observation cannot be safely keyed.
/// </summary>
public sealed class ProcessPropertyReader
{
    private readonly IProcessPropertyAccess _access;

    public ProcessPropertyReader()
        : this(new SystemDiagnosticsProcessPropertyAccess())
    {
    }

    public ProcessPropertyReader(IProcessPropertyAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    public ProcessPropertyObservation Capture(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        int? processId = TryRead(() => _access.ReadProcessId(process));
        if (!processId.HasValue)
        {
            return new ProcessPropertyObservation(
                ProcessId: null,
                StartedAtUtc: null,
                Name: null,
                WorkingSetBytes: null,
                PrivateBytes: null,
                TotalCpuTime: null);
        }

        DateTimeOffset? startedAtUtc = TryRead(() => _access.ReadStartedAtUtc(process));
        string? name = TryRead(() => _access.ReadProcessName(process));
        long? workingSet = TryRead(() => _access.ReadWorkingSetBytes(process));
        long? privateBytes = TryRead(() => _access.ReadPrivateBytes(process));
        TimeSpan? totalCpuTime = TryRead(() => _access.ReadTotalCpuTime(process));

        return new ProcessPropertyObservation(
            ProcessId: processId,
            StartedAtUtc: startedAtUtc,
            Name: string.IsNullOrWhiteSpace(name) ? null : name,
            WorkingSetBytes: ToUnsigned(workingSet),
            PrivateBytes: ToUnsigned(privateBytes),
            TotalCpuTime: totalCpuTime);
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or Win32Exception
                or NotSupportedException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            return default;
        }
    }

    private static ulong? ToUnsigned(long? value) =>
        value is >= 0 ? (ulong)value.Value : null;

    private sealed class SystemDiagnosticsProcessPropertyAccess : IProcessPropertyAccess
    {
        public int ReadProcessId(Process process) => process.Id;

        public DateTimeOffset ReadStartedAtUtc(Process process) =>
            new(process.StartTime.ToUniversalTime());

        public string ReadProcessName(Process process) => process.ProcessName;

        public long ReadWorkingSetBytes(Process process) => process.WorkingSet64;

        public long ReadPrivateBytes(Process process) => process.PrivateMemorySize64;

        public TimeSpan ReadTotalCpuTime(Process process) => process.TotalProcessorTime;
    }
}
