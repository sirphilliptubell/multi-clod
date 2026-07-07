using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using MultiClod.Shared;

if (args.Length != 1)
{
    return 1;
}

var directory = args[0];

// Probe (don't hold) the mutex: WaitOne(TimeSpan.Zero) returning true means nobody else owns it
// right now, i.e. no MultiClod.App instance is running. This stub is never the long-running
// holder, so it releases immediately either way.
using var mutex = new Mutex(initiallyOwned: false, FromHereProtocol.MutexName, out _);
var acquired = mutex.WaitOne(TimeSpan.Zero);
if (acquired)
{
    mutex.ReleaseMutex();
    TryLaunchNewInstance(directory);
}
else
{
    TrySendToRunningInstance(directory);
}

return 0;

static void TrySendToRunningInstance(string directory)
{
    try
    {
        using var client = new NamedPipeClientStream(".", FromHereProtocol.PipeName, PipeDirection.Out);
        client.Connect(2000);

        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine(directory);
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
    {
        // No UI to report to - this runs invisibly from an Explorer context menu. Silently no-op,
        // matching the stub's own failure policy for a missing appPath below.
    }
}

static void TryLaunchNewInstance(string directory)
{
    try
    {
        var appPath = ReadAppPath();
        if (appPath is null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo(appPath) { UseShellExecute = false };
        startInfo.ArgumentList.Add("--from-here");
        startInfo.ArgumentList.Add(directory);
        Process.Start(startInfo);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
    {
    }
}

static string? ReadAppPath()
{
    var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FromHereProtocol.DataDirectoryName);
    var configPath = Path.Combine(dataRoot, FromHereProtocol.ConfigFileName);
    if (!File.Exists(configPath))
    {
        return null;
    }

    var json = File.ReadAllText(configPath);
    var config = JsonSerializer.Deserialize<FromHereConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    return config?.AppPath;
}

sealed record FromHereConfig(string? AppPath);
