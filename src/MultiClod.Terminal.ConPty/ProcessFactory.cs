using System.Runtime.InteropServices;
using static MultiClod.Terminal.ConPty.Native.ProcessApi;

namespace MultiClod.Terminal.ConPty;

/// <summary>
/// Starts and configures a process attached to a pseudoconsole, implementing the sequence
/// described in
/// https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session#preparing-for-creation-of-the-child-process.
/// Ported from Microsoft's own samples/ConPTY/MiniTerm (microsoft/terminal repo); the working
/// directory parameter is new (MiniTerm always launches in the caller's own current directory).
/// </summary>
internal static class ProcessFactory
{
    internal static Win32Process Start(string commandLine, string workingDirectory, IntPtr attributes, IntPtr hPC)
    {
        var startupInfo = ConfigureProcessThread(hPC, attributes);
        var processInfo = RunProcess(ref startupInfo, commandLine, workingDirectory);
        return new Win32Process(startupInfo, processInfo);
    }

    private static STARTUPINFOEX ConfigureProcessThread(IntPtr hPC, IntPtr attributes)
    {
        var lpSize = IntPtr.Zero;
        var success = InitializeProcThreadAttributeList(
            lpAttributeList: IntPtr.Zero,
            dwAttributeCount: 1,
            dwFlags: 0,
            lpSize: ref lpSize);
        if (success || lpSize == IntPtr.Zero)
        {
            // We're not expecting `success` here - this first call is only meant to compute lpSize.
            throw new InvalidOperationException("Could not calculate the number of bytes for the attribute list. " + Marshal.GetLastWin32Error());
        }

        var startupInfo = default(STARTUPINFOEX);
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);

        success = InitializeProcThreadAttributeList(
            lpAttributeList: startupInfo.lpAttributeList,
            dwAttributeCount: 1,
            dwFlags: 0,
            lpSize: ref lpSize);
        if (!success)
        {
            throw new InvalidOperationException("Could not set up attribute list. " + Marshal.GetLastWin32Error());
        }

        success = UpdateProcThreadAttribute(
            lpAttributeList: startupInfo.lpAttributeList,
            dwFlags: 0,
            attribute: attributes,
            lpValue: hPC,
            cbSize: (IntPtr)IntPtr.Size,
            lpPreviousValue: IntPtr.Zero,
            lpReturnSize: IntPtr.Zero);
        if (!success)
        {
            throw new InvalidOperationException("Could not set pseudoconsole thread attribute. " + Marshal.GetLastWin32Error());
        }

        return startupInfo;
    }

    private static PROCESS_INFORMATION RunProcess(ref STARTUPINFOEX startupInfo, string commandLine, string workingDirectory)
    {
        var securityAttributeSize = Marshal.SizeOf<SECURITY_ATTRIBUTES>();
        var processSecurity = new SECURITY_ATTRIBUTES { nLength = securityAttributeSize };
        var threadSecurity = new SECURITY_ATTRIBUTES { nLength = securityAttributeSize };

        var success = CreateProcess(
            lpApplicationName: null,
            lpCommandLine: commandLine,
            lpProcessAttributes: ref processSecurity,
            lpThreadAttributes: ref threadSecurity,
            bInheritHandles: false,
            dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT,
            lpEnvironment: IntPtr.Zero,
            lpCurrentDirectory: string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
            lpStartupInfo: ref startupInfo,
            lpProcessInformation: out PROCESS_INFORMATION processInfo);
        if (!success)
        {
            throw new InvalidOperationException("Could not create process. " + Marshal.GetLastWin32Error());
        }

        return processInfo;
    }
}
