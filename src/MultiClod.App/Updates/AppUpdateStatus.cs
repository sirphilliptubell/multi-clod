namespace MultiClod.App.Updates;

/// <summary>
/// Live state of <see cref="AppUpdateCoordinator"/>'s background update checking, surfaced to the
/// UI (see MainWindow's title bar, and the startup splash screen) so a long-running check/download
/// isn't invisible to the user.
/// </summary>
public enum AppUpdateStatus {
	CheckingForUpdates,
	UpToDate,
	DownloadingUpdate,
	RestartToUpdate,
}

public static class AppUpdateStatusText {
	/// <summary>Shared human-readable text - kept in one place so MainWindow's title bar and the
	/// startup splash screen never drift apart.</summary>
	public static string Describe(this AppUpdateStatus status) => status switch {
		AppUpdateStatus.CheckingForUpdates => "Checking for updates",
		AppUpdateStatus.UpToDate => "Up to date",
		AppUpdateStatus.DownloadingUpdate => "Downloading updates",
		AppUpdateStatus.RestartToUpdate => "Restart to update",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
	};
}
