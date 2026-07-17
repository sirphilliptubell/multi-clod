using System.ComponentModel;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace MultiClod.App.Git;

/// <summary>
/// Shells out to the user's own git.exe (on PATH) rather than a git library - this app has no
/// other git dependency to justify pulling one in, and every operation GitRepository/GitWorktree
/// need is a plain command with parseable stdout. Uses CliWrap (the app-wide standard for
/// launching CLI processes) rather than raw Process/ProcessStartInfo.
/// </summary>
internal static class GitProcess
{
    internal static (int ExitCode, string StdOut, string StdErr) Run(string workingDirectory, params string[] args)
    {
        try
        {
            // GitRepository/GitWorktree's callers are synchronous WPF event handlers, so this
            // blocks rather than propagating async up through the whole call chain. Running
            // CliWrap's async execution via Task.Run (instead of awaiting it directly on this
            // thread) means the continuation has no captured SynchronizationContext to marshal
            // back onto, so blocking on it here can't deadlock the UI thread.
            var result = Task.Run(() => Cli.Wrap("git")
                    .WithArguments(args)
                    .WithWorkingDirectory(workingDirectory)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync().Task)
                .GetAwaiter()
                .GetResult();

            return (result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Win32Exception)
        {
            // git isn't installed, or isn't on PATH - treated the same as any other failed command
            // rather than a special case, so callers only ever need to check ExitCode.
            return (-1, string.Empty, "git was not found on PATH.");
        }
    }
}
