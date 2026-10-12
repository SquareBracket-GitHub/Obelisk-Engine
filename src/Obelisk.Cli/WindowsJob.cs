using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Obelisk.Cli;

/// <summary>Private Windows process ownership; closing the last handle kills all assigned descendants.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsJob : IDisposable
{
    private readonly JobHandle _handle;
    private readonly object _stopGate = new();
    private Task? _stop;
    public WindowsJob()
    {
        _handle = CreateJobObjectW(0, null);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            _handle.Dispose(); throw error;
        }
    }
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            if (!process.HasExited) throw new Win32Exception(error);
        }
    }
    public void Dispose() => _handle.Dispose();
    public Task StopAsync()
    {
        lock (_stopGate) return _stop ??= StopCore();
    }
    private async Task StopCore()
    {
        try
        {
            if (!TerminateJobObject(_handle, 1)) throw new InvalidOperationException($"Could not terminate owned process tree: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                if (!QueryInformationJobObject(_handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), 0))
                    throw new InvalidOperationException("Could not verify termination of owned process tree.");
                if (accounting.ActiveProcesses == 0) return;
                if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("Owned process tree did not stop within five seconds; restart aborted.");
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        finally { Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUser, TotalKernel, PeriodUser, PeriodKernel;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    private sealed class JobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern JobHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(JobHandle job, int kind, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(JobHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(JobHandle job, int kind, out Accounting accounting, uint size, nint returnedLength);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
