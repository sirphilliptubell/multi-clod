using System.Runtime.InteropServices;
using System.Text;
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
    // Set by VS Code on any process it launches/debugs (F5, not just its integrated terminal),
    // regardless of the "console" setting in launch.json - "externalTerminal" only changes where
    // stdio is connected, not what env vars the debuggee inherits. Confirmed by hand: a session
    // launched this way runs claude interactively (real responses, real MCP servers) but silently
    // never persists a transcript or registers in ~/.claude/sessions - the same launch outside VS
    // Code works fine. TERM_PROGRAM=vscode is the most likely single trigger (a common convention
    // CLIs use to detect "running inside an editor" and special-case behavior), but since none of
    // these are needed by claude and stripping them can't break anything, the whole family gets
    // scrubbed rather than betting on isolating the exact one.
    private static readonly HashSet<string> EditorInjectedVariablesToStrip = new(StringComparer.OrdinalIgnoreCase)
    {
        "TERM_PROGRAM",
        "TERM_PROGRAM_VERSION",
        "VSCODE_PID",
        "VSCODE_CWD",
        "VSCODE_NLS_CONFIG",
        "VSCODE_IPC_HOOK",
        "VSCODE_IPC_HOOK_CLI",
        "VSCODE_INJECTION",
        "VSCODE_IPC_HOOK_EXTHOST",
        "VSCODE_IPC_HOOK_CLI_EXTHOST",
        "VSCODE_GIT_ASKPASS_NODE",
        "VSCODE_GIT_ASKPASS_EXTRA_ARGS",
        "VSCODE_GIT_ASKPASS_MAIN",
        "VSCODE_GIT_IPC_HANDLE",
        "VSCODE_INSPECTOR_OPTIONS",
        "ELECTRON_RUN_AS_NODE",
    };

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

        var environmentBlock = BuildEnvironmentBlock();
        try
        {
            var success = CreateProcess(
                lpApplicationName: null,
                lpCommandLine: commandLine,
                lpProcessAttributes: ref processSecurity,
                lpThreadAttributes: ref threadSecurity,
                bInheritHandles: false,
                dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                lpEnvironment: environmentBlock,
                lpCurrentDirectory: string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
                lpStartupInfo: ref startupInfo,
                lpProcessInformation: out PROCESS_INFORMATION processInfo);
            if (!success)
            {
                throw new InvalidOperationException("Could not create process. " + Marshal.GetLastWin32Error());
            }

            return processInfo;
        }
        finally
        {
            // Safe to free right after CreateProcess returns - it copies the block into the new
            // process's own address space rather than referencing this memory afterward.
            Marshal.FreeHGlobal(environmentBlock);
        }
    }

    /// <summary>
    /// Builds a CreateProcessW-shaped environment block (sequential "KEY=VALUE\0" strings,
    /// double-null-terminated) from this process's own environment, minus
    /// <see cref="EditorInjectedVariablesToStrip"/>. Passing an explicit block instead of
    /// IntPtr.Zero (inherit-everything) is what makes the strip effective - CreateProcess still
    /// inherits nothing beyond what's in this block.
    /// </summary>
    private static IntPtr BuildEnvironmentBlock()
    {
        var block = new StringBuilder();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (EditorInjectedVariablesToStrip.Contains(key))
            {
                continue;
            }

            block.Append(key).Append('=').Append(entry.Value).Append('\0');
        }

        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }
}
