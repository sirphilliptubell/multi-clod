using Microsoft.Win32;
using MultiClod.App.Persistence;
using MultiClod.Shared;
using System.IO;
using System.Security;
using System.Text.Json;

namespace MultiClod.App.FromHere;

/// <summary>
/// Self-installs the "Multi-Clod from here" Explorer integration: deploys the
/// MultiClod.FromHere stub to ~/.multi-clod/from-here-tool, records this process's own exe
/// path so the stub knows what to launch, and registers the two HKCU shell verbs. Called once
/// from App.OnStartup, only by the process that wins the single-instance mutex (see App.xaml.cs) -
/// a process that hands off to an already-running instance never calls this.
///
/// Every step below is independently best-effort: a failure here just means the context menu
/// doesn't work until the next successful launch retries it, mirroring
/// <see cref="SessionStore"/>'s swallow-and-continue policy for file I/O
/// (SessionStore.WriteWithBackupRotation).
/// </summary>
public static class FromHereInstaller {
	private const string StubFileSearchPattern = "MultiClod.FromHere.*";
	private const string StubExeFileName = "MultiClod.FromHere.exe";
	private const string StubToolDirectoryName = "from-here-tool";

	// Debug builds register a separate verb (rather than overwriting the Release one) so both
	// configs' context-menu entries and Install() calls never race each other on the same
	// registry key - matches the separate Mutex/PipeName/DataDirectoryName in FromHereProtocol.
#if DEBUG
	private const string VerbName = "A-MultiClodFromHereDebug"; // the "A-" helps sort this verb above other verbs in the same registry key, but Explorer still has the final say
	private const string VerbDisplayText = "Multi-Clod from here (Debug)";
#else
	private const string VerbName = "A-MultiClodFromHere"; // the "A-" helps sort this verb above other verbs in the same registry key, but Explorer still has the final say
	private const string VerbDisplayText = "Multi-Clod from here";
#endif

	private static readonly JsonSerializerOptions ConfigJsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public static void Install() {
		var stubPath = DeployStub();
		WriteConfig();

		if (stubPath is not null) {
			RegisterVerb(@"Software\Classes\Directory\shell\" + VerbName, stubPath, "%1");
			RegisterVerb(@"Software\Classes\Directory\Background\shell\" + VerbName, stubPath, "%V");
		}
	}

	/// <summary>
	/// Copies every MultiClod.FromHere.* file from our own output directory into
	/// ~/.multi-clod/from-here-tool, then deletes anything already there that wasn't just
	/// copied. from-here-tool is created and exclusively owned by this installer - nothing else
	/// ever places files there - so deleting whatever isn't in this run's copy set is always safe.
	/// </summary>
	private static string? DeployStub() {
		try {
			var targetDirectory = Path.Combine(MultiClodDataDirectory.Root, StubToolDirectoryName);
			Directory.CreateDirectory(targetDirectory);

			var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var source in Directory.GetFiles(AppContext.BaseDirectory, StubFileSearchPattern)) {
				var fileName = Path.GetFileName(source);
				File.Copy(source, Path.Combine(targetDirectory, fileName), overwrite: true);
				copiedNames.Add(fileName);
			}

			foreach (var existing in Directory.GetFiles(targetDirectory)) {
				if (!copiedNames.Contains(Path.GetFileName(existing))) {
					File.Delete(existing);
				}
			}

			var stubPath = Path.Combine(targetDirectory, StubExeFileName);
			return File.Exists(stubPath) ? stubPath : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
			return null;
		}
	}

	private static void WriteConfig() {
		try {
			var appPath = Environment.ProcessPath;
			if (appPath is null) {
				return;
			}

			Directory.CreateDirectory(MultiClodDataDirectory.Root);
			var configPath = Path.Combine(MultiClodDataDirectory.Root, FromHereProtocol.ConfigFileName);
			var json = JsonSerializer.Serialize(new FromHereConfig(appPath), ConfigJsonOptions);
			File.WriteAllText(configPath, json);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
		}
	}

	private static void RegisterVerb(string verbKeyPath, string stubPath, string argumentPlaceholder) {
		try {
			using var verbKey = Registry.CurrentUser.CreateSubKey(verbKeyPath);
			if (verbKey is null) {
				return;
			}

			verbKey.SetValue(null, VerbDisplayText);

			// Icon: the stub's own embedded icon (index 0, set via ApplicationIcon in
			// MultiClod.FromHere.csproj) - unlike the command string below, Icon values are
			// conventionally unquoted even though the path could contain spaces.
			verbKey.SetValue("Icon", $"{stubPath},0");

			using var commandKey = verbKey.CreateSubKey("command");
			commandKey?.SetValue(null, $"\"{stubPath}\" \"{argumentPlaceholder}\"");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) {
		}
	}

	private sealed record FromHereConfig(string AppPath);
}
