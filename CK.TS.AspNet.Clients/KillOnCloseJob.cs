using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CK.TypeScript;

/// <summary>
/// A Windows job object that kills its processes when it is closed: a process assigned to it and all the processes it
/// starts, even the orphaned ones (a process tree kill misses them: yarn starts "ng serve" through an intermediate process
/// that exits). The job is closed by <see cref="Dispose"/> or by the system when the host process ends, even abruptly.
/// </summary>
sealed class KillOnCloseJob : IDisposable
{
    readonly SafeFileHandle _handle;

    KillOnCloseJob( SafeFileHandle handle ) => _handle = handle;

    /// <summary>
    /// Creates a job and assigns the process to it. The processes that it starts from now on are in the job: the process
    /// is assigned right after it is started, before it starts anything (a node process takes far longer to start).
    /// </summary>
    /// <param name="process">The process.</param>
    /// <returns>The job.</returns>
    /// <exception cref="Win32Exception">The job can't be created or the process can't be assigned to it.</exception>
    [SupportedOSPlatform( "windows" )]
    public static KillOnCloseJob Assign( Process process )
    {
        var handle = CreateJobObject( IntPtr.Zero, null );
        if( handle.IsInvalid ) throw new Win32Exception();
        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if( !SetInformationJobObject( handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() )
                || !AssignProcessToJobObject( handle, process.Handle ) )
            {
                throw new Win32Exception();
            }
            return new KillOnCloseJob( handle );
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Kills all the processes of the job.
    /// </summary>
    public void Dispose() => _handle.Dispose();

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout( LayoutKind.Sequential )]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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

    [StructLayout( LayoutKind.Sequential )]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout( LayoutKind.Sequential )]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
    static extern SafeFileHandle CreateJobObject( IntPtr lpJobAttributes, string? lpName );

    [DllImport( "kernel32.dll", SetLastError = true )]
    static extern bool SetInformationJobObject( SafeFileHandle hJob, int infoType, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength );

    [DllImport( "kernel32.dll", SetLastError = true )]
    static extern bool AssignProcessToJobObject( SafeFileHandle hJob, IntPtr hProcess );
}
