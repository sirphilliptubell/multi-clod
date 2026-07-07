using System.Runtime.InteropServices;
using static MultiClod.Terminal.ConPty.Native.ProcessApi;

namespace MultiClod.Terminal.ConPty;

/// <summary>
/// Owns the raw STARTUPINFOEX/PROCESS_INFORMATION handles for a process started against a
/// pseudoconsole. Named Win32Process (not Process) to avoid clashing with System.Diagnostics.Process,
/// which ConPtyConnection also uses (wrapped around the same PID) to get Exited/ExitCode for free.
/// Ported from Microsoft's own samples/ConPTY/MiniTerm (microsoft/terminal repo).
/// </summary>
internal sealed class Win32Process : IDisposable
{
    private bool disposed;

    public Win32Process(STARTUPINFOEX startupInfo, PROCESS_INFORMATION processInfo)
    {
        this.StartupInfo = startupInfo;
        this.ProcessInfo = processInfo;
    }

    public STARTUPINFOEX StartupInfo { get; }

    public PROCESS_INFORMATION ProcessInfo { get; }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        if (this.StartupInfo.lpAttributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(this.StartupInfo.lpAttributeList);
            Marshal.FreeHGlobal(this.StartupInfo.lpAttributeList);
        }

        if (this.ProcessInfo.hProcess != IntPtr.Zero)
        {
            CloseHandle(this.ProcessInfo.hProcess);
        }

        if (this.ProcessInfo.hThread != IntPtr.Zero)
        {
            CloseHandle(this.ProcessInfo.hThread);
        }
    }
}
