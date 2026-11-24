using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RamSentinel.App.Platform;

/// <summary>
/// Measures whole-system CPU utilization from deltas returned by GetSystemTimes.
/// The first sample establishes a baseline and therefore returns null.
/// </summary>
internal sealed class WindowsCpuUsageProvider
{
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;

    public double? ReadPercent()
    {
        if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        ulong idle = idleTime.ToUInt64();
        ulong kernel = kernelTime.ToUInt64();
        ulong user = userTime.ToUInt64();

        if (_previousIdle is null || _previousKernel is null || _previousUser is null)
        {
            SaveSample(idle, kernel, user);
            return null;
        }

        ulong idleDelta = Delta(idle, _previousIdle.Value);
        ulong kernelDelta = Delta(kernel, _previousKernel.Value);
        ulong userDelta = Delta(user, _previousUser.Value);
        SaveSample(idle, kernel, user);

        ulong totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0)
        {
            return null;
        }

        ulong busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        return Math.Clamp((double)busyDelta / totalDelta * 100d, 0d, 100d);
    }

    private void SaveSample(ulong idle, ulong kernel, ulong user)
    {
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;
    }

    private static ulong Delta(ulong current, ulong previous) =>
        current >= previous ? current - previous : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);
}
