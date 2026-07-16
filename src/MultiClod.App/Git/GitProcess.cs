using System.ComponentModel;
using System.Diagnostics;

namespace MultiClod.App.Git;

/// <summary>
/// Shells out to the user's own git.exe (on PATH) rather than a git library - this app has no
/// other git dependency to justify pulling one in, and every operation GitRepository/GitWorktree
/// need is a plain command with parseable stdout.
/// </summary>
internal static class GitProcess
{
    internal static (int ExitCode, string StdOut, string StdErr) Run(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo)!;
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }
        catch (Win32Exception)
        {
            // git isn't installed, or isn't on PATH - treated the same as any other failed command
            // rather than a special case, so callers only ever need to check ExitCode.
            return (-1, string.Empty, "git was not found on PATH.");
        }
    }
}
