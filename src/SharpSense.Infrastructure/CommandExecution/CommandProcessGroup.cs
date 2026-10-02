using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class CommandProcessGroup
{
    private readonly SafeFileHandle? _job;

    private CommandProcessGroup(SafeFileHandle? job) => _job = job;

    public static CommandProcessGroup Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            if (NativeMethods.CreateSession() == -1)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new CommandProcessGroup(null);
        }

        var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        try
        {
            var limits = new NativeMethods.ExtendedLimits
            {
                Basic = new NativeMethods.BasicLimits { Flags = 0x2000 } // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };
            if (job.IsInvalid ||
                !NativeMethods.SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<NativeMethods.ExtendedLimits>()) ||
                !NativeMethods.AssignProcessToJobObject(job, NativeMethods.GetCurrentProcess()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new CommandProcessGroup(job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    [DoesNotReturn]
    public void Terminate()
    {
        if (_job is not null)
        {
            _job.Dispose();
            Environment.Exit(0);
        }
        else
        {
            // This runs inside the supervisor's own session. No stale PID lookup
            // is needed, and unrelated concurrent commands have different groups.
            if (NativeMethods.Kill(0, 9) == 0)
            {
                // Signal delivery can lag the syscall return. Do not emit a
                // fail-fast diagnostic while successful termination is pending.
                Environment.Exit(0);
            }
        }

        Environment.FailFast("Could not terminate the owned command process group.");
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        internal static extern int CreateSession();

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicLimits
        {
            public long ProcessTime;
            public long JobTime;
            public uint Flags;
            public nuint MinimumWorkingSet;
            public nuint MaximumWorkingSet;
            public uint ActiveProcesses;
            public nuint Affinity;
            public uint Priority;
            public uint Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations;
            public ulong WriteOperations;
            public ulong OtherOperations;
            public ulong ReadBytes;
            public ulong WriteBytes;
            public ulong OtherBytes;
            public nuint ProcessMemory;
            public nuint JobMemory;
            public nuint PeakProcessMemory;
            public nuint PeakJobMemory;
        }
    }
}
