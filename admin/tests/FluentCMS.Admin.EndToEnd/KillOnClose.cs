using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentCMS.Admin.EndToEnd;

// One Windows job object for the test process, with kill-on-close: the system closes its handle when the test process ends,
// a hard kill included, and kills every process in the job, and their children, which join it. Elsewhere it does nothing.
internal static partial class KillOnClose
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static readonly Lazy<IntPtr> Job = new(Create);

    public static void Add(Process process)
    {
        if (!OperatingSystem.IsWindows() || Job.Value == IntPtr.Zero)
        {
            return;
        }

        // A process that is already in a job it may not leave stays out; the other ways of killing it still hold.
        _ = AssignProcessToJobObject(Job.Value, process.Handle);
    }

    private static IntPtr Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        var job = CreateJobObjectW(IntPtr.Zero, null);
        var limits = new ExtendedLimits { BasicLimitInformation = new BasicLimits { LimitFlags = KillOnJobClose } };
        return job != IntPtr.Zero && SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())
            ? job
            : IntPtr.Zero;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
